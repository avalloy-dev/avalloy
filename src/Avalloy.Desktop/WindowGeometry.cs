using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform;
using Avalonia.Reactive;
using Avalonia.Threading;
using Avalloy.Services;
using Microsoft.Extensions.Logging;

namespace Avalloy;

/// <summary>
/// Persisted window geometry section. Implement on a preferences POCO (typically
/// stored in the per-machine state file — geometry is display-bound, not roaming)
/// and hand it to <see cref="WindowGeometry.BindGeometry"/> to get restore-on-open
/// and save-on-close for free.
/// <para>
/// <see cref="PositionX"/>/<see cref="PositionY"/> are nullable: <c>null</c> means
/// "let the OS place the window" (first launch and after a reset). Width/Height
/// default to the window's design size in the model.
/// </para>
/// </summary>
public interface IWindowGeometry
{
    double Width { get; set; }
    double Height { get; set; }
    double? PositionX { get; set; }
    double? PositionY { get; set; }
}

/// <summary>An <see cref="IWindowGeometry"/> that also remembers whether the
/// window was maximized. When maximized, the stored Width/Height/position are the
/// pre-maximize bounds the OS restores to on un-maximize.</summary>
public interface IMaximizableWindowGeometry : IWindowGeometry
{
    bool IsMaximized { get; set; }
}

/// <summary>
/// Restores persisted size/position/maximized state onto a <see cref="Window"/>
/// and writes it back as it changes. Replaces the per-window copy-paste of this
/// logic.
/// </summary>
public static class WindowGeometry
{
    /// <summary>
    /// Apply <paramref name="prefs"/> to <paramref name="window"/> now, track every
    /// size/position/state change back into <paramref name="prefs"/> while the window
    /// lives, and wire a <see cref="Window.Closing"/> handler that captures the final
    /// geometry and flushes.
    /// <para>
    /// Call right after the window is constructed (so <c>MinWidth</c>/<c>MinHeight</c>
    /// from XAML are already set), before it opens — the native window is then created
    /// at its final frame and the user never sees a post-show resize.
    /// </para>
    /// <para>
    /// A saved position is only reapplied while its titlebar region still lands on a
    /// live screen, so a stale position from an unplugged monitor falls back to the
    /// window's XAML startup placement. A saved maximized state pre-sizes the unshown
    /// window to the target screen's work area so the first visible frame already reads
    /// as maximized; the remembered Normal frame is re-applied on the first un-maximize.
    /// </para>
    /// <para>
    /// Live tracking only mutates <paramref name="prefs"/> — persistence rides the
    /// owner's mutation-tracked debounced save, where wired. <paramref name="flush"/>
    /// is invoked synchronously inside the Closing handler after the final capture.
    /// Pass the owning <c>PreferencesService.SaveNow</c> here: on app quit the process
    /// exits before a debounced timer fires — so without a synchronous flush the
    /// position is silently lost and the window reopens centered every time.
    /// </para>
    /// </summary>
    public static void BindGeometry(this Window window, IWindowGeometry prefs, Action? flush = null)
    {
        // Restore. Width/Height guarded against the design minimum so a stale or
        // hand-edited file can't shrink the window below usable size.
        if (prefs.Width >= window.MinWidth) window.Width = prefs.Width;
        if (prefs.Height >= window.MinHeight) window.Height = prefs.Height;

        Screen? targetScreen = null;
        PixelPoint? restoredPosition = null;
        if (prefs.PositionX is { } x && prefs.PositionY is { } y)
        {
            // The probe point sits where the drag region would be, so a stale position
            // can never strand the window somewhere unreachable.
            targetScreen = ScreenContaining(window, new PixelPoint((int)x + 60, (int)y + 20));
            if (targetScreen is not null)
            {
                restoredPosition = new PixelPoint((int)x, (int)y);
                window.Position = restoredPosition.Value;
                // Manual stops the OS from re-centering over the position we just set.
                window.WindowStartupLocation = WindowStartupLocation.Manual;
            }
        }

        if (prefs is IMaximizableWindowGeometry { IsMaximized: true })
        {
            // Pre-size to the target screen's work area: the OS realizes the native
            // window at whatever frame it has when first shown, and applies Maximized
            // only around show — without this the user watches the Normal-sized frame
            // snap to full size. The Normal frame stays in prefs (capture below skips
            // it while maximized) and is re-applied on the first un-maximize.
            var normalWidth = window.Width;
            var normalHeight = window.Height;
            var normalPosition = restoredPosition;

            var screen = targetScreen ?? PrimaryScreen(window);
            if (screen is not null)
            {
                var area = screen.WorkingArea;
                window.Position = area.Position;
                window.WindowStartupLocation = WindowStartupLocation.Manual;
                window.Width = area.Width / screen.Scaling;
                window.Height = area.Height / screen.Scaling;
            }
            window.WindowState = WindowState.Maximized;

            IDisposable? once = null;
            once = window.GetObservable(Window.WindowStateProperty).Subscribe(
                new AnonymousObserver<WindowState>(state =>
                {
                    if (state != WindowState.Normal) return;
                    once?.Dispose();
                    if (normalWidth >= window.MinWidth) window.Width = normalWidth;
                    if (normalHeight >= window.MinHeight) window.Height = normalHeight;
                    if (normalPosition is { } p) window.Position = p;
                }));
        }

        // Track changes into prefs. Captures are Posted to the dispatcher rather than
        // taken inline: during a maximize the ClientSize change can arrive before
        // WindowState flips, and an inline capture would stamp the maximized size over
        // the remembered Normal frame.
        var capturePending = false;
        void QueueCapture()
        {
            if (capturePending) return;
            capturePending = true;
            Dispatcher.UIThread.Post(() =>
            {
                capturePending = false;
                Capture(window, prefs);
            }, DispatcherPriority.Background);
        }
        window.GetObservable(Window.ClientSizeProperty)
            .Subscribe(new AnonymousObserver<Size>(_ => QueueCapture()));
        window.GetObservable(Window.WindowStateProperty)
            .Subscribe(new AnonymousObserver<WindowState>(_ => QueueCapture()));
        window.PositionChanged += (_, _) => QueueCapture();

        window.Closing += (_, _) =>
        {
            try
            {
                Capture(window, prefs);

                // Force the write to disk now; the debounced save would be lost if
                // this close is part of app quit. See the param doc above.
                flush?.Invoke();
            }
            catch (Exception ex)
            {
                AlloyLogging.CreateLogger(nameof(WindowGeometry))
                    .LogError(ex, "Window geometry save failed");
            }
        };
    }

    private static void Capture(Window window, IWindowGeometry prefs)
    {
        switch (window.WindowState)
        {
            case WindowState.Normal:
                // Save the styled Width/Height — the exact properties restore sets, so
                // the frame round-trips without drift (ClientSize can differ from the
                // frame by platform chrome). NaN = auto-sized; fall back to ClientSize.
                prefs.Width = double.IsNaN(window.Width) ? window.ClientSize.Width : window.Width;
                prefs.Height = double.IsNaN(window.Height) ? window.ClientSize.Height : window.Height;
                prefs.PositionX = window.Position.X;
                prefs.PositionY = window.Position.Y;
                if (prefs is IMaximizableWindowGeometry normal) normal.IsMaximized = false;
                break;
            case WindowState.Maximized:
                // A maximized window reports full-screen bounds we don't want to restore
                // to — keep the last Normal frame; the flag re-maximizes on next launch.
                if (prefs is IMaximizableWindowGeometry max) max.IsMaximized = true;
                break;
            // Minimized / FullScreen are transient — leave the saved state alone.
        }
    }

    private static Screen? ScreenContaining(Window window, PixelPoint point)
    {
        try
        {
            foreach (var screen in window.Screens.All)
                if (screen.Bounds.Contains(point))
                    return screen;
        }
        catch
        {
            // Screens can be unavailable in headless/design contexts — treat as no match.
        }
        return null;
    }

    private static Screen? PrimaryScreen(Window window)
    {
        try
        {
            return window.Screens.Primary
                ?? (window.Screens.ScreenCount > 0 ? window.Screens.All[0] : null);
        }
        catch
        {
            return null;
        }
    }
}
