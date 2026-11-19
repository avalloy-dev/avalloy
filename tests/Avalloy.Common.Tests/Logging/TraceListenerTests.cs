using System.Diagnostics;
using AwesomeAssertions;
using Avalloy.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Avalloy.Services.Tests;

[TestClass]
[DoNotParallelize]   // Trace.Listeners and AlloyLogging are process-wide
public class TraceListenerTests
{
    private ILoggerFactory _originalFactory = null!;

    [TestInitialize]
    public void Setup() => _originalFactory = AlloyLogging.Factory;

    [TestCleanup]
    public void Cleanup()
    {
        AlloyLogging.Factory = _originalFactory;
        for (var i = Trace.Listeners.Count - 1; i >= 0; i--)
        {
            if (Trace.Listeners[i] is AlloyLoggingTraceListener)
                Trace.Listeners.RemoveAt(i);
        }
    }

    private static (LoggerFactory Factory, LogBuffer Buffer) CreateCapture()
    {
        var buffer = new LogBuffer(capacity: 100, uiPost: a => a());
        var factory = new LoggerFactory();
        factory.AddProvider(new LogBufferProvider(buffer));
        return (factory, buffer);
    }

    [TestMethod]
    public void Trace_events_keep_their_severity_and_land_under_the_trace_category()
    {
        var (factory, buffer) = CreateCapture();
        using (factory)
        {
            AlloyLogging.Factory = factory;
            var listener = new AlloyLoggingTraceListener();
            listener.TraceEvent(null, "src", TraceEventType.Warning, 0, "careful");
            listener.TraceEvent(null, "src", TraceEventType.Error, 0, "broken");
            listener.TraceEvent(null, "src", TraceEventType.Information, 0, "fyi");
        }

        var entries = buffer.Snapshot();
        entries.Select(e => e.Level).Should().Equal(LogLevel.Warning, LogLevel.Error, LogLevel.Information);
        entries.Should().OnlyContain(e => e.Category == "Trace");
        entries[0].Message.Should().Be("careful");
    }

    [TestMethod]
    public void Plain_writes_stay_below_the_default_minimum_level()
    {
        // Debug.WriteLine/Trace.WriteLine is libraries' chatter channel — a terminal control logs every
        // keystroke through it, so at Information a typed password reached the log file a character at a
        // time. Under AvalloyApp's default minimum (Information) these writes must not be recorded at all,
        // while the severity-carrying Trace.TraceWarning still is.
        var buffer = new LogBuffer(capacity: 100, uiPost: a => a());
        using (var factory = LoggerFactory.Create(b => b
                   .SetMinimumLevel(LogLevel.Information)
                   .AddProvider(new LogBufferProvider(buffer))))
        {
            AlloyLogging.Factory = factory;
            AlloyLoggingTraceListener.Install();
            Trace.WriteLine("[TerminalView] OnTextInput: Sending 'S' to PTY");
            Trace.Write("partial");
            Trace.TraceWarning("careful");
        }

        buffer.Snapshot().Select(e => e.Message).Should().Equal("careful");
    }

    [TestMethod]
    public void Plain_writes_are_logged_at_debug_and_failures_at_error()
    {
        var (factory, buffer) = CreateCapture();
        using (factory)
        {
            AlloyLogging.Factory = factory;
            var listener = new AlloyLoggingTraceListener();
            listener.WriteLine("plain line");
            listener.Fail("assertion failed", "detail");
            listener.Fail(null, null);   // Trace.Assert(false) carries no text — still a failure worth an entry
        }

        var entries = buffer.Snapshot();
        entries.Select(e => e.Level).Should().Equal(LogLevel.Debug, LogLevel.Error, LogLevel.Error);
        entries[1].Message.Should().Contain("assertion failed").And.Contain("detail");
    }

#if DEBUG   // Debug.WriteLine is [Conditional("DEBUG")] — compiled out of a Release test build
    [TestMethod]
    public void Debug_WriteLine_reaches_the_installed_listener()
    {
        // Debug and Trace share Trace.Listeners on .NET (Core): once Trace is initialised it installs a
        // provider that routes Debug.Write/WriteLine through the listener collection — the path a
        // library's keystroke logging took into the file.
        var (factory, buffer) = CreateCapture();
        using (factory)
        {
            AlloyLogging.Factory = factory;
            AlloyLoggingTraceListener.Install();
            Debug.WriteLine("from Debug.WriteLine");
        }

        buffer.Snapshot().Should().ContainSingle(e => e.Message == "from Debug.WriteLine")
            .Which.Level.Should().Be(LogLevel.Debug);
    }
#endif

    [TestMethod]
    public void Logger_is_not_cached_while_the_factory_is_still_null()
    {
        // The regression this guards: caching the logger resolved BEFORE
        // AvalloyApp publishes the factory would pin a NullLogger forever,
        // silently discarding all trace output for the life of the process.
        var listener = new AlloyLoggingTraceListener();

        AlloyLogging.Factory = NullLoggerFactory.Instance;
        listener.WriteLine("emitted before init"); // resolves a NullLogger — must not stick

        var (factory, buffer) = CreateCapture();
        using (factory)
        {
            AlloyLogging.Factory = factory;
            listener.WriteLine("emitted after init");
        }

        buffer.Snapshot().Single().Message.Should().Be("emitted after init");
    }

    [TestMethod]
    public void Install_is_idempotent()
    {
        var first = AlloyLoggingTraceListener.Install();
        var second = AlloyLoggingTraceListener.Install();

        second.Should().BeSameAs(first);
        Trace.Listeners.OfType<AlloyLoggingTraceListener>().Should().HaveCount(1);
    }
}
