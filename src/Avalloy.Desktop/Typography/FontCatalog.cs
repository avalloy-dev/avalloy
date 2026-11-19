using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Avalloy.Typography;

/// <summary>The three roles an app sets type in. One catalog per axis, one persisted preference per axis.</summary>
public enum FontAxis
{
    /// <summary>Chrome, prose, controls — everything that isn't code or a name.</summary>
    Ui,

    /// <summary>Everything monospaced: editors, diffs, terminals, paths and ids in the chrome.</summary>
    Code,

    /// <summary>Names, titles and numerals — type that carries identity rather than just being read.</summary>
    Display,
}

/// <summary>
/// The faces one axis offers, and how to apply the picked one.
///
/// <para>Applying writes a small group of resources rather than one: the family, the face's own OpenType
/// FEATURES, and its own plain/emphatic WEIGHTS. The last two exist because neither travels with a family name.
/// A feature tag means whatever its designer assigned it, and a weight name is whatever a foundry assigned its own
/// ramp — measured, two display families at the same nominal weight can differ by a third in ink. Hard-coding
/// either at the call sites makes swapping face a silent restyling instead of a choice.</para>
///
/// <para><see cref="FamilyKey"/> is configurable so an app with its own resource contract can keep it, but the
/// defaults — <c>Font.Ui</c>/<c>Font.Mono</c>/<c>Font.Display</c> — are the shared vocabulary every Avalloy
/// control reads, and an app is best served by leaving them alone.</para>
/// </summary>
public sealed class FontCatalog
{
    /// <param name="axis">Which role these faces set.</param>
    /// <param name="faces">The faces, in picker order. The first is the fallback for an unknown id.</param>
    /// <param name="defaultId">Id used when the preference is empty or names a face that has since been dropped —
    /// a pref written by a newer build must never leave the app fontless. Defaults to the first face.</param>
    /// <param name="familyKey">Resource key for the family. Defaults to the axis' <c>Font.*</c> token.</param>
    public FontCatalog(FontAxis axis, IReadOnlyList<FontFaceDef> faces, string? defaultId = null,
        string? familyKey = null)
    {
        if (faces.Count == 0) throw new ArgumentException("A font catalog needs at least one face.", nameof(faces));
        Axis = axis;
        Faces = faces;
        DefaultId = defaultId ?? faces[0].Id;
        FamilyKey = familyKey ?? DefaultFamilyKey(axis);
    }

    public FontAxis Axis { get; }
    public IReadOnlyList<FontFaceDef> Faces { get; }
    public string DefaultId { get; }

    /// <summary>Resource key the resolved family is written to.</summary>
    public string FamilyKey { get; }

    /// <summary>Resource key for the face's <see cref="FontFaceDef.FeatureSet"/>.</summary>
    public string FeaturesKey => DefaultFamilyKey(Axis) + ".Features";

    /// <summary>Resource key for the face's emphatic weight.</summary>
    public string WeightKey => DefaultFamilyKey(Axis) + ".Weight";

    /// <summary>Resource key for the face's plain weight.</summary>
    public string WeightPlainKey => DefaultFamilyKey(Axis) + ".WeightPlain";

    /// <summary>The <c>Font.*</c> token an axis writes by default.</summary>
    public static string DefaultFamilyKey(FontAxis axis) => axis switch
    {
        FontAxis.Ui => "Font.Ui",
        FontAxis.Code => "Font.Mono",
        FontAxis.Display => "Font.Display",
        _ => throw new ArgumentOutOfRangeException(nameof(axis)),
    };

    /// <summary>The face for an id, falling back to <see cref="DefaultId"/> for empty/unknown ids.</summary>
    public FontFaceDef Resolve(string? id) =>
        Faces.FirstOrDefault(f => string.Equals(f.Id, id, StringComparison.OrdinalIgnoreCase))
        ?? Faces.FirstOrDefault(f => string.Equals(f.Id, DefaultId, StringComparison.OrdinalIgnoreCase))
        ?? Faces[0];

    /// <summary>The resolved family for an id — null for a defer face.</summary>
    public FontFamily? FamilyFor(string? id) => Resolve(id).Family;

    /// <summary>Write the picked face's family, features and weights onto <paramref name="resources"/>.
    ///
    /// <para>A DEFER face (null family) skips the family write only: the theme's value stands, which is what makes
    /// a "Theme default" entry a no-op rather than a clear. Features and weights are always written, including
    /// when empty, so switching from a face that declares them to one that doesn't actually turns them off.</para>
    /// </summary>
    public void Apply(IResourceDictionary resources, string? id)
    {
        var face = Resolve(id);
        if (face.Family is { } family) resources[FamilyKey] = family;
        resources[FeaturesKey] = face.FeatureSet;
        resources[WeightKey] = face.EmphaticWeight;
        resources[WeightPlainKey] = face.PlainWeight;
    }

    /// <summary>Convenience over <see cref="Apply(IResourceDictionary, string?)"/> for the application itself —
    /// writing on <c>Application.Resources</c> shadows the same key in any merged dictionary (an owner dictionary
    /// wins over its merged ones) and notifies every DynamicResource consumer, which is what repaints live.</summary>
    public void Apply(Application app, string? id) => Apply(app.Resources, id);
}
