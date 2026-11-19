# CliRunner

Ships in **`Avalloy.Common`**, the plain .NET package under Base and Desktop (it was its own
package, `Avalloy.Cli`, until 0.23.0). An Avalloy app has it already; a headless program can
reference `Avalloy.Common` alone, with no Avalonia.

The one place an app spawns a child command-line tool. Every call is time-boxed, kills the
process tree when the box expires, drains stdout and stderr concurrently, and reports failure
in-band rather than throwing, so a caller degrades gracefully when a tool is absent, refuses,
or hangs.

```csharp
var result = await CliRunner.RunAsync(
    "git", workingDirectory, ["status", "--porcelain"], timeout: TimeSpan.FromSeconds(10), ct);

if (!result.Ok)
{
    toasts.ShowWarning(result.Message("git is not available"));
    return;
}
Parse(result.Stdout);
```

Arguments are a list, never a command string, so there is no shell and no quoting to get
wrong. The default timeout is thirty seconds. `ExistsAsync(exe)` probes whether a tool is
reachable at all.

`CliResult` carries `Ok`, `Stdout`, `Stderr`, the `ExitCode` when it got that far, and a
`CliFailure` that separates a non-zero exit from `TimedOut`, `Canceled` and a failure to
start. The distinction that matters most is cancelled versus timed out: work the user
abandoned must not be reported as a hung tool, and a genuinely hung tool must not be written
off as a change of mind.

`LoginShellPath` ships in this package too, because it exists for the children `CliRunner`
spawns: it [runs the user's login shell](/core/app#the-login-shell-path) once and merges its
PATH into the process, so a tool that resolves from a terminal also resolves from a packaged
app. It is in `Avalloy.Common` rather than Desktop so a headless host gets it without Avalonia; its
namespace is still `Avalloy.Services`. Its login-shell call is exactly the one that used to
hang an app forever when an rc file filled the stderr pipe, and it is why every spawn goes
through here.

`CliRunner` lives in the `Avalloy.Cli` namespace rather than `Avalloy.Process` because a namespace of
that name shadows `System.Diagnostics.Process` for every file inside it.
