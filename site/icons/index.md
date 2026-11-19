<div class="av-banner acc-icons"><img src="/marks/icons.svg" alt="The icon pipeline" /><span class="name"><span class="lite">Avalloy · </span>Icons</span></div>

# The icon pipeline

One control renders every glyph, the geometry is authored once as an SVG, and packs swap as a
dictionary. `Avalloy.Common` holds the pure part, the flattener, the baker and the pack writer
(namespace `Avalloy.Icons.Baking`, the separate `Avalloy.Icons` package until 0.23.0), with no
Avalonia dependency; `Avalloy.IconTool` is that baker as a dotnet tool; and Desktop holds
the runtime, `GlyphIcon`, the resolver and the picker, plus a baseline
[Lucide](https://lucide.dev) pack.

## Rendering a glyph

```xml
<icons:GlyphIcon Glyph="chevron-up" Size="16" Foreground="{DynamicResource Text.Secondary}"/>
```

`GlyphIcon` resolves the `Icon.<Glyph>` resource from the nearest scope, so it works inside a
per-theme `ThemeVariantScope` and pack switching is a dictionary swap, and draws it tinted by
`Foreground`, which inherits the ambient text colour. Fill or stroke per the glyph's metadata;
duotone glyphs swap to their lit layer on pointer-over. An id is the SVG filename without its
extension, and packs follow the Lucide convention of one file per icon named uniformly, so
swapping packs keeps the same command set working.

Curated **roles** are aliases a pack bakes in, `Chevron`, `Check`, `Close`, `Warning`,
`Success` and the shell staples, so Desktop's control templates can reference a role and any
pack that bakes the roles satisfies them.

`FileIcon` is the full-colour file-type badge from the Material Icon Theme, keyed by
extension; an unknown extension renders nothing and takes no space.

## Packs

`IconResolver` discovers packs, bundled through `AvaloniaResource` and installed by the user,
exposes the active one, and resolves ids. Sources are ordered and later ones override earlier
ones by pack id, so an app can ship its own `lucide` over Avalloy's baseline, and user packs
override all bundled. Override `AppIconPackSources` on the app class to contribute yours, and
`DefaultIconPackId` to pick the default.

A bundled pack is a baked `<id>.icons.axaml` plus `pack.json` under
`Assets/iconpacks/<id>/`, parsed by the XAML loader. A user pack is raw SVGs, baked at load
time through `IconBaker` and `IconGlyphMaterializer`.

`IconPickerView` and `IconPickerViewModel` are the reusable picker: a search box over a grid
of swatches, raising exactly one picked id. Build one over the active pack with
`FromResolver`, and fill the `Footer` slot with app affordances such as image upload.

## Baking

SVGs become XAML geometry at build time. See [Baking a pack](/icons/baking).
