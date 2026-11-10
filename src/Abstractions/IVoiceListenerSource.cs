namespace Nyra.Abstractions;

/// <summary>Optional bridge capability, reports who is in a voice channel with the bot.</summary>
/// <remarks>
/// Lets a connection count its <see cref="GuildConnection.Listeners"/>, the users in its voice channel who are
/// not bots. Implemented by the built-in bridges from the library's cache, consumed opportunistically. A user
/// whose member the cache does not hold counts as a listener on NetCord and DSharpPlus, so a gap in the cache
/// keeps the bot in its channel. Discord.Net only sees the users its cache holds.
/// <para>
/// Handlers run inline on the library's gateway dispatch thread, the same as
/// <see cref="IGuildRemovalSource.GuildRemoved"/>.
/// </para>
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
