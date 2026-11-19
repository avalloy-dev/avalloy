using AwesomeAssertions;
using Avalloy.Icons;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Avalloy.Desktop.Tests;

/// <summary>
/// How <see cref="IconResolver"/> reads a user-installed pack's <c>pack.json</c>.
/// </summary>
/// <remarks>
/// The manifest is read through source-generated metadata, so the resolver works in trimmed and Native AOT
/// apps. These pin that it reads what the reflection reader did: every field, by its lower-case name, and a
/// pack whose manifest does not parse is skipped rather than failing discovery.
/// </remarks>
[TestClass]
public sealed class IconResolverUserPackTests
{
    [TestMethod]
    public void A_User_Pack_Is_Read_From_Its_Manifest()
    {
        var root = Directory.CreateTempSubdirectory("avalloy-iconpacks-").FullName;
        try
        {
            var pack = Directory.CreateDirectory(Path.Combine(root, "mine")).FullName;
            File.WriteAllText(Path.Combine(pack, "pack.json"), """
                { "id": "mine", "name": "My Glyphs", "description": "Hand drawn", "homepage": "https://example.com", "version": "1.2.0" }
                """);
            File.WriteAllText(Path.Combine(pack, "star.svg"), "<svg/>");

            var broken = Directory.CreateDirectory(Path.Combine(root, "broken")).FullName;
            File.WriteAllText(Path.Combine(broken, "pack.json"), "{ not json");

            var resolver = new IconResolver(new IconResolverOptions { UserPacksRoot = root, DefaultPackId = "mine" });

            resolver.AvailablePacks.Should().ContainSingle("the broken pack is skipped");
            var read = resolver.ActivePack!;
            read.Id.Should().Be("mine");
            read.DisplayName.Should().Be("My Glyphs");
            read.Description.Should().Be("Hand drawn");
            read.Homepage.Should().Be("https://example.com");
            resolver.Resolve("star").Should().EndWith("/mine/star.svg");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
