using AwesomeAssertions;
using Avalloy.Preferences;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Avalloy.Base.Tests;

/// <summary>
/// The one place <see cref="DebouncedFileWatcher"/> runs against a real <see cref="FileSystemWatcher"/>.
/// </summary>
/// <remarks>
/// Every decision it makes is pinned in <see cref="DebouncedFileWatcherTests"/> on a manual clock. What only
/// the real thing can show is that the watcher, now turned on in the background to keep ~60 ms of FSEvents
/// start-up off an app's launch, does come up, and that disposing it while it is still coming up is safe.
/// </remarks>
[TestClass]
public sealed class DebouncedFileWatcherRealDiskTests
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(5);

    [TestMethod]
    public async Task The_Watcher_Comes_Up_In_The_Background_And_Reports_An_External_Edit()
    {
        var dir = Directory.CreateTempSubdirectory("avalloy-watcher-").FullName;
        try
        {
            var path = Path.Combine(dir, "preferences.json");
            File.WriteAllText(path, "{}");
            using var changed = new ManualResetEventSlim();
            using var watcher = new DebouncedFileWatcher(path, changed.Set, debounceMs: 50);

            (await watcher.FileSystemWatchStarted.WaitAsync(Patience)).Should().BeTrue();
            // The state poll would report the edit below on its own, so check the watcher itself is on.
            watcher.IsWatchingFileSystem.Should().BeTrue();

            File.WriteAllText(path, """{ "edited": true }""");

            changed.Wait(Patience).Should().BeTrue("an edit by someone else is reported");
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [TestMethod]
    public async Task Disposing_Before_The_Watcher_Is_Up_Is_Safe()
    {
        var dir = Directory.CreateTempSubdirectory("avalloy-watcher-").FullName;
        try
        {
            var watcher = new DebouncedFileWatcher(Path.Combine(dir, "preferences.json"), () => { });
            watcher.Dispose();

            var act = () => watcher.FileSystemWatchStarted.WaitAsync(Patience);

            await act.Should().NotThrowAsync("the background start must not touch a disposed watcher");
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
