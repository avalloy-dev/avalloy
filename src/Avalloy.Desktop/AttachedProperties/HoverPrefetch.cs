using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace Avalloy.AttachedProperties;

/// <summary>
/// Speculative work for whatever the pointer rests on: once it has dwelt on one target for <see cref="Dwell"/>,
/// <c>prepare</c> starts building what a click would open, so the click lands on finished work. One target at a
/// time — entering another cancels the first and hands it to <c>discard</c>.
///
/// <list type="bullet">
///   <item><see cref="Enter"/> starts the dwell for a target (entering the same one again is a no-op).</item>
///   <item><see cref="Leave"/> drops it — cancelled if still running, then discarded — unless it was claimed.</item>
///   <item><see cref="Claim"/> is the click: the work done so far is the caller's to keep, anything still running
///   is cancelled, and a leave no longer drops it. Claiming a different target drops the current one instead.</item>
/// </list>
///
/// A claimed target is still handed to <c>discard</c> when the next one is entered or the prefetch is disposed,
/// so <c>discard</c> must leave alone whatever the click already took.
///
/// The dwell counts on the given <see cref="TimeProvider"/>, so a test advances a fake clock instead of waiting.
/// <see cref="Pending"/> is the current flight, for a caller (or a test) that has to know the work has settled.
/// </summary>
public sealed class HoverPrefetch<TKey> : IDisposable where TKey : notnull
{
    /// <summary>The default dwell: longer than <see cref="HoverIntent.DwellMs"/>, since what it starts costs more
    /// than revealing a button.</summary>
    public static readonly TimeSpan DefaultDwell = TimeSpan.FromMilliseconds(350);

    private readonly Func<TKey, CancellationToken, Task> _prepare;
    private readonly Action<TKey> _discard;
    private readonly Action<Exception>? _onError;
    private readonly TimeProvider _time;
    private readonly IEqualityComparer<TKey> _comparer = EqualityComparer<TKey>.Default;
    private Flight? _flight;

    /// <param name="prepare">Builds the speculative work for a target. It must honor the token.</param>
    /// <param name="discard">Throws away what <paramref name="prepare"/> built for a target nobody claimed —
    /// called for every target that is dropped, whether its work finished, was cancelled, or never started.</param>
    /// <param name="dwell">How long the pointer must rest first; <see cref="DefaultDwell"/> when null.</param>
    /// <param name="time">The clock; <see cref="HoverIntent.Time"/> when null.</param>
    /// <param name="onError">Told when <paramref name="prepare"/> fails; the target is dropped either way. A
    /// trace warning when null.</param>
    public HoverPrefetch(Func<TKey, CancellationToken, Task> prepare, Action<TKey> discard,
        TimeSpan? dwell = null, TimeProvider? time = null, Action<Exception>? onError = null)
    {
        _prepare = prepare;
        _discard = discard;
        _onError = onError;
        _time = time ?? HoverIntent.Time;
        Dwell = dwell ?? DefaultDwell;
    }

    public TimeSpan Dwell { get; }

    /// <summary>The current flight: the dwell and then the work. Completed when nothing is in flight.</summary>
    public Task Pending { get; private set; } = Task.CompletedTask;

    /// <summary>The target in flight or held, or default when there is none.</summary>
    public TKey? Target => _flight is { } flight ? flight.Key : default;

    private sealed class Flight(TKey key)
    {
        internal TKey Key { get; } = key;
        internal CancellationTokenSource Cancellation { get; } = new();
        internal bool Claimed;
    }

    public void Enter(TKey key)
    {
        if (_flight is { } current && _comparer.Equals(current.Key, key)) return;
        Dispose();
        var flight = _flight = new Flight(key);
        Pending = RunAsync(flight);
    }

    public void Leave(TKey key)
    {
        if (_flight is { Claimed: false } flight && _comparer.Equals(flight.Key, key)) Dispose();
    }

    public void Claim(TKey key)
    {
        if (_flight is not { } flight) return;
        if (!_comparer.Equals(flight.Key, key)) { Dispose(); return; }
        flight.Claimed = true;
        if (!Pending.IsCompleted) flight.Cancellation.Cancel();
    }

    private async Task RunAsync(Flight flight)
    {
        try
        {
            await Task.Delay(Dwell, _time, flight.Cancellation.Token);
            flight.Cancellation.Token.ThrowIfCancellationRequested();
            await _prepare(flight.Key, flight.Cancellation.Token);
        }
        catch (OperationCanceledException) when (flight.Cancellation.IsCancellationRequested)
        {
            // Dropped (and discarded) or claimed: either way the owner already has what it needs.
        }
        catch (Exception ex)
        {
            if (ReferenceEquals(_flight, flight)) Dispose();
            if (_onError is { } onError) onError(ex);
            else Trace.TraceWarning("Hover prefetch failed: {0}", ex.Message);
        }
        finally { flight.Cancellation.Dispose(); }
    }

    /// <summary>Drop the current target: cancel what is still running and discard it, claimed or not.</summary>
    public void Dispose()
    {
        if (_flight is not { } flight) return;
        _flight = null;
        // A completed flight has already disposed its token source, and has nothing left to cancel.
        if (!Pending.IsCompleted) flight.Cancellation.Cancel();
        _discard(flight.Key);
    }
}
