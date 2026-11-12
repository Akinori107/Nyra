using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging;
using NetCord.Gateway;

namespace Nyra.NetCord;

/// <summary>Entry points for enabling and accessing Nyra on NetCord clients.</summary>
public static class GatewayClientExtensions
{
    private static readonly Lock UseNyraGate = new();
    private static readonly ConditionalWeakTable<GatewayClient, NyraExtension> Extensions = [];
    private static readonly ConditionalWeakTable<
        ShardedGatewayClient,
        NyraExtension
    > ShardedExtensions = [];

    /// <summary>Enables Nyra on the client.</summary>
    /// <remarks>
    /// Pass a logger to get Nyra's log messages. Enable it on a client that is not disposed, or every connect
    /// times out after sixty seconds.
    /// <para>
    /// Voice needs the guilds and guild voice states intents. Nyra cannot check them on NetCord, so set them in
    /// the client's configuration.
    /// </para>
    /// </remarks>
    /// <param name="client">The client to enable Nyra on.</param>
    /// <param name="logger">The logger to write the bridge's messages to.</param>
    /// <returns>
    /// The enabled extension, the existing one when Nyra is already on, which keeps its first logger.
    /// </returns>
    public static NyraExtension UseNyra(this GatewayClient client, ILogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(client);

        lock (UseNyraGate)
        {
            if (Find(Extensions, client) is { } existing)
                return existing;

            var extension = new NyraExtension(new NetCordBridge(client, logger));
            Extensions.AddOrUpdate(client, extension);
            return extension;
        }
    }

    /// <summary>Enables Nyra on the sharded client.</summary>
    /// <remarks>
    /// Pass a logger to get Nyra's log messages. Voice needs the guilds and guild voice states intents.
    /// </remarks>
    /// <param name="client">The sharded client to enable Nyra on, one extension covering every shard.</param>
    /// <param name="logger">The logger to write the bridge's messages to.</param>
    /// <returns>
    /// The enabled extension, the existing one when Nyra is already on, which keeps its first logger.
    /// </returns>
    public static NyraExtension UseNyra(this ShardedGatewayClient client, ILogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(client);

        lock (UseNyraGate)
        {
            if (Find(ShardedExtensions, client) is { } existing)
                return existing;

            foreach (var shard in client)
                if (Find(Extensions, shard) is not null)
                {
                    try
                    {
                        logger?.LogWarning(
                            LogEvents.Sharding,
                            "A shard of this sharded client carries its own Nyra extension, the sharded one and the shard's own one receive the same events and race the same joins. Dispose the shard's own one, the extension this call returns keeps working once it is gone."
                        );
                    }
                    catch { }
                    break;
                }

            var extension = new NyraExtension(new NetCordBridge(client, logger));
            ShardedExtensions.AddOrUpdate(client, extension);
            return extension;
        }
    }

    /// <summary>Gets the Nyra extension of the client, or null when Nyra is not enabled.</summary>
    /// <param name="client">The client to read the extension of.</param>
    /// <returns>The extension of the client, null when Nyra is not enabled.</returns>
    public static NyraExtension? GetNyra(this GatewayClient client)
    {
        ArgumentNullException.ThrowIfNull(client);
        return Find(Extensions, client);
    }

    /// <summary>Gets the Nyra extension of the sharded client, or null when Nyra is not enabled.</summary>
    /// <param name="client">The sharded client to read the extension of.</param>
    /// <returns>The extension of the sharded client, null when Nyra is not enabled.</returns>
    public static NyraExtension? GetNyra(this ShardedGatewayClient client)
    {
        ArgumentNullException.ThrowIfNull(client);
        return Find(ShardedExtensions, client);
    }

    private static NyraExtension? Find<TClient>(
        ConditionalWeakTable<TClient, NyraExtension> extensions,
        TClient client
    )
        where TClient : class =>
        extensions.TryGetValue(client, out var extension) && !extension.IsDisposed
            ? extension
            : null;
}
