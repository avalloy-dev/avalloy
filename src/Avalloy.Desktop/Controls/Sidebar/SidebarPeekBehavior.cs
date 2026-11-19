using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Avalloy.AttachedProperties;

namespace Avalloy.Controls.Sidebar;

/// <summary>
/// The hover-peek choreography for a morphing sidebar surface, extracted whole so the feel
/// survives reuse — the industry-standard hover-panel behavior, both halves:
/// <list type="bullet">
///   <item>OPEN through a <see cref="HoverIntent"/> dwell — a straight cursor pass across the
///     rail never flashes the sidebar open; only a genuine dwell peeks it.</item>
///   <item>CLOSE on pointer-leave after a short grace (an accidental exit/re-entry never flaps
///     it); re-entry cancels. Only TEXT ENTRY inside the surface (search, inline rename) — or a
///     caller-supplied <c>holdOpen</c> territory (satellite panels hanging off the edge) — holds
///     it open; a row click focusing a tree must not pin the peek forever.</item>
///   <item>The peek retracts the moment any item is CLICKED — except inside the hold-open
///     region (a search row is a place you stay) and scrollbar interactions (scrolling must
///     not slam it shut). Child chrome that marks its taps Handled never reaches this.</item>
///   <item>Focus leaving the surface retracts the peek — unless the pointer is still over it
///     (closing under the cursor would fight the hover).</item>
/// </list>
/// Wire-up is self-contained: construct once from the sidebar view's ctor. The host's
/// <see cref="ISidebarHost.PeekOpen"/>/<see cref="ISidebarHost.PeekClose"/> own the state; the
/// scrim's tap-to-close stays at the usage site (one line).
/// </summary>
public sealed class SidebarPeekBehavior
{
    private readonly Control _view;
    private readonly Border _surface;
    private readonly Func<ISidebarHost?> _host;
    private readonly Func<Control?>? _holdOpenRegion;
    private readonly Func<bool>? _holdOpen;
    private readonly HoverIntent _peekIntent;
    private readonly DispatcherTimer _closeGrace;

    /// <param name="view">The sidebar view root — its bubbling Tapped drives click-retract.</param>
    /// <param name="surface">The morphing surface border (hover + focus tracking).</param>
    /// <param name="holdOpenRegion">Optional: the region whose taps do NOT retract (search row).</param>
    /// <param name="holdOpen">Optional: extra hover territory that counts as being ON the surface —
    /// satellite panels anchored to its edge (footer flyovers). Retracting while the pointer is in one
    /// would yank the panel out from under the cursor mid-reach. Pair it with
    /// <see cref="ScheduleClose"/> from the satellite's own pointer-leave.</param>
    public SidebarPeekBehavior(
        Control view, Border surface, Func<ISidebarHost?> host, Func<Control?>? holdOpenRegion = null,
        Func<bool>? holdOpen = null)
    {
        _view = view;
        _surface = surface;
        _host = host;
        _holdOpenRegion = holdOpenRegion;
        _holdOpen = holdOpen;

        _peekIntent = new HoverIntent(reveal => { if (reveal) _host()?.PeekOpen(); });
        _closeGrace = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
        _closeGrace.Tick += (_, _) =>
        {
            _closeGrace.Stop();
            if (_surface.IsPointerOver) return;
            if (_holdOpen?.Invoke() == true) return;
            if (TextInputFocusedWithin()) return;
            _host()?.PeekClose();
        };

        _surface.PointerEntered += OnSurfaceEntered;
        _surface.PointerExited += OnSurfaceExited;
        _surface.LostFocus += (_, _) =>
        {
            if (_surface.IsKeyboardFocusWithin || _surface.IsPointerOver) return;
            if (_holdOpen?.Invoke() == true) return;
            _host()?.PeekClose();
        };
        // Bubble-phase: child chrome (chevrons, chips) that marks its taps Handled never trips this.
        _view.Tapped += OnAnyItemTapped;
    }

    private void OnSurfaceEntered(object? sender, PointerEventArgs e)
    {
        _closeGrace.Stop();
        if (_host() is { Open: false }) _peekIntent.Enter();   // dwell before peeking open
    }

    private void OnSurfaceExited(object? sender, PointerEventArgs e)
    {
        _peekIntent.Leave();          // cancel a pending open
        _closeGrace.Stop();           // grace-delayed close (re-checks pointer + text focus on fire)
        _closeGrace.Start();
    }

    /// <summary>Re-arm the grace close from OUTSIDE the surface — for a satellite panel that had been
    /// holding the peek open (see the <c>holdOpen</c> ctor arg) and has just lost the pointer. The tick
    /// re-checks everything, so calling this while the pointer is still on the surface is harmless.</summary>
    public void ScheduleClose() { _closeGrace.Stop(); _closeGrace.Start(); }

    /// <summary>True while the keyboard focus is a text box INSIDE the surface — the only focus
    /// that holds the peek open.</summary>
    private bool TextInputFocusedWithin() =>
        TopLevel.GetTopLevel(_view)?.FocusManager?.GetFocusedElement() is TextBox box
        && _surface.IsVisualAncestorOf(box);

    private void OnAnyItemTapped(object? sender, TappedEventArgs e)
    {
        if (_host() is not { Open: true, Pinned: false } host) return;
        if (e.Source is not Control source) return;
        if (_holdOpenRegion?.Invoke() is { } hold
            && (ReferenceEquals(source, hold) || hold.IsVisualAncestorOf(source))) return;
        if (source.FindAncestorOfType<ScrollBar>(includeSelf: true) is not null) return;
        host.PeekClose();
    }
}
