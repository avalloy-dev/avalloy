using Avalonia.Input;
using Avalloy.Commands.Keymaps;

namespace Avalloy.Base.Tests;

// Compact fixtures for the keymap tests. Production accepts typed models only.
internal static class KeymapFixtures
{
    internal static KeymapBinding Binding(string fixture)
    {
        var options = KeyBindingOptions.None;
        if (fixture.StartsWith("performable:")) { options |= KeyBindingOptions.ConsumeIfExecutable; fixture = fixture[12..]; }
        if (fixture.StartsWith("unconsumed:")) { options |= KeyBindingOptions.PassThrough; fixture = fixture[11..]; }
        var split = fixture.LastIndexOf('=');
        var triggers = fixture[..split].Split('>').Select(Trigger).ToArray();
        var action = fixture[(split + 1)..].Split(':', 2);
        return new(triggers, [new(action[0], action.Length > 1 ? action[1] : null)], options);
    }

    internal static KeyTrigger Trigger(string text)
    {
        var mods = KeyModifiers.None;
        while (true)
        {
            var i = text.IndexOf('+');
            if (i <= 0) break;
            var m = text[..i] switch
            {
                "super" => KeyModifiers.Meta, "ctrl" => KeyModifiers.Control,
                "alt" => KeyModifiers.Alt, "shift" => KeyModifiers.Shift, _ => KeyModifiers.None,
            };
            if (m == KeyModifiers.None) break;
            mods |= m; text = text[(i + 1)..];
        }
        var code = text switch
        {
            "up" or "arrow_up" => "ArrowUp", "down" => "ArrowDown", "escape" => "Escape",
            "digit_1" => "Digit1", "enter" => "Enter", "backquote" => "Backquote", _ => null,
        };
        return new(mods, code is null ? KeyKind.Unicode : KeyKind.Physical, code ?? text);
    }

    internal static Keymap Map(params string[] fixtures)
    {
        var builder = new KeymapBuilder();
        foreach (var fixture in fixtures) builder.Bind(Binding(fixture));
        return builder.Build();
    }
}
