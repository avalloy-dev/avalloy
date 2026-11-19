using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Avalloy.Icons.Baking;

/// <summary>Which duotone layer to keep when flattening a two-tone active SVG.
/// The convention: the back (secondary) layer carries an <c>opacity</c> attribute;
/// the front (primary) layer does not.</summary>
public enum DuoLayer
{
    /// <summary>All drawable shapes (single-layer glyphs).</summary>
    All,
    /// <summary>Only shapes WITHOUT an <c>opacity</c> attr — the full-opacity front.</summary>
    Primary,
    /// <summary>Only shapes WITH an <c>opacity</c> attr — the faded back layer.</summary>
    Secondary,
}

/// <summary>SVG metadata read from a glyph's root: its viewBox edge, whether it
/// fills, and its effective stroke weight.</summary>
public readonly record struct SvgMeta(double Box, bool Filled, double StrokeWidth);

/// <summary>
/// Pure SVG → Avalonia path-data flattening. No file or console I/O — every method
/// takes SVG text and returns strings/values, so the whole thing is trivially
/// unit-testable and safe to run at build time (the tool) or at load time (Desktop's
/// user-pack baker). Handles the subset of SVG the jelly/Lucide libraries use:
/// <c>path</c>/<c>circle</c>/<c>rect</c>, a baked <c>translate+scale</c> transform,
/// fill-rule detection, and the opacity-split duotone convention.
/// </summary>
public static class SvgFlattener
{
    private static readonly CultureInfo Ci = CultureInfo.InvariantCulture;

    private static string G(double d) => d.ToString("0.######", Ci);

    // ── SVG shape → path-data helpers ───────────────────────────────────────────
    internal static string CircleToPath(double cx, double cy, double r) =>
        $"M{G(cx - r)} {G(cy)}a{G(r)} {G(r)} 0 1 0 {G(2 * r)} 0a{G(r)} {G(r)} 0 1 0 {G(-2 * r)} 0Z";

    internal static string RectToPath(double x, double y, double w, double h, double rx, double ry)
    {
        if (rx <= 0 && ry <= 0) return $"M{G(x)} {G(y)}h{G(w)}v{G(h)}h{G(-w)}Z";
        if (rx == 0) rx = ry;
        if (ry == 0) ry = rx;
        return $"M{G(x + rx)} {G(y)}h{G(w - 2 * rx)}a{G(rx)} {G(ry)} 0 0 1 {G(rx)} {G(ry)}v{G(h - 2 * ry)}" +
               $"a{G(rx)} {G(ry)} 0 0 1 {G(-rx)} {G(ry)}h{G(-(w - 2 * rx))}a{G(rx)} {G(ry)} 0 0 1 {G(-rx)} {G(-ry)}" +
               $"v{G(-(h - 2 * ry))}a{G(rx)} {G(ry)} 0 0 1 {G(rx)} {G(-ry)}Z";
    }

    // A <polyline>/<polygon> "points" list → a path: moveto the first point,
    // lineto the rest, optionally closed. Points may be space- or comma-separated.
    private static string PolyToPath(string el, bool close)
    {
        var m = Regex.Match(el, @"\bpoints=""([^""]*)""");
        if (!m.Success) return "";
        var n = Regex.Matches(m.Groups[1].Value, @"-?(?:\d+\.?\d*|\.\d+)(?:[eE][+-]?\d+)?")
                     .Select(x => double.Parse(x.Value, NumberStyles.Any, Ci)).ToList();
        if (n.Count < 4) return "";
        var sb = new StringBuilder();
        sb.Append('M').Append(G(n[0])).Append(' ').Append(G(n[1]));
        for (int i = 2; i + 1 < n.Count; i += 2)
            sb.Append('L').Append(G(n[i])).Append(' ').Append(G(n[i + 1]));
        if (close) sb.Append('Z');
        return sb.ToString();
    }

    private static double Num(string el, string attr, double def = 0)
    {
        var m = Regex.Match(el, $@"\b{attr}=""([^""]*)""");
        return m.Success && double.TryParse(m.Groups[1].Value, NumberStyles.Any, Ci, out var v) ? v : def;
    }

    /// <summary>Bake a <c>translate(tx ty) scale(s)</c> transform (uniform scale) into
    /// SVG path data — Avalonia's path mini-language has no transform, so a
    /// <c>&lt;path transform="…"&gt;</c> (e.g. a brand logo shrunk inside its frame) must be
    /// flattened into coordinates. Absolute commands get the full affine; relative
    /// ones scale only.</summary>
    internal static string TransformPath(string d, double tx, double ty, double s)
    {
        var tk = Regex.Matches(d, @"[A-Za-z]|-?(?:\d+\.?\d*|\.\d+)(?:[eE][+-]?\d+)?").Select(m => m.Value).ToList();
        var sb2 = new StringBuilder();
        int i = 0;
        double Nx() => double.Parse(tk[i++], NumberStyles.Any, Ci);
        void Em(double v) { sb2.Append(G(v)); sb2.Append(' '); }
        char cmd = ' ';
        bool first = true;
        while (i < tk.Count)
        {
            if (tk[i].Length == 1 && char.IsLetter(tk[i][0]))
            {
                cmd = tk[i][0]; i++;
                sb2.Append(cmd); sb2.Append(' ');
                if (char.ToUpperInvariant(cmd) == 'Z') { first = false; continue; }
            }
            bool abs = char.IsUpper(cmd);
            switch (char.ToUpperInvariant(cmd))
            {
                case 'M': case 'L': case 'T':
                {
                    double x = Nx(), y = Nx();
                    if (abs || (first && char.ToUpperInvariant(cmd) == 'M')) { Em(tx + s * x); Em(ty + s * y); }
                    else { Em(s * x); Em(s * y); }
                    if (char.ToUpperInvariant(cmd) == 'M') cmd = abs ? 'L' : 'l'; // trailing pairs = implicit lineto
                    break;
                }
                case 'H': { double x = Nx(); Em(abs ? tx + s * x : s * x); break; }
                case 'V': { double y = Nx(); Em(abs ? ty + s * y : s * y); break; }
                case 'C': for (var k = 0; k < 3; k++) { double x = Nx(), y = Nx(); if (abs) { Em(tx + s * x); Em(ty + s * y); } else { Em(s * x); Em(s * y); } } break;
                case 'S': case 'Q': for (var k = 0; k < 2; k++) { double x = Nx(), y = Nx(); if (abs) { Em(tx + s * x); Em(ty + s * y); } else { Em(s * x); Em(s * y); } } break;
                case 'A':
                {
                    double rx = Nx(), ry = Nx(), rot = Nx(), laf = Nx(), sf = Nx(), x = Nx(), y = Nx();
                    Em(s * rx); Em(s * ry); Em(rot); Em(laf); Em(sf);
                    if (abs) { Em(tx + s * x); Em(ty + s * y); } else { Em(s * x); Em(s * y); }
                    break;
                }
                default: i++; break;
            }
            first = false;
        }
        return sb2.ToString().Trim();
    }

    /// <summary>Avalonia's path mini-language defaults to the even-odd fill rule and
    /// has no per-element rule — the whole figure shares a leading token. SVG defaults
    /// to non-zero, so a mark that relies on winding needs an explicit <c>F1</c>. Only
    /// emit a token when the source declares fill-rule / clip-rule, so a library that
    /// declares none (riding Avalonia's even-odd default) is untouched.</summary>
    public static string FillRulePrefix(string svg)
    {
        var m = Regex.Match(svg, @"(?:fill|clip)-rule=""(evenodd|nonzero)""");
        return !m.Success ? "" : m.Groups[1].Value == "evenodd" ? "F0 " : "F1 ";
    }

    /// <summary>Concatenate every drawable element (<c>path</c>/<c>circle</c>/<c>rect</c>)
    /// into one Avalonia path-data string. <paramref name="layer"/> selects the duotone
    /// layer by the opacity-attribute convention. <paramref name="onWarning"/>, if
    /// supplied, receives a message for any path transform that isn't a bakeable
    /// translate+scale (that path is emitted un-transformed).</summary>
    public static string ShapesToData(string svg, DuoLayer layer = DuoLayer.All, Action<string>? onWarning = null)
    {
        var outp = new List<string>();
        foreach (Match m in Regex.Matches(svg, @"<(?:path|circle|rect|line|polyline|polygon)\b[^>]*>"))
        {
            var el = m.Value;
            var hasOp = Regex.IsMatch(el, @"\bopacity=""[^""]*""");
            if (layer == DuoLayer.Secondary && !hasOp) continue;
            if (layer == DuoLayer.Primary && hasOp) continue;
            if (el.StartsWith("<path"))
            {
                var d = Regex.Match(el, @"\bd=""([^""]*)""");
                if (d.Success)
                {
                    var data = d.Groups[1].Value.Trim();
                    var tf = Regex.Match(el, @"transform=""translate\(\s*(-?[\d.]+)[ ,]+(-?[\d.]+)\s*\)\s*scale\(\s*(-?[\d.]+)\s*\)""");
                    if (tf.Success)
                        data = TransformPath(data, double.Parse(tf.Groups[1].Value, NumberStyles.Any, Ci),
                                                   double.Parse(tf.Groups[2].Value, NumberStyles.Any, Ci),
                                                   double.Parse(tf.Groups[3].Value, NumberStyles.Any, Ci));
                    else if (el.Contains("transform="))
                        onWarning?.Invoke("unhandled path transform (not translate+scale): " +
                                          Regex.Match(el, @"transform=""[^""]*""").Value);
                    outp.Add(AbsolutizeLeadingMoveto(data));
                }
            }
            else if (el.StartsWith("<circle"))
                outp.Add(CircleToPath(Num(el, "cx"), Num(el, "cy"), Num(el, "r")));
            else if (el.StartsWith("<rect"))
                outp.Add(RectToPath(Num(el, "x"), Num(el, "y"), Num(el, "width"), Num(el, "height"),
                                    Num(el, "rx"), Num(el, "ry")));
            else if (el.StartsWith("<polyline"))
                outp.Add(PolyToPath(el, close: false));
            else if (el.StartsWith("<polygon"))
                outp.Add(PolyToPath(el, close: true));
            else if (el.StartsWith("<line"))
                outp.Add($"M{G(Num(el, "x1"))} {G(Num(el, "y1"))}L{G(Num(el, "x2"))} {G(Num(el, "y2"))}");
        }
        return string.Join(" ", outp);
    }

    /// <summary>Normalize a single path's <em>leading</em> moveto to absolute form so the
    /// path is safe to concatenate with others. SVG treats the first moveto of a path as
    /// absolute even when written lowercase (<c>m</c>), but once several <c>&lt;path&gt;</c>
    /// data strings are joined into one geometry (see <see cref="ShapesToData"/>), that
    /// leading <c>m</c> is re-read as relative to the previous subpath's end — which flings
    /// the shape off-canvas (this is what broke the two-stroke <c>x</c> glyph). We rewrite
    /// <c>m x y …</c> to <c>M x y l …</c>: the moveto point becomes absolute while the
    /// implicit trailing pairs stay relative linetos, so standalone rendering is unchanged
    /// but concatenation is now correct. Paths already starting with <c>M</c> (and shapes
    /// like circle/rect that emit <c>M</c>) are returned untouched.</summary>
    internal static string AbsolutizeLeadingMoveto(string d)
    {
        d = d.Trim();
        if (d.Length == 0 || d[0] != 'm') return d;
        var tk = Regex.Matches(d, @"[A-Za-z]|-?(?:\d+\.?\d*|\.\d+)(?:[eE][+-]?\d+)?")
                      .Select(x => x.Value).ToList();
        // tk[0] == "m"; tk[1], tk[2] are the (absolute) moveto coordinates.
        if (tk.Count < 3) return d; // malformed — leave as-is
        var sb = new StringBuilder();
        sb.Append('M').Append(tk[1]).Append(' ').Append(tk[2]);
        int i = 3;
        // A bare number right after the moveto pair is an implicit lineto, and after a
        // lowercase moveto those are RELATIVE — reintroduce them under an explicit 'l'.
        if (i < tk.Count && !(tk[i].Length == 1 && char.IsLetter(tk[i][0])))
            sb.Append('l');
        for (; i < tk.Count; i++)
        {
            var t = tk[i];
            var isCmd = t.Length == 1 && char.IsLetter(t[0]);
            if (!isCmd) sb.Append(' ');
            sb.Append(t);
        }
        return sb.ToString();
    }

    /// <summary>Read a glyph's <c>viewBox</c> (→ <see cref="SvgMeta.Box"/>), whether it
    /// fills (absence of <c>fill="none"</c>), and its effective stroke weight (drawn for
    /// <c>fill:none</c> glyphs, or a heavy stroke &gt; 6 on a filled one).</summary>
    public static SvgMeta ParseMeta(string svg)
    {
        double box = 24;
        var vb = Regex.Match(svg, @"viewBox=""([^""]+)""");
        if (vb.Success)
        {
            var p = vb.Groups[1].Value.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                      .Select(s => double.Parse(s, NumberStyles.Any, Ci)).ToArray();
            if (p.Length == 4) box = Math.Max(p[2], p[3]);
        }
        var filled = !svg.Contains("fill=\"none\"");
        double strokeW = 0;
        var sw = Regex.Match(svg, @"stroke-width=""([^""]*)""");
        if (sw.Success && double.TryParse(sw.Groups[1].Value, NumberStyles.Any, Ci, out var v) && (!filled || v > 6))
            strokeW = v;
        return new SvgMeta(box, filled, strokeW);
    }
}
