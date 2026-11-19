using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalloy.Views;

namespace Avalloy.Services;

/// <summary>
/// Default dialog implementation parented to the current main window. Confirms
/// route through the themed <see cref="ConfirmationDialog"/> so every Avalloy
/// app gets a consistent, themed prompt; <see cref="ShowInfoAsync"/> stays
/// on the bare programmatic window (no themed info dialog yet). Apps that want
/// richer interactions register their own <see cref="IDialogService"/>.
/// </summary>
public sealed class DialogService(ShellHost host) : IDialogService
{
    public Task ShowInfoAsync(string title, string message) =>
        ShowAsync(title, message, includeCancel: false);

    public Task<bool> ConfirmAsync(string title, string message, string confirmLabel = "Confirm")
    {
        // No main window (headless tests, early startup) — auto-confirm rather
        // than block on a dialog that can't be parented or shown modally.
        if (host.MainWindow is not { } owner) return Task.FromResult(true);
        return ConfirmationDialog.AskAsync(owner, title, message, confirmLabel);
    }

    public Task<(bool Confirmed, bool SuppressFuture)> ConfirmWithSuppressAsync(string title, string message, string confirmLabel = "Confirm")
    {
        if (host.MainWindow is not { } owner) return Task.FromResult((true, false));
        return ConfirmationDialog.AskWithSuppressAsync(owner, title, message, confirmLabel);
    }

    private Task<bool> ShowAsync(string title, string message, bool includeCancel, string? primaryLabel = null)
    {
        var tcs = new TaskCompletionSource<bool>();

        var primary = new Button
        {
            Content = primaryLabel ?? (includeCancel ? "OK" : "Close"),
            MinWidth = 80,
        };
        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            HorizontalAlignment = HorizontalAlignment.Right,
        };
        if (includeCancel)
        {
            var cancel = new Button { Content = "Cancel", MinWidth = 80 };
            buttons.Children.Add(cancel);
        }
        buttons.Children.Add(primary);

        var dialog = new Window
        {
            Title = title,
            Width = 400,
            Height = 180,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false,
            Content = new StackPanel
            {
                Margin = new Thickness(20),
                Spacing = 18,
                Children =
                {
                    new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap },
                    buttons,
                },
            },
        };

        primary.Click += (_, _) => { tcs.TrySetResult(true); dialog.Close(); };
        if (includeCancel && buttons.Children[0] is Button cancelBtn)
            cancelBtn.Click += (_, _) => { tcs.TrySetResult(false); dialog.Close(); };
        dialog.Closed += (_, _) => tcs.TrySetResult(false);

        if (host.MainWindow is { } parent)
            _ = dialog.ShowDialog(parent);
        else
            dialog.Show();
        return tcs.Task;
    }
}
