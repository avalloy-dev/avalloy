using Avalonia.Input;

namespace Avalloy.Commands.Keymaps;

/// <summary>What one press did, with the chord keys it hands back.</summary>
/// <typeparam name="TKey">The host's key event.</typeparam>
/// <param name="Result">The match.</param>
/// <param name="GiveBack">
/// Keys of a chord this press broke off. They were held from the program and now belong to it, in
/// order, delivered to <see cref="Target"/> ahead of this press.
/// </param>
/// <param name="ChordKeys">
/// For <see cref="MatchKind.Bound"/>, the earlier keys of the chord it completed. The host drops them
/// when it consumes the press, and gives them back to <see cref="Target"/> when the binding lets the key through.
/// </param>
/// <param name="Target">Where the handed-back keys were going: whatever was focused when the chord began.</param>
/// <param name="StillDown">
/// For <see cref="MatchKind.Bound"/>, the identities of the chord's earlier keys that are still held
/// down: their releases are yet to come. A host that consumed the chord swallows them too, so the
/// program never sees a release for a press it was not given.
/// </param>
/// <remarks>
/// <see cref="GiveBack"/> and <see cref="ChordKeys"/> hold presses and the releases
/// <see cref="ChordSession{TKey}.Release"/> buffered, interleaved in the order they happened.
/// </remarks>
public readonly record struct ChordStep<TKey>(
    MatchResult Result,
    IReadOnlyList<TKey> GiveBack,
    IReadOnlyList<TKey> ChordKeys,
    object? Target,
    IReadOnlyCollection<object> StillDown);

/// <summary>
/// One window's chord in progress: the matcher's place in the trie and the keys held back from the
/// program meanwhile. Each window owns one, so a leader pressed in one window can neither complete
/// a chord in another nor have its keys replayed there.
/// </summary>
/// <typeparam name="TKey">The host's key event, kept so a broken chord can be replayed as it was.</typeparam>
public sealed class ChordSession<TKey>
{
    private readonly SequenceMatcher _matcher = new(Keymap.Empty);
    private readonly List<TKey> _held = [];
    private readonly HashSet<object> _down = [];
    // Keys whose press a keymap swap discarded unreplayed while they were still down: the program
    // never saw the press, so it must not see the release either.
    private readonly HashSet<object> _orphaned = [];
    private readonly Func<TKey, object> _identity;
    private readonly Func<TKey, TKey>? _release;
    private readonly Dictionary<object, (KeyModifiers Modifiers, TKey Key, bool ControlOwned)> _presses = [];
    private object? _target;

    /// <summary>A session whose keys are matched press to release by <paramref name="identity"/>
    /// (the physical key, say); by default the key value itself.</summary>
    public ChordSession(Func<TKey, object>? identity = null, Func<TKey, TKey>? release = null)
    {
        _identity = identity ?? (key => key!);
        _release = release;
    }

    /// <summary>Where the pending prefix began, including after its buffered repeats were flushed.</summary>
    public object? Target => _target;

    /// <summary>Cmd-modified keys may never report KeyUp. Retire their down lifecycle when Cmd
    /// releases, preserving the prefix and a balanced replay when a release factory is supplied.</summary>
    public void ModifiersChanged(KeyModifiers modifiers)
    {
        foreach (var (id, press) in _presses.ToArray())
            if (press.Modifiers.HasFlag(KeyModifiers.Meta) && !modifiers.HasFlag(KeyModifiers.Meta))
                EndPress(id);
    }

    private void EndPress(object id)
    {
        if (_presses.Remove(id, out var press) && !press.ControlOwned)
        {
            _orphaned.Add(id);
            if (_release is not null) _held.Add(_release(press.Key));
        }
        _down.Remove(id);
    }

    /// <summary>Flush buffered repeats of a key whose first press reached the control before
    /// forwarding its real release. Keep the sequence prefix and original replay target.</summary>
    public IReadOnlyList<TKey> ReleaseToControl(TKey key)
    {
        var id = _identity(key);
        var flush = _held.Where(k => Equals(_identity(k), id)).ToList();
        _held.RemoveAll(k => Equals(_identity(k), id));
        _down.Remove(id);
        _presses.Remove(id);
        _orphaned.Remove(id);
        return flush;
    }

    /// <summary>Whether a chord is under way in this session.</summary>
    public bool IsPending => _matcher.IsPending;

    /// <summary>
    /// Match <paramref name="press"/> against <paramref name="keymap"/>. A keymap different from the
    /// last press's (a config reload) abandons any chord under way without replaying it.
    /// </summary>
    /// <param name="keymap">The keymap in force.</param>
    /// <param name="press">The press, as the matcher sees it.</param>
    /// <param name="key">The host's event for it, held if it becomes part of a chord.</param>
    /// <param name="target">Where the press was going, remembered when it starts a chord.</param>
    public ChordStep<TKey> Press(Keymap keymap, KeyPress press, TKey key, object? target, bool controlOwnsRelease = false)
    {
        if (!ReferenceEquals(_matcher.Keymap, keymap))
        {
            _matcher.Keymap = keymap;
            _held.Clear();
            _orphaned.UnionWith(_down);
            _down.Clear();
            _presses.Clear();
        }
        var id = _identity(key);
        ModifiersChanged(press.Modifiers);
        if (_presses.TryGetValue(id, out var previous) && previous.Modifiers != press.Modifiers)
            EndPress(id);

        // Auto-repeat of a key the chord already holds: buffered with it, so a replay carries the
        // repeats and the release lands after them, but not a step of the chord. Matched as a step,
        // a held Ctrl+A repeating would break ctrl+a>n and restart it.
        if (_held.Count > 0 && _down.Contains(id))
        {
            _held.Add(key);
            return new ChordStep<TKey>(new MatchResult(MatchKind.Pending, null, 0), [], [], _target, []);
        }

        // A new press of a key means its old release has come and gone, wherever it went.
        _orphaned.Remove(id);

        var heldTarget = _target;
        var wasPending = _matcher.IsPending;
        var result = _matcher.Press(press);
        IReadOnlyList<TKey> giveBack = [];
        IReadOnlyList<TKey> chordKeys = [];
        IReadOnlyCollection<object> stillDown = [];

        if (result.Abandoned > 0)
            giveBack = Take(out _);

        switch (result.Kind)
        {
            case MatchKind.Pending:
                if (!wasPending || result.Abandoned > 0) _target = target;
                _held.Add(key);
                _down.Add(id);
                _presses[id] = (press.Modifiers, key, controlOwnsRelease);
                break;
            case MatchKind.Bound:
                chordKeys = Take(out stillDown);
                break;
        }
        return new ChordStep<TKey>(result, giveBack, chordKeys, heldTarget, stillDown);
    }

    /// <summary>
    /// Offer a key release. While a chord is held, the release of one of its held keys is buffered
    /// with the presses (true: keep it from the program), so a broken chord replays press and release
    /// in the order they happened. The release of a key whose held press a keymap swap discarded is
    /// the session's too (true: swallow it). Any other release is not the session's (false).
    /// </summary>
    public bool Release(TKey key)
    {
        var id = _identity(key);
        if (_orphaned.Remove(id))
            return true;
        if (_held.Count == 0 || !_down.Remove(id))
            return false;
        _presses.Remove(id);
        _held.Add(key);
        return true;
    }

    /// <summary>
    /// Abandon a pending chord without replaying it: the window lost focus, and the held keys may be
    /// released elsewhere, so a later replay could deliver a press whose release never comes. Keys
    /// still down keep their release suppressed, should it come here after all; a key's next press
    /// ends that wait, as it does after a keymap swap.
    /// </summary>
    public void Cancel()
    {
        _matcher.Reset();
        _held.Clear();
        _orphaned.UnionWith(_down);
        _down.Clear();
        _presses.Clear();
    }

    private List<TKey> Take(out IReadOnlyCollection<object> stillDown)
    {
        var keys = _held.ToList();
        stillDown = _down.ToList();
        _held.Clear();
        _down.Clear();
        _presses.Clear();
        return keys;
    }
}
