using CommunityToolkit.Mvvm.ComponentModel;

namespace Avalloy.Preferences;

/// <summary>
/// App-agnostic appearance preferences: the theme/density slots, variant choice,
/// accent behavior, and per-variant token overrides that <c>ThemeService</c>
/// consumes via <see cref="IAppearanceModel"/>. A Avalloy app gets theming by
/// embedding this directly in its root preferences model, or by subclassing it to
/// add app-specific fields (e.g. Desktop's alternating-rows toggle).
/// <para>
/// Slot names default to empty so this shared model carries no app-specific theme
/// names; the actual fallback comes from the per-app default-light/default-dark
/// names injected into <c>ThemeService</c>. A subclass can set its own fresh-start
/// defaults in its constructor.
/// </para>
/// </summary>
public partial class AppearancePrefs : ObservableObject, IAppearanceModel
{
    /// <summary>Name of an applied density preset — references the density catalog.</summary>
    [ObservableProperty] private string _densityPreset = "normal";

    /// <summary>Name of the theme preset rendered while the resolved variant is
    /// Light. Together with <see cref="ThemePresetDark"/> + <see cref="ThemeVariant"/>
    /// this drives the two-slot theme model: the variant decides which slot is
    /// active, and the slot name resolves to a single-variant theme.</summary>
    [ObservableProperty] private string _themePresetLight = "";

    /// <summary>Name of the theme preset rendered while the resolved variant is Dark.</summary>
    [ObservableProperty] private string _themePresetDark = "";

    /// <summary>Which slot is currently rendering. Pinned by every theme click on
    /// a Themes page. <see cref="ThemeVariantPreference.System"/> defers to the OS.</summary>
    [ObservableProperty] private ThemeVariantPreference _themeVariant = ThemeVariantPreference.System;

    /// <summary>Id of the active icon pack. Defaults to Avalloy's baseline
    /// <c>"lucide"</c>; an icon-pack picker writes the chosen pack's id here and
    /// <c>AvalloyApp.ApplyIconPack</c> pushes it onto the shared resolver.</summary>
    [ObservableProperty] private string _iconPackId = "lucide";

    /// <summary>Id of the active file-type icon pack used by the file explorer (e.g.
    /// <c>"material"</c> or <c>"vscode-icons"</c>). Distinct from <see cref="IconPackId"/>,
    /// which selects the monochrome command-glyph pack. The app seeds <c>FileIconMap</c>
    /// from this at startup and writes the explorer's pack picker choice back here.</summary>
    [ObservableProperty] private string _fileIconPackId = "material";

    /// <summary>Selected monospace font token (references a <c>ThemeService</c> mono-font catalog entry; empty =
    /// catalog default). Drives every code surface (editor, terminal, transcript code blocks) via
    /// <c>AlloyCodeFontFamily</c>.</summary>
    [ObservableProperty] private string _monoFont = "";

    /// <summary>Selected UI font token (references a <c>ThemeService</c> UI-font catalog entry; empty = catalog
    /// default). Drives the general UI font via <c>AlloyBodyFontFamily</c> — kept proportional.</summary>
    [ObservableProperty] private string _uiFont = "";

    /// <summary>When true, OS appearance changes (light↔dark) automatically flip
    /// <see cref="ThemeVariant"/> to match. When false, the variant stays where it
    /// was pinned and ignores OS changes.</summary>
    [ObservableProperty] private bool _autoSwitchVariant = true;

    /// <summary>When true (default), the accent selection wash follows the live OS
    /// accent, overriding the active theme's value for that one token. Other accent
    /// brushes stay theme-owned either way.</summary>
    [ObservableProperty] private bool _useSystemAccent = true;

    /// <summary>When true (default), the control focus ring follows the live OS
    /// accent rather than the theme's neutral high-contrast adorner, so focus matches
    /// the platform's native convention (e.g. the macOS accent glow). Independent of
    /// <see cref="UseSystemAccent"/> — focus is an OS/accessibility cue, toggled on
    /// its own.</summary>
    [ObservableProperty] private bool _useSystemFocusColor = true;

    /// <summary>Per-token overrides applied while the Light slot is active. Keys are
    /// token names (or directive names); values are JSON-syntax strings the cascade
    /// re-parses (e.g. <c>"#FF8800"</c> for a Color, <c>"0,3,0,0"</c> for a
    /// Thickness).</summary>
    [ObservableProperty] private Dictionary<string, string> _themeOverridesLight = new();

    /// <summary>Per-token overrides applied while the Dark slot is active.</summary>
    [ObservableProperty] private Dictionary<string, string> _themeOverridesDark = new();

    // IAppearanceModel exposes the overrides as read-only; the concrete dictionaries
    // satisfy that contract via explicit implementation.
    IReadOnlyDictionary<string, string> IAppearanceModel.ThemeOverridesLight => ThemeOverridesLight;
    IReadOnlyDictionary<string, string> IAppearanceModel.ThemeOverridesDark => ThemeOverridesDark;
}
