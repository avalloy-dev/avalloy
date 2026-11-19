// Avalloy.IconTool — the iconpack creation tool.
//
//   iconpack create --id <id> --name <name> --src <svgDir> --out <packDir>
//                   [--duo <duoDir>] [--roles <icons.md>] [--desc <text>]
//                   [--homepage <url>] [--version <v>] [--ns <controlNamespace>]
//                   [--class <Full.ClassName>] [--class-modifier <internal|public>]
//
// Reads   <svgDir>/*.svg          — one file per icon (id = filename without ".svg").
//                                    Layer/variant siblings (<id>.duo.svg, <id>.fill.svg,
//                                    <id>.sm<N>.svg[.fill.svg]) are folded in, not listed.
//         [<duoDir>/<id>.svg]     — optional parallel dir of lit duotone layers (jelly style).
//         [<icons.md>]            — optional curated role table (| **Role** | `rel/path.svg` |),
//                                    baked as extra role-keyed resources (semantic aliases over
//                                    the pack). Paths resolve relative to the manifest's dir.
// Writes  <packDir>/pack.json     — manifest with the authoritative icon list (slugs only).
//         <packDir>/<id>.icons.axaml — baked ResourceDictionary of <IconGlyph> resources.
//
// A thin I/O shell: all SVG→geometry logic lives in Avalloy.Common (Avalloy.Icons.Baking).

using System.Text.RegularExpressions;
using Avalloy.Icons.Baking;

if (args.Length == 0 || args[0] != "create")
{
    Console.Error.WriteLine("usage: iconpack create --id <id> --name <name> --src <svgDir> --out <packDir> " +
                            "[--duo <duoDir>] [--roles <icons.md>] [--desc <text>] [--homepage <url>] [--version <v>] [--ns <ns>] [--class <name>] [--class-modifier <m>]");
    return 1;
}

var opt = ParseArgs(args);
string Require(string key) =>
    opt.TryGetValue(key, out var v) && !string.IsNullOrEmpty(v) ? v
        : throw new ArgumentException($"missing required --{key}");

string id, name, srcDir, outDir;
try
{
    id = Require("id");
    name = Require("name");
    srcDir = Require("src");
    outDir = Require("out");
}
catch (ArgumentException ex)
{
    Console.Error.WriteLine(ex.Message);
    return 1;
}
opt.TryGetValue("duo", out var duoDir);

if (!Directory.Exists(srcDir))
{
    Console.Error.WriteLine($"source dir not found: {srcDir}");
    return 1;
}

var writerOptions = (opt.TryGetValue("ns", out var ns) && !string.IsNullOrEmpty(ns)
    ? new IconPackWriterOptions { ControlNamespace = ns }
    : new IconPackWriterOptions()) with
{
    ClassName = opt.GetValueOrDefault("class"),
    ClassModifier = opt.GetValueOrDefault("class-modifier"),
};

// Discover base icons: every *.svg that isn't a layer/variant sibling.
var icons = new List<KeyValuePair<string, IconGlyphSpec>>();
int warnings = 0;
void Warn(string m) { Console.Error.WriteLine($"  ! {m}"); warnings++; }

foreach (var file in Directory.EnumerateFiles(srcDir, "*.svg")
             .Where(f => !IsLayerFile(Path.GetFileName(f)))
             .OrderBy(f => Path.GetFileName(f), StringComparer.Ordinal))
{
    var slug = Path.GetFileNameWithoutExtension(file);
    var svg = File.ReadAllText(file);
    var active = FindActiveLayer(srcDir, duoDir, slug);
    var small = FindSmallVariant(srcDir, slug);
    var spec = IconBaker.Bake(svg, active, small, m => Warn($"{slug}: {m}"));
    icons.Add(new(slug, spec));
}

// Optional curated role aliases (an app's icons.md): bake extra role-keyed
// entries. Roles resolve relative to the manifest's own directory, with the same
// layer/small-variant sibling inference. Role keys are NOT added to pack.json's
// icons[] (the picker enumerates slugs) — they're extra resources in the dictionary.
var roleEntries = new List<KeyValuePair<string, IconGlyphSpec>>();
if (EmptyToNull(opt.GetValueOrDefault("roles")) is { } rolesFile)
{
    if (!File.Exists(rolesFile))
    {
        Console.Error.WriteLine($"roles manifest not found: {rolesFile}");
        return 1;
    }
    var baseDir = Path.GetDirectoryName(Path.GetFullPath(rolesFile))!;
    foreach (Match rm in Regex.Matches(File.ReadAllText(rolesFile),
                 @"\|\s*\*\*(\w+)\*\*\s*\|\s*`([\w./\-]+\.svg)`"))
    {
        var role = rm.Groups[1].Value;
        var full = Path.Combine(baseDir, rm.Groups[2].Value);
        if (!File.Exists(full)) { Warn($"role {role}: '{rm.Groups[2].Value}' not found — skipped"); continue; }
        var dir = Path.GetDirectoryName(full)!;
        var slug = Path.GetFileNameWithoutExtension(full);
        var active = FindActiveLayer(dir, null, slug);
        var small = FindSmallVariant(dir, slug);
        roleEntries.Add(new(role, IconBaker.Bake(File.ReadAllText(full), active, small, m => Warn($"{role}: {m}"))));
    }
}

if (icons.Count == 0 && roleEntries.Count == 0)
{
    Console.Error.WriteLine($"no icons found in {srcDir}");
    return 1;
}

Directory.CreateDirectory(outDir);
var axamlPath = Path.Combine(outDir, $"{id}.icons.axaml");
var jsonPath = Path.Combine(outDir, "pack.json");
// Slugs + roles share the one baked dictionary (disjoint keyspaces); pack.json
// lists slugs only.
File.WriteAllText(axamlPath, IconPackWriter.WriteResourceDictionary(icons.Concat(roleEntries), writerOptions));
File.WriteAllText(jsonPath, IconPackWriter.WritePackJson(
    id, name, icons.Select(i => i.Key),
    description: EmptyToNull(opt.GetValueOrDefault("desc")),
    homepage: EmptyToNull(opt.GetValueOrDefault("homepage")),
    version: EmptyToNull(opt.GetValueOrDefault("version"))));

Console.WriteLine($"Baked {icons.Count} icons" +
                  (roleEntries.Count > 0 ? $" + {roleEntries.Count} roles" : "") +
                  $" → {axamlPath} (+ pack.json)" +
                  (warnings > 0 ? $"  [{warnings} warning(s)]" : ""));
return 0;

// ── discovery helpers (I/O; the pure flattening is all in IconBaker) ─────────────

// A file is a layer/variant sibling (folded into its base icon, not a standalone icon)
// when its stem ends in ".duo"/".fill", or is a ".sm<N>" optical variant (with optional
// ".fill"). e.g. "star.duo.svg", "stitch.sm20.svg", "stitch.sm20.fill.svg".
static bool IsLayerFile(string fileName)
{
    var stem = fileName[..^".svg".Length];
    if (stem.EndsWith(".duo") || stem.EndsWith(".fill")) return true;
    return Regex.IsMatch(stem, @"\.sm\d*$");
}

// The lit/active layer for a slug: a parallel --duo dir file wins (jelly-pack style),
// then a same-dir ".duo.svg" (jelly), then a ".fill.svg" (bespoke). Null when none.
static string? FindActiveLayer(string srcDir, string? duoDir, string slug)
{
    if (!string.IsNullOrEmpty(duoDir))
    {
        var parallel = Path.Combine(duoDir, slug + ".svg");
        if (File.Exists(parallel)) return File.ReadAllText(parallel);
    }
    foreach (var suffix in new[] { ".duo.svg", ".fill.svg" })
    {
        var sibling = Path.Combine(srcDir, slug + suffix);
        if (File.Exists(sibling)) return File.ReadAllText(sibling);
    }
    return null;
}

// A small optical-size variant "<slug>.sm<N>.svg" (bare ".sm" → max 16), with an optional
// "<slug>.sm<N>.fill.svg" active layer.
static SmallVariantInput? FindSmallVariant(string srcDir, string slug)
{
    foreach (var f in Directory.EnumerateFiles(srcDir, slug + ".sm*.svg")
                 .OrderBy(x => x, StringComparer.Ordinal))
    {
        var fn = Path.GetFileName(f);
        if (fn.EndsWith(".fill.svg")) continue; // that's the small active layer
        var m = Regex.Match(fn, @"\.sm(\d+)?\.svg$");
        if (!m.Success) continue;
        var max = m.Groups[1].Success ? double.Parse(m.Groups[1].Value) : 16;
        var fillPath = f[..^".svg".Length] + ".fill.svg";
        var active = File.Exists(fillPath) ? File.ReadAllText(fillPath) : null;
        return new SmallVariantInput(File.ReadAllText(f), max, active);
    }
    return null;
}

static string? EmptyToNull(string? s) => string.IsNullOrEmpty(s) ? null : s;

static Dictionary<string, string> ParseArgs(string[] args)
{
    var d = new Dictionary<string, string>(StringComparer.Ordinal);
    for (int i = 1; i < args.Length; i++)
    {
        if (!args[i].StartsWith("--")) continue;
        var key = args[i][2..];
        var val = i + 1 < args.Length && !args[i + 1].StartsWith("--") ? args[++i] : "";
        d[key] = val;
    }
    return d;
}
