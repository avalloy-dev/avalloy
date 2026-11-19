using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Threading;
using Avalloy.Commands;

namespace Avalloy.Views.Controls;

/// <summary>
/// The command-palette surface (see CommandPaletteView.axaml): overlay card, search box, result list.
/// Bind it to a <see cref="CommandPaletteViewModel"/> and drop it in the host's overlay slot.
/// <para>Keyboard is claimed here, TUNNELLING through the whole palette, because ↑↓/Enter must work while
/// focus is in the TEXT BOX — the palette is unusable if moving the selection means leaving the query — and
/// must keep working once Tab has moved focus into the list. Tunnelling, the palette sees them before the
/// list does: Enter on a row would not reach a bubbling handler at all, and ↑↓ follow the palette's rules
/// (wrap, step over disabled rows) rather than the list's. What each key does is
/// <see cref="CommandPaletteViewModel.HandleKey"/>'s.</para>
/// <para>The look of a row can be tuned per palette — <see cref="ShowGlyphs"/>, <see cref="RowHeight"/>,
/// <see cref="HintFontSize"/>, <see cref="HintForeground"/> — and the list's accessible name with
/// <see cref="ListName"/>. Every default is the palette as it was.</para>
/// </summary>
public partial class CommandPaletteView : UserControl
{
    public static readonly StyledProperty<bool> ShowGlyphsProperty =
        AvaloniaProperty.Register<CommandPaletteView, bool>(nameof(ShowGlyphs), true);

    public static readonly StyledProperty<double> RowHeightProperty =
        AvaloniaProperty.Register<CommandPaletteView, double>(nameof(RowHeight), 40);

    public static readonly StyledProperty<double> HintFontSizeProperty =
        AvaloniaProperty.Register<CommandPaletteView, double>(nameof(HintFontSize), 11);

    public static readonly StyledProperty<IBrush?> HintForegroundProperty =
        AvaloniaProperty.Register<CommandPaletteView, IBrush?>(nameof(HintForeground));

    public static readonly StyledProperty<string> ListNameProperty =
        AvaloniaProperty.Register<CommandPaletteView, string>(nameof(ListName), "Results");

    /// <summary>Whether each row starts with its <see cref="PaletteItem.Glyph"/>. True by default; false for a
    /// palette whose rows have no icons of their own, where every row would show the same run glyph.</summary>
    public bool ShowGlyphs
    {
        get => GetValue(ShowGlyphsProperty);
        set => SetValue(ShowGlyphsProperty, value);
    }

    /// <summary>A row's minimum height. 40 by default.</summary>
    public double RowHeight
    {
        get => GetValue(RowHeightProperty);
        set => SetValue(RowHeightProperty, value);
    }

    /// <summary>The size of the right-aligned hints. 11 by default.</summary>
    public double HintFontSize
    {
        get => GetValue(HintFontSizeProperty);
        set => SetValue(HintFontSizeProperty, value);
    }

    /// <summary>The brush for the right-aligned hints. Defaults to the theme's <c>Text.Ghost</c>, set by a style,
    /// so a local value or an app style wins; to follow another theme token, bind it to that resource (in XAML,
    /// <c>HintForeground="{DynamicResource Text.Muted}"</c>).</summary>
    public IBrush? HintForeground
    {
        get => GetValue(HintForegroundProperty);
        set => SetValue(HintForegroundProperty, value);
    }

    /// <summary>What assistive technology calls the result list. "Results" by default — name it for what this
    /// palette lists ("Commands", "Projects").</summary>
    public string ListName
    {
        get => GetValue(ListNameProperty);
        set => SetValue(ListNameProperty, value);
    }

    public CommandPaletteView()
    {
        AvaloniaXamlLoader.Load(this);
        AddHandler(KeyDownEvent, OnPaletteKeyDown, RoutingStrategies.Tunnel);
    }

    private CommandPaletteViewModel? Vm => DataContext as CommandPaletteViewModel;

    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);
        // Land in the search box so the user types straight into it. Posted at Input priority: the
        // control is loaded but the overlay's own layout pass has not necessarily run, and focusing a
        // TextBox that is not yet arranged does not stick.
        Dispatcher.UIThread.Post(() => this.FindControl<TextBox>("Search")?.Focus(), DispatcherPriority.Input);
    }

    // Esc is handled at the window level, with every other overlay.
    private void OnPaletteKeyDown(object? sender, KeyEventArgs e)
    {
        if (Vm?.HandleKey(e.Key, e.KeyModifiers) == true) e.Handled = true;
    }

    private void OnItemTapped(object? sender, TappedEventArgs e)
    {
        if ((sender as Control)?.DataContext is PaletteItem item) Vm?.RunItem(item);
    }
}
