using System.Text.Json.Serialization;
using Nyra.Entities;

namespace Nyra.EventArgs;

/// <summary>
/// EternalBox context of the playing track, reported whenever the server signals the track start.
/// </summary>
/// <remarks>NodeLink only.</remarks>
public sealed class EternalBoxInfoUpdatedEventArgs : System.EventArgs
{
    /// <summary>The guild the player plays in.</summary>
    [JsonPropertyName("guildId"), JsonInclude]
    [JsonConverter(typeof(TolerantSnowflakeConverter))]
    public ulong GuildId { get; internal set; }

    /// <summary>The track the info belongs to, always the track the event fired for.</summary>
    [JsonPropertyName("track"), JsonInclude]
    public Track? Track { get; internal set; }

    /// <summary>The info the provider reported for the track.</summary>
    /// <remarks>
    /// Absorbs a null the server sends, the getter never hands out null, a missing or nulled info reads as an
    /// empty one.
    /// </remarks>
    [JsonPropertyName("eternalbox"), JsonInclude]
    public EternalBoxInfo Info
    {
        get;
        internal set => field = value ?? new();
    } = new();
}
