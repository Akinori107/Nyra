using System.Threading.Channels;
using Microsoft.Extensions.Logging;

namespace Nyra.Internal;

internal sealed class EventPump
{
    private const int BacklogWarnThreshold = 64;

    private const int DroppableBacklogLimit = 512;

    private const int MaxBacklog = 4096;

    private readonly Channel<Func<Task>> _queue = Channel.CreateUnbounded<Func<Task>>(
        new UnboundedChannelOptions { SingleReader = true }
    );

    private int _queuedItemCount;

    private readonly Task _consumer;
    private readonly string _owner;
    private readonly EventId _category;

    private volatile ILogger? _logger;
    private int _backlogged;
    private int _dropWarned;

    private static readonly AsyncLocal<EventPump?> CurrentConsumer = new();

    internal ILogger? Logger
    {
        get => _logger;
        set => _logger = value;
    }

    public EventPump(string owner, ILogger? logger, EventId category)
    {
        _owner = owner;
        _logger = logger;
        _category = category;
        var suppressFlow = !ExecutionContext.IsFlowSuppressed();
        var flow = suppressFlow ? ExecutionContext.SuppressFlow() : default;
        try
        {
            _consumer = Task.Run(ConsumeAsync);
        }
        finally
        {
            if (suppressFlow)
                flow.Undo();
        }
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

        if (!_queue.Writer.TryWrite(work))
        {
            Interlocked.Decrement(ref _queuedItemCount);
            return false;
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
        _queue.Writer.TryComplete();

        if (ReferenceEquals(CurrentConsumer.Value, this))
            return;

        await _consumer
            .WaitAsync(drainTimeout)
            .ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
        if (!_consumer.IsCompleted)
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

    private async Task ConsumeAsync()
    {
        CurrentConsumer.Value = this;
        await foreach (var work in _queue.Reader.ReadAllAsync().ConfigureAwait(false))
            try
            {
                await work().ConfigureAwait(false);
            }
            catch (Exception exception)
            {
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
