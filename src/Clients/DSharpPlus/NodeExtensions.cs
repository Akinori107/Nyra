using global::DSharpPlus;
using global::DSharpPlus.Entities;

namespace Nyra.DSharpPlus;

/// <summary>Nyra entry points that need DSharpPlus channel types.</summary>
public static class NodeExtensions
{
    /// <summary>
    /// Connects the bot to the specified voice or stage channel and creates a guild connection
    /// on the node.
    /// </summary>
    public static Task<GuildConnection> ConnectAsync(
        this Node node,
        DiscordChannel channel,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(channel);

        if (channel.Type is not (ChannelType.Voice or ChannelType.Stage))
            throw new ArgumentException(
                "The channel must be a voice or stage channel.",
                nameof(channel)
            );

        if (channel.GuildId is null)
            throw new ArgumentException("The channel does not belong to a guild.", nameof(channel));

        return node.ConnectAsync(channel.GuildId.Value, channel.Id, cancellationToken);
    }
}
