using Microsoft.Extensions.Logging;
using Nyra.Internal;

namespace Nyra.Tests;

public sealed class EventPumpTests
{
    [Test]
    public async Task RejectsAfterComplete()
    {
        var pump = new EventPump("pump-reject", new RecordingLogger(), new EventId(7, "edges"));
        await pump.CompleteAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
        await Assert.That(pump.TryDispatch(() => Task.CompletedTask)).IsFalse();
        await Assert
            .That(
                pump.TryDispatch<System.EventArgs>(
                    null,
                    new object(),
                    System.EventArgs.Empty,
                    (_, _) => Task.CompletedTask
                )
            )
            .IsFalse();
    }

    [Test]
    public async Task DropWarnsOncePerEpisode()
    {
        var logger = new RecordingLogger();
        var pump = new EventPump("pump-drop", logger, new EventId(7, "edges"));

        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await Assert.That(pump.TryDispatch(() => gate.Task)).IsTrue();

        var droppedAt = -1;
        for (var i = 0; i < 4200; i++)
            if (!pump.TryDispatch(() => Task.CompletedTask))
            {
                droppedAt = i;
                break;
            }
        await Assert.That(droppedAt).IsGreaterThan(0);

        for (var i = 0; i < 5; i++)
            pump.TryDispatch(() => Task.CompletedTask);

        gate.SetResult();
        await pump.CompleteAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);

        var dropperLogger = new RecordingLogger();
        var dropper = new EventPump("pump-droppable", dropperLogger, new EventId(7, "edges"));
        var hold = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        dropper.TryDispatch(() => hold.Task);

        var droppedDroppable = false;
        for (var i = 0; i < 600; i++)
            if (!dropper.TryDispatch(() => Task.CompletedTask, droppable: true))
            {
                droppedDroppable = true;
                break;
            }
        await Assert.That(droppedDroppable).IsTrue();

        hold.SetResult();
        await dropper.CompleteAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
    }

    [Test]
    public async Task BacklogWarnsOncePerEpisode()
    {
        var logger = new RecordingLogger();
        var pump = new EventPump("pump-backlog", logger, new EventId(7, "edges"));

        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await Assert.That(pump.TryDispatch(() => gate.Task)).IsTrue();
        var ran = 0;
        for (var i = 0; i < 70; i++)
            pump.TryDispatch(() =>
            {
                Interlocked.Increment(ref ran);
                return Task.CompletedTask;
            });

        for (var i = 0; i < 10; i++)
            pump.TryDispatch(() => Task.CompletedTask);

        var drained = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        pump.TryDispatch(() =>
        {
            drained.SetResult();
            return Task.CompletedTask;
        });
        gate.SetResult();
        await Assert
            .That(() => drained.Task.IsCompleted)
            .WaitsFor(assert => assert.IsTrue(), TimeSpan.FromSeconds(5));
        await Assert
            .That(() => Volatile.Read(ref ran) == 70)
            .WaitsFor(assert => assert.IsTrue(), TimeSpan.FromSeconds(5));
        await Task.Delay(50).ConfigureAwait(false);

        var secondGate = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        pump.TryDispatch(() => secondGate.Task);
        for (var i = 0; i < 65; i++)
            pump.TryDispatch(() => Task.CompletedTask);

        secondGate.SetResult();
        await pump.CompleteAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
    }

    [Test]
    public async Task BacklogSurvivesAThrowingLogger()
    {
        var logger = new ThrowingLogger();
        var pump = new EventPump("pump-backlog-throw", logger, new EventId(7, "edges"));
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        pump.TryDispatch(() => gate.Task);
        var ran = 0;
        for (var i = 0; i < 70; i++)
            pump.TryDispatch(() =>
            {
                Interlocked.Increment(ref ran);
                return Task.CompletedTask;
            });
        gate.SetResult();
        await Assert
            .That(() => Volatile.Read(ref ran) == 70)
            .WaitsFor(assert => assert.IsTrue(), TimeSpan.FromSeconds(5));
        await Assert
            .That(async () =>
                await pump.CompleteAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false)
            )
            .ThrowsNothing();
    }

    [Test]
    public async Task BoundedCompleteDetachesTheStuckConsumer()
    {
        var logger = new RecordingLogger();
        var pump = new EventPump("pump-drain", logger, new EventId(7, "edges"));
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        pump.TryDispatch(() => gate.Task);
        pump.TryDispatch(() => Task.CompletedTask);

        var sw = System.Diagnostics.Stopwatch.StartNew();
        await pump.CompleteAsync(TimeSpan.FromMilliseconds(250)).ConfigureAwait(false);
        sw.Stop();
        await Assert.That(sw.Elapsed).IsLessThan(TimeSpan.FromSeconds(3));
        await Assert.That(pump.TryDispatch(() => Task.CompletedTask)).IsFalse();

        gate.SetResult();
        await Task.Delay(50).ConfigureAwait(false);
    }

    [Test]
    public async Task DrainTimeoutSurvivesAThrowingLogger()
    {
        var pump = new EventPump("pump-drain-throw", new ThrowingLogger(), new EventId(7, "edges"));
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var finished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        pump.TryDispatch(async () =>
        {
            await gate.Task.ConfigureAwait(false);
            finished.SetResult();
        });

        var watch = System.Diagnostics.Stopwatch.StartNew();
        await pump.CompleteAsync(TimeSpan.FromMilliseconds(200)).ConfigureAwait(false);
        watch.Stop();

        await Assert.That(watch.Elapsed).IsLessThan(TimeSpan.FromSeconds(3));
        await Assert.That(pump.TryDispatch(() => Task.CompletedTask)).IsFalse();

        gate.SetResult();
        var settled = await Task.WhenAny(finished.Task, Task.Delay(TimeSpan.FromSeconds(5)))
            .ConfigureAwait(false);
        await Assert.That(settled).IsSameReferenceAs(finished.Task);
    }

    [Test]
    public async Task CompleteFromInsideTheOwnConsumer()
    {
        var logger = new RecordingLogger();
        var pump = new EventPump("pump-self", logger, new EventId(7, "edges"));
        var inside = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        pump.TryDispatch(async () =>
        {
            await pump.CompleteAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
            inside.SetResult();
        });

        var settled = await Task.WhenAny(inside.Task, Task.Delay(3000)).ConfigureAwait(false);
        await Assert.That(settled).IsSameReferenceAs(inside.Task);
        await Assert.That(logger.Messages.Count).IsEqualTo(0);

        await pump.CompleteAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
    }

    [Test]
    public async Task LateLoggerArmsTheWarnings()
    {
        var silent = new EventPump("pump-silent", null, new EventId(7, "edges"));
        var silentGate = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        silent.TryDispatch(() => silentGate.Task);
        for (var i = 0; i < 65; i++)
            silent.TryDispatch(() => Task.CompletedTask);
        silentGate.SetResult();
        await silent.CompleteAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);

        var logger = new RecordingLogger();
        var armed = new EventPump("pump-armed", null, new EventId(7, "edges")) { Logger = logger };
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        armed.TryDispatch(() => gate.Task);
        for (var i = 0; i < 65; i++)
            armed.TryDispatch(() => Task.CompletedTask);
        await Assert.That(ReferenceEquals(armed.Logger, logger)).IsTrue();

        gate.SetResult();
        await armed.CompleteAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
    }

    [Test]
    public async Task ThrowingWorkItemSurvives()
    {
        var logger = new RecordingLogger();
        var pump = new EventPump("pump-failure", logger, new EventId(7, "edges"));
        var after = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        pump.TryDispatch(() => throw new InvalidOperationException("the item itself broke"));
        pump.TryDispatch(() =>
        {
            after.SetResult();
            return Task.CompletedTask;
        });

        var settled = await Task.WhenAny(after.Task, Task.Delay(3000)).ConfigureAwait(false);
        await Assert.That(settled).IsSameReferenceAs(after.Task);

        await pump.CompleteAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
    }

    [Test]
    public async Task ThrowingLoggerCannotKillTheConsumer()
    {
        var pump = new EventPump("pump-fatal", new ThrowingLogger(), new EventId(7, "edges"));
        var after = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        pump.TryDispatch(() => throw new InvalidOperationException("no listener left"));
        pump.TryDispatch(() =>
        {
            after.SetResult();
            return Task.CompletedTask;
        });

        var settled = await Task.WhenAny(after.Task, Task.Delay(3000)).ConfigureAwait(false);
        await Assert.That(settled).IsSameReferenceAs(after.Task);

        await pump.CompleteAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
    }

    private sealed class RecordingLogger : ILogger
    {
        public List<string> Messages { get; } = [];

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter
        ) => Messages.Add(formatter(state, exception));

        public bool IsEnabled(LogLevel logLevel) => true;

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;
    }

    private sealed class ThrowingLogger : ILogger
    {
        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter
        ) => throw new InvalidOperationException("the logger itself is broken");

        public bool IsEnabled(LogLevel logLevel) => false;

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;
    }
}
