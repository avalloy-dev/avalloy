# The starter app

`demos/Avalloy.Starter` in the repo is the seed for a new app: copy the folder, rename the
namespace, and you have a window with a docking sidebar, a title bar, a command stack with
keybindings, a settings overlay, persisted preferences and window geometry, light and dark
themes that follow the OS, and a launch splash.

```sh
git clone https://github.com/avalloy-dev/avalloy
dotnet run --project avalloy/demos/Avalloy.Starter
```

## What it wires

**Commands.** Each command is a class with a `[Command]` attribute and an `ExecuteAsync`.
Dependencies come through the constructor because commands are resolved from DI per
invocation. Gestures are bound in one dictionary, with `Mod+` standing for Cmd on macOS and
Ctrl elsewhere.

```csharp
[Command("starter.sidebarPin", DisplayName = "Pin sidebar open", Icon = "Panel",
    Category = "Shell", AllowWhenTextInputFocused = true)]
public sealed class ToggleSidebarPinCommand(ShellViewModel shell) : IAlloyCommand
{
    public Task ExecuteAsync() { shell.SidebarPinned = !shell.SidebarPinned; return Task.CompletedTask; }
}

public static readonly IReadOnlyDictionary<string, IReadOnlyList<string>> DefaultKeymap =
    new Dictionary<string, IReadOnlyList<string>>
    {
        ["starter.sidebarPin"] = ["Mod+B"],
        ["starter.toggleTheme"] = ["Mod+T"],
        ["starter.settings"] = ["Mod+OemComma"],
        ["starter.quit"] = ["Mod+Q"],
        ["ShowLogViewer"] = ["Shift+F8"],
    };
```

```csharp
protected override void ConfigureServices(IServiceCollection services)
{
    services.AddSingleton(_prefs!);
    services.AddSingleton<ShellViewModel>();
    services.AddSingleton<MainWindow>();

    Commands.Register<ToggleSidebarPinCommand>();
    Commands.Register<ToggleThemeCommand>();
    Commands.Register<OpenSettingsCommand>();
    Commands.Register<QuitCommand>();
    Commands.Register<ShowLogViewerCommand>();   // Avalloy's own log viewer window
}

protected override IReadOnlyDictionary<string, IReadOnlyList<string>> BuildKeymap() =>
    StarterCommands.DefaultKeymap;
```

**The sidebar.** Column 0 of the body grid is the reserved column. `SidebarDockController`
drives its width between the rail and the persisted docked width with the dock morph, and
owns the edge pull on the seam. The shell view model implements `ISidebarHost`, so the
framework owns the mechanics and the app owns the state.

**The splash.** Two overrides opt in to the deferred launch: `CreateSplashWindow` returns an
`AlloySplashWindow`, and `PrepareLaunchAsync` reports phases through it while the slow boot
work runs. The main window is shown and painted underneath before the splash closes. Delete
both overrides for the classic synchronous launch.

**Preferences and geometry.** `StarterPreferences` embeds `AppearancePrefs`, a
`WindowGeometryPrefs` implementing `IMaximizableWindowGeometry`, and a shell section. Window
size, position and maximized state restore on open and save on close through
`WindowGeometry.BindGeometry`.

## The other demo

`demos/NativeThemeDemo` is the smallest working app on Desktop plus Themes: one window, a theme
picker over the two templates and a custom "Ocean" theme, and a gallery of the type ramp,
surfaces and controls. It is the fastest way to see what a token change does.
