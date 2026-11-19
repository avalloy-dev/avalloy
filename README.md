# Avalloy

An application platform for [Avalonia](https://avaloniaui.net) desktop apps: app bootstrap, window
chrome, a token-driven theme engine that follows the OS light/dark switch, commands and keybindings
with a command palette, preferences with hot reload, and an icon pipeline. It is the layer a desktop
app sits on, so that the app itself is only the app.

.NET 10, Avalonia 12.1. MIT licensed. Docs: [avalloy.dev](https://avalloy.dev).

## Packages

Three packages in a straight line, each building on the one before, plus a build-time tool. They
are versioned and released together.

```
Avalloy.Common   plain .NET, no Avalonia
   ↑
Avalloy.Base     Avalonia's base package only
   ↑
Avalloy.Desktop  the desktop shell
```

A desktop app references `Avalloy.Desktop` alone; a terminal app references `Avalloy.Base`;
headless code (an app's engine, a CLI, a service) references `Avalloy.Common`.

- **`Avalloy.Common`** — the plain-.NET floor, with no Avalonia and no dependency beyond
  Microsoft.Extensions.Logging.Abstractions: the shared contracts (`IUserNotifier`, which
  Desktop's toasts implement, and `AppPaths`, where an app's config, data and logs live), bounded
  child-process execution (`CliRunner`, `CliResult`: always time-boxed, kills the process tree on
  expiry, reports failure in-band), `LoginShellPath` (a Dock-launched app gets its login shell's
  PATH), the logging stack (the crash-safe `FileLoggerProvider`, the `LogBuffer` ring, the `Trace`
  bridge, `IEventRecorder`, the static `AlloyLogging`), and the pure-BCL SVG baker with the
  `<IconPack>` build targets.
- **`Avalloy.Base`** — what an app needs without a desktop: preferences with hot reload,
  presets and the theme token cascade, the theming seams, commands, keymaps and the command
  palette's view model. References Avalonia's base package only (no desktop platform, Fluent or
  Skia), so a terminal app on Consolonia can ship it as a Native AOT binary.
- **`Avalloy.Desktop`** — the app shell on top of Base: `AvalloyApp` bootstrap, `ShellWindow`
  and the title bar, the bundled theme and density presets, dialogs, toasts, the theme engines
  (`ThemeService`, and `NativeThemeController` with the token contract and the light/dark
  templates), tabs, the sidebar, the control layer and the bundled Lucide icons with `GlyphIcon`.
- **`Avalloy.IconTool`** — the build-time icon baker as a dotnet tool (`avalloy-icontool`),
  over Common's baker.

Coming from the earlier `Avalloy.Core` and `Avalloy.Themes` packages: see
[Migrating](site/guide/migrating.md) for the package and `avares://` mapping.

## Getting started

```sh
dotnet add package Avalloy.Desktop
```

Then look at the two demos:

- `demos/NativeThemeDemo` is the smallest working app: an `AvalloyApp` with the native theme
  controller, and preferences that remember the picked theme.
- `demos/Avalloy.Starter` is a starter app to copy and rename when you begin a new one
  ([guide](site/guide/starter.md)).

## Building

```sh
dotnet build Avalloy.slnx
dotnet test --solution Avalloy.slnx
```

A fresh clone restores from nuget.org alone, with no credentials. The baked icon output is checked
in, and `<IconPack>` re-bakes a pack only when its SVGs change.

Changes are recorded with `changerig add` and released with `shiprig`
([rigsmith](https://rigsmith.dev)). The release credential comes from the environment and is never
in the tree.

## License

Avalloy is licensed under the [MIT License](LICENSE).

Bundled third-party material keeps its own license: the Lucide icons (ISC, with MIT for the
icons derived from Feather). See [`THIRD-PARTY-NOTICES.md`](THIRD-PARTY-NOTICES.md).
