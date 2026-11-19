using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Avalloy;
using Avalloy.Preferences;

namespace Avalloy.Starter;

/// <summary>
/// Root preferences POCO persisted by Avalloy.Base's <see cref="PreferencesService{T}"/>: the appearance
/// section the theme controller reads, the window geometry <c>BindGeometry</c> restores/saves, and
/// the shell's own bits (sidebar pin). Mutating any tracked property triggers a debounced save.
/// </summary>
public sealed partial class StarterPreferences : ObservableObject, IPreferencesModel
{
    [ObservableProperty]
    private AppearancePrefs _appearance = new()
    {
        ThemePresetLight = "Light",
        ThemePresetDark = "Dark",
        ThemeVariant = ThemeVariantPreference.System,
    };

    [ObservableProperty] private WindowGeometryPrefs _window = new();
    [ObservableProperty] private StarterShellPrefs _shell = new();

    public void RegisterMutationHandler(PropertyChangedEventHandler handler)
    {
        PropertyChanged += handler;
        Appearance.PropertyChanged += handler;
        Window.PropertyChanged += handler;
        Shell.PropertyChanged += handler;
    }

    public void UnregisterMutationHandler(PropertyChangedEventHandler handler)
    {
        PropertyChanged -= handler;
        Appearance.PropertyChanged -= handler;
        Window.PropertyChanged -= handler;
        Shell.PropertyChanged -= handler;
    }
}

/// <summary>Persisted main-window geometry (Avalloy's <see cref="IMaximizableWindowGeometry"/> —
/// null position = let the OS place the first launch).</summary>
public sealed partial class WindowGeometryPrefs : ObservableObject, IMaximizableWindowGeometry
{
    [ObservableProperty] private double _width = 1180;
    [ObservableProperty] private double _height = 760;
    [ObservableProperty] private double? _positionX;
    [ObservableProperty] private double? _positionY;
    [ObservableProperty] private bool _isMaximized;
}

/// <summary>Shell layout choices that survive restart.</summary>
public sealed partial class StarterShellPrefs : ObservableObject
{
    /// <summary>⌘B — true = the sidebar is docked wide; false = the narrow rail.</summary>
    [ObservableProperty] private bool _sidebarPinned = true;

    /// <summary>The docked sidebar width (edge-pull drag resizes it; SidebarDockController clamps).</summary>
    [ObservableProperty] private double _sidebarWidth = 240;
}
