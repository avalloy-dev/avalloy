namespace Avalloy.Commands;

/// <summary>
/// A named, invokable action. Implementations are resolved per invocation
/// from the DI container, so dependencies are declared via the implementation's
/// constructor — no shared "context bag" is threaded through. Pair with
/// <see cref="CommandAttribute"/> to publish metadata (name, display label,
/// category, icon, …) the registry discovers via reflection.
/// </summary>
public interface IAlloyCommand
{
    /// <summary>Run the command. May be sync (return <see cref="Task.CompletedTask"/>)
    /// or async. Exceptions propagate to the invocation surface.</summary>
    Task ExecuteAsync();
}
