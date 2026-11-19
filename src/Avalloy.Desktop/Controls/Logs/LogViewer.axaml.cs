using Avalonia;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalloy.Services;

namespace Avalloy.Controls.Logs;

/// <summary>
/// Embeddable log panel: live tail over <see cref="LogBuffer"/> with level /
/// category / text filtering, pause, copy, and a "reveal log file" affordance.
/// Drop <c>&lt;logs:LogViewer/&gt;</c> anywhere — with no DataContext supplied it
/// self-wires to <see cref="AlloyLogging.Buffer"/> (set <see cref="Buffer"/> to
/// target another buffer). Ships its own styles (see LogViewerStyles.axaml), so
/// host apps need no style wiring. For a ready-made host window plus command,
/// see <see cref="LogViewerWindow"/> and <c>ShowLogViewerCommand</c>.
/// </summary>
public partial class LogViewer : UserControl
{
    /// <summary>Buffer to display when the control creates its own view model
    /// (i.e. no <see cref="LogViewerViewModel"/> DataContext was supplied).
    /// Null = <see cref="AlloyLogging.Buffer"/>.</summary>
    public static readonly StyledProperty<LogBuffer?> BufferProperty =
        AvaloniaProperty.Register<LogViewer, LogBuffer?>(nameof(Buffer));

    public LogBuffer? Buffer
    {
        get => GetValue(BufferProperty);
        set => SetValue(BufferProperty, value);
    }

    private LogViewerViewModel? _ownedVm;

    public LogViewer()
    {
        InitializeComponent();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        if (DataContext is not LogViewerViewModel)
        {
            _ownedVm = new LogViewerViewModel(Buffer ?? AlloyLogging.Buffer);
            DataContext = _ownedVm;
        }
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        if (_ownedVm is { } vm)
        {
            vm.Dispose();
            _ownedVm = null;
            DataContext = null;
        }
    }

    private async void OnCopyClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not LogViewerViewModel vm) return;
        var selected = EntriesList.SelectedItems?.OfType<LogEntryRow>().ToList();
        var text = vm.CopyText(selected);
        if (text.Length == 0) return;
        if (TopLevel.GetTopLevel(this)?.Clipboard is { } clipboard)
            await clipboard.SetTextAsync(text);
    }
}
