# Theming seams

The theming seams do not draw; they live in `Avalloy.Base`, and `ThemeService` in `Avalloy.Desktop`. It owns the lifecycle, one apply at startup and a re-apply on every OS
accent or light/dark change, and hands the work to whichever engine the app answers with.

## IThemeController

The seam `AvalloyApp` drives. An engine resolves an `IAppearanceModel`, the theme slots,
variant and overrides, and pushes the result into the running application's resources. Two
implementations ship:

| Engine | Package | Theme is | Best for |
| --- | --- | --- | --- |
| `NativeThemeController` | Avalloy.Desktop | A hand-authored `ResourceDictionary` per theme, swapped live | Apps with a designed palette; the templates are the starting point |
| `ThemeService` | Avalloy.Desktop | A JSON preset of tokens through the cascade, plus density and user overrides | Apps where users pick and tweak themes |

An app bringing its own engine implements the interface and returns it from
`ResolveThemeController`.

## ThemeService

The app-agnostic engine over presets. It knows nothing about what draws the controls;
everything specific is injected: the `TokenSchema` to clear and parse against, the default
light and dark theme names, and two optional delegates that bracket the generic phases.

- `preApply` runs at phase 2, after the schema clear and before the theme's tokens land.
  This is where a control-theme engine consumes directives, an accent ramp or a neutral tint,
  and regenerates its palette, so the theme's own values then layer on top of freshly
  generated ones. The dictionary is bare here; read `ThemeApplyContext.EffectiveDirective`.
- `postApply` runs at phase 7, after user overrides and before `Applied` fires. This is where
  an app derives resources from final values, pane-focus geometry, say, and where an adapter
  mirrors resolved tokens into its own slots. Any earlier and a theme's font would silently
  overwrite the user's pick.

`ThemeApplyContext` is the state both delegates receive: the live dictionary, the resolved
theme, the effective overrides, the active variant, and the source preferences, with helpers
for set-if-absent and derived resources.

`TabStripTheming.Apply` is the worked example: compose `TabStripTheming.Schema` into your
allowlist and call it from `postApply`, and the strip's lookups resolve whichever app embeds
the control.

## The pieces both engines share

- `ThemeVariantResolver` turns the appearance model into the effective `ThemeVariant`:
  explicit light or dark pins it, `System` asks the OS through `PlatformSettings`. One place,
  so the policy cannot drift between engines. Degrades to dark when the host cannot be queried.
- `AvalloyTheme` is the notification hub. An engine calls `NotifyApplied` after pushing a
  theme; controls subscribe to `Applied` and re-resolve engine-published tokens without a
  dependency on any engine.
- `IAccentPublisher` publishes the theme's canonical accent as `{Prefix}AccentBrush`,
  `{Prefix}AccentColor` and `{Prefix}AccentSelectionBrush`, refreshed at startup, on variant
  flip and on OS accent change. Native-theme apps usually need none; their dictionaries
  define the accent directly.

## The palette arithmetic

`NeutralPalette` derives the chrome palette, surfaces, hairlines and text tiers, from a
neutral ramp addressed by shade number. `DefaultNeutralRamp` builds eleven anchors from a
midtone colour, tints mixed in sRGB and shades in linear RGB, because the mixing space
changes the result at both ends. The arithmetic and the ramp are separate on purpose: the
arithmetic is small and exactly specified, the ramp is colour science, and a bug in one must
not be mistaken for a bug in the other. `SurfaceContext` and `ColorLocation` name where in
the chrome a colour is being asked for.
