using System.Text.Json.Serialization;
using Nyra.Entities;

namespace Nyra.EventArgs;

/// <summary>Raised when a mix layer ended.</summary>
/// <remarks>
/// NodeLink only. The FINISHED and MAIN_ENDED reasons only arrive when the server's mix auto-cleanup is
/// enabled, a layer that plays through with it off ends through the mixer's teardown instead.
/// </remarks>
public sealed class MixEndedEventArgs : System.EventArgs
{
    /// <summary>The guild the player plays in.</summary>
    [JsonPropertyName("guildId"), JsonInclude]
    [JsonConverter(typeof(TolerantSnowflakeConverter))]
    public ulong GuildId { get; internal set; }

    /// <summary>The id of the mix layer that ended.</summary>
    /// <remarks>
    /// Absorbs a null the server sends, the getter never hands out null, a missing or nulled id reads as the
    /// empty string.
    /// </remarks>
    [JsonPropertyName("mixId"), JsonInclude]
    public string MixId
    {
        get;
        internal set => field = value ?? string.Empty;
    } = string.Empty;

    /// <summary>Why the layer ended.</summary>
    /// <remarks>
    /// Defaults to <see cref="MixEndReason.Unknown"/>, a frame without a reason must not read as a natural
    /// finish.
    /// </remarks>
    [JsonPropertyName("reason"), JsonInclude]
    public MixEndReason EndReason { get; internal set; } = MixEndReason.Unknown;
}
