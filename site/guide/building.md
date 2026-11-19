# Building the repo

```sh
git clone https://github.com/avalloy-dev/avalloy
cd avalloy
dotnet build Avalloy.slnx
dotnet test --solution Avalloy.slnx
```

A fresh clone builds with nuget.org alone. No private feed, no credential, no tool restore.
The icon baker is an in-repo project that `build/iconpacks.targets` runs when a pack's SVGs
change, and the baked output is checked in, so a build never needs it unless you edit an SVG.

CI runs the same build, test and pack on ubuntu, macOS and Windows, on GitHub-hosted runners.
Three legs because the pieces are genuinely per-platform: the title bar, the theme controller
that follows the OS, and the updater all take different paths on each.

## Layout

| Path | What |
| --- | --- |
| `src/Avalloy.Common` | Plain .NET: the shared contracts (`IUserNotifier`, `AppPaths`), `CliRunner`, the logging stack, the icon baker |
| `src/Avalloy.Base` | Preferences, the token cascade, commands and keymaps: no desktop |
| `src/Avalloy.Desktop` | The app shell, with the native theme engine and templates |
| `src/Avalloy.IconTool` | The icon baker as a dotnet tool |
| `tests/` | One MSTest project per package, on Microsoft Testing Platform |
| `demos/` | The starter app and the smallest themed app |
| `site/` | This site |

Every project under `src/` packs. Packing is off by default in the root
`Directory.Build.props` and switched on in `src/Directory.Build.props`, so tests and demos can
never accidentally ship. One `<VersionPrefix>` versions the whole repo in lockstep.

## Changes and releases

Changes are recorded with `changerig add` and released with `shiprig`, from
[rigsmith](https://rigsmith.dev). A user-facing change gets a changeset in the same pull
request; `changerig version` bumps the manifests and writes each package's `CHANGELOG.md`
from those changesets, cascading to dependents. The release credential comes from the
environment as `NUGET_API_KEY` and is never in the tree.

## Contributing

The XML doc comments are the design notes and ship in the packages. When you add a public
type, say what it is for and what it deliberately does not do; when you change behaviour, say
why in the commit. Pull requests run the three-platform CI.
