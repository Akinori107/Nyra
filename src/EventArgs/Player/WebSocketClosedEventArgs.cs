using System.Text.Json.Serialization;

namespace Nyra.EventArgs;

/// <summary>Raised when the Discord voice websocket of the player was closed.</summary>
/// <remarks>
/// For NodeLink this also reports synthetic voice deaths (worker failure, UDP/IP-discovery loss) that never
/// closed a Discord websocket (see <see cref="Code"/>). It also emits deaths that never came from Discord
/// wearing Discord codes, for example 4015 when its voice library gives up reconnecting and 4014 when the
/// voice server endpoint is null. Do not attribute 4xxx to Discord on NodeLink without checking the reason.
/// </remarks>
public sealed class WebSocketClosedEventArgs : System.EventArgs
{
    /// <summary>The guild the player plays in.</summary>
    [JsonPropertyName("guildId"), JsonInclude]
    [JsonConverter(typeof(TolerantSnowflakeConverter))]
    public ulong GuildId { get; internal set; }

    /// <summary>Discord voice close event code, the 4xxx family signals failures.</summary>
    /// <remarks>
    /// NodeLink also emits synthetic non-Discord codes, 1000 on client destroy, 5001 on cluster worker
    /// failure, and null on voice failures that carry no close code at all (UDP loss, IP discovery failure),
    /// those deaths never opened a close handshake, there is no code to report.
    /// </remarks>
    [JsonPropertyName("code"), JsonInclude]
    public int? Code { get; internal set; }

    /// <summary>Why the voice websocket closed.</summary>
    /// <remarks>
    /// Absorbs a null reason the server sends, readers see an empty string instead of null. Defensive
    /// only, no current server sends one, NodeLink's no-close deaths carry the library's own reason
    /// string, see <see cref="Code"/> for the discriminator.
    /// </remarks>
    [JsonPropertyName("reason"), JsonInclude]
    public string Reason
    {
        get;
        internal set => field = value ?? string.Empty;
    } = string.Empty;

    /// <summary>Whether the websocket was closed by Discord.</summary>
    /// <remarks>
    /// NodeLink reports true for every synthetic voice death as well, its voice library sets it
    /// unconditionally, so true does not imply a Discord-initiated close outside Lavalink.
    /// </remarks>
    [JsonPropertyName("byRemote"), JsonInclude]
    public bool ByRemote { get; internal set; }
}
