namespace Avalloy.Icons.Baking;

/// <summary>A small optical-size variant fed to <see cref="IconBaker.Bake"/>: the
/// simplified SVG drawn at sizes ≤ <see cref="MaxSize"/>, with an optional two-tone
/// active layer.</summary>
public readonly record struct SmallVariantInput(string Svg, double MaxSize, string? ActiveLayerSvg = null);

/// <summary>
/// Composes a full <see cref="IconGlyphSpec"/> from source SVG text: the base glyph,
/// an optional duotone/active-layer SVG, and an optional small optical-size variant.
/// Pure — no file or console I/O; callers supply the SVG strings (the tool reads them
/// from disk at build time, Avalloy.Desktop reads user-pack files at load time). Fill/stroke
/// metadata is <b>always</b> taken from the source, so stroke-only Lucide glyphs bake
/// correctly (see ADR 0001).
/// </summary>
public static class IconBaker
{
    /// <param name="svg">The base glyph SVG (the "rest" layer).</param>
    /// <param name="activeLayerSvg">Optional two-tone active layer (jelly <c>.duo</c> /
    /// bespoke <c>.fill</c>), split into primary/secondary by the opacity convention.</param>
    /// <param name="small">Optional small optical-size variant.</param>
    /// <param name="onWarning">Optional sink for non-fatal flatten warnings (e.g. an
    /// unhandled path transform). Null = silent.</param>
    public static IconGlyphSpec Bake(
        string svg,
        string? activeLayerSvg = null,
        SmallVariantInput? small = null,
        Action<string>? onWarning = null)
    {
        var spec = BakeLayer(svg, activeLayerSvg, onWarning);
        if (small is { } sm)
            spec = spec with
            {
                Small = BakeLayer(sm.Svg, sm.ActiveLayerSvg, onWarning),
                SmallMaxSize = sm.MaxSize,
            };
        return spec;
    }

    /// <summary>Bake one layer (base or small) to a spec — geometry + metadata + its
    /// own duotone. The fill-rule prefix is applied uniformly here (unlike the original
    /// generator, which only prefixed the base layer); it emits a token only when the
    /// source declares a fill/clip rule, so this is a no-op for the existing library.</summary>
    private static IconGlyphSpec BakeLayer(string svg, string? activeLayerSvg, Action<string>? onWarning)
    {
        var meta = SvgFlattener.ParseMeta(svg);
        var geom = SvgFlattener.FillRulePrefix(svg) + SvgFlattener.ShapesToData(svg, DuoLayer.All, onWarning);
        var (duoP, duoS) = activeLayerSvg is null ? (null, null) : DuoLayers(activeLayerSvg, onWarning);
        return new IconGlyphSpec
        {
            Geometry = geom,
            Box = meta.Box,
            Filled = meta.Filled,
            StrokeWidth = meta.StrokeWidth,
            DuoPrimary = NullIfEmpty(duoP),
            DuoSecondary = NullIfEmpty(duoS),
        };
    }

    /// <summary>The two-tone (primary / .4 secondary) layers from an active SVG — a
    /// jelly <c>.duo.svg</c> or a bespoke <c>.fill.svg</c>. Both share the opacity-split
    /// convention, so one reader serves both.</summary>
    public static (string? Primary, string? Secondary) DuoLayers(string activeLayerSvg, Action<string>? onWarning = null)
        => (SvgFlattener.ShapesToData(activeLayerSvg, DuoLayer.Primary, onWarning),
            SvgFlattener.ShapesToData(activeLayerSvg, DuoLayer.Secondary, onWarning));

    private static string? NullIfEmpty(string? s) => string.IsNullOrEmpty(s) ? null : s;
}
