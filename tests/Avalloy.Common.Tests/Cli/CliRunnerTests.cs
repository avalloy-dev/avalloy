using AwesomeAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Avalloy.Cli.Tests;

/// <summary>
/// The runner's contract. Everything here provokes a real child into misbehaving rather than faking
/// it, because the failures worth guarding against are the ones a mock cannot reproduce: a full pipe
/// buffer, a process that outlives its budget, a tree that survives the kill. Uses <c>/bin/sh</c> for
/// the misbehaving cases and skips itself on Windows.
/// </summary>
[TestClass]
public class CliRunnerTests
{
    private static bool Posix => !OperatingSystem.IsWindows();

    private static Task<CliResult> ShAsync(string script, TimeSpan? timeout = null, CancellationToken ct = default) =>
        CliRunner.RunAsync("/bin/sh", null, ["-c", script], ct, timeout);

    [TestMethod]
    public async Task Captures_stdout_and_reports_success()
    {
        if (!Posix) { Assert.Inconclusive("needs a POSIX shell"); return; }

        var r = await ShAsync("echo hello");

        r.Ok.Should().BeTrue();
        r.Failure.Should().Be(CliFailure.None);
        r.Stdout.Trim().Should().Be("hello");
    }

    [TestMethod]
    public async Task A_nonzero_exit_reports_ExitCode_and_keeps_stderr()
    {
        if (!Posix) { Assert.Inconclusive("needs a POSIX shell"); return; }

        var r = await ShAsync("echo 'it went wrong' >&2; exit 3");

        r.Ok.Should().BeFalse();
        r.Failure.Should().Be(CliFailure.ExitCode, "it ran to completion — it just refused");
        r.Stderr.Should().Contain("it went wrong");
        r.Message("fallback").Should().Contain("it went wrong");
    }

    // Launch failure is its own answer: "the tool isn't installed" is a state callers act on
    // (fall back, show onboarding), not a fault to report as a crash.
    [TestMethod]
    public async Task A_missing_executable_reports_NotStarted_rather_than_throwing()
    {
        var r = await CliRunner.RunAsync("avalloy-no-such-tool-" + Guid.NewGuid().ToString("n"), null, ["--version"]);

        r.Ok.Should().BeFalse();
        r.Failure.Should().Be(CliFailure.NotStarted);
    }

    // THE deadlock regression. A child that fills the ~64KB stderr buffer blocks on the write, so it
    // never exits and never closes stdout; a runner that awaits stdout to completion before reading
    // stderr waits forever. Both pipes are drained concurrently, so this returns — with all 200KB.
    [TestMethod]
    public async Task A_child_flooding_stderr_does_not_deadlock()
    {
        if (!Posix) { Assert.Inconclusive("needs a POSIX shell"); return; }

        var run = ShAsync(
            "i=0; while [ $i -lt 3200 ]; do " +
            "echo '................................................................' >&2; " +
            "i=$((i + 1)); done; exit 1",
            TimeSpan.FromSeconds(30));
        var finished = await Task.WhenAny(run, Task.Delay(TimeSpan.FromSeconds(20)));

        finished.Should().BeSameAs(run, "a full stderr pipe must not wedge the runner");
        var r = await run;
        r.Failure.Should().Be(CliFailure.ExitCode, "it finished and refused — it did not time out");
        r.Stderr.Length.Should().BeGreaterThan(100_000, "every byte is captured, not just the first pipeful");
    }

    // Same hazard on the other stream: a child that floods stdout while we wait on stderr.
    [TestMethod]
    public async Task A_child_flooding_stdout_does_not_deadlock()
    {
        if (!Posix) { Assert.Inconclusive("needs a POSIX shell"); return; }

        var r = await ShAsync(
            "i=0; while [ $i -lt 3200 ]; do " +
            "echo '................................................................'; " +
            "i=$((i + 1)); done",
            TimeSpan.FromSeconds(30));

        r.Ok.Should().BeTrue();
        r.Stdout.Length.Should().BeGreaterThan(100_000);
    }

    [TestMethod]
    public async Task An_overrunning_child_reports_TimedOut_within_its_box()
    {
        if (!Posix) { Assert.Inconclusive("needs a POSIX shell"); return; }

        var started = System.Diagnostics.Stopwatch.StartNew();
        var r = await ShAsync("sleep 30", TimeSpan.FromMilliseconds(500));
        started.Stop();

        r.Failure.Should().Be(CliFailure.TimedOut);
        r.TimedOut.Should().BeTrue();
        started.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(10), "the box is the point");
    }

    // A timeout that returns while the child keeps running leaks a process per call. The whole TREE
    // is killed, so the grandchild never reaches the line past its sleep.
    [TestMethod]
    public async Task A_timed_out_child_tree_is_actually_killed()
    {
        if (!Posix) { Assert.Inconclusive("needs a POSIX shell"); return; }

        var marker = Path.Combine(Path.GetTempPath(), "avalloy-cli-" + Guid.NewGuid().ToString("n"));
        try
        {
            var r = await ShAsync($"(sleep 3; echo alive > '{marker}') & wait", TimeSpan.FromMilliseconds(300));
            r.Failure.Should().Be(CliFailure.TimedOut);

            await Task.Delay(TimeSpan.FromSeconds(5));   // well past the grandchild's own sleep
            File.Exists(marker).Should().BeFalse("the kill must reach the whole tree, not just the shell");
        }
        finally
        {
            try { File.Delete(marker); } catch { /* best effort */ }
        }
    }

    // Cancellation and timeout arrive through the same linked token; reporting one as the other
    // sends whoever reads the log hunting a hang that never happened.
    [TestMethod]
    public async Task Caller_cancellation_reports_Canceled_not_TimedOut()
    {
        if (!Posix) { Assert.Inconclusive("needs a POSIX shell"); return; }

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));

        var r = await ShAsync("sleep 30", TimeSpan.FromMinutes(5), cts.Token);

        r.Failure.Should().Be(CliFailure.Canceled);
        r.Canceled.Should().BeTrue();
        r.TimedOut.Should().BeFalse();
    }

    // No shell, so nothing to quote and nothing to inject: arguments reach the child as given.
    [TestMethod]
    public async Task Arguments_pass_through_verbatim_without_a_shell()
    {
        if (!Posix) { Assert.Inconclusive("needs a POSIX shell"); return; }

        string[] awkward = ["a b", "'q'", "\"d\"", "ü", "$(echo pwned)", "; rm -rf /"];

        var r = await CliRunner.RunAsync("/bin/sh", null, ["-c", "printf '%s\\n' \"$@\"", "--", .. awkward]);

        r.Ok.Should().BeTrue();
        r.Stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries).Should().Equal(awkward);
    }

    // A reader blocks until EOF, so a runner that writes stdin without closing it hangs; one that
    // writes inline before draining hangs on a payload past the pipe buffer.
    [TestMethod]
    public async Task Stdin_is_written_and_closed_even_when_large()
    {
        if (!Posix) { Assert.Inconclusive("needs a POSIX shell"); return; }

        var payload = string.Join('\n', Enumerable.Range(0, 20_000).Select(i => $"line{i}"));

        var r = await CliRunner.RunAsync("/bin/sh", null, ["-c", "wc -l"], stdin: payload);

        r.Ok.Should().BeTrue();
        int.Parse(r.Stdout.Trim()).Should().Be(19_999, "cat saw the whole payload and then EOF");
    }

    [TestMethod]
    public async Task Environment_entries_reach_the_child_and_layer_over_the_inherited_set()
    {
        if (!Posix) { Assert.Inconclusive("needs a POSIX shell"); return; }

        var r = await CliRunner.RunAsync("/bin/sh", null, ["-c", "echo mine=$AVALLOY_PROBE home=${HOME:+set}"],
            env: new Dictionary<string, string> { ["AVALLOY_PROBE"] = "yes" });

        r.Stdout.Should().Contain("mine=yes");
        r.Stdout.Should().Contain("home=set", "the caller adds to the environment, it doesn't replace it");
    }

    [TestMethod]
    public async Task The_working_directory_is_honoured()
    {
        var dir = Directory.CreateTempSubdirectory("avalloy-cli-cwd-").FullName;
        try
        {
            if (!Posix) { Assert.Inconclusive("needs a POSIX shell"); return; }

            var r = await CliRunner.RunAsync("/bin/sh", dir, ["-c", "pwd -P"]);

            // macOS temp paths are symlinked (/var → /private/var), so compare the resolved leaf.
            r.Stdout.Trim().Should().EndWith(Path.GetFileName(dir));
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { /* best effort */ }
        }
    }

    [TestMethod]
    public async Task ExistsAsync_answers_for_a_real_tool_and_a_missing_one()
    {
        (await CliRunner.ExistsAsync("dotnet")).Should().BeTrue();
        (await CliRunner.ExistsAsync("avalloy-definitely-not-a-tool")).Should().BeFalse();
    }

    [TestMethod]
    public async Task The_Numeric_Exit_Code_Is_Reported()
    {
        if (!Posix) { Assert.Inconclusive("POSIX shell required"); }

        // Some tools speak through DISTINCT non-zero codes (`security` exits 44 for "not found");
        // callers telling those apart is the whole reason CliResult carries the number.
        var r = await ShAsync("exit 44");
        r.Ok.Should().BeFalse();
        r.Failure.Should().Be(CliFailure.ExitCode);
        r.ExitCode.Should().Be(44);

        (await ShAsync("true")).ExitCode.Should().Be(0);
        (await ShAsync("sleep 30", timeout: TimeSpan.FromMilliseconds(200))).ExitCode.Should().BeNull(
            "a killed process's code is the signal, not the tool's answer");
    }
}
