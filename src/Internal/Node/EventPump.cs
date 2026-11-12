using Microsoft.Extensions.Logging;

namespace Nyra.Internal;

internal sealed class EventPump(string owner, ILogger? logger, EventId category)
{
    private const int BacklogWarnThreshold = 64;

    private const int DroppableBacklogLimit = 512;

    private const int MaxBacklog = 4096;

    private static readonly ErrorThrottle Errors = new();

    private readonly Queue<Func<Task>> _queue = new();

    private readonly Lock _gate = new();

    private int _queuedItemCount;

    private bool _draining;

    private bool _completed;

    private Task _consumer = Task.CompletedTask;
    private readonly string _owner = owner;
    private readonly EventId _category = category;

    private volatile ILogger? _logger = logger;
    private int _backlogged;
    private int _dropWarned;

    private static readonly AsyncLocal<EventPump?> CurrentConsumer = new();

    internal ILogger? Logger
    {
        get => _logger;
        set => _logger = value;
    }

    public bool TryDispatch(Func<Task> work, bool droppable = false)
    {
        var depth = Interlocked.Increment(ref _queuedItemCount);
        if (depth > MaxBacklog || (droppable && depth > DroppableBacklogLimit))
        {
            Interlocked.Decrement(ref _queuedItemCount);
            WarnDropping(depth);
            return false;
        }

        lock (_gate)
        {
            if (_completed)
            {
                Interlocked.Decrement(ref _queuedItemCount);
                return false;
            }

            _queue.Enqueue(work);
            if (!_draining)
            {
                _draining = true;
                _consumer = StartConsumer();
            }
        }

        if (depth > BacklogWarnThreshold)
            if (Interlocked.Exchange(ref _backlogged, 1) == 0)
                try
                {
                    _logger?.LogWarning(
                        _category,
                        "Event backlog of {Owner} reached {Backlog} items: a handler is stuck or slow.",
                        _owner,
                        depth
                    );
                }
                catch { }

        return true;
    }

    public bool TryDispatch<TArgs>(
        AsyncEventHandler<TArgs>? handler,
        object sender,
        TArgs args,
        Func<Exception, System.EventArgs?, Task> onError,
        bool droppable = false
    )
        where TArgs : System.EventArgs
    {
        if (handler is null)
            return false;

        return TryDispatch(() => handler.RaiseAsync(sender, args, onError), droppable);
    }

    private void WarnDropping(int depth)
    {
        if (Interlocked.Exchange(ref _dropWarned, 1) != 0)
            return;

        try
        {
            _logger?.LogWarning(
                _category,
                "Event pump of {Owner} is {Backlog} items behind: events are skipped until the handlers catch up.",
                _owner,
                depth
            );
        }
        catch { }
    }

    public async Task CompleteAsync(TimeSpan drainTimeout)
    {
        Task consumer;
        lock (_gate)
        {
            _completed = true;
            consumer = _consumer;
        }

        if (ReferenceEquals(CurrentConsumer.Value, this))
            return;

        await consumer
            .WaitAsync(drainTimeout)
            .ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
        if (!consumer.IsCompleted)
            try
            {
                _logger?.LogWarning(
                    _category,
                    "Event pump of {Owner} did not drain within {Timeout}: {Backlog} item(s) left, the consumer is detached.",
                    _owner,
                    drainTimeout,
                    Volatile.Read(ref _queuedItemCount)
                );
            }
            catch { }
    }

    private Task StartConsumer()
    {
        var suppressFlow = !ExecutionContext.IsFlowSuppressed();
        var flow = suppressFlow ? ExecutionContext.SuppressFlow() : default;
        try
        {
            return Task.Run(ConsumeAsync);
        }
        finally
        {
            if (suppressFlow)
                flow.Undo();
        }
    }

    private async Task ConsumeAsync()
    {
        CurrentConsumer.Value = this;
        while (true)
        {
            Func<Task>? work;
            lock (_gate)
                if (!_queue.TryDequeue(out work))
                {
                    _queue.TrimExcess();
                    _draining = false;
                    return;
                }

            try
            {
                await work().ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                if (Errors.TryAdmit(exception, out _))
                    try
                    {
                        _logger?.LogError(
                            _category,
                            exception,
                            "Event pump of {Owner} caught a work item failure.",
                            _owner
                        );
                    }
                    catch { }
            }
            finally
            {
                if (Interlocked.Decrement(ref _queuedItemCount) == 0)
                {
                    Volatile.Write(ref _backlogged, 0);
                    Volatile.Write(ref _dropWarned, 0);
                }
            }
        }
    }
}
