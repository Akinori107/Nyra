using Discord;

namespace Nyra.DiscordNet;

/// <summary>Nyra entry points that need Discord.Net channel types.</summary>
public static class NodeExtensions
{
    /// <summary>
    /// Connects the bot to the specified voice or stage channel and creates a guild connection
    /// on the node.
    /// </summary>
    public static Task<GuildConnection> ConnectAsync(
        this Node node,
        IVoiceChannel channel,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(channel);

        // IVoiceChannel already restricts to guild voice and stage channels.
        return node.ConnectAsync(channel.GuildId, channel.Id, cancellationToken);
    }
}
