using Nyra.Entities;
using Nyra.Internal;

namespace Nyra;

/// <summary>The mix layers of a guild connection.</summary>
public sealed class GuildMix
{
    private readonly GuildConnection _connection;
    private readonly Node _node;

    internal GuildMix(GuildConnection connection)
    {
        _connection = connection;
        _node = connection.Node;
    }

    private ulong GuildId => _connection.GuildId;

    /// <summary>
    /// Starts a mix layer, the track plays on top of the main one, server-side, the returned <see
    /// cref="MixLayer.Id"/> changes or stops it later.
    /// </summary>
    /// <remarks>
    /// Preconditions the server enforces with a 500 or a 403, the player must have an active stream, the layer
    /// count must be under its cap, the mix toggle must be on. A paused or trackless player answers 500, the
    /// toggle off answers 403.
    /// </remarks>
    /// <param name="track">The track the layer plays.</param>
    /// <param name="volume">How loud the layer plays, 0.0-1.0.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The started layer.</returns>
    /// <exception cref="NotSupportedException">The node is not NodeLink.</exception>
    public Task<MixLayer> StartAsync(
        Track track,
        float? volume = null,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(track);
        ArgumentException.ThrowIfNullOrWhiteSpace(track.Encoded);
        FilterValidation.ThrowIfOutOfRange(volume, 0.0f, 1.0f, "mix volume");
        NodeGuards.RequireNodeLink(_node, "mix layers");
        _connection.EnsureActive();

        return _node.Rest.Mix.CreateAsync(GuildId, track.Encoded, volume, cancellationToken);
    }

    /// <summary>Lists the mix layers playing on top of the main track.</summary>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The layers playing on the connection.</returns>
    /// <exception cref="NotSupportedException">The node is not NodeLink.</exception>
    public Task<IReadOnlyList<MixLayer>> GetLayersAsync(
        CancellationToken cancellationToken = default
    )
    {
        NodeGuards.RequireNodeLink(_node, "mix layers");
        _connection.EnsureActive();

        return _node.Rest.Mix.ListAsync(GuildId, cancellationToken);
    }

    /// <summary>Changes the loudness of one mix layer, 0.0-1.0.</summary>
    /// <param name="mixLayerId">The id of the mix layer.</param>
    /// <param name="volume">How loud the layer plays, 0.0-1.0.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A task that completes when the layer's volume was set.</returns>
    /// <exception cref="NotSupportedException">The node is not NodeLink.</exception>
    public Task SetVolumeAsync(
        string mixLayerId,
        float volume,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(mixLayerId);
        FilterValidation.ThrowIfOutOfRange(volume, 0.0f, 1.0f, "mix volume");
        NodeGuards.RequireNodeLink(_node, "mix layers");
        _connection.EnsureActive();

        return _node.Rest.Mix.SetVolumeAsync(GuildId, mixLayerId, volume, cancellationToken);
    }

    /// <summary>Stops one mix layer, the main track keeps playing.</summary>
    /// <param name="mixLayerId">The id of the mix layer to stop.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A task that completes when the layer was stopped.</returns>
    /// <exception cref="NotSupportedException">The node is not NodeLink.</exception>
    public Task StopAsync(string mixLayerId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(mixLayerId);
        NodeGuards.RequireNodeLink(_node, "mix layers");
        _connection.EnsureActive();

        return _node.Rest.Mix.RemoveAsync(GuildId, mixLayerId, cancellationToken);
    }
}
