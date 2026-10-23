using Nyra.Abstractions;

namespace Nyra.Internal;

internal sealed class NodeConnector
{
    private readonly Node _node;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly CancellationTokenSource _gateLife = new();
    private TaskCompletionSource<bool>? _completion;
    private int _completionOwner;

    internal NodeConnector(Node node) => _node = node;

    internal async Task ConnectAsync(
        IDiscordBridge client,
        NyraExtension? expectedExtension,
        CancellationToken cancellationToken
    )
    {
        if (
            _node.Wiring.WiredClient is not null
            && !ReferenceEquals(_node.Wiring.WiredClient, client)
        )
            throw new InvalidOperationException(
                $"The node '{_node.Name}' is already attached to another Discord client."
            );

        using var gateWait = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            _gateLife.Token
        );
        await _gate.WaitAsync(gateWait.Token).ConfigureAwait(false);
        try
        {
            _node.ThrowIfDisposed();

            if (_node.IsReady)
                return;

            var stopEpoch = _node.StopEpoch;
            var clientInformation = await client
                .WaitForReadyAsync(cancellationToken)
                .ConfigureAwait(false);

            _node.AttachClient(client, clientInformation, expectedExtension, stopEpoch);

            var completion = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously
            );
            lock (_node.EventsLock)
            {
                _completion = completion;
                _completionOwner = _node.Connection.LatestGeneration + 1;
            }

            if (_node.IsReady)
            {
                if (ReferenceEquals(_completion, completion))
                    _completion = null;
                return;
            }

            var started = false;
            try
            {
                lock (_node.EventsLock)
                    _node.ThrowIfInterruptedWhileConnecting(expectedExtension, stopEpoch);

                started = await _node
                    .Connection.ConnectAsync(cancellationToken)
                    .ConfigureAwait(false);

                if (started)
                    lock (_node.EventsLock)
                        _node.ThrowIfInterruptedWhileConnecting(expectedExtension, stopEpoch);
            }
            catch
            {
                if (ReferenceEquals(_completion, completion))
                    _completion = null;
                completion.TrySetResult(false);

                bool interrupted;
                lock (_node.EventsLock)
                    interrupted = _node.Disposed || _node.StopEpoch != stopEpoch;

                if (started || interrupted)
                    await _node
                        .Connection.StopAsync()
                        .ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
                throw;
            }

            if (!started)
                lock (_node.EventsLock)
                    if (ReferenceEquals(_completion, completion))
                        _completionOwner = _node.Connection.LatestGeneration;

            bool becameReady;
            try
            {
                becameReady = await completion
                    .Task.WaitAsync(cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                if (ReferenceEquals(_completion, completion))
                    _completion = null;
                throw;
            }

            if (ReferenceEquals(_completion, completion))
                _completion = null;

            if (!becameReady)
                throw new InvalidOperationException(
                    $"The node '{_node.Name}' disconnected before becoming ready."
                );
        }
        finally
        {
            _gate.Release();
        }
    }

    internal TaskCompletionSource<bool>? TakeCompletion(int? maxOwnerGeneration = null)
    {
        var completion = Interlocked.Exchange(ref _completion, null);
        if (completion is null)
            return null;

        if (maxOwnerGeneration is { } max && Volatile.Read(ref _completionOwner) > max)
        {
            Interlocked.CompareExchange(ref _completion, completion, null);
            return null;
        }

        return completion;
    }

    internal void CancelGate()
    {
        try
        {
            _gateLife.Cancel();
        }
        catch (ObjectDisposedException) { }
    }
}
