namespace Avalloy.Icons;

/// <summary>
/// Avalloy's contribution to the icon-pack system: the baseline Lucide pack
/// (shipped inside Avalloy.dll) plus a helper that assembles the
/// default <see cref="IconResolverOptions"/> every AvalloyApp uses.
/// </summary>
public static class AvalloyIconPacks
{
    /// <summary>Lucide (ISC) — bundled in Avalloy.Desktop.dll so every
    /// AvalloyApp has icons day one. Addressable as
    /// <c>avares://Avalloy.Desktop/Assets/iconpacks/lucide/{icon}.svg</c>.</summary>
    public static IconPackSource Lucide { get; } = new()
    {
        PacksBaseUri = "avares://Avalloy.Desktop/Assets/iconpacks/",
        PackIds = new[] { "lucide" },
        CompiledResources = id => id == "lucide" ? new LucideIcons() : null,
    };

    /// <summary>Default resolver options: Avalloy's baseline Lucide source plus
    /// any extra app-bundled sources, with user packs under
    /// <c>{LocalApplicationData}/{appDataFolder}/iconpacks</c>.</summary>
    /// <param name="appDataFolder">App folder under LocalApplicationData (e.g.
    /// "Alloy"). Typically the entry assembly name.</param>
    /// <param name="appSources">Extra pack sources the app bundles beyond the baseline.</param>
    /// <param name="defaultPackId">The pack selected at startup.</param>
    public static IconResolverOptions DefaultOptions(
        string appDataFolder,
        IEnumerable<IconPackSource>? appSources = null,
        string defaultPackId = "lucide") =>
        DefaultOptions(AppPaths.ForApp(appDataFolder), appSources, defaultPackId);

    /// <summary>As above, with user packs under <c>{paths.DataDir}/iconpacks</c>.</summary>
    public static IconResolverOptions DefaultOptions(
        AppPaths paths,
        IEnumerable<IconPackSource>? appSources = null,
        string defaultPackId = "lucide") => new()
    {
        BundledSources = new[] { Lucide }.Concat(appSources ?? Enumerable.Empty<IconPackSource>()).ToList(),
        DefaultPackId = defaultPackId,
        UserPacksRoot = Path.Combine(paths.DataDir, "iconpacks"),
    };
}
