using Avalloy.Commands.Keymaps;

namespace Avalloy.Commands;

/// <summary>Opt-in command with an opaque parameter and host-owned invocation target.</summary>
public interface IParameterizedCommand : IAlloyCommand
{
    bool CanExecute(string? parameter, object? target = null) => true;

    /// <summary>Three-state gate. <see cref="ActionReadiness.Unavailable"/> means the command exists only to be
    /// listed (a stub), so a key bound to it passes through as if nothing were registered.
    /// Defaults to <see cref="CanExecute"/>: Ready or NotNow.</summary>
    ActionReadiness Readiness(string? parameter, object? target = null) =>
        CanExecute(parameter, target) ? ActionReadiness.Ready : ActionReadiness.NotNow;

    Task ExecuteAsync(string? parameter, object? target = null);
    Task IAlloyCommand.ExecuteAsync() => ExecuteAsync(null);
}
