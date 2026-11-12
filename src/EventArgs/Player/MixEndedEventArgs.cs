using System.Text.Json.Serialization;
using Nyra.Entities;

namespace Nyra.EventArgs;

/// <summary>Raised when a mix layer ended.</summary>
/// <remarks>
/// NodeLink only. Some reasons only arrive when the server's mix auto-cleanup is on.
/// </remarks>
public sealed class MixEndedEventArgs : System.EventArgs
{
    /// <summary>The guild the player plays in.</summary>
    [JsonPropertyName("guildId"), JsonInclude]
    [JsonConverter(typeof(SnowflakeConverter))]
    public ulong GuildId { get; internal set; }

    /// <summary>The id of the mix layer that ended.</summary>
    /// <remarks>Never null, a missing value reads as an empty string.</remarks>
    [JsonPropertyName("mixId"), JsonInclude]
    public string MixId
    {
        get;
        internal set => field = value ?? string.Empty;
    } = string.Empty;

    /// <summary>Why the layer ended.</summary>
    /// <remarks>
    /// <see cref="MixEndReason.Unknown"/> when the server sent none.
    /// </remarks>
    [JsonPropertyName("reason"), JsonInclude]
    public MixEndReason EndReason { get; internal set; } = MixEndReason.Unknown;
}
