using System.Text.Json;
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
    private readonly GuildVoiceForwarder _voiceForward;
    private readonly GuildCommands _commands;
    private readonly Func<Exception, System.EventArgs?, Task> _handlerError;
    private volatile bool _tearingDown;
    private volatile bool _restoring;

    private readonly Lock _restoreLock = new();

    private bool _restoreAgain;
    private RestoreSnapshot? _restoreSnapshot;
    private readonly GuildTimer _idleTimer;
    private readonly GuildTimer _aloneTimer;
    private readonly Lock _listenersLock = new();
    private HashSet<ulong>? _listeners;
    private volatile bool _pausedWhenAlone;

    private sealed record RestoreSnapshot(
        Track? Track,
        TimeSpan? Position,
        int Volume,
        FilterChain Filters,
        bool Paused,
        long CapturedTick
    )
    {
        internal bool HasFilters =>
            !FilterComparer.FiltersEqual(new FilterChain(), Filters)
            || (Filters.Volume is { } volume && volume != 1f);
    }

    private static readonly TimeSpan VoiceCloseGracePeriod = TimeSpan.FromSeconds(2);

    private static readonly TimeSpan ForwardActivityWindow = TimeSpan.FromSeconds(5);

    private static readonly TimeSpan VoiceRescueInterval = TimeSpan.FromSeconds(60);

    private long _voiceRescueTick = long.MinValue / 2;

    private int _voiceCloseHandling;

    private long _lastVoiceConnectedTick = long.MinValue;

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
        Node = node;
        Filters = new GuildFilters(this);
        Lyrics = new GuildLyrics(this);
        SponsorBlock = new GuildSponsorBlock(this);
        Mix = new GuildMix(this);
        Effects = new GuildEffects(this);
        Playback = new GuildPlayback(this, node.Time);
        _voiceForward = new GuildVoiceForwarder(this);
        Patches = new GuildPatches(this);
        Teardown = new GuildTeardown(this);
        _commands = new GuildCommands(this);
        Pump = new EventPump($"guild {guildId}", node.Logger, LogEvents.Playback);
        _handlerError = OnHandlerErrorAsync;
        _idleTimer = new GuildTimer(node.Time, OnIdleTimerFired);
        _aloneTimer = new GuildTimer(node.Time, OnAloneTimerFired);
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

    internal GuildPatches Patches { get; }

    internal GuildTeardown Teardown { get; }

    internal GuildPlayback Playback { get; }

    internal EventPump Pump { get; }

    internal Task RaiseInvalidatedAsync(ulong? lastChannelId) =>
        GuildConnectionInvalidated.RaiseAsync(
            this,
            new GuildConnectionInvalidatedEventArgs(GuildId, lastChannelId),
            _handlerError
        );

    internal void MarkTearingDown()
    {
        _tearingDown = true;
        _idleTimer.Stop();
        _aloneTimer.Stop();
    }

    private int _voiceLeaveClaimed;

    internal bool TryClaimVoiceLeave() => Interlocked.Exchange(ref _voiceLeaveClaimed, 1) == 0;

    internal void ReleaseVoiceLeave() => Volatile.Write(ref _voiceLeaveClaimed, 0);

    private volatile bool _playerCommitted;

    internal bool IsPlayerCommitted => _playerCommitted;

    internal void CommitPlayerUpdate()
    {
        _playerCommitted = true;
        UpdateIdleTimer();
        SyncListeners();
    }

    /// <summary>The node this connection plays through.</summary>
    public Node Node { get; }

    internal TimeProvider Time => Node.Time;

    /// <summary>The audio filters of the connection.</summary>
    public GuildFilters Filters { get; }

    /// <summary>The lyrics subscription of the connection.</summary>
    public GuildLyrics Lyrics { get; }

    /// <summary>The SponsorBlock settings of the connection.</summary>
    public GuildSponsorBlock SponsorBlock { get; }

    /// <summary>The mix layers of the connection.</summary>
    public GuildMix Mix { get; }

    /// <summary>The fading, ducking, loudness and crossfade effects of the connection.</summary>
    public GuildEffects Effects { get; }

    /// <summary>The track currently loaded, or null when nothing is playing.</summary>
    /// <remarks>
    /// A destroy by another client only raises <see cref="PlayerDestroyed"/> and keeps this track until the
    /// next play or a teardown. A later PlayAsync creates the player again.
    /// </remarks>
    public Track? CurrentTrack
    {
        get;
        internal set
        {
            field = value;
            UpdateIdleTimer();
        }
    }

    /// <summary>
    /// How many users who are not bots are in the voice channel with the bot, or null when the bridge does not
    /// report it.
    /// </summary>
    /// <remarks>The built-in bridges report it from the library's cache, see <see cref="IVoiceListenerSource"/>.</remarks>
    public int? Listeners
    {
        get
        {
            lock (_listenersLock)
                return _listeners?.Count;
        }
    }

    /// <summary>The last player state reported by the server, or null when none arrived yet.</summary>
    public PlayerState? State { get; internal set; }

    /// <summary>Whether playback is paused, as last confirmed by the server.</summary>
    public bool IsPaused { get; internal set; }

    /// <summary>
    /// The player volume (0-1000), as last confirmed by the server, 100 is the default.
    /// </summary>
    public int Volume { get; internal set; } = 100;

    /// <summary>
    /// Estimated position, moved forward from the last server report while playing.
    /// </summary>
    /// <remarks>
    /// Null before the first report and after a disconnect, and it stops at the end of a track that finished.
    /// </remarks>
    public TimeSpan? Position => Playback.Position;

    /// <summary>Raised when the server started playing the current track.</summary>
    public event AsyncEventHandler<TrackStartedEventArgs>? TrackStarted;

    /// <summary>
    /// Raised when the current track ended, <see cref="TrackFinishedEventArgs.EndReason"/> says why.
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
    /// Skipped while handlers are far behind, <see cref="State"/> and <see cref="Position"/> always hold the
    /// latest.
    /// </remarks>
    public event AsyncEventHandler<PlayerUpdateEventArgs>? PlayerUpdated;

    /// <summary>Raised when the node's Discord voice websocket for this guild was closed.</summary>
    /// <remarks>
    /// On NodeLink a close here does not always come from Discord, even with a 4xxx code. Check
    /// <see cref="WebSocketClosedEventArgs.Reason"/> before blaming Discord.
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
    /// Skipped while handlers are far behind.
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
    /// NodeLink only. Some reasons only arrive when the server's mix auto-cleanup is on.
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
    /// NodeLink only. The player created by the join usually fires before a handler can subscribe, so treat the
    /// end of the connect as the creation. This event reports the other creations, like another client or a
    /// re-creation after a resume.
    /// </remarks>
    public event AsyncEventHandler<PlayerCreatedEventArgs>? PlayerCreated;

    /// <summary>
    /// Raised when the player was destroyed by an actor outside this client on the same session, not by this
    /// connection's own REST teardown (DisconnectAsync).
    /// </summary>
    /// <remarks>
    /// NodeLink only. A new connection of the same guild can still get a late destroy from the one before it.
    /// The destroy is only a notice, the track, position and filters stay until the next play or a teardown.
    /// </remarks>
    public event AsyncEventHandler<PlayerDestroyedEventArgs>? PlayerDestroyed;

    /// <summary>
    /// Raised when this guild's player is gone for good, the node session could not be resumed and restoring the
    /// connection on the new one failed (see <see cref="GuildConnectionRestored"/>), or a NodeLink cluster worker
    /// crashed and took the player with it.
    /// </summary>
    public event AsyncEventHandler<GuildConnectionInvalidatedEventArgs>? GuildConnectionInvalidated;

    /// <summary>
    /// Raised when the node's session could not be resumed and this connection was restored on the new one,
    /// the bot stays in its voice channel.
    /// </summary>
    /// <remarks>
    /// The new player is empty unless <see cref="NodeOptions.RescuePlayback"/> sent the track again. Calls made
    /// while the restore runs throw <see cref="InvalidOperationException"/>.
    /// </remarks>
    public event AsyncEventHandler<GuildConnectionRestoredEventArgs>? GuildConnectionRestored;

    /// <summary>
    /// Raised when the connection went <see cref="NodeOptions.IdleTimeout"/> without a track, right before it
    /// disconnects.
    /// </summary>
    /// <remarks>A handler that starts a track keeps the connection.</remarks>
    public event AsyncEventHandler<GuildConnectionIdleEventArgs>? IdleTimedOut;

    /// <summary>Raised when <see cref="Listeners"/> changes.</summary>
    public event AsyncEventHandler<GuildConnectionListenersEventArgs>? ListenersChanged;

    /// <summary>
    /// Raised when the connection went <see cref="NodeOptions.AloneTimeout"/> without <see cref="Listeners"/>,
    /// right before it disconnects.
    /// </summary>
    /// <remarks>The connection stays when someone joined while the handlers ran.</remarks>
    public event AsyncEventHandler<GuildConnectionListenersEventArgs>? AloneTimedOut;

    /// <summary>The voice connection dropped and is being re-established.</summary>
    /// <remarks>
    /// NodeLink only. Raised once per retry.
    /// </remarks>
    public event AsyncEventHandler<PlayerReconnectingEventArgs>? PlayerReconnecting;

    /// <summary>The voice connection became established.</summary>
    /// <remarks>
    /// NodeLink only. The first connection usually fires before a handler can subscribe, so treat the end of the
    /// connect as that one.
    /// </remarks>
    public event AsyncEventHandler<PlayerConnectedEventArgs>? PlayerConnected;

    /// <summary>A live stream announced new station metadata while playing.</summary>
    /// <remarks>NodeLink only.</remarks>
    public event AsyncEventHandler<StreamMetadataUpdatedEventArgs>? StreamMetadataUpdated;

    /// <summary>
    /// Raised when a track with EternalBox info starts.
    /// </summary>
    /// <remarks>NodeLink only.</remarks>
    public event AsyncEventHandler<EternalBoxInfoUpdatedEventArgs>? EternalBoxInfoUpdated;

    /// <summary>Raised when the EternalBox engine jumps to a different beat of the playing track.</summary>
    /// <remarks>NodeLink only.</remarks>
    public event AsyncEventHandler<EternalBoxJumpedEventArgs>? EternalBoxJumped;

    /// <summary>Raised when a handler of one of the other events of this connection threw.</summary>
    public event AsyncEventHandler<HandlerErrorEventArgs>? HandlerError;

    /// <summary>
    /// Plays a track, replacing whatever plays unless <see cref="PlayOptions.NoReplace"/> is set.
    /// </summary>
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
    ) => _commands.PlayAsync(track, options, cancellationToken: cancellationToken);

    internal Task PlayWithNextAsync(
        Track track,
        Track nextTrack,
        CancellationToken cancellationToken
    ) => _commands.PlayAsync(track, null, nextTrack, cancellationToken);

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
    /// Changes where the current track stops.
    /// </summary>
    /// <remarks>
    /// The track then ends as if it finished. On NodeLink a change mid-track does not work on every source, so
    /// prefer <see cref="PlayOptions.EndTime"/> there. To clear the end time, play the track again without one,
    /// since a null does not clear it on Lavalink.
    /// </remarks>
    /// <param name="endTime">Where to stop the track.</param>
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
    /// NodeLink can rate-limit requests per guild, which throws a <see cref="Exceptions.RestException"/> with
    /// status 429.
    /// </remarks>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A task that completes when the player was paused.</returns>
    public Task PauseAsync(CancellationToken cancellationToken = default)
    {
        _pausedWhenAlone = false;
        return _commands.SetPausedAsync(true, cancellationToken);
    }

    /// <summary>Resumes playback.</summary>
    /// <remarks>
    /// NodeLink rate-limits requests per guild, see the remarks on <see cref="PauseAsync"/>.
    /// </remarks>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A task that completes when the player was resumed.</returns>
    public Task ResumeAsync(CancellationToken cancellationToken = default)
    {
        _pausedWhenAlone = false;
        return _commands.SetPausedAsync(false, cancellationToken);
    }

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
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The fetched player, now the connection's state.</returns>
    public Task<Player> RefreshAsync(CancellationToken cancellationToken = default) =>
        _commands.RefreshAsync(cancellationToken);

    /// <summary>Disconnects the bot from the voice channel and destroys the player on the server.</summary>
    /// <remarks>
    /// If the call is cancelled or the server fails the destroy, the connection takes no more operations until
    /// another disconnect finishes the job or a new connect takes the guild over.
    /// <para>
    /// Connecting again right after disconnecting a bot that was already out of voice can throw a
    /// <see cref="Exceptions.VoiceHandshakeException"/> until <see cref="NodeOptions.VoiceHandshakeTimeout"/>
    /// passes.
    /// </para>
    /// </remarks>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A task that completes when the player was destroyed.</returns>
    /// <exception cref="RestException">The server refused the player destroy.</exception>
    public Task DisconnectAsync(CancellationToken cancellationToken = default) =>
        Teardown.DisconnectAsync(cancellationToken);

    internal void CaptureForRestore()
    {
        if (_restoreSnapshot is not null || _tearingDown || ChannelId is null)
            return;

        Interlocked.CompareExchange(
            ref _restoreSnapshot,
            new RestoreSnapshot(
                CurrentTrack,
                Position,
                Volume,
                Filters.Applied,
                IsPaused,
                Node.Time.GetMilliseconds()
            ),
            null
        );
    }

    internal void DiscardRestoreSnapshot() => Interlocked.Exchange(ref _restoreSnapshot, null);

    internal async Task<bool> TryRestoreAsync()
    {
        var snapshot = Interlocked.Exchange(ref _restoreSnapshot, null);
        if (_tearingDown || ChannelId is null)
            return false;

        lock (_restoreLock)
        {
            if (_restoring)
            {
                if (snapshot is not null)
                    Interlocked.CompareExchange(ref _restoreSnapshot, snapshot, null);
                _restoreAgain = true;
                return true;
            }

            _restoring = true;
            snapshot = TakeNewerSnapshot(snapshot);
        }

        ulong channelId;
        bool resent;
        var released = false;
        try
        {
            while (true)
            {
                if (_tearingDown || ChannelId is not { } current)
                    return false;
                channelId = current;
                var epoch = Node.EventHandlers.ConnectionEpoch;

                await _voiceForward.DrainAsync().ConfigureAwait(false);
                await Patches.DrainAsync().ConfigureAwait(false);

                ResetPlayerState();
                await Filters.ResetForNewSessionAsync().ConfigureAwait(false);
                _voiceForward.ClearPending();

                var voice = CurrentVoice(channelId);
                var rescue =
                    Node.Options.RescuePlayback
                    && snapshot is not null
                    && !IsRescueExpired(snapshot)
                        ? ResumeIfSomeoneJoined(snapshot)
                        : null;
                var resendsTrack = rescue?.Track is not null;

                var combined =
                    rescue is not null
                    && await _voiceForward
                        .ForwardWithAsync(voice, () => ResendPlaybackAsync(rescue, voice))
                        .ConfigureAwait(false);
                if (!combined)
                    await _voiceForward.ForwardAsync(voice).ConfigureAwait(false);

                bool stalled;
                bool movedOn;
                lock (_restoreLock)
                {
                    var again = _restoreAgain && !_tearingDown;
                    _restoreAgain = false;
                    if (again)
                    {
                        snapshot = TakeNewerSnapshot(snapshot);
                        continue;
                    }

                    stalled = _voiceForward.HasPending;
                    movedOn = Node.EventHandlers.ConnectionEpoch != epoch;
                    if (
                        stalled
                        && movedOn
                        && !_tearingDown
                        && TakeNewerSnapshot(snapshot) is { } kept
                    )
                        Interlocked.CompareExchange(ref _restoreSnapshot, kept, null);
                    _restoring = false;
                    released = true;
                }

                if (_tearingDown)
                    return false;

                if (stalled)
                    return movedOn;

                resent = combined && resendsTrack;
                break;
            }
        }
        finally
        {
            if (!released)
                lock (_restoreLock)
                {
                    _restoring = false;
                    _restoreAgain = false;
                }
        }

        ReapplyListeners();

        Dispatch(
            GuildConnectionRestored,
            new GuildConnectionRestoredEventArgs(GuildId, channelId, resent)
        );
        return true;
    }

    private RestoreSnapshot? TakeNewerSnapshot(RestoreSnapshot? current)
    {
        var next = Interlocked.Exchange(ref _restoreSnapshot, null);
        if (next is null || current is null)
            return next ?? current;
        if (next.Track is null != current.Track is null)
            return next.Track is null ? current : next;
        return next.CapturedTick >= current.CapturedTick ? next : current;
    }

    private RestoreSnapshot ResumeIfSomeoneJoined(RestoreSnapshot snapshot)
    {
        if (snapshot.Track is null || Listeners is not > 0 || !_pausedWhenAlone)
            return snapshot;

        _pausedWhenAlone = false;
        return snapshot with { Paused = false };
    }

    private bool IsRescueExpired(RestoreSnapshot snapshot) =>
        Node.Options.RescueTimeout != Timeout.InfiniteTimeSpan
        && Node.Time.GetMilliseconds() - snapshot.CapturedTick
            > Node.Options.RescueTimeout.TotalMilliseconds;

    private async Task<bool> ResendPlaybackAsync(RestoreSnapshot snapshot, PlayerVoiceState voice)
    {
        try
        {
            await ResendInOneUpdateAsync(snapshot, voice).ConfigureAwait(false);
            return true;
        }
        catch (Exception exception)
        {
            if (_tearingDown || (exception is OperationCanceledException && !Node.IsReady))
                return false;

            Node.HandleError(
                new InvalidOperationException(
                    $"Failed to restore the playback of guild {GuildId}: {exception.Message}",
                    exception
                )
            );
            return false;
        }
    }

    private static long ResendStartMilliseconds(RestoreSnapshot snapshot, Track track)
    {
        if (snapshot.Position is not { } position || track.Info.IsStream)
            return 0;

        var last = track.Info.Length - TimeSpan.FromMilliseconds(1);
        var start = track.Info.Length > TimeSpan.Zero && position >= last ? last : position;
        return (long)start.TotalMilliseconds;
    }

    private async Task ResendInOneUpdateAsync(RestoreSnapshot snapshot, PlayerVoiceState voice)
    {
        var filters = snapshot.HasFilters ? GuildFilters.Normalize(snapshot.Filters) : null;
        int? volume = snapshot.Volume != 100 ? snapshot.Volume : null;
        PlayerUpdateTrack? trackUpdate = null;
        long? startMilliseconds = null;

        if (snapshot.Track is { } track)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(track.Encoded);

            startMilliseconds = ResendStartMilliseconds(snapshot, track);
            trackUpdate = new PlayerUpdateTrack
            {
                Encoded = track.Encoded,
                UserData = track.UserData is { ValueKind: JsonValueKind.Object }
                    ? track.UserData
                    : null,
            };

            Playback.StampPlayRequested();
            if (snapshot.Paused)
                Playback.StampPauseRequested();
        }

        var requestTick = Node.Time.GetMilliseconds();

        var player = await Patches
            .SendAsync(
                ct =>
                    Node.Rest.Players.UpdateAsync(
                        GuildId,
                        new PlayerUpdatePayload
                        {
                            Track = trackUpdate,
                            Position = startMilliseconds,
                            Volume = volume,
                            Paused = trackUpdate is not null && snapshot.Paused ? true : null,
                            Filters = filters,
                            Voice = voice,
                        },
                        ct,
                        timeout: GuildVoiceForwarder.VoiceSendDeadline
                    ),
                CancellationToken.None
            )
            .ConfigureAwait(false);

        if (filters is not null)
            await Filters.LandRescuedAsync(player, filters).ConfigureAwait(false);
        CaptureState(player, requestTick, startMilliseconds);
    }

    internal Task DrainVoiceForwardAsync(CancellationToken cancellationToken = default) =>
        _voiceForward.DrainAsync(cancellationToken);

    internal Task DrainFiltersAsync(CancellationToken cancellationToken = default) =>
        Filters.DrainAsync(cancellationToken);

    internal Task CompleteEventPumpAsync() => Pump.CompleteAsync(TimeSpan.FromSeconds(5));

    /// <summary>Returns the connection as its guild and node.</summary>
    /// <returns>The guild and node of the connection.</returns>
    public override string ToString() => $"{GuildId} ({Node})";

    internal void HandleTrackStart(TrackStartedEventArgs eventArgs)
    {
        if (_tearingDown)
            return;

        Playback.OnTrackStart(eventArgs);

        Dispatch(TrackStarted, eventArgs);
    }

    internal void HandleTrackEnd(TrackFinishedEventArgs eventArgs)
    {
        if (_tearingDown)
            return;

        Playback.OnTrackEnd(eventArgs);

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

        Playback.OnPlayerUpdate(eventArgs);

        Dispatch(PlayerUpdated, eventArgs, droppable: true);
    }

    internal void HandleWebSocketClosed(WebSocketClosedEventArgs eventArgs)
    {
        Dispatch(WebSocketClosed, eventArgs);

        if (!Node.IsNodeLink || eventArgs.ByRemote)
            _ = DisconnectAfterVoiceClosedWithGraceAsync(
                eventArgs.Code,
                Node.Time.GetMilliseconds()
            );
    }

    internal Lock PlaybackPositionLock => Playback.PositionLock;

    internal void ReanchorTimescaleUnderLock(TimescaleFilter? oldTimescale) =>
        Playback.OnTimescaleChangedUnderLock(oldTimescale);

    private async Task DisconnectAfterVoiceClosedWithGraceAsync(int? closeCode, long closeTick)
    {
        var transientClose = closeCode is 4009 or 4015 or 4022;

        await Task.Delay(VoiceCloseGracePeriod, Node.Time, CancellationToken.None)
            .ConfigureAwait(false);

        if (_tearingDown)
            return;

        if (Interlocked.Read(ref _lastVoiceConnectedTick) >= closeTick)
            return;

        var lastForwardTick = _voiceForward.LastForwardTick;
        var suppress =
            !_voiceForward.HasPending
            && (
                transientClose
                    ? Node.Time.GetMilliseconds() - lastForwardTick
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
                && Node.Client is { } bridge
                && Node.Time.GetMilliseconds() - Volatile.Read(ref _voiceRescueTick)
                    > VoiceRescueInterval.TotalMilliseconds
            )
            {
                Volatile.Write(ref _voiceRescueTick, Node.Time.GetMilliseconds());
                var baseline = _voiceForward.LastForwardTick;

                await SendRescueJoinAsync(bridge, leaveFirst: false).ConfigureAwait(false);
                if (await RescueSettledAsync(VoiceCloseGracePeriod, baseline).ConfigureAwait(false))
                    return;

                await SendRescueJoinAsync(bridge, leaveFirst: true).ConfigureAwait(false);
                if (await RescueSettledAsync(ForwardActivityWindow, baseline).ConfigureAwait(false))
                    return;
            }

            await Teardown.AfterVoiceClosedAsync().ConfigureAwait(false);
        }
        finally
        {
            Volatile.Write(ref _voiceCloseHandling, 0);
        }
    }

    private async Task<bool> RescueSettledAsync(TimeSpan window, long baseline)
    {
        await Task.Delay(window, Node.Time, CancellationToken.None).ConfigureAwait(false);
        return _tearingDown || _voiceForward.LastForwardTick > baseline;
    }

    private async Task SendRescueJoinAsync(IDiscordBridge bridge, bool leaveFirst)
    {
        try
        {
            if (_tearingDown)
                return;

            if (leaveFirst)
                await Node.VoiceHandshake.LeaveChannelAsync(bridge, GuildId).ConfigureAwait(false);

            if (_tearingDown)
                return;

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

        Playback.OnVolumeChanged(eventArgs);

        Dispatch(VolumeChanged, eventArgs);
    }

    internal void HandleFiltersChanged(FiltersChangedEventArgs eventArgs) =>
        Dispatch(FiltersChanged, eventArgs);

    internal void HandleTrackSought(TrackSoughtEventArgs eventArgs)
    {
        if (_tearingDown)
            return;

        Playback.OnTrackSought(eventArgs);

        Dispatch(TrackSought, eventArgs);
    }

    internal void HandlePauseChanged(PauseChangedEventArgs eventArgs)
    {
        if (_tearingDown)
            return;

        Playback.OnPauseChanged(eventArgs);

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

    internal void HandlePlayerConnected(PlayerConnectedEventArgs eventArgs)
    {
        Interlocked.Exchange(ref _lastVoiceConnectedTick, Node.Time.GetMilliseconds());
        Dispatch(PlayerConnected, eventArgs);
    }

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
        SyncListeners();
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

        await _voiceForward.ForwardAsync(CurrentVoice(channelId)).ConfigureAwait(false);
    }

    private PlayerVoiceState CurrentVoice(ulong channelId) =>
        new()
        {
            Token = VoiceToken,
            Endpoint = VoiceEndpoint,
            SessionId = VoiceSessionId,
            ChannelId = channelId,
        };

    internal Task ResendPendingVoiceAsync() => _voiceForward.ResendPendingAsync();

    internal static bool IsSameTrack(Track? current, Track candidate) =>
        current is not null
        && (
            (
                !string.IsNullOrEmpty(current.Info.Identifier)
                && current.Info.Identifier == candidate.Info.Identifier
            ) || (!string.IsNullOrEmpty(current.Encoded) && current.Encoded == candidate.Encoded)
        );

    internal void CaptureState(Player player, long requestTick, long? startMilliseconds = null)
    {
        if (_tearingDown)
            Node.Registry.DestroyPlayerExclusivelyAsync(this, CancellationToken.None)
                .ObserveFaults(Node.HandleError);

        Playback.Capture(player, requestTick, startMilliseconds);
    }

    internal void ClearState()
    {
        _tearingDown = true;
        ChannelId = null;
        ResetPlayerState();
    }

    private void ResetPlayerState()
    {
        IsPaused = false;
        State = null;
        Volume = 100;
        Playback.Clear();
    }

    internal async Task ClearStateAsync()
    {
        ClearState();
        await Filters.ResetAsync().ConfigureAwait(false);
        _voiceForward.ClearPending();
    }

    internal void EnsureNotTearingDown()
    {
        if (_tearingDown)
            throw new InvalidOperationException(
                "The connection is being torn down, it no longer accepts operations."
            );
    }

    internal void EnsureActive()
    {
        EnsureNotTearingDown();
        if (_restoring)
            throw new InvalidOperationException(
                "The connection is being restored after the node lost its session, try again in a moment."
            );
        if (!Node.IsReady)
            throw new InvalidOperationException($"The node '{Node.Name}' is not ready.");
        if (!IsConnected)
            throw new InvalidOperationException("The bot is not connected to a voice channel.");
    }

    private void UpdateIdleTimer()
    {
        var timeout = Node.Options.IdleTimeout;
        if (timeout == Timeout.InfiniteTimeSpan || !_playerCommitted)
            return;

        if (CurrentTrack is not null)
            _idleTimer.Disarm();
        else
            _idleTimer.Arm(timeout);
    }

    private void OnIdleTimerFired()
    {
        if (_tearingDown || CurrentTrack is not null || ChannelId is not { } channelId)
            return;

        if (_restoring)
        {
            UpdateIdleTimer();
            return;
        }

        LeaveIdleAsync(channelId).ObserveFaults(Node.HandleError);
    }

    private async Task LeaveIdleAsync(ulong channelId)
    {
        await IdleTimedOut
            .RaiseAsync(this, new GuildConnectionIdleEventArgs(GuildId, channelId), _handlerError)
            .ConfigureAwait(false);

        if (_tearingDown || CurrentTrack is not null)
            return;

        await DisconnectAsync().ConfigureAwait(false);
    }

    internal void SyncListeners()
    {
        if (
            _tearingDown
            || ChannelId is not { } channelId
            || Node.Client is not IVoiceListenerSource source
        )
            return;

        IReadOnlyCollection<ulong>? found;
        try
        {
            found = source.GetListeners(GuildId, channelId);
        }
        catch (Exception exception)
        {
            Node.HandleError(exception);
            return;
        }

        if (found is null)
            return;

        int? before;
        int after;
        lock (_listenersLock)
        {
            before = _listeners?.Count;
            _listeners = [.. found];
            after = _listeners.Count;
        }

        OnListenersChanged(before, after, channelId);
    }

    internal void HandleListenerVoiceState(ulong userId, ulong? channelId)
    {
        if (_tearingDown || ChannelId is not { } own)
            return;

        if (Listeners is null)
            SyncListeners();

        int before;
        int after;
        lock (_listenersLock)
        {
            if (_listeners is null)
                return;

            before = _listeners.Count;
            if (channelId == own)
                _listeners.Add(userId);
            else
                _listeners.Remove(userId);
            after = _listeners.Count;
        }

        OnListenersChanged(before, after, own);
    }

    private void OnListenersChanged(int? before, int after, ulong channelId)
    {
        if (before == after)
            return;

        Dispatch(
            ListenersChanged,
            new GuildConnectionListenersEventArgs(GuildId, channelId, after)
        );
        ApplyListeners(after);
    }

    internal void ReapplyListeners()
    {
        if (Listeners is { } listeners)
            ApplyListeners(listeners);
    }

    private void ApplyListeners(int listeners)
    {
        if (listeners > 0)
            _aloneTimer.Disarm();
        else if (Node.Options.AloneTimeout != Timeout.InfiniteTimeSpan)
            _aloneTimer.Arm(Node.Options.AloneTimeout);

        if (Node.Options.PauseWhenAlone || _pausedWhenAlone)
            SettlePauseWhenAloneAsync().ObserveFaults(Node.HandleError);
    }

    private async Task SettlePauseWhenAloneAsync()
    {
        for (var pass = 0; pass < 4; pass++)
        {
            if (_tearingDown || _restoring || !Node.IsReady || Listeners is not { } listeners)
                return;

            if (listeners == 0)
            {
                if (!Node.Options.PauseWhenAlone || CurrentTrack is null || IsPaused)
                    return;

                await _commands.SetPausedAsync(true, CancellationToken.None).ConfigureAwait(false);
                _pausedWhenAlone = true;
            }
            else
            {
                if (!_pausedWhenAlone)
                    return;

                if (IsPaused)
                    await _commands
                        .SetPausedAsync(false, CancellationToken.None)
                        .ConfigureAwait(false);
                _pausedWhenAlone = false;
            }
        }
    }

    private void OnAloneTimerFired()
    {
        if (_tearingDown || ChannelId is not { } channelId)
            return;

        SyncListeners();
        if (Listeners != 0)
            return;

        if (_restoring)
        {
            _aloneTimer.Arm(Node.Options.AloneTimeout);
            return;
        }

        LeaveAloneAsync(channelId).ObserveFaults(Node.HandleError);
    }

    private async Task LeaveAloneAsync(ulong channelId)
    {
        await AloneTimedOut
            .RaiseAsync(
                this,
                new GuildConnectionListenersEventArgs(GuildId, channelId, 0),
                _handlerError
            )
            .ConfigureAwait(false);

        if (_tearingDown || Listeners != 0)
            return;

        await DisconnectAsync().ConfigureAwait(false);
    }

    private void Dispatch<TArgs>(
        AsyncEventHandler<TArgs>? handler,
        TArgs eventArgs,
        bool droppable = false
    )
        where TArgs : System.EventArgs =>
        Pump.TryDispatch(handler, this, eventArgs, _handlerError, droppable);

    private Task OnHandlerErrorAsync(Exception exception, System.EventArgs? origin) =>
        HandlerError.RaiseAsync(
            this,
            new HandlerErrorEventArgs(exception, origin),
            (handlerFailure, _) =>
            {
                try
                {
                    Node.Logger?.LogError(
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
