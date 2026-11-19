using System;
using System.Threading;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Styling;

namespace Avalloy.Views.Controls;

/// <summary>Animates its own height toward its child's desired height (≈220ms ease-out) when
/// <see cref="MorphKey"/> changes, so a state swap inside (e.g. an expanded card ⇄ a one-line summary chip)
/// MORPHS instead of snapping — the surrounding layout never jumps. Content stays top-anchored and clipped,
/// so a shrinking child reads as collapsing upward. First measure never animates (no entrance jitter).
///
/// <para>Only a <see cref="MorphKey"/> change animates. Any OTHER height change settles instantly, because a
/// height that moves for some other reason is not a state swap — a container recycled onto a different row, a
/// re-wrap at a new width, a live row growing as it streams. Animating those puts a new height on screen every
/// frame for 220ms, and inside a virtualizing panel that re-measures, re-estimates the extent, and comes back
/// as another height change: the morph feeds itself and the list will not settle.</para>
///
/// <para>With no <see cref="MorphKey"/> bound nothing ever animates. That is the safe default — a caller that
/// has not said which change is a state change does not get to guess.</para></summary>
public sealed class HeightMorph : Decorator
{
    /// <summary>The in-flight animated height; NaN = idle (size to content).</summary>
    public static readonly StyledProperty<double> MorphHeightProperty =
        AvaloniaProperty.Register<HeightMorph, double>(nameof(MorphHeight), double.NaN);

    public double MorphHeight
    {
        get => GetValue(MorphHeightProperty);
        set => SetValue(MorphHeightProperty, value);
    }

    /// <summary>What makes a height change worth animating: bind the state that swaps the content (an
    /// IsAnswered flag, a mode enum). The morph arms when this changes and disarms once it has spent it.</summary>
    public static readonly StyledProperty<object?> MorphKeyProperty =
        AvaloniaProperty.Register<HeightMorph, object?>(nameof(MorphKey));

    public object? MorphKey
    {
        get => GetValue(MorphKeyProperty);
        set => SetValue(MorphKeyProperty, value);
    }

    static HeightMorph() => AffectsMeasure<HeightMorph>(MorphHeightProperty);

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        // Arm on the change itself, not on the height it will cause: the two arrive in separate passes, and
        // by the second one there is nothing left to say which pass was the state swap.
        if (change.Property == MorphKeyProperty && !_first) _armed = true;
    }

    public HeightMorph() => ClipToBounds = true;

    private double _target = double.NaN;   // the child height we're showing / animating toward
    private CancellationTokenSource? _cts;
    private bool _armed;                   // a MorphKey change is waiting for the height it causes
    private bool _first = true;            // the initial MorphKey binding is not a swap

    protected override Size MeasureOverride(Size availableSize)
    {
        if (Child is not { } child) return default;
        child.Measure(new Size(availableSize.Width, double.PositiveInfinity));
        var want = child.DesiredSize.Height;

        if (double.IsNaN(_target))
        {
            _target = want;               // first measure — settle instantly
            _first = false;
        }
        else if (Math.Abs(want - _target) > 0.5)
        {
            if (_armed)
            {
                _armed = false;
                var from = double.IsNaN(MorphHeight) ? _target : MorphHeight;   // continue from wherever we are
                _target = want;
                StartAnimation(from, want);
            }
            else
            {
                // Not a state swap: take the new height at once. Any animation in flight is abandoned rather
                // than retargeted — it was animating toward a height nobody is asking for any more.
                _cts?.Cancel();
                _target = want;
                SetCurrentValue(MorphHeightProperty, double.NaN);
            }
        }

        var shown = double.IsNaN(MorphHeight) ? _target : MorphHeight;
        return new Size(child.DesiredSize.Width, shown);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        // Give the child its NATURAL height (top-anchored, clipped) — squashing it mid-morph would rewrap text.
        Child?.Arrange(new Rect(0, 0, finalSize.Width, Math.Max(Child.DesiredSize.Height, finalSize.Height)));
        return finalSize;
    }

    private async void StartAnimation(double from, double to)
    {
        _cts?.Cancel();
        var cts = _cts = new CancellationTokenSource();
        var anim = new Animation
        {
            Duration = TimeSpan.FromMilliseconds(220),
            Easing = new CubicEaseOut(),
            // FillMode None: on completion the animated value releases back to NaN = auto = the new height —
            // no end-flicker, and no Animation-priority fill value stuck above later local sets.
            FillMode = FillMode.None,
            Children =
            {
                new KeyFrame { Cue = new Cue(0d), Setters = { new Setter(MorphHeightProperty, from) } },
                new KeyFrame { Cue = new Cue(1d), Setters = { new Setter(MorphHeightProperty, to) } },
            },
        };
        try { await anim.RunAsync(this, cts.Token); }
        catch (OperationCanceledException) { /* superseded by a newer morph */ }
    }
}
