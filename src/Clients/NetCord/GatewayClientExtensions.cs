using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging;
using NetCord.Gateway;

namespace Nyra.NetCord;

/// <summary>Entry points for enabling and accessing Nyra on NetCord clients.</summary>
public static class GatewayClientExtensions
{
    private static readonly Lock UseNyraGate = new();
    private static readonly ConditionalWeakTable<GatewayClient, NyraExtension> Extensions = new();
    private static readonly ConditionalWeakTable<
        ShardedGatewayClient,
        NyraExtension
    > ShardedExtensions = new();

    /// <summary>Enables Nyra on the client.</summary>
    /// <remarks>
    /// NetCord logs through its own logger, configured only at client creation, so pass a logger here to
    /// receive Nyra's log messages. Enable it on a live client, since one already disposed accepts the
    /// subscription silently and every later connect waits out the sixty-second gateway timeout with a cause
    /// that blames the gateway.
    /// <para>
    /// Voice connections need the guilds and guild voice states intents. Without the guilds intent a kick leaves
    /// a zombie no guild delete reports, and without voice states every join times out waiting for its own state
    /// update.
    /// </para>
    /// <para>
    /// NetCord exposes no way to read the configured intents at runtime, so Nyra cannot warn about a missing
    /// one the way the DSharpPlus adapter does, verify them in the client's configuration.
    /// </para>
    /// </remarks>
    /// <param name="client">The client to enable Nyra on.</param>
    /// <param name="logger">The logger to write the bridge's messages to.</param>
    /// <returns>
    /// The enabled extension, the existing one when Nyra is already on. The logger of the first enabling is
    /// the one the extension keeps for its lifetime, the logger of a later call is ignored unless the earlier
    /// extension was disposed.
    /// </returns>
    public static NyraExtension UseNyra(this GatewayClient client, ILogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(client);

        lock (UseNyraGate)
        {
            if (Extensions.TryGetValue(client, out var existing) && !existing.IsDisposed)
                return existing;

            var extension = new NyraExtension(new NetCordBridge(client, logger));
            Extensions.Remove(client);
            Extensions.Add(client, extension);
            return extension;
        }
    }

    /// <summary>Enables Nyra on the sharded client.</summary>
    /// <remarks>
    /// NetCord logs through its own logger, configured only at client creation, so pass a logger here to
    /// receive Nyra's log messages. A disposed sharded client rejects the subscription at the first event
    /// subscription with the library's own ObjectDisposedException, unlike the single client, which accepts
    /// it silently and waits out the gateway timeout.
    /// <para>
    /// Voice connections need the guilds and guild voice states intents, see the remarks of the single-client
    /// <see cref="UseNyra(GatewayClient, ILogger)"/> for the failure modes and why Nyra cannot verify them at
    /// runtime.
    /// </para>
    /// </remarks>
    /// <param name="client">The sharded client to enable Nyra on, one extension covering every shard.</param>
    /// <param name="logger">The logger to write the bridge's messages to.</param>
    /// <returns>
    /// The enabled extension, the existing one when Nyra is already on. The logger of the first enabling is
    /// the one the extension keeps for its lifetime, the logger of a later call is ignored unless the earlier
    /// extension was disposed.
    /// </returns>
    public static NyraExtension UseNyra(this ShardedGatewayClient client, ILogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(client);

        lock (UseNyraGate)
        {
            if (ShardedExtensions.TryGetValue(client, out var existing) && !existing.IsDisposed)
                return existing;

            foreach (var shard in client)
                if (
                    Extensions.TryGetValue(shard, out var shardExtension)
                    && !shardExtension.IsDisposed
                )
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
            ShardedExtensions.Remove(client);
            ShardedExtensions.Add(client, extension);
            return extension;
        }
    }

    /// <summary>Gets the Nyra extension of the client, or null when Nyra is not enabled.</summary>
    /// <param name="client">The client to read the extension of.</param>
    /// <returns>The extension of the client, null when Nyra is not enabled.</returns>
    public static NyraExtension? GetNyra(this GatewayClient client)
    {
        ArgumentNullException.ThrowIfNull(client);
        return Extensions.TryGetValue(client, out var extension) && !extension.IsDisposed
            ? extension
            : null;
    }

    /// <summary>Gets the Nyra extension of the sharded client, or null when Nyra is not enabled.</summary>
    /// <param name="client">The sharded client to read the extension of.</param>
    /// <returns>The extension of the sharded client, null when Nyra is not enabled.</returns>
    public static NyraExtension? GetNyra(this ShardedGatewayClient client)
    {
        ArgumentNullException.ThrowIfNull(client);
        return ShardedExtensions.TryGetValue(client, out var extension) && !extension.IsDisposed
            ? extension
            : null;
    }
}
