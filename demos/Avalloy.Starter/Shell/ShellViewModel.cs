using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Avalloy.Controls.Sidebar;
using Avalloy.Preferences;
using Avalloy.Text;
using Avalloy.Views;

namespace Avalloy.Starter.Shell;

/// <summary>A row in the AlloySelect showcase (LabelPath/DescriptionPath resolve these by name).</summary>
public sealed record SampleOption(string Label, string Description);

/// <summary>
/// The starter's shell state: the persisted sidebar pin (⌘B) + the little bits of data the
/// gallery content binds. Implements <see cref="ISidebarHost"/> — the seam Avalloy's
/// SidebarDockController drives (reserved-column morph, edge pull, width persistence). This
/// starter has no hover-peek overlay, so Open mirrors Pinned and the peek calls no-op; grow
/// into SidebarPeekBehavior when the sidebar becomes a morphing overlay surface.
/// </summary>
public sealed partial class ShellViewModel : ObservableObject, ISidebarHost
{
    bool ISidebarHost.Pinned => SidebarPinned;
    bool ISidebarHost.Open => SidebarPinned;
    void ISidebarHost.TogglePin() => SidebarPinned = !SidebarPinned;
    void ISidebarHost.PeekOpen() { }
    void ISidebarHost.PeekClose() { }
    bool ISidebarHost.EdgePullAllowed => true;
    double ISidebarHost.SavedDockedWidth
    {
        get => _prefs?.Current.Shell.SidebarWidth ?? 240;
        set { if (_prefs?.Current.Shell is { } shell) shell.SidebarWidth = value; }
    }

    private readonly PreferencesService<StarterPreferences>? _prefs;
    private readonly DateTimeOffset _launchedAt = DateTimeOffset.Now;

    /// <summary>DI supplies the prefs service at runtime; the parameterless design-time path runs
    /// without one (nothing persists in the previewer).</summary>
    public ShellViewModel(PreferencesService<StarterPreferences>? prefs = null)
    {
        _prefs = prefs;
        _sidebarPinned = prefs?.Current.Shell.SidebarPinned ?? true;
    }

    /// <summary>Docked wide vs the narrow rail. Persisted; mutations auto-save. MainWindow watches
    /// this and tells the dock controller to morph the reserved column.</summary>
    [ObservableProperty] private bool _sidebarPinned;

    partial void OnSidebarPinnedChanged(bool value)
    {
        if (_prefs?.Current.Shell is { } shell) shell.SidebarPinned = value;
    }

    // ── Settings (⌘, / the sidebar's Settings row) — Avalloy's PreferencesOverlay on a
    //    two-section PrefsNavigator; the section bodies live in MainWindow.axaml. ────────────────

    [ObservableProperty] private bool _settingsOpen;

    public PrefsNavigator SettingsNav { get; } = BuildSettingsNav();

    private static PrefsNavigator BuildSettingsNav()
    {
        var nav = new PrefsNavigator();
        nav.Add("General", "sidebar pinned rail dock");
        nav.Add("Appearance", "theme light dark follow system");
        return nav;
    }

    public string VersionLine => "Avalloy Starter · dev build";

    private static App? AppHost => Avalonia.Application.Current as App;

    /// <summary>Follow the OS appearance; off = the current variant is pinned.</summary>
    public bool FollowSystem => AppHost?.Prefs.Appearance.ThemeVariant == ThemeVariantPreference.System;

    public bool LightSelected => AppHost?.EffectiveThemeId == "Light";
    public bool DarkSelected => AppHost?.EffectiveThemeId == "Dark";

    [RelayCommand]
    private void ToggleFollowSystem()
    {
        AppHost?.SetFollowSystem(!FollowSystem);
        RefreshAppearance();
    }

    [RelayCommand]
    private void PickVariant(string variant)
    {
        AppHost?.SetVariant(variant == "Light" ? ThemeVariantPreference.Light : ThemeVariantPreference.Dark);
        RefreshAppearance();
    }

    /// <summary>Re-read the appearance-derived rows (also called when settings opens, so the ⌘T
    /// titlebar toggle can't leave the section stale).</summary>
    public void RefreshAppearance()
    {
        OnPropertyChanged(nameof(FollowSystem));
        OnPropertyChanged(nameof(LightSelected));
        OnPropertyChanged(nameof(DarkSelected));
    }

    partial void OnSettingsOpenChanged(bool value)
    {
        if (value) RefreshAppearance();
    }

    // ── Gallery sample data ──────────────────────────────────────────────────────────────────

    /// <summary>AlloyDropDown showcase — bound TwoWay to the popover's IsOpen; pick handlers close it.</summary>
    [ObservableProperty] private bool _exportMenuOpen;

    [ObservableProperty] private SampleOption? _selectedOption;

    public IReadOnlyList<SampleOption> Options { get; } =
    [
        new("Balanced", "The default profile — a sensible middle."),
        new("Fast", "Skips the deep passes; results in seconds."),
        new("Thorough", "Every pass, every check. Slow and complete."),
    ];

    /// <summary>Footer recency line — Avalloy's shared "N min ago" formatter.</summary>
    public string LaunchedAgo => RelativeTime.Format(_launchedAt) is { Length: > 0 } ago ? ago : "just now";
}
