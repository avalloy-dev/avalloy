using System.Collections.Generic;
using Avalonia.Media;

namespace Avalloy.Typography;

/// <summary>
/// One pickable typeface on one axis — what a font picker offers and what the app applies.
///
/// <para>Faces are <b>bundled</b>: an app ships the files, which is what makes it look the same on every OS —
/// and puts the burden of the weight ramp on the app, because <b>Avalonia takes exactly ONE face per file</b>
/// (a variable font's <c>fvar</c> default instance) and SYNTHESIZES every other weight and the italic by
/// emboldening or shearing the one it has.</para>
///
/// <para>A face named for the OS to resolve — a bare fallback stack — is deliberately NOT a distinct shape here.
/// It declares nothing, so nothing about it can be verified; what a machine actually has is what it renders, and
/// a picker offering one is promising something it can only keep on the machine it was written on. Pass a stack
/// as the family if you want one (the "System default" entry is exactly that), but it is an ordinary face that
/// happens to declare no weights, not a supported second model.</para>
///
/// <para>None of that fails loudly. A missing Bold renders as a thickened Regular, a missing italic as a sheared
/// upright, and a missing Medium as a plain Regular — Avalonia only simulates at 600 and above. Declaring what a
/// face actually ships is what lets <see cref="FontFaceVerification"/> assert it, and that assertion is the only
/// thing standing between "we bundled the files" and "the files are reachable".</para>
/// </summary>
/// <param name="Id">Stable persisted id — never the display name.</param>
/// <param name="DisplayName">What a picker shows.</param>
/// <param name="Family">The family to apply, or <c>null</c> to DEFER: the applier leaves the resource exactly as
/// the theme (or the app's baseline) left it. A null family is what lets a theme own the font by default, so a
/// picker's leading "Theme default" entry is a no-op rather than a clear.</param>
/// <param name="HasItalic">Whether a real italic FILE is reachable. False = Avalonia shears the upright, whose ink
/// leans past the advance width.</param>
/// <param name="Features">OpenType features to switch on for THIS face, in HarfBuzz syntax (<c>"ss09, frac"</c>).
/// Per-face and never app-wide: a feature tag means whatever the individual designer assigned it, so <c>ss01</c>
/// can be a single-storey 'a' in one family and a high-legibility 'i' in another.</param>
/// <param name="Weights">Weights bundled as REAL files beyond 400.</param>
/// <param name="PlainWeight">This face's answer to "set at its normal colour".</param>
/// <param name="EmphaticWeight">This face's answer to "set emphatic". Separate from a hard-coded number because a
/// weight NAME is whatever a foundry assigned its own ramp: two families at the same nominal weight can differ by
/// a third in ink, so one shared number sets one of them correctly and the other far too dark.</param>
public sealed record FontFaceDef(
    string Id,
    string DisplayName,
    FontFamily? Family,
    bool HasItalic = false,
    string? Features = null,
    FontWeight[]? Weights = null,
    FontWeight PlainWeight = FontWeight.Normal,
    FontWeight EmphaticWeight = FontWeight.SemiBold)
{
    /// <summary>Every weight this face draws with a real file — always including
    /// <see cref="FontWeight.Normal"/>, which every face ships by definition.</summary>
    public IReadOnlyList<FontWeight> RealWeights =>
        Weights is null ? [FontWeight.Normal] : [FontWeight.Normal, .. Weights];

    /// <summary>The parsed features — EMPTY, never null, when the face declares none. The distinction is
    /// load-bearing wherever features INHERIT: an empty collection is a real value that overrides an inherited
    /// one, where null leaves the property unset and lets an ancestor's features through. That is what keeps a UI
    /// face's stylistic sets out of code text, where the same tag means something else.</summary>
    public FontFeatureCollection FeatureSet =>
        Features is { Length: > 0 } f ? FontFeatureCollection.Parse(f) : [];

    /// <summary>A face whose files ship inside the app: identical on every OS, and the app's problem to declare.
    /// <paramref name="uri"/> should be a FOLDER uri (<c>avares://App/Assets/Fonts/ui/x#Family</c>) — a folder
    /// loads every file in it, which is the only way a family's real italic and real weights are reachable. A
    /// single-FILE uri silently contributes one face, leaving bundled Bold/Italic files unused.</summary>
    public static FontFaceDef Bundled(string id, string displayName, string uri,
        bool hasItalic = false, string? features = null, FontWeight[]? weights = null,
        FontWeight plainWeight = FontWeight.Normal, FontWeight emphaticWeight = FontWeight.SemiBold) =>
        new(id, displayName, new FontFamily(uri), hasItalic, features, weights, plainWeight, emphaticWeight);

    /// <summary>The picker's leading "leave it alone" entry: whatever the active theme declares wins. Registered
    /// under the empty id so an unset preference resolves here.</summary>
    public static FontFaceDef ThemeDefault(string displayName = "Theme default") =>
        new("", displayName, null);
}
