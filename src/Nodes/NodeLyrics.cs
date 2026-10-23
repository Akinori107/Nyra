using System.Text.Json;
using Nyra.Entities;
using Nyra.Exceptions;

namespace Nyra;

/// <summary>Lyrics, chapters and meanings of the tracks of a node.</summary>
public sealed class NodeLyrics
{
    private readonly Node _node;

    internal NodeLyrics(Node node) => _node = node;

    /// <summary>
    /// Loads a track's lyrics, <paramref name="language"/> applies to NodeLink, <paramref
    /// name="skipTrackSource"/> to Lavalink with the LavaLyrics plugin.
    /// </summary>
    /// <remarks>
    /// For live per-line lyrics use GuildLyrics.SubscribeAsync instead, which on Lavalink needs a plugin build
    /// that has the subscribe route.
    /// </remarks>
    /// <param name="track">The track to load the lyrics of.</param>
    /// <param name="language">The language to prefer, null lets the server pick.</param>
    /// <param name="skipTrackSource">Whether to skip the track's source as a provider.</param>
    /// <param name="source">
    /// The lyrics provider to force, such as <c>lrclib</c> or <c>musixmatch</c>, null lets the server pick.
    /// Only the NodeLink route carries it, honored by recent NodeLink dev builds, the server forces the
    /// provider and answers an unknown one with a dedicated error. The LavaLyrics route has no such
    /// parameter.
    /// </param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The lyrics result of the track.</returns>
    /// <exception cref="NotSupportedException">
    /// The node is not NodeLink and does not have the LavaLyrics plugin.
    /// </exception>
    /// <exception cref="IncompatibleServerException">The server's /v4/info version is not supported.</exception>
    public async Task<LyricsResult> LoadAsync(
        Track track,
        string? language = null,
        bool skipTrackSource = false,
        string? source = null,
        CancellationToken cancellationToken = default
    )
    {
        _node.ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(track);
        ArgumentException.ThrowIfNullOrWhiteSpace(track.Encoded);
        await _node.EnsureServerInfoAsync(cancellationToken).ConfigureAwait(false);
        NodeGuards.RequireNodeLinkOrPlugin(_node, NodeGuards.LavaLyricsPlugin, "lyrics");

        return await _node
            .Rest.Lyrics.LoadAsync(
                track.Encoded,
                language,
                skipTrackSource,
                source,
                cancellationToken
            )
            .ConfigureAwait(false);
    }

    /// <summary>Loads the chapters of a track, in milliseconds, mostly YouTube videos carry them.</summary>
    /// <remarks>NodeLink only.</remarks>
    /// <param name="track">The track to load the chapters of.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The track's chapters.</returns>
    /// <exception cref="NotSupportedException">The node is not NodeLink.</exception>
    /// <exception cref="IncompatibleServerException">The server's /v4/info version is not supported.</exception>
    public async Task<IReadOnlyList<Chapter>> LoadChaptersAsync(
        Track track,
        CancellationToken cancellationToken = default
    )
    {
        _node.ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(track);
        ArgumentException.ThrowIfNullOrWhiteSpace(track.Encoded);
        await _node.EnsureServerInfoAsync(cancellationToken).ConfigureAwait(false);
        NodeGuards.RequireNodeLink(_node, "chapters");

        return await _node
            .Rest.Lyrics.LoadChaptersAsync(track.Encoded, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Loads the meaning of a track, song lore, translations and cross-references, as its providers resolve
    /// them.
    /// </summary>
    /// <param name="track">The track to load the meaning of.</param>
    /// <param name="language">The language to prefer, null lets the server pick.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The meaning result of the track.</returns>
    /// <exception cref="NotSupportedException">The node is not NodeLink.</exception>
    /// <exception cref="IncompatibleServerException">The server's /v4/info version is not supported.</exception>
    public async Task<MeaningResult> LoadMeaningAsync(
        Track track,
        string? language = null,
        CancellationToken cancellationToken = default
    )
    {
        _node.ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(track);
        ArgumentException.ThrowIfNullOrWhiteSpace(track.Encoded);
        await _node.EnsureServerInfoAsync(cancellationToken).ConfigureAwait(false);
        NodeGuards.RequireNodeLink(_node, "track meanings");

        try
        {
            return await _node
                .Rest.Lyrics.LoadMeaningAsync(track.Encoded, language, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (RestException exception) when (exception.StatusCode == 500)
        {
            var message = TryReadErrorMessage(exception.ResponseBody);
            if (message is null)
                throw;

            return new MeaningResult
            {
                LoadType = MeaningLoadType.Error,
                Data = new MeaningData { ErrorMessage = message },
            };
        }
    }

    private static string? TryReadErrorMessage(string? responseBody)
    {
        if (string.IsNullOrWhiteSpace(responseBody))
            return null;

        try
        {
            using var document = JsonDocument.Parse(responseBody);
            return
                document.RootElement.ValueKind == JsonValueKind.Object
                && document.RootElement.TryGetProperty("message", out var message)
                && message.ValueKind == JsonValueKind.String
                && !string.IsNullOrWhiteSpace(message.GetString())
                ? message.GetString()
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
