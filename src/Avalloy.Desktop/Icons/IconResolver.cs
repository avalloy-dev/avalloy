using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalloy.Icons.Baking;

namespace Avalloy.Icons;

/// <summary>One assembly's worth of bundled packs. Avalloy contributes its
/// baseline source (see <see cref="AvalloyIconPacks.Lucide"/>); apps add their
/// own. Sources are discovered in order and later sources override earlier ones by
/// pack id, so an app can ship its own <c>lucide</c> over Avalloy's baseline;
/// user-installed packs override all bundled.</summary>
public sealed record IconPackSource
{
    /// <summary>avares folder holding this source's packs, ending in '/'. Each pack
    /// lives at <c>{PacksBaseUri}{packId}/</c>.
    /// e.g. <c>avares://Avalloy.Desktop/Assets/iconpacks/</c>.</summary>
    public required string PacksBaseUri { get; init; }

    /// <summary>Pack ids compiled into the assembly as AvaloniaResource. AssetLoader
    /// can't enumerate avares folders, so bundled packs are listed explicitly.</summary>
    public required IReadOnlyList<string> PackIds { get; init; }

    /// <summary>The owning assembly's avares root (e.g. <c>avares://Avalloy</c>),
    /// derived from <see cref="PacksBaseUri"/>'s host. Used as the base URI for
    /// <see cref="Avalonia.Platform.AssetLoader.GetAssets"/> so the correct assembly
    /// manifest is enumerated (the single-argument form sometimes gets it wrong).</summary>
    public string AssemblyAvaresRoot
    {
        get { var u = new Uri(PacksBaseUri); return $"{u.Scheme}://{u.Host}"; }
    }

    /// <summary>Optional: builds a pack's baked dictionary from its compiled class, e.g.
    /// <c>id =&gt; id == "mine" ? new MineIcons() : null</c>, instead of loading
    /// <c>{id}.icons.axaml</c> by URI at runtime. A pack baked with a <c>Class</c> has one.
    /// Trimmed and Native AOT apps need this for every pack they bundle: loading XAML by URI
    /// is something the trimmer cannot follow, and a pack whose dictionary was trimmed away
    /// shows no icons. Null, or a null result, loads by URI.</summary>
    public Func<string, ResourceDictionary?>? CompiledResources { get; init; }
}

/// <summary>
/// Configuration for <see cref="IconResolver"/>. The host app supplies its own
/// asset locations so the resolver carries no app-specific paths.
/// </summary>
public sealed record IconResolverOptions
{
    /// <summary>Bundled pack sources, in discovery order. Avalloy's baseline source
    /// goes first; app sources follow. Later sources override earlier ones by id.</summary>
    public IReadOnlyList<IconPackSource> BundledSources { get; init; } = Array.Empty<IconPackSource>();

    /// <summary>The pack selected by default (and the fallback target). If it isn't
    /// discovered, the first available pack wins.</summary>
    public string DefaultPackId { get; init; } = "";

    /// <summary>Directory holding user-installed packs — one subfolder per pack, each
    /// with a <c>pack.json</c>. May not exist; absence simply means no user packs.</summary>
    public required string UserPacksRoot { get; init; }
}

/// <summary>
/// Discovers icon packs (bundled via AvaloniaResource + user-installed), exposes the
/// current active pack, and resolves icon IDs to URIs that the SVG renderer can load.
/// <para>
/// Icon ID = SVG filename without extension (e.g., "arrow-up"). Packs are expected to
/// follow the Lucide convention: one SVG file per icon, named uniformly across packs so
/// swapping packs Just Works for the same command set.
/// </para>
/// </summary>
public sealed class IconResolver
{
    private readonly IconResolverOptions _options;
    private readonly Dictionary<string, IconPack> _packs;
    private string _activePackId;

    public IconResolver(IconResolverOptions options)
    {
        _options = options;
        _activePackId = options.DefaultPackId;
        _packs = DiscoverPacks();
        // The default pack ships embedded; if it ever goes missing fall back to the
        // first discovered pack so the toolbar still renders something.
        if (!_packs.ContainsKey(_activePackId) && _packs.Count > 0)
            _activePackId = _packs.Keys.First();
    }

    /// <summary>All discovered packs, bundled and user-installed. Iteration order is
    /// bundled-first, then user packs alphabetically.</summary>
    public IReadOnlyCollection<IconPack> AvailablePacks => _packs.Values;

    /// <summary>Currently selected pack ID. Set via <see cref="SetActivePack"/>.</summary>
    public string ActivePackId => _activePackId;

    public IconPack? ActivePack => _packs.TryGetValue(_activePackId, out var p) ? p : null;

    /// <summary>Raised when <see cref="SetActivePack"/> actually changes the active
    /// pack. Consumers that resolve icon URIs lazily (e.g. computed VM properties)
    /// subscribe to re-raise their own change notifications so bound icons repaint.</summary>
    public event EventHandler? ActivePackChanged;

    /// <summary>Switch the active pack. No-op when the id is unknown or already
    /// active; otherwise updates and raises <see cref="ActivePackChanged"/>.</summary>
    public void SetActivePack(string packId)
    {
        if (!_packs.ContainsKey(packId) || _activePackId == packId) return;
        _activePackId = packId;
        ActivePackChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Return a URI string the SVG renderer can load, or null if the active
    /// pack doesn't have an icon with this ID. Always prefer the active pack; we don't
    /// fall back across packs (would mask missing icons).</summary>
    public string? Resolve(string iconId)
    {
        if (string.IsNullOrEmpty(iconId)) return null;
        var pack = ActivePack;
        if (pack is null || !pack.AvailableIcons.Contains(iconId)) return null;
        return pack.ResolveIconUri(iconId);
    }

    /// <summary>Build the active pack's baked <c>IconGlyph</c> resource dictionary — the
    /// one <c>AvalloyApp</c> merges into <c>Application.Resources</c> so the
    /// <c>GlyphIcon</c> control can resolve <c>Icon.&lt;id&gt;</c>. Null when the active
    /// pack has no baked form (or loading failed). Called on the UI thread (touches
    /// Avalonia resources); pack switching rebuilds and re-swaps it.</summary>
    public ResourceDictionary? BuildActivePackResources() => ActivePack?.BuildResources?.Invoke();

    /// <summary>Load a bundled pack's pre-baked <c>.icons.axaml</c> from its avares URI — for
    /// packs whose source gives no <see cref="IconPackSource.CompiledResources"/>.</summary>
    [UnconditionalSuppressMessage("Trimming", "IL2026", Justification =
        "The fallback for packs without a compiled dictionary; IconPackSource.CompiledResources documents that trimmed apps need one.")]
    private static ResourceDictionary? LoadBakedDictionary(string bakedUri)
    {
        try
        {
            return AvaloniaXamlLoader.Load(new Uri(bakedUri)) as ResourceDictionary;
        }
        catch
        {
            // Missing/malformed baked dictionary — the pack simply renders nothing
            // rather than throwing during startup.
            return null;
        }
    }

    /// <summary>Bake a user-installed pack's raw SVGs into an in-memory
    /// <c>IconGlyph</c> dictionary, keyed <c>Icon.&lt;id&gt;</c> to match the control's
    /// lookup. Base-SVG only (layer/small-variant sibling conventions are a bundled-pack
    /// authoring concern); a glyph that fails to bake is skipped, not fatal.</summary>
    private static ResourceDictionary MaterializeUserPack(string packDir, IReadOnlyCollection<string> iconIds)
    {
        var dict = new ResourceDictionary();
        foreach (var id in iconIds)
        {
            var path = Path.Combine(packDir, id + ".svg");
            if (!File.Exists(path)) continue;
            try
            {
                var spec = IconBaker.Bake(File.ReadAllText(path));
                dict["Icon." + id] = IconGlyphMaterializer.ToIconGlyph(spec);
            }
            catch { /* skip an unbakeable glyph */ }
        }
        return dict;
    }

    private Dictionary<string, IconPack> DiscoverPacks()
    {
        var result = new Dictionary<string, IconPack>(StringComparer.OrdinalIgnoreCase);

        foreach (var bundled in DiscoverBundledPacks())
            result[bundled.Id] = bundled; // later source overrides earlier with same id

        foreach (var user in DiscoverUserPacks())
            result[user.Id] = user; // user overrides bundled with same id

        return result;
    }

    private IEnumerable<IconPack> DiscoverBundledPacks()
    {
        // Avalonia's AssetLoader doesn't enumerate avares:// folders, so each source's
        // pack list is supplied via options. Bundled packs are part of the build, not
        // user-discoverable, so listing them once at construction is fine.
        foreach (var source in _options.BundledSources)
            foreach (var id in source.PackIds)
            {
                var pack = TryLoadBundledPack(source, id);
                if (pack is not null) yield return pack;
            }
    }

    private IconPack? TryLoadBundledPack(IconPackSource source, string packId)
    {
        try
        {
            var manifest = LoadBundledManifest(source, packId);
            if (manifest is null) return null;
            // Prefer enumeration via AssetLoader so adding/removing an SVG file is the
            // only step needed. Fall back to the manifest's explicit `icons` array when
            // enumeration returns nothing (older builds, packaging quirks).
            var icons = EnumerateBundledIcons(source, packId);
            if (icons.Count == 0 && manifest.Icons is { Count: > 0 })
                icons = new HashSet<string>(manifest.Icons, StringComparer.OrdinalIgnoreCase);
            // The pre-baked IconGlyph dictionary sits beside the raw SVGs, named by
            // the pack folder id (matching what the icon tool writes).
            var bakedUri = $"{source.PacksBaseUri}{packId}/{packId}.icons.axaml";
            return new IconPack(
                Id: string.IsNullOrEmpty(manifest.Id) ? packId : manifest.Id,
                DisplayName: string.IsNullOrEmpty(manifest.Name) ? packId : manifest.Name,
                Description: manifest.Description,
                Homepage: manifest.Homepage,
                ResolveIconUri: id => $"{source.PacksBaseUri}{packId}/{id}.svg",
                AvailableIcons: icons,
                BuildResources: () => source.CompiledResources?.Invoke(packId) ?? LoadBakedDictionary(bakedUri));
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Walk the assembly's AvaloniaResource manifest for *.svg files under the
    /// pack's folder. Avalonia 12 exposes this via <c>AssetLoader.GetAssets(uri, baseUri)</c>
    /// — the base-URI form disambiguates which assembly's manifest to look at, which the
    /// single-argument form sometimes gets wrong.</summary>
    private static HashSet<string> EnumerateBundledIcons(IconPackSource source, string packId)
    {
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            var baseUri = new Uri(source.AssemblyAvaresRoot);
            var folder = new Uri($"{source.PacksBaseUri}{packId}/");
            foreach (var uri in Avalonia.Platform.AssetLoader.GetAssets(folder, baseUri))
            {
                var leaf = Path.GetFileName(uri.AbsolutePath);
                if (!leaf.EndsWith(".svg", StringComparison.OrdinalIgnoreCase)) continue;
                ids.Add(leaf[..^".svg".Length]);
            }
        }
        catch
        {
            // Enumeration failed (uncommon — fall through to the manifest list).
        }
        return ids;
    }

    private static IconPackManifest? LoadBundledManifest(IconPackSource source, string packId)
    {
        var uri = new Uri($"{source.PacksBaseUri}{packId}/pack.json");
        using var stream = Avalonia.Platform.AssetLoader.Open(uri);
        return JsonSerializer.Deserialize(stream, IconJsonContext.Default.IconPackManifest);
    }

    private IEnumerable<IconPack> DiscoverUserPacks()
    {
        var root = _options.UserPacksRoot;
        if (string.IsNullOrEmpty(root) || !Directory.Exists(root)) yield break;
        foreach (var dir in Directory.EnumerateDirectories(root).OrderBy(d => d, StringComparer.OrdinalIgnoreCase))
        {
            IconPack? pack = null;
            try
            {
                var manifestPath = Path.Combine(dir, "pack.json");
                if (!File.Exists(manifestPath)) continue;
                var manifest = JsonSerializer.Deserialize(File.ReadAllText(manifestPath), IconJsonContext.Default.IconPackManifest);
                if (manifest is null || string.IsNullOrWhiteSpace(manifest.Id)) continue;
                var packDir = dir;
                var icons = Directory.EnumerateFiles(packDir, "*.svg")
                    .Select(p => Path.GetFileNameWithoutExtension(p)!)
                    .Where(n => !string.IsNullOrEmpty(n))
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);
                pack = new IconPack(
                    Id: manifest.Id,
                    DisplayName: string.IsNullOrEmpty(manifest.Name) ? manifest.Id : manifest.Name,
                    Description: manifest.Description,
                    Homepage: manifest.Homepage,
                    ResolveIconUri: id => new Uri(Path.Combine(packDir, id + ".svg")).AbsoluteUri,
                    AvailableIcons: icons,
                    BuildResources: () => MaterializeUserPack(packDir, icons));
            }
            catch { /* skip malformed packs */ }
            if (pack is not null) yield return pack;
        }
    }
}
