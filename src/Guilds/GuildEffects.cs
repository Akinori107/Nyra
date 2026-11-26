using Nyra.Entities;
using Nyra.Internal;

namespace Nyra;

/// <summary>The fading, ducking, loudness and crossfade effects of a guild connection.</summary>
public sealed class GuildEffects
{
    private readonly GuildConnection _connection;
    private readonly Node _node;

    internal GuildEffects(GuildConnection connection)
    {
        _connection = connection;
        _node = connection.Node;
    }

    private ulong GuildId => _connection.GuildId;

    /// <summary>Configures fades around the player lifecycle, null disables every fade.</summary>
    /// <param name="options">The fades to apply, null resets them.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A task that completes when the fades were applied.</returns>
    /// <exception cref="NotSupportedException">The node is not NodeLink.</exception>
    public async Task SetFadingAsync(
        FadingOptions? options = null,
        CancellationToken cancellationToken = default
    )
    {
        NodeGuards.RequireNodeLink(_node, options?.Ducking is not null ? "ducking" : "fading");
        FilterValidation.ThrowIfOutOfRange(
            options?.Ducking?.TargetVolume,
            0.0f,
            1.0f,
            "ducking target volume"
        );
        _connection.EnsureActive();

        var payload = new PlayerUpdatePayload { Fading = options ?? new FadingOptions() };
        await _connection.Patches.UpdateAsync(payload, cancellationToken).ConfigureAwait(false);
        _connection.RememberEffects(payload);
    }

    /// <summary>Turns loudness normalization on or off.</summary>
    /// <param name="enabled">Whether the loudness normalizer is on.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A task that completes when the normalizer was switched.</returns>
    /// <exception cref="NotSupportedException">The node is not NodeLink.</exception>
    public Task SetLoudnessNormalizerAsync(
        bool enabled,
        CancellationToken cancellationToken = default
    ) =>
        UpdateAsync(
            "loudness normalization",
            new PlayerUpdatePayload { LoudnessNormalizer = enabled },
            cancellationToken
        );

    /// <summary>
    /// Turns speaking-detection ducking on or off, the depth and timing live in <see
    /// cref="FadingOptions.Ducking"/> instead.
    /// </summary>
    /// <param name="enabled">Whether ducking is on.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A task that completes when ducking was switched.</returns>
    /// <exception cref="NotSupportedException">The node is not NodeLink.</exception>
    public Task SetDuckingAsync(bool enabled, CancellationToken cancellationToken = default) =>
        UpdateAsync("ducking", new PlayerUpdatePayload { Ducking = enabled }, cancellationToken);

    /// <summary>Configures the crossfade between consecutive tracks, null turns it off.</summary>
    /// <param name="options">The crossfade to apply, null resets it.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A task that completes when the crossfade was applied.</returns>
    /// <exception cref="NotSupportedException">The node is not NodeLink.</exception>
    public Task SetCrossfadeAsync(
        CrossfadeOptions? options = null,
        CancellationToken cancellationToken = default
    ) =>
        UpdateAsync(
            "crossfade",
            new PlayerUpdatePayload { Crossfade = options ?? new CrossfadeOptions() },
            cancellationToken
        );

    /// <summary>
    /// Preloads the next track for a gapless/crossfaded transition when the current one ends, null clears it.
    /// </summary>
    /// <param name="nextTrack">The track to crossfade into, null clears it.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A task that completes when the next track was set.</returns>
    /// <exception cref="NotSupportedException">The node is not NodeLink.</exception>
    public async Task SetNextTrackAsync(
        Track? nextTrack,
        CancellationToken cancellationToken = default
    )
    {
        NodeGuards.RequireNodeLink(_node, "next track preloading");
        _connection.EnsureActive();

        if (nextTrack is not null)
            ArgumentException.ThrowIfNullOrWhiteSpace(nextTrack.Encoded);

        var payload = nextTrack is null
            ? new NextTrackUpdate()
            : new NextTrackUpdate
            {
                NextTrack = new PlayerUpdateTrack { Encoded = nextTrack.Encoded },
            };
        var requestTick = _node.Time.GetMilliseconds();
        var player = await _connection
            .Patches.SendAsync(
                ct => _node.Rest.Players.UpdateAsync(GuildId, payload, ct),
                cancellationToken
            )
            .ConfigureAwait(false);
        _connection.CaptureState(player, requestTick);
    }

    private async Task UpdateAsync(
        string feature,
        PlayerUpdatePayload payload,
        CancellationToken cancellationToken
    )
    {
        NodeGuards.RequireNodeLink(_node, feature);
        _connection.EnsureActive();

        await _connection.Patches.UpdateAsync(payload, cancellationToken).ConfigureAwait(false);
        _connection.RememberEffects(payload);
    }
}
