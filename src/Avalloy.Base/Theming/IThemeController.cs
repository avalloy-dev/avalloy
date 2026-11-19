using Avalloy.Preferences;

namespace Avalloy.Theming;

/// <summary>
/// The theme-apply seam <see cref="AvalloyApp"/> drives. An engine resolves an
/// <see cref="IAppearanceModel"/> (theme slots, variant, overrides) and pushes the
/// result into the running Application's resources. Avalloy owns the lifecycle:
/// one apply at startup and a re-apply on every OS accent / light-dark change.
/// <para>
/// Two implementations ship: <c>ThemeService</c> (in Avalloy.Desktop) drives the
/// preset/override cascade; <c>NativeThemeController</c> (also Avalloy.Desktop) swaps
/// hand-authored merged <c>ResourceDictionary</c> themes. An app bringing its own
/// engine implements this seam instead, and answers it from <c>ResolveThemeController</c>.
/// </para>
/// </summary>
public interface IThemeController
{
    /// <summary>Apply the supplied appearance to the Application's resources.
    /// Read fresh on every call so it always reflects the latest preference state.</summary>
    void Apply(IAppearanceModel appearance);
}
