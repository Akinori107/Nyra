using System.Text.Json.Serialization;

namespace Nyra.EventArgs;

/// <summary>Raised when the server destroyed the player.</summary>
/// <remarks>
/// NodeLink only. Nyra's own teardown is not reported, so a destroy here came from another client on the same
/// session, from the server itself (like the bot being kicked from voice), or late from an older connection of
/// the same guild.
/// <para>
/// The destroy is only a notice. The track, position and filters stay until the next play or a teardown, and a
/// later PlayAsync creates the player again.
/// </para>
/// </remarks>
public sealed class PlayerDestroyedEventArgs : System.EventArgs
{
    /// <summary>The guild the player played in.</summary>
    [JsonPropertyName("guildId"), JsonInclude]
    [JsonConverter(typeof(SnowflakeConverter))]
    public ulong GuildId { get; internal set; }
}
