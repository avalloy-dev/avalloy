namespace Avalloy.Preferences;

/// <summary>The change-detection seam <see cref="PreferencesService{T}"/> writes
/// through: it brackets its own writes in <see cref="SuppressEvents"/> so they are
/// not mistaken for external edits.</summary>
internal interface IFileChangeWatcher : IDisposable
{
    IDisposable SuppressEvents();
}

/// <summary>One-shot timers, injectable so the debounce can be driven without
/// waiting on the wall clock.</summary>
internal interface IWatcherScheduler
{
    /// <summary>Runs <paramref name="callback"/> once after
    /// <paramref name="dueMs"/>; disposing the result cancels it.</summary>
    IDisposable Schedule(int dueMs, Action callback);
}

internal sealed class TimerScheduler : IWatcherScheduler
{
    public static readonly TimerScheduler Instance = new();
    public IDisposable Schedule(int dueMs, Action callback) =>
        new Timer(_ => callback(), null, dueMs, Timeout.Infinite);
}

/// <summary>
/// Wraps <see cref="FileSystemWatcher"/> with two refinements that make it
/// usable for preference-file hot-reload:
/// <list type="bullet">
///   <item>Debounces bursts of FS events (rename/atomic-write sequences fire 2–3 events) into a single callback.</item>
///   <item>Lets callers suppress their own write-induced events via <see cref="SuppressEvents"/>.</item>
/// </list>
/// </summary>
/// <remarks>
/// Own writes are recognised by the file's state (existence, last-write time,
/// length) as it stood when the suppression ended, not by how long ago the write
/// was: FSEvents can deliver a write's event long after it happened, so any
/// event, poll or pending callback that finds the file still in that state is
/// ignored. Starting a suppression also cancels a callback already pending.
///
/// <para>Turning the <see cref="FileSystemWatcher"/> on is done in the background:
/// on macOS it starts an FSEvents stream and waits for its run loop, measured at
/// ~60 ms, which would otherwise sit on an app's startup path. The state poll
/// runs from construction, so a change made before it is up is still seen.</para>
/// </remarks>
public sealed class DebouncedFileWatcher : IFileChangeWatcher
{
    private readonly FileSystemWatcher? _watcher;
    private readonly Action _onChanged;
    private readonly Func<FileState> _readState;
    private readonly IWatcherScheduler _scheduler;
    private readonly int _debounceMs;
    private readonly int _pollMs;
    private readonly int _suppressionTailMs;
    private readonly object _gate = new();
    private IDisposable? _pending;
    private long _generation;
    private IDisposable? _pollTimer;
    private readonly List<IDisposable> _suppressionTimers = new();
    private int _suppressionDepth;
    private FileState _lastState;
    private FileState? _ownWriteState;
    private bool _disposed;
    // Guards turning the FileSystemWatcher on against disposing it. Separate from
    // _gate so a save never waits behind the slow start.
    private readonly object _fswGate = new();
    private bool _fswDisposed;

    /// <param name="suppressionTailMs">Extra time a suppression is held after it
    /// is disposed. Own writes are recognised by file state, so this is no longer
    /// needed and defaults to 0.</param>
    public DebouncedFileWatcher(string filePath, Action onChanged, int debounceMs = 250, int suppressionTailMs = 0)
        : this(filePath, onChanged, debounceMs, suppressionTailMs, readState: null, TimerScheduler.Instance, watchFileSystem: true)
    {
    }

    /// <summary>Test seam: <paramref name="readState"/> replaces reading the real
    /// file, and with <paramref name="watchFileSystem"/> false no
    /// <see cref="FileSystemWatcher"/> is started — drive it with
    /// <see cref="NotifyFileEvent"/> and the scheduler instead.</summary>
    internal DebouncedFileWatcher(string filePath, Action onChanged, int debounceMs, int suppressionTailMs,
        Func<FileState>? readState, IWatcherScheduler scheduler, bool watchFileSystem)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            throw new ArgumentException("filePath required", nameof(filePath));
        var dir = Path.GetDirectoryName(filePath);
        var file = Path.GetFileName(filePath);
        if (string.IsNullOrEmpty(dir) || string.IsNullOrEmpty(file))
            throw new ArgumentException($"Could not resolve directory + file from '{filePath}'.", nameof(filePath));

        _onChanged = onChanged;
        _readState = readState ?? (() => ReadState(filePath));
        _scheduler = scheduler;
        _debounceMs = debounceMs;
        _suppressionTailMs = suppressionTailMs;
        _lastState = _readState();

        if (watchFileSystem)
        {
            _watcher = new FileSystemWatcher(dir, file)
            {
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size,
            };
            _watcher.Changed += OnFileEvent;
            _watcher.Created += OnFileEvent;
            _watcher.Renamed += OnFileEvent;
            FileSystemWatchStarted = Task.Run(StartFileSystemWatch);
        }
        else
        {
            FileSystemWatchStarted = Task.FromResult(false);
        }

        _pollMs = Math.Clamp(debounceMs / 2, 50, 250);
        _pollTimer = _scheduler.Schedule(_pollMs, PollTick);
    }

    /// <summary>Completes once the background start of the
    /// <see cref="FileSystemWatcher"/> has finished: true if it is raising events,
    /// false if it failed, was disposed first, or there is none.</summary>
    internal Task<bool> FileSystemWatchStarted { get; }

    /// <summary>Whether the <see cref="FileSystemWatcher"/> is raising events now.</summary>
    internal bool IsWatchingFileSystem
    {
        get { lock (_fswGate) return !_fswDisposed && _watcher?.EnableRaisingEvents == true; }
    }

    private bool StartFileSystemWatch()
    {
        lock (_fswGate)
        {
            if (_fswDisposed) return false;
            try
            {
                _watcher!.EnableRaisingEvents = true;
                return true;
            }
            catch (Exception)
            {
                // Out of watch handles, directory gone, and the like: the state
                // poll still sees changes, a poll interval later.
                return false;
            }
        }
    }

    /// <summary>
    /// Suppresses callbacks until the returned IDisposable is disposed, and
    /// cancels one already pending. Safe to nest; callbacks resume only when the
    /// outermost scope disposes. The file's state at that point is remembered as
    /// this caller's own write, so late events for it are ignored too.
    /// </summary>
    public IDisposable SuppressEvents()
    {
        lock (_gate)
        {
            _suppressionDepth++;
            CancelPendingLocked();
        }
        return new Suppression(this);
    }

    private void OnFileEvent(object sender, FileSystemEventArgs e) => NotifyFileEvent();

    /// <summary>What a <see cref="FileSystemWatcher"/> event does; internal so
    /// tests can raise one.</summary>
    internal void NotifyFileEvent()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _lastState = _readState();
            if (_suppressionDepth > 0 || _lastState == _ownWriteState) return;
            ScheduleCallbackLocked();
        }
    }

    /// <summary>One state poll; internal so tests can run one.</summary>
    internal void Poll()
    {
        lock (_gate)
        {
            if (_disposed) return;
            var state = _readState();
            if (state == _lastState) return;

            _lastState = state;
            if (_suppressionDepth > 0 || state == _ownWriteState) return;
            ScheduleCallbackLocked();
        }
    }

    private void PollTick()
    {
        Poll();
        lock (_gate)
        {
            if (_disposed) return;
            _pollTimer?.Dispose();
            _pollTimer = _scheduler.Schedule(_pollMs, PollTick);
        }
    }

    private void ScheduleCallbackLocked()
    {
        CancelPendingLocked();
        var generation = _generation;
        _pending = _scheduler.Schedule(_debounceMs, () => Fire(generation));
    }

    private void CancelPendingLocked()
    {
        // Bumping the generation also stops a timer whose callback is already
        // queued on the thread pool, which disposing it cannot.
        _generation++;
        _pending?.Dispose();
        _pending = null;
    }

    private void Fire(long generation)
    {
        lock (_gate)
        {
            if (_disposed || generation != _generation || _suppressionDepth > 0) return;
            _pending = null;
            // Re-check at fire time: a write of our own may have landed since the
            // event that scheduled this.
            _lastState = _readState();
            if (_lastState == _ownWriteState) return;
        }
        _onChanged();
    }

    /// <summary>Stops the watcher. No callback starts after this returns; one
    /// already running is not waited for.</summary>
    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            CancelPendingLocked();
            _pollTimer?.Dispose();
            _pollTimer = null;
foreach (var timer in _suppressionTimers)
                timer.Dispose();
            _suppressionTimers.Clear();
        }
        lock (_fswGate)
        {
            _fswDisposed = true;
            _watcher?.Dispose();
        }
    }

    private static FileState ReadState(string path)
    {
        try
        {
            var info = new FileInfo(path);
            return info.Exists
                ? new FileState(true, info.LastWriteTimeUtc.Ticks, info.Length)
                : new FileState(false, 0, 0);
        }
        catch
        {
            return new FileState(false, 0, 0);
        }
    }

    internal readonly record struct FileState(bool Exists, long LastWriteTicks, long Length);

    private void EndSuppression()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _lastState = _readState();
            _ownWriteState = _lastState;
        }

        if (_suppressionTailMs <= 0)
        {
            ReleaseSuppression();
            return;
        }

        IDisposable? timer = null;
        lock (_gate)
        {
            timer = _scheduler.Schedule(_suppressionTailMs, () =>
            {
                lock (_gate) _suppressionTimers.Remove(timer!);
                timer!.Dispose();
                ReleaseSuppression();
            });
            _suppressionTimers.Add(timer);
        }
    }

    private void ReleaseSuppression()
    {
        lock (_gate)
        {
            if (_suppressionDepth > 0)
                _suppressionDepth--;
        }
    }

    private sealed class Suppression : IDisposable
    {
        private readonly DebouncedFileWatcher _owner;
        private int _disposed;
        public Suppression(DebouncedFileWatcher owner) { _owner = owner; }
        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 1) return;
            _owner.EndSuppression();
        }
    }
}
