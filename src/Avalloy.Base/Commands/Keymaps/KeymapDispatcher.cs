using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace Avalloy.Commands.Keymaps;

/// <summary>Opt-in sequence dispatch. Marking KeyDown handled suppresses its text on Avalonia 12.1.4;
/// TextInput is never intercepted, including text arriving without a KeyDown on macOS.</summary>
public sealed class KeymapDispatcher
{
    private readonly KeyboardDispatcher _commands;

    public KeymapDispatcher(KeyboardDispatcher commands, Keymap keymap)
    {
        _commands = commands;
        Keymap = keymap;
    }

    /// <summary>Replacing the immutable map abandons pending chords on their next press.</summary>
    public Keymap Keymap { get; set; }

    /// <summary>
    /// The keyboard layout presses read a key's own character from when the event cannot say: Windows
    /// reports no <c>KeySymbol</c> with Ctrl held, and a composing macOS Option leaves <c>Key</c> at the US
    /// position. <see cref="KeyboardLayouts.Current"/> unless set; <see cref="KeyboardLayouts.None"/>
    /// matches as before, by the US layout's character at the key's position.
    /// </summary>
    public IKeyboardLayout Layout { get; set; } = KeyboardLayouts.Current;

    public ActionReadiness Readiness(CommandInvocation action, object? target) =>
        _commands.Readiness(action.Name, action.Parameter, target);

    public Task RunAsync(CommandInvocation action, object? target = null) =>
        _commands.RunByNameAsync(action.Name, action.Parameter, target);

    /// <summary>Route keys before the focused control sees them. The host observes every execution task.
    /// Window deactivation cancels its pending chord. Dispose when detaching. TextInput is untouched.</summary>
    public IDisposable Listen(Interactive root, Func<IInputElement?> focused, KeyboardState state,
        Action<Task> observeExecution)
    {
        EventHandler<KeyEventArgs> down = (_, e) => observeExecution(OnKeyDown(e, focused(), state));
        EventHandler<KeyEventArgs> up = (_, e) => OnKeyUp(e, state);
        EventHandler deactivate = (_, _) => state.Deactivate();
        root.AddHandler(InputElement.KeyDownEvent, down, RoutingStrategies.Tunnel);
        root.AddHandler(InputElement.KeyUpEvent, up, RoutingStrategies.Tunnel);
        if (root is Window window) window.Deactivated += deactivate;
        return new Subscription(() =>
        {
            root.RemoveHandler(InputElement.KeyDownEvent, down);
            root.RemoveHandler(InputElement.KeyUpEvent, up);
            if (root is Window w) w.Deactivated -= deactivate;
            state.Deactivate();
        });
    }

    private sealed class Subscription(Action dispose) : IDisposable
    {
        private Action? _dispose = dispose;
        public void Dispose() => Interlocked.Exchange(ref _dispose, null)?.Invoke();
    }

    /// <summary>The single-key binding this press would complete, ignoring any chord under way.</summary>
    public KeymapBinding? Peek(KeyEventArgs e) =>
        IsModifierKey(e) || e.Key == Key.ImeProcessed ? null : SequenceMatcher.Peek(Keymap, ToPress(e, Layout));

    /// <summary>
    /// Handle a key on its way down to the focused terminal of the window that owns
    /// <paramref name="window"/>. Marks the event handled when the key is the
    /// keymap's. Returns the command chain's execution task. A binding acts on that window, the one the key came from,
    /// whichever window happens to be active, and every action of a chain acts on it.
    /// </summary>
    public Task OnKeyDown(KeyEventArgs e, IInputElement? focused, KeyboardState window)
    {
        if (window.Replaying || e.Handled)
            return Task.CompletedTask;
        window.Chord.ModifiersChanged(e.KeyModifiers);
        // A new press of a key means its last release has come and gone, wherever it went.
        window.SwallowRelease.Remove(KeyIdentity(e));
        // ImeProcessed: an input method is composing with this key, so it is the IME's, never a binding's.
        if (IsModifierKey(e) || e.Key == Key.ImeProcessed)
        {
            PassThrough(e, window);
            return Task.CompletedTask;
        }

        var step = window.Chord.Press(Keymap, ToPress(e, Layout), Copy(e), focused, window.TerminalDown.Contains(KeyIdentity(e)));
        Replay(window, step.GiveBack, step.Target);

        switch (step.Result.Kind)
        {
            case MatchKind.Pending:
                // Held, not swallowed: its release is buffered with it (OnKeyUp) until the chord settles.
                e.Handled = true;
                return Task.CompletedTask;

            case MatchKind.Bound:
                var binding = step.Result.Binding!;
                // Resolved here, at the keypress, and passed down: focus moving to another window
                // while an earlier action of a chain runs must not send the rest there.
                var owner = window.Owner;
                var decision = KeyDecision.For(binding, action => Readiness(action, owner));
                if (decision.Consume)
                {
                    // The program saw none of these presses, so it must see none of their releases.
                    window.SwallowRelease.UnionWith(step.StillDown.Where(id => !window.TerminalDown.Contains(id)));
                    if (!window.TerminalDown.Contains(KeyIdentity(e)))
                        window.SwallowRelease.Add(KeyIdentity(e));
                    // Handled is all it takes to stop the key typing; Avalonia suppresses text for a handled KeyDown.
                    e.Handled = true;
                    return decision.Run ? RunChainAsync(binding, owner) : Task.CompletedTask;
                }
                // The binding lets the key through, so the chord's earlier keys go through too.
                Replay(window, step.ChordKeys, step.Target);
                PassThrough(e, window);
                return decision.Run ? RunChainAsync(binding, owner) : Task.CompletedTask;

            default:
                PassThrough(e, window);
                return Task.CompletedTask;
        }
    }

    // The key goes on to the terminal: note it as down there, so a repeat of it while the palette is
    // open is known for the terminal's and its release is left to reach the terminal.
    private static void PassThrough(KeyEventArgs e, KeyboardState window)
    {
        window.TerminalDown.Add(KeyIdentity(e));
    }

    /// <summary>
    /// Note a key pressed while the palette is open. Its press goes to the palette, so its release
    /// must not reach the terminal once the palette closes and refocuses it — a modifier included
    /// (the Ctrl+Shift of the chord that closes it). The exception is an auto-repeat of a key the
    /// terminal saw pressed before the palette opened, modifier or not: the terminal is owed that release.
    /// </summary>
    public static void OnPaletteKeyDown(KeyEventArgs e, KeyboardState window)
    {
        var id = KeyIdentity(e);
        if (window.TerminalDown.Contains(id))
            return;
        window.SwallowRelease.Add(id);
    }

    /// <summary>
    /// Handle a key release on its way to the terminal: a release of a key held in a pending chord is
    /// buffered with it, and a release of a key the keymap consumed is swallowed, so the program (with
    /// Kitty key-event reporting, which reports releases) never sees a release without its press.
    /// </summary>
    public bool OnKeyUp(KeyEventArgs e, KeyboardState window)
    {
        if (window.Replaying || e.Handled)
            return false;
        var id = KeyIdentity(e);
        if (window.TerminalDown.Contains(id))
        {
            var flush = window.Chord.ReleaseToControl(Copy(e));
            window.Chord.ModifiersChanged(e.KeyModifiers);
            Replay(window, flush, window.Chord.Target);
            window.TerminalDown.Remove(id);
            window.SwallowRelease.Remove(id);
            return false;
        }
        var buffered = window.Chord.Release(Copy(e));
        window.Chord.ModifiersChanged(e.KeyModifiers);
        var suppressed = window.SwallowRelease.Remove(id);
        if (buffered || suppressed)
        {
            e.Handled = true;
            return true;
        }
        return false;
    }

    /// <summary>What pairs a release with its press: the physical key, or the virtual key without one.</summary>
    public static object KeyIdentity(KeyEventArgs e) =>
        e.PhysicalKey != PhysicalKey.None ? e.PhysicalKey : e.Key;

    private async Task RunChainAsync(KeymapBinding binding, object? window)
    {
        // Commands run in order, rechecking readiness and retaining the invocation target.
        foreach (var action in binding.Actions)
            await RunAsync(action, window);
    }

    // A chord that did not complete gives its keys back to whatever was focused when it started,
    // as the keystrokes they were: the KeyDown, then the text it would have typed.
    private static void Replay(KeyboardState window, IReadOnlyList<KeyEventArgs> keys, object? to)
    {
        if (keys.Count == 0 || to is not Interactive target) return;

        window.Replaying = true;
        try
        {
            // Presses and the releases buffered with them, in the order they happened.
            foreach (var key in keys)
            {
                var replayed = Copy(key);
                target.RaiseEvent(replayed);
                if (key.RoutedEvent == InputElement.KeyUpEvent) window.TerminalDown.Remove(KeyIdentity(key));
                else window.TerminalDown.Add(KeyIdentity(key));
                if (key.RoutedEvent == InputElement.KeyDownEvent && !replayed.Handled && TypedText(key) is { } text)
                    target.RaiseEvent(new TextInputEventArgs { RoutedEvent = InputElement.TextInputEvent, Text = text });
            }
        }
        finally
        {
            window.Replaying = false;
        }
    }

    private static string? TypedText(KeyEventArgs e) =>
        (e.KeyModifiers & (KeyModifiers.Control | KeyModifiers.Meta)) == 0
        && e.KeySymbol is { Length: > 0 } s && !char.IsControl(s[0]) ? s : null;

    private static KeyEventArgs Copy(KeyEventArgs e) => new()
    {
        RoutedEvent = e.RoutedEvent == InputElement.KeyUpEvent ? InputElement.KeyUpEvent : InputElement.KeyDownEvent,
        Key = e.Key,
        KeyModifiers = e.KeyModifiers,
        PhysicalKey = e.PhysicalKey,
        KeySymbol = e.KeySymbol,
        KeyDeviceType = e.KeyDeviceType,
    };

    internal static KeyEventArgs CopyRelease(KeyEventArgs e)
    {
        var release = Copy(e);
        release.RoutedEvent = InputElement.KeyUpEvent;
        return release;
    }

    private static bool IsModifierKey(KeyEventArgs e) => e.Key is
        Key.LeftCtrl or Key.RightCtrl or Key.LeftShift or Key.RightShift or
        Key.LeftAlt or Key.RightAlt or Key.LWin or Key.RWin;

    /// <summary>Convert an Avalonia event to a layout-aware press. Meta is Cmd on macOS.</summary>
    /// <remarks>
    /// Where the event cannot say what the key types, this guesses the US layout; pass a layout to
    /// <see cref="ToPress(KeyEventArgs, IKeyboardLayout, KeymapPlatform?)"/> to ask it instead, as the dispatcher does.
    /// </remarks>
    public static KeyPress ToPress(KeyEventArgs e, KeymapPlatform? platform = null) =>
        ToPress(e, KeyboardLayouts.None, platform);

    /// <summary>Convert an Avalonia event to a press, asking <paramref name="layout"/> what the key types where the event cannot say.</summary>
    public static KeyPress ToPress(KeyEventArgs e, IKeyboardLayout layout, KeymapPlatform? platform = null)
    {
        var mods = e.KeyModifiers;

        var physical = KeyNames.FromAvaloniaPhysical(e.PhysicalKey.ToString()) ?? FromVirtualKey(e.Key);
        var virtualLetter = e.Key is >= Key.A and <= Key.Z ? e.Key.ToString() : null;
        return KeyNames.ToPress(mods, physical, e.KeySymbol, layout,
            optionComposes: (platform ?? KeymapPlatforms.Current) == KeymapPlatform.MacOS, virtualLetter);
    }

    // For the rare platform event without a physical key: the virtual key's W3C code, where there is an obvious one.
    internal static string? FromVirtualKey(Key key) => key switch
    {
        >= Key.A and <= Key.Z => "Key" + key,
        >= Key.D0 and <= Key.D9 => "Digit" + (key - Key.D0),
        >= Key.F1 and <= Key.F24 => key.ToString(),
        Key.Up => "ArrowUp", Key.Down => "ArrowDown", Key.Left => "ArrowLeft", Key.Right => "ArrowRight",
        Key.PageUp => "PageUp", Key.PageDown => "PageDown", Key.Home => "Home", Key.End => "End",
        Key.Enter => "Enter", Key.Escape => "Escape", Key.Back => "Backspace", Key.Tab => "Tab",
        Key.Insert => "Insert", Key.Delete => "Delete", Key.Space => "Space",
        Key.OemComma => "Comma", Key.OemPeriod => "Period", Key.OemMinus => "Minus", Key.OemPlus => "Equal",
        Key.OemOpenBrackets => "BracketLeft", Key.OemCloseBrackets => "BracketRight",
        _ => null,
    };
}

/// <summary>One window's keyboard state: its chord in progress, and the flags around replaying it.</summary>
/// <param name="owner">The window the keys belong to, and the one their bindings act on.</param>
public sealed class KeyboardState(object? owner)
{
    /// <summary>The window these keys come from.</summary>
    public object? Owner { get; } = owner;

    /// <summary>Keys the terminal has seen pressed and not yet released.</summary>
    public HashSet<object> TerminalDown { get; } = [];

    /// <summary>
    /// The window lost focus: end a pending chord unreplayed (its keys may be released elsewhere) and
    /// forget which keys the terminal holds, since their releases go to the other window.
    /// </summary>
    public void Deactivate()
    {
        Chord.Cancel();
        TerminalDown.Clear();
    }

    /// <summary>The chord under way in this window.</summary>
    public ChordSession<KeyEventArgs> Chord { get; } = new(KeymapDispatcher.KeyIdentity, KeymapDispatcher.CopyRelease);

    /// <summary>Keys whose press the keymap consumed and whose release has not come yet.</summary>
    public HashSet<object> SwallowRelease { get; } = [];

    /// <summary>Held keys are being replayed here; they pass the dispatcher untouched.</summary>
    public bool Replaying { get; set; }
}
