using global::NetCord;

namespace Nyra.NetCord;

/// <summary>Nyra entry points that need NetCord channel types.</summary>
public static class NodeExtensions
{
    /// <summary>
    /// Connects the bot to the specified voice or stage channel and creates a guild connection
    /// on the node.
    /// </summary>
    public static Task<GuildConnection> ConnectAsync(
        this Node node,
        IVoiceGuildChannel channel,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(channel);
        return node.ConnectAsync(channel.GuildId, channel.Id, cancellationToken);
    }
}
