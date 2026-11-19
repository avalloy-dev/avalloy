using Avalonia;
using Avalloy.Controls.Tabs;
using Avalloy.Preferences;
using Avalloy.Services;

namespace Avalloy.Theming;

/// <summary>
/// The theme tail + token surface for <see cref="Controls.Tabs.TabStrip"/>. Apps
/// that host a TabStrip compose <see cref="Schema"/> into their token allowlist
/// and invoke <see cref="Apply"/> from their <see cref="ThemeService"/>
/// <c>postApply</c> delegate, so the strip's <c>DynamicResource</c> lookups
/// resolve regardless of which app embeds the control.
/// <para>
/// Token <em>names</em> stay <c>"Pane*"</c>/<c>"AlloyActiveTab*"</c> — they
/// shipped under those names in theme JSON before the control was extracted, and
/// renaming them would strand existing presets. The control is the file-manager-
/// agnostic owner now; the names are just history.
/// </para>
/// </summary>
public static class TabStripTheming
{
    /// <summary>Theme directive selecting the active-tab indicator shape:
    /// "Underline" (default) / "Overline" / "Border" / "Dot" / "None".</summary>
    public const string ActiveIndicatorDirective = "AlloyPaneTabActiveIndicator";

    // -- Schema tokens (theme-author-facing; see name-history note above). --
    public const string ActiveTabBackgroundKey      = "AlloyActiveTabBackground";
    public const string ActiveTabForegroundKey      = "AlloyActiveTabForeground";
    /// <summary>Hairline separating the strip from the content below it.</summary>
    public const string StripBorderBrushKey         = "AlloyTabStripBorderBrush";

    // -- Neutral chrome keys the (engine-agnostic) TabStripStyles bind to. The
    //    styles used to hit the theme engine's resources directly; since the control
    //    moved to Avalloy.Desktop they bind these instead, and Apply mirrors each
    public const string StripBackgroundKey       = "AlloyTabStripBackground";
    public const string HoverBackgroundKey       = "AlloyTabHoverBackground";
    public const string ButtonHoverBackgroundKey = "AlloyTabButtonHoverBackground";
    public const string GhostBackgroundKey       = "AlloyTabGhostBackground";
    public const string ForegroundKey            = "AlloyTabForeground";
    public const string ForegroundStrongKey      = "AlloyTabForegroundStrong";
    public const string ForegroundDimKey         = "AlloyTabForegroundDim";
    public const string SuccessBrushKey          = "AlloyTabSuccessBrush";
    public const string DangerBrushKey           = "AlloyTabDangerBrush";
    public const string FontFamilyKey            = "AlloyTabFontFamily";
    public const string FontSizeKey              = "AlloyTabFontSize";
    public const string StripPaddingKey              = "PaneTabStripPadding";
    public const string StripBorderThicknessKey      = "PaneTabStripBorderThickness";
    public const string TabPaddingKey               = "PaneTabPadding";
    public const string TabSpacingKey               = "PaneTabSpacing";
    public const string TabCornerRadiusKey          = "PaneTabCornerRadius";
    public const string TabMinHeightKey             = "PaneTabMinHeight";
    public const string ActiveIndicatorThicknessKey = "PaneTabActiveIndicatorThickness";

    // -- Derived resources synthesized by Apply (typed Thickness / size the XAML
    //    binds to; Avalonia won't auto-convert a scalar DynamicResource to these). --
    public const string ActiveUnderlineThicknessKey = "PaneTabActiveUnderlineThickness";
    public const string ActiveOverlineThicknessKey  = "PaneTabActiveOverlineThickness";
    public const string ActiveBorderThicknessKey    = "PaneTabActiveBorderThickness";
    public const string ActiveDotSizeKey            = "PaneTabActiveDotSize";

    /// <summary>Resolved indicator string published for the control to stamp as
    /// an <c>indicator-*</c> class. The control-side contract lives in
    /// <see cref="TabStripTokens"/> (Controls/Tabs); mirrored here for the Apply tail.</summary>
    public const string ActiveIndicatorKey = TabStripTokens.ActiveIndicatorKey;

    /// <summary>Default indicator size — matches the historical 3px underline.</summary>
    public const double DefaultActiveIndicatorThickness = 3.0;

    /// <summary>Default indicator shape when no theme sets the directive.</summary>
    public const string DefaultActiveIndicator = TabStripTokens.DefaultActiveIndicator;

    /// <summary>Token allowlist fragment for the tab strip. Compose into an app's
    /// schema so the cascade clears + parses these alongside its own tokens.</summary>
    public static TokenSchema Schema { get; } = new(new Dictionary<string, TokenKind>
    {
        // DefaultForeground (mirrored below) so a theme that omits them keeps the
        // historical look; a theme that wants a colored "selected tab" pill sets them.
        [ActiveTabBackgroundKey]      = TokenKind.Color,
        [ActiveTabForegroundKey]      = TokenKind.Color,
        // Strip/content divider: a theme recolors the hairline here and zeroes
        // StripBorderThicknessKey below to drop it entirely.
        [StripBorderBrushKey]         = TokenKind.Color,
        // NOTE: the neutral-chrome keys (StripBackgroundKey, HoverBackgroundKey, the
        // ForegroundKeys, fonts, …) are deliberately NOT schema tokens — they aren't
        // surfaced in the theme editor. The consuming app publishes them from its own
        // palette in its theme tail; see the note in Apply.
        // Geometry: top-only rounding (Chrome / VSCode) by default; a Safari-card
        // theme sets TabCornerRadius="5,5,5,5".
        [TabCornerRadiusKey]          = TokenKind.CornerRadius,
        [TabPaddingKey]               = TokenKind.Thickness,
        [TabSpacingKey]               = TokenKind.Thickness,
        [StripPaddingKey]             = TokenKind.Thickness,
        [StripBorderThicknessKey]     = TokenKind.Thickness,
        [TabMinHeightKey]             = TokenKind.Size,
        [ActiveIndicatorThicknessKey] = TokenKind.Size,
    });

    /// <summary>Theme-tail phase: mirror active-tab brush defaults, ensure the
    /// geometry tokens resolve to something, and synthesize the per-shape indicator
    /// resources + resolved indicator string. Runs after the cascade + overrides.</summary>
    public static void Apply(ThemeApplyContext ctx)
    {
        // This used to mirror fourteen neutral-chrome brushes and fonts (strip background,
        // hover, the three foregrounds, success/danger, font family + size, the active-tab
        // pair, the strip border) out of a third-party theme engine's resources. Those keys
        // stopped existing when the shell dropped that engine, and the mirroring went with them.
        //
        // NOTHING HERE REPLACES IT — publishing those keys is now the consuming app's job,
        // from whatever palette it actually has, in its own theme tail AFTER this runs.
        // A consuming app does it in its token bridge from its own Surface.*/Text.* tokens. An app
        // that hosts a TabStrip and publishes none of them gets an unstyled strip, which
        // is the failure mode to look for if tabs come up bare.
        //
        // What remains below is the part that is genuinely palette-independent: geometry
        // defaults and the active-indicator synthesis.

        // Geometry defaults: without these the DynamicResource lookups collapse to
        // a CLR zero and the strip loses its padding/spacing/rounding.
        ctx.EnsureDefault(StripPaddingKey,    new Thickness(6, 4, 6, 0));
        // Hairline under the strip, separating it from the hosted content. A
        // theme drops it with "0" rather than by clearing the brush.
        ctx.EnsureDefault(StripBorderThicknessKey, new Thickness(0, 0, 0, 1));
        ctx.EnsureDefault(TabPaddingKey,      new Thickness(12, 4, 8, 4));
        ctx.EnsureDefault(TabSpacingKey,      new Thickness(0, 3, 2, 0));
        ctx.EnsureDefault(TabCornerRadiusKey, new CornerRadius(5, 5, 0, 0));
        ctx.EnsureDefault(TabMinHeightKey,    32.0);   // was hard-coded in the styles pre-tokenization

        var res = ctx.Resources;
        var thickness =
            res.TryGetValue(ActiveIndicatorThicknessKey, out var tv) && tv is double td && td >= 0
                ? td
                : DefaultActiveIndicatorThickness;
        res[ActiveUnderlineThicknessKey] = new Thickness(0, 0, 0, thickness);
        // Overline carries no bottom reservation — the styles hand this same
        // thickness to inactive tabs (transparent), so nothing shifts on activate.
        res[ActiveOverlineThicknessKey]  = new Thickness(0, thickness, 0, 0);
        res[ActiveBorderThicknessKey]    = new Thickness(thickness);
        res[ActiveDotSizeKey]            = thickness * 2;

        var indicator = ctx.EffectiveDirective(ActiveIndicatorDirective);
        res[ActiveIndicatorKey] = Normalize(indicator) ?? DefaultActiveIndicator;
    }

    private static string? Normalize(string? raw) =>
        raw?.ToLowerInvariant() switch
        {
            "underline" => "Underline",
            "overline"  => "Overline",
            "border"    => "Border",
            "dot"       => "Dot",
            "none"      => "None",
            _           => null,
        };
}
