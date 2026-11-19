using AwesomeAssertions;
using Avalloy.Services;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Avalloy.Services.Tests;

[TestClass]
public class LogBufferTests
{
    private static LogEntry Entry(string message, LogLevel level = LogLevel.Information) =>
        new(DateTimeOffset.Now, level, "test", message, null, default, null);

    [TestMethod]
    public void Add_stamps_monotonic_sequences_and_raises_flushed()
    {
        var buffer = new LogBuffer(capacity: 8, uiPost: a => a());
        var flushed = new List<LogEntry>();
        buffer.Flushed += batch => flushed.AddRange(batch);

        buffer.Add(Entry("one"));
        buffer.Add(Entry("two"));

        flushed.Select(e => e.Message).Should().Equal("one", "two");
        flushed.Select(e => e.Sequence).Should().Equal(1L, 2L);
        buffer.TotalAdded.Should().Be(2);
    }

    [TestMethod]
    public void Ring_wraps_at_capacity_keeping_newest()
    {
        var buffer = new LogBuffer(capacity: 4, uiPost: a => a());
        for (var i = 1; i <= 10; i++)
            buffer.Add(Entry($"m{i}"));

        buffer.Count.Should().Be(4);
        buffer.TotalAdded.Should().Be(10);
        var snapshot = buffer.Snapshot();
        snapshot.Select(e => e.Message).Should().Equal("m7", "m8", "m9", "m10"); // oldest-first
        snapshot.Select(e => e.Sequence).Should().Equal(7L, 8L, 9L, 10L);
    }

    [TestMethod]
    public void Flush_is_coalesced_one_post_per_burst()
    {
        var posts = new List<Action>();
        var buffer = new LogBuffer(capacity: 8, uiPost: posts.Add);
        var batches = new List<IReadOnlyList<LogEntry>>();
        buffer.Flushed += batches.Add;

        buffer.Add(Entry("a"));
        buffer.Add(Entry("b"));
        buffer.Add(Entry("c"));

        posts.Should().HaveCount(1); // burst coalesced into a single scheduled flush
        posts[0]();
        batches.Should().HaveCount(1);
        batches[0].Select(e => e.Message).Should().Equal("a", "b", "c");

        // The next add after a flush schedules again.
        buffer.Add(Entry("d"));
        posts.Should().HaveCount(2);
    }

    [TestMethod]
    public void Clear_empties_but_sequences_stay_monotonic()
    {
        var buffer = new LogBuffer(capacity: 4, uiPost: a => a());
        buffer.Add(Entry("before"));
        buffer.Clear();

        buffer.Count.Should().Be(0);
        buffer.Snapshot().Should().BeEmpty();

        buffer.Add(Entry("after"));
        buffer.Snapshot().Single().Sequence.Should().Be(2);
    }

    [TestMethod]
    public void Concurrent_adds_lose_nothing_and_never_collide()
    {
        const int threads = 8, perThread = 500;
        var buffer = new LogBuffer(capacity: 1000, uiPost: _ => { }); // never flush — pending stays bounded
        Parallel.For(0, threads, t =>
        {
            for (var i = 0; i < perThread; i++)
                buffer.Add(Entry($"t{t}-{i}"));
        });

        buffer.TotalAdded.Should().Be(threads * perThread);
        var snapshot = buffer.Snapshot();
        snapshot.Should().HaveCount(1000);
        snapshot.Select(e => e.Sequence).Should().OnlyHaveUniqueItems();
        snapshot.Select(e => e.Sequence).Should().BeInAscendingOrder();
    }
}
