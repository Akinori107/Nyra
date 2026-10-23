using System.Runtime.ExceptionServices;
using Microsoft.Extensions.Logging;
using Nyra.Abstractions;
using Nyra.EventArgs;
using Nyra.Internal;

namespace Nyra;

/// <summary>The extension that owns the audio nodes of a Discord client.</summary>
public sealed class NyraExtension : IAsyncDisposable, IDisposable
{
    private readonly Lock _lock = new();
    private readonly List<Node> _nodes = [];
    private readonly Dictionary<ulong, Node> _guildClaims = new();
    private readonly EventPump _events;

    private static readonly TimeSpan GatewayReadyTimeout = TimeSpan.FromSeconds(60);
    private volatile bool _disposed;

    /// <summary>Forwarded from every node when its websocket connects, with the node as sender.</summary>
    public event AsyncEventHandler<NodeConnectedEventArgs>? NodeConnected;

    /// <summary>Forwarded from every node when it turns ready, with the node as sender.</summary>
    public event AsyncEventHandler<NodeReadyEventArgs>? NodeReady;

    /// <summary>Forwarded from every node when its websocket drops, with the node as sender.</summary>
    public event AsyncEventHandler<NodeDisconnectedEventArgs>? NodeDisconnected;

    /// <summary>Forwarded from every node on internal errors, with the node as sender.</summary>
    public event AsyncEventHandler<NodeErrorEventArgs>? NodeError;

    /// <summary>Forwarded from every node on its statistics reports, with the node as sender.</summary>
    public event AsyncEventHandler<NodeStatsEventArgs>? NodeStatsUpdated;

    /// <summary>
    /// Forwarded from every node on connection status pushes (NodeLink only), with the node as sender.
    /// </summary>
    public event AsyncEventHandler<NodeConnectionStatusEventArgs>? NodeConnectionStatusUpdated;

    /// <summary>Forwarded from every node when a cluster worker of it failed, with the node as sender.</summary>
    public event AsyncEventHandler<WorkerFailedEventArgs>? NodeWorkerFailed;

    /// <summary>The Discord client bridge the nodes attach to.</summary>
    public IDiscordBridge Client { get; }

    /// <summary>Snapshot of the nodes added to this client.</summary>
    public IReadOnlyList<Node> Nodes
    {
        get
        {
            lock (_lock)
                return _nodes.ToArray();
        }
    }

    /// <summary>Creates an extension bound to a Discord client bridge.</summary>
    /// <param name="client">The bridge the nodes attach to.</param>
    public NyraExtension(IDiscordBridge client)
    {
        ArgumentNullException.ThrowIfNull(client);
        Client = client;
        _events = new EventPump("extension", client.Logger, LogEvents.Extension);
        _forwardError = OnForwardErrorAsync;
    }

    /// <summary>Whether the extension was disposed.</summary>
    public bool IsDisposed => _disposed;

    /// <summary>Adds a node, it connects when <see cref="ConnectAllNodesAsync"/> runs.</summary>
    /// <param name="node">The node to add.</param>
    public void AddNode(Node node)
    {
        ArgumentNullException.ThrowIfNull(node);

        lock (_lock)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            if (_nodes.Contains(node))
                return;

            if (node.Extension is { } owner && !ReferenceEquals(owner, this))
                throw new InvalidOperationException(
                    $"The node '{node.Name}' is already added to another extension."
                );

            if (
                _nodes.Any(existing =>
                    string.Equals(existing.Name, node.Name, StringComparison.OrdinalIgnoreCase)
                )
            )
                throw new InvalidOperationException(
                    $"A node named '{node.Name}' is already registered on this client."
                );

            if (
                _nodes.Any(existing =>
                    string.Equals(
                        existing.Endpoint.Host,
                        node.Endpoint.Host,
                        StringComparison.OrdinalIgnoreCase
                    )
                    && existing.Endpoint.Port == node.Endpoint.Port
                    && existing.Endpoint.IsSecure == node.Endpoint.IsSecure
                )
            )
                throw new InvalidOperationException(
                    $"A node for {node.Endpoint} is already registered on this client."
                );

            _nodes.Add(node);
            node.Extension = this;

            node.Connected += OnNodeConnected;
            node.Ready += OnNodeReady;
            node.Disconnected += OnNodeDisconnected;
            node.Error += OnNodeError;
            node.StatsUpdated += OnNodeStatsUpdated;
            node.ConnectionStatusUpdated += OnNodeConnectionStatusUpdated;
            node.WorkerFailed += OnNodeWorkerFailed;
        }
    }

    /// <summary>Removes a node and disconnects it, guild connections living on it are left untouched.</summary>
    /// <remarks>
    /// The disconnect unwires the node's Discord events, so the guild connections left on the removed node
    /// lose the Discord-side tracking (a kick, a move, a voice server re-target) until the node is connected
    /// again, a removal that lands during the dormancy finds no subscriber, and the connection stays
    /// registered until then (see <see cref="Node.DisconnectAsync"/> for the same rule on a plain stop).
    /// </remarks>
    /// <param name="node">The node to remove.</param>
    /// <param name="cancellationToken">A token to cancel the node's teardown.</param>
    /// <returns>A task that completes when the node was removed.</returns>
    public async Task RemoveNodeAsync(Node node, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(node);

        lock (_lock)
            if (!_nodes.Remove(node))
                return;

        try
        {
            await node.DisconnectAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            if (!node.Disposed)
                lock (node.EventsLock)
                    NyraMetrics.Forget(node);

            lock (_lock)
            {
                UnwireNode(node);

                if (!_nodes.Contains(node))
                    node.Extension = null;
            }
        }
    }

    private void UnwireNode(Node node)
    {
        node.Connected -= OnNodeConnected;
        node.Ready -= OnNodeReady;
        node.Disconnected -= OnNodeDisconnected;
        node.Error -= OnNodeError;
        node.StatsUpdated -= OnNodeStatsUpdated;
        node.ConnectionStatusUpdated -= OnNodeConnectionStatusUpdated;
        node.WorkerFailed -= OnNodeWorkerFailed;
    }

    internal bool ContainsNode(Node node)
    {
        lock (_lock)
            return _nodes.Contains(node);
    }

    /// <summary>
    /// Connects every added node, a node that fails to connect is skipped and logged if the client has a
    /// logger.
    /// </summary>
    /// <remarks>
    /// Skipped is not stopped, a node whose initial connect failed keeps retrying in the background and
    /// connects by itself once its server is reachable again, only <see cref="Node.DisconnectAsync"/> (or
    /// removing and disposing the node) stops it.
    /// </remarks>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A task that completes when every node settled.</returns>
    public async Task ConnectAllNodesAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        try
        {
            await Client
                .WaitForReadyAsync(cancellationToken)
                .WaitAsync(GatewayReadyTimeout, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (TimeoutException exception)
        {
            try
            {
                Client.Logger?.LogError(
                    LogEvents.Extension,
                    exception,
                    "The Discord gateway of the client did not become ready within {Timeout}, ConnectAllNodesAsync gave up waiting.",
                    GatewayReadyTimeout
                );
            }
            catch { }

            throw new TimeoutException(
                $"The Discord gateway did not become ready within {GatewayReadyTimeout}, ConnectAllNodesAsync gave up waiting.",
                exception
            );
        }
        catch (InvalidOperationException exception)
        {
            try
            {
                Client.Logger?.LogError(
                    LogEvents.Extension,
                    exception,
                    "The Discord gateway of the client closed fatally before becoming ready."
                );
            }
            catch { }

            throw;
        }
        catch (OperationCanceledException exception)
            when (_disposed && !cancellationToken.IsCancellationRequested)
        {
            try
            {
                Client.Logger?.LogError(
                    LogEvents.Extension,
                    exception,
                    "The extension was disposed while connecting, the bridge's dispose failed the pending ready wait."
                );
            }
            catch { }

            throw new ObjectDisposedException(
                nameof(NyraExtension),
                "The extension was disposed while connecting, the bridge's dispose failed the pending ready wait."
            );
        }

        _events.Logger = Client.Logger;

        ObjectDisposedException.ThrowIf(_disposed, this);

        await Task.WhenAll(Nodes.Select(node => ConnectNodeAsync(node, cancellationToken)))
            .ConfigureAwait(false);
    }

    private async Task ConnectNodeAsync(Node node, CancellationToken cancellationToken)
    {
        try
        {
            await node.ConnectAsync(Client, this, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (ObjectDisposedException) when (_disposed)
        {
            throw;
        }
        catch (Exception exception)
        {
            try
            {
                Client.Logger?.LogError(
                    LogEvents.Extension,
                    exception,
                    "The node {NodeName} failed to connect and was skipped.",
                    node.Name
                );
            }
            catch { }
        }
    }

    /// <summary>The ready node with the fewest playing players, nodes without stats yet rank last.</summary>
    /// <remarks>Null while no node is ready.</remarks>
    /// <returns>The best node, null while none is ready.</returns>
    public Node? GetNode()
    {
        Node? best = null;
        var bestScore = int.MaxValue;
        lock (_lock)
            foreach (var node in _nodes)
            {
                if (!node.IsReady)
                    continue;

                var score = node.Stats?.PlayingPlayers ?? int.MaxValue;
                if (best is null || score < bestScore)
                {
                    bestScore = score;
                    best = node;
                }
            }

        return best;
    }

    /// <summary>
    /// The connection playing in the guild, on whatever node it lives, or null when the guild is not
    /// connected.
    /// </summary>
    /// <param name="guildId">The guild to get the connection of.</param>
    /// <returns>The guild's connection, null when it is not connected.</returns>
    public GuildConnection? GetConnection(ulong guildId)
    {
        lock (_lock)
            foreach (var node in _nodes)
                if (node.GetConnection(guildId) is { } connection)
                    return connection;

        return null;
    }

    /// <summary>
    /// Disconnects and disposes every node of the extension and the bridge it routes through, asynchronously.
    /// </summary>
    /// <returns>A task that completes when every node was disposed.</returns>
    public async ValueTask DisposeAsync()
    {
        if (DetachNodes() is not { } nodes)
            return;

        var firstDisposeFailure = default(Exception);

        await Task.WhenAll(
                nodes.Select(async node =>
                {
                    try
                    {
                        await node.DisposeAsync().ConfigureAwait(false);
                    }
                    catch (Exception exception)
                    {
                        Interlocked.CompareExchange(ref firstDisposeFailure, exception, null);
                    }
                })
            )
            .ConfigureAwait(false);

        try
        {
            await DisposeBridgeAsync().ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            firstDisposeFailure ??= exception;
        }
        await _events.CompleteAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);

        if (firstDisposeFailure is not null)
            LogAndThrowDisposeFailure(firstDisposeFailure);
    }

    /// <summary>Disconnects and disposes every node of the extension and the bridge it routes through.</summary>
    /// <remarks>
    /// Blocks on the guild teardown roundtrips of every node at once (each bounded by the HTTP timeout),
    /// prefer <see cref="DisposeAsync"/> wherever the caller can await.
    /// </remarks>
    public void Dispose()
    {
        if (DetachNodes() is not { } nodes)
            return;

        var firstDisposeFailure = default(Exception);

        var nodesDisposed = Task.WhenAll(
            nodes.Select(node =>
                Task.Run(() =>
                {
                    try
                    {
                        node.Dispose();
                    }
                    catch (Exception exception)
                    {
                        Interlocked.CompareExchange(ref firstDisposeFailure, exception, null);
                    }
                })
            )
        );

        if (!nodesDisposed.Wait(TimeSpan.FromSeconds(30)))
            Interlocked.CompareExchange(
                ref firstDisposeFailure,
                new TimeoutException(
                    "A node did not finish its synchronous disposal within thirty seconds, the extension stopped waiting for it."
                ),
                null
            );

        try
        {
            DisposeBridge();
        }
        catch (Exception exception)
        {
            Interlocked.CompareExchange(ref firstDisposeFailure, exception, null);
        }

        var drained = _events.CompleteAsync(TimeSpan.FromSeconds(2));
        try
        {
            if (!drained.Wait(TimeSpan.FromSeconds(3)))
                drained.ObserveFaults();
        }
        catch { }

        if (firstDisposeFailure is not null)
            LogAndThrowDisposeFailure(firstDisposeFailure);
    }

    private Node[]? DetachNodes()
    {
        lock (_lock)
        {
            if (_disposed)
                return null;

            _disposed = true;
            var nodes = _nodes.ToArray();
            _nodes.Clear();
            foreach (var node in nodes)
            {
                UnwireNode(node);

                node.Extension = null;
            }

            return nodes;
        }
    }

    private void LogAndThrowDisposeFailure(Exception failure)
    {
        try
        {
            Client.Logger?.LogError(
                LogEvents.Extension,
                failure,
                "A node or the bridge of the extension failed to dispose."
            );
        }
        catch { }

        ExceptionDispatchInfo.Throw(failure);
    }

    private async ValueTask DisposeBridgeAsync()
    {
        switch (Client)
        {
            case IAsyncDisposable asyncDisposable:
                await asyncDisposable.DisposeAsync().ConfigureAwait(false);
                break;
            case IDisposable disposable:
                disposable.Dispose();
                break;
        }
    }

    private void DisposeBridge()
    {
        switch (Client)
        {
            case IDisposable disposable:
                disposable.Dispose();
                break;
            case IAsyncDisposable asyncDisposable:
            {
                var disposed = asyncDisposable.DisposeAsync().AsTask();
                var completed = false;
                try
                {
                    completed = disposed.Wait(TimeSpan.FromSeconds(3));
                }
                catch (AggregateException exception)
                {
                    ExceptionDispatchInfo.Capture(exception.InnerException!).Throw();
                }

                if (completed)
                    disposed.GetAwaiter().GetResult();
                else
                    disposed.ObserveFaults();
                break;
            }
        }
    }

    internal bool TryClaimGuild(ulong guildId, Node node, out Node? owner)
    {
        lock (_lock)
        {
            if (_guildClaims.TryGetValue(guildId, out owner))
                return ReferenceEquals(owner, node);

            foreach (var existing in _nodes)
                if (!ReferenceEquals(existing, node) && existing.GetConnection(guildId) is { })
                {
                    owner = existing;
                    return false;
                }

            _guildClaims[guildId] = node;
            owner = null;
            return true;
        }
    }

    internal void ReleaseGuildClaim(ulong guildId, Node node)
    {
        lock (_lock)
            if (_guildClaims.TryGetValue(guildId, out var owner) && ReferenceEquals(owner, node))
                _guildClaims.Remove(guildId);
    }

    private readonly Func<Exception, System.EventArgs?, Task> _forwardError;

    private Task OnNodeConnected(object? sender, NodeConnectedEventArgs eventArgs) =>
        Forward(NodeConnected, sender, eventArgs);

    private Task OnNodeReady(object? sender, NodeReadyEventArgs eventArgs) =>
        Forward(NodeReady, sender, eventArgs);

    private Task OnNodeDisconnected(object? sender, NodeDisconnectedEventArgs eventArgs) =>
        Forward(NodeDisconnected, sender, eventArgs);

    private Task OnNodeError(object? sender, NodeErrorEventArgs eventArgs) =>
        Forward(NodeError, sender, eventArgs);

    private Task OnNodeStatsUpdated(object? sender, NodeStatsEventArgs eventArgs) =>
        Forward(NodeStatsUpdated, sender, eventArgs, droppable: true);

    private Task OnNodeConnectionStatusUpdated(
        object? sender,
        NodeConnectionStatusEventArgs eventArgs
    ) => Forward(NodeConnectionStatusUpdated, sender, eventArgs, droppable: true);

    private Task OnNodeWorkerFailed(object? sender, WorkerFailedEventArgs eventArgs) =>
        Forward(NodeWorkerFailed, sender, eventArgs);

    private Task Forward<TArgs>(
        AsyncEventHandler<TArgs>? handler,
        object? sender,
        TArgs eventArgs,
        bool droppable = false
    )
        where TArgs : System.EventArgs
    {
        _events.TryDispatch(handler, sender ?? this, eventArgs, _forwardError, droppable);
        return Task.CompletedTask;
    }

    private Task OnForwardErrorAsync(Exception exception, System.EventArgs? origin)
    {
        try
        {
            Client.Logger?.LogError(
                LogEvents.Node,
                exception,
                "An event handler of the Nyra extension threw (origin {Origin}).",
                origin?.GetType().Name ?? "none"
            );
        }
        catch { }

        return Task.CompletedTask;
    }
}
