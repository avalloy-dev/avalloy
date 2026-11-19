namespace Avalloy.Preferences;

/// <summary>
/// Classification of one preset token. Drives which <see cref="ResourceTokens"/>
/// parser the cascade dispatches to, and is matched against the cascade-stage
/// filter (themes provide everything except <see cref="Size"/>; density
/// presets provide only <see cref="Size"/>).
/// </summary>
public enum TokenKind
{
    /// <summary>SolidColorBrush from a hex string or named color.</summary>
    Color,
    /// <summary>Avalonia FontFamily from a comma-separated fallback chain.</summary>
    FontFamily,
    /// <summary>FontWeight from a name (e.g. "SemiBold") or numeric value (e.g. 600).</summary>
    FontWeight,
    /// <summary>Typography size (BodyFontSize, …) as a positive double.</summary>
    FontSize,
    /// <summary>Layout size (RowHeight, CellPadding, …) as a positive double.</summary>
    Size,
    /// <summary>Avalonia <see cref="Avalonia.Thickness"/> from a string
    /// ("2", "2,4", "1,2,3,4") or a number (uniform thickness).</summary>
    Thickness,
    /// <summary>Avalonia <see cref="Avalonia.CornerRadius"/> from a string
    /// ("4", "4,4,0,0") or a number (uniform radius).</summary>
    CornerRadius,
}

/// <summary>
/// Per-app allowlist mapping token names to their kinds. Apps construct one
/// of these once at startup and hand it to <see cref="ResourceCascade"/>; the
/// cascade uses it to (a) know which keys to clear on each apply, and (b) pick
/// the right parser per token.
/// </summary>
public sealed class TokenSchema
{
    private readonly IReadOnlyDictionary<string, TokenKind> _tokens;

    public TokenSchema(IReadOnlyDictionary<string, TokenKind> tokens)
    {
        // Defensive copy with explicit ordinal comparison — token names match
        // XAML resource keys, which are case-sensitive.
        _tokens = new Dictionary<string, TokenKind>(tokens, StringComparer.Ordinal);
    }

    /// <summary>Lookup. Returns <c>null</c> for unknown tokens; the cascade
    /// silently skips those so an out-of-allowlist key in a preset JSON can't
    /// pollute the resource dictionary.</summary>
    public TokenKind? Get(string token) =>
        _tokens.TryGetValue(token, out var k) ? k : null;

    /// <summary>Every token name the schema manages. The cascade enumerates
    /// this to clear stale values before re-applying.</summary>
    public IEnumerable<string> AllTokens => _tokens.Keys;

    /// <summary>Union of this schema with <paramref name="other"/>, returning a
    /// new schema. On key collision <paramref name="other"/> wins, so an app can
    /// extend a base (Avalloy) schema and re-kind a token if it must. This is
    /// how a consuming app hands the cascade a <em>merged</em> allowlist — the
    /// cascade clears + parses by schema, so the Avalloy generic tokens and the
    /// app's own additions (e.g. Desktop's Pane*/Tab* keys) must live in one
    /// schema or a theme-swap would leave the app's tokens stale.</summary>
    public TokenSchema Compose(TokenSchema other)
    {
        var merged = new Dictionary<string, TokenKind>(_tokens, StringComparer.Ordinal);
        foreach (var (k, v) in other._tokens) merged[k] = v;
        return new TokenSchema(merged);
    }
}
