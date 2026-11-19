namespace Avalloy.Icons.Baking;

/// <summary>
/// One icon baked to Avalonia path-data strings — the pure (Avalonia-free) output
/// of <see cref="IconBaker"/>. Mirrors the settable surface of the runtime
/// <c>IconGlyph</c> model, but carries geometry as strings (parsed to
/// <c>Avalonia.Media.Geometry</c> either by the XAML loader for bundled packs, or
/// by Desktop's materializer for user packs). All coordinates are in the source
/// <see cref="Box"/> viewBox; the renderer scales to the requested size.
/// </summary>
public sealed record IconGlyphSpec
{
    /// <summary>The glyph outline: every drawable shape flattened into one Avalonia
    /// path-data string (may carry a leading <c>F0</c>/<c>F1</c> fill-rule token).</summary>
    public required string Geometry { get; init; }

    /// <summary>The source viewBox edge (max of width/height), for uniform scaling.</summary>
    public double Box { get; init; } = 24;

    /// <summary>Fill the geometry (jelly / bespoke marks). False for stroke-only
    /// Lucide-style glyphs (<c>fill="none"</c>).</summary>
    public bool Filled { get; init; } = true;

    /// <summary>Stroke weight in source units; 0 = no stroke. Drawn for
    /// <see cref="Filled"/>==false glyphs or a heavy stroke on a filled one.</summary>
    public double StrokeWidth { get; init; }

    /// <summary>Two-tone hover/active layers (front / back). Null for single-layer glyphs.</summary>
    public string? DuoPrimary { get; init; }
    public string? DuoSecondary { get; init; }

    /// <summary>Optional small optical-size variant, drawn in place of this glyph at
    /// sizes ≤ <see cref="SmallMaxSize"/>. Carries its own box/fill/stroke/duotone;
    /// its own <see cref="SmallMaxSize"/>/<see cref="Small"/> are ignored.</summary>
    public IconGlyphSpec? Small { get; init; }

    /// <summary>Inclusive max size at which <see cref="Small"/> is used; 0 = none.</summary>
    public double SmallMaxSize { get; init; }
}
