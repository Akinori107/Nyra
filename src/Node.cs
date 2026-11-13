using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Nyra.Abstractions;
using Nyra.Entities;
using Nyra.EventArgs;
using Nyra.Exceptions;
using Nyra.Internal;

namespace Nyra;

/// <summary>
/// A connection to a Lavalink v4.2+ or NodeLink v3.9+ server, which one is detected at connect. An older
/// server makes the queries throw <see cref="IncompatibleServerException"/>.
/// </summary>
public sealed class Node : IAsyncDisposable, IDisposable
{
    private readonly NodeQueries _queries;
    private readonly NodeServerInfo _serverInfo;
    private readonly NodeConnector _connector;
    private readonly EventPump _events;
    private static readonly TimeSpan TimeoutLimit = TimeSpan.FromDays(40);

    private int _stopEpoch;
    private volatile bool _disposed;

    /// <summary>Creates a node for an endpoint.</summary>
    /// <param name="endpoint">The endpoint of the server.</param>
    /// <param name="options">The behavioral options, null uses the defaults.</param>
    public Node(Endpoint endpoint, NodeOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        Endpoint = endpoint;
        Options = options ?? new NodeOptions();
        ResetSessionSettings();
        ArgumentException.ThrowIfNullOrWhiteSpace(Options.Name);
        ArgumentNullException.ThrowIfNull(Options.TimeProvider, nameof(NodeOptions.TimeProvider));

        if (Options.ReconnectDelay <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(
                nameof(options),
                "NodeOptions.ReconnectDelay must be positive."
            );
        if (Options.MaxReconnectDelay < Options.ReconnectDelay)
            throw new ArgumentOutOfRangeException(
                nameof(options),
                "NodeOptions.MaxReconnectDelay must not be smaller than ReconnectDelay."
            );
        ThrowIfOverLimit(Options.MaxReconnectDelay, nameof(NodeOptions.MaxReconnectDelay));
        if (
            Options.ResumeTimeout < TimeSpan.Zero
            || Options.ResumeTimeout.TotalSeconds > int.MaxValue
        )
            throw new ArgumentOutOfRangeException(
                nameof(options),
                "NodeOptions.ResumeTimeout must be zero or a positive number of seconds."
            );
        if (
            Options.RescueTimeout != Timeout.InfiniteTimeSpan
            && Options.RescueTimeout <= TimeSpan.Zero
        )
            throw new ArgumentOutOfRangeException(
                nameof(options),
                "NodeOptions.RescueTimeout must be positive, or Timeout.InfiniteTimeSpan for no limit."
            );
        if (Options.FailoverAfter != Timeout.InfiniteTimeSpan)
        {
            if (Options.FailoverAfter < TimeSpan.Zero)
                throw new ArgumentOutOfRangeException(
                    nameof(options),
                    "NodeOptions.FailoverAfter must be zero or positive, or Timeout.InfiniteTimeSpan to never move."
                );
            ThrowIfOverLimit(Options.FailoverAfter, nameof(NodeOptions.FailoverAfter));
        }
        ThrowIfInvalidLeaveTimeout(Options.IdleTimeout, nameof(NodeOptions.IdleTimeout));
        ThrowIfInvalidLeaveTimeout(Options.AloneTimeout, nameof(NodeOptions.AloneTimeout));
        ThrowIfNotPositive(
            Options.VoiceHandshakeTimeout,
            nameof(NodeOptions.VoiceHandshakeTimeout)
        );
        ThrowIfOverLimit(Options.VoiceHandshakeTimeout, nameof(NodeOptions.VoiceHandshakeTimeout));
        ThrowIfNotPositive(Options.ReadyTimeout, nameof(NodeOptions.ReadyTimeout));
        ThrowIfOverLimit(Options.ReadyTimeout, nameof(NodeOptions.ReadyTimeout));

        _events = new EventPump($"node {Options.Name}", null, LogEvents.Node);
        _eventError = OnEventErrorAsync;

        Rest = new RestClient(this);
        var dispatcher = new NodeEventDispatcher(this);
        Connection = new NodeConnection(this, dispatcher, cancellationToken =>
            EnsureServerInfoAsync(cancellationToken, forceProbe: true)
        );
        VoiceHandshake = new VoiceHandshakeCoordinator(
            Options.VoiceHandshakeTimeout,
            Options.TimeProvider
        );
        _queries = new NodeQueries(this);
        _serverInfo = new NodeServerInfo(this);
        _connector = new NodeConnector(this);
        Groups = new NodeGroups(this);
        Tracks = new NodeTracks(this);
        Lyrics = new NodeLyrics(this);
        YouTube = new NodeYouTube(this);
        RoutePlanner = new NodeRoutePlanner(this);
        Registry = new GuildRegistry(this);
        EventHandlers = new NodeEventHandlers(this);
        Wiring = new NodeBridgeWiring(this);
    }

    private static void ThrowIfNotPositive(
        TimeSpan value,
        string option,
        string paramName = "options"
    )
    {
        if (value <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(
                paramName,
                $"NodeOptions.{option} must be positive."
            );
    }

    private static void ThrowIfInvalidLeaveTimeout(
        TimeSpan value,
        string option,
        string paramName = "options"
    )
    {
        if (value == Timeout.InfiniteTimeSpan)
            return;

        if (value <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(
                paramName,
                $"NodeOptions.{option} must be positive, or Timeout.InfiniteTimeSpan to never leave."
            );
        ThrowIfOverLimit(value, option, paramName);
    }

    private static void ThrowIfOverLimit(
        TimeSpan value,
        string option,
        string paramName = "options"
    )
    {
        if (value > TimeoutLimit)
            throw new ArgumentOutOfRangeException(
                paramName,
                $"NodeOptions.{option} must not exceed {TimeoutLimit.TotalDays:N0} days."
            );
    }

    /// <summary>The endpoint the node connects to.</summary>
    public Endpoint Endpoint { get; }

    /// <summary>The node's display name, used in logs and error messages, must not be blank.</summary>
    public string Name => Options.Name;

    /// <summary>
    /// True when the server is NodeLink, false on Lavalink or before the first connect.
    /// </summary>
    public bool IsNodeLink => ServerInfo?.IsNodeLink ?? false;

    /// <summary>Server-reported version, like <c>3.9.0</c>.</summary>
    /// <remarks>
    /// Null before the first connect, after a disconnect until the next query, and on Lavalink dev builds. A
    /// NodeLink dev build reads as its base version.
    /// </remarks>
    public NodeVersion? Version => ServerInfo?.Version;

    /// <summary>
    /// Detected at connect, true when the server reports the given plugin as loaded, Lavalink and NodeLink
    /// alike.
    /// </summary>
    /// <param name="name">The plugin to check for, for example lavasrc-plugin, matched case-insensitively.</param>
    /// <returns>Whether the plugin is loaded, false before the first connect.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="name"/> is null.</exception>
    public bool HasPlugin(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        return ServerInfo?.HasPlugin(name) ?? false;
    }

    internal NodeOptions Options { get; }

    internal TimeProvider Time => Options.TimeProvider;

    /// <summary>The node session id of the current connection, null before the node first became ready.</summary>
    /// <remarks>
    /// Kept across disconnects so a resuming connection can replay it. Not a Discord voice session id.
    /// </remarks>
    public string? SessionId
    {
        get => Volatile.Read(ref field);
        internal set => Volatile.Write(ref field, value);
    }

    internal bool ResumingEnabled
    {
        get => Volatile.Read(ref field);
        private set => Volatile.Write(ref field, value);
    }

    internal int ResumeTimeoutSeconds
    {
        get => Volatile.Read(ref field);
        private set => Volatile.Write(ref field, value);
    }

    internal void ResetSessionSettings()
    {
        ResumingEnabled = Options.Resuming;
        ResumeTimeoutSeconds = (int)Options.ResumeTimeout.TotalSeconds;
    }

    private const long RestFailureMemoryMilliseconds = 30_000;

    private long _restFailedAt = -1;

    internal bool RestDegraded
    {
        get
        {
            var failedAt = Volatile.Read(ref _restFailedAt);
            return failedAt >= 0
                && Time.GetMilliseconds() - failedAt < RestFailureMemoryMilliseconds;
        }
    }

    internal void NoteRestFailure() => Volatile.Write(ref _restFailedAt, Time.GetMilliseconds());

    internal void NoteRestSuccess() => Volatile.Write(ref _restFailedAt, -1);

    /// <summary>
    /// Whether the websocket to the server is open, wait for <see cref="IsReady"/> before issuing requests.
    /// </summary>
    public bool IsConnected => Connection.IsConnected;

    /// <summary>Whether the node received the ready operation and can serve requests.</summary>
    public bool IsReady
    {
        get => Volatile.Read(ref field);
        internal set => Volatile.Write(ref field, value);
    }

    internal bool LastReadyResumed
    {
        get => Volatile.Read(ref field);
        set => Volatile.Write(ref field, value);
    }

    /// <summary>
    /// The last statistics reported by the server, or null before the first <c>stats</c> operation.
    /// </summary>
    public Stats? Stats
    {
        get => Volatile.Read(ref field);
        internal set => Volatile.Write(ref field, value);
    }

    /// <summary>Last reported outbound connection status, or null before the first event.</summary>
    /// <remarks>
    /// NodeLink only, and only pushed while the session has a player.
    /// </remarks>
    public ConnectionStatus? ConnectionStatus
    {
        get => Volatile.Read(ref field);
        internal set => Volatile.Write(ref field, value);
    }

    internal IDiscordBridge? Client { get; private set; }

    internal NyraExtension? Extension { get; set; }

    private long _currentUserId;

    internal ulong CurrentUserId => (ulong)Interlocked.Read(ref _currentUserId);

    internal RestClient Rest { get; }

    internal NodeConnection Connection { get; }

    /// <summary>The player groups of the node, NodeLink only.</summary>
    public NodeGroups Groups { get; }

    /// <summary>Decoding, encoding and streaming of the tracks of the node.</summary>
    public NodeTracks Tracks { get; }

    /// <summary>Lyrics, chapters and meanings of the tracks of the node.</summary>
    public NodeLyrics Lyrics { get; }

    /// <summary>The YouTube source configuration of the node.</summary>
    public NodeYouTube YouTube { get; }

    /// <summary>The IP route planner of the node.</summary>
    public NodeRoutePlanner RoutePlanner { get; }

    internal ServerInfo? ServerInfo => _serverInfo.Current;

    internal Task EnsureServerInfoAsync(
        CancellationToken cancellationToken,
        bool forceProbe = false
    ) => _serverInfo.EnsureAsync(cancellationToken, forceProbe);

    internal async Task RequireNodeLinkAsync(string feature, CancellationToken cancellationToken)
    {
        await EnsureServerInfoAsync(cancellationToken).ConfigureAwait(false);
        NodeGuards.RequireNodeLink(this, feature);
    }

    internal void InvalidateServerInfo() => _serverInfo.Invalidate();

    internal Task ConnectAsync(
        IDiscordBridge client,
        NyraExtension? expectedExtension,
        CancellationToken cancellationToken
    ) => _connector.ConnectAsync(client, expectedExtension, cancellationToken);

    internal TaskCompletionSource<bool>? TakeConnectCompletion(int? maxOwnerGeneration = null) =>
        _connector.TakeCompletion(maxOwnerGeneration);

    internal NodeBridgeWiring Wiring { get; }

    internal NodeEventHandlers EventHandlers { get; }

    internal GuildRegistry Registry { get; }

    internal int StopEpoch => Volatile.Read(ref _stopEpoch);

    internal void AttachClient(
        IDiscordBridge client,
        ClientInformation clientInformation,
        NyraExtension? expectedExtension,
        int stopEpoch
    )
    {
        lock (EventsLock)
        {
            ThrowIfInterruptedWhileConnecting(expectedExtension, stopEpoch);

            if (Wiring.WiredClient is not null && !ReferenceEquals(Wiring.WiredClient, client))
                throw new InvalidOperationException(
                    $"The node '{Name}' is already attached to another Discord client."
                );

            if (clientInformation.CurrentUserId is 0)
                throw new InvalidOperationException(
                    "The bridge returned a client identification without a user id, the server would reject it on every retry."
                );

            Client = client;
            Interlocked.Exchange(ref _currentUserId, (long)clientInformation.CurrentUserId);
            _events.Logger = client.Logger;

            Stopped = false;
            Wiring.Wire(client);
        }
    }

    internal Lock EventsLock { get; } = new();

    internal bool Stopped { get; private set; } = true;

    internal bool Disposed => _disposed;

    internal bool IsShutDown => Stopped || Disposed;

    internal VoiceHandshakeCoordinator VoiceHandshake { get; }

    internal ConcurrentDictionary<ulong, GuildConnection> Connections => Registry.Connections;

    internal ILogger? Logger => Client?.Logger;

    /// <summary>
    /// The websocket connection was established, the node is not ready yet, wait for <see cref="Ready"/>.
    /// </summary>
    public event AsyncEventHandler<NodeConnectedEventArgs>? Connected;

    /// <summary>Raised once per connection when the server sent its ready operation.</summary>
    public event AsyncEventHandler<NodeReadyEventArgs>? Ready;

    /// <summary>The websocket connection was lost, reconnection attempts follow.</summary>
    /// <remarks><see cref="NodeDisconnectedEventArgs.Final"/> marks the last one.</remarks>
    public event AsyncEventHandler<NodeDisconnectedEventArgs>? Disconnected;

    /// <summary>Raised for internal client errors, see <see cref="NodeErrorEventArgs"/> for the categories.</summary>
    /// <remarks>
    /// Reconnect failures are only logged, playback errors come through the guild connection, and a handler that
    /// throws is caught and logged. Each kind of error is raised at most five times per thirty seconds, the
    /// rest are counted in one log line.
    /// </remarks>
    public event AsyncEventHandler<NodeErrorEventArgs>? Error;

    /// <summary>
    /// Raised when the server reports its statistics, the latest report is also <see cref="Stats"/>.
    /// </summary>
    public event AsyncEventHandler<NodeStatsEventArgs>? StatsUpdated;

    /// <summary>Raised on every <c>ConnectionStatusEvent</c>.</summary>
    /// <remarks>
    /// NodeLink only. <see cref="ConnectionStatus"/> may already be newer than the args when the handler runs,
    /// and an update can still arrive right after a stop.
    /// </remarks>
    public event AsyncEventHandler<NodeConnectionStatusEventArgs>? ConnectionStatusUpdated;

    /// <summary>
    /// A NodeLink cluster worker crashed and the affected guilds' players died with it, raised after the
    /// connections were invalidated.
    /// </summary>
    /// <remarks>NodeLink only.</remarks>
    public event AsyncEventHandler<WorkerFailedEventArgs>? WorkerFailed;

    /// <summary>Stops the websocket connection, active guild connections are left untouched.</summary>
    /// <remarks>
    /// Until the node connects again, those connections stop following Discord changes like a kick or a move.
    /// </remarks>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A task that completes when the websocket was stopped.</returns>
    public async Task DisconnectAsync(CancellationToken cancellationToken = default)
    {
        var pendingConnect = StopAndUnwireDiscordEvents();
        try
        {
            await Connection.StopAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            lock (EventsLock)
                if (!Disposed && !IsConnected)
                    NyraMetrics.ReportConnection(this, connected: false);

            pendingConnect?.TrySetResult(false);
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        MarkDisposed();
        var pendingConnect = ClaimStoppedDiscordEvents();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        try
        {
            await Registry.DisconnectGuildsAsync(deadline.Token).ConfigureAwait(false);
            await Connection.StopAsync().ConfigureAwait(false);
        }
        finally
        {
            ReleaseAfterDispose(pendingConnect);
            await _events.CompleteAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
        }
    }

    /// <summary>Stops the node synchronously, the guild connections are torn down over the network.</summary>
    /// <remarks>
    /// Blocks until every guild is torn down, prefer <see cref="DisposeAsync"/> where you can await.
    /// </remarks>
    public void Dispose()
    {
        MarkDisposed();
        var pendingConnect = ClaimStoppedDiscordEvents();
        try
        {
            Registry.DisconnectGuilds();
        }
        finally
        {
            ReleaseAfterDispose(pendingConnect);
            try
            {
                _events.CompleteAsync(TimeSpan.FromSeconds(2)).Wait(TimeSpan.FromSeconds(3));
            }
            catch { }
        }
    }

    /// <summary>Returns the node as its name and endpoint.</summary>
    /// <returns>The name and endpoint of the node.</returns>
    public override string ToString() => $"{Name} ({Endpoint})";

    /// <summary>
    /// Connects the bot to the voice channel and creates a guild connection on this node.
    /// </summary>
    /// <param name="guildId">The guild to connect.</param>
    /// <param name="channelId">The voice channel to join.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The guild connection created by the join.</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="guildId"/> or <paramref name="channelId"/> is zero.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// The guild is already connected through a different node (one guild plays on one node at a time) or to a
    /// different channel on this node, disconnect it before connecting again. Also thrown when the node is not
    /// attached to a Discord client or not ready.
    /// </exception>
    /// <exception cref="VoiceHandshakeException">
    /// The voice handshake of the join failed, see its message for the cause.
    /// </exception>
    /// <exception cref="NodeUnreachableException">
    /// The node became unreachable between the handshake and the player registration.
    /// </exception>
    /// <exception cref="TimeoutException">
    /// The node's REST endpoint did not answer the player registration within its bound.
    /// </exception>
    /// <exception cref="RestException">
    /// The node rejected the player registration, see its message for the reply.
    /// </exception>
    /// <exception cref="OperationCanceledException">The join was cancelled through the token.</exception>
    public Task<GuildConnection> ConnectAsync(
        ulong guildId,
        ulong channelId,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentOutOfRangeException.ThrowIfZero(guildId);
        ArgumentOutOfRangeException.ThrowIfZero(channelId);
        return Registry.ConnectAsync(guildId, channelId, cancellationToken);
    }

    /// <summary>The guild connection playing on this node, or null when the guild is not connected here.</summary>
    /// <param name="guildId">The guild to get the connection of.</param>
    /// <returns>The guild's connection, null when it is not connected here.</returns>
    public GuildConnection? GetConnection(ulong guildId) => Registry.GetConnection(guildId);

    /// <summary>
    /// Loads tracks for a query, direct http(s) and <c>ftts</c> URLs are sent as-is, plain terms resolve per
    /// <paramref name="searchType"/> (Auto searches YouTube).
    /// </summary>
    /// <remarks>A URL's <c>t</c>/<c>start</c> parameter carries over to <see cref="Track.StartPosition"/>.</remarks>
    /// <param name="query">What to load, a raw identifier or a search query.</param>
    /// <param name="searchType">How to treat the query, Auto detects it.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The load result of the query.</returns>
    /// <exception cref="IncompatibleServerException">The server's /v4/info version is not supported.</exception>
    public Task<LoadResult> LoadTracksAsync(
        string query,
        SearchType searchType = SearchType.Auto,
        CancellationToken cancellationToken = default
    ) => _queries.LoadTracksAsync(query, searchType, cancellationToken);

    /// <summary>
    /// Lists every player of this node's session, including guilds this client is not connected to (useful
    /// after a resume).
    /// </summary>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>Every player of the node's session.</returns>
    /// <exception cref="IncompatibleServerException">The server's /v4/info version is not supported.</exception>
    public Task<IReadOnlyList<Player>> ListPlayersAsync(
        CancellationToken cancellationToken = default
    ) => _queries.ListPlayersAsync(cancellationToken);

    /// <summary>
    /// Fetches the node's descriptive info over REST, version, build time, git revision, runtime, enabled
    /// sources/filters/plugins.
    /// </summary>
    /// <remarks>
    /// Members only one server has are null on the other.
    /// </remarks>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The descriptive info of the node.</returns>
    /// <exception cref="RestException">
    /// The response could not be read.
    /// </exception>
    /// <exception cref="IncompatibleServerException">The server's /v4/info version is not supported.</exception>
    public Task<NodeInfo> GetNodeInfoAsync(CancellationToken cancellationToken = default) =>
        _queries.GetNodeInfoAsync(cancellationToken);

    /// <summary>Fetches statistics over REST.</summary>
    /// <remarks>
    /// CPU, memory, uptime, per-frame loss counts, also pushed over the websocket (<see cref="StatsUpdated"/>,
    /// <see cref="Stats"/>).
    /// </remarks>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The stats of the node.</returns>
    /// <exception cref="IncompatibleServerException">The server's /v4/info version is not supported.</exception>
    public Task<Stats> GetStatsAsync(CancellationToken cancellationToken = default) =>
        _queries.GetStatsAsync(cancellationToken);

    /// <summary>
    /// Fetches the node's outbound connection health over REST, also pushed over the websocket (<see
    /// cref="ConnectionStatusUpdated"/>, <see cref="ConnectionStatus"/>).
    /// </summary>
    /// <remarks>NodeLink only.</remarks>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The outbound connection health of the node.</returns>
    /// <exception cref="NotSupportedException">The node is not NodeLink.</exception>
    /// <exception cref="IncompatibleServerException">The server's /v4/info version is not supported.</exception>
    public Task<ConnectionStatus> GetConnectionStatusAsync(
        CancellationToken cancellationToken = default
    ) => _queries.GetConnectionStatusAsync(cancellationToken);

    /// <summary>
    /// Probes the server version if it was not detected yet, null on dev builds without a parseable semver.
    /// </summary>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The detected version, null on dev builds.</returns>
    /// <exception cref="IncompatibleServerException">The server's /v4/info version is not supported.</exception>
    public Task<NodeVersion?> GetVersionAsync(CancellationToken cancellationToken = default) =>
        _queries.GetVersionAsync(cancellationToken);

    /// <summary>
    /// Changes the session's resuming behavior at runtime, whether the server keeps the session's players
    /// through a disconnect, and how long it holds them.
    /// </summary>
    /// <remarks>
    /// They last as long as the session. A new session starts again from <see cref="NodeOptions.Resuming"/> and
    /// <see cref="NodeOptions.ResumeTimeout"/>.
    /// </remarks>
    /// <param name="resuming">Whether the session resumes its players after a reconnect.</param>
    /// <param name="timeout">
    /// How long a disconnected resumable session is kept alive, the server counts it in whole seconds.
    /// </param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The session state the server stored.</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="timeout"/> is negative or past <see cref="int.MaxValue"/> seconds.
    /// </exception>
    public Task<SessionConfig> UpdateSessionAsync(
        bool resuming,
        TimeSpan timeout,
        CancellationToken cancellationToken = default
    )
    {
        ThrowIfDisposed();
        if (timeout < TimeSpan.Zero || timeout.TotalSeconds > int.MaxValue)
            throw new ArgumentOutOfRangeException(
                nameof(timeout),
                timeout,
                "The resume timeout must be zero or a positive number of seconds."
            );

        return UpdateSessionCoreAsync(resuming, (int)timeout.TotalSeconds, cancellationToken);
    }

    private async Task<SessionConfig> UpdateSessionCoreAsync(
        bool resuming,
        int timeoutSeconds,
        CancellationToken cancellationToken
    )
    {
        var session = await Rest
            .Session.UpdateAsync(resuming, timeoutSeconds, cancellationToken)
            .ConfigureAwait(false);
        ResumingEnabled = resuming;
        ResumeTimeoutSeconds = timeoutSeconds;
        return session;
    }

    internal void HandleError(Exception exception) => EventHandlers.HandleError(exception);

    internal async Task GuardAsync(Task operation)
    {
        try
        {
            await operation.ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            HandleError(exception);
        }
    }

    internal Task OnEventErrorAsync(Exception exception, System.EventArgs? origin)
    {
        try
        {
            Logger?.LogError(
                LogEvents.Node,
                exception,
                "An event handler of node {NodeName} threw (origin {Origin}).",
                Name,
                origin?.GetType().Name ?? "none"
            );
        }
        catch { }

        return Task.CompletedTask;
    }

    private readonly Func<Exception, System.EventArgs?, Task> _eventError;

    internal void RaiseConnected(NodeConnectedEventArgs eventArgs) =>
        _events.TryDispatch(Connected, this, eventArgs, _eventError);

    internal void RaiseReady(NodeReadyEventArgs eventArgs) =>
        _events.TryDispatch(Ready, this, eventArgs, _eventError);

    internal void RaiseStatsUpdated(NodeStatsEventArgs eventArgs) =>
        _events.TryDispatch(StatsUpdated, this, eventArgs, _eventError, droppable: true);

    internal void RaiseWorkerFailed(WorkerFailedEventArgs eventArgs) =>
        _events.TryDispatch(WorkerFailed, this, eventArgs, _eventError);

    internal void RaiseConnectionStatusUpdated(NodeConnectionStatusEventArgs eventArgs) =>
        _events.TryDispatch(ConnectionStatusUpdated, this, eventArgs, _eventError, droppable: true);

    internal void RaiseDisconnected(NodeDisconnectedEventArgs eventArgs) =>
        _events.TryDispatch(Disconnected, this, eventArgs, _eventError);

    internal void RaiseError(NodeErrorEventArgs eventArgs) =>
        _events.TryDispatch(Error, this, eventArgs, _eventError);

    private TaskCompletionSource<bool>? StopAndUnwireDiscordEvents()
    {
        lock (EventsLock)
        {
            var pendingConnect = ClaimStoppedDiscordEvents();
            Wiring.Unwire();
            return pendingConnect;
        }
    }

    private TaskCompletionSource<bool>? ClaimStoppedDiscordEvents()
    {
        lock (EventsLock)
        {
            Stopped = true;
            _stopEpoch++;
            IsReady = false;
            _serverInfo.Invalidate();

            EventHandlers.OnStopped();

            return TakeConnectCompletion();
        }
    }

    private void ReleaseAfterDispose(TaskCompletionSource<bool>? pendingConnect)
    {
        lock (EventsLock)
        {
            Wiring.Unwire();
            NyraMetrics.Forget(this);
        }

        pendingConnect?.TrySetResult(false);
        Connection.Dispose();
        _connector.CancelGate();
    }

    private void MarkDisposed()
    {
        lock (EventsLock)
            _disposed = true;
    }

    internal void ThrowIfInterruptedWhileConnecting(NyraExtension? expectedExtension, int stopEpoch)
    {
        ThrowIfDisposed();

        if (stopEpoch != _stopEpoch)
            throw new InvalidOperationException($"The node '{Name}' was stopped while connecting.");

        if (
            (expectedExtension is not null && !ReferenceEquals(Extension, expectedExtension))
            || (Extension is { } owner && !owner.ContainsNode(this))
        )
            throw new InvalidOperationException(
                $"The node '{Name}' was removed from its extension while connecting."
            );
    }

    internal void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    internal static string LogSafe(string? text) =>
        text is null
            ? string.Empty
            : string.Concat(text.Where(static c => c <= 127 && !char.IsControl(c)));
}
