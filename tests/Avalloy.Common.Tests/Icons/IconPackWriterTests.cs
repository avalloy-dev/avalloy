using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using AwesomeAssertions;
using Avalloy.Icons.Baking;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Avalloy.Icons.Baking.Tests;

[TestClass]
public class IconPackWriterTests
{
    private static readonly IconGlyphSpec StrokeGlyph = new()
    {
        Geometry = "M18 6 6 18 m6 6 12 12",
        Box = 24,
        Filled = false,
        StrokeWidth = 2,
    };

    [TestMethod]
    public void ResourceDictionary_emits_keyed_glyph_with_metadata()
    {
        var xaml = IconPackWriter.WriteResourceDictionary(
            new[] { new KeyValuePair<string, IconGlyphSpec>("x", StrokeGlyph) });

        xaml.Should().Contain("xmlns:ic=\"using:Avalloy.Icons\"");
        xaml.Should().Contain("<ic:IconGlyph x:Key=\"Icon.x\"");
        xaml.Should().Contain("Filled=\"False\"");
        xaml.Should().Contain("StrokeWidth=\"2\"");
        xaml.Should().Contain("Geometry=\"M18 6 6 18 m6 6 12 12\"");
    }

    [TestMethod]
    public void ResourceDictionary_namespace_is_parameterizable_for_apps()
    {
        var xaml = IconPackWriter.WriteResourceDictionary(
            new[] { new KeyValuePair<string, IconGlyphSpec>("x", StrokeGlyph) },
            new IconPackWriterOptions { ControlNamespace = "Sample.App.Controls" });

        xaml.Should().Contain("xmlns:ic=\"using:Sample.App.Controls\"");
    }

    [TestMethod]
    public void ResourceDictionary_omits_defaulted_attributes_for_filled_glyph()
    {
        var filled = new IconGlyphSpec { Geometry = "M0 0", Box = 24 };
        var xaml = IconPackWriter.WriteResourceDictionary(
            new[] { new KeyValuePair<string, IconGlyphSpec>("dot", filled) });

        // Filled==true (default) and StrokeWidth==0 must not be emitted.
        xaml.Should().NotContain("Filled=");
        xaml.Should().NotContain("StrokeWidth=");
    }

    [TestMethod]
    public void PackJson_carries_the_authoritative_icon_list()
    {
        var json = IconPackWriter.WritePackJson(
            "lucide", "Lucide", new[] { "x", "check", "eject" },
            description: "Lucide (ISC)", version: "0.2.0");

        json.Should().Contain("\"id\": \"lucide\"");
        json.Should().Contain("\"name\": \"Lucide\"");
        json.Should().Contain("\"version\": \"0.2.0\"");
        json.Should().Contain("\"icons\"");
        json.Should().Contain("\"check\"");
    }

    [TestMethod]
    public void ResourceDictionary_can_be_compiled_to_a_class()
    {
        var plain = IconPackWriter.WriteResourceDictionary(new[] { new KeyValuePair<string, IconGlyphSpec>("x", StrokeGlyph) });
        var compiled = IconPackWriter.WriteResourceDictionary(new[] { new KeyValuePair<string, IconGlyphSpec>("x", StrokeGlyph) },
            new IconPackWriterOptions { ClassName = "My.App.MyIcons", ClassModifier = "internal" });

        plain.Should().NotContain("x:Class", "no class asked for: the dictionary is loaded by URI, as before");
        compiled.Should().Contain("x:Class=\"My.App.MyIcons\"").And.Contain("x:ClassModifier=\"internal\"");
        compiled.Replace("\r", "")
            .Replace("\n                    x:Class=\"My.App.MyIcons\"\n                    x:ClassModifier=\"internal\"", "")
            .Should().Be(plain.Replace("\r", ""), "the class attributes are the only difference");
    }

    [TestMethod]
    public void PackJson_is_byte_for_byte_what_the_reflection_serializer_wrote()
    {
        // The manifest is now written by hand so baking needs no reflection. It is checked in, so it
        // must come out exactly as before or the next bake churns it: escaping included.
        string Reflection(string id, string name, string[] icons, string? description, string? homepage, string? version)
        {
            var manifest = new Dictionary<string, object?> { ["id"] = id, ["name"] = name };
            if (description is not null) manifest["description"] = description;
            if (homepage is not null) manifest["homepage"] = homepage;
            if (version is not null) manifest["version"] = version;
            manifest["icons"] = icons.ToArray();
            return JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true });
        }

        var full = (Id: "my-pack", Name: "Café \"Glyphs\" <&>", Icons: new[] { "a-b", "ünïcode", "q\"uote" },
            Description: "Line one\nline two", Homepage: "https://example.com/?a=1&b=2", Version: "1.0.0-beta+7");

        IconPackWriter.WritePackJson(full.Id, full.Name, full.Icons, full.Description, full.Homepage, full.Version)
            .Should().Be(Reflection(full.Id, full.Name, full.Icons, full.Description, full.Homepage, full.Version));
        IconPackWriter.WritePackJson("bare", "Bare", new string[0])
            .Should().Be(Reflection("bare", "Bare", new string[0], null, null, null), "optional fields left out, and an empty list");
    }
}
