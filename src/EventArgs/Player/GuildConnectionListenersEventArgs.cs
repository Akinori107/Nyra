namespace Nyra.EventArgs;

/// <summary>Carries how many users who are not bots are in the voice channel of a guild connection.</summary>
/// <param name="guildId">The guild of the connection.</param>
/// <param name="channelId">The voice channel of the connection.</param>
/// <param name="listeners">How many users who are not bots are in the channel, the bot left out.</param>
public sealed class GuildConnectionListenersEventArgs(ulong guildId, ulong channelId, int listeners)
    : System.EventArgs
{
    /// <summary>The guild of the connection.</summary>
    public ulong GuildId { get; } = guildId;

    /// <summary>The voice channel of the connection.</summary>
    public ulong ChannelId { get; } = channelId;

    /// <summary>How many users who are not bots are in the channel, the bot left out.</summary>
    public int Listeners { get; } = listeners;
}
