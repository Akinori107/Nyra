using System.Runtime.CompilerServices;
using global::DSharpPlus;
using Microsoft.Extensions.Logging;

namespace Nyra.DSharpPlus;

/// <summary>Entry points for enabling and accessing Nyra on DSharpPlus clients.</summary>
public static class DiscordClientExtensions
{
    private static readonly Lock UseNyraGate = new();
    private static readonly ConditionalWeakTable<DiscordClient, NyraExtension> Extensions = new();

    /// <summary>Enables Nyra on the client.</summary>
    public static NyraExtension UseNyra(this DiscordClient client)
    {
        ArgumentNullException.ThrowIfNull(client);

        lock (UseNyraGate)
        {
            if (Extensions.TryGetValue(client, out var existing))
                return existing;

            // Without the guilds intent the voice channel is never cached, turning a move into a spurious disconnect.
            if (
                !client.Intents.HasIntent(DiscordIntents.Guilds)
                || !client.Intents.HasIntent(DiscordIntents.GuildVoiceStates)
            )
            {
                client.Logger.LogCritical(
                    Events.Intents,
                    "The Nyra extension is registered but the guilds and guild voice states intents are not enabled. Voice connections cannot work without them."
                );
            }

            var extension = new NyraExtension(new DSharpPlusBridge(client));
            Extensions.Add(client, extension);
            return extension;
        }
    }

    /// <summary>Gets the Nyra extension of the client, or null when Nyra is not enabled.</summary>
    public static NyraExtension? GetNyra(this DiscordClient client)
    {
        ArgumentNullException.ThrowIfNull(client);
        return Extensions.TryGetValue(client, out var extension) ? extension : null;
    }

    /// <summary>Enables Nyra on every initialized shard.</summary>
    public static async Task<IReadOnlyDictionary<int, NyraExtension>> UseNyraAsync(
        this DiscordShardedClient client
    )
    {
        ArgumentNullException.ThrowIfNull(client);

        var extensions = new Dictionary<int, NyraExtension>();
        await client.InitializeShardsAsync().ConfigureAwait(false);

        foreach (var shard in client.ShardClients.Values)
            extensions[shard.ShardId] = shard.UseNyra();

        return extensions;
    }

    /// <summary>Gets the Nyra extensions of all initialized shards.</summary>
    public static async Task<IReadOnlyDictionary<int, NyraExtension>> GetNyraAsync(
        this DiscordShardedClient client
    )
    {
        ArgumentNullException.ThrowIfNull(client);

        await client.InitializeShardsAsync().ConfigureAwait(false);

        var extensions = new Dictionary<int, NyraExtension>();
        foreach (var shard in client.ShardClients.Values)
        {
            if (shard.GetNyra() is { } extension)
                extensions[shard.ShardId] = extension;
        }

        return extensions;
    }
}
