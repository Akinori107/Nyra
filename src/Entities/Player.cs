using System.Text.Json;
using System.Text.Json.Serialization;
using Nyra.Entities.Filters;

namespace Nyra.Entities;

/// <summary>
/// Discord voice state of a player. When sent, <see cref="Token"/>, <see cref="Endpoint"/>,
/// <see cref="SessionId"/> and <see cref="ChannelId"/> must be provided together: Lavalink
/// rejects the update without a channel id, NodeLink only requires the first three.
/// </summary>
public sealed record VoiceState
{
    [JsonPropertyName("token")]
    public string? Token { get; init; }

    [JsonPropertyName("endpoint")]
    public string? Endpoint { get; init; }

    [JsonPropertyName("sessionId")]
    public string? SessionId { get; init; }

    /// <summary>Sent by Lavalink v4.2.0+ and NodeLink, older servers omit it.</summary>
    [JsonPropertyName("channelId")]
    public ulong? ChannelId { get; init; }
}

/// <summary>Player state, delivered through <c>playerUpdate</c> operations and player REST responses.</summary>
public sealed record PlayerState
{
    /// <summary>Unix timestamp, in milliseconds.</summary>
    [JsonPropertyName("time")]
    public long TimeMilliseconds { get; init; }

    [JsonIgnore]
    public DateTimeOffset Time =>
        TimeMilliseconds is >= MinUnixMilliseconds and <= MaxUnixMilliseconds
            ? DateTimeOffset.FromUnixTimeMilliseconds(TimeMilliseconds)
            : DateTimeOffset.MinValue;

    /// <summary>Unix epoch milliseconds of <see cref="DateTimeOffset.MinValue"/>.</summary>
    private const long MinUnixMilliseconds = -62_135_596_800_000;

    /// <summary>Unix epoch milliseconds of <see cref="DateTimeOffset.MaxValue"/>.</summary>
    private const long MaxUnixMilliseconds = 253_402_300_799_999;

    /// <summary>Playback position in milliseconds, 0 when nothing is playing.</summary>
    [JsonPropertyName("position")]
    public long PositionMilliseconds { get; init; }

    [JsonIgnore]
    public TimeSpan Position =>
        PositionMilliseconds is >= 0 and <= TrackInfo.MaxTimeSpanMilliseconds
            ? TimeSpan.FromMilliseconds(PositionMilliseconds)
            : TimeSpan.Zero;

    /// <summary>Whether the node is connected to the Discord voice gateway.</summary>
    [JsonPropertyName("connected")]
    public bool Connected { get; init; }

    /// <summary>Voice gateway latency in milliseconds. Lavalink reports -1 and NodeLink 0 when not connected.</summary>
    [JsonPropertyName("ping")]
    public long Ping { get; init; } = -1;
}

/// <summary>
/// Audio filter chain of a player. A sent chain replaces the whole chain server-side: known
/// filters left null are cleared, while <see cref="PluginFilters"/> is only sent when set.
/// </summary>
public sealed record FilterChain
{
    /// <summary>Volume filter, 0.0-5.0, where 1.0 disables it. Independent of the player volume (0-1000), values above 1.0 may clip. Lavalink only, NodeLink accepts the member but never applies it.</summary>
    [JsonPropertyName("volume")]
    public float? Volume { get; init; }

    /// <summary>Equalizer bands, band ids range from 0 to 14.</summary>
    [JsonPropertyName("equalizer")]
    public IReadOnlyList<EqualizerBand>? Equalizer { get; init; }

    /// <summary>Eliminates part of a band, usually vocals.</summary>
    [JsonPropertyName("karaoke")]
    public KaraokeFilter? Karaoke { get; init; }

    /// <summary>Changes speed, pitch and rate.</summary>
    [JsonPropertyName("timescale")]
    public TimescaleFilter? Timescale { get; init; }

    /// <summary>Quickly oscillates the volume.</summary>
    [JsonPropertyName("tremolo")]
    public TremoloFilter? Tremolo { get; init; }

    /// <summary>Quickly oscillates the pitch.</summary>
    [JsonPropertyName("vibrato")]
    public VibratoFilter? Vibrato { get; init; }

    [JsonPropertyName("distortion")]
    public DistortionFilter? Distortion { get; init; }

    /// <summary>Rotates the audio around the stereo channels.</summary>
    [JsonPropertyName("rotation")]
    public RotationFilter? Rotation { get; init; }

    /// <summary>Mixes both channels with a configurable factor.</summary>
    [JsonPropertyName("channelMix")]
    public ChannelMixFilter? ChannelMix { get; init; }

    /// <summary>Suppresses higher frequencies.</summary>
    [JsonPropertyName("lowPass")]
    public LowPassFilter? LowPass { get; init; }

    /// <summary>Repeats the audio after a delay. NodeLink only.</summary>
    [JsonPropertyName("echo")]
    public EchoFilter? Echo { get; init; }

    /// <summary>Simulates a room's reflections. NodeLink only.</summary>
    [JsonPropertyName("reverb")]
    public ReverbFilter? Reverb { get; init; }

    /// <summary>Suppresses lower frequencies. NodeLink only.</summary>
    [JsonPropertyName("highpass")]
    public HighPassFilter? HighPass { get; init; }

    /// <summary>Layers slightly delayed copies for a thickening effect. NodeLink only.</summary>
    [JsonPropertyName("chorus")]
    public ChorusFilter? Chorus { get; init; }

    /// <summary>Sweeps a notch through the spectrum for a whooshing effect. NodeLink only.</summary>
    [JsonPropertyName("phaser")]
    public PhaserFilter? Phaser { get; init; }

    /// <summary>Sweeps a comb delay for a jet-plane effect. NodeLink only.</summary>
    [JsonPropertyName("flanger")]
    public FlangerFilter? Flanger { get; init; }

    /// <summary>Places the audio at a point in space around the listener. NodeLink only.</summary>
    [JsonPropertyName("spatial")]
    public SpatialFilter? Spatial { get; init; }

    /// <summary>Squashes the dynamic range of the audio. NodeLink only.</summary>
    [JsonPropertyName("compressor")]
    public CompressorFilter? Compressor { get; init; }

    /// <summary>Degrades the audio like a record player. NodeLink only.</summary>
    [JsonPropertyName("phonograph")]
    public PhonographFilter? Phonograph { get; init; }

    /// <summary>Rotates the audio through four dimensions. NodeLink 3.9+.</summary>
    [JsonPropertyName("tesseract")]
    public TesseractFilter? Tesseract { get; init; }

    /// <summary>Plugin-provided filters, keyed by plugin name.</summary>
    [JsonPropertyName("pluginFilters")]
    public Dictionary<string, JsonElement>? PluginFilters { get; init; }
}

/// <summary>Base of the track portion of an update-player request, the derived records pick the wire form.</summary>
[JsonDerivedType(typeof(PlayerUpdateTrack))]
[JsonDerivedType(typeof(PlayerIdentifierUpdateTrack))]
internal abstract record PlayerUpdateTrackBase
{
    /// <summary>Client data the server attaches to the track and echoes back.</summary>
    [JsonPropertyName("userData")]
    public JsonElement? UserData { get; init; }
}

/// <summary>Track portion of an update-player request: a track string plays it, null stops playback.</summary>
internal sealed record PlayerUpdateTrack : PlayerUpdateTrackBase
{
    // The null condition must never be ignored: encoded:null is how playback is stopped.
    [JsonPropertyName("encoded"), JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public string? Encoded { get; init; }

    /// <summary>Selects a dub or alternate audio track on sources that expose more than one, NodeLink only.</summary>
    [JsonPropertyName("audioTrackId")]
    public string? AudioTrackId { get; init; }
}

/// <summary>
/// Identifier form of the track portion: the server resolves and plays the identifier itself.
/// The encoded member is absent on purpose: Lavalink rejects a payload carrying both, and
/// NodeLink silently prefers encoded over identifier when both are present. Identifier loading
/// is Lavalink-only, so this form never carries <c>audioTrackId</c>: that member is NodeLink-only.
/// </summary>
internal sealed record PlayerIdentifierUpdateTrack : PlayerUpdateTrackBase
{
    [JsonPropertyName("identifier")]
    public string Identifier { get; init; } = string.Empty;
}

/// <summary>
/// Update-player request body, omitted members leave the player untouched. Uses the
/// <c>track</c> member, the only form both servers accept.
/// </summary>
internal sealed record PlayerUpdatePayload
{
    /// <summary>Omitted leaves the current track playing.</summary>
    [JsonPropertyName("track")]
    public PlayerUpdateTrackBase? Track { get; init; }

    /// <summary>Seek position in milliseconds, with a new track, starts it at that offset.</summary>
    [JsonPropertyName("position")]
    public long? Position { get; init; }

    /// <summary>End time marker in milliseconds, must be greater than zero.</summary>
    [JsonPropertyName("endTime")]
    public long? EndTime { get; init; }

    /// <summary>0-1000, where 100 is the default.</summary>
    [JsonPropertyName("volume")]
    public int? Volume { get; init; }

    [JsonPropertyName("paused")]
    public bool? Paused { get; init; }

    /// <summary>Replaces all previously applied filters.</summary>
    [JsonPropertyName("filters")]
    public FilterChain? Filters { get; init; }

    /// <summary>Replaces the whole fade configuration around the player lifecycle. NodeLink only.</summary>
    [JsonPropertyName("fading")]
    public FadingOptions? Fading { get; init; }

    /// <summary>Turns loudness normalization on or off. NodeLink only.</summary>
    [JsonPropertyName("loudnessNormalizer")]
    public bool? LoudnessNormalizer { get; init; }

    /// <summary>
    /// Turns speaking-detection ducking on or off, the duck depth and timing are set through
    /// <see cref="FadingOptions.Ducking"/> instead. NodeLink 3.9+.
    /// </summary>
    [JsonPropertyName("ducking")]
    public bool? Ducking { get; init; }

    /// <summary>Replaces the whole crossfade configuration between consecutive tracks. NodeLink 3.9+.</summary>
    [JsonPropertyName("crossfade")]
    public CrossfadeOptions? Crossfade { get; init; }

    [JsonPropertyName("voice")]
    public VoiceState? Voice { get; init; }
}

/// <summary>End-time-only update body, a null member clears the end time.</summary>
internal sealed record EndTimeUpdate
{
    // Null must reach the wire: it is how the end time is cleared.
    [JsonPropertyName("endTime"), JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public long? EndTime { get; init; }
}

/// <summary>Next-track-only update body, a null member clears the preloaded track. NodeLink only.</summary>
internal sealed record NextTrackUpdate
{
    // Null must reach the wire: it is how the preload is cleared.
    [JsonPropertyName("nextTrack"), JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public PlayerUpdateTrack? NextTrack { get; init; }
}

/// <summary>A player as returned by the audio node.</summary>
public sealed record Player
{
    [JsonPropertyName("guildId")]
    public ulong GuildId { get; init; }

    [JsonPropertyName("track")]
    public Track? Track { get; init; }

    [JsonPropertyName("volume")]
    public int Volume { get; init; }

    [JsonPropertyName("paused")]
    public bool IsPaused { get; init; }

    /// <summary>Absent in update-player responses from older servers.</summary>
    [JsonPropertyName("state")]
    public PlayerState? State { get; init; }

    [JsonPropertyName("voice")]
    public VoiceState? Voice { get; init; }

    /// <summary>Only the filters that were explicitly configured.</summary>
    [JsonPropertyName("filters")]
    public FilterChain? Filters { get; init; }

    /// <summary>The player's fade configuration, NodeLink only, null on Lavalink.</summary>
    [JsonPropertyName("fading")]
    public FadingOptions? Fading { get; init; }

    /// <summary>Whether loudness normalization is on, NodeLink only, null on Lavalink.</summary>
    [JsonPropertyName("loudnessNormalizer")]
    public bool? LoudnessNormalizer { get; init; }

    /// <summary>The player's crossfade configuration, NodeLink 3.9+, null on Lavalink and older NodeLink.</summary>
    [JsonPropertyName("crossfade")]
    public CrossfadeOptions? Crossfade { get; init; }
}

/// <summary>Session resuming configuration, request body and response of a session update.</summary>
public sealed record Session
{
    [JsonPropertyName("resuming")]
    public bool Resuming { get; init; }

    /// <summary>Seconds a disconnected resumable session is kept alive.</summary>
    [JsonPropertyName("timeout")]
    public int Timeout { get; init; }
}

/// <summary>
/// Options of a playback request issued through <see cref="GuildConnection.PlayAsync"/> or
/// <see cref="GuildConnection.PlayIdentifierAsync"/>.
/// </summary>
/// <param name="StartPosition">Where to start the track.</param>
/// <param name="EndTime">Playback stops when the track reaches this position.</param>
/// <param name="NoReplace">When true, does not replace a track that is already playing.</param>
/// <param name="UserData">Client data the server attaches to the track and echoes back on every response.</param>
/// <param name="AudioTrackId">
/// Selects a dub or alternate audio track on sources that expose more than one, NodeLink only.
/// Not usable with <see cref="GuildConnection.PlayIdentifierAsync"/>, which is Lavalink-only.
/// </param>
public sealed record PlayOptions(
    TimeSpan? StartPosition = null,
    TimeSpan? EndTime = null,
    bool NoReplace = false,
    JsonElement? UserData = null,
    string? AudioTrackId = null
);
