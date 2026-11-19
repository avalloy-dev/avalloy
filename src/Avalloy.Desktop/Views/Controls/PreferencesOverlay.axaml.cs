using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;

namespace Avalloy.Views.Controls;

/// <summary>
/// The preferences shell (see PreferencesOverlay.axaml): overlaycard + search-first section rail +
/// content column. Set <see cref="Navigator"/> (the rail model), <see cref="Title"/>,
/// <see cref="VersionLine"/>, and put the section bodies in <see cref="Sections"/> — each body
/// binds its IsVisible to the navigator's <c>Selected</c>. The ✕ raises
/// <see cref="OverlayCard.CloseRequestedEvent"/>, so the window's one overlay-dismiss handler
/// closes it like every other overlay.
/// </summary>
public partial class PreferencesOverlay : UserControl
{
    public static readonly StyledProperty<PrefsNavigator?> NavigatorProperty =
        AvaloniaProperty.Register<PreferencesOverlay, PrefsNavigator?>(nameof(Navigator));

    public static readonly StyledProperty<string> TitleProperty =
        AvaloniaProperty.Register<PreferencesOverlay, string>(nameof(Title), "Preferences");

    /// <summary>Rail footer line (e.g. "v1.2 · check for updates"); empty hides nothing — keep it short.</summary>
    public static readonly StyledProperty<string> VersionLineProperty =
        AvaloniaProperty.Register<PreferencesOverlay, string>(nameof(VersionLine), "");

    public static readonly StyledProperty<double> CardWidthProperty =
        AvaloniaProperty.Register<PreferencesOverlay, double>(nameof(CardWidth), 1080);

    public static readonly StyledProperty<double> CardHeightProperty =
        AvaloniaProperty.Register<PreferencesOverlay, double>(nameof(CardHeight), 700);

    /// <summary>The app's section bodies (a panel of section stacks, visibility-switched by the
    /// navigator). Inherits the control's DataContext, so app bindings work unchanged.
    /// Set via the EXPLICIT property element (&lt;vc:PreferencesOverlay.Sections&gt;) — nested
    /// direct content would route to ContentControl.Content and replace the card chrome.</summary>
    public static readonly StyledProperty<object?> SectionsProperty =
        AvaloniaProperty.Register<PreferencesOverlay, object?>(nameof(Sections));

    public PrefsNavigator? Navigator { get => GetValue(NavigatorProperty); set => SetValue(NavigatorProperty, value); }
    public string Title { get => GetValue(TitleProperty); set => SetValue(TitleProperty, value); }
    public string VersionLine { get => GetValue(VersionLineProperty); set => SetValue(VersionLineProperty, value); }
    public double CardWidth { get => GetValue(CardWidthProperty); set => SetValue(CardWidthProperty, value); }
    public double CardHeight { get => GetValue(CardHeightProperty); set => SetValue(CardHeightProperty, value); }

    public object? Sections { get => GetValue(SectionsProperty); set => SetValue(SectionsProperty, value); }

    /// <summary>The rail footer was tapped (open About / release notes — the app's call).</summary>
    public event EventHandler? VersionTapped;

    // Named parts, resolved ONCE right after Load — property setters fire while the parent XAML is
    // still applying attributes, when FindControl can't reach a name scope yet.
    private readonly Border? _card;
    private readonly TextBlock? _titleText;
    private readonly TextBlock? _versionText;
    private readonly ContentPresenter? _body;
    private readonly ItemsControl? _navList;
    private readonly AlloyTextBox? _searchBox;
    private readonly TextBlock? _sectionTitle;

    public PreferencesOverlay()
    {
        AvaloniaXamlLoader.Load(this);
        _card = this.FindControl<Border>("Card");
        _titleText = this.FindControl<TextBlock>("TitleText");
        _versionText = this.FindControl<TextBlock>("VersionText");
        _body = this.FindControl<ContentPresenter>("Body");
        _navList = this.FindControl<ItemsControl>("NavList");
        _searchBox = this.FindControl<AlloyTextBox>("SearchBox");
        _sectionTitle = this.FindControl<TextBlock>("SectionTitle");
        Sync();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == NavigatorProperty || change.Property == TitleProperty ||
            change.Property == VersionLineProperty || change.Property == SectionsProperty ||
            change.Property == CardWidthProperty || change.Property == CardHeightProperty)
            Sync();
    }

    private void Sync()
    {
        if (_card is { } card)
        {
            card.Width = CardWidth;
            card.Height = CardHeight;
        }
        if (_titleText is { } title) title.Text = Title;
        if (_versionText is { } version) version.Text = VersionLine;
        if (_body is { } body) body.Content = Sections;
        var nav = Navigator;
        if (_navList is { } list) list.ItemsSource = nav?.Sections;
        // The search box + section title bind against the navigator as their DataContext.
        if (_searchBox is { } search) search.DataContext = nav;
        if (_sectionTitle is { } section) section.DataContext = nav;
    }

    private void OnClose(object? sender, TappedEventArgs e) =>
        RaiseEvent(new RoutedEventArgs(OverlayCard.CloseRequestedEvent));

    private void OnVersionTapped(object? sender, TappedEventArgs e) => VersionTapped?.Invoke(this, EventArgs.Empty);
}
