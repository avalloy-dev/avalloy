using System.Diagnostics;

// For INVOKING command-line tools, not for being one. Nothing in this package touches Avalonia:
// Avalloy.Desktop needs it before Avalonia is initialised (LoginShellPath runs first thing at startup), and
// a headless consumer -- an engine shelling git, a store reading the OS keychain -- spawns a child
// without wanting a UI stack for it.
//
// Its own package, again. It was folded into Core (4040a01) when it was two files with one
// consumer; then headless consumers arrived for real -- Avalloy.Secrets, and an agent
// runner, which reached CliRunner through Avalloy.Desktop (then Avalloy.Core) and so shipped Avalonia.Desktop, Fluent
// and every platform's Skia natives to run a console. Avalloy.Desktop depends on this package, so
// Core's own consumers see no change.
//
// The namespace is Avalloy.Cli rather than Avalloy.Process on purpose: a namespace called
// `Avalloy.Process` shadows System.Diagnostics.Process for every file inside it, so
// `Process.Start` stops compiling. The types are Cli* anyway.
namespace Avalloy.Cli;

/// <summary>
/// The one place an app spawns a child CLI. Every call is time-boxed, kills the process tree when the box
/// expires, drains both pipes concurrently, and reports failure in-band as a <see cref="CliResult"/> rather
/// than throwing. Callers pass an argument LIST — nothing here ever builds a command string, so there is no
/// shell and no quoting to get wrong.
/// </summary>
public static class CliRunner
{
    /// <summary>The ceiling applied when a caller names no timeout. Deliberately finite: an unbounded child
    /// is an unbounded hang, and the caller who forgets is exactly the caller who needed the ceiling.</summary>
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(30);

    // How long we'll wait for the pipe reads to unwind after killing a child before giving up on them. They
    // normally complete the instant the process dies; this only bounds a pathological grandchild holding the
    // inherited handles open (a daemon a hook started), which must not strand the caller's await.
    private static readonly TimeSpan DrainGrace = TimeSpan.FromSeconds(2);

    /// <summary>Run <paramref name="exe"/> with <paramref name="args"/> and collect its output.</summary>
    /// <param name="exe">Executable name or path — resolved against PATH, never through a shell.</param>
    /// <param name="cwd">Working directory; empty inherits the current one.</param>
    /// <param name="args">Arguments, passed individually (spaces and quotes need no escaping).</param>
    /// <param name="ct">Cancels the run; the child is killed and the result reads <see cref="CliFailure.Canceled"/>.</param>
    /// <param name="timeout">Per-call ceiling; <see cref="DefaultTimeout"/> when null.</param>
    /// <param name="env">Extra environment entries layered over the inherited environment.</param>
    /// <param name="stdin">Written to the child's standard input, which is then CLOSED — commands that read
    /// to EOF (<c>check-ignore --stdin</c>, <c>update-ref --stdin</c>) never answer until it is.</param>
    public static async Task<CliResult> RunAsync(
        string exe,
        string? cwd,
        IReadOnlyList<string> args,
        CancellationToken ct = default,
        TimeSpan? timeout = null,
        IReadOnlyDictionary<string, string>? env = null,
        string? stdin = null)
    {
        var psi = new ProcessStartInfo(exe)
        {
            WorkingDirectory = cwd ?? "",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = stdin is not null,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var a in args)
        {
            psi.ArgumentList.Add(a);
        }

        if (env is not null)
        {
            foreach (var (key, value) in env)
            {
                psi.Environment[key] = value;
            }
        }

        var limit = timeout ?? DefaultTimeout;

        // Launch failure is its own answer, kept apart from a fault mid-run: "gh isn't installed" is a state
        // callers act on (fall back, show onboarding), while a drain that blew up is a bug to surface. Start
        // usually THROWS Win32Exception for a missing executable rather than returning null, so both shapes
        // have to land here or the useful case gets filed under "something went wrong".
        Process? process;
        try
        {
            process = Process.Start(psi);
        }
        catch (Exception ex)
        {
            return CliResult.Failed(CliFailure.NotStarted, "", ex.Message);
        }

        if (process is null)
        {
            return CliResult.Failed(CliFailure.NotStarted, "", $"{exe} failed to start");
        }

        try
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(limit);
            var token = timeoutCts.Token;

            // Both pipes are drained CONCURRENTLY, and stdin is written as a task rather than inline. A child
            // that fills the ~64KB stderr buffer (push progress, a chatty hook, a long "fatal:" dump) blocks
            // forever on that write, so it never exits and never closes stdout — a runner that reads one
            // stream to the end before touching the other deadlocks, and without a timeout it deadlocks
            // permanently. Same reason stdin can't be written inline: a payload past the buffer blocks against
            // output nobody is reading yet.
            var stdoutTask = process.StandardOutput.ReadToEndAsync(token);
            var stderrTask = process.StandardError.ReadToEndAsync(token);
            var stdinTask = stdin is null ? Task.CompletedTask : WriteStdinAsync(process, stdin);

            try
            {
                await stdinTask.WaitAsync(token).ConfigureAwait(false);
                var stdout = await stdoutTask.ConfigureAwait(false);
                var stderr = await stderrTask.ConfigureAwait(false);
                await process.WaitForExitAsync(token).ConfigureAwait(false);

                return process.ExitCode == 0
                    ? CliResult.Success(stdout, stderr)
                    : CliResult.Failed(CliFailure.ExitCode, stdout, stderr, process.ExitCode);
            }
            catch (OperationCanceledException)
            {
                Kill(process);
                await ObserveAsync(stdoutTask, stderrTask, stdinTask).ConfigureAwait(false);

                // The linked token fires for either reason, so ask the CALLER's token which one it was. A
                // cancelled turn reported as a timeout sends the user hunting a hang that never happened.
                return ct.IsCancellationRequested
                    ? CliResult.Failed(CliFailure.Canceled, "", $"{exe} cancelled")
                    : CliResult.Failed(CliFailure.TimedOut, "", $"{exe} timed out after {limit.TotalSeconds:0.#}s");
            }
        }
        catch (Exception ex)
        {
            Kill(process);
            return CliResult.Failed(CliFailure.Faulted, "", ex.Message);
        }
        finally
        {
            process.Dispose();
        }
    }

    /// <summary>Whether <paramref name="exe"/> is on PATH and runnable, asked the cheapest way there is.
    /// Never throws; a missing tool is a <c>false</c>, not an exception.</summary>
    public static async Task<bool> ExistsAsync(string exe, CancellationToken ct = default) =>
        (await RunAsync(exe, null, ["--version"], ct, TimeSpan.FromSeconds(10)).ConfigureAwait(false)).Ok;

    private static async Task WriteStdinAsync(Process process, string payload)
    {
        try
        {
            await process.StandardInput.WriteAsync(payload).ConfigureAwait(false);
            process.StandardInput.Close();   // the close IS the signal — a reader blocks until EOF arrives
        }
        catch
        {
            // The child died before reading its input (bad arguments, missing tool). Its exit code carries
            // that story; there's nothing this path can usefully add.
        }
    }

    private static void Kill(Process process)
    {
        try
        {
            process.Kill(entireProcessTree: true);
        }
        catch
        {
            // Already gone, or we lost the race with its exit. Either way there's nothing left to kill.
        }
    }

    // Await the abandoned pipe tasks so their cancellation doesn't surface later as an unobserved exception,
    // but never let them hold the caller: the outcome is already decided by the time we get here.
    private static async Task ObserveAsync(params Task[] tasks)
    {
        try
        {
            await Task.WhenAll(tasks).WaitAsync(DrainGrace).ConfigureAwait(false);
        }
        catch
        {
            // Cancelled, faulted, or still stuck behind a leaked handle — all expected, all already accounted for.
        }
    }
}
