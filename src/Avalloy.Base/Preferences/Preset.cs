using System.Text.Json;

namespace Avalloy.Preferences;

/// <summary>
/// What kind of tokens a preset contributes to the cascade.
/// </summary>
public enum PresetKind
{
    Theme,
    Density,
}

/// <summary>
/// For theme presets, which display variant the preset's tokens are
/// designed for. Density presets don't use this (they're variant-agnostic);
/// loading code must default to <see cref="None"/> for non-theme presets.
/// </summary>
public enum PresetVariant
{
    /// <summary>Density presets and any theme preset that hasn't declared
    /// a variant. Themes loaded with <see cref="None"/> can't participate
    /// in the light/dark slot model and will be skipped by the picker.</summary>
    None,
    Light,
    Dark,
}

/// <summary>
/// One named preset loaded from a JSON file. Theme presets ship one variant
/// per file (<c>"variant": "light"</c> or <c>"variant": "dark"</c>); density
/// presets are variant-agnostic. Tokens live directly on <see cref="Tokens"/>
/// — there's no nested variant block any more.
/// </summary>
public sealed class Preset
{
    public required string Name { get; init; }
    public required string DisplayName { get; init; }
    public required PresetKind Kind { get; init; }
    public required PresetVariant Variant { get; init; }
    public required IReadOnlyDictionary<string, JsonElement> Tokens { get; init; }
}
