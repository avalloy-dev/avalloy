using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Text.Json.Serialization;
using Avalonia.Input;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Avalloy.Commands;

/// <summary>Source-generated metadata for a keymap row's <see cref="PaletteItem.Key"/>, the
/// <c>[name, parameter]</c> pair, so building it needs no reflection (trimming, Native AOT). Default
/// options, as the reflection serializer used: the keys apps have stored as recents are unchanged.</summary>
[JsonSerializable(typeof(string[]))]
internal sealed partial class PaletteJsonContext : JsonSerializerContext;

/// <summary>One row in a command palette — a named target (pane, action, file, command) with a glyph
/// and a hint (shortcut / path / category), plus the action it runs.
/// <para>Everything past <see cref="Label"/>, <see cref="Hint"/>, <see cref="Glyph"/>, <see cref="Run"/> and
/// <see cref="Group"/> is opt-in: left at its default, a row looks and behaves as it always has.</para></summary>
public sealed class PaletteItem
{
    /// <summary>How many of <see cref="Hints"/> the row prints; the rest are counted ("+2") and listed
    /// in <see cref="HintTip"/>.</summary>
    public const int ShownHints = 2;

    public required string Label { get; init; }

    /// <summary>Right-aligned secondary text: a shortcut, a path, a category. Searched alongside
    /// <see cref="Label"/>, so typing a folder name finds files under it.</summary>
    public string Hint { get; init; } = "";

    /// <summary>Several right-aligned hints — every shortcut that fires the row, in the app's own notation
    /// (⌘D, ⌃A H), first the one to show first. When set it replaces <see cref="Hint"/> on screen: the first
    /// <see cref="ShownHints"/> are printed, then "+N". Unlike <see cref="Hint"/> these are NOT searched —
    /// they are key labels, and "d" matching every row bound to ⌘D is noise.</summary>
    public IReadOnlyList<string> Hints { get; init; } = [];

    /// <summary>Quiet text right after the label (a category, a scope). Shown, not searched.</summary>
    public string Detail { get; init; } = "";

    /// <summary>Icon-pack glyph role, resolved by <c>GlyphIcon</c>. Defaults to the run affordance.</summary>
    public string Glyph { get; init; } = "Enter";

    public required Action Run { get; init; }

    /// <summary>Lower groups sort first (e.g. 0 = panes/actions, 1 = files), so fixed commands stay on
    /// top once a query narrows the list.</summary>
    public int Group { get; init; }

    /// <summary>False lists the row but greys it out: keyboard selection skips it, clicking and Enter do
    /// nothing, it sorts below the enabled rows, and assistive technology reports it unavailable.</summary>
    public bool IsEnabled { get; init; } = true;

    /// <summary>Why a disabled row is disabled ("not available yet"), printed beside it and read out.</summary>
    public string Note { get; init; } = "";

    /// <summary>Identity for <see cref="PaletteOptions.Recent"/> — a command name, or name plus parameter
    /// for one row per parameter. Null falls back to <see cref="Label"/>; see <see cref="RecentKey"/>.</summary>
    public string? Key { get; init; }

    /// <summary>The key recents are matched on: <see cref="Key"/>, else <see cref="Label"/>.</summary>
    public string RecentKey => Key ?? Label;

    /// <summary>What the row prints on the right: <see cref="Hint"/>, or the first <see cref="ShownHints"/>
    /// of <see cref="Hints"/> and a count of the rest.</summary>
    public string HintText => Hints.Count switch
    {
        0 => Hint,
        <= ShownHints => string.Join("  ", Hints),
        _ => $"{string.Join("  ", Hints.Take(ShownHints))}  +{Hints.Count - ShownHints}",
    };

    /// <summary>Every hint, one per line, when the row cannot print them all; otherwise null, for no tooltip.</summary>
    public string? HintTip => Hints.Count > ShownHints ? string.Join("\n", Hints) : null;

    /// <summary>The row's text that only exists when there is a <see cref="Detail"/> — the label's suffix.</summary>
    public string DetailSuffix => string.IsNullOrEmpty(Detail) ? "" : "  " + Detail;

    public double RowOpacity => IsEnabled ? 1.0 : 0.45;

    /// <summary>What assistive technology reads for the row: label, detail, every hint, and the note.</summary>
    public string AccessibleName => string.Join(", ",
        new[] { Label, Detail }
            .Concat(Hints.Count > 0 ? Hints : [Hint])
            .Append(Note)
            .Where(s => !string.IsNullOrEmpty(s)));
}

/// <summary>
/// Opt-in palette behaviour; <c>new PaletteOptions()</c> is the palette as it always was.
/// </summary>
public sealed class PaletteOptions
{
    /// <summary>The original cap on rendered rows.</summary>
    public const int DefaultMaxResults = 40;

    /// <summary>How a query matches and orders rows. <see cref="PaletteMatching.Subsequence"/> by default.</summary>
    public PaletteMatching Matching { get; init; } = PaletteMatching.Subsequence;

    /// <summary>Cap on rendered rows, applied AFTER ranking, so the best matches are the ones kept.</summary>
    public int MaxResults { get; init; } = DefaultMaxResults;

    /// <summary>Recently used <see cref="PaletteItem.RecentKey"/>s, most recent first. With an empty query
    /// they float to the top in this order; with a query they break ties between equal matches. Empty by
    /// default — the palette only orders by recency when it is told what was recent.</summary>
    public IReadOnlyList<string> Recent { get; init; } = [];

    /// <summary>Called with a row when the palette runs it (after closing, before <see cref="PaletteItem.Run"/>)
    /// — where the app records it, e.g. <see cref="PaletteRecents.Use(PaletteItem)"/>.</summary>
    public Action<PaletteItem>? Used { get; init; }

    /// <summary>True leaves ↑↓/Enter alone while a modifier is held, so Shift+↑ selects in the search box and a
    /// modified Enter reaches the app's own bindings. False by default: the palette claims those keys whatever
    /// modifiers are held, as it always has.</summary>
    public bool IgnoreModifiedKeys { get; init; }
}

/// <summary>
/// <see cref="CommandPaletteViewModel.FromRegistryWith"/>'s
/// knobs. The defaults produce exactly the rows the shorter overload does.
/// </summary>
public sealed class RegistryPaletteOptions
{
    /// <summary>Extra filter on top of the CanExecute gate — for commands that are registered but have no
    /// business being typed (debug-only, or already reachable as a first-class row).</summary>
    public Func<CommandDescriptor, bool>? Include { get; init; }

    /// <summary>Sort group for every row produced here, so callers can rank commands against their own row kinds.</summary>
    public int Group { get; init; }

    /// <summary>Keep commands whose gate is closed, as disabled rows (<see cref="PaletteItem.IsEnabled"/> false),
    /// instead of leaving them out.</summary>
    public bool KeepDisabled { get; init; }

    /// <summary>The <see cref="PaletteItem.Note"/> for a row kept by <see cref="KeepDisabled"/>; null for none.
    /// Given the command, so the reason can differ per command.</summary>
    public Func<CommandDescriptor, string?>? DisabledNote { get; init; }

    /// <summary>The app's own hint strings for a command — every key that fires it, in its own notation —
    /// set as <see cref="PaletteItem.Hints"/>. Null keeps the default: the dispatcher's first gesture as
    /// <see cref="PaletteItem.Hint"/>. To show every dispatcher gesture, pass
    /// <c>d =&gt; dispatcher.GesturesFor(d.Name)</c>.</summary>
    public Func<CommandDescriptor, IReadOnlyList<string>>? Hints { get; init; }
}

/// <summary>
/// A filtered, keyboard-driven list over a fixed set of <see cref="PaletteItem"/>: type to narrow,
/// ↑↓ to move, Enter to run. Pair it with <c>CommandPaletteView</c>.
///
/// <para><b>It is deliberately not tied to <see cref="CommandRegistry"/>.</b> The registry is one
/// source — see <see cref="FromRegistry"/> — but the same control is the natural SCOPED picker
/// ("Move to Project…", "Change type to…"), where the list is a handful of one kind of thing that no
/// registry knows about. Taking items rather than a registry is what lets one control serve both.</para>
///
/// <para>Scoped callers must pass their own <see cref="Placeholder"/>: the global prompt is a claim
/// about what the box searches, and left on a picker of four stitch types it names three things that
/// aren't in the list.</para>
///
/// <para>Disabled rows, several hints, recents and scored ranking are all opt-in, through
/// <see cref="PaletteItem"/>'s optional fields and <see cref="PaletteOptions"/>.</para>
/// </summary>
public sealed partial class CommandPaletteViewModel : ObservableObject
{
    /// <summary>What a full command palette searches — the default, so the global caller states nothing.</summary>
    public const string GlobalPlaceholder = "Search panes, files, actions…";

    private readonly IReadOnlyList<PaletteItem> _all;
    private readonly Action _close;
    private readonly PaletteOptions _options;
    private readonly Dictionary<string, int> _recency = new(StringComparer.Ordinal);

    public CommandPaletteViewModel(IReadOnlyList<PaletteItem> items, Action close, string? placeholder = null)
        : this(items, close, new PaletteOptions(), placeholder)
    {
    }

    /// <summary>A palette with the opt-in behaviour of <paramref name="options"/>.
    /// <para>A factory rather than a constructor overload ON PURPOSE: beside the original
    /// <c>(items, close, string? placeholder)</c>, an overload taking a <see cref="PaletteOptions"/> in the
    /// third position leaves an app's <c>new CommandPaletteViewModel(items, close, null)</c> one overload rule
    /// away from binding to the wrong constructor.</para></summary>
    public static CommandPaletteViewModel Create(
        IReadOnlyList<PaletteItem> items, Action close, PaletteOptions options, string? placeholder = null)
        => new(items, close, options, placeholder);

    private CommandPaletteViewModel(IReadOnlyList<PaletteItem> items, Action close, PaletteOptions options, string? placeholder)
    {
        _all = items;
        _close = close;
        _options = options;
        for (var i = 0; i < options.Recent.Count; i++)
            _recency.TryAdd(options.Recent[i], i);
        Placeholder = string.IsNullOrWhiteSpace(placeholder) ? GlobalPlaceholder : placeholder;
        Filter();
    }

    /// <summary>Prompt in the search box — what THIS palette is a list of.</summary>
    public string Placeholder { get; }

    [ObservableProperty] private string _query = "";
    [ObservableProperty] private int _selectedIndex;

    public ObservableCollection<PaletteItem> Results { get; } = [];

    partial void OnQueryChanged(string value) => Filter();

    // The cap is a picker's, not a browser's: past a screenful, narrowing the query beats scrolling, and an
    // unbounded list makes an empty query as expensive as the whole index. Ranking runs first, so the cap
    // keeps the best rows. Every sort key below is constant across rows that use no opt-in field (all
    // enabled, nothing recent), and OrderBy is stable, so such a list orders exactly as it always has.
    private void Filter()
    {
        var q = Query.Trim();
        var empty = string.IsNullOrEmpty(q);
        var cap = Math.Max(0, _options.MaxResults);

        Results.Clear();
        if (empty && _recency.Count == 0)
        {
            // Nothing to rank: the caller's order with disabled rows after the enabled ones. Kept lazy so the
            // cap stops the read — opening a palette over a large list (every file in a tree) reads a
            // screenful, not the whole list, exactly as before rows could be disabled.
            foreach (var i in _all.Where(i => i.IsEnabled).Concat(_all.Where(i => !i.IsEnabled)).Take(cap))
                Results.Add(i);
            SelectedIndex = PaletteSearch.Step(Results.Count, -1, +1, IsSelectable);
            return;
        }

        var candidates = _all
            .Select((item, index) => (item, index, match: empty ? (Tier: 0, Score: 0) : Match(item, q)))
            .Where(x => x.match is not null)
            .Select(x => (x.item, x.index, match: x.match!.Value));

        // An empty query is not a search, so the caller's arrangement stands; only disabled rows sink and
        // recent ones rise.
        var ordered = empty
            ? candidates.OrderBy(x => x.item.IsEnabled ? 0 : 1)
            : candidates.OrderBy(x => x.item.Group)
                        .ThenBy(x => x.item.IsEnabled ? 0 : 1)
                        .ThenBy(x => x.match.Tier)
                        .ThenByDescending(x => x.match.Score);
        var matched = ordered
            .ThenBy(x => _recency.TryGetValue(x.item.RecentKey, out var r) ? r : int.MaxValue)
            .ThenBy(x => x.index)
            .Select(x => x.item);

        foreach (var i in matched.Take(cap)) Results.Add(i);
        SelectedIndex = PaletteSearch.Step(Results.Count, -1, +1, IsSelectable);
    }

    /// <summary>Null for no match; otherwise a tier (lower first) and a score (higher first) within it.</summary>
    private (int Tier, int Score)? Match(PaletteItem item, string q)
    {
        if (_options.Matching == PaletteMatching.Scored)
        {
            if (PaletteSearch.Score(q, item.Label) is { } label) return (0, label);
            if (PaletteSearch.Score(q, item.Hint) is { } hint) return (1, hint);
            return null;
        }
        if (!Matches(item.Label, q) && !Matches(item.Hint, q)) return null;
        return (0, item.Label.StartsWith(q, StringComparison.OrdinalIgnoreCase) ? 1 : 0);
    }

    // Case-insensitive SUBSEQUENCE match ("ots" matches "oauth.ts"), so short typed fragments find long
    // names without the user knowing where the fragment sits.
    private static bool Matches(string haystack, string needle)
    {
        if (string.IsNullOrEmpty(needle)) return true;
        if (string.IsNullOrEmpty(haystack)) return false;
        int hi = 0;
        foreach (var c in needle)
        {
            var found = haystack.IndexOf(c.ToString(), hi, StringComparison.OrdinalIgnoreCase);
            if (found < 0) return false;
            hi = found + 1;
        }
        return true;
    }

    private bool IsSelectable(int index) => Results[index].IsEnabled;

    /// <summary>
    /// Build palette rows from every registered command — the "reach any command by name" list, which is
    /// what <see cref="ICommandSurface"/> was always for.
    ///
    /// <para>Rows are labelled <c>Category: DisplayName</c> so a category is typeable ("view" finds the
    /// View commands) without a second column to search, and hinted with the shortcut that actually fires
    /// the command. Commands whose <see cref="IConditionalCommand.CanExecute"/> gate is closed RIGHT NOW
    /// are omitted — the list is built per open, so it reflects the state the user is looking at.</para>
    ///
    /// <para>Merge the result with hand-built rows (files, panes, recent items) before constructing the
    /// view model; this is one source, not the whole palette.</para>
    /// </summary>
    /// <param name="include">Extra filter on top of the CanExecute gate — for commands that are registered
    /// but have no business being typed (debug-only, or already reachable as a first-class row).</param>
    /// <param name="group">Sort group for every row produced here, so callers can rank commands against
    /// their own row kinds.</param>
    public static IReadOnlyList<PaletteItem> FromRegistry(
        CommandRegistry registry,
        KeyboardDispatcher dispatcher,
        Func<CommandDescriptor, bool>? include = null,
        int group = 0)
        => FromRegistryWith(registry, dispatcher, new RegistryPaletteOptions { Include = include, Group = group });

    /// <summary>
    /// <see cref="FromRegistry(CommandRegistry, KeyboardDispatcher, Func{CommandDescriptor, bool}?, int)"/> with
    /// the opt-in knobs: closed-gate commands kept as disabled rows, and the app's own hint strings.
    /// Every row's <see cref="PaletteItem.Key"/> is the command name, for <see cref="PaletteOptions.Recent"/>.
    /// <para>Its own name, not a <c>FromRegistry</c> overload: an app's <c>FromRegistry(registry, dispatcher, null)</c>
    /// would bind to an overload whose third parameter is the options (every parameter supplied beats one left
    /// to its default) and throw on the null.</para>
    /// </summary>
    public static IReadOnlyList<PaletteItem> FromRegistryWith(
        CommandRegistry registry,
        KeyboardDispatcher dispatcher,
        RegistryPaletteOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var rows = new List<PaletteItem>();
        foreach (var d in registry.All)
        {
            if (options.Include is not null && !options.Include(d)) continue;
            var enabled = dispatcher.CanRun(d.Name);
            if (!enabled && !options.KeepDisabled) continue;

            var name = d.Name;
            rows.Add(new PaletteItem
            {
                Label = string.IsNullOrEmpty(d.Category) || d.Category == "General"
                    ? d.DisplayName
                    : $"{d.Category}: {d.DisplayName}",
                Hint = options.Hints is null && dispatcher.GesturesFor(name) is [var first, ..] ? first : "",
                Hints = options.Hints?.Invoke(d) ?? [],
                Glyph = string.IsNullOrEmpty(d.Icon) ? "Enter" : d.Icon,
                Group = options.Group,
                Key = name,
                IsEnabled = enabled,
                Note = enabled ? "" : options.DisabledNote?.Invoke(d) ?? "",
                // Fire-and-forget: PaletteItem.Run is synchronous because most rows are (focus a pane,
                // open a file). Faults are traced rather than left unobserved — a command that throws is
                // a bug in the command, and losing it to a silent discard is how it stays one.
                Run = () =>
                {
                    try
                    {
                        var task = dispatcher.RunByNameAsync(name);
                        if (!task.IsCompletedSuccessfully) task.ContinueWith(
                            t => Trace.TraceError($"Command '{name}' failed: {t.Exception?.GetBaseException().Message}"),
                            CancellationToken.None,
                            TaskContinuationOptions.OnlyOnFaulted,
                            TaskScheduler.Default);
                    }
                    catch (Exception ex)
                    {
                        Trace.TraceError($"Command '{name}' failed to start: {ex.Message}");
                    }
                },
            });
        }
        return rows;
    }

    /// <summary>Build parameter-aware rows from the effective keymap. Each distinct first command
    /// and parameter gets a row; hints use platform labels and execution preserves the host target.</summary>
    public static IReadOnlyList<PaletteItem> FromKeymap(
        CommandRegistry registry,
        Keymaps.KeymapDispatcher dispatcher,
        object? target = null,
        Keymaps.KeymapPlatform? platform = null)
    {
        return dispatcher.Keymap.Bindings.Select(b => b.Action).OfType<Keymaps.CommandInvocation>()
            .Distinct().Where(a => dispatcher.Readiness(a, target) == Keymaps.ActionReadiness.Ready)
            .Select(a =>
            {
                var d = registry.Get(a.Name)!;
                return new PaletteItem
                {
                    Label = d.DisplayName + (a.Parameter is null ? "" : $": {a.Parameter}"),
                    Hints = dispatcher.Keymap.BindingsFor(a.Name, a.Parameter)
                        .Select(b => Keymaps.KeyNames.Display(b.Sequence, platform ?? Keymaps.KeymapPlatforms.Current)).ToArray(),
                    Key = System.Text.Json.JsonSerializer.Serialize(new[] { a.Name, a.Parameter }, PaletteJsonContext.Default.StringArray),
                    IsEnabled = true,
                    Detail = d.Category == "General" ? "" : d.Category,
                    Glyph = string.IsNullOrEmpty(d.Icon) ? "Enter" : d.Icon,
                    Run = () =>
                    {
                        try
                        {
                            var task = dispatcher.RunAsync(a, target);
                            if (!task.IsCompletedSuccessfully) task.ContinueWith(
                                t => Trace.TraceError($"Command '{a.Name}' failed: {t.Exception?.GetBaseException().GetType().Name}"),
                                CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted, TaskScheduler.Default);
                        }
                        catch (Exception ex) { Trace.TraceError($"Command '{a.Name}' failed: {ex.GetType().Name}"); }
                    },
                };
            }).ToList();
    }

    /// <summary>Move the selection <paramref name="delta"/> enabled rows, wrapping at both ends so ↑ from the
    /// top lands on the last row. Disabled rows are stepped over.</summary>
    public void Move(int delta)
    {
        var enabled = Results.Count(r => r.IsEnabled);
        if (enabled == 0 || delta == 0) return;
        var index = SelectedIndex;
        // Every `enabled` steps from an enabled row come back to it, so only the remainder moves. From no
        // selection (or a disabled one) the first step is what lands on a row, so it is counted apart.
        var distance = Math.Abs((long)delta);
        var onRow = index >= 0 && index < Results.Count && Results[index].IsEnabled;
        var steps = onRow ? distance % enabled : (distance - 1) % enabled + 1;
        for (; steps > 0; steps--)
            index = PaletteSearch.Step(Results.Count, index, delta, IsSelectable);
        SelectedIndex = index;
    }

    /// <summary>The palette's keys, wherever focus is in it: ↓ and ↑ move the selection (wrapping, over
    /// disabled rows), Enter runs it. True when the key was one of those, for the view to mark it handled so
    /// neither the search box nor the list acts on it as well. Esc is not here: the host window's overlay
    /// dismiss owns it. With <see cref="PaletteOptions.IgnoreModifiedKeys"/>, a key pressed with any
    /// <paramref name="modifiers"/> is left alone too.</summary>
    public bool HandleKey(Key key, KeyModifiers modifiers = KeyModifiers.None)
    {
        if (_options.IgnoreModifiedKeys && modifiers != KeyModifiers.None) return false;
        switch (key)
        {
            case Key.Down: Move(+1); return true;
            case Key.Up: Move(-1); return true;
            case Key.Enter: Activate(); return true;
            default: return false;
        }
    }

    /// <summary>Run the selected row (Enter / click) and dismiss.
    /// <para>With rows listed but none of them able to run, Enter does nothing and the palette stays open, as a
    /// click on a disabled row does. With no rows at all it closes: Esc is not the only way out of a palette
    /// showing nothing.</para></summary>
    public void Activate()
    {
        if (SelectedIndex >= 0 && SelectedIndex < Results.Count) RunItem(Results[SelectedIndex]);
        else if (Results.Count == 0 || Results.Any(r => r.IsEnabled)) RunItem(null);
    }

    /// <summary>Dismiss, THEN run. That order is deliberate: a command that opens its own overlay must not
    /// have the palette's close tear it back down. A disabled row does neither — the palette stays open.</summary>
    public void RunItem(PaletteItem? item)
    {
        if (item is { IsEnabled: false }) return;
        _close();
        if (item is null) return;
        _options.Used?.Invoke(item);
        item.Run();
    }
}
