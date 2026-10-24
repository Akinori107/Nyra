using Microsoft.Extensions.Logging;
using Nyra.Abstractions;
using Nyra.Entities;
using Nyra.Entities.Filters;
using Nyra.EventArgs;
using Nyra.Exceptions;
using Nyra.Internal;

namespace Nyra;

/// <summary>
/// A player bound to a guild on a <see cref="Node"/>, playback, filters and Discord voice connection.
/// </summary>
public sealed class GuildConnection
{
    private readonly Node _node;
    private readonly GuildFilters _filters;
    private readonly GuildLyrics _lyrics;
    private readonly GuildSponsorBlock _sponsorBlock;
    private readonly GuildMix _mix;
    private readonly GuildEffects _effects;
    private readonly GuildPlayback _playback;
    private readonly GuildVoiceForwarder _voiceForward;
    private readonly GuildPatches _patches;
    private readonly GuildTeardown _teardown;
    private readonly GuildCommands _commands;
    private readonly EventPump _pump;
    private readonly Func<Exception, System.EventArgs?, Task> _handlerError;
    private volatile bool _tearingDown;

    private static readonly TimeSpan VoiceCloseGracePeriod = TimeSpan.FromSeconds(2);

    private static readonly TimeSpan ForwardActivityWindow = TimeSpan.FromSeconds(5);

    private static readonly TimeSpan VoiceRescueInterval = TimeSpan.FromSeconds(60);

    private long _voiceRescueTick = long.MinValue / 2;

    private int _voiceCloseHandling;

    private bool _observedSelfDeaf;
    private bool _observedSelfMute;

    internal GuildConnection(
        Node node,
        ulong guildId,
        ulong channelId,
        string sessionId,
        string voiceEndpoint,
        string voiceToken
    )
    {
        _node = node;
        _filters = new GuildFilters(this);
        _lyrics = new GuildLyrics(this);
        _sponsorBlock = new GuildSponsorBlock(this);
        _mix = new GuildMix(this);
        _effects = new GuildEffects(this);
        _playback = new GuildPlayback(this);
        _voiceForward = new GuildVoiceForwarder(this);
        _patches = new GuildPatches(this);
        _teardown = new GuildTeardown(this);
        _commands = new GuildCommands(this);
        _pump = new EventPump($"guild {guildId}", node.Logger, LogEvents.Playback);
        _handlerError = OnHandlerErrorAsync;
        GuildId = guildId;
        ChannelId = channelId;
        VoiceSessionId = sessionId;
        VoiceEndpoint = voiceEndpoint;
        VoiceToken = voiceToken;
    }

    /// <summary>The guild the connection plays in.</summary>
    public ulong GuildId { get; }

    /// <summary>
    /// The voice channel id the bot is connected to, or null after the connection was disconnected.
    /// </summary>
    public ulong? ChannelId
    {
        get => Volatile.Read(ref _channelId) is not 0 and var id ? id : null;
        internal set => Volatile.Write(ref _channelId, value ?? 0);
    }

    private ulong _channelId;

    /// <summary>
    /// Discord voice session id captured at join, updated when Discord rotates it on a channel move.
    /// </summary>
    public string VoiceSessionId { get; internal set; }

    /// <summary>
    /// Discord voice server endpoint captured at join, updated when Discord re-targets the voice server.
    /// </summary>
    public string VoiceEndpoint { get; internal set; }

    /// <summary>Discord voice token captured at join, updated when Discord re-targets the voice server.</summary>
    public string VoiceToken { get; internal set; }

    /// <summary>
    /// Whether the bot is in a Discord voice channel on this connection, not the node websocket (see <see
    /// cref="Node.IsConnected"/>).
    /// </summary>
    public bool IsConnected => ChannelId is not null;

    internal bool IsTearingDown => _tearingDown;

    internal GuildPatches Patches => _patches;

    internal GuildTeardown Teardown => _teardown;

    internal GuildPlayback Playback => _playback;

    internal EventPump Pump => _pump;

    internal Task RaiseInvalidatedAsync(ulong? lastChannelId) =>
        GuildConnectionInvalidated.RaiseAsync(
            this,
            new GuildConnectionInvalidatedEventArgs(GuildId, lastChannelId),
            _handlerError
        );

    internal bool IsNodeLinkServer => _node.IsNodeLink;

    internal void MarkTearingDown() => _tearingDown = true;

    private int _voiceLeaveClaimed;

    internal bool TryClaimVoiceLeave() => Interlocked.Exchange(ref _voiceLeaveClaimed, 1) == 0;

    internal void ReleaseVoiceLeave() => Volatile.Write(ref _voiceLeaveClaimed, 0);

    private volatile bool _playerCommitted;

    internal bool IsPlayerCommitted => _playerCommitted;

    internal void CommitPlayerUpdate() => _playerCommitted = true;

    /// <summary>The node this connection plays through.</summary>
    public Node Node => _node;

    /// <summary>The audio filters of the connection.</summary>
    public GuildFilters Filters => _filters;

    /// <summary>The lyrics subscription of the connection.</summary>
    public GuildLyrics Lyrics => _lyrics;

    /// <summary>The SponsorBlock settings of the connection.</summary>
    public GuildSponsorBlock SponsorBlock => _sponsorBlock;

    /// <summary>The mix layers of the connection.</summary>
    public GuildMix Mix => _mix;

    /// <summary>The fading, ducking, loudness and crossfade effects of the connection.</summary>
    public GuildEffects Effects => _effects;

    /// <summary>The track currently loaded, or null when nothing is playing.</summary>
    /// <remarks>
    /// A server-side destroy by another actor (a co-client) only raises <see cref="PlayerDestroyed"/>, it does
    /// not clear this, the stale track lives on until the next play or a teardown resets it, and a later
    /// PlayAsync re-creates the player.
    /// </remarks>
    public Track? CurrentTrack { get; internal set; }

    /// <summary>The last player state reported by the server, or null when none arrived yet.</summary>
    public PlayerState? State { get; internal set; }

    /// <summary>Whether playback is paused, as last confirmed by the server.</summary>
    public bool IsPaused { get; internal set; }

    /// <summary>
    /// The player volume (0-1000), as last confirmed by the server, 100 is the default. The server enforces
    /// the range, the read does not clamp.
    /// </summary>
    public int Volume { get; internal set; } = 100;

    /// <summary>
    /// Estimated position, the last server report advanced by elapsed wall time while playing, scaled by the
    /// applied timescale until the next report lands.
    /// </summary>
    /// <remarks>
    /// The sustained pace follows the raw reports, which differ per server kind. NodeLink reports already
    /// carry the timescale, Lavalink reports advance at wall time under one, so a timescale speed other than
    /// one leaves the Lavalink estimate at wall pace. Reports arrive on the server's player update interval.
    /// <para>
    /// Null before the first report and after a disconnect, freezes at the final position after a natural
    /// end.
    /// </para>
    /// </remarks>
    public TimeSpan? Position => _playback.Position;

    /// <summary>Raised when the server started playing the current track.</summary>
    public event AsyncEventHandler<TrackStartedEventArgs>? TrackStarted;

    /// <summary>
    /// Raised when the current track ended, <see cref="TrackFinishedEventArgs.EndReason"/> says why, a null
    /// <see cref="TrackFinishedEventArgs.Track"/> (NodeLink) may report a reset of an earlier playthrough,
    /// see the remarks of that property.
    /// </summary>
    public event AsyncEventHandler<TrackFinishedEventArgs>? TrackFinished;

    /// <summary>Raised when the server failed to load or play the current track.</summary>
    public event AsyncEventHandler<TrackExceptionEventArgs>? TrackException;

    /// <summary>
    /// Raised when playback of the current track stalled long enough to exceed the node's threshold.
    /// </summary>
    public event AsyncEventHandler<TrackStuckEventArgs>? TrackStuck;

    /// <summary>Raised on every position report of the player.</summary>
    /// <remarks>
    /// Reports are skipped while the connection's event queue is more than 512 events behind (a stuck or slow
    /// handler), the latest state is always available through <see cref="State"/> and <see cref="Position"/>.
    /// </remarks>
    public event AsyncEventHandler<PlayerUpdateEventArgs>? PlayerUpdated;

    /// <summary>Raised when the node's Discord voice websocket for this guild was closed.</summary>
    /// <remarks>
    /// For NodeLink this also reports synthetic voice deaths (worker failure, UDP/IP-discovery loss) that
    /// never closed a Discord websocket (see <see cref="WebSocketClosedEventArgs.Code"/>). NodeLink also
    /// emits deaths that never came from Discord wearing Discord codes, for example 4015 when its voice
    /// library gives up reconnecting and 4014 when the voice server endpoint is null. Do not attribute 4xxx to
    /// Discord on NodeLink without checking the reason.
    /// </remarks>
    public event AsyncEventHandler<WebSocketClosedEventArgs>? WebSocketClosed;

    /// <summary>Raised when the node loaded the lyrics of the current track through a lyrics subscription.</summary>
    public event AsyncEventHandler<LyricsFoundEventArgs>? LyricsFound;

    /// <summary>
    /// Raised when no timed lyrics were found for the track, no provider had any, or the provider returned
    /// text-only lyrics (LavaLyrics).
    /// </summary>
    /// <remarks>NodeLink answers it only when nothing was found.</remarks>
    public event AsyncEventHandler<LyricsNotFoundEventArgs>? LyricsNotFound;

    /// <summary>Raised for every lyrics line that becomes current during playback.</summary>
    /// <remarks>
    /// Lines are skipped while the connection's event queue is more than 512 events behind (a stuck or slow
    /// handler).
    /// </remarks>
    public event AsyncEventHandler<LyricsLineEventArgs>? LyricsLine;

    /// <summary>Raised when the node loaded the SponsorBlock segments of the current track.</summary>
    /// <remarks>NodeLink only.</remarks>
    public event AsyncEventHandler<SponsorBlockSegmentsLoadedEventArgs>? SponsorBlockSegmentsLoaded;

    /// <summary>Raised when the node skipped a SponsorBlock segment of the current track.</summary>
    /// <remarks>NodeLink only.</remarks>
    public event AsyncEventHandler<SponsorBlockSegmentSkippedEventArgs>? SponsorBlockSegmentSkipped;

    /// <summary>Raised when a mix started playing on top of the main track.</summary>
    /// <remarks>NodeLink only.</remarks>
    public event AsyncEventHandler<MixStartedEventArgs>? MixStarted;

    /// <summary>Raised when a mix layer ended.</summary>
    /// <remarks>
    /// NodeLink only. The FINISHED and MAIN_ENDED reasons only arrive when the server's mix auto-cleanup is
    /// enabled, a layer that plays through with it off ends through the mixer's teardown instead.
    /// </remarks>
    public event AsyncEventHandler<MixEndedEventArgs>? MixEnded;

    /// <summary>Raised when the player's volume changed.</summary>
    /// <remarks>NodeLink only.</remarks>
    public event AsyncEventHandler<VolumeChangedEventArgs>? VolumeChanged;

    /// <summary>Raised when the server's applied filter chain changed.</summary>
    /// <remarks>NodeLink only.</remarks>
    public event AsyncEventHandler<FiltersChangedEventArgs>? FiltersChanged;

    /// <summary>Raised when the playback position of the current track was sought.</summary>
    /// <remarks>NodeLink only.</remarks>
    public event AsyncEventHandler<TrackSoughtEventArgs>? TrackSought;

    /// <summary>
    /// Raised on both pausing and resuming, <see cref="PauseChangedEventArgs.IsPaused"/> tells them apart.
    /// </summary>
    /// <remarks>NodeLink only.</remarks>
    public event AsyncEventHandler<PauseChangedEventArgs>? PauseChanged;

    /// <summary>Raised when the server created the player.</summary>
    /// <remarks>
    /// NodeLink only. The creation this connection's own join caused races the return of the connect and
    /// usually fires before any handler can subscribe, so treat the connect's completion as the creation
    /// signal. This event only reports creations by other actors (a co-client, a re-creation after a resume).
    /// </remarks>
    public event AsyncEventHandler<PlayerCreatedEventArgs>? PlayerCreated;

    /// <summary>
    /// Raised when the player was destroyed by an actor outside this client on the same session, not by this
    /// connection's own REST teardown (DisconnectAsync).
    /// </summary>
    /// <remarks>
    /// A destroy frame queued by a kick or pass can still drain through this connection, and a successor
    /// connection of the same guild can observe a stale frame of a predecessor's teardown. The destroy is a
    /// notification only, the connection's playback half (track, position estimate, filters) keeps its last
    /// state until the next play or a teardown resets it. NodeLink only.
    /// </remarks>
    public event AsyncEventHandler<PlayerDestroyedEventArgs>? PlayerDestroyed;

    /// <summary>
    /// Raised when this guild's player is gone for good, the node session could not be resumed, or a NodeLink
    /// cluster worker crashed and took the player with it.
    /// </summary>
    public event AsyncEventHandler<GuildConnectionInvalidatedEventArgs>? GuildConnectionInvalidated;

    /// <summary>The voice connection dropped and is being re-established.</summary>
    /// <remarks>
    /// NodeLink only. The server dispatches this per re-establishment attempt, one reconnect with an
    /// intermediate failure can carry more than one raise.
    /// </remarks>
    public event AsyncEventHandler<PlayerReconnectingEventArgs>? PlayerReconnecting;

    /// <summary>The voice connection became established.</summary>
    /// <remarks>
    /// NodeLink only. The establishment of the join itself can dispatch before a handler can attach (the
    /// connect's roundtrip races the voice websocket), the reliable signal for the first join is the connect
    /// completing, reconnect cycles raise this while subscribed.
    /// </remarks>
    public event AsyncEventHandler<PlayerConnectedEventArgs>? PlayerConnected;

    /// <summary>A live stream announced new station metadata while playing.</summary>
    /// <remarks>NodeLink only.</remarks>
    public event AsyncEventHandler<StreamMetadataUpdatedEventArgs>? StreamMetadataUpdated;

    /// <summary>
    /// Raised when the node reports EternalBox context for the playing track, whenever the server signals the
    /// track start.
    /// </summary>
    /// <remarks>NodeLink only.</remarks>
    public event AsyncEventHandler<EternalBoxInfoUpdatedEventArgs>? EternalBoxInfoUpdated;

    /// <summary>Raised when the EternalBox engine jumps to a different beat of the playing track.</summary>
    /// <remarks>NodeLink only.</remarks>
    public event AsyncEventHandler<EternalBoxJumpedEventArgs>? EternalBoxJumped;

    /// <summary>Raised when a handler of one of the other events of this connection threw.</summary>
    public event AsyncEventHandler<HandlerErrorEventArgs>? HandlerError;

    /// <summary>
    /// Plays a track, replacing whatever plays unless <see cref="PlayOptions.NoReplace"/> is set, falls back
    /// to <see cref="Track.StartPosition"/> without an explicit start, consumed after the first accepted play.
    /// </summary>
    /// <remarks>
    /// The consumption is best-effort on the echo, a play the server ignored (NodeLink drops a re-sent
    /// identical track) can consume the offset without a restart, the next accepted play then starts from
    /// zero.
    /// </remarks>
    /// <param name="track">The track to play.</param>
    /// <param name="options">How to start the track, null starts it plain.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A task that completes when the server accepted the play.</returns>
    /// <exception cref="NotSupportedException">
    /// <see cref="PlayOptions.AudioTrackId"/> was given and the node is not NodeLink.
    /// </exception>
    public Task PlayAsync(
        Track track,
        PlayOptions? options = null,
        CancellationToken cancellationToken = default
    ) => _commands.PlayAsync(track, options, cancellationToken);

    /// <summary>
    /// Plays a raw identifier and lets the server load it, search queries and playlists are rejected by both
    /// servers.
    /// </summary>
    /// <remarks>Lavalink only.</remarks>
    /// <param name="identifier">What to load, a raw identifier (a source URL or a video id).</param>
    /// <param name="options">How to start the loaded track, null starts it plain.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The loaded track, as the player reports it.</returns>
    /// <exception cref="NotSupportedException">The node is a NodeLink server.</exception>
    public Task<Track> PlayIdentifierAsync(
        string identifier,
        PlayOptions? options = null,
        CancellationToken cancellationToken = default
    ) => _commands.PlayIdentifierAsync(identifier, options, cancellationToken);

    /// <summary>
    /// Moves the end time marker mid-track, honored by Lavalink, accepted but not applied by NodeLink, set it
    /// through <see cref="PlayOptions.EndTime"/> there.
    /// </summary>
    /// <remarks>
    /// On Lavalink the marker fires and the end reports as a natural finish. A null <paramref name="endTime"/>
    /// does not clear it, the server skips the marker block for a null value, play the track again without an
    /// end time to clear it.
    /// <para>
    /// On NodeLink an end time that travels with a play works on every source, but a marker moved mid-track
    /// only applies on the native seek paths (SABR sources like YouTube and the legacy path), plain http
    /// sources drop the value on the re-seek. A replay without an end time clears an applied marker, and the
    /// update is silently dropped while the position still reads zero (voice startup, the first second or so).
    /// </para>
    /// </remarks>
    /// <param name="endTime">
    /// The marker to stop the track at. On NodeLink a null value is sent as-is, on Lavalink it is a no-op (the
    /// marker stays armed, see remarks).
    /// </param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A task that completes when the end time was updated.</returns>
    public Task SetEndTimeAsync(TimeSpan? endTime, CancellationToken cancellationToken = default) =>
        _commands.SetEndTimeAsync(endTime, cancellationToken);

    /// <summary>Stops the player, clearing the current track.</summary>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A task that completes when the player was stopped.</returns>
    public Task StopAsync(CancellationToken cancellationToken = default) =>
        _commands.StopAsync(cancellationToken);

    /// <summary>Pauses playback.</summary>
    /// <remarks>
    /// NodeLink rate-limits requests per guild and answers 429, which surfaces as an <see
    /// cref="Exceptions.RestException"/> with status 429. The limits are set on the server and cover any
    /// request for the guild. Lavalink applies no such limit.
    /// </remarks>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A task that completes when the player was paused.</returns>
    public Task PauseAsync(CancellationToken cancellationToken = default) =>
        _commands.SetPausedAsync(true, cancellationToken);

    /// <summary>Resumes playback.</summary>
    /// <remarks>
    /// NodeLink rate-limits requests per guild, see the remarks on <see cref="PauseAsync"/>.
    /// </remarks>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A task that completes when the player was resumed.</returns>
    public Task ResumeAsync(CancellationToken cancellationToken = default) =>
        _commands.SetPausedAsync(false, cancellationToken);

    /// <summary>Seeks the current track to a position.</summary>
    /// <remarks>
    /// NodeLink rate-limits requests per guild, see the remarks on <see cref="PauseAsync"/>.
    /// </remarks>
    /// <param name="position">Where to seek to.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A task that completes when the seek was accepted.</returns>
    public Task SeekAsync(TimeSpan position, CancellationToken cancellationToken = default) =>
        _commands.SeekAsync(position, cancellationToken);

    /// <summary>Sets the volume of the player.</summary>
    /// <remarks>
    /// NodeLink rate-limits requests per guild, see the remarks on <see cref="PauseAsync"/>.
    /// </remarks>
    /// <param name="volume">The volume to set, 100 is the default.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A task that completes when the volume was set.</returns>
    public Task SetVolumeAsync(int volume, CancellationToken cancellationToken = default) =>
        _commands.SetVolumeAsync(volume, cancellationToken);

    /// <summary>
    /// Fetches the player as the server sees it, takes its state in and returns it, useful to re-sync after a
    /// resumed reconnect or an out-of-band change.
    /// </summary>
    /// <remarks>After a session loss the player is gone, reconnect.</remarks>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The fetched player, now the connection's state.</returns>
    public Task<Player> RefreshAsync(CancellationToken cancellationToken = default) =>
        _commands.RefreshAsync(cancellationToken);

    /// <summary>Disconnects the bot from the voice channel and destroys the player on the server.</summary>
    /// <remarks>
    /// A cancellation during the teardown leaves the connection marked as tearing down. The registry removal
    /// runs inside the destroy, so a cancellation before it leaves the connection registered, one after it
    /// leaves it removed but unwiped. Either way it accepts no operations until another disconnect completes
    /// the cleanup or a new connect takes the guild over.
    /// <para>
    /// The state wipe only runs when the destroy settled. The event pump retires with the teardown attempt
    /// either way, since a dying connection has no reader left for events.
    /// </para>
    /// <para>
    /// A destroy the server failed is not swallowed and throws to the caller. The connection stays unwiped and
    /// accepts no operations until a retry or a takeover cleans it up.
    /// </para>
    /// <para>
    /// A connect right after disconnecting a bot that was already out of voice can fail with a <see
    /// cref="Exceptions.VoiceHandshakeException"/> until <see cref="NodeOptions.VoiceHandshakeTimeout"/>
    /// passes. The library ignores the guild's voice state updates while it waits for the leave echo, and none
    /// comes if the bot was not in voice.
    /// </para>
    /// </remarks>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A task that completes when the player was destroyed.</returns>
    /// <exception cref="RestException">The server refused the player destroy.</exception>
    public Task DisconnectAsync(CancellationToken cancellationToken = default) =>
        _teardown.DisconnectAsync(cancellationToken);

    internal Task DrainVoiceForwardAsync(CancellationToken cancellationToken = default) =>
        _voiceForward.DrainAsync(cancellationToken);

    internal Task DrainFiltersAsync(CancellationToken cancellationToken = default) =>
        _filters.DrainAsync(cancellationToken);

    internal Task CompleteEventPumpAsync() => _pump.CompleteAsync(TimeSpan.FromSeconds(5));

    /// <summary>Returns the connection as its guild and node.</summary>
    /// <returns>The guild and node of the connection.</returns>
    public override string ToString() => $"{GuildId} ({_node})";

    internal void HandleTrackStart(TrackStartedEventArgs eventArgs)
    {
        if (_tearingDown)
            return;

        if (_filters.HasPendingFilters)
            _ = _filters.ResendPendingFiltersAsync();

        _playback.OnTrackStart(eventArgs);

        Dispatch(TrackStarted, eventArgs);
    }

    internal void HandleTrackEnd(TrackFinishedEventArgs eventArgs)
    {
        if (_tearingDown)
            return;

        _playback.OnTrackEnd(eventArgs);

        Dispatch(TrackFinished, eventArgs);
    }

    internal void HandleTrackException(TrackExceptionEventArgs eventArgs) =>
        Dispatch(TrackException, eventArgs);

    internal void HandleTrackStuck(TrackStuckEventArgs eventArgs) =>
        Dispatch(TrackStuck, eventArgs);

    internal void HandlePlayerUpdate(PlayerUpdateEventArgs eventArgs)
    {
        if (_tearingDown)
            return;

        _playback.OnPlayerUpdate(eventArgs);

        Dispatch(PlayerUpdated, eventArgs, droppable: true);
    }

    internal void HandleWebSocketClosed(WebSocketClosedEventArgs eventArgs)
    {
        Dispatch(WebSocketClosed, eventArgs);

        if (!_node.IsNodeLink)
            _ = DisconnectAfterVoiceClosedWithGraceAsync(eventArgs.Code, Environment.TickCount64);
    }

    internal Lock PlaybackPositionLock => _playback.PositionLock;

    internal void ReanchorTimescaleUnderLock(TimescaleFilter? oldTimescale) =>
        _playback.OnTimescaleChangedUnderLock(oldTimescale);

    private async Task DisconnectAfterVoiceClosedWithGraceAsync(int? closeCode, long closeTick)
    {
        var transientClose = closeCode is 4009 or 4015 or 4022;

        await Task.Delay(VoiceCloseGracePeriod, CancellationToken.None).ConfigureAwait(false);

        if (_tearingDown)
            return;

        var lastForwardTick = _voiceForward.LastForwardTick;
        var suppress =
            !_voiceForward.HasPending
            && (
                transientClose
                    ? Environment.TickCount64 - lastForwardTick
                        < VoiceCloseGracePeriod.TotalMilliseconds
                            + ForwardActivityWindow.TotalMilliseconds
                    : lastForwardTick >= closeTick
            );
        if (suppress)
            return;

        if (Interlocked.Exchange(ref _voiceCloseHandling, 1) != 0)
            return;

        try
        {
            if (
                closeCode is not 4014
                && _node.Client is { } bridge
                && Environment.TickCount64 - Volatile.Read(ref _voiceRescueTick)
                    > VoiceRescueInterval.TotalMilliseconds
            )
            {
                Volatile.Write(ref _voiceRescueTick, Environment.TickCount64);
                var baseline = _voiceForward.LastForwardTick;

                await SendRescueJoinAsync(bridge, leaveFirst: false).ConfigureAwait(false);
                if (await RescueSettledAsync(VoiceCloseGracePeriod, baseline).ConfigureAwait(false))
                    return;

                await SendRescueJoinAsync(bridge, leaveFirst: true).ConfigureAwait(false);
                if (await RescueSettledAsync(ForwardActivityWindow, baseline).ConfigureAwait(false))
                    return;
            }

            await _teardown.AfterVoiceClosedAsync().ConfigureAwait(false);
        }
        finally
        {
            Volatile.Write(ref _voiceCloseHandling, 0);
        }
    }

    private async Task<bool> RescueSettledAsync(TimeSpan window, long baseline)
    {
        await Task.Delay(window, CancellationToken.None).ConfigureAwait(false);
        return _tearingDown || _voiceForward.LastForwardTick > baseline;
    }

    private async Task SendRescueJoinAsync(IDiscordBridge bridge, bool leaveFirst)
    {
        try
        {
            if (leaveFirst)
                await _node.VoiceHandshake.LeaveChannelAsync(bridge, GuildId).ConfigureAwait(false);

            using var rescueCts = new CancellationTokenSource(
                GuildVoiceForwarder.VoiceSendDeadline
            );
            await bridge
                .SendVoiceUpdateAsync(
                    GuildId,
                    ChannelId,
                    Volatile.Read(ref _observedSelfDeaf),
                    Volatile.Read(ref _observedSelfMute),
                    cancellationToken: rescueCts.Token
                )
                .ConfigureAwait(false);
        }
        catch { }
    }

    internal void HandleLyricsFound(LyricsFoundEventArgs eventArgs) =>
        Dispatch(LyricsFound, eventArgs);

    internal void HandleLyricsNotFound(LyricsNotFoundEventArgs eventArgs) =>
        Dispatch(LyricsNotFound, eventArgs);

    internal void HandleLyricsLine(LyricsLineEventArgs eventArgs) =>
        Dispatch(LyricsLine, eventArgs, droppable: true);

    internal void HandleSponsorBlockSegmentsLoaded(SponsorBlockSegmentsLoadedEventArgs eventArgs) =>
        Dispatch(SponsorBlockSegmentsLoaded, eventArgs);

    internal void HandleSponsorBlockSegmentSkipped(SponsorBlockSegmentSkippedEventArgs eventArgs) =>
        Dispatch(SponsorBlockSegmentSkipped, eventArgs);

    internal void HandleMixStarted(MixStartedEventArgs eventArgs) =>
        Dispatch(MixStarted, eventArgs);

    internal void HandleMixEnded(MixEndedEventArgs eventArgs) => Dispatch(MixEnded, eventArgs);

    internal void HandleVolumeChanged(VolumeChangedEventArgs eventArgs)
    {
        if (_tearingDown)
            return;

        _playback.OnVolumeChanged(eventArgs);

        Dispatch(VolumeChanged, eventArgs);
    }

    internal void HandleFiltersChanged(FiltersChangedEventArgs eventArgs) =>
        Dispatch(FiltersChanged, eventArgs);

    internal void HandleTrackSought(TrackSoughtEventArgs eventArgs)
    {
        if (_tearingDown)
            return;

        _playback.OnTrackSought(eventArgs);

        Dispatch(TrackSought, eventArgs);
    }

    internal void HandlePauseChanged(PauseChangedEventArgs eventArgs)
    {
        if (_tearingDown)
            return;

        _playback.OnPauseChanged(eventArgs);

        Dispatch(PauseChanged, eventArgs);
    }

    internal void HandlePlayerCreated(PlayerCreatedEventArgs eventArgs) =>
        Dispatch(PlayerCreated, eventArgs);

    internal void HandlePlayerDestroyed(PlayerDestroyedEventArgs eventArgs) =>
        Dispatch(PlayerDestroyed, eventArgs);

    internal void HandlePlayerReconnecting(PlayerReconnectingEventArgs eventArgs)
    {
        _ = _voiceForward.ResendPendingAsync();

        Dispatch(PlayerReconnecting, eventArgs);
    }

    internal void HandlePlayerConnected(PlayerConnectedEventArgs eventArgs) =>
        Dispatch(PlayerConnected, eventArgs);

    internal void HandleStreamMetadataUpdated(StreamMetadataUpdatedEventArgs eventArgs) =>
        Dispatch(StreamMetadataUpdated, eventArgs);

    internal void HandleEternalBoxInfoUpdated(EternalBoxInfoUpdatedEventArgs eventArgs) =>
        Dispatch(EternalBoxInfoUpdated, eventArgs);

    internal void HandleEternalBoxJumped(EternalBoxJumpedEventArgs eventArgs) =>
        Dispatch(EternalBoxJumped, eventArgs);

    internal void HandleVoiceMoved(ulong channelId, string? sessionId)
    {
        if (_tearingDown)
            return;

        ChannelId = channelId;
        HandleVoiceSessionUpdated(sessionId);
    }

    internal void HandleVoiceSessionUpdated(string? sessionId)
    {
        if (_tearingDown)
            return;

        if (!string.IsNullOrEmpty(sessionId))
            VoiceSessionId = sessionId;
    }

    internal void ObserveSelfFlags(bool? selfDeaf, bool? selfMute)
    {
        if (selfDeaf is { } deaf)
            Volatile.Write(ref _observedSelfDeaf, deaf);
        if (selfMute is { } mute)
            Volatile.Write(ref _observedSelfMute, mute);
    }

    internal async Task HandleVoiceServerUpdatedAsync(string token, string endpoint)
    {
        if (_tearingDown || ChannelId is not { } channelId)
            return;

        VoiceToken = token;
        VoiceEndpoint = endpoint;

        var voice = new PlayerVoiceState
        {
            Token = token,
            Endpoint = endpoint,
            SessionId = VoiceSessionId,
            ChannelId = channelId,
        };

        await _voiceForward.ForwardAsync(voice).ConfigureAwait(false);
    }

    internal Task ResendPendingVoiceAsync() => _voiceForward.ResendPendingAsync();

    internal static bool IsSameTrack(Track? current, Track candidate) =>
        current is not null
        && (
            (
                !string.IsNullOrEmpty(current.Info.Identifier)
                && current.Info.Identifier == candidate.Info.Identifier
            ) || (!string.IsNullOrEmpty(current.Encoded) && current.Encoded == candidate.Encoded)
        );

    internal void CaptureState(Player player, long requestTick)
    {
        if (_tearingDown)
            _node
                .Registry.DestroyPlayerExclusivelyAsync(this, CancellationToken.None)
                .ObserveFaults(_node.HandleError);

        _playback.Capture(player, requestTick);
    }

    internal void ClearState()
    {
        _tearingDown = true;
        ChannelId = null;
        IsPaused = false;
        State = null;
        Volume = 100;
        _playback.Clear();
    }

    internal async Task ClearStateAsync()
    {
        ClearState();
        await _filters.ResetAsync().ConfigureAwait(false);
        _voiceForward.ClearPending();
    }

    internal void EnsureActive()
    {
        if (_tearingDown)
            throw new InvalidOperationException(
                "The connection is being torn down, it no longer accepts operations."
            );
        if (!_node.IsReady)
            throw new InvalidOperationException($"The node '{_node.Name}' is not ready.");
        if (!IsConnected)
            throw new InvalidOperationException("The bot is not connected to a voice channel.");
    }

    private void Dispatch<TArgs>(
        AsyncEventHandler<TArgs>? handler,
        TArgs eventArgs,
        bool droppable = false
    )
        where TArgs : System.EventArgs =>
        _pump.TryDispatch(handler, this, eventArgs, _handlerError, droppable);

    private Task OnHandlerErrorAsync(Exception exception, System.EventArgs? origin) =>
        HandlerError.RaiseAsync(
            this,
            new HandlerErrorEventArgs(exception, origin),
            (handlerFailure, _) =>
            {
                try
                {
                    _node.Logger?.LogError(
                        LogEvents.Playback,
                        handlerFailure,
                        "A HandlerError handler of guild {GuildId} threw.",
                        GuildId
                    );
                }
                catch { }
                return Task.CompletedTask;
            }
        );
}
