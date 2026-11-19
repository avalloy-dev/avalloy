using System.ComponentModel;
using System.IO.Abstractions;
using System.IO.Abstractions.TestingHelpers;
using System.Text.Json.Serialization;
using AwesomeAssertions;
using Avalloy.Preferences;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Avalloy.Base.Tests;

/// <summary>
/// The debounced save <see cref="PreferencesService{T}.RequestSave"/> schedules, against failing IO and
/// against <see cref="PreferencesService{T}.Dispose"/> and <see cref="PreferencesService{T}.Relocate"/>.
/// </summary>
/// <remarks>
/// <para>The save runs on a thread-pool timer thread, where an exception ends the process. That is how
/// it was found: a consuming app's test process died on Windows CI with a FileNotFoundException from
/// File.Replace, because a test deleted its folder while a save was still pending, and Dispose had not
/// stopped a save that had already fired. A real app hits the same thing when antivirus holds the file.</para>
///
/// <para>The timer is a <see cref="ManualScheduler"/> the test fires by hand, and a save is held at the
/// awkward moment by the model's own getter, which the serializer calls before the service takes its IO
/// lock: the window a save that has fired but not yet written sits in.</para>
/// </remarks>
[TestClass]
public sealed class PreferencesDelayedSaveTests
{
    private const string Dir = "/prefs";
    private const string PrefsPath = "/prefs/preferences.json";
    private const int DebounceMs = 300;

    [TestMethod]
    [DataRow(false, DisplayName = "IOException (sharing violation)")]
    [DataRow(true, DisplayName = "UnauthorizedAccessException")]
    public void A_Delayed_Save_That_Cannot_Write_Is_Logged_Not_Thrown(bool accessDenied)
    {
        var rig = new Rig();
        rig.Service.Initialize();
        Exception failure = accessDenied
            ? new UnauthorizedAccessException("denied")
            : new IOException("The process cannot access the file because it is being used by another process.");
        rig.Io.FailingFile.ReplaceFails = failure;

        rig.Service.Current.Name = "changed";
        var fire = () => rig.Scheduler.Advance(DebounceMs);

        fire.Should().NotThrow("an exception on a timer thread ends the process");
        rig.Log.Warnings.Should().ContainSingle().Which.Should().BeSameAs(failure);
    }

    [TestMethod]
    public void A_Delayed_Save_Whose_Model_Fails_To_Serialize_Is_Logged_Not_Thrown()
    {
        var rig = new Rig();
        rig.Service.Initialize();
        var failure = new InvalidOperationException("getter failed");
        rig.Service.Current.Name = "changed";
        rig.Service.Current.OnRead = () => throw failure;

        var fire = () => rig.Scheduler.Advance(DebounceMs);

        fire.Should().NotThrow("the timer thread is a top-level boundary");
        rig.Log.Warnings.Should().ContainSingle().Which.Should().BeSameAs(failure);
    }

    [TestMethod]
    public void A_Logger_That_Throws_Does_Not_Escape_A_Failed_Save()
    {
        var rig = new Rig();
        rig.Service.Initialize();
        rig.Log.Throws = true;
        rig.Io.FailingFile.ReplaceFails = new IOException("gone");

        rig.Service.Current.Name = "changed";
        var fire = () => rig.Scheduler.Advance(DebounceMs);
        rig.Service.Current.Name = "last";
        var dispose = () => rig.Service.Dispose();

        fire.Should().NotThrow();
        dispose.Should().NotThrow();
    }

    [TestMethod]
    public void A_Model_Getter_That_Disposes_During_The_Flush_Disposes_Once()
    {
        var rig = new Rig(watch: true);
        rig.Service.Initialize();
        rig.Service.Current.Name = "last";
        var writesBefore = rig.Io.FailingFile.Writes;
        // Bounded, so a version that recurses fails here rather than overflowing the stack.
        var calls = 0;
        bool? flushUnderAnyLock = null;
        rig.Service.Current.OnRead = () =>
        {
            flushUnderAnyLock ??= rig.Service.HoldsAnyLockForTests;
            if (++calls <= 3) rig.Service.Dispose();
        };

        rig.Service.Dispose();

        flushUnderAnyLock.Should().BeFalse("the flush serializes holding none of the service's locks");
        rig.Watchers.Should().ContainSingle().Which.Disposals.Should().Be(1);
        rig.Io.FailingFile.Writes.Should().Be(writesBefore + 1, "the flush writes once");
        rig.Io.File.ReadAllText(PrefsPath).Should().Contain("\"last\"");
    }

    [TestMethod]
    public void A_Delayed_Save_Queued_Across_A_Relocate_Does_Not_Overwrite_The_Adopted_File()
    {
        // The cancelled timer's callback runs while Relocate is between moving the path and loading
        // the file there: what it would write is the old settings, over the ones being adopted.
        var rig = new Rig(watch: true, onWatch: (rig, path) =>
        {
            if (path.StartsWith("/elsewhere", StringComparison.Ordinal)) rig.Scheduler.RunCancelled();
        });
        rig.Io.AddFile("/elsewhere/preferences.json", new MockFileData("""{ "name": "adopted" }"""));
        rig.Service.Initialize();
        rig.Service.Current.Name = "old";

        rig.Service.Relocate("/elsewhere", reload: true);

        rig.Service.Current.Name.Should().Be("adopted");
        rig.Io.File.ReadAllText("/elsewhere/preferences.json").Should().Contain("\"adopted\"");
    }

    [TestMethod]
    public void An_Explicit_Save_Already_Serializing_When_Dispose_Runs_Writes_Nothing_After_It()
    {
        var rig = new Rig();
        rig.Service.Initialize();
        rig.Service.Current.Name = "changed";
        using var held = rig.HoldNextSerialization();

        var saver = Start(() => rig.Service.SaveNow());
        held.Entered.Wait();
        held.UnderAnyLock.Should().BeFalse("model code runs holding none of the service's locks");
        // Checked before Dispose runs here: a save serializing under the IO lock would block it.
        held.UnderAnyLock.Should().BeFalse("a save serializes the model holding none of the service's locks");
        rig.Service.Dispose();   // its own flush reads the model unheld and finishes
        var writesAtDispose = rig.Io.FailingFile.Writes;
        held.Release();
        saver.Join();

        rig.Io.FailingFile.Writes.Should().Be(writesAtDispose, "nothing writes after Dispose returns");
    }

    [TestMethod]
    public void A_Save_That_Serialized_An_Older_Snapshot_Does_Not_Overwrite_A_Newer_One()
    {
        // Saves serialize outside the IO lock, so two can reach it in either order. The first has
        // captured "older" and is held; the second, started later with "newer", writes first.
        var rig = new Rig();
        rig.Service.Initialize();
        rig.Service.Current.Name = "older";
        rig.Scheduler.Advance(DebounceMs);
        using var held = new Hold();
        var armed = 1;
        rig.Service.Current.OnReadTail = () =>
        {
            if (Interlocked.Exchange(ref armed, 0) == 0) return;
            held.Park(rig.Service);
        };

        var olderSave = Start(() => rig.Service.SaveNow());
        held.Entered.Wait();
        held.UnderAnyLock.Should().BeFalse("model code runs holding none of the service's locks");
        rig.Service.Current.Name = "newer";
        rig.Service.SaveNow();
        held.Release();
        olderSave.Join();

        rig.Io.File.ReadAllText(PrefsPath).Should().Contain("\"newer\"", "the older snapshot must not land on top");
    }

    [TestMethod]
    public void A_Save_That_Serialized_The_Model_A_Load_Replaced_Does_Not_Overwrite_The_Loaded_File()
    {
        var rig = new Rig();
        rig.Service.Initialize();
        rig.Service.Current.Name = "older";
        rig.Scheduler.Advance(DebounceMs);
        using var held = new Hold();
        var armed = 1;
        rig.Service.Current.OnReadTail = () =>
        {
            if (Interlocked.Exchange(ref armed, 0) == 0) return;
            held.Park(rig.Service);
        };

        var olderSave = Start(() => rig.Service.SaveNow());
        held.Entered.Wait();
        held.UnderAnyLock.Should().BeFalse("model code runs holding none of the service's locks");
        rig.Io.File.WriteAllText(PrefsPath, """{ "name": "external" }""");
        rig.Service.Load();
        held.Release();
        olderSave.Join();

        rig.Io.File.ReadAllText(PrefsPath).Should().Contain("\"external\"", "the file Load adopted stays");
        rig.Service.Current.Name.Should().Be("external");
    }

    [TestMethod]
    public void A_Delayed_Save_That_Fires_As_A_Later_Request_Replaces_It_Writes_Nothing()
    {
        var rig = new Rig();
        rig.Service.Initialize();
        var writesBefore = rig.Io.FailingFile.Writes;

        rig.Service.Current.Name = "first";
        rig.Service.Current.Name = "second";
        rig.Scheduler.RunCancelled();

        rig.Io.FailingFile.Writes.Should().Be(writesBefore, "the later request's own timer writes, after its debounce");
    }

    [TestMethod]
    public void A_Delayed_Save_That_Fires_As_The_Service_Is_Disposed_Writes_Nothing()
    {
        var rig = new Rig();
        rig.Service.Initialize();
        rig.Service.Current.Name = "changed";

        rig.Service.Dispose();
        var writesAtDispose = rig.Io.FailingFile.Writes;
        rig.Scheduler.RunCancelled();

        rig.Io.FailingFile.Writes.Should().Be(writesAtDispose, "nothing writes after Dispose returns");
    }

    [TestMethod]
    public void A_Change_Still_Waiting_For_Its_Delayed_Save_Is_Written_On_Dispose()
    {
        var rig = new Rig();
        rig.Service.Initialize();

        rig.Service.Current.Name = "last";
        rig.Service.Dispose();

        rig.Io.File.ReadAllText(PrefsPath).Should().Contain("\"last\"");
    }

    [TestMethod]
    public void Dispose_Writes_Nothing_When_No_Save_Is_Owed()
    {
        var rig = new Rig();
        rig.Service.Initialize();
        rig.Service.Current.Name = "saved";
        rig.Scheduler.Advance(DebounceMs);
        var writesBefore = rig.Io.FailingFile.Writes;

        rig.Service.Dispose();

        rig.Io.FailingFile.Writes.Should().Be(writesBefore, "the delayed save already wrote the change");
    }

    [TestMethod]
    public void A_Flush_On_Dispose_That_Cannot_Write_Is_Logged_Not_Thrown()
    {
        var rig = new Rig();
        rig.Service.Initialize();
        rig.Service.Current.Name = "last";
        var failure = new IOException("gone");
        rig.Io.FailingFile.ReplaceFails = failure;

        var dispose = () => rig.Service.Dispose();

        dispose.Should().NotThrow();
        rig.Log.Warnings.Should().ContainSingle().Which.Should().BeSameAs(failure);
    }

    [TestMethod]
    public void An_Explicit_Save_After_Dispose_Does_Nothing()
    {
        var rig = new Rig();
        rig.Service.Initialize();
        rig.Service.Dispose();
        var writesAtDispose = rig.Io.FailingFile.Writes;

        rig.Service.Current.Name = "late";
        var reads = 0;
        rig.Service.Current.OnRead = () => reads++;
        rig.Service.SaveNow();

        rig.Io.FailingFile.Writes.Should().Be(writesAtDispose);
        reads.Should().Be(0, "a save after Dispose doesn't even serialize the model");
    }

    [TestMethod]
    public void A_Delayed_Save_Still_Serializing_When_Dispose_Runs_Writes_Nothing_After_It()
    {
        // The delayed save holds no lock while it serializes, so Dispose does not wait for it: Dispose
        // cancels it, flushes the change itself, and the save is dropped when it comes to write.
        var rig = new Rig();
        rig.Service.Initialize();
        rig.Service.Current.Name = "held";
        using var held = rig.HoldNextSerialization();

        var saver = Start(() => rig.Scheduler.Advance(DebounceMs));
        held.Entered.Wait();
        held.UnderAnyLock.Should().BeFalse("a delayed save serializes holding none of the service's locks");
        rig.Service.Dispose();
        var writesWhenDisposeReturned = rig.Io.FailingFile.Writes;
        held.Release();
        saver.Join();

        rig.Io.File.ReadAllText(PrefsPath).Should().Contain("\"held\"", "Dispose flushed the change");
        rig.Io.FailingFile.Writes.Should().Be(writesWhenDisposeReturned, "nothing writes after Dispose returns");
    }

    [TestMethod]
    public void A_Delayed_Save_Still_Serializing_When_Relocate_Runs_Writes_At_Neither_Location()
    {
        // Relocate cancels the save that is serializing and writes the settings at the new location
        // itself; the cancelled save is dropped when it comes to write.
        var rig = new Rig();
        rig.Service.Initialize();
        var oldContent = rig.Io.File.ReadAllText(PrefsPath);
        rig.Service.Current.Name = "held";
        using var held = rig.HoldNextSerialization();

        var saver = Start(() => rig.Scheduler.Advance(DebounceMs));
        held.Entered.Wait();
        held.UnderAnyLock.Should().BeFalse("model code runs holding none of the service's locks");
        rig.Service.Relocate("/elsewhere", reload: false);
        var writesWhenRelocateReturned = rig.Io.FailingFile.Writes;
        held.Release();
        saver.Join();

        rig.Io.File.ReadAllText(PrefsPath).Should().Be(oldContent, "the cancelled save never writes the old location");
        rig.Io.File.ReadAllText("/elsewhere/preferences.json").Should().Contain("\"held\"");
        rig.Io.FailingFile.Writes.Should().Be(writesWhenRelocateReturned, "the cancelled save writes nowhere");
    }

    [TestMethod]
    public void A_Delayed_Save_Cancelled_By_A_Relocate_Does_Not_Write_The_New_Location_Before_It()
    {
        // The cancelled save reaches the IO lock after the path has moved but before Relocate's own
        // write, so no newer snapshot has landed yet: only its stale generation keeps it from writing
        // the new location.
        var rig = new Rig();
        rig.Service.Initialize();
        rig.Service.Current.Name = "held";
        using var saveHeld = rig.HoldNextSerialization();
        using var relocateHeld = new Hold();

        var saver = Start(() => rig.Scheduler.Advance(DebounceMs));
        saveHeld.Entered.Wait();
        saveHeld.UnderAnyLock.Should().BeFalse("model code runs holding none of the service's locks");
        var relocator = Start(() =>
        {
            TestPrefs.ReadHookOnThisThread = () =>
            {
                TestPrefs.ReadHookOnThisThread = null;
                relocateHeld.Park(rig.Service);   // Relocate, moved, serializing its own write
            };
            rig.Service.Relocate("/elsewhere", reload: false);
        });
        relocateHeld.Entered.Wait();
        relocateHeld.UnderAnyLock.Should().BeFalse("model code runs holding none of the service's locks");
        saveHeld.Release();
        saver.Join();

        rig.Io.File.Exists("/elsewhere/preferences.json").Should().BeFalse("the cancelled save writes nowhere");
        relocateHeld.Release();
        relocator.Join();
        rig.Io.File.ReadAllText("/elsewhere/preferences.json").Should().Contain("\"held\"");
    }

    [TestMethod]
    public void A_Getter_That_Disposes_While_Load_Seeds_The_File_Does_Not_Deadlock_With_A_Delayed_Save()
    {
        // Load seeds a missing file by serializing a fresh model. Its getter runs the delayed save that
        // is due, then disposes. Serializing while holding the IO lock would leave the save waiting
        // for that lock and Dispose waiting for the save, so the getter checks the lock first and
        // plays the rest out only when it is free: a wrong build fails here instead of hanging.
        var rig = new Rig();
        rig.Service.Initialize();
        rig.Service.Current.Name = "pending";
        rig.Io.File.Delete(PrefsPath);
        bool? underAnyLock = null;

        var loader = Start(() =>
        {
            TestPrefs.ReadHookOnThisThread = () =>
            {
                TestPrefs.ReadHookOnThisThread = null;
                underAnyLock = rig.Service.HoldsAnyLockForTests;
                if (underAnyLock == true) return;
                Start(() => rig.Scheduler.Advance(DebounceMs)).Join();
                rig.Service.Dispose();
            };
            rig.Service.Load();
        });
        loader.Join();

        underAnyLock.Should().BeFalse("Load serializes the defaults holding none of the service's locks");
    }

    [TestMethod]
    public void A_Getter_That_Disposes_During_An_Explicit_Save_Does_Not_Deadlock_With_A_Delayed_Save()
    {
        var rig = new Rig();
        rig.Service.Initialize();
        rig.Service.Current.Name = "pending";
        bool? underAnyLock = null;

        var explicitSaver = Start(() =>
        {
            TestPrefs.ReadHookOnThisThread = () =>
            {
                TestPrefs.ReadHookOnThisThread = null;
                underAnyLock = rig.Service.HoldsAnyLockForTests;
                if (underAnyLock == true) return;
                Start(() => rig.Scheduler.Advance(DebounceMs)).Join();
                rig.Service.Dispose();
            };
            rig.Service.SaveNow();
        });
        explicitSaver.Join();

        underAnyLock.Should().BeFalse("SaveNow serializes the model holding none of the service's locks");
    }

    [TestMethod]
    public void A_Reload_Delivered_While_Dispose_Runs_Does_Not_Replace_The_Model()
    {
        // The watcher's change callback arrives once Dispose has begun but before it has finished:
        // here, from inside its own disposal of the watcher.
        Action? notify = null;
        var rig = new Rig(watch: true, onWatch: (_, _) => { }, onChanged: callback => notify = callback);
        rig.Service.Initialize();
        var model = rig.Service.Current;
        rig.Io.File.WriteAllText(PrefsPath, """{ "name": "external" }""");
        rig.Watchers.Single().OnDispose = () => notify!();

        rig.Service.Dispose();

        rig.Service.Current.Should().BeSameAs(model, "a reload stops once Dispose has begun");
    }

    [TestMethod]
    public void A_Save_That_Lands_While_Load_Builds_The_Model_Is_Not_Overwritten()
    {
        // Load builds the model without the IO lock, so a save can land between its read of the file
        // and its adopting what it read. Here the file is missing, and the save lands while Load
        // serializes the defaults it would seed it with: seeding anyway would write over that save.
        var rig = new Rig();
        rig.Service.Initialize();
        rig.Io.File.Delete(PrefsPath);
        rig.Service.Current.Name = "mine";
        TestPrefs.ReadHookOnThisThread = () =>
        {
            TestPrefs.ReadHookOnThisThread = null;
            rig.Service.SaveNow();
        };
        try
        {
            rig.Service.Load();
        }
        finally
        {
            TestPrefs.ReadHookOnThisThread = null;
        }

        rig.Io.File.ReadAllText(PrefsPath).Should().Contain("\"mine\"");
        rig.Service.Current.Name.Should().Be("mine", "Load read the file again and adopted the save");
    }

    [TestMethod]
    public void A_Second_Dispose_Returns_Only_Once_The_First_Has_Finished()
    {
        var rig = new Rig(watch: true);
        rig.Service.Initialize();
        rig.Service.Current.Name = "last";
        using var paused = new Hold();
        rig.Watchers.Single().OnDispose = () =>
        {
            paused.Entered.Set();
            paused.Gate.Wait();   // the first Dispose, after it set its disposing flag
        };
        int writesWhenFirstReturned = -1, writesWhenSecondReturned = -1;

        var first = Start(() =>
        {
            rig.Service.Dispose();
            writesWhenFirstReturned = rig.Io.FailingFile.Writes;
        });
        paused.Entered.Wait();
        var second = Start(() =>
        {
            rig.Service.Dispose();
            writesWhenSecondReturned = rig.Io.FailingFile.Writes;
        });

        BlockedOrFinished(second).Should().BeTrue("the second Dispose waits for the first");
        paused.Release();
        first.Join();
        second.Join();

        rig.Io.File.ReadAllText(PrefsPath).Should().Contain("\"last\"");
        rig.Io.FailingFile.Writes.Should().Be(writesWhenFirstReturned, "nothing writes after the first returns");
        rig.Io.FailingFile.Writes.Should().Be(writesWhenSecondReturned, "nor after the second");
    }

    [TestMethod]
    public void A_Reload_Already_Deserializing_When_Dispose_Begins_Does_Not_Replace_The_Model()
    {
        // The reload is past its first check and building the model when Dispose starts; Dispose is
        // held after setting its disposing flag, so the service is not yet disposed when the reload
        // comes to adopt what it built.
        Action? notify = null;
        var rig = new Rig(watch: true, onChanged: callback => notify = callback);
        rig.Service.Initialize();
        var model = rig.Service.Current;
        rig.Io.File.WriteAllText(PrefsPath, """{ "name": "external" }""");
        using var deserializing = new Hold();
        using var disposing = new Hold();
        rig.Watchers.Single().OnDispose = () =>
        {
            disposing.Entered.Set();
            disposing.Gate.Wait();
        };

        var reloader = Start(() =>
        {
            TestPrefs.WriteHookOnThisThread = () =>
            {
                TestPrefs.WriteHookOnThisThread = null;
                deserializing.Park(rig.Service);
            };
            notify!();
        });
        deserializing.Entered.Wait();
        deserializing.UnderAnyLock.Should().BeFalse("model code runs holding none of the service's locks");
        var disposer = Start(() => rig.Service.Dispose());
        disposing.Entered.Wait();
        deserializing.Release();
        reloader.Join();
        disposing.Release();
        disposer.Join();

        rig.Service.Current.Should().BeSameAs(model, "a reload stops once Dispose has begun, even one in flight");
    }

    [TestMethod]
    public void A_Watcher_That_Fails_To_Stop_Does_Not_Cost_The_Last_Change()
    {
        var rig = new Rig(watch: true);
        rig.Service.Initialize();
        rig.Service.Current.Name = "last";
        var failure = new IOException("watcher teardown failed");
        rig.Watchers.Single().OnDispose = () => throw failure;

        var dispose = () => rig.Service.Dispose();

        dispose.Should().NotThrow("Dispose runs on shutdown paths, where a throw is a crash");
        rig.Io.File.ReadAllText(PrefsPath).Should().Contain("\"last\"", "the owed change is still written");
        rig.Log.Warnings.Should().ContainSingle().Which.Should().BeSameAs(failure);
    }

    [TestMethod]
    public void A_Watcher_That_Fails_To_Stop_Does_Not_Stop_A_Relocate()
    {
        var rig = new Rig(watch: true);
        rig.Service.Initialize();
        rig.Service.Current.Name = "moving";
        var failure = new IOException("watcher teardown failed");
        rig.Watchers.Single().OnDispose = () => throw failure;

        var relocate = () => rig.Service.Relocate("/elsewhere", reload: false);

        relocate.Should().NotThrow();
        // Built the way the service builds it: Path.Combine joins with '\' on Windows.
        rig.Service.PreferencesPath.Should().Be(rig.Io.Path.Combine("/elsewhere", "preferences.json"));
        rig.Io.File.ReadAllText("/elsewhere/preferences.json").Should().Contain("\"moving\"");
        rig.Log.Warnings.Should().ContainSingle().Which.Should().BeSameAs(failure);
    }

    [TestMethod]
    public void A_Relocate_Whose_Watcher_Cannot_Start_Stays_Where_It_Was()
    {
        var failure = new IOException("cannot watch there");
        var rig = new Rig(watch: true, onWatch: (_, path) =>
        {
            if (path.StartsWith("/elsewhere", StringComparison.Ordinal)) throw failure;
        });
        rig.Service.Initialize();
        var path = rig.Service.PreferencesPath;
        rig.Service.Current.Name = "pending";

        var relocate = () => rig.Service.Relocate("/elsewhere", reload: false);

        relocate.Should().Throw<IOException>();
        rig.Service.PreferencesPath.Should().Be(path);
        rig.Watchers[0].Disposals.Should().Be(0, "the old watcher keeps watching");
        rig.Log.Warnings.Should().Contain(failure);
        rig.Scheduler.Advance(DebounceMs);
        rig.Io.File.ReadAllText(PrefsPath).Should().Contain("\"pending\"", "the save still waiting still happens, here");
    }

    [TestMethod]
    public void A_Relocate_Whose_Reload_Fails_Moves_Back()
    {
        // The new location has no file, so the reload seeds one, and that write fails.
        var rig = new Rig(watch: true);
        rig.Service.Initialize();
        var path = rig.Service.PreferencesPath;
        rig.Service.Current.Name = "pending";
        var failure = new IOException("cannot write there");
        rig.Io.FailingFile.MoveFails = failure;

        var relocate = () => rig.Service.Relocate("/elsewhere", reload: true);

        relocate.Should().Throw<IOException>();
        rig.Service.PreferencesPath.Should().Be(path);
        rig.Service.Current.Name.Should().Be("pending", "the reload never adopted anything");
        rig.Watchers[0].Disposals.Should().Be(0, "the old watcher keeps watching");
        rig.Watchers[1].Disposals.Should().Be(1, "the watcher made for the new location is stopped");
        rig.Log.Warnings.Should().Contain(failure);
        rig.Io.FailingFile.MoveFails = null;
        rig.Scheduler.Advance(DebounceMs);
        rig.Io.File.ReadAllText(PrefsPath).Should().Contain("\"pending\"", "the save still owed still happens, here");
    }

    [TestMethod]
    public void A_Relocate_Whose_Model_Constructor_Throws_Moves_Back()
    {
        // Not an IO failure: the model itself throws while the reload builds it. Nothing was adopted,
        // so the move is undone all the same.
        var rig = new Rig(watch: true);
        rig.Io.AddFile("/elsewhere/preferences.json", new MockFileData("""{ "name": "adopted" }"""));
        rig.Service.Initialize();
        var path = rig.Service.PreferencesPath;
        var model = rig.Service.Current;
        TestPrefs.ConstructHookOnThisThread = () => throw new InvalidOperationException("model failed");
        Action relocate = () => rig.Service.Relocate("/elsewhere", reload: true);

        try
        {
            // new T() goes through Activator, which wraps what the constructor threw.
            relocate.Should().Throw<System.Reflection.TargetInvocationException>()
                .WithInnerException<InvalidOperationException>();
        }
        finally
        {
            TestPrefs.ConstructHookOnThisThread = null;
        }

        rig.Service.PreferencesPath.Should().Be(path);
        rig.Service.Current.Should().BeSameAs(model);
        rig.Watchers[0].Disposals.Should().Be(0, "the old watcher keeps watching");
        rig.Watchers[1].Disposals.Should().Be(1, "the watcher made for the new location is stopped");
    }

    [TestMethod]
    public void A_Relocate_That_Reloads_Leaves_Nothing_Owed()
    {
        // The change waiting for its delayed save gives way to the file adopted at the new location,
        // so Dispose has nothing left to write.
        var rig = new Rig();
        rig.Io.AddFile("/elsewhere/preferences.json", new MockFileData("""{ "name": "adopted" }"""));
        rig.Service.Initialize();
        rig.Service.Current.Name = "pending";
        rig.Service.Relocate("/elsewhere", reload: true);
        var writesAfterRelocate = rig.Io.FailingFile.Writes;

        rig.Service.Dispose();

        rig.Io.FailingFile.Writes.Should().Be(writesAfterRelocate);
    }

    [TestMethod]
    public void A_Relocate_After_Dispose_Does_Nothing()
    {
        var rig = new Rig(watch: true);
        rig.Service.Initialize();
        var path = rig.Service.PreferencesPath;
        rig.Service.Dispose();

        rig.Service.Relocate("/elsewhere", reload: false);

        rig.Service.PreferencesPath.Should().Be(path);
        rig.Io.File.Exists("/elsewhere/preferences.json").Should().BeFalse();
        rig.Watchers.Should().ContainSingle("a disposed service starts no watcher");
    }

    [TestMethod]
    public void A_Relocate_That_Waits_For_A_Running_Dispose_Does_Nothing()
    {
        var rig = new Rig(watch: true);
        rig.Service.Initialize();
        var path = rig.Service.PreferencesPath;
        using var paused = new Hold();
        rig.Watchers.Single().OnDispose = () =>
        {
            paused.Entered.Set();
            paused.Gate.Wait();   // Dispose, holding the gate a Relocate has to take
        };

        var disposer = Start(() => rig.Service.Dispose());
        paused.Entered.Wait();
        var relocator = Start(() => rig.Service.Relocate("/elsewhere", reload: false));
        BlockedOrFinished(relocator).Should().BeTrue("Relocate waits for the gate Dispose holds");
        paused.Release();
        disposer.Join();
        relocator.Join();

        rig.Service.PreferencesPath.Should().Be(path);
        rig.Io.File.Exists("/elsewhere/preferences.json").Should().BeFalse();
        rig.Watchers.Should().ContainSingle("a disposing service starts no watcher");
    }

    [TestMethod]
    public void A_Reload_That_Read_The_Old_File_Before_A_Relocate_Reads_Again()
    {
        // A reload has read the old location's file and is building its model when Relocate moves
        // the service and loads the new location. Adopting what it built would put the old file's
        // settings back.
        Action? notifyOld = null;
        var rig = new Rig(watch: true, onChanged: callback => notifyOld ??= callback);
        rig.Io.AddFile("/elsewhere/preferences.json", new MockFileData("""{ "name": "adopted" }"""));
        rig.Service.Initialize();
        rig.Io.File.WriteAllText(PrefsPath, """{ "name": "old location" }""");
        using var deserializing = new Hold();

        var reloader = Start(() =>
        {
            TestPrefs.WriteHookOnThisThread = () =>
            {
                TestPrefs.WriteHookOnThisThread = null;
                deserializing.Park(rig.Service);
            };
            notifyOld!();
        });
        deserializing.Entered.Wait();
        deserializing.UnderAnyLock.Should().BeFalse("model code runs holding none of the service's locks");
        rig.Service.Relocate("/elsewhere", reload: true);
        deserializing.Release();
        reloader.Join();

        rig.Service.Current.Name.Should().Be("adopted");
    }

    [TestMethod]
    public void A_Dispose_Nested_On_The_Disposing_Thread_Returns_At_Once()
    {
        var rig = new Rig(watch: true);
        rig.Service.Initialize();
        rig.Watchers.Single().OnDispose = () => rig.Service.Dispose();
        // A nested Dispose that waited would wait on itself; failing at the wait shows it without hanging.
        rig.Service.BeforeDisposeWaitsForTests = () => throw new InvalidOperationException("a nested Dispose waited");

        var dispose = () => rig.Service.Dispose();

        dispose.Should().NotThrow();
    }

    [TestMethod]
    public void A_Getter_That_Disposes_In_A_Delayed_Save_While_A_Relocate_Is_Mid_Move_Does_Not_Deadlock()
    {
        // #178. A delayed save is serializing when a Relocate on another thread parks mid-move, holding
        // the locks the move takes; the save's model getter then calls Dispose. A getter that ran
        // under any of the service's locks could take them out of order here. It holds none: its
        // Dispose waits for the move, and everything completes.
        var rig = new Rig(watch: true);
        rig.Service.Initialize();
        rig.Service.Current.Name = "pending";
        using var inGetter = new Hold();
        using var midMove = new Hold();
        bool? getterUnderAnyLock = null;
        var getterArmed = 1;
        rig.Service.Current.OnRead = () =>
        {
            if (Interlocked.Exchange(ref getterArmed, 0) == 0) return;
            // Checked before parking: a getter parked under a lock would stall the Relocate too.
            getterUnderAnyLock = rig.Service.HoldsAnyLockForTests;
            inGetter.Entered.Set();
            if (getterUnderAnyLock == true) return;   // would deadlock: fail below instead of hanging
            inGetter.Gate.Wait();
            rig.Service.Dispose();
        };
        var swapArmed = 1;
        rig.Service.InsideSwapForTests = () =>
        {
            if (Interlocked.Exchange(ref swapArmed, 0) == 0) return;
            midMove.Entered.Set();
            midMove.Gate.Wait();
        };

        var saver = Start(() => rig.Scheduler.Advance(DebounceMs));
        inGetter.Entered.Wait();
        getterUnderAnyLock.Should().BeFalse("a delayed save serializes holding none of the service's locks");
        var relocator = Start(() => rig.Service.Relocate("/elsewhere", reload: false));
        midMove.Entered.Wait();
        inGetter.Release();
        BlockedOrFinished(saver).Should().BeTrue("the getter's Dispose waits for the move under way");
        midMove.Release();
        relocator.Join();
        saver.Join();

        getterUnderAnyLock.Should().BeFalse("a delayed save serializes holding none of the service's locks");
        rig.Watchers[1].Disposals.Should().Be(1, "Dispose stopped the watcher the Relocate started");
        rig.Io.File.ReadAllText("/elsewhere/preferences.json").Should().Contain("\"pending\"");
    }

    [TestMethod]
    public void A_Dispose_During_A_Relocate_Mid_Move_Leaves_Nothing_Half_Done()
    {
        // Relocate is parked mid-move when Dispose begins. Dispose waits for the move, then stops the
        // watcher it started and writes the settings the new location is owed.
        var rig = new Rig(watch: true);
        rig.Service.Initialize();
        using var midMove = new Hold();
        var armed = 1;
        rig.Service.InsideSwapForTests = () =>
        {
            if (Interlocked.Exchange(ref armed, 0) == 0) return;
            midMove.Entered.Set();
            midMove.Gate.Wait();
        };
        var writesWhenDisposeReturned = -1;

        var relocator = Start(() => rig.Service.Relocate("/elsewhere", reload: false));
        midMove.Entered.Wait();
        var disposer = Start(() =>
        {
            rig.Service.Dispose();
            writesWhenDisposeReturned = rig.Io.FailingFile.Writes;
        });
        BlockedOrFinished(disposer).Should().BeTrue("Dispose waits for the move under way");
        midMove.Release();
        relocator.Join();
        disposer.Join();

        rig.Watchers.Should().HaveCount(2);
        rig.Watchers[1].Disposals.Should().Be(1, "the watcher Relocate started is disposed, not leaked");
        rig.Io.File.Exists("/elsewhere/preferences.json").Should().BeTrue("the new location got the settings");
        rig.Io.FailingFile.Writes.Should().Be(writesWhenDisposeReturned, "nothing writes after Dispose returns");
    }

    [TestMethod]
    public void A_Relocate_Writing_When_Dispose_Runs_Still_Leaves_The_Settings_At_The_New_Location()
    {
        // Relocate has moved and is serializing the settings for the new location, holding no lock,
        // when Dispose runs. Dispose waits for the Relocate under way before it flushes, and nothing
        // writes after it returns.
        var rig = new Rig(watch: true);
        rig.Service.Initialize();
        rig.Service.Current.Name = "moving";
        rig.Scheduler.Advance(DebounceMs);   // nothing owed before the move
        using var paused = new Hold();
        var writesWhenDisposeReturned = -1;

        var relocator = Start(() =>
        {
            TestPrefs.ReadHookOnThisThread = () =>
            {
                TestPrefs.ReadHookOnThisThread = null;
                paused.Park(rig.Service);   // Relocate, serializing its write
            };
            rig.Service.Relocate("/elsewhere", reload: false);
        });
        paused.Entered.Wait();
        paused.UnderAnyLock.Should().BeFalse("Relocate serializes its write holding no lock");
        var disposer = Start(() =>
        {
            rig.Service.Dispose();
            writesWhenDisposeReturned = rig.Io.FailingFile.Writes;
        });
        BlockedOrFinished(disposer).Should().BeTrue("Dispose waits for the Relocate under way");
        paused.Release();
        relocator.Join();
        disposer.Join();

        rig.Io.File.ReadAllText("/elsewhere/preferences.json").Should().Contain("\"moving\"");
        rig.Io.FailingFile.Writes.Should().Be(writesWhenDisposeReturned, "nothing writes after Dispose returns");
    }

    [TestMethod]
    public void A_Dispose_During_A_Relocate_That_Reloads_Keeps_The_Adopted_Settings()
    {
        // The old model has a change no save has written, and gains another while Relocate(reload:true)
        // deserializes the target. Dispose, on another thread, must not flush the old model to the new
        // path: Relocate would then read that back, and the settings it was adopting would be lost.
        var rig = new Rig(watch: true);
        rig.Io.AddFile("/elsewhere/preferences.json", new MockFileData("""{ "name": "adopted" }"""));
        rig.Service.Initialize();
        rig.Service.Current.Name = "unsaved";
        var oldModel = rig.Service.Current;
        using var deserializing = new Hold();

        var relocator = Start(() =>
        {
            TestPrefs.WriteHookOnThisThread = () =>
            {
                TestPrefs.WriteHookOnThisThread = null;
                deserializing.Park(rig.Service);   // Relocate, reading the target
            };
            rig.Service.Relocate("/elsewhere", reload: true);
        });
        deserializing.Entered.Wait();
        deserializing.UnderAnyLock.Should().BeFalse("model code runs holding none of the service's locks");
        oldModel.Name = "late";   // owed again: only waiting for the Relocate keeps it off the new path
        var disposer = Start(() => rig.Service.Dispose());
        BlockedOrFinished(disposer).Should().BeTrue("Dispose waits for the Relocate under way");
        deserializing.Release();
        relocator.Join();
        disposer.Join();

        rig.Service.Current.Name.Should().Be("adopted");
        var file = rig.Io.File.ReadAllText("/elsewhere/preferences.json");
        file.Should().Contain("\"adopted\"");
        file.Should().NotContain("\"unsaved\"").And.NotContain("\"late\"");
    }

    [TestMethod]
    public void A_Dispose_From_Inside_A_Relocate_That_Reloads_Writes_Nothing_Over_The_Target()
    {
        // A model setter, run on the relocating thread while the target is deserialized, disposes. It
        // can't wait for the Relocate it is part of, so what keeps its flush off the new path is that the
        // reload gave up the change still waiting: the target file wins.
        var rig = new Rig(watch: true);
        rig.Io.AddFile("/elsewhere/preferences.json", new MockFileData("""{ "name": "adopted" }"""));
        rig.Service.Initialize();
        rig.Service.Current.Name = "unsaved";

        TestPrefs.WriteHookOnThisThread = () =>
        {
            TestPrefs.WriteHookOnThisThread = null;
            rig.Service.Dispose();
        };
        try
        {
            rig.Service.Relocate("/elsewhere", reload: true);
        }
        finally
        {
            TestPrefs.WriteHookOnThisThread = null;
        }

        var file = rig.Io.File.ReadAllText("/elsewhere/preferences.json");
        file.Should().Contain("\"adopted\"").And.NotContain("\"unsaved\"");
        rig.Service.Current.Name.Should().Be("adopted");
    }

    [TestMethod]
    public void A_Reloaded_Handler_That_Throws_Does_Not_Stop_A_Relocate()
    {
        var rig = new Rig(watch: true);
        rig.Io.AddFile("/elsewhere/preferences.json", new MockFileData("""{ "name": "adopted" }"""));
        rig.Service.Initialize();
        var failure = new InvalidOperationException("handler failed");
        rig.Service.PreferencesReloaded += (_, _) => throw failure;

        var relocate = () => rig.Service.Relocate("/elsewhere", reload: true);

        relocate.Should().NotThrow("a handler is outside code; its failure is logged");
        rig.Service.PreferencesPath.Should().Be(rig.Io.Path.Combine("/elsewhere", "preferences.json"));
        rig.Service.Current.Name.Should().Be("adopted");
        rig.Watchers[0].Disposals.Should().Be(1, "the old watcher is let go");
        rig.Log.Warnings.Should().Contain(failure);
    }

    [TestMethod]
    public void A_Failure_After_A_Relocate_Adopted_The_Target_Still_Lets_The_Old_Watcher_Go()
    {
        // Past the commit point (the target adopted), the old model fails as it is let go. Nothing is
        // rolled back, and the old watcher is still stopped.
        var rig = new Rig(watch: true);
        rig.Io.AddFile("/elsewhere/preferences.json", new MockFileData("""{ "name": "adopted" }"""));
        rig.Service.Initialize();
        rig.Service.Current.OnUnregister = () => throw new InvalidOperationException("model failed");

        var relocate = () => rig.Service.Relocate("/elsewhere", reload: true);

        relocate.Should().Throw<InvalidOperationException>();
        rig.Service.PreferencesPath.Should().Be(rig.Io.Path.Combine("/elsewhere", "preferences.json"));
        rig.Service.Current.Name.Should().Be("adopted");
        rig.Watchers[0].Disposals.Should().Be(1, "the old watcher is let go after the commit point too");
    }

    [TestMethod]
    public void A_Dispose_From_A_Relocate_Handler_While_Another_Thread_Disposes_Finishes_And_Disposes_Once()
    {
        // Thread A's Dispose waits for B's Relocate, and B's PreferencesReloaded handler calls Dispose.
        // B's Dispose, inside B's own Relocate, must not wait for A's (which waits for B): it returns,
        // B finishes, and A's dispose completes.
        var rig = new Rig(watch: true);
        rig.Io.AddFile("/elsewhere/preferences.json", new MockFileData("""{ "name": "adopted" }"""));
        rig.Service.Initialize();
        using var inHandler = new Hold();
        var armed = 1;
        rig.Service.PreferencesReloaded += (_, _) =>
        {
            if (Interlocked.Exchange(ref armed, 0) == 0) return;
            inHandler.Park(rig.Service);
            rig.Service.Dispose();
        };
        Thread? relocatorThread = null;
        var waitedOnRelocatingThread = 0;
        rig.Service.BeforeDisposeWaitsForTests = () =>
        {
            if (Thread.CurrentThread != Volatile.Read(ref relocatorThread)) return;
            Interlocked.Exchange(ref waitedOnRelocatingThread, 1);
            throw new InvalidOperationException("a Dispose inside Relocate waited");   // fail, not hang
        };

        var relocator = new Thread(() => rig.Service.Relocate("/elsewhere", reload: true)) { IsBackground = true };
        Volatile.Write(ref relocatorThread, relocator);
        relocator.Start();
        inHandler.Entered.Wait();
        inHandler.UnderAnyLock.Should().BeFalse("handlers run holding none of the service's locks");
        var disposer = Start(() => rig.Service.Dispose());
        BlockedOrFinished(disposer).Should().BeTrue("A's Dispose waits for the Relocate under way");
        inHandler.Release();
        relocator.Join();
        disposer.Join();

        Volatile.Read(ref waitedOnRelocatingThread).Should().Be(0, "B's Dispose, inside its own Relocate, never waits");
        rig.Service.Current.Name.Should().Be("adopted");
        rig.Watchers[1].Disposals.Should().Be(1, "disposed once");
    }

    [TestMethod]
    public void A_Setter_That_Mutates_And_Disposes_During_A_Relocate_That_Reloads_Leaves_The_Target_As_It_Was()
    {
        // While the reload deserializes the target, a setter changes the old model and disposes. The
        // change is to the model being replaced, so it is discarded (the target wins); the Dispose is
        // deferred to the end of the Relocate, after adoption, and has nothing to flush.
        var rig = new Rig(watch: true);
        rig.Io.AddFile("/elsewhere/preferences.json", new MockFileData("""{ "name": "adopted" }"""));
        rig.Service.Initialize();
        var oldModel = rig.Service.Current;
        rig.Service.BeforeDisposeWaitsForTests = () => throw new InvalidOperationException("a Dispose waited");
        TestPrefs.WriteHookOnThisThread = () =>
        {
            TestPrefs.WriteHookOnThisThread = null;
            oldModel.Name = "late";
            rig.Service.Dispose();
        };
        var writesBefore = rig.Io.FailingFile.Writes;
        try
        {
            rig.Service.Relocate("/elsewhere", reload: true);
        }
        finally
        {
            TestPrefs.WriteHookOnThisThread = null;
        }

        rig.Service.Current.Name.Should().Be("adopted");
        rig.Io.File.ReadAllText("/elsewhere/preferences.json").Should().Contain("\"adopted\"").And.NotContain("\"late\"");
        rig.Io.FailingFile.Writes.Should().Be(writesBefore, "the old model's change is discarded, and nothing is flushed");
        rig.Watchers[1].Disposals.Should().Be(1, "the deferred Dispose ran once the Relocate finished");
    }

    [TestMethod]
    public void A_Change_Made_After_A_Deferred_Dispose_Is_Not_Saved()
    {
        // Nothing is owed. A getter run by an explicit save disposes, then changes the model. The
        // Dispose is deferred to the end of the save; from the moment it is called, a change makes no
        // save owed, so there is nothing for the deferred Dispose to flush (and the save itself, once
        // disposing has begun, writes nothing).
        var rig = new Rig();
        rig.Service.Initialize();
        rig.Service.Current.Name = "saved";
        rig.Scheduler.Advance(DebounceMs);
        var armed = 1;
        rig.Service.Current.OnReadTail = () =>
        {
            if (Interlocked.Exchange(ref armed, 0) == 0) return;
            rig.Service.Dispose();
            rig.Service.Current.Name = "after";
        };

        rig.Service.SaveNow();

        rig.Io.File.ReadAllText(PrefsPath).Should().Contain("\"saved\"").And.NotContain("\"after\"");
    }

    [TestMethod]
    public void A_Save_During_A_Seed_That_Finds_The_File_Appeared_Writes_Neither_Defaults_Nor_Over_It()
    {
        // #182. Load found the file missing and is writing defaults when an editor creates it, and a
        // save starts in that moment. Current stays the old model until the defaults are on disk, so
        // the save never sees them; and as the file appeared, the save is older than the file the
        // reload goes on to adopt. The save reaches the IO lock before the reload reads again.
        var rig = new Rig();
        rig.Service.Initialize();
        rig.Service.Current.Name = "mine";
        rig.Scheduler.Advance(DebounceMs);
        rig.Io.File.Delete(PrefsPath);
        var oldModel = rig.Service.Current;
        var oldModelReads = 0;
        using var saveCaptured = new ManualResetEventSlim();
        oldModel.OnRead = () =>
        {
            Interlocked.Increment(ref oldModelReads);
            saveCaptured.Set();   // serializing, so past taking its model and snapshot number
        };
        Thread? saver = null;
        var armed = 1;
        rig.Io.FailingFile.BeforeMove = destination =>
        {
            if (destination != rig.Service.PreferencesPath) return;
            if (Interlocked.Exchange(ref armed, 0) == 0) return;
            rig.Io.File.WriteAllText(PrefsPath, """{ "name": "external" }""");
            var started = Start(() => rig.Service.SaveNow());
            Volatile.Write(ref saver, started);
            saveCaptured.Wait();   // it has taken its model and number; it then waits for the IO lock held here
        };
        rig.Service.BeforeReloadRetriesForTests = () => Volatile.Read(ref saver)?.Join();
        var writesBefore = rig.Io.FailingFile.Writes;

        rig.Service.Load();

        Volatile.Read(ref saver).Should().NotBeNull("the save ran while the defaults were being written");
        Volatile.Read(ref oldModelReads).Should().BeGreaterThan(0, "the save took the old model, never the defaults");
        rig.Io.File.ReadAllText(PrefsPath).Should().Contain("\"external\"", "the file that appeared is kept");
        rig.Service.Current.Name.Should().Be("external");
        rig.Io.FailingFile.Writes.Should().Be(writesBefore, "neither the defaults nor the save were written");
    }

    [TestMethod]
    public void An_Explicit_Save_Serializing_When_Dispose_Begins_Writes_Nothing()
    {
        // #183. Dispose has begun (it is stopping the watcher) while an explicit save is serializing.
        // The save is not the flush, so once disposing has begun it writes nothing; the flush writes
        // the change instead.
        var rig = new Rig(watch: true);
        rig.Service.Initialize();
        rig.Service.Current.Name = "changed";
        using var held = rig.HoldNextSerialization();
        using var disposing = new Hold();
        rig.Watchers.Single().OnDispose = () =>
        {
            disposing.Entered.Set();
            disposing.Gate.Wait();   // Dispose, begun
        };

        var saver = Start(() => rig.Service.SaveNow());
        held.Entered.Wait();
        held.UnderAnyLock.Should().BeFalse("model code runs holding none of the service's locks");
        var disposer = Start(() => rig.Service.Dispose());
        disposing.Entered.Wait();
        var writesWhenDisposeBegan = rig.Io.FailingFile.Writes;
        held.Release();
        saver.Join();

        rig.Io.FailingFile.Writes.Should().Be(writesWhenDisposeBegan, "a save already serializing writes nothing once Dispose has begun");
        disposing.Release();
        disposer.Join();
        rig.Io.File.ReadAllText(PrefsPath).Should().Contain("\"changed\"", "the flush wrote the change");
    }

    [TestMethod]
    public void A_Load_After_A_Deferred_Dispose_Keeps_The_Unsaved_Change_For_The_Flush()
    {
        // An unsaved change, and no file. A getter run by an explicit save requests Dispose (deferred to
        // the end of the save), then calls Load. Once Dispose has begun, Load changes nothing: it
        // neither seeds defaults nor replaces the model, so the deferred Dispose flushes the change.
        var rig = new Rig();
        rig.Service.Initialize();
        rig.Service.Current.Name = "unsaved";
        rig.Io.File.Delete(PrefsPath);
        var armed = 1;
        rig.Service.Current.OnReadTail = () =>
        {
            if (Interlocked.Exchange(ref armed, 0) == 0) return;
            rig.Service.Dispose();
            rig.Service.Load();
        };

        rig.Service.SaveNow();

        rig.Service.Current.Name.Should().Be("unsaved");
        rig.Io.File.ReadAllText(PrefsPath).Should().Contain("\"unsaved\"", "the deferred Dispose flushed the change");
    }

    [TestMethod]
    public void A_Dispose_Requested_While_Load_Seeds_Stops_The_Seed_And_Keeps_The_Unsaved_Change()
    {
        // Load passed its first check and is serializing the defaults it would seed a missing file with
        // when a Dispose is requested from that code (deferred to the end of the Load). It neither
        // seeds nor adopts; the flush is the one write, and it writes the unsaved change.
        var rig = new Rig();
        rig.Service.Initialize();
        rig.Service.Current.Name = "unsaved";
        rig.Io.File.Delete(PrefsPath);
        TestPrefs.ReadHookOnThisThread = () =>
        {
            TestPrefs.ReadHookOnThisThread = null;
            rig.Service.Dispose();
        };
        var writesBefore = rig.Io.FailingFile.Writes;
        try
        {
            rig.Service.Load();
        }
        finally
        {
            TestPrefs.ReadHookOnThisThread = null;
        }

        rig.Service.Current.Name.Should().Be("unsaved");
        rig.Io.File.ReadAllText(PrefsPath).Should().Contain("\"unsaved\"");
        rig.Io.FailingFile.Writes.Should().Be(writesBefore + 1, "no seed: the flush is the only write");
    }

    [TestMethod]
    public void A_Seed_Never_Counts_As_A_Pending_Save()
    {
        // A change is waiting for its save when Load finds the file missing and seeds defaults. The
        // defaults are not that change: a save is still owed afterwards, and Dispose writes it.
        var rig = new Rig();
        rig.Service.Initialize();
        rig.Service.Current.Name = "unsaved";
        rig.Io.File.Delete(PrefsPath);
        rig.Service.Load();
        var writesAfterLoad = rig.Io.FailingFile.Writes;

        rig.Service.Dispose();

        rig.Io.FailingFile.Writes.Should().Be(writesAfterLoad + 1, "the seed did not count as the pending save");
    }

    [TestMethod]
    public void A_Dispose_From_A_Relocate_Callback_Holds_No_Lock_And_Completes()
    {
        // A PreferencesReloaded handler runs inside Relocate's reload. It runs holding none of the
        // service's locks, so a Dispose from it is an ordinary Dispose.
        var rig = new Rig(watch: true);
        rig.Service.Initialize();
        bool? handlerUnderAnyLock = null;
        var armed = 1;
        rig.Service.PreferencesReloaded += (_, _) =>
        {
            if (Interlocked.Exchange(ref armed, 0) == 0) return;
            handlerUnderAnyLock = rig.Service.HoldsAnyLockForTests;
            if (handlerUnderAnyLock == true) return;
            rig.Service.Dispose();
        };

        rig.Service.Relocate("/elsewhere", reload: true);

        handlerUnderAnyLock.Should().BeFalse("handlers run holding none of the service's locks");
        rig.Watchers[1].Disposals.Should().Be(1, "the handler's Dispose ran to the end");
    }

    [TestMethod]
    public void A_Save_That_Took_The_Model_A_Reload_Replaces_Cannot_Overwrite_The_Adopted_File()
    {
        // #180. A reload has read an external edit and is building its model when a save takes the
        // current model and its snapshot number and starts serializing. The reload then adopts the
        // edit. The save's number was taken before the reload's, since a reload takes its number as it
        // swaps Current, so the save is older and must not land on the adopted file.
        Action? notify = null;
        var rig = new Rig(watch: true, onChanged: callback => notify ??= callback);
        rig.Service.Initialize();
        rig.Service.Current.Name = "older";
        rig.Scheduler.Advance(DebounceMs);
        rig.Io.File.WriteAllText(PrefsPath, """{ "name": "external" }""");
        using var reloading = new Hold();
        using var saving = new Hold();
        var saveArmed = 1;
        rig.Service.Current.OnReadTail = () =>
        {
            if (Interlocked.Exchange(ref saveArmed, 0) == 0) return;
            saving.Park(rig.Service);   // the save, past taking its model and number
        };

        var reloader = Start(() =>
        {
            TestPrefs.WriteHookOnThisThread = () =>
            {
                TestPrefs.WriteHookOnThisThread = null;
                reloading.Park(rig.Service);   // the reload, past reading the edit
            };
            notify!();
        });
        reloading.Entered.Wait();
        reloading.UnderAnyLock.Should().BeFalse("model code runs holding none of the service's locks");
        var saver = Start(() => rig.Service.SaveNow());
        saving.Entered.Wait();
        saving.UnderAnyLock.Should().BeFalse("model code runs holding none of the service's locks");
        reloading.Release();
        reloader.Join();
        saving.Release();
        saver.Join();

        rig.Service.Current.Name.Should().Be("external");
        rig.Io.File.ReadAllText(PrefsPath).Should().Contain("\"external\"", "the save of the replaced model is older");
    }

    [TestMethod]
    public void A_File_That_Appears_While_Load_Seeds_Defaults_Is_Kept()
    {
        // #179. Load found the file missing and is writing defaults when an editor or a sync tool
        // creates it. The defaults go in only if the file is still missing; here it isn't, so it is
        // loaded instead.
        var rig = new Rig();
        rig.Service.Initialize();
        rig.Io.File.Delete(PrefsPath);
        var armed = 1;
        rig.Io.FailingFile.BeforeMove = destination =>
        {
            // The service's own path string: Path.Combine joins with '\' on Windows.
            if (destination != rig.Service.PreferencesPath) return;
            if (Interlocked.Exchange(ref armed, 0) == 0) return;
            rig.Io.File.WriteAllText(PrefsPath, """{ "name": "external" }""");
        };

        rig.Service.Load();

        rig.Io.File.ReadAllText(PrefsPath).Should().Contain("\"external\"", "the file that appeared is kept");
        rig.Service.Current.Name.Should().Be("external");
    }

    private static Thread Start(Action action)
    {
        var thread = new Thread(() => action()) { IsBackground = true };
        thread.Start();
        return thread;
    }

    /// <summary>Spins until <paramref name="thread"/> blocks or ends, and says which: true for blocked.
    /// No clock involved — a thread that doesn't wait ends, and one that does is seen waiting.</summary>
    private static bool BlockedOrFinished(Thread thread)
    {
        SpinWait.SpinUntil(() => !thread.IsAlive || IsBlocked(thread));
        return thread.IsAlive;
    }

    // Only ever asked to see a thread block, never to see one not block: a thread can read as waiting
    // for a moment for reasons of the runtime's own (a lock taken while it compiles), so "it didn't
    // block" can't be told from a snapshot. Those claims are made with HoldsAnyLockForTests and
    // BeforeDisposeWaitsForTests instead.
    private static bool IsBlocked(Thread thread) =>
        thread.IsAlive && (thread.ThreadState & ThreadState.WaitSleepJoin) != 0;

    private sealed class Rig
    {
        public Rig(bool watch = false, Action<Rig, string>? onWatch = null, Action<Action>? onChanged = null)
        {
            Io.Directory.CreateDirectory(Dir);
            Service = new PreferencesService<TestPrefs>(Dir, TimeSpan.FromMilliseconds(DebounceMs), Io,
                "preferences.json", watch, typeInfoResolver: null,
                watcherFactory: (path, changed) =>
                {
                    var watcher = new CountingWatcher();
                    Watchers.Add(watcher);
                    onChanged?.Invoke(changed);
                    onWatch?.Invoke(this, path);
                    return watcher;
                },
                scheduler: Scheduler, logger: Log);
            // A Dispose that waits while its own thread is inside one of the service's operations waits
            // on itself. Fail there, so a build that does it fails its tests instead of hanging them.
            Service.BeforeDisposeWaitsForTests = () =>
            {
                if (Service.InOperationForTests)
                    throw new InvalidOperationException("a Dispose waited inside an operation on its own thread");
            };
        }

        public List<CountingWatcher> Watchers { get; } = new();
        public FailingFileSystem Io { get; } = new();
        public ManualScheduler Scheduler { get; } = new();
        public RecordingLogger Log { get; } = new();
        public PreferencesService<TestPrefs> Service { get; }

        /// <summary>Blocks the next read of <see cref="TestPrefs.Name"/> — the serializer's, inside a save —
        /// until released. Later reads go straight through.</summary>
        public Hold HoldNextSerialization()
        {
            var hold = new Hold();
            var armed = 1;
            Service.Current.OnRead = () =>
            {
                if (Interlocked.Exchange(ref armed, 0) == 0) return;
                hold.Park(Service);
            };
            return hold;
        }
    }

    private sealed class Hold : IDisposable
    {
        /// <summary>Whether the held read ran under the service's IO lock; set before <see cref="Entered"/>.</summary>
        public bool UnderAnyLock { get; private set; }

        /// <summary>Parks the calling model code until released, unless it is running under one of
        /// the service's locks: then it records that and returns at once, so a wrong build fails the
        /// test's check of <see cref="UnderAnyLock"/> instead of stalling everything behind the lock.</summary>
        public void Park(PreferencesService<TestPrefs> service)
        {
            UnderAnyLock = service.HoldsAnyLockForTests;
            Entered.Set();
            if (!UnderAnyLock) Gate.Wait();
        }
        public ManualResetEventSlim Entered { get; } = new();
        public ManualResetEventSlim Gate { get; } = new();
        public void Release() => Gate.Set();

        public void Dispose()
        {
            Gate.Set();   // never leave a test thread parked
            Entered.Dispose();
        }
    }

    internal sealed class FailingFileSystem : MockFileSystem
    {
        public FailingFileSystem() => FailingFile = new FailingMockFile(this);

        public FailingMockFile FailingFile { get; }
        public override IFile File => FailingFile;
    }

    /// <summary>Counts the renames that complete a save, and fails <c>Replace</c> on request.</summary>
    internal sealed class FailingMockFile(MockFileSystem fs) : MockFile(fs)
    {
        private int _writes;
        // MockFile.Replace moves through the overridable Move: that is one write, not two.
        [ThreadStatic] private static bool _replacing;

        public Exception? ReplaceFails { get; set; }
        public int Writes => Volatile.Read(ref _writes);

        public override void Replace(string sourceFileName, string destinationFileName, string? destinationBackupFileName)
        {
            if (ReplaceFails is { } failure) throw failure;
            _replacing = true;
            try { base.Replace(sourceFileName, destinationFileName, destinationBackupFileName); }
            finally { _replacing = false; }
            Interlocked.Increment(ref _writes);
        }

        public Exception? MoveFails { get; set; }

        /// <summary>Runs just before a move, with its destination: how a test makes a file appear at
        /// the last moment.</summary>
        public Action<string>? BeforeMove { get; set; }

        public override void Move(string sourceFileName, string destFileName)
        {
            if (!_replacing) BeforeMove?.Invoke(destFileName);
            if (!_replacing && MoveFails is { } failure) throw failure;
            base.Move(sourceFileName, destFileName);
            if (!_replacing) Interlocked.Increment(ref _writes);
        }
    }

    internal sealed class CountingWatcher : IFileChangeWatcher
    {
        public int Disposals { get; private set; }
        public Action? OnDispose { get; set; }
        public IDisposable SuppressEvents() => new Scope();

        public void Dispose()
        {
            Disposals++;
            OnDispose?.Invoke();
        }

        private sealed class Scope : IDisposable
        {
            public void Dispose() { }
        }
    }

    internal sealed class RecordingLogger : ILogger
    {
        private readonly List<Exception?> _warnings = new();

        /// <summary>Every call to <see cref="Log{TState}"/> throws: a provider failing at shutdown.</summary>
        public bool Throws { get; set; }

        public IReadOnlyList<Exception?> Warnings
        {
            get { lock (_warnings) return _warnings.ToList(); }
        }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (Throws) throw new ObjectDisposedException("ILoggerFactory");
            if (logLevel != LogLevel.Warning) return;
            lock (_warnings) _warnings.Add(exception);
        }
    }

    public sealed class TestPrefs : IPreferencesModel, INotifyPropertyChanged
    {
        private string _name = "default";

        public TestPrefs() => ConstructHookOnThisThread?.Invoke();

        /// <summary>Runs in every constructor called on this thread: how a test makes the model the
        /// service builds for a reload throw.</summary>
        [ThreadStatic] public static Action? ConstructHookOnThisThread;

        public string Name
        {
            get
            {
                OnRead?.Invoke();
                ReadHookOnThisThread?.Invoke();
                return _name;
            }
            set
            {
                WriteHookOnThisThread?.Invoke();
                _name = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Name)));
            }
        }

        /// <summary>Serialized after <see cref="Name"/>: holding a save here holds one that has already
        /// captured <see cref="Name"/>, so what it goes on to write is that older value.</summary>
        public string Tail
        {
            get
            {
                OnReadTail?.Invoke();
                return "tail";
            }
            set { }
        }

        [JsonIgnore]
        public Action? OnRead { get; set; }

        [JsonIgnore]
        public Action? OnReadTail { get; set; }

        /// <summary>Runs on every read made on this thread, by any instance: how a test reaches a model
        /// the service creates itself, such as the defaults Load seeds a missing file with.</summary>
        [ThreadStatic] public static Action? ReadHookOnThisThread;

        /// <summary>Runs on every write made on this thread: how a test holds a reload while it
        /// deserializes the file into a fresh model.</summary>
        [ThreadStatic] public static Action? WriteHookOnThisThread;

        public event PropertyChangedEventHandler? PropertyChanged;

        public void RegisterMutationHandler(PropertyChangedEventHandler handler) => PropertyChanged += handler;
        public void UnregisterMutationHandler(PropertyChangedEventHandler handler)
        {
            OnUnregister?.Invoke();
            PropertyChanged -= handler;
        }

        [JsonIgnore]
        public Action? OnUnregister { get; set; }
    }
}
