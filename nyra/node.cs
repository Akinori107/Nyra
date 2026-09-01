using System.Collections.Concurrent;
using DSharpPlus;
using DSharpPlus.Entities;
using DSharpPlus.EventArgs;
using Microsoft.Extensions.Logging;
using Nyra.Entities;
using Nyra.EventArgs;
using Nyra.Exceptions;
using Nyra.Internal;

namespace Nyra;

/// <summary>
/// A connection to a Lavalink v4 or NodeLink v3+ server. The kind is detected at runtime on
/// connect (GET /v4/info) so nothing needs to be declared. Owns the websocket lifecycle, the
/// REST client and the voice handshake coordination, and exposes the guild connections
/// created through it.
/// </summary>
public sealed class Node : IAsyncDisposable, IDisposable
{
    private readonly NodeOptions _options;
    private readonly NodeConnection _connection;
    private readonly VoiceHandshakeCoordinator _voiceHandshake;
    private readonly RestClient _rest;
    private readonly SemaphoreSlim _connectGate = new(1, 1);
    private readonly Dictionary<ulong, (SemaphoreSlim Semaphore, int RefCount)> _guildGates = new();
    private readonly object _guildGatesLock = new();
    private ServerInfo? _serverInfo;
    private TaskCompletionSource<bool>? _connectCompletion;
    private bool _discordEventsWired;
    private bool _stopped = true;

    public Node(Endpoint endpoint, NodeOptions? options = null)
    {
        Endpoint = endpoint ?? throw new ArgumentNullException(nameof(endpoint));
        _options = options ?? new NodeOptions();
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
        if (
            _options.ResumeTimeout < TimeSpan.Zero
            || _options.ResumeTimeout.TotalSeconds > int.MaxValue
        )
            throw new ArgumentOutOfRangeException(
                nameof(options),
                "NodeOptions.ResumeTimeout must be zero or a positive number of seconds."
            );
        if (_options.VoiceHandshakeTimeout <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(
                nameof(options),
                "NodeOptions.VoiceHandshakeTimeout must be positive."
            );

        _rest = new RestClient(this);
        var dispatcher = new NodeEventDispatcher(this);
        // The server probe runs inside the connection's retry cycle, so a node that is down
        // at startup (or loses detection) is retried together with the websocket.
        _connection = new NodeConnection(this, _options, dispatcher, EnsureServerInfoAsync);
        _voiceHandshake = new VoiceHandshakeCoordinator(_options.VoiceHandshakeTimeout);
    }

    /// <summary>The network endpoint of the server.</summary>
    public Endpoint Endpoint { get; }

    /// <summary>The node's display name, used in logs and error messages.</summary>
    public string Name => _options.Name;

    /// <summary>Detected at connect: true when the server is NodeLink, false when it is Lavalink.</summary>
    public bool IsNodeLink => _serverInfo?.IsNodeLink ?? false;

    /// <summary>
    /// Version reported by the server, for example <c>3.9.0</c>. Null before the first contact and
    /// on dev builds, which report a commit hash instead of a semver.
    /// </summary>
    public NodeVersion? Version => _serverInfo?.Version;

    /// <summary>Detected at connect: true when Lavalink reports the given plugin as loaded. Always false for NodeLink.</summary>
    public bool HasPlugin(string name) => _serverInfo?.HasPlugin(name) ?? false;

    internal NodeOptions Options => _options;

    /// <summary>The node session id of the current connection, or null before the node became ready. Not a Discord voice session id.</summary>
    public string? SessionId { get; internal set; }

    /// <summary>Whether the websocket to the server is open; wait for <see cref="IsReady"/> before issuing requests.</summary>
    public bool IsConnected => _connection.IsConnected;

    /// <summary>Whether the node received the <c>ready</c> operation and can serve requests.</summary>
    public bool IsReady { get; internal set; }

    /// <summary>The last statistics reported by the server, or null before the first <c>stats</c> operation.</summary>
    public Stats? Stats { get; internal set; }

    internal DiscordClient? Discord { get; private set; }

    internal RestClient Rest => _rest;

    internal ILogger? Logger => Discord?.Logger;

    internal ConcurrentDictionary<ulong, GuildConnection> Connections { get; } = new();

    /// <summary>The websocket connection was established; the node is not ready yet, wait for <see cref="Ready"/>.</summary>
    public event EventHandler<NodeConnectedEventArgs>? Connected;

    /// <summary>Raised once per connection when the server sent its <c>ready</c> operation.</summary>
    public event EventHandler<NodeReadyEventArgs>? Ready;

    /// <summary>The websocket connection was lost; reconnection attempts follow. <see cref="NodeDisconnectedEventArgs.Final"/> marks the last one.</summary>
    public event EventHandler<NodeDisconnectedEventArgs>? Disconnected;

    /// <summary>Raised for internal errors: malformed payloads, failed reconnects, handlers that threw.</summary>
    public event EventHandler<NodeErrorEventArgs>? Error;

    /// <summary>
    /// Attaches the node to a Discord client, opens the websocket and waits for the server's
    /// <c>ready</c> operation. Called by <see cref="NyraExtension.ConnectAllNodesAsync"/>; calling
    /// it again on a ready node is a no-op, and a concurrent or later call waits for the connect
    /// already in flight. When the initial connect fails, the exception is rethrown and the node
    /// keeps retrying in the background with the configured backoff.
    /// </summary>
    internal async Task ConnectAsync(DiscordClient client, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(client);

        if (client.CurrentUser is null)
            throw new InvalidOperationException(
                "The Discord client is not ready yet (no current user)."
            );

        if (Discord is not null && !ReferenceEquals(Discord, client))
            throw new InvalidOperationException(
                $"The node '{Name}' is already attached to another Discord client."
            );

        await _connectGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (IsReady)
                return;

            Discord = client;
            _stopped = false;

            if (!_discordEventsWired)
            {
                client.VoiceStateUpdated += OnDiscordVoiceStateUpdated;
                client.VoiceServerUpdated += OnDiscordVoiceServerUpdated;
                _discordEventsWired = true;
            }

            // Completed with true by the ready operation, false when the connection drops first.
            // A completion left pending by a cancelled caller is adopted instead of replaced,
            // so no waiter is ever orphaned.
            var completion =
                _connectCompletion is { } pending && !pending.Task.IsCompleted
                    ? pending
                    : new TaskCompletionSource<bool>(
                        TaskCreationOptions.RunContinuationsAsynchronously
                    );
            _connectCompletion = completion;

            try
            {
                await _connection.ConnectAsync(cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                if (ReferenceEquals(_connectCompletion, completion))
                    _connectCompletion = null;
                throw;
            }

            var becameReady = await completion
                .Task.WaitAsync(cancellationToken)
                .ConfigureAwait(false);
            if (ReferenceEquals(_connectCompletion, completion))
                _connectCompletion = null;

            if (!becameReady)
                throw new InvalidOperationException(
                    $"The node '{Name}' disconnected before becoming ready."
                );
        }
        finally
        {
            _connectGate.Release();
        }
    }

    /// <summary>
    /// Probes /v4/info once and remembers the result. Concurrent calls may probe twice; the
    /// request is idempotent.
    /// </summary>
    internal async Task EnsureServerInfoAsync(CancellationToken cancellationToken)
    {
        if (_serverInfo is not null)
            return;

        ServerInfo info;
        try
        {
            info = await _rest.GetServerInfoAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (RestException exception) when (exception.StatusCode == 404)
        {
            throw new InvalidOperationException(
                $"The server at {Endpoint} does not expose /v4/info; only Lavalink v4 and NodeLink v3+ are supported.",
                exception
            );
        }

        if (string.IsNullOrEmpty(info.RawVersion))
            throw new InvalidOperationException(
                $"The server at {Endpoint} did not report a version."
            );

        // Dev builds report a commit hash; they are always current and skip the floor check.
        if (info.Version is { } version)
        {
            var minimum = info.IsNodeLink
                ? NodeVersion.MinimumNodeLink
                : NodeVersion.MinimumLavalink;
            if (version < minimum)
            {
                var software = info.IsNodeLink ? "NodeLink" : "Lavalink";
                throw new InvalidOperationException(
                    $"{software} {version} at {Endpoint} is too old; {software} {minimum} or newer is required."
                );
            }
        }

        _serverInfo = info;
        Logger?.LogInformation(
            Events.Node,
            "Node {NodeName} runs {Software} {Version}.",
            Name,
            info.IsNodeLink ? "NodeLink" : "Lavalink",
            info.Version?.ToString() ?? info.RawVersion
        );
    }

    /// <summary>
    /// Loads tracks for a query. Direct http(s) URLs are always sent as-is — prefixing a URL
    /// with a search prefix is never valid — while plain terms resolve according to
    /// <paramref name="searchType"/> (<see cref="SearchType.Auto"/> searches YouTube). A URL's
    /// <c>t</c>/<c>start</c> parameter carries over to <see cref="Track.StartPosition"/>.
    /// </summary>
    public async Task<LoadResult> LoadTracksAsync(
        string query,
        SearchType searchType = SearchType.Auto,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(query);

        await EnsureServerInfoAsync(cancellationToken).ConfigureAwait(false);

        if (
            query.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            || query.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
        )
        {
            var result = await _rest
                .LoadTracksAsync(query, cancellationToken)
                .ConfigureAwait(false);
            AttachUrlStartPosition(result, query);
            return result;
        }

        var identifier = searchType switch
        {
            SearchType.Auto or SearchType.YouTube => $"ytsearch:{query}",
            SearchType.YouTubeMusic => $"ytmsearch:{query}",
            SearchType.SoundCloud => $"scsearch:{query}",
            SearchType.Raw => query,
            _ => throw new ArgumentOutOfRangeException(
                nameof(searchType),
                searchType,
                "Unknown search type."
            ),
        };

        return await _rest.LoadTracksAsync(identifier, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Loads the lyrics of a track: <paramref name="language"/> applies to NodeLink,
    /// <paramref name="skipTrackSource"/> to Lavalink with the LavaLyrics plugin. For live
    /// per-line lyrics use <see cref="GuildConnection.SubscribeLyricsAsync"/> instead.
    /// </summary>
    /// <exception cref="NotSupportedException">The node is not NodeLink and does not have the LavaLyrics plugin.</exception>
    public async Task<LyricsResult> LoadLyricsAsync(
        Track track,
        string? language = null,
        bool skipTrackSource = false,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(track);
        await EnsureServerInfoAsync(cancellationToken).ConfigureAwait(false);
        NodeGuards.RequireNodeLinkOrPlugin(this, NodeGuards.LavaLyricsPlugin, "lyrics");

        return await _rest
            .LoadLyricsAsync(track.Encoded, language, skipTrackSource, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>Loads the chapters of a track, in milliseconds; mostly YouTube videos carry them.</summary>
    /// <exception cref="NotSupportedException">The node is not NodeLink.</exception>
    public async Task<IReadOnlyList<Chapter>> LoadChaptersAsync(
        Track track,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(track);
        await EnsureServerInfoAsync(cancellationToken).ConfigureAwait(false);
        NodeGuards.RequireNodeLink(this, "chapters");

        return await _rest
            .LoadChaptersAsync(track.Encoded, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Attaches the source link's offset to the loaded track(s): all of a search result,
    /// but only the selected entry of a playlist.
    /// </summary>
    private static void AttachUrlStartPosition(LoadResult result, string query)
    {
        if (UrlStartPosition.ParseMilliseconds(query) is not { } milliseconds)
            return;

        var offset = TimeSpan.FromMilliseconds(milliseconds);

        switch (result)
        {
            case LoadResult.TrackLoaded loaded:
                loaded.Track.StartPosition = offset;
                break;

            case LoadResult.SearchLoaded search:
                foreach (var track in search.Tracks)
                    track.StartPosition = offset;
                break;

            case LoadResult.PlaylistLoaded playlist:
                var index = playlist.PlaylistInfo.SelectedTrack;
                if (index >= 0 && index < playlist.Tracks.Count)
                    playlist.Tracks[index].StartPosition = offset;
                break;
        }
    }

    /// <summary>Probes the server version if it was not detected yet; null on dev builds without a parseable semver.</summary>
    public async Task<NodeVersion?> GetVersionAsync(CancellationToken cancellationToken = default)
    {
        await EnsureServerInfoAsync(cancellationToken).ConfigureAwait(false);
        return Version;
    }

    /// <summary>
    /// Connects the bot to the specified voice channel and creates a guild connection on this
    /// node, forwarding the voice credentials to the server.
    /// </summary>
    public async Task<GuildConnection> ConnectAsync(
        DiscordChannel channel,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(channel);

        if (channel.Type is not (ChannelType.Voice or ChannelType.Stage))
            throw new ArgumentException(
                "The channel must be a voice or stage channel.",
                nameof(channel)
            );

        if (channel.GuildId is null)
            throw new ArgumentException("The channel does not belong to a guild.", nameof(channel));

        if (Discord is null)
            throw new InvalidOperationException(
                "The node is not attached to a Discord client yet; start it through the extension first."
            );

        if (!IsReady)
            throw new InvalidOperationException($"The node '{Name}' is not ready yet.");

        var guildId = channel.GuildId.Value;

        // Serialize the connects of one guild; racing connects would both register and leak a player.
        var gate = AcquireGate(guildId);
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (Connections.TryGetValue(guildId, out var existing))
            {
                if (existing.Channel?.Id == channel.Id)
                    return existing;

                throw new InvalidOperationException(
                    $"The bot is already connected to {existing.Channel} in this guild. Disconnect it before connecting to {channel}."
                );
            }

            // One guild, one node: two nodes holding the same voice credentials fight over the
            // Discord voice connection.
            if (Discord.GetNyra() is { } extension)
                foreach (var other in extension.Nodes)
                {
                    if (!ReferenceEquals(other, this) && other.GetConnection(guildId) is { })
                        throw new InvalidOperationException(
                            $"The bot is already connected in this guild through node '{other.Name}'. Disconnect it before connecting through '{Name}'."
                        );
                }

            var (stateUpdate, serverUpdate) = await _voiceHandshake
                .WaitForUpdatesAsync(guildId, channel.Id, Discord, cancellationToken)
                .ConfigureAwait(false);

            var connection = new GuildConnection(
                this,
                guildId,
                channel,
                stateUpdate.SessionId,
                serverUpdate.Endpoint,
                serverUpdate.VoiceToken
            );

            try
            {
                await _rest
                    .UpdatePlayerAsync(
                        guildId,
                        new PlayerUpdatePayload
                        {
                            Voice = new VoiceState
                            {
                                Token = serverUpdate.VoiceToken,
                                Endpoint = serverUpdate.Endpoint,
                                SessionId = stateUpdate.SessionId,
                                ChannelId = channel.Id,
                            },
                        },
                        noReplace: false,
                        cancellationToken
                    )
                    .ConfigureAwait(false);
            }
            catch
            {
                // Do not leave the bot parked in the voice channel when the server rejects the
                // player update.
                await _voiceHandshake.LeaveChannelAsync(Discord, guildId).ConfigureAwait(false);
                throw;
            }

            Connections[guildId] = connection;
            return connection;
        }
        finally
        {
            gate.Release();
            ReleaseGate(guildId);
        }
    }

    /// <summary>The guild connection playing on this node, or null when the guild is not connected here.</summary>
    public GuildConnection? GetConnection(ulong guildId) =>
        Connections.TryGetValue(guildId, out var connection) ? connection : null;

    // Refcounted so the entry can be dropped once nobody is waiting on it, instead of pinning one
    // SemaphoreSlim per guild ever seen for the life of the node.
    private SemaphoreSlim AcquireGate(ulong guildId)
    {
        lock (_guildGatesLock)
        {
            if (_guildGates.TryGetValue(guildId, out var entry))
            {
                _guildGates[guildId] = (entry.Semaphore, entry.RefCount + 1);
                return entry.Semaphore;
            }

            var semaphore = new SemaphoreSlim(1, 1);
            _guildGates[guildId] = (semaphore, 1);
            return semaphore;
        }
    }

    private void ReleaseGate(ulong guildId)
    {
        lock (_guildGatesLock)
        {
            if (!_guildGates.TryGetValue(guildId, out var entry))
                return;

            if (entry.RefCount <= 1)
                _guildGates.Remove(guildId);
            else
                _guildGates[guildId] = (entry.Semaphore, entry.RefCount - 1);
        }
    }

    /// <summary>Destroys the player under the guild's connect gate, so a cleanup cannot race a reconnect.</summary>
    internal async Task DestroyPlayerExclusivelyAsync(
        GuildConnection connection,
        CancellationToken cancellationToken
    )
    {
        var gate = AcquireGate(connection.GuildId);
        await gate.WaitAsync(CancellationToken.None).ConfigureAwait(false);
        try
        {
            // Another connection already claimed the guild; its player must survive.
            if (
                Connections.TryGetValue(connection.GuildId, out var current)
                && !ReferenceEquals(current, connection)
            )
                return;

            Connections.TryRemove(connection.GuildId, out _);

            try
            {
                await _rest
                    .DestroyPlayerAsync(connection.GuildId, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (RestException exception) when (exception.StatusCode == 404)
            {
                // The player was already gone on the server; nothing to clean up.
            }
            catch (Exception exception)
            {
                // Best-effort cleanup; surface it through the error event.
                HandleError(exception);
            }
        }
        finally
        {
            gate.Release();
            ReleaseGate(connection.GuildId);
        }
    }

    /// <summary>Stops the websocket connection; active guild connections are left untouched.</summary>
    public async Task DisconnectAsync(CancellationToken cancellationToken = default)
    {
        _stopped = true;
        IsReady = false;
        await _connection.StopAsync(cancellationToken).ConfigureAwait(false);
        // Release a connect still waiting on ready; the stop path cannot fail it anymore.
        Interlocked.Exchange(ref _connectCompletion, null)?.TrySetResult(false);
        UnwireDiscordEvents();
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await DisconnectGuildsAsync().ConfigureAwait(false);
        await DisconnectAsync().ConfigureAwait(false);
        _connection.Dispose();
        GC.SuppressFinalize(this);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _stopped = true;
        IsReady = false;
        DisconnectGuilds();
        _connection.Dispose();
        Interlocked.Exchange(ref _connectCompletion, null)?.TrySetResult(false);
        UnwireDiscordEvents();
        GC.SuppressFinalize(this);
    }

    private async Task DisconnectGuildsAsync()
    {
        foreach (var connection in Connections.Values.ToArray())
        {
            try
            {
                await connection.DisconnectAsync().ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                HandleError(exception);
            }
        }
    }

    private void DisconnectGuilds() => DisconnectGuildsAsync().GetAwaiter().GetResult();

    /// <inheritdoc />
    public override string ToString() => $"{Name} ({Endpoint})";

    internal void HandleConnected() =>
        Connected.Raise(this, new NodeConnectedEventArgs(this), OnEventError);

    internal async Task HandleReadyAsync(NodeReadyEventArgs eventArgs)
    {
        if (_stopped)
            return;

        SessionId = eventArgs.SessionId;
        IsReady = true;

        // A session that did not resume means the previous one — and every player on it — is
        // gone; keep the local connections honest instead of claiming playback that no longer
        // exists.
        if (!eventArgs.Resumed && !Connections.IsEmpty)
        {
            var invalidated = Connections.Values.ToArray();
            foreach (var connection in invalidated)
                connection.HandleSessionInvalidated();

            Logger?.LogWarning(
                Events.Node,
                "The session of node {NodeName} was not resumed; {Count} guild connection(s) were invalidated. Reconnect the bot to the voice channels to resume playback.",
                Name,
                invalidated.Length
            );
        }

        Ready.Raise(this, eventArgs, OnEventError);

        Logger?.LogInformation(
            Events.Node,
            "Node {NodeName} is ready (session {SessionId}, resumed: {Resumed}).",
            Name,
            SessionId,
            eventArgs.Resumed
        );

        // Completed after the Ready event, so a caller resuming from ConnectAsync observes
        // the event as already raised.
        Interlocked.Exchange(ref _connectCompletion, null)?.TrySetResult(true);

        if (!_options.Resuming)
            return;

        try
        {
            await _rest
                .UpdateSessionAsync(
                    true,
                    (int)_options.ResumeTimeout.TotalSeconds,
                    CancellationToken.None
                )
                .ConfigureAwait(false);
            Logger?.LogInformation(
                Events.Node,
                "Session resuming enabled for node {NodeName} (timeout {Timeout}s).",
                Name,
                _options.ResumeTimeout.TotalSeconds
            );
        }
        catch (Exception exception)
        {
            HandleError(
                new InvalidOperationException(
                    $"Failed to enable session resuming on node '{Name}': {exception.Message}",
                    exception
                )
            );
        }
    }

    internal void HandleStats(Stats stats) => Stats = stats;

    internal void HandleDisconnected(
        System.Net.WebSockets.WebSocketCloseStatus? closeStatus,
        string? closeDescription,
        bool final
    )
    {
        IsReady = false;

        // A connection that drops before the first ready operation fails the pending connect.
        Interlocked.Exchange(ref _connectCompletion, null)?.TrySetResult(false);

        Disconnected.Raise(
            this,
            new NodeDisconnectedEventArgs(closeStatus, closeDescription, final),
            OnEventError
        );
    }

    internal void HandleError(Exception exception)
    {
        Logger?.LogError(exception, "An error occurred on node {NodeName}.", Name);
        Error.Raise(this, new NodeErrorEventArgs(exception, this), OnEventError);
    }

    internal Task SendVoiceLeaveAsync(ulong guildId) =>
        _voiceHandshake.LeaveChannelAsync(Discord!, guildId);

    internal void OnEventError(Exception exception) =>
        Logger?.LogError(exception, "An event handler of node {NodeName} threw.", Name);

    private Task OnDiscordVoiceStateUpdated(
        DiscordClient sender,
        VoiceStateUpdateEventArgs eventArgs
    )
    {
        // Track our own voice state: Discord moving or removing the bot changes or kills the
        // guild connection without any REST call of ours.
        if (
            eventArgs.User?.Id == Discord!.CurrentUser.Id
            && GetConnection(eventArgs.Guild.Id) is { } connection
        )
        {
            var channel = eventArgs.After?.Channel;
            if (channel is null)
                _ = connection.HandleVoiceDisconnectedAsync();
            else if (channel.Id != connection.Channel?.Id)
                connection.HandleVoiceMoved(channel, eventArgs.SessionId);
        }

        _voiceHandshake.HandleVoiceStateUpdate(Discord!, eventArgs);
        return Task.CompletedTask;
    }

    private Task OnDiscordVoiceServerUpdated(
        DiscordClient sender,
        VoiceServerUpdateEventArgs eventArgs
    )
    {
        if (_voiceHandshake.HandleVoiceServerUpdate(eventArgs))
            return Task.CompletedTask;

        // No pending handshake and a complete update: Discord re-targeted the voice server of a
        // live connection (channel move or voice server migration); forward the credentials.
        if (
            !string.IsNullOrWhiteSpace(eventArgs.Endpoint)
            && GetConnection(eventArgs.Guild.Id) is { } connection
        )
        {
            _ = connection.HandleVoiceServerUpdatedAsync(eventArgs.VoiceToken, eventArgs.Endpoint);
        }

        return Task.CompletedTask;
    }

    private void UnwireDiscordEvents()
    {
        if (!_discordEventsWired)
            return;

        var discord = Discord!;
        discord.VoiceStateUpdated -= OnDiscordVoiceStateUpdated;
        discord.VoiceServerUpdated -= OnDiscordVoiceServerUpdated;
        _discordEventsWired = false;
    }
}
