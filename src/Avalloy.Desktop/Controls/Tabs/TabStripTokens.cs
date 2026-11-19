namespace Avalloy.Controls.Tabs;

/// <summary>
/// The runtime theming contract <see cref="TabStrip"/> reads, independent of any
/// theme engine. The shell tail (<c>TabStripTheming</c>) and the native theme
/// engine both publish <see cref="ActiveIndicatorKey"/> into the app resources;
/// the control reads it back here. Keeping these constants in Desktop lets the strip
/// stay engine-agnostic while the per-engine <em>population</em> of the token
/// lives in the engine layer.
/// </summary>
public static class TabStripTokens
{
    /// <summary>Resource key holding the resolved indicator shape string
    /// ("Underline" / "Overline" / "Border" / "Dot" / "None") the strip stamps as
    /// an <c>indicator-*</c> class.</summary>
    public const string ActiveIndicatorKey = "AlloyPaneTabActiveIndicator";

    /// <summary>Default indicator shape when no theme sets the directive.</summary>
    public const string DefaultActiveIndicator = "Underline";
}
