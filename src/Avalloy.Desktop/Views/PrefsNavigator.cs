using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Avalloy.Views;

/// <summary>One section on the preferences rail: the visible name, the search keywords its rows
/// answer to, and the selection state the rail chrome renders.</summary>
public sealed partial class PrefSection : ObservableObject
{
    public required string Name { get; init; }

    /// <summary>Space-separated row labels/synonyms for search-first filtering — every row label in
    /// the section belongs here so typing any of them surfaces the section.</summary>
    public string Keywords { get; init; } = "";

    [ObservableProperty] private bool _selected;
    [ObservableProperty] private bool _visible = true;

    /// <summary>Wired by <see cref="PrefsNavigator.Add"/> — selects this section.</summary>
    public IRelayCommand? Pick { get; internal set; }
}

/// <summary>
/// The search-first section rail behind <see cref="Controls.PreferencesOverlay"/>: a flat list of
/// sections (no nesting, ever), a search box that filters the rail to sections with matching rows
/// and jumps to the first match when the current section filters out, and single selection. The
/// app builds it once (<see cref="Add"/> per section) and binds its section bodies' visibility to
/// <see cref="Selected"/>.
/// </summary>
public sealed partial class PrefsNavigator : ObservableObject
{
    public ObservableCollection<PrefSection> Sections { get; } = [];

    /// <summary>The selected section's name — section bodies key their visibility off this.</summary>
    [ObservableProperty] private string _selected = "";

    [ObservableProperty] private string _searchText = "";

    public PrefSection Add(string name, string keywords = "")
    {
        var section = new PrefSection { Name = name, Keywords = keywords };
        section.Pick = new RelayCommand(() => Select(name));
        section.Selected = Sections.Count == 0 && Selected.Length == 0 || name == Selected;
        if (section.Selected) Selected = name;
        Sections.Add(section);
        return section;
    }

    public void Select(string name)
    {
        Selected = name;
        foreach (var s in Sections) s.Selected = s.Name == name;
    }

    partial void OnSearchTextChanged(string value)
    {
        var q = (value ?? "").Trim();
        foreach (var s in Sections)
            s.Visible = q.Length == 0
                || s.Name.Contains(q, StringComparison.OrdinalIgnoreCase)
                || s.Keywords.Contains(q, StringComparison.OrdinalIgnoreCase);
        // Jump to the first matching section when the current one filtered out.
        if (q.Length > 0 && Sections.FirstOrDefault(s => s.Name == Selected) is { Visible: false }
            && Sections.FirstOrDefault(s => s.Visible) is { } first)
            Select(first.Name);
    }
}
