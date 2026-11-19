using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;

namespace Avalloy.Icons;

/// <summary>The reusable icon-picker view: a search box + a grid of <see cref="GlyphIcon"/>
/// swatches over an <see cref="IconPickerViewModel"/>, plus a <see cref="Footer"/> slot the
/// host fills with app-specific affordances (image upload, monogram, …). Picking a swatch
/// drives the VM directly (Click + Tag).</summary>
public partial class IconPickerView : UserControl
{
    /// <summary>App-supplied footer content, shown under the grid (e.g. upload / monogram
    /// buttons). The host wires its own handlers; the generic picker only picks glyph ids.</summary>
    public static readonly StyledProperty<object?> FooterProperty =
        AvaloniaProperty.Register<IconPickerView, object?>(nameof(Footer));

    public object? Footer
    {
        get => GetValue(FooterProperty);
        set => SetValue(FooterProperty, value);
    }

    public IconPickerView() => AvaloniaXamlLoader.Load(this);

    private void OnPickIcon(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { Tag: string id } && DataContext is IconPickerViewModel vm)
            vm.Pick(id);
    }
}
