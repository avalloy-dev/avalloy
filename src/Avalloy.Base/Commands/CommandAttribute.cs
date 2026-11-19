namespace Avalloy.Commands;

/// <summary>
/// Marks an <see cref="IAlloyCommand"/> implementation and publishes the metadata
/// the registry needs to expose the command to menus, toolbars, palettes, and
/// the keymap editor. Required for assembly scanning via
/// <see cref="CommandRegistry.RegisterFromAssembly"/>; explicit
/// <see cref="CommandRegistry.Register{T}"/> calls also read from it.
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class CommandAttribute : Attribute
{
    public CommandAttribute(string name)
    {
        Name = name;
    }

    /// <summary>Stable identifier referenced from the keymap, menus, and
    /// toolbar customization. Never displayed.</summary>
    public string Name { get; }

    /// <summary>Human-readable label shown in menus and the keyboard editor.
    /// Defaults to <see cref="Name"/> if unset.</summary>
    public string? DisplayName { get; init; }

    /// <summary>Grouping label used for sectioning in keyboard / toolbar editors.
    /// Defaults to <c>"General"</c> if unset.</summary>
    public string? Category { get; init; }

    /// <summary>Token referencing the glyph for toolbar / menu / palette rendering.
    /// Null = no graphic.</summary>
    public string? Icon { get; init; }

    /// <summary>Informational: the command's effect is reversible via the app's
    /// undo stack. Surfaces use this to render an "Undo X" affordance.</summary>
    public bool IsUndoable { get; init; }

    /// <summary>Informational: invocation usually prompts the user before
    /// running (destructive actions). Surfaces can append a "…" to the label.
    /// The dispatcher does NOT invoke a confirm dialog itself — commands that
    /// need confirmation inject the dialog service and prompt inside
    /// <see cref="IAlloyCommand.ExecuteAsync"/>.</summary>
    public bool RequiresConfirmation { get; init; }

    /// <summary>The command appears in the toolbar customization surface.
    /// Granular cursor moves / internal toggles set this false even though
    /// they're rebindable.</summary>
    public bool ShowInToolbar { get; init; }

    /// <summary>When true, the keyboard dispatcher may run this command even
    /// while a text-editing control has focus. Keep false for editing chords
    /// and file operations; set true for app-level commands like tab/window
    /// management that should remain global.</summary>
    public bool AllowWhenTextInputFocused { get; init; }
}
