using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalloy.Preferences;

namespace NativeThemeDemo;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        Loaded += (_, _) => RefreshStatus();
    }

    private App? Host => Application.Current as App;

    // Picking a specific theme implies a manual (non-system) variant pin.
    private void OnPickLight(object? sender, PointerPressedEventArgs e) => Pick("Light", dark: false);
    private void OnPickDark(object? sender, PointerPressedEventArgs e) => Pick("Dark", dark: true);
    private void OnPickOcean(object? sender, PointerPressedEventArgs e) => Pick("Ocean", dark: true);

    private void Pick(string id, bool dark)
    {
        if (Host is not { } app) return;
        if (dark) app.Appearance.ThemePresetDark = id;
        else app.Appearance.ThemePresetLight = id;
        app.Appearance.ThemeVariant = dark ? ThemeVariantPreference.Dark : ThemeVariantPreference.Light;
        app.ApplyTheme();
        RefreshStatus();
    }

    private void OnToggleFollow(object? sender, RoutedEventArgs e)
    {
        if (Host is not { } app) return;
        if (FollowSystem.IsChecked == true)
        {
            app.Appearance.ThemeVariant = ThemeVariantPreference.System;
        }
        else
        {
            // Pin to whatever is rendering now so unticking doesn't jump variants.
            var dark = app.EffectiveThemeId is "Dark" or "Ocean";
            app.Appearance.ThemeVariant = dark ? ThemeVariantPreference.Dark : ThemeVariantPreference.Light;
        }
        app.ApplyTheme();
        RefreshStatus();
    }

    private void RefreshStatus()
    {
        if (Host is not { } app) return;
        FollowSystem.IsChecked = app.Appearance.ThemeVariant == ThemeVariantPreference.System;
        StatusText.Text = $"Active: {app.EffectiveThemeId}  ·  variant pref: {app.Appearance.ThemeVariant}";
    }
}
