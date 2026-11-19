using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.VisualTree;

namespace Avalloy.Views;

/// <summary>
/// Avalloy's themed confirm dialog: a chromeless card styled entirely through
/// the shared theme tokens, so every Avalloy app gets a consistent, themed
/// confirm prompt (rather than the bare programmatic window). Used by
/// <see cref="Services.DialogService"/> and callable directly via
/// <see cref="AskAsync"/> / <see cref="AskWithSuppressAsync"/>.
/// </summary>
/// <summary>Outcome of a three-way <see cref="ConfirmationDialog.AskThreeWayAsync"/>
/// prompt.</summary>
public enum ConfirmationChoice
{
    /// <summary>The Cancel button, or the dialog was dismissed.</summary>
    Cancel,

    /// <summary>The accented default (right-most) button.</summary>
    Primary,

    /// <summary>The optional middle button.</summary>
    Alternate,
}

public partial class ConfirmationDialog : Window
{
    private bool _confirmed;
    private ConfirmationChoice _choice = ConfirmationChoice.Cancel;

    public ConfirmationDialog()
    {
        InitializeComponent();
        Opened += (_, _) =>
        {
            // Focus the Confirm button on open so Enter activates it. Without an
            // explicit focus the default keyboard focus falls on Cancel (first
            // focusable), and IsDefault on Confirm only triggers when no other
            // focused control consumes Enter.
            ConfirmButton.Focus();
            DegradeIfNotTransparent();
        };
    }

    /// <summary>The chromeless card relies on window transparency for its
    /// rounded corners + shadow (the Margin gap shows the transparent window
    /// behind the card). Where the platform won't grant transparency — X11
    /// without a compositor — that gap renders opaque black. Detect that via
    /// <see cref="TopLevel.ActualTransparencyLevel"/> after open and collapse
    /// the card to fill the window (no margin, square corners, no shadow), so
    /// the card's own opaque background covers 100% of the client area and
    /// nothing of the window shows through.</summary>
    private void DegradeIfNotTransparent()
    {
        if (ActualTransparencyLevel != WindowTransparencyLevel.None) return;
        Card.Margin = new Thickness(0);
        Card.CornerRadius = new CornerRadius(0);
        Card.BoxShadow = default;
    }

    /// <summary>No native title bar (WindowDecorations=None) means no grab
    /// handle — let the user drag the card body to reposition the dialog,
    /// except when the press lands on an interactive control (the buttons or
    /// the suppress checkbox), which need the click for themselves.</summary>
    private void OnCardPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        if (e.Source is Visual v &&
            (v.FindAncestorOfType<Button>(includeSelf: true) is not null ||
             v.FindAncestorOfType<CheckBox>(includeSelf: true) is not null))
            return;
        BeginMoveDrag(e);
    }

    /// <summary>Convenience factory: show a confirm prompt parented to
    /// <paramref name="owner"/> and resolve true on Confirm, false on Cancel
    /// or close. Pass <paramref name="cancelLabel"/> to relabel the secondary
    /// button when "Cancel" doesn't describe the action it takes (e.g. "Quit").</summary>
    public static async Task<bool> AskAsync(Window owner, string title, string message,
        string confirmLabel = "Confirm", string cancelLabel = "Cancel")
    {
        var dlg = new ConfirmationDialog();
        dlg.TitleText.Text = title;
        dlg.MessageText.Text = message;
        dlg.ConfirmButton.Content = confirmLabel;
        dlg.CancelButton.Content = cancelLabel;
        // Title bar caption left blank on purpose — the bold body heading is
        // the title (native macOS alerts carry no title-bar text); duplicating
        // it in the caption reads as redundant/janky.
        await dlg.ShowDialog(owner);
        return dlg._confirmed;
    }

    /// <summary>Same as <see cref="AskAsync"/> but with a "Don't show this
    /// again" checkbox below the message. Returns (Confirmed,
    /// SuppressFuture). Suppress is always false when Confirmed is false —
    /// dismissing the dialog without accepting can't both reject *and* tell
    /// us to hide future prompts, that's a confusing combo.</summary>
    public static async Task<(bool Confirmed, bool SuppressFuture)> AskWithSuppressAsync(Window owner, string title, string message, string confirmLabel = "Confirm")
    {
        var dlg = new ConfirmationDialog();
        dlg.TitleText.Text = title;
        dlg.MessageText.Text = message;
        dlg.ConfirmButton.Content = confirmLabel;
        // Title bar caption left blank on purpose — see AskAsync.
        dlg.SuppressCheckbox.IsVisible = true;
        await dlg.ShowDialog(owner);
        var confirmed = dlg._confirmed;
        var suppress = confirmed && dlg.SuppressCheckbox.IsChecked == true;
        return (confirmed, suppress);
    }

    /// <summary>Three-way variant: Cancel / <paramref name="alternateLabel"/> /
    /// <paramref name="primaryLabel"/>. The primary (right-most, accented)
    /// button is still the Enter default; the middle button surfaces a second
    /// affirmative choice without a second dialog. Returns
    /// <see cref="ConfirmationChoice.Cancel"/> on dismiss.</summary>
    public static async Task<ConfirmationChoice> AskThreeWayAsync(Window owner, string title, string message,
        string primaryLabel, string alternateLabel, string cancelLabel = "Cancel")
    {
        var dlg = new ConfirmationDialog();
        dlg.TitleText.Text = title;
        dlg.MessageText.Text = message;
        dlg.ConfirmButton.Content = primaryLabel;
        dlg.AltButton.Content = alternateLabel;
        dlg.AltButton.IsVisible = true;
        dlg.CancelButton.Content = cancelLabel;
        await dlg.ShowDialog(owner);
        return dlg._choice;
    }

    private void OnConfirmClick(object? sender, RoutedEventArgs e)
    {
        _confirmed = true;
        _choice = ConfirmationChoice.Primary;
        Close();
    }

    private void OnAltClick(object? sender, RoutedEventArgs e)
    {
        _choice = ConfirmationChoice.Alternate;
        Close();
    }

    private void OnCancelClick(object? sender, RoutedEventArgs e)
    {
        _confirmed = false;
        _choice = ConfirmationChoice.Cancel;
        Close();
    }
}
