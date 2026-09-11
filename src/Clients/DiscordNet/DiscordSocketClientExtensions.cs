using System.Runtime.CompilerServices;
using Discord.WebSocket;
using Microsoft.Extensions.Logging;

namespace Nyra.DiscordNet;

/// <summary>Entry points for enabling Nyra on Discord.Net clients.</summary>
public static class DiscordSocketClientExtensions
{
    private static readonly Lock UseNyraGate = new();
    private static readonly ConditionalWeakTable<BaseSocketClient, NyraExtension> Extensions =
        new();

    /// <summary>
    /// Enables Nyra on the client. Discord.Net offers no native Microsoft.Extensions.Logging,
    /// pass a logger to receive the library's log messages through it.
    /// </summary>
    public static NyraExtension UseNyra(this DiscordSocketClient client, ILogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(client);

        lock (UseNyraGate)
        {
            if (Extensions.TryGetValue(client, out var existing))
                return existing;

            var extension = new NyraExtension(new DiscordNetBridge(client, logger));
            Extensions.Add(client, extension);
            return extension;
        }
    }

    /// <summary>
    /// Enables Nyra on every shard. Discord.Net offers no native Microsoft.Extensions.Logging,
    /// pass a logger to receive the library's log messages through it.
    /// </summary>
    public static NyraExtension UseNyra(this DiscordShardedClient client, ILogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(client);

        lock (UseNyraGate)
        {
            if (Extensions.TryGetValue(client, out var existing))
                return existing;

            var extension = new NyraExtension(new DiscordNetBridge(client, logger));
            Extensions.Add(client, extension);
            return extension;
        }
    }

    /// <summary>Gets the Nyra extension of the client, or null when Nyra is not enabled.</summary>
    public static NyraExtension? GetNyra(this DiscordSocketClient client)
    {
        ArgumentNullException.ThrowIfNull(client);
        return Extensions.TryGetValue(client, out var extension) ? extension : null;
    }

    /// <summary>Gets the Nyra extension of the sharded client, or null when Nyra is not enabled.</summary>
    public static NyraExtension? GetNyra(this DiscordShardedClient client)
    {
        ArgumentNullException.ThrowIfNull(client);
        return Extensions.TryGetValue(client, out var extension) ? extension : null;
    }
}
