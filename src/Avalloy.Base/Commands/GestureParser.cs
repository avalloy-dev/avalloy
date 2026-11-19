using Avalonia.Input;

namespace Avalloy.Commands;

/// <summary>
/// Wraps <see cref="KeyGesture.Parse"/> with "Mod+" sugar that resolves to Cmd
/// on macOS and Ctrl elsewhere. Keeps preferences.json portable across platforms
/// while leveraging Avalonia's existing parser for everything else.
/// </summary>
public static class GestureParser
{
    /// <summary>Parses a gesture string. Returns null on parse failure.</summary>
    public static KeyGesture? TryParse(string? input)
    {
        if (string.IsNullOrWhiteSpace(input)) return null;
        var normalized = SubstituteMod(input);
        // KeyGesture.Parse throws FormatException for shape problems and
        // ArgumentException when a token doesn't match a Key or modifier name.
        try { return KeyGesture.Parse(normalized); }
        catch (System.FormatException) { return null; }
        catch (System.ArgumentException) { return null; }
    }

    /// <summary>
    /// Builds a "Mod+"-portable representation of a runtime <see cref="KeyGesture"/>.
    /// If the gesture's modifiers include the platform-default modifier (Meta on
    /// macOS, Control elsewhere) and don't include the off-platform one, that
    /// modifier is rendered as "Mod" so the binding stays cross-platform.
    /// </summary>
    public static string ToPortableString(KeyGesture gesture)
    {
        var mods = gesture.KeyModifiers;
        var parts = new System.Collections.Generic.List<string>(4);

        if (OperatingSystem.IsMacOS())
        {
            if ((mods & KeyModifiers.Meta) != 0) parts.Add("Mod");
            if ((mods & KeyModifiers.Control) != 0) parts.Add("Ctrl");
        }
        else
        {
            if ((mods & KeyModifiers.Control) != 0) parts.Add("Mod");
            if ((mods & KeyModifiers.Meta) != 0) parts.Add("Meta");
        }
        if ((mods & KeyModifiers.Alt) != 0) parts.Add("Alt");
        if ((mods & KeyModifiers.Shift) != 0) parts.Add("Shift");
        parts.Add(gesture.Key.ToString());
        return string.Join("+", parts);
    }

    private static string SubstituteMod(string input)
    {
        var primary = OperatingSystem.IsMacOS() ? "Cmd" : "Ctrl";
        var result = System.Text.RegularExpressions.Regex.Replace(
            input, @"\bMod\b", primary, System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        // Bare digit tokens ("Mod+1", "Mod+Shift+2") translate to the
        // D-prefixed Key enum name ("D1", "D2"). Without this, Avalonia's
        // KeyConverter takes the bare digit as the integer Key value —
        // "1" → Key.Cancel (enum=1), "2" → Key.Back (enum=2), "3" →
        // Key.Tab (enum=3). That misbinding silently collides with real
        // Cmd+Back / Cmd+Tab keystrokes; we don't catch it because both
        // sides parse to a "valid" KeyGesture. Saw it bite when
        // Mod+2/Move overwrote Mod+Back/Delete in the keymap dictionary.
        result = System.Text.RegularExpressions.Regex.Replace(
            result, @"(?<=^|\+)([0-9])(?=\+|$)", "D$1");
        return result;
    }
}
