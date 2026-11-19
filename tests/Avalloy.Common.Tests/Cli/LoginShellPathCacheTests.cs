using AwesomeAssertions;
using Avalloy.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Avalloy.Cli.Tests;

/// <summary>
/// The remembered login-shell PATH.
/// </summary>
/// <remarks>
/// <para>Resolving it means running <c>$SHELL -ilc</c> — an interactive AND login shell, so every rc
/// file runs — and it happens before an app puts anything on screen, because nothing may spawn a
/// child process until PATH is right. Measured on a real machine that was two seconds of every
/// launch.</para>
///
/// <para>Remembering it is therefore worth it, and the whole question is when the memory is allowed
/// to be reused. These pin that: the stamp has to move when anything that decides the answer moves,
/// and a value written under one stamp must never be handed back under another.</para>
/// </remarks>
[TestClass]
public sealed class LoginShellPathCacheTests
{
    // The query tests stand a fake shell up as an executable script, which needs a Unix file mode and a POSIX
    // shell to run it: on Windows they are inconclusive, the way CliRunnerTests handles the same need.
    [System.Runtime.Versioning.UnsupportedOSPlatformGuard("windows")]
    private static bool Posix => !OperatingSystem.IsWindows();

    [TestMethod]
    public void A_Value_Is_Returned_Only_For_The_Stamp_It_Was_Written_Under()
    {
        var cache = LoginShellPath.CachePath();
        var saved = File.Exists(cache) ? File.ReadAllText(cache) : null;
        try
        {
            LoginShellPath.WriteCache("stamp-one", "/opt/homebrew/bin:/usr/bin");

            LoginShellPath.ReadCache("stamp-one")!.Path.Should().Be(
                "/opt/homebrew/bin:/usr/bin", "the stamp is the one it was written under");
            LoginShellPath.ReadCache("stamp-two").Should().BeNull(
                "a different stamp means a shell file changed, and the answer may have changed with it");
        }
        finally
        {
            if (saved is null) File.Delete(cache);
            else File.WriteAllText(cache, saved);
        }
    }

    [TestMethod]
    public void A_PATH_Containing_The_Separator_Survives_The_Round_Trip()
    {
        // The stamp and the value share one file, split on a NUL — which a PATH cannot contain but a
        // newline, the obvious alternative, absolutely can.
        var cache = LoginShellPath.CachePath();
        var saved = File.Exists(cache) ? File.ReadAllText(cache) : null;
        try
        {
            var stamp = "/etc/paths|638000000000000000|42\n/home/x/.zshrc|638000000000000001|7\n";
            var path = "/opt/homebrew/bin:/Users/x/.local/state/fnm_multishells/931_1757/bin:/usr/bin";

            LoginShellPath.WriteCache(stamp, path);

            LoginShellPath.ReadCache(stamp)!.Path.Should().Be(path, "a multi-line stamp does not truncate the value");
        }
        finally
        {
            if (saved is null) File.Delete(cache);
            else File.WriteAllText(cache, saved);
        }
    }

    [TestMethod]
    public void The_Query_Asks_With_The_PATH_This_Process_STARTED_With()
    {
        // The regression this exists for: the query shell inherits our environment, so asking it after
        // a merge hands our own additions back with its own on top. Remembering THAT compounds it every
        // launch — measured growing the PATH by two entries per run before this was fixed.
        if (!Posix) { Assert.Inconclusive("needs a POSIX shell"); return; }
        var directory = Directory.CreateTempSubdirectory("avalloy-query-").FullName;
        var original = Environment.GetEnvironmentVariable("PATH");
        try
        {
            var shell = Path.Combine(directory, "fake-shell");
            File.WriteAllText(shell, "#!/bin/sh\nprintf '__AVALLOY_PATH__%s\\n' \"$PATH\"\n");
            File.SetUnixFileMode(
                shell,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);

            // The process PATH stands in for one a merge has already grown; the query is told to ask
            // with the pristine one instead, and must honour that rather than letting the child inherit.
            Environment.SetEnvironmentVariable("PATH", "/a/merged/entry:" + original);

            var answered = LoginShellPath.QueryLoginShellPath(shell, original ?? "");

            answered.Should().NotBeNull("the fake shell prints its PATH behind the marker");
            answered.Should().NotContain("/a/merged/entry",
                "the shell must be asked with the PATH the process started with, or its answer carries "
                + "our own additions and remembering it compounds them every launch");
        }
        finally
        {
            Environment.SetEnvironmentVariable("PATH", original);
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public void The_Prompt_Is_Remembered_Beside_The_PATH_And_A_File_Without_One_Still_Reads()
    {
        var cache = LoginShellPath.CachePath();
        var saved = File.Exists(cache) ? File.ReadAllText(cache) : null;
        try
        {
            // A multi-line, coloured prompt: newlines and escapes both have to survive the round trip.
            var prompt = "\u001b[32mjohn@mac\u001b[0m avalloy-prompt-cwd\n% ";
            LoginShellPath.WriteCache("stamp", "/usr/bin", prompt);
            var remembered = LoginShellPath.ReadCache("stamp");
            remembered!.Path.Should().Be("/usr/bin");
            remembered.Prompt.Should().Be(prompt);

            // The file a launch before prompts were remembered wrote: stamp and PATH only.
            File.WriteAllText(cache, "stamp\0/usr/bin");
            var older = LoginShellPath.ReadCache("stamp");
            older!.Path.Should().Be("/usr/bin", "the PATH is still what it was");
            older.Prompt.Should().BeNull("there was no prompt to remember");
        }
        finally
        {
            if (saved is null) File.Delete(cache);
            else File.WriteAllText(cache, saved);
        }
    }

    [TestMethod]
    public void A_Shell_That_Prints_Its_Prompt_Has_It_Taken_Between_The_Markers()
    {
        // The fake shell stands in for zsh (by name, which is what picks the prompt command) and prints what a
        // real one would: the PATH line, then the expanded prompt — two lines, with print -P's trailing newline.
        if (!Posix) { Assert.Inconclusive("needs a POSIX shell"); return; }
        var directory = Directory.CreateTempSubdirectory("avalloy-prompt-").FullName;
        try
        {
            var shell = Path.Combine(directory, "zsh");
            File.WriteAllText(shell,
                "#!/bin/sh\nprintf '__AVALLOY_PATH__%s\\n' \"$PATH\"\n"
                + "printf '__AVALLOY_PROMPT__'; printf 'john@mac %s\\n%% \\n' \"$(basename \"$PWD\")\"; printf '__AVALLOY_PROMPT_END__\\n'\n");
            File.SetUnixFileMode(shell, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);

            var answered = LoginShellPath.QueryLoginShell(shell, Environment.GetEnvironmentVariable("PATH") ?? "");

            answered.Should().NotBeNull();
            answered!.Path.Should().NotBeEmpty();
            answered.Prompt.Should().Be("john@mac avalloy-prompt-cwd\n% ",
                "the shell ran in the sentinel directory, the prompt shows it, and only print -P's own newline is dropped");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public void A_Shell_That_Cannot_Expand_A_Prompt_Is_Not_Asked_For_One()
    {
        LoginShellPath.PromptCommand("/bin/zsh").Should().Contain("print -P");
        LoginShellPath.PromptCommand("/opt/homebrew/bin/bash").Should().Contain("PS1@P");
        LoginShellPath.PromptCommand("/opt/homebrew/bin/fish").Should().Contain("fish_prompt");
        LoginShellPath.PromptCommand("/bin/sh").Should().BeNull("sh has no prompt expansion to ask for");
        LoginShellPath.PromptCommand("/usr/local/bin/nu").Should().BeNull();
    }

    [TestMethod]
    public void The_Prompt_Text_Is_What_Sits_Between_The_Markers()
    {
        LoginShellPath.ExtractPrompt("banner\n__AVALLOY_PATH__/usr/bin\n__AVALLOY_PROMPT__x % \n__AVALLOY_PROMPT_END__\n")
            .Should().Be("x % ");
        LoginShellPath.ExtractPrompt("__AVALLOY_PROMPT__\n__AVALLOY_PROMPT_END__\n").Should().BeNull("a blank prompt is no prompt");
        LoginShellPath.ExtractPrompt("__AVALLOY_PATH__/usr/bin\n").Should().BeNull("no markers, no prompt");
        LoginShellPath.ExtractPrompt("__AVALLOY_PROMPT__cut off").Should().BeNull("an unterminated prompt is not trusted");
    }

    [TestMethod]
    public void The_Stamp_Moves_When_A_Shell_File_Does()
    {
        var directory = Directory.CreateTempSubdirectory("avalloy-stamp-").FullName;
        try
        {
            // The shell binary is itself part of the stamp, so a touched "shell" moves it — the same
            // way an edited .zshrc does, without this test depending on the developer's own rc files.
            var shell = Path.Combine(directory, "fake-shell");
            File.WriteAllText(shell, "#!/bin/sh\n");

            var before = LoginShellPath.Stamp(shell);
            File.WriteAllText(shell, "#!/bin/sh\n# changed\n");
            var after = LoginShellPath.Stamp(shell);

            before.Should().NotBe(after, "a changed shell file has to invalidate the remembered PATH");
            LoginShellPath.Stamp(shell).Should().Be(after, "and an unchanged one has to keep it");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
