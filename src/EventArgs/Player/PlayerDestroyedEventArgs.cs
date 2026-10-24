using System.Text.Json.Serialization;

namespace Nyra.EventArgs;

/// <summary>Raised when the server destroyed the player.</summary>
/// <remarks>
/// NodeLink only, and every destroy arrives as a WebSocketClosed of code 1000 immediately before this
/// event. Nyra drops the echo of its own teardown (the registry removal precedes the REST destroy), so a
/// raised destroy came from a co-client on the same session, from the server itself (a kick's voice death,
/// the session reap of a closing socket), or from a stale frame of a predecessor's teardown.
/// <para>
/// The destroy is a notification only. The connection's playback half (track, position estimate, filters)
/// keeps its last state until the next play or a teardown resets it, and a subsequent PlayAsync re-creates
/// the player.
/// </para>
/// </remarks>
public sealed class PlayerDestroyedEventArgs : System.EventArgs
{
    /// <summary>The guild the player played in.</summary>
    [JsonPropertyName("guildId"), JsonInclude]
    [JsonConverter(typeof(TolerantSnowflakeConverter))]
    public ulong GuildId { get; internal set; }
}
