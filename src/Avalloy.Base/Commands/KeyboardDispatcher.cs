using Avalonia.Input;
using Microsoft.Extensions.DependencyInjection;

namespace Avalloy.Commands;

/// <summary>
/// Looks up the command bound to a key gesture, resolves the singleton command
/// through the DI container, and invokes it. Owns the inverted
/// (gesture → command name) dictionary built from the keymap source.
/// <para>
/// The keymap source is a delegate so the dispatcher stays decoupled from any
/// particular preferences backend. Apps usually wire it to a function that
/// merges built-in defaults with user overrides and call <see cref="Rebuild"/>
/// when those overrides change.
/// </para>
/// </summary>
public sealed class KeyboardDispatcher
{
    private readonly CommandRegistry _registry;
    private readonly IServiceProvider _services;
    private readonly Func<IReadOnlyDictionary<string, IReadOnlyList<string>>> _keymapSource;
    private Dictionary<KeyGesture, string> _gestureToCommand = new();
    private Func<KeyGesture, bool, Task?>? _fallback;

    public KeyboardDispatcher(
        CommandRegistry registry,
        IServiceProvider services,
        Func<IReadOnlyDictionary<string, IReadOnlyList<string>>> keymapSource)
    {
        _registry = registry;
        _services = services;
        _keymapSource = keymapSource;
        Rebuild();
    }

    /// <summary>Create an opt-in sequence dispatcher sharing this command registry and services.</summary>
    public Keymaps.KeymapDispatcher CreateKeymapDispatcher(Keymaps.Keymap keymap) => new(this, keymap);

    /// <summary>
    /// Look up the gesture in the active keymap and invoke the bound command.
    /// Returns the command's task if a binding matched (caller should set
    /// <c>e.Handled = true</c> and await), or null if nothing matched.
    /// </summary>
    public Task? TryDispatch(KeyEventArgs e, bool textInputFocused = false)
    {
        var gesture = new KeyGesture(e.Key, e.KeyModifiers);
        if (_gestureToCommand.TryGetValue(gesture, out var commandName))
        {
            // Gesture mapped but command unregistered → return null (not our event)
            // rather than CompletedTask, so other key handlers downstream still get
            // their shot. This matters during incremental migrations where the
            // keymap references commands that haven't been registered yet.
            var descriptor = _registry.Get(commandName);
            if (descriptor is null) return null;
            if (textInputFocused && !descriptor.AllowWhenTextInputFocused) return null;
            return RunByNameAsync(commandName);
        }
        // The static keymap is authoritative; only on a miss do we offer the
        // gesture to the fallback (favorites' dynamic per-entry hotkeys). This
        // ordering guarantees a registered command can never be shadowed.
        return _fallback?.Invoke(gesture, textInputFocused);
    }

    /// <summary>Register a resolver consulted when no static binding matches a
    /// gesture. Returns a task to claim the gesture, or null to decline. Used by
    /// the host app to dispatch runtime-defined bindings (e.g. favorites) without
    /// teaching the dispatcher about them. At most one fallback; last call wins.</summary>
    public void SetGestureFallback(Func<KeyGesture, bool, Task?>? fallback) => _fallback = fallback;

    /// <summary>Resolve and invoke a command by name. Surfaces (toolbar
    /// buttons, palettes) that know the command name but not its body call
    /// through here. Returns <see cref="Task.CompletedTask"/> if the name is
    /// unregistered or the command's <see cref="IConditionalCommand.CanExecute"/>
    /// gate is closed.</summary>
    public Task RunByNameAsync(string commandName)
    {
        return RunByNameAsync(commandName, null);
    }

    /// <summary>Invoke a command with an opaque parameter and optional host target.</summary>
    public Task RunByNameAsync(string commandName, string? parameter, object? target = null)
    {
        var command = Resolve(commandName);
        if (command is null || !CanExecute(command, parameter, target)) return Task.CompletedTask;
        return command is IParameterizedCommand pc ? pc.ExecuteAsync(parameter, target) : command.ExecuteAsync();
    }

    /// <summary>Whether this name has a registered implementation.</summary>
    public bool IsRegistered(string commandName) => _registry.Get(commandName) is not null;

    /// <summary>Read a parameter-aware execution gate without running the command.</summary>
    public bool CanRun(string commandName, string? parameter, object? target = null)
    {
        var command = Resolve(commandName);
        return command is not null && CanExecute(command, parameter, target);
    }

    /// <summary>Three-state gate: Unavailable for an unregistered name or a command that reports itself
    /// unavailable, NotNow for a closed gate, otherwise Ready.</summary>
    public Keymaps.ActionReadiness Readiness(string commandName, string? parameter, object? target = null)
    {
        var command = Resolve(commandName);
        return command is null ? Keymaps.ActionReadiness.Unavailable : Readiness(command, parameter, target);
    }

    private IAlloyCommand? Resolve(string name) => _registry.Get(name) is { } descriptor
        ? (IAlloyCommand)_services.GetRequiredService(descriptor.ImplementationType) : null;

    private static bool CanExecute(IAlloyCommand command, string? parameter, object? target) =>
        Readiness(command, parameter, target) == Keymaps.ActionReadiness.Ready;

    // A command's own Unavailable outranks a closed conditional gate: it is not there at all.
    private static Keymaps.ActionReadiness Readiness(IAlloyCommand command, string? parameter, object? target)
    {
        var own = command is IParameterizedCommand pc ? pc.Readiness(parameter, target) : Keymaps.ActionReadiness.Ready;
        if (own == Keymaps.ActionReadiness.Unavailable) return own;
        return command is IConditionalCommand cc && !cc.CanExecute() ? Keymaps.ActionReadiness.NotNow : own;
    }

    /// <summary>Returns the command name (if any) bound to a given gesture in
    /// the active keymap. Useful for menus that want to show the active
    /// shortcut next to a command label.</summary>
    public string? CommandForGesture(string gestureText)
    {
        var g = GestureParser.TryParse(gestureText);
        if (g is null) return null;
        return _gestureToCommand.TryGetValue(g, out var name) ? name : null;
    }

    /// <summary>Would <see cref="RunByNameAsync"/> actually do something? False for an unregistered
    /// name or a command whose <see cref="IConditionalCommand.CanExecute"/> gate is closed.
    /// <para>Surfaces that LIST commands need this, because <see cref="RunByNameAsync"/> answers a
    /// closed gate by silently completing — which is right for a keystroke and wrong for a menu row
    /// or palette entry, where offering a choice that does nothing is worse than not offering it.</para></summary>
    public bool CanRun(string commandName)
    {
        return CanRun(commandName, null);
    }

    /// <summary>The gestures that currently FIRE a command — the reverse of
    /// <see cref="CommandForGesture"/>, and the direction a menu or palette actually needs: it holds the
    /// command and wants the shortcut to print beside it. Empty for an unbound command.
    /// <para>Read from the resolved map, not the keymap source, so a gesture another command won on
    /// last-write-wins is absent here rather than printed next to a row it no longer triggers. Sorted
    /// for a stable display order — the inverted map has none of its own.</para></summary>
    public IReadOnlyList<string> GesturesFor(string commandName)
    {
        List<string>? found = null;
        foreach (var (gesture, name) in _gestureToCommand)
        {
            if (!string.Equals(name, commandName, StringComparison.Ordinal)) continue;
            (found ??= []).Add(GestureParser.ToPortableString(gesture));
        }
        if (found is null) return [];
        found.Sort(StringComparer.Ordinal);
        return found;
    }

    /// <summary>Re-pull the keymap from the source. Call this from the
    /// preferences-changed handler when bindings might have shifted.</summary>
    public void Rebuild()
    {
        var next = new Dictionary<KeyGesture, string>();
        foreach (var (commandName, gestures) in _keymapSource())
        {
            foreach (var gestureStr in gestures)
            {
                var gesture = GestureParser.TryParse(gestureStr);
                if (gesture is null) continue;
                next[gesture] = commandName; // last write wins on conflict
            }
        }
        _gestureToCommand = next;
    }
}
