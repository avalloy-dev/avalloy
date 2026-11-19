using Avalonia.Input;

namespace Avalloy.Commands.Keymaps;

/// <summary>
/// One key press as the matcher sees it.
/// </summary>
/// <param name="Modifiers">The modifiers held; Meta is Cmd on macOS and the Windows key elsewhere.</param>
/// <param name="Physical">The key's W3C <c>KeyboardEvent.code</c> (<c>KeyA</c>, <c>ArrowUp</c>), or null when unknown.</param>
/// <param name="Unicode">The character the key types unshifted, lower-cased, or null when it types none.</param>
/// <param name="Produced">
/// The character the layout produced with the modifiers held (<c>+</c> for Shift+= on a US layout),
/// lower-cased, or null. Build presses with <see cref="KeyNames.ToPress(Avalonia.Input.KeyModifiers, string?, string?, IKeyboardLayout, bool, string?)"/>.
/// </param>
public readonly record struct KeyPress(KeyModifiers Modifiers, string? Physical, string? Unicode, string? Produced = null);

/// <summary>What a key press did to the sequence.</summary>
public enum MatchKind
{
    /// <summary>Nothing is bound: the key belongs to the terminal.</summary>
    Unbound,

    /// <summary>The key starts or continues a chord; hold it until the chord ends.</summary>
    Pending,

    /// <summary>The key completes a binding.</summary>
    Bound,
}

/// <summary>The outcome of one press.</summary>
/// <param name="Kind">What happened.</param>
/// <param name="Binding">The binding completed, for <see cref="MatchKind.Bound"/>.</param>
/// <param name="Abandoned">
/// How many held presses of a chord this press broke off. They were swallowed while the chord was
/// pending and now belong to the terminal, oldest first, ahead of this press.
/// </param>
public readonly record struct MatchResult(MatchKind Kind, KeymapBinding? Binding, int Abandoned);

/// <summary>
/// Walks the keymap's trie one press at a time. A press that breaks off a chord abandons the held
/// presses and is then matched afresh, so it can start a binding of its own.
/// </summary>
public sealed class SequenceMatcher
{
    private Keymap _keymap;
    private string? _prefix;
    private int _held;

    /// <summary>A matcher over <paramref name="keymap"/>.</summary>
    public SequenceMatcher(Keymap keymap) => _keymap = keymap;

    /// <summary>The keymap matched against.</summary>
    public Keymap Keymap
    {
        get => _keymap;
        set
        {
            _keymap = value;
            Reset();
        }
    }

    /// <summary>Whether a chord is under way.</summary>
    public bool IsPending => _prefix is not null;

    /// <summary>How many presses the pending chord holds.</summary>
    public int Held => _held;

    /// <summary>Forget any chord under way, without reporting its presses.</summary>
    public void Reset()
    {
        _prefix = null;
        _held = 0;
    }

    /// <summary>Match one press. Modifier keys pressed on their own should not be passed in.</summary>
    public MatchResult Press(KeyPress press)
    {
        var abandoned = 0;
        if (_prefix is not null)
        {
            if (Step(_prefix, press) is { } continued)
                return continued;
            abandoned = _held;
            Reset();
        }
        return (Step(null, press) ?? new MatchResult(MatchKind.Unbound, null, 0)) with { Abandoned = abandoned };
    }

    /// <summary>The single-key binding <paramref name="press"/> would complete, without touching any chord under way.</summary>
    public static KeymapBinding? Peek(Keymap keymap, KeyPress press)
    {
        foreach (var trigger in Candidates(press))
            if (keymap.Find(trigger) is { } binding)
                return binding;
        return null;
    }

    private MatchResult? Step(string? prefix, KeyPress press)
    {
        // Unicode first, then physical, as the host looks them up: the layout's character wins over
        // the key's position.
        foreach (var trigger in Candidates(press))
        {
            var key = prefix is null ? trigger : prefix + ">" + trigger;
            if (_keymap.Find(key) is { } binding)
            {
                Reset();
                return new MatchResult(MatchKind.Bound, binding, 0);
            }
            if (_keymap.IsPrefix(key))
            {
                _prefix = key;
                _held++;
                return new MatchResult(MatchKind.Pending, null, 0);
            }
        }
        return null;
    }

    // In order: the character the layout produced, the key's unshifted character, the key's position.
    // When Shift was what produced the character (Shift+= giving +), Shift is consumed: ctrl++ is
    // Ctrl and whatever key types +, so the + of a US Shift+= and a German unshifted + both match it.
    // A Shift that changed nothing (Shift+A giving a, lower-cased) is still a modifier of the trigger.
    private static IEnumerable<string> Candidates(KeyPress press)
    {
        if (!string.IsNullOrEmpty(press.Produced) && press.Produced != press.Unicode)
        {
            var mods = press.Modifiers.HasFlag(KeyModifiers.Shift) ? press.Modifiers & ~KeyModifiers.Shift : press.Modifiers;
            yield return new KeyTrigger(mods, KeyKind.Unicode, press.Produced).ToString();
        }
        if (!string.IsNullOrEmpty(press.Unicode))
            yield return new KeyTrigger(press.Modifiers, KeyKind.Unicode, press.Unicode).ToString();
        if (!string.IsNullOrEmpty(press.Physical))
            yield return new KeyTrigger(press.Modifiers, KeyKind.Physical, press.Physical).ToString();
    }
}

/// <summary>Whether an action can run, as the dispatcher asks it.</summary>
public enum ActionReadiness
{
    /// <summary>No command is registered for this name, or the command reports itself unavailable.</summary>
    Unavailable,

    /// <summary>It exists but has nothing to do right now (copy with no selection).</summary>
    NotNow,

    /// <summary>It can run.</summary>
    Ready,
}

/// <summary>What to do with a key that completed a binding.</summary>
/// <param name="Run">Run the binding's actions.</param>
/// <param name="Consume">Keep the key from the terminal.</param>
public readonly record struct KeyDecision(bool Run, bool Consume)
{
    /// <summary>Run any ready command in the chain. Unregistered chains pass through; a closed
    /// gate consumes unless ConsumeIfExecutable or PassThrough applies. Suppress always consumes.</summary>
    public static KeyDecision For(KeymapBinding binding, Func<CommandInvocation, ActionReadiness> readiness)
    {
        var chain = ActionReadiness.Unavailable;
        foreach (var action in binding.Actions)
        {
            var r = readiness(action);
            if (r > chain) chain = r;
        }
        if (binding.Options.HasFlag(KeyBindingOptions.Suppress))
            return new KeyDecision(Run: chain == ActionReadiness.Ready, Consume: true);

        var unconsumed = binding.Options.HasFlag(KeyBindingOptions.PassThrough);
        switch (chain)
        {
            case ActionReadiness.Unavailable:
                return new KeyDecision(Run: false, Consume: false);
            case ActionReadiness.NotNow:
                return new KeyDecision(Run: false, Consume: !unconsumed && !binding.Options.HasFlag(KeyBindingOptions.ConsumeIfExecutable));
            default:
                return new KeyDecision(Run: true, Consume: !unconsumed);
        }
    }
}
