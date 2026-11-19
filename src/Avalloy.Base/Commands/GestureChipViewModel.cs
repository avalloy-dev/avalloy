using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Avalloy.Commands;

/// <summary>
/// One gesture binding within a <see cref="KeyboardShortcutRowViewModel"/>. The
/// editor renders the row's <see cref="KeyboardShortcutRowViewModel.Chips"/> as an
/// inline list — each chip is a recordable mini-TextBox with an × remove button.
/// New empty chips can be appended via the row's "+ Add" command.
/// </summary>
public sealed partial class GestureChipViewModel : ObservableObject
{
    private readonly KeyboardShortcutRowViewModel _row;

    public GestureChipViewModel(KeyboardShortcutRowViewModel row, string gesture)
    {
        _row = row;
        _gestureText = gesture;
        RemoveCommand = new RelayCommand(() => _row.RemoveChip(this));
    }

    [ObservableProperty] private string _gestureText;

    public string GestureDisplay => string.IsNullOrEmpty(GestureText) ? "press keys" : GestureText;
    partial void OnGestureTextChanged(string value) => OnPropertyChanged(nameof(GestureDisplay));

    /// <summary>Pass-through to the owning row's command id — lets the recorder
    /// handler do self-vs-other-command conflict checks without reaching back
    /// through the visual tree.</summary>
    public string OwningCommandName => _row.CommandName;

    public ICommand RemoveCommand { get; }

    /// <summary>Called by the editor's KeyDown handler when a real key is captured.
    /// Empty text removes this chip; otherwise updates and persists.</summary>
    public void OnRecordedGesture(string? gesture)
    {
        if (string.IsNullOrEmpty(gesture))
        {
            _row.RemoveChip(this);
            return;
        }
        GestureText = gesture;
        _row.PersistFromChips();
    }
}
