using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;
using Avalloy.Preferences;
using Avalloy.Typography;
using Avalloy.Theming;
using Microsoft.Extensions.Logging;

namespace Avalloy.Services;

/// <summary>
/// App-agnostic theming engine. Resolves an <see cref="IAppearanceModel"/>'s
/// theme + variant preferences against the supplied theme catalog and applies the
/// resulting tokens to the Application's resource dictionary.
/// <para>
/// This class knows nothing about what actually draws the controls. Everything
/// app- or engine-specific is injected: the <see cref="TokenSchema"/> the cascade
/// clears/parses against, the default-light/default-dark theme names used as
/// fallbacks, and two optional delegates that bracket the generic phases.
/// </para>
/// <para>
/// The two seams differ only in timing, and the ordering is the whole point:
/// <list type="bullet">
///   <item><c>preApply</c> runs at phase 2 — after the schema clear, BEFORE the
///     theme's tokens land. This is where a control-theme engine consumes
///     directives that regenerate its palette (accent ramp, neutral tint), so that
///     the theme's own token values then layer on top of freshly generated ones.
///     The resource dictionary is deliberately bare here; read
///     <see cref="ThemeApplyContext.EffectiveDirective"/>, not resources.</item>
///   <item><c>postApply</c> runs at phase 7 — after user overrides, before
///     <see cref="Applied"/> fires. This is where an app derives resources from
///     final values (pane-focus or tab-indicator geometry, say) and where an
///     engine adapter mirrors resolved tokens into its own slots. Running such a
///     mirror any earlier lets a theme's font silently overwrite the user's pick.</item>
/// </list>
/// </para>
/// <para>
/// Theme JSON never names an engine's types — a directive is just a string here,
/// and the delegate on the far side decides what it means.
/// </para>
/// </summary>
public sealed class ThemeService : IThemeController
{
    private readonly Application _app;
    private readonly PresetCatalog _themes;
    private readonly TokenSchema _schema;
    private readonly string _defaultLightName;
    private readonly string _defaultDarkName;
    private readonly Action<ThemeApplyContext>? _preApply;
    private readonly Action<ThemeApplyContext>? _postApply;
    private readonly FontCatalog? _monoFonts;
    private readonly FontCatalog? _uiFonts;
    private readonly FontFamily? _baselineBodyFont;
    private readonly FontFamily? _baselineCodeFont;
    private readonly ILogger<ThemeService>? _logger;

    /// <param name="app">The running Application whose Resources receive the theme.</param>
    /// <param name="themes">Catalog of theme presets (both variants).</param>
    /// <param name="schema">The token allowlist the cascade clears + parses against.
    /// Must include every token any preset in <paramref name="themes"/> can set, or
    /// stale tokens linger across swaps.</param>
    /// <param name="defaultLightName">Theme name used when the Light slot points at
    /// a missing/empty preset.</param>
    /// <param name="defaultDarkName">Theme name used when the Dark slot points at a
    /// missing/empty preset.</param>
    /// <param name="postApply">Optional app-specific tail run after overrides, before
    /// <see cref="Applied"/>. Null for apps with no derived resources.</param>
    /// <param name="preApply">Optional head run at phase 2 — after the schema clear, before
    /// the theme's tokens land. The seam for a control-theme engine whose palette must be
    /// regenerated from directives before theme values layer on top. Null for apps whose
    /// controls are themed by tokens alone (the native lane).</param>
    /// <param name="logger">Optional diagnostics.</param>
    /// <param name="monoFonts">Optional <see cref="FontCatalog"/> of selectable monospace faces. When the
    /// resolved <see cref="IAppearanceModel.MonoFont"/> names a face carrying a family, it overrides the theme's
    /// <c>Font.Mono</c>; a <see cref="FontFaceDef.ThemeDefault"/> face leaves the theme's value alone.
    /// Build it with the family key pointed at that resource: <c>new FontCatalog(FontAxis.Code, faces,
    /// familyKey)</c>, passing the default family key for its axis.</param>
    /// <param name="uiFonts">Optional catalog of selectable UI/proportional faces. Same contract as
    /// <paramref name="monoFonts"/>, over <c>Font.Ui</c>.</param>
    /// <param name="baselineBodyFont">The app's fallback UI font, applied to <c>Font.Ui</c> only when
    /// neither the active theme nor the font pref sets one. This is what keeps control metrics stable across
    /// themes that decline to pick a font — without it, such a theme silently falls through to the engine's own
    /// font and every control changes height. Null opts out.</param>
    /// <param name="baselineCodeFont">Same, for <c>Font.Mono</c>.</param>
    public ThemeService(
        Application app,
        PresetCatalog themes,
        TokenSchema schema,
        string defaultLightName,
        string defaultDarkName,
        Action<ThemeApplyContext>? postApply = null,
        Action<ThemeApplyContext>? preApply = null,
        ILogger<ThemeService>? logger = null,
        FontCatalog? monoFonts = null,
        FontCatalog? uiFonts = null,
        FontFamily? baselineBodyFont = null,
        FontFamily? baselineCodeFont = null)
    {
        _app = app;
        _themes = themes;
        _schema = schema;
        _defaultLightName = defaultLightName;
        _defaultDarkName = defaultDarkName;
        _postApply = postApply;
        _preApply = preApply;
        _monoFonts = monoFonts;
        _uiFonts = uiFonts;
        _baselineBodyFont = baselineBodyFont;
        _baselineCodeFont = baselineCodeFont;
        _logger = logger;
    }

    /// <summary>The registered monospace-font catalog (for a picker). Empty if the app registered none.</summary>
    public IReadOnlyList<FontFaceDef> MonoFonts => _monoFonts?.Faces ?? System.Array.Empty<FontFaceDef>();

    /// <summary>The registered UI-font catalog (for a picker). Empty if the app registered none.</summary>
    public IReadOnlyList<FontFaceDef> UiFonts => _uiFonts?.Faces ?? System.Array.Empty<FontFaceDef>();

    /// <summary>Per-token edits the theme editor wants to apply on top of the
    /// persisted slot overrides without writing them to disk. When non-null, takes
    /// priority over the slot's persisted overrides at <see cref="Apply"/> time.
    /// Targets whichever slot is currently rendering.</summary>
    public IReadOnlyDictionary<string, string>? SessionOverrides { get; set; }

    /// <summary>The merged effective overrides for <paramref name="prefs"/> at the
    /// resolved active variant: the matching slot's persisted overrides + session
    /// overrides (session wins).</summary>
    public IReadOnlyDictionary<string, string> EffectiveOverrides(IAppearanceModel prefs)
    {
        var variant = ResolveActiveVariant(prefs);
        var persisted = variant == ThemeVariant.Light
            ? prefs.ThemeOverridesLight
            : prefs.ThemeOverridesDark;
        if (SessionOverrides is null || SessionOverrides.Count == 0)
            return persisted ?? new Dictionary<string, string>();
        var merged = new Dictionary<string, string>(persisted ?? new Dictionary<string, string>(),
            StringComparer.Ordinal);
        foreach (var (k, v) in SessionOverrides) merged[k] = v;
        return merged;
    }

    /// <summary>Apply <paramref name="prefs"/> to the running app. Idempotent —
    /// safe to call after every preferences-changed event.</summary>
    public void Apply(IAppearanceModel prefs)
    {
        // Resolve which slot to render. System honors the OS; explicit Light/Dark
        // pin the slot. Falls back to the injected default for the resolved
        // variant if the named slot is missing/empty.
        var resolvedVariant = ResolveActiveVariant(prefs);
        var slotName = resolvedVariant == ThemeVariant.Light
            ? prefs.ThemePresetLight
            : prefs.ThemePresetDark;
        var theme = _themes.GetTheme(slotName) ?? FallbackForVariant(resolvedVariant);
        var overrides = EffectiveOverrides(prefs);

        // Phase 1: clear schema-managed keys + write the density layer. The
        // pre-clear prevents stale tokens from a previously active theme from
        // lingering. Density runs here so theme variants (phase 3) can win.
        // For() clears every schema-managed key, so stale tokens from a previously active
        // theme cannot linger when the new one declines to set them.
        ResourceCascade.For(_app.Resources, _schema);

        // Phase 2: flip the theme variant, then hand the engine its directives. The
        // RequestedThemeVariant assignment fires ActualThemeVariantChanged, which
        // AvalloyApp hooks to republish its OS-derived accent brushes — letting
        // that land BETWEEN clear and theme overrides means the OS accent shines
        // through for keys the theme doesn't override.
        _app.RequestedThemeVariant = resolvedVariant;

        // The pre-apply seam. Deliberately before phase 3: an engine that regenerates a
        // whole palette from a directive (accent ramp, neutral tint) must do it now, so the
        // theme's own tokens land ON TOP of freshly generated values rather than being
        // wiped by a regeneration that happens later. Resources are bare at this point —
        // the delegate reads directives, not resources.
        _preApply?.Invoke(new ThemeApplyContext(this, _app, theme, overrides, resolvedVariant, prefs));

        // Phase 3: layer the theme tokens on top without re-clearing.
        ResourceCascade.Continue(_app.Resources, _schema)
            .ApplyTheme(theme);

        // Phase 3b: baseline fonts. Set-if-absent, so a theme that names a font
        // wins and one that stays silent still lands on the app's font rather
        // than the engine's. First of the four font layers:
        //   baseline (here) → theme token (phase 3) → font pref (4e) → override (5).
        ApplyBaselineFonts();

        // Phase 4: optional system-accent override for the selection affordances.
        // When the user opts in (default), the live OS accent drives the Alloy selection wash —
        // the pane cursor row, and (via the app's own forwarding) Fluent's stock list/grid
        // selection — so a selected row and selected text match the platform accent rather than
        // the brand ramp. Foreground is forced White: OS accents span the full hue/saturation
        // range and White is the safe contrast pick.
        if (prefs.UseSystemAccent)
        {
            var accent = TryGetSystemAccent() ?? DefaultSystemAccent;
            var accentBrush = new SolidColorBrush(accent);
            var onAccentText = new SolidColorBrush(Colors.White);

            _app.Resources["AlloyAccentSelectionBrush"] = accentBrush;
            _app.Resources["AlloyAccentSelectionForeground"] = onAccentText;
        }

        // Phase 4a2: derive a translucent variant of the (now-final) selection wash
        // for TEXT selection inside editable fields (the inline rename box, search /
        // address inputs). Avalonia 12's TextPresenter does NOT recolor selected
        // glyphs — SelectionForegroundBrush is ignored — so a saturated highlight
        // leaves the body text in its normal (dark) foreground and unreadable, worst
        // under low-contrast themes (e.g. Solarized light: slate text on the blue
        // wash). A translucent accent — macOS's own text-selection convention — keeps
        // the dark glyphs legible while still reading as the accent. Same hue as
        // AlloyAccentSelectionBrush (read back after the Phase 4 override) so row
        // selection and text selection stay visually consistent. Runs in both
        // branches; the resource follows the live OS accent because Phase 4 already
        // re-resolved the source brush.
        if (_app.Resources.TryGetResource("AlloyAccentSelectionBrush", _app.ActualThemeVariant, out var selObj)
            && selObj is ISolidColorBrush selBrush)
        {
            var c = selBrush.Color;
            _app.Resources["AlloyAccentSelectionBrushSubtle"] =
                new SolidColorBrush(Color.FromArgb(0x4D, c.R, c.G, c.B));
        }

        // Phase 4b: optional system-accent focus ring, published as AlloyFocusRingBrush for the
        // app to forward onto whatever its control theme uses. A neutral high-contrast ring reads
        // as foreign on macOS, where focus is the OS accent glow. REMOVED rather than set when the
        // pref is off, so the control theme's own focus visual takes over. Updates live:
        // AvalloyApp re-applies the theme on PlatformSettings.ColorValuesChanged.
        if (prefs.UseSystemFocusColor)
            _app.Resources["AlloyFocusRingBrush"] =
                new SolidColorBrush(TryGetSystemAccent() ?? DefaultSystemAccent);
        else
            _app.Resources.Remove("AlloyFocusRingBrush");

        // Phase 4e: resolve the user's font preferences (mono + UI) against the registered catalogs into the
        // Alloy font resources. A pick carrying a family overrides the theme; the catalogs' "Theme default"
        // entry carries none and leaves the theme's value standing (see FontChoice).
        ApplyFontPreferences(prefs);

        // Phase 5: per-token user overrides (editor session buffer + persisted
        // prefs), layered last so an explicitly-tweaked token wins. Schema tokens
        // only — directives route through the Phase 2 engine call.
        ApplySchemaOverrides(overrides);

        // Phase 6: derive the last of the font tokens. Runs AFTER overrides — it used to run at
        // phase 5, i.e. before them, which is why every font knob in the theme editor was
        // silently inert.
        ApplyBaselineCodeFontSize();

        // Phase 7: app-specific tail. Runs after overrides so derived resources see
        // final values, and before Applied so the app's own Applied subscribers
        // (which read the published resources) observe a complete set.
        _postApply?.Invoke(new ThemeApplyContext(this, _app, theme, overrides, resolvedVariant, prefs));

        // Fire the post-apply notification last so subscribers see the fully-
        // published resource set.
        Applied?.Invoke(this, EventArgs.Empty);
        AnyApplied?.Invoke(this, EventArgs.Empty);
        // Engine-agnostic fan-out so Desktop's controls (TabStrip) refresh without a reference to this
        // service. The native theme controller raises the same hub.
        Avalloy.Theming.AvalloyTheme.NotifyApplied();
    }

    /// <summary>Raised at the end of every successful <see cref="Apply"/>. Used by
    /// view code that derives non-resource state (e.g. CSS-class stamping) from the
    /// published resources — Avalonia's DynamicResource pipeline re-evaluates on
    /// resource swaps but does not propagate to control <c>Classes</c>.</summary>
    public event EventHandler? Applied;

    /// <summary>Static mirror of <see cref="Applied"/> for reusable controls that
    /// derive class state from published resources but have no reference to the
    /// app's <see cref="ThemeService"/> instance (e.g. Avalloy's
    /// <see cref="Controls.Tabs.TabStrip"/>). Fires on every successful apply
    /// regardless of which service raised it.</summary>
    public static event EventHandler? AnyApplied;

    /// <summary>Effective value for a directive, considering (in priority order):
    /// user overrides → active theme/variant → null. Override values are JSON-quoted
    /// strings for directives; strip the quoting. Exposed to the post-apply tail.</summary>
    internal static string? EffectiveDirective(
        Preset? theme, string directiveKey,
        IReadOnlyDictionary<string, string> overrides)
    {
        if (overrides.TryGetValue(directiveKey, out var raw))
        {
            var trimmed = raw.Trim();
            if (trimmed.Length >= 2 && trimmed[0] == '"' && trimmed[^1] == '"')
                return trimmed[1..^1];
            return trimmed;
        }
        return ExtractDirective(theme, directiveKey);
    }

    /// <summary>Apply per-token user overrides as the last resource-write phase.
    /// Each entry is parsed against its <see cref="TokenSchema"/> kind. Directive
    /// keys are skipped — those route through the Phase 2 engine call.</summary>
    private void ApplySchemaOverrides(IReadOnlyDictionary<string, string> overrides)
    {
        if (overrides.Count == 0) return;
        foreach (var (key, raw) in overrides)
        {
            var kind = _schema.Get(key);
            if (kind is null) continue; // directive or unknown — skip
            JsonElement element;
            try
            {
                using var doc = JsonDocument.Parse(raw);
                element = doc.RootElement.Clone();
            }
            catch
            {
                _logger?.LogDebug("Theme override '{Key}' has unparseable JSON value '{Raw}' — skipped.", key, raw);
                continue;
            }
            switch (kind)
            {
                case TokenKind.Color:        ResourceTokens.TryApplyColor(_app.Resources, key, element); break;
                case TokenKind.FontFamily:   ResourceTokens.TryApplyFontFamily(_app.Resources, key, element); break;
                case TokenKind.FontWeight:   ResourceTokens.TryApplyFontWeight(_app.Resources, key, element); break;
                case TokenKind.FontSize:     ResourceTokens.TryApplyDouble(_app.Resources, key, element); break;
                case TokenKind.Size:         ResourceTokens.TryApplyDouble(_app.Resources, key, element); break;
                case TokenKind.Thickness:    ResourceTokens.TryApplyThickness(_app.Resources, key, element); break;
                case TokenKind.CornerRadius: ResourceTokens.TryApplyCornerRadius(_app.Resources, key, element); break;
            }
        }
    }

    private Color? TryGetSystemAccent()
    {
        try { return _app.PlatformSettings?.GetColorValues().AccentColor1; }
        catch { return null; }
    }

    /// <summary>Fallback when no live OS accent is available — same default blue
    /// AvalloyApp uses for the selection wash.</summary>
    private static readonly Color DefaultSystemAccent = Color.Parse("#0064E1");

    /// <summary>Phase 4e — resolve the user's mono + UI font picks against the registered catalogs.</summary>
    private void ApplyFontPreferences(IAppearanceModel prefs)
    {
        // FontCatalog.Apply writes the family onto the catalog's own familyKey (Alloy* here) and, alongside it,
        // the picked face's OpenType features and its plain/emphatic weights. A DEFER face (null family) skips
        // the family write only — leaving the resource exactly as the theme, or the phase-3b baseline, left it.
        // That used to Remove the key, which meant a theme could never own its own font: the pref layer wiped
        // it on every apply.
        _monoFonts?.Apply(_app.Resources, prefs.MonoFont);
        _uiFonts?.Apply(_app.Resources, prefs.UiFont);
    }

    /// <summary>Phase 3b — the app's fallback fonts, written only when the active theme declared none.
    /// Set-if-absent (phase 1 cleared the schema keys, so "present" can only mean the theme just wrote it).</summary>
    private void ApplyBaselineFonts()
    {
        if (_baselineBodyFont is { } body && !HasValue("Font.Ui"))
            _app.Resources["Font.Ui"] = body;
        if (_baselineCodeFont is { } code && !HasValue("Font.Mono"))
            _app.Resources["Font.Mono"] = code;
    }

    /// <summary>Baseline for <c>AlloyCodeFontSize</c>: the effective small body size. Unlike the families there's
    /// no app-supplied default — mono cells historically rendered at <c>DefaultFontSizeSmall</c>, so deriving it
    /// keeps them identical until a theme says otherwise. Runs in phase 6, after overrides, so an explicit value
    /// from any layer wins.</summary>
    private void ApplyBaselineCodeFontSize()
    {
        if (HasValue("AlloyCodeFontSize")) return;

        // The theme's own small size when it declared one; otherwise the constant below.
        var size = GeneratedSmallFontSizeFallback;
        if (_app.Resources.TryGetValue("AlloyBodyFontSizeSmall", out var themed) && themed is double td)
            size = td;

        _app.Resources["AlloyCodeFontSize"] = size;
    }

    private bool HasValue(string key) =>
        _app.Resources.TryGetValue(key, out var v) && v is not null;

    /// <summary>Last-resort code-font size: the historical small-body size at its default
    /// base size (<c>RoundToHalf(14 * 0.9)</c>). Only reached when the generated resource can't be resolved —
    /// e.g. a headless test host with no ModernTheme loaded — where matching what the real app renders beats a
    /// round number.</summary>
    private const double GeneratedSmallFontSizeFallback = 12.5;

    public IEnumerable<Preset> AvailableThemes => _themes.Themes;

    /// <summary>The variant the user's prefs resolve to right now. Explicit
    /// Light/Dark pin the slot; System defers to the OS.</summary>
    public ThemeVariant ResolveActiveVariant(IAppearanceModel prefs) =>
        ThemeVariantResolver.Resolve(prefs, _app, _logger);

    /// <summary>Injected fallback used when a configured slot points at a missing
    /// theme. Two-stage lookup: the matching-variant default name, then any theme of
    /// that variant in the catalog.</summary>
    private Preset? FallbackForVariant(ThemeVariant variant)
    {
        var bundled = variant == ThemeVariant.Light ? _defaultLightName : _defaultDarkName;
        var pv = variant == ThemeVariant.Light ? PresetVariant.Light : PresetVariant.Dark;
        return _themes.GetTheme(bundled)
            ?? System.Linq.Enumerable.FirstOrDefault(_themes.Themes, t => t.Variant == pv);
    }

    private static string? ExtractDirective(Preset? preset, string directiveKey)
    {
        if (preset is null) return null;
        if (preset.Tokens.TryGetValue(directiveKey, out var token)
            && token.ValueKind == JsonValueKind.String)
        {
            return token.GetString();
        }
        return null;
    }
}

/// <summary>
/// The state handed to <see cref="ThemeService"/>'s pre- and post-apply delegates: the
/// live resource dictionary plus the resolved theme / effective overrides / active
/// variant / source preferences, with a few terse helpers for the common
/// "set-if-absent" and derived-resource patterns an app's theme tail needs.
/// <para>
/// The same type serves both seams, but they see different worlds. In <c>postApply</c>
/// (after user overrides, before <see cref="ThemeService.Applied"/>) the resource
/// dictionary is fully populated and safe to read. In <c>preApply</c> (phase 2) it has
/// just been CLEARED — only <see cref="EffectiveDirective"/>, <see cref="Theme"/>,
/// <see cref="Variant"/> and <see cref="Prefs"/> carry meaning there.
/// </para>
/// </summary>
public sealed class ThemeApplyContext
{
    private readonly ThemeService _service;

    internal ThemeApplyContext(ThemeService service, Application app, Preset? theme,
        IReadOnlyDictionary<string, string> overrides, ThemeVariant variant,
        IAppearanceModel prefs)
    {
        _service = service;
        App = app;
        Theme = theme;
        Overrides = overrides;
        Variant = variant;
        Prefs = prefs;
    }

    /// <summary>The Application whose <see cref="Application.Resources"/> are being
    /// written. Use for variant-aware <see cref="StyledElement.TryGetResource"/>.</summary>
    public Application App { get; }

    /// <summary>The live resource dictionary the tail writes derived values into.</summary>
    public IResourceDictionary Resources => App.Resources;

    /// <summary>The resolved active theme preset (may be null if the catalog is empty).</summary>
    public Preset? Theme { get; }

    /// <summary>The effective per-token overrides (persisted + session).</summary>
    public IReadOnlyDictionary<string, string> Overrides { get; }

    /// <summary>The resolved active variant.</summary>
    public ThemeVariant Variant { get; }

    /// <summary>The preferences this apply is rendering. Carried so a delegate can read
    /// the non-token knobs — <see cref="IAppearanceModel.UseSystemAccent"/> and
    /// <see cref="IAppearanceModel.UseSystemFocusColor"/> — without the app having to
    /// close over its own prefs service just to forward them.</summary>
    public IAppearanceModel Prefs { get; }

    /// <summary>Effective directive value: user override → theme → null.</summary>
    public string? EffectiveDirective(string directiveKey) =>
        ThemeService.EffectiveDirective(Theme, directiveKey, Overrides);

    /// <summary>Publish <paramref name="value"/> at <paramref name="key"/> only when
    /// nothing already wrote it. The safety-net default a sparse theme/override would
    /// otherwise leave collapsing to a CLR zero.</summary>
    public void EnsureDefault(string key, object value)
    {
        if (!Resources.ContainsKey(key))
            Resources[key] = value;
    }

}
