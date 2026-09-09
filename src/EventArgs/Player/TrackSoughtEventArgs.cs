using System.Text.Json.Serialization;
using Nyra.Entities;

namespace Nyra.EventArgs;

public sealed class TrackSoughtEventArgs : System.EventArgs
{
    [JsonPropertyName("guildId")]
    public ulong GuildId { get; init; }

    /// <summary>Playback position after the seek, in milliseconds.</summary>
    [JsonPropertyName("position")]
    public long PositionMilliseconds { get; init; }

    /// <summary>Signed distance the position moved by, in milliseconds, negative for a seek backwards.</summary>
    [JsonPropertyName("duration")]
    public long DeltaMilliseconds { get; init; }

    [JsonIgnore]
    public TimeSpan Position =>
        PositionMilliseconds is >= 0 and <= TrackInfo.MaxTimeSpanMilliseconds
            ? TimeSpan.FromMilliseconds(PositionMilliseconds)
            : TimeSpan.Zero;

    [JsonIgnore]
    public TimeSpan Delta =>
        DeltaMilliseconds
            is >= -TrackInfo.MaxTimeSpanMilliseconds
                and <= TrackInfo.MaxTimeSpanMilliseconds
            ? TimeSpan.FromMilliseconds(DeltaMilliseconds)
            : TimeSpan.Zero;
}
