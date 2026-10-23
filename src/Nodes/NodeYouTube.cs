using Nyra.Entities;
using Nyra.Exceptions;

namespace Nyra;

/// <summary>The YouTube source configuration of a node.</summary>
public sealed class NodeYouTube
{
    private readonly Node _node;

    internal NodeYouTube(Node node) => _node = node;

    /// <summary>
    /// Reads the runtime YouTube configuration of the node, the masked refresh token and visitor data, whether
    /// a token is configured at all.
    /// </summary>
    /// <remarks>
    /// With <paramref name="validate"/> the server also probes whether the stored token still works.
    /// </remarks>
    /// <param name="validate">Whether the server should validate the stored token while reading.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The current YouTube configuration, tokens masked.</returns>
    /// <exception cref="NotSupportedException">The node is not NodeLink.</exception>
    /// <exception cref="IncompatibleServerException">The server's /v4/info version is not supported.</exception>
    public async Task<YouTubeConfig> GetConfigAsync(
        bool validate = false,
        CancellationToken cancellationToken = default
    )
    {
        _node.ThrowIfDisposed();
        await _node.EnsureServerInfoAsync(cancellationToken).ConfigureAwait(false);
        NodeGuards.RequireNodeLink(_node, "the YouTube config");

        return await _node
            .Rest.YouTube.GetConfigAsync(validate, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>Changes the runtime YouTube configuration of the node.</summary>
    /// <remarks>
    /// The server validates a new refresh token before applying it and rejects the whole patch when it fails,
    /// so a bad token never breaks the working configuration.
    /// </remarks>
    /// <param name="refreshToken">The new OAuth refresh token, null keeps the current one.</param>
    /// <param name="visitorData">The new visitor data, null keeps the current one.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The outcome of the update as the server reports it.</returns>
    /// <exception cref="ArgumentException">
    /// Both <paramref name="refreshToken"/> and <paramref name="visitorData"/> are null or empty.
    /// </exception>
    /// <exception cref="RestException">The server rejected the new token, no changes were applied.</exception>
    /// <exception cref="NotSupportedException">The node is not NodeLink.</exception>
    /// <exception cref="IncompatibleServerException">The server's /v4/info version is not supported.</exception>
    public async Task<YouTubeConfigUpdate> UpdateConfigAsync(
        string? refreshToken = null,
        string? visitorData = null,
        CancellationToken cancellationToken = default
    )
    {
        _node.ThrowIfDisposed();
        if (string.IsNullOrEmpty(refreshToken) && string.IsNullOrEmpty(visitorData))
            throw new ArgumentException("At least one of refreshToken or visitorData must be set.");
        if (refreshToken is not null && string.IsNullOrWhiteSpace(refreshToken))
            throw new ArgumentException(
                "The refresh token must not be blank when set.",
                nameof(refreshToken)
            );
        if (visitorData is not null && string.IsNullOrWhiteSpace(visitorData))
            throw new ArgumentException(
                "The visitor data must not be blank when set.",
                nameof(visitorData)
            );

        await _node.EnsureServerInfoAsync(cancellationToken).ConfigureAwait(false);
        NodeGuards.RequireNodeLink(_node, "the YouTube config");

        return await _node
            .Rest.YouTube.UpdateConfigAsync(
                new YouTubeConfigPatch(refreshToken, visitorData),
                cancellationToken
            )
            .ConfigureAwait(false);
    }

    /// <summary>Exchanges a YouTube refresh token for a fresh access token through the node's OAuth route.</summary>
    /// <remarks>
    /// The request carries the server's own OAuth credentials, the client only supplies the refresh token.
    /// </remarks>
    /// <param name="refreshToken">The refresh token to exchange.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The issued access token.</returns>
    /// <exception cref="ArgumentException"><paramref name="refreshToken"/> is null or blank.</exception>
    /// <exception cref="RestException">The token exchange failed.</exception>
    /// <exception cref="NotSupportedException">The node is not NodeLink.</exception>
    /// <exception cref="IncompatibleServerException">The server's /v4/info version is not supported.</exception>
    public async Task<YouTubeToken> RefreshTokenAsync(
        string refreshToken,
        CancellationToken cancellationToken = default
    )
    {
        _node.ThrowIfDisposed();
        ArgumentException.ThrowIfNullOrWhiteSpace(refreshToken);

        await _node.EnsureServerInfoAsync(cancellationToken).ConfigureAwait(false);
        NodeGuards.RequireNodeLink(_node, "the YouTube OAuth route");

        return await _node
            .Rest.YouTube.RefreshTokenAsync(refreshToken, cancellationToken)
            .ConfigureAwait(false);
    }
}
