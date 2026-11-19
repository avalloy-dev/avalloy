<div class="av-banner acc-core"><img src="/marks/core.svg" alt="Avalloy.Desktop" /><span class="name"><span class="lite">Avalloy.</span>Desktop</span></div>

# The app shell

`Avalloy.Desktop` is the layer an app sits on. Root namespace `Avalloy`. It has one dependency
that matters, Avalonia, plus the Microsoft.Extensions family for DI, configuration and
logging, and CommunityToolkit.Mvvm. It carries no error reporter: an app that wants one
adds it in `ConfigureLogging`.

It is built on **`Avalloy.Base`**, which holds the parts that need no desktop: commands,
keymaps and the palette's view model, preferences, presets and the token cascade, and the
theming seams. Base references Avalonia's base package and nothing that draws, so a terminal
app on Consolonia takes Base alone. Both build on **`Avalloy.Common`**, plain .NET with no
Avalonia: the shared contracts (`IUserNotifier`, `AppPaths`), `CliRunner` and `LoginShellPath`,
the logging stack and the icon baker. Desktop brings both with it, and the namespaces are the
same either way.

```sh
dotnet add package Avalloy.Desktop
# or, without a desktop:
dotnet add package Avalloy.Base
# or, without Avalonia at all:
dotnet add package Avalloy.Common
```

| Area | The pieces | Page |
| --- | --- | --- |
| The app | `AvalloyApp`, `ShellAvalloyApp`, the deferred launch and `AlloySplashWindow`, `SingleInstanceGuard`, `AppRelaunch` | [The app](/core/app) |
| Windows | `ShellWindow`, `ShellDialog`, `AlloyTitleBar`, `TitleBarHeaderReserve`, `MacOSTitleBar`, `WindowGeometry`, `WindowPlacement` | [Windows and the title bar](/core/window) |
| Commands | `IAlloyCommand`, `[Command]`, `CommandRegistry`, `KeyboardDispatcher`, `GestureParser`, the palette, the shortcut editor | [Commands and keybindings](/core/commands) |
| Preferences | `PreferencesService<T>`, `PreferencesLocation`, presets, `ResourceCascade`, `TokenSchema`, `AppearancePrefs` | [Preferences](/core/preferences) |
| Theming | `IThemeController`, `ThemeService`, `IAccentPublisher`, `AvalloyTheme`, `ThemeVariantResolver`, `NeutralPalette` | [Theming seams](/core/theming) |
| Controls | `TabStrip`, the sidebar framework, the field controls, `OverlayCard`, `PreferencesOverlay`, attached behaviours | [Controls](/core/controls) |
| Services | Dialogs, toasts, logging (from `Avalloy.Common`) and the log viewer, the folder picker, the macOS helpers | [Services](/core/services) |
| Processes | `CliRunner`, `CliResult` and `LoginShellPath`, from `Avalloy.Common` | [CliRunner](/core/cli) |

## The shape of it

Three ideas run through the package.

**Seams, not switches.** Anything an app might do differently is an interface the app class
answers: `ResolveThemeController`, `ResolveAccentPublisher`, `IDialogService`,
`IKeymapStore`, `IUserNotifier`. The default implementations are real, but nothing is sealed
in.

**Tokens, not styles.** Controls bind resource keys, `Surface.Pane`, `Text.Secondary`,
`Radius.Card`, and never a literal. A theme is a dictionary of those keys; swapping it
repaints the app live. The keys themselves are the [contract](/themes/tokens).

**Engine-agnostic controls.** `TabStrip`, `GlyphIcon` and the rest read their tokens back
through `AvalloyTheme.Applied`, so the same control works under the native engine, the
preset cascade, or an engine you bring.

## Namespaces

| Namespace | Holds |
| --- | --- |
| `Avalloy` | `AvalloyApp`, `ShellWindow`, `ShellDialog`, `WindowGeometry` |
| `Avalloy.Commands` | The command stack |
| `Avalloy.Preferences` | Preferences, presets, the cascade |
| `Avalloy.Services` | Dialogs, toasts, logging, `ThemeService`, the platform helpers |
| `Avalloy.Theming` | The theme seams and the palette arithmetic |
| `Avalloy.Controls.*` | `TabStrip`, the sidebar, the title bar, the log viewer, the splash |
| `Avalloy.Views.Controls` | The field controls, overlays, the palette view |
| `Avalloy.Icons` | `GlyphIcon`, `IconResolver`, the picker |
| `Avalloy.AttachedProperties` | `HoverIntent`, `AutoScroll`, `InlineRename`, `MacOSTitleBar`, ... |
| `Avalloy.Typography` | `FontCatalog`, `FontFaceDef`, `FontFaceVerification`, `CapCentering` |
| `Avalloy.Cli` | `CliRunner`, `CliResult` |
