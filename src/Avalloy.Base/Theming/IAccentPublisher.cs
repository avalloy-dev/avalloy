using Avalonia;
using Avalonia.Styling;

namespace Avalloy.Theming;

/// <summary>
/// The accent-publishing seam <see cref="AvalloyApp"/> drives. Resolves the
/// current theme's canonical accent color and publishes it as the app's
/// <c>{Prefix}AccentBrush</c> / <c>{Prefix}AccentColor</c> /
/// <c>{Prefix}AccentSelectionBrush</c> resources so XAML can bind
/// <c>{DynamicResource AppAccentBrush}</c>. Avalloy calls
/// <see cref="Refresh"/> at startup, on theme-variant flip, and on OS accent change.
/// <para>
/// An app may ship a publisher that reads the resolved
/// ModernTheme ramp. Native-theme apps typically need no publisher (their theme
/// dictionaries define accent tokens directly), so the seam is optional — a null
/// publisher leaves the app accent-only / token-driven.
/// </para>
/// </summary>
public interface IAccentPublisher
{
    /// <summary>Re-resolve and republish the accent resources on <paramref name="app"/>
    /// for the given active <paramref name="variant"/>.</summary>
    void Refresh(Application app, ThemeVariant variant);
}
