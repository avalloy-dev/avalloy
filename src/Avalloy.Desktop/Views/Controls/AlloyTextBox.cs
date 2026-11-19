using System;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;

namespace Avalloy.Views.Controls;

/// <summary>Validation status of a <see cref="AlloyTextBox"/> — drives the :error / :valid pseudo-classes
/// (Danger border + wash + warning glyph / trailing Ok check).</summary>
public enum AlloyFieldStatus { None, Error, Valid }

/// <summary>The canonical text field — a restyled
/// NATIVE <see cref="TextBox"/>, so caret/selection/undo/IME/clipboard and the automation peer all come
/// from the primitive; AlloyControls.axaml swaps the Fluent template for the spec anatomy: optional
/// uppercase <see cref="Label"/> above (supplied UPPERCASE by the caller, like .ddsection), the
/// Field.Bg well (Field.Hairline as an inset BoxShadow) with optional leading glyph · clear affix · status glyph · stepper ·
/// full-bleed <see cref="Prefix"/>/<see cref="Suffix"/> segments, and <see cref="Helper"/> below
/// (turns Danger under Status=Error). Variant classes: <c>sm</c> (32px), <c>search</c> (inset well on
/// Surface.Pane), <c>area</c> (multi-line, r10, wraps; pair with <see cref="FooterHint"/> /
/// <see cref="ShowCounter"/>), <c>number</c> (mono value + <see cref="Stepper"/>).</summary>
public class AlloyTextBox : TextBox
{
    protected override Type StyleKeyOverride => typeof(AlloyTextBox);

    /// <summary>Uppercase eyebrow label above the field; null = no label row.</summary>
    public static readonly StyledProperty<string?> LabelProperty =
        AvaloniaProperty.Register<AlloyTextBox, string?>(nameof(Label));

    /// <summary>Quiet line under the field; shown in Danger when <see cref="Status"/> is Error, so error
    /// messages go here too.</summary>
    public static readonly StyledProperty<string?> HelperProperty =
        AvaloniaProperty.Register<AlloyTextBox, string?>(nameof(Helper));

    /// <summary>Optional leading GlyphIcon role name inside the field (e.g. "Search").</summary>
    public static readonly StyledProperty<string?> LeadingGlyphProperty =
        AvaloniaProperty.Register<AlloyTextBox, string?>(nameof(LeadingGlyph));

    /// <summary>Show the trailing clear (xmark) affix while the field has a value; click empties and refocuses.</summary>
    public static readonly StyledProperty<bool> ShowClearProperty =
        AvaloniaProperty.Register<AlloyTextBox, bool>(nameof(ShowClear));

    /// <summary>Validation status — Error paints Danger border + Danger.Wash fill + warning glyph;
    /// Valid shows the trailing Ok check.</summary>
    public static readonly StyledProperty<AlloyFieldStatus> StatusProperty =
        AvaloniaProperty.Register<AlloyTextBox, AlloyFieldStatus>(nameof(Status));

    /// <summary>Full-bleed mono segment before the input (e.g. "example.com/"), split by a Field.Hairline rule.</summary>
    public static readonly StyledProperty<string?> PrefixProperty =
        AvaloniaProperty.Register<AlloyTextBox, string?>(nameof(Prefix));

    /// <summary>Full-bleed mono segment after the input (e.g. a unit).</summary>
    public static readonly StyledProperty<string?> SuffixProperty =
        AvaloniaProperty.Register<AlloyTextBox, string?>(nameof(Suffix));

    /// <summary>Show the two-button +/− stepper (number variant); steps the numeric Text by
    /// <see cref="StepValue"/> clamped to <see cref="Minimum"/>/<see cref="Maximum"/>.</summary>
    public static readonly StyledProperty<bool> StepperProperty =
        AvaloniaProperty.Register<AlloyTextBox, bool>(nameof(Stepper));

    public static readonly StyledProperty<double> StepValueProperty =
        AvaloniaProperty.Register<AlloyTextBox, double>(nameof(StepValue), 1);

    public static readonly StyledProperty<double> MinimumProperty =
        AvaloniaProperty.Register<AlloyTextBox, double>(nameof(Minimum), double.MinValue);

    public static readonly StyledProperty<double> MaximumProperty =
        AvaloniaProperty.Register<AlloyTextBox, double>(nameof(Maximum), double.MaxValue);

    /// <summary>Mono hint on the footer row inside the field (textarea variant, e.g. "⌘↵ to weave").</summary>
    public static readonly StyledProperty<string?> FooterHintProperty =
        AvaloniaProperty.Register<AlloyTextBox, string?>(nameof(FooterHint));

    /// <summary>Live "n / max" char counter on the footer row (uses <see cref="TextBox.MaxLength"/>; just "n" when unset).</summary>
    public static readonly StyledProperty<bool> ShowCounterProperty =
        AvaloniaProperty.Register<AlloyTextBox, bool>(nameof(ShowCounter));

    /// <summary>Arbitrary content pinned flush-right inside the field (spec anatomy trailing slot) — a count
    /// label, an esc / ⌘K keycap, a unit chip. Sits after the clear/status/stepper affixes. Null = collapsed.</summary>
    public static readonly StyledProperty<object?> TrailingProperty =
        AvaloniaProperty.Register<AlloyTextBox, object?>(nameof(Trailing));

    public string? Label { get => GetValue(LabelProperty); set => SetValue(LabelProperty, value); }
    public string? Helper { get => GetValue(HelperProperty); set => SetValue(HelperProperty, value); }
    public string? LeadingGlyph { get => GetValue(LeadingGlyphProperty); set => SetValue(LeadingGlyphProperty, value); }
    public bool ShowClear { get => GetValue(ShowClearProperty); set => SetValue(ShowClearProperty, value); }
    public AlloyFieldStatus Status { get => GetValue(StatusProperty); set => SetValue(StatusProperty, value); }
    public string? Prefix { get => GetValue(PrefixProperty); set => SetValue(PrefixProperty, value); }
    public string? Suffix { get => GetValue(SuffixProperty); set => SetValue(SuffixProperty, value); }
    public bool Stepper { get => GetValue(StepperProperty); set => SetValue(StepperProperty, value); }
    public double StepValue { get => GetValue(StepValueProperty); set => SetValue(StepValueProperty, value); }
    public double Minimum { get => GetValue(MinimumProperty); set => SetValue(MinimumProperty, value); }
    public double Maximum { get => GetValue(MaximumProperty); set => SetValue(MaximumProperty, value); }
    public string? FooterHint { get => GetValue(FooterHintProperty); set => SetValue(FooterHintProperty, value); }
    public bool ShowCounter { get => GetValue(ShowCounterProperty); set => SetValue(ShowCounterProperty, value); }
    public object? Trailing { get => GetValue(TrailingProperty); set => SetValue(TrailingProperty, value); }

    public static readonly DirectProperty<AlloyTextBox, string> CounterTextProperty =
        AvaloniaProperty.RegisterDirect<AlloyTextBox, string>(nameof(CounterText), o => o.CounterText);

    private string _counterText = "";
    public string CounterText
    {
        get => _counterText;
        private set => SetAndRaise(CounterTextProperty, ref _counterText, value);
    }

    public static readonly DirectProperty<AlloyTextBox, bool> HasFooterProperty =
        AvaloniaProperty.RegisterDirect<AlloyTextBox, bool>(nameof(HasFooter), o => o.HasFooter);

    private bool _hasFooter;
    public bool HasFooter
    {
        get => _hasFooter;
        private set => SetAndRaise(HasFooterProperty, ref _hasFooter, value);
    }

    private Button? _clear, _stepUp, _stepDown;

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);
        if (_clear is not null) _clear.Click -= OnClearClick;
        if (_stepUp is not null) _stepUp.Click -= OnStepUp;
        if (_stepDown is not null) _stepDown.Click -= OnStepDown;
        _clear = e.NameScope.Find<Button>("PART_Clear");
        _stepUp = e.NameScope.Find<Button>("PART_StepUp");
        _stepDown = e.NameScope.Find<Button>("PART_StepDown");
        if (_clear is not null) _clear.Click += OnClearClick;
        if (_stepUp is not null) _stepUp.Click += OnStepUp;
        if (_stepDown is not null) _stepDown.Click += OnStepDown;
        Refresh();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == TextProperty || change.Property == ShowClearProperty ||
            change.Property == MaxLengthProperty || change.Property == ShowCounterProperty ||
            change.Property == FooterHintProperty)
            Refresh();
        else if (change.Property == StatusProperty)
        {
            PseudoClasses.Set(":error", Status == AlloyFieldStatus.Error);
            PseudoClasses.Set(":valid", Status == AlloyFieldStatus.Valid);
        }
        else if (change.Property == IsReadOnlyProperty)
            PseudoClasses.Set(":readonly", IsReadOnly);
    }

    private void Refresh()
    {
        PseudoClasses.Set(":canclear", ShowClear && !IsReadOnly && Text is { Length: > 0 });
        var len = Text?.Length ?? 0;
        CounterText = MaxLength > 0 ? $"{len} / {MaxLength}" : len.ToString();
        HasFooter = ShowCounter || FooterHint is { Length: > 0 };
    }

    private void OnClearClick(object? sender, RoutedEventArgs e)
    {
        Clear();
        Focus();
    }

    private void OnStepUp(object? sender, RoutedEventArgs e) => Step(+1);
    private void OnStepDown(object? sender, RoutedEventArgs e) => Step(-1);

    private void Step(int direction)
    {
        if (IsReadOnly) return;
        double.TryParse(Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value);
        value = Math.Clamp(value + direction * StepValue, Minimum, Maximum);
        Text = value.ToString("0.###", CultureInfo.InvariantCulture);
    }
}
