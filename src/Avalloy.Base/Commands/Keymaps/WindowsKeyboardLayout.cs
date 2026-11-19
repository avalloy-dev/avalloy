using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Avalloy.Commands.Keymaps;

/// <summary>
/// A Windows keyboard layout's characters, by scan code: <c>MapVirtualKeyExW</c> for the unshifted
/// one, which Avalonia also uses for Alt chords (<c>KeyInterop.GetKeySymbolFromVirtualKey</c>), and
/// <c>ToUnicodeEx</c> with Shift alone for the shifted one, told not to change the keyboard's state so a
/// pending dead key is left for the typing that follows.
/// </summary>
/// <remarks>
/// The virtual key cannot stand in for the scan code: OEM codes name no one character across layouts
/// (<c>VK_OEM_PLUS</c> is <c>=</c> on US and <c>+</c> on German), and the number row's are positional.
/// A dead key answers with the character it would add (<c>^</c>), as Avalonia's <c>KeySymbol</c> does on macOS.
/// </remarks>
internal sealed partial class WindowsKeyboardLayout : IKeyboardLayout
{
    private readonly nint _layout;

    /// <summary>The UI thread's layout, read at each lookup so a layout switch is seen at once.</summary>
    public WindowsKeyboardLayout() { }

    /// <summary>A given layout (an <c>HKL</c>), whatever the thread's is.</summary>
    internal WindowsKeyboardLayout(nint layout) => _layout = layout;

    // Set-1 scan codes of the keys whose character the layout decides.
    private static readonly Dictionary<string, uint> s_scanCodes = new(StringComparer.Ordinal)
    {
        ["Digit1"] = 0x02, ["Digit2"] = 0x03, ["Digit3"] = 0x04, ["Digit4"] = 0x05, ["Digit5"] = 0x06,
        ["Digit6"] = 0x07, ["Digit7"] = 0x08, ["Digit8"] = 0x09, ["Digit9"] = 0x0A, ["Digit0"] = 0x0B,
        ["Minus"] = 0x0C, ["Equal"] = 0x0D, ["BracketLeft"] = 0x1A, ["BracketRight"] = 0x1B,
        ["Semicolon"] = 0x27, ["Quote"] = 0x28, ["Backquote"] = 0x29, ["Backslash"] = 0x2B,
        ["Comma"] = 0x33, ["Period"] = 0x34, ["Slash"] = 0x35, ["IntlBackslash"] = 0x56,
        ["IntlRo"] = 0x73, ["IntlYen"] = 0x7D,
        ["KeyQ"] = 0x10, ["KeyW"] = 0x11, ["KeyE"] = 0x12, ["KeyR"] = 0x13, ["KeyT"] = 0x14,
        ["KeyY"] = 0x15, ["KeyU"] = 0x16, ["KeyI"] = 0x17, ["KeyO"] = 0x18, ["KeyP"] = 0x19,
        ["KeyA"] = 0x1E, ["KeyS"] = 0x1F, ["KeyD"] = 0x20, ["KeyF"] = 0x21, ["KeyG"] = 0x22,
        ["KeyH"] = 0x23, ["KeyJ"] = 0x24, ["KeyK"] = 0x25, ["KeyL"] = 0x26,
        ["KeyZ"] = 0x2C, ["KeyX"] = 0x2D, ["KeyC"] = 0x2E, ["KeyV"] = 0x2F, ["KeyB"] = 0x30,
        ["KeyN"] = 0x31, ["KeyM"] = 0x32,
    };

    private const uint MAPVK_VK_TO_CHAR = 2;
    private const uint MAPVK_VSC_TO_VK_EX = 3;
    private const uint DeadKeyBit = 0x8000_0000;
    private const int VK_SHIFT = 0x10;
    // ToUnicodeEx: leave the keyboard's state alone (Windows 10 1607 and later).
    private const uint DoNotChangeKeyboardState = 0x4;

    public string? Unshifted(string code)
    {
        if (!OperatingSystem.IsWindows() || !s_scanCodes.TryGetValue(code, out var scan)) return null;
        var layout = Layout();
        var vk = MapVirtualKeyExW(scan, MAPVK_VSC_TO_VK_EX, layout);
        if (vk == 0) return null;
        var ch = MapVirtualKeyExW(vk, MAPVK_VK_TO_CHAR, layout) & ~DeadKeyBit;
        return ch is 0 or > 0xFFFF ? null : ((char)ch).ToString();
    }

    public unsafe string? Shifted(string code)
    {
        if (!OperatingSystem.IsWindows() || !s_scanCodes.TryGetValue(code, out var scan)) return null;
        var layout = Layout();
        var vk = MapVirtualKeyExW(scan, MAPVK_VSC_TO_VK_EX, layout);
        if (vk == 0) return null;
        var state = stackalloc byte[256];
        new Span<byte>(state, 256).Clear();
        state[VK_SHIFT] = 0x80;
        var buffer = stackalloc char[4];
        var n = ToUnicodeEx(vk, scan, state, buffer, 4, DoNotChangeKeyboardState, layout);
        // A dead key answers -1 with the character it would add; anything longer than one character is no key's own.
        return n switch
        {
            1 or -1 => buffer[0].ToString(),
            2 when char.IsSurrogatePair(buffer[0], buffer[1]) => new string(buffer, 0, 2),
            _ => null,
        };
    }

    [SupportedOSPlatform("windows")]
    private nint Layout() => _layout != 0 ? _layout : GetKeyboardLayout(0);

    [LibraryImport("user32.dll")]
    private static partial uint MapVirtualKeyExW(uint code, uint mapType, nint layout);

    [LibraryImport("user32.dll")]
    private static partial nint GetKeyboardLayout(uint thread);

    [LibraryImport("user32.dll")]
    private static unsafe partial int ToUnicodeEx(uint virtualKey, uint scanCode, byte* keyState, char* buffer,
        int bufferSize, uint flags, nint layout);
}
