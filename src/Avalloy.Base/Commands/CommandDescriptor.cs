using System.Diagnostics.CodeAnalysis;
using System.Reflection;

namespace Avalloy.Commands;

/// <summary>
/// Runtime metadata view of a registered command. Built once from the
/// <see cref="CommandAttribute"/> on the implementation type and cached in
/// the <see cref="CommandRegistry"/>. Surfaces (menus, toolbar, keyboard
/// editor) read from this rather than reaching for the attribute themselves.
/// </summary>
public sealed class CommandDescriptor
{
    public required string Name { get; init; }
    /// <summary>The command's type. AvalloyApp registers it in DI, which constructs it, so its
    /// public constructors are kept when the app is trimmed.</summary>
    [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)]
    public required Type ImplementationType { get; init; }
    public required string DisplayName { get; init; }
    public required string Category { get; init; }
    public string? Icon { get; init; }
    public bool IsUndoable { get; init; }
    public bool RequiresConfirmation { get; init; }
    public bool ShowInToolbar { get; init; }
    public bool AllowWhenTextInputFocused { get; init; }

    /// <summary>Build a descriptor from a command implementation type. The
    /// type must carry a <see cref="CommandAttribute"/>; throws otherwise so
    /// registration failures surface at startup rather than at first invoke.</summary>
    public static CommandDescriptor FromType([DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] Type type)
    {
        var attr = type.GetCustomAttribute<CommandAttribute>()
            ?? throw new InvalidOperationException(
                $"Type {type.FullName} is missing [Command]. " +
                $"Either decorate it with [Command(\"name\", ...)] or remove it from registration.");

        return new CommandDescriptor
        {
            Name = attr.Name,
            ImplementationType = type,
            DisplayName = attr.DisplayName ?? attr.Name,
            Category = attr.Category ?? "General",
            Icon = attr.Icon,
            IsUndoable = attr.IsUndoable,
            RequiresConfirmation = attr.RequiresConfirmation,
            ShowInToolbar = attr.ShowInToolbar,
            AllowWhenTextInputFocused = attr.AllowWhenTextInputFocused,
        };
    }
}
