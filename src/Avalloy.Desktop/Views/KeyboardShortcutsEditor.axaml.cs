using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Avalloy.Commands;

namespace Avalloy.Views;

/// <summary>
/// Reusable keyboard-shortcuts editor. Binds to a
/// <see cref="KeyboardShortcutsViewModel"/>; the chip recorder below turns each
/// non-modifier keypress into the new binding, Escape unbinds the chip, and the
/// VM resolves cross-command conflicts (moving the gesture off whatever command
/// previously held it, with a warning toast if one is wired).
/// </summary>
public partial class KeyboardShortcutsEditor : UserControl
{
    public KeyboardShortcutsEditor() => AvaloniaXamlLoader.Load(this);

    private void OnGestureRecorderKeyDown(object? sender, KeyEventArgs e)
    {
        if (sender is not TextBox tb || tb.DataContext is not GestureChipViewModel chip) return;
        if (DataContext is not KeyboardShortcutsViewModel vm) return;

        if (e.Key is Key.LeftCtrl or Key.RightCtrl
                   or Key.LeftShift or Key.RightShift
                   or Key.LeftAlt or Key.RightAlt
                   or Key.LWin or Key.RWin) return;

        if (e.Key == Key.Escape && e.KeyModifiers == KeyModifiers.None)
        {
            chip.OnRecordedGesture(null);
            e.Handled = true;
            return;
        }

        vm.RecordGesture(chip, new KeyGesture(e.Key, e.KeyModifiers));
        e.Handled = true;
    }
}
