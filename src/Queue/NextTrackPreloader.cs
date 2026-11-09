using Nyra.Entities;

namespace Nyra.Queue;

internal sealed class NextTrackPreloader(GuildQueue queue)
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private volatile Track? _preloaded;

    internal void Forget() => _preloaded = null;

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
