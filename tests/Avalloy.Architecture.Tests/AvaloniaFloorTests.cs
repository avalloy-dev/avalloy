using System.Xml.Linq;
using AwesomeAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Avalloy.Architecture.Tests;

/// <summary>
/// The Avalonia version a shipped package references is the floor every consumer resolves unless they
/// override it, so it decides platform behaviour nobody here can see from a headless test. Below 12.1.4,
/// Avalonia.Native's <c>-[AvnView keyDown:]</c> hands an unmodified key to the input context first and raises
/// no <c>KeyDown</c> for a printable one: on macOS a chord such as <c>ctrl+a&gt;h</c> never completes and an
/// unmodified letter binding never fires. 12.1.4 raises <c>KeyDown</c> first (as <c>Key.ImeProcessed</c> while
/// composing), the order Win32 and X11 already used.
/// </summary>
[TestClass]
public class AvaloniaFloorTests
{
    private static readonly Version Floor = new(12, 1, 4);

    /// <summary>The packages versioned in step with Avalonia itself. AvaloniaEdit and AvaloniaUI.* are not.</summary>
    private static readonly string[] AvaloniaPackages =
    [
        "Avalonia",
        "Avalonia.Desktop",
        "Avalonia.Diagnostics",
        "Avalonia.Headless",
        "Avalonia.Native",
        "Avalonia.Skia",
        "Avalonia.Themes.Fluent",
        "Avalonia.Themes.Simple",
        "Avalonia.Win32",
        "Avalonia.X11",
    ];

    [TestMethod]
    public void Every_Shipped_Avalonia_Reference_Is_At_Least_12_1_4()
    {
        var src = Path.Combine(RepoRoot(), "src");
        var references = Directory.EnumerateFiles(src, "*.*proj", SearchOption.AllDirectories)
            .Concat(Directory.EnumerateFiles(src, "*.props", SearchOption.AllDirectories))
            .Where(p => !p.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"))
            .SelectMany(p => XDocument.Load(p).Descendants()
                .Where(e => e.Name.LocalName is "PackageReference" or "PackageVersion")
                .Select(e => (
                    File: Path.GetRelativePath(src, p),
                    Id: (string?)e.Attribute("Include") ?? (string?)e.Attribute("Update"),
                    Version: (string?)e.Attribute("Version") ?? e.Elements().FirstOrDefault(c => c.Name.LocalName == "Version")?.Value)))
            .Where(r => AvaloniaPackages.Contains(r.Id, StringComparer.OrdinalIgnoreCase))
            .ToList();

        foreach (var package in new[] { "Avalloy.Base", "Avalloy.Desktop" })
        {
            references.Should().Contain(r => r.Id == "Avalonia" && r.File.StartsWith(package + Path.DirectorySeparatorChar, StringComparison.Ordinal),
                $"the floor is only checked if {package}'s Avalonia reference is found");
        }

        var below = references
            .Where(r => r.Version is null || !Version.TryParse(r.Version.Split('-')[0], out var v) || v < Floor)
            .Select(r => $"{r.File}: {r.Id} {r.Version ?? "(no version)"}");

        below.Should().BeEmpty($"below Avalonia {Floor}, macOS raises no KeyDown for an unmodified printable key");
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
