using System.Collections.ObjectModel;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Avalloy.Commands;

/// <summary>
/// One row in the keyboard-shortcuts editor. Wraps a single
/// <see cref="CommandDescriptor"/> (the runtime view of a command + its
/// <see cref="CommandAttribute"/> metadata) and exposes the currently-effective
/// list of gestures (user overrides if any, otherwise the defaults). Edits flow
/// through <see cref="GestureChipViewModel"/> instances and persist through the
/// injected <see cref="IKeymapStore"/>.
/// </summary>
public sealed class KeyboardShortcutRowViewModel : ObservableObject
{
    private readonly IKeymapStore _store;

    public KeyboardShortcutRowViewModel(CommandDescriptor command, IKeymapStore store)
    {
        Command = command;
        _store = store;
        ResetCommand = new RelayCommand(Reset);
        AddGestureCommand = new RelayCommand(AddEmptyChip);
        RebuildChips();
    }

    public CommandDescriptor Command { get; }
    public string CommandName => Command.Name;
    public string DisplayName => Command.DisplayName;
    public string Category => Command.Category;

    public ObservableCollection<GestureChipViewModel> Chips { get; } = new();

    /// <summary>Compact read-only summary of the effective gestures ("Cmd+D · F6")
    /// for the command list, so bindings are scannable without selecting each row.
    /// Same raw gesture text the chips show; empty (and collapsed) when unbound.</summary>
    public string GesturesSummary => string.Join(" · ",
        Chips.Select(c => c.GestureText).Where(s => !string.IsNullOrEmpty(s)));

    public bool HasGestures => Chips.Any(c => !string.IsNullOrEmpty(c.GestureText));

    private void NotifyGesturesChanged()
    {
        OnPropertyChanged(nameof(GesturesSummary));
        OnPropertyChanged(nameof(HasGestures));
    }

    /// <summary>True iff the user has overridden the defaults for this command.</summary>
    public bool IsOverridden => _store.HasOverride(CommandName);

    public ICommand ResetCommand { get; }
    public ICommand AddGestureCommand { get; }

    /// <summary>The default gesture list for this command under the host's current
    /// state. Used by the reset button and override-collapse detection.</summary>
    private IReadOnlyList<string> DefaultGestures => _store.DefaultGesturesFor(CommandName);

    /// <summary>Called by the editor when a non-binding pref (e.g. a mode that
    /// changes the resolved default for this row) changes, so the displayed chips
    /// update for rows the user hasn't overridden.</summary>
    public void RefreshFromDefaults()
    {
        if (!IsOverridden) RebuildChips();
    }

    /// <summary>Called by a chip's record handler or × button. Walks the current
    /// chip list, writes the result to the user keymap (or clears the entry to
    /// fall back to defaults), and fires the change notification so the dispatcher
    /// rebuilds.</summary>
    public void PersistFromChips()
    {
        var current = Chips.Select(c => c.GestureText)
                           .Where(s => !string.IsNullOrEmpty(s))
                           .ToList();
        if (current.SequenceEqual(DefaultGestures))
        {
            // User edited their way back to the defaults; clear the override.
            _store.ClearOverride(CommandName);
        }
        else
        {
            _store.SetOverride(CommandName, current);
        }
        _store.RaiseChanged();
        OnPropertyChanged(nameof(IsOverridden));
        NotifyGesturesChanged();
    }

    public void RemoveChip(GestureChipViewModel chip)
    {
        if (Chips.Remove(chip)) PersistFromChips();
    }

    public bool RemoveGestureBinding(string gesture, bool raiseShortcutsChanged = true)
    {
        IReadOnlyList<string> source = _store.GetOverride(CommandName) ?? DefaultGestures;
        var next = source.Where(g => !GesturesEqual(g, gesture)).ToList();
        if (next.Count == source.Count) return false;

        if (next.SequenceEqual(DefaultGestures))
            _store.ClearOverride(CommandName);
        else
            _store.SetOverride(CommandName, next);

        if (raiseShortcutsChanged)
            _store.RaiseChanged();
        OnPropertyChanged(nameof(IsOverridden));
        RebuildChips();
        return true;
    }

    private void AddEmptyChip()
    {
        Chips.Add(new GestureChipViewModel(this, string.Empty));
        // Don't persist yet — empty chip will be filled in by the recorder.
    }

    private void Reset()
    {
        if (_store.ClearOverride(CommandName))
        {
            _store.RaiseChanged();
            OnPropertyChanged(nameof(IsOverridden));
        }
        RebuildChips();
    }

    private void RebuildChips()
    {
        Chips.Clear();
        IReadOnlyList<string> source = _store.GetOverride(CommandName) ?? DefaultGestures;
        foreach (var g in source) Chips.Add(new GestureChipViewModel(this, g));
        NotifyGesturesChanged();
    }

    private static bool GesturesEqual(string left, string right)
    {
        var l = GestureParser.TryParse(left);
        var r = GestureParser.TryParse(right);
        return l is not null && r is not null && l.Equals(r);
    }
}
