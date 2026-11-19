using AwesomeAssertions;
using Avalloy.Icons.Baking;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Avalloy.Icons.Baking.Tests;

[TestClass]
public class IconBakerTests
{
    // A real Lucide "x" glyph: stroke-only (fill="none"), two paths, viewBox 24.
    private const string LucideX =
        """<svg xmlns="http://www.w3.org/2000/svg" width="24" height="24" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M18 6 6 18"/><path d="m6 6 12 12"/></svg>""";

    [TestMethod]
    public void Bake_strokeOnly_lucide_preserves_fill_and_stroke_metadata()
    {
        var spec = IconBaker.Bake(LucideX);

        // The ADR's load-bearing invariant: stroke-only glyphs must NOT bake as filled.
        spec.Filled.Should().BeFalse();
        spec.StrokeWidth.Should().Be(2);
        spec.Box.Should().Be(24);
        // The second stroke is a separate <path> that opens with a relative 'm' — it must
        // bake to an absolute 'M' so the joined geometry stays a real X (see
        // ShapesToData_absolutizes_a_second_paths_leading_relative_moveto). A bare 'm' here
        // is the concatenation bug that rendered the app-wide close icon as a chevron.
        spec.Geometry.Should().Be("M18 6 6 18 M6 6l 12 12");
        spec.DuoPrimary.Should().BeNull();
        spec.Small.Should().BeNull();
    }

    [TestMethod]
    public void ParseMeta_reads_viewbox_fill_and_stroke()
    {
        SvgFlattener.ParseMeta("""<svg viewBox="0 0 32 32" fill="none" stroke-width="3"/>""")
            .Should().Be(new SvgMeta(32, false, 3));

        // Filled glyph: default, and a light stroke (≤6) is ignored.
        SvgFlattener.ParseMeta("""<svg viewBox="0 0 24 24" stroke-width="2"/>""")
            .Should().Be(new SvgMeta(24, true, 0));

        // Heavy stroke on a filled glyph is kept.
        SvgFlattener.ParseMeta("""<svg viewBox="0 0 24 24" stroke-width="8"/>""")
            .Should().Be(new SvgMeta(24, true, 8));
    }

    [TestMethod]
    public void ShapesToData_converts_circle_and_rect_and_joins()
    {
        SvgFlattener.ShapesToData("""<svg><circle cx="12" cy="12" r="6"/></svg>""")
            .Should().Be("M6 12a6 6 0 1 0 12 0a6 6 0 1 0 -12 0Z");

        SvgFlattener.ShapesToData("""<svg><rect x="2" y="4" width="10" height="6"/></svg>""")
            .Should().Be("M2 4h10v6h-10Z");

        SvgFlattener.ShapesToData("""<svg><path d="M0 0"/><path d="M1 1"/></svg>""")
            .Should().Be("M0 0 M1 1");
    }

    [TestMethod]
    public void ShapesToData_absolutizes_a_second_paths_leading_relative_moveto()
    {
        // The lucide "x": two strokes, the second a SEPARATE <path> that opens with a
        // RELATIVE 'm'. SVG treats a path's first moveto as absolute, but naive
        // concatenation re-reads it as relative to the previous stroke's end — which
        // flung the second stroke off-canvas and left a chevron-looking single slash.
        // The leading 'm' must become an absolute 'M' (trailing implicit pairs stay
        // relative), so the joined geometry still draws a real X.
        SvgFlattener.ShapesToData("""<svg><path d="M18 6 6 18"/><path d="m6 6 12 12"/></svg>""")
            .Should().Be("M18 6 6 18 M6 6l 12 12");
    }

    [TestMethod]
    public void ShapesToData_converts_polyline_polygon_and_line()
    {
        // Lucide's "code" glyph is polylines — these must flatten, not vanish.
        SvgFlattener.ShapesToData("""<svg><polyline points="16 18 22 12 16 6"/></svg>""")
            .Should().Be("M16 18L22 12L16 6");

        SvgFlattener.ShapesToData("""<svg><polygon points="0 0 10 0 5 8"/></svg>""")
            .Should().Be("M0 0L10 0L5 8Z");

        SvgFlattener.ShapesToData("""<svg><line x1="5" y1="12" x2="19" y2="12"/></svg>""")
            .Should().Be("M5 12L19 12");
    }

    [TestMethod]
    public void FillRulePrefix_only_emitted_when_declared()
    {
        SvgFlattener.FillRulePrefix("""<path d="M0 0"/>""").Should().BeEmpty();
        SvgFlattener.FillRulePrefix("""<path fill-rule="evenodd" d="M0 0"/>""").Should().Be("F0 ");
        SvgFlattener.FillRulePrefix("""<path clip-rule="nonzero" d="M0 0"/>""").Should().Be("F1 ");
    }

    [TestMethod]
    public void TransformPath_bakes_translate_and_scale_into_coordinates()
    {
        // translate(4 4) scale(2) on "M1 1 L2 2": absolute cmds get the full affine.
        SvgFlattener.ShapesToData("""<svg><path transform="translate(4 4) scale(2)" d="M1 1L2 2"/></svg>""")
            .Should().Be("M 6 6 L 8 8");
    }

    [TestMethod]
    public void DuoLayers_split_by_opacity_attribute()
    {
        const string active = """<svg><path d="FRONT"/><path d="BACK" opacity="0.4"/></svg>""";
        var (primary, secondary) = IconBaker.DuoLayers(active);
        primary.Should().Be("FRONT");
        secondary.Should().Be("BACK");
    }

    [TestMethod]
    public void Bake_with_active_layer_populates_duotone()
    {
        const string baseSvg = """<svg viewBox="0 0 24 24"><path d="REST"/></svg>""";
        const string active = """<svg><path d="FRONT"/><path d="BACK" opacity="0.4"/></svg>""";

        var spec = IconBaker.Bake(baseSvg, activeLayerSvg: active);

        spec.Geometry.Should().Be("REST");
        spec.DuoPrimary.Should().Be("FRONT");
        spec.DuoSecondary.Should().Be("BACK");
    }

    [TestMethod]
    public void Bake_with_small_variant_carries_its_own_metadata()
    {
        const string baseSvg = """<svg viewBox="0 0 24 24"><path d="BIG"/></svg>""";
        const string smallSvg = """<svg viewBox="0 0 16 16" fill="none" stroke-width="1.5"><path d="SMALL"/></svg>""";

        var spec = IconBaker.Bake(baseSvg, small: new SmallVariantInput(smallSvg, MaxSize: 16));

        spec.SmallMaxSize.Should().Be(16);
        spec.Small.Should().NotBeNull();
        spec.Small!.Geometry.Should().Be("SMALL");
        spec.Small.Box.Should().Be(16);
        spec.Small.Filled.Should().BeFalse();
        spec.Small.StrokeWidth.Should().Be(1.5);
    }
}
