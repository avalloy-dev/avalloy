using Avalonia.Media;
using Avalonia.Media.TextFormatting;

namespace Avalloy.Typography;

/// <summary>
/// Optically centres a CAPITAL inside a box — a monogram tile, an avatar, a badge.
///
/// <para>Centring a TextBlock centres its LINE BOX, which spans ascent+descent. A capital's ink spans only
/// baseline→cap-height, and every face puts those in a different place, so a vertically-centred capital sits off
/// centre by <c>(asc+desc)/2 − asc + cap/2</c>. The error is a property of the FACE, not of the layout, which is
/// why a constant tuned to one font silently goes wrong the moment the face changes — measured across two display
/// faces it ranged from 0.10 em to exactly 0, i.e. the whole correction.</para>
///
/// <para>Cap height is read from the ink box of a capital rather than a metrics field: Avalonia's
/// <see cref="FontMetrics"/> exposes ascent, descent and line gap but no cap height, and the OS/2 table's own
/// value is optional and not always honest. 'H' is used because it is flat-topped — a round capital like 'O'
/// overshoots the cap line by design, which would bias the result.</para>
/// </summary>
public static class CapCentering
{
    /// <summary>The downward offset, in EM, that puts a capital's ink on the box's true centre. Positive = move
    /// the text down. Returns 0 when the face can't be measured, which leaves the naive centring in place rather
    /// than introducing a wrong correction.</summary>
    public static double OffsetEm(Typeface typeface)
    {
        if (!FontManager.Current.TryGetGlyphTypeface(typeface, out var face)) return 0;

        var em = face.Metrics.DesignEmHeight;
        if (em <= 0) return 0;

        // Ascent is negative (above the baseline) in Avalonia's metrics; descent positive.
        double ascent = -face.Metrics.Ascent;
        double descent = face.Metrics.Descent;

        // Measured at a large reference size and normalised, so rounding in the layout is a rounding error in
        // the fourth decimal of an em rather than a visible fraction of a pixel at UI sizes.
        const double Reference = 512;
        var layout = new TextLayout("H", typeface, Reference, Brushes.Black);
        if (layout.TextLines.Count == 0) return 0;
        var cap = layout.TextLines[0].Extent / Reference;   // in em: H spans baseline -> cap exactly
        if (cap <= 0) return 0;

        // Ascent/descent are design units; normalise them the same way before combining.
        return ((ascent + descent) / 2 - ascent) / em + cap / 2;
    }

    /// <summary>The same offset in DIP at a given font size.</summary>
    public static double Offset(Typeface typeface, double fontSize) => OffsetEm(typeface) * fontSize;
}
