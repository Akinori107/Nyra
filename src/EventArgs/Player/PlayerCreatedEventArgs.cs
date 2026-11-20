using System.Text.Json.Serialization;
using Nyra.Entities;

namespace Nyra.EventArgs;

/// <summary>Raised when the server created the player.</summary>
/// <remarks>
/// NodeLink only. The player created by the join usually fires before a handler can subscribe, so treat the
/// end of the connect as the creation. Players created by another client on another session are not
/// reported.
/// </remarks>
public sealed class PlayerCreatedEventArgs : System.EventArgs
{
    /// <summary>The guild the player plays in.</summary>
    [JsonPropertyName("guildId"), JsonInclude]
    [JsonConverter(typeof(SnowflakeConverter))]
    public ulong GuildId { get; internal set; }

    /// <summary>The player the server created.</summary>
    /// <remarks>Never null, a missing value reads as an empty instance.</remarks>
    [JsonPropertyName("player"), JsonInclude]
    public Player Player
    {
        get;
        internal set => field = value ?? new();
    } = new();
}
