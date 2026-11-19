namespace Avalloy.Preferences;

/// <summary>
/// The app-agnostic token allowlist + directive keys the shared
/// <see cref="Avalloy.Services.ThemeService"/> understands. Apps
/// either pass <see cref="Schema"/> directly (minimal theming) or
/// <see cref="TokenSchema.Compose"/> it with their own additions (e.g. Desktop's
/// Pane*/Tab* keys). The token <em>names</em> carry the <c>Alloy*</c> prefix — a
/// brand-neutral namespace owned by the Avalloy (not any one consuming app), so a
/// theme JSON reads identically across every consumer.
/// </summary>
public static class AvalloyTokens
{
    /// <summary>Theme directive that selects the accent-color family. Carries a
    /// string name ("Orange", "Blue", …) the ThemeService translates to the
    /// underlying engine's ramp. NOT a resource token — kept out of
    /// <see cref="Schema"/> so the cascade doesn't try to write it as a brush.</summary>
    public const string AccentRampDirective = "AlloyAccentRamp";


    /// <summary>Theme directive that casts the generated chrome neutrals (window/container backgrounds,
    /// borders, neutral foregrounds) toward a hue — a hex string like "#0E3A44". Drives a
    /// <c>TintedNeutralPaletteFactory</c> so a theme can fully recolor the shell, not just content. Omit for
    /// the default grayscale neutrals. NOT a resource token (it reconfigures the palette).</summary>
    public const string NeutralTintDirective = "AlloyNeutralTint";

    /// <summary>The generic color/typography/accent tokens any Avalloy app can
    /// theme. Density-engine output (sizes) layered by app schemas as needed.</summary>
    public static TokenSchema Schema { get; } = new(new Dictionary<string, TokenKind>
    {
        // -- Accent --
        ["AlloyAccentBrush"]               = TokenKind.Color,
        ["AlloyAccentSelectionBrush"]      = TokenKind.Color,
        ["AlloyAccentSelectionForeground"] = TokenKind.Color,

        // -- Body text color --
        ["AlloyBodyForeground"]            = TokenKind.Color,

        // -- Body font (UI text) --
        ["Font.Ui"]                      = TokenKind.FontFamily,
        ["AlloyBodyFontWeight"]            = TokenKind.FontWeight,
        ["AlloyBodyFontSizeSmall"]         = TokenKind.FontSize,
        ["AlloyBodyFontSizeMedium"]        = TokenKind.FontSize,
        ["AlloyBodyFontSizeLarge"]         = TokenKind.FontSize,
        ["AlloyHeaderFontWeight"]          = TokenKind.FontWeight,

        // -- Code font (mono) --
        ["Font.Mono"]                    = TokenKind.FontFamily,
        ["AlloyCodeFontSize"]              = TokenKind.FontSize,
    });
}
