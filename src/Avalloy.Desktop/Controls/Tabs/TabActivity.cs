using System.Globalization;
using Avalonia.Data.Converters;

namespace Avalloy.Controls.Tabs;

/// <summary>
/// A tab's live status, rendered by <see cref="TabStrip"/> as a small indicator
/// that stays visible on inactive/background tabs — the point is watching several
/// long-running things (e.g. agent patches) at once without clicking in. Orthogonal
/// to <see cref="ITabItem.Icon"/> (identity glyph): a tab can show its type icon and
/// a running state simultaneously.
/// </summary>
public enum TabActivity
{
    /// <summary>Nothing in flight — the indicator slot collapses.</summary>
    Idle,

    /// <summary>Work in progress — a quiet accent pulse.</summary>
    Running,

    /// <summary>Wants the user's eyes — a solid accent badge.</summary>
    NeedsAttention,

    /// <summary>Failed — an error-tint badge.</summary>
    Error,

    /// <summary>Finished successfully — a settled check.</summary>
    Complete,
}

/// <summary>
/// Returns <c>true</c> when the bound <see cref="TabActivity"/> equals the
/// <c>ConverterParameter</c> (its name). Lets the per-tab template stamp an
/// <c>act-*</c> class per state declaratively, so the styles drive each state's
/// visual (pulse / badge / check) without code-behind subscribing to every item.
/// </summary>
public sealed class TabActivityEqualsConverter : IValueConverter
{
    public static readonly TabActivityEqualsConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is TabActivity a && parameter is string s &&
        string.Equals(a.ToString(), s, StringComparison.Ordinal);

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
