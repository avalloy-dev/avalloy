using Avalonia;
using Avalonia.Platform;
using Avalonia.Styling;
using Avalloy.Preferences;
using Microsoft.Extensions.Logging;

namespace Avalloy.Theming;

/// <summary>
/// Engine-agnostic resolution of the effective <see cref="ThemeVariant"/> from an
/// <see cref="IAppearanceModel"/>: explicit Light/Dark pin the slot; System defers to
/// the OS via <c>PlatformSettings.GetColorValues()</c>. Both theme engines — the
/// shell <c>ThemeService</c> and the native <c>NativeThemeController</c> — call this
/// so the System-defer policy and the OS probe live in exactly one place and can't
/// drift. Degrades to Dark when the host can't be queried (headless tests, broken
/// plumbing).
/// </summary>
public static class ThemeVariantResolver
{
    /// <summary>The variant the prefs resolve to right now. Explicit Light/Dark pin
    /// the slot; System defers to <see cref="ResolveSystemVariant"/>.</summary>
    public static ThemeVariant Resolve(IAppearanceModel prefs, Application app, ILogger? logger = null) =>
        prefs.ThemeVariant switch
        {
            ThemeVariantPreference.Light => ThemeVariant.Light,
            ThemeVariantPreference.Dark => ThemeVariant.Dark,
            _ => ResolveSystemVariant(app, logger),
        };

    /// <summary>The OS's current light/dark, via Avalonia's <c>PlatformSettings</c>.
    /// Falls back to Dark when the host can't be queried.</summary>
    public static ThemeVariant ResolveSystemVariant(Application app, ILogger? logger = null)
    {
        try
        {
            var settings = app.PlatformSettings;
            if (settings is not null)
                return settings.GetColorValues().ThemeVariant == PlatformThemeVariant.Light
                    ? ThemeVariant.Light
                    : ThemeVariant.Dark;
        }
        catch (Exception ex)
        {
            logger?.LogDebug(ex, "PlatformSettings unavailable; defaulting to Dark");
        }
        return ThemeVariant.Dark;
    }
}
