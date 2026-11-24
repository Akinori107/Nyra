using Nyra.Internal;

namespace Nyra.Tests;

public sealed class GuildTimerTests
{
    private sealed class QueuedTimerProvider : TimeProvider
    {
        public List<QueuedTimer> Timers { get; } = [];

        public int ChangeFailure { get; set; }

        public override ITimer CreateTimer(
            TimerCallback callback,
            object? state,
            TimeSpan dueTime,
            TimeSpan period
        )
        {
            var timer = new QueuedTimer(callback, state, ChangeFailure);
            Timers.Add(timer);
            return timer;
        }
    }

    private sealed class QueuedTimer(TimerCallback callback, object? state, int changeFailure)
        : ITimer
    {
        public bool Disposed { get; private set; }

        public bool Change(TimeSpan dueTime, TimeSpan period) =>
            changeFailure == 2
                ? throw new ArgumentOutOfRangeException(nameof(dueTime))
                : !Disposed && changeFailure == 0;

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
    [Arguments(1)]
    [Arguments(2)]
    public async Task FailedArmDisposesTimerAndAllowsRetry(int failure)
    {
        var time = new QueuedTimerProvider { ChangeFailure = failure };
        var count = 0;
        var timer = new GuildTimer(time, () => count++);
        try
        {
            if (failure == 2)
                await Assert
                    .That(() => timer.Arm(TimeSpan.FromMinutes(1)))
                    .Throws<ArgumentOutOfRangeException>();
            else
                timer.Arm(TimeSpan.FromMinutes(1));

            await Assert.That(time.Timers[0].Disposed).IsTrue();
            time.ChangeFailure = 0;
            timer.Arm(TimeSpan.FromMinutes(1));
            time.Timers[0].RunQueuedCallback();
            await Assert.That(count).IsEqualTo(0);
            time.Timers[^1].RunQueuedCallback();
            await Assert.That(count).IsEqualTo(1);
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
