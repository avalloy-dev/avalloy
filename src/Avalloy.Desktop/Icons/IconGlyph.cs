using Avalonia.Media;

namespace Avalloy.Icons;

/// <summary>A single icon's geometry + render metadata, as generated into a pack's
/// <c>&lt;id&gt;.icons.axaml</c> by <c>Avalloy.IconTool</c> (or materialized at
/// runtime from a user pack via <see cref="Baking.IconBaker"/>). A plain resource
/// object (not a control) so one <see cref="GlyphIcon"/> can look it up by key and
/// render it tinted by the ambient text colour. The <see cref="Geometry"/> string
/// attributes parse via Avalonia's built-in geometry converter.</summary>
public sealed class IconGlyph
{
    /// <summary>The glyph outline (all shapes flattened to one geometry).</summary>
    public Geometry? Geometry { get; set; }

    /// <summary>Jelly duotone hover layers (front / back). Null for non-duotone families.</summary>
    public Geometry? DuoPrimary { get; set; }
    public Geometry? DuoSecondary { get; set; }

    /// <summary>Fill the geometry (jelly / bespoke marks). False for stroke-only Lucide glyphs.</summary>
    public bool Filled { get; set; } = true;

    /// <summary>Stroke weight in source units; 0 = no stroke. (Non-zero + <see cref="Filled"/> both draw.)</summary>
    public double StrokeWidth { get; set; }

    /// <summary>The source viewBox edge the geometry was authored in (24 / 560 / 640), for uniform scaling.</summary>
    public double Box { get; set; } = 24;

    /// <summary>Optional small optical-size variant — drawn in place of <see cref="Geometry"/> when the requested
    /// Size is ≤ <see cref="SmallMaxSize"/>. Authored as a sibling <c>&lt;name&gt;.sm&lt;N&gt;.svg</c>, so a
    /// simplified low-size glyph can differ entirely from the detailed large one. Carries its own box / fill /
    /// stroke; no duotone (the swap is suppressed at small sizes). Null when the icon ships only one SVG.</summary>
    public Geometry? SmallGeometry { get; set; }

    /// <summary>The small variant's own viewBox edge (may differ from <see cref="Box"/>).</summary>
    public double SmallBox { get; set; } = 24;

    /// <summary>Fill the small geometry. Independent of <see cref="Filled"/>.</summary>
    public bool SmallFilled { get; set; } = true;

    /// <summary>The small variant's stroke weight in source units; 0 = no stroke.</summary>
    public double SmallStrokeWidth { get; set; }

    /// <summary>The small variant's own two-tone hover / active layers (front / back). Null when the small
    /// art ships no <c>.fill</c> — in which case it stays flat when lit.</summary>
    public Geometry? SmallDuoPrimary { get; set; }
    public Geometry? SmallDuoSecondary { get; set; }

    /// <summary>Inclusive max Size at which <see cref="SmallGeometry"/> is used; 0 = no small variant.</summary>
    public double SmallMaxSize { get; set; }
}
