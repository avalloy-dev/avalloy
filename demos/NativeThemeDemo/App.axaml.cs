using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalloy;
using Avalloy.Preferences;
using Avalloy.Theming;
using Avalloy.Themes;
using Microsoft.Extensions.DependencyInjection;

namespace NativeThemeDemo;

/// <summary>
/// Validation sample: a working app on Avalloy.Desktop alone (plumbing and
/// the native theme engine). It inherits the Desktop
/// <see cref="AvalloyApp"/> base, returns a <see cref="NativeThemeController"/>
/// from <see cref="ResolveThemeController"/>, and persists the picked theme via
/// Base's <see cref="PreferencesService{T}"/>. Nothing else in the graph.
/// </summary>
public partial class App : AvalloyApp
{
    private PreferencesService<DemoPreferences>? _prefs;
    private NativeThemeController? _theme;

    // Two neutral templates shipped by Avalloy.Desktop + a custom app-supplied
    // "Ocean" dark theme, proving an app extends the catalog with its own palette.
    private static readonly IReadOnlyList<ThemeDescriptor> ThemeCatalog = new[]
    {
        new ThemeDescriptor("Light", "avares://Avalloy.Desktop/Themes/Template.Light.axaml", false),
        new ThemeDescriptor("Dark",  "avares://Avalloy.Desktop/Themes/Template.Dark.axaml",  true),
        new ThemeDescriptor("Ocean", "avares://NativeThemeDemo/Themes/Demo.Ocean.axaml",              true),
    };

    public IReadOnlyList<ThemeDescriptor> Themes => ThemeCatalog;

    /// <summary>The live, persisted appearance the picker mutates.</summary>
    public AppearancePrefs Appearance => _prefs!.Current.Appearance;

    /// <summary>The theme id rendering right now (variant + OS resolved). Null-safe
    /// before the controller is resolved.</summary>
    public string EffectiveThemeId => _theme?.EffectiveThemeId(Appearance) ?? "Light";

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    protected override void ConfigureServices(IServiceCollection services)
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NativeThemeDemo");
        _prefs = new PreferencesService<DemoPreferences>(dir);
        _prefs.Initialize(); // create dir, load persisted prefs, hook debounced auto-save
        services.AddSingleton(_prefs);
    }

    protected override Window CreateMainWindow() => new MainWindow();

    protected override IThemeController? ResolveThemeController() =>
        _theme = new NativeThemeController(this, ThemeCatalog, defaultLightId: "Light", defaultDarkId: "Dark");

    protected override IAppearanceModel? GetThemeAppearance() => Appearance;

    /// <summary>Re-apply after the picker mutates prefs. Mutations also persist via
    /// the prefs service's debounced save, so the choice survives restart.</summary>
    public void ApplyTheme() => ReapplyTheme();
}
