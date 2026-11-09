namespace Nyra.Abstractions;

/// <summary>Raised when the bot was removed from a Discord guild.</summary>
/// <remarks>
/// A guild removal produces no voice state update for the bot, so this is the only notification a live voice
/// connection gets while the gateway session is connected. A removal that lands inside a gateway outage that
/// invalidates the session is not reported because no guild delete is dispatched for it, so the re-invite
/// join has to notice the dead connection on its own.
/// </remarks>
/// <param name="guildId">The id of the guild the bot was removed from.</param>
public sealed class GuildRemovedEventArgs(ulong guildId) : System.EventArgs
{
    /// <summary>The id of the guild the bot was removed from.</summary>
    public ulong GuildId { get; } = guildId;
}
