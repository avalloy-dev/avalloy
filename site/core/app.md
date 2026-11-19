# The app

`AvalloyApp` is the base `Application`. It owns the bootstrap so the app class is a list of
answers to hooks rather than a page of wiring.

## What it does on startup

```mermaid
sequenceDiagram
    participant A as AvalloyApp
    participant S as Splash (optional)
    participant W as Main window
    A->>A: ConfigureAppConfiguration, ConfigureLogging
    A->>A: LoginShellPath.Ensure (macOS/Linux)
    A->>A: ConfigureServices, register commands as singletons
    A->>A: build KeyboardDispatcher from BuildKeymap
    A->>S: CreateSplashWindow, show it
    A->>A: PrepareLaunchAsync(progress)
    A->>W: CreateMainWindow, publish on ShellHost
    A->>A: apply IThemeController, icon pack, accent
    A->>W: show; LaunchLanded
    A->>S: close once real content is up
```

The splash path is opt-in. With no `CreateSplashWindow` override the launch is synchronous.

## The hooks

| Override | What it answers |
| --- | --- |
| `ConfigureServices(IServiceCollection)` | Your services and commands. Every `CommandRegistry` entry is also registered as a singleton so the dispatcher can resolve it cheaply. |
| `CreateMainWindow()` | The main window. Published on `ShellHost` for dialog parenting. |
| `BuildKeymap()` | Command name to gestures. `Mod+` means Cmd on macOS and Ctrl elsewhere. |
| `ResolveThemeController()` | The [theme engine](/core/theming). Applied at startup and re-applied on every OS accent or light/dark change. |
| `GetThemeAppearance()` | The `IAppearanceModel` the engine reads, typically your embedded `AppearancePrefs`. |
| `ResolveAccentPublisher()` | Optional. Publishes the accent as `{Prefix}AccentBrush` resources for XAML to bind. |
| `ConfigureLogging(ILoggingBuilder)` | Add providers. The in-app buffer, console, debug and rolling file are already there. |
| `ConfigureAppConfiguration(IConfigurationBuilder)` | Extra configuration sources. |
| `DockIconAvaresUri` | The macOS Dock and Cmd-Tab icon, so it shows during `dotnet run` too. |
| `AppDataFolderName` | Where per-app state lives under the platform data folder. |
| `AppIconPackSources`, `DefaultIconPackId` | Your bundled [icon packs](/icons/); the default is `lucide`. |
| `CreateSplashWindow()`, `PrepareLaunchAsync(progress)`, `LaunchLanded(progress)` | The deferred launch. |
| `KeepRunningWhenClosed` | Close-to-background: the window hides and relaunching the app re-shows it. |
| `HandleDispatcherExceptions` | Catch unhandled exceptions on the UI thread instead of crashing. |

Two members are for the app to call: `RequestQuit()` when quitting must go through the
close-to-background guard, and `ReapplyTheme()` after a preference change.

## ShellAvalloyApp

`ShellAvalloyApp` is `AvalloyApp` with the batteries in. It registers the themed
`DialogService` and maps a `ResolveThemeService()` hook onto the engine-agnostic seam, so an
app on Desktop's own preset cascade overrides one method and returns its `ThemeService`. An app
on the native engine, or one bringing another engine, derives from `AvalloyApp` and answers
`ResolveThemeController` itself.

## One instance

`SingleInstanceGuard` makes the first process the primary and holds an exclusive lock file
for its lifetime. A later process fails to acquire, signals the primary over a named pipe to
come to the foreground, and exits. The OS releases the lock on exit, crash included, so there
is never a stale one. Both the lock and the pipe are namespaced per user.

`AppRelaunch` is the quit-and-come-back for settings that only take effect at boot. It cannot
simply spawn its replacement, because the new process would find the lock still held. So a
helper outlives the app, waits for the lock to release, and only then launches.

## The login-shell PATH

A GUI app launched from the Dock, Spotlight or a desktop launcher inherits the minimal system
PATH, because the login shell's rc files never run. Any bare-name tool the app spawns later
works from a terminal and comes up not found from the packaged app. `LoginShellPath` runs the
user's real login shell once at startup, under a four-second box through
[`CliRunner`](/core/cli), and merges its PATH into the process environment. Every later child
inherits it. On by default through `ResolveLoginShellPath`; a no-op on Windows. It ships in
`Avalloy.Common`, so a headless host can call `LoginShellPath.Ensure()` itself.

## Logging

The logging stack lives in `Avalloy.Common`, the plain .NET package, and Desktop wires it up.
`AlloyLogging` is the static accessor for code that is not DI-resolved. Four providers are
wired by default: `LogBufferProvider` first, so the in-app [log viewer](/core/services#logging)
sees startup logging; console and debug; and `FileLoggerProvider`, a rolling file written
synchronously and flushed per entry, because a background queue would drop exactly the entry
before a crash. Avalonia's own internal log, the binding errors it otherwise swallows, is
routed in through `AvaloniaLogSink`, and `System.Diagnostics.Trace` through the trace bridge.

The trace bridge logs under the `Trace` category. `Trace.TraceError`, `TraceWarning` and
`TraceInformation` keep their severity, and `Debug.Assert`/`Trace.Fail` log at Error. Plain
`Debug.WriteLine` and `Trace.WriteLine` log at Debug, below the default Information minimum,
so they never reach the file or the viewer. Libraries write anything through that channel; a
terminal control writes every keystroke, passwords included. Lowering the category with
`logging.AddFilter("Trace", LogLevel.Debug)` sends all of it to disk, so do that only for a
diagnostic build.

A CLI or service gets the same crash-safe file log without Avalonia: build a `LoggerFactory`
with `FileLoggerProvider.TryCreate(AppPaths.ForApp("MyTool"))`, then call
`AlloyLogging.Publish(factory, provider.FilePath)`.
