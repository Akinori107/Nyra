namespace Nyra.Internal;

internal sealed class GuildTeardown
{
    private readonly GuildConnection _connection;
    private readonly Node _node;
    private int _invalidationRaised;

    internal GuildTeardown(GuildConnection connection)
    {
        _connection = connection;
        _node = connection.Node;
    }

    internal async Task DisconnectAsync(CancellationToken cancellationToken = default)
    {
        _connection.MarkTearingDown();

        try
        {
            await DrainAndRemoveAsync(destroyPlayerOnly: false, cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            await _connection.CompleteEventPumpAsync().ConfigureAwait(false);
        }
    }

    internal async Task AfterVoiceClosedAsync()
    {
        try
        {
            _connection.MarkTearingDown();

            await Task.CompletedTask.ConfigureAwait(ConfigureAwaitOptions.ForceYielding);

            await DrainAndRemoveAsync(destroyPlayerOnly: false).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            _node.HandleError(exception);
        }
        finally
        {
            await _connection.CompleteEventPumpAsync().ConfigureAwait(false);
        }
    }

    internal async Task HandleSessionInvalidatedAsync()
    {
        _connection.MarkTearingDown();

        await _connection.DrainVoiceForwardAsync().ConfigureAwait(false);
        await _connection.Patches.DrainAsync().ConfigureAwait(false);

        var lastChannelId = _connection.ChannelId;

        try
        {
            await _node
                .Registry.DisconnectExclusivelyAsync(_connection, CancellationToken.None)
                .ConfigureAwait(false);
        }
        finally
        {
            await _connection.ClearStateAsync().ConfigureAwait(false);
            await RaiseInvalidatedOnceAsync(lastChannelId).ConfigureAwait(false);
        }
    }

    private async Task RaiseInvalidatedOnceAsync(ulong? lastChannelId)
    {
        if (Interlocked.Exchange(ref _invalidationRaised, 1) != 0)
            return;

        Task RaiseInvalidated() => _connection.RaiseInvalidatedAsync(lastChannelId);

        if (!_connection.Pump.TryDispatch(RaiseInvalidated))
        {
            await _connection.Pump.CompleteAsync(TimeSpan.FromSeconds(1)).ConfigureAwait(false);
            _ = Task.Run(RaiseInvalidated);
        }
        await _connection.Pump.CompleteAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
    }

    internal async Task HandleVoiceDisconnectedAsync()
    {
        _connection.MarkTearingDown();

        try
        {
            await DrainAndRemoveAsync(destroyPlayerOnly: true).ConfigureAwait(false);
        }
        finally
        {
            await _connection.CompleteEventPumpAsync().ConfigureAwait(false);
        }
    }

    private async Task DrainAndRemoveAsync(
        bool destroyPlayerOnly,
        CancellationToken cancellationToken = default
    )
    {
        await _connection.DrainVoiceForwardAsync(cancellationToken).ConfigureAwait(false);
        await _connection.DrainFiltersAsync(cancellationToken).ConfigureAwait(false);
        await _connection.Patches.DrainAsync(cancellationToken).ConfigureAwait(false);

        var removed = destroyPlayerOnly
            ? await _node
                .Registry.DestroyPlayerExclusivelyAsync(_connection, cancellationToken)
                .ConfigureAwait(false)
            : await _node
                .Registry.DisconnectExclusivelyAsync(_connection, cancellationToken)
                .ConfigureAwait(false);
        if (removed)
            await _connection.ClearStateAsync().ConfigureAwait(false);
    }
}
