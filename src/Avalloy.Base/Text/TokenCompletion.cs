using System.Collections.Generic;
using System.Linq;

namespace Avalloy.Text;

/// <summary>
/// Pure helpers for trigger-char autocomplete in text fields (e.g. <c>$token</c>
/// in a path, or a custom trigger in a prompt template): find the fragment the
/// user is typing after the trigger, decide which token names match it, and
/// splice a chosen token back into the text. Token names are bare (no trigger);
/// the leading trigger char is what starts the fragment and is re-added on apply.
/// </summary>
public static class TokenCompletion
{
    /// <summary>The bare token fragment currently being typed (the run of word
    /// chars after the last <paramref name="trigger"/>), or null when the text
    /// isn't in a token. An empty string means the user just typed the trigger
    /// (offer everything).</summary>
    public static string? TrailingFragment(string? text, char trigger = '$')
    {
        if (string.IsNullOrEmpty(text)) return null;
        int at = text.LastIndexOf(trigger);
        if (at < 0) return null;
        var after = text[(at + 1)..];
        return after.All(c => char.IsLetterOrDigit(c) || c == '_') ? after : null;
    }

    /// <summary>Token names (bare) that start with the active fragment, or empty
    /// when not in a token. Case-insensitive.</summary>
    public static IEnumerable<string> Matches(string? text, IEnumerable<string> tokenNames, char trigger = '$')
    {
        var fragment = TrailingFragment(text, trigger);
        if (fragment is null) return Enumerable.Empty<string>();
        return tokenNames.Where(n => n.StartsWith(fragment, System.StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>True when the autocomplete should consider <paramref name="tokenName"/>
    /// for the given text — the AutoCompleteBox item filter.</summary>
    public static bool ItemMatches(string? text, string tokenName, char trigger = '$')
    {
        var fragment = TrailingFragment(text, trigger);
        return fragment is not null && tokenName.StartsWith(fragment, System.StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Replace the trailing <c>{trigger}fragment</c> with
    /// <c>{trigger}token{suffix}</c>. Path callers pass <c>suffix: "/"</c> so the
    /// user can type the subpath directly (and portable paths standardize on
    /// <c>/</c>, which the resolver maps to the native separator per OS); other
    /// callers leave it empty. Returns the text unchanged when there's no active
    /// fragment.</summary>
    public static string Apply(string? text, string tokenName, char trigger = '$', string suffix = "")
    {
        text ??= string.Empty;
        int at = text.LastIndexOf(trigger);
        return at < 0 ? text : text[..at] + trigger + tokenName + suffix;
    }
}
