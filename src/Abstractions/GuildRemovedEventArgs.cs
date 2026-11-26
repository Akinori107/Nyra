namespace Nyra.Abstractions;

/// <summary>Raised when the bot was removed from a Discord guild.</summary>
/// <remarks>
/// Discord sends no voice update for a removal, so this is the only notice. A removal during a gateway outage is
/// not reported.
/// </remarks>
/// <param name="guildId">The id of the guild the bot was removed from.</param>
public sealed class GuildRemovedEventArgs(ulong guildId) : System.EventArgs
{
    /// <summary>The id of the guild the bot was removed from.</summary>
    public ulong GuildId { get; } = guildId;
}
