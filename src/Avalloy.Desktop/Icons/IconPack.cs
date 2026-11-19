using System.Text.Json.Serialization;
using Avalonia.Controls;

namespace Avalloy.Icons;

/// <summary>One icon pack — bundled with an assembly (read from
/// <c>avares://{Assembly}/Assets/iconpacks/&lt;id&gt;/</c>) or installed by the
/// user in their iconpacks folder.</summary>
public sealed record IconPack(
    string Id,
    string DisplayName,
    string? Description,
    string? Homepage,
    /// <summary>How icon URIs are constructed for this pack. For bundled packs
    /// this is <c>{source.PacksBaseUri}{Id}/{iconId}.svg</c> (an <c>avares://</c>
    /// URI); for user packs this is a <c>file://</c> URI pointing into their
    /// iconpacks dir. Legacy Svg.Skia render path.</summary>
    Func<string, string> ResolveIconUri,
    IReadOnlyCollection<string> AvailableIcons,
    /// <summary>Builds this pack's baked <c>IconGlyph</c> resource dictionary — the
    /// active-pack dictionary the <c>GlyphIcon</c> control resolves against. Bundled
    /// packs load their pre-baked <c>&lt;id&gt;.icons.axaml</c>; user packs bake their
    /// raw SVGs on the fly. Null when the pack has no baked form. See ADR 0001.</summary>
    Func<ResourceDictionary?>? BuildResources = null);

/// <summary>Source-generated metadata for <see cref="IconPackManifest"/>, so reading
/// <c>pack.json</c> needs no reflection (trimming, Native AOT). Default options, as the
/// reflection reader used: the names come from the <c>[JsonPropertyName]</c>s.</summary>
[JsonSerializable(typeof(IconPackManifest))]
internal sealed partial class IconJsonContext : JsonSerializerContext;

/// <summary>JSON shape of <c>pack.json</c> inside each icon pack folder.</summary>
internal sealed class IconPackManifest
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("description")] public string? Description { get; set; }
    [JsonPropertyName("homepage")] public string? Homepage { get; set; }
    [JsonPropertyName("version")] public string? Version { get; set; }
    /// <summary>Optional explicit list of icon IDs in this pack. If present, it
    /// authoritatively defines the pack's available icons (used for bundled
    /// packs where AssetLoader enumeration isn't reliable). User packs leave
    /// this empty and the resolver walks the folder for *.svg files instead.</summary>
    [JsonPropertyName("icons")] public List<string>? Icons { get; set; }
}
