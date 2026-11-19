using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Avalloy.Services;

/// <summary>
/// Quit and come back. Used where a setting can only take effect at boot — a storage backend read once during
/// startup, a runtime flag baked in at launch — and leaving the user staring at the desktop is a poor hand-off.
///
/// <para><b>Why a waiting helper at all.</b> An app that enforces one instance per user (see
/// <see cref="SingleInstanceGuard"/>) cannot simply spawn its replacement: the new process finds the lock still
/// held, reports "already running", and exits — leaving nothing running at all. So something must outlive us,
/// watch for this process to disappear, and only then launch. The OS releases the lock on exit, including on a
/// crash. A fixed sleep would be a guess: shutdown can take seconds when there are subprocesses to close.</para>
///
/// <para><b>Why the app watches itself.</b> The first version shelled out — <c>/bin/sh -c "…"</c> on Unix,
/// <c>cmd /c "…"</c> on Windows — and shipped broken: the script went through <c>ProcessStartInfo.Arguments</c>,
/// which is re-parsed with Windows quoting rules, so the quotes around the executable path terminated the outer
/// quote and the shell got a truncated command. It still "started", so the failure was silent. Fixing the quoting
/// would have left two shell dialects to keep correct, and <c>cmd.exe</c>'s rules differ from the ones
/// <see cref="ProcessStartInfo.ArgumentList"/> escapes for — so the Windows half would have stayed a guess. There
/// is no shell now: we re-launch our OWN executable with <see cref="WatchFlag"/>, and that instance does the
/// waiting in C#. One code path, no quoting, same behaviour on macOS, Windows and Linux.</para>
///
/// <para><b>Wiring.</b> Set <see cref="AppName"/> and <see cref="HoldsInstanceLock"/> once at startup, before
/// anything else here runs — <see cref="TryRunWatcher"/> is called from the very first lines of <c>Main</c>, so
/// "before Avalonia" is not early enough on its own. Then call <see cref="TryRunWatcher"/> first thing (returning
/// immediately if it reports true), and <see cref="ProtectIfRelaunched"/> right after.</para>
/// </summary>
public static class AppRelaunch
{
    /// <summary>Marks a process whose only job is to wait for another to exit and then start the app.</summary>
    public const string WatchFlag = "--relaunch-after";

    /// <summary>
    /// Names a DIFFERENT executable for the watcher to launch — how a self-update restarts into a staged build.
    /// The watcher is still this build, deliberately: it is known to
    /// work, and it is the one process guaranteed to still exist while the app it is replacing shuts down.
    /// Absent, the watcher relaunches the executable it is itself running, which is the ordinary restart.
    /// </summary>
    public const string TargetFlag = "--relaunch-into";

    /// <summary>
    /// Everything AFTER this flag is the original application's own argv, to be handed to the relaunched
    /// process verbatim. Variadic and therefore always last.
    ///
    /// <para>Without it a relaunch silently loses every command-line option the app was started with. That is
    /// not hypothetical for this library: <see cref="Preferences.PreferencesLocation.Resolve"/> reads
    /// <c>--prefs-dir</c> from argv, so an app launched against a custom preferences directory came back
    /// against its default one — with no error, and a different set of settings.</para>
    /// </summary>
    public const string ArgvFlag = "--relaunch-argv";

    /// <summary>
    /// This app's name. Drives the single-instance id, the watcher's log location, and the environment marker a
    /// relaunched instance looks for — all of which must match what the app itself uses, so they are derived from
    /// one value rather than repeated. Set it once at startup, ahead of every other call here.
    /// </summary>
    public static string AppName { get; set; } = "App";

    /// <summary>
    /// Does THIS process hold the single-instance lock? Supplied by the app, because only the app knows whether
    /// its <see cref="SingleInstanceGuard.Acquire"/> succeeded.
    ///
    /// <para>It answers a question the lock file cannot: "held" tells you somebody has it, not whether that
    /// somebody is you. Without this, the ordinary restart — where we ARE the holder — would look identical to
    /// being a secondary window, and <see cref="ScheduleRelaunch"/> would refuse every time. Left unset, the
    /// refusal is skipped rather than guessed at: an app with no single-instance guard has no secondary case.</para>
    /// </summary>
    public static Func<bool>? HoldsInstanceLock { get; set; }

    /// <summary>Environment marker set on a relaunched instance, so it knows to ignore the hangup coming from the
    /// terminal that is in the middle of disappearing. Derived from <see cref="AppName"/> so two Avalloy apps on
    /// one machine cannot read each other's.</summary>
    internal static string RelaunchedMarkerName =>
        new string([.. AppName.ToUpperInvariant().Select(c => char.IsAsciiLetterOrDigit(c) ? c : '_')]) + "_RELAUNCHED";

    /// <summary>
    /// Called at startup by a process that MAY have been started by a watcher. When it was, the launching terminal
    /// is already going away and will SIGHUP this process group; a GUI app has no business dying because the shell
    /// that started it closed. Returns null (and registers nothing) for an ordinary launch, where Ctrl-C and
    /// friends must keep working normally.
    /// </summary>
    public static IDisposable? ProtectIfRelaunched()
        => Environment.GetEnvironmentVariable(RelaunchedMarkerName) == "1" ? IgnoreHangup() : null;

    /// <summary>How long the watcher waits before giving up. A shutdown still hanging after this has bigger
    /// problems, and the watcher must not become an immortal process.</summary>
    private static readonly TimeSpan WatchTimeout = TimeSpan.FromSeconds(60);

    /// <summary>
    /// Schedule a relaunch, then let the caller shut down normally. Returns false when the executable path can't
    /// be resolved or the watcher won't start — the caller should then tell the user to reopen the app themselves
    /// rather than claiming it will come back.
    /// </summary>
    /// <param name="targetExe">Come back as a DIFFERENT executable — a promoted self-update slot, or the
    /// installed build a slot is being abandoned for. Null = relaunch this one, the ordinary restart. The
    /// target is validated here rather than in the watcher: a path that doesn't exist must fail while there is
    /// still a window on screen to say so, not sixty seconds later in a process with no UI.</param>
    public static bool ScheduleRelaunch(string? targetExe = null)
    {
        var exe = Environment.ProcessPath;
        Log($"ScheduleRelaunch: exe={exe ?? "(null)"} target={targetExe ?? "(self)"} pid={Environment.ProcessId}");
        if (string.IsNullOrEmpty(exe))
        {
            Log("ScheduleRelaunch: ABORT — no ProcessPath");
            return false;
        }

        if (targetExe is { Length: > 0 } && !File.Exists(targetExe))
        {
            Log($"ScheduleRelaunch: ABORT — target does not exist: {targetExe}");
            return false;
        }

        // Another instance already open? Then this process is a secondary that never took the lock, and quitting
        // it leaves that one running — nothing would appear to "restart". Say so rather than promise one.
        if (OtherInstanceIsRunning())
        {
            Log("ScheduleRelaunch: ABORT — another instance holds the lock");
            return false;
        }

        try
        {
            var psi = new ProcessStartInfo(exe) { UseShellExecute = false, CreateNoWindow = true };
            psi.ArgumentList.Add(WatchFlag);
            psi.ArgumentList.Add(Environment.ProcessId.ToString());
            if (targetExe is { Length: > 0 })
            {
                psi.ArgumentList.Add(TargetFlag);
                psi.ArgumentList.Add(targetExe);
            }

            // Our own argv, forwarded through the watcher so the replacement starts the way we did. Last,
            // because it is variadic.
            var forward = ForwardableArgs(Environment.GetCommandLineArgs());
            if (forward.Length > 0)
            {
                psi.ArgumentList.Add(ArgvFlag);
                foreach (var arg in forward) psi.ArgumentList.Add(arg);
            }

            var started = Process.Start(psi);
            Log($"ScheduleRelaunch: watcher pid={started?.Id.ToString() ?? "(null)"}");
            return started is not null;
        }
        catch (Exception ex)
        {
            Log("ScheduleRelaunch: FAILED — " + ex.Message);
            return false;
        }
    }

    /// <summary>
    /// Called first thing in <c>Main</c>. When this process was started as a watcher, wait for the old instance to
    /// exit, launch the app properly, and report true so the caller returns WITHOUT touching Avalonia, the engine,
    /// or — critically — the single-instance lock a watcher must never take.
    /// </summary>
    /// <param name="args">The process arguments.</param>
    /// <param name="exePath">What to launch once the wait completes. Defaults to this executable — the only
    /// sensible production value; tests point it at a stand-in so they exercise this method rather than a copy
    /// of its logic.</param>
    /// <param name="settleDelay">Pause after the old process exits, before launching. Tests shorten it.</param>
    /// <param name="lockProbe">How to ask whether the single-instance lock is free. Defaults to the real guard;
    /// tests inject one so they exercise the wait without depending on whether the app happens to be running on
    /// the developer's machine.</param>
    public static bool TryRunWatcher(
        string[] args, string? exePath = null, TimeSpan? settleDelay = null, Func<bool>? lockProbe = null)
    {
        var pid = ParseWatchPid(args);
        if (pid is null)
        {
            return false;
        }

        // The watcher inherits the launching app's process group. When that app exits from a terminal session —
        // `rig run` / `dotnet run` — the shell tears the group down and SIGHUPs everything in it, killing the
        // watcher before it can do anything. Observed exactly that: "watcher pid=…" logged, then silence. Refusing
        // the signal keeps it alive long enough to finish its one job. SIGINT/SIGTERM likewise: a Ctrl-C aimed at
        // the foreground app must not cancel the restart it just asked for.
        using var hangup = IgnoreShutdownSignals();

        WatcherLog($"waiting for pid {pid.Value} to exit");
        if (!WaitForExit(pid.Value, WatchTimeout))
        {
            // Still alive after the full timeout. Launching now is the worse of the two bad outcomes: an app
            // with no single-instance guard gets a DUPLICATE, and one with a guard spends another minute
            // waiting before starting a replacement that loses the lock and exits. Either way nothing useful
            // appears — and the user still has their (wedged) original, which is more than a duplicate gives
            // them. Report it and stop.
            WatcherLog($"ABORT — pid {pid.Value} still running after {WatchTimeout.TotalSeconds:F0}s");
            return true;   // this WAS a watcher and it handled its argv; it just has nothing safe to launch
        }

        WatcherLog("target exited");

        // A moment past exit: the OS releases the lock as the process dies, but file handles and WAL sidecars
        // settle fractionally later.
        Thread.Sleep(settleDelay ?? TimeSpan.FromSeconds(1));

        // Waiting on the PID alone is not enough. If ANY instance still holds the single-instance lock — the one
        // we watched shutting down slowly, or an unrelated window the user left open — the process we start will
        // lose the race, signal a primary that has no window to show, and exit. The app then simply never
        // reappears, which is exactly the failure this whole helper exists to avoid. So wait for the lock itself
        // to be free, not merely for one PID to vanish.
        WaitForInstanceLock(WatchTimeout, lockProbe);
        WatcherLog("instance lock free; launching");

        try
        {
            // Precedence: the test's stand-in, then a self-update target from the command line, then ourselves.
            var target = exePath ?? ParseTarget(args) ?? Environment.ProcessPath;
            var forward = ParseForwardedArgs(args);
            WatcherLog($"launching {target ?? "(null)"}{(forward.Length > 0 ? $" with {forward.Length} arg(s)" : "")}");
            Launch(target, forward);
            WatcherLog("launch issued");
        }
        catch (Exception ex)
        {
            WatcherLog("launch FAILED — " + ex.Message);
        }

        return true;
    }

    /// <summary>The PID to wait for, or null when this isn't a watcher process.</summary>
    internal static int? ParseWatchPid(string[] args)
    {
        for (var i = 0; i < args.Length - 1; i++)
        {
            if (args[i] == WatchFlag && int.TryParse(args[i + 1], out var pid) && pid > 0)
            {
                return pid;
            }
        }

        return null;
    }

    /// <summary>
    /// This process's own arguments, minus the ones that belong to the relaunch machinery — what a replacement
    /// should be started with. Takes the full <see cref="Environment.GetCommandLineArgs"/> array (element 0 is
    /// the executable) so it can be tested without a process.
    /// <para>The machinery's flags are stripped rather than passed on: a watcher that forwarded
    /// <see cref="WatchFlag"/> would produce a replacement that immediately became another watcher.</para>
    /// </summary>
    internal static string[] ForwardableArgs(string[] commandLineArgs)
    {
        var result = new List<string>();

        for (var i = 1; i < commandLineArgs.Length; i++)
        {
            var arg = commandLineArgs[i];

            // Variadic and always last: everything after it is already the app's own argv, so take the rest
            // and stop. This is what makes forwarding survive a SECOND relaunch.
            if (arg == ArgvFlag)
            {
                for (var j = i + 1; j < commandLineArgs.Length; j++) result.Add(commandLineArgs[j]);
                break;
            }

            // Flag + value pairs.
            if (arg == WatchFlag || arg == TargetFlag) { i++; continue; }

            result.Add(arg);
        }

        return [.. result];
    }

    /// <summary>The argv the watcher was told to hand on, or empty when it was not given any.</summary>
    internal static string[] ParseForwardedArgs(string[] args)
    {
        for (var i = 0; i < args.Length; i++)
        {
            if (args[i] != ArgvFlag) continue;
            return args[(i + 1)..];
        }

        return [];
    }

    /// <summary>The executable a self-update wants relaunched, or null for the ordinary "come back as
    /// yourself" restart. An empty value is treated as absent rather than as an unlaunchable path.</summary>
    internal static string? ParseTarget(string[] args)
    {
        for (var i = 0; i < args.Length - 1; i++)
        {
            if (args[i] == TargetFlag && args[i + 1].Length > 0)
            {
                return args[i + 1];
            }
        }

        return null;
    }

    /// <summary>Block until <paramref name="pid"/> is gone or the timeout elapses. A PID that has already exited
    /// throws from <see cref="Process.GetProcessById(int)"/> and counts as gone — the common race, and exactly
    /// the state we were waiting for.
    /// <para>Returns FALSE if it gave up while the process was still alive. That distinction is the whole point
    /// of the return value: the two outcomes are opposite instructions to the caller, and a void return made
    /// them indistinguishable — the watcher logged "target exited" and launched either way.</para></summary>
    internal static bool WaitForExit(int pid, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (!IsRunning(pid)) return true;
            Thread.Sleep(200);
        }

        return !IsRunning(pid);
    }

    /// <summary>
    /// Whether <paramref name="pid"/> is still alive. Deliberately a poll rather than
    /// <see cref="Process.WaitForExit()"/>: on Unix that only blocks for processes the caller STARTED, and the
    /// relationship here is inverted — the process we wait on is our own parent. It returned after 27ms against a
    /// live app, so the watcher launched into a still-running instance, lost the single-instance lock, and the
    /// replacement exited. That was the restart failing in the field.
    /// </summary>
    private static bool IsRunning(int pid)
    {
        try
        {
            using var proc = Process.GetProcessById(pid);
            return !proc.HasExited;
        }
        catch (ArgumentException)
        {
            return false;   // no such process
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    /// <summary>
    /// Start the app the way the OS expects. On macOS the executable sits inside the bundle
    /// (<c>MyApp.app/Contents/MacOS/MyApp</c>); running it directly works but bypasses Launch Services, so the new
    /// instance can come up unactivated. <c>open</c> is a plain executable taking ordinary argv, so this still
    /// involves no shell.
    /// </summary>
    private static void Launch(string? exe, string[] forwardArgs)
    {
        if (string.IsNullOrEmpty(exe)) return;

        if (BundleOf(exe) is { } bundle)
        {
            var openPsi = new ProcessStartInfo("/usr/bin/open") { UseShellExecute = false };
            openPsi.ArgumentList.Add("-n");
            openPsi.ArgumentList.Add(bundle);
            if (forwardArgs.Length > 0)
            {
                // `open --args …` hands the rest to the app rather than to open itself. Always last.
                openPsi.ArgumentList.Add("--args");
                foreach (var arg in forwardArgs) openPsi.ArgumentList.Add(arg);
            }

            Process.Start(openPsi);   // Launch Services starts it detached; no group to inherit
            return;
        }

        // A dev/unbundled run: the new process inherits OUR process group, which is the dying terminal's. It gets
        // SIGHUPed the moment the shell tears down — the watcher survives that, the app it starts does not, which
        // is why "launch issued" was the last thing in the log and no window ever appeared. Mark it so it can
        // protect itself the instant it starts.
        var psi = new ProcessStartInfo(exe) { UseShellExecute = false };
        foreach (var arg in forwardArgs) psi.ArgumentList.Add(arg);
        psi.Environment[RelaunchedMarkerName] = "1";
        Process.Start(psi);
    }

    /// <summary>
    /// The single-instance id. Must match what the app passed to <see cref="SingleInstanceGuard.Acquire"/> —
    /// hence derived from <see cref="AppName"/> rather than stated twice, because two spellings of the same id
    /// fail silently by simply never colliding.
    /// </summary>
    internal static string InstanceId => AppName;

    /// <summary>
    /// True when an instance OTHER than this process holds the single-instance lock. We are normally the holder,
    /// so the probe on its own can only report "free" — which is why the holder question needs
    /// <see cref="HoldsInstanceLock"/> to answer.
    /// </summary>
    internal static bool OtherInstanceIsRunning()
    {
        try { return !SingleInstanceGuard.IsAvailable(InstanceId) && HoldsInstanceLock?.Invoke() is false; }
        catch { return false; }   // can't tell → attempt the relaunch rather than refuse it
    }

    /// <summary>Poll until no instance holds the lock, or the timeout elapses. Launching anyway on timeout is
    /// deliberate: a visible "already running" hand-off beats doing nothing at all.</summary>
    internal static void WaitForInstanceLock(TimeSpan timeout, Func<bool>? isAvailable = null)
    {
        var probe = isAvailable ?? (() => SingleInstanceGuard.IsAvailable(InstanceId));
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (probe()) return;
            Thread.Sleep(250);
        }
    }

    /// <summary>
    /// Refuse the signals that tear down a terminal's process group, so the watcher outlives the shell that
    /// started the app. No-op on Windows, which has no POSIX signals. The registration is disposed when the
    /// watcher finishes — it is deliberately narrow, covering only this one waiting process.
    /// </summary>
    private static IDisposable? IgnoreShutdownSignals()
        => Register(PosixSignal.SIGHUP, PosixSignal.SIGINT, PosixSignal.SIGTERM);

    /// <summary>Only the hangup: a relaunched app must still honour Ctrl-C and a deliberate kill.</summary>
    private static IDisposable? IgnoreHangup() => Register(PosixSignal.SIGHUP);

    private static IDisposable? Register(params PosixSignal[] signals)
    {
        if (OperatingSystem.IsWindows()) return null;

        try
        {
            var reg = new List<IDisposable>();
            foreach (var sig in signals)
            {
                reg.Add(PosixSignalRegistration.Create(sig, ctx => ctx.Cancel = true));
            }

            return new Registrations(reg);
        }
        catch
        {
            return null;   // best effort: better to race the signal than not to run at all
        }
    }

    private sealed class Registrations(List<IDisposable> items) : IDisposable
    {
        public void Dispose()
        {
            foreach (var i in items)
            {
                try { i.Dispose(); } catch { /* shutting down anyway */ }
            }
        }
    }

    /// <summary>
    /// The watcher runs BEFORE Avalonia and Avalloy logging exist, so anything it sends to
    /// <see cref="Trace"/> goes nowhere — which is why the first instrumented run recorded the watcher starting and
    /// then fell silent. It writes its own file instead, next to the app log where anyone would look.
    /// </summary>
    private static void WatcherLog(string message)
    {
        Trace.TraceInformation("[relaunch] watcher: " + message);   // harmless if nothing is listening
        try
        {
            var dir = AppPaths.ForApp(AppName).LogsDir;
            Directory.CreateDirectory(dir);
            File.AppendAllText(
                Path.Combine(dir, $"{AppName}.relaunch.log"),
                $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [pid {Environment.ProcessId}] {message}{Environment.NewLine}");
        }
        catch { /* diagnostics must never break the restart */ }
    }

    /// <summary>
    /// Relaunch is a sequence of invisible steps spread across two processes, and every failure so far has been
    /// silent — the app just didn't come back. Trace goes to the console AND Avalloy's rolling log (see
    /// <see cref="TraceLoggerBridge"/>), so a failed restart can be diagnosed from evidence instead of guesswork.
    /// </summary>
    private static void Log(string message) => Trace.TraceInformation("[relaunch] " + message);

    /// <summary>The <c>.app</c> bundle containing <paramref name="exe"/>, or null when it isn't inside one (a dev
    /// run, or any non-macOS platform).</summary>
    internal static string? BundleOf(string exe)
    {
        const string marker = "/Contents/MacOS/";
        var i = exe.IndexOf(marker, StringComparison.Ordinal);
        return i < 0 ? null : exe[..i];
    }
}
