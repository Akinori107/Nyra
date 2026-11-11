using Nyra.Entities;

namespace Nyra.Queue;

internal sealed class NextTrackPreloader(GuildQueue queue)
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private volatile Track? _preloaded;
    private volatile Track? _playedWithNext;

    internal void Forget()
    {
        _preloaded = null;
        _playedWithNext = null;
    }

    internal void OnTrackStarted(Track started)
    {
        var playedWithNext = _playedWithNext;
        _playedWithNext = null;
        if (!GuildConnection.IsSameTrack(playedWithNext, started))
            _preloaded = null;
    }

    internal async Task PlayAsync(Track play, QueueState after, CancellationToken cancellationToken)
    {
        var connection = queue.Connection;
        var next =
            connection.Node.IsNodeLink && queue.PreloadNext
                ? after.NextToPreload(queue.Policy)
                : null;
        if (next is null || string.IsNullOrWhiteSpace(next.Encoded))
        {
            await connection
                .PlayAsync(play, cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            return;
        }

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await connection.PlayWithNextAsync(play, next, cancellationToken).ConfigureAwait(false);
            _preloaded = next;
            _playedWithNext = play;
        }
        finally
        {
            _gate.Release();
        }
    }

    internal async Task RefreshAsync()
    {
        var connection = queue.Connection;
        if (!connection.Node.IsNodeLink)
            return;

        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            var target = queue.PreloadNext ? queue.State.NextToPreload(queue.Policy) : null;
            if (ReferenceEquals(_preloaded, target))
                return;

            await connection.Effects.SetNextTrackAsync(target).ConfigureAwait(false);
            _preloaded = target;
        }
        catch (Exception) { }
        finally
        {
            _gate.Release();
        }
    }
}
