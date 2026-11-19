using System.Reflection;
using AwesomeAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Avalloy.Architecture.Tests;

/// <summary>
/// The packages a headless host (a CLI, a service, an agent runner) can take without a UI stack must never
/// acquire one. Nothing else enforces that: a single <c>ProjectReference</c> to Avalloy.Desktop — which is how
/// Avalloy.Updates used to get its toasts — brings Avalonia into every consumer, and every one of them still
/// builds and runs, so it goes unnoticed until someone ships a server with a windowing toolkit in it.
/// </summary>
[TestClass]
public class PlainPackageTests
{
    /// <summary>Packages that must stay pure .NET. Keep in step with this project's ProjectReferences.</summary>
    private static readonly string[] PlainPackages =
    [
        "Avalloy.Common",     // the plain floor Base and Desktop build on; also guarded by CommonPackageTests
    ];

    /// <summary>Packages that are Avalonia packages by design.</summary>
    private static readonly string[] UiPackages =
    [
        "Avalloy.Base",       // Avalonia's base package only, no desktop; guarded by BasePackageTests
        "Avalloy.Desktop",    // the shell, Themes' token dictionaries and control themes included
    ];

    /// <summary>Not referenced by anything: a build-time tool, so there is no consumer to protect.</summary>
    private static readonly string[] Tools = ["Avalloy.IconTool"];

    private static bool IsForbidden(string assemblyName) =>
        assemblyName.StartsWith("Avalonia", StringComparison.Ordinal)
        || UiPackages.Contains(assemblyName, StringComparer.Ordinal);

    public static IEnumerable<object[]> Plain => PlainPackages.Select(p => new object[] { p });

    [TestMethod]
    [DynamicData(nameof(Plain))]
    public void A_Plain_Package_Reaches_No_Avalonia_Through_Avalloy_References(string package)
    {
        // Walk Avalloy's own references transitively, naming the path, so a failure says WHICH reference
        // brought the UI in rather than only that something did.
        var offenders = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var queue = new Queue<(Assembly Assembly, string Path)>();
        queue.Enqueue((Assembly.Load(new AssemblyName(package)), package));

        while (queue.TryDequeue(out var current))
        {
            foreach (var reference in current.Assembly.GetReferencedAssemblies())
            {
                var name = reference.Name!;
                var path = $"{current.Path} → {name}";
                if (IsForbidden(name))
                {
                    offenders.Add(path);
                }
                else if (name.StartsWith("Avalloy", StringComparison.Ordinal) && seen.Add(name))
                {
                    queue.Enqueue((Assembly.Load(reference), path));
                }
            }
        }

        offenders.Should().BeEmpty($"{package} is a plain package: a headless host must be able to use it without Avalonia");
    }

    [TestMethod]
    public void Nothing_In_The_Plain_Packages_Dependency_Closure_Is_Avalonia()
    {
        // The reflection walk above only follows Avalloy's own assemblies. A third-party package that pulled in
        // Avalonia would hide behind it — but it would still land in THIS project's dependency closure, which is
        // exactly the plain packages plus the test framework. The host's trusted-assembly list is that closure.
        var tpa = (AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string)
            ?? throw new InvalidOperationException("The host published no trusted-assembly list.");

        var loaded = tpa.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Select(Path.GetFileNameWithoutExtension)
            .ToList();

        loaded.Should().Contain("Avalloy.Common", "the closure is only evidence if the plain packages are in it");
        loaded.Where(n => IsForbidden(n!)).Should().BeEmpty();
    }

    [TestMethod]
    public void Every_Package_In_Src_Is_Classified()
    {
        // A new package has to be put in one of the lists, which is the moment to decide whether it is plain.
        var src = Path.Combine(RepoRoot(), "src");
        var projects = Directory.EnumerateFiles(src, "*.csproj", SearchOption.AllDirectories)
            .Where(p => !p.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"))
            .Select(Path.GetFileNameWithoutExtension)
            .Order(StringComparer.Ordinal)
            .ToList();

        projects.Should().BeEquivalentTo(PlainPackages.Concat(UiPackages).Concat(Tools),
            "every project under src/ is either a plain package (add it here AND as a ProjectReference), "
            + "a UI package, or a tool");
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
