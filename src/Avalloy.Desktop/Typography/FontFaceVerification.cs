using System.Collections.Generic;
using Avalonia.Media;

namespace Avalloy.Typography;

/// <summary>One thing a face claimed that the font manager did not deliver.</summary>
/// <param name="Face">The face that was checked.</param>
/// <param name="Claim">What it declared — for a test message.</param>
/// <param name="Detail">What actually happened instead.</param>
public sealed record FontFaceProblem(FontFaceDef Face, string Claim, string Detail)
{
    public override string ToString() => $"{Face.DisplayName}: {Claim} — {Detail}";
}

/// <summary>
/// Checks that a bundled face delivers what it declared, against the live font manager.
///
/// <para><b>Every failure this catches is otherwise silent.</b> Avalonia answers a missing weight by emboldening
/// the Regular, a missing italic by shearing it, and a wrong family name by falling back to whatever else the uri
/// holds — all of which render perfectly readable text, so nothing errors, nothing logs, and the app simply looks
/// slightly cruder than the fonts it ships. In one app that state went unnoticed for a year across every face in
/// the app, because it fails UNIFORMLY: with nothing drawn correctly there is nothing to compare against.</para>
///
/// <para>Deliberately a plain function over <see cref="FontManager"/> rather than a set of test assertions, so it
/// needs no test framework and no headless session of its own — each app calls it from the harness it already
/// has, and gets the same guarantees.</para>
/// </summary>
public static class FontFaceVerification
{
    /// <summary>Check one face. Returns empty when it delivered everything it declared.
    ///
    /// <para>A face that declares nothing is held to nothing — a defer entry, or one whose family is a bare OS
    /// stack. There is no claim to check, and what such a face resolves to varies by machine anyway.</para></summary>
    public static IReadOnlyList<FontFaceProblem> Verify(FontFaceDef face, FontManager fonts)
    {
        var problems = new List<FontFaceProblem>();
        if (face.Family is not { } family) return problems;   // defer entry — nothing claimed

        if (!fonts.TryGetGlyphTypeface(new Typeface(family), out var regular))
        {
            problems.Add(new(face, "loads", $"'{family}' did not resolve to any typeface"));
            return problems;
        }

        // The default instance is what every un-weighted run draws with. A face whose file defaults to Thin or
        // ExtraLight — which several variable fonts do as shipped — renders the entire app hairline.
        if (regular.Weight != FontWeight.Normal)
            problems.Add(new(face, "ships its Regular instance",
                $"the face resolved at {regular.Weight}; Avalonia draws a file's default instance and " +
                "cannot move a variable axis, so that is the only upright weight it will ever draw"));

        if (regular.FontSimulations != FontSimulations.None)
            problems.Add(new(face, "ships a real Regular", $"resolved with {regular.FontSimulations}"));

        // Italic: claimed or not, assert which way it went. A face that DOESN'T bundle one is fine — but the
        // app needs to know, because a sheared upright leans past its advance width and clips.
        if (fonts.TryGetGlyphTypeface(new Typeface(family, FontStyle.Italic), out var italic))
        {
            var sheared = italic.FontSimulations.HasFlag(FontSimulations.Oblique);
            if (face.HasItalic && sheared)
                problems.Add(new(face, "bundles a real italic",
                    "the italic came back sheared from the upright — the file is missing, or its family name " +
                    "strands it in a family of its own (the weight-suffixed name(1) trap)"));
            else if (!face.HasItalic && !sheared)
                problems.Add(new(face, "bundles no italic", "a real italic file resolved — update the declaration"));
        }

        foreach (var weight in face.RealWeights)
        {
            if (!fonts.TryGetGlyphTypeface(new Typeface(family, FontStyle.Normal, weight), out var got))
            {
                problems.Add(new(face, $"bundles a real {weight}", "it did not resolve at all"));
                continue;
            }

            if (got.FontSimulations != FontSimulations.None)
                problems.Add(new(face, $"bundles a real {weight}",
                    $"Avalonia synthesized it ({got.FontSimulations}) instead of drawing a file"));

            // NOT redundant with the simulation check: Avalonia only simulates at 600 and above, so a missing
            // Medium comes back as a clean, unsimulated 400 and looks perfectly healthy.
            if (got.Weight != weight)
                problems.Add(new(face, $"bundles a real {weight}", $"the face that resolved reports {got.Weight}"));

            if (face.HasItalic
                && fonts.TryGetGlyphTypeface(new Typeface(family, FontStyle.Italic, weight), out var wi)
                && wi.FontSimulations != FontSimulations.None)
                problems.Add(new(face, $"bundles a real {weight} Italic",
                    $"Avalonia synthesized it ({wi.FontSimulations})"));
        }

        // The nominated weights are only a claim when the face DECLARED a ramp. A face that declares none has
        // not opted into the weight system at all: it ships one file, everything else is synthesized, and
        // holding it to the record's default emphatic weight would report a defect on every plain face in a
        // catalog. Declaring weights is what turns the nomination into a promise.
        if (face.Weights is null) return problems;

        // For a face that DID declare, the weights the app will actually SET have to be ones it draws, or every
        // site that asks for them gets a synthesized face — which reads as a font choice rather than a defect.
        if (!face.RealWeights.Contains(face.PlainWeight))
            problems.Add(new(face, $"nominates {face.PlainWeight} as its plain weight",
                "that weight is not among the ones it bundles"));
        if (!face.RealWeights.Contains(face.EmphaticWeight))
            problems.Add(new(face, $"nominates {face.EmphaticWeight} as its emphatic weight",
                "that weight is not among the ones it bundles"));

        return problems;
    }

    /// <summary>Check every face in a catalog.</summary>
    public static IReadOnlyList<FontFaceProblem> Verify(FontCatalog catalog, FontManager fonts)
    {
        var all = new List<FontFaceProblem>();
        foreach (var face in catalog.Faces) all.AddRange(Verify(face, fonts));
        return all;
    }
}
