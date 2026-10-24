using System.Text.Json;
using System.Text.Json.Serialization;

namespace Nyra.Entities;

/// <summary>Discord voice state of a player.</summary>
/// <remarks>
/// When sent, <see cref="Token"/>, <see cref="Endpoint"/>, <see cref="SessionId"/> and <see
/// cref="ChannelId"/> must go together. Lavalink rejects the update without a channel id, NodeLink only
/// requires the first three.
/// <para>
/// That pairing is the write model only. Read back, on the player events, the same record mirrors the
/// server's report member by member, with nulls wherever the server sent none.
/// </para>
/// </remarks>
public sealed record PlayerVoiceState
{
    /// <summary>The token of the voice session.</summary>
    [JsonPropertyName("token"), JsonInclude]
    public string? Token { get; internal set; }

    /// <summary>The endpoint of the voice server.</summary>
    [JsonPropertyName("endpoint"), JsonInclude]
    public string? Endpoint { get; internal set; }

    /// <summary>The id of the voice session.</summary>
    [JsonPropertyName("sessionId"), JsonInclude]
    public string? SessionId { get; internal set; }

    /// <summary>Sent by Lavalink v4.2.0+ and NodeLink, older servers omit it.</summary>
    [JsonPropertyName("channelId"), JsonInclude]
    public ulong? ChannelId { get; internal set; }
}

/// <summary>Player state, delivered through <c>playerUpdate</c> operations and player REST responses.</summary>
public sealed record PlayerState
{
    /// <summary>Unix timestamp, in milliseconds.</summary>
    [JsonPropertyName("time"), JsonInclude]
    public long TimeMilliseconds { get; internal set; }

    /// <summary>When the server reported the state.</summary>
    /// <remarks>
    /// <see cref="DateTimeOffset.MinValue"/> when the epoch value is out of the convertible range. The
    /// degenerate in-range value <see cref="UnixTime.MinMilliseconds"/> converts to exactly <see
    /// cref="DateTimeOffset.MinValue"/> too, so the sentinel cannot be told apart from it.
    /// </remarks>
    [JsonIgnore]
    public DateTimeOffset Time => UnixTime.ToDateTimeOffset(TimeMilliseconds);

    /// <summary>
    /// Playback position in milliseconds, 0 when nothing is playing, or the live-stream marker (<see
    /// cref="long.MaxValue"/>) on NodeLink streams.
    /// </summary>
    /// <remarks><see cref="Position"/> reports zero for it.</remarks>
    [JsonPropertyName("position"), JsonInclude]
    [JsonConverter(typeof(RoundedInt64Converter))]
    public long PositionMilliseconds { get; internal set; }

    /// <summary>
    /// The playback position the server reported, zero for a live stream, whose raw marker is <see
    /// cref="PositionMilliseconds"/>.
    /// </summary>
    [JsonIgnore]
    public TimeSpan Position => TrackInfo.ToTimeSpan(PositionMilliseconds);

    /// <summary>Whether the node is connected to the Discord voice gateway.</summary>
    [JsonPropertyName("connected"), JsonInclude]
    public bool Connected { get; internal set; }

    /// <summary>Voice gateway latency in milliseconds.</summary>
    /// <remarks>Lavalink reports -1 and NodeLink 0 when not connected.</remarks>
    [JsonPropertyName("ping"), JsonInclude]
    public long Ping { get; internal set; } = -1;
}

[JsonDerivedType(typeof(PlayerUpdateTrack))]
[JsonDerivedType(typeof(PlayerIdentifierUpdateTrack))]
internal abstract record PlayerUpdateTrackBase
{
    [JsonPropertyName("userData")]
    public JsonElement? UserData { get; init; }
}

internal sealed record PlayerUpdateTrack : PlayerUpdateTrackBase
{
    [JsonPropertyName("encoded"), JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public string? Encoded { get; init; }

    [JsonPropertyName("audioTrackId")]
    public string? AudioTrackId { get; init; }

    public bool Equals(PlayerUpdateTrack? other) =>
        other is not null
        && Encoded == other.Encoded
        && AudioTrackId == other.AudioTrackId
        && ContentEquality.JsonElementEquals(UserData, other.UserData);

    public override int GetHashCode() => HashCode.Combine(Encoded, AudioTrackId);
}

internal sealed record PlayerIdentifierUpdateTrack : PlayerUpdateTrackBase
{
    [JsonPropertyName("identifier")]
    public string Identifier { get; init; } = string.Empty;

    public bool Equals(PlayerIdentifierUpdateTrack? other) =>
        other is not null
        && Identifier == other.Identifier
        && ContentEquality.JsonElementEquals(UserData, other.UserData);

    public override int GetHashCode() => Identifier.GetHashCode();
}

internal sealed record PlayerUpdatePayload
{
    [JsonPropertyName("track")]
    public PlayerUpdateTrackBase? Track { get; init; }

    [JsonPropertyName("position")]
    public long? Position { get; init; }

    [JsonPropertyName("endTime")]
    public long? EndTime { get; init; }

    [JsonPropertyName("volume")]
    public int? Volume { get; init; }

    [JsonPropertyName("paused")]
    public bool? Paused { get; init; }

    [JsonPropertyName("filters")]
    public FilterChain? Filters { get; init; }

    [JsonPropertyName("fading")]
    public FadingOptions? Fading { get; init; }

    [JsonPropertyName("loudnessNormalizer")]
    public bool? LoudnessNormalizer { get; init; }

    [JsonPropertyName("ducking")]
    public bool? Ducking { get; init; }

    [JsonPropertyName("crossfade")]
    public CrossfadeOptions? Crossfade { get; init; }

    [JsonPropertyName("voice")]
    public PlayerVoiceState? Voice { get; init; }
}

internal sealed record EndTimeUpdate
{
    [JsonPropertyName("endTime"), JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public long? EndTime { get; init; }
}

internal sealed record NextTrackUpdate
{
    [JsonPropertyName("nextTrack"), JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public PlayerUpdateTrack? NextTrack { get; init; }
}

/// <summary>A player as returned by the audio node.</summary>
public sealed record Player
{
    /// <summary>The guild the player plays in.</summary>
    [JsonPropertyName("guildId"), JsonInclude]
    public ulong GuildId { get; internal set; }

    /// <summary>The track the player plays, null when stopped.</summary>
    [JsonPropertyName("track"), JsonInclude]
    public Track? Track { get; internal set; }

    /// <summary>The volume of the player, 100 is the default.</summary>
    [JsonPropertyName("volume"), JsonInclude]
    [JsonConverter(typeof(RoundedInt32Converter))]
    public int Volume { get; internal set; }

    /// <summary>Whether playback is paused.</summary>
    [JsonPropertyName("paused"), JsonInclude]
    public bool IsPaused { get; internal set; }

    /// <summary>Always sent by every reachable server, nullable only as tolerance for malformed responses.</summary>
    [JsonPropertyName("state"), JsonInclude]
    public PlayerState? State { get; internal set; }

    /// <summary>The voice state the server reports for the player.</summary>
    [JsonPropertyName("voice"), JsonInclude]
    public PlayerVoiceState? Voice { get; internal set; }

    /// <summary>
    /// Only the filters that were explicitly configured, with one exception, a NodeLink server retains inert
    /// filter blocks in its state, so a non-null block here is not proof the filter is audible, only that the
    /// server remembers it.
    /// </summary>
    [JsonPropertyName("filters"), JsonInclude]
    public FilterChain? Filters { get; internal set; }

    /// <summary>The player's fade configuration, NodeLink only, null on Lavalink.</summary>
    [JsonPropertyName("fading"), JsonInclude]
    public FadingOptions? Fading { get; internal set; }

    /// <summary>Whether loudness normalization is on, NodeLink only, null on Lavalink.</summary>
    [JsonPropertyName("loudnessNormalizer"), JsonInclude]
    public bool? LoudnessNormalizer { get; internal set; }

    /// <summary>The player's crossfade configuration, NodeLink only.</summary>
    [JsonPropertyName("crossfade"), JsonInclude]
    public CrossfadeOptions? Crossfade { get; internal set; }
}

/// <summary>Session resuming configuration, request body and response of a session update.</summary>
public sealed record SessionConfig
{
    /// <summary>Whether the session resumes its players after a reconnect.</summary>
    [JsonPropertyName("resuming")]
    public bool Resuming { get; init; }

    /// <summary>Seconds a disconnected resumable session is kept alive.</summary>
    [JsonPropertyName("timeout")]
    [JsonConverter(typeof(RoundedInt32Converter))]
    public int Timeout { get; init; }
}
