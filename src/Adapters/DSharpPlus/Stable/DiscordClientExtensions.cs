using System.Runtime.CompilerServices;
using DSharpPlus;
using Microsoft.Extensions.Logging;

namespace Nyra.DSharpPlus;

/// <summary>Entry points for enabling, accessing, and removing Nyra on DSharpPlus clients.</summary>
public static class DiscordClientExtensions
{
    private static readonly Lock UseNyraGate = new();
    private static readonly ConditionalWeakTable<DiscordClient, NyraExtension> Extensions = new();

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
                    shardCount is int total
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
            Extensions.Remove(client);
            Extensions.Add(client, extension);
            return extension;
        }
    }

    /// <summary>
    /// Disposes the client's Nyra extension and forgets it, the Discord client itself is left running.
    /// </summary>
    /// <remarks>
    /// A stop of the sharded client destroys its shard objects without disposing their extensions. The
    /// orphaned fleet stays alive on the dead shards, and a later <c>StartAsync</c> builds fresh shards whose
    /// extensions know nothing of it, so a second UseNyra there races the old fleet's node sessions. Call this
    /// per shard before stopping or recycling a sharded client, or dispose the extensions UseNyra returned.
    /// </remarks>
    /// <param name="client">The client whose Nyra extension to remove.</param>
    public static void RemoveNyra(this DiscordClient client)
    {
        ArgumentNullException.ThrowIfNull(client);
        TakeExtension(client)?.Dispose();
    }

    /// <summary>Removes the client's Nyra extension and disposes it without blocking the caller.</summary>
    /// <remarks>
    /// The awaitable counterpart of <see cref="RemoveNyra(DiscordClient)"/>, the dispose tears every node
    /// down in parallel instead of blocking a sync caller for the whole bound, prefer it on shutdown paths
    /// that can await.
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
    /// A shard already enabled on its own keeps it, but without the sharded total it cannot reject foreign
    /// guilds and is flagged with a warning.
    /// <para>
    /// Stopping the sharded client destroys its shard objects without disposing these extensions. The orphaned
    /// fleet stays alive on the dead shards, GetNyraAsync on the restarted client reports nothing, and a second
    /// UseNyraAsync builds a new fleet whose nodes fight the old one's sessions for the same user id. Call
    /// RemoveNyra (or dispose the extensions this returns) on every shard before stopping or recycling the
    /// client.
    /// </para>
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
    /// This is not a pure read, an uninitialized sharded client initializes here, which does gateway I/O, can
    /// throw the library's raw auth error on a bad token and can hang on rate limits. Call it on an already
    /// started client for a pure read.
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
    > ShardInitGates = new();

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
