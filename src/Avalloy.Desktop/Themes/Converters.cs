using System;
using System.Globalization;
using Avalonia;
using Avalonia.Data;
using Avalonia.Data.Converters;
using Avalonia.Layout;
using Avalonia.Media;

namespace Avalloy.Themes;

/// <summary>
/// Reusable, app-agnostic value converters for native-themed Avalloy apps. The
/// theme-resource ones key off the native token contract (<c>Accent</c>,
/// <c>Surface.*</c>, <c>Border.*</c>, <c>Text.*</c>) that Avalloy.Desktop's
/// native templates define, so any consumer that ships those keys gets correct results.
/// Product-specific converters (brand ramps, domain glyphs) stay in the app.
/// </summary>
public static class Converters
{
    // ── Pure value transforms (no resource lookups) ──────────────────────────────

    /// <summary>double → left-only <see cref="Thickness"/> (for indent margins).</summary>
    public static readonly IValueConverter LeftThickness =
        new FuncValueConverter<double, Thickness>(v => new Thickness(v, 0, 0, 0));

    /// <summary>bool → 1.0 / 0.0 (e.g. show a colored border only when selected).</summary>
    public static readonly IValueConverter BoolToOpacity =
        new FuncValueConverter<bool, double>(b => b ? 1.0 : 0.0);

    /// <summary>int &gt; 0 → true (badges/pills that show only when there's something).</summary>
    public static readonly IValueConverter Positive = new FuncValueConverter<int, bool>(n => n > 0);

    /// <summary>int == 0 → true (an "all caught up" empty state).</summary>
    public static readonly IValueConverter IsZero = new FuncValueConverter<int, bool>(n => n == 0);

    /// <summary>bool → SemiBold / Normal (e.g. the current row reads bold).</summary>
    public static readonly IValueConverter BoolToWeight =
        new FuncValueConverter<bool, FontWeight>(b => b ? FontWeight.SemiBold : FontWeight.Normal);

    /// <summary>bool (selected) → full vs dimmed opacity for multi-pick chips.</summary>
    public static readonly IValueConverter SelectedOpacity =
        new FuncValueConverter<bool, double>(sel => sel ? 1.0 : 0.45);

    /// <summary>bool → toggle-thumb alignment: Right when on, Left when off.</summary>
    public static readonly IValueConverter ToggleThumbAlign =
        new FuncValueConverter<bool, HorizontalAlignment>(
            on => on ? HorizontalAlignment.Right : HorizontalAlignment.Left);

    /// <summary>string == ConverterParameter → bool. Drives the selected state of
    /// segmented/radio controls whose backing value is a string (e.g. a mode enum).</summary>
    public static readonly IValueConverter StringEquals = new StringEqualsConverter();

    private sealed class StringEqualsConverter : IValueConverter
    {
        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
            => string.Equals(value as string, parameter as string, StringComparison.Ordinal);

        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
            => BindingOperations.DoNothing;
    }

    /// <summary>TWO-WAY <see cref="StringEquals"/> for segmented radio groups: true when the bound value's
    /// string equals the ConverterParameter; checking a radio writes that option back to the source.
    /// Unchecking is a no-op so sibling radios clearing themselves don't wipe the source. Pairs with
    /// <c>RadioButton.seg2</c>.</summary>
    public static readonly IValueConverter StringMatch = new StringMatchConverter();

    private sealed class StringMatchConverter : IValueConverter
    {
        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
            string.Equals(value?.ToString(), parameter?.ToString(), StringComparison.Ordinal);

        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
            value is true ? parameter?.ToString() ?? BindingOperations.DoNothing : BindingOperations.DoNothing;
    }

    /// <summary>Two-way bool inverse — lets the second radio in a two-option segmented group bind IsChecked to
    /// the same backing bool as the first. Both options write back to the one property.</summary>
    public static readonly IValueConverter Negate = new NegateConverter();

    private sealed class NegateConverter : IValueConverter
    {
        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value is not true;
        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => value is not true;
    }

    /// <summary>Enabled → full opacity; disabled → dimmed (greyed-out rows/options).</summary>
    public static readonly IValueConverter BoolToEnabledOpacity =
        new FuncValueConverter<bool, double>(enabled => enabled ? 1.0 : 0.4);

    // ── Token-contract resource converters (key off Accent / Surface.* / Border.* / Text.*) ──

    /// <summary>bool → a "selected" segment fill (<c>Surface.Active3</c>) vs transparent.</summary>
    public static readonly IValueConverter OnOffBg =
        new FuncValueConverter<bool, IBrush?>(on => on ? Res("Surface.Active3") : Brushes.Transparent);

    /// <summary>bool → a toggle-track fill: the theme <c>Accent</c> when on, neutral
    /// <c>Surface.Raised2</c> when off.</summary>
    public static readonly IValueConverter ToggleTrack =
        new FuncValueConverter<bool, IBrush?>(on => on ? Res("Accent") : Res("Surface.Raised2"));

    /// <summary>bool (selected) → a card frame: theme <c>Accent</c> when chosen,
    /// <c>Border.Soft</c> otherwise.</summary>
    public static readonly IValueConverter ThemeCardBorder =
        new FuncValueConverter<bool, IBrush?>(sel => sel ? Res("Accent") : Res("Border.Soft"));

    /// <summary>Inline-action button fill by kind: "primary" → theme <c>Accent</c>,
    /// "neutral" → <c>Surface.Raised2</c>, otherwise transparent (outline).</summary>
    public static readonly IValueConverter ActionBg = new FuncValueConverter<string?, IBrush?>(k => k switch
    {
        "primary" => Res("Accent"),
        "neutral" => Res("Surface.Raised2"),
        _ => Brushes.Transparent,
    });

    /// <summary>Inline-action foreground: white on "primary", else <c>Text.Secondary</c>.</summary>
    public static readonly IValueConverter ActionFg = new FuncValueConverter<string?, IBrush?>(k =>
        k == "primary" ? Brushes.White : Res("Text.Secondary"));

    /// <summary>Inline-action border: <c>Border.Strong</c> on "outline", else transparent.</summary>
    public static readonly IValueConverter ActionBorder = new FuncValueConverter<string?, IBrush?>(k =>
        k == "outline" ? Res("Border.Strong") : Brushes.Transparent);

    /// <summary>Resolve a resource-key string (bound from a view-model) to the current theme's brush —
    /// for rows/glyphs whose tone is data-driven (a status key, a state colour) rather than styled.</summary>
    public static readonly IValueConverter ResourceBrush =
        new FuncValueConverter<string?, IBrush?>(key => key is { Length: > 0 } ? Res(key) : null);

    /// <summary>Resolve a brush resource against the active theme variant; null if absent.</summary>
    public static IBrush? Res(string key)
        => Application.Current is { } app
           && app.TryGetResource(key, app.ActualThemeVariant, out var v) && v is IBrush b
            ? b
            : null;
}
