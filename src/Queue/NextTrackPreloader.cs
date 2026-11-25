using Nyra.Entities;

namespace Nyra.Queue;

internal sealed class NextTrackPreloader(GuildQueue queue)
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Lock _cacheLock = new();
    private long _cacheGeneration;

    private sealed record Preload(GuildConnection Connection, Track Track);

    private volatile Preload? _preloaded;
    private Track? _playedWithNext;

    internal void Forget()
    {
        lock (_cacheLock)
        {
            _cacheGeneration++;
            _preloaded = null;
            _playedWithNext = null;
        }
    }

    internal void OnTrackStarted(Track started)
    {
        lock (_cacheLock)
        {
            var playedWithNext = _playedWithNext;
            _playedWithNext = null;
            if (!GuildConnection.IsSameTrack(playedWithNext, started))
            {
                _cacheGeneration++;
                _preloaded = null;
            }
        }
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
            lock (_cacheLock)
            {
                _preloaded = new Preload(connection, next);
                _playedWithNext = play;
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    internal async Task RefreshAsync()
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            var connection = queue.Connection;
            if (!connection.Node.IsNodeLink)
                return;

            var target = queue.PreloadNext ? queue.State.NextToPreload(queue.Policy) : null;
            long generation;
            lock (_cacheLock)
            {
                var cached = _preloaded;
                if (cached is not null && !ReferenceEquals(cached.Connection, connection))
                    _preloaded = cached = null;
                if (ReferenceEquals(cached?.Track, target))
                    return;
                generation = _cacheGeneration;
            }

            await connection.Effects.SetNextTrackAsync(target).ConfigureAwait(false);
            lock (_cacheLock)
                if (generation == _cacheGeneration)
                    _preloaded = target is null ? null : new Preload(connection, target);
        }
        catch (Exception) { }
        finally
        {
            _gate.Release();
        }
    }
}
