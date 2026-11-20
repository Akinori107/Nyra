namespace Nyra.Abstractions;

/// <summary>A user who is not a bot joined, left or changed a voice channel in a guild.</summary>
/// <param name="guildId">The id of the guild the voice state belongs to.</param>
/// <param name="userId">The id of the user whose voice state changed.</param>
/// <param name="channelId">The voice channel the user is in after the change, null when they left.</param>
public sealed class ListenerVoiceStateUpdatedEventArgs(
    ulong guildId,
    ulong userId,
    ulong? channelId
) : System.EventArgs
{
    /// <summary>The id of the guild the voice state belongs to.</summary>
    public ulong GuildId { get; } = guildId;

    /// <summary>The id of the user whose voice state changed.</summary>
    public ulong UserId { get; } = userId;

    /// <summary>The voice channel the user is in after the change, null when they left.</summary>
    public ulong? ChannelId { get; } = channelId;
}
