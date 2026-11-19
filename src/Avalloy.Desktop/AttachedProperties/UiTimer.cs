using System;
using System.Threading;
using Avalonia.Threading;

namespace Avalloy.AttachedProperties;

/// <summary>
/// A one-shot timer that counts on a <see cref="TimeProvider"/> and ticks on the UI thread at
/// <see cref="DispatcherPriority.Input"/>. It stands in for a <see cref="DispatcherTimer"/>, which counts only real
/// time, so a test can hand it a fake clock and advance it by hand.
/// <see cref="Start"/> leaves a running timer alone. <see cref="Stop"/> also drops a tick already posted, so a stop
/// that lands between the clock firing and the dispatcher running the tick still wins.
/// </summary>
internal sealed class UiTimer(TimeProvider time, TimeSpan interval, Action tick)
{
    private ITimer? _timer;
    private int _generation;

    public bool IsRunning => _timer is not null;

    public void Start()
    {
        if (_timer is not null) return;
        var generation = ++_generation;
        // Input priority, not Background: while a large view is still settling after load, continuous
        // layout and render work starves Background work, so a dwell would never land.
        _timer = time.CreateTimer(_ => Dispatcher.UIThread.Post(() => Fire(generation), DispatcherPriority.Input),
            null, interval, Timeout.InfiniteTimeSpan);
    }

    public void Stop()
    {
        _generation++;
        _timer?.Dispose();
        _timer = null;
    }

    private void Fire(int generation)
    {
        if (generation != _generation) return;
        Stop();
        tick();
    }
}
