namespace Nyra.Abstractions;

/// <summary>Optional bridge capability, reports who is in a voice channel with the bot.</summary>
/// <remarks>
/// Lets a connection count its <see cref="GuildConnection.Listeners"/>. The built-in bridges implement it from
/// the library's cache. Handlers run on the gateway thread, so keep them fast.
/// </remarks>
public interface IVoiceListenerSource
{
    /// <summary>
    /// Raised when a user who is not a bot, other than the current user, joins, leaves or changes a voice
    /// channel in a guild.
    /// </summary>
    event EventHandler<ListenerVoiceStateUpdatedEventArgs>? ListenerVoiceStateUpdated;

    /// <summary>
    /// Returns the users in a voice channel who are not bots, the current user left out, from the library's
    /// cache.
    /// </summary>
    /// <param name="guildId">The guild of the voice channel.</param>
    /// <param name="channelId">The voice channel to look in.</param>
    /// <returns>The user ids, or null when the cache does not hold the guild or the channel.</returns>
    IReadOnlyCollection<ulong>? GetListeners(ulong guildId, ulong channelId);
}
