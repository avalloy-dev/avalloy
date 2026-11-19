using Avalloy.Cli;

namespace Avalloy.Services;

/// <summary>
/// macOS/Linux GUI apps launched from Finder/Dock/Spotlight inherit only a minimal PATH
/// (<c>/usr/bin:/bin:/usr/sbin:/sbin</c>) — the login shell's rc files never run. So any bare-name
/// child process an app spawns (<c>claude</c>, <c>codex</c>, <c>node</c>/<c>npx</c> via a version
/// manager, <c>rg</c>, an <c>$EDITOR</c>, …) resolves fine when the app is launched from a terminal
/// but comes up "not found" from a packaged <c>.app</c>, because those tools live in
/// <c>/opt/homebrew/bin</c>, <c>~/.local/bin</c>, an <c>fnm</c>/<c>nvm</c> per-shell dir, etc. — none
/// of them on that minimal PATH.
///
/// This resolves the user's REAL login-shell PATH once at startup and merges it into the process
/// environment. Because <c>ProcessStartInfo</c> children inherit the parent process's
/// environment (unless a call site overrides it), updating THIS process's PATH is enough for every
/// later spawn to find the tools — no per-call plumbing, no hardcoded lists.
///
/// Hardcoding well-known dirs is not sufficient: Node via <c>fnm</c> lives under a dynamic
/// <c>~/.local/state/fnm_multishells/&lt;pid&gt;_…/bin</c> that only the shell's own init produces, so the
/// login shell is the single source of truth. Best-effort and bounded: any failure leaves PATH as-is.
/// No-op on Windows.
///
/// <c>AvalloyApp</c> calls <see cref="Ensure"/> automatically at the very start of
/// initialization (gated by <c>AvalloyApp.ResolveLoginShellPath</c>), before any app service can
/// spawn a child — so consumers get this for free.
///
/// <para>The same shell is asked for its PROMPT while it is there (<see cref="Prompt"/>): every rc
/// file has run by then, so the expanded prompt is the one the user's terminal shows, and an app that
/// draws a stand-in prompt before a shell exists (a dormant terminal pane) can draw the real one. It
/// rides along for free — the shell is already running — and it is cached and refreshed under the
/// same stamp as the PATH.</para>
/// </summary>
public static class LoginShellPath
{
    private const string Marker = "__AVALLOY_PATH__";
    private const string PromptMarker = "__AVALLOY_PROMPT__";
    private const string PromptEnd = "__AVALLOY_PROMPT_END__";

    /// <summary>The name of the directory the query shell is run in. Wherever the prompt shows the working
    /// directory, its expansion contains this name (or the whole path ending in it), which is what lets
    /// <see cref="PromptFor"/> put any directory there afterwards.</summary>
    public const string CwdSentinel = "avalloy-prompt-cwd";

    private static bool _done;

    /// <summary>The user's prompt as their login+interactive shell expands it, with the working directory
    /// shown as <see cref="CwdSentinel"/> — substitute per directory with <see cref="PromptFor"/>. Null until
    /// <see cref="Ensure"/> has run, on Windows, for a shell this cannot ask (only zsh, bash and fish are), and
    /// for a shell that printed nothing usable. May carry ANSI colour sequences, exactly as the terminal would
    /// receive them. Multi-line prompts keep their newlines. A prompt built by a precmd hook (powerlevel10k,
    /// starship) expands only partially here, because no command has run; callers should treat a prompt with
    /// control characters they cannot render as absent.</summary>
    public static string? Prompt { get; private set; }

    /// <summary>What was remembered from the last query: the PATH, and the prompt if the shell gave one.</summary>
    internal sealed record Remembered(string Path, string? Prompt);

    /// <summary><see cref="Prompt"/> with the sentinel directory replaced: the whole sentinel path (however the
    /// shell spelled it) becomes <paramref name="directoryDisplay"/>, and a bare sentinel name (a prompt that
    /// shows only the last segment) becomes <paramref name="directoryLeaf"/>. Null when there is no prompt.</summary>
    public static string? PromptFor(string directoryDisplay, string directoryLeaf)
    {
        var prompt = Prompt;
        if (string.IsNullOrEmpty(prompt)) return null;
        // Longest first: a full path contains the bare name, so the name must not be replaced first.
        foreach (var full in SentinelPaths())
        {
            prompt = prompt.Replace(full, directoryDisplay, StringComparison.Ordinal);
        }
        return prompt.Replace(CwdSentinel, directoryLeaf, StringComparison.Ordinal);
    }

    /// <summary>Every spelling the shell might give the sentinel directory: as created, resolved through
    /// symlinks (macOS's /var → /private/var), and home-relative (zsh's <c>%~</c>).</summary>
    private static IEnumerable<string> SentinelPaths()
    {
        var dir = SentinelDirectory(create: false);
        var forms = new List<string> { dir };
        try
        {
            var resolved = new DirectoryInfo(dir).ResolveLinkTarget(returnFinalTarget: true)?.FullName;
            if (resolved is { Length: > 0 }) forms.Add(resolved);
            var real = Path.GetFullPath(dir);
            if (real != dir) forms.Add(real);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var withHome = new List<string>();
        foreach (var f in forms)
        {
            if (home.Length > 0 && f.StartsWith(home, StringComparison.Ordinal)) withHome.Add("~" + f[home.Length..]);
        }
        forms.AddRange(withHome);
        return forms.Distinct(StringComparer.Ordinal).OrderByDescending(f => f.Length);
    }

    /// <summary>The directory the query runs in — under the system temp, named <see cref="CwdSentinel"/>.
    /// Under temp rather than a per-app location so the prompt is not tied to one app's paths.</summary>
    internal static string SentinelDirectory(bool create)
    {
        var dir = Path.Combine(Path.GetTempPath().TrimEnd(Path.DirectorySeparatorChar), CwdSentinel);
        if (create)
        {
            try { Directory.CreateDirectory(dir); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
        return dir;
    }


    /// <summary>
    /// Where the resolved PATH is remembered between launches, shared by every Avalloy app on this
    /// machine — the login shell's PATH is a property of the USER's shell, not of any one app.
    /// </summary>
    internal static string CachePath() =>
        Path.Combine(AppPaths.Shared.ConfigDir, "login-shell-path");

    /// <summary>
    /// The files whose contents decide the answer, stamped. A cached PATH is only reused while every
    /// one of these is unchanged — which is what makes reuse safe rather than merely fast.
    /// </summary>
    /// <remarks>
    /// The stamp deliberately covers the shell itself (a changed <c>$SHELL</c> is a different answer),
    /// the rc files both shells read, and <c>/etc/paths</c> plus <c>/etc/paths.d</c>, which macOS's
    /// path_helper composes the base PATH from.
    /// </remarks>
    internal static string Stamp(string shell)
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var files = new List<string>
        {
            shell,
            "/etc/paths", "/etc/zshenv", "/etc/zprofile", "/etc/zshrc", "/etc/profile", "/etc/bashrc",
            Path.Combine(home, ".zshenv"), Path.Combine(home, ".zprofile"), Path.Combine(home, ".zshrc"),
            Path.Combine(home, ".zlogin"), Path.Combine(home, ".bash_profile"), Path.Combine(home, ".bashrc"),
            Path.Combine(home, ".profile"),
        };

        try
        {
            if (Directory.Exists("/etc/paths.d")) files.AddRange(Directory.EnumerateFiles("/etc/paths.d"));
        }
        catch (IOException)
        {
            // An unreadable /etc/paths.d just leaves those entries out of the stamp.
        }

        var stamp = new System.Text.StringBuilder();
        foreach (var file in files.OrderBy(f => f, StringComparer.Ordinal))
        {
            try
            {
                var info = new FileInfo(file);
                if (info.Exists) stamp.Append(file).Append('|').Append(info.LastWriteTimeUtc.Ticks).Append('|')
                    .Append(info.Length).Append('\n');
            }
            catch (IOException)
            {
                // A file we cannot stat contributes nothing; the others still key the cache.
            }
        }

        return stamp.ToString();
    }

    /// <summary>Resolve the login-shell PATH and merge it (login entries first, de-duplicated) into this
    /// process's PATH. Idempotent — safe to call more than once; only the first call does work.</summary>
    public static void Ensure()
    {
        if (_done || OperatingSystem.IsWindows())
        {
            return;
        }

        _done = true;

        try
        {
            var shell = ResolveShell();
            var stamp = Stamp(shell);

            // Read BEFORE anything merges into it, and passed explicitly to every query below. The
            // query shell inherits this process's environment, so asking it after a merge hands our
            // own additions back with its own on top — and remembering that answer compounds it on
            // the next launch, which a run-to-run measurement caught growing the PATH by two entries
            // a launch. Explicit rather than captured at type load, because what has already touched
            // this type is not something a launch should depend on.
            var askWith = Environment.GetEnvironmentVariable("PATH") ?? "";

            // A hit is applied immediately and the shell is re-queried BEHIND the launch. The stamp
            // catches an edited rc file, but not everything: fnm hands out a per-shell
            // ~/.local/state/fnm_multishells/<pid>_…/bin that is different for every shell it starts,
            // so a remembered PATH can name a directory belonging to a shell that has since exited.
            // The background re-query is what heals that — it lands well before an app spawns
            // anything, because the first spawn is a user asking for it.
            if (ReadCache(stamp) is { } remembered)
            {
                Merge(remembered.Path);
                Prompt = remembered.Prompt;
                RefreshBehind(shell, stamp, askWith);
                return;
            }

            // A miss is paid for now. It is the first launch on this machine, or the user changed a
            // shell file since the last one, and until the query returns nothing knows where node is.
            var resolved = QueryLoginShell(shell, askWith);
            if (resolved is null || string.IsNullOrWhiteSpace(resolved.Path))
            {
                return;
            }

            WriteCache(stamp, resolved.Path, resolved.Prompt);
            Merge(resolved.Path);
            Prompt = resolved.Prompt;
        }
        catch
        {
            // Best effort — a shell that hangs, refuses -i, or isn't there must never block app startup.
        }
    }

    /// <summary>Merges a resolved PATH into this process's, login entries first, de-duplicated.</summary>
    private static void Merge(string resolved)
    {
        try
        {
            var current = Environment.GetEnvironmentVariable("PATH") ?? "";

            // Union: login-shell entries first (they carry the version-manager / homebrew dirs the GUI
            // launch dropped), then any current entries not already present (keeps the minimal
            // /usr/bin:/bin set even if the shell somehow omitted it). Order-preserving, no dupes.
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var merged = resolved.Split(':', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Concat(current.Split(':', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                .Where(seen.Add)
                .ToArray();

            if (merged.Length > 0)
            {
                Environment.SetEnvironmentVariable("PATH", string.Join(':', merged));
            }
        }
        catch (ArgumentException)
        {
            // A PATH the environment refuses is left as it was.
        }
    }

    /// <summary>Re-queries the shell off the launch and adopts the answer if it moved.</summary>
    private static void RefreshBehind(string shell, string stamp, string askWith) => Task.Run(() =>
    {
        try
        {
            var resolved = QueryLoginShell(shell, askWith);
            if (resolved is null || string.IsNullOrWhiteSpace(resolved.Path)) return;

            WriteCache(stamp, resolved.Path, resolved.Prompt);
            Merge(resolved.Path);
            Prompt = resolved.Prompt;
        }
        catch
        {
            // The remembered PATH is already in place; a failed refresh changes nothing.
        }
    });

    /// <summary>What was remembered, if it was written for exactly this stamp: the PATH, and the prompt when
    /// one was written with it (a file from before prompts were remembered simply has none).</summary>
    internal static Remembered? ReadCache(string stamp)
    {
        try
        {
            var path = CachePath();
            if (!File.Exists(path)) return null;

            // Stamp, then the PATH, then the prompt, NUL-separated: one write, and a truncated file
            // simply fails to match. NUL because a PATH cannot contain one and a prompt has no business to.
            var text = File.ReadAllText(path);
            var parts = text.Split('\0');
            if (parts.Length < 2 || parts[0] != stamp) return null;

            return new Remembered(parts[1], parts.Length >= 3 && parts[2].Length > 0 ? parts[2] : null);
        }
        catch (IOException)
        {
            return null;
        }
    }

    internal static void WriteCache(string stamp, string resolved, string? prompt = null)
    {
        try
        {
            var path = CachePath();
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, stamp + '\0' + resolved + '\0' + (prompt ?? ""));
        }
        catch (IOException)
        {
            // Not being able to remember it only costs the next launch the query again.
        }
    }

    internal static string ResolveShell()
    {
        var shell = Environment.GetEnvironmentVariable("SHELL");
        if (string.IsNullOrEmpty(shell) || !File.Exists(shell))
        {
            shell = File.Exists("/bin/zsh") ? "/bin/zsh" : "/bin/sh";
        }

        return shell;
    }

    /// <summary>The PATH alone — <see cref="QueryLoginShell"/> without the prompt.</summary>
    internal static string? QueryLoginShellPath(string shell, string askWith) => QueryLoginShell(shell, askWith)?.Path;

    /// <summary>Run the user's login+interactive shell and print its PATH behind a marker line — and, for a
    /// shell that can expand its own prompt, the prompt between two more. Interactive (<c>-i</c>) so rc files
    /// (where <c>fnm</c>/<c>nvm</c>/<c>rbenv</c> hooks usually live) run; login (<c>-l</c>) so profile files run
    /// too. The markers isolate the answers from any banner noise the shell may print. Bounded to a few
    /// seconds and killed on overrun. Returns null on any failure.</summary>
    internal static Remembered? QueryLoginShell(string shell, string askWith)
    {
        // -ilc <cmd>: interactive + login + command. `printf` avoids the trailing-newline ambiguity of echo.
        // Through CliRunner rather than a local spawn, for the reason this runs at all: it is the FIRST
        // thing a Avalloy app does, on the startup thread. The old local version redirected stderr and
        // never read it, and took stdout with a blocking GetResult() AFTER WaitForExit — so an rc file
        // noisy enough to fill the stderr pipe, or a daemon it backgrounded inheriting the stdout handle,
        // blocked startup forever with no timeout able to reach it. CliRunner drains both concurrently,
        // kills the tree on overrun, and bounds the drain itself.
        //
        // Run IN the sentinel directory, so the prompt's working-directory segment expands to something a
        // consumer can find and replace (PromptFor). The PATH does not care where it is asked from.
        var command = $"printf '{Marker}%s\\n' \"$PATH\"";
        if (PromptCommand(shell) is { } promptCommand) command += "; " + promptCommand;
        var result = CliRunner.RunAsync(
            shell, SentinelDirectory(create: true), ["-ilc", command],
            timeout: TimeSpan.FromSeconds(4),
            env: new Dictionary<string, string> { ["PATH"] = askWith })
            .GetAwaiter().GetResult();

        if (!result.Ok)
        {
            return null;
        }

        var line = result.Stdout
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .LastOrDefault(l => l.StartsWith(Marker, StringComparison.Ordinal));
        if (line is null) return null;

        return new Remembered(line[Marker.Length..], ExtractPrompt(result.Stdout));
    }

    /// <summary>How each shell prints its prompt expanded, between the two prompt markers: zsh's <c>print -P</c>
    /// expands prompt escapes; bash 4.4+ has <c>${PS1@P}</c>; fish's prompt is a function. Anything else is not
    /// asked — a shell we cannot expand a prompt for should not be handed a command it may not parse.</summary>
    internal static string? PromptCommand(string shell) => Path.GetFileName(shell) switch
    {
        "zsh" => $"printf '{PromptMarker}'; print -P -- \"$PROMPT\"; printf '{PromptEnd}\\n'",
        "bash" => $"printf '{PromptMarker}%s{PromptEnd}\\n' \"${{PS1@P}}\"",
        "fish" => $"printf '{PromptMarker}'; fish_prompt; printf '{PromptEnd}\\n'",
        _ => null,
    };

    /// <summary>The text between the prompt markers, less the one newline <c>print -P</c> appends. Null when the
    /// markers are missing (the shell was not asked, or did not get that far) or the prompt is blank.</summary>
    internal static string? ExtractPrompt(string stdout)
    {
        var start = stdout.LastIndexOf(PromptMarker, StringComparison.Ordinal);
        if (start < 0) return null;
        start += PromptMarker.Length;
        var end = stdout.IndexOf(PromptEnd, start, StringComparison.Ordinal);
        if (end < 0) return null;
        var prompt = stdout[start..end];
        if (prompt.EndsWith("\r\n", StringComparison.Ordinal)) prompt = prompt[..^2];
        else if (prompt.EndsWith('\n')) prompt = prompt[..^1];
        return string.IsNullOrWhiteSpace(prompt) ? null : prompt;
    }
}
