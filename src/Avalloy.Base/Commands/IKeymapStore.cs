namespace Avalloy.Commands;

/// <summary>
/// Pluggable backing for the keyboard-shortcuts editor. The editor (and the
/// row/chip view models beneath it) read the effective gestures for a command —
/// the user override if one exists, otherwise the defaults — and write or clear
/// user overrides. The host app owns <em>where</em> overrides persist and
/// <em>what</em> the defaults are; the latter may depend on app state (e.g. a
/// modal mode that swaps which command gets a given key), so it is queried fresh
/// rather than cached.
/// </summary>
/// <remarks>
/// Mutations (<see cref="SetOverride"/> / <see cref="ClearOverride"/>) are
/// deliberately silent — callers batch several edits and then fire a single
/// <see cref="RaiseChanged"/> so the centralized preferences chokepoint sees one
/// change and triggers a single debounced save + dispatcher rebuild.
/// </remarks>
public interface IKeymapStore
{
    /// <summary>The default gestures for a command with no user override applied.
    /// Returns an empty list for an unbound command.</summary>
    IReadOnlyList<string> DefaultGesturesFor(string commandName);

    /// <summary>True iff the user has an explicit override entry for this command
    /// (including an explicit "unbound" entry).</summary>
    bool HasOverride(string commandName);

    /// <summary>The user's override gestures for this command, or <c>null</c> when
    /// there is no override (so callers fall back to <see cref="DefaultGesturesFor"/>).</summary>
    IReadOnlyList<string>? GetOverride(string commandName);

    /// <summary>Record a user override for this command.</summary>
    void SetOverride(string commandName, IReadOnlyList<string> gestures);

    /// <summary>Remove any user override so the command reverts to its defaults.
    /// Returns <c>true</c> if an entry was actually removed.</summary>
    bool ClearOverride(string commandName);

    /// <summary>Signal that overrides changed — fires the debounced save and the
    /// keyboard-dispatcher rebuild. Call once after a batch of mutations.</summary>
    void RaiseChanged();
}
