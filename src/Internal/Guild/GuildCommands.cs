using System.Text.Json;
using Nyra.Entities;

namespace Nyra.Internal;

internal sealed class GuildCommands
{
    private readonly GuildConnection _connection;
    private readonly Node _node;

    internal GuildCommands(GuildConnection connection)
    {
        _connection = connection;
        _node = connection.Node;
    }

    private ulong GuildId => _connection.GuildId;

    internal async Task PlayAsync(
        Track track,
        PlayOptions? options = null,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(track);
        ArgumentException.ThrowIfNullOrWhiteSpace(track.Encoded);
        ValidatePlayOptions(options);

        if (options?.AudioTrackId is not null)
            NodeGuards.RequireNodeLink(_node, "PlayOptions.AudioTrackId");

        _connection.EnsureActive();

        var positionMilliseconds = (long?)options?.StartPosition?.TotalMilliseconds;
        var urlStart = positionMilliseconds is null ? track.StartPosition : null;
        if (urlStart is not null)
            positionMilliseconds = (long)urlStart.Value.TotalMilliseconds;

        if (
            _node.IsNodeLink
            && options?.NoReplace != true
            && GuildConnection.IsSameTrack(_connection.CurrentTrack, track)
        )
            await _connection
                .Patches.UpdateAsync(
                    new PlayerUpdatePayload { Track = new PlayerUpdateTrack { Encoded = null } },
                    cancellationToken
                )
                .ConfigureAwait(false);

        var refusable = options?.NoReplace == true && _connection.CurrentTrack is not null;

        var stamps = _connection.Playback.StampPlayRequested();
        var requestTick = Environment.TickCount64;

        Task<Player> SendPlayAsync(CancellationToken ct) =>
            _node.Rest.Players.UpdateAsync(
                GuildId,
                new PlayerUpdatePayload
                {
                    Track = new PlayerUpdateTrack
                    {
                        Encoded = track.Encoded,
                        UserData = options?.UserData,
                        AudioTrackId = options?.AudioTrackId,
                    },
                    Position = positionMilliseconds,
                    EndTime = (long?)options?.EndTime?.TotalMilliseconds,
                },
                ct,
                options?.NoReplace ?? false
            );

        var player = await SendReconcilingAsync(
                SendPlayAsync,
                current => current is not null && GuildConnection.IsSameTrack(current.Track, track),
                stamps,
                requestTick,
                cancellationToken
            )
            .ConfigureAwait(false);

        if (urlStart is not null && !refusable && GuildConnection.IsSameTrack(player.Track, track))
            track.StartPosition = null;

        _connection.CaptureState(player, requestTick);
    }

    internal async Task<Track> PlayIdentifierAsync(
        string identifier,
        PlayOptions? options = null,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(identifier);
        ValidatePlayOptions(options);
        if (options?.AudioTrackId is not null)
            throw new ArgumentException(
                "PlayOptions.AudioTrackId is not supported by PlayIdentifierAsync.",
                nameof(options)
            );

        NodeGuards.RequireLavalink(_node, "PlayIdentifierAsync");
        _connection.EnsureActive();

        var positionMilliseconds = (long?)options?.StartPosition?.TotalMilliseconds;
        positionMilliseconds ??= UrlStartPosition.ParseMilliseconds(identifier);

        var stamps = _connection.Playback.StampPlayRequested();
        var requestTick = Environment.TickCount64;
        var before = _connection.CurrentTrack;

        Task<Player> SendIdentifierAsync(CancellationToken ct) =>
            _node.Rest.Players.UpdateAsync(
                GuildId,
                new PlayerUpdatePayload
                {
                    Track = new PlayerIdentifierUpdateTrack
                    {
                        Identifier = identifier,
                        UserData = options?.UserData,
                    },
                    Position = positionMilliseconds,
                    EndTime = (long?)options?.EndTime?.TotalMilliseconds,
                },
                ct,
                options?.NoReplace ?? false
            );

        var player = await SendReconcilingAsync(
                SendIdentifierAsync,
                current =>
                    current?.Track is { } serverTrack
                    && (before is null || !GuildConnection.IsSameTrack(serverTrack, before)),
                stamps,
                requestTick,
                cancellationToken
            )
            .ConfigureAwait(false);

        _connection.CaptureState(player, requestTick);
        return player.Track
            ?? throw new InvalidOperationException(
                "The server did not resolve the identifier into a track."
            );
    }

    internal async Task SetEndTimeAsync(
        TimeSpan? endTime,
        CancellationToken cancellationToken = default
    )
    {
        if (endTime is { } end && end <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(
                nameof(endTime),
                end,
                "The end time must be greater than zero."
            );
        _connection.EnsureActive();

        var payload = new EndTimeUpdate { EndTime = (long?)endTime?.TotalMilliseconds };
        var requestTick = Environment.TickCount64;
        var player = await _connection
            .Patches.SendAsync(
                ct => _node.Rest.Players.UpdateAsync(GuildId, payload, ct),
                cancellationToken
            )
            .ConfigureAwait(false);
        _connection.CaptureState(player, requestTick);
    }

    internal async Task StopAsync(CancellationToken cancellationToken = default)
    {
        _connection.EnsureActive();

        await _connection
            .Patches.UpdateAsync(
                new PlayerUpdatePayload { Track = new PlayerUpdateTrack { Encoded = null } },
                cancellationToken
            )
            .ConfigureAwait(false);
    }

    internal async Task SetPausedAsync(bool paused, CancellationToken cancellationToken = default)
    {
        _connection.EnsureActive();

        var stamps = paused
            ? _connection.Playback.StampPauseRequested()
            : _connection.Playback.StampResumeRequested();
        var requestTick = Environment.TickCount64;

        Task<Player> SendPausedAsync(CancellationToken ct) =>
            _node.Rest.Players.UpdateAsync(
                GuildId,
                new PlayerUpdatePayload { Paused = paused },
                ct
            );

        var player = await SendReconcilingAsync(
                SendPausedAsync,
                current => current?.IsPaused == paused,
                stamps,
                requestTick,
                cancellationToken
            )
            .ConfigureAwait(false);
        _connection.CaptureState(player, requestTick);
    }

    internal async Task SeekAsync(TimeSpan position, CancellationToken cancellationToken = default)
    {
        if (position < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(
                nameof(position),
                position,
                "The seek position must not be negative."
            );
        _connection.EnsureActive();

        await _connection
            .Patches.UpdateAsync(
                new PlayerUpdatePayload { Position = (long)position.TotalMilliseconds },
                cancellationToken
            )
            .ConfigureAwait(false);
    }

    internal async Task SetVolumeAsync(int volume, CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(volume, 0);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(volume, 1000);
        _connection.EnsureActive();

        await _connection
            .Patches.UpdateAsync(new PlayerUpdatePayload { Volume = volume }, cancellationToken)
            .ConfigureAwait(false);
    }

    internal async Task<Player> RefreshAsync(CancellationToken cancellationToken = default)
    {
        _connection.EnsureActive();

        var requestTick = Environment.TickCount64;
        var requestTicket = GuildFilters.NextTicket();
        var player =
            await _node.Rest.Players.GetAsync(GuildId, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"The node has no player for guild {GuildId}.");

        await _connection
            .Filters.CaptureServerFiltersAsync(player, requestTicket, cancellationToken)
            .ConfigureAwait(false);
        _connection.CaptureState(player, requestTick);

        return player;
    }

    private async Task<Player> SendReconcilingAsync(
        Func<CancellationToken, Task<Player>> send,
        Func<Player?, bool> landed,
        GuildPlayback.RequestTicks stamps,
        long requestTick,
        CancellationToken cancellationToken
    )
    {
        try
        {
            return await _connection
                .Patches.SendAsync(send, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            if (
                !GuildPatches.MayHaveLanded(exception)
                || !await _connection
                    .Patches.ReconcileLandedAsync(landed, requestTick, cancellationToken)
                    .ConfigureAwait(false)
            )
                _connection.Playback.RestoreRequestTicks(stamps);
            throw;
        }
    }

    private static void ValidatePlayOptions(PlayOptions? options)
    {
        var startPosition = options?.StartPosition;
        var endTime = options?.EndTime;

        if (startPosition is { } start && start < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(
                nameof(options),
                start,
                "PlayOptions.StartPosition must not be negative."
            );
        if (endTime is { } end && end <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(
                nameof(options),
                end,
                "PlayOptions.EndTime must be greater than zero."
            );
        ThrowIfUserDataNotAnObject(options?.UserData, nameof(options));
        if (startPosition is { } startEnd && endTime is { } endEnd && startEnd >= endEnd)
            throw new ArgumentOutOfRangeException(
                nameof(options),
                "PlayOptions.StartPosition must be earlier than PlayOptions.EndTime."
            );
        if (options?.AudioTrackId is { } audioTrackId && string.IsNullOrWhiteSpace(audioTrackId))
            throw new ArgumentException(
                "PlayOptions.AudioTrackId must not be empty or whitespace.",
                nameof(options)
            );
    }

    private static void ThrowIfUserDataNotAnObject(JsonElement? userData, string paramName)
    {
        if (userData is { ValueKind: not JsonValueKind.Object })
            throw new ArgumentException(
                "PlayOptions.UserData must be a JSON object, other value kinds are rejected by the server.",
                paramName
            );
    }
}
