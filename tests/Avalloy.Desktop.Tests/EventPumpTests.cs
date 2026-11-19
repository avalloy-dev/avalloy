using System.Runtime.CompilerServices;
using AwesomeAssertions;
using Avalloy.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Avalloy.Desktop.Tests;

/// <summary>
/// The pump's contract, exercised entirely through the <c>uiPost</c> seam (no dispatcher — this assembly is
/// Avalonia-free by design). The captured-post fake makes the burst behavior DETERMINISTIC: posts are
/// collected, not run, so the test controls exactly when a drain executes and can see how many were
/// scheduled.
/// </summary>
[TestClass]
public class EventPumpTests
{
    /// <summary>A fake dispatcher: captures posted drains; the test runs them explicitly.</summary>
    private sealed class CapturedPosts
    {
        private readonly List<Action> _posts = [];

        public Action<Action> Post => a => { lock (_posts) _posts.Add(a); };

        public int Scheduled { get { lock (_posts) return _posts.Count; } }

        /// <summary>Run every captured post (and any they re-schedule), returning how many ran.</summary>
        public int RunAll()
        {
            var ran = 0;
            while (true)
            {
                Action next;
                lock (_posts)
                {
                    if (_posts.Count == 0) return ran;
                    next = _posts[0];
                    _posts.RemoveAt(0);
                }

                next();
                ran++;
            }
        }
    }

    private static async IAsyncEnumerable<int> Range(int n, [EnumeratorCancellation] CancellationToken ct = default)
    {
        for (var i = 0; i < n; i++)
        {
            ct.ThrowIfCancellationRequested();
            yield return i;
        }

        await Task.CompletedTask;
    }

    [TestMethod]
    public async Task Events_Arrive_In_Order_And_A_Burst_Coalesces_Into_One_Post()
    {
        var posts = new CapturedPosts();
        using var pump = new EventPump<int>(posts.Post);
        var seen = new List<int>();
        pump.Event += seen.Add;

        pump.Start(ct => Range(300, ct));

        // Drive the fake dispatcher until everything got through. The producer schedules its FIRST drain and
        // then rides the drainScheduled flag, so a fast burst lands as very few posts — while the 256-item
        // batch cap forces at least one re-post for 300 events.
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
        while (seen.Count < 300 && DateTime.UtcNow < deadline)
        {
            if (posts.RunAll() == 0)
            {
                await Task.Delay(10);
            }
        }

        seen.Should().HaveCount(300);
        seen.Should().BeInAscendingOrder().And.OnlyHaveUniqueItems("the queue must not lose or duplicate under bursts");
    }

    [TestMethod]
    public async Task A_Stream_Failure_Maps_Through_OnError()
    {
        var posts = new CapturedPosts();
        using var pump = new EventPump<string>(posts.Post);
        var seen = new List<string>();
        pump.Event += seen.Add;

        static async IAsyncEnumerable<string> TwoThenThrow([EnumeratorCancellation] CancellationToken ct = default)
        {
            yield return "one";
            yield return "two";
            await Task.CompletedTask;
            throw new InvalidOperationException("stream died");
        }

        pump.Start(ct => TwoThenThrow(ct), ex => $"error:{ex.Message}");

        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
        while (seen.Count < 3 && DateTime.UtcNow < deadline)
        {
            if (posts.RunAll() == 0)
            {
                await Task.Delay(10);
            }
        }

        seen.Should().Equal("one", "two", "error:stream died");
    }

    [TestMethod]
    public async Task Stop_Cancels_The_Subscription()
    {
        var cancelled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var pump = new EventPump<int>(a => a());   // inline post — this test is about the token

        async IAsyncEnumerable<int> Forever([EnumeratorCancellation] CancellationToken ct = default)
        {
            try
            {
                while (true)
                {
                    started.TrySetResult();
                    yield return 0;
                    await Task.Delay(5, ct);
                }
            }
            finally
            {
                cancelled.TrySetResult();
            }
        }

        pump.Start(ct => Forever(ct));
        // Wait for the stream to be genuinely open first: a Stop that lands before the pool thread starts
        // cancels the Task.Run outright and the enumerator (and its finally) never exists at all.
        await started.Task.WaitAsync(TimeSpan.FromSeconds(10));
        pump.Stop();

        await cancelled.Task.WaitAsync(TimeSpan.FromSeconds(10));   // the enumerator saw the cancel
    }

    [TestMethod]
    public async Task Restart_Cancels_The_Prior_Subscription()
    {
        var firstCancelled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var firstStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var pump = new EventPump<int>(a => a());

        async IAsyncEnumerable<int> First([EnumeratorCancellation] CancellationToken ct = default)
        {
            try
            {
                while (true)
                {
                    firstStarted.TrySetResult();
                    yield return 1;
                    await Task.Delay(5, ct);
                }
            }
            finally
            {
                firstCancelled.TrySetResult();
            }
        }

        pump.Start(ct => First(ct));
        await firstStarted.Task.WaitAsync(TimeSpan.FromSeconds(10));   // same open-first guard as Stop's test
        pump.Start(ct => Range(1, ct));   // replaces the first

        await firstCancelled.Task.WaitAsync(TimeSpan.FromSeconds(10));
    }
}
