using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalloy.Starter.Shell;
using Avalloy;
using Avalloy.Commands;
using Avalloy.Preferences;
using Avalloy.Theming;
using Avalloy.Themes;
using Microsoft.Extensions.DependencyInjection;

namespace Avalloy.Starter;

/// <summary>
/// The starter shell app: AvalloyApp bootstrap + NativeThemeController on the neutral
/// templates + the command stack. Copy, rename, and replace the theme catalog with your own
/// palette dictionaries (copies of Template.Light/Dark with your brand values) as the app
/// grows an identity.
/// </summary>
public partial class App : AvalloyApp
{
    private PreferencesService<StarterPreferences>? _prefs;
    private NativeThemeController? _theme;

    private static readonly IReadOnlyList<ThemeDescriptor> ThemeCatalog = new[]
    {
        new ThemeDescriptor("Light", "avares://Avalloy.Desktop/Themes/Template.Light.axaml", false),
        new ThemeDescriptor("Dark",  "avares://Avalloy.Desktop/Themes/Template.Dark.axaml",  true),
    };

    /// <summary>The live, persisted preferences (appearance + window geometry + shell bits).</summary>
    public StarterPreferences Prefs => _prefs!.Current;

    /// <summary>Synchronous flush — window-close geometry writes pass this to BindGeometry.</summary>
    public void SavePrefsNow() => _prefs?.SaveNow();

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    /// <summary>Prefs are needed twice, first-call-wins: the splash path reads the appearance BEFORE
    /// <see cref="ConfigureServices"/> runs (theme early-apply), and the classic path builds them here.</summary>
    private void EnsurePrefs()
    {
        if (_prefs is not null) return;
        _prefs = new PreferencesService<StarterPreferences>(
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Avalloy.Starter"));
        _prefs.Initialize();   // create dir, load persisted prefs, hook debounced auto-save
    }

    // ── Launch splash (demo of AvalloyApp's deferred launch) ─────────────────────────────────
    // Opting in is one override; the fake phases below stand in for real boot work (opening stores,
    // cloud sync, engine spawn). Delete these two overrides for the classic synchronous launch.

    protected override Window? CreateSplashWindow()
    {
        EnsurePrefs(); // so the theme early-apply can read the saved appearance
        return new Controls.Splash.AlloySplashWindow { AppName = "Avalloy Starter" };
    }

    protected override async Task PrepareLaunchAsync(IProgress<string> progress)
    {
        progress.Report("Warming up…");
        await Task.Delay(600);
        progress.Report("Pretending to sync — 12.4 MB");
        await Task.Delay(900);
        progress.Report("Starting the shell…");
        await Task.Delay(400);
    }

    protected override void ConfigureServices(IServiceCollection services)
    {
        EnsurePrefs();
        services.AddSingleton(_prefs!);
        services.AddSingleton<ShellViewModel>();
        services.AddSingleton<MainWindow>();

        // The command stack: registration order matters only to command-listing surfaces
        // (a palette); gestures bind via BuildKeymap below.
        Commands.Register<ToggleSidebarPinCommand>();
        Commands.Register<ToggleThemeCommand>();
        Commands.Register<OpenSettingsCommand>();
        Commands.Register<QuitCommand>();
        // Avalloy's log viewer window (Desktop's commands aren't assembly-scanned).
        Commands.Register<ShowLogViewerCommand>();
    }

    protected override IReadOnlyDictionary<string, IReadOnlyList<string>> BuildKeymap() =>
        StarterCommands.DefaultKeymap;

    protected override Window CreateMainWindow() => Services.GetRequiredService<MainWindow>();

    protected override IThemeController? ResolveThemeController() =>
        _theme = new NativeThemeController(this, ThemeCatalog, defaultLightId: "Light", defaultDarkId: "Dark");

    protected override IAppearanceModel? GetThemeAppearance() => Prefs.Appearance;

    /// <summary>The theme id actually rendering right now (variant + OS resolved).</summary>
    public string EffectiveThemeId => _theme?.EffectiveThemeId(Prefs.Appearance) ?? "Dark";

    /// <summary>⌘T / titlebar moon-sun: pin the variant to the opposite of what's rendering now.
    /// The mutation persists via the prefs service's debounced save.</summary>
    public void ToggleTheme() =>
        SetVariant(EffectiveThemeId is "Light" ? ThemeVariantPreference.Dark : ThemeVariantPreference.Light);

    /// <summary>Pin (or un-pin, via System) the appearance and re-apply. Persists via debounced save.</summary>
    public void SetVariant(ThemeVariantPreference variant)
    {
        Prefs.Appearance.ThemeVariant = variant;
        ReapplyTheme();
    }

    /// <summary>Follow the OS appearance (on) or pin whatever is rendering right now (off).</summary>
    public void SetFollowSystem(bool follow) =>
        SetVariant(follow ? ThemeVariantPreference.System
            : EffectiveThemeId is "Light" ? ThemeVariantPreference.Light : ThemeVariantPreference.Dark);
}
