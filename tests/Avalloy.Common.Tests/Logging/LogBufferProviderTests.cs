using AwesomeAssertions;
using Avalloy.Services;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Avalloy.Services.Tests;

[TestClass]
public class LogBufferProviderTests
{
    private static (LoggerFactory Factory, LogBuffer Buffer) Create()
    {
        var buffer = new LogBuffer(capacity: 100, uiPost: a => a());
        var factory = new LoggerFactory();
        factory.AddProvider(new LogBufferProvider(buffer));
        return (factory, buffer);
    }

    [TestMethod]
    public void Captures_level_category_message_and_event_id()
    {
        var (factory, buffer) = Create();
        using (factory)
        {
            var logger = factory.CreateLogger("My.Category");
            logger.Log(LogLevel.Warning, new EventId(7, "seven"), "value is {Value}", 42);
        }

        var entry = buffer.Snapshot().Single();
        entry.Level.Should().Be(LogLevel.Warning);
        entry.Category.Should().Be("My.Category");
        entry.Message.Should().Be("value is 42");
        entry.EventId.Id.Should().Be(7);
        entry.ExceptionText.Should().BeNull();
        entry.Scopes.Should().BeNull();
    }

    [TestMethod]
    public void Flattens_exceptions_to_text_at_capture()
    {
        var (factory, buffer) = Create();
        using (factory)
        {
            var logger = factory.CreateLogger("cat");
            try
            {
                throw new InvalidOperationException("boom");
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "failed");
            }
        }

        var entry = buffer.Snapshot().Single();
        entry.Message.Should().Be("failed");
        entry.ExceptionText.Should().Contain("InvalidOperationException").And.Contain("boom");
    }

    [TestMethod]
    public void Flattens_nested_scopes_outer_to_inner()
    {
        var (factory, buffer) = Create();
        using (factory)
        {
            var logger = factory.CreateLogger("cat");
            using (logger.BeginScope("outer"))
            using (logger.BeginScope("inner"))
            {
                logger.LogInformation("scoped");
            }

            logger.LogInformation("unscoped");
        }

        var entries = buffer.Snapshot();
        entries[0].Scopes.Should().Be("outer => inner");
        entries[1].Scopes.Should().BeNull();
    }
}

[TestClass]
public class EventRecorderTests
{
    [TestMethod]
    public void Events_land_as_information_under_the_events_category()
    {
        var buffer = new LogBuffer(capacity: 10, uiPost: a => a());
        using var factory = new LoggerFactory();
        factory.AddProvider(new LogBufferProvider(buffer));

        IEventRecorder recorder = new LoggingEventRecorder(factory);
        recorder.Record("thing-happened", new Dictionary<string, object?> { ["count"] = 3, ["kind"] = "demo" });
        recorder.Record("bare-event");

        var entries = buffer.Snapshot();
        entries[0].Level.Should().Be(LogLevel.Information);
        entries[0].Category.Should().Be("Avalloy.Events");
        entries[0].Message.Should().Be("event thing-happened · count=3 kind=demo");
        entries[1].Message.Should().Be("event bare-event");
    }
}
