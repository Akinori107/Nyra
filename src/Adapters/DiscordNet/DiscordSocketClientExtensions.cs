using System.Runtime.CompilerServices;
using Discord.WebSocket;
using Microsoft.Extensions.Logging;

namespace Nyra.DiscordNet;

/// <summary>Entry points for enabling and accessing Nyra on Discord.Net clients.</summary>
public static class DiscordSocketClientExtensions
{
    private static readonly Lock UseNyraGate = new();
    private static readonly ConditionalWeakTable<BaseSocketClient, NyraExtension> Extensions = [];

    /// <summary>Enables Nyra on the client.</summary>
    /// <remarks>
    /// Pass a logger to get Nyra's log messages, without one nothing is logged.
    /// <para>
    /// Voice needs the guilds and guild voice states intents. Nyra cannot check them on Discord.Net, so set them
    /// in the DiscordSocketConfig. Logging in as another account needs the extension disposed and enabled again.
    /// </para>
    /// </remarks>
    /// <param name="client">The client to enable Nyra on.</param>
    /// <param name="logger">The logger to write the library's messages through.</param>
    /// <returns>
    /// The enabled extension, the existing one when Nyra is already on, which keeps its first logger.
    /// </returns>
    public static NyraExtension UseNyra(this DiscordSocketClient client, ILogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(client);
        return Enable(client, () => new DiscordNetBridge(client, logger));
    }

    /// <summary>Enables Nyra on every shard.</summary>
    /// <remarks>
    /// Pass a logger to get Nyra's log messages. Voice needs the guilds and guild voice states intents. Do not
    /// also enable Nyra on one of its shards, both would handle the same joins.
    /// </remarks>
    /// <param name="client">The sharded client to enable Nyra on, one extension covering every shard.</param>
    /// <param name="logger">The logger to write the library's messages through.</param>
    /// <returns>
    /// The enabled extension, the existing one when Nyra is already on, which keeps its first logger.
    /// </returns>
    public static NyraExtension UseNyra(this DiscordShardedClient client, ILogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(client);
        return Enable(client, () => new DiscordNetBridge(client, logger));
    }

    /// <summary>Gets the Nyra extension of the client, or null when Nyra is not enabled.</summary>
    /// <param name="client">The client to read the extension of.</param>
    /// <returns>The extension of the client, null when Nyra is not enabled.</returns>
    public static NyraExtension? GetNyra(this DiscordSocketClient client)
    {
        ArgumentNullException.ThrowIfNull(client);
        return Find(client);
    }

    /// <summary>Gets the Nyra extension of the sharded client, or null when Nyra is not enabled.</summary>
    /// <param name="client">The sharded client to read the extension of.</param>
    /// <returns>The extension of the sharded client, null when Nyra is not enabled.</returns>
    public static NyraExtension? GetNyra(this DiscordShardedClient client)
    {
        ArgumentNullException.ThrowIfNull(client);
        return Find(client);
    }

    private static NyraExtension Enable(
        BaseSocketClient client,
        Func<DiscordNetBridge> createBridge
    )
    {
        lock (UseNyraGate)
        {
            if (Find(client) is { } existing)
                return existing;

            var extension = new NyraExtension(createBridge());
            Extensions.AddOrUpdate(client, extension);
            return extension;
        }
    }

    private static NyraExtension? Find(BaseSocketClient client) =>
        Extensions.TryGetValue(client, out var extension) && !extension.IsDisposed
            ? extension
            : null;
}
