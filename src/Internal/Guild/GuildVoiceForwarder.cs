using Nyra.Entities;

namespace Nyra.Internal;

internal sealed class GuildVoiceForwarder
{
    private readonly GuildConnection _connection;

    private readonly SemaphoreSlim _forwardLock = new(1, 1);

    private PlayerVoiceState? _pendingVoiceForward;

    private readonly CancellationTokenSource _sendCts = new();

    internal static readonly TimeSpan VoiceSendDeadline = TimeSpan.FromSeconds(10);

    internal GuildVoiceForwarder(GuildConnection connection) => _connection = connection;

    private long _lastForwardTick;

    internal long LastForwardTick => Interlocked.Read(ref _lastForwardTick);

    internal async Task ForwardAsync(PlayerVoiceState voice)
    {
        await _forwardLock.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_connection.IsTearingDown)
                return;

            await ForwardVoiceAsync(voice).ConfigureAwait(false);
        }
        finally
        {
            _forwardLock.Release();
        }
    }

    internal async Task ResendPendingAsync()
    {
        if (_connection.IsTearingDown)
            return;

        await _forwardLock.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_connection.IsTearingDown)
                return;

            if (_pendingVoiceForward is { } pending)
                await ForwardVoiceAsync(pending).ConfigureAwait(false);
        }
        finally
        {
            _forwardLock.Release();
        }
    }

    internal void ClearPending() => _pendingVoiceForward = null;

    internal bool HasPending => _pendingVoiceForward is not null;

    internal async Task DrainAsync(CancellationToken cancellationToken = default)
    {
        var acquired = false;
        try
        {
            await _forwardLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            acquired = true;
        }
        catch (OperationCanceledException)
        {
            _sendCts.Cancel();
            throw;
        }
        finally
        {
            if (acquired)
                _forwardLock.Release();
        }
    }

    private async Task ForwardVoiceAsync(PlayerVoiceState voice)
    {
        _pendingVoiceForward = voice;
        try
        {
            using var attemptCts = CancellationTokenSource.CreateLinkedTokenSource(_sendCts.Token);
            attemptCts.CancelAfter(VoiceSendDeadline);
            await _connection
                .Node.Rest.Players.UpdateAsync(
                    _connection.GuildId,
                    new PlayerUpdatePayload { Voice = voice },
                    attemptCts.Token
                )
                .ConfigureAwait(false);
            _pendingVoiceForward = null;
            Interlocked.Exchange(ref _lastForwardTick, Environment.TickCount64);
        }
        catch (OperationCanceledException) when (_sendCts.IsCancellationRequested) { }
        catch (Exception exception)
        {
            _connection.Node.HandleError(
                new InvalidOperationException(
                    $"Failed to update the voice connection of guild {_connection.GuildId}: {exception.Message}",
                    exception
                )
            );
        }
    }
}
