namespace Nyra.EventArgs;

/// <summary>Raised when a guild connection went <see cref="NodeOptions.IdleTimeout"/> without a track.</summary>
/// <remarks>The connection disconnects once the handlers return, unless one of them started a track.</remarks>
/// <param name="guildId">The guild whose connection went idle.</param>
/// <param name="channelId">The voice channel the connection is leaving.</param>
public sealed class GuildConnectionIdleEventArgs(ulong guildId, ulong channelId) : System.EventArgs
{
    /// <summary>The guild whose connection went idle.</summary>
    public ulong GuildId { get; } = guildId;

    /// <summary>The voice channel the connection is leaving.</summary>
    public ulong ChannelId { get; } = channelId;
}
