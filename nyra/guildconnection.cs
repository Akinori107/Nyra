using System.Text.Json;
using DSharpPlus.Entities;
using Nyra.Entities;
using Nyra.Entities.Filters;
using Nyra.EventArgs;
using Nyra.Exceptions;
using Nyra.Internal;

namespace Nyra;

/// <summary>
/// A player bound to a guild on a specific <see cref="Node"/>: playback control, audio filters
/// and Discord voice connection management.
/// </summary>
public sealed class GuildConnection
{
    // Normalization goes through the Lavalink (flat) options: serialize fills the neutral
    // defaults, deserialize reads them back, so the stored chain is always the effective one.
    private static readonly JsonSerializerOptions NormalizeOptions = NyraJson.OptionsFor(false);

    private readonly Node _node;
    private readonly SemaphoreSlim _filterGate = new(1, 1);
    private FilterChain _filters = new();
    private FilterChain? _pendingFilters;
    private long _positionMilliseconds;
    private long _positionTick;
    private bool _positionAdvancing;
    private bool _hasPosition;

    internal GuildConnection(
        Node node,
        ulong guildId,
        DiscordChannel channel,
        string sessionId,
        string voiceEndpoint,
        string voiceToken
    )
    {
        _node = node;
        GuildId = guildId;
        Channel = channel;
        VoiceSessionId = sessionId;
        VoiceEndpoint = voiceEndpoint;
        VoiceToken = voiceToken;
    }

    /// <summary>The guild this connection plays in.</summary>
    public ulong GuildId { get; }

    /// <summary>The voice channel the bot is connected to, or null after the connection was disconnected.</summary>
    public DiscordChannel? Channel { get; internal set; }

    public DiscordGuild? Guild => Channel?.Guild;

    /// <summary>Discord voice session id captured at join; updated when Discord rotates it on a channel move.</summary>
    public string VoiceSessionId { get; internal set; }

    /// <summary>Discord voice server endpoint captured at join.</summary>
    public string VoiceEndpoint { get; }

    /// <summary>Discord voice token captured at join (Discord's, not the node password).</summary>
    public string VoiceToken { get; }

    /// <summary>Whether the bot is in a Discord voice channel on this connection; not the node websocket (see <see cref="Node.IsConnected"/>).</summary>
    public bool IsConnected => Channel is not null;

    /// <summary>The node this connection plays through.</summary>
    public Node Node => _node;

    /// <summary>The track currently loaded, or null when nothing is playing.</summary>
    public Track? CurrentTrack { get; internal set; }

    /// <summary>The last player state reported by the server, or null when none arrived yet.</summary>
    public PlayerState? State { get; internal set; }

    /// <summary>Whether playback is paused, as last confirmed by the server.</summary>
    public bool IsPaused { get; internal set; }

    /// <summary>The player volume (0-1000), as last confirmed by the server; 100 is the default.</summary>
    public int Volume { get; internal set; } = 100;

    /// <summary>
    /// Estimated playback position: the last position reported by the server, advanced by the
    /// elapsed wall time while playing, scaled by the applied <see cref="TimescaleFilter.Speed"/>.
    /// Server updates arrive every few seconds (about 5 on Lavalink, 2 on NodeLink).
    /// </summary>
    public TimeSpan? Position
    {
        get
        {
            if (!_hasPosition)
                return null;

            if (!_positionAdvancing)
                return SafePosition(_positionMilliseconds);

            var speed = _filters.Timescale?.Speed ?? 1.0;
            var position =
                _positionMilliseconds + (long)((Environment.TickCount64 - _positionTick) * speed);
            if (
                CurrentTrack is { Info.LengthMilliseconds: > 0 } track
                && position > track.Info.LengthMilliseconds
            )
                position = track.Info.LengthMilliseconds;

            return SafePosition(position);
        }
    }

    private static TimeSpan SafePosition(long milliseconds) =>
        milliseconds is >= 0 and <= TrackInfo.MaxTimeSpanMilliseconds
            ? TimeSpan.FromMilliseconds(milliseconds)
            : TimeSpan.Zero;

    /// <summary>The filter chain currently applied, as confirmed by the server.</summary>
    public FilterChain AppliedFilters => _filters;

    /// <summary>Raised when the server started playing the current track.</summary>
    public event EventHandler<TrackStartedEventArgs>? TrackStarted;

    /// <summary>Raised when the current track ended; <see cref="TrackFinishedEventArgs.EndReason"/> says why.</summary>
    public event EventHandler<TrackFinishedEventArgs>? TrackFinished;

    /// <summary>Raised when the server failed to load or play the current track.</summary>
    public event EventHandler<TrackExceptionEventArgs>? TrackException;

    /// <summary>Raised when playback of the current track stalled long enough to exceed the node's threshold.</summary>
    public event EventHandler<TrackStuckEventArgs>? TrackStuck;

    /// <summary>Raised on every position report of the player (every few seconds while connected).</summary>
    public event EventHandler<PlayerUpdateEventArgs>? PlayerUpdated;

    /// <summary>Raised when the node's Discord voice websocket for this guild was closed.</summary>
    public event EventHandler<WebsocketClosedEventArgs>? WebSocketClosed;

    /// <summary>Raised when the node loaded the lyrics of the current track through a lyrics subscription.</summary>
    public event EventHandler<LyricsFoundEventArgs>? LyricsFound;

    /// <summary>Raised when no lyrics provider had lyrics for the current track.</summary>
    public event EventHandler<LyricsNotFoundEventArgs>? LyricsNotFound;

    /// <summary>Raised for every lyrics line that becomes current during playback.</summary>
    public event EventHandler<LyricsLineEventArgs>? LyricsLine;

    /// <summary>Raised when a handler of one of the other events of this connection threw.</summary>
    public event EventHandler<PlaybackErrorEventArgs>? PlaybackError;

    /// <summary>
    /// Plays a track, replacing whatever is playing unless <see cref="PlayOptions.NoReplace"/>
    /// is set. Uses <see cref="Track.StartPosition"/> as the position if set and no explicit
    /// <see cref="PlayOptions.StartPosition"/> is given; consumed after the first accepted play.
    /// </summary>
    public async Task PlayAsync(
        Track track,
        PlayOptions? options = null,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(track);
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
        if (startPosition is { } startEnd && endTime is { } endEnd && startEnd >= endEnd)
            throw new ArgumentOutOfRangeException(
                nameof(options),
                "PlayOptions.StartPosition must be earlier than PlayOptions.EndTime."
            );

        EnsureActive();

        // Falls back to the track's URL-derived offset; consumed only once the play is accepted.
        long? positionMilliseconds = (long?)options?.StartPosition?.TotalMilliseconds;
        var urlStart = positionMilliseconds is null ? track.StartPosition : null;
        if (urlStart is not null)
            positionMilliseconds = (long)urlStart.Value.TotalMilliseconds;

        // NodeLink ignores a re-sent identical track; stop it first to force a restart.
        if (_node.IsNodeLink && options?.NoReplace != true && IsSameTrack(CurrentTrack, track))
            await _node
                .Rest.UpdatePlayerAsync(
                    GuildId,
                    new PlayerUpdatePayload { Track = new PlayerUpdateTrack { Encoded = null } },
                    noReplace: false,
                    cancellationToken
                )
                .ConfigureAwait(false);

        var player = await _node
            .Rest.UpdatePlayerAsync(
                GuildId,
                new PlayerUpdatePayload
                {
                    Track = new PlayerUpdateTrack { Encoded = track.Encoded },
                    Position = positionMilliseconds,
                    EndTime = (long?)options?.EndTime?.TotalMilliseconds,
                },
                options?.NoReplace ?? false,
                cancellationToken
            )
            .ConfigureAwait(false);

        // A noReplace refusal echoes the track that kept playing, not this one.
        if (urlStart is not null && IsSameTrack(player.Track, track))
            track.StartPosition = null;

        CaptureState(player);
    }

    /// <summary>Stops the player, clearing the current track.</summary>
    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        EnsureActive();

        var player = await _node
            .Rest.UpdatePlayerAsync(
                GuildId,
                new PlayerUpdatePayload { Track = new PlayerUpdateTrack { Encoded = null } },
                noReplace: false,
                cancellationToken
            )
            .ConfigureAwait(false);

        CaptureState(player);
    }

    /// <summary>Pauses playback.</summary>
    public async Task PauseAsync(CancellationToken cancellationToken = default)
    {
        EnsureActive();

        var player = await _node
            .Rest.UpdatePlayerAsync(
                GuildId,
                new PlayerUpdatePayload { Paused = true },
                noReplace: false,
                cancellationToken
            )
            .ConfigureAwait(false);
        CaptureState(player);
    }

    /// <summary>Resumes playback.</summary>
    public async Task ResumeAsync(CancellationToken cancellationToken = default)
    {
        EnsureActive();

        var player = await _node
            .Rest.UpdatePlayerAsync(
                GuildId,
                new PlayerUpdatePayload { Paused = false },
                noReplace: false,
                cancellationToken
            )
            .ConfigureAwait(false);
        CaptureState(player);
    }

    /// <summary>Seeks to the specified position of the current track.</summary>
    public async Task SeekAsync(TimeSpan position, CancellationToken cancellationToken = default)
    {
        if (position < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(
                nameof(position),
                position,
                "The seek position must not be negative."
            );
        EnsureActive();

        var player = await _node
            .Rest.UpdatePlayerAsync(
                GuildId,
                new PlayerUpdatePayload { Position = (long)position.TotalMilliseconds },
                noReplace: false,
                cancellationToken
            )
            .ConfigureAwait(false);

        CaptureState(player);
    }

    /// <summary>Sets the player volume, 0-1000, where 100 is the default.</summary>
    public async Task SetVolumeAsync(int volume, CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(volume, 0);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(volume, 1000);
        EnsureActive();

        var player = await _node
            .Rest.UpdatePlayerAsync(
                GuildId,
                new PlayerUpdatePayload { Volume = volume },
                noReplace: false,
                cancellationToken
            )
            .ConfigureAwait(false);
        CaptureState(player);
    }

    /// <summary>
    /// Subscribes to lyrics updates: <see cref="LyricsFound"/> delivers the full lyrics,
    /// <see cref="LyricsLine"/> the current line as playback advances.
    /// <paramref name="skipTrackSource"/> skips the track source's own lyrics.
    /// </summary>
    /// <exception cref="NotSupportedException">The node is not NodeLink and does not have the LavaLyrics plugin.</exception>
    public Task SubscribeLyricsAsync(
        bool skipTrackSource = false,
        CancellationToken cancellationToken = default
    )
    {
        NodeGuards.RequireNodeLinkOrPlugin(
            _node,
            NodeGuards.LavaLyricsPlugin,
            "lyrics subscription"
        );
        EnsureActive();

        return _node.Rest.SubscribeLyricsAsync(GuildId, skipTrackSource, cancellationToken);
    }

    /// <summary>Ends the lyrics subscription; no further lyrics events arrive.</summary>
    /// <exception cref="NotSupportedException">The node is not NodeLink and does not have the LavaLyrics plugin.</exception>
    public Task UnsubscribeLyricsAsync(CancellationToken cancellationToken = default)
    {
        NodeGuards.RequireNodeLinkOrPlugin(
            _node,
            NodeGuards.LavaLyricsPlugin,
            "lyrics subscription"
        );
        EnsureActive();

        return _node.Rest.UnsubscribeLyricsAsync(GuildId, cancellationToken);
    }

    /// <summary>Applies an audio filter, replacing the previous filter of the same kind. On NodeLink, a filter set while no track is loaded is re-sent when the next track starts.</summary>
    public async Task AddFilterAsync(IFilter filter, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filter);
        RequireNodeLinkFilters(
            new FilterChain
            {
                Echo = filter as EchoFilter,
                Reverb = filter as ReverbFilter,
                HighPass = filter as HighPassFilter,
                Chorus = filter as ChorusFilter,
                Phaser = filter as PhaserFilter,
                Flanger = filter as FlangerFilter,
                Spatial = filter as SpatialFilter,
                Compressor = filter as CompressorFilter,
                Phonograph = filter as PhonographFilter,
                Tape = filter as TapeFilter,
                Tesseract = filter as TesseractFilter,
            }
        );

        await _filterGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            EnsureActive();

            var chain = filter switch
            {
                KaraokeFilter karaoke => DesiredFilters() with { Karaoke = karaoke },
                TimescaleFilter timescale => DesiredFilters() with { Timescale = timescale },
                TremoloFilter tremolo => DesiredFilters() with { Tremolo = tremolo },
                VibratoFilter vibrato => DesiredFilters() with { Vibrato = vibrato },
                DistortionFilter distortion => DesiredFilters() with { Distortion = distortion },
                RotationFilter rotation => DesiredFilters() with { Rotation = rotation },
                ChannelMixFilter channelMix => DesiredFilters() with { ChannelMix = channelMix },
                LowPassFilter lowPass => DesiredFilters() with { LowPass = lowPass },
                EchoFilter echo => DesiredFilters() with { Echo = echo },
                ReverbFilter reverb => DesiredFilters() with { Reverb = reverb },
                HighPassFilter highPass => DesiredFilters() with { HighPass = highPass },
                ChorusFilter chorus => DesiredFilters() with { Chorus = chorus },
                PhaserFilter phaser => DesiredFilters() with { Phaser = phaser },
                FlangerFilter flanger => DesiredFilters() with { Flanger = flanger },
                SpatialFilter spatial => DesiredFilters() with { Spatial = spatial },
                CompressorFilter compressor => DesiredFilters() with { Compressor = compressor },
                PhonographFilter phonograph => DesiredFilters() with { Phonograph = phonograph },
                TapeFilter tape => DesiredFilters() with { Tape = tape },
                TesseractFilter tesseract => DesiredFilters() with { Tesseract = tesseract },
                _ => throw new ArgumentException(
                    $"The filter type '{filter.GetType().Name}' is not supported.",
                    nameof(filter)
                ),
            };

            await SendFiltersAsync(Normalize(chain), cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _filterGate.Release();
        }
    }

    /// <summary>Removes the audio filter of the given type, when applied.</summary>
    public Task RemoveFilterAsync<TFilter>(CancellationToken cancellationToken = default)
        where TFilter : IFilter => RemoveFilterAsync(typeof(TFilter), cancellationToken);

    /// <summary>Removes the audio filter of the same kind as the given one; matching is by filter type, not by value.</summary>
    public Task RemoveFilterAsync(IFilter filter, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filter);
        return RemoveFilterAsync(filter.GetType(), cancellationToken);
    }

    /// <summary>Removes all audio filters, including the volume filter, the equalizer and plugin filters.</summary>
    public async Task ClearFiltersAsync(CancellationToken cancellationToken = default)
    {
        await _filterGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            EnsureActive();

            await SendFiltersAsync(Normalize(new FilterChain()), cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            _filterGate.Release();
        }
    }

    /// <summary>Sets the equalizer bands, replacing the previous ones; an empty list clears the equalizer.</summary>
    public async Task SetEqualizerAsync(
        IReadOnlyList<EqualizerBand>? bands = null,
        CancellationToken cancellationToken = default
    )
    {
        await _filterGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            EnsureActive();

            var chain = DesiredFilters() with
            {
                Equalizer = bands is { Count: > 0 } ? bands : null,
            };
            await SendFiltersAsync(Normalize(chain), cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _filterGate.Release();
        }
    }

    /// <summary>
    /// Sets the volume filter, 0.0-5.0 (1.0 disables it). Independent of the player volume;
    /// values above 1.0 may clip. Lavalink only — NodeLink does not implement it.
    /// </summary>
    /// <exception cref="NotSupportedException">The node is a NodeLink server.</exception>
    public async Task SetVolumeFilterAsync(
        float volume,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(volume, 0.0f);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(volume, 5.0f);
        NodeGuards.RequireLavalink(_node, "volume filter");

        await _filterGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            EnsureActive();

            var chain = DesiredFilters() with { Volume = volume };
            await SendFiltersAsync(Normalize(chain), cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _filterGate.Release();
        }
    }

    /// <summary>Applies a complete filter chain, replacing everything applied so far; filters left null are cleared.</summary>
    public async Task ApplyFiltersAsync(
        FilterChain filters,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(filters);
        if (filters.Volume is not null)
            NodeGuards.RequireLavalink(_node, "volume filter");
        RequireNodeLinkFilters(filters);

        await _filterGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            EnsureActive();

            await SendFiltersAsync(Normalize(filters), cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _filterGate.Release();
        }
    }

    /// <summary>
    /// Fetches the authoritative player from the server, adopts its state and returns it;
    /// useful to re-synchronize a live connection after a resumed reconnect or when the player
    /// was changed out-of-band. After a session loss there is nothing left to read — the
    /// server destroyed the player — so reconnect the bot instead.
    /// </summary>
    public async Task<Player> RefreshAsync(CancellationToken cancellationToken = default)
    {
        EnsureActive();

        var player =
            await _node.Rest.GetPlayerAsync(GuildId, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"The node has no player for guild {GuildId}.");

        await _filterGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            CaptureState(player);
            if (player.Filters is { } serverFilters)
                _filters = serverFilters;
        }
        finally
        {
            _filterGate.Release();
        }

        return player;
    }

    /// <summary>Disconnects the bot from the voice channel and destroys the player on the server.</summary>
    public async Task DisconnectAsync(CancellationToken cancellationToken = default)
    {
        await _node.SendVoiceLeaveAsync(GuildId).ConfigureAwait(false);
        await _node.DestroyPlayerExclusivelyAsync(this, cancellationToken).ConfigureAwait(false);
        ClearState();
    }

    /// <inheritdoc />
    public override string ToString() => $"{GuildId} ({_node})";

    internal void HandleTrackStart(TrackStartedEventArgs eventArgs)
    {
        CurrentTrack = eventArgs.Track;

        // A filter update NodeLink dropped while no track was loaded is re-sent now that one
        // is playing.
        if (_pendingFilters is not null)
            _ = ResendPendingFiltersAsync();

        TrackStarted.Raise(this, eventArgs, OnHandlerError);
    }

    internal void HandleTrackEnd(TrackFinishedEventArgs eventArgs)
    {
        // Replaced tracks are followed immediately by their successor, so CurrentTrack never
        // flickers to null. Also ignore an end event for a track that isn't current (covers
        // NodeLink's stop-then-play restart, whose TrackEnd can race the play response).
        if (
            eventArgs.EndReason != TrackEndReason.Replaced
            && (eventArgs.Track is null || IsSameTrack(CurrentTrack, eventArgs.Track))
        )
            CurrentTrack = null;

        _positionAdvancing = false;
        TrackFinished.Raise(this, eventArgs, OnHandlerError);
    }

    internal void HandleTrackException(TrackExceptionEventArgs eventArgs) =>
        TrackException.Raise(this, eventArgs, OnHandlerError);

    internal void HandleTrackStuck(TrackStuckEventArgs eventArgs) =>
        TrackStuck.Raise(this, eventArgs, OnHandlerError);

    internal void HandlePlayerUpdate(PlayerUpdateEventArgs eventArgs)
    {
        State = eventArgs.State;
        _positionMilliseconds = eventArgs.State.PositionMilliseconds;
        _positionTick = Environment.TickCount64;
        _hasPosition = true;
        _positionAdvancing =
            eventArgs.State.Connected && !IsPaused && CurrentTrack is { Info.IsStream: false };
        PlayerUpdated.Raise(this, eventArgs, OnHandlerError);
    }

    internal void HandleWebSocketClosed(WebsocketClosedEventArgs eventArgs)
    {
        WebSocketClosed.Raise(this, eventArgs, OnHandlerError);

        // Without the voice websocket there is no audio either; tear the connection down.
        _ = DisconnectAfterVoiceClosedAsync();
    }

    internal void HandleLyricsFound(LyricsFoundEventArgs eventArgs) =>
        LyricsFound.Raise(this, eventArgs, OnHandlerError);

    internal void HandleLyricsNotFound(LyricsNotFoundEventArgs eventArgs) =>
        LyricsNotFound.Raise(this, eventArgs, OnHandlerError);

    internal void HandleLyricsLine(LyricsLineEventArgs eventArgs) =>
        LyricsLine.Raise(this, eventArgs, OnHandlerError);

    private async Task DisconnectAfterVoiceClosedAsync()
    {
        try
        {
            await DisconnectAsync().ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            _node.HandleError(exception);
        }
    }

    /// <summary>The node session was lost; the server destroyed every player bound to it.</summary>
    internal void HandleSessionInvalidated()
    {
        ClearState();
        _node.Connections.TryRemove(GuildId, out _);
    }

    /// <summary>Discord removed the bot from the voice channel; the player is destroyed best-effort.</summary>
    internal async Task HandleVoiceDisconnectedAsync()
    {
        ClearState();
        await _node
            .DestroyPlayerExclusivelyAsync(this, CancellationToken.None)
            .ConfigureAwait(false);
    }

    /// <summary>Discord moved the bot to another voice channel of the guild.</summary>
    internal void HandleVoiceMoved(DiscordChannel channel, string? sessionId)
    {
        Channel = channel;
        if (!string.IsNullOrEmpty(sessionId))
            VoiceSessionId = sessionId;
    }

    /// <summary>Discord re-targeted the voice server; forwards the fresh credentials to the node.</summary>
    internal async Task HandleVoiceServerUpdatedAsync(string token, string endpoint)
    {
        // No channel means no complete voice state to forward; the connection is dying anyway.
        if (Channel is null)
            return;

        try
        {
            await _node
                .Rest.UpdatePlayerAsync(
                    GuildId,
                    new PlayerUpdatePayload
                    {
                        Voice = new VoiceState
                        {
                            Token = token,
                            Endpoint = endpoint,
                            SessionId = VoiceSessionId,
                            ChannelId = Channel.Id,
                        },
                    },
                    noReplace: false,
                    CancellationToken.None
                )
                .ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            _node.HandleError(
                new InvalidOperationException(
                    $"Failed to update the voice connection of guild {GuildId}: {exception.Message}",
                    exception
                )
            );
        }
    }

    /// <summary>Whether the two are the same track, by identifier or encoded match.</summary>
    private static bool IsSameTrack(Track? current, Track candidate) =>
        current is not null
        && (
            (
                !string.IsNullOrEmpty(current.Info.Identifier)
                && current.Info.Identifier == candidate.Info.Identifier
            ) || (!string.IsNullOrEmpty(current.Encoded) && current.Encoded == candidate.Encoded)
        );

    private void CaptureState(Player player)
    {
        CurrentTrack = player.Track;
        IsPaused = player.IsPaused;
        Volume = player.Volume;

        if (player.State is { } state)
        {
            State = state;
            _positionMilliseconds = state.PositionMilliseconds;
            _positionTick = Environment.TickCount64;
            _hasPosition = true;
        }

        _positionAdvancing =
            (player.State?.Connected ?? true)
            && !player.IsPaused
            && player.Track is { Info.IsStream: false };
    }

    /// <summary>The chain a new filter update builds on: the desired one when a previous update was dropped, else the applied one.</summary>
    private FilterChain DesiredFilters() => _pendingFilters ?? _filters;

    private void RequireNodeLinkFilters(FilterChain filters)
    {
        if (filters.Tesseract is not null)
            NodeGuards.RequireNodeLink(_node, "tesseract filter", new NodeVersion(3, 9, 0));
        else if (
            filters.Echo is not null
            || filters.Reverb is not null
            || filters.HighPass is not null
            || filters.Chorus is not null
            || filters.Phaser is not null
            || filters.Flanger is not null
            || filters.Spatial is not null
            || filters.Compressor is not null
            || filters.Phonograph is not null
            || filters.Tape is not null
        )
            NodeGuards.RequireNodeLink(_node, "this audio filter");
    }

    private async Task RemoveFilterAsync(Type filterType, CancellationToken cancellationToken)
    {
        await _filterGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            EnsureActive();

            var chain = filterType switch
            {
                _ when filterType == typeof(KaraokeFilter) => DesiredFilters() with
                {
                    Karaoke = null,
                },
                _ when filterType == typeof(TimescaleFilter) => DesiredFilters() with
                {
                    Timescale = null,
                },
                _ when filterType == typeof(TremoloFilter) => DesiredFilters() with
                {
                    Tremolo = null,
                },
                _ when filterType == typeof(VibratoFilter) => DesiredFilters() with
                {
                    Vibrato = null,
                },
                _ when filterType == typeof(DistortionFilter) => DesiredFilters() with
                {
                    Distortion = null,
                },
                _ when filterType == typeof(RotationFilter) => DesiredFilters() with
                {
                    Rotation = null,
                },
                _ when filterType == typeof(ChannelMixFilter) => DesiredFilters() with
                {
                    ChannelMix = null,
                },
                _ when filterType == typeof(LowPassFilter) => DesiredFilters() with
                {
                    LowPass = null,
                },
                _ when filterType == typeof(EchoFilter) => DesiredFilters() with { Echo = null },
                _ when filterType == typeof(ReverbFilter) => DesiredFilters() with
                {
                    Reverb = null,
                },
                _ when filterType == typeof(HighPassFilter) => DesiredFilters() with
                {
                    HighPass = null,
                },
                _ when filterType == typeof(ChorusFilter) => DesiredFilters() with
                {
                    Chorus = null,
                },
                _ when filterType == typeof(PhaserFilter) => DesiredFilters() with
                {
                    Phaser = null,
                },
                _ when filterType == typeof(FlangerFilter) => DesiredFilters() with
                {
                    Flanger = null,
                },
                _ when filterType == typeof(SpatialFilter) => DesiredFilters() with
                {
                    Spatial = null,
                },
                _ when filterType == typeof(CompressorFilter) => DesiredFilters() with
                {
                    Compressor = null,
                },
                _ when filterType == typeof(PhonographFilter) => DesiredFilters() with
                {
                    Phonograph = null,
                },
                _ when filterType == typeof(TapeFilter) => DesiredFilters() with { Tape = null },
                _ when filterType == typeof(TesseractFilter) => DesiredFilters() with
                {
                    Tesseract = null,
                },
                _ => throw new ArgumentException(
                    $"The filter type '{filterType.Name}' is not supported.",
                    nameof(filterType)
                ),
            };

            await SendFiltersAsync(Normalize(chain), cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _filterGate.Release();
        }
    }

    private async Task SendFiltersAsync(FilterChain desired, CancellationToken cancellationToken)
    {
        var player = await _node
            .Rest.UpdatePlayerAsync(
                GuildId,
                new PlayerUpdatePayload { Filters = desired },
                noReplace: false,
                cancellationToken
            )
            .ConfigureAwait(false);

        if (_node.IsNodeLink && player.Filters is { } effective)
        {
            // NodeLink drops filter changes while no track is loaded, so the response is the
            // only source of truth there; a dropped chain is remembered and re-sent when the
            // next track starts. Lavalink applies exactly what was sent.
            _filters = effective;
            _pendingFilters = FiltersEqual(effective, desired) ? null : desired;
        }
        else
        {
            _filters = desired;
            _pendingFilters = null;
        }
    }

    private async Task ResendPendingFiltersAsync()
    {
        await _filterGate.WaitAsync(CancellationToken.None).ConfigureAwait(false);
        try
        {
            if (_pendingFilters is not { } desired || !_node.IsReady || !IsConnected)
                return;

            var player = await _node
                .Rest.UpdatePlayerAsync(
                    GuildId,
                    new PlayerUpdatePayload { Filters = desired },
                    noReplace: false,
                    CancellationToken.None
                )
                .ConfigureAwait(false);
            if (player.Filters is { } effective)
            {
                _filters = effective;
                if (FiltersEqual(effective, desired))
                    _pendingFilters = null;
            }
        }
        catch (Exception exception)
        {
            // Best-effort on an event path; the next track start retries.
            _node.HandleError(
                new InvalidOperationException(
                    $"Failed to re-apply the filters of guild {GuildId}: {exception.Message}",
                    exception
                )
            );
        }
        finally
        {
            _filterGate.Release();
        }
    }

    /// <summary>Value comparison of two chains; unset members count as their neutral defaults, and plugin filters are not tracked (NodeLink does not echo them).</summary>
    private static bool FiltersEqual(FilterChain left, FilterChain right) =>
        left.Volume == right.Volume
        && left.Karaoke == right.Karaoke
        && left.Timescale == right.Timescale
        && left.Tremolo == right.Tremolo
        && left.Vibrato == right.Vibrato
        && left.Distortion == right.Distortion
        && left.Rotation == right.Rotation
        && left.ChannelMix == right.ChannelMix
        && left.LowPass == right.LowPass
        && BandsEqual(left.Equalizer, right.Equalizer);

    private static bool BandsEqual(
        IReadOnlyList<EqualizerBand>? left,
        IReadOnlyList<EqualizerBand>? right
    )
    {
        if (left is not { Count: > 0 })
            return right is not { Count: > 0 };

        return right is { Count: > 0 } && left.SequenceEqual(right);
    }

    /// <summary>Fills unset members with their neutral defaults, so a chain compares equal to the server echo of the same chain.</summary>
    private static FilterChain Normalize(FilterChain chain) =>
        JsonSerializer.Deserialize<FilterChain>(
            JsonSerializer.SerializeToUtf8Bytes(chain, NormalizeOptions),
            NormalizeOptions
        ) ?? new FilterChain();

    private void ClearState()
    {
        Channel = null;
        CurrentTrack = null;
        IsPaused = false;
        _positionAdvancing = false;
        _hasPosition = false;
        _pendingFilters = null;
    }

    private void EnsureActive()
    {
        if (!_node.IsReady)
            throw new InvalidOperationException($"The node '{_node.Name}' is not ready.");
        if (!IsConnected)
            throw new InvalidOperationException("The bot is not connected to a voice channel.");
    }

    private void OnHandlerError(Exception exception) =>
        PlaybackError.Raise(this, new PlaybackErrorEventArgs(exception), null);
}
