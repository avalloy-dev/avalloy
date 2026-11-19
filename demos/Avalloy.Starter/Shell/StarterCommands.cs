using Avalonia.Controls.ApplicationLifetimes;
using Avalloy.Commands;

namespace Avalloy.Starter.Shell;

/// <summary>
/// The starter's app-level commands on Avalloy's command stack: one [Command] class per
/// chord, registered in <see cref="App.ConfigureServices"/> and bound in
/// <see cref="DefaultKeymap"/>. MainWindow.OnKeyDown feeds unhandled keys through
/// <c>App.Keyboard.TryDispatch</c> — add a command + a keymap row and the chord just works.
/// </summary>
public static class StarterCommands
{
    /// <summary>Command name → gesture strings ("Mod+" = ⌘ on macOS, Ctrl elsewhere).</summary>
    public static readonly IReadOnlyDictionary<string, IReadOnlyList<string>> DefaultKeymap =
        new Dictionary<string, IReadOnlyList<string>>
        {
            ["starter.sidebarPin"] = ["Mod+B"],
            ["starter.toggleTheme"] = ["Mod+T"],
            ["starter.settings"] = ["Mod+OemComma"],
            ["starter.quit"] = ["Mod+Q"],
            ["ShowLogViewer"] = ["Shift+F8"],
        };
}

[Command("starter.settings", DisplayName = "Settings", Icon = "Settings", Category = "Shell",
    AllowWhenTextInputFocused = true)]
public sealed class OpenSettingsCommand(ShellViewModel shell) : IAlloyCommand
{
    public Task ExecuteAsync() { shell.SettingsOpen = true; return Task.CompletedTask; }
}

[Command("starter.sidebarPin", DisplayName = "Pin sidebar open", Icon = "Panel", Category = "Shell",
    AllowWhenTextInputFocused = true)]
public sealed class ToggleSidebarPinCommand(ShellViewModel shell) : IAlloyCommand
{
    public Task ExecuteAsync() { shell.SidebarPinned = !shell.SidebarPinned; return Task.CompletedTask; }
}

[Command("starter.toggleTheme", DisplayName = "Toggle light/dark", Icon = "moon", Category = "Shell",
    AllowWhenTextInputFocused = true)]
public sealed class ToggleThemeCommand : IAlloyCommand
{
    public Task ExecuteAsync()
    {
        (Avalonia.Application.Current as App)?.ToggleTheme();
        return Task.CompletedTask;
    }
}

[Command("starter.quit", DisplayName = "Quit", Category = "Shell", AllowWhenTextInputFocused = true)]
public sealed class QuitCommand(IClassicDesktopStyleApplicationLifetime lifetime) : IAlloyCommand
{
    public Task ExecuteAsync() { lifetime.Shutdown(); return Task.CompletedTask; }
}
