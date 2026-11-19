using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace Avalloy.Services;

/// <summary>
/// Extracts the <see cref="Color"/> from an <see cref="ISolidColorBrush"/>.
/// Used to feed theme-brush resources into APIs that need a Color
/// (e.g. <c>Svg.CurrentColor</c>, which is theme-aware via the binding —
/// when the brush resource updates on theme variant change, the bound
/// Color updates with it).
/// </summary>
public sealed class BrushColorConverter : IValueConverter
{
    public static readonly BrushColorConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is ISolidColorBrush b ? b.Color : Colors.Transparent;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
