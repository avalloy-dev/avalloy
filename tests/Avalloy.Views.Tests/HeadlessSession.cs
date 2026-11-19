using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Themes.Fluent;
using Avalonia.Threading;

[assembly: AvaloniaTestApplication(typeof(Avalloy.Views.Tests.HeadlessSession))]

namespace Avalloy.Views.Tests;

/// <summary>
/// The one headless Avalonia session these tests share: Avalonia's platform is process-wide, so two sessions
/// would fight over it. Fluent supplies the control templates (a ListBox needs its ScrollViewer to scroll);
/// drawing is headless, since nothing here reads pixels — only layout, focus, routing and automation.
/// </summary>
public static class HeadlessSession
{
    public sealed class App : Application
    {
        public override void Initialize() => Styles.Add(new FluentTheme());
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>().UseHeadless(new AvaloniaHeadlessPlatformOptions());

    public static HeadlessUnitTestSession Get() =>
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(HeadlessSession).Assembly);

    /// <summary>Run posted work and lay the window out until it is stable — a few passes, since a layout pass
    /// can post work (the palette's deferred focus, a scroll) that changes layout again.</summary>
    public static void Settle(Window window)
    {
        for (var i = 0; i < 3; i++)
        {
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
        }
        Dispatcher.UIThread.RunJobs();
    }
}
