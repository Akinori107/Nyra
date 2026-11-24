using Nyra.Internal;

namespace Nyra.Tests;

public sealed class GuildTimerTests
{
    private sealed class QueuedTimerProvider : TimeProvider
    {
        public List<QueuedTimer> Timers { get; } = [];

        public override ITimer CreateTimer(
            TimerCallback callback,
            object? state,
            TimeSpan dueTime,
            TimeSpan period
        )
        {
            var timer = new QueuedTimer(callback, state);
            Timers.Add(timer);
            return timer;
        }
    }

    private sealed class QueuedTimer(TimerCallback callback, object? state) : ITimer
    {
        public bool Disposed { get; private set; }

        public bool Change(TimeSpan dueTime, TimeSpan period) => !Disposed;

        public void Dispose() => Disposed = true;

        public ValueTask DisposeAsync()
        {
            Dispose();
            return ValueTask.CompletedTask;
        }

        public void RunQueuedCallback() => callback(state);
    }

    [Test]
    public async Task DisarmedCallbackCannotConsumeTheNextArm()
    {
        var time = new QueuedTimerProvider();
        var count = 0;
        var timer = new GuildTimer(time, () => count++);
        try
        {
            timer.Arm(TimeSpan.FromMinutes(1));
            var old = time.Timers[0];
            timer.Disarm();
            timer.Arm(TimeSpan.FromMinutes(1));
            old.RunQueuedCallback();
            await Assert.That(count).IsEqualTo(0);
            await Assert.That(old.Disposed).IsTrue();

            var current = time.Timers[^1];
            current.RunQueuedCallback();
            current.RunQueuedCallback();
            await Assert.That(count).IsEqualTo(1);
            await Assert.That(current.Disposed).IsTrue();
        }
        finally
        {
            timer.Stop();
        }
    }

    [Test]
    public async Task StopInvalidatesQueuedCallbacksAndPreventsRearming()
    {
        var time = new QueuedTimerProvider();
        var count = 0;
        var timer = new GuildTimer(time, () => count++);
        timer.Arm(TimeSpan.FromMinutes(1));
        timer.Arm(TimeSpan.FromMinutes(2));
        timer.Stop();
        time.Timers[0].RunQueuedCallback();
        timer.Arm(TimeSpan.FromMinutes(1));

        await Assert.That(count).IsEqualTo(0);
        await Assert.That(time.Timers.Count).IsEqualTo(1);
        await Assert.That(time.Timers[0].Disposed).IsTrue();
    }
}
