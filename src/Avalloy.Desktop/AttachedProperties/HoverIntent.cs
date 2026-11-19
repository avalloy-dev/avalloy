using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

namespace Avalloy.AttachedProperties;

/// <summary>
/// Hover-intent for hover-revealed affordances (edge pulls, header-strip nudgers, seam handles): the
/// affordance wakes only after the pointer DWELLS in its zone — a straight pass across it never
/// flashes chrome. Hide is instant on leave, unless a leave grace is set. Avalonia has no built-in
/// hover-intent outside ToolTip.ShowDelay, hence this.
///
/// Two ways in:
/// - Declarative (the hover zone IS an element): <c>alloy:HoverIntent.RevealClass="reveal"</c> on
///   the zone — after the dwell the class is added to it (style off <c>.reveal</c>), leave removes it
///   instantly. No code-behind.
/// - Imperative (the zone is pointer math, e.g. seam bands): construct with a setRevealed callback
///   and drive <see cref="Enter"/>/<see cref="Leave"/> from your own pointer tracking.
///
/// Scrolling counts as motion: put <c>alloy:HoverIntent.RearmOnScroll="True"</c> on a ScrollViewer and
/// every armed intent inside it re-arms whenever the content moves — see <see cref="RearmWithin"/>.
///
/// When the revealed affordance sits APART from its zone (a copy button beside a chip, a card under a link),
/// give the intent a leave grace: leaving keeps it revealed that long, so the pointer can cross the gap, and the
/// affordance itself calls <see cref="Enter"/> and <see cref="Leave"/> as the pointer enters and leaves it.
///
/// Every timer counts on <see cref="Time"/> (or the clock handed to the constructor), so a test drives the
/// dwell with a fake clock instead of waiting it out.
/// </summary>
public sealed class HoverIntent
{
    /// <summary>The shared dwell (ms) before a hover-revealed affordance shows.</summary>
    public const int DwellMs = 220;

    /// <summary>The clock an intent counts on when none is handed to its constructor, the attached behavior's
    /// included. Read when the intent is made, so set it before the views that use it are built. Tests set a fake
    /// one; an app leaves it alone.</summary>
    public static TimeProvider Time { get; set; } = TimeProvider.System;

    private readonly UiTimer _dwell;
    private readonly UiTimer? _grace;
    private readonly Action<bool> _setRevealed;
    // Set only on the attached-behavior path — it scopes RearmWithin to one subtree. Imperative
    // intents (seam bands) have no host and are therefore never re-armed by someone else's scroll.
    private readonly Control? _host;
    private bool _inZone;
    private bool _listed;

    /// <param name="setRevealed">Applies the reveal/hide (opacity, class, hit-test) — called with
    /// <c>true</c> after the dwell elapses, <c>false</c> on leave (or once the leave grace runs out). Must be
    /// idempotent.</param>
    /// <param name="dwellMs">How long the pointer must rest in the zone before the reveal.</param>
    /// <param name="leaveGraceMs">How long a revealed affordance outlives a leave; 0 hides at once.</param>
    /// <param name="time">The clock to count on; <see cref="Time"/> when null.</param>
    public HoverIntent(Action<bool> setRevealed, int dwellMs = DwellMs, int leaveGraceMs = 0, TimeProvider? time = null)
        : this(setRevealed, null, dwellMs, leaveGraceMs, time) { }

    private HoverIntent(Action<bool> setRevealed, Control? host, int dwellMs, int leaveGraceMs, TimeProvider? time)
    {
        _setRevealed = setRevealed;
        _host = host;
        var clock = time ?? Time;
        _dwell = new UiTimer(clock, TimeSpan.FromMilliseconds(dwellMs), () => Apply(true));
        if (leaveGraceMs > 0) _grace = new UiTimer(clock, TimeSpan.FromMilliseconds(leaveGraceMs), Hide);
    }

    /// <summary>Whether the affordance is revealed now: the dwell landed, and nothing has hidden it since.</summary>
    public bool IsRevealed { get; private set; }

    /// <summary>Pointer entered the hover zone (or the revealed affordance) — start the dwell countdown, or, if
    /// the affordance is already up, keep it there.</summary>
    public void Enter()
    {
        _grace?.Stop();
        _inZone = true;
        List();
        if (!IsRevealed) _dwell.Start();
    }

    /// <summary>Pointer left the hover zone — cancel any pending reveal and hide, at once or after the leave
    /// grace when the affordance is up.</summary>
    public void Leave()
    {
        _dwell.Stop();
        _inZone = false;
        if (IsRevealed && _grace is not null) { _grace.Start(); return; }
        Unlist();
        Apply(false);
    }

    /// <summary>Hide now, whatever the pointer is doing, and forget the zone: a press that acts on the
    /// affordance, or a zone torn out of the tree. The next <see cref="Enter"/> earns the dwell again.</summary>
    public void Hide()
    {
        _dwell.Stop();
        _grace?.Stop();
        _inZone = false;
        Unlist();
        Apply(false);
    }

    // Hide now and restart the countdown: the pointer is still in the zone, but the zone MOVED, so the
    // dwell has to be re-earned. Reveal lands ~DwellMs after the motion settles. An intent out of its zone
    // and only waiting out its grace has nothing to re-earn: it just goes.
    private void Rearm()
    {
        if (!_inZone) { Hide(); return; }
        _dwell.Stop();
        Apply(false);
        _dwell.Start();
    }

    private void Apply(bool revealed)
    {
        IsRevealed = revealed;
        _setRevealed(revealed);
    }

    private void List() { if (!_listed) { _listed = true; _armed.Add(this); } }
    private void Unlist() { if (_listed) { _listed = false; _armed.Remove(this); } }

    // ── Motion gating ────────────────────────────────────────────────────────────────────────────────
    // Every intent that has been entered and not yet hidden (in its zone, or waiting out its grace). Bounded by
    // how many nested hover zones one pointer can be inside (in practice 1–2); Leave(), Hide() and
    // DetachedFromVisualTree all retire entries.
    private static readonly List<HoverIntent> _armed = new();

    /// <summary>Re-arm every intent hosted inside <paramref name="scope"/>: hide it now and restart its
    /// dwell. Call when the content under the pointer MOVES (scroll, reflow) rather than the pointer —
    /// otherwise a row that merely slides past the cursor can sit there long enough to trip the dwell
    /// and flash its hover chrome, which reads as a strobe down a scrolling list.</summary>
    public static void RearmWithin(Visual scope)
    {
        for (var i = _armed.Count - 1; i >= 0; i--)
        {
            var intent = _armed[i];
            if (intent._host is { } host && scope.IsVisualAncestorOf(host)) intent.Rearm();
        }
    }

    // ── Attached behavior ────────────────────────────────────────────────────────────────────────────
    // One instance per host, keyed weakly so retired visuals don't pin intents (or vice versa).
    private static readonly ConditionalWeakTable<Control, HoverIntent> _intents = new();

    /// <summary>The style class the dwell adds to the element (and leave removes instantly).
    /// Setting it arms the behavior; clearing to null/empty disarms it.</summary>
    public static readonly AttachedProperty<string?> RevealClassProperty =
        AvaloniaProperty.RegisterAttached<HoverIntent, Control, string?>("RevealClass");

    public static string? GetRevealClass(Control element) => element.GetValue(RevealClassProperty);
    public static void SetRevealClass(Control element, string? value) => element.SetValue(RevealClassProperty, value);

    /// <summary>Put on a ScrollViewer: content motion inside it re-arms the dwell of any hover zone the
    /// pointer is currently in, so hover chrome never wakes on rows that are merely scrolling past.</summary>
    public static readonly AttachedProperty<bool> RearmOnScrollProperty =
        AvaloniaProperty.RegisterAttached<HoverIntent, Control, bool>("RearmOnScroll");

    public static bool GetRearmOnScroll(Control element) => element.GetValue(RearmOnScrollProperty);
    public static void SetRearmOnScroll(Control element, bool value) => element.SetValue(RearmOnScrollProperty, value);

    static HoverIntent()
    {
        RevealClassProperty.Changed.AddClassHandler<Control>(OnRevealClassChanged);
        RearmOnScrollProperty.Changed.AddClassHandler<Control>(OnRearmOnScrollChanged);
    }

    private static void OnRevealClassChanged(Control host, AvaloniaPropertyChangedEventArgs e)
    {
        if (_intents.TryGetValue(host, out var old))
        {
            old.Hide();
            _intents.Remove(host);
            host.PointerEntered -= OnHostEntered;
            host.PointerExited -= OnHostExited;
            host.DetachedFromVisualTree -= OnHostDetached;
        }
        if (e.NewValue is string cls && !string.IsNullOrWhiteSpace(cls))
        {
            _intents.Add(host, new HoverIntent(reveal =>
            {
                if (reveal) { if (!host.Classes.Contains(cls)) host.Classes.Add(cls); }
                else host.Classes.Remove(cls);
            }, host, DwellMs, 0, null));
            host.PointerEntered += OnHostEntered;
            host.PointerExited += OnHostExited;
            // A host can be torn out (or recycled by a virtualizing panel) while the pointer is still
            // over it, so PointerExited never arrives: retire it here or it stays in _armed forever and
            // comes back wearing a stale reveal class.
            host.DetachedFromVisualTree += OnHostDetached;
        }
    }

    private static void OnRearmOnScrollChanged(Control host, AvaloniaPropertyChangedEventArgs e)
    {
        host.RemoveHandler(ScrollViewer.ScrollChangedEvent, OnScopeScrolled);
        if (e.NewValue is true) host.AddHandler(ScrollViewer.ScrollChangedEvent, OnScopeScrolled);
    }

    private static void OnScopeScrolled(object? sender, ScrollChangedEventArgs e)
    {
        // Offset only. Extent churn alone (a streaming transcript growing below a parked reader) doesn't
        // move anything under the pointer — gating on it would suppress hover for as long as content
        // arrives. Auto-follow raises its own offset change, so pinned-to-bottom still counts as motion.
        if (Math.Abs(e.OffsetDelta.Y) < 0.5 && Math.Abs(e.OffsetDelta.X) < 0.5) return;
        if (sender is Visual scope) RearmWithin(scope);
    }

    // PointerEntered/Exited track IsPointerOver, which covers descendants — moving from the zone onto a
    // child (a chip, the − / ＋ themselves) does NOT count as leaving.
    private static void OnHostEntered(object? sender, PointerEventArgs e)
    { if (sender is Control c && _intents.TryGetValue(c, out var i)) i.Enter(); }

    private static void OnHostExited(object? sender, PointerEventArgs e)
    { if (sender is Control c && _intents.TryGetValue(c, out var i)) i.Leave(); }

    private static void OnHostDetached(object? sender, VisualTreeAttachmentEventArgs e)
    { if (sender is Control c && _intents.TryGetValue(c, out var i)) i.Hide(); }
}
