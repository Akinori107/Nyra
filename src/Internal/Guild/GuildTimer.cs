namespace Nyra.Internal;

internal sealed class GuildTimer(TimeProvider time, Action fired)
{
    private readonly Lock _lock = new();
    private ITimer? _timer;
    private bool _armed;
    private bool _stopped;

    internal void Arm(TimeSpan due)
    {
        lock (_lock)
        {
            if (_armed || _stopped)
                return;

            _timer ??= time.CreateTimer(
                static state => ((GuildTimer)state!).Fire(),
                this,
                Timeout.InfiniteTimeSpan,
                Timeout.InfiniteTimeSpan
            );
            _timer.Change(due, Timeout.InfiniteTimeSpan);
            _armed = true;
        }
    }

    internal void Disarm()
    {
        lock (_lock)
        {
            if (!_armed)
                return;

            _timer?.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
            _armed = false;
        }
    }

    internal void Stop()
    {
        lock (_lock)
        {
            _stopped = true;
            _armed = false;
            _timer?.Dispose();
            _timer = null;
        }
    }

    private void Fire()
    {
        lock (_lock)
        {
            if (!_armed)
                return;

            _armed = false;
        }

        fired();
    }
}
