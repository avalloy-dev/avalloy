using System.ComponentModel;
using AwesomeAssertions;
using Avalloy.Preferences;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Avalloy.Base.Tests;

/// <summary>
/// What the file watcher is allowed to throw away.
///
/// PreferencesService saves on a debounce and watches the file for external edits, so between a
/// mutation and its write there is always a window where memory is ahead of disk. A reload during
/// that window replaces <c>Current</c> with the older content and the edit is gone — silently, with
/// no error and nothing in a log.
///
/// The watcher suppresses events for its own writes, but suppression is a TIMING window (a 500ms
/// tail), and on macOS FSEvents routinely arrive after it closes. A watcher cannot tell a late echo
/// of our own write from somebody else's edit; only the content can. These pin that distinction.
///
/// Found from the other end: a consuming app's test suite failed about once in fifteen runs, on a
/// different test each time, always "the value I just set is not the value I read back".
/// </summary>
[TestClass]
public class PreferencesSelfWriteTests
{
    /// <summary>A throwaway directory per test, so a watcher never sees another test's file.</summary>
    private sealed class TempDir : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(), "avalloy-prefs-" + Guid.NewGuid().ToString("N")[..10]);

        public TempDir() => Directory.CreateDirectory(Path);

        public void Dispose()
        {
            try { Directory.Delete(Path, recursive: true); } catch { /* best effort */ }
        }
    }

    private sealed class TestPrefs : IPreferencesModel
    {
        public Section General { get; set; } = new();

        public void RegisterMutationHandler(PropertyChangedEventHandler handler) =>
            General.PropertyChanged += handler;

        public void UnregisterMutationHandler(PropertyChangedEventHandler handler) =>
            General.PropertyChanged -= handler;
    }

    private sealed class Section : INotifyPropertyChanged
    {
        private string _value = string.Empty;

        public string Value
        {
            get => _value;
            set
            {
                if (_value == value) return;
                _value = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Value)));
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
    }

    /// <summary>
    /// Long enough that the debounced save never fires on its own during a test, so "what is in
    /// memory but not yet on disk" is under the test's control rather than the clock's.
    /// </summary>
    private static readonly TimeSpan NeverSavesByItself = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Long enough for the watcher's own suppression tail (500ms) to lapse.
    ///
    /// Without this wait every test here is vacuous: a write landing inside the tail is swallowed as
    /// ours, so nothing reloads and every assertion about reloading passes for the wrong reason. The
    /// first draft of this file did exactly that.
    /// </summary>
    private static Task AfterSuppressionLapses() => Task.Delay(800);

    private static async Task<bool> WaitUntil(Func<bool> condition, int timeoutMs = 3000)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (DateTime.UtcNow < deadline)
        {
            if (condition()) return true;
            await Task.Delay(25);
        }

        return condition();
    }

    [TestMethod]
    public async Task A_file_event_for_our_own_write_does_not_discard_an_unsaved_edit()
    {
        using var dir = new TempDir();
        using var service = new PreferencesService<TestPrefs>(dir.Path, NeverSavesByItself);
        service.Initialize();

        service.Current.General.Value = "saved";
        service.SaveNow();
        await AfterSuppressionLapses();

        // Memory is now ahead of disk — the ordinary state of a settings screen mid-edit.
        service.Current.General.Value = "typed but not yet saved";

        // A late echo of our own write: byte-for-byte what we put there. This is what the service
        // sees when an FSEvent arrives after the suppression tail has closed, and it is entirely
        // indistinguishable from an external edit unless the content is compared.
        File.WriteAllText(service.PreferencesPath, File.ReadAllText(service.PreferencesPath));

        // Long enough for the watcher's poll (125ms) plus its debounce (250ms), several times over.
        await Task.Delay(1500);

        service.Current.General.Value.Should().Be("typed but not yet saved",
            "a reload triggered by our own write must not roll memory back to disk");
    }

    [TestMethod]
    public async Task A_genuine_external_edit_is_still_picked_up()
    {
        // The other half, and the reason this cannot be fixed by simply not reloading: hot-reload of
        // an edit somebody actually made is the feature the watcher exists for.
        using var dir = new TempDir();
        using var service = new PreferencesService<TestPrefs>(dir.Path, NeverSavesByItself);
        service.Initialize();

        service.Current.General.Value = "ours";
        service.SaveNow();
        await AfterSuppressionLapses();

        File.WriteAllText(service.PreferencesPath, """{"General":{"Value":"edited by hand"}}""");

        (await WaitUntil(() => service.Current.General.Value == "edited by hand"))
            .Should().BeTrue("an external edit must still replace Current");
    }

    [TestMethod]
    public async Task An_external_edit_after_our_own_write_is_not_masked_by_it()
    {
        // The combination that a naive "ignore one reload after each save" fix would get wrong:
        // our write, then somebody else's edit, then the late echo of ours. The edit must win.
        using var dir = new TempDir();
        using var service = new PreferencesService<TestPrefs>(dir.Path, NeverSavesByItself);
        service.Initialize();

        service.Current.General.Value = "ours";
        service.SaveNow();
        await AfterSuppressionLapses();

        File.WriteAllText(service.PreferencesPath, """{"General":{"Value":"theirs"}}""");

        (await WaitUntil(() => service.Current.General.Value == "theirs"))
            .Should().BeTrue("the external edit is the newest content and must be loaded");
    }
}
