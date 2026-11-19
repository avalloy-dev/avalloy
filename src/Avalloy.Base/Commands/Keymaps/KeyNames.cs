using System.Text;
using Avalonia.Input;

namespace Avalloy.Commands.Keymaps;

/// <summary>Key names between Avalonia, W3C codes, US-layout characters, and what a person reads.</summary>
public static class KeyNames
{
    /// <summary>
    /// The W3C code for an Avalonia <c>PhysicalKey</c> name. Avalonia's names are W3C's except for
    /// letters (<c>A</c> for <c>KeyA</c>) and the keypad (<c>NumPad1</c> for <c>Numpad1</c>).
    /// </summary>
    public static string? FromAvaloniaPhysical(string name)
    {
        if (name is "None" or "") return null;
        if (name.Length == 1 && char.IsAsciiLetterUpper(name[0])) return "Key" + name;
        if (name.StartsWith("NumPad", StringComparison.Ordinal)) return "Numpad" + name["NumPad".Length..];
        return name;
    }

    private static readonly Dictionary<string, string> s_usUnshifted = BuildUs();

    private static Dictionary<string, string> BuildUs()
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Backquote"] = "`", ["Minus"] = "-", ["Equal"] = "=", ["BracketLeft"] = "[", ["BracketRight"] = "]",
            ["Backslash"] = "\\", ["Semicolon"] = ";", ["Quote"] = "'", ["Comma"] = ",", ["Period"] = ".",
            ["Slash"] = "/", ["Space"] = " ",
        };
        for (var c = 'a'; c <= 'z'; c++) map["Key" + char.ToUpperInvariant(c)] = c.ToString();
        for (var d = '0'; d <= '9'; d++) map["Digit" + d] = d.ToString();
        return map;
    }

    /// <summary>
    /// The character a key types unshifted on a US layout, for matching unicode triggers when the
    /// platform reports the shifted or modified character instead (<c>{</c> for Shift+[, <c>å</c> for Option+A).
    /// </summary>
    public static string? UsUnshifted(string? code) =>
        code is not null && s_usUnshifted.TryGetValue(code, out var s) ? s : null;

    /// <summary>
    /// The one character <paramref name="keySymbol"/> is, lower-cased (triggers are), or null when it
    /// is not exactly one printable code point.
    /// </summary>
    public static string? Produced(string? keySymbol)
    {
        if (string.IsNullOrEmpty(keySymbol)) return null;
        var single = keySymbol.Length == 1 || (keySymbol.Length == 2 && char.IsSurrogatePair(keySymbol[0], keySymbol[1]));
        if (!single || char.IsControl(keySymbol, 0)) return null;
        return keySymbol.ToLowerInvariant();
    }

    /// <summary>
    /// Whether Alt made <paramref name="keySymbol"/> a different character than the key's own: macOS
    /// Option composes (Option+A is <c>å</c>), and what it composes is never plain ASCII. Elsewhere
    /// Alt composes nothing, and on macOS an Option press that still reports ASCII (a layout that
    /// leaves the key alone) reports the key's own character.
    /// </summary>
    public static bool AltComposed(string? keySymbol, KeyModifiers modifiers, bool optionComposes) =>
        optionComposes && modifiers.HasFlag(KeyModifiers.Alt)
        && Produced(keySymbol) is { } produced && produced.Any(c => c > 0x7F);

    /// <summary>
    /// The character the key types with no modifier held. With no Shift held that is what the layout
    /// reported (German <c>+</c> and <c>ß</c>, the AZERTY <c>a</c> on QWERTY's Q key), Alt included
    /// unless it composed something else. With Shift held, or a composing Option, the layout's answer is
    /// the modified character, so it falls back to the reported letter (layout-aware) or, for anything
    /// else, the US-layout character at that position.
    /// </summary>
    /// <param name="optionComposes">True on macOS, where Option changes the reported character.</param>
    /// <param name="virtualLetter">
    /// The letter the platform's virtual key names (Avalonia's <c>Key</c>, A to Z), lower-cased. It
    /// follows the layout, so it is the fallback when no letter is reported: Windows reports no symbol
    /// with Ctrl held, and without this AZERTY Ctrl+Shift+A (the key in QWERTY's Q position) read as
    /// <c>ctrl+shift+q</c>, which is quit.
    /// </param>
    public static string? Unshifted(string? code, string? keySymbol, KeyModifiers modifiers, bool optionComposes = false,
        string? virtualLetter = null) =>
        Unshifted(code, keySymbol, modifiers, KeyboardLayouts.None, optionComposes, virtualLetter);

    /// <summary>
    /// The character the key types with no modifier held, as <see cref="Unshifted(string?, string?, KeyModifiers, bool, string?)"/>,
    /// asking <paramref name="layout"/> where the event cannot say and the US layout was the guess.
    /// </summary>
    /// <remarks>
    /// <para>With no character reported (Windows with Ctrl held), the virtual letter still comes first,
    /// since Windows' follows the layout; the layout answers for the rest: German Ctrl+ß is <c>ß</c>, not
    /// the <c>-</c> US keeps there, and AZERTY Ctrl+à is <c>à</c>, not <c>0</c>.</para>
    /// <para>When Option composed (macOS), the layout comes before the virtual letter, which is then
    /// Avalonia's US-position fallback: AZERTY Cmd+Option+Z is <c>z</c>, not the <c>w</c> of its position.</para>
    /// </remarks>
    /// <param name="layout">The current keyboard layout; <see cref="KeyboardLayouts.None"/> keeps the US guess.</param>
    public static string? Unshifted(string? code, string? keySymbol, KeyModifiers modifiers, IKeyboardLayout layout,
        bool optionComposes = false, string? virtualLetter = null)
    {
        var composed = AltComposed(keySymbol, modifiers, optionComposes);
        var produced = composed ? null : Produced(keySymbol);
        if (!modifiers.HasFlag(KeyModifiers.Shift) && produced is not null)
            return produced;
        if (ShiftMadeTheDigit(code, produced, modifiers))
            return null;
        if (produced is { Length: 1 } && char.IsLetter(produced[0]))
            return produced;
        var own = produced is null && code is not null ? Produced(layout.Unshifted(code)) : null;
        if (composed && own is not null)
            return own;
        if (IsVirtualLetter(virtualLetter))
            return virtualLetter!.ToLowerInvariant();
        return own ?? UsUnshifted(code);
    }

    private static bool IsVirtualLetter(string? virtualLetter) =>
        virtualLetter is { Length: 1 } && char.IsAsciiLetter(virtualLetter[0]);

    /// <summary>
    /// Whether Shift is what made a number-row key type its own digit, as on AZERTY (Shift+à is 0).
    /// </summary>
    /// <remarks>
    /// <para>The proper answer needs the layout's unmodified character, which Avalonia does not report
    /// (on macOS it would take our own <c>UCKeyTranslate</c> call), so this is a narrow rule instead:
    /// on a <c>DigitN</c> key with Shift held, a reported <c>N</c> means the unshifted character is
    /// something else. That holds on every layout whose number row is shifted (French, Belgian, Czech,
    /// Slovak, Lithuanian); a layout typing the digit both with and without Shift would get it wrong,
    /// and none is known to.</para>
    /// <para>Then the unshifted character is unknown rather than the US guess (the same digit), so the
    /// matcher sees a produced character different from it and treats Shift as consumed: Cmd+Shift+à
    /// matches <c>Meta+0</c>.</para>
    /// </remarks>
    public static bool ShiftMadeTheDigit(string? code, string? produced, KeyModifiers modifiers) =>
        modifiers.HasFlag(KeyModifiers.Shift)
        && code is { Length: 6 } && code.StartsWith("Digit", StringComparison.Ordinal)
        && produced is { Length: 1 } && produced[0] == code[5];

    /// <summary>
    /// A key event as the matcher sees it. <paramref name="keySymbol"/> is what the platform says the
    /// key produced (Avalonia's <c>KeySymbol</c>); on macOS that is the layout's character with Shift,
    /// Caps Lock and Option applied and Ctrl and Cmd ignored, so Cmd+Shift+= reports <c>+</c>.
    /// </summary>
    /// <param name="optionComposes">True on macOS, where Option changes the reported character.</param>
    /// <param name="virtualLetter">The virtual key's letter, if it is one; see <see cref="Unshifted(string?, string?, KeyModifiers, bool, string?)"/>.</param>
    public static KeyPress ToPress(KeyModifiers modifiers, string? code, string? keySymbol, bool optionComposes = false,
        string? virtualLetter = null) =>
        ToPress(modifiers, code, keySymbol, KeyboardLayouts.None, optionComposes, virtualLetter);

    /// <summary>
    /// A key event as the matcher sees it, as <see cref="ToPress(KeyModifiers, string?, string?, bool, string?)"/>,
    /// asking <paramref name="layout"/> for the key's own characters where the event cannot say.
    /// </summary>
    /// <remarks>
    /// With no character reported and Shift held (Windows Ctrl+Shift), the layout's shifted character is
    /// what the key produced, so a Shift that made it is consumed: AZERTY Ctrl+Shift+à types 0 and matches
    /// <c>ctrl+0</c>. A letter keeps the virtual key's answer, which Shift does not change.
    /// </remarks>
    /// <param name="layout">The current keyboard layout; <see cref="KeyboardLayouts.None"/> keeps the US guess.</param>
    public static KeyPress ToPress(KeyModifiers modifiers, string? code, string? keySymbol, IKeyboardLayout layout,
        bool optionComposes = false, string? virtualLetter = null)
    {
        // A composed character (Option+A is å) is not what an alt: trigger names; the key is.
        var composed = AltComposed(keySymbol, modifiers, optionComposes);
        var produced = composed ? null : Produced(keySymbol);
        if (produced is null && !composed && code is not null && modifiers.HasFlag(KeyModifiers.Shift)
            && !IsVirtualLetter(virtualLetter))
            produced = Produced(layout.Shifted(code));
        return new KeyPress(modifiers, code, Unshifted(code, keySymbol, modifiers, layout, optionComposes, virtualLetter), produced);
    }

    /// <summary>
    /// A binding as a person reads it: <c>⇧⌘P</c> on macOS, <c>Ctrl+Shift+P</c> elsewhere; the steps
    /// of a chord separated by a space.
    /// </summary>
    public static string Display(IEnumerable<KeyTrigger> sequence, KeymapPlatform platform) =>
        string.Join(' ', sequence.Select(t => Display(t, platform)));

    /// <summary>Format an existing Avalonia gesture using the same platform conventions.</summary>
    public static string Display(KeyGesture gesture, KeymapPlatform platform) =>
        Display(new KeyTrigger(gesture.KeyModifiers, KeyKind.Physical,
            KeymapDispatcher.FromVirtualKey(gesture.Key) ?? gesture.Key.ToString()), platform);

    /// <summary>One trigger as a person reads it.</summary>
    public static string Display(KeyTrigger trigger, KeymapPlatform platform)
    {
        var mac = platform == KeymapPlatform.MacOS;
        var m = trigger.Modifiers;
        var sb = new StringBuilder();
        if (mac)
        {
            if (m.HasFlag(KeyModifiers.Control)) sb.Append('⌃');
            if (m.HasFlag(KeyModifiers.Alt)) sb.Append('⌥');
            if (m.HasFlag(KeyModifiers.Shift)) sb.Append('⇧');
            if (m.HasFlag(KeyModifiers.Meta)) sb.Append('⌘');
            return sb.Append(KeyLabel(trigger, mac)).ToString();
        }
        if (m.HasFlag(KeyModifiers.Control)) sb.Append("Ctrl+");
        if (m.HasFlag(KeyModifiers.Alt)) sb.Append("Alt+");
        if (m.HasFlag(KeyModifiers.Shift)) sb.Append("Shift+");
        if (m.HasFlag(KeyModifiers.Meta)) sb.Append(platform == KeymapPlatform.Windows ? "Win+" : "Super+");
        return sb.Append(KeyLabel(trigger, mac)).ToString();
    }

    private static string KeyLabel(KeyTrigger trigger, bool mac)
    {
        var key = trigger.Key;
        if (trigger.Kind != KeyKind.Physical)
            return key == " " ? "Space" : key.ToUpperInvariant();
        if (key.StartsWith("Key", StringComparison.Ordinal) && key.Length == 4) return key[3..];
        if (key.StartsWith("Digit", StringComparison.Ordinal)) return key[5..];
        if (UsUnshifted(key) is { } ch && ch != " ") return ch;
        return key switch
        {
            "ArrowUp" => "↑", "ArrowDown" => "↓", "ArrowLeft" => "←", "ArrowRight" => "→",
            "PageUp" => mac ? "⇞" : "PgUp", "PageDown" => mac ? "⇟" : "PgDn",
            "Home" => mac ? "↖" : "Home", "End" => mac ? "↘" : "End",
            "Enter" => mac ? "↩" : "Enter", "Backspace" => mac ? "⌫" : "Backspace",
            "Delete" => mac ? "⌦" : "Del", "Escape" => mac ? "⎋" : "Esc", "Tab" => mac ? "⇥" : "Tab",
            "Space" => "Space", "Insert" => "Ins",
            _ => key,
        };
    }
}
