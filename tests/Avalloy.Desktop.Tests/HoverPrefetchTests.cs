using Avalloy.AttachedProperties;
using AwesomeAssertions;
using Microsoft.Extensions.Time.Testing;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Avalloy.Desktop.Tests;

/// <summary>
/// <see cref="HoverPrefetch{TKey}"/> on a fake clock: work starts only after the dwell, a leave or a new target
/// drops the old one (cancelled and discarded), a claim keeps what was built, and a failure is reported and dropped.
/// </summary>
[TestClass]
public class HoverPrefetchTests
{
    private readonly FakeTimeProvider _time = new();
    private readonly List<string> _prepared = new();
    private readonly List<string> _discarded = new();
    private readonly Dictionary<string, CancellationToken> _tokens = new();
    private readonly TaskCompletionSource _started = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private TaskCompletionSource? _gate;

    // A test that fails mid-flight must not leave its prepare parked on the gate.
    [TestCleanup]
    public void ReleaseGate() => _gate?.TrySetResult();

    private HoverPrefetch<string> Make(Action<Exception>? onError = null) => new(async (key, ct) =>
    {
        _tokens[key] = ct;
        _started.TrySetResult();
        if (_gate is { } gate) await gate.Task.WaitAsync(ct);
        _prepared.Add(key);
    }, _discarded.Add, time: _time, onError: onError);

    private void Dwell(HoverPrefetch<string> prefetch) => _time.Advance(prefetch.Dwell);

    [TestMethod]
    public async Task It_Prepares_Only_Once_The_Pointer_Has_Dwelt()
    {
        var prefetch = Make();

        prefetch.Enter("a");
        _time.Advance(prefetch.Dwell - TimeSpan.FromMilliseconds(1));
        _prepared.Should().BeEmpty();

        _time.Advance(TimeSpan.FromMilliseconds(1));
        await prefetch.Pending;
        _prepared.Should().Equal("a");
        _discarded.Should().BeEmpty();
    }

    [TestMethod]
    public async Task A_Leave_Before_The_Dwell_Discards_Without_Preparing()
    {
        var prefetch = Make();

        prefetch.Enter("a");
        prefetch.Leave("a");
        await prefetch.Pending;
        Dwell(prefetch);

        _prepared.Should().BeEmpty();
        _discarded.Should().Equal("a");
        prefetch.Target.Should().BeNull();
    }

    [TestMethod]
    public async Task Entering_Another_Target_Cancels_And_Discards_The_First()
    {
        var prefetch = Make();
        _gate = new TaskCompletionSource();

        prefetch.Enter("a");
        Dwell(prefetch);
        await _started.Task;
        var first = prefetch.Pending;
        prefetch.Enter("b");

        _tokens["a"].IsCancellationRequested.Should().BeTrue();
        _discarded.Should().Equal("a");
        await first;
        prefetch.Target.Should().Be("b");
    }

    [TestMethod]
    public async Task Entering_The_Same_Target_Again_Changes_Nothing()
    {
        var prefetch = Make();

        prefetch.Enter("a");
        var pending = prefetch.Pending;
        prefetch.Enter("a");

        prefetch.Pending.Should().BeSameAs(pending);
        Dwell(prefetch);
        await prefetch.Pending;
        _prepared.Should().Equal("a");
    }

    [TestMethod]
    public async Task A_Claim_Cancels_What_Is_Still_Running_And_A_Leave_No_Longer_Drops_It()
    {
        var prefetch = Make();
        _gate = new TaskCompletionSource();

        prefetch.Enter("a");
        Dwell(prefetch);
        await _started.Task;
        prefetch.Claim("a");

        // Before awaiting: had the claim not cancelled, the prepare would still be parked on the gate.
        _tokens["a"].IsCancellationRequested.Should().BeTrue("the click takes over; the speculation stops");
        await prefetch.Pending;
        prefetch.Leave("a");
        _discarded.Should().BeEmpty();
        prefetch.Target.Should().Be("a");
    }

    [TestMethod]
    public async Task Claiming_A_Different_Target_Drops_The_Current_One()
    {
        var prefetch = Make();

        prefetch.Enter("a");
        Dwell(prefetch);
        await prefetch.Pending;
        prefetch.Claim("b");

        _discarded.Should().Equal("a");
        prefetch.Target.Should().BeNull();
    }

    [TestMethod]
    public async Task A_Failed_Prepare_Is_Reported_And_Dropped()
    {
        var errors = new List<Exception>();
        var prefetch = new HoverPrefetch<string>((_, _) => throw new InvalidOperationException("boom"),
            _discarded.Add, time: _time, onError: errors.Add);

        prefetch.Enter("a");
        Dwell(prefetch);
        await prefetch.Pending;

        errors.Should().ContainSingle().Which.Message.Should().Be("boom");
        _discarded.Should().Equal("a");
        prefetch.Target.Should().BeNull();
    }
}
