using System.Text.Json.Serialization;

namespace Avalloy.Preferences;

/// <summary>User's light/dark choice for theme variants.
/// <list type="bullet">
///   <item><c>System</c> — follow the OS variant via Avalonia's
///         <c>PlatformSettings.GetColorValues()</c>; flips when the OS changes.</item>
///   <item><c>Light</c> / <c>Dark</c> — explicit, ignores the OS.</item>
/// </list>
/// Themes that ship only one variant pin to that variant regardless of this
/// preference.</summary>
[JsonConverter(typeof(CamelCaseEnumConverter<ThemeVariantPreference>))]
public enum ThemeVariantPreference { System, Light, Dark }

/// <summary>
/// The read contract <c>ThemeService</c> consumes to apply a theme. Lives in
/// Avalloy so the theming engine is app-agnostic: any Avalloy app exposes
/// an appearance section implementing this (typically by embedding or
/// subclassing the concrete <see cref="AppearancePrefs"/>) and gets theming for
/// free. The members are exactly the inputs the cascade + engine directives
/// need — nothing file-manager-specific.
/// </summary>
public interface IAppearanceModel
{
    /// <summary>Name of an applied density preset — references the density
    /// catalog. Empty falls back to the engine's default density.</summary>
    string DensityPreset { get; }

    /// <summary>Theme preset name rendered while the resolved variant is Light.
    /// Empty (or missing in the catalog) falls back to the app's injected
    /// default-light name.</summary>
    string ThemePresetLight { get; }

    /// <summary>Theme preset name rendered while the resolved variant is Dark.</summary>
    string ThemePresetDark { get; }

    /// <summary>Which slot is currently rendering. <c>System</c> defers to the
    /// OS; explicit Light/Dark pin the slot.</summary>
    ThemeVariantPreference ThemeVariant { get; }

    /// <summary>Id of the active icon pack — references the resolver's discovered
    /// packs (see <c>IconResolver.AvailablePacks</c>). Empty falls back to the
    /// resolver's default pack. <c>AvalloyApp.ApplyIconPack</c> pushes this onto
    /// the shared <c>IconResolver</c> at startup and whenever the app re-applies.</summary>
    string IconPackId { get; }

    /// <summary>Selected monospace font token — references a <c>ThemeService</c> mono-font catalog entry. Empty
    /// falls back to the catalog default. Resolves to <c>AlloyCodeFontFamily</c> (the code-font
    /// code-font slot).</summary>
    string MonoFont { get; }

    /// <summary>Selected UI font token — references a <c>ThemeService</c> UI-font catalog entry. Empty falls back
    /// to the catalog default. Resolves to <c>AlloyBodyFontFamily</c> (the body/UI
    /// font slot, so it must stay proportional — never a monospace).</summary>
    string UiFont { get; }

    /// <summary>When true, OS appearance changes flip <see cref="ThemeVariant"/>
    /// to match so the Light/Dark slots swap with the system.</summary>
    bool AutoSwitchVariant { get; }

    /// <summary>When true, the focused/selection accent wash follows the live OS
    /// accent, overriding the active theme's value for that one token.</summary>
    bool UseSystemAccent { get; }

    /// <summary>When true, the control focus ring follows the live OS accent
    /// (instead of the theme's neutral high-contrast adorner), matching the
    /// platform's native focus convention. Independent of <see cref="UseSystemAccent"/>.</summary>
    bool UseSystemFocusColor { get; }

    /// <summary>Per-token overrides applied while the Light slot is active.
    /// Keys are token names (or directive names); values are JSON-syntax strings
    /// the cascade re-parses.</summary>
    IReadOnlyDictionary<string, string> ThemeOverridesLight { get; }

    /// <summary>Per-token overrides applied while the Dark slot is active.</summary>
    IReadOnlyDictionary<string, string> ThemeOverridesDark { get; }
}
