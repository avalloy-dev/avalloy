using System;
using System.Linq;
using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace Avalloy.AttachedProperties;

/// <summary>
/// Opt-in "show while scrolling" reveal for a <see cref="ScrollViewer"/> styled
/// with the <c>OverlayScrollViewer</c> / <c>OverlayScrollBar</c> theme
/// (<c>avares://Avalloy.Desktop/Themes/OverlayScrollBars.axaml</c>).
/// Avalonia's <c>AllowAutoHide</c> only reveals the bar on pointer-over (its
/// ScrollBar does <c>IsExpanded = IsPointerOver</c> and never reacts to scroll);
/// this adds the macOS behaviour where the bar also fades in while content is
/// actually moving and fades out a short beat after it stops.
///
/// Enable with <c>ap:OverlayScrollbars.Reveal="True"</c> on a ScrollViewer —
/// directly, or via a style so it also reaches scrollers nested inside control
/// templates (e.g. a <see cref="TreeView"/>'s internal scroller).
/// </summary>
public static class OverlayScrollbars
{
    public static readonly AttachedProperty<bool> RevealProperty =
        AvaloniaProperty.RegisterAttached<ScrollViewer, bool>("Reveal", typeof(OverlayScrollbars));

    public static void SetReveal(ScrollViewer o, bool value) => o.SetValue(RevealProperty, value);
    public static bool GetReveal(ScrollViewer o) => o.GetValue(RevealProperty);

    /// <summary>Class the OverlayScrollBar theme styles to full opacity; toggled
    /// on the live scrollbar for whichever axis the content is moving on.</summary>
    private const string ScrollingClass = "scrolling";
    private static readonly TimeSpan HideDelay = TimeSpan.FromMilliseconds(1100);

    private static readonly ConditionalWeakTable<ScrollViewer, State> States = new();

    static OverlayScrollbars()
    {
        RevealProperty.Changed.AddClassHandler<ScrollViewer>(OnRevealChanged);
    }

    private static void OnRevealChanged(ScrollViewer sv, AvaloniaPropertyChangedEventArgs e)
    {
        // Always tear down before (re)wiring so a flip-flop can't double-subscribe.
        sv.AttachedToVisualTree -= OnAttached;
        sv.DetachedFromVisualTree -= OnDetached;
        Unwire(sv);

        if (!e.GetNewValue<bool>()) return;

        // Re-wire on every attach (tab switches detach/reattach the pane). Wire now
        // too — covers both the already-attached case (style applied to a templated
        // scroller after it entered the tree) and the not-yet-attached case (the
        // ScrollChanged subscription simply waits until it starts raising).
        sv.AttachedToVisualTree += OnAttached;
        sv.DetachedFromVisualTree += OnDetached;
        Wire(sv);
    }

    private static void OnAttached(object? sender, VisualTreeAttachmentEventArgs e)
    {
        if (sender is ScrollViewer sv) Wire(sv);
    }

    private static void OnDetached(object? sender, VisualTreeAttachmentEventArgs e)
    {
        if (sender is ScrollViewer sv) Unwire(sv);
    }

    private static void Wire(ScrollViewer sv)
    {
        if (States.TryGetValue(sv, out _)) return; // already wired
        States.Add(sv, new State());
        sv.ScrollChanged += OnScrollChanged;
    }

    private static void Unwire(ScrollViewer sv)
    {
        sv.ScrollChanged -= OnScrollChanged;
        if (States.TryGetValue(sv, out var state))
        {
            state.Dispose();
            States.Remove(sv);
        }
    }

    private static void OnScrollChanged(object? sender, ScrollChangedEventArgs e)
    {
        if (sender is not ScrollViewer sv) return;
        // Only react to real movement, not viewport/extent changes from navigation
        // or layout (those don't mean the user scrolled).
        var vertical = e.OffsetDelta.Y != 0;
        var horizontal = e.OffsetDelta.X != 0;
        if (!vertical && !horizontal) return;
        if (!States.TryGetValue(sv, out var state)) return;

        if (vertical) Reveal(state.GetBar(sv, Orientation.Vertical));
        if (horizontal) Reveal(state.GetBar(sv, Orientation.Horizontal));

        state.EnsureTimer();
        state.Timer!.Stop();
        state.Timer.Start();
    }

    private static void Reveal(ScrollBar? bar)
    {
        if (bar is null) return;
        if (!bar.Classes.Contains(ScrollingClass))
            bar.Classes.Add(ScrollingClass);
    }

    private sealed class State : IDisposable
    {
        private ScrollBar? _vertical;
        private ScrollBar? _horizontal;
        public DispatcherTimer? Timer;

        public ScrollBar? GetBar(ScrollViewer sv, Orientation orientation)
        {
            var cached = orientation == Orientation.Vertical ? _vertical : _horizontal;
            // Re-find when never seen, or when a template rebuild (e.g. theme swap)
            // orphaned the cached bar without a detach.
            if (cached is null || !cached.IsAttachedToVisualTree())
            {
                cached = sv.GetVisualDescendants()
                    .OfType<ScrollBar>()
                    .FirstOrDefault(b => b.Orientation == orientation);
                if (orientation == Orientation.Vertical) _vertical = cached;
                else _horizontal = cached;
            }
            return cached;
        }

        public void EnsureTimer()
        {
            if (Timer is not null) return;
            Timer = new DispatcherTimer { Interval = HideDelay };
            Timer.Tick += (_, _) =>
            {
                Timer!.Stop();
                // Pointer-over keeps it visible on its own (theme rule), so dropping
                // the scroll-reveal class here unconditionally is safe.
                RemoveScrollingClass();
            };
        }

        public void Dispose()
        {
            Timer?.Stop();
            Timer = null;
            // Clear the reveal too: a detach inside the hide window (tab switch
            // mid-scroll) would otherwise leave the bar pinned visible on reattach.
            RemoveScrollingClass();
            _vertical = null;
            _horizontal = null;
        }

        private void RemoveScrollingClass()
        {
            _vertical?.Classes.Remove(ScrollingClass);
            _horizontal?.Classes.Remove(ScrollingClass);
        }
    }
}
