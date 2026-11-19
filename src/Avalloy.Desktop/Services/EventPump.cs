using Avalonia.Threading;

namespace Avalloy.Services;

/// <summary>
/// Bridges a background <see cref="IAsyncEnumerable{T}"/> event stream (an engine's Subscribe) onto the UI
/// thread: it pulls events off the dispatcher's thread pool and re-raises each via <see cref="Event"/>
/// through <see cref="Dispatcher.UIThread"/>. Plain events + <c>Post</c> — no Rx. One pump per live
/// subscription; call <see cref="Start"/> to (re)subscribe and <see cref="Stop"/>/<see cref="Dispose"/> to
/// detach. Extracted from a shipping app's engine→UI bridge.
/// </summary>
/// <param name="uiPost">Marshalling seam for <see cref="Event"/>, same shape as <c>LogBuffer</c>'s. Default
/// posts to the Avalonia dispatcher, falling back to inline invocation when no dispatcher exists
/// (headless tests).</param>
public sealed class EventPump<TEvent>(Action<Action>? uiPost = null) : IDisposable
{
    private readonly Action<Action> _uiPost = uiPost ?? DefaultPost;
    private CancellationTokenSource? _cts;

    /// <summary>Raised on the UI thread for every live event. Safe to touch view-model state directly.</summary>
    public event Action<TEvent>? Event;

    /// <summary>
    /// (Re)start the subscription; any prior one is cancelled first. <paramref name="subscribe"/> opens the
    /// stream (called once, on a pool thread); <paramref name="onError"/> maps a stream failure to an event
    /// the consumer can render — null logs the failure to <see cref="System.Diagnostics.Trace"/> instead.
    /// </summary>
    public void Start(Func<CancellationToken, IAsyncEnumerable<TEvent>> subscribe, Func<Exception, TEvent>? onError = null)
    {
        Stop();
        var cts = new CancellationTokenSource();
        _cts = cts;
        var token = cts.Token;

        // One dispatcher Post per BURST, not per event: the UI reader drains fast, so per-event Posts
        // convert stream pressure into DISPATCHER-queue pressure, which is input lag — the shape behind
        // "the app went unresponsive for a moment" under a fast producer. Events queue here; the first of
        // a burst schedules one drain job, and everything that arrives before it runs rides along. The
        // drain caps its batch so a big burst yields the UI thread between chunks instead of becoming one
        // long job. Ordering is preserved: one producer, FIFO queue, drains run serially on the dispatcher.
        var pending = new System.Collections.Concurrent.ConcurrentQueue<TEvent>();
        var drainScheduled = 0;

        void Drain()
        {
            Volatile.Write(ref drainScheduled, 0);   // BEFORE draining — an enqueue we miss re-schedules
            var n = 0;
            while (n++ < 256 && pending.TryDequeue(out var evt)) Event?.Invoke(evt);
            if (!pending.IsEmpty && Interlocked.CompareExchange(ref drainScheduled, 1, 0) == 0)
                _uiPost(Drain);
        }

        _ = Task.Run(async () =>
        {
            try
            {
                await foreach (var e in subscribe(token).ConfigureAwait(false))
                {
                    pending.Enqueue(e);
                    if (Interlocked.CompareExchange(ref drainScheduled, 1, 0) == 0)
                        _uiPost(Drain);
                }
            }
            catch (OperationCanceledException)
            {
                // Normal teardown.
            }
            catch (Exception ex)
            {
                if (onError is not null)
                    _uiPost(() => Event?.Invoke(onError(ex)));
                else
                    System.Diagnostics.Trace.TraceError($"EventPump<{typeof(TEvent).Name}> stream failed: {ex}");
            }
        }, token);
    }

    /// <summary>Cancel the current subscription, if any.</summary>
    public void Stop()
    {
        var cts = _cts;
        _cts = null;
        if (cts is null) return;
        try { cts.Cancel(); } catch { /* already disposed */ }
        cts.Dispose();
    }

    public void Dispose() => Stop();

    private static void DefaultPost(Action action)
    {
        try
        {
            Dispatcher.UIThread.Post(action);
        }
        catch (NullReferenceException)
        {
            action(); // no dispatcher (test env) — run inline
        }
    }
}
