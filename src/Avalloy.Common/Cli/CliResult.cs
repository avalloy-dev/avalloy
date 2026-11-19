namespace Avalloy.Cli;

/// <summary>Why a <see cref="CliResult"/> isn't <see cref="CliResult.Ok"/>. The distinction that matters most
/// is <see cref="Canceled"/> vs <see cref="TimedOut"/>: work the user abandoned must not be reported as a
/// hung tool, and a genuinely hung tool must not be written off as "the user changed their mind".</summary>
public enum CliFailure
{
    /// <summary>The command ran and exited 0.</summary>
    None,

    /// <summary>The executable couldn't be launched at all — not on PATH, not executable.</summary>
    NotStarted,

    /// <summary>It ran to completion and exited non-zero. <see cref="CliResult.Stderr"/> carries its own message.</summary>
    ExitCode,

    /// <summary>It outlived its timeout and was killed. Nothing it printed is trustworthy.</summary>
    TimedOut,

    /// <summary>The caller's <see cref="CancellationToken"/> fired and it was killed.</summary>
    Canceled,

    /// <summary>Spawning or draining threw (permissions, a vanished working directory).</summary>
    Faulted,
}

/// <summary>The outcome of one CLI invocation. Never an exception: every runner in this assembly reports
/// failure in-band so callers can degrade gracefully when a tool is absent, refuses, or hangs.</summary>
/// <param name="Ok">The command exited 0.</param>
/// <param name="Stdout">Everything it wrote to standard output (empty when it never got that far).</param>
/// <param name="Stderr">Everything it wrote to standard error, or our own diagnostic when it never ran.</param>
/// <param name="Failure">Why it isn't <paramref name="Ok"/>; <see cref="CliFailure.None"/> when it is.</param>
public readonly record struct CliResult(bool Ok, string Stdout, string Stderr, CliFailure Failure)
{
    /// <summary>The process's numeric exit code, when it ran to completion (0 on <see cref="Ok"/>;
    /// the tool's own code on <see cref="CliFailure.ExitCode"/>). Null when it never ran or was
    /// killed — a killed process's code is the signal, not the tool's answer. Exists because some
    /// tools speak through DISTINCT non-zero codes (`security` exits 44 for "item not found",
    /// which callers must tell apart from "keychain locked").
    /// <para>Build results through <see cref="Success"/>/<see cref="Failed"/>, which keep this in
    /// step with <see cref="Failure"/>. The positional constructor cannot enforce it, so a
    /// hand-built value (a test fake) can carry <c>Failure == ExitCode</c> with a null code — a
    /// state the runner never produces, which silently disables any consumer branching on the
    /// number (and, since records compare by value, won't equal the runner's output either).</para></summary>
    public int? ExitCode { get; init; }

    /// <summary>It was killed for outliving its timeout.</summary>
    public bool TimedOut => Failure == CliFailure.TimedOut;

    /// <summary>It was killed because the caller cancelled.</summary>
    public bool Canceled => Failure == CliFailure.Canceled;

    /// <summary>The best single-line explanation: the tool's own stderr when it managed to say something,
    /// else its stdout, else <paramref name="fallback"/>. What error surfaces should show.</summary>
    public string Message(string fallback) =>
        Stderr.Trim() is { Length: > 0 } e ? e
        : Stdout.Trim() is { Length: > 0 } o ? o
        : fallback;

    /// <summary>A completed, exit-0 result. Public so fakes build the same shape the runner does.</summary>
    public static CliResult Success(string stdout, string stderr = "") =>
        new(true, stdout, stderr, CliFailure.None) { ExitCode = 0 };

    /// <summary>A failed result. Pass <paramref name="exitCode"/> whenever <paramref name="failure"/>
    /// is <see cref="CliFailure.ExitCode"/> — that pairing is what exit-code-branching consumers rely
    /// on; leave it null for the never-ran/killed failures. Public so fakes build honest states.</summary>
    public static CliResult Failed(CliFailure failure, string stdout, string stderr, int? exitCode = null) =>
        new(false, stdout, stderr, failure) { ExitCode = exitCode };
}
