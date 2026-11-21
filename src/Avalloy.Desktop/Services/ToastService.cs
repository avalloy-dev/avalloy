using Avalonia.Controls;
using Avalonia.Controls.Notifications;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace Avalloy.Services;

/// <summary>Severity tiers for toasts. <see cref="Notice"/> sits between
/// Success and Warning semantically: the action finished, but not everything
/// the user asked for happened — a skipped conflict, a canceled operation.
/// Nothing went WRONG (that's Warning/Error territory: locked files, I/O
/// failures), so it renders with the neutral Information visuals rather
/// than alarm colors. Appended last so existing numeric values are stable.</summary>
public enum ToastSeverity { Info, Success, Warning, Error, Notice }

/// <summary>One toast notification. Created via <see cref="ToastService"/>'s helpers.</summary>
public sealed record Toast(
    string Title,
    string? Message,
    ToastSeverity Severity,
    TimeSpan Duration,
    string? ActionLabel = null,
    Action? OnAction = null,
    string? SecondaryActionLabel = null,
    Action? OnSecondaryAction = null)
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public bool IsPersistent => Duration <= TimeSpan.Zero;
}

/// <summary>
/// App-wide facade for transient notifications. Backed by Avalonia's built-in
/// <see cref="WindowNotificationManager"/>. Call <see cref="Attach"/> from the
/// shell window once the top-level is available.
/// <para>Also the app's <see cref="IUserNotifier"/>: <see cref="AvalloyApp"/> registers this instance
/// under that interface, so a package with no UI (Avalloy.Updates) reaches the user through toasts
/// without referencing Avalloy.Desktop. Safe before <see cref="Attach"/> — those toasts queue.</para>
/// </summary>
public sealed class ToastService : IUserNotifier
{
    private WindowNotificationManager? _manager;
    private readonly object _gate = new();
    // Toasts posted before Attach (e.g. plugin-loader errors during app initialization)
    // queue here and flush once the top-level is wired up. Without the queue,
    // any startup feedback the user might care about would silently vanish.
    private readonly Queue<Toast> _pending = new();

    /// <summary>Attach to a top-level window. Idempotent; safe to call more than
    /// once if the window is recreated. Toasts that were posted before Attach
    /// are flushed in arrival order.</summary>
    public void Attach(TopLevel topLevel)
    {
        _manager = new WindowNotificationManager(topLevel)
        {
            Position = NotificationPosition.BottomRight,
            MaxItems = 5,
        };
        Toast[] pending;
        lock (_gate)
        {
            pending = _pending.ToArray();
            _pending.Clear();
        }
        foreach (var t in pending) Show(t);
    }

    public Guid Show(Toast toast)
    {
        // Marshal to the dispatcher so background callers (operation runner,
        // file watchers) don't need to know about the UI thread.
        Dispatcher.UIThread.Post(() =>
        {
            if (_manager is null)
            {
                // Buffer until Attach. Bounded to keep early-loop bugs from
                // ballooning memory before any UI exists.
                lock (_gate)
                {
                    _pending.Enqueue(toast);
                    while (_pending.Count > 32) _pending.Dequeue();
                }
                return;
            }

            // Toasts without action buttons map cleanly onto Avalonia's
            // built-in Notification — gets the standard NotificationCard
            // styling for free. Toasts with actions need custom content
            // since Notification only exposes a single OnClick.
            if (toast.OnAction is null && toast.OnSecondaryAction is null)
            {
                // A title-only toast is a single line. Avalonia's Notification
                // template always styles Title as a heading (SemiBold), so a
                // one-liner reads as bold "for no reason". Render it as a plain
                // body-weight line instead; the bold heading is reserved for
                // toasts that genuinely have a header + subheader (Message).
                // FontWeight is pinned explicitly because it's inherited — a bare
                // TextBlock would otherwise pick up any bold ancestor weight.
                if (string.IsNullOrEmpty(toast.Message))
                {
                    var line = new TextBlock
                    {
                        Text = toast.Title,
                        FontWeight = FontWeight.Normal,
                        TextWrapping = TextWrapping.Wrap,
                        // Same reason BuildActionContent sets one: a bare Control
                        // bypasses the INotification/string data templates that carry
                        // the 12 inset, landing flush against the card edges.
                        Margin = new Avalonia.Thickness(12),
                    };
                    line.Classes.Add("theme-text-body");
                    _manager.Show(line, MapType(toast.Severity),
                        toast.IsPersistent ? TimeSpan.Zero : toast.Duration);
                    return;
                }

                _manager.Show(new Notification(
                    toast.Title,
                    toast.Message,
                    MapType(toast.Severity),
                    toast.IsPersistent ? TimeSpan.Zero : toast.Duration));
                return;
            }

            _manager.Show(BuildActionContent(toast), MapType(toast.Severity),
                toast.IsPersistent ? TimeSpan.Zero : toast.Duration);
        });
        return toast.Id;
    }

    public Guid ShowInfo(string title, string? message = null, TimeSpan? duration = null) =>
        Show(new Toast(title, message, ToastSeverity.Info, duration ?? TimeSpan.FromSeconds(4)));

    public Guid ShowSuccess(string title, string? message = null, TimeSpan? duration = null) =>
        Show(new Toast(title, message, ToastSeverity.Success, duration ?? TimeSpan.FromSeconds(4)));

    /// <summary>"Done, but not everything happened" — skipped conflicts,
    /// canceled operations. Neutral visuals; see <see cref="ToastSeverity.Notice"/>.
    /// Duration sits between Success (4s) and Warning (6s): there's something
    /// to read, but nothing to be alarmed about.</summary>
    public Guid ShowNotice(string title, string? message = null, TimeSpan? duration = null) =>
        Show(new Toast(title, message, ToastSeverity.Notice, duration ?? TimeSpan.FromSeconds(5)));

    public Guid ShowWarning(string title, string? message = null, TimeSpan? duration = null) =>
        Show(new Toast(title, message, ToastSeverity.Warning, duration ?? TimeSpan.FromSeconds(6)));

    public Guid ShowError(string title, string? message = null, TimeSpan? duration = null) =>
        Show(new Toast(title, message, ToastSeverity.Error, duration ?? TimeSpan.FromSeconds(8)));

    // Explicit: the Show* methods return the toast's id, which IUserNotifier has no use for.
    void IUserNotifier.Info(string title, string message) => ShowInfo(title, message);
    void IUserNotifier.Success(string title, string message) => ShowSuccess(title, message);
    void IUserNotifier.Error(string title, string message) => ShowError(title, message);

    private static NotificationType MapType(ToastSeverity s) => s switch
    {
        ToastSeverity.Success => NotificationType.Success,
        ToastSeverity.Warning => NotificationType.Warning,
        ToastSeverity.Error => NotificationType.Error,
        // Notice deliberately shares Information's neutral visuals —
        // Avalonia's NotificationType is a closed 4-value enum, and neutral
        // is exactly the right read for "completed with caveats".
        _ => NotificationType.Information,
    };

    /// <summary>Builds a custom NotificationCard content with title + message +
    /// up to two action buttons. WindowNotificationManager hosts whatever we
    /// pass it inside its standard NotificationCard frame.
    /// <para>
    /// The margin is ours to set. NotificationCard's template presenter carries no
    /// padding — the inset normally comes from the built-in data templates for
    /// INotification and string, which each declare Margin="12". A custom Control
    /// like this one bypasses those templates and lands straight in the presenter,
    /// so without this it renders flush to the card edges. 12 matches the built-ins,
    /// so an action toast and a plain one line up.
    /// </para>
    /// <para>
    /// Only visible since FluentTheme: the previous theme's NotificationCard wrapped the
    /// presenter in a Border carrying the card's Padding, which covered custom
    /// content too.
    /// </para>
    /// <para>
    /// Pressing either button runs its action and then closes the card it is hosted in, even
    /// if the action throws: a toast with buttons is a question, and once it is answered it
    /// has to go, or a persistent one reads as "the button does nothing". The first press
    /// decides the toast; a second press while the card animates out does nothing.
    /// </para></summary>
    internal static Control BuildActionContent(Toast toast)
    {
        var stack = new StackPanel
        {
            Spacing = 6,
            Orientation = Orientation.Vertical,
            Margin = new Avalonia.Thickness(12),
        };
        stack.Children.Add(new TextBlock
        {
            Text = toast.Title,
            FontWeight = FontWeight.SemiBold,
            TextWrapping = TextWrapping.Wrap,
        });
        if (!string.IsNullOrEmpty(toast.Message))
        {
            stack.Children.Add(new TextBlock
            {
                Text = toast.Message,
                TextWrapping = TextWrapping.Wrap,
                Opacity = 0.85,
            });
        }
        var buttons = new StackPanel
        {
            Spacing = 8,
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Avalonia.Thickness(0, 4, 0, 0),
        };
        var answered = false;
        Button ActionButton(string label, Action action)
        {
            var b = new Button { Content = label };
            b.Click += (_, _) =>
            {
                if (answered) return;
                answered = true;
                try { action(); }
                finally { b.FindAncestorOfType<NotificationCard>()?.Close(); }
            };
            return b;
        }
        if (toast.OnSecondaryAction is not null && !string.IsNullOrEmpty(toast.SecondaryActionLabel))
            buttons.Children.Add(ActionButton(toast.SecondaryActionLabel, toast.OnSecondaryAction));
        if (toast.OnAction is not null && !string.IsNullOrEmpty(toast.ActionLabel))
            buttons.Children.Add(ActionButton(toast.ActionLabel, toast.OnAction));
        if (buttons.Children.Count > 0) stack.Children.Add(buttons);
        return stack;
    }
}
