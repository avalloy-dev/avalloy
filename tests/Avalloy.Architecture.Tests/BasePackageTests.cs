using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Text.Json;
using AwesomeAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Avalloy.Architecture.Tests;

/// <summary>
/// Avalloy.Base is the part of the platform a terminal app takes without a desktop: HermitCrab ships as a Native
/// AOT binary on Consolonia with it alone. Before it was split out of Avalloy.Desktop, every restore of such an
/// app pulled the desktop graph and every publish wrote ~19 MB of Skia, HarfBuzz and AvaloniaNative libraries
/// next to the binary, unused. Nothing else stops that coming back: one PackageReference added to Base, or one
/// ProjectReference to Desktop, and every consumer still builds and runs.
/// </summary>
/// <remarks>Base is read as FILES — its restore graph and its built assembly's metadata — not referenced:
/// it is a UI package, and this project's own closure must stay Avalonia-free (<see cref="PlainPackageTests"/>).</remarks>
[TestClass]
public class BasePackageTests
{
    /// <summary>What a desktop pulls in. Prefixes, so a package's native-asset siblings are caught with it.</summary>
    private static readonly string[] Forbidden =
    [
        "Avalonia.Desktop",
        "Avalonia.Native",
        "Avalonia.Skia",
        "Avalonia.Themes.",
        "Avalonia.Win32",
        "Avalonia.X11",
        "Svg.",
        "SkiaSharp",
        "ShimSkiaSharp",
        "HarfBuzzSharp",
        "Microsoft.Extensions.Configuration",
        "Avalloy.Desktop",
        "Avalloy.Editing",
    ];

    private static bool IsForbidden(string name) =>
        Forbidden.Any(f => name.StartsWith(f, StringComparison.OrdinalIgnoreCase));

    [TestMethod]
    public void Base_Restores_No_Desktop_Package()
    {
        // The restore graph is the whole transitive package closure a consumer of Base gets.
        using var assets = JsonDocument.Parse(File.ReadAllText(
            Path.Combine(RepoRoot(), "src", "Avalloy.Base", "obj", "project.assets.json")));
        var packages = assets.RootElement.GetProperty("targets").EnumerateObject()
            .SelectMany(target => target.Value.EnumerateObject())
            .Select(library => library.Name.Split('/')[0])
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        packages.Should().Contain("Avalonia", "the check is only evidence if Base's own graph was read");
        packages.Where(IsForbidden).Should().BeEmpty(
            "Avalloy.Base must restore without the desktop graph: a terminal app on it would ship Skia and the desktop platform");
    }

    [TestMethod]
    public void Base_References_No_Desktop_Assembly()
    {
        // Walk Avalloy's own references transitively, naming the path, so a failure says WHICH reference brought
        // the desktop in. Third-party references are only checked, not followed: their closure is the restore
        // graph above.
        var bin = BuildOutput("Avalloy.Base");
        var offenders = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal) { "Avalloy.Base" };
        var queue = new Queue<(string Name, string Path)>();
        queue.Enqueue(("Avalloy.Base", "Avalloy.Base"));
        var walked = 0;

        while (queue.TryDequeue(out var current))
        {
            walked++;
            foreach (var name in AssemblyReferences(Path.Combine(bin, current.Name + ".dll")))
            {
                var path = $"{current.Path} → {name}";
                if (IsForbidden(name))
                    offenders.Add(path);
                else if (name.StartsWith("Avalloy", StringComparison.Ordinal) && seen.Add(name))
                    queue.Enqueue((name, path));
            }
        }

        walked.Should().BeGreaterThan(1, "Base references Avalloy.Common, so the walk must have followed it");
        offenders.Should().BeEmpty("Avalloy.Base must not reach the desktop through any assembly it references");
    }

    [TestMethod]
    public void The_Bundled_Presets_Are_Embedded_In_Desktop_And_Not_In_Base()
    {
        // Apps load them as PresetCatalog.Load(typeof(AvalloyApp).Assembly, "Avalloy.Presets.themes."), so they
        // must be in Desktop's assembly under those names. PresetCatalog itself lives in Base; the presets stayed.
        var desktop = ManifestResources(Path.Combine(BuildOutput("Avalloy.Desktop"), "Avalloy.Desktop.dll"));

        desktop.Should().Contain([
            "Avalloy.Presets.themes.avalloy-dark.json",
            "Avalloy.Presets.themes.avalloy-light.json",
            "Avalloy.Presets.densities.compact.json",
            "Avalloy.Presets.densities.normal.json",
            "Avalloy.Presets.densities.spacious.json",
        ]);
        ManifestResources(Path.Combine(BuildOutput("Avalloy.Base"), "Avalloy.Base.dll"))
            .Where(r => r.StartsWith("Avalloy.Presets.", StringComparison.Ordinal))
            .Should().BeEmpty("one copy, in Desktop: a second in Base would be a second source of truth");
    }

    private static List<string> AssemblyReferences(string dll)
    {
        using var pe = new PEReader(File.OpenRead(dll));
        var md = pe.GetMetadataReader();
        return md.AssemblyReferences.Select(h => md.GetString(md.GetAssemblyReference(h).Name)).ToList();
    }

    private static List<string> ManifestResources(string dll)
    {
        using var pe = new PEReader(File.OpenRead(dll));
        var md = pe.GetMetadataReader();
        return md.ManifestResources.Select(h => md.GetString(md.GetManifestResource(h).Name)).ToList();
    }

    /// <summary>A src/ project's build output in the configuration this test was built in (the build-order-only
    /// ProjectReferences in this project's csproj make sure it exists).</summary>
    private static string BuildOutput(string project)
    {
        var tfm = new DirectoryInfo(AppContext.BaseDirectory);
        var dir = Path.Combine(RepoRoot(), "src", project, "bin", tfm.Parent!.Name, tfm.Name);
        if (!Directory.Exists(dir)) throw new DirectoryNotFoundException($"{project} is not built at {dir}");
        return dir;
    }

    private static string RepoRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "Avalloy.slnx"))) return dir.FullName;
        }

        throw new InvalidOperationException("Avalloy.slnx not found above " + AppContext.BaseDirectory);
    }
}
