using System.ComponentModel;
using System.IO.Abstractions.TestingHelpers;
using AwesomeAssertions;
using Avalloy.Preferences;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Avalloy.Base.Tests;

/// <summary>
/// When <see cref="PreferencesService{T}"/> replaces <see cref="PreferencesService{T}.Current"/> from disk.
/// </summary>
/// <remarks>
/// <para>A reload swaps the whole model out from under anything holding its sections, so one that
/// isn't needed is not harmless. These pin that a change notification reloads only when the file holds
/// something the service did not itself write or read — whatever the watcher's timing — and that the
/// seed write on first launch goes through the watcher like any other save.</para>
///
/// <para>The watcher here is a fake the test triggers by hand, on a <see cref="MockFileSystem"/>: a
/// notification can arrive at any time, so the tests deliver it at the awkward ones.</para>
/// </remarks>
[TestClass]
public sealed class PreferencesServiceReloadTests
{
    private const string Dir = "/prefs";
    private const string PrefsPath = "/prefs/preferences.json";

    [TestMethod]
    public void The_First_Launch_Write_Is_Suppressed_And_Does_Not_Reload()
    {
        var rig = new Rig();

        rig.Service.Initialize();

        rig.Io.File.Exists(PrefsPath).Should().BeTrue("a missing file is seeded with defaults");
        rig.Watcher.Should().NotBeNull();
        rig.Watcher!.WritesSuppressed.Should().Be(1, "the watcher has to exist before the seed write to suppress it");

        var loaded = rig.Service.Current;
        rig.Reloads = 0;
        rig.Watcher.RaiseChanged();

        rig.Reloads.Should().Be(0);
        rig.Service.Current.Should().BeSameAs(loaded);
    }

    [TestMethod]
    public void A_Notification_For_Our_Own_Save_Does_Not_Reload_However_Late()
    {
        var rig = new Rig();
        rig.Service.Initialize();
        rig.Reloads = 0;
        var loaded = rig.Service.Current;

        loaded.Name = "changed";
        rig.Service.SaveNow();
        rig.Watcher!.RaiseChanged();

        rig.Reloads.Should().Be(0, "the file holds exactly what we wrote");
        rig.Service.Current.Should().BeSameAs(loaded);
    }

    [TestMethod]
    public void A_Reload_Pending_Before_A_Save_Does_Not_Undo_It()
    {
        // An external edit's notification is pending when our save lands on top of it. When the
        // notification is delivered the file holds our save, which must not be swapped for a reload of
        // itself — still less for the edit it overwrote.
        var rig = new Rig();
        rig.Service.Initialize();
        rig.Reloads = 0;
        var loaded = rig.Service.Current;

        rig.Io.File.WriteAllText(PrefsPath, """{ "name": "external" }""");
        loaded.Name = "ours";
        rig.Service.SaveNow();
        rig.Watcher!.RaiseChanged();

        rig.Reloads.Should().Be(0);
        rig.Service.Current.Should().BeSameAs(loaded);
        rig.Service.Current.Name.Should().Be("ours");
    }

    [TestMethod]
    public void An_External_Edit_Reloads_Once()
    {
        var rig = new Rig();
        rig.Service.Initialize();
        rig.Reloads = 0;

        rig.Io.File.WriteAllText(PrefsPath, """{ "name": "external" }""");
        rig.Watcher!.RaiseChanged();
        rig.Watcher.RaiseChanged();

        rig.Reloads.Should().Be(1, "the second notification finds the file holding what the first one read");
        rig.Service.Current.Name.Should().Be("external");
    }

    [TestMethod]
    public void A_Notification_That_Finds_The_File_Mid_Write_Does_Not_Reload()
    {
        // Unreadable because somebody is still writing it. Loading that would fall back to defaults,
        // which is worse than waiting for the next poll to ask again.
        var rig = new Rig();
        rig.Service.Initialize();
        rig.Reloads = 0;
        var loaded = rig.Service.Current;

        rig.Io.AddFile(PrefsPath, new MockFileData("""{ "name": "half""") { AllowedFileShare = FileShare.None });
        rig.Watcher!.RaiseChanged();

        rig.Reloads.Should().Be(0);
        rig.Service.Current.Should().BeSameAs(loaded);
        rig.Service.LastLoadError.Should().BeNull();
    }

    [TestMethod]
    public void An_Existing_File_Is_Not_Reloaded_Until_It_Changes()
    {
        var rig = new Rig();
        rig.Io.AddFile(PrefsPath, new MockFileData("""{ "name": "on disk" }"""));
        rig.Service.Initialize();
        rig.Reloads = 0;

        rig.Watcher!.RaiseChanged();

        rig.Reloads.Should().Be(0, "it holds what Initialize read");
        rig.Watcher.WritesSuppressed.Should().Be(0);
    }

    [TestMethod]
    public void A_Notification_After_Dispose_Does_Not_Reload()
    {
        var rig = new Rig();
        rig.Service.Initialize();
        rig.Reloads = 0;
        var loaded = rig.Service.Current;

        rig.Io.File.WriteAllText(PrefsPath, """{ "name": "external" }""");
        rig.Service.Dispose();
        rig.Watcher!.Disposed.Should().BeTrue();
        rig.Watcher.RaiseChanged();

        rig.Reloads.Should().Be(0, "a reload in flight when the service is disposed is cancelled");
        rig.Service.Current.Should().BeSameAs(loaded);
    }

    [TestMethod]
    public void An_Explicit_Load_Always_Reloads()
    {
        var rig = new Rig();
        rig.Service.Initialize();
        rig.Reloads = 0;

        rig.Service.Load();

        rig.Reloads.Should().Be(1, "Load is a programmatic reload, not a change notification");
    }

    private sealed class Rig
    {
        public Rig()
        {
            Io.Directory.CreateDirectory(Dir);
            Service = new PreferencesService<TestPrefs>(Dir, TimeSpan.FromHours(1), Io, "preferences.json",
                watch: true, typeInfoResolver: null, watcherFactory: (_, onChanged) => Watcher = new FakeWatcher(Io, onChanged));
            Service.PreferencesReloaded += (_, _) => Reloads++;
        }

        public MockFileSystem Io { get; } = new();
        public PreferencesService<TestPrefs> Service { get; }
        public FakeWatcher? Watcher { get; private set; }
        public int Reloads { get; set; }
    }

    private sealed class FakeWatcher(MockFileSystem io, Action onChanged) : IFileChangeWatcher
    {
        public int WritesSuppressed { get; private set; }
        public bool Disposed { get; private set; }

        public void RaiseChanged() => onChanged();

        public IDisposable SuppressEvents()
        {
            var before = io.File.Exists(PrefsPath) ? io.File.ReadAllText(PrefsPath) : null;
            return new Scope(() =>
            {
                var after = io.File.Exists(PrefsPath) ? io.File.ReadAllText(PrefsPath) : null;
                if (after != before) WritesSuppressed++;
            });
        }

        public void Dispose() => Disposed = true;

        private sealed class Scope(Action onDispose) : IDisposable
        {
            public void Dispose() => onDispose();
        }
    }

    public sealed class TestPrefs : IPreferencesModel, INotifyPropertyChanged
    {
        private string _name = "default";

        public string Name
        {
            get => _name;
            set
            {
                _name = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Name)));
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        public void RegisterMutationHandler(PropertyChangedEventHandler handler) => PropertyChanged += handler;
        public void UnregisterMutationHandler(PropertyChangedEventHandler handler) => PropertyChanged -= handler;
    }
}
