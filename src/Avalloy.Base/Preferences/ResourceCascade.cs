using System.Text.Json;
using Avalonia.Controls;

namespace Avalloy.Preferences;

/// <summary>
/// Orchestrates the standard preset-driven theming cascade: compile-time
/// defaults (from XAML) → theme preset → density preset → user overrides.
/// Each <see cref="For"/> call clears the schema's managed token keys first
/// so removed/unset preferences cleanly revert to the XAML defaults.
/// <para>
/// Apps declare a <see cref="TokenSchema"/> once and call the fluent builder
/// from their preferences-changed handler:
/// </para>
/// <code>
/// ResourceCascade.For(Resources, MyTokens.Schema)
///     .ApplyTheme(catalog.GetTheme(prefs.ThemePreset))
///     .ApplyUserOverrides(prefs, MyTokens.ApplyUserOverrides);
/// </code>
/// </summary>
public sealed class ResourceCascade
{
    private readonly IResourceDictionary _resources;
    private readonly TokenSchema _schema;

    private ResourceCascade(IResourceDictionary resources, TokenSchema schema)
    {
        _resources = resources;
        _schema = schema;
    }

    /// <summary>Start a fresh cascade. Clears every schema-managed key out of
    /// <paramref name="resources"/> immediately so previous-state tokens don't
    /// linger when the new cascade decides not to set them.</summary>
    public static ResourceCascade For(IResourceDictionary resources, TokenSchema schema)
    {
        var cascade = new ResourceCascade(resources, schema);
        foreach (var key in schema.AllTokens)
            resources.Remove(key);
        return cascade;
    }

    /// <summary>Resume cascading against an already-prepared resource dictionary
    /// — does NOT pre-clear. Used when an intermediate step (theme-variant
    /// flip, framework-driven accent republish) needs to land between the
    /// clear and the final overrides; calling <see cref="For"/> a second
    /// time would wipe out that intermediate publish.</summary>
    public static ResourceCascade Continue(IResourceDictionary resources, TokenSchema schema) =>
        new(resources, schema);

    /// <summary>Apply a theme preset's tokens. Themes are free to include
    /// density tokens (PaneRowHeight, etc.) when the author wants the theme
    /// to dictate density alongside color; the density preset still runs
    /// before the theme in the standard order, so themes that omit density
    /// tokens defer to the user's density preset.</summary>
    public ResourceCascade ApplyTheme(Preset? preset) =>
        ApplyPreset(preset, kindFilter: null);

    /// <summary>Apply an arbitrary preset, optionally narrowed by a kind
    /// filter. Exposed for apps that want a different stage shape (e.g. a
    /// "syntax" preset that contributes only color tokens of a particular
    /// naming convention).</summary>
    public ResourceCascade ApplyPreset(Preset? preset, Func<TokenKind, bool>? kindFilter = null)
    {
        if (preset is null) return this;
        foreach (var (key, value) in preset.Tokens)
        {
            var kind = _schema.Get(key);
            if (kind is null) continue;
            if (kindFilter is not null && !kindFilter(kind.Value)) continue;
            ApplyToken(key, kind.Value, value);
        }
        return this;
    }

    /// <summary>Run an app-provided override callback as the final stage. The
    /// callback owns whatever transformation makes sense for the host — one
    /// accent color → a family of derived brushes, user-selected font sizes,
    /// computed contrast brushes, etc. — that doesn't fit the preset-JSON model.</summary>
    public ResourceCascade ApplyUserOverrides<T>(T overrides, Action<IResourceDictionary, T> apply)
    {
        apply(_resources, overrides);
        return this;
    }

    private void ApplyToken(string key, TokenKind kind, JsonElement value)
    {
        switch (kind)
        {
            case TokenKind.Color:      ResourceTokens.TryApplyColor(_resources, key, value); break;
            case TokenKind.FontFamily: ResourceTokens.TryApplyFontFamily(_resources, key, value); break;
            case TokenKind.FontWeight: ResourceTokens.TryApplyFontWeight(_resources, key, value); break;
            case TokenKind.FontSize:   ResourceTokens.TryApplyDouble(_resources, key, value); break;
            case TokenKind.Size:       ResourceTokens.TryApplyDouble(_resources, key, value); break;
            case TokenKind.Thickness:  ResourceTokens.TryApplyThickness(_resources, key, value); break;
            case TokenKind.CornerRadius: ResourceTokens.TryApplyCornerRadius(_resources, key, value); break;
        }
    }
}
