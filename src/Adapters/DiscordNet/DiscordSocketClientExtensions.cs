using System.Runtime.CompilerServices;
using Discord.WebSocket;
using Microsoft.Extensions.Logging;

namespace Nyra.DiscordNet;

/// <summary>Entry points for enabling and accessing Nyra on Discord.Net clients.</summary>
public static class DiscordSocketClientExtensions
{
    private static readonly Lock UseNyraGate = new();
    private static readonly ConditionalWeakTable<BaseSocketClient, NyraExtension> Extensions =
        new();

    /// <summary>Enables Nyra on the client.</summary>
    /// <remarks>
    /// Discord.Net offers no native Microsoft.Extensions.Logging, pass a logger to receive the library's log
    /// messages through it. Without one the bridge stays mute by design.
    /// <para>
    /// Voice connections need the guilds and guild voice states intents. Without guilds the join fails fast on
    /// a channel it cannot resolve (guilds arrive as unavailable shells) and a kick leaves a zombie no guild
    /// delete reports. Without voice states Discord.Net itself rejects the join with a missing-intent error the
    /// moment the connect is attempted.
    /// </para>
    /// <para>
    /// Discord.Net exposes no way to read the configured intents at runtime, so Nyra cannot warn about a
    /// missing one the way the DSharpPlus adapter does, verify them in the DiscordSocketConfig.
    /// </para>
    /// <para>
    /// A client re-logged-in as another account needs the extension disposed and enabled again, the bridge
    /// keeps the first account's id and only reports the switch through a logger.
    /// </para>
    /// </remarks>
    /// <param name="client">The client to enable Nyra on.</param>
    /// <param name="logger">The logger to write the library's messages through.</param>
    /// <returns>
    /// The enabled extension, the existing one when Nyra is already on. The logger of the first enabling is
    /// the one the extension keeps for its lifetime, the logger of a later call is ignored unless the earlier
    /// extension was disposed.
    /// </returns>
    public static NyraExtension UseNyra(this DiscordSocketClient client, ILogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(client);
        return Enable(client, () => new DiscordNetBridge(client, logger));
    }

    /// <summary>Enables Nyra on every shard.</summary>
    /// <remarks>
    /// Discord.Net offers no native Microsoft.Extensions.Logging, pass a logger to receive the library's log
    /// messages through it.
    /// <para>
    /// Voice connections need the guilds and guild voice states intents, see the remarks of the single-client
    /// <see cref="UseNyra(DiscordSocketClient, ILogger)"/> for the failure modes and why Nyra cannot verify
    /// them at runtime.
    /// </para>
    /// <para>
    /// Do not also enable Nyra on an inner shard of the same sharded client. The shard forwards its events to
    /// the sharded client, so both bridges receive them and the two extensions race the same joins with no
    /// cross-extension guard.
    /// </para>
    /// </remarks>
    /// <param name="client">The sharded client to enable Nyra on, one extension covering every shard.</param>
    /// <param name="logger">The logger to write the library's messages through.</param>
    /// <returns>
    /// The enabled extension, the existing one when Nyra is already on. The logger of the first enabling is
    /// the one the extension keeps for its lifetime, the logger of a later call is ignored unless the earlier
    /// extension was disposed.
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
            Extensions.Remove(client);
            Extensions.Add(client, extension);
            return extension;
        }
    }

    private static NyraExtension? Find(BaseSocketClient client) =>
        Extensions.TryGetValue(client, out var extension) && !extension.IsDisposed
            ? extension
            : null;
}
