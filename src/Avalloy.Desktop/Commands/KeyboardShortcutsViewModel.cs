using Avalonia.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Avalloy.Services;

namespace Avalloy.Commands;

/// <summary>
/// View model for the reusable keyboard-shortcuts editor. Builds one
/// <see cref="KeyboardShortcutRowViewModel"/> per registered command, drives the
/// list/detail filtering and selection, and owns the cross-command conflict
/// resolution that runs when the user records a gesture already bound elsewhere.
/// <para>
/// Everything app-specific is funnelled through the injected
/// <see cref="IKeymapStore"/> (override persistence + defaults). The optional
/// <see cref="ToastService"/> surfaces the "moved a shortcut off another command"
/// notice; pass <c>null</c> to stay silent (e.g. in tests).
/// </para>
/// </summary>
public partial class KeyboardShortcutsViewModel : ObservableObject
{
    private readonly CommandRegistry _commands;
    private readonly KeyboardDispatcher _keyboard;
    private IKeymapStore _store;
    private readonly ToastService? _toasts;

    public KeyboardShortcutsViewModel(
        CommandRegistry commands,
        KeyboardDispatcher keyboard,
        IKeymapStore store,
        ToastService? toasts = null)
    {
        _commands = commands;
        _keyboard = keyboard;
        _store = store;
        _toasts = toasts;
        ClearFilterCommand = new RelayCommand(() => Filter = string.Empty);
        RebuildRows();
        SelectedShortcut = Rows.FirstOrDefault();
    }

    /// <summary>Every command, sorted by category then display name. Rebuilt when
    /// the backing keymap is rebound (see <see cref="RebuildRows"/>).</summary>
    public IReadOnlyList<KeyboardShortcutRowViewModel> Rows { get; private set; } =
        Array.Empty<KeyboardShortcutRowViewModel>();

    /// <summary>Backs the × button inside the filter box.</summary>
    public RelayCommand ClearFilterCommand { get; }

    /// <summary>True iff <see cref="Filter"/> is non-empty — drives clear-button
    /// visibility.</summary>
    public bool HasFilter => !string.IsNullOrEmpty(Filter);

    /// <summary>Live filter text. Empty (or whitespace) shows everything; otherwise
    /// matches DisplayName, Category, CommandName, and any bound gesture text —
    /// case-insensitive contains.</summary>
    [ObservableProperty] private string _filter = string.Empty;

    [ObservableProperty] private KeyboardShortcutRowViewModel? _selectedShortcut;

    /// <summary>The list the editor's command ListBox binds to.</summary>
    public IEnumerable<KeyboardShortcutRowViewModel> FilteredShortcuts
    {
        get
        {
            var q = Filter?.Trim();
            if (string.IsNullOrEmpty(q)) return Rows;
            return Rows.Where(r => Matches(r, q));
        }
    }

    partial void OnFilterChanged(string value)
    {
        OnPropertyChanged(nameof(FilteredShortcuts));
        OnPropertyChanged(nameof(HasFilter));
    }

    private static bool Matches(KeyboardShortcutRowViewModel row, string q)
    {
        if (row.DisplayName.Contains(q, StringComparison.OrdinalIgnoreCase)) return true;
        if (row.Category.Contains(q, StringComparison.OrdinalIgnoreCase)) return true;
        if (row.CommandName.Contains(q, StringComparison.OrdinalIgnoreCase)) return true;
        foreach (var chip in row.Chips)
        {
            if (!string.IsNullOrEmpty(chip.GestureText)
                && chip.GestureText.Contains(q, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    /// <summary>Point the editor at a new keymap store — e.g. after a preferences
    /// reload swapped the backing model instances — and rebuild the rows. The VM
    /// instance stays stable so any bound view keeps working across the reload.</summary>
    public void Rebind(IKeymapStore store)
    {
        _store = store;
        RebuildRows();
    }

    /// <summary>(Re)build the row set from the command registry, preserving the
    /// current selection by command name across the rebuild. Call after the
    /// backing keymap instance is swapped (e.g. on a preferences reload).</summary>
    public void RebuildRows()
    {
        var name = SelectedShortcut?.CommandName;
        Rows = _commands.All
            .OrderBy(d => d.Category, StringComparer.OrdinalIgnoreCase)
            .ThenBy(d => d.DisplayName, StringComparer.OrdinalIgnoreCase)
            .Select(cmd => new KeyboardShortcutRowViewModel(cmd, _store))
            .ToArray();
        OnPropertyChanged(nameof(Rows));
        OnPropertyChanged(nameof(FilteredShortcuts));
        SelectedShortcut = (name is null
            ? null
            : Rows.FirstOrDefault(r => r.CommandName == name))
            ?? Rows.FirstOrDefault();
    }

    /// <summary>Refresh the displayed defaults for every non-overridden row — call
    /// when host state that feeds <see cref="IKeymapStore.DefaultGesturesFor"/>
    /// changes (e.g. a mode toggle that reassigns a default binding).</summary>
    public void RefreshDefaults()
    {
        foreach (var row in Rows) row.RefreshFromDefaults();
    }

    /// <summary>Strip <paramref name="gesture"/> from every command except
    /// <paramref name="owningCommandName"/>, raising a single change notification
    /// if anything moved.</summary>
    public bool RemoveGestureFromOtherCommands(string gesture, string owningCommandName)
    {
        var removed = false;
        foreach (var row in Rows)
        {
            if (row.CommandName == owningCommandName) continue;
            removed |= row.RemoveGestureBinding(gesture, raiseShortcutsChanged: false);
        }
        if (removed) _store.RaiseChanged();
        return removed;
    }

    /// <summary>Apply a freshly-recorded gesture to a chip: if the gesture is
    /// already bound to a different command, move it here (and warn), then commit
    /// it to the chip. Invoked by the editor's chip-recorder KeyDown handler.</summary>
    public void RecordGesture(GestureChipViewModel chip, KeyGesture gesture)
    {
        var portable = GestureParser.ToPortableString(gesture);
        var owning = chip.OwningCommandName;

        if (_keyboard.CommandForGesture(portable) is { } existing
            && existing != owning
            && _commands.Get(existing) is { } otherCmd)
        {
            RemoveGestureFromOtherCommands(portable, owning);
            _toasts?.ShowWarning(
                $"Shortcut conflict: {portable}",
                $"Removed it from \"{otherCmd.DisplayName}\".");
        }
        else
        {
            RemoveGestureFromOtherCommands(portable, owning);
        }

        chip.OnRecordedGesture(portable);
    }
}
