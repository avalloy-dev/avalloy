using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Avalloy.Services;

/// <summary>
/// Static accessor for Avalloy's <see cref="ILoggerFactory"/>. Populated
/// once at startup through <see cref="Publish"/> — by <c>AvalloyApp</c> in a UI app, by the
/// host itself anywhere else. Use for diagnostic logs
/// from code that isn't DI-resolved (XAML-loaded views, attached property
/// handlers, static helpers). Code that resolves through DI should inject
/// <c>ILogger&lt;T&gt;</c> directly instead.
/// </summary>
/// <remarks>
/// Falls back to <see cref="NullLogger"/> before init completes and in tests,
/// so callers never need to null-check. Once <c>AvalloyApp.OnFrameworkInitializationCompleted</c>
/// finishes, calls flow through the configured providers (console + debug by
/// default, plus DevTools in Debug builds, plus any provider a subclass adds
/// via <c>ConfigureLogging</c> — Sentry being the obvious one).
/// </remarks>
public static class AlloyLogging
{
    public static ILoggerFactory Factory { get; internal set; } = NullLoggerFactory.Instance;

    /// <summary>Path of the rolling log file Avalloy wires up in
    /// <c>AvalloyApp.ConfigureLogging</c>, or null if the file sink couldn't be
    /// opened. Surface it in a diagnostics screen / "reveal log" button — after a
    /// crash it's the only place the exception was written, since a Finder-launched
    /// app has no console to receive the console provider's output.</summary>
    /// <remarks>Published together with <see cref="Factory"/> and after it (<see cref="Publish"/>), so a caller that
    /// waits for the path to appear can log at once: the line reaches the file.</remarks>
    public static string? LogFilePath { get; internal set; }

    /// <summary>
    /// Make logging available to non-DI callers: the factory first, then the log file's path. The file sink exists
    /// from the moment it is registered, but until the service container is built every
    /// <see cref="CreateLogger(string)"/> returns a NullLogger. A path published then said "the log is ready" while
    /// a line logged on that cue was dropped. That happened to an app's UI hang watchdog, whose "armed" line vanished
    /// once the app's service registration grew past the watchdog's poll.
    /// <para>Call once, when the factory is live. <c>AvalloyApp</c> does; a headless host does it itself.</para>
    /// </summary>
    public static void Publish(ILoggerFactory factory, string? logFilePath)
    {
        Factory = factory;
        AfterFactoryPublished?.Invoke();
        LogFilePath = logFilePath;
    }

    /// <summary>Called inside <see cref="Publish"/> between the factory and the path. Tests only: how a test sees the
    /// order the two are published in.</summary>
    internal static Action? AfterFactoryPublished { get; set; }

    /// <summary>Ring of recent entries retained for in-app surfaces (the
    /// <c>LogViewer</c> control). Replaced by <c>AvalloyApp</c> during
    /// logging setup; the default instance means pre-init and test callers can
    /// always bind/subscribe without null checks (it just stays empty until a
    /// <see cref="LogBufferProvider"/> feeds it). Set it before adding the
    /// <see cref="LogBufferProvider"/> that feeds it.</summary>
    public static LogBuffer Buffer { get; set; } = new();

    public static ILogger<T> CreateLogger<T>() => Factory.CreateLogger<T>();

    public static ILogger CreateLogger(string category) => Factory.CreateLogger(category);

    /// <summary>Record a named app event (feature used, milestone reached) through
    /// ordinary logging: one Information entry under the <c>Avalloy.Events</c>
    /// category, which lands in every configured sink. A reporter that models
    /// breadcrumbs and keeps Information-level entries (Sentry does both) will
    /// attach it to any later error; that is the reporter's behaviour, not a
    /// guarantee of this call. Static convenience for
    /// non-DI call sites; DI code should inject <see cref="IEventRecorder"/>.</summary>
    public static void Event(string name, params (string Key, object? Value)[] properties) =>
        EventLogging.Record(CreateLogger(EventLogging.Category), name, properties);

    /// <summary>Map a JS-console level string ("log"/"info"/"warn"/"error"
    /// /"debug"/"trace") to the matching <see cref="LogLevel"/>. Unknown
    /// values default to <see cref="LogLevel.Information"/>. Used by WebView
    /// hosts to bridge browser console.log() calls into ILogger.</summary>
    public static LogLevel MapJsConsoleLevel(string? level) => level switch
    {
        "trace" => LogLevel.Trace,
        "debug" => LogLevel.Debug,
        "log" or "info" => LogLevel.Information,
        "warn" or "warning" => LogLevel.Warning,
        "error" => LogLevel.Error,
        _ => LogLevel.Information,
    };
}
