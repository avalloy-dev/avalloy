using System.Reflection;
using Avalonia.Controls;

namespace Avalloy.Controls.Logs;

/// <summary>
/// Ready-made non-modal host for <see cref="LogViewer"/>. Process-singleton:
/// <see cref="ShowOrActivate"/> re-activates the open window instead of
/// stacking a second tail over the same buffer. Apps typically don't construct
/// this directly — they register <c>ShowLogViewerCommand</c> and bind a key.
/// </summary>
public partial class LogViewerWindow : Window
{
    private static LogViewerWindow? _open;

    public LogViewerWindow()
    {
        InitializeComponent();
        var app = Assembly.GetEntryAssembly()?.GetName().Name ?? "Avalloy";
        Title = $"Logs — {app}";
    }

    /// <summary>Show the viewer window, or bring the already-open one forward.
    /// Non-modal (owned when <paramref name="owner"/> is supplied) so the app
    /// stays usable while tailing.</summary>
    public static void ShowOrActivate(Window? owner = null)
    {
        if (_open is { } existing)
        {
            existing.Activate();
            return;
        }

        var window = new LogViewerWindow();
        _open = window;
        window.Closed += (_, _) => _open = null;
        if (owner is not null) window.Show(owner);
        else window.Show();
    }
}
