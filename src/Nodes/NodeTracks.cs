using Nyra.Entities;
using Nyra.Exceptions;
using Nyra.Internal;

namespace Nyra;

/// <summary>Decoding, encoding and streaming of the tracks of a node.</summary>
public sealed class NodeTracks
{
    private readonly Node _node;

    internal NodeTracks(Node node) => _node = node;

    /// <summary>
    /// Decodes a base64 track through the server (with its metadata), <see cref="Track.Decode"/> skips the
    /// server call for tracks this library understands.
    /// </summary>
    /// <param name="encodedTrack">The base64 track to decode.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The decoded track.</returns>
    /// <exception cref="IncompatibleServerException">The server's /v4/info version is not supported.</exception>
    public async Task<Track> DecodeAsync(
        string encodedTrack,
        CancellationToken cancellationToken = default
    )
    {
        _node.ThrowIfDisposed();
        ArgumentException.ThrowIfNullOrWhiteSpace(encodedTrack);
        await _node.EnsureServerInfoAsync(cancellationToken).ConfigureAwait(false);

        return await _node
            .Rest.Tracks.DecodeAsync(encodedTrack, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>Decodes base64 tracks, contacting the server.</summary>
    /// <param name="encodedTracks">The base64 tracks to decode.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The decoded tracks, in the order they were given.</returns>
    /// <exception cref="IncompatibleServerException">The server's /v4/info version is not supported.</exception>
    public async Task<IReadOnlyList<Track>> DecodeManyAsync(
        IEnumerable<string> encodedTracks,
        CancellationToken cancellationToken = default
    )
    {
        _node.ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(encodedTracks);
        var tracks = encodedTracks.ToList();
        if (tracks.Count == 0)
            throw new ArgumentException(
                "At least one encoded track is required.",
                nameof(encodedTracks)
            );

        if (tracks.Any(static track => string.IsNullOrWhiteSpace(track)))
            throw new ArgumentException(
                "Every encoded track is required, the collection contained a null or whitespace entry.",
                nameof(encodedTracks)
            );
        await _node.EnsureServerInfoAsync(cancellationToken).ConfigureAwait(false);

        return await _node
            .Rest.Tracks.DecodeManyAsync(tracks, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>Encodes track information into a base64 track the node can play.</summary>
    /// <remarks>NodeLink only.</remarks>
    /// <param name="info">The track info to encode.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The encoded base64 track.</returns>
    /// <exception cref="NotSupportedException">The node is not NodeLink.</exception>
    /// <exception cref="IncompatibleServerException">The server's /v4/info version is not supported.</exception>
    public async Task<string> EncodeAsync(
        TrackInfo info,
        CancellationToken cancellationToken = default
    )
    {
        _node.ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(info);
        await _node.EnsureServerInfoAsync(cancellationToken).ConfigureAwait(false);
        NodeGuards.RequireNodeLink(_node, "track encoding");

        return await _node
            .Rest.Tracks.EncodeAsync(ToEncodeRequest(info), cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>Encodes track information into base64 tracks the node can play.</summary>
    /// <remarks>NodeLink only.</remarks>
    /// <param name="infos">The track infos to encode.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The encoded base64 tracks, in the order they were given.</returns>
    /// <exception cref="NotSupportedException">The node is not NodeLink.</exception>
    /// <exception cref="IncompatibleServerException">The server's /v4/info version is not supported.</exception>
    public async Task<IReadOnlyList<string>> EncodeManyAsync(
        IEnumerable<TrackInfo> infos,
        CancellationToken cancellationToken = default
    )
    {
        _node.ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(infos);
        var list = infos.ToList();
        if (list.Count == 0)
            throw new ArgumentException("At least one track is required.", nameof(infos));

        if (list.Any(static t => t is null))
            throw new ArgumentException(
                "Every track info is required, the collection contained a null entry.",
                nameof(infos)
            );

        await _node.EnsureServerInfoAsync(cancellationToken).ConfigureAwait(false);
        NodeGuards.RequireNodeLink(_node, "track encoding");

        var requests = list.Select(ToEncodeRequest).ToList();

        return await _node
            .Rest.Tracks.EncodeManyAsync(requests, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>Resolves the direct stream URL of a track's audio.</summary>
    /// <remarks>
    /// The server picks a default format when no <paramref name="itag"/> is given. The resolved URL is
    /// short-lived, re-resolve it instead of caching it.
    /// </remarks>
    /// <param name="track">The track to resolve.</param>
    /// <param name="itag">The itag to resolve for, null lets the server pick.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The resolved stream URL and format.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="track"/> is null.</exception>
    /// <exception cref="ArgumentException">The track carries a null or empty encoded form.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="itag"/> is negative.</exception>
    /// <exception cref="RestException">The route is disabled on the server, or the track failed to resolve.</exception>
    /// <exception cref="NodeUnreachableException">The transport failed before an answer arrived.</exception>
    /// <exception cref="TimeoutException">The node's REST endpoint did not answer within its bound.</exception>
    /// <exception cref="NotSupportedException">The node is not NodeLink.</exception>
    /// <exception cref="IncompatibleServerException">The server's /v4/info version is not supported.</exception>
    public Task<TrackStreamUrl> GetStreamUrlAsync(
        Track track,
        int? itag = null,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(track);
        return GetStreamUrlAsyncCore(track.Encoded, itag, cancellationToken);
    }

    private async Task<TrackStreamUrl> GetStreamUrlAsyncCore(
        string encodedTrack,
        int? itag,
        CancellationToken cancellationToken = default
    )
    {
        _node.ThrowIfDisposed();
        ArgumentException.ThrowIfNullOrWhiteSpace(encodedTrack);
        if (itag is < 0)
            throw new ArgumentOutOfRangeException(nameof(itag), itag, "Must be 0 or greater.");

        await _node.EnsureServerInfoAsync(cancellationToken).ConfigureAwait(false);
        NodeGuards.RequireNodeLink(_node, "the track stream route");

        return await _node
            .Rest.Tracks.GetStreamUrlAsync(encodedTrack, itag, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Opens the decoded audio of a track as a readable stream, played through the node's audio pipeline with
    /// the given volume, start position and filter chain.
    /// </summary>
    /// <remarks>The caller owns the returned stream.</remarks>
    /// <param name="track">The track to stream.</param>
    /// <param name="volume">The playback volume, 0-1000, 100 is the neutral value.</param>
    /// <param name="position">Where in the track the stream starts.</param>
    /// <param name="filters">The filter chain applied to the stream, null streams unfiltered.</param>
    /// <param name="format">
    /// The container format of the stream, raw PCM by default. Only <see cref="LoadStreamFormat.Raw"/> and
    /// <see cref="LoadStreamFormat.Wav"/> are valid, any other value is rejected before the first request.
    /// </param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The audio stream of the track, the caller must dispose it.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="track"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="volume"/> is outside 0-1000, <paramref name="position"/> is negative, or <paramref
    /// name="format"/> is not <see cref="LoadStreamFormat.Raw"/> or <see cref="LoadStreamFormat.Wav"/>.
    /// </exception>
    /// <exception cref="RestException">The route is disabled on the server, or the track failed to load.</exception>
    /// <exception cref="NodeUnreachableException">The transport failed before an answer arrived.</exception>
    /// <exception cref="TimeoutException">The node's REST endpoint did not answer within its bound.</exception>
    /// <exception cref="NotSupportedException">
    /// The node is not NodeLink, or the node is a NodeLink older than 3.10.0 and <paramref name="format"/> is
    /// <see cref="LoadStreamFormat.Wav"/>.
    /// </exception>
    /// <exception cref="IncompatibleServerException">The server's /v4/info version is not supported.</exception>
    public Task<Stream> OpenAudioStreamAsync(
        Track track,
        int volume = 100,
        TimeSpan? position = null,
        FilterChain? filters = null,
        LoadStreamFormat format = LoadStreamFormat.Raw,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(track);
        if (position is { } negative && negative < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(
                nameof(position),
                position,
                "Must be zero or greater."
            );
        return OpenAudioStreamAsyncCore(
            track.Encoded,
            volume,
            (long)(position?.TotalMilliseconds ?? 0),
            filters,
            format,
            cancellationToken
        );
    }

    private async Task<Stream> OpenAudioStreamAsyncCore(
        string encodedTrack,
        int volume,
        long positionMilliseconds,
        FilterChain? filters = null,
        LoadStreamFormat format = LoadStreamFormat.Raw,
        CancellationToken cancellationToken = default
    )
    {
        _node.ThrowIfDisposed();
        ArgumentException.ThrowIfNullOrWhiteSpace(encodedTrack);
        if (volume is < 0 or > 1000)
            throw new ArgumentOutOfRangeException(
                nameof(volume),
                volume,
                "Must be between 0 and 1000."
            );
        if (format is not (LoadStreamFormat.Raw or LoadStreamFormat.Wav))
            throw new ArgumentOutOfRangeException(
                nameof(format),
                format,
                "The load stream format must be Raw or Wav."
            );

        await _node.EnsureServerInfoAsync(cancellationToken).ConfigureAwait(false);
        NodeGuards.RequireNodeLink(_node, "the load stream route");
        if (format is LoadStreamFormat.Wav)
            NodeGuards.RequireNodeLink(
                _node,
                "the wav format of the load stream",
                new NodeVersion(3, 10, 0)
            );
        if (filters is { } streamFilters)
        {
            FilterValidation.ValidateFilterBounds(streamFilters, nodeLink: true);
            if (streamFilters.Volume is not null)
                throw new NotSupportedException(
                    "The volume filter is Lavalink-only and the load stream route is NodeLink-only, the member would never apply."
                );
        }

        return await _node
            .Rest.Tracks.OpenLoadStreamAsync(
                encodedTrack,
                volume,
                positionMilliseconds,
                filters,
                format,
                cancellationToken
            )
            .ConfigureAwait(false);
    }

    private static TrackEncodeRequest ToEncodeRequest(TrackInfo info) =>
        new()
        {
            Identifier = info.Identifier,
            Author = info.Author,
            Length =
                info.LengthMilliseconds >= TrackInfo.MaxTimeSpanMilliseconds
                    ? 0
                    : info.LengthMilliseconds,
            IsStream = info.IsStream,
            IsSeekable = info.IsSeekable,
            Position =
                info.PositionMilliseconds >= TrackInfo.MaxTimeSpanMilliseconds
                    ? 0
                    : info.PositionMilliseconds,
            Title = info.Title,
            Uri = info.Uri,
            SourceName = info.SourceName,
            ArtworkUrl = info.ArtworkUrl,
            Isrc = info.Isrc,
        };
}
