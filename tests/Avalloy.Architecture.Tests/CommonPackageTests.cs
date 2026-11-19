using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Text.Json;
using AwesomeAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Avalloy.Architecture.Tests;

/// <summary>
/// Avalloy.Common is the floor everything else builds on: Base and Desktop reference it, and so does every
/// headless consumer (an app's engine, Avalloy.Secrets, a CLI) that takes it precisely to stay off Avalonia. So it
/// must not merely avoid Avalonia through Avalloy's own references (<see cref="PlainPackageTests"/>), it must bring
/// no Avalonia from anywhere, and no package beyond the logging abstractions the four packages it was made from
/// (Abstractions, Cli, Logging, Icons) already took. A package added here reaches every consumer of the platform.
/// </summary>
/// <remarks>Read as FILES, as <see cref="BasePackageTests"/> does Base: its restore graph and its built assembly's
/// metadata.</remarks>
[TestClass]
public class CommonPackageTests
{
    /// <summary>Everything Common may restore: Microsoft.Extensions.Logging.Abstractions and what it depends on.</summary>
    private static readonly string[] Allowed =
    [
        "Microsoft.Extensions.Logging.Abstractions",
        "Microsoft.Extensions.DependencyInjection.Abstractions",
    ];

    /// <summary>Build-time only: IsAotCompatible brings the trimmer's MSBuild tasks, which never reach a consumer.</summary>
    private static readonly string[] BuildOnly = ["Microsoft.NET.ILLink.Tasks"];

    [TestMethod]
    public void Common_Restores_No_Avalonia_And_Nothing_Beyond_The_Logging_Abstractions()
    {
        using var assets = JsonDocument.Parse(File.ReadAllText(
            Path.Combine(RepoRoot(), "src", "Avalloy.Common", "obj", "project.assets.json")));
        var libraries = assets.RootElement.GetProperty("targets").EnumerateObject()
            .SelectMany(target => target.Value.EnumerateObject())
            .Select(library => (Name: library.Name.Split('/')[0], Type: library.Value.GetProperty("type").GetString()))
            .DistinctBy(l => l.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        libraries.Should().Contain(l => l.Name == "Microsoft.Extensions.Logging.Abstractions",
            "the check is only evidence if Common's own graph was read");
        libraries.Where(l => l.Name.StartsWith("Avalonia", StringComparison.OrdinalIgnoreCase)).Should().BeEmpty(
            "Avalloy.Common must restore without Avalonia: a headless host takes it to stay off the UI stack");
        libraries.Where(l => l.Type == "project").Should().BeEmpty(
            "Common is the floor: every other Avalloy package builds on it, so it references none of them");
        libraries.Select(l => l.Name).Except(BuildOnly).Should().BeEquivalentTo(Allowed,
            "a package added to Avalloy.Common reaches every Avalloy consumer, headless or not");
    }

    [TestMethod]
    public void Common_References_No_Avalonia_Or_Avalloy_Assembly()
    {
        var tfm = new DirectoryInfo(AppContext.BaseDirectory);
        var dll = Path.Combine(RepoRoot(), "src", "Avalloy.Common", "bin", tfm.Parent!.Name, tfm.Name, "Avalloy.Common.dll");
        if (!File.Exists(dll)) throw new FileNotFoundException("Avalloy.Common is not built", dll);

        using var pe = new PEReader(File.OpenRead(dll));
        var md = pe.GetMetadataReader();
        var references = md.AssemblyReferences.Select(h => md.GetString(md.GetAssemblyReference(h).Name)).ToList();

        references.Should().Contain("Microsoft.Extensions.Logging.Abstractions", "the check is only evidence if Common's references were read");
        references.Where(r => r.StartsWith("Avalonia", StringComparison.Ordinal) || r.StartsWith("Avalloy", StringComparison.Ordinal))
            .Should().BeEmpty();
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
