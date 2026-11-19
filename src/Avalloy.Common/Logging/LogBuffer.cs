using Microsoft.Extensions.Logging;

namespace Avalloy.Services;

/// <summary>One captured log entry. The exception is flattened to text at capture
/// time so retained entries never root an object graph.</summary>
public sealed record LogEntry(
    DateTimeOffset Timestamp,
    LogLevel Level,
    string Category,
    string Message,
    string? ExceptionText,
    EventId EventId,
    string? Scopes)
{
    /// <summary>Monotonic id stamped by <see cref="LogBuffer.Add"/>. Lets a viewer
    /// reconcile a <see cref="LogBuffer.Snapshot"/> with subsequently flushed
    /// batches (skip entries at or below the snapshot's newest sequence), and
    /// makes "N dropped" derivable from <see cref="LogBuffer.TotalAdded"/>.</summary>
    public long Sequence { get; init; }
}

/// <summary>
/// Thread-safe bounded ring of recent <see cref="LogEntry"/> items, retained from
/// process start so a viewer that opens later still sees startup logging. Writers
/// (any thread) pay a lock and an array store; consumers get an initial
/// <see cref="Snapshot"/> plus coalesced <see cref="Flushed"/> batches raised on
/// the UI thread — one dispatcher post per burst, not per entry.
/// <para>"The UI thread" is whatever <see cref="DefaultPost"/> marshals to. This package has
/// no UI, so on its own that is an inline call on the writing thread; <c>AvalloyApp</c> sets it
/// to the Avalonia dispatcher before any buffer of its own exists.</para>
/// </summary>
public sealed class LogBuffer
{
    private readonly object _gate = new();
    private readonly LogEntry[] _ring;
    private readonly Action<Action> _uiPost;
    private List<LogEntry> _pending = new();
    private bool _flushScheduled;
    private int _head;
    private int _count;
    private long _totalAdded;

    /// <param name="capacity">Entries retained; oldest are overwritten.</param>
    /// <param name="uiPost">Marshalling seam for <see cref="Flushed"/>. Null means
    /// <see cref="DefaultPost"/> as it stands when the buffer is constructed.</param>
    public LogBuffer(int capacity = 2000, Action<Action>? uiPost = null)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(capacity, 1);
        Capacity = capacity;
        _ring = new LogEntry[capacity];
        _uiPost = uiPost ?? DefaultPost;
    }

    /// <summary>
    /// How a buffer constructed without a <c>uiPost</c> raises <see cref="Flushed"/>. Inline by default —
    /// right for a headless host, where there is no UI thread to reach. A UI host replaces it ONCE at
    /// startup, before creating buffers: <c>AvalloyApp</c> sets it to post to the Avalonia dispatcher, so
    /// a viewer bound to any buffer an app creates keeps receiving batches on the UI thread.
    /// </summary>
    public static Action<Action> DefaultPost { get; set; } = static action => action();

    public int Capacity { get; }

    public int Count
    {
        get { lock (_gate) return _count; }
    }

    /// <summary>Total entries ever added (== the newest entry's <see cref="LogEntry.Sequence"/>).
    /// Survives <see cref="Clear"/> so sequences stay monotonic for the life of the buffer.</summary>
    public long TotalAdded
    {
        get { lock (_gate) return _totalAdded; }
    }

    /// <summary>Raised on the UI thread with each coalesced batch of new entries,
    /// in arrival order. Subscribe after taking a <see cref="Snapshot"/> and skip
    /// entries whose sequence the snapshot already covered.</summary>
    public event Action<IReadOnlyList<LogEntry>>? Flushed;

    /// <summary>Append an entry (stamping its <see cref="LogEntry.Sequence"/>) and
    /// schedule a flush. Safe from any thread; never throws.</summary>
    public void Add(LogEntry entry)
    {
        var schedule = false;
        lock (_gate)
        {
            entry = entry with { Sequence = ++_totalAdded };
            _ring[_head] = entry;
            _head = (_head + 1) % Capacity;
            if (_count < Capacity) _count++;

            // Pending is bounded too: a run that never flushes (headless, UI thread
            // wedged) must not grow past what the ring itself retains.
            _pending.Add(entry);
            if (_pending.Count > Capacity) _pending.RemoveAt(0);

            if (!_flushScheduled)
            {
                _flushScheduled = true;
                schedule = true;
            }
        }

        if (schedule) _uiPost(FlushToUi);
    }

    /// <summary>Copy of the retained entries, oldest first.</summary>
    public IReadOnlyList<LogEntry> Snapshot()
    {
        lock (_gate)
        {
            var result = new LogEntry[_count];
            var start = (_head - _count + Capacity) % Capacity;
            for (var i = 0; i < _count; i++)
                result[i] = _ring[(start + i) % Capacity];
            return result;
        }
    }

    /// <summary>Drop all retained + pending entries. Sequences keep counting from
    /// where they were, so viewers' dedupe-by-sequence logic stays valid.</summary>
    public void Clear()
    {
        lock (_gate)
        {
            Array.Clear(_ring);
            _head = 0;
            _count = 0;
            _pending.Clear();
        }
    }

    private void FlushToUi()
    {
        List<LogEntry> batch;
        lock (_gate)
        {
            _flushScheduled = false;
            if (_pending.Count == 0) return;
            batch = _pending;
            _pending = new List<LogEntry>();
        }

        Flushed?.Invoke(batch);
    }

}
