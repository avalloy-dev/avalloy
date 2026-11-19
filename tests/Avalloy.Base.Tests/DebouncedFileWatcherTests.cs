using AwesomeAssertions;
using Avalloy.Preferences;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using FileState = Avalloy.Preferences.DebouncedFileWatcher.FileState;

namespace Avalloy.Base.Tests;

/// <summary>
/// The preferences file watcher's decision of when a change is somebody else's.
/// </summary>
/// <remarks>
/// <para>It used to answer by time: a save held suppression for 500ms after it finished, and an event
/// that turned up later than that was taken for an external edit. On macOS FSEvents can be later than
/// that, so a save of our own occasionally reloaded the whole preferences model behind everything holding
/// its sections. It now answers by state: the file as it stood when our write finished is ours, however
/// late the event saying so arrives.</para>
///
/// <para>Everything here runs on a manual clock and a file state the test sets, with no
/// <see cref="FileSystemWatcher"/>: the FSEvents latency that made this flaky is exactly what a test
/// must not wait on. The events are raised by hand, in the late orders FSEvents can deliver them.</para>
/// </remarks>
[TestClass]
public sealed class DebouncedFileWatcherTests
{
    private const int DebounceMs = 250;
    private static readonly FileState Missing = new(false, 0, 0);

    [TestMethod]
    public void A_Save_Inside_Suppression_Schedules_Nothing_Even_When_Its_Event_Arrives_Late()
    {
        var rig = new Rig(new FileState(true, 100, 10));

        using (rig.Watcher.SuppressEvents())
        {
            rig.State = new FileState(true, 200, 12);
            rig.Watcher.NotifyFileEvent();
        }

        rig.Clock.Advance(5_000);
        rig.Watcher.NotifyFileEvent();
        rig.Watcher.Poll();
        rig.Clock.Pending(DebounceMs).Should().Be(0,
            "a late event for our own write schedules nothing, rather than relying on the callback to notice");
        rig.Clock.Advance(5_000);

        rig.Callbacks.Should().Be(0, "the file still holds our own write, five seconds or fifty after it");
    }

    [TestMethod]
    public void An_External_Edit_Schedules_Exactly_One_Callback()
    {
        var rig = new Rig(new FileState(true, 100, 10));

        rig.State = new FileState(true, 300, 14);
        rig.Watcher.NotifyFileEvent();
        rig.Watcher.NotifyFileEvent();
        rig.Watcher.Poll();
        rig.Watcher.NotifyFileEvent();
        rig.Clock.Pending(DebounceMs).Should().Be(1);
        rig.Clock.Advance(DebounceMs);

        rig.Callbacks.Should().Be(1, "an atomic write's burst of events is one change");
    }

    [TestMethod]
    public void An_External_Edit_After_Our_Own_Save_Is_Still_Seen()
    {
        var rig = new Rig(new FileState(true, 100, 10));
        using (rig.Watcher.SuppressEvents())
            rig.State = new FileState(true, 200, 12);

        rig.State = new FileState(true, 300, 12);
        rig.Watcher.NotifyFileEvent();
        rig.Clock.Advance(DebounceMs);

        rig.Callbacks.Should().Be(1, "only the state we wrote is ours; any other is an edit");
    }

    [TestMethod]
    public void The_First_Write_Of_A_Missing_File_Is_Our_Own()
    {
        var rig = new Rig(Missing);

        using (rig.Watcher.SuppressEvents())
            rig.State = new FileState(true, 100, 10);
        rig.Watcher.NotifyFileEvent();
        rig.Clock.Advance(DebounceMs);

        rig.Callbacks.Should().Be(0, "creating the file with defaults is a save like any other");
    }

    [TestMethod]
    public void Starting_A_Save_Cancels_A_Callback_Already_Pending()
    {
        var rig = new Rig(new FileState(true, 100, 10));
        rig.State = new FileState(true, 150, 11);
        rig.Watcher.NotifyFileEvent();

        rig.Clock.Advance(DebounceMs / 2);
        using (rig.Watcher.SuppressEvents())
        {
            rig.Clock.Pending(DebounceMs).Should().Be(0, "starting the save cancels it outright");
            rig.State = new FileState(true, 200, 12);
        }
        rig.Clock.Advance(5_000);

        rig.Callbacks.Should().Be(0,
            "the file now holds our save, which already includes whatever was pending; reloading would swap it out");
    }

    [TestMethod]
    public void A_Superseded_Callback_Already_Queued_Does_Not_Run()
    {
        // A System.Threading.Timer that has fired but whose callback has not yet been picked up by the
        // thread pool is not stopped by Dispose. Run the cancelled callback anyway to stand in for that:
        // the file really has changed, so only the generation check stops it reporting the change twice.
        var rig = new Rig(new FileState(true, 100, 10));
        rig.State = new FileState(true, 150, 11);
        rig.Watcher.NotifyFileEvent();
        rig.State = new FileState(true, 160, 12);
        rig.Watcher.NotifyFileEvent();

        rig.Clock.RunCancelled();
        rig.Clock.Advance(DebounceMs);

        rig.Callbacks.Should().Be(1, "one edit, debounced, is one reload");
    }

    [TestMethod]
    public void A_Callback_Rechecks_The_File_When_It_Fires()
    {
        // An external edit's event schedules a callback; before it fires the file is put back to the
        // state we last wrote (our own save racing the editor). Firing must not report a change that
        // is no longer there.
        var rig = new Rig(new FileState(true, 100, 10));
        using (rig.Watcher.SuppressEvents())
            rig.State = new FileState(true, 200, 12);
        var ours = rig.State;

        rig.State = new FileState(true, 300, 14);
        rig.Watcher.NotifyFileEvent();
        rig.State = ours;
        rig.Clock.Advance(DebounceMs);

        rig.Callbacks.Should().Be(0);
    }

    [TestMethod]
    public void Dispose_Cancels_A_Pending_Callback()
    {
        var rig = new Rig(new FileState(true, 100, 10));
        rig.State = new FileState(true, 300, 14);
        rig.Watcher.NotifyFileEvent();

        rig.Watcher.Dispose();
        rig.Clock.Advance(DebounceMs);
        rig.Clock.RunCancelled();
        rig.Watcher.NotifyFileEvent();
        rig.Watcher.Poll();

        rig.Callbacks.Should().Be(0, "nothing may call back into a service that has been disposed");
    }

    [TestMethod]
    public void A_Suppression_Tail_Still_Holds_When_Asked_For()
    {
        var rig = new Rig(new FileState(true, 100, 10), suppressionTailMs: 500);

        var scope = rig.Watcher.SuppressEvents();
        rig.State = new FileState(true, 200, 12);
        scope.Dispose();

        rig.State = new FileState(true, 300, 14);
        rig.Watcher.NotifyFileEvent();
        rig.Clock.Advance(DebounceMs);
        rig.Callbacks.Should().Be(0, "inside the tail, events are still suppressed");

        rig.Clock.Advance(500);
        rig.Watcher.NotifyFileEvent();
        rig.Clock.Advance(DebounceMs);
        rig.Callbacks.Should().Be(1, "after it, an edit is an edit");
    }

    private sealed class Rig
    {
        public Rig(FileState initial, int suppressionTailMs = 0)
        {
            State = initial;
            Watcher = new DebouncedFileWatcher("/prefs/preferences.json", () => Callbacks++, DebounceMs,
                suppressionTailMs, () => State, Clock, watchFileSystem: false);
        }

        public FileState State { get; set; }
        public ManualScheduler Clock { get; } = new();
        public DebouncedFileWatcher Watcher { get; }
        public int Callbacks { get; private set; }
    }
}

/// <summary>A clock that only moves when told to, firing what falls due in order.</summary>
internal sealed class ManualScheduler : IWatcherScheduler
{
    private readonly List<Entry> _entries = new();
    private long _now;

    public IDisposable Schedule(int dueMs, Action callback)
    {
        var entry = new Entry(_now + dueMs, dueMs, callback);
        _entries.Add(entry);
        return entry;
    }

    public void Advance(int ms)
    {
        var until = _now + ms;
        while (true)
        {
            var next = _entries.Where(e => !e.Cancelled && !e.Ran && e.Due <= until).MinBy(e => e.Due);
            if (next is null) break;
            _now = next.Due;
            next.Run();
        }
        _now = until;
    }

    /// <summary>How many callbacks scheduled with this delay are still waiting to run.</summary>
    public int Pending(int dueMs) => _entries.Count(e => e.DelayMs == dueMs && !e.Cancelled && !e.Ran);

    /// <summary>Runs every cancelled callback that has not run — what a timer that fired just as it was
    /// disposed does.</summary>
    public void RunCancelled()
    {
        foreach (var entry in _entries.Where(e => e.Cancelled && !e.Ran).ToList())
            entry.Run();
    }

    private sealed class Entry(long due, int delayMs, Action callback) : IDisposable
    {
        public long Due { get; } = due;
        public int DelayMs { get; } = delayMs;
        public bool Cancelled { get; private set; }
        public bool Ran { get; private set; }

        public void Run()
        {
            Ran = true;
            callback();
        }

        public void Dispose() => Cancelled = true;
    }
}
