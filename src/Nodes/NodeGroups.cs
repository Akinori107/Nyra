using System.Text.Json;
using Nyra.Entities;
using Nyra.Exceptions;
using Nyra.Internal;

namespace Nyra;

/// <summary>The player groups of a node, NodeLink only.</summary>
public sealed class NodeGroups
{
    private readonly Node _node;

    internal NodeGroups(Node node) => _node = node;

    /// <summary>Lists the player groups of the node's session.</summary>
    /// <remarks>NodeLink only.</remarks>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The groups, empty when the session has none.</returns>
    /// <exception cref="NotSupportedException">The node is not NodeLink.</exception>
    /// <exception cref="IncompatibleServerException">The server's /v4/info version is not supported.</exception>
    public async Task<IReadOnlyList<Group>> ListAsync(CancellationToken cancellationToken = default)
    {
        _node.ThrowIfDisposed();
        await _node.EnsureServerInfoAsync(cancellationToken).ConfigureAwait(false);
        NodeGuards.RequireNodeLink(_node, "groups");

        return await _node.Rest.Groups.ListAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Creates a player group.</summary>
    /// <remarks>
    /// Creating a group records its membership only, it neither creates nor connects any player, the members'
    /// players are created by a group update. NodeLink only.
    /// </remarks>
    /// <param name="groupId">The group id, only letters, digits, underscores and dashes are addressable.</param>
    /// <param name="guildIds">The initial members, optional.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The created group.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="groupId"/> is null or blank.</exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="groupId"/> carries characters the group routes cannot address.
    /// </exception>
    /// <exception cref="RestException">A group with the id already exists, the server answers 409.</exception>
    /// <exception cref="NotSupportedException">The node is not NodeLink.</exception>
    /// <exception cref="IncompatibleServerException">The server's /v4/info version is not supported.</exception>
    public Task<Group> CreateAsync(
        string groupId,
        IEnumerable<ulong>? guildIds = null,
        CancellationToken cancellationToken = default
    ) =>
        CreateAsyncCore(
            groupId,
            guildIds as IReadOnlyList<ulong> ?? guildIds?.ToArray(),
            cancellationToken
        );

    private async Task<Group> CreateAsyncCore(
        string groupId,
        IReadOnlyList<ulong>? guildIds,
        CancellationToken cancellationToken = default
    )
    {
        _node.ThrowIfDisposed();
        ValidateGroupId(groupId);
        await _node.EnsureServerInfoAsync(cancellationToken).ConfigureAwait(false);
        NodeGuards.RequireNodeLink(_node, "groups");

        return await _node
            .Rest.Groups.CreateAsync(groupId, guildIds, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>Reads one player group.</summary>
    /// <remarks>NodeLink only.</remarks>
    /// <param name="groupId">The group id.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The group, null when it does not exist.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="groupId"/> is null or blank.</exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="groupId"/> carries characters the group routes cannot address.
    /// </exception>
    /// <exception cref="NotSupportedException">The node is not NodeLink.</exception>
    /// <exception cref="IncompatibleServerException">The server's /v4/info version is not supported.</exception>
    public async Task<Group?> GetAsync(
        string groupId,
        CancellationToken cancellationToken = default
    )
    {
        _node.ThrowIfDisposed();
        ValidateGroupId(groupId);
        await _node.EnsureServerInfoAsync(cancellationToken).ConfigureAwait(false);
        NodeGuards.RequireNodeLink(_node, "groups");

        return await _node.Rest.Groups.GetAsync(groupId, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Applies an update to a group, membership changes first, then the track and player state on every
    /// member, 35 ms per guild.
    /// </summary>
    /// <remarks>
    /// The server answers 200 even when individual guilds fail, always check the result's errors list.
    /// NodeLink only.
    /// </remarks>
    /// <param name="groupId">The group id.</param>
    /// <param name="update">What the update changes, only the set members are applied.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The membership, the reached players and the per-guild failures.</returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="groupId"/> or <paramref name="update"/> is null.
    /// </exception>
    /// <exception cref="ArgumentException">
    /// The group track names both an encoded track and an identifier, or its user data is not a JSON object.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// The position or end time is negative, or the volume is outside 0-1000.
    /// </exception>
    /// <exception cref="RestException">The group does not exist, the server answers 404.</exception>
    /// <exception cref="NotSupportedException">The node is not NodeLink.</exception>
    /// <exception cref="IncompatibleServerException">The server's /v4/info version is not supported.</exception>
    public async Task<GroupUpdateResult> UpdateAsync(
        string groupId,
        GroupUpdate update,
        CancellationToken cancellationToken = default
    )
    {
        _node.ThrowIfDisposed();
        ValidateGroupId(groupId);
        ArgumentNullException.ThrowIfNull(update);
        await _node.EnsureServerInfoAsync(cancellationToken).ConfigureAwait(false);
        var info = _node.ServerInfo;
        ValidateGroupUpdate(update, info is { IsNodeLink: true });
        NodeGuards.RequireNodeLink(info, _node, "groups");

        return await _node
            .Rest.Groups.UpdateAsync(groupId, update, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>Deletes a player group.</summary>
    /// <remarks>
    /// The members' players keep playing, a delete only drops the membership record. NodeLink only.
    /// </remarks>
    /// <param name="groupId">The group id.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A task that completes when the group was deleted.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="groupId"/> is null or blank.</exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="groupId"/> carries characters the group routes cannot address.
    /// </exception>
    /// <exception cref="RestException">The group does not exist, the server answers 404.</exception>
    /// <exception cref="NotSupportedException">The node is not NodeLink.</exception>
    /// <exception cref="IncompatibleServerException">The server's /v4/info version is not supported.</exception>
    public async Task DeleteAsync(string groupId, CancellationToken cancellationToken = default)
    {
        _node.ThrowIfDisposed();
        ValidateGroupId(groupId);
        await _node.EnsureServerInfoAsync(cancellationToken).ConfigureAwait(false);
        NodeGuards.RequireNodeLink(_node, "groups");

        await _node.Rest.Groups.DeleteAsync(groupId, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Makes every group member play a track.</summary>
    /// <remarks>
    /// A group play always replaces the members' current tracks, there is no no-replace for groups. NodeLink
    /// only.
    /// </remarks>
    /// <param name="groupId">The group id.</param>
    /// <param name="track">The track every member plays.</param>
    /// <param name="position">Where the track starts, null plays from the beginning.</param>
    /// <param name="endTime">Where the track stops, null plays to the end.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The update result, check its errors list.</returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="groupId"/> is null or blank, or <paramref name="track"/> is null.
    /// </exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="groupId"/> carries characters the group routes cannot address, or <paramref
    /// name="track"/> carries a null or blank encoded, the wire stop form.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="position"/> is negative, or <paramref name="endTime"/> is not greater than zero.
    /// </exception>
    /// <exception cref="RestException">The group does not exist, the server answers 404.</exception>
    /// <exception cref="NotSupportedException">The node is not NodeLink.</exception>
    /// <exception cref="IncompatibleServerException">The server's /v4/info version is not supported.</exception>
    public Task<GroupUpdateResult> PlayTrackAsync(
        string groupId,
        Track track,
        TimeSpan? position = null,
        TimeSpan? endTime = null,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(track);
        ArgumentException.ThrowIfNullOrWhiteSpace(track.Encoded);
        if (position is { } negativePosition && negativePosition < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(
                nameof(position),
                position,
                "Must be zero or greater."
            );
        if (endTime is { } nonPositiveEnd && nonPositiveEnd <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(
                nameof(endTime),
                endTime,
                "Must be greater than zero."
            );
        return UpdateAsync(
            groupId,
            new GroupUpdate
            {
                Track = new GroupTrack { Encoded = track.Encoded },
                PositionMilliseconds = (long?)position?.TotalMilliseconds,
                EndTimeMilliseconds = (long?)endTime?.TotalMilliseconds,
            },
            cancellationToken
        );
    }

    /// <summary>Stops every group member's track.</summary>
    /// <remarks>NodeLink only.</remarks>
    /// <param name="groupId">The group id.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The update result, check its errors list.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="groupId"/> is null or blank.</exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="groupId"/> carries characters the group routes cannot address.
    /// </exception>
    /// <exception cref="RestException">The group does not exist, the server answers 404.</exception>
    /// <exception cref="NotSupportedException">The node is not NodeLink.</exception>
    /// <exception cref="IncompatibleServerException">The server's /v4/info version is not supported.</exception>
    public Task<GroupUpdateResult> StopAsync(
        string groupId,
        CancellationToken cancellationToken = default
    ) => UpdateAsync(groupId, new GroupUpdate { Track = GroupTrack.Stop }, cancellationToken);

    /// <summary>Pauses every group member.</summary>
    /// <remarks>NodeLink only.</remarks>
    /// <param name="groupId">The group id.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The update result, check its errors list.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="groupId"/> is null or blank.</exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="groupId"/> carries characters the group routes cannot address.
    /// </exception>
    /// <exception cref="RestException">The group does not exist, the server answers 404.</exception>
    /// <exception cref="NotSupportedException">The node is not NodeLink.</exception>
    /// <exception cref="IncompatibleServerException">The server's /v4/info version is not supported.</exception>
    public Task<GroupUpdateResult> PauseAsync(
        string groupId,
        CancellationToken cancellationToken = default
    ) => UpdateAsync(groupId, new GroupUpdate { Paused = true }, cancellationToken);

    /// <summary>Resumes every group member.</summary>
    /// <remarks>NodeLink only.</remarks>
    /// <param name="groupId">The group id.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The update result, check its errors list.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="groupId"/> is null or blank.</exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="groupId"/> carries characters the group routes cannot address.
    /// </exception>
    /// <exception cref="RestException">The group does not exist, the server answers 404.</exception>
    /// <exception cref="NotSupportedException">The node is not NodeLink.</exception>
    /// <exception cref="IncompatibleServerException">The server's /v4/info version is not supported.</exception>
    public Task<GroupUpdateResult> ResumeAsync(
        string groupId,
        CancellationToken cancellationToken = default
    ) => UpdateAsync(groupId, new GroupUpdate { Paused = false }, cancellationToken);

    /// <summary>Sets the volume of every group member.</summary>
    /// <remarks>NodeLink only.</remarks>
    /// <param name="groupId">The group id.</param>
    /// <param name="volume">The volume, 0-1000, 100 is the neutral value.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The update result, check its errors list.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="groupId"/> is null or blank.</exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="groupId"/> carries characters the group routes cannot address.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="volume"/> is outside 0-1000.</exception>
    /// <exception cref="RestException">The group does not exist, the server answers 404.</exception>
    /// <exception cref="NotSupportedException">The node is not NodeLink.</exception>
    /// <exception cref="IncompatibleServerException">The server's /v4/info version is not supported.</exception>
    public Task<GroupUpdateResult> SetVolumeAsync(
        string groupId,
        int volume,
        CancellationToken cancellationToken = default
    ) => UpdateAsync(groupId, new GroupUpdate { Volume = volume }, cancellationToken);

    /// <summary>Adds guilds to a group.</summary>
    /// <remarks>The addition only changes the membership, it does not touch the players.</remarks>
    /// <param name="groupId">The group id.</param>
    /// <param name="guildIds">The guilds to add.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The update result, check its errors list.</returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="groupId"/> is null or blank, or <paramref name="guildIds"/> is null.
    /// </exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="groupId"/> carries characters the group routes cannot address.
    /// </exception>
    /// <exception cref="RestException">The group does not exist, the server answers 404.</exception>
    /// <exception cref="NotSupportedException">The node is not NodeLink.</exception>
    /// <exception cref="IncompatibleServerException">The server's /v4/info version is not supported.</exception>
    public Task<GroupUpdateResult> AddPlayersAsync(
        string groupId,
        IEnumerable<ulong> guildIds,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(guildIds);
        return UpdateAsync(
            groupId,
            new GroupUpdate
            {
                Players = new GroupMembership
                {
                    Add = guildIds as IReadOnlyList<ulong> ?? guildIds.ToArray(),
                },
            },
            cancellationToken
        );
    }

    /// <summary>Removes guilds from a group.</summary>
    /// <remarks>The removal only drops the membership, the players keep playing.</remarks>
    /// <param name="groupId">The group id.</param>
    /// <param name="guildIds">The guilds to remove.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The update result, check its errors list.</returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="groupId"/> is null or blank, or <paramref name="guildIds"/> is null.
    /// </exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="groupId"/> carries characters the group routes cannot address.
    /// </exception>
    /// <exception cref="RestException">The group does not exist, the server answers 404.</exception>
    /// <exception cref="NotSupportedException">The node is not NodeLink.</exception>
    /// <exception cref="IncompatibleServerException">The server's /v4/info version is not supported.</exception>
    public Task<GroupUpdateResult> RemovePlayersAsync(
        string groupId,
        IEnumerable<ulong> guildIds,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(guildIds);
        return UpdateAsync(
            groupId,
            new GroupUpdate
            {
                Players = new GroupMembership
                {
                    Remove = guildIds as IReadOnlyList<ulong> ?? guildIds.ToArray(),
                },
            },
            cancellationToken
        );
    }

    private static void ValidateGroupId(string groupId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(groupId);
        foreach (var character in groupId)
            if (!char.IsAsciiLetterOrDigit(character) && character is not '_' and not '-')
                throw new ArgumentException(
                    $"'{groupId}' is not a usable group id: only letters, digits, underscores and "
                        + "dashes are addressable on the server.",
                    nameof(groupId)
                );
    }

    private static void ValidateGroupUpdate(GroupUpdate update, bool nodeLink)
    {
        if (update.PositionMilliseconds is < 0)
            throw new ArgumentOutOfRangeException(
                nameof(update),
                update.PositionMilliseconds,
                "The group position must be 0 or greater."
            );

        if (update.EndTimeMilliseconds is <= 0)
            throw new ArgumentOutOfRangeException(
                nameof(update),
                update.EndTimeMilliseconds,
                "The group end time must be greater than zero."
            );

        if (
            update.PositionMilliseconds is { } position
            && update.EndTimeMilliseconds is { } endTime
            && position >= endTime
        )
            throw new ArgumentOutOfRangeException(
                nameof(update),
                update.EndTimeMilliseconds,
                "The group position must be earlier than the group end time."
            );

        if (update.Volume is < 0 or > 1000)
            throw new ArgumentOutOfRangeException(
                nameof(update),
                update.Volume,
                "The group volume must be between 0 and 1000."
            );

        FilterValidation.ThrowIfOutOfRange(
            update.Fading?.Ducking?.TargetVolume,
            0.0f,
            1.0f,
            "ducking target volume"
        );

        if (update.Filters is { } groupFilters)
        {
            FilterValidation.ValidateFilterBounds(groupFilters, nodeLink);
            if (groupFilters.Volume is not null)
                throw new NotSupportedException(
                    "The volume filter is Lavalink-only and groups are NodeLink-only, the member would never apply."
                );
        }

        if (update.Track is { } track)
        {
            if (track.Encoded is not null)
                ArgumentException.ThrowIfNullOrWhiteSpace(track.Encoded);
            if (track.AudioTrackId is not null)
                ArgumentException.ThrowIfNullOrWhiteSpace(track.AudioTrackId);
            if (track.Language is not null)
                ArgumentException.ThrowIfNullOrWhiteSpace(track.Language);

            if (track.Encoded is not null && track.Identifier is not null)
                throw new ArgumentException(
                    "A group track carries the encoded track or the identifier, not both, the identifier is not supported on the group loader, drop it and keep the encoded track.",
                    nameof(update)
                );

            if (track.Identifier is not null)
                throw new NotSupportedException(
                    "A group track cannot play by identifier, the group loader never resolves one, use the encoded track."
                );

            if (track.UserData is { ValueKind: not JsonValueKind.Object })
                throw new ArgumentException(
                    "The group track user data must be a JSON object.",
                    nameof(update)
                );
        }
    }
}
