using System.Runtime.InteropServices;

namespace Avalloy.Commands.Keymaps;

/// <summary>
/// A macOS keyboard layout's characters, by virtual key code: <c>UCKeyTranslate</c> on the layout's
/// <c>'uchr'</c> data with no modifier, or Shift alone, translated the way Avalonia translates
/// <c>KeySymbol</c> (<c>KeyTransform.mm</c>, <c>CharsFromScanCode</c>), so the answer is the character
/// Avalonia would report for the key pressed alone.
/// </summary>
/// <remarks>
/// <para>It reads the keyboard layout input source, not the current input source, so an input method in use
/// (Kotoeri, Pinyin) still answers with the layout under it. Nothing here selects or changes an input source.</para>
/// <para>Text Input Sources aborts the process when two threads call it at once ("If you are a UI application,
/// you must call TIS/TSM API on the main thread"), and Avalonia calls it on the main thread for every key. So
/// the current layout answers only on the main thread, where Avalonia raises key events, and only in a
/// window-server session; anywhere else it answers null, which falls back to the US guess, rather than risk
/// an abort or a call that never returns. Every call here also holds one lock.</para>
/// </remarks>
internal sealed unsafe partial class MacKeyboardLayout : IKeyboardLayout
{
    private const string Carbon = "/System/Library/Frameworks/Carbon.framework/Carbon";
    private const string CoreFoundation = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";
    private const string CoreGraphics = "/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics";
    private const string LibSystem = "/usr/lib/libSystem.dylib";

    private static readonly Lock s_tis = new();
    private static readonly Lazy<bool> s_guiSession = new(ReadGuiSession);

    private readonly string? _inputSourceId;

    /// <summary>
    /// The current keyboard layout, read at each lookup so a layout switch is seen at once. It answers only
    /// on the main thread.
    /// </summary>
    public MacKeyboardLayout() { }

    /// <summary>
    /// An installed layout by its input source ID (<c>com.apple.keylayout.French</c>), read without
    /// selecting it, on any thread: for a process with no UI of its own, such as a test host.
    /// </summary>
    internal MacKeyboardLayout(string inputSourceId) => _inputSourceId = inputSourceId;

    /// <summary>Whether this thread is the process's main thread.</summary>
    internal static bool OnMainThread => OperatingSystem.IsMacOS() && pthread_main_np() == 1;

    /// <summary>Whether the process runs in a window-server (GUI) session, without which there is no keyboard to ask.</summary>
    internal static bool HasGuiSession => OperatingSystem.IsMacOS() && s_guiSession.Value;

    private static bool ReadGuiSession()
    {
        var session = CGSessionCopyCurrentDictionary();
        if (session == 0) return false;
        CFRelease(session);
        return true;
    }

    // macOS virtual key codes (kVK_ANSI_*, kVK_ISO_Section, kVK_JIS_*), as Avalonia's KeyTransform.mm maps them.
    private static readonly Dictionary<string, ushort> s_keyCodes = new(StringComparer.Ordinal)
    {
        ["KeyA"] = 0x00, ["KeyS"] = 0x01, ["KeyD"] = 0x02, ["KeyF"] = 0x03, ["KeyH"] = 0x04, ["KeyG"] = 0x05,
        ["KeyZ"] = 0x06, ["KeyX"] = 0x07, ["KeyC"] = 0x08, ["KeyV"] = 0x09, ["IntlBackslash"] = 0x0A,
        ["KeyB"] = 0x0B, ["KeyQ"] = 0x0C, ["KeyW"] = 0x0D, ["KeyE"] = 0x0E, ["KeyR"] = 0x0F, ["KeyY"] = 0x10,
        ["KeyT"] = 0x11, ["Digit1"] = 0x12, ["Digit2"] = 0x13, ["Digit3"] = 0x14, ["Digit4"] = 0x15,
        ["Digit6"] = 0x16, ["Digit5"] = 0x17, ["Equal"] = 0x18, ["Digit9"] = 0x19, ["Digit7"] = 0x1A,
        ["Minus"] = 0x1B, ["Digit8"] = 0x1C, ["Digit0"] = 0x1D, ["BracketRight"] = 0x1E, ["KeyO"] = 0x1F,
        ["KeyU"] = 0x20, ["BracketLeft"] = 0x21, ["KeyI"] = 0x22, ["KeyP"] = 0x23, ["KeyL"] = 0x25,
        ["KeyJ"] = 0x26, ["Quote"] = 0x27, ["KeyK"] = 0x28, ["Semicolon"] = 0x29, ["Backslash"] = 0x2A,
        ["Comma"] = 0x2B, ["Slash"] = 0x2C, ["KeyN"] = 0x2D, ["KeyM"] = 0x2E, ["Period"] = 0x2F,
        ["Backquote"] = 0x32, ["IntlYen"] = 0x5D, ["IntlRo"] = 0x5E,
    };

    private const ushort kUCKeyActionDown = 0;
    private const ushort kVK_Space = 0x31;
    private const uint ShiftModifierState = (0x0200 >> 8) & 0xFF; // (shiftKey >> 8) & 0xFF

    public string? Unshifted(string code) => Translate(code, 0);

    public string? Shifted(string code) => Translate(code, ShiftModifierState);

    private string? Translate(string code, uint modifierState)
    {
        if (!OperatingSystem.IsMacOS() || !s_keyCodes.TryGetValue(code, out var keyCode)) return null;
        if (_inputSourceId is null && !OnMainThread) return null;
        if (!HasGuiSession) return null;
        lock (s_tis)
            return TranslateLocked(keyCode, modifierState);
    }

    private string? TranslateLocked(ushort keyCode, uint modifierState)
    {
        var source = _inputSourceId is null ? TISCopyCurrentKeyboardLayoutInputSource() : CopyInputSource(_inputSourceId);
        if (source == 0) return null;
        try
        {
            var data = TISGetInputSourceProperty(source, Symbol(ref s_layoutDataKey, "kTISPropertyUnicodeKeyLayoutData"));
            if (data == 0) return null;
            var layout = CFDataGetBytePtr(data);
            var buffer = stackalloc char[4];
            uint dead = 0;
            nuint length = 0;
            // No options, as Avalonia passes: a dead key then leaves its state, and translating a space
            // in that state gives the character it would add.
            if (UCKeyTranslate(layout, keyCode, kUCKeyActionDown, modifierState, LMGetKbdType(), 0, &dead, 4, &length, buffer) != 0)
                return null;
            if (dead != 0 && UCKeyTranslate(layout, kVK_Space, kUCKeyActionDown, 0, LMGetKbdType(), 0, &dead, 4, &length, buffer) != 0)
                return null;
            return length is 0 or > 4 ? null : new string(buffer, 0, (int)length);
        }
        finally
        {
            CFRelease(source);
        }
    }

    // An installed keyboard layout by ID, without selecting it: TISCreateInputSourceList filtered on the ID.
    private static nint CopyInputSource(string id)
    {
        nint name;
        fixed (char* chars = id)
            name = CFStringCreateWithCharacters(0, chars, id.Length);
        if (name == 0) return 0;
        try
        {
            var key = Symbol(ref s_inputSourceIdKey, "kTISPropertyInputSourceID");
            var filter = CFDictionaryCreate(0, &key, &name, 1,
                Export(CoreFoundation, "kCFTypeDictionaryKeyCallBacks"), Export(CoreFoundation, "kCFTypeDictionaryValueCallBacks"));
            if (filter == 0) return 0;
            try
            {
                var list = TISCreateInputSourceList(filter, 1);
                if (list == 0) return 0;
                try
                {
                    if (CFArrayGetCount(list) == 0) return 0;
                    var source = CFArrayGetValueAtIndex(list, 0);
                    CFRetain(source);
                    return source;
                }
                finally { CFRelease(list); }
            }
            finally { CFRelease(filter); }
        }
        finally { CFRelease(name); }
    }

    private static nint s_layoutDataKey, s_inputSourceIdKey;

    // A CFStringRef constant the framework exports, read once.
    private static nint Symbol(ref nint cache, string name)
    {
        if (cache == 0) cache = *(nint*)Export(Carbon, name);
        return cache;
    }

    private static nint Export(string library, string name) => NativeLibrary.GetExport(NativeLibrary.Load(library), name);

    [LibraryImport(LibSystem)]
    private static partial int pthread_main_np();

    [LibraryImport(CoreGraphics)]
    private static partial nint CGSessionCopyCurrentDictionary();

    [LibraryImport(Carbon)]
    private static partial nint TISCopyCurrentKeyboardLayoutInputSource();

    [LibraryImport(Carbon)]
    private static partial nint TISGetInputSourceProperty(nint source, nint key);

    [LibraryImport(Carbon)]
    private static partial nint TISCreateInputSourceList(nint properties, byte includeAllInstalled);

    [LibraryImport(Carbon)]
    private static partial byte LMGetKbdType();

    [LibraryImport(Carbon)]
    private static partial int UCKeyTranslate(nint layout, ushort virtualKeyCode, ushort keyAction, uint modifierKeyState,
        uint keyboardType, uint keyTranslateOptions, uint* deadKeyState, nuint maxStringLength, nuint* actualStringLength,
        char* unicodeString);

    [LibraryImport(CoreFoundation)]
    private static partial nint CFDataGetBytePtr(nint data);

    [LibraryImport(CoreFoundation)]
    private static partial nint CFStringCreateWithCharacters(nint allocator, char* chars, nint length);

    [LibraryImport(CoreFoundation)]
    private static partial nint CFDictionaryCreate(nint allocator, nint* keys, nint* values, nint count,
        nint keyCallBacks, nint valueCallBacks);

    [LibraryImport(CoreFoundation)]
    private static partial nint CFArrayGetCount(nint array);

    [LibraryImport(CoreFoundation)]
    private static partial nint CFArrayGetValueAtIndex(nint array, nint index);

    [LibraryImport(CoreFoundation)]
    private static partial nint CFRetain(nint value);

    [LibraryImport(CoreFoundation)]
    private static partial void CFRelease(nint value);
}
