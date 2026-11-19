using Avalonia.Media;
using Avalloy.Icons.Baking;

namespace Avalloy.Icons;

/// <summary>Turns a pure <see cref="IconGlyphSpec"/> (path-data strings, from
/// <see cref="IconBaker"/>) into a runtime <see cref="IconGlyph"/> with parsed
/// Avalonia geometries. Bundled packs skip this — their geometry is parsed by the
/// XAML loader from the baked <c>.icons.axaml</c>; this path is for user-installed
/// packs baked from raw SVGs at load time.</summary>
public static class IconGlyphMaterializer
{
    public static IconGlyph ToIconGlyph(IconGlyphSpec s) => new()
    {
        Geometry = Parse(s.Geometry),
        Box = s.Box,
        Filled = s.Filled,
        StrokeWidth = s.StrokeWidth,
        DuoPrimary = Parse(s.DuoPrimary),
        DuoSecondary = Parse(s.DuoSecondary),
        SmallMaxSize = s.SmallMaxSize,
        SmallGeometry = Parse(s.Small?.Geometry),
        SmallBox = s.Small?.Box ?? 24,
        SmallFilled = s.Small?.Filled ?? true,
        SmallStrokeWidth = s.Small?.StrokeWidth ?? 0,
        SmallDuoPrimary = Parse(s.Small?.DuoPrimary),
        SmallDuoSecondary = Parse(s.Small?.DuoSecondary),
    };

    // Avalonia's path mini-language parser accepts the leading F0/F1 fill-rule token
    // the baker emits, so specs round-trip through Geometry.Parse unchanged.
    private static Geometry? Parse(string? data) =>
        string.IsNullOrEmpty(data) ? null : Geometry.Parse(data);
}
