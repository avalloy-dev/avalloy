# Baking a pack

A pack starts as a directory of SVGs and ends as a checked-in `<id>.icons.axaml` and
`pack.json`. Baking happens at build time, only when the SVGs change, so consumers of your app
never run the tool.

## In your app

Install the baker into the project's tool manifest once:

```sh
dotnet new tool-manifest
dotnet tool install Avalloy.IconTool
```

Referencing the `Avalloy.Common` package directly imports a targets file that bakes any
`IconPack` items you declare before build. (It shipped in `Avalloy.Icons` until 0.23.0. NuGet
imports a package's build targets only for a direct reference, so getting Common through
`Avalloy.Desktop` is not enough.)

```xml
<ItemGroup>
  <IconPack Include="lucide">
    <Name>Lucide</Name>
    <SrcDir>$(MSBuildProjectDirectory)\Assets\iconpacks\lucide</SrcDir>
    <OutDir>$(MSBuildProjectDirectory)\Assets\iconpacks\lucide</OutDir>
    <DuoDir></DuoDir>
    <Roles></Roles>
    <Description>Lucide (ISC)</Description>
    <Homepage>https://lucide.dev</Homepage>
    <Version>0.2.0</Version>
  </IconPack>
  <IconPackInput Include="$(MSBuildProjectDirectory)\Assets\iconpacks\lucide\*.svg" />
</ItemGroup>
```

`IconPackInput` is what makes it incremental: the target's inputs are your SVGs and its
output the baked file, so an unchanged pack costs nothing. Set `SkipBakeIconPacks=true` to
skip it entirely. The tool version is pinned in the manifest, so an upgrade is an explicit
act; re-bake by touching an SVG or deleting the baked output.

## By hand

```sh
avalloy-icontool create --id lucide --name Lucide --src Assets/iconpacks/lucide --out Assets/iconpacks/lucide \
    [--duo <duoDir>] [--roles <icons.md>] [--desc <text>] [--homepage <url>] [--version <v>] [--ns <controlNamespace>]
```

`--ns` sets the control namespace the emitted dictionary imports, so the same writer serves
any consuming app.

## Duotone and small variants

Two conventions the flattener understands. A **duotone** icon is a two-tone active SVG in a
parallel `--duo` directory: the back layer carries an `opacity` attribute, the front layer does
not, and the baker keeps both so `GlyphIcon` can swap to the lit layer on hover. A **small
variant** is a simplified drawing used at sizes up to a threshold, fed to the baker alongside
the base glyph.

## What the pure library does

`SvgFlattener` turns SVG text into Avalonia path data: `path`, `circle` and `rect`, a baked
translate-plus-scale transform, fill-rule detection, and the opacity-split duotone
convention. `IconBaker` composes a full `IconGlyphSpec` from the base, duotone and small
variant SVGs, taking fill and stroke metadata from the source so stroke-only Lucide glyphs
bake correctly. `IconPackWriter` emits the dictionary and the manifest as strings. None of it
touches a file, which is what lets the same code run at build time in the tool and at load
time in Desktop for user packs, and be tested without either.
