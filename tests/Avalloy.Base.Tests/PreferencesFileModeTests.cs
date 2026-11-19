using System.ComponentModel;
using AwesomeAssertions;
using Avalloy.Preferences;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Avalloy.Base.Tests;

/// <summary>
/// A save keeps the permissions the file already had.
///
/// Saves are atomic — a temp file written and renamed over the original — and a temp file is born
/// with the umask's mode. So an app that restricted its preferences file to its owner, because it
/// holds client secrets or API tokens, found it world-readable again after the next save. The
/// debounced save made that unfixable from outside: it gives the app no moment to restrict again.
///
/// Found from a consuming app whose credentials file was 0600 after it restricted it and 0644 a
/// few seconds later. Real file system only: these are about what the OS records, so they are
/// skipped on Windows, which has no Unix file mode.
/// </summary>
[TestClass]
[OSCondition(ConditionMode.Exclude, OperatingSystems.Windows)]
[System.Runtime.Versioning.UnsupportedOSPlatform("windows")]
public class PreferencesFileModeTests
{
    private const UnixFileMode OwnerOnly = UnixFileMode.UserRead | UnixFileMode.UserWrite;

    private sealed class TempDir : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(), "avalloy-prefs-mode-" + Guid.NewGuid().ToString("N")[..10]);

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

    [TestInitialize]
    public void UnixOnly()
    {
        if (OperatingSystem.IsWindows()) Assert.Inconclusive("Unix permissions only.");
    }

    private static PreferencesService<TestPrefs> Open(string dir, TimeSpan? debounce = null)
    {
        var prefs = new PreferencesService<TestPrefs>(dir, debounce, watch: false);
        prefs.Initialize();
        return prefs;
    }

    [TestMethod]
    public void A_restricted_file_stays_restricted_after_a_save()
    {
        using var dir = new TempDir();
        using var prefs = Open(dir.Path);
        File.SetUnixFileMode(prefs.PreferencesPath, OwnerOnly);

        prefs.Current.General.Value = "secret";
        prefs.SaveNow();

        File.GetUnixFileMode(prefs.PreferencesPath).Should().Be(OwnerOnly);
        File.ReadAllText(prefs.PreferencesPath).Should().Contain("secret", "the save itself still happened");
    }

    [TestMethod]
    public async Task A_restricted_file_stays_restricted_after_a_debounced_save()
    {
        // The case an app cannot work around: nothing tells it when this save lands.
        using var dir = new TempDir();
        using var prefs = Open(dir.Path, TimeSpan.FromMilliseconds(20));
        File.SetUnixFileMode(prefs.PreferencesPath, OwnerOnly);

        prefs.Current.General.Value = "rotated";   // mutation → RequestSave

        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!File.ReadAllText(prefs.PreferencesPath).Contains("rotated") && DateTime.UtcNow < deadline)
            await Task.Delay(20);

        File.ReadAllText(prefs.PreferencesPath).Should().Contain("rotated", "the debounced save must have run");
        File.GetUnixFileMode(prefs.PreferencesPath).Should().Be(OwnerOnly);
    }

    [TestMethod]
    public void Whatever_mode_the_file_had_is_kept_not_forced_to_owner_only()
    {
        // Preserving, not tightening: a file someone made group-readable stays group-readable.
        const UnixFileMode groupReadable = OwnerOnly | UnixFileMode.GroupRead;
        using var dir = new TempDir();
        using var prefs = Open(dir.Path);
        File.SetUnixFileMode(prefs.PreferencesPath, groupReadable);

        prefs.SaveNow();

        File.GetUnixFileMode(prefs.PreferencesPath).Should().Be(groupReadable);
    }

    [TestMethod]
    public void A_new_file_is_created_with_the_usual_mode()
    {
        using var dir = new TempDir();
        var reference = Path.Combine(dir.Path, "reference.txt");
        File.WriteAllText(reference, string.Empty);

        using var prefs = Open(dir.Path);   // no file yet: Initialize seeds one

        File.GetUnixFileMode(prefs.PreferencesPath).Should().Be(File.GetUnixFileMode(reference));
    }
}
