using Avalloy.Preferences;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Avalloy.Base.Tests;

[TestClass]
public class FileSetWatcherTests
{
    private static readonly string Root = Path.Combine(Path.GetTempPath(), "watch-home");
    private static string P(string path) => Path.Combine(Root, path.Replace('/', Path.DirectorySeparatorChar));

    [TestMethod]
    public void Missing_ancestor_walk_reaches_nearest_existing_directory()
    {
        var fs = new MemoryDisk(); var clock = new Clock();
        using var watcher = new FileSetWatcher(fs, clock);
        watcher.SetPaths([P("a/b/c/file")]); clock.Fire();
        CollectionAssert.AreEqual(new[] { Root }, fs.Active.Select(s => s.Path).ToArray());
        fs.Emit(Root, "b", true); Assert.AreEqual(0, clock.Pending);
        fs.Emit(Root, "a", true); Assert.AreEqual(1, clock.Pending);
    }

    [TestMethod]
    public void Missing_segments_advance_and_directory_replacement_recovers()
    {
        var fs = new MemoryDisk(); var clock = new Clock();
        using var watcher = new FileSetWatcher(fs, clock);
        int changes = 0; watcher.Changed += (_, _) => changes++;
        watcher.SetPaths([P("a/b/file")]); clock.Fire();
        Assert.IsTrue(fs.Active.Any(s => s.Path == Root));
        fs.Directories.Add(P("a")); fs.Emit(Root, "a", true); clock.Fire();
        Assert.IsTrue(fs.Active.Any(s => s.Path == P("a")));
        fs.Directories.Add(P("a/b")); fs.Emit(P("a"), "b", true); clock.Fire();
        Assert.IsTrue(fs.Active.Any(s => s.Path == P("a/b")));
        var old = fs.Active.Single(s => s.Path == P("a/b"));
        fs.Emit(P("a"), "b", true); clock.Fire();
        Assert.IsTrue(old.Disposed);
        fs.Emit(P("a/b"), "file", false); clock.Fire();
        Assert.AreEqual(5, changes);
    }

    [TestMethod]
    public void Bursts_atomic_saves_case_variants_errors_and_disposal()
    {
        var fs = new MemoryDisk(); fs.Directories.Add(P("a")); var clock = new Clock();
        using var watcher = new FileSetWatcher(fs, clock);
        int changes = 0; watcher.Changed += (_, _) => changes++;
        watcher.SetPaths([P("a/file")]); clock.Fire();
        fs.Emit(P("a"), "unrelated", true); Assert.AreEqual(0, clock.Pending);
        fs.Emit(P("a"), "FILE", false); fs.Emit(P("a"), "file", true);
        Assert.AreEqual(1, clock.Pending); clock.Fire(); Assert.AreEqual(2, changes);
        fs.Emit(P("a"), null, true); clock.Fire(); Assert.AreEqual(3, changes);
        fs.Emit(P("a"), "file", false); var stale = clock.Last;
        watcher.Dispose(); stale!(); Assert.AreEqual(3, changes);
        Assert.IsFalse(fs.Active.Any());
    }

    [TestMethod]
    public void Ordinary_writes_do_not_rebuild_and_case_only_file_directory_collision_is_seen()
    {
        var fs = new MemoryDisk(); fs.Directories.UnionWith([P("a"), P("a/extra")]); var clock = new Clock();
        using var watcher = new FileSetWatcher(fs, clock);
        int changes = 0; watcher.Changed += (_, _) => changes++;
        watcher.SetPaths([P("a/Extra"), P("a/extra/file")]); clock.Fire();
        var count = fs.Created;
        fs.Emit(P("a"), "extra", false); Assert.AreEqual(0, clock.Pending);
        fs.Emit(P("a"), "Extra", false); clock.Fire();
        Assert.AreEqual(2, changes); Assert.AreEqual(count, fs.Created);
    }

    [TestMethod]
    public void Retarget_closes_read_subscribe_race_and_drops_old_names()
    {
        var fs = new MemoryDisk(); var clock = new Clock();
        using var watcher = new FileSetWatcher(fs, clock);
        int changes = 0; watcher.Changed += (_, _) => changes++;
        watcher.SetPaths([P("old")]); clock.Fire();
        watcher.SetPaths([P("new")]); clock.Fire(); Assert.AreEqual(2, changes);
        fs.Emit(Root, "old", false); Assert.AreEqual(0, clock.Pending);
        fs.Emit(Root, "new", false); clock.Fire(); Assert.AreEqual(3, changes);
    }

    [TestMethod]
    [DataRow(true, 2)] [DataRow(false, 2)] [DataRow(true, 3)] [DataRow(false, 3)]
    public void Cap_keeps_whole_parent_chains_and_all_names(bool innerFirst, int cap)
    {
        var fs = new MemoryDisk(); fs.Directories.UnionWith([P("p"), P("p/c")]); var clock = new Clock();
        using var watcher = new FileSetWatcher(fs, clock, maxDirectories: cap);
        var inner = P("p/c/f1"); var outer = P("p/f2");
        watcher.SetPaths(innerFirst ? [inner, outer] : [outer, inner]); clock.Fire();
        Assert.AreEqual(cap, fs.Active.Count()); Assert.AreEqual(cap == 3 ? 0 : 1, watcher.DroppedDirectories);
        fs.Emit(P("p"), "f2", false); Assert.AreEqual(1, clock.Pending); clock.Fire();
        fs.Emit(P("p/c"), "f1", false); Assert.AreEqual(cap == 3 ? 1 : 0, clock.Pending);
    }

    [TestMethod]
    public void Thousands_of_paths_are_bounded_and_later_pairs_that_fit_are_kept()
    {
        var fs = new MemoryDisk(); var clock = new Clock();
        var files = Enumerable.Range(0, 100).Select(i => P($"p{i}/d/f")).ToArray();
        foreach (var f in files) { fs.Directories.Add(Path.GetDirectoryName(f)!); fs.Directories.Add(Path.GetDirectoryName(Path.GetDirectoryName(f)!)!); }
        using var watcher = new FileSetWatcher(fs, clock, maxDirectories: 5);
        watcher.SetPaths(files.Concat(Enumerable.Range(0, 3000).Select(i => P($"p0/d/f{i}"))));
        clock.Fire(); Assert.AreEqual(4, fs.Active.Count()); Assert.AreEqual(98, watcher.DroppedDirectories);
        fs.Emit(P("p0/d"), "f2999", false); Assert.AreEqual(1, clock.Pending);
    }

    [TestMethod]
    public void Shared_parent_is_excluded_but_direct_files_and_missing_segments_are_watched()
    {
        var fs = new MemoryDisk(); fs.Shared.Add(Root); fs.Directories.Add(P("private")); var clock = new Clock();
        using var watcher = new FileSetWatcher(fs, clock);
        watcher.SetPaths([P("private/f")]); clock.Fire(); Assert.AreEqual(1, fs.Active.Count());
        watcher.SetPaths([P("private/f"), P("direct"), P("missing/f")]); clock.Fire();
        fs.Emit(Root, "PRIVATE", true); Assert.AreEqual(0, clock.Pending);
        fs.Emit(Root, "direct", false); Assert.AreEqual(1, clock.Pending); clock.Fire();
        fs.Emit(Root, "missing", true); Assert.AreEqual(1, clock.Pending);
    }

    [TestMethod]
    public void Reload_can_retarget_same_paths_and_a_throwing_handler_leaves_watchers_alive()
    {
        var fs = new MemoryDisk(); var clock = new Clock();
        using var watcher = new FileSetWatcher(fs, clock);
        int changes = 0;
        watcher.Changed += (_, _) => { changes++; watcher.SetPaths([P("file")]); };
        watcher.SetPaths([P("file")]); clock.Fire();
        Assert.AreEqual(0, clock.Pending);
        EventHandler fail = (_, _) => throw new InvalidOperationException("read failed");
        watcher.Changed += fail;
        fs.Emit(Root, "file", true);
        Assert.ThrowsExactly<InvalidOperationException>(() => clock.Fire());
        watcher.Changed -= fail;
        fs.Emit(Root, "file", false); clock.Fire();
        Assert.AreEqual(3, changes);
    }

    [TestMethod]
    public void Creation_deletion_and_rename_away_are_all_notifications_without_reading_targets()
    {
        var fs = new MemoryDisk(); var clock = new Clock();
        using var watcher = new FileSetWatcher(fs, clock);
        int changes = 0; watcher.Changed += (_, _) => changes++;
        watcher.SetPaths([P("file")]); clock.Fire();
        // No content/existence API exists on the seam: a FIFO target cannot be opened.
        foreach (var name in new[] { "file", "FILE", "file" })
        { fs.Emit(Root, name, true); clock.Fire(); }
        Assert.AreEqual(4, changes);
    }

    [TestMethod]
    public void Case_only_structural_collision_rebuilds_but_directory_writes_do_not()
    {
        var fs = new MemoryDisk(); fs.Directories.UnionWith([P("a"), P("a/extra")]);
        var clock = new Clock(); using var watcher = new FileSetWatcher(fs, clock);
        int changes = 0; watcher.Changed += (_, _) => changes++;
        watcher.SetPaths([P("a/Extra"), P("a/extra/file")]); clock.Fire();
        var old = fs.Active.Single(s => s.Path == P("a/extra"));
        // Case-sensitive disk: an atomic rename over Extra collides with extra/ by case.
        fs.Emit(P("a"), "Extra", true); fs.Emit(P("a"), "temporary", true);
        clock.Fire(); Assert.AreEqual(2, changes); Assert.IsTrue(old.Disposed);
        var count = fs.Created;
        fs.Emit(P("a"), "extra", false);
        Assert.AreEqual(0, clock.Pending); Assert.AreEqual(count, fs.Created);
        // Renaming away uses the old watched name even if the destination is unrelated.
        fs.Emit(P("a"), "Extra", true); fs.Emit(P("a"), "away", true);
        clock.Fire(); Assert.AreEqual(3, changes);
    }

    [TestMethod]
    public void Root_file_appearing_during_setup_is_reported_without_an_event()
    {
        var fs = new MemoryDisk(); var clock = new Clock();
        bool exists = false, observed = false;
        fs.OnWatch = _ => exists = true; // after caller read, before subscribing completes
        using var watcher = new FileSetWatcher(fs, clock);
        watcher.Changed += (_, _) => observed = exists;
        watcher.SetPaths([P("config")]);
        Assert.IsFalse(observed); clock.Fire(); Assert.IsTrue(observed);
        Assert.AreEqual(0, clock.Pending);
    }

    [TestMethod]
    public void Directories_created_during_setup_are_reconciled_without_an_event()
    {
        var fs = new MemoryDisk(); var clock = new Clock();
        fs.OnWatch = _ => fs.Directories.UnionWith([P("a"), P("a/b")]);
        using var watcher = new FileSetWatcher(fs, clock);
        watcher.SetPaths([P("a/b/file")]); clock.Fire();
        Assert.IsTrue(fs.Active.Any(s => s.Path == P("a/b")));
        fs.Emit(P("a/b"), "file", false); Assert.AreEqual(1, clock.Pending);
    }

    [TestMethod]
    public void Cap_skips_an_oversized_chain_then_keeps_a_later_candidate_that_fits()
    {
        var fs = new MemoryDisk(); var clock = new Clock();
        fs.Directories.UnionWith([P("p"), P("p/c"), P("other")]);
        using var watcher = new FileSetWatcher(fs, clock, maxDirectories: 2);
        watcher.SetPaths([P("p/c/inner"), P("p/outer"), P("other/file")]); clock.Fire();
        CollectionAssert.AreEquivalent(new[] { P("p"), Root }, fs.Active.Select(s => s.Path).ToArray());
        Assert.AreEqual(2, watcher.DroppedDirectories);
        // The retained file's parent subscription must retain its structural name.
        var old = fs.Active.Single(s => s.Path == P("p"));
        fs.Emit(Root, "p", true); clock.Fire(); Assert.IsTrue(old.Disposed);
        fs.Emit(P("p"), "outer", false); Assert.AreEqual(1, clock.Pending);
    }

    [TestMethod]
    public void Debounce_is_global_and_stale_cancelled_callbacks_cannot_fire()
    {
        var fs = new MemoryDisk(); fs.Directories.UnionWith([P("a"), P("b")]);
        var clock = new Clock(); using var watcher = new FileSetWatcher(fs, clock);
        int changes = 0; watcher.Changed += (_, _) => changes++;
        watcher.SetPaths([P("a/f"), P("b/g")]); clock.Fire();
        fs.Emit(P("a"), "f", false); var stale = clock.Last!;
        fs.Emit(P("b"), "g", false);
        stale(); Assert.AreEqual(1, changes);
        Assert.AreEqual(1, clock.Pending); clock.Fire(); Assert.AreEqual(2, changes);
    }

    [TestMethod]
    public void Disposal_rejects_stale_subscriptions_and_retargeting_and_is_idempotent()
    {
        var fs = new MemoryDisk(); var clock = new Clock();
        var watcher = new FileSetWatcher(fs, clock);
        int changes = 0; watcher.Changed += (_, _) => changes++;
        watcher.SetPaths([P("file")]); var old = fs.Active.Single(); var pending = clock.Last!;
        watcher.Dispose(); watcher.Dispose(); old.Callback("file", true); pending(); clock.Fire();
        Assert.AreEqual(0, changes); Assert.AreEqual(0, clock.Pending); Assert.IsFalse(fs.Active.Any());
        Assert.ThrowsExactly<ObjectDisposedException>(() => watcher.SetPaths([P("other")]));
    }

    [TestMethod]
    public void A_directory_disappearing_during_subscription_is_retried()
    {
        var fs = new MemoryDisk(); fs.Directories.Add(P("a")); var clock = new Clock();
        fs.OnWatch = path =>
        {
            if (path != P("a")) return;
            fs.OnWatch = null; fs.Directories.Remove(P("a")); throw new DirectoryNotFoundException();
        };
        using var watcher = new FileSetWatcher(fs, clock);
        watcher.SetPaths([P("a/file")]); clock.Fire();
        Assert.IsTrue(fs.Active.Any(s => s.Path == Root));
        fs.Directories.Add(P("a")); fs.Emit(Root, "a", true); clock.Fire();
        Assert.IsTrue(fs.Active.Any(s => s.Path == P("a")));
    }

    [TestMethod]
    public void Second_pass_keeps_covering_subscription_and_revalidates_new_directories()
    {
        var fs = new MemoryDisk(); var clock = new Clock();
        using var watcher = new FileSetWatcher(fs, clock);
        int changes = 0; watcher.Changed += (_, _) => changes++;
        watcher.SetPaths([P("a/b/file")]);
        var covering = fs.Active.Single(s => s.Path == Root);
        var checks = 0;
        // Freeze the second pass's planning view, then make the whole hierarchy appear
        // during its validation read. No event is supplied to rescue a stale plan.
        fs.OnDirectoryExists = path =>
        {
            if (path != Root || ++checks != 1) return;
            Assert.IsFalse(covering.Disposed, "ancestor must cover the update");
            fs.Directories.Add(P("a"));
        };
        fs.OnWatch = path =>
        {
            if (path != P("a")) return;
            Assert.IsFalse(covering.Disposed);
            fs.Directories.Add(P("a/b"));
        };
        clock.Fire();
        Assert.IsTrue(fs.Active.Any(s => s.Path == P("a/b")));
        fs.Emit(P("a/b"), "file", false); clock.Fire(); Assert.AreEqual(2, changes);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void Persistent_failures_back_off_stop_and_preserve_healthy_subscriptions(bool resourceLimit)
    {
        var fs = new MemoryDisk(); fs.Directories.UnionWith([P("good"), P("denied")]);
        var clock = new Clock(); using var watcher = new FileSetWatcher(fs, clock);
        int attempts = 0, changes = 0; watcher.Changed += (_, _) => changes++;
        fs.OnWatch = path => { if (path == P("denied")) { attempts++; if (resourceLimit) throw new IOException("watch limit"); throw new UnauthorizedAccessException(); } };
        watcher.SetPaths([P("good/file"), P("denied/file")]);
        var healthy = fs.Active.Single(s => s.Path == P("good"));
        // Fire includes the initial reconciliation hint and the first retry. Subsequent
        // maintenance-only turns must not emit Changed or reconstruct the healthy watcher.
        clock.Fire(); var hints = changes;
        for (var i = 0; i < 5; i++) clock.Fire();
        Assert.AreEqual(4, attempts); Assert.AreEqual(1, hints); Assert.AreEqual(hints, changes);
        Assert.AreEqual(0, clock.Pending); Assert.IsFalse(healthy.Disposed);
        Assert.AreEqual(1, fs.Active.Count(s => s.Path == P("good")));
        CollectionAssert.AreEqual(new[] { TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(4) },
            clock.Delays.Where(d => d >= TimeSpan.FromSeconds(1)).ToArray());
        fs.OnWatch = null; fs.Emit(Root, "denied", true); clock.Fire();
        Assert.IsTrue(fs.Active.Any(s => s.Path == P("denied")));
        Assert.AreEqual(hints + 1, changes); Assert.IsFalse(healthy.Disposed);
    }

    [TestMethod]
    [DataRow(false, 2, true)]
    [DataRow(false, 1, false)]
    [DataRow(true, 1, true)]
    public void Missing_segment_host_has_parent_coverage_subject_to_shared_rule_and_cap(bool shared, int cap, bool covered)
    {
        var fs = new MemoryDisk(); fs.Directories.Add(P("a"));
        if (shared) fs.Shared.Add(Root);
        var clock = new Clock(); using var watcher = new FileSetWatcher(fs, clock, maxDirectories: cap);
        watcher.SetPaths([P("a/b/file")]); clock.Fire();
        Assert.AreEqual(covered, fs.Active.Any(s => s.Path == P("a")));
        Assert.AreEqual(covered ? 0 : 1, watcher.DroppedDirectories);
        if (!covered || shared) return;
        var old = fs.Active.Single(s => s.Path == P("a"));
        fs.Emit(Root, "a", true); clock.Fire(); Assert.IsTrue(old.Disposed);
        fs.Directories.Add(P("a/b")); fs.Emit(P("a"), "b", true); clock.Fire();
        fs.Emit(P("a/b"), "file", false); Assert.AreEqual(1, clock.Pending);
    }

    private sealed class Clock : IFileSetWatchScheduler
    {
        private readonly List<Job> _jobs = [];
        public Action? Last;
        public List<TimeSpan> Delays = [];
        public int Pending => _jobs.Count(j => !j.Disposed);
        public IDisposable Schedule(TimeSpan delay, Action callback) { Delays.Add(delay); Last = callback; var j = new Job(callback); _jobs.Add(j); return j; }
        public void Fire() { var jobs = _jobs.ToArray(); _jobs.Clear(); foreach (var j in jobs) if (!j.Disposed) j.Callback(); }
    }
    private sealed class Job(Action callback) : IDisposable
    {
        public Action Callback = callback;
        public bool Disposed;
        public void Dispose() => Disposed = true;
    }
    private sealed class Subscription(string path, Action<string?, bool> callback) : IDisposable
    {
        public string Path = path;
        public Action<string?, bool> Callback = callback;
        public bool Disposed;
        public void Dispose() => Disposed = true;
    }
    private sealed class MemoryDisk : IFileSetWatchFileSystem
    {
        public HashSet<string> Directories = [Root];
        public HashSet<string> Shared = [];
        private readonly List<Subscription> _subscriptions = [];
        public IEnumerable<Subscription> Active => _subscriptions.Where(s => !s.Disposed);
        public int Created => _subscriptions.Count;
        public Action<string>? OnWatch;
        public Action<string>? OnDirectoryExists;
        public bool DirectoryExists(string path) { OnDirectoryExists?.Invoke(path); return Directories.Contains(path); }
        public bool IsSharedDirectory(string path) => Shared.Contains(path);
        public IDisposable WatchDirectory(string path, Action<string?, bool> changed) { OnWatch?.Invoke(path); if (!DirectoryExists(path)) throw new DirectoryNotFoundException(path); var s = new Subscription(path, changed); _subscriptions.Add(s); return s; }
        public void Emit(string path, string? name, bool structural) { foreach (var s in Active.Where(s => s.Path == path).ToArray()) s.Callback(name, structural); }
    }
}
