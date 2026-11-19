# The token contract

Every colour, radius, size and face a Desktop control draws with is a resource key. A theme is a
dictionary of those keys. Keep every key and change the values, and the whole app follows.

## Theme-independent: `Tokens.axaml`

Merged once and shared by every theme.

| Group | Keys |
| --- | --- |
| Faces | `Font.Ui`, `Font.Mono`, `Font.Display`, each with `.Weight`, `.WeightPlain` and `.Features` |
| Sizes | `Size.Eyebrow`, `Size.Caption`, `Size.Body`, `Size.BodyLg`, `Size.Title`, `Size.Display` |
| Radii | `Radius.Chip`, `.Input`, `.Swatch`, `.Card`, `.Pill`, `.Badge`, `.TileSm`, `.Button`, `.ButtonLg`, `.Row`, `.Field`, `.Tile`, `.Menu`, `.Pane`, `.Window` |
| Edges | `Edge.Hairline`, `Menu.ShadowRoom` |
| Tab strip | `AlloyTabFontFamily`, `AlloyTabFontSize`, `PaneTabStripPadding`, `PaneTabPadding`, `PaneTabSpacing`, `PaneTabMinHeight`, `PaneTabCornerRadius`, the active-tab indicator geometry |

The faces carry their own weights and features because neither travels with a family name.
A weight name is whatever a foundry assigned its ramp; two display families at the same
nominal weight can differ by a third in ink. `FontCatalog` writes all three when a face is
picked, which is what keeps swapping a face a choice rather than a silent restyling.

## Per variant: the templates

Each template defines the same set of keys. Colours come in pairs, `Surface.Pane.Color` and
`Surface.Pane`, the `Color` for APIs that need one and the brush for everything else.

| Group | Keys |
| --- | --- |
| Surfaces | `Surface.Canvas`, `.Window`, `.Pane`, `.Pane2`, `.Inset`, `.Hover`, `.Raised`, `.Raised2`, `.Active3`, `.Terminal`, `.CodeChip` |
| Lines | `Seam`, `Divider`, `Divider.Strong`, `Edge.Pane`, `.PaneStrong`, `.Quiet`, `.Card`, `.Chip`, `.Control`, `.ControlHover`, `.Menu` |
| Fields | `Field.Bg`, `.Placeholder`, `.Hairline`, `.Focus.Ring`, and the rest, hover, focus and error shadows |
| Menus and popovers | `Combo.Bg`, `Menu.Bg`, `Menu.Edge`, `Menu.Shadow`, `Shadow.Card`, `Shadow.Popover`, `Shadow.Drawer`, `Scrim`, `Focus.Ring` |
| Options | `Option.Hover.Bg`, `.Selected.Bg`, `.Focus.Bg`, `.Selected.Border`, `.Hover.Shadow`, `.Focus.Ring` |
| Borders | `Border.Strong`, `Border.Mid`, `Border.Soft`, `Border.AccentHover` |
| Text and accent | The text tiers, the accent, and the semantic colours for success, warning and danger |

The surfaces form a container ladder: each step sits one shade further from the page
background, and dark inverts the direction. That is the property `NeutralPalette` pins, and
it is why a brand theme should move the whole ladder rather than one rung.

## The type ramp: `Type.axaml`

Style classes on `TextBlock`, each binding a face and a size token: `display`, `title`,
`eyebrow` (uppercase, tracked), `secondary`, `muted`, `caption`, `mono`, and `accent` as a
modifier. Author copy with classes, not font properties, and the ramp follows the theme.

## The style vocabulary: `ControlStyles.axaml`

The classes Desktop's own views use and yours can: `card` for a pane on a surface, `actionbtn`
for the chip-style button, `ddrow` for a list row with `selected`, `bare` for an icon-only
button with no chrome, and the rest. `AlloyControls.axaml` carries the control themes that
swap Fluent's templates for the field controls and `DrawerPull`.

## Naming

Resource tokens use the brand-neutral `Alloy*` prefix where they are prefixed, so a theme file
reads identically across every app. Controls are unprefixed except where an Avalonia type name
collides: `AlloyTextBox`, `AlloySelect`, `AlloyDropDown`, `AlloyTitleBar`.
