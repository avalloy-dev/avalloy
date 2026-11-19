<div class="av-banner acc-themes"><img src="/marks/themes.svg" alt="Native themes, in Avalloy.Desktop" /><span class="name"><span class="lite">Avalloy.Desktop · </span>Themes</span></div>

# The native theme engine

The native theme engine is the pure-Avalonia theming model: a set of hand-authored merged
`ResourceDictionary` themes, one of which is swapped into `Application.Resources` at runtime
while `RequestedThemeVariant` is set so Fluent's own control resources track light and dark.
Controls bind tokens through `DynamicResource`, so a swap repaints live. It ships in
`Avalloy.Desktop` (it was the separate `Avalloy.Themes` package until 0.23.0), and nothing of it
applies until an app includes its dictionaries and returns a `NativeThemeController`.

```sh
dotnet add package Avalloy.Desktop
```

## What ships

| File | What |
| --- | --- |
| `Themes/Tokens.axaml` | Theme-independent tokens: fonts and their weights and features, sizes, radii, the tab-strip geometry. Merge it once in `App.axaml`. |
| `Themes/Template.Light.axaml`, `Themes/Template.Dark.axaml` | The two neutral palettes: every surface, edge, field, shadow, text tier and accent key. Copy one per brand theme. |
| `Themes/Type.axaml` | The type ramp as style classes: `display`, `title`, `eyebrow`, `secondary`, `muted`, `caption`, `mono`. |
| `Themes/ControlStyles.axaml` | The style vocabulary: `card`, `actionbtn`, `ddrow`, `bare`, and the rest. |
| `Themes/AlloyControls.axaml` | Control themes for Desktop's field controls and `DrawerPull`. |

## NativeThemeController

The `IThemeController` for this engine. Give it the catalog and the default ids, return it
from `ResolveThemeController`, and it resolves the appearance model's light and dark slots
and variant, `System` deferring to the OS, then swaps the dictionary and raises `Applied`.

```csharp
private static readonly IReadOnlyList<ThemeDescriptor> ThemeCatalog =
[
    new("Light", "avares://Avalloy.Desktop/Themes/Template.Light.axaml", Dark: false),
    new("Dark",  "avares://Avalloy.Desktop/Themes/Template.Dark.axaml",  Dark: true),
    new("Ocean", "avares://MyApp/Themes/Ocean.axaml",                   Dark: true),
];

protected override IThemeController? ResolveThemeController() =>
    _theme = new NativeThemeController(this, ThemeCatalog, defaultLightId: "Light", defaultDarkId: "Dark");

protected override IAppearanceModel? GetThemeAppearance() => Prefs.Appearance;
```

`ThemeDescriptor` is an id, the `avares://` URI of the dictionary, and whether it is dark.
`EffectiveThemeId(appearance)` tells you which one is showing. To change theme, set the slot
or variant on the appearance model and call `ReapplyTheme()`.

## Making it yours

Copy `Template.Dark.axaml` into your app, keep every key, and replace the values. That is the
whole contract; the [token page](/themes/tokens) lists the keys. A theme that drops a key gets
the Fluent default for whatever bound it, which is the failure mode to look for when a control
comes up bare.

`Converters` holds the reusable value converters keyed off the contract, `Accent`,
`Surface.*`, `Border.*`, `Text.*`, so any theme that ships those keys gets correct results.
