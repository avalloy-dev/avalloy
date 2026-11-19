using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Avalloy.Services;

/// <summary>
/// Forwards <see cref="Trace"/> output into <see cref="AlloyLogging"/>. UI-free
/// library assemblies conventionally report background failures and provisioning
/// detail through <c>System.Diagnostics.Trace</c> — which, in a GUI process with
/// no listener attached, goes nowhere at all. That silence is how a background
/// worker can fail and leave no record anywhere for the user to hand back. With
/// this attached, trace output lands on the console and in Avalloy's rolling
/// log file like everything else. Installed by <c>AvalloyApp</c> once
/// the logger factory exists (see <c>AvalloyApp.BridgeTraceToLogging</c>).
/// <para>Only severity-carrying output (<c>Trace.TraceError</c>/<c>TraceWarning</c>/<c>TraceInformation</c>,
/// <c>Trace.Fail</c>) reaches the default Information sinks. Plain <c>Debug.WriteLine</c>/<c>Trace.WriteLine</c>
/// is logged at Debug: libraries write anything through it, keystrokes included, and whatever is logged at
/// or above the minimum level is persisted to the log file. Lowering the <c>Trace</c> category to Debug
/// (<c>logging.AddFilter("Trace", LogLevel.Debug)</c>) writes all of it to disk — do that only knowing
/// what the app's libraries print.</para>
/// </summary>
public sealed class AlloyLoggingTraceListener(string category = "Trace") : TraceListener
{
    // Resolved lazily and only cached once the real factory is published:
    // caching whatever the first write returns would pin a NullLogger forever
    // if anything traces before AvalloyApp init completes.
    private ILogger? _logger;

    private ILogger Logger
    {
        get
        {
            if (_logger is not null) return _logger;
            var logger = AlloyLogging.CreateLogger(category);
            if (!ReferenceEquals(AlloyLogging.Factory, NullLoggerFactory.Instance))
                _logger = logger;
            return logger;
        }
    }

    /// <summary>Attach a listener to <see cref="Trace.Listeners"/>. Idempotent —
    /// if one is already attached, it's returned instead of adding a second
    /// (which would double every entry).</summary>
    public static AlloyLoggingTraceListener Install(string category = "Trace")
    {
        foreach (var listener in Trace.Listeners)
        {
            if (listener is AlloyLoggingTraceListener existing)
                return existing;
        }

        var created = new AlloyLoggingTraceListener(category);
        Trace.Listeners.Add(created);
        return created;
    }

    public override void Write(string? message) => WriteLine(message);

    // Debug.Write/WriteLine and Trace.Write/WriteLine carry no severity and are libraries' chatter channel —
    // a terminal control logs every keystroke through it. Logged at Debug, so the default Information
    // minimum keeps it out of the file and the log viewer; an app opts in with a "Trace" category filter.
    public override void WriteLine(string? message)
    {
        if (!string.IsNullOrWhiteSpace(message)) Logger.LogDebug("{Message}", message);
    }

    // Debug.Assert/Debug.Fail/Trace.Fail — the base implementation routes through WriteLine, which would
    // now bury a failed assertion at Debug.
    public override void Fail(string? message, string? detailMessage)
    {
        // Logged even with no text: Trace.Assert(false) carries none, and the failure itself is the news.
        var text = string.IsNullOrEmpty(detailMessage) ? message : $"{message} {detailMessage}";
        Logger.LogError("Fail: {Message}", string.IsNullOrWhiteSpace(text) ? "(no message)" : text);
    }

    // Trace.TraceError/TraceWarning route here — keep their severity instead of flattening everything to info.
    public override void TraceEvent(TraceEventCache? cache, string source, TraceEventType eventType, int id,
        string? message)
    {
        if (string.IsNullOrWhiteSpace(message)) return;
        Logger.Log(
            eventType switch
            {
                TraceEventType.Critical => LogLevel.Critical,
                TraceEventType.Error => LogLevel.Error,
                TraceEventType.Warning => LogLevel.Warning,
                TraceEventType.Verbose => LogLevel.Debug,
                _ => LogLevel.Information,
            },
            "{Message}", message);
    }

    public override void TraceEvent(TraceEventCache? cache, string source, TraceEventType eventType, int id,
        string? format, params object?[]? args) =>
        TraceEvent(cache, source, eventType, id, args is null || format is null ? format : string.Format(format, args));
}
