using Avalloy.Preferences;
using Avalloy.Services;
using Avalonia;
using AwesomeAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Avalloy.Desktop.Tests;

/// <summary>
/// The contract of ThemeService's two delegate seams is entirely about WHEN they run.
/// Nothing about that is visible to the compiler — wire the engine adapter to the wrong
/// one and it still builds, still runs, and just silently produces the wrong palette or
/// stomps the user's font. These tests pin the ordering.
/// <para>
/// No Avalonia.Headless here on purpose: ThemeService touches only
/// <c>Application.Resources</c> and <c>RequestedThemeVariant</c>, so a bare
/// <see cref="Application"/> with no rendering platform is enough.
/// </para>
/// </summary>
[TestClass]
public sealed class ThemeApplySeamTests
{
    private const string TokenKey = "AlloySeamProbeToken";
    private const string DirectiveKey = "engine.seamProbe";

    /// <summary>The pre-apply seam must land after the schema clear but BEFORE the theme's
    /// tokens do — that ordering is the reason it exists, since an engine that regenerates
    /// its palette here needs the theme's values to layer on top of the fresh ones.
    /// <para>
    /// This needs a REAL theme preset in the catalog. With an empty catalog the token only
    /// arrives at phase 5 (overrides), so a pre-apply seam misplaced anywhere between
    /// phases 2 and 5 still sees an empty dictionary and the test passes while the contract
    /// is broken — verified by mutation.
    /// </para></summary>
    [TestMethod]
    public void PreApply_Runs_Before_Theme_Tokens_Land_And_PostApply_After()
    {
        var app = new Application();
        // Seed the key so "cleared" is distinguishable from "never set" — if the pre-apply
        // seam were invoked before phase 1 instead of after it, this stale value would
        // still be sitting there and the assertion below would catch it.
        app.Resources[TokenKey] = "stale-value-from-a-previous-theme";

        object? seenInPre = "sentinel";
        object? seenInPost = null;

        var service = BuildService(app,
            themes: CatalogWithThemeSetting(TokenKey, "#FF0000"),
            themeName: ProbeThemeName,
            preApply: ctx => seenInPre = ctx.Resources.TryGetValue(TokenKey, out var v) ? v : null,
            postApply: ctx => seenInPost = ctx.Resources.TryGetValue(TokenKey, out var v) ? v : null);

        service.Apply(Prefs(themeName: ProbeThemeName));

        seenInPre.Should().BeNull(
            "phase 1 clears schema-managed keys and the pre-apply seam runs after that clear "
            + "but before the theme's tokens are written at phase 3");
        seenInPost.Should().NotBeNull(
            "the post-apply seam runs after the theme and after user overrides, so final "
            + "values are readable there");
    }

    /// <summary>Both seams resolve directives, because the engine reads its palette inputs in
    /// pre-apply while the app's tail may branch on the same directive in post-apply.</summary>
    [TestMethod]
    public void Both_Seams_See_Effective_Directives()
    {
        var app = new Application();
        string? fromPre = null, fromPost = null;

        var service = BuildService(app,
            preApply: ctx => fromPre = ctx.EffectiveDirective(DirectiveKey),
            postApply: ctx => fromPost = ctx.EffectiveDirective(DirectiveKey));

        service.Apply(Prefs(overrides: new Dictionary<string, string>
        {
            [DirectiveKey] = "Indigo",
        }));

        fromPre.Should().Be("Indigo");
        fromPost.Should().Be("Indigo");
    }

    /// <summary>The context carries the source preferences so an engine adapter can read the
    /// non-token knobs it needs (system accent / focus colour) without the app closing over
    /// its own prefs service purely to forward them.</summary>
    [TestMethod]
    public void Context_Carries_Source_Prefs()
    {
        var app = new Application();
        IAppearanceModel? seen = null;

        var service = BuildService(app, preApply: ctx => seen = ctx.Prefs);
        service.Apply(Prefs(useSystemFocusColor: true));

        seen.Should().NotBeNull();
        seen!.UseSystemFocusColor.Should().BeTrue();
    }

    /// <summary>Both seams are optional — the native lane wires neither.</summary>
    [TestMethod]
    public void Apply_Works_With_No_Seams_Wired()
    {
        var app = new Application();
        var act = () => BuildService(app).Apply(Prefs());
        act.Should().NotThrow();
    }

    private const string ProbeThemeName = "seam-probe";

    /// <summary>A catalog holding one real light theme that sets <paramref name="token"/>.
    /// Written to a temp directory because <see cref="PresetCatalog"/> only ingests presets
    /// from embedded resources or a user-presets root — there is no "add this Preset"
    /// entry point, and going through the real loader keeps the test honest about the
    /// JSON shape besides.</summary>
    private static PresetCatalog CatalogWithThemeSetting(string token, string value)
    {
        var root = Path.Combine(Path.GetTempPath(), "avalloy-seam-tests", Guid.NewGuid().ToString("n"));
        var themes = Path.Combine(root, "themes");
        Directory.CreateDirectory(themes);
        File.WriteAllText(Path.Combine(themes, $"{ProbeThemeName}.json"), $$"""
            {
              "name": "{{ProbeThemeName}}",
              "displayName": "Seam Probe",
              "kind": "theme",
              "variant": "light",
              "tokens": { "{{token}}": "{{value}}" }
            }
            """);
        return PresetCatalog.Load(userPresetsRoot: root);
    }

    private static ThemeService BuildService(
        Application app,
        PresetCatalog? themes = null,
        string themeName = "none",
        Action<ThemeApplyContext>? preApply = null,
        Action<ThemeApplyContext>? postApply = null) =>
        new(
            app: app,
            themes: themes ?? PresetCatalog.Empty,
            schema: new TokenSchema(new Dictionary<string, TokenKind>
            {
                [TokenKey] = TokenKind.Color,
            }),
            defaultLightName: themeName,
            defaultDarkName: themeName,
            postApply: postApply,
            preApply: preApply);

    private static IAppearanceModel Prefs(
        IReadOnlyDictionary<string, string>? overrides = null,
        bool useSystemFocusColor = false,
        string themeName = "") =>
        new FakeAppearance
        {
            ThemePresetLight = themeName,
            ThemeOverridesLight = overrides ?? new Dictionary<string, string>(),
            UseSystemFocusColor = useSystemFocusColor,
        };

    /// <summary>Minimal IAppearanceModel. Variant is pinned to Light so the resolver never
    /// consults the OS — there is no platform under these tests.</summary>
    private sealed class FakeAppearance : IAppearanceModel
    {
        public string DensityPreset => "";
        public string ThemePresetLight { get; init; } = "";
        public string ThemePresetDark => "";
        public ThemeVariantPreference ThemeVariant => ThemeVariantPreference.Light;
        public string IconPackId => "";
        public string MonoFont => "";
        public string UiFont => "";
        public bool AutoSwitchVariant => false;
        public bool UseSystemAccent => false;
        public bool UseSystemFocusColor { get; init; }
        public IReadOnlyDictionary<string, string> ThemeOverridesLight { get; init; }
            = new Dictionary<string, string>();
        public IReadOnlyDictionary<string, string> ThemeOverridesDark { get; init; }
            = new Dictionary<string, string>();
    }
}
