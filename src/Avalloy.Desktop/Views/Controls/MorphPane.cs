using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Rendering.Composition;
using Avalonia.Rendering.Composition.Animations;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace Avalloy.Views.Controls;

/// <summary>A pane whose WIDTH morphs on the RENDER THREAD — the drop-in replacement for a Border whose
/// Width carries a <c>DoubleTransition</c>.
///
/// <para>Animating layout Width costs a measure/arrange of the whole subtree on every one of the ~17 frames,
/// on the UI thread, so anything else busy on that thread (a chat view realizing, a big list materializing)
/// shows up as a stutter in the morph. A composition animation can't touch Width — the render thread only
/// owns the server visual's own properties — so this control re-expresses the same motion as a CLIP:</para>
/// <list type="bullet">
///   <item>Layout jumps straight to the target width, ONCE. Because the pane is a <see cref="Border"/>, its
///     <c>ClipToBounds</c> clip is a ROUNDED rect built from the composition visual's size (see Avalonia's
///     <c>ServerBorderVisual.PushClipToBounds</c>) — so an animation of that size sweeps a properly-rounded
///     edge across the content, and no corner is ever squared off mid-flight.</item>
///   <item>That size animation is an IMPLICIT one, keyed on "Size". Avalonia writes the visual's size from
///     the element's bounds on every sync, which would clobber an explicitly-started animation; registering
///     it implicitly turns that write into the TRIGGER instead, so the two can never desync. It is armed for
///     the length of one flight and taken back off at the settle — left standing it would also animate the
///     size writes that AREN'T morphs (first layout, window resize, splitter drag).</item>
///   <item>The child stays laid out at the WIDER of (from, to) for the length of the flight, so a collapse
///     has something to clip — content laid out at the narrow width would leave the shrinking clip with
///     nothing to reveal and the pane would just snap. It drops to the target when the flight lands, a beat
///     BEFORE the animation formally ends so that last layout pass hides under the ease-out's tail.</item>
/// </list>
///
/// <para>Net: one layout pass per toggle instead of one per frame, at the START of an expansion and at the
/// END of a collapse — i.e. never on the frame the eye is following. If the UI thread is busy the motion
/// still runs (the render thread owns it) and the deferred layout simply lands late, under a clip that is
/// already at rest, so a late frame can't be seen. The one case nothing can save: a UI thread hard-blocked
/// ACROSS the toggle itself never gets to start the animation.</para>
///
/// <para>Hit-testing follows the target width immediately (it reads the client-side size, not the animated
/// one), which is the safe direction: a retracting pane stops swallowing clicks at once rather than after
/// the motion.</para>
///
/// <para>Right-anchored chrome INSIDE the pane — an edge seam, a pin button — would otherwise sit at the
/// content width for the whole flight and pop into place at the end. Mark it <see cref="RideEdgeProperty"/>
/// and it rides the moving edge on the render thread too, via <c>Translation</c> (the one visual property
/// layout never writes back, so it survives a sync mid-flight).</para>
///
/// <para>Chrome contract: MorphPane paints NOTHING — it is a clip frame, so give it the pane's
/// <see cref="Border.CornerRadius"/> (that's the shape of the clip) and let its CHILD carry the fill, as a
/// Border with the same corner radius. The fill has to live in the child because the child is what stays
/// laid out at the flight width: a fill painted by the pane itself would follow the pane's layout width and
/// snap narrow the instant a collapse began, leaving the clip nothing to reveal. <c>Background</c>,
/// BorderBrush/BorderThickness and Padding on the pane are all ignored for the same reason.</para></summary>
public class MorphPane : Border
{
    /// <summary>The width to morph to. Set this instead of <c>Width</c> (styles included) — <c>Width</c> is
    /// what we're deliberately not animating. NaN = size to content, no morph.</summary>
    public static readonly StyledProperty<double> PaneWidthProperty =
        AvaloniaProperty.Register<MorphPane, double>(nameof(PaneWidth), double.NaN);

    /// <summary>Length of the morph. Zero = snap (and no implicit animation at all) — the setting for a pane
    /// being dragged live by a splitter, where an animation would rubber-band behind the pointer.</summary>
    public static readonly StyledProperty<TimeSpan> MorphDurationProperty =
        AvaloniaProperty.Register<MorphPane, TimeSpan>(nameof(MorphDuration), TimeSpan.FromMilliseconds(280));

    public static readonly StyledProperty<Easing> MorphEasingProperty =
        AvaloniaProperty.Register<MorphPane, Easing>(nameof(MorphEasing), new SplineEasing(0.22, 1, 0.36, 1));

    /// <summary>How far into the flight the content is handed back to layout, 0–1. That settle is one layout
    /// pass over the whole subtree and it changes shape discretely — rows stop being full-bleed and their
    /// selection chrome closes into rail chips — so WHEN it happens is the only lever on how visible it is.
    ///
    /// <para>A pane that paints its own fill has to stay late (the default 0.75, where an ease-out spline is
    /// already within a fraction of a pixel): settle it early and the fill narrows out from under the clip,
    /// leaving a sliver of whatever is behind the pane. A content-only pane stacked over a SEPARATE fill pane
    /// has no such floor — it can settle early, while the edge is still visibly sweeping, which reads as part
    /// of the collapse rather than a thing that happens after it.</para>
    ///
    /// <para>Not validated against the easing: with a linear or ease-IN curve, 0.75 of the time really is only
    /// 0.75 of the distance, and the settle will show. Pair an early fraction with an ease-out.</para></summary>
    public static readonly StyledProperty<double> SettleFractionProperty =
        AvaloniaProperty.Register<MorphPane, double>(nameof(SettleFraction), 0.75);

    /// <summary>Set on a right-anchored descendant so it tracks the animated edge instead of the content
    /// width. Registration is by visual-tree attachment, so it costs nothing per frame and nothing per
    /// flight — only the handful of marked elements are ever touched.</summary>
    public static readonly AttachedProperty<bool> RideEdgeProperty =
        AvaloniaProperty.RegisterAttached<MorphPane, Visual, bool>("RideEdge");

    /// <summary>Which pane a rider registered with — so a detach can unregister without walking a tree
    /// that's already being torn down.</summary>
    private static readonly AttachedProperty<MorphPane?> RiderOwnerProperty =
        AvaloniaProperty.RegisterAttached<MorphPane, Visual, MorphPane?>("RiderOwner");

    public static void SetRideEdge(Visual element, bool value) => element.SetValue(RideEdgeProperty, value);
    public static bool GetRideEdge(Visual element) => element.GetValue(RideEdgeProperty);

    static MorphPane()
    {
        AffectsMeasure<MorphPane>(PaneWidthProperty);
        RideEdgeProperty.Changed.AddClassHandler<Visual>((v, e) => OnRideEdgeChanged(v, e.GetNewValue<bool>()));
    }

    public MorphPane() => ClipToBounds = true;   // the clip IS the morph

    public double PaneWidth
    {
        get => GetValue(PaneWidthProperty);
        set => SetValue(PaneWidthProperty, value);
    }

    public TimeSpan MorphDuration
    {
        get => GetValue(MorphDurationProperty);
        set => SetValue(MorphDurationProperty, value);
    }

    public Easing MorphEasing
    {
        get => GetValue(MorphEasingProperty);
        set => SetValue(MorphEasingProperty, value);
    }

    /// <summary>The width the CHILD is laid out at: the target at rest, the wider of (from, to) in flight.
    /// NaN until the first PaneWidth lands.</summary>
    private double _contentWidth = double.NaN;

    /// <summary>The width the clip is resting at / animating toward — the last PaneWidth we acted on.</summary>
    private double _shown = double.NaN;

    private readonly List<Visual> _riders = [];
    private DispatcherTimer? _settle;

    /// <summary>Is startup over? Nothing morphs until it is. A shell coming up hands out a whole sequence of
    /// widths as its state lands — a style default, then open/pinned as the view model reads them back from
    /// preferences, then the restored pinned width once the reserved column resolves — and animating those
    /// makes the pane bounce through them while the app is still loading. Latched from an ApplicationIdle
    /// post, which by definition can't run until that queue has drained.</summary>
    private bool _ready;

    // ── Layout: the pane measures at the target, the child at the flight width ─────────────────────────

    protected override Size MeasureOverride(Size availableSize)
    {
        var content = ContentWidth(availableSize.Width);
        Child?.Measure(new Size(content, availableSize.Height));

        var width = double.IsNaN(PaneWidth) ? Child?.DesiredSize.Width ?? 0 : PaneWidth;
        return new Size(width, Child?.DesiredSize.Height ?? 0);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        // Deliberately wider than finalSize during a flight: the overflow is what the shrinking clip has left
        // to reveal. Nothing else can see it — the clip bounds hit-testing too.
        Child?.Arrange(new Rect(0, 0, ContentWidth(finalSize.Width), finalSize.Height));
        return finalSize;
    }

    private double ContentWidth(double available) =>
        !double.IsNaN(_contentWidth) ? _contentWidth
        : !double.IsNaN(PaneWidth) ? PaneWidth
        : available;

    // ── The morph ──────────────────────────────────────────────────────────────────────────────────────

    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);
        Dispatcher.UIThread.Post(() => _ready = true, DispatcherPriority.ApplicationIdle);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == PaneWidthProperty) OnPaneWidthChanged(change.GetNewValue<double>());
    }

    private void OnPaneWidthChanged(double to)
    {
        if (double.IsNaN(to)) return;

        var from = _shown;
        if (Math.Abs(to - from) < 0.5) return;   // NaN compares false — a first width still lands below
        _shown = to;

        // Snap when there's nowhere to animate from (the first width, or any width handed out before the pane
        // has ever been painted), when the caller asked for none (a live splitter drag), or when there's no
        // composition visual yet to carry the flight.
        if (double.IsNaN(from) || !_ready || MorphDuration <= TimeSpan.Zero || !TryAttachImplicits())
        {
            Settle();
            return;
        }

        // Never narrow the content mid-flight: an interrupted morph (re-open during a collapse) must keep
        // whatever it was already laid out at, or the clip sweeping back out would find nothing there.
        _contentWidth = Math.Max(double.IsNaN(_contentWidth) ? from : _contentWidth, to);
        InvalidateMeasure();

        AnimateRiders(from, to);

        // The clip animation itself needs no start call — layout is about to write the new size onto the
        // visual, and the implicit animation turns that write into the flight.
        _settle ??= NewSettleTimer();
        _settle.Stop();
        _settle.Interval = SettleDelay();
        _settle.Start();
    }

    public double SettleFraction
    {
        get => GetValue(SettleFractionProperty);
        set => SetValue(SettleFractionProperty, value);
    }

    /// <summary>When to hand the content back to layout. Waiting the FULL duration puts the settle's one
    /// layout pass — the expensive one, the whole subtree — after the motion has visibly finished, which
    /// reads as a beat of lag before the pane's final state (rail selection chrome, trimmed labels) appears.
    /// An ease-out spline is within a fraction of a pixel of its target long before it formally ends, so
    /// land it under the tail instead — or earlier still, under the motion itself, where a content-only pane
    /// can go. See <see cref="SettleFractionProperty"/> for which panes can and can't.</summary>
    private TimeSpan SettleDelay() =>
        TimeSpan.FromTicks((long)(MorphDuration.Ticks * Math.Clamp(SettleFraction, 0, 1)));

    private DispatcherTimer NewSettleTimer()
    {
        var timer = new DispatcherTimer();
        timer.Tick += (_, _) => { timer.Stop(); Settle(); };
        return timer;
    }

    /// <summary>Land the flight: the implicit animation comes back off, content drops to the target width and
    /// the riders hand their offset back to layout. All in one beat, ahead of the layout pass, so they commit
    /// together — no flash. Landing LATE (a busy UI thread) is invisible: the clip is already at rest and the
    /// riders are already holding their end value.</summary>
    private void Settle()
    {
        _settle?.Stop();

        // Off FIRST: content narrowing doesn't resize the pane, but anything else that re-syncs the visual in
        // this beat would otherwise be read as a fresh morph.
        if (ElementComposition.GetElementVisual(this) is { } visual) visual.ImplicitAnimations = null;

        if (double.IsNaN(_contentWidth) || Math.Abs(_contentWidth - _shown) > 0.5)
        {
            _contentWidth = _shown;
            InvalidateMeasure();
        }

        foreach (var rider in _riders)
            if (ElementComposition.GetElementVisual(rider) is { } riderVisual)
                LandRider(riderVisual);
    }

    /// <summary>Return one rider's <c>Translation</c> to zero — as a one-shot RAMP, never an assignment.
    ///
    /// <para>Two reasons an assignment cannot do this, and they compound. A composition animation writes
    /// every frame straight into the SERVER-side property field, so the client-side backing field never sees
    /// those values; the generated client setter is equality-filtered, so writing the value it already
    /// believes it holds (zero) is dropped before it reaches the server. And <see cref="Settle"/> runs at
    /// <see cref="SettleFraction"/> of the flight — mid-animation by design — so the rider animation is still
    /// running and would overwrite the assignment even if it landed.</para>
    ///
    /// <para>The visible bug was a rider stuck at a stale offset FOREVER, because nothing later resets it.
    /// Only shrinking flights show it: <see cref="AnimateRiders"/> ends at <c>to - _contentWidth</c>, which is
    /// zero when growing (content is already the target width) but negative when narrowing — the rider is
    /// riding an edge that is still sweeping inward. Unpinning a widened sidebar to a narrower peek left the
    /// pane's pin button that far inside the pane, on top of the content.</para>
    ///
    /// <para>Effectively instantaneous, and deliberately so: settling narrows the content to the target width
    /// in this same beat, which moves the rider's layout base out by exactly the offset being cancelled. The
    /// two changes annul each other, so the rider does not move on screen — that is what lets the reset
    /// happen mid-flight without a flash. A count-based instance interpolates from the live server value and
    /// holds its last keyframe once past its duration, so it lands on zero and stays there.</para></summary>
    private static void LandRider(CompositionVisual visual)
    {
        var ramp = visual.Compositor.CreateVector3DKeyFrameAnimation();
        ramp.Target = "Translation";
        ramp.Duration = TimeSpan.FromMilliseconds(1);
        ramp.InsertExpressionKeyFrame(0f, "this.StartingValue");
        ramp.InsertKeyFrame(1f, default);
        visual.StartAnimation("Translation", ramp);
    }

    /// <summary>Arm the "Size" implicit animation, for the length of ONE flight — <see cref="Settle"/> takes it
    /// back off. It has to be armed on demand, not left standing: an implicit animation fires on EVERY write of
    /// the property, and layout writes the visual's size for reasons that are not morphs — the first layout
    /// (0 → full width, which a pinned sidebar would then sweep open through the whole of startup), a window
    /// resize, a splitter drag. Only the width changes we initiate should move.</summary>
    private bool TryAttachImplicits()
    {
        if (ElementComposition.GetElementVisual(this) is not { } visual) return false;

        var animation = visual.Compositor.CreateVectorKeyFrameAnimation();
        animation.Target = "Size";   // implicit animations are dispatched through the group API, which requires it
        animation.Duration = MorphDuration;
        // Width interpolates; HEIGHT lands instantly — a window resize must not drag the pane's height
        // along on the morph spline.
        animation.InsertExpressionKeyFrame(0f, "Vector2(this.StartingValue.X, this.FinalValue.Y)");
        animation.InsertExpressionKeyFrame(1f, "this.FinalValue", MorphEasing);

        var implicits = visual.Compositor.CreateImplicitAnimationCollection();
        implicits["Size"] = animation;
        visual.ImplicitAnimations = implicits;
        return true;
    }

    // ── Edge riders ────────────────────────────────────────────────────────────────────────────────────

    /// <summary>Slide the marked chrome from the old edge to the new one. Explicit (not implicit) because
    /// nothing in layout writes <c>Translation</c> — which is exactly why it's safe to drive by hand — and
    /// both animations are committed in the same batch as the layout change, so they share a start time.</summary>
    private void AnimateRiders(double from, double to)
    {
        foreach (var rider in _riders)
        {
            if (ElementComposition.GetElementVisual(rider) is not { } visual) continue;
            var animation = visual.Compositor.CreateVector3DKeyFrameAnimation();
            animation.Target = "Translation";
            animation.Duration = MorphDuration;
            animation.InsertKeyFrame(0f, new Vector3D(from - _contentWidth, 0, 0));
            animation.InsertKeyFrame(1f, new Vector3D(to - _contentWidth, 0, 0), MorphEasing);
            visual.StartAnimation("Translation", animation);
        }
    }

    private static void OnRideEdgeChanged(Visual rider, bool on)
    {
        rider.AttachedToVisualTree -= RiderAttached;
        rider.DetachedFromVisualTree -= RiderDetached;
        Unregister(rider);
        if (!on) return;

        rider.AttachedToVisualTree += RiderAttached;
        rider.DetachedFromVisualTree += RiderDetached;
        if (rider.IsAttachedToVisualTree()) Register(rider);   // already up (a runtime set, not XAML)
    }

    private static void RiderAttached(object? sender, VisualTreeAttachmentEventArgs e)
    {
        if (sender is Visual rider) Register(rider);
    }

    private static void RiderDetached(object? sender, VisualTreeAttachmentEventArgs e)
    {
        if (sender is Visual rider) Unregister(rider);
    }

    private static void Register(Visual rider)
    {
        if (rider.GetVisualAncestors().OfType<MorphPane>().FirstOrDefault() is not { } pane) return;
        rider.SetValue(RiderOwnerProperty, pane);
        if (!pane._riders.Contains(rider)) pane._riders.Add(rider);
    }

    private static void Unregister(Visual rider)
    {
        if (rider.GetValue(RiderOwnerProperty) is { } pane) pane._riders.Remove(rider);
        rider.SetValue(RiderOwnerProperty, null);
    }
}
