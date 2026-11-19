using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Avalloy.Icons;

/// <summary>
/// A reusable, pack-agnostic icon picker: a searchable list of icon ids that renders
/// as a grid of <see cref="GlyphIcon"/> swatches in <see cref="IconPickerView"/>, and
/// raises exactly one picked id through the <c>onPick</c> callback. It knows only about
/// glyph ids — image upload / monogram / persistence are app concerns, added by the host
/// via <c>IconPickerView.Footer</c>. Build one over the active pack with
/// <see cref="FromResolver"/>, or pass any id list directly (e.g. for a gallery).
/// </summary>
public sealed partial class IconPickerViewModel : ObservableObject
{
    private readonly Action<string> _onPick;
    private readonly IReadOnlyList<IconPickItem> _all;

    public IconPickerViewModel(IEnumerable<string> iconIds, Action<string> onPick, string? current = null)
    {
        _onPick = onPick;
        _all = iconIds.Select(id => new IconPickItem(id)).ToList();
        Results = new ObservableCollection<IconPickItem>(_all);
        _selected = current;
    }

    /// <summary>Build a picker over the resolver's active pack (its <c>pack.json</c> icon list).</summary>
    public static IconPickerViewModel FromResolver(IconResolver resolver, Action<string> onPick, string? current = null)
        => new(resolver.ActivePack?.AvailableIcons ?? Array.Empty<string>(), onPick, current);

    /// <summary>Design-time / gallery preview.</summary>
    public static IconPickerViewModel Demo() => new(Array.Empty<string>(), _ => { });

    public ObservableCollection<IconPickItem> Results { get; }

    /// <summary>Total icons in the pack — for a search watermark / count.</summary>
    public int TotalCount => _all.Count;

    [ObservableProperty] private string _search = "";

    /// <summary>The currently-picked id (to highlight its swatch); null = none.</summary>
    [ObservableProperty] private string? _selected;

    partial void OnSearchChanged(string value)
    {
        var q = value.Trim();
        Results.Clear();
        var matches = string.IsNullOrEmpty(q)
            ? _all
            : _all.Where(i => i.Id.Contains(q, StringComparison.OrdinalIgnoreCase));
        foreach (var it in matches) Results.Add(it);
    }

    // A plain method (not a command) so the view's code-behind invokes it from a click
    // handler (Click + Tag) — no cross-scope command binding in the item template.
    public void Pick(string? id)
    {
        if (string.IsNullOrWhiteSpace(id)) return;
        Selected = id;
        _onPick(id);
    }
}

/// <summary>One icon-id cell in the picker grid.</summary>
public sealed record IconPickItem(string Id);
