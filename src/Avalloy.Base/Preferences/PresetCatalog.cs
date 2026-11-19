using System.Reflection;
using System.Text.Json;

namespace Avalloy.Preferences;

/// <summary>
/// Loads named theme and density presets and looks them up by name. Bundled
/// presets ship as embedded resources matching <paramref name="embeddedPrefix"/>
/// (e.g. <c>"MyApp.Presets."</c>). User presets, if a directory is provided,
/// live at <c>{userDir}/{themes|densities}/*.json</c> and override bundled
/// presets with the same name.
/// </summary>
public sealed class PresetCatalog
{
    private readonly Dictionary<(PresetKind, string), Preset> _presets = new(PresetKeyComparer.Instance);

    private sealed class PresetKeyComparer : IEqualityComparer<(PresetKind, string)>
    {
        public static readonly PresetKeyComparer Instance = new();
        public bool Equals((PresetKind, string) x, (PresetKind, string) y) =>
            x.Item1 == y.Item1 && StringComparer.OrdinalIgnoreCase.Equals(x.Item2, y.Item2);
        public int GetHashCode((PresetKind, string) obj) =>
            HashCode.Combine(obj.Item1, StringComparer.OrdinalIgnoreCase.GetHashCode(obj.Item2));
    }

    /// <summary>Empty catalog — useful for tests that only exercise user-override logic.</summary>
    public static PresetCatalog Empty { get; } = new();

    private PresetCatalog() { }

    /// <summary>Load bundled (embedded) presets from <paramref name="bundledAssembly"/>
    /// matching <paramref name="embeddedPrefix"/>, plus any user presets under
    /// <paramref name="userPresetsRoot"/>/<c>themes</c> and <c>densities</c>.
    /// User presets override bundled by name.</summary>
    public static PresetCatalog Load(
        Assembly? bundledAssembly = null,
        string? embeddedPrefix = null,
        string? userPresetsRoot = null)
    {
        var catalog = new PresetCatalog();
        if (bundledAssembly is not null && !string.IsNullOrEmpty(embeddedPrefix))
            catalog.LoadBundled(bundledAssembly, embeddedPrefix);
        if (!string.IsNullOrEmpty(userPresetsRoot))
            catalog.LoadUserDirectory(userPresetsRoot);
        return catalog;
    }

    /// <summary>Layer additional bundled (embedded) presets from another assembly onto
    /// this catalog — e.g. a consuming app adds its own themes on top of Avalloy's
    /// base set. Same override rule as <see cref="Load"/>: a later preset replaces an
    /// earlier one with the same (kind, name). Returns <c>this</c> for fluent chaining.</summary>
    public PresetCatalog AddBundled(Assembly bundledAssembly, string embeddedPrefix)
    {
        if (bundledAssembly is not null && !string.IsNullOrEmpty(embeddedPrefix))
            LoadBundled(bundledAssembly, embeddedPrefix);
        return this;
    }

    public Preset? GetTheme(string? name) => Get(PresetKind.Theme, name);
    public Preset? GetDensity(string? name) => Get(PresetKind.Density, name);

    public IEnumerable<Preset> Themes => _presets.Values.Where(p => p.Kind == PresetKind.Theme);
    public IEnumerable<Preset> Densities => _presets.Values.Where(p => p.Kind == PresetKind.Density);

    private Preset? Get(PresetKind kind, string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        return _presets.TryGetValue((kind, name), out var p) ? p : null;
    }

    private void LoadBundled(Assembly assembly, string embeddedPrefix)
    {
        foreach (var resourceName in assembly.GetManifestResourceNames())
        {
            if (!resourceName.StartsWith(embeddedPrefix, StringComparison.Ordinal) ||
                !resourceName.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                continue;
            using var stream = assembly.GetManifestResourceStream(resourceName);
            if (stream is null) continue;
            using var reader = new StreamReader(stream);
            TryParseAndAdd(reader.ReadToEnd());
        }
    }

    private void LoadUserDirectory(string userPresetsRoot)
    {
        foreach (var sub in new[] { "themes", "densities" })
        {
            var dir = Path.Combine(userPresetsRoot, sub);
            if (!Directory.Exists(dir)) continue;
            foreach (var file in Directory.EnumerateFiles(dir, "*.json"))
            {
                try { TryParseAndAdd(File.ReadAllText(file)); }
                catch { /* malformed user preset — ignore, fall back to bundled */ }
            }
        }
    }

    private void TryParseAndAdd(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return;
        Preset? preset;
        try { preset = ParsePreset(json); }
        catch { return; }
        if (preset is null) return;
        _presets[(preset.Kind, preset.Name)] = preset;
    }

    private static Preset? ParsePreset(string json)
    {
        using var doc = JsonDocument.Parse(json, new JsonDocumentOptions
        {
            CommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
        });
        var root = doc.RootElement;
        if (root.ValueKind != JsonValueKind.Object) return null;

        var name = root.TryGetProperty("name", out var n) ? n.GetString() : null;
        var displayName = root.TryGetProperty("displayName", out var d) ? d.GetString() : name;
        var kindString = root.TryGetProperty("kind", out var k) ? k.GetString() : null;
        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(kindString)) return null;

        if (!TryParseKind(kindString, out var kind)) return null;

        // Theme presets must declare a single variant ("light" or "dark").
        // Density presets are variant-agnostic. The legacy combined-shape
        // (variants: { light: {...}, dark: {...} }) is no longer supported —
        // the loader rejects it so old files don't silently load wrong.
        var variant = PresetVariant.None;
        if (kind == PresetKind.Theme)
        {
            if (root.TryGetProperty("variants", out _))
                return null; // legacy combined-shape — reject
            var variantString = root.TryGetProperty("variant", out var vEl)
                ? vEl.GetString() : null;
            if (!TryParseVariant(variantString, out variant)) return null;
        }

        var tokens = new Dictionary<string, JsonElement>();
        if (root.TryGetProperty("tokens", out var t) && t.ValueKind == JsonValueKind.Object)
            foreach (var prop in t.EnumerateObject())
                tokens[prop.Name] = prop.Value.Clone();

        return new Preset
        {
            Name = name!,
            DisplayName = displayName ?? name!,
            Kind = kind,
            Variant = variant,
            Tokens = tokens,
        };
    }

    private static bool TryParseKind(string s, out PresetKind kind)
    {
        switch (s.ToLowerInvariant())
        {
            case "theme": kind = PresetKind.Theme; return true;
            case "density": kind = PresetKind.Density; return true;
            default: kind = default; return false;
        }
    }

    private static bool TryParseVariant(string? s, out PresetVariant variant)
    {
        switch (s?.ToLowerInvariant())
        {
            case "light": variant = PresetVariant.Light; return true;
            case "dark":  variant = PresetVariant.Dark;  return true;
            default:      variant = PresetVariant.None;  return false;
        }
    }
}
