using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;

namespace Avalloy.Controls.Splash;

/// <summary>
/// The Avalloy launch splash — a token-themed borderless card (app mark + name, a live boot-status line,
/// and build vitals) meant to be on screen before the app's slow boot work runs. Designed for
/// <see cref="AvalloyApp"/>'s deferred-launch path: return one from
/// <see cref="AvalloyApp.CreateSplashWindow"/> and report phases through the window itself — it implements
/// <see cref="IProgress{T}"/> of string, marshalling onto the UI thread.
///
/// <para>Colors/typography ride the shared token contract (<c>Surface.Pane2</c>, <c>Radius.Window</c>,
/// <c>Text.*</c>, <c>Font.Display</c>), so it wears whatever theme the app applied. It is
/// <see cref="WindowBase.Topmost"/> on purpose: the main window is shown and painted UNDERNEATH it, and the
/// handoff closes the splash only once real content is up — no bare-desktop gap between the windows.</para>
/// </summary>
public partial class AlloySplashWindow : Window, IProgress<string>
{
    /// <summary>The app's display name, next to the mark.</summary>
    public static readonly StyledProperty<string> AppNameProperty =
        AvaloniaProperty.Register<AlloySplashWindow, string>(nameof(AppName), "");

    /// <summary>The app mark shown beside the name; the slot collapses when null.</summary>
    public static readonly StyledProperty<IImage?> LogoProperty =
        AvaloniaProperty.Register<AlloySplashWindow, IImage?>(nameof(Logo));

    /// <summary>The live boot phase, user-facing ("Syncing your data — 12.4 MB").</summary>
    public static readonly StyledProperty<string> StatusProperty =
        AvaloniaProperty.Register<AlloySplashWindow, string>(nameof(Status), "Starting…");

    /// <summary>Build vitals line ("Debug · built Aug 15, 2:32 PM"). Seeded from
    /// <see cref="ComputeBuildInfo"/>; settable for apps that want their own wording.</summary>
    public static readonly StyledProperty<string> BuildInfoProperty =
        AvaloniaProperty.Register<AlloySplashWindow, string>(nameof(BuildInfo), "");

    public AlloySplashWindow()
    {
        AvaloniaXamlLoader.Load(this);
        BuildInfo = ComputeBuildInfo();
    }

    /// <inheritdoc cref="AppNameProperty"/>
    public string AppName
    {
        get => GetValue(AppNameProperty);
        set => SetValue(AppNameProperty, value);
    }

    /// <inheritdoc cref="LogoProperty"/>
    public IImage? Logo
    {
        get => GetValue(LogoProperty);
        set => SetValue(LogoProperty, value);
    }

    /// <inheritdoc cref="StatusProperty"/>
    public string Status
    {
        get => GetValue(StatusProperty);
        set => SetValue(StatusProperty, value);
    }

    /// <inheritdoc cref="BuildInfoProperty"/>
    public string BuildInfo
    {
        get => GetValue(BuildInfoProperty);
        set => SetValue(BuildInfoProperty, value);
    }

    /// <summary>Load the app mark from an <c>avares://</c> uri — the one-liner for the common case.</summary>
    public void SetLogo(string avaresUri) =>
        Logo = new Bitmap(AssetLoader.Open(new Uri(avaresUri)));

    /// <summary>Boot phases arrive from whatever thread the launch work runs on; hop to the UI thread.</summary>
    public void Report(string value) => Dispatcher.UIThread.Post(() => Status = value);

    /// <summary>
    /// "Which build is this?" from the ENTRY assembly — channel via <see cref="DebuggableAttribute"/> (the
    /// app's own configuration, not the configuration this library was packaged with), build time from the
    /// assembly file's write time: the build time in dev, the install time for a packaged app — both the
    /// honest answer, with no build-system counter that would dirty incremental builds.
    /// </summary>
    [UnconditionalSuppressMessage("SingleFile", "IL3000", Justification =
        "Location is empty in a single-file app, and that case falls back to Environment.ProcessPath.")]
    public static string ComputeBuildInfo()
    {
        var entry = Assembly.GetEntryAssembly();
        var channel = entry?.GetCustomAttribute<DebuggableAttribute>()?.IsJITTrackingEnabled == true
            ? "Debug" : "Release";
        try
        {
            var path = entry?.Location;
            if (string.IsNullOrEmpty(path)) path = Environment.ProcessPath;
            if (!string.IsNullOrEmpty(path) && File.Exists(path))
            {
                return $"{channel} · built {File.GetLastWriteTime(path):MMM d, h:mm tt}";
            }
        }
        catch { /* vitals must never break boot */ }
        return channel;
    }
}
