namespace Nyra.Internal;

internal sealed class GuildTimer(TimeProvider time, Action fired)
{
    private readonly Lock _lock = new();
    private ITimer? _timer;
    private long _generation;
    private bool _stopped;

    internal void Arm(TimeSpan due)
    {
        lock (_lock)
        {
            if (_timer is not null || _stopped)
                return;

            var generation = ++_generation;
            _timer = time.CreateTimer(
                _ => Fire(generation),
                null,
                Timeout.InfiniteTimeSpan,
                Timeout.InfiniteTimeSpan
            );
            _timer.Change(due, Timeout.InfiniteTimeSpan);
        }
    }

    internal void Disarm()
    {
        lock (_lock)
            DisposeTimer();
    }

    internal void Stop()
    {
        lock (_lock)
        {
            _stopped = true;
            DisposeTimer();
        }
    }

    private void DisposeTimer()
    {
        var timer = _timer;
        _timer = null;
        timer?.Dispose();
    }

    private void Fire(long generation)
    {
        lock (_lock)
        {
            if (_timer is null || generation != _generation)
                return;

            DisposeTimer();
        }

        fired();
    }
}
