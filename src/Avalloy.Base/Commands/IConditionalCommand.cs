namespace Avalloy.Commands;

/// <summary>
/// Opt-in extension of <see cref="IAlloyCommand"/> for commands whose execution
/// is gated on runtime state (selection non-empty, undo stack non-empty,
/// etc.). The dispatcher checks <see cref="CanExecute"/> before
/// <see cref="IAlloyCommand.ExecuteAsync"/>; menu and toolbar surfaces read it
/// for enable/disable.
/// </summary>
public interface IConditionalCommand : IAlloyCommand
{
    /// <summary>True when the command is currently executable. Must be cheap
    /// — invoked synchronously on menu render and on every matching keystroke.</summary>
    bool CanExecute();
}
