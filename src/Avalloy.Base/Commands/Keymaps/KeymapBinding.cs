using Avalonia.Input;

namespace Avalloy.Commands.Keymaps;

/// <summary>Character matching follows the layout; physical matching uses W3C key codes.</summary>
public enum KeyKind { Unicode, Physical }

/// <summary>A parsed trigger. Character keys should be lowercase printable code points.</summary>
public sealed record KeyTrigger(KeyModifiers Modifiers, KeyKind Kind, string Key)
{
    // An opaque lookup token: encoding prevents punctuation from colliding with sequence separators.
    public override string ToString() => $"{(int)Modifiers}:{(int)Kind}:{Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(Key))}";
}

/// <summary>A command name and its uninterpreted parameter. No action names are reserved.</summary>
public sealed record CommandInvocation(string Name, string? Parameter = null);

/// <summary>Optional rules for consuming a completed binding.</summary>
[Flags]
public enum KeyBindingOptions
{
    None = 0,
    ConsumeIfExecutable = 1,
    PassThrough = 2,
    /// <summary>Always consume, even with no actions. Implements a disabled key.</summary>
    Suppress = 4,
}

/// <summary>A parsed sequence and ordered command chain. The keymap snapshots its lists.</summary>
public sealed record KeymapBinding(IReadOnlyList<KeyTrigger> Sequence,
    IReadOnlyList<CommandInvocation> Actions, KeyBindingOptions Options = KeyBindingOptions.None)
{
    public CommandInvocation? Action => Actions.Count == 0 ? null : Actions[0];
}
