using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using Avalloy.Commands;
using Avalloy.Icons;
using Avalloy.Preferences;
using Avalloy.Services;
using Avalloy.Theming;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Avalloy;

/// <summary>
/// Base <see cref="Application"/> for Avalloy-built apps. Owns the
/// cross-cutting platform bootstrap:
/// <list type="bullet">
///   <item>Builds the DI container (subclass declares services + commands
///         via <see cref="ConfigureServices"/>).</item>
///   <item>Auto-registers every <see cref="CommandRegistry"/> entry as a
///         singleton service so the dispatcher can resolve it cheaply.</item>
///   <item>Spawns the main window via <see cref="CreateMainWindow"/> and
///         publishes it on <see cref="ShellHost"/> for dialog parenting.</item>
///   <item>Builds the <see cref="KeyboardDispatcher"/> with the registry,
///         service provider, and the keymap from <see cref="BuildKeymap"/>.</item>
///   <item>Sets the macOS dock / Cmd-Tab icon from
///         <see cref="DockIconAvaresUri"/>.</item>
///   <item>Drives the optional theming seam: applies the resolved
///         <see cref="IThemeController"/> at startup + on OS variant/accent change,
///         and republishes accent resources via the optional
///         <see cref="IAccentPublisher"/>.</item>
/// </list>
/// Avalloy.Desktop takes no dependency on any concrete theme engine: an app picks the shell
/// (via <c>ShellAvalloyApp</c>) or the native engine (Avalloy.Themes)
/// by which package it references and which seam it returns.
/// </summary>
public abstract class AvalloyApp : Application
{
    // LogBuffer lives in the UI-free Avalloy.Logging, where its default flush is inline. In an app, a
    // viewer bound to ANY buffer — Avalloy's own or one the app creates — expects its batches on the UI
    // thread, as they always arrived when LogBuffer was part of Core. Installed in the type initializer
    // so it precedes every buffer an AvalloyApp creates.
    static AvalloyApp() => LogBuffer.DefaultPost = PostToUiThread;

    private static void PostToUiThread(Action action)
    {
        try
        {
            Dispatcher.UIThread.Post(action, DispatcherPriority.Background);
        }
        catch (NullReferenceException)
        {
            action(); // no dispatcher (test env) — run inline
        }
    }

    /// <summary>The fully-built DI container. Null until
    /// <see cref="OnFrameworkInitializationCompleted"/> finishes. Consumers
    /// should accept their dependencies via constructor injection rather than
    /// reach through here; the field is intentionally not exposed via a
    /// static "Current" accessor.</summary>
    public IServiceProvider Services { get; private set; } = null!;

    /// <summary>The optional theme engine resolved from
    /// <see cref="ResolveThemeController"/>. Null for apps that don't theme.</summary>
    private IThemeController? _themeController;

    /// <summary>The optional accent publisher resolved from
    /// <see cref="ResolveAccentPublisher"/>. Null leaves the app accent-only /
    /// token-driven (e.g. native-theme apps whose dictionaries define accents).</summary>
    private IAccentPublisher? _accentPublisher;

    /// <summary>Bootstrap configuration sourced from <c>appsettings.json</c>
    /// (next to the entry assembly) + environment variables. Populated before
    /// the DI container is built and registered as a singleton, so consumers
    /// can inject <see cref="IConfiguration"/>.</summary>
    public IConfiguration Configuration { get; private set; } = null!;

    /// <summary>The per-app command catalog. Populated inside
    /// <see cref="ConfigureServices"/> (either via explicit
    /// <see cref="CommandRegistry.Register{T}"/> calls or
    /// <see cref="CommandRegistry.RegisterFromAssembly"/>).</summary>
    public CommandRegistry Commands { get; } = new();

    /// <summary>The keyboard dispatcher; resolves gestures → commands and
    /// invokes them via DI. Null until initialization completes.</summary>
    public KeyboardDispatcher Keyboard { get; private set; } = null!;

    /// <summary>Override to register services and populate
    /// <see cref="Commands"/>. Called once during initialization, before the
    /// container is built.</summary>
    protected virtual void ConfigureServices(IServiceCollection services) { }

    /// <summary>Override to register layer-level built-in services that should be
    /// present regardless of whether the app subclass calls <c>base</c> in
    /// <see cref="ConfigureServices"/>. Avalloy calls this unconditionally during
    /// the built-in registration phase (before <see cref="ConfigureServices"/>), so
    /// the app can still replace these via a later <c>AddSingleton</c>. Used by
    /// <c>ShellAvalloyApp</c> to register the themed dialog service.</summary>
    protected virtual void ConfigureBuiltInServices(IServiceCollection services) { }

    /// <summary>Override to add extra logging providers. Avalloy already
    /// wires console + debug + rolling-file providers and the Avalonia Developer
    /// Tools observable (Debug builds). Override and call base, then add any
    /// app-specific sinks. Error reporting is the app's to opt into here too —
    /// Avalloy carries no reporter of its own, so a consumer that wants one
    /// references it (Sentry, say) and adds it after <c>base</c>:
    /// <code>
    /// protected override void ConfigureLogging(ILoggingBuilder logging)
    /// {
    ///     base.ConfigureLogging(logging);
    ///     logging.AddSentry(o => Configuration.GetSection("Sentry").Bind(o));
    /// }
    /// </code>
    /// <see cref="Configuration"/> is built before this runs, so a DSN or an
    /// opt-out preference can gate the call.</summary>
    protected virtual void ConfigureLogging(ILoggingBuilder logging)
    {
        logging.AddConfiguration(Configuration.GetSection("Logging"));
        logging.SetMinimumLevel(LogLevel.Information);
        // In-memory ring, retained from process start so a viewer opened later
        // still shows startup logging. The LogViewer control binds to
        // AlloyLogging.Buffer.
        AlloyLogging.Buffer = new LogBuffer(LogBufferCapacity);
        logging.AddProvider(new LogBufferProvider(AlloyLogging.Buffer));
        logging.AddSimpleConsole(o =>
        {
            o.SingleLine = true;
            o.TimestampFormat = "HH:mm:ss.fff ";
        });
        logging.AddDebug();
        // File sink. A packaged app launched from Finder/Explorer has no console, so
        // console + debug alone mean a crash leaves NO record of the exception — just an
        // OS report with an unsymbolicated JIT stack. Everything logged now also lands in
        // AlloyLogging.LogFilePath, which is the first thing to read after a crash.
        if (FileLoggerProvider.TryCreate(Paths) is { } fileProvider)
        {
            logging.AddProvider(fileProvider);
            // Held, not published: the logger factory doesn't exist until the service container is built, and a
            // path visible before it would tell a caller the log is ready while its lines still went nowhere.
            // InitializeAvalloy publishes both together (AlloyLogging.Publish).
            _logFilePath = fileProvider.FilePath;
        }
    }

    private string? _logFilePath;

    /// <summary>Entries retained by the in-memory <see cref="LogBuffer"/> the
    /// <c>LogViewer</c> control displays. Override to trade memory for history.</summary>
    protected virtual int LogBufferCapacity => 2000;

    /// <summary>When true (the default), Avalloy attaches a
    /// <see cref="AlloyLoggingTraceListener"/> once the logger factory is
    /// published, so <c>System.Diagnostics.Trace</c> output from UI-free library
    /// assemblies lands in the configured sinks instead of nowhere.</summary>
    protected virtual bool BridgeTraceToLogging => true;

    /// <summary>Minimum Avalonia log level bridged into <see cref="AlloyLogging"/>
    /// via <see cref="AvaloniaLogSink"/>, or null to leave Avalonia's logger
    /// untouched. Defaults to Warning in Debug builds (binding errors surface in
    /// the console/file/viewer) and null in Release. Apps wanting binding-error
    /// forensics from the field can return Warning unconditionally.</summary>
    protected virtual Avalonia.Logging.LogEventLevel? AvaloniaLogBridgeLevel =>
#if DEBUG
        Avalonia.Logging.LogEventLevel.Warning;
#else
        null;
#endif

    /// <summary>Minimum level surfaced in the DevTools logs panel. Defaults
    /// to <see cref="LogLevel.Debug"/> so the panel is useful for inspection
    /// even when the file/console providers are tuned higher.</summary>
    protected virtual LogLevel DevToolsLogLevel => LogLevel.Debug;

    /// <summary>Override to construct the main window. Typically
    /// <c>Services.GetRequiredService&lt;MainWindow&gt;()</c> after registering
    /// <c>MainWindow</c> with the container in
    /// <see cref="ConfigureServices"/>, or just <c>new MainWindow()</c> for
    /// simple cases.</summary>
    protected abstract Window CreateMainWindow();

    /// <summary>Override to return the default keymap (command name → list of
    /// gesture strings). Apps typically merge built-in defaults with user
    /// preference overrides here. Default is empty (no shortcuts).</summary>
    protected virtual IReadOnlyDictionary<string, IReadOnlyList<string>> BuildKeymap() =>
        new Dictionary<string, IReadOnlyList<string>>();

    /// <summary>Override to supply the Avalonia resource URI for the macOS
    /// dock / Cmd-Tab icon. Return <c>null</c> to skip — production <c>.app</c>
    /// bundles supply an <c>.icns</c> that supersedes the runtime call.</summary>
    protected virtual string? DockIconAvaresUri => null;

    /// <summary>When true (the default), Avalloy resolves the user's real login-shell PATH at the very
    /// start of initialization and merges it into the process environment — see <see cref="LoginShellPath"/>.
    /// A packaged macOS/Linux <c>.app</c> launched from Finder/Dock otherwise gets only a minimal PATH, so any
    /// bare-name child process the app spawns (a CLI, <c>$EDITOR</c>, a version-managed <c>node</c>, …) can't be
    /// found. Override to false for an app that never shells out and wants to skip the one-time shell probe.</summary>
    protected virtual bool ResolveLoginShellPath => true;

    /// <summary>When true, the app runs under <see cref="ShutdownMode.OnExplicitShutdown"/>
    /// so closing the last window does NOT exit the process — the host app decides
    /// (e.g. hide to the tray / menu bar and keep background work running). The app
    /// only exits via <see cref="RequestQuit"/> or an OS-level quit (⌘Q, app-menu
    /// Quit, logout), both of which set <see cref="IsQuitRequested"/>. Default false:
    /// classic last-window-closes-the-app behaviour. No-op outside the desktop lifetime.</summary>
    protected virtual bool KeepRunningWhenClosed => false;

    /// <summary>True once a real application quit has been requested — either via
    /// <see cref="RequestQuit"/> or an OS shutdown request (⌘Q / app-menu Quit /
    /// logout, surfaced through <see cref="IClassicDesktopStyleApplicationLifetime.ShutdownRequested"/>).
    /// Window close handlers read this to tell "quit the app" apart from "hide the
    /// window". Only meaningful when <see cref="KeepRunningWhenClosed"/> is true.</summary>
    public bool IsQuitRequested { get; private set; }

    /// <summary>Request a real application exit. Marks <see cref="IsQuitRequested"/>
    /// so window close handlers stop intercepting, then shuts the lifetime down.
    /// Use this for the tray / menu-bar "Quit" affordance under
    /// <see cref="KeepRunningWhenClosed"/>.</summary>
    public void RequestQuit()
    {
        IsQuitRequested = true;
        (ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.Shutdown();
    }

    /// <summary>Re-show and foreground the main window after a close-to-background
    /// hide. Safe to call from any thread (marshals to the UI thread); no-op if
    /// there's no main window. Used by single-instance activation (a second launch
    /// signalling the running one) and, later, the tray/menu-bar "Open" action.</summary>
    public void ShowMainWindowFromBackground()
    {
        void Run()
        {
            var window = (ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;
            if (window is null) return;
            if (!window.IsVisible) window.Show();
            if (window.WindowState == WindowState.Minimized) window.WindowState = WindowState.Normal;
            window.Activate();
            // Promote the process to frontmost / unhide on macOS (close-to-background
            // there hides the whole app via NSApp; Activate un-hides it).
            MacAppActivation.Activate();
        }

        try
        {
            if (Dispatcher.UIThread.CheckAccess()) Run();
            else Dispatcher.UIThread.Post(Run);
        }
        catch (NullReferenceException)
        {
            Run(); // no dispatcher (test env) — run inline
        }
    }

    /// <summary>App data folder under <see cref="Environment.SpecialFolder.LocalApplicationData"/>.
    /// Defaults to the entry assembly name, so the user icon-pack path is correct with no
    /// override. Override to pin a name independent of the assembly.</summary>
    protected virtual string AppDataFolderName =>
        Assembly.GetEntryAssembly()?.GetName().Name ?? "AvalloyApp";

    /// <summary>Where this app's config, data and logs live, registered in DI as <see cref="AppPaths"/>
    /// for anything that writes a file — Desktop's own log and icon-pack folders included. Defaults to the
    /// platform conventions for <see cref="AppDataFolderName"/>; override with <c>base.Paths with { … }</c>
    /// to relocate one folder.</summary>
    protected virtual AppPaths Paths => _paths ??= AppPaths.ForApp(AppDataFolderName);

    private AppPaths? _paths;

    /// <summary>Extra app-bundled icon-pack sources beyond Avalloy's baseline Lucide.
    /// Default: none — the app inherits Avalloy's pack. Override to ship packs from the
    /// app's own assembly (e.g. <c>new IconPackSource { PacksBaseUri = "avares://MyApp/Assets/iconpacks/", PackIds = ["my-pack"] }</c>).</summary>
    protected virtual IReadOnlyList<IconPackSource> AppIconPackSources => Array.Empty<IconPackSource>();

    /// <summary>Icon pack selected at startup. Defaults to Avalloy's baseline "lucide".</summary>
    protected virtual string DefaultIconPackId => "lucide";

    /// <summary>Whether an unhandled exception in a UI-thread dispatcher job should be
    /// swallowed after it's logged. Default <c>false</c>: log, then let the process die,
    /// which is the pre-existing behavior. Override to <c>true</c> to keep the app alive —
    /// worth it for a shell that can plausibly carry on, but be clear about the trade:
    /// execution resumes with whatever state the aborted job left half-written.
    /// <para>All-or-nothing. When only some exceptions are worth surviving, override
    /// <see cref="ShouldHandleDispatcherException"/> instead.</para></summary>
    protected virtual bool HandleDispatcherExceptions => false;

    /// <summary>Per-exception version of <see cref="HandleDispatcherExceptions"/>: decides
    /// whether <paramref name="exception"/> should be swallowed after it's logged. Defaults
    /// to <see cref="HandleDispatcherExceptions"/>, so overriding neither keeps the
    /// let-it-die behavior and overriding only the bool keeps working.
    /// <para>This is the hook to use when a specific, well-understood failure shouldn't be
    /// fatal while everything else still is — the motivating case being an exception raised
    /// from inside a layout or render pass, where the throw is in framework bookkeeping
    /// rather than in app state, so carrying on is a far better bet than losing the
    /// session. Keep the predicate narrow; the wider it gets the closer it is to the
    /// blanket <c>true</c> this exists to avoid.</para></summary>
    /// <param name="exception">The exception that escaped the dispatcher job.</param>
    protected virtual bool ShouldHandleDispatcherException(Exception exception)
        => HandleDispatcherExceptions;

    /// <summary>Override to supply the app's theme engine so Avalloy owns the
    /// theme-apply lifecycle: an initial apply at startup and a re-apply on every OS
    /// accent / light-dark change. Default <c>null</c> means no theming — the app is
    /// responsible for any theme wiring itself. Shell apps return their
    /// <c>ThemeService</c> (see <c>ShellAvalloyApp</c>); native-theme apps
    /// return a <c>NativeThemeController</c>.</summary>
    protected virtual IThemeController? ResolveThemeController() => null;

    /// <summary>Override to supply the accent publisher Avalloy drives at startup,
    /// on theme-variant flip, and on OS accent change. Default <c>null</c> leaves the
    /// app accent-only / token-driven (native-theme dictionaries define accents
    /// directly, so they typically need no publisher).</summary>
    protected virtual IAccentPublisher? ResolveAccentPublisher() => null;

    /// <summary>Override to supply the appearance prefs <see cref="ReapplyTheme"/>
    /// hands to the theme engine. Read fresh on every apply so it always reflects the
    /// latest preference state. Default <c>null</c> (no theming).</summary>
    protected virtual IAppearanceModel? GetThemeAppearance() => null;

    /// <summary>Apply the current appearance through the resolved theme engine.
    /// No-op when either the engine or the appearance is null, so it's safe to wire
    /// unconditionally. The app subclass calls this from its own preferences-changed
    /// trigger; Avalloy calls it at startup and on OS accent / variant changes.</summary>
    protected void ReapplyTheme()
    {
        var controller = _themeController;
        var appearance = GetThemeAppearance();
        if (controller is null || appearance is null) return;
        controller.Apply(appearance);
    }

    /// <summary>Push the appearance model's <see cref="IAppearanceModel.IconPackId"/>
    /// onto the shared <see cref="IconResolver"/>. No-op when there's no appearance
    /// model or the id is empty (the resolver keeps its default pack). Like
    /// <see cref="ReapplyTheme"/>, Avalloy calls this once at startup and the app
    /// subclass calls it from its own preferences-changed trigger; the resolver's
    /// <see cref="IconResolver.ActivePackChanged"/> event handles UI refresh from there.</summary>
    protected void ApplyIconPack()
    {
        var resolver = Services?.GetService<IconResolver>();
        if (resolver is null) return;
        var packId = GetThemeAppearance()?.IconPackId;
        if (!string.IsNullOrEmpty(packId)) resolver.SetActivePack(packId);
        // Always (re)load the active pack's baked IconGlyph dictionary into app
        // resources so the GlyphIcon control can resolve Icon.<id> — even with no
        // appearance model, where the resolver keeps its default pack (Lucide).
        SwapIconPackResources(resolver);
    }

    private ResourceDictionary? _iconPackResources;

    /// <summary>Swap the active pack's baked dictionary into
    /// <see cref="Application.Resources"/>: remove the previous pack's, add the
    /// current one. This is how "pack switching" works — <c>GlyphIcon</c> stays a
    /// plain <c>TryFindResource("Icon."+id)</c> lookup and only one pack is merged at
    /// a time, so ids never collide across packs. See ADR 0001.</summary>
    private void SwapIconPackResources(IconResolver resolver)
    {
        var dict = resolver.BuildActivePackResources();
        if (dict is null) return;
        if (_iconPackResources is not null) Resources.MergedDictionaries.Remove(_iconPackResources);
        _iconPackResources = dict;
        Resources.MergedDictionaries.Add(dict);
    }

    /// <summary>Override to customize the configuration sources. Loads an
    /// <c>appsettings.json</c> <em>embedded</em> in the entry assembly (the
    /// baseline) plus an optional on-disk <c>appsettings.json</c> next to the
    /// entry assembly (a dev/ops override) plus environment variables.
    /// Subclasses can add per-environment files, user secrets, command-line
    /// overrides, etc.
    ///
    /// The embedded copy is the baseline because shipping appsettings.json as a
    /// loose file inside a macOS <c>.app</c> bundle's <c>Contents/MacOS</c>
    /// directory breaks codesign: that directory may contain only Mach-O code,
    /// and codesign rejects the bundle ("In subcomponent: …/appsettings.json,
    /// code object is not signed at all"). Embedding keeps the bundle
    /// signable while preserving the config.</summary>
    protected virtual void ConfigureAppConfiguration(IConfigurationBuilder builder)
    {
        // 1. Embedded baseline (works inside a signed, single-file bundle).
        var entryAsm = Assembly.GetEntryAssembly();
        var resName = entryAsm?.GetManifestResourceNames()
            .FirstOrDefault(n => n.EndsWith("appsettings.json", StringComparison.OrdinalIgnoreCase));
        if (entryAsm is not null && resName is not null)
        {
            using var res = entryAsm.GetManifestResourceStream(resName);
            if (res is not null)
            {
                // Copy to a MemoryStream: the JSON provider reads the stream
                // during builder.Build() (after this method returns), so the
                // source stream must not be disposed before then.
                var ms = new MemoryStream();
                res.CopyTo(ms);
                ms.Position = 0;
                builder.AddJsonStream(ms);
            }
        }

        // 2. Optional on-disk override next to the entry assembly (dev/ops).
        var entryDir = Path.GetDirectoryName(AppContext.BaseDirectory) ?? AppContext.BaseDirectory;
        builder.SetBasePath(entryDir);
        builder.AddJsonFile("appsettings.json", optional: true, reloadOnChange: false);
        builder.AddEnvironmentVariables();
    }

    // ── Launch splash (opt-in) ─────────────────────────────────────────────────────────────────
    // Apps whose boot does real work (opening stores, cloud sync, engine spawn) can put a splash on
    // screen FIRST and run that work off the UI thread, instead of the window appearing only after
    // everything is ready. Return a window (usually Controls.Splash.AlloySplashWindow) from
    // CreateSplashWindow to opt in; the launch then becomes:
    //
    //   splash.Show() → PrepareLaunchAsync (background thread, phases → IProgress) → Avalloy init
    //   (MainWindow built against the prepared state) → MainWindow.Show() UNDER the Topmost splash →
    //   hold until LaunchLanded (bounded) → close the splash after the next rendered frame.
    //
    // The default (null) keeps the classic synchronous launch byte-for-byte.

    /// <summary>Return a splash window to opt into the deferred launch; null (the default) keeps the
    /// classic synchronous launch. The window is shown before any boot work runs — apps should apply
    /// their theme first if they override <see cref="OnFrameworkInitializationCompleted"/>'s prefix
    /// (Avalloy applies the resolved theme controller before showing it).</summary>
    protected virtual Window? CreateSplashWindow() => null;

    /// <summary>The slow half of boot (open stores, sync, spawn engines), run OFF the UI thread while
    /// the splash shows. Runs BEFORE the DI container exists — build app state into fields your
    /// service factories then hand out. Report user-facing phases through <paramref name="progress"/>
    /// (an <see cref="Controls.Splash.AlloySplashWindow"/> displays them live). A throw keeps the splash
    /// up wearing the error message rather than launching a broken app.</summary>
    protected virtual Task PrepareLaunchAsync(IProgress<string> progress) => Task.CompletedTask;

    /// <summary>Optional landing signal: return a task that completes when the shell's first REAL
    /// content is up (tree loaded, workspace restored) and the splash holds until then — the main
    /// window's first frame is usually an empty shell, and dropping the splash onto it reads as a
    /// flash of nothing. Null (the default) releases the splash on the first rendered frame. Report a
    /// phase ("Loading your workspace…") through <paramref name="progress"/> if the wait is long.</summary>
    protected virtual Task? LaunchLanded(IProgress<string> progress) => null;

    /// <summary>Upper bound on the <see cref="LaunchLanded"/> hold, so a faulted shell load can never
    /// strand a Topmost splash over the app.</summary>
    protected virtual TimeSpan SplashLandingTimeout => TimeSpan.FromSeconds(30);

    public override void OnFrameworkInitializationCompleted()
    {
        // 0a. Resolve the login-shell PATH before anything can spawn a child process. A packaged
        //     macOS/Linux .app launched from Finder/Dock inherits only /usr/bin:/bin:/usr/sbin:/sbin,
        //     so bare-name spawns (CLIs, $EDITOR, a version-managed node) would come up "not found".
        //     Runs before the subclass's engine/service boot — including PrepareLaunchAsync below,
        //     which is exactly the code that spawns CLIs on the deferred path.
        if (ResolveLoginShellPath)
            LoginShellPath.Ensure();

        // Deferred launch: splash first, the slow boot off-thread, Avalloy init afterwards.
        // The design-time XAML previewer boots the App for styles/resources only — no splash, no
        // deferred work there; it takes the classic path like any non-desktop lifetime.
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime
            && !Design.IsDesignMode
            && CreateSplashWindow() is { } splash)
        {
            // Theme early, so the splash paints in the user's palette rather than the default
            // variant. Safe to resolve twice: InitializeAvalloy re-resolves after DI is up, and
            // controllers cache or re-apply idempotently. Best-effort by design — an app whose
            // appearance model isn't constructible yet (prefs built in ConfigureServices, which
            // hasn't run) just gets a default-variant splash, never a failed launch.
            try
            {
                _themeController = ResolveThemeController();
                ReapplyTheme();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.TraceWarning(
                    "Splash theme early-apply skipped (appearance not ready yet): " + ex.Message);
            }
            splash.Show();

            var progress = splash as IProgress<string> ?? new NullProgress();
            Task.Run(() => PrepareLaunchAsync(progress))
                .ContinueWith(t => Dispatcher.UIThread.Post(() => FinishDeferredLaunch(splash, progress, t)));
            return;
        }

        InitializeAvalloy();
    }

    private sealed class NullProgress : IProgress<string>
    {
        public void Report(string value) { }
    }

    /// <summary>
    /// Second half of the deferred launch, back on the UI thread once <see cref="PrepareLaunchAsync"/>
    /// finished (or faulted). Avalloy init builds MainWindow against the prepared state; the window
    /// is then shown manually — the lifetime auto-shows only a MainWindow that existed before
    /// <c>Start()</c> returned, and ours deliberately did not.
    /// </summary>
    private void FinishDeferredLaunch(Window splash, IProgress<string> progress, Task prepared)
    {
        if (prepared.Status != TaskStatus.RanToCompletion)
        {
            // The splash stays up wearing the error — there is nothing to show behind it, and quitting
            // needs no wiring. Trace, not AlloyLogging: the logger factory is built in Avalloy init,
            // which never ran.
            var root = prepared.Exception?.GetBaseException();
            progress.Report("Launch failed — " + (root?.Message ?? "cancelled"));
            System.Diagnostics.Trace.TraceError("Deferred launch failed before Avalloy init: " + root);
            return;
        }

        InitializeAvalloy();

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop
            && desktop.MainWindow is { } main)
        {
            // Shown UNDER the (Topmost) splash, so first layout + paint happen covered; the handoff
            // releases the splash once content is up. Close-ordering matters throughout: with
            // ShutdownMode.OnLastWindowClose there must always be a window on screen.
            main.Show();
            main.Activate();
            _ = SplashHandoffAsync(main, splash, progress);
        }
        else
        {
            splash.Close(); // no main window to hand off to — don't strand a Topmost splash
        }
    }

    /// <summary>Hold the splash until the app reports its shell has landed (bounded by
    /// <see cref="SplashLandingTimeout"/>), then release it after the next rendered frame — Show()
    /// returns before first paint, and closing the splash in that gap flashes bare desktop.</summary>
    private async Task SplashHandoffAsync(Window main, Window splash, IProgress<string> progress)
    {
        try
        {
            if (LaunchLanded(progress) is { } landed)
                await Task.WhenAny(landed, Task.Delay(SplashLandingTimeout));
        }
        catch { /* a faulted landing signal must not strand the splash */ }
        main.RequestAnimationFrame(_ => Dispatcher.UIThread.Post(splash.Close));
    }

    /// <summary>The classic Avalloy bootstrap — config, DI, MainWindow, theming. One body serving
    /// both paths: directly from <see cref="OnFrameworkInitializationCompleted"/>, or after
    /// <see cref="PrepareLaunchAsync"/> on the deferred (splash) path.</summary>
    private void InitializeAvalloy()
    {
        // 0. Build configuration first — ConfigureLogging may gate sinks on it,
        //    and we register it in DI for app code that wants it.
        var configBuilder = new ConfigurationBuilder();
        ConfigureAppConfiguration(configBuilder);
        Configuration = configBuilder.Build();

        // 1. Build the DI container. Avalloy built-ins first so subclass
        //    ConfigureServices can replace them with AddSingleton calls if
        //    desired (last registration wins for AddSingleton<TService,TImpl>).
        var services = new ServiceCollection();
        services.AddSingleton(Configuration);
        // Logging gets its own AddLogging call so subclasses can extend via
        // ConfigureLogging (an error reporter, Seq, extra sinks). The factory is
        // also pulled out and parked on AlloyLogging so non-DI call sites
        // (XAML views, static helpers) can reach it without service-locating
        // through Services.
        services.AddLogging(ConfigureLogging);
        services.AddSingleton(Commands);
        services.AddSingleton<ShellHost>();
        // Toast notifications. The shell window is expected to call
        // ToastService.Attach(topLevel) once it's open so queued + future
        // toasts can render via WindowNotificationManager.
        services.AddSingleton<ToastService>();
        // The same instance, as the notifier the UI-less packages (Avalloy.Updates) talk to the user through.
        services.AddSingleton<IUserNotifier>(sp => sp.GetRequiredService<ToastService>());
        services.AddSingleton(Paths);
        // Event recording + the retained log ring, injectable for app code and
        // the LogViewer. Factory form so they resolve the instances the
        // AddLogging call above actually wired.
        services.AddSingleton<IEventRecorder, LoggingEventRecorder>();
        services.AddSingleton(_ => AlloyLogging.Buffer);
        // Icon packs: Avalloy's baseline Lucide (bundled in this assembly) plus any
        // app-supplied sources. Lazy factory captures `this` so the Paths /
        // AppIconPackSources / DefaultIconPackId hooks stay overridable. Apps that build
        // the resolver eagerly (e.g. a hand-built composition bundle) can register their
        // own instance in ConfigureServices — last registration wins, and this factory
        // then never runs.
        services.AddSingleton(_ => new IconResolver(AvalloyIconPacks.DefaultOptions(
            Paths, AppIconPackSources, DefaultIconPackId)));
        // KeyboardDispatcher: registered via factory so it's lazily built on
        // first resolve (after Services is sealed). Captures `this` so
        // BuildKeymap stays an instance hook subclasses can override.
        services.AddSingleton<KeyboardDispatcher>(sp =>
            new KeyboardDispatcher(
                sp.GetRequiredService<CommandRegistry>(),
                sp,
                BuildKeymap));
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktopLifetime)
            services.AddSingleton<IClassicDesktopStyleApplicationLifetime>(desktopLifetime);

        // Layer-level built-ins (e.g. the shell's dialog service), registered before the
        // app's ConfigureServices so they're always present yet still overridable.
        ConfigureBuiltInServices(services);

        ConfigureServices(services);

        // 2. Auto-register every commanded type as a singleton so the dispatcher
        //    can resolve it cheaply. Apps that want a different lifetime can register
        //    explicitly in ConfigureServices BEFORE we get here — DI's first-
        //    registration-wins for keyed services means our AddSingleton
        //    becomes a no-op if the type is already registered.
        foreach (var descriptor in Commands.All)
            services.TryAddSingletonImplementation(descriptor.ImplementationType);

        Services = services.BuildServiceProvider();
        // Resolve the keyboard dispatcher now so the public Keyboard property
        // is non-null for the rest of init (CreateMainWindow can hand it to
        // its window via DI, and subclasses wiring prefs-reload hooks can
        // subscribe to its Rebuild method).
        Keyboard = Services.GetRequiredService<KeyboardDispatcher>();

        // Publish the factory for non-DI callers (AlloyLogging.CreateLogger<T>()), then the log file's path.
        // Set before any logging happens during the rest of init.
        AlloyLogging.Publish(Services.GetRequiredService<ILoggerFactory>(), _logFilePath);

        // Bridges that need the factory to exist: Trace output from UI-free
        // library assemblies, and Avalonia's internal log (binding errors etc.).
        if (BridgeTraceToLogging)
            AlloyLoggingTraceListener.Install();
        if (AvaloniaLogBridgeLevel is { } avaloniaLevel)
            AvaloniaLogSink.Install(avaloniaLevel);

        // The ONLY hook that actually sees a UI-thread exception. Note what this is NOT:
        // AppDomain.CurrentDomain.UnhandledException is notify-only on .NET Core — the runtime
        // tears the process down (TerminateProcess → abort()) no matter what the handler does,
        // so it can log but never rescue. Dispatcher.UnhandledException fires while the job that
        // threw is still being unwound, and a handler that sets Handled genuinely swallows.
        // Covers anything run through the dispatcher: Post/InvokeAsync continuations,
        // DispatcherTimer ticks, async-void handlers resuming on the UI thread.
        Dispatcher.UIThread.UnhandledException += (_, e) =>
        {
            AlloyLogging.CreateLogger("Avalloy.Dispatcher")
                .LogCritical(e.Exception, "Unhandled exception in a UI-thread dispatcher job");
            // Default is to let it crash — swallowing leaves the app running on whatever
            // half-updated state the throw abandoned, which is its own class of bug report.
            // Apps that would rather stay up override HandleDispatcherExceptions (all of
            // them) or ShouldHandleDispatcherException (only the ones they've reasoned about).
            e.Handled = ShouldHandleDispatcherException(e.Exception);
        };

        // 3. Spawn the main window. Subclass decides whether it's DI-resolved
        //    or directly constructed.
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // Opt-in tray/menu-bar lifetime: don't let the last window's close exit
            // the process. The host app's window-close handler hides instead, and
            // RequestQuit()/OS shutdown requests perform the real exit. An OS quit
            // (⌘Q, app-menu Quit, logout) arrives as ShutdownRequested — record it so
            // close handlers know it's a quit, not a hide.
            if (KeepRunningWhenClosed)
            {
                desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
                desktop.ShutdownRequested += (_, _) => IsQuitRequested = true;
            }

            var mainWindow = CreateMainWindow();
            desktop.MainWindow = mainWindow;
            Services.GetRequiredService<ShellHost>().MainWindow = mainWindow;
        }

        // 4. macOS dock icon. No-op off macOS or when no URI supplied.
        if (!string.IsNullOrEmpty(DockIconAvaresUri))
            MacAppIcon.SetFromAvaloniaResource(DockIconAvaresUri);

#if DEBUG
        // 5. Attach Avalonia Developer Tools and hand it our logger factory so
        //    the Logs panel surfaces ILogger output alongside Avalonia's own
        //    Visual/Layout/Binding events. Hosting apps should NOT also call
        //    .WithDeveloperTools() on the AppBuilder — that path takes no
        //    options and would double-attach.
        this.AttachDeveloperTools(o =>
            o.AddMicrosoftLoggerObservable(AlloyLogging.Factory, DevToolsLogLevel));
#endif

        base.OnFrameworkInitializationCompleted();

        // Resolve the optional theming seams. Apps that don't theme return null and
        // everything below stays inert — exactly the pre-theming behavior.
        _themeController = ResolveThemeController();
        _accentPublisher = ResolveAccentPublisher();
        ReapplyTheme(); // initial apply (no-op without an engine + appearance)
        ApplyIconPack(); // initial active-pack selection (no-op without an appearance model)

        RefreshAccentBrush();
        ActualThemeVariantChanged += (_, _) => RefreshAccentBrush();
        // Live OS-accent tracking. Both macOS (Settings → Appearance → Accent)
        // and Windows (Settings → Personalization → Colors) raise
        // ColorValuesChanged on PlatformSettings when the user changes their
        // accent (or flips system light/dark) at runtime. Re-apply the theme first
        // — that re-resolves the System variant + repaints the accent wash — then
        // republish {Prefix}AccentSelectionBrush so both follow along without a
        // restart. ReapplyTheme is a no-op for accent-only apps.
        if (PlatformSettings is { } ps)
            ps.ColorValuesChanged += (_, _) =>
            {
                ReapplyTheme();
                RefreshAccentBrush();
            };
    }

    /// <summary>
    /// Re-resolve and republish the app's accent resources via the resolved
    /// <see cref="IAccentPublisher"/> (e.g. <c>{Prefix}AccentBrush</c> /
    /// <c>{Prefix}AccentColor</c> / <c>{Prefix}AccentSelectionBrush</c> for binding
    /// via <c>{DynamicResource AppAccentBrush}</c>). No-op when no publisher is
    /// resolved. Avalloy calls this at startup, on theme-variant flip, and on OS
    /// accent change; apps may call it after a manual theme switch.
    /// </summary>
    public void RefreshAccentBrush() => _accentPublisher?.Refresh(this, ActualThemeVariant);
}

/// <summary>Internal helper for the AvalloyApp bootstrap.</summary>
internal static class ServiceCollectionExtensions
{
    /// <summary>Add the type as a singleton self-registration unless something
    /// is already registered against it. Mirrors the intent of
    /// <c>TryAddSingleton&lt;T&gt;()</c> but takes a runtime Type.</summary>
    public static void TryAddSingletonImplementation(this IServiceCollection services,
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] Type implementationType)
    {
        for (int i = 0; i < services.Count; i++)
        {
            if (services[i].ServiceType == implementationType) return;
        }
        services.AddSingleton(implementationType);
    }
}
