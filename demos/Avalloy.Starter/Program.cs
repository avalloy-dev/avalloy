using Avalonia;

namespace Avalloy.Starter;

internal sealed class Program
{
    [STAThread]
    public static void Main(string[] args) =>
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            // OVERLAY popups are required for the painted popover chrome: .popoverframe/
            // .popovercard draw their Menu.Shadow as a BoxShadow, which clips to a dirty
            // square band in a separate native popup window.
            .With(new AvaloniaNativePlatformOptions { OverlayPopups = true })
            .With(new Win32PlatformOptions { OverlayPopups = true })
            .LogToTrace();
}
