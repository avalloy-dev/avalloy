using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.IO.Abstractions;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Avalloy.Services;
using Microsoft.Extensions.Logging;

namespace Avalloy.Preferences;

/// <summary>
/// Owns the live <typeparamref name="T"/> preferences model, persists it to
/// <c>preferences.json</c>, and reloads when the file is edited externally.
/// </summary>
/// <typeparam name="T">App preferences root POCO. Implements
/// <see cref="IPreferencesModel"/> so the service can hook mutation tracking
/// without knowing the section layout.</typeparam>
public sealed class PreferencesService<T> : IDisposable
    where T : class, IPreferencesModel, new()
{
    // Locks. Always taken in this order, never the other way round:
    //   _lifecycleGate  The state changes of Relocate and Dispose (the disposal check, cancelling the
    //                   delayed save, moving the path and watcher, stopping the watcher), so the two
    //                   never interleave. Released before either runs a reload or a write.
    //   _ioGate         Reading and writing the file, and the path and watcher reference they use.
    //   _gate           The save timer, the save counters, the snapshot number and the swap of
    //                   Current. Held briefly; nothing is taken inside it.
    // No code from outside the service runs while it holds ANY of them: not the model (its
    // constructor, getters and setters, so no serializing or deserializing), not the
    // PreferencesReloaded/Mutated handlers, not the logger. All of those can call back into the
    // service — Dispose from a getter, say — and doing so under a lock is how a lock order gets
    // reversed. The file system and the watcher are not outside code here: a watcher's own lock is a
    // leaf (it never calls back into the service while holding it), so SuppressEvents runs under _ioGate.
    //
    // Lifecycle. A save (WriteCurrent), a reload (Reload), a Relocate and Dispose's own work are each an
    // operation, counted per thread (_operationDepth). Dispose called while its own thread is inside
    // an operation — from a getter, setter or handler that operation runs — is deferred: it never
    // waits and never flushes there. It raises the disposing flag (so nothing new is saved and no new
    // Relocate starts) and the outermost operation on that thread runs the real Dispose on its way
    // out, once it has let go of everything. A Dispose from any other thread waits for the real one to
    // finish. An operation never waits on a nested Dispose, so no thread waits on one waiting on it.
    private readonly object _lifecycleGate = new();
    private string _prefsPath;
    private readonly string _fileName;
    private readonly TimeSpan _saveDebounce;
    private readonly bool _watch;
    private readonly object _gate = new();
    // From the app's source-generated resolver when it passed one, else from
    // reflection. Every (de)serialization goes through these.
    private readonly JsonTypeInfo<T> _readTypeInfo;
    private readonly JsonTypeInfo<T> _writeTypeInfo;

    private const string ReflectionMessage =
        "Reads and writes the model through reflection. Pass a source-generated JsonSerializerContext " +
        "to the IJsonTypeInfoResolver overload instead.";
    private readonly IFileSystem _io;
    private readonly Func<string, Action, IFileChangeWatcher?> _watcherFactory;
    // Serialises writing the file against reading it for an external-change
    // reload, so a reload never reads the file from just before a save.
    private readonly object _ioGate = new();
    private readonly IWatcherScheduler _scheduler;
    // Null outside tests: resolved from AlloyLogging when there is something to log, so a service
    // built before the app publishes its logger factory doesn't pin a NullLogger.
    private readonly ILogger? _logger;
    private IFileChangeWatcher? _watcher;
    private IDisposable? _saveTimer;
    // RequestSave calls so far, and the latest of them a write has covered; both under _gate.
    // A save is owed while they differ.
    private long _saveRequests;
    private long _savedRequests;
    // Bumped by every schedule and every cancel, under _gate. A delayed save writes only while its
    // own schedule is still the current one — checked when it fires and again under _ioGate as it
    // writes — so one cancelled at any point before it writes does nothing.
    private long _saveGeneration;
    // Set by the first Dispose, deferred or not: from then on nothing new is saved and no Relocate
    // starts. _disposed is only set when the real Dispose is done, and _disposeDone with it.
    private int _disposing;
    private readonly ManualResetEventSlim _disposeDone = new();
    // Operations running on each thread (see Lifecycle above), and the thread whose outermost
    // operation owes the real Dispose because a Dispose was deferred on it.
    private readonly ThreadLocal<int> _operationDepth = new();
    private int _deferredDisposeThread;
    // The thread running the real Dispose, while it runs: its own waits are not a Dispose waiting
    // inside an operation.
    private int _disposeCoreThread;
    // Relocations in flight, from the move until the end of the reload or write that follows it;
    // under _gate. The real Dispose waits, holding no lock, for _relocationIdle before it flushes, so
    // its flush never writes the old model to a path whose file a Relocate is adopting. It never runs
    // inside a Relocate (a Dispose there is deferred), so it never waits for its own.
    private int _relocations;
    private readonly ManualResetEventSlim _relocationIdle = new(true);
    // True from a Relocate(reload:true)'s move until the reload adopts the target, under _gate. Current
    // is still the model being replaced then, and the target file wins: its mutations are discarded,
    // not saved.
    private bool _discardingMutations;
    private readonly PropertyChangedEventHandler _sectionHandler;
    // Bumped under _ioGate by every write this service makes to the file and by every Relocate:
    // how a reload tells that the file it read is no longer the file, and reads again.
    private long _fileVersion;
    // A save takes the next snapshot number and the model to serialize together, under _gate; a reload
    // takes one in the same _gate hold in which it replaces Current. So a higher number is always a
    // later model. The highest that has reached the file — written, or adopted by a reload — is
    // _writtenSnapshot, under _ioGate; a write holding a lower number is skipped.
    private long _snapshots;
    private long _writtenSnapshot;
    /// <summary>
    /// The exact text the file is believed to hold, as last written or last read by this service.
    ///
    /// This is what tells our own write apart from somebody else's edit, and it has to be the
    /// content rather than a timer: on macOS FSEvents are dispatched asynchronously and can arrive
    /// long after the write, and a watcher has no way to know whose write it is looking at.
    /// Reloading on a late echo replaces <see cref="Current"/> with content that is already ours and
    /// silently discards every mutation made since the save — which, with a 300ms save debounce
    /// behind a settings screen, is whatever the user typed last.
    /// </summary>
    private string? _syncedContent;
    private bool _disposed;

    public T Current { get; private set; } = new();

    /// <summary>Fires after <see cref="Current"/> has been replaced from an
    /// external file change or programmatic reload.</summary>
    public event EventHandler? PreferencesReloaded;

    /// <summary>Fires when any nested
    /// <see cref="INotifyPropertyChanged"/> section on <see cref="Current"/>
    /// mutates. Triggers a debounced save and lets consumers re-apply derived
    /// resources.</summary>
    public event EventHandler? PreferencesMutated;

    /// <param name="fileName">File to persist, resolved relative to
    /// <paramref name="baseDir"/>. Defaults to <c>preferences.json</c>. Pass a
    /// nested path (e.g. <c>machines/foo.macos.json</c>) to write into a
    /// subdirectory — the parent is created on <see cref="Initialize"/>.</param>
    /// <param name="watch">When false, no external-change watcher is started —
    /// the model is load-once / write-only. Used for the per-machine state file,
    /// which has no remote writer and whose backing objects are captured by
    /// view-models, so a runtime reload that swapped them would orphan those
    /// references.</param>
    [RequiresUnreferencedCode(ReflectionMessage)]
    [RequiresDynamicCode(ReflectionMessage)]
    public PreferencesService(string baseDir, TimeSpan? saveDebounce = null, IFileSystem? io = null,
        string fileName = "preferences.json", bool watch = true)
        : this(baseDir, saveDebounce, io, fileName, watch, typeInfoResolver: null, watcherFactory: null)
    {
    }

    /// <summary>
    /// As the other constructor, but (de)serializes <typeparamref name="T"/>
    /// through <paramref name="typeInfoResolver"/> instead of reflection — pass
    /// a source-generated context's <c>Default</c>. That removes the
    /// serializer's first-use warm-up from the startup path, and is what
    /// trimming and Native AOT need.
    /// </summary>
    /// <param name="typeInfoResolver">Metadata for <typeparamref name="T"/> and
    /// every type reachable from it, typically
    /// <c>[JsonSerializable(typeof(MyPrefs))] partial class MyPrefsJsonContext :
    /// JsonSerializerContext</c>. Only its metadata is used: the file format
    /// (camelCase names and enums, comments and trailing commas accepted, nulls
    /// written) is the service's own, whatever options the context declares.
    /// Throws here if it has no metadata for <typeparamref name="T"/>.</param>
    /// <remarks>Enums are not given a converter on this path, because the one
    /// that serves every enum needs runtime code generation. Each enum the model
    /// reaches declares its own instead —
    /// <c>[JsonConverter(typeof(CamelCaseEnumConverter&lt;MyEnum&gt;))]</c> — and one
    /// that does not would be written as a number, so construction throws
    /// naming it. Properties another source generator writes are invisible to
    /// the JSON one, so CommunityToolkit.Mvvm's <c>[ObservableProperty]</c> goes
    /// on <c>public partial</c> properties in a model the app compiles, not on
    /// fields. A type left with no properties at all throws here; one that loses
    /// only some of them cannot be told apart from a model that has fewer.
    /// <see cref="AppearancePrefs"/> is not affected: it arrives compiled.</remarks>
    public PreferencesService(string baseDir, IJsonTypeInfoResolver typeInfoResolver, TimeSpan? saveDebounce = null,
        IFileSystem? io = null, string fileName = "preferences.json", bool watch = true)
        : this(baseDir, saveDebounce, io, fileName, watch,
            typeInfoResolver ?? throw new ArgumentNullException(nameof(typeInfoResolver)), watcherFactory: null)
    {
    }

    /// <summary>Test seam: <paramref name="watcherFactory"/> builds the
    /// change watcher for a path and callback, in place of a
    /// <see cref="DebouncedFileWatcher"/> on the real disk;
    /// <paramref name="scheduler"/> runs the debounced save in place of a timer, and
    /// <paramref name="logger"/> receives what would go to <see cref="AlloyLogging"/>.</summary>
    [UnconditionalSuppressMessage("Trimming", "IL2026", Justification =
        "Reflection is used only when typeInfoResolver is null, which the public constructor that allows it is annotated for.")]
    [UnconditionalSuppressMessage("AOT", "IL3050", Justification =
        "Reflection is used only when typeInfoResolver is null, which the public constructor that allows it is annotated for.")]
    internal PreferencesService(string baseDir, TimeSpan? saveDebounce, IFileSystem? io,
        string fileName, bool watch, IJsonTypeInfoResolver? typeInfoResolver,
        Func<string, Action, IFileChangeWatcher?>? watcherFactory,
        IWatcherScheduler? scheduler = null, ILogger? logger = null)
    {
        _io = io ?? new System.IO.Abstractions.FileSystem();
        _watcherFactory = watcherFactory ?? DefaultWatcher;
        _sectionHandler = OnSectionMutated;
        _scheduler = scheduler ?? TimerScheduler.Instance;
        _logger = logger;
        BaseDirectory = baseDir;
        _fileName = fileName;
        _prefsPath = _io.Path.Combine(baseDir, fileName);
        _saveDebounce = saveDebounce ?? TimeSpan.FromMilliseconds(300);
        _watch = watch;

        var readOptions = new JsonSerializerOptions
        {
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
            PropertyNameCaseInsensitive = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        };
        var writeOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            // Always write nulls so the JSON serves as discovery/documentation
            // for all available settings, not just the ones the user touched.
            DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        };

        var reflection = typeInfoResolver is null;
        if (reflection)
        {
            UseReflection(readOptions);
            UseReflection(writeOptions);
        }
        else
        {
            readOptions.TypeInfoResolver = typeInfoResolver;
            writeOptions.TypeInfoResolver = typeInfoResolver;
        }
        _readTypeInfo = TypeInfoFor(readOptions);
        _writeTypeInfo = TypeInfoFor(writeOptions);
        if (!reflection)
            ContextFormatGuard.Require(_writeTypeInfo, _fileName);
    }

    [RequiresUnreferencedCode(ReflectionMessage)]
    [RequiresDynamicCode(ReflectionMessage)]
    private static void UseReflection(JsonSerializerOptions options)
    {
        options.TypeInfoResolver = new DefaultJsonTypeInfoResolver();
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
    }

    private static JsonTypeInfo<T> TypeInfoFor(JsonSerializerOptions options)
    {
        try
        {
            return (JsonTypeInfo<T>)options.GetTypeInfo(typeof(T));
        }
        catch (NotSupportedException ex)
        {
            throw new ArgumentException(
                $"{options.TypeInfoResolver!.GetType().Name} has no JSON metadata for {typeof(T).Name}; add [JsonSerializable(typeof({typeof(T).Name}))] to it.",
                "typeInfoResolver", ex);
        }
    }

    public string BaseDirectory { get; private set; }
    public string PreferencesPath => _prefsPath;

    /// <summary>
    /// Re-point the service at a different directory at runtime, so a
    /// preferences-location change takes effect without an app restart.
    /// Does nothing once <see cref="Dispose"/> has begun.
    /// </summary>
    /// <param name="newBaseDir">The directory to read/write
    /// <see cref="PreferencesPath"/> from now on.</param>
    /// <param name="reload">When true, <see cref="Load"/> the file at the new
    /// location, replacing <see cref="Current"/> and raising
    /// <see cref="PreferencesReloaded"/> so consumers re-apply — use this when
    /// adopting settings that already live at the target. When false,
    /// <see cref="Current"/> is preserved (its object graph stays intact for
    /// anything holding references to it) and written out to the new location;
    /// use this when the target was just seeded with the current settings, or
    /// for the per-machine state file whose sections are captured by
    /// view-models. With reload, changes made to the current model while the
    /// reload is under way are discarded: the file at the target wins.</param>
    public void Relocate(string newBaseDir, bool reload)
    {
        BeginOperation();
        try { RelocateCore(newBaseDir, reload); }
        finally { EndOperation(); }
    }

    private void RelocateCore(string newBaseDir, bool reload)
    {
        // All or nothing. What can fail before anything moves is prepared first; the move itself
        // cannot fail; and if the reload or write after it fails before adopting anything, the move is
        // undone.
        var newPath = _io.Path.Combine(newBaseDir, _fileName);
        IFileChangeWatcher? watcher;
        (string BaseDirectory, string Path, IFileChangeWatcher? Watcher) previous;
        long savedBefore, givenUpTo = -1;
        T modelBefore;
        try
        {
            lock (_lifecycleGate)
            {
                // Once Dispose has begun this does nothing, as SaveNow does. Dispose raises its flag
                // before it takes this gate, so either this move is done before Dispose changes
                // anything, or this sees the flag and changes nothing.
                if (IsDisposing) return;

                // Prepared while nothing has moved: a failure here leaves the service where it was,
                // with a delayed save still waiting still scheduled.
                var dir = _io.Path.GetDirectoryName(newPath);
                if (!string.IsNullOrEmpty(dir)) _io.Directory.CreateDirectory(dir);
                // Watching before the write below, so it is suppressed as our own.
                watcher = _watch ? _watcherFactory(newPath, OnFileExternallyChanged) : null;

                // The move, which cannot fail. A delayed save cancelled here is dropped when it comes
                // to write, however far it got, and Relocate is the write instead: the current settings
                // at the new location, or with reload, the file already there.
                lock (_gate)
                {
                    savedBefore = _savedRequests;
                    if (reload)
                    {
                        // The file at the new location wins: the change still waiting gives way to it,
                        // so no flush can write the old model over the file being adopted.
                        givenUpTo = _saveRequests;
                        _savedRequests = _saveRequests;
                    }
                    else
                    {
                        // The new location owes the settings until a write lands there.
                        _saveRequests++;
                    }
                    // In flight until the end of this Relocate: Dispose waits for it before flushing.
                    if (_relocations++ == 0) _relocationIdle.Reset();
                    // Until the reload adopts the target, Current is the model being replaced.
                    if (reload) _discardingMutations = true;
                }
                CancelDelayedSave();
                previous = SwapLocation(newBaseDir, newPath, watcher);
                modelBefore = Current;
            }
        }
        catch (Exception ex)
        {
            LogFailure(ex, "could not move to " + newPath + "; staying here");
            throw;
        }

        // Holding no lock: the reload and the write run the model's code. Adopting the new location's
        // file is the commit point: after it, nothing is rolled back, and the old watcher is let go
        // whatever happens.
        var movedBack = false;
        try
        {
            try
            {
                // Relocate's own reload finishes even if a Dispose begins meanwhile: the move is done,
                // and the change that was waiting already gave way to the target.
                if (reload) Reload(onlyIfChanged: false, forRelocate: true);
                else WriteCurrent();
            }
            catch (Exception ex) when (!reload || ReferenceEquals(Current, modelBefore))
            {
                lock (_gate) _discardingMutations = false;   // the old model stays: it saves again
                // Nothing was adopted (a model constructor that throws, a write that fails): back to
                // the old location and its watcher, with the change still owed there. A Dispose that
                // began meanwhile has already stopped the new watcher; then there is nowhere to go
                // back to.
                lock (_lifecycleGate)
                {
                    if (!IsDisposing)
                    {
                        SwapLocation(previous.BaseDirectory, previous.Path, previous.Watcher);
                        movedBack = true;
                    }
                }
                if (movedBack)
                {
                    DisposeWatcher(watcher);
                    bool owed;
                    lock (_gate)
                    {
                        // The change given up for the reload is owed again, here.
                        if (_savedRequests == givenUpTo) _savedRequests = savedBefore;
                        owed = _savedRequests != _saveRequests;
                    }
                    if (owed) RequestSave();
                }
                LogFailure(ex, "could not move to " + newPath + (movedBack ? "; moved back" : ""));
                throw;
            }
        }
        finally
        {
            // Unless the move was undone, the old watcher goes: on success, and after a failure past
            // the commit point.
            if (!movedBack) DisposeWatcher(previous.Watcher);
            lock (_gate)
            {
                if (--_relocations == 0) _relocationIdle.Set();
            }
        }
    }

    /// <summary>Moves the path and the watcher together, under <c>_ioGate</c> where a reload and a
    /// write read them, and returns what they were. The new version sends a reload that read the old
    /// file to read again. Cannot fail.</summary>
    private (string BaseDirectory, string Path, IFileChangeWatcher? Watcher) SwapLocation(
        string baseDirectory, string path, IFileChangeWatcher? watcher)
    {
        lock (_ioGate)
        {
            InsideSwapForTests?.Invoke();
            var previous = (BaseDirectory, _prefsPath, _watcher);
            BaseDirectory = baseDirectory;
            _prefsPath = path;
            _watcher = watcher;
            _fileVersion++;
            return previous;
        }
    }

    /// <summary>A watcher that fails to stop is logged, never thrown: it must not leave a move half done.</summary>
    private void DisposeWatcher(IFileChangeWatcher? watcher)
    {
        try { watcher?.Dispose(); }
        catch (Exception ex)
        {
            LogFailure(ex, "the file watcher failed to stop");
        }
    }

    /// <summary>If non-null, the most recent <see cref="Load"/> couldn't parse
    /// the file and defaults were used instead.</summary>
    public Exception? LastLoadError { get; private set; }

    /// <summary>Creates the base directory if missing and loads (or seeds)
    /// preferences.json. Starts the watcher.</summary>
    public void Initialize()
    {
        // The file may live in a subdirectory of BaseDirectory (e.g. the
        // per-machine state file under machines/); create the immediate parent
        // rather than only BaseDirectory.
        var dir = _io.Path.GetDirectoryName(_prefsPath);
        if (!string.IsNullOrEmpty(dir)) _io.Directory.CreateDirectory(dir);
        // Watch before Load, which writes defaults when the file is missing:
        // that write has to be suppressed as our own like any other save.
        StartWatching();
        Load();
    }

    /// <summary>
    /// Reload from disk, replacing <see cref="Current"/>. Forgiving — if the
    /// file is missing, empty, malformed, or has an invalid enum value,
    /// <see cref="Current"/> falls back to defaults rather than throwing.
    /// Errors are exposed via <see cref="LastLoadError"/>. Does nothing once
    /// <see cref="Dispose"/> has begun, deferred or not: the model is being
    /// thrown away, and replacing it would lose a change Dispose is about to flush.
    /// </summary>
    public void Load() => Reload(onlyIfChanged: false);

    /// <param name="onlyIfChanged">When true (an external-change notification),
    /// do nothing if the file still holds what this service last wrote or read.</param>
    /// <remarks>
    /// The model's own code — its constructor, setters while deserializing, getters while seeding —
    /// runs outside <c>_ioGate</c>. Model code can call back into the service (Dispose, say), and doing
    /// that while holding the IO lock inverts the lock order against a delayed save waiting for it.
    /// So the file is read under the lock, the model built without it, and the lock taken again to
    /// adopt it; a save that landed in between means the read is stale, and it starts over.
    /// </remarks>
    /// <param name="forRelocate">Relocate's own reload, which adopts the target even once a Dispose has
    /// begun.</param>
    private void Reload(bool onlyIfChanged, bool forRelocate = false)
    {
        BeginOperation();
        try { ReloadCore(onlyIfChanged, forRelocate); }
        finally { EndOperation(); }
    }

    private void ReloadCore(bool onlyIfChanged, bool forRelocate)
    {
        var retrying = false;
        while (true)
        {
            if (retrying) BeforeReloadRetriesForTests?.Invoke();
            retrying = true;
            string? text = null;
            Exception? error = null;
            bool exists;
            long version;

            lock (_ioGate)
            {
                // Once Dispose has begun, a reload of any kind changes nothing (see Load).
                if (IsDisposing && !forRelocate) return;
                version = _fileVersion;
                exists = _io.File.Exists(_prefsPath);
                if (exists)
                {
                    try
                    {
                        text = _io.File.ReadAllText(_prefsPath);
                    }
                    catch when (onlyIfChanged)
                    {
                        // Caught mid-write. There is nothing to learn from a partial file, and reloading
                        // one is actively harmful — a truncated document does not parse, and this would
                        // fall back to defaults. The watcher polls, so a settled file raises the question
                        // again.
                        return;
                    }
                    catch (Exception ex)
                    {
                        error = ex;
                    }

                    if (onlyIfChanged && text == _syncedContent) return;
                }
            }

            T next = new();
            if (error is null && !string.IsNullOrWhiteSpace(text))
            {
                try
                {
                    next = JsonSerializer.Deserialize(text, _readTypeInfo) ?? new T();
                }
                catch (Exception ex)
                {
                    // Malformed JSON, invalid enum — fall back to defaults.
                    error = ex;
                }
            }
            var seed = exists ? null : JsonSerializer.Serialize(next, _writeTypeInfo);
            // Wired before it is visible, so no mutation of the adopted model goes unsaved.
            next.RegisterMutationHandler(_sectionHandler);

            T previous = null!;
            var adopted = false;
            try
            {
                lock (_ioGate)
                {
                    if (IsDisposing && !forRelocate) return;   // begun meanwhile: no seed, no adoption
                    if (_fileVersion != version) continue;   // saved or moved since the read: read again

                    if (seed is not null)
                    {
                        // The defaults are written before Current changes, so no save can take the
                        // default model unless they are on disk.
                        long seedSnapshot;
                        lock (_gate) seedSnapshot = ++_snapshots;
                        if (!TryCreateFileLocked(seed, seedSnapshot))
                        {
                            // The file appeared since it was found missing: load it rather than write
                            // defaults over it. A save that took the model while this ran is older than
                            // that file, which the retry adopts in its place.
                            long appeared;
                            lock (_gate) appeared = ++_snapshots;   // a number of its own: later than every save's
                            _writtenSnapshot = Math.Max(_writtenSnapshot, appeared);
                            continue;
                        }
                        // A seed writes defaults, not the model any pending save request was for: it
                        // never counts as that save.
                    }

                    // Current and its snapshot number change together (see _snapshots): a save that
                    // took the model this replaces holds a lower number, and cannot write over the
                    // file adopted (or seeded) here.
                    long snapshot;
                    lock (_gate)
                    {
                        previous = Current;
                        Current = next;
                        snapshot = ++_snapshots;
                        // The model being replaced is gone now: the adopted one's mutations save.
                        _discardingMutations = false;
                    }
                    _writtenSnapshot = Math.Max(_writtenSnapshot, snapshot);
                    if (seed is null) _syncedContent = text;

                    LastLoadError = error;
                    adopted = true;
                }
            }
            finally
            {
                if (!adopted) next.UnregisterMutationHandler(_sectionHandler);
            }

            previous.UnregisterMutationHandler(_sectionHandler);
            // A handler is outside code: one that throws is logged, as a failed save is, and does not
            // undo the reload or abort what called it.
            try { PreferencesReloaded?.Invoke(this, EventArgs.Empty); }
            catch (Exception ex)
            {
                LogFailure(ex, "a PreferencesReloaded handler threw");
            }
            return;
        }
    }

    private void OnSectionMutated(object? sender, PropertyChangedEventArgs e)
    {
        // A change to the model a Relocate(reload:true) is replacing is discarded: the target wins.
        bool discard;
        lock (_gate) discard = _discardingMutations;
        if (!discard) RequestSave();
        PreferencesMutated?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Schedule a debounced save. Multiple rapid calls coalesce into
    /// one write. Does nothing once <see cref="Dispose"/> has begun, deferred or
    /// not: the model is being thrown away.</summary>
    public void RequestSave()
    {
        if (IsDisposing) return;
        lock (_gate)
        {
            ++_saveRequests;
            var generation = ++_saveGeneration;
            _saveTimer?.Dispose();
            _saveTimer = _scheduler.Schedule((int)Math.Min(_saveDebounce.TotalMilliseconds, int.MaxValue),
                () => SaveFromTimer(generation));
        }
    }

    /// <summary>
    /// The debounced save. It runs on a thread-pool timer thread, where an exception has nobody to
    /// catch it and ends the process, so it never throws: a folder that is gone, a file another
    /// process holds open (antivirus, on Windows), or a model that fails to serialize costs this one
    /// save, not the app.
    /// </summary>
    private void SaveFromTimer(long generation)
    {
        try
        {
            // Cancelled or superseded: Dispose, Relocate or a later RequestSave owns the write now.
            // Checked again as it writes, so a cancel at any point before then still holds.
            lock (_gate) if (generation != _saveGeneration) return;
            WriteCurrent(generation: generation);
        }
        catch (Exception ex)
        {
            LogFailure(ex, "the delayed save was dropped");
        }
    }

    private void CancelDelayedSave()
    {
        lock (_gate)
        {
            _saveGeneration++;
            _saveTimer?.Dispose();
            _saveTimer = null;
        }
    }

    private bool IsDisposing => Volatile.Read(ref _disposing) != 0;

    /// <summary>Tests only: whether the calling thread holds any of the service's locks. How a test
    /// sees that the model's code never runs under one, without waiting to see whether something
    /// deadlocks.</summary>
    internal bool HoldsAnyLockForTests =>
        Monitor.IsEntered(_lifecycleGate) || Monitor.IsEntered(_ioGate) || Monitor.IsEntered(_gate);

    /// <summary>Tests only: runs inside the move of the path and watcher, holding the locks it takes.
    /// How a test parks a Relocate mid-move.</summary>
    internal Action? InsideSwapForTests { get; set; }

    /// <summary>Tests only: runs just before a Dispose waits, for another Dispose or for a Relocate in
    /// flight. A test that expects no wait throws here, so a wrong wait fails at once instead of
    /// hanging.</summary>
    internal Action? BeforeDisposeWaitsForTests { get; set; }

    /// <summary>Tests only: whether the calling thread is inside one of this service's operations.
    /// A Dispose that waits there would wait on itself.</summary>
    internal bool InOperationForTests =>
        _operationDepth.Value > (Volatile.Read(ref _disposeCoreThread) == Environment.CurrentManagedThreadId ? 1 : 0);

    /// <summary>Tests only: runs, holding no lock, just before a reload reads the file again because
    /// what it read went stale (a save landed, the file moved, or it appeared).</summary>
    internal Action? BeforeReloadRetriesForTests { get; set; }

    /// <summary>Logs a save, or a teardown, that failed. Never throws: it is called where an exception
    /// would end the process or escape Dispose, and a logger that fails (a provider that throws, a
    /// factory disposed during shutdown) must not be what does that.</summary>
    private void LogFailure(Exception ex, string outcome)
    {
        try
        {
            (_logger ?? AlloyLogging.CreateLogger("Avalloy.Preferences"))
                .LogWarning(ex, "Preferences {Path}: {Outcome}", _prefsPath, outcome);
        }
        catch
        {
            // Nowhere left to report it.
        }
    }

    /// <summary>Write the current model to disk immediately, suppressing the
    /// watcher event for the duration. Does nothing once the service is disposed:
    /// <see cref="Dispose"/> has already written the last change it saw.</summary>
    public void SaveNow() => WriteCurrent();

    /// <summary>Writes <see cref="Current"/>: SaveNow, the delayed save, Relocate's write and
    /// Dispose's flush. The model is serialized holding no lock, since serializing runs its getters.</summary>
    /// <param name="isDisposeFlush">Dispose's own last write, the one save that runs while disposing.</param>
    /// <param name="generation">The delayed save's schedule: it writes only while that is still current.</param>
    private void WriteCurrent(bool isDisposeFlush = false, long? generation = null)
    {
        BeginOperation();
        try { WriteCurrentCore(isDisposeFlush, generation); }
        finally { EndOperation(); }
    }

    private void WriteCurrentCore(bool isDisposeFlush, long? generation)
    {
        // Before serializing, so a save that starts once Dispose has begun does no work at all.
        if (!isDisposeFlush && IsDisposing) return;
        long request, snapshot;
        T prefs;
        // The model and its snapshot number together, in the hold a reload swaps Current in, so a
        // higher number is always a later model.
        lock (_gate)
        {
            request = _saveRequests;
            snapshot = ++_snapshots;
            prefs = Current;
        }
        var json = JsonSerializer.Serialize(prefs, _writeTypeInfo);
        lock (_ioGate)
        {
            // A save that was already serializing when Dispose began: Dispose flushes, and nothing else
            // saves once it has begun.
            if (_disposed || (!isDisposeFlush && IsDisposing)) return;
            // A delayed save cancelled while it serialized: Dispose or Relocate owns the write now.
            if (generation is { } scheduled)
                lock (_gate) if (scheduled != _saveGeneration) return;

            if (!WriteFileLocked(json, snapshot)) return;
            lock (_gate) _savedRequests = Math.Max(_savedRequests, request);
        }
    }

    /// <summary>Writes the defaults a reload seeds a missing file with, only if the file is still
    /// missing: the temp file is moved into place without overwriting, so a file an editor or a sync
    /// tool created since the reload looked is kept, and false says to load that instead. The caller
    /// holds <c>_ioGate</c>.</summary>
    private bool TryCreateFileLocked(string json, long snapshot)
    {
        using (_watcher?.SuppressEvents())
        {
            var path = _prefsPath;
            var temp = path + ".tmp";
            _io.File.WriteAllText(temp, json);
            try
            {
                _io.File.Move(temp, path);   // never overwrites: fails if the file is there now
            }
            catch (IOException) when (_io.File.Exists(path))
            {
                _io.File.Delete(temp);
                return false;
            }
            _syncedContent = json;
            _fileVersion++;
            _writtenSnapshot = Math.Max(_writtenSnapshot, snapshot);
            return true;
        }
    }

    /// <summary>Writes <paramref name="json"/> as the file, atomically, with the watcher's events
    /// suppressed, unless a write of a later <paramref name="snapshot"/> has already landed: two saves
    /// serialize outside the lock and can reach it in either order, and the older must not overwrite
    /// the newer. False when it was skipped. The caller holds <c>_ioGate</c>; nothing here runs model
    /// code.</summary>
    private bool WriteFileLocked(string json, long snapshot)
    {
        if (snapshot < _writtenSnapshot) return false;
        WriteFileLocked(json);
        _writtenSnapshot = snapshot;
        return true;
    }

    private void WriteFileLocked(string json)
    {
        using (_watcher?.SuppressEvents())
        {
            // Read once: Relocate can move the path while a save is waiting for the lock.
            var path = _prefsPath;
            // Atomic write: write to temp, then rename. Guards against partial
            // files on crash.
            var temp = path + ".tmp";
            var exists = _io.File.Exists(path);

            // The temp file becomes the file, so it must carry the file's permissions. Created
            // fresh it gets the umask's (typically 0644): a preferences file an app had restricted
            // to its owner because it holds secrets would turn world-readable on the next save,
            // and a debounced save gives the app no moment to restrict it again. Applied before
            // the content is written, so the secrets are never on disk under the looser mode.
            if (exists && FileModeOf(path) is { } mode)
            {
                _io.File.WriteAllText(temp, string.Empty);
                SetFileMode(temp, mode);
            }

            _io.File.WriteAllText(temp, json);
            if (exists) _io.File.Replace(temp, path, null);
            else _io.File.Move(temp, path);
            _syncedContent = json;
            _fileVersion++;
        }
    }

    /// <summary>The file's Unix permissions, or null on Windows or where they cannot be read.</summary>
    private UnixFileMode? FileModeOf(string path)
    {
        if (OperatingSystem.IsWindows()) return null;
        try { return _io.File.GetUnixFileMode(path); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return null;   // best effort: an unreadable mode just means today's behaviour
        }
    }

    private void SetFileMode(string path, UnixFileMode mode)
    {
        if (OperatingSystem.IsWindows()) return;
        try { _io.File.SetUnixFileMode(path, mode); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            // Best effort, as above — never a reason to fail the save itself.
        }
    }

    private void StartWatching()
    {
        // Caller opted out of external-change tracking (per-machine state file).
        if (!_watch) return;
        _watcher?.Dispose();
        _watcher = _watcherFactory(_prefsPath, OnFileExternallyChanged);
    }

    private IFileChangeWatcher? DefaultWatcher(string path, Action onChanged)
    {
        // DebouncedFileWatcher wraps System.IO.FileSystemWatcher which can
        // only observe real disk paths. When tests run on MockFileSystem
        // the path exists only in-memory — skip the watcher rather than
        // throwing on the constructor's CheckPathValidity. External-edit
        // hot-reload is a production feature; tests exercising prefs
        // semantics don't need it.
        if (_io is not System.IO.Abstractions.FileSystem) return null;
        return new DebouncedFileWatcher(path, onChanged);
    }

    private void OnFileExternallyChanged()
    {
        try { Reload(onlyIfChanged: true); }
        catch
        {
            // Swallow — the editor may have written a half-formed file; next
            // save will recover.
        }
    }

    /// <summary>Stops watching, cancels the delayed save (one already serializing
    /// is dropped when it comes to write), and writes a change still waiting for
    /// it, so the last one isn't lost. Nothing is written after it returns. A
    /// write that fails here is logged, not thrown. From the moment it begins, a
    /// reload for an external change no longer replaces <see cref="Current"/>,
    /// one already in flight included. Called from inside the service's own work
    /// on the same thread — a model getter or setter, a PreferencesReloaded
    /// handler — it returns at once: nothing more is saved, and the work it
    /// interrupted finishes and then disposes. A call from another thread returns
    /// only once the dispose has finished.</summary>
    public void Dispose()
    {
        if (_operationDepth.Value > 0)
        {
            // Called from inside an operation on this thread (a getter, setter or handler it runs):
            // never wait and never flush here. The first Dispose raises the flag, so nothing new is
            // saved and no Relocate starts, and leaves the real work to the outermost operation on
            // this thread, which runs it once it has let go of everything. If another thread's
            // Dispose is already under way, it is waiting for this operation, not the other way round.
            if (Interlocked.Exchange(ref _disposing, 1) == 0)
                Volatile.Write(ref _deferredDisposeThread, Environment.CurrentManagedThreadId);
            return;
        }
        if (Interlocked.Exchange(ref _disposing, 1) != 0)
        {
            // Another thread's Dispose is running, or is deferred to the end of its operation.
            if (!_disposeDone.IsSet)
            {
                BeforeDisposeWaitsForTests?.Invoke();
                _disposeDone.Wait();
            }
            return;
        }
        DisposeCore();
    }

    /// <summary>The real Dispose: run by the first Dispose, or by the operation one was deferred to.
    /// Itself an operation, so a Dispose that its own flush or watcher calls back into is deferred,
    /// and finds the work already being done.</summary>
    private void DisposeCore()
    {
        BeginOperation();
        Volatile.Write(ref _disposeCoreThread, Environment.CurrentManagedThreadId);
        try
        {
            // The state change, under the lifecycle gate: a Relocate is either done with its move or
            // sees the flag and makes none. A delayed save cancelled here is dropped when it comes to
            // write, however far it got.
            Exception? watcherFailure = null;
            lock (_lifecycleGate)
            {
                CancelDelayedSave();
                try { _watcher?.Dispose(); }
                catch (Exception ex) { watcherFailure = ex; }
            }
            // A watcher that fails to stop must not cost the last change, or escape Dispose.
            if (watcherFailure is not null) LogFailure(watcherFailure, "the file watcher failed to stop");

            // A Relocate that moved before the gate above is still reloading or writing: wait for it,
            // holding no lock, so the flush doesn't write the old model to a path whose file it is
            // adopting. Any Relocate that comes later sees the flag and does nothing. This never runs
            // inside a Relocate (a Dispose there is deferred), so it never waits for its own.
            if (!_relocationIdle.IsSet)
            {
                BeforeDisposeWaitsForTests?.Invoke();
                _relocationIdle.Wait();
            }

            // Holding no lock: the flush serializes the model.
            try
            {
                bool owed;
                lock (_gate) owed = _savedRequests != _saveRequests;
                if (owed) WriteCurrent(isDisposeFlush: true);
            }
            catch (Exception ex)
            {
                LogFailure(ex, "the last change was not written");
            }
        }
        finally
        {
            lock (_ioGate) _disposed = true;
            Volatile.Write(ref _disposeCoreThread, 0);
            _disposeDone.Set();
            EndOperation();
        }
    }

    private void BeginOperation() => _operationDepth.Value++;

    /// <summary>Leaves an operation. The outermost one on a thread a Dispose was deferred on runs the
    /// real Dispose, now that the operation holds nothing and has finished.</summary>
    private void EndOperation()
    {
        if (--_operationDepth.Value > 0) return;
        if (Volatile.Read(ref _deferredDisposeThread) != Environment.CurrentManagedThreadId) return;
        Volatile.Write(ref _deferredDisposeThread, 0);
        DisposeCore();
    }
}
