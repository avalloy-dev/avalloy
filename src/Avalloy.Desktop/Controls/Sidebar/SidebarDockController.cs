using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalloy.AttachedProperties;
using Avalloy.Views.Controls;

namespace Avalloy.Controls.Sidebar;

/// <summary>Geometry + feel constants for <see cref="SidebarDockController"/>. The defaults are the
/// tuned siderail values; override per app only where the anatomy differs.</summary>
public sealed class SidebarDockOptions
{
    /// <summary>The collapsed rail's SURFACE width — the reserved column never goes narrower.</summary>
    public double RailWidth { get; init; } = 76;

    /// <summary>The surface's own pane margins inside the reserved column (left + right). Zero when
    /// the sidebar control stretches to fill the column edge-to-edge.</summary>
    public double SurfaceMargins { get; init; } = 15;

    public double MinDockedWidth { get; init; } = 200;
    public double MaxDockedWidth { get; init; } = 480;

    /// <summary>Pin/unpin morph duration — matches a .28s cubic ease-out surface morph.</summary>
    public double MorphMs { get; init; } = 280;
}

/// <summary>
/// The window-side siderail mechanics, extracted whole so the feel survives reuse:
/// <list type="bullet">
///   <item>The RESERVED COLUMN: rail width unpinned; the persisted, clamped docked width pinned —
///     with the cubic-ease dock morph on pin/unpin (clamps widen for the transit and land on the
///     state's real min/max with the final frame).</item>
///   <item>Live width tracking while pinned: syncs the surface's docked width every layout pass
///     (dock-animation frames and splitter drags included) and persists a user resize via
///     <see cref="ISidebarHost.SavedDockedWidth"/>.</item>
///   <item>The EDGE PULL on the seam: a PASSIVE seam watcher (tunnel PointerMoved, handledEventsToo
///     — a hit-testable hover strip would steal a GridSplitter's drag) with a dwell before the
///     reveal; tap toggles the pin; when pinned, a ≥3px drag resizes the column instead (the Tap
///     that follows a drag is swallowed).</item>
/// </list>
/// The host's pin state stays the app's: flip it in <see cref="ISidebarHost.TogglePin"/>, then call
/// <see cref="NotifyPinChanged"/> (from the shell's property-changed watcher) to run the morph.
/// The <see cref="DrawerPull"/> should start Opacity=0 + IsHitTestVisible=false with an Opacity
/// transition — the controller only flips those two properties.
/// </summary>
public sealed class SidebarDockController
{
    private readonly TopLevel _topLevel;
    private readonly Grid _bodyGrid;
    private readonly int _column;
    private readonly DrawerPull _pullTab;
    private readonly Func<ISidebarHost?> _host;
    private readonly SidebarDockOptions _o;
    private readonly Action<double>? _onSurfaceWidth;
    private readonly HoverIntent _seamIntent;

    private double RailReserved => _o.RailWidth + _o.SurfaceMargins;

    private int _widthGen;
    private double _lastPersistedWidth;

    /// <param name="onSurfaceWidth">Optional: receives the surface width (column minus
    /// <see cref="SidebarDockOptions.SurfaceMargins"/>) every layout pass while pinned — for a
    /// morphing overlay surface that must track the reserved column live.</param>
    public SidebarDockController(
        TopLevel topLevel, Grid bodyGrid, int column, DrawerPull pullTab,
        Func<ISidebarHost?> host, SidebarDockOptions? options = null,
        Action<double>? onSurfaceWidth = null)
    {
        _topLevel = topLevel;
        _bodyGrid = bodyGrid;
        _column = column;
        _pullTab = pullTab;
        _host = host;
        _o = options ?? new SidebarDockOptions();
        _onSurfaceWidth = onSurfaceWidth;

        ApplyReserved();

        // Track the column while pinned: keep the surface width in sync and persist a user resize
        // (the host's setter is expected to debounce the actual save).
        _bodyGrid.LayoutUpdated += OnBodyLayoutUpdated;

        // Edge pull: the passive seam watcher (tunnel fires for every move, even handled ones).
        _topLevel.AddHandler(InputElement.PointerMovedEvent, OnSeamPointerMoved,
            RoutingStrategies.Tunnel, handledEventsToo: true);
        _topLevel.AddHandler(InputElement.PointerExitedEvent, (_, _) => HidePull());
        // Reveal only after the pointer DWELLS in the band — a straight pass never flashes the pull.
        _seamIntent = new HoverIntent(SetPullRevealed);

        _pullTab.PointerPressed += OnPullPressed;
        _pullTab.PointerMoved += OnPullDragMoved;
        _pullTab.PointerReleased += OnPullReleased;
        _pullTab.Tapped += OnPullTapped;
    }

    /// <summary>Call when the host's pin state changed (its property watcher) — morphs the reserved
    /// column to the new state.</summary>
    /// <param name="animate">False to land docked without the morph. Restoring a persisted latch at
    /// startup is not a gesture: there's nothing for the morph to narrate, and running one competes
    /// with the load for the UI thread — which reads as the shell hanging half-open.</param>
    public void NotifyPinChanged(bool animate = true) => ApplyReserved(animate);

    private void OnBodyLayoutUpdated(object? sender, EventArgs e)
    {
        if (_host() is not { Pinned: true } host) return;
        var w = _bodyGrid.ColumnDefinitions[_column].ActualWidth;
        _onSurfaceWidth?.Invoke(Math.Max(w - _o.SurfaceMargins, _o.RailWidth));
        if (Math.Abs(w - _lastPersistedWidth) > 0.5 && w >= _o.MinDockedWidth)
        {
            _lastPersistedWidth = w;
            host.SavedDockedWidth = w;
        }
    }

    private void ApplyReserved(bool animate = false)
    {
        var col = _bodyGrid.ColumnDefinitions[_column];
        var host = _host();
        var pinned = host?.Pinned ?? false;
        var target = pinned
            ? Math.Clamp(host?.SavedDockedWidth ?? RailReserved, _o.MinDockedWidth, _o.MaxDockedWidth)
            : RailReserved;
        var (min, max) = pinned ? (_o.MinDockedWidth, _o.MaxDockedWidth) : (RailReserved, RailReserved);
        if (pinned) _onSurfaceWidth?.Invoke(Math.Max(col.ActualWidth - _o.SurfaceMargins, _o.RailWidth));

        var gen = ++_widthGen;
        var from = col.ActualWidth;
        if (!animate || Math.Abs(from - target) < 0.5)
        {
            col.MinWidth = min;
            col.MaxWidth = max;
            col.Width = new GridLength(target);
            return;
        }

        // Animate the dock so pin/unpin reads as the surface's morph. Clamps widen for the transit
        // and land on the state's real min/max with the final frame.
        col.MinWidth = Math.Min(min, Math.Min(from, target));
        col.MaxWidth = Math.Max(max, Math.Max(from, target));
        TimeSpan? start = null;
        void Frame(TimeSpan now)
        {
            if (gen != _widthGen) return;
            start ??= now;
            var t = Math.Clamp((now - start.Value).TotalMilliseconds / _o.MorphMs, 0, 1);
            var eased = 1 - Math.Pow(1 - t, 3);   // cubic ease-out
            col.Width = new GridLength(from + (target - from) * eased);
            if (t >= 1)
            {
                col.MinWidth = min;
                col.MaxWidth = max;
                col.Width = new GridLength(target);
            }
            else _topLevel.RequestAnimationFrame(Frame);
        }
        _topLevel.RequestAnimationFrame(Frame);
    }

    // ── Edge pull reveal (dwell in the seam band) ───────────────────────────────────────────────

    private bool _pullRevealed;

    private void OnSeamPointerMoved(object? sender, PointerEventArgs e)
    {
        var inBand = false;
        if (_host() is { EdgePullAllowed: true })
        {
            // Seam = the reserved column's edge (docked or resting rail alike).
            var seam = _bodyGrid.ColumnDefinitions[_column].ActualWidth;
            var p = e.GetPosition(_bodyGrid);
            inBand = p.X >= seam - 14 && p.X <= seam + 11
                  && p.Y >= 0 && p.Y <= _bodyGrid.Bounds.Height;
        }
        if (inBand) _seamIntent.Enter();
        else HidePull();
    }

    private void HidePull() => _seamIntent.Leave();

    private void SetPullRevealed(bool reveal)
    {
        if (reveal == _pullRevealed) return;
        _pullRevealed = reveal;
        _pullTab.Opacity = reveal ? 1 : 0;          // fades via the tab's Opacity transition
        _pullTab.IsHitTestVisible = reveal;         // click-through when hidden
    }

    // ── Edge pull drag/tap: drag resizes the DOCKED sidebar; a press only becomes a drag after
    //    3px of travel — under that, release Taps (toggle pin) as usual. ─────────────────────────

    private bool _dragActive;
    private bool _dragged;
    private double _dragStartX;
    private double _dragStartWidth;

    private void OnPullPressed(object? sender, PointerPressedEventArgs e)
    {
        _dragged = false;
        if (_host() is not { Pinned: true }) return;
        if (!e.GetCurrentPoint(_pullTab).Properties.IsLeftButtonPressed) return;
        _dragActive = true;
        _dragStartX = e.GetPosition(_bodyGrid).X;
        _dragStartWidth = _bodyGrid.ColumnDefinitions[_column].ActualWidth;
        e.Pointer.Capture(_pullTab);
    }

    private void OnPullDragMoved(object? sender, PointerEventArgs e)
    {
        if (!_dragActive) return;
        var dx = e.GetPosition(_bodyGrid).X - _dragStartX;
        if (!_dragged && Math.Abs(dx) < 3) return;
        _dragged = true;
        _widthGen++;   // cancel any in-flight dock animation
        var col = _bodyGrid.ColumnDefinitions[_column];
        col.Width = new GridLength(Math.Clamp(_dragStartWidth + dx, col.MinWidth, col.MaxWidth));
    }

    private void OnPullReleased(object? sender, PointerReleasedEventArgs e)
    {
        _dragActive = false;
        // _dragged stays set until OnPullTapped so the Tap after a drag doesn't ALSO toggle.
    }

    private void OnPullTapped(object? sender, TappedEventArgs e)
    {
        if (_dragged) { _dragged = false; return; }
        _host()?.TogglePin();
    }
}
