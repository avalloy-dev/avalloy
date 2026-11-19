# Migrating to 0.23.0

0.23.0 collapses Avalloy's public packages to three, in a straight line:

```
Avalloy.Common   plain .NET, no Avalonia
   ↑
Avalloy.Base     Avalonia's base package only
   ↑
Avalloy.Desktop  the desktop shell
```

A desktop app references **Avalloy.Desktop** alone, a terminal app references **Avalloy.Base**, and
headless code (an app's engine, a CLI, a service) references **Avalloy.Common** and gets no Avalonia.
Namespaces are unchanged, so `using` lines and `using:` XAML namespaces keep compiling. What changes is
package references, `avares://` URIs, and anything that names an assembly. It is a rebuild-required
release: no type forwards are left from the removed assemblies.

## Packages

| Was (0.22.0) | Now (0.23.0) |
| --- | --- |
| `Avalloy.Core` | `Avalloy.Desktop`, or `Avalloy.Base` for code that needs no desktop |
| `Avalloy.Themes` | `Avalloy.Desktop` |
| `Avalloy` (meta-package) | `Avalloy.Desktop` |
| `Avalloy.Abstractions` | `Avalloy.Common` |
| `Avalloy.Cli` | `Avalloy.Common` |
| `Avalloy.Logging` | `Avalloy.Common` |
| `Avalloy.Icons` | `Avalloy.Common` |
| `Avalloy.IconTool` | unchanged |

Drop the references that become duplicates: an app that referenced Core, Themes and Icons now
references Avalloy.Desktop once. Desktop brings Base and Common with it, and Base brings Common.

```xml
<!-- 0.22.0 -->
<PackageReference Include="Avalloy.Core" Version="0.22.0" />
<PackageReference Include="Avalloy.Themes" Version="0.22.0" />
<PackageReference Include="Avalloy.Icons" Version="0.22.0" />

<!-- 0.23.0 -->
<PackageReference Include="Avalloy.Desktop" Version="0.23.0" />
```

## `avares://` URIs

| Was | Now |
| --- | --- |
| `avares://Avalloy.Core/Assets/iconpacks/lucide/{icon}.svg` | `avares://Avalloy.Desktop/Assets/iconpacks/lucide/{icon}.svg` |
| `avares://Avalloy.Core/Themes/ShellChrome.axaml` | `avares://Avalloy.Desktop/Themes/ShellChrome.axaml` |
| `avares://Avalloy.Core/Themes/OverlayScrollBars.axaml` | `avares://Avalloy.Desktop/Themes/OverlayScrollBars.axaml` |
| anything else under `avares://Avalloy.Core/` | the same path under `avares://Avalloy.Desktop/` |
| `avares://Avalloy.Themes/Themes/Tokens.axaml` | `avares://Avalloy.Desktop/Themes/Tokens.axaml` |
| `avares://Avalloy.Themes/Themes/Type.axaml` | `avares://Avalloy.Desktop/Themes/Type.axaml` |
| `avares://Avalloy.Themes/Themes/ControlStyles.axaml` | `avares://Avalloy.Desktop/Themes/ControlStyles.axaml` |
| `avares://Avalloy.Themes/Themes/AlloyControls.axaml` | `avares://Avalloy.Desktop/Themes/AlloyControls.axaml` |
| `avares://Avalloy.Themes/Themes/Template.Light.axaml` | `avares://Avalloy.Desktop/Themes/Template.Light.axaml` |
| `avares://Avalloy.Themes/Themes/Template.Dark.axaml` | `avares://Avalloy.Desktop/Themes/Template.Dark.axaml` |

The `ThemeDescriptor` URIs an app passes to `NativeThemeController` are strings, so a stale one fails
at runtime, not at build time. Search for both old roots, not just the XAML.

## Assembly names

- `;assembly=` in a XAML `clr-namespace`: `Avalloy.Core` becomes `Avalloy.Desktop` or `Avalloy.Base`,
  depending on where the type now lives; `Avalloy.Themes` becomes `Avalloy.Desktop`;
  `Avalloy.Abstractions`, `Avalloy.Cli`, `Avalloy.Logging` and `Avalloy.Icons` become `Avalloy.Common`.
- Anything else that names an assembly as a string changes the same way: `Assembly.Load(...)`, an
  `InternalsVisibleTo`, a trimmer root descriptor.
- Presets: the bundled theme and density presets stay in Avalloy.Desktop under the same names, so
  `PresetCatalog.Load(typeof(AvalloyApp).Assembly, "Avalloy.Presets.themes.")` works as before. Found
  through `typeof(PresetCatalog).Assembly` instead, they come back empty: that is Avalloy.Base now.

## Baking your own icon packs

The `<IconPack>` build targets moved from the Avalloy.Icons package to Avalloy.Common
(`build/Avalloy.Common.targets`), with the same item contract. NuGet imports a package's build targets
only for a direct reference, so a project that bakes packs references Avalloy.Common directly, even
though Desktop brings it. The baker in the tool manifest is still `Avalloy.IconTool`. See
[Baking a pack](/icons/baking).

## Versions

Avalloy.Common, Base and Desktop release together at one version.
