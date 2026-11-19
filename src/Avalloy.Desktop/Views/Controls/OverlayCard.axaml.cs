using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;

namespace Avalloy.Views.Controls;

/// <summary>
/// Modal-overlay shell with <see cref="Header"/> / <see cref="Body"/> / <see cref="Footer"/> slots and a built-in
/// close button. Provides the standard card chrome (via the <c>.overlaycard</c> style) so an overlay no longer
/// hand-rolls the outer <c>Border</c>, the header row, and the ✕ + its dismiss handler. The ✕ is a real
/// (keyboard-accessible) <see cref="Button"/> that raises the bubbling <see cref="CloseRequested"/> event; the
/// overlay host handles it once (e.g. at the window) to dismiss — no per-overlay code-behind. Set Width/Height on
/// the control itself. The body/footer keep their own padding; the close button floats top-right of the card.
/// </summary>
public partial class OverlayCard : UserControl
{
    public static readonly StyledProperty<object?> HeaderProperty =
        AvaloniaProperty.Register<OverlayCard, object?>(nameof(Header));

    public static readonly StyledProperty<object?> BodyProperty =
        AvaloniaProperty.Register<OverlayCard, object?>(nameof(Body));

    public static readonly StyledProperty<object?> FooterProperty =
        AvaloniaProperty.Register<OverlayCard, object?>(nameof(Footer));

    /// <summary>Show the built-in top-right close ✕. Default true; set false for overlays whose footer already
    /// carries the dismiss (e.g. a Cancel button).</summary>
    public static readonly StyledProperty<bool> ShowCloseProperty =
        AvaloniaProperty.Register<OverlayCard, bool>(nameof(ShowClose), defaultValue: true);

    public object? Header { get => GetValue(HeaderProperty); set => SetValue(HeaderProperty, value); }
    public object? Body { get => GetValue(BodyProperty); set => SetValue(BodyProperty, value); }
    public object? Footer { get => GetValue(FooterProperty); set => SetValue(FooterProperty, value); }
    public bool ShowClose { get => GetValue(ShowCloseProperty); set => SetValue(ShowCloseProperty, value); }

    /// <summary>Raised (bubbling) when the close ✕ is clicked. The overlay host handles it to dismiss.</summary>
    public static readonly RoutedEvent<RoutedEventArgs> CloseRequestedEvent =
        RoutedEvent.Register<OverlayCard, RoutedEventArgs>(nameof(CloseRequested), RoutingStrategies.Bubble);

    public event System.EventHandler<RoutedEventArgs> CloseRequested
    {
        add => AddHandler(CloseRequestedEvent, value);
        remove => RemoveHandler(CloseRequestedEvent, value);
    }

    public OverlayCard() => AvaloniaXamlLoader.Load(this);

    private void OnCloseClick(object? sender, RoutedEventArgs e) =>
        RaiseEvent(new RoutedEventArgs(CloseRequestedEvent));
}
