using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalloy.Commands;

namespace Avalloy.Views.Controls;

/// <summary>
/// A click-and-press hotkey field. Focus it and press a chord; the captured
/// gesture is stored in <see cref="Hotkey"/> as a "Mod+"-portable string. The
/// clear button unbinds. Conflict rejection lives in the bound VM: if it refuses
/// a captured value (reverting <see cref="Hotkey"/> via its setter), this box
/// shows the previous value again.
/// </summary>
public partial class HotkeyBox : UserControl
{
    public static readonly StyledProperty<string?> HotkeyProperty =
        AvaloniaProperty.Register<HotkeyBox, string?>(
            nameof(Hotkey), defaultBindingMode: Avalonia.Data.BindingMode.TwoWay);

    public string? Hotkey
    {
        get => GetValue(HotkeyProperty);
        set => SetValue(HotkeyProperty, value);
    }

    private TextBlock _display = null!;

    public HotkeyBox()
    {
        AvaloniaXamlLoader.Load(this);
        _display = this.FindControl<TextBlock>("Display")!;
        var clear = this.FindControl<Button>("ClearButton")!;
        clear.Click += OnClearClick;
        GotFocus += (_, _) => UpdateDisplay();
        LostFocus += (_, _) => UpdateDisplay();
        UpdateDisplay();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == HotkeyProperty) UpdateDisplay();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        // Let Tab traverse focus and Escape blur without being captured.
        if (e.Key is Key.Tab) { base.OnKeyDown(e); return; }
        if (e.Key is Key.Escape) { Focusable = true; UpdateDisplay(); return; }
        // Ignore lone modifier presses — wait for the actual key.
        if (IsModifier(e.Key)) return;

        var gesture = new KeyGesture(e.Key, e.KeyModifiers);
        Hotkey = GestureParser.ToPortableString(gesture);
        e.Handled = true;
    }

    private void OnClearClick(object? sender, RoutedEventArgs e)
    {
        Hotkey = null;
        Focus();
    }

    private void UpdateDisplay()
    {
        _display.Text = IsFocused
            ? string.IsNullOrEmpty(Hotkey) ? "Press a shortcut…" : Hotkey
            : string.IsNullOrEmpty(Hotkey) ? "Not set" : Hotkey;
    }

    private static bool IsModifier(Key key) => key is
        Key.LeftCtrl or Key.RightCtrl or
        Key.LeftShift or Key.RightShift or
        Key.LeftAlt or Key.RightAlt or
        Key.LWin or Key.RWin;
}
