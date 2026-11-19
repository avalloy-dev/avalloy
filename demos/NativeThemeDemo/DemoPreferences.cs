using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Avalloy.Preferences;

namespace NativeThemeDemo;

/// <summary>
/// Root preferences POCO persisted by Avalloy.Base's <see cref="PreferencesService{T}"/>.
/// Holds a single <see cref="AppearancePrefs"/> section (Base's concrete
/// <see cref="IAppearanceModel"/>), which the <c>NativeThemeController</c> reads.
/// Mutating any appearance property triggers a debounced save, so the picked theme
/// + follow-system choice survive restart with zero extra wiring.
/// </summary>
public sealed partial class DemoPreferences : ObservableObject, IPreferencesModel
{
    [ObservableProperty]
    private AppearancePrefs _appearance = new()
    {
        // Default to the neutral templates shipped by Avalloy.Desktop; System
        // variant follows the OS until the user pins Light/Dark in the picker.
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
