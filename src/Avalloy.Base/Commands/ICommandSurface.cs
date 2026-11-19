namespace Avalloy.Commands;

/// <summary>
/// Implemented by the shell window so child views (toolbar, content host,
/// command palette) can fire a command by name. The <see cref="ShellWindow"/>
/// base supplies a default implementation that delegates to
/// <c>AvalloyApp.Keyboard.RunByNameAsync</c>, so most subclasses don't need
/// to wire anything.
/// </summary>
public interface ICommandSurface
{
    /// <summary>Dispatch a registered command by name. No-op when the command
    /// isn't found or its <see cref="IConditionalCommand.CanExecute"/> gate is
    /// closed. Errors should be surfaced to the user by the implementation,
    /// not thrown.</summary>
    Task RunCommandAsync(string commandName);
}
