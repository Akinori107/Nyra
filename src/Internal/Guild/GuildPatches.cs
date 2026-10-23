using Nyra.Entities;
using Nyra.Exceptions;

namespace Nyra.Internal;

internal sealed class GuildPatches
{
    private readonly GuildConnection _connection;
    private readonly Node _node;

    internal GuildPatches(GuildConnection connection)
    {
        _connection = connection;
        _node = connection.Node;
    }

    private ulong GuildId => _connection.GuildId;

    private readonly Lock _playerPatchGate = new();
    private int _inFlightPlayerPatches;
    private TaskCompletionSource? _playerPatchesIdle;

    private readonly CancellationTokenSource _playerPatchCts = new();

    internal async Task DrainAsync(CancellationToken cancellationToken = default)
    {
        Task idle;
        lock (_playerPatchGate)
        {
            if (_inFlightPlayerPatches == 0)
                return;

            _playerPatchesIdle ??= new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously
            );
            idle = _playerPatchesIdle.Task;
        }

        try
        {
            await idle.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            _playerPatchCts.Cancel();
            _node
                .Registry.DestroyPlayerExclusivelyAsync(_connection, CancellationToken.None)
                .ObserveFaults(_node.HandleError);
            throw;
        }
    }

    internal async Task<Player> SendAsync(
        Func<CancellationToken, Task<Player>> send,
        CancellationToken cancellationToken
    )
    {
        lock (_playerPatchGate)
        {
            if (_connection.IsTearingDown)
                throw new InvalidOperationException(
                    "The connection is being torn down, it no longer accepts operations."
                );
            _inFlightPlayerPatches++;
        }

        try
        {
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken,
                _playerPatchCts.Token
            );
            return await send(linked.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
            when (_playerPatchCts.IsCancellationRequested
                && !cancellationToken.IsCancellationRequested
            )
        {
            throw new OperationCanceledException(
                "The connection's teardown aborted this player update.",
                cancellationToken
            );
        }
        finally
        {
            lock (_playerPatchGate)
                if (--_inFlightPlayerPatches == 0 && _playerPatchesIdle is { } idle)
                {
                    _playerPatchesIdle = null;
                    idle.TrySetResult();
                }
        }
    }

    internal async Task UpdateAsync(
        PlayerUpdatePayload payload,
        CancellationToken cancellationToken
    )
    {
        var requestTick = Environment.TickCount64;
        var player = await SendAsync(
                ct => _node.Rest.Players.UpdateAsync(GuildId, payload, ct),
                cancellationToken
            )
            .ConfigureAwait(false);
        _connection.CaptureState(player, requestTick);
    }

    internal static bool MayHaveLanded(Exception exception) =>
        exception
            is TimeoutException
                or HttpRequestException
                or NodeUnreachableException
                or RestException { StatusCode: >= 200 and < 300 };

    internal async Task<bool> ReconcileLandedAsync(
        Func<Player?, bool> landed,
        long requestTick,
        CancellationToken cancellationToken
    )
    {
        Player? player;
        try
        {
            player = await _node
                .Rest.Players.GetAsync(GuildId, cancellationToken)
                .ConfigureAwait(false);
        }
        catch
        {
            return false;
        }

        if (player is null || !landed(player))
            return false;

        _connection.CaptureState(player, requestTick);
        return true;
    }
}
