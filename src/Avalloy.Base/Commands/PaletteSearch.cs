namespace Avalloy.Commands;

/// <summary>How a <see cref="CommandPaletteViewModel"/> matches and orders rows against a query.</summary>
public enum PaletteMatching
{
    /// <summary>The original behaviour: a case-insensitive subsequence of the label or hint, then rows whose
    /// label STARTS with the query, then the caller's order. Unscored, so "fmt" ranks "unformatted" and
    /// "format document" by position in the list, not by how well they match.</summary>
    Subsequence,

    /// <summary>Ranked by <see cref="PaletteSearch.Score"/>: a match at the very start and at word starts
    /// first, contiguous runs ahead of scattered letters, shorter labels on ties. Label matches rank
    /// above rows that matched only on their <see cref="PaletteItem.Hint"/>.</summary>
    Scored,
}

/// <summary>Fuzzy scoring for the palette's <see cref="PaletteMatching.Scored"/> mode.</summary>
public static class PaletteSearch
{
    private const int StartBonus = 8;
    private const int WordStartBonus = 6;
    private const int RunBonus = 5;

    /// <summary>
    /// How well <paramref name="query"/> matches <paramref name="text"/> as a case-insensitive subsequence,
    /// or <see langword="null"/> when it does not. Higher is better. Each matched character scores 1, plus
    /// 8 at the very start of the text or 6 at a word start (after a non-alphanumeric, or a camelCase
    /// hump), plus 5 when it directly follows the previous match. Spaces in the query are skipped. Ties
    /// go to the shorter text: "Copy" over "Copy URL to Clipboard" for "copy".
    /// <para>Scored over the BEST alignment, not the first one found, so "tb" against "Toggle Tab Bar"
    /// is credited for the T and B word starts rather than for the first t and b it reaches.</para>
    /// </summary>
    public static int? Score(string query, string text)
    {
        var needle = query.Where(c => c != ' ').Select(char.ToLowerInvariant).ToArray();
        if (needle.Length == 0) return 0;
        if (text.Length < needle.Length) return null;

        // best[j]: the highest score for the query so far with its last character matched at text[j].
        const int None = int.MinValue;
        var best = new int[text.Length];
        var next = new int[text.Length];
        for (var j = 0; j < text.Length; j++)
            best[j] = char.ToLowerInvariant(text[j]) == needle[0] ? Bonus(text, j) : None;

        for (var i = 1; i < needle.Length; i++)
        {
            var before = None; // max of best[0..j-1]: the previous character matched anywhere earlier
            for (var j = 0; j < text.Length; j++)
            {
                var adjacent = j > 0 ? best[j - 1] : None;
                if (adjacent != None) before = Math.Max(before, adjacent);

                next[j] = None;
                if (before == None || char.ToLowerInvariant(text[j]) != needle[i]) continue;
                next[j] = Bonus(text, j) + (adjacent != None ? Math.Max(before, adjacent + RunBonus) : before);
            }
            (best, next) = (next, best);
        }

        var top = best.Max();
        return top == None ? null : top * 100 - text.Length;
    }

    private static int Bonus(string text, int i) =>
        1 + (i == 0 ? StartBonus : IsWordStart(text, i) ? WordStartBonus : 0);

    private static bool IsWordStart(string text, int i) =>
        !char.IsLetterOrDigit(text[i - 1]) || (char.IsUpper(text[i]) && char.IsLower(text[i - 1]));

    /// <summary>
    /// The next enabled row from <paramref name="current"/> in the direction of <paramref name="delta"/>'s
    /// sign, wrapping at both ends; <paramref name="current"/> itself when it is the only enabled row; -1
    /// when none is. From -1, +1 finds the first enabled row and -1 the last.
    /// </summary>
    internal static int Step(int count, int current, int delta, Func<int, bool> enabled)
    {
        if (count <= 0) return -1;
        var step = delta < 0 ? -1 : 1;
        var i = current < 0 ? (step > 0 ? -1 : count) : current;
        for (var n = 0; n < count; n++)
        {
            i = ((i + step) % count + count) % count;
            if (enabled(i)) return i;
        }
        return -1;
    }
}

/// <summary>
/// Most-recently-used palette keys, newest first, capped — the list to hand
/// <see cref="PaletteOptions.Recent"/>, updated from <see cref="PaletteOptions.Used"/>.
/// <para>In memory only: persisting it is the app's job. Seed it from what the app saved and save
/// <see cref="Keys"/> back when it changes.</para>
/// </summary>
public sealed class PaletteRecents
{
    private readonly List<string> _keys = [];
    private readonly int _capacity;

    public PaletteRecents(int capacity = 20, IEnumerable<string>? seed = null)
    {
        _capacity = Math.Max(1, capacity);
        if (seed is null) return;
        foreach (var key in seed)
        {
            if (_keys.Count == _capacity) break;
            if (!_keys.Contains(key)) _keys.Add(key);
        }
    }

    /// <summary>The keys, most recent first.</summary>
    public IReadOnlyList<string> Keys => _keys;

    /// <summary>Raised after <see cref="Use"/> changes the list — the app's cue to persist it.</summary>
    public event EventHandler? Changed;

    /// <summary>Move <paramref name="key"/> to the front, dropping the oldest past the capacity.</summary>
    public void Use(string key)
    {
        if (_keys.Count > 0 && _keys[0] == key) return;
        _keys.Remove(key);
        _keys.Insert(0, key);
        if (_keys.Count > _capacity) _keys.RemoveAt(_keys.Count - 1);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Record a palette row by its <see cref="PaletteItem.RecentKey"/> — fits
    /// <see cref="PaletteOptions.Used"/> directly.</summary>
    public void Use(PaletteItem item) => Use(item.RecentKey);
}
