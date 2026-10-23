using Nyra.Entities;

namespace Nyra;

/// <summary>The SponsorBlock settings of a guild connection.</summary>
public sealed class GuildSponsorBlock
{
    private readonly GuildConnection _connection;
    private readonly Node _node;

    internal GuildSponsorBlock(GuildConnection connection)
    {
        _connection = connection;
        _node = connection.Node;
    }

    private ulong GuildId => _connection.GuildId;

    /// <summary>
    /// Reads the SponsorBlock state, enabled categories, action types, the current track's segments and the
    /// skip margin.
    /// </summary>
    /// <remarks>NodeLink only.</remarks>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The SponsorBlock state of the connection.</returns>
    /// <exception cref="NotSupportedException">The node is not NodeLink.</exception>
    public Task<SponsorBlockState> GetAsync(CancellationToken cancellationToken = default)
    {
        NodeGuards.RequireNodeLink(_node, "SponsorBlock");
        _connection.EnsureActive();

        return _node.Rest.SponsorBlock.GetAsync(GuildId, cancellationToken);
    }

    /// <summary>Changes the SponsorBlock settings, members left null keep their current value.</summary>
    /// <remarks>NodeLink only.</remarks>
    /// <param name="settings">The settings to apply.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The state as the server reports it now.</returns>
    /// <exception cref="NotSupportedException">The node is not NodeLink.</exception>
    public Task<SponsorBlockState> SetAsync(
        SponsorBlockSettings settings,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (settings.SkipMarginMilliseconds is < 0)
            throw new ArgumentOutOfRangeException(
                nameof(settings),
                settings.SkipMarginMilliseconds,
                "The skip margin must be 0 or greater."
            );
        if (settings.Categories?.Contains(null) == true)
            throw new ArgumentException(
                "SponsorBlockSettings.Categories must not contain null entries.",
                nameof(settings)
            );
        if (settings.ActionTypes?.Contains(null) == true)
            throw new ArgumentException(
                "SponsorBlockSettings.ActionTypes must not contain null entries.",
                nameof(settings)
            );
        NodeGuards.RequireNodeLink(_node, "SponsorBlock");
        _connection.EnsureActive();

        return _node.Rest.SponsorBlock.SetAsync(GuildId, settings, cancellationToken);
    }

    /// <summary>Replaces the current track's SponsorBlock segments with the given ones.</summary>
    /// <remarks>NodeLink only.</remarks>
    /// <param name="segments">The segments to submit.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The state as the server reports it now.</returns>
    /// <exception cref="NotSupportedException">The node is not NodeLink.</exception>
    public Task<SponsorBlockState> SetSegmentsAsync(
        IReadOnlyList<SponsorBlockSegment> segments,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(segments);
        if (segments.Any(segment => segment is null))
            throw new ArgumentException(
                "The segment list carries a null element.",
                nameof(segments)
            );
        foreach (var segment in segments)
        {
            if (segment.Start is < 0)
                throw new ArgumentOutOfRangeException(
                    nameof(segments),
                    segment.Start,
                    "A segment start must be 0 or greater."
                );
            if (segment.End is < 0)
                throw new ArgumentOutOfRangeException(
                    nameof(segments),
                    segment.End,
                    "A segment end must be 0 or greater."
                );
            if (segment.Start is not null && segment.End is not null && segment.End < segment.Start)
                throw new ArgumentOutOfRangeException(
                    nameof(segments),
                    segment.End,
                    "A segment end must not come before its start."
                );
        }
        NodeGuards.RequireNodeLink(_node, "SponsorBlock");
        _connection.EnsureActive();

        return _node.Rest.SponsorBlock.SetSegmentsAsync(GuildId, segments, cancellationToken);
    }

    /// <summary>Clears the current track's SponsorBlock segments, the settings survive it.</summary>
    /// <remarks>NodeLink only.</remarks>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A task that completes when the state was cleared.</returns>
    /// <exception cref="NotSupportedException">The node is not NodeLink.</exception>
    public Task ClearAsync(CancellationToken cancellationToken = default)
    {
        NodeGuards.RequireNodeLink(_node, "SponsorBlock");
        _connection.EnsureActive();

        return _node.Rest.SponsorBlock.ClearAsync(GuildId, cancellationToken);
    }
}
