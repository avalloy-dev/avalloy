using AwesomeAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Avalloy.Services.Tests;

/// <summary>
/// <see cref="LogBuffer.DefaultPost"/> is how a UI host keeps every buffer's batches on its UI thread now that
/// LogBuffer has no UI of its own. AvalloyApp installs the Avalonia dispatcher there; an app that creates its
/// own buffer for a viewer relies on that reaching it.
/// </summary>
[TestClass]
[DoNotParallelize]   // DefaultPost is process-wide
public class LogBufferDefaultPostTests
{
    private Action<Action> _original = null!;

    [TestInitialize]
    public void Setup() => _original = LogBuffer.DefaultPost;

    [TestCleanup]
    public void Teardown() => LogBuffer.DefaultPost = _original;

    private static LogEntry Entry(string message) =>
        new(DateTimeOffset.Now, LogLevel.Information, "Test", message, null, default, null);

    [TestMethod]
    public void Without_A_UI_Host_A_Batch_Is_Raised_Inline()
    {
        var buffer = new LogBuffer(capacity: 8);
        IReadOnlyList<LogEntry>? flushed = null;
        buffer.Flushed += batch => flushed = batch;

        buffer.Add(Entry("hello"));

        flushed.Should().ContainSingle().Which.Message.Should().Be("hello",
            "a headless host has no UI thread to post to, so the batch arrives on the writer");
    }

    [TestMethod]
    public void A_Buffer_Created_After_The_Host_Installs_A_Post_Uses_It()
    {
        var posted = new List<Action>();
        LogBuffer.DefaultPost = posted.Add;

        var buffer = new LogBuffer(capacity: 8);
        var flushed = 0;
        buffer.Flushed += _ => flushed++;
        buffer.Add(Entry("hello"));

        posted.Should().ContainSingle("the flush goes through the host's post, not inline");
        flushed.Should().Be(0, "nothing is raised until the host runs what it was handed");
        posted[0]();
        flushed.Should().Be(1);
    }

    [TestMethod]
    public void An_Explicit_UiPost_Wins_Over_The_Default()
    {
        var viaDefault = 0;
        LogBuffer.DefaultPost = _ => viaDefault++;

        var buffer = new LogBuffer(capacity: 8, uiPost: a => a());
        buffer.Add(Entry("hello"));

        viaDefault.Should().Be(0);
    }
}
