using System.Text.Json.Serialization;
using Nyra.Entities;

namespace Nyra.EventArgs;

/// <summary>Raised when the server created the player.</summary>
/// <remarks>
/// NodeLink only. The creation this connection's own join caused races the return of the connect and usually
/// fires before any handler can subscribe, so treat the connect's completion as the creation signal.
/// <para>
/// This event only reports creations that happen on this session after a handler subscribed, such as a
/// re-creation after a resume (the voice re-send or a later play re-creates a player lost during the outage).
/// A creation by a co-client on another session never delivers here, the event is scoped to the creating
/// session.
/// </para>
/// </remarks>
public sealed class PlayerCreatedEventArgs : System.EventArgs
{
    /// <summary>The guild the player plays in.</summary>
    [JsonPropertyName("guildId"), JsonInclude]
    [JsonConverter(typeof(TolerantSnowflakeConverter))]
    public ulong GuildId { get; internal set; }

    /// <summary>The player the server created.</summary>
    /// <remarks>
    /// Absorbs a null player the server sends, the setter substitutes a fresh instance, so handlers never
    /// read null even when the frame carries one. The fallback player is hollow, <see
    /// cref="Nyra.Entities.Player.GuildId"/> reads 0 (use the args' <see cref="GuildId"/>, which the frame
    /// populated), <see cref="Nyra.Entities.Player.Volume"/> reads 0 (the server's default is 100), and every
    /// reference member is null.
    /// </remarks>
    [JsonPropertyName("player"), JsonInclude]
    public Player Player
    {
        get;
        internal set => field = value ?? new();
    } = new();
}
