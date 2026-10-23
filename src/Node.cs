using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Nyra.Abstractions;
using Nyra.Entities;
using Nyra.EventArgs;
using Nyra.Exceptions;
using Nyra.Internal;

namespace Nyra;

/// <summary>
/// A connection to a Lavalink v4.2+ or NodeLink v3.9+ server. The kind is detected at connect (GET /v4/info)
/// and probed again by the first query after a disconnect drops the detection. A server below the floors
/// throws <see cref="IncompatibleServerException"/> from the query surface.
/// </summary>
/// <remarks>
/// Owns the websocket, the REST client, the voice handshake and the guild connections created through it.
/// </remarks>
public sealed class Node : IAsyncDisposable, IDisposable
{
    private readonly NodeOptions _options;
    private readonly NodeConnection _connection;
    private readonly VoiceHandshakeCoordinator _voiceHandshake;
    private readonly RestClient _rest;
    private readonly NodeQueries _queries;
    private readonly NodeServerInfo _serverInfo;
    private readonly NodeConnector _connector;
    private readonly NodeGroups _groups;
    private readonly NodeTracks _tracks;
    private readonly NodeLyrics _lyrics;
    private readonly NodeYouTube _youTube;
    private readonly NodeRoutePlanner _routePlanner;
    private readonly GuildRegistry _registry;
    private readonly EventPump _events;

    private readonly NodeEventHandlers _eventHandlers;
    private readonly NodeBridgeWiring _wiring;
    private bool _stopped = true;

    private static readonly TimeSpan TimeoutLimit = TimeSpan.FromDays(40);

    private int _stopEpoch;
    private volatile bool _disposed;
    private readonly Lock _eventsLock = new();

    /// <summary>Creates a node for an endpoint.</summary>
    /// <param name="endpoint">The endpoint of the server.</param>
    /// <param name="options">The behavioral options, null uses the defaults.</param>
    public Node(Endpoint endpoint, NodeOptions? options = null)
    {
        Endpoint = endpoint ?? throw new ArgumentNullException(nameof(endpoint));
        _options = options ?? new NodeOptions();
        _resumingEnabled = _options.Resuming;
        _resumeTimeoutSeconds = (int)_options.ResumeTimeout.TotalSeconds;
        ArgumentException.ThrowIfNullOrWhiteSpace(_options.Name);

        if (_options.ReconnectDelay <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(
                nameof(options),
                "NodeOptions.ReconnectDelay must be positive."
            );
        if (_options.MaxReconnectDelay < _options.ReconnectDelay)
            throw new ArgumentOutOfRangeException(
                nameof(options),
                "NodeOptions.MaxReconnectDelay must not be smaller than ReconnectDelay."
            );
        ThrowIfOverLimit(_options.MaxReconnectDelay, nameof(NodeOptions.MaxReconnectDelay));
        if (
            _options.ResumeTimeout < TimeSpan.Zero
            || _options.ResumeTimeout.TotalSeconds > int.MaxValue
        )
            throw new ArgumentOutOfRangeException(
                nameof(options),
                "NodeOptions.ResumeTimeout must be zero or a positive number of seconds."
            );
        ThrowIfNotPositive(
            _options.VoiceHandshakeTimeout,
            nameof(NodeOptions.VoiceHandshakeTimeout)
        );
        ThrowIfOverLimit(_options.VoiceHandshakeTimeout, nameof(NodeOptions.VoiceHandshakeTimeout));
        ThrowIfNotPositive(_options.ReadyTimeout, nameof(NodeOptions.ReadyTimeout));
        ThrowIfOverLimit(_options.ReadyTimeout, nameof(NodeOptions.ReadyTimeout));

        _events = new EventPump($"node {_options.Name}", null, LogEvents.Node);
        _eventError = OnEventErrorAsync;

        _rest = new RestClient(this);
        var dispatcher = new NodeEventDispatcher(this);
        _connection = new NodeConnection(
            this,
            dispatcher,
            cancellationToken => EnsureServerInfoAsync(cancellationToken, forceProbe: true)
        );
        _voiceHandshake = new VoiceHandshakeCoordinator(_options.VoiceHandshakeTimeout);
        _queries = new NodeQueries(this);
        _serverInfo = new NodeServerInfo(this);
        _connector = new NodeConnector(this);
        _groups = new NodeGroups(this);
        _tracks = new NodeTracks(this);
        _lyrics = new NodeLyrics(this);
        _youTube = new NodeYouTube(this);
        _routePlanner = new NodeRoutePlanner(this);
        _registry = new GuildRegistry(this);
        _eventHandlers = new NodeEventHandlers(this);
        _wiring = new NodeBridgeWiring(this);
    }

    private static void ThrowIfNotPositive(TimeSpan value, string option)
    {
        if (value <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(
                "options",
                $"NodeOptions.{option} must be positive."
            );
    }

    private static void ThrowIfOverLimit(TimeSpan value, string option)
    {
        if (value > TimeoutLimit)
            throw new ArgumentOutOfRangeException(
                "options",
                $"NodeOptions.{option} must not exceed {TimeoutLimit.TotalDays:N0} days."
            );
    }

    /// <summary>The endpoint the node connects to.</summary>
    public Endpoint Endpoint { get; }

    /// <summary>The node's display name, used in logs and error messages, must not be blank.</summary>
    public string Name => _options.Name;

    /// <summary>
    /// Detected at connect, true when the server is NodeLink, false when it is Lavalink or no detection is
    /// held.
    /// </summary>
    public bool IsNodeLink => ServerInfo?.IsNodeLink ?? false;

    /// <summary>Server-reported version, e.g. <c>3.9.0</c>.</summary>
    /// <remarks>
    /// Null while no detection is held (before the first connect, and after a disconnect until a query
    /// re-probes the server) and on Lavalink dev builds (commit hash, not semver). NodeLink dev pre-release
    /// tags parse as their base version.
    /// <para>
    /// Null means always current by policy (dev builds skip the version floors), so write
    /// <c>node.Version is { } v</c> and compare the bound variable, a lifted relational operator on the null
    /// answers false and would invert the meaning.
    /// </para>
    /// </remarks>
    public NodeVersion? Version => ServerInfo?.Version;

    /// <summary>
    /// Detected at connect, true when the server reports the given plugin as loaded, Lavalink and NodeLink
    /// alike.
    /// </summary>
    /// <param name="name">The plugin to check for, for example lavasrc-plugin, matched case-insensitively.</param>
    /// <returns>Whether the plugin is loaded, false until the first connect and while no detection is held.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="name"/> is null.</exception>
    public bool HasPlugin(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        return ServerInfo?.HasPlugin(name) ?? false;
    }

    internal NodeOptions Options => _options;

    /// <summary>The node session id of the current connection, null before the node first became ready.</summary>
    /// <remarks>
    /// Kept across disconnects so a resuming connection can replay it. Not a Discord voice session id.
    /// </remarks>
    public string? SessionId
    {
        get => Volatile.Read(ref _sessionId);
        internal set => Volatile.Write(ref _sessionId, value);
    }

    private string? _sessionId;

    internal bool ResumingEnabled
    {
        get => Volatile.Read(ref _resumingEnabled);
        private set => Volatile.Write(ref _resumingEnabled, value);
    }

    private bool _resumingEnabled;

    internal int ResumeTimeoutSeconds
    {
        get => Volatile.Read(ref _resumeTimeoutSeconds);
        private set => Volatile.Write(ref _resumeTimeoutSeconds, value);
    }

    private int _resumeTimeoutSeconds;

    /// <summary>
    /// Whether the websocket to the server is open, wait for <see cref="IsReady"/> before issuing requests.
    /// </summary>
    public bool IsConnected => _connection.IsConnected;

    /// <summary>Whether the node received the ready operation and can serve requests.</summary>
    public bool IsReady
    {
        get => Volatile.Read(ref _isReady);
        internal set => Volatile.Write(ref _isReady, value);
    }

    private bool _isReady;

    /// <summary>
    /// The last statistics reported by the server, or null before the first <c>stats</c> operation.
    /// </summary>
    public Stats? Stats
    {
        get => Volatile.Read(ref _stats);
        internal set => Volatile.Write(ref _stats, value);
    }

    private Stats? _stats;

    /// <summary>Last reported outbound connection status, or null before the first event.</summary>
    /// <remarks>
    /// NodeLink only, and only while the node's session holds at least one player, a playerless session
    /// receives no pushes, <see cref="GetConnectionStatusAsync"/> works without one.
    /// </remarks>
    public ConnectionStatus? ConnectionStatus
    {
        get => Volatile.Read(ref _connectionStatus);
        internal set => Volatile.Write(ref _connectionStatus, value);
    }

    private ConnectionStatus? _connectionStatus;

    internal IDiscordBridge? Client { get; private set; }

    internal NyraExtension? Extension { get; set; }

    private long _currentUserId;

    internal ulong CurrentUserId => (ulong)Interlocked.Read(ref _currentUserId);

    internal RestClient Rest => _rest;

    internal NodeConnection Connection => _connection;

    /// <summary>The player groups of the node, NodeLink only.</summary>
    public NodeGroups Groups => _groups;

    /// <summary>Decoding, encoding and streaming of the tracks of the node.</summary>
    public NodeTracks Tracks => _tracks;

    /// <summary>Lyrics, chapters and meanings of the tracks of the node.</summary>
    public NodeLyrics Lyrics => _lyrics;

    /// <summary>The YouTube source configuration of the node.</summary>
    public NodeYouTube YouTube => _youTube;

    /// <summary>The IP route planner of the node.</summary>
    public NodeRoutePlanner RoutePlanner => _routePlanner;

    internal ServerInfo? ServerInfo => _serverInfo.Current;

    internal Task EnsureServerInfoAsync(
        CancellationToken cancellationToken,
        bool forceProbe = false
    ) => _serverInfo.EnsureAsync(cancellationToken, forceProbe);

    internal void InvalidateServerInfo() => _serverInfo.Invalidate();

    internal Task ConnectAsync(
        IDiscordBridge client,
        NyraExtension? expectedExtension,
        CancellationToken cancellationToken
    ) => _connector.ConnectAsync(client, expectedExtension, cancellationToken);

    internal TaskCompletionSource<bool>? TakeConnectCompletion(int? maxOwnerGeneration = null) =>
        _connector.TakeCompletion(maxOwnerGeneration);

    internal NodeBridgeWiring Wiring => _wiring;

    internal NodeEventHandlers EventHandlers => _eventHandlers;

    internal GuildRegistry Registry => _registry;

    internal int StopEpoch => Volatile.Read(ref _stopEpoch);

    internal void AttachClient(
        IDiscordBridge client,
        ClientInformation clientInformation,
        NyraExtension? expectedExtension,
        int stopEpoch
    )
    {
        lock (_eventsLock)
        {
            ThrowIfInterruptedWhileConnecting(expectedExtension, stopEpoch);

            if (_wiring.WiredClient is not null && !ReferenceEquals(_wiring.WiredClient, client))
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

            _stopped = false;
            _wiring.Wire(client);
        }
    }

    internal Lock EventsLock => _eventsLock;

    internal bool Stopped => _stopped;

    internal bool Disposed => _disposed;

    internal VoiceHandshakeCoordinator VoiceHandshake => _voiceHandshake;

    internal ConcurrentDictionary<ulong, GuildConnection> Connections => _registry.Connections;

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
    /// Reconnect failures log separately, playback errors surface through the guild connection, a throwing
    /// handler is caught and logged. Errors of the same kind are reported five times per thirty seconds, the
    /// rest of the window is counted in one log line, so a malformed frame repeated for every player cannot
    /// flood the log or the event queue.
    /// </remarks>
    public event AsyncEventHandler<NodeErrorEventArgs>? Error;

    /// <summary>
    /// Raised when the server reports its statistics, the latest report is also <see cref="Stats"/>.
    /// </summary>
    /// <remarks>
    /// Frames landing while a ready's final phase runs collapse into the one raise after <see cref="Ready"/>.
    /// </remarks>
    public event AsyncEventHandler<NodeStatsEventArgs>? StatsUpdated;

    /// <summary>Raised on every <c>ConnectionStatusEvent</c>.</summary>
    /// <remarks>
    /// The args carry the snapshot <see cref="ConnectionStatus"/> held when the raise was queued, a newer
    /// push may have replaced the property by handler time. NodeLink only. The event's guildId is node-wide
    /// data delivered inside a guild-scoped payload, not exposed here.
    /// <para>
    /// An update queued before a stop or a disconnect can still run after it returns, a slow handler ahead of
    /// it in the queue holds it back. The dispose drains what is left with a bounded wait.
    /// </para>
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
    /// The Discord events are unwired by the stop, so the guild connections left behind lose the Discord-side
    /// tracking (a kick, a move, a voice server re-target) until the node is connected again, a removal that
    /// lands during the dormancy finds no subscriber and the connection stays registered until then.
    /// </remarks>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A task that completes when the websocket was stopped.</returns>
    public async Task DisconnectAsync(CancellationToken cancellationToken = default)
    {
        var pendingConnect = StopAndUnwireDiscordEvents();
        try
        {
            await _connection.StopAsync(cancellationToken).ConfigureAwait(false);
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
            await _registry.DisconnectGuildsAsync(deadline.Token).ConfigureAwait(false);
            await _connection.StopAsync().ConfigureAwait(false);
        }
        finally
        {
            ReleaseAfterDispose(pendingConnect);
            await _events.CompleteAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
        }
    }

    /// <summary>Stops the node synchronously, the guild connections are torn down over the network.</summary>
    /// <remarks>
    /// Blocks on every guild teardown roundtrip, each bounded by the HTTP timeout, prefer <see
    /// cref="DisposeAsync"/> wherever the caller can await.
    /// </remarks>
    public void Dispose()
    {
        MarkDisposed();
        var pendingConnect = ClaimStoppedDiscordEvents();
        try
        {
            _registry.DisconnectGuilds();
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
    /// Connects the bot to the voice channel and creates a guild connection on this node, forwarding the voice
    /// credentials.
    /// </summary>
    /// <remarks>Validating the channel kind is up to the adapters, the core only sees ids.</remarks>
    /// <param name="guildId">The guild to connect.</param>
    /// <param name="channelId">The voice channel to join.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The guild connection created by the join.</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="guildId"/> or <paramref name="channelId"/> is zero, a join that could only ever fail as
    /// a handshake timeout with no reason.
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
        return _registry.ConnectAsync(guildId, channelId, cancellationToken);
    }

    /// <summary>The guild connection playing on this node, or null when the guild is not connected here.</summary>
    /// <param name="guildId">The guild to get the connection of.</param>
    /// <returns>The guild's connection, null when it is not connected here.</returns>
    public GuildConnection? GetConnection(ulong guildId) => _registry.GetConnection(guildId);

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
    /// Served by both, server-specific members null on the other kind. This read is strict, the connect probe
    /// that admitted the node is not, a server whose info carries a member in an unexpected shape (a
    /// stringified boolean, a fractional build time) can be connected and playing while this call fails.
    /// </remarks>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The descriptive info of the node.</returns>
    /// <exception cref="RestException">
    /// The response body does not deserialize into <see cref="NodeInfo"/>, the status still reports OK.
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
    /// <remarks>
    /// Like <see cref="Version"/>, null means always current by policy, see its remarks for how to compare it.
    /// </remarks>
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
    /// Both values are sent together, the server applies them as a pair. They last as long as the session,
    /// a resumed session keeps them, a new session starts from <see cref="NodeOptions.Resuming"/> and <see
    /// cref="NodeOptions.ResumeTimeout"/> again.
    /// </remarks>
    /// <param name="resuming">Whether the session resumes its players after a reconnect.</param>
    /// <param name="timeout">
    /// How long a disconnected resumable session is kept alive, the server counts it in whole seconds.
    /// </param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The session state the server stored.</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="timeout"/> is negative or exceeds <see cref="int.MaxValue"/> seconds, the limits the
    /// constructor enforces on <c>NodeOptions.ResumeTimeout</c>.
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
        var session = await _rest
            .Session.UpdateAsync(resuming, timeoutSeconds, cancellationToken)
            .ConfigureAwait(false);
        ResumingEnabled = resuming;
        ResumeTimeoutSeconds = timeoutSeconds;
        return session;
    }

    internal void HandleError(Exception exception) => _eventHandlers.HandleError(exception);

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
        lock (_eventsLock)
        {
            var pendingConnect = ClaimStoppedDiscordEvents();
            _wiring.Unwire();
            return pendingConnect;
        }
    }

    private TaskCompletionSource<bool>? ClaimStoppedDiscordEvents()
    {
        lock (_eventsLock)
        {
            _stopped = true;
            _stopEpoch++;
            IsReady = false;
            _serverInfo.Invalidate();

            _eventHandlers.OnStopped();

            return TakeConnectCompletion();
        }
    }

    private void ReleaseAfterDispose(TaskCompletionSource<bool>? pendingConnect)
    {
        lock (_eventsLock)
        {
            _wiring.Unwire();
            NyraMetrics.Forget(this);
        }

        pendingConnect?.TrySetResult(false);
        _connection.Dispose();
        _connector.CancelGate();
    }

    private void MarkDisposed()
    {
        lock (_eventsLock)
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
        if (_disposed)
        {
            Logger?.LogError(
                LogEvents.Node,
                "Node {NodeName} was disposed, it cannot be used anymore.",
                Name
            );
            throw new ObjectDisposedException(nameof(Node));
        }
    }

    internal static string LogSafe(string? text) =>
        text is null
            ? string.Empty
            : string.Concat(text.Where(static c => c <= 127 && !char.IsControl(c)));
}
