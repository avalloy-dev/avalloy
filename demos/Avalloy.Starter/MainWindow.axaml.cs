using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalloy.Starter.Shell;
using Avalloy;
using Avalloy.Controls.Sidebar;
using Avalloy.Views.Controls;

namespace Avalloy.Starter;

public partial class MainWindow : Window
{
    /// <summary>Design-time / previewer ctor — an unpersisted shell so the frame renders.</summary>
    public MainWindow()
    {
        AvaloniaXamlLoader.Load(this);
        DataContext = new ShellViewModel();
        WireSidebarDock();
    }

    /// <summary>Runtime ctor (DI): persisted geometry + the shared shell state.</summary>
    public MainWindow(ShellViewModel shell)
    {
        AvaloniaXamlLoader.Load(this);
        if (App is { } app) this.BindGeometry(app.Prefs.Window, app.SavePrefsNow);
        DataContext = shell;
        WireSidebarDock();
        // One handler dismisses any OverlayCard-family ✕ (PreferencesOverlay's included — bubbles here).
        AddHandler(OverlayCard.CloseRequestedEvent, (_, _) => { if (Vm is { } vm) vm.SettingsOpen = false; });
    }

    private App? App => Application.Current as App;
    private ShellViewModel? Vm => DataContext as ShellViewModel;

    private SidebarDockController? _sidebarDock;

    // Avalloy's SidebarDockController owns the siderail mechanics: the reserved column's
    // rail ↔ docked morph, the passive seam watcher that reveals the edge pull, tap-to-pin,
    // drag-to-resize, and width persistence (via ISidebarHost = the ShellViewModel).
    private void WireSidebarDock()
    {
        if (this.FindControl<Grid>("BodyGrid") is not { } body ||
            this.FindControl<DrawerPull>("EdgePullTab") is not { } pull)
            return;
        _sidebarDock = new SidebarDockController(
            this, body, column: 0, pull,
            host: () => Vm,
            options: new SidebarDockOptions
            {
                SurfaceMargins = 0,        // the sidebar Border fills the column edge-to-edge
                MinDockedWidth = 180,
                MaxDockedWidth = 360,
            });
        if (Vm is { } vm) vm.PropertyChanged += OnVmPropertyChanged;
    }

    private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ShellViewModel.SidebarPinned)) _sidebarDock?.NotifyPinChanged();
    }

    // Every unhandled key goes through Avalloy's dispatcher: gesture → command name
    // (App.BuildKeymap) → the [Command] singleton. Add a command + a keymap row; done.
    // (Keyboard is null in the XAML previewer — AvalloyApp init doesn't run there.)
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Handled) return;
        if (App is { Keyboard: { } keyboard } && keyboard.TryDispatch(e) is not null)
        {
            e.Handled = true;
            return;
        }
        // Esc dismisses the overlay layer (contextual — stays hand-routed, not a command).
        if (e.Key == Key.Escape && Vm is { SettingsOpen: true } vm)
        {
            vm.SettingsOpen = false;
            e.Handled = true;
        }
    }

    private void OnScrimTapped(object? sender, TappedEventArgs e)
    {
        if (Vm is { } vm) vm.SettingsOpen = false;
    }

    // Pointer affordances dispatch the SAME commands the chords hit — one definition each.
    // (The drawer pull's tap is owned by SidebarDockController — no handler here.)
    private void OnToggleTheme(object? sender, TappedEventArgs e) => Run("starter.toggleTheme");

    private void Run(string command) => App?.Keyboard.RunByNameAsync(command);

    private void OnOpenSettings(object? sender, TappedEventArgs e) => Run("starter.settings");

    private void OnExportPick(object? sender, TappedEventArgs e)
    {
        if (Vm is { } vm) vm.ExportMenuOpen = false;
    }
}
