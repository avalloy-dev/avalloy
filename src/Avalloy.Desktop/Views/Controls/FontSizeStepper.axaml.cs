using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;

namespace Avalloy.Views.Controls;

/// <summary>The shared − / ＋ text-size stepper. Owns the step/clamp, the per-click flash readout, and
/// the live tooltips; hosts set the range + <see cref="TipNoun"/>, seed <see cref="Value"/> from their
/// pref, and persist on <see cref="ValueChanged"/>. Reveal is host-driven via <see cref="IsRevealed"/>
/// (typically a HoverIntent dwell class on the host's header strip — see FontSizeStepper.axaml).</summary>
public partial class FontSizeStepper : UserControl
{
    /// <summary>The current size (pt). Programmatic sets (pref seeding) go through the same
    /// change notification as clicks — hosts just re-apply, which is idempotent.</summary>
    public static readonly StyledProperty<double> ValueProperty =
        AvaloniaProperty.Register<FontSizeStepper, double>(nameof(Value), 12);

    public static readonly StyledProperty<double> MinimumProperty =
        AvaloniaProperty.Register<FontSizeStepper, double>(nameof(Minimum), 8);

    public static readonly StyledProperty<double> MaximumProperty =
        AvaloniaProperty.Register<FontSizeStepper, double>(nameof(Maximum), 24);

    /// <summary>The stock size — the tooltips/flash flag it ("12 (default)" / "14 · default 12").</summary>
    public static readonly StyledProperty<double> DefaultSizeProperty =
        AvaloniaProperty.Register<FontSizeStepper, double>(nameof(DefaultSize), 12);

    /// <summary>What the tooltips call the text being sized ("terminal text", "chat text").</summary>
    public static readonly StyledProperty<string> TipNounProperty =
        AvaloniaProperty.Register<FontSizeStepper, string>(nameof(TipNoun), "text");

    /// <summary>Shows/hides the buttons (:revealed pseudo-class → opacity). Hosts set it from their own
    /// hover-intent zone so the reveal covers the whole header strip, not just the tiny buttons.</summary>
    public static readonly StyledProperty<bool> IsRevealedProperty =
        AvaloniaProperty.Register<FontSizeStepper, bool>(nameof(IsRevealed), false);

    public double Value { get => GetValue(ValueProperty); set => SetValue(ValueProperty, value); }
    public double Minimum { get => GetValue(MinimumProperty); set => SetValue(MinimumProperty, value); }
    public double Maximum { get => GetValue(MaximumProperty); set => SetValue(MaximumProperty, value); }
    public double DefaultSize { get => GetValue(DefaultSizeProperty); set => SetValue(DefaultSizeProperty, value); }
    public string TipNoun { get => GetValue(TipNounProperty); set => SetValue(TipNounProperty, value); }
    public bool IsRevealed { get => GetValue(IsRevealedProperty); set => SetValue(IsRevealedProperty, value); }

    /// <summary>Raised whenever <see cref="Value"/> actually changes (clamped no-op clicks don't fire).</summary>
    public event EventHandler<double>? ValueChanged;

    public FontSizeStepper()
    {
        AvaloniaXamlLoader.Load(this);
    }

    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);
        SyncTips();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == ValueProperty)
        {
            ValueChanged?.Invoke(this, Value);
            SyncTips();
        }
        else if (change.Property == DefaultSizeProperty || change.Property == TipNounProperty)
        {
            SyncTips();
        }
        else if (change.Property == IsRevealedProperty)
        {
            PseudoClasses.Set(":revealed", IsRevealed);
        }
    }

    private void OnSmaller(object? sender, TappedEventArgs e) => Bump(-1);

    private void OnLarger(object? sender, TappedEventArgs e) => Bump(+1);

    private void Bump(double delta)
    {
        var next = Math.Clamp(Value + delta, Minimum, Maximum);
        if (next != Value) Value = next;
        Flash();   // clamped no-ops flash too — the number explains WHY nothing moved
    }

    // The open tooltip doesn't refresh mid-hover, so each click flashes the live size beside the − for
    // ~a second (restarted per click, so a run of clicks reads as one readout tracking the value).
    private DispatcherTimer? _flashTimer;

    private void Flash()
    {
        if (this.FindControl<TextBlock>("FlashText") is not { } flash) return;
        flash.Text = Value == DefaultSize ? $"{Value:0.#} · default" : $"{Value:0.#}";
        if (!flash.Classes.Contains("show")) flash.Classes.Add("show");
        if (_flashTimer is null)
        {
            _flashTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1100) };
            _flashTimer.Tick += (_, _) =>
            {
                _flashTimer!.Stop();
                this.FindControl<TextBlock>("FlashText")?.Classes.Remove("show");
            };
        }
        _flashTimer.Stop();
        _flashTimer.Start();
    }

    // The −/＋ tips carry the live size, flagging stock vs. nudged ("12 (default)" / "14 · default 12") —
    // the only place the number shows at rest, so the host's header stays clean.
    private void SyncTips()
    {
        var suffix = Value == DefaultSize
            ? $"{Value:0.#} (default)"
            : $"{Value:0.#} · default {DefaultSize:0.#}";
        if (this.FindControl<Border>("SmallerBtn") is { } smaller) ToolTip.SetTip(smaller, $"Smaller {TipNoun} — {suffix}");
        if (this.FindControl<Border>("LargerBtn") is { } larger) ToolTip.SetTip(larger, $"Larger {TipNoun} — {suffix}");
    }
}
