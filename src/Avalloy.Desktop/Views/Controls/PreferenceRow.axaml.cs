using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace Avalloy.Views.Controls;

/// <summary>
/// A single settings row — <see cref="Title"/> + optional <see cref="Description"/> on the left, a <see cref="Trailing"/>
/// control (button / toggle / flyout / chips) on the right. Replaces the hand-rolled <c>*,Auto</c> grid + label stack
/// that prefs/settings panes repeat for every setting. The description hides itself when unset.
/// </summary>
public partial class PreferenceRow : UserControl
{
    public static readonly StyledProperty<string?> TitleProperty =
        AvaloniaProperty.Register<PreferenceRow, string?>(nameof(Title));

    public static readonly StyledProperty<string?> DescriptionProperty =
        AvaloniaProperty.Register<PreferenceRow, string?>(nameof(Description));

    public static readonly StyledProperty<object?> TrailingProperty =
        AvaloniaProperty.Register<PreferenceRow, object?>(nameof(Trailing));

    /// <summary>Bold row label.</summary>
    public string? Title { get => GetValue(TitleProperty); set => SetValue(TitleProperty, value); }

    /// <summary>Muted helper line under the title; hidden when null/empty.</summary>
    public string? Description { get => GetValue(DescriptionProperty); set => SetValue(DescriptionProperty, value); }

    /// <summary>The right-hand control for this row.</summary>
    public object? Trailing { get => GetValue(TrailingProperty); set => SetValue(TrailingProperty, value); }

    public PreferenceRow() => AvaloniaXamlLoader.Load(this);
}
