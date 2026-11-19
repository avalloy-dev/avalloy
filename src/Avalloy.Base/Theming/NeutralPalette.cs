using Avalonia.Media;

namespace Avalloy.Theming;

/// <summary>Where in the chrome a colour is being asked for. Maps to the container ladder:
/// each step sits one shade further from the page background.</summary>
public enum SurfaceContext
{
    /// <summary>The page/pane background — the bottom of the ladder.</summary>
    Container1,
    Container2,
    Container3,
    Container4,
    Container5,
    /// <summary>List rows. Shares Container3's base shade but takes state offsets.</summary>
    ListItem,
}

/// <summary>Which role within a <see cref="SurfaceContext"/> is wanted.</summary>
public enum ColorLocation
{
    Background,
    /// <summary>A hairline against the context's background — three shade steps away.</summary>
    Border,
    ForegroundNormal,
    ForegroundSecondary,
    ForegroundTertiary,
}

/// <summary>A neutral colour ramp addressed by shade number. Shade 0 is the lightest end and
/// <see cref="NeutralPalette.MaxShadeNumber"/> the darkest; implementations interpolate between
/// whatever anchors they hold.</summary>
public interface INeutralRamp
{
    /// <summary>The colour at <paramref name="shadeNumber"/>, clamped into range.</summary>
    Color Resolve(int shadeNumber);
}

/// <summary>
/// Derives the chrome palette — surfaces, hairlines and text tiers — from a neutral ramp.
/// <para>
/// This is the shade ARITHMETIC only; producing the ramp itself is a separate concern behind
/// <see cref="INeutralRamp"/>. The split matters because the arithmetic is small, exactly
/// specified and cheap to test against a synthetic ramp, whereas ramp generation is colour
/// science. Keeping them apart means a bug in one cannot be mistaken for a bug in the other.
/// </para>
/// <para>
/// Behaviour was pinned against the palette generator this was calibrated on, which fixes
/// the properties this must reproduce: the container ladder is monotonic
/// away from the page background, dark inverts the shade number, and the text tiers descend in
/// contrast. Constants below (base shades, the 3-step border offset, the contrast percentages,
/// the 0.7 lightness threshold) are the values that generator uses.
/// </para>
/// </summary>
public static class NeutralPalette
{
    /// <summary>The darkest addressable shade. Shade numbers run 0 (lightest) to here.</summary>
    public const int MaxShadeNumber = 1000;

    /// <summary>Shades are quantised to this, so one "step" moves by this much.</summary>
    public const int ShadeStepDelta = 50;

    /// <summary>How far a hairline sits from its container's background, in steps.</summary>
    private const int BorderShadeSteps = 3;

    // Contrast percentages, i.e. how far a text tier travels from its background toward maximum
    // contrast. Two sets because the eye needs different separation depending on whether dark
    // text sits on light chrome or the reverse.
    private const double NormalOnLight = 0.85, SecondaryOnLight = 0.63, TertiaryOnLight = 0.48;
    private const double NormalOnDark = 0.84, SecondaryOnDark = 0.70, TertiaryOnDark = 0.59;

    /// <summary>Perceived lightness at or above which a colour counts as light, and therefore
    /// wants dark text on it.</summary>
    private const double LightnessThreshold = 0.7;

    /// <summary>The ladder. Each container is one 50-shade step further from the background.</summary>
    private static int BaseShade(SurfaceContext context) => context switch
    {
        SurfaceContext.Container1 => 50,
        SurfaceContext.Container2 => 100,
        SurfaceContext.Container3 => 150,
        SurfaceContext.Container4 => 200,
        SurfaceContext.Container5 => 250,
        SurfaceContext.ListItem => 150,
        _ => 50,
    };

    /// <summary>The shade number a request resolves to, before the ramp is consulted.
    /// Exposed because the arithmetic is worth testing on its own, without a ramp.</summary>
    public static int ShadeNumberFor(SurfaceContext context, ColorLocation location, bool dark, INeutralRamp ramp)
    {
        var shade = BaseShade(context);
        if (location == ColorLocation.Border)
            shade += BorderShadeSteps * ShadeStepDelta;

        // Shades are authored light-first, so a dark variant mirrors the whole ladder. This
        // happens BEFORE the foreground calculation, which is why that one reads the already-
        // mirrored background rather than re-inverting.
        if (dark)
            shade = MaxShadeNumber - shade;

        return IsForeground(location) ? ForegroundShade(ramp, shade, location) : Clamp(shade);
    }

    /// <summary>The colour for a request.</summary>
    public static Color Resolve(INeutralRamp ramp, SurfaceContext context, ColorLocation location, bool dark)
        => ramp.Resolve(ShadeNumberFor(context, location, dark, ramp));

    private static bool IsForeground(ColorLocation location) =>
        location is ColorLocation.ForegroundNormal
                 or ColorLocation.ForegroundSecondary
                 or ColorLocation.ForegroundTertiary;

    /// <summary>Walk from the background toward whichever end of the ramp contrasts with it,
    /// stopping at the tier's contrast percentage, then quantise back onto the shade grid.</summary>
    private static int ForegroundShade(INeutralRamp ramp, int backgroundShade, ColorLocation tier)
    {
        backgroundShade = Clamp(backgroundShade);

        // A light background wants dark text, so aim at the dark end, and vice versa.
        var backgroundIsLight = IsLight(ramp.Resolve(backgroundShade));
        var target = backgroundIsLight ? MaxShadeNumber : 0;

        var contrast = tier switch
        {
            ColorLocation.ForegroundNormal => backgroundIsLight ? NormalOnLight : NormalOnDark,
            ColorLocation.ForegroundSecondary => backgroundIsLight ? SecondaryOnLight : SecondaryOnDark,
            _ => backgroundIsLight ? TertiaryOnLight : TertiaryOnDark,
        };

        var delta = (int)Math.Round(Math.Abs(backgroundShade - target) * contrast, MidpointRounding.AwayFromZero);
        var shade = backgroundShade < target ? backgroundShade + delta : backgroundShade - delta;

        return Clamp(RoundToStep(shade));
    }

    private static int RoundToStep(int value) =>
        (int)Math.Round((double)value / ShadeStepDelta, MidpointRounding.AwayFromZero) * ShadeStepDelta;

    private static int Clamp(int shade) => Math.Clamp(shade, 0, MaxShadeNumber);

    /// <summary>Whether a colour reads as light. CIE L* rather than a naive RGB average — the
    /// eye's response is far from linear, and the mid-ladder greys are exactly where a cheap
    /// approximation picks the wrong text colour.</summary>
    public static bool IsLight(Color color) => PerceivedLightness(color) >= LightnessThreshold;

    /// <summary>CIE L*, normalised to 0..1.</summary>
    public static double PerceivedLightness(Color color)
    {
        // sRGB -> linear
        static double Linear(byte channel)
        {
            var c = channel / 255.0;
            return c <= 0.04045 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
        }

        var y = 0.2126 * Linear(color.R) + 0.7152 * Linear(color.G) + 0.0722 * Linear(color.B);

        const double epsilon = 216.0 / 24389.0;
        const double kappa = 24389.0 / 27.0;
        var lStar = y < epsilon ? y * kappa : Math.Cbrt(y) * 116.0 - 16.0;
        return lStar / 100.0;
    }
}
