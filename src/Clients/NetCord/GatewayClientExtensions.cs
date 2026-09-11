using System.Runtime.CompilerServices;
using global::NetCord.Gateway;
using Microsoft.Extensions.Logging;

namespace Nyra.NetCord;

/// <summary>Entry points for enabling Nyra on NetCord clients.</summary>
public static class GatewayClientExtensions
{
    private static readonly Lock UseNyraGate = new();
    private static readonly ConditionalWeakTable<GatewayClient, NyraExtension> Extensions = new();
    private static readonly ConditionalWeakTable<
        ShardedGatewayClient,
        NyraExtension
    > ShardedExtensions = new();

    /// <summary>
    /// Enables Nyra on the client. NetCord logs through its own logger, configured only at
    /// client creation, so pass a logger here to receive Nyra's log messages.
    /// </summary>
    public static NyraExtension UseNyra(this GatewayClient client, ILogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(client);

        lock (UseNyraGate)
        {
            if (Extensions.TryGetValue(client, out var existing))
                return existing;

            var extension = new NyraExtension(new NetCordBridge(client, logger));
            Extensions.Add(client, extension);
            return extension;
        }
    }

    /// <summary>
    /// Enables Nyra on the sharded client. NetCord logs through its own logger, configured only
    /// at client creation, so pass a logger here to receive Nyra's log messages.
    /// </summary>
    public static NyraExtension UseNyra(this ShardedGatewayClient client, ILogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(client);

        lock (UseNyraGate)
        {
            if (ShardedExtensions.TryGetValue(client, out var existing))
                return existing;

            var extension = new NyraExtension(new NetCordBridge(client, logger));
            ShardedExtensions.Add(client, extension);
            return extension;
        }
    }

    /// <summary>Gets the Nyra extension of the client, or null when Nyra is not enabled.</summary>
    public static NyraExtension? GetNyra(this GatewayClient client)
    {
        ArgumentNullException.ThrowIfNull(client);
        return Extensions.TryGetValue(client, out var extension) ? extension : null;
    }

    /// <summary>Gets the Nyra extension of the sharded client, or null when Nyra is not enabled.</summary>
    public static NyraExtension? GetNyra(this ShardedGatewayClient client)
    {
        ArgumentNullException.ThrowIfNull(client);
        return ShardedExtensions.TryGetValue(client, out var extension) ? extension : null;
    }
}
