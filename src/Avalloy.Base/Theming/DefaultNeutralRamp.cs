using Avalonia.Media;

namespace Avalloy.Theming;

/// <summary>
/// The default neutral ramp — Part A of the palette work. Builds eleven anchor shades from a
/// midtone colour and interpolates between them, so <see cref="NeutralPalette"/> can address any
/// shade number.
/// <para>
/// Anchors are built by mixing the midtone toward white (tints, shades 100-400) and toward a
/// near-black target (shades 600-900), with the midtone itself at 500. Tints mix in sRGB and
/// shades in linear RGB — the mixing space genuinely matters here: linear RGB desaturates
/// noticeably when approaching white, and sRGB transitions inconsistently between colour families
/// when approaching black.
/// </para>
/// <para>
/// Calibrated against the palette generator existing apps were themed with, so their chrome
/// did not shift when this replaced it; the constants are the ones that reproduce it.
/// </para>
/// </summary>
public sealed class DefaultNeutralRamp : INeutralRamp
{
    /// <summary>The default neutral midtone — a slightly blue-cast grey, which is what
    /// keeps generated chrome from looking flatly neutral.</summary>
    public static readonly Color DefaultMidtone = Color.FromRgb(0x6C, 0x72, 0x81);

    /// <summary>The darkest a neutral target goes. Not pure black: a near-black keeps the
    /// darkest chrome from crushing against true black.</summary>
    private const byte MaxDarkComponentLevel = 35;

    // Mix ratios toward the light target (tints) and dark target (shades). The dark-side ratios
    // are the low end of their range, which is what a fully-dark neutral target selects.
    private static readonly double[] TintRatios = [0.95, 0.82, 0.65, 0.35];
    private static readonly double[] ShadeRatios = [0.35, 0.65, 0.82, 0.95];

    private readonly (int Number, Color Color)[] _anchors;
    private readonly double _tintR, _tintG, _tintB;

    /// <summary>Builds a ramp around <paramref name="midtone"/>.</summary>
    /// <param name="midtone">The shade-500 colour. Defaults to <see cref="DefaultMidtone"/>.</param>
    /// <param name="darkness">0..1, how far the dark end travels toward black. 0 keeps the
    /// near-black target; 1 drives it to true black.</param>
    /// <param name="tint">Optional hue to cast every shade toward. This is the single knob a warm
    /// or cool theme turns — it recolours all chrome at once (backgrounds, hairlines, secondary
    /// text) because every one of them derives from this ramp.</param>
    /// <param name="tintAmount">Cast strength 0..1. Combined with the tint's own saturation, so a
    /// muted tint reads as a subtle wash.</param>
    public DefaultNeutralRamp(Color? midtone = null, double darkness = 0.0,
        Color? tint = null, double tintAmount = 0.6)
    {
        if (tint is { } t)
        {
            // Cast by the tint's CHROMA — its channels minus its own luma — so luminance is left
            // alone and only hue moves. That is what keeps the contrast ladder intact: every
            // surface shifts colour without any text becoming less legible against it.
            var amount = Math.Clamp(tintAmount, 0, 1);
            var luma = 0.299 * t.R + 0.587 * t.G + 0.114 * t.B;
            _tintR = (t.R - luma) * amount;
            _tintG = (t.G - luma) * amount;
            _tintB = (t.B - luma) * amount;
        }

        var mid = midtone ?? DefaultMidtone;
        var light = Colors.White;

        var darkLevel = (byte)(MaxDarkComponentLevel - Math.Clamp(darkness, 0, 1) * MaxDarkComponentLevel);
        var dark = Color.FromRgb(darkLevel, darkLevel, darkLevel);

        _anchors =
        [
            (50, light),
            (100, MixSrgb(mid, light, TintRatios[0])),
            (200, MixSrgb(mid, light, TintRatios[1])),
            (300, MixSrgb(mid, light, TintRatios[2])),
            (400, MixSrgb(mid, light, TintRatios[3])),
            (500, mid),
            (600, Shade(mid, dark, ShadeRatios[0])),
            (700, Shade(mid, dark, ShadeRatios[1])),
            (800, Shade(mid, dark, ShadeRatios[2])),
            (900, Shade(mid, dark, ShadeRatios[3])),
            // The darkest shade carries a trace of the midtone's hue so dark themes don't bottom
            // out on a dead neutral: tint the dark target toward the midtone, then pull most of
            // the way back. The result sits a hair off pure grey rather than lighter than the
            // target — pulling toward the midtone directly overshoots badly.
            (950, MixSrgb(TintTowardEdge(dark, mid), dark, 0.7)),
        ];
    }

    /// <inheritdoc/>
    public Color Resolve(int shadeNumber) => Cast(ResolveUntinted(shadeNumber));

    private Color ResolveUntinted(int shadeNumber)
    {
        var n = Math.Clamp(shadeNumber, 0, NeutralPalette.MaxShadeNumber);

        // The ends are implicit: pure white below the first anchor, pure black above the last.
        if (n == 0) return Colors.White;
        if (n == NeutralPalette.MaxShadeNumber) return Colors.Black;

        for (var i = 0; i < _anchors.Length; i++)
        {
            if (_anchors[i].Number == n)
                return _anchors[i].Color;

            if (_anchors[i].Number > n)
            {
                var (prevNumber, prevColor) = i > 0 ? _anchors[i - 1] : (0, Colors.White);
                var (nextNumber, nextColor) = _anchors[i];
                var ratio = (double)(n - prevNumber) / (nextNumber - prevNumber);
                return MixSrgb(prevColor, nextColor, ratio);
            }
        }

        // Past the last anchor: interpolate on toward black.
        var (lastNumber, lastColor) = _anchors[^1];
        var tail = (double)(n - lastNumber) / (NeutralPalette.MaxShadeNumber - lastNumber);
        return MixSrgb(lastColor, Colors.Black, tail);
    }

    /// <summary>Apply the tint's chroma offset. Applied to every resolved shade rather than only
    /// to the anchors, so interpolated shades carry the cast too.</summary>
    private Color Cast(Color c)
    {
        if (_tintR == 0 && _tintG == 0 && _tintB == 0) return c;

        static byte Add(byte channel, double delta) => (byte)Math.Clamp(channel + delta, 0, 255);
        return Color.FromArgb(c.A, Add(c.R, _tintR), Add(c.G, _tintG), Add(c.B, _tintB));
    }

    /// <summary>Build a dark-side shade: mix toward the dark target in linear RGB, then boost
    /// saturation proportionally to how far the mix travelled.
    /// <para>
    /// The saturation boost is the part that is easy to miss. Mixing a desaturated grey toward
    /// near-black bleeds out what little chroma it had, so the deep end of the ramp would drift
    /// to a dead neutral; scaling saturation by (1 + ratio) holds the midtone's cast all the way
    /// down. Without it the darkest surfaces come out visibly flatter.
    /// </para></summary>
    private static Color Shade(Color midtone, Color darkTarget, double ratio)
    {
        var mixed = MixLinear(midtone, darkTarget, ratio);

        var (h, s, v) = ToHsv(mixed);
        return FromHsv(h, Math.Clamp(s + ratio * s, 0, 1), v);
    }

    /// <summary>Tint <paramref name="color"/> using <paramref name="tint"/>'s hue, pushed toward
    /// whichever end of the spectrum <paramref name="color"/> already sits nearer. Dark colours
    /// therefore pick up a DARKENED version of the tint rather than being lightened by it.</summary>
    private static Color TintTowardEdge(Color color, Color tint)
    {
        // Rec.709 luminance on raw channel values (0..255), not gamma-expanded — this mirrors the
        // spectrum-edge decision the reference implementation makes.
        var luminance = 0.2126 * color.R + 0.7152 * color.G + 0.0722 * color.B;
        var edge = luminance < 128.0 ? 0 : 255;
        var amount = Math.Abs((luminance - 128.0) / 128.0);

        // Truncating (not rounding) matches the reference and is worth a byte or two here.
        static byte Move(byte from, int edge, double amount) => (byte)(from + (int)((edge - from) * amount));

        return Color.FromRgb(Move(tint.R, edge, amount), Move(tint.G, edge, amount), Move(tint.B, edge, amount));
    }

    private static (double H, double S, double V) ToHsv(Color c)
    {
        double r = c.R / 255.0, g = c.G / 255.0, b = c.B / 255.0;
        var max = Math.Max(r, Math.Max(g, b));
        var min = Math.Min(r, Math.Min(g, b));
        var delta = max - min;

        double h = 0;
        if (delta > 0)
        {
            if (max == r) h = 60 * (((g - b) / delta) % 6);
            else if (max == g) h = 60 * (((b - r) / delta) + 2);
            else h = 60 * (((r - g) / delta) + 4);
        }
        if (h < 0) h += 360;

        return (h, max == 0 ? 0 : delta / max, max);
    }

    private static Color FromHsv(double h, double s, double v)
    {
        var c = v * s;
        var x = c * (1 - Math.Abs(h / 60.0 % 2 - 1));
        var m = v - c;

        var (r, g, b) = h switch
        {
            < 60 => (c, x, 0.0),
            < 120 => (x, c, 0.0),
            < 180 => (0.0, c, x),
            < 240 => (0.0, x, c),
            < 300 => (x, 0.0, c),
            _ => (c, 0.0, x),
        };

        return Color.FromRgb(
            (byte)Math.Round((r + m) * 255.0),
            (byte)Math.Round((g + m) * 255.0),
            (byte)Math.Round((b + m) * 255.0));
    }

    /// <summary>Mix in sRGB space — ratio 0 returns <paramref name="from"/>, 1 returns
    /// <paramref name="to"/>.</summary>
    private static Color MixSrgb(Color from, Color to, double ratio)
    {
        var r = Math.Clamp(ratio, 0, 1);
        return Color.FromRgb(
            (byte)Math.Round(from.R + (to.R - from.R) * r),
            (byte)Math.Round(from.G + (to.G - from.G) * r),
            (byte)Math.Round(from.B + (to.B - from.B) * r));
    }

    /// <summary>Mix in linear RGB — gamma-correct, so mid-mixes toward black stay consistent
    /// across colour families instead of drifting.</summary>
    private static Color MixLinear(Color from, Color to, double ratio)
    {
        var r = Math.Clamp(ratio, 0, 1);

        static double ToLinear(byte c)
        {
            var v = c / 255.0;
            return v <= 0.04045 ? v / 12.92 : Math.Pow((v + 0.055) / 1.055, 2.4);
        }

        static byte ToSrgb(double v)
        {
            var s = v <= 0.0031308 ? v * 12.92 : 1.055 * Math.Pow(v, 1.0 / 2.4) - 0.055;
            return (byte)Math.Round(Math.Clamp(s, 0, 1) * 255.0);
        }

        return Color.FromRgb(
            ToSrgb(ToLinear(from.R) + (ToLinear(to.R) - ToLinear(from.R)) * r),
            ToSrgb(ToLinear(from.G) + (ToLinear(to.G) - ToLinear(from.G)) * r),
            ToSrgb(ToLinear(from.B) + (ToLinear(to.B) - ToLinear(from.B)) * r));
    }
}
