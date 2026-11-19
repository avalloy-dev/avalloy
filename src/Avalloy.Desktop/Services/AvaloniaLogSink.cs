using System.Collections.Concurrent;
using Avalonia.Logging;
using Microsoft.Extensions.Logging;

namespace Avalloy.Services;

/// <summary>Routes Avalonia's internal log (binding errors, theme/property failures, layout
/// complaints) into <see cref="AlloyLogging"/> so they reach the console and log file instead of
/// vanishing into <c>System.Diagnostics.Trace</c>. These are exactly the errors Avalonia swallows
/// silently — a broken binding or an illegal ControlTheme selector otherwise just renders nothing.
/// Installed by <see cref="AvalloyApp"/> (see <c>AvalloyApp.AvaloniaLogBridgeLevel</c>);
/// hosting apps should NOT also call <c>.LogToTrace()</c> on the AppBuilder, which would replace
/// this sink.</summary>
public sealed class AvaloniaLogSink(LogEventLevel minLevel) : ILogSink
{
    private readonly ConcurrentDictionary<string, ILogger> _loggers = new();

    /// <summary>Set this sink as Avalonia's logger. Last install wins (Avalonia
    /// holds a single sink).</summary>
    public static void Install(LogEventLevel minLevel) =>
        Avalonia.Logging.Logger.Sink = new AvaloniaLogSink(minLevel);

    public bool IsEnabled(LogEventLevel level, string area) => level >= minLevel;

    public void Log(LogEventLevel level, string area, object? source, string messageTemplate) =>
        Log(level, area, source, messageTemplate, []);

    public void Log(LogEventLevel level, string area, object? source, string messageTemplate, params object?[] propertyValues)
    {
        if (level < minLevel) return;
        var logger = _loggers.GetOrAdd(area, a => AlloyLogging.CreateLogger("Avalonia." + a));
        var template = source is null ? messageTemplate : $"{messageTemplate} (source: {source.GetType().Name})";
        logger.Log(Map(level), template, propertyValues);
    }

    private static LogLevel Map(LogEventLevel level) => level switch
    {
        LogEventLevel.Verbose => LogLevel.Trace,
        LogEventLevel.Debug => LogLevel.Debug,
        LogEventLevel.Information => LogLevel.Information,
        LogEventLevel.Warning => LogLevel.Warning,
        LogEventLevel.Error => LogLevel.Error,
        _ => LogLevel.Critical,
    };
}
