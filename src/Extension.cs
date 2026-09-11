using Nyra.Abstractions;
using Nyra.EventArgs;
using Nyra.Internal;

namespace Nyra;

/// <summary>The extension that owns the audio nodes of a Discord client.</summary>
public sealed class NyraExtension : IAsyncDisposable, IDisposable
{
    private readonly Lock _lock = new();
    private readonly List<Node> _nodes = [];

    /// <summary>Forwarded from every node when its websocket connects, with the node as sender.</summary>
    public event EventHandler<NodeConnectedEventArgs>? NodeConnected;

    /// <summary>Forwarded from every node when it turns ready, with the node as sender.</summary>
    public event EventHandler<NodeReadyEventArgs>? NodeReady;

    /// <summary>Forwarded from every node when its websocket drops, with the node as sender.</summary>
    public event EventHandler<NodeDisconnectedEventArgs>? NodeDisconnected;

    /// <summary>Forwarded from every node on internal errors, with the node as sender.</summary>
    public event EventHandler<NodeErrorEventArgs>? NodeError;

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
    public NyraExtension(IDiscordBridge client)
    {
        ArgumentNullException.ThrowIfNull(client);
        Client = client;
    }

    /// <summary>Adds a node, it connects when <see cref="ConnectAllNodesAsync"/> runs.</summary>
    public void AddNode(Node node)
    {
        ArgumentNullException.ThrowIfNull(node);

        lock (_lock)
        {
            if (_nodes.Contains(node))
                return;

            if (_nodes.Any(existing => existing.Name == node.Name))
            {
                throw new InvalidOperationException(
                    $"A node named '{node.Name}' is already registered on this client."
                );
            }

            if (
                _nodes.Any(existing =>
                    existing.Endpoint.Host == node.Endpoint.Host
                    && existing.Endpoint.Port == node.Endpoint.Port
                    && existing.Endpoint.IsSecure == node.Endpoint.IsSecure
                )
            )
            {
                throw new InvalidOperationException(
                    $"A node for {node.Endpoint} is already registered on this client."
                );
            }

            _nodes.Add(node);
        }

        node.Extension = this;

        node.Connected += OnNodeConnected;
        node.Ready += OnNodeReady;
        node.Disconnected += OnNodeDisconnected;
        node.Error += OnNodeError;
    }

    /// <summary>Removes a node and disconnects it, guild connections living on it are left untouched.</summary>
    public async Task RemoveNodeAsync(Node node)
    {
        ArgumentNullException.ThrowIfNull(node);

        lock (_lock)
            _nodes.Remove(node);

        node.Connected -= OnNodeConnected;
        node.Ready -= OnNodeReady;
        node.Disconnected -= OnNodeDisconnected;
        node.Error -= OnNodeError;

        await node.DisconnectAsync().ConfigureAwait(false);
    }

    /// <summary>Connects every added node, a node that fails to connect is logged and skipped.</summary>
    public async Task ConnectAllNodesAsync(CancellationToken cancellationToken = default)
    {
        // Connecting requires the current user (the User-Id websocket header). When called
        // before the gateway is ready, wait for it instead of permanently skipping every node.
        await Client.WaitForReadyAsync(cancellationToken).ConfigureAwait(false);

        foreach (var node in Nodes)
        {
            try
            {
                await node.ConnectAsync(Client, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception)
            {
                // Already logged, in order with the delay before its first retry, by the
                // node's own reconnect loop, nothing further to do than move on.
            }
        }
    }

    /// <summary>
    /// Gets the ready node with the fewest playing players, nodes that have not reported
    /// statistics yet rank last. Null while no node is ready.
    /// </summary>
    public Node? GetNode()
    {
        Node? best = null;
        var bestScore = int.MaxValue;
        lock (_lock)
        {
            foreach (var node in _nodes)
            {
                if (!node.IsReady)
                    continue;

                var score = node.Stats?.PlayingPlayers ?? int.MaxValue;
                if (score < bestScore)
                {
                    bestScore = score;
                    best = node;
                }
            }
        }

        return best;
    }

    /// <summary>The connection playing in the guild, whichever node it lives on, or null when the guild is not connected.</summary>
    public GuildConnection? GetConnection(ulong guildId)
    {
        foreach (var node in Nodes)
            if (node.GetConnection(guildId) is { } connection)
                return connection;

        return null;
    }

    /// <summary>Disconnects and disposes every node of the extension asynchronously.</summary>
    public async ValueTask DisposeAsync()
    {
        Node[] nodes;
        lock (_lock)
        {
            nodes = _nodes.ToArray();
            _nodes.Clear();
        }

        foreach (var node in nodes)
        {
            node.Connected -= OnNodeConnected;
            node.Ready -= OnNodeReady;
            node.Disconnected -= OnNodeDisconnected;
            node.Error -= OnNodeError;
            await node.DisposeAsync().ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        Node[] nodes;
        lock (_lock)
        {
            nodes = _nodes.ToArray();
            _nodes.Clear();
        }

        foreach (var node in nodes)
        {
            node.Connected -= OnNodeConnected;
            node.Ready -= OnNodeReady;
            node.Disconnected -= OnNodeDisconnected;
            node.Error -= OnNodeError;
            node.Dispose();
        }
    }

    private void OnNodeConnected(object? sender, NodeConnectedEventArgs eventArgs) =>
        NodeConnected.Raise(sender ?? this, eventArgs);

    private void OnNodeReady(object? sender, NodeReadyEventArgs eventArgs) =>
        NodeReady.Raise(sender ?? this, eventArgs);

    private void OnNodeDisconnected(object? sender, NodeDisconnectedEventArgs eventArgs) =>
        NodeDisconnected.Raise(sender ?? this, eventArgs);

    private void OnNodeError(object? sender, NodeErrorEventArgs eventArgs) =>
        NodeError.Raise(sender ?? this, eventArgs);
}
