using System.Diagnostics.CodeAnalysis;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Styling;
using Avalloy.Preferences;
using Avalloy.Theming;

namespace Avalloy.Themes;

/// <summary>One selectable theme: an id, the <c>avares://</c> URI of its
/// hand-authored <see cref="Avalonia.Controls.ResourceDictionary"/>, and whether it
/// is a dark variant (drives <see cref="Application.RequestedThemeVariant"/> so the
/// base FluentTheme's control resources match).</summary>
public sealed record ThemeDescriptor(string Id, string Uri, bool Dark);

/// <summary>
/// Pure-Avalonia <see cref="IThemeController"/> that drives the native theming
/// model: a set of hand-authored merged
/// <see cref="ResourceDictionary"/> themes, one of which is swapped into
/// <see cref="Application.Resources"/> at runtime while
/// <see cref="Application.RequestedThemeVariant"/> is set so FluentTheme's own
/// control resources track Light/Dark. Controls bind theme tokens via
/// <c>{DynamicResource}</c>, so a swap repaints live.
/// <para>
/// Resolution reuses Avalloy.Base's <see cref="IAppearanceModel"/>: the light/dark slots are
/// <see cref="IAppearanceModel.ThemePresetLight"/> / <see cref="IAppearanceModel.ThemePresetDark"/>,
/// and <see cref="IAppearanceModel.ThemeVariant"/> chooses the slot (System defers to
/// the OS). After each apply it raises <see cref="AvalloyTheme.Applied"/> so Desktop's
/// controls refresh.
/// </para>
/// </summary>
public sealed class NativeThemeController : IThemeController
{
    private readonly Application _app;
    private readonly IReadOnlyList<ThemeDescriptor> _themes;
    private readonly string _defaultLightId;
    private readonly string _defaultDarkId;
    private IResourceProvider? _slot;

    /// <param name="app">The running Application whose resources receive the theme.</param>
    /// <param name="themes">The selectable themes (must include the two defaults).</param>
    /// <param name="defaultLightId">Theme id used when the resolved light slot is missing.</param>
    /// <param name="defaultDarkId">Theme id used when the resolved dark slot is missing.</param>
    public NativeThemeController(
        Application app,
        IReadOnlyList<ThemeDescriptor> themes,
        string defaultLightId,
        string defaultDarkId)
    {
        _app = app;
        _themes = themes;
        _defaultLightId = defaultLightId;
        _defaultDarkId = defaultDarkId;
    }

    /// <summary>The registered themes (e.g. to populate a theme picker).</summary>
    public IReadOnlyList<ThemeDescriptor> Themes => _themes;

    /// <summary>The theme id that would render right now for <paramref name="appearance"/>
    /// (variant + OS resolved, falling back to the defaults for an unknown slot).</summary>
    public string EffectiveThemeId(IAppearanceModel appearance)
    {
        var dark = ThemeVariantResolver.Resolve(appearance, _app) == ThemeVariant.Dark;
        var id = dark ? appearance.ThemePresetDark : appearance.ThemePresetLight;
        return _themes.Any(t => t.Id == id) ? id : (dark ? _defaultDarkId : _defaultLightId);
    }

    /// <summary>Resolve + swap the active theme dictionary and set the base Light/Dark
    /// variant. Idempotent — safe to call on every preferences / OS change.</summary>
    [UnconditionalSuppressMessage("Trimming", "IL2026", Justification =
        "A theme is chosen by URI at runtime, so it is loaded by URI. What trimming could remove is the assembly a URI names: "
        + "the bundled templates are in Avalloy.Desktop, which holds this type, and an app's own themes are in an assembly it references.")]
    public void Apply(IAppearanceModel appearance)
    {
        var id = EffectiveThemeId(appearance);
        var theme = _themes.FirstOrDefault(t => t.Id == id) ?? _themes[0];

        var dict = new ResourceInclude((Uri?)null) { Source = new Uri(theme.Uri) };
        if (_slot is not null) _app.Resources.MergedDictionaries.Remove(_slot);
        _app.Resources.MergedDictionaries.Add(dict);
        _slot = dict;

        _app.RequestedThemeVariant = theme.Dark ? ThemeVariant.Dark : ThemeVariant.Light;

        // Fan out to engine-agnostic subscribers (e.g. Desktop's controls that derive
        // class state from the freshly published resources).
        AvalloyTheme.NotifyApplied();
    }
}
