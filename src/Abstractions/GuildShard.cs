namespace Nyra.Abstractions;

/// <summary>Discord's routing of a guild to the shard that serves it.</summary>
public static class GuildShard
{
    /// <summary>Returns the id of the shard that serves a guild.</summary>
    /// <param name="guildId">The id of the guild.</param>
    /// <param name="shardCount">The total number of shards, must be positive.</param>
    /// <returns>The shard id, from 0 to <paramref name="shardCount"/> - 1.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="shardCount"/> is not positive.</exception>
    public static int IdFor(ulong guildId, int shardCount)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(shardCount);
        return (int)((guildId >> 22) % (ulong)shardCount);
    }
}
