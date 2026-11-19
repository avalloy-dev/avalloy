# Getting started

Avalloy is a set of NuGet packages for [Avalonia](https://avaloniaui.net) desktop apps on
.NET 10. One of them makes an app: **Avalloy.Desktop** is the app shell, and it carries the native
theme engine and the neutral templates it ships. It brings **Avalloy.Base** (preferences, the token
cascade, commands and keymaps) and **Avalloy.Common** (plain .NET: logging, `CliRunner`, the shared
contracts) with it. The others are opt-in. Coming from 0.22.0 or earlier? See
[Migrating to 0.23.0](/guide/migrating).

```sh
dotnet add package Avalloy.Desktop
```

Everything restores from nuget.org. There is no private feed and no credential.

## The app class

Derive from `AvalloyApp`. It owns the cross-cutting bootstrap: the DI container, logging with
a console, debug, rolling-file and in-app buffer provider, the command registry and keyboard
dispatcher, the main window, and the theme lifecycle. You override the handful of hooks that
are yours.

```csharp
public partial class App : AvalloyApp
{
    private PreferencesService<MyPreferences>? _prefs;

    private static readonly IReadOnlyList<ThemeDescriptor> ThemeCatalog =
    [
        new("Light", "avares://Avalloy.Desktop/Themes/Template.Light.axaml", Dark: false),
        new("Dark",  "avares://Avalloy.Desktop/Themes/Template.Dark.axaml",  Dark: true),
    ];

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    protected override void ConfigureServices(IServiceCollection services)
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MyApp");
        _prefs = new PreferencesService<MyPreferences>(dir);
        _prefs.Initialize();            // create the dir, load, hook debounced auto-save
        services.AddSingleton(_prefs);
    }

    protected override Window CreateMainWindow() => new MainWindow();

    protected override IThemeController? ResolveThemeController() =>
        new NativeThemeController(this, ThemeCatalog, defaultLightId: "Light", defaultDarkId: "Dark");

    protected override IAppearanceModel? GetThemeAppearance() => _prefs!.Current.Appearance;
}
```

That is a themed app that persists its preferences and follows the OS light/dark switch.
`ResolveThemeController` is the [theme seam](/core/theming): answer it with Avalloy's native
controller, with Desktop's preset cascade, or with an engine of your own.

## App.axaml

The theme engine needs Fluent underneath, the type ramp and control styles from Themes, and
the theme-independent tokens merged in. The per-variant palette is what the controller swaps
at runtime.

```xml
<Application xmlns="https://github.com/avaloniaui"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             x:Class="MyApp.App"
             RequestedThemeVariant="Dark">
  <Application.Styles>
    <FluentTheme />
    <StyleInclude Source="avares://Avalloy.Desktop/Themes/Type.axaml" />
    <StyleInclude Source="avares://Avalloy.Desktop/Themes/ControlStyles.axaml" />
  </Application.Styles>
  <Application.Resources>
    <ResourceDictionary>
      <ResourceDictionary.MergedDictionaries>
        <ResourceInclude Source="avares://Avalloy.Desktop/Themes/Tokens.axaml" />
      </ResourceDictionary.MergedDictionaries>
    </ResourceDictionary>
  </Application.Resources>
</Application>
```

## The preferences model

Preferences are a plain observable object. Embed `AppearancePrefs` and the theme engine reads
it through `IAppearanceModel`; implement `IPreferencesModel` so the service can watch for
mutations without knowing your section layout.

```csharp
public sealed partial class MyPreferences : ObservableObject, IPreferencesModel
{
    [ObservableProperty]
    private AppearancePrefs _appearance = new()
    {
        ThemePresetLight = "Light",
        ThemePresetDark = "Dark",
        ThemeVariant = ThemeVariantPreference.System,
    };

    public void RegisterMutationHandler(PropertyChangedEventHandler handler)
    {
        PropertyChanged += handler;
        Appearance.PropertyChanged += handler;
    }

    public void UnregisterMutationHandler(PropertyChangedEventHandler handler)
    {
        PropertyChanged -= handler;
        Appearance.PropertyChanged -= handler;
    }
}
```

The file is `preferences.json` in the directory you gave the service. Edit it by hand and the
app reloads; see [Preferences](/core/preferences).

## The window

A window that paints into the title bar region, with Avalloy's title bar across the top.
On macOS the traffic lights overlay the far left; on Windows and Linux the caption buttons
sit at the right, and `RightContent` is kept clear of them automatically.

```xml
<Window xmlns="https://github.com/avaloniaui"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        xmlns:tb="using:Avalloy.Controls.TitleBar"
        xmlns:ap="using:Avalloy.AttachedProperties"
        xmlns:icons="using:Avalloy.Icons"
        x:Class="MyApp.MainWindow"
        Background="{DynamicResource Surface.Window}"
        ExtendClientAreaToDecorationsHint="True"
        ap:MacOSTitleBar.IsThick="True">
  <Grid RowDefinitions="44,*">
    <tb:AlloyTitleBar Grid.Row="0" MinHeight="44" Padding="0"
                      Background="{DynamicResource Surface.Window}">
      <TextBlock Text="My App" Classes="secondary"
                 HorizontalAlignment="Center" VerticalAlignment="Center"/>
      <tb:AlloyTitleBar.RightContent>
        <icons:GlyphIcon Glyph="moon" Size="15"/>
      </tb:AlloyTitleBar.RightContent>
    </tb:AlloyTitleBar>
    <!-- your content -->
  </Grid>
</Window>
```

Derive the window from `ShellWindow` instead of `Window` when you want the keyboard
dispatcher wired in: the tunnelled key handler, the command surface for toolbars and the
palette, and the macOS activation plumbing. See [Windows and the title bar](/core/window).

## Where to next

- [The starter app](/guide/starter) is all of the above, plus a sidebar, commands, a splash,
  and settings. Copy it and rename it.
- [Base and Desktop](/core/) is the tour of the app shell.
- [Themes](/themes/) explains the token contract and how to make the templates your brand.
