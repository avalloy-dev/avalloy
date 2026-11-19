namespace Avalloy.Commands.Keymaps;

/// <summary>
/// The current keyboard layout's own characters for a physical key, read without touching the
/// keyboard's state. Presses consult it only where the platform's event cannot say: Windows reports
/// no <c>KeySymbol</c> with Ctrl held, and on macOS a composing Option leaves Avalonia's <c>Key</c> at
/// the US position.
/// </summary>
public interface IKeyboardLayout
{
    /// <summary>
    /// What the key types with no modifier held (German <c>ß</c> on the <c>Minus</c> key, AZERTY
    /// <c>à</c> on <c>Digit0</c>), or null when unknown.
    /// </summary>
    /// <param name="code">The key's W3C <c>KeyboardEvent.code</c>: <c>KeyA</c>, <c>Digit0</c>, <c>Minus</c>.</param>
    string? Unshifted(string code);

    /// <summary>What the key types with Shift alone held (AZERTY <c>0</c> on <c>Digit0</c>), or null when unknown.</summary>
    /// <param name="code">The key's W3C <c>KeyboardEvent.code</c>.</param>
    string? Shifted(string code);
}

/// <summary>The keyboard layouts presses can read.</summary>
public static class KeyboardLayouts
{
    /// <summary>Knows no key, so presses fall back to the platform's event and the US layout, as before.</summary>
    public static IKeyboardLayout None { get; } = new NoLayout();

    /// <summary>
    /// This platform's current layout: Windows and macOS read it from the OS; elsewhere it is
    /// <see cref="None"/>, since X11's keysym already follows the layout.
    /// </summary>
    /// <remarks>Read it on the UI thread, where key events arrive: both OSes answer for the thread's own input.</remarks>
    public static IKeyboardLayout Current { get; } =
        OperatingSystem.IsWindows() ? new WindowsKeyboardLayout()
        : OperatingSystem.IsMacOS() ? new MacKeyboardLayout()
        : None;

    private sealed class NoLayout : IKeyboardLayout
    {
        public string? Unshifted(string code) => null;
        public string? Shifted(string code) => null;
    }
}
