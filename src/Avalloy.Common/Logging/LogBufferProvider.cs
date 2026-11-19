using System.Text;
using Microsoft.Extensions.Logging;

namespace Avalloy.Services;

/// <summary>
/// <see cref="ILoggerProvider"/> that captures every entry into a <see cref="LogBuffer"/>
/// so in-app surfaces (the <c>LogViewer</c> control) can show live and recent logging.
/// Registered first in <c>AvalloyApp.ConfigureLogging</c>, before any window exists,
/// so startup logging is retained too. Level filtering is left to the shared MEL
/// configuration, same as <see cref="FileLoggerProvider"/>.
/// </summary>
public sealed class LogBufferProvider(LogBuffer buffer) : ILoggerProvider, ISupportExternalScope
{
    private readonly LogBuffer _buffer = buffer;
    private IExternalScopeProvider _scopes = new LoggerExternalScopeProvider();

    /// <summary>Called by <see cref="LoggerFactory"/> so scopes flow across every
    /// provider that opts in via <see cref="ISupportExternalScope"/>.</summary>
    public void SetScopeProvider(IExternalScopeProvider scopeProvider) => _scopes = scopeProvider;

    public ILogger CreateLogger(string categoryName) => new BufferLogger(this, categoryName);

    public void Dispose()
    {
    }

    private sealed class BufferLogger(LogBufferProvider provider, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull =>
            provider._scopes.Push(state);

        public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel))
                return;

            provider._buffer.Add(new LogEntry(
                DateTimeOffset.Now,
                logLevel,
                category,
                formatter(state, exception),
                exception?.ToString(),
                eventId,
                LogScopeFormatter.Flatten(provider._scopes)));
        }
    }
}

/// <summary>Flattens the active scope chain to display text, shared by the buffer
/// and file providers so both render scopes identically.</summary>
internal static class LogScopeFormatter
{
    /// <summary>Outer-to-inner scopes joined with <c>" => "</c>, or null when no
    /// scope is active (the overwhelmingly common case — callers skip rendering).</summary>
    public static string? Flatten(IExternalScopeProvider scopes)
    {
        StringBuilder? sb = null;
        scopes.ForEachScope<object?>((scope, _) =>
        {
            if (sb is null) sb = new StringBuilder();
            else sb.Append(" => ");
            sb.Append(scope);
        }, null);
        return sb?.ToString();
    }
}
