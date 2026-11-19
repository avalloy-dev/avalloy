namespace Avalloy.Preferences;

/// <summary>Metadata and directory notifications only; implementations must never open target files.</summary>
public interface IFileSetWatchFileSystem
{
    bool DirectoryExists(string path);
    bool IsSharedDirectory(string path);
    /// <summary>Subscribe before returning. A null name signals lost events; renames report both names.</summary>
    IDisposable WatchDirectory(string path, Action<string?, bool> changed);
}

/// <summary>Schedules a callback asynchronously, once. Disposing cancels it; callbacks may race cancellation.</summary>
public interface IFileSetWatchScheduler
{
    IDisposable Schedule(TimeSpan delay, Action callback);
}

/// <summary>Watches an ordered set of paths, including missing files and directories, without reading files.</summary>
/// <remarks>Changed runs on the scheduler thread; marshal to the UI in the handler. Each changed path set
/// schedules a reconciliation notification after subscribing, closing the caller's read/subscribe race.
/// Events are hints to reread, not a claim that file contents differ. Paths earlier in the set have
/// priority at the directory cap. Shared parents are excluded only from replacement monitoring.</remarks>
public sealed class FileSetWatcher : IDisposable
{
    private readonly object _gate = new();
    private readonly IFileSetWatchFileSystem _fs;
    private readonly IFileSetWatchScheduler _scheduler;
    private readonly TimeSpan _debounce;
    private readonly int _maxDirectories;
    private readonly Dictionary<string, Subscription> _subscriptions = new(StringComparer.Ordinal);
    private readonly HashSet<string> _failed = new(StringComparer.Ordinal);
    private IDisposable? _retry;
    private long _retryGeneration;
    private int _retryAttempts;
    private const int MaxRetries = 3;
    private const int MaxReconcilePasses = 4;
    private string[] _paths = [];
    private IDisposable? _pending;
    private long _generation;
    private bool _disposed;
    private bool _rebuild;

    public FileSetWatcher(IFileSetWatchFileSystem? fileSystem = null,
        IFileSetWatchScheduler? scheduler = null, TimeSpan? debounce = null, int maxDirectories = 64)
    {
        if (maxDirectories < 1) throw new ArgumentOutOfRangeException(nameof(maxDirectories));
        _debounce = debounce ?? TimeSpan.FromMilliseconds(250);
        if (_debounce < TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(debounce));
        _fs = fileSystem ?? new PhysicalFileSystem();
        _scheduler = scheduler ?? new TimerScheduler();
        _maxDirectories = maxDirectories;
    }

    public event EventHandler? Changed;
    /// <summary>Number of candidate directories omitted by the cap at the latest reconciliation.</summary>
    public int DroppedDirectories { get; private set; }

    public void SetPaths(IEnumerable<string> paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        var fullPaths = paths.Select(Path.GetFullPath).Distinct(StringComparer.Ordinal).ToArray();
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_paths.SequenceEqual(fullPaths, StringComparer.Ordinal)) return;
            _paths = fullPaths;
            ResetRetries();
            Reconcile();
            _rebuild = true;
            Schedule();
        }
    }

    private WatchPlan Plan() => WatchPlan.For(_paths, _fs.DirectoryExists, _maxDirectories, _fs.IsSharedDirectory);

    private void Reconcile()
    {
        var plan = Plan();
        // Revalidate after subscribing: an entire missing hierarchy may appear while a
        // subscription is being installed. Existing ancestors cover every intermediate pass.
        for (var pass = 0; pass < MaxReconcilePasses; pass++)
        {
            foreach (var directory in plan.Directories.OrderBy(d => d.Path.Length))
            {
                _subscriptions.TryGetValue(directory.Path, out var old);
                if (old is { Invalidated: false })
                {
                    old.Directory = Merge(old.Directory, directory);
                    continue;
                }
                if (_failed.Contains(directory.Path)) continue;
                var subscription = new Subscription(directory);
                try
                {
                    // Publish the pending subscription so parent events during installation
                    // invalidate it too. Keep the old handle alive until installation succeeds.
                    _subscriptions[directory.Path] = subscription;
                    subscription.Handle = _fs.WatchDirectory(directory.Path,
                        (name, structural) => OnEvent(subscription, name, structural));
                    old?.Dispose();
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException)
                {
                    subscription.Dispose();
                    if (old is null) _subscriptions.Remove(directory.Path);
                    else _subscriptions[directory.Path] = old;
                    // Disappearance changes the next plan immediately. An existing but inaccessible
                    // directory (or resource exhaustion) gets only the bounded background retries.
                    if (_fs.DirectoryExists(directory.Path))
                        _failed.Add(directory.Path);
                }
            }
            var next = Plan();
            if (SamePlan(plan, next))
            {
                FinishPlan(next);
                ScheduleRetry();
                return;
            }
            plan = next;
        }
        // Continuous topology churn must not monopolize the caller or open unbounded watchers.
        FinishPlan(plan);
        ScheduleRetry(force: true);
    }

    private static WatchedDirectory Merge(WatchedDirectory old, WatchedDirectory current) => new(current.Path,
        old.Names.Concat(current.Names).ToHashSet(WatchPlan.NameComparer),
        old.Subdirectories.Concat(current.Subdirectories).ToHashSet(WatchPlan.NameComparer));

    private static bool SamePlan(WatchPlan first, WatchPlan second) =>
        first.Directories.Count == second.Directories.Count && first.Directories.Zip(second.Directories)
            .All(pair => pair.First.Path == pair.Second.Path && pair.First.Names.SetEquals(pair.Second.Names)
                && pair.First.Subdirectories.SetEquals(pair.Second.Subdirectories));

    private void FinishPlan(WatchPlan plan)
    {
        DroppedDirectories = plan.Dropped;
        var wanted = plan.Directories.ToDictionary(d => d.Path, StringComparer.Ordinal);
        foreach (var path in _subscriptions.Keys.Where(p => !wanted.ContainsKey(p)).ToArray())
        {
            _subscriptions[path].Dispose();
            _subscriptions.Remove(path);
        }
        foreach (var pair in _subscriptions) pair.Value.Directory = wanted[pair.Key];
        _failed.RemoveWhere(p => !wanted.ContainsKey(p));
    }

    private void OnEvent(Subscription subscription, string? name, bool structural)
    {
        lock (_gate)
        {
            if (_disposed || !subscription.Active) return;
            var directory = subscription.Directory;
            if (name is not null && !React(directory, name, structural)) return;
            if (name is null) subscription.Invalidated = true;
            else if (structural)
            {
                var changed = Path.Combine(directory.Path, name);
                foreach (var pair in _subscriptions)
                    if (pair.Key.Equals(changed, StringComparison.OrdinalIgnoreCase) ||
                        pair.Key.StartsWith(changed + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                        pair.Value.Invalidated = true;
            }
            ResetRetries();
            _rebuild |= name is null || structural;
            Schedule();
        }
    }

    private void ResetRetries()
    {
        _retryGeneration++;
        _retry?.Dispose();
        _retry = null;
        _retryAttempts = 0;
        _failed.Clear();
    }

    private void ScheduleRetry(bool force = false)
    {
        if (_retry is not null || _retryAttempts >= MaxRetries || (!force && _failed.Count == 0)) return;
        var delay = TimeSpan.FromSeconds(1 << _retryAttempts++);
        var generation = ++_retryGeneration;
        _retry = _scheduler.Schedule(delay, () =>
        {
            lock (_gate)
            {
                if (_disposed || generation != _retryGeneration) return;
                _retry?.Dispose();
                _retry = null;
                _failed.Clear();
                Reconcile();
                // A retry is maintenance, not a filesystem notification.
            }
        });
    }

    private sealed class Subscription(WatchedDirectory directory) : IDisposable
    {
        public WatchedDirectory Directory = directory;
        public IDisposable? Handle;
        public bool Active = true;
        public bool Invalidated;
        public void Dispose()
        {
            Active = false;
            Handle?.Dispose();
        }
    }

    private bool React(WatchedDirectory directory, string name, bool structural)
    {
        if (!directory.Names.Contains(name)) return false;
        return !directory.Subdirectories.Contains(name) || structural ||
            !_fs.DirectoryExists(Path.Combine(directory.Path, name));
    }

    private void Schedule()
    {
        _pending?.Dispose();
        var generation = ++_generation;
        _pending = _scheduler.Schedule(_debounce, () =>
        {
            lock (_gate)
            {
                if (_disposed || generation != _generation) return;
                _pending?.Dispose();
                _pending = null;
                if (_rebuild)
                {
                    _rebuild = false;
                    Reconcile();
                }
                Changed?.Invoke(this, EventArgs.Empty);
            }
        });
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            _generation++;
            _pending?.Dispose();
            _retryGeneration++;
            _retry?.Dispose();
            foreach (var subscription in _subscriptions.Values) subscription.Dispose();
            _subscriptions.Clear();
        }
    }

    private sealed class TimerScheduler : IFileSetWatchScheduler
    {
        public IDisposable Schedule(TimeSpan delay, Action callback)
        {
            var timer = new Timer(_ => callback(), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
            timer.Change(delay, Timeout.InfiniteTimeSpan);
            return timer;
        }
    }

    internal sealed class PhysicalFileSystem : IFileSetWatchFileSystem
    {
        public bool DirectoryExists(string path) => Directory.Exists(path);
        public bool IsSharedDirectory(string path) => SharedDirectory.IsShared(path);
        public IDisposable WatchDirectory(string path, Action<string?, bool> changed)
        {
            var watcher = new FileSystemWatcher(path)
            {
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite | NotifyFilters.Size
            };
            watcher.Created += (_, e) => changed(e.Name, true);
            watcher.Deleted += (_, e) => changed(e.Name, true);
            watcher.Changed += (_, e) => changed(e.Name, false);
            watcher.Renamed += (_, e) => { changed(e.OldName, true); changed(e.Name, true); };
            watcher.Error += (_, _) => changed(null, true);
            try { watcher.EnableRaisingEvents = true; return watcher; }
            catch { watcher.Dispose(); throw; }
        }
    }
}
