using Nyra.Exceptions;

namespace Nyra;

/// <summary>The lyrics subscription of a guild connection.</summary>
public sealed class GuildLyrics
{
    private readonly GuildConnection _connection;
    private readonly Node _node;

    internal GuildLyrics(GuildConnection connection)
    {
        _connection = connection;
        _node = connection.Node;
    }

    private ulong GuildId => _connection.GuildId;

    private static bool RouteMissing(RestException exception) =>
        exception.ResponseBody?.Contains("No endpoint", StringComparison.Ordinal) == true;

    private NotSupportedException MissingRoute(RestException exception) =>
        new(
            $"The lyrics subscription requires a master build of the '{NodeGuards.LavaLyricsPlugin}' Lavalink plugin (the released builds answer 404 on it), the node '{_node.Name}' runs one without the route.",
            exception
        );

    /// <summary>
    /// Subscribes to lyrics updates, <see cref="GuildConnection.LyricsFound"/> delivers the full lyrics, <see
    /// cref="GuildConnection.LyricsLine"/> the current line as playback advances, <see
    /// cref="GuildConnection.LyricsNotFound"/> when the track has no timed lyrics, and <paramref
    /// name="skipTrackSource"/> skips the source's own lyrics.
    /// </summary>
    /// <remarks>
    /// NodeLink answers for the current track when it is playing, Lavalink answers from the next track start.
    /// On a node that is not NodeLink, this needs a LavaLyrics build that has the subscribe routes. Without
    /// them this call fails with a <see cref="NotSupportedException"/> naming the missing route instead of the
    /// raw 404.
    /// </remarks>
    /// <param name="skipTrackSource">Whether to skip the track's source as a provider.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A task that completes when the subscription was sent.</returns>
    /// <exception cref="NotSupportedException">
    /// The node is not NodeLink and does not have the LavaLyrics plugin, or its LavaLyrics build carries no
    /// subscribe route (the released builds).
    /// </exception>
    public Task SubscribeAsync(
        bool skipTrackSource = false,
        CancellationToken cancellationToken = default
    ) =>
        SendAsync(
            ct => _node.Rest.Lyrics.SubscribeAsync(GuildId, skipTrackSource, ct),
            cancellationToken
        );

    /// <summary>
    /// Drops the lyrics subscription, <see cref="GuildConnection.LyricsFound"/> and <see
    /// cref="GuildConnection.LyricsLine"/> stop arriving.
    /// </summary>
    /// <remarks>
    /// On a node that is not NodeLink, this needs a LavaLyrics build that has the unsubscribe route. Without it
    /// this call fails with a <see cref="NotSupportedException"/> naming the missing route instead of the raw
    /// 404.
    /// </remarks>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A task that completes when the subscription was dropped.</returns>
    /// <exception cref="NotSupportedException">
    /// The node is not NodeLink and does not have the LavaLyrics plugin, or its LavaLyrics build carries no
    /// subscribe route (the released builds).
    /// </exception>
    public Task UnsubscribeAsync(CancellationToken cancellationToken = default) =>
        SendAsync(ct => _node.Rest.Lyrics.UnsubscribeAsync(GuildId, ct), cancellationToken);

    private async Task SendAsync(
        Func<CancellationToken, Task> request,
        CancellationToken cancellationToken
    )
    {
        NodeGuards.RequireNodeLinkOrPlugin(
            _node,
            NodeGuards.LavaLyricsPlugin,
            "lyrics subscription"
        );
        _connection.EnsureActive();

        var isLavalink = !_node.IsNodeLink;
        try
        {
            await request(cancellationToken).ConfigureAwait(false);
        }
        catch (RestException exception)
            when (isLavalink && exception.StatusCode is 404 && RouteMissing(exception))
        {
            throw MissingRoute(exception);
        }
    }
}
