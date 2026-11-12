using System.Runtime.CompilerServices;
using DSharpPlus;
using Microsoft.Extensions.Logging;

namespace Nyra.DSharpPlus;

/// <summary>Entry points for enabling, accessing, and removing Nyra on DSharpPlus clients.</summary>
public static class DiscordClientExtensions
{
    private static readonly Lock UseNyraGate = new();
    private static readonly ConditionalWeakTable<DiscordClient, NyraExtension> Extensions = [];

    /// <summary>Enables Nyra on the client.</summary>
    /// <param name="client">The client to enable Nyra on.</param>
    /// <returns>The enabled extension, the existing one when Nyra is already on.</returns>
    public static NyraExtension UseNyra(this DiscordClient client) =>
        UseNyra(client, shardCount: null);

    /// <summary>Enables Nyra on a manually sharded client.</summary>
    /// <remarks>
    /// Pass the total so the bridge can reject guilds of another shard, without it the join times out with a
    /// misleading permission error. Use the sharded client's own entry point instead when the shards live in
    /// one process.
    /// </remarks>
    /// <param name="client">The client to enable Nyra on.</param>
    /// <param name="shardCount">The total number of shards the fleet runs.</param>
    /// <returns>The enabled extension, the existing one when Nyra is already on.</returns>
    public static NyraExtension UseNyra(this DiscordClient client, int? shardCount)
    {
        ArgumentNullException.ThrowIfNull(client);

        lock (UseNyraGate)
        {
            if (Extensions.TryGetValue(client, out var existing) && !existing.IsDisposed)
            {
                if (
                    shardCount is { } total
                    && total > 1
                    && existing.Client is DSharpPlusBridge bridge
                    && bridge.ShardCountHint != total
                )
                    try
                    {
                        client.Logger.LogWarning(
                            LogEvents.Sharding,
                            "The Nyra extension of this shard was not wired with the sharded client's total of {Total} shards, it cannot reject guilds that belong to another shard. Dispose it and enable the shard through the sharded client again to pick the total up.",
                            total
                        );
                    }
                    catch { }

                return existing;
            }

            if (
                !client.Intents.HasIntent(DiscordIntents.Guilds)
                || !client.Intents.HasIntent(DiscordIntents.GuildVoiceStates)
            )
                try
                {
                    client.Logger.LogCritical(
                        LogEvents.Intents,
                        "The Nyra extension is registered but the guilds and guild voice states intents are not both enabled. Voice connections cannot work without them: without the guilds intent a move reads as a disconnect and a kick leaves a zombie the re-invite hands out again."
                    );
                }
                catch { }

            var extension = new NyraExtension(new DSharpPlusBridge(client, shardCount));
            Extensions.AddOrUpdate(client, extension);
            return extension;
        }
    }

    /// <summary>
    /// Disposes the client's Nyra extension and forgets it, the Discord client itself is left running.
    /// </summary>
    /// <remarks>
    /// Call it on every shard before stopping or restarting a sharded client, stopping it does not dispose
    /// the extensions.
    /// </remarks>
    /// <param name="client">The client whose Nyra extension to remove.</param>
    public static void RemoveNyra(this DiscordClient client)
    {
        ArgumentNullException.ThrowIfNull(client);
        TakeExtension(client)?.Dispose();
    }

    /// <summary>Removes the client's Nyra extension and disposes it without blocking the caller.</summary>
    /// <remarks>
    /// Prefer it over the blocking RemoveNyra where you can await.
    /// </remarks>
    /// <param name="client">The client whose Nyra extension to remove.</param>
    /// <returns>A task that completes when the extension was removed and disposed.</returns>
    public static async Task RemoveNyraAsync(this DiscordClient client)
    {
        ArgumentNullException.ThrowIfNull(client);
        if (TakeExtension(client) is { } existing)
            await existing.DisposeAsync().ConfigureAwait(false);
    }

    private static NyraExtension? TakeExtension(DiscordClient client)
    {
        lock (UseNyraGate)
        {
            if (!Extensions.TryGetValue(client, out var existing))
                return null;

            Extensions.Remove(client);
            return existing;
        }
    }

    /// <summary>Gets the Nyra extension of the client, or null when Nyra is not enabled.</summary>
    /// <param name="client">The client to read the extension of.</param>
    /// <returns>The extension of the client, null when Nyra is not enabled.</returns>
    public static NyraExtension? GetNyra(this DiscordClient client)
    {
        ArgumentNullException.ThrowIfNull(client);
        return Extensions.TryGetValue(client, out var extension) && !extension.IsDisposed
            ? extension
            : null;
    }

    /// <summary>Enables Nyra on every shard of the sharded client.</summary>
    /// <remarks>
    /// Call RemoveNyra, or dispose the returned extensions, before stopping or restarting the sharded client.
    /// Stopping it does not dispose them, and enabling Nyra again would run two sets of nodes for the same bot.
    /// </remarks>
    /// <param name="client">The sharded client to enable Nyra on, one extension per shard.</param>
    /// <returns>The extensions of the shards, keyed by shard id.</returns>
    public static async Task<IReadOnlyDictionary<int, NyraExtension>> UseNyraAsync(
        this DiscordShardedClient client
    )
    {
        ArgumentNullException.ThrowIfNull(client);

        var extensions = new Dictionary<int, NyraExtension>();
        await InitializeShardsSerializedAsync(client).ConfigureAwait(false);

        var shardCount = client.ShardClients.Count;
        foreach (var shard in client.ShardClients.Values)
            extensions[shard.ShardId] = shard.UseNyra(shardCount);

        return extensions;
    }

    /// <summary>Gets the Nyra extensions of all initialized shards.</summary>
    /// <remarks>
    /// On a client that was not started yet this connects to the gateway first.
    /// </remarks>
    /// <param name="client">The sharded client to read the extensions of.</param>
    /// <returns>The extensions of the initialized shards, keyed by shard id.</returns>
    public static async Task<IReadOnlyDictionary<int, NyraExtension>> GetNyraAsync(
        this DiscordShardedClient client
    )
    {
        ArgumentNullException.ThrowIfNull(client);

        await InitializeShardsSerializedAsync(client).ConfigureAwait(false);

        var extensions = new Dictionary<int, NyraExtension>();
        foreach (var shard in client.ShardClients.Values)
            if (shard.GetNyra() is { } extension)
                extensions[shard.ShardId] = extension;

        return extensions;
    }

    private static readonly ConditionalWeakTable<
        DiscordShardedClient,
        SemaphoreSlim
    > ShardInitGates = [];

    private static async Task InitializeShardsSerializedAsync(DiscordShardedClient client)
    {
        var gate = ShardInitGates.GetValue(client, static _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync().ConfigureAwait(false);
        try
        {
            await client.InitializeShardsAsync().ConfigureAwait(false);
        }
        finally
        {
            gate.Release();
        }
    }
}
