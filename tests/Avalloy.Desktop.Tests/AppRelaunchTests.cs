using System.Diagnostics;
using AwesomeAssertions;
using Avalloy.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Avalloy.Desktop.Tests;

/// <summary>
/// The relaunch helper must actually relaunch, on every platform.
///
/// <para>The first version compiled, started a process, and returned true while doing nothing: the shell script
/// went through <c>ProcessStartInfo.Arguments</c>, which is re-parsed with Windows quoting rules, so the quotes
/// around the executable path terminated the outer quote and <c>sh</c> got a truncated command. It "started", so
/// nothing surfaced. The design that replaced it has no shell at all — the app watches itself — which is what
/// makes these tests meaningful on Windows and Linux rather than macOS-only.</para>
/// </summary>
// Asserts on wall-clock timeouts around spawned processes, so it is timing-sensitive — but it holds no
// process-global state (the instance lock is injected as an isAvailable predicate), so it can run alongside
// its siblings here.
[TestClass]
[DoNotParallelize]   // the identity tests swap process-wide statics (AppName / HoldsInstanceLock)
public sealed class AppRelaunchTests
{
    private string _dir = "";

    [TestInitialize]
    public void Setup()
    {
        _dir = Path.Combine(Path.GetTempPath(), "avalloy-relaunch-" + Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(_dir);
    }

    [TestCleanup]
    public void Cleanup()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* temp */ }
    }

    // ── The watcher contract ────────────────────────────────────────────────────────────────────────────

    [TestMethod]
    public void A_Normal_Launch_Is_Not_A_Watcher()
    {
        AppRelaunch.ParseWatchPid([]).Should().BeNull();
        AppRelaunch.ParseWatchPid(["--prefs-dir", "/tmp"]).Should().BeNull();
        AppRelaunch.TryRunWatcher(["--prefs-dir", "/tmp"]).Should().BeFalse(
            "a normal launch must fall through to the real app");
    }

    [TestMethod]
    public void A_Watcher_Launch_Is_Recognised()
        => AppRelaunch.ParseWatchPid([AppRelaunch.WatchFlag, "4321"]).Should().Be(4321);

    /// <summary>A malformed flag must not be treated as a watcher — that process would exit instead of starting
    /// the app, and the user would be left with nothing.</summary>
    [TestMethod]
    public void A_Malformed_Watch_Argument_Is_Not_A_Watcher()
    {
        AppRelaunch.ParseWatchPid([AppRelaunch.WatchFlag]).Should().BeNull();
        AppRelaunch.ParseWatchPid([AppRelaunch.WatchFlag, "not-a-pid"]).Should().BeNull();
        AppRelaunch.ParseWatchPid([AppRelaunch.WatchFlag, "0"]).Should().BeNull();
        AppRelaunch.ParseWatchPid([AppRelaunch.WatchFlag, "-1"]).Should().BeNull();
    }

    // ── The self-update target ────────────────────────────────────────────

    /// <summary>An ordinary restart names no target and comes back as itself.</summary>
    [TestMethod]
    public void An_Ordinary_Relaunch_Has_No_Target()
    {
        AppRelaunch.ParseTarget([]).Should().BeNull();
        AppRelaunch.ParseTarget([AppRelaunch.WatchFlag, "4321"]).Should().BeNull();
    }

    [TestMethod]
    public void A_Self_Update_Relaunch_Carries_The_Slot_Executable()
        => AppRelaunch.ParseTarget([AppRelaunch.WatchFlag, "4321", AppRelaunch.TargetFlag, "/slots/v1/MyApp"])
            .Should().Be("/slots/v1/MyApp");

    /// <summary>A malformed target must read as absent, so the watcher relaunches the build it is running
    /// rather than trying to start "" and leaving the user with nothing.</summary>
    [TestMethod]
    public void A_Malformed_Target_Falls_Back_To_An_Ordinary_Relaunch()
    {
        AppRelaunch.ParseTarget([AppRelaunch.TargetFlag]).Should().BeNull();
        AppRelaunch.ParseTarget([AppRelaunch.TargetFlag, ""]).Should().BeNull();
    }

    /// <summary>
    /// The target is validated while there is still a window on screen to report the failure. Scheduling a
    /// relaunch into a path that doesn't exist would shut the app down and then discover, in a process with no
    /// UI, that there is nothing to start — the user would simply lose their app.
    /// </summary>
    [TestMethod]
    public void Scheduling_A_Relaunch_Into_A_Missing_Executable_Is_Refused()
        => AppRelaunch.ScheduleRelaunch(Path.Combine(_dir, "no-such-build", "MyApp"))
            .Should().BeFalse("the failure must surface before the app commits to shutting down");

    /// <summary>
    /// End to end, through the product's own watcher and a real argv round-trip: a self-update must launch the
    /// STAGED build, not the one doing the watching. Getting this backwards is the failure that looks exactly
    /// like success — the app restarts, comes back, and is still the old version.
    /// </summary>
    [TestMethod]
    public async Task Watcher_Launches_The_Staged_Build_Not_Itself()
    {
        using var stand = StartSleeper(seconds: 2);

        var slot = Path.Combine(_dir, "slots", "abc123-20260812142233");
        Directory.CreateDirectory(slot);
        var marker = Path.Combine(slot, "launched.txt");
        var stagedExe = WriteLauncher(Path.Combine(slot, "app"), marker);

        var watcher = Task.Run(() => AppRelaunch.TryRunWatcher(
            [AppRelaunch.WatchFlag, stand.Id.ToString(), AppRelaunch.TargetFlag, stagedExe],
            settleDelay: TimeSpan.FromMilliseconds(200),
            lockProbe: () => true));

        (await watcher).Should().BeTrue();
        (await WaitForFileAsync(marker, TimeSpan.FromSeconds(25)))
            .Should().BeTrue("the watcher must launch the staged build named on its command line");
    }

    // ── The wait ────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>The whole point: it must not return while the old instance still holds the lock.</summary>
    [TestMethod]
    public async Task WaitForExit_Blocks_Until_The_Process_Exits()
    {
        using var stand = StartSleeper(seconds: 3);
        var sw = Stopwatch.StartNew();

        await Task.Run(() => AppRelaunch.WaitForExit(stand.Id, TimeSpan.FromSeconds(30)));

        sw.Elapsed.Should().BeGreaterThan(TimeSpan.FromSeconds(2),
            "returning early is what makes the replacement hit the single-instance lock");
        stand.HasExited.Should().BeTrue();
    }

    /// <summary>
    /// The case that broke in the field: the watcher waits on its own PARENT, not a child it started. On Unix
    /// <see cref="Process.WaitForExit()"/> only blocks for processes the caller started — against a non-child it
    /// returned in 27ms, so the watcher launched while the old app still held the single-instance lock and the
    /// replacement exited as a secondary. Nothing came back.
    /// </summary>
    [TestMethod]
    public async Task WaitForExit_Waits_For_A_Process_This_One_Did_Not_Start()
    {
        // A grandchild: started by a shell, so it is NOT a child of this test process — the same relationship the
        // watcher has to the app it waits on.
        var pidFile = Path.Combine(_dir, "grandchild.pid");
        using (var launcher = Process.Start(GrandchildLauncher(pidFile))!)
        {
            await launcher.WaitForExitAsync();
        }

        var pid = int.Parse(File.ReadAllText(pidFile).Trim());
        var sw = Stopwatch.StartNew();
        await Task.Run(() => AppRelaunch.WaitForExit(pid, TimeSpan.FromSeconds(30)));

        sw.Elapsed.Should().BeGreaterThan(TimeSpan.FromSeconds(2),
            "returning early against a non-child is exactly what made the restart launch too soon");
    }

    /// <summary>Losing the race — the process exits before the watcher looks — must return immediately, not
    /// throw and not hang.</summary>
    [TestMethod]
    public async Task WaitForExit_Returns_Immediately_For_An_Already_Dead_Process()
    {
        using var stand = StartSleeper(seconds: 0);
        await stand.WaitForExitAsync();
        var deadPid = stand.Id;

        var sw = Stopwatch.StartNew();
        AppRelaunch.WaitForExit(deadPid, TimeSpan.FromSeconds(30));
        sw.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(5));
    }

    /// <summary>It must give up rather than linger forever as an orphan.</summary>
    [TestMethod]
    public async Task WaitForExit_Gives_Up_At_The_Timeout()
    {
        using var stand = StartSleeper(seconds: 30);
        try
        {
            var sw = Stopwatch.StartNew();
            await Task.Run(() => AppRelaunch.WaitForExit(stand.Id, TimeSpan.FromSeconds(1)));
            sw.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(15));
            stand.HasExited.Should().BeFalse("the timeout expired; the process is still running");
        }
        finally
        {
            try { stand.Kill(); } catch { /* already gone */ }
        }
    }

    // ── The instance lock ───────────────────────────────────────────────────────────────────────────────

    /// <summary>Waiting on the old PID is not sufficient: if anything still holds the single-instance lock, the
    /// process we start loses the race and exits, and the app never reappears. That was the observed failure.</summary>
    [TestMethod]
    public void WaitForInstanceLock_Blocks_While_The_Lock_Is_Held()
    {
        var held = true;
        var sw = Stopwatch.StartNew();
        var releaser = Task.Run(async () => { await Task.Delay(1200); held = false; });

        AppRelaunch.WaitForInstanceLock(TimeSpan.FromSeconds(20), isAvailable: () => !held);

        sw.Elapsed.Should().BeGreaterThan(TimeSpan.FromSeconds(1),
            "launching while the lock is held is what makes the replacement exit silently");
        releaser.Wait();
    }

    /// <summary>A free lock must not cost a poll interval.</summary>
    [TestMethod]
    public void WaitForInstanceLock_Returns_At_Once_When_Free()
    {
        var sw = Stopwatch.StartNew();
        AppRelaunch.WaitForInstanceLock(TimeSpan.FromSeconds(20), isAvailable: () => true);
        sw.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(2));
    }

    /// <summary>Never wait forever — launching into a contested lock still beats doing nothing.</summary>
    [TestMethod]
    public void WaitForInstanceLock_Gives_Up_At_The_Timeout()
    {
        var sw = Stopwatch.StartNew();
        AppRelaunch.WaitForInstanceLock(TimeSpan.FromMilliseconds(600), isAvailable: () => false);
        sw.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(10));
    }

    /// <summary>The real probe reports the lock free when no instance is running — otherwise every relaunch would
    /// stall for the full timeout.</summary>
    [TestMethod]
    public void The_Real_Instance_Probe_Reports_Free_When_Nothing_Holds_It()
    {
        SingleInstanceGuard.IsAvailable("AvalloyRelaunchProbe-" + Guid.NewGuid().ToString("n"))
            .Should().BeTrue();
    }

    /// <summary>And held while a guard is alive — a probe that always says "free" would defeat the wait.</summary>
    [TestMethod]
    public void The_Real_Instance_Probe_Reports_Held_While_A_Guard_Lives()
    {
        var id = "AvalloyRelaunchProbe-" + Guid.NewGuid().ToString("n");
        using var guard = SingleInstanceGuard.Acquire(id);
        guard.Should().NotBeNull();
        SingleInstanceGuard.IsAvailable(id).Should().BeFalse();
    }

    // ── Platform launch shape ───────────────────────────────────────────────────────────────────────────

    /// <summary>A macOS bundle launches via Launch Services so the new instance activates; anything else is run
    /// directly. Pure string work, so it is verifiable on every platform.</summary>
    [TestMethod]
    public void Bundle_Detection_Picks_The_App_Not_The_Inner_Binary()
    {
        AppRelaunch.BundleOf("/Applications/MyApp.app/Contents/MacOS/MyApp")
            .Should().Be("/Applications/MyApp.app");
        AppRelaunch.BundleOf("/Users/me/My Apps/MyApp.app/Contents/MacOS/MyApp")
            .Should().Be("/Users/me/My Apps/MyApp.app");
        AppRelaunch.BundleOf(@"C:\Program Files\MyApp\MyApp.exe").Should().BeNull();
        AppRelaunch.BundleOf("/home/me/myapp/MyApp").Should().BeNull();
    }

    // ── End to end ──────────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// The real thing, driving the product's own watcher: it must wait for the stand-in "old instance" and only
    /// then launch. The target lives in a folder with a space, which is what the previous shell-based version got
    /// wrong — and it goes through the same argv round-trip a real launch does.
    /// </summary>
    [TestMethod]
    public async Task Watcher_Waits_For_The_Old_Process_Then_Launches()
    {
        using var stand = StartSleeper(seconds: 3);

        var spaced = Path.Combine(_dir, "a folder with spaces");
        Directory.CreateDirectory(spaced);
        var marker = Path.Combine(spaced, "launched.txt");
        var target = WriteLauncher(Path.Combine(spaced, "app"), marker);

        var watcher = Task.Run(() => AppRelaunch.TryRunWatcher(
            [AppRelaunch.WatchFlag, stand.Id.ToString()],
            exePath: target,
            settleDelay: TimeSpan.FromMilliseconds(200),
            // Not the real instance lock: whether the app happens to be running on this machine has nothing to do
            // with what this test asserts, and depending on it makes the suite fail for unrelated reasons.
            lockProbe: () => true));

        await Task.Delay(1500);
        File.Exists(marker).Should().BeFalse("the replacement must wait for the lock holder to exit");

        await stand.WaitForExitAsync();
        (await WaitForFileAsync(marker, TimeSpan.FromSeconds(25)))
            .Should().BeTrue("the app must be launched once the old process is gone");
        (await watcher).Should().BeTrue("a watcher launch must report that it handled the process");
    }

    /// <summary>
    /// A secondary window must not promise a restart. It never took the lock, so quitting it leaves the real
    /// instance running and nothing appears to happen — the user watches a window vanish and calls it a crash.
    /// <para>Untestable before the extraction: the check read a static field on the consuming app's own App class, so
    /// there was no way to stand in a process that was NOT the holder.</para>
    /// </summary>
    [TestMethod]
    public void A_Secondary_Instance_Refuses_To_Promise_A_Relaunch()
    {
        var previousName = AppRelaunch.AppName;
        var previousProbe = AppRelaunch.HoldsInstanceLock;
        try
        {
            AppRelaunch.AppName = "AvalloyRelaunchProbe-" + Guid.NewGuid().ToString("n");
            using var heldBySomeoneElse = SingleInstanceGuard.Acquire(AppRelaunch.InstanceId);
            heldBySomeoneElse.Should().NotBeNull();
            AppRelaunch.HoldsInstanceLock = () => false;   // ...and it isn't us

            AppRelaunch.ScheduleRelaunch().Should().BeFalse(
                "the caller must tell the user to reopen the app rather than claim it will come back");
        }
        finally
        {
            AppRelaunch.AppName = previousName;
            AppRelaunch.HoldsInstanceLock = previousProbe;
        }
    }

    // ── Surviving the terminal that started us ──────────────────────────────────────────────────────────

    /// <summary>
    /// The relaunched app inherits the dying terminal's process group and gets SIGHUPed as the shell tears it
    /// down. That was a real failure: "launch issued" was the last line in the log and no window ever appeared.
    /// The marker is how the new process knows to refuse the signal.
    /// </summary>
    [TestMethod]
    public void A_Relaunched_Instance_Protects_Itself_From_The_Hangup()
    {
        var previousName = AppRelaunch.AppName;
        try
        {
            AppRelaunch.AppName = "Contoso";
            Environment.SetEnvironmentVariable(AppRelaunch.RelaunchedMarkerName, "1");

            using var guard = AppRelaunch.ProtectIfRelaunched();

            if (OperatingSystem.IsWindows())
                guard.Should().BeNull("Windows has no POSIX signals to refuse");
            else
                guard.Should().NotBeNull("the hangup arrives from the terminal that is disappearing");
        }
        finally
        {
            Environment.SetEnvironmentVariable(AppRelaunch.RelaunchedMarkerName, null);
            AppRelaunch.AppName = previousName;
        }
    }

    /// <summary>An ordinary launch must keep honouring Ctrl-C — the protection is narrow on purpose.</summary>
    [TestMethod]
    public void An_Ordinary_Launch_Registers_No_Signal_Handler()
    {
        var previousName = AppRelaunch.AppName;
        try
        {
            AppRelaunch.AppName = "Contoso";
            Environment.SetEnvironmentVariable(AppRelaunch.RelaunchedMarkerName, null);

            AppRelaunch.ProtectIfRelaunched().Should().BeNull();
        }
        finally { AppRelaunch.AppName = previousName; }
    }

    /// <summary>
    /// The last step can still fail — a staged build deleted between validation and launch, a permissions
    /// change. The watcher must report it and exit, not crash: it is the only process left, and a crash here
    /// is indistinguishable from the restart silently never happening.
    /// </summary>
    [TestMethod]
    public async Task A_Launch_That_Fails_Does_Not_Crash_The_Watcher()
    {
        using var stand = StartSleeper(seconds: 1);
        var notAnExecutable = Path.Combine(_dir, "not-runnable.txt");
        File.WriteAllText(notAnExecutable, "definitely not a program");

        var handled = await Task.Run(() => AppRelaunch.TryRunWatcher(
            [AppRelaunch.WatchFlag, stand.Id.ToString()],
            exePath: notAnExecutable,
            settleDelay: TimeSpan.FromMilliseconds(200),
            lockProbe: () => true));

        handled.Should().BeTrue("it was a watcher and it ran to completion, however badly the launch went");
    }

    // ── Forwarding the app's own argv ───────────────────────────────────────────────────────────────────

    /// <summary>
    /// A relaunch must start the replacement the way this process was started. Avalloy's own
    /// <see cref="Avalloy.Preferences.PreferencesLocation"/> reads <c>--prefs-dir</c> from argv, so dropping it
    /// silently relaunches against a DIFFERENT preferences directory — no error, just other settings.
    /// </summary>
    [TestMethod]
    public void The_Apps_Own_Arguments_Are_Forwarded()
        => AppRelaunch.ForwardableArgs(["/path/to/App", "--prefs-dir", "/custom", "--verbose"])
            .Should().Equal(["--prefs-dir", "/custom", "--verbose"]);

    /// <summary>The machinery's own flags must NOT travel: a replacement started with the watch flag would
    /// immediately become another watcher instead of the app.</summary>
    [TestMethod]
    public void The_Relaunch_Flags_Are_Stripped()
        => AppRelaunch.ForwardableArgs(
                ["/path/to/App", AppRelaunch.WatchFlag, "4321", AppRelaunch.TargetFlag, "/slots/v1/App", "--verbose"])
            .Should().Equal(["--verbose"]);

    /// <summary>A second relaunch has to keep working: the forwarded block is variadic, so everything past it
    /// is already the app's own argv and passes through whole.</summary>
    [TestMethod]
    public void Forwarding_Survives_A_Second_Relaunch()
        => AppRelaunch.ForwardableArgs(
                ["/path/to/App", AppRelaunch.WatchFlag, "4321", AppRelaunch.ArgvFlag, "--prefs-dir", "/custom"])
            .Should().Equal(["--prefs-dir", "/custom"]);

    [TestMethod]
    public void An_Ordinary_Launch_Forwards_Nothing_Extra()
        => AppRelaunch.ForwardableArgs(["/path/to/App"]).Should().BeEmpty();

    [TestMethod]
    public void The_Watcher_Reads_Back_What_It_Was_Given()
        => AppRelaunch.ParseForwardedArgs(
                [AppRelaunch.WatchFlag, "4321", AppRelaunch.ArgvFlag, "--prefs-dir", "/custom", "--verbose"])
            .Should().Equal(["--prefs-dir", "/custom", "--verbose"]);

    [TestMethod]
    public void A_Watcher_Given_No_Argv_Reads_Back_Nothing()
        => AppRelaunch.ParseForwardedArgs([AppRelaunch.WatchFlag, "4321"]).Should().BeEmpty();

    /// <summary>End to end through the real watcher: the launched build must receive the arguments.</summary>
    [TestMethod]
    public async Task The_Watcher_Launches_The_Target_With_The_Forwarded_Arguments()
    {
        using var stand = StartSleeper(seconds: 1);

        var marker = Path.Combine(_dir, "argv.txt");
        var target = WriteArgvRecorder(Path.Combine(_dir, "app"), marker);

        var watcher = Task.Run(() => AppRelaunch.TryRunWatcher(
            [AppRelaunch.WatchFlag, stand.Id.ToString(), AppRelaunch.ArgvFlag, "--prefs-dir", "/custom"],
            exePath: target,
            settleDelay: TimeSpan.FromMilliseconds(200),
            lockProbe: () => true));

        (await watcher).Should().BeTrue();
        (await WaitForFileAsync(marker, TimeSpan.FromSeconds(25))).Should().BeTrue();

        File.ReadAllText(marker).Should().Contain("--prefs-dir").And.Contain("/custom");
    }

    // ── Giving up ───────────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// The watcher must be able to tell "it exited" from "I gave up". Launching after a timeout is the worse
    /// of two bad outcomes: an app with no single-instance guard gets a DUPLICATE, and one with a guard spends
    /// another minute before starting a replacement that loses the lock and exits.
    /// </summary>
    [TestMethod]
    public async Task WaitForExit_Reports_Whether_The_Process_Actually_Went()
    {
        using var stand = StartSleeper(seconds: 30);
        try
        {
            await Task.Run(() => AppRelaunch.WaitForExit(stand.Id, TimeSpan.FromSeconds(1)))
                .ContinueWith(t => t.Result.Should().BeFalse("the process is still running"));
        }
        finally
        {
            try { stand.Kill(); } catch { /* already gone */ }
        }

        using var quick = StartSleeper(seconds: 0);
        (await Task.Run(() => AppRelaunch.WaitForExit(quick.Id, TimeSpan.FromSeconds(30))))
            .Should().BeTrue("it really did exit");
    }

    /// <summary>And the watcher acts on it: a target that never dies must not be replaced.</summary>
    [TestMethod]
    public async Task A_Watcher_Whose_Target_Never_Exits_Launches_Nothing()
    {
        using var stand = StartSleeper(seconds: 30);
        try
        {
            var marker = Path.Combine(_dir, "should-not-exist.txt");
            var target = WriteLauncher(Path.Combine(_dir, "never"), marker);

            // The wait is the product's own 60s constant, so this asserts on the ABORT path by pointing the
            // watcher at a process that outlives its patience only if we let it run that long. Instead the
            // stand-in is killed after the assertion window, and the marker must still be absent.
            var watcher = Task.Run(() => AppRelaunch.TryRunWatcher(
                [AppRelaunch.WatchFlag, stand.Id.ToString()],
                exePath: target,
                settleDelay: TimeSpan.FromMilliseconds(50),
                lockProbe: () => true));

            (await WaitForFileAsync(marker, TimeSpan.FromSeconds(3))).Should().BeFalse(
                "the target is still alive, so nothing may be launched yet");

            stand.Kill();
            await watcher;
        }
        finally
        {
            try { stand.Kill(); } catch { /* already gone */ }
        }
    }

    // ── App identity (the extraction seam) ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Four things must agree on the app's name — the single-instance id, the watcher's log directory, the log
    /// FILE, and the environment marker a relaunched instance reads. They are derived from one property rather
    /// than stated four times, because every one of them fails SILENTLY when it disagrees: a mismatched instance
    /// id simply never collides, and a mismatched marker just means the relaunched app doesn't protect itself.
    /// </summary>
    [TestMethod]
    public void The_Instance_Id_Follows_The_App_Name()
    {
        var previous = AppRelaunch.AppName;
        try
        {
            AppRelaunch.AppName = "Contoso";
            AppRelaunch.InstanceId.Should().Be("Contoso");
        }
        finally { AppRelaunch.AppName = previous; }
    }

    [TestMethod]
    public void The_Default_App_Name_Is_Neutral()
        => AppRelaunch.AppName.Should().Be("App",
            "an app that never sets it still gets a working relaunch, just an anonymous one");

    /// <summary>The marker is an environment variable name, so it must survive an app name that isn't one.</summary>
    [TestMethod]
    [DataRow("Sample", "SAMPLE_RELAUNCHED")]
    [DataRow("My App", "MY_APP_RELAUNCHED")]
    [DataRow("a.b-c", "A_B_C_RELAUNCHED")]
    public void The_Relaunch_Marker_Is_A_Legal_Variable_Name(string appName, string expected)
    {
        var previous = AppRelaunch.AppName;
        try
        {
            AppRelaunch.AppName = appName;
            AppRelaunch.RelaunchedMarkerName.Should().Be(expected);
        }
        finally { AppRelaunch.AppName = previous; }
    }

    /// <summary>An unset probe must not make every relaunch refuse: an app with no single-instance guard has no
    /// secondary case to protect against, and refusing would leave it unable to restart at all.</summary>
    [TestMethod]
    public void With_No_Lock_Probe_The_Secondary_Refusal_Is_Skipped()
    {
        var previous = AppRelaunch.HoldsInstanceLock;
        try
        {
            AppRelaunch.HoldsInstanceLock = null;
            AppRelaunch.OtherInstanceIsRunning().Should().BeFalse();
        }
        finally { AppRelaunch.HoldsInstanceLock = previous; }
    }

    /// <summary>
    /// The question the lock file cannot answer. "Held" is the normal state during our OWN restart — we are the
    /// holder — so without the probe the ordinary case would be indistinguishable from being a secondary window,
    /// and every relaunch would refuse.
    /// </summary>
    [TestMethod]
    public void We_Are_Not_A_Secondary_When_We_Hold_The_Lock()
    {
        var previousName = AppRelaunch.AppName;
        var previousProbe = AppRelaunch.HoldsInstanceLock;
        try
        {
            AppRelaunch.AppName = "AvalloyRelaunchProbe-" + Guid.NewGuid().ToString("n");
            using var guard = SingleInstanceGuard.Acquire(AppRelaunch.InstanceId);
            guard.Should().NotBeNull("the test must actually hold the lock for this to mean anything");

            AppRelaunch.HoldsInstanceLock = () => true;
            AppRelaunch.OtherInstanceIsRunning().Should().BeFalse("the holder is us");

            AppRelaunch.HoldsInstanceLock = () => false;
            AppRelaunch.OtherInstanceIsRunning().Should().BeTrue(
                "held by somebody who isn't us means this process is a secondary");
        }
        finally
        {
            AppRelaunch.AppName = previousName;
            AppRelaunch.HoldsInstanceLock = previousProbe;
        }
    }

    /// <summary>A free lock is nobody else's, whatever the probe says.</summary>
    [TestMethod]
    public void A_Free_Lock_Is_Never_Somebody_Else_Holding_It()
    {
        var previousName = AppRelaunch.AppName;
        var previousProbe = AppRelaunch.HoldsInstanceLock;
        try
        {
            AppRelaunch.AppName = "AvalloyRelaunchProbe-" + Guid.NewGuid().ToString("n");
            AppRelaunch.HoldsInstanceLock = () => false;
            AppRelaunch.OtherInstanceIsRunning().Should().BeFalse();
        }
        finally
        {
            AppRelaunch.AppName = previousName;
            AppRelaunch.HoldsInstanceLock = previousProbe;
        }
    }

    // ── helpers ─────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Start a process that outlives its launcher, and write its pid to <paramref name="pidFile"/> — a
    /// GRANDCHILD of this test, which is the whole point: it reproduces the watcher's relationship to the app
    /// it waits on, where Process.WaitForExit is unavailable because we did not start it.
    ///
    /// <para>This hardcoded <c>/bin/sh</c> until 2026-08-12, so it threw Win32Exception on Windows the first
    /// time these tests ran there. Windows has no shell idiom for "background it and tell me the pid", hence
    /// Start-Process -PassThru.</para>
    /// </summary>
    private static ProcessStartInfo GrandchildLauncher(string pidFile)
    {
        var psi = OperatingSystem.IsWindows()
            ? new ProcessStartInfo("powershell")
            {
                ArgumentList =
                {
                    "-NoProfile", "-Command",
                    "$p = Start-Process -FilePath powershell -ArgumentList "
                    + "'-NoProfile','-Command','Start-Sleep -Seconds 4' -PassThru -WindowStyle Hidden; "
                    + $"Set-Content -LiteralPath '{pidFile.Replace("'", "''")}' -Value $p.Id",
                },
            }
            : new ProcessStartInfo("/bin/sh") { ArgumentList = { "-c", $"sleep 4 & echo $! > '{pidFile}'" } };

        psi.UseShellExecute = false;
        psi.CreateNoWindow = true;
        return psi;
    }

    /// <summary>Not Windows' <c>timeout</c>: it refuses to run when stdin is redirected and exits at once, so
    /// what these tests saw depended on how the test run itself was launched. <c>ping</c> reads no input, and
    /// waits a second between its N+1 echoes.</summary>
    private static Process StartSleeper(int seconds)
    {
        var psi = OperatingSystem.IsWindows()
            ? new ProcessStartInfo("cmd.exe") { ArgumentList = { "/c", $"ping -n {seconds + 1} 127.0.0.1 >nul 2>&1" } }
            : new ProcessStartInfo("/bin/sh") { ArgumentList = { "-c", $"sleep {seconds}" } };
        psi.UseShellExecute = false;
        psi.CreateNoWindow = true;
        return Process.Start(psi)!;
    }

    /// <summary>A tiny executable that records the arguments it was given — the observable proof that argv
    /// survived the round trip through the watcher.</summary>
    private static string WriteArgvRecorder(string pathNoExtension, string marker)
    {
        if (OperatingSystem.IsWindows())
        {
            var bat = pathNoExtension + ".bat";
            File.WriteAllText(bat, $"@echo %* > \"{marker}\"\r\n");
            return bat;
        }

        var sh = pathNoExtension + ".sh";
        File.WriteAllText(sh, "#!/bin/sh\necho \"$@\" > '" + marker.Replace("'", "'\\''") + "'\n");
        File.SetUnixFileMode(sh, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        return sh;
    }

    /// <summary>A tiny executable that touches <paramref name="marker"/> — the observable proof of a launch.</summary>
    private static string WriteLauncher(string pathNoExtension, string marker)
    {
        if (OperatingSystem.IsWindows())
        {
            var bat = pathNoExtension + ".bat";
            File.WriteAllText(bat, $"@echo launched > \"{marker}\"\r\n");
            return bat;
        }

        var sh = pathNoExtension + ".sh";
        File.WriteAllText(sh, "#!/bin/sh\necho launched > '" + marker.Replace("'", "'\\''") + "'\n");
        File.SetUnixFileMode(sh, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        return sh;
    }

    private static async Task<bool> WaitForFileAsync(string path, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (File.Exists(path)) return true;
            await Task.Delay(200);
        }

        return false;
    }
}
