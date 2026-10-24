using System.Collections.Concurrent;

namespace Nyra.Abstractions;

/// <summary>Remembers when the bot was removed from a guild, so a join that began before it can be refused.</summary>
/// <remarks>A record older than ten minutes is dropped by <see cref="Sweep"/>.</remarks>
public sealed class GuildRemovalFence
{
    private const long WindowMilliseconds = 10 * 60 * 1000;
    private const int SweepThreshold = 64;

    private readonly ConcurrentDictionary<ulong, long> _ticks = new();

    /// <summary>Records a removal from a guild at the current tick.</summary>
    /// <param name="guildId">The id of the guild.</param>
    public void Mark(ulong guildId)
    {
        _ticks[guildId] = Environment.TickCount64;
        if (_ticks.Count > SweepThreshold)
            Sweep();
    }

    /// <summary>Whether the bot was removed from a guild at or after a tick.</summary>
    /// <param name="guildId">The id of the guild.</param>
    /// <param name="sinceTick">The <see cref="Environment.TickCount64"/> value the join began at.</param>
    /// <returns>True when a removal at or after <paramref name="sinceTick"/> is on record.</returns>
    public bool RemovedSince(ulong guildId, long sinceTick) =>
        _ticks.TryGetValue(guildId, out var tick) && tick >= sinceTick;

    /// <summary>Drops the record of a guild.</summary>
    /// <param name="guildId">The id of the guild.</param>
    public void Forget(ulong guildId) => _ticks.TryRemove(guildId, out _);

    /// <summary>Drops the records older than the fence window.</summary>
    public void Sweep()
    {
        var now = Environment.TickCount64;
        foreach (var pair in _ticks)
            if (now - pair.Value > WindowMilliseconds)
                _ticks.TryRemove(pair);
    }

    /// <summary>Drops every record.</summary>
    public void Clear() => _ticks.Clear();
}
