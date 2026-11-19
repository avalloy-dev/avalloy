using System.Text;
using Microsoft.Extensions.Logging;

namespace Avalloy.Services;

/// <summary>
/// Records named app events (feature used, milestone reached) with optional
/// properties. The Avalloy implementation writes them through ordinary
/// logging — no analytics backend — so events land in every configured sink
/// and a reporter that models breadcrumbs and keeps Information-level entries
/// (Sentry does both) will attach them to any later error — the reporter's
/// behaviour, not a promise of this interface.
/// Non-DI call sites can use <see cref="AlloyLogging.Event"/> instead.
/// </summary>
public interface IEventRecorder
{
    void Record(string name, IReadOnlyDictionary<string, object?>? properties = null);
}

/// <summary>Default <see cref="IEventRecorder"/>: one Information entry per event
/// under the <c>Avalloy.Events</c> category. Registered as a built-in by
/// <c>AvalloyApp</c>.</summary>
public sealed class LoggingEventRecorder(ILoggerFactory factory) : IEventRecorder
{
    private readonly ILogger _logger = factory.CreateLogger(EventLogging.Category);

    public void Record(string name, IReadOnlyDictionary<string, object?>? properties = null) =>
        EventLogging.Record(_logger, name, properties);
}

/// <summary>Shared formatting so <see cref="AlloyLogging.Event"/> and
/// <see cref="LoggingEventRecorder"/> emit identical entries.</summary>
internal static class EventLogging
{
    public const string Category = "Avalloy.Events";

    public static void Record(ILogger logger, string name, IEnumerable<(string Key, object? Value)>? properties) =>
        Record(logger, name, properties?.Select(p => new KeyValuePair<string, object?>(p.Key, p.Value)));

    public static void Record(ILogger logger, string name, IEnumerable<KeyValuePair<string, object?>>? properties)
    {
        if (Flatten(properties) is { } props)
            logger.LogInformation("event {EventName} · {EventProperties}", name, props);
        else
            logger.LogInformation("event {EventName}", name);
    }

    private static string? Flatten(IEnumerable<KeyValuePair<string, object?>>? properties)
    {
        if (properties is null) return null;
        StringBuilder? sb = null;
        foreach (var (key, value) in properties)
        {
            if (sb is null) sb = new StringBuilder();
            else sb.Append(' ');
            sb.Append(key).Append('=').Append(value);
        }
        return sb?.ToString();
    }
}
