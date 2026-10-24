using System.Text.Json;
using System.Text.Json.Serialization;
using Nyra.Entities.Filters;

namespace Nyra.Entities;

/// <summary>Audio filter chain of a player.</summary>
/// <remarks>
/// A sent chain replaces the whole chain server-side, known filters left null are cleared, <see
/// cref="PluginFilters"/> is only sent when set.
/// </remarks>
public sealed record FilterChain
{
    /// <summary>Volume filter, 0.0-5.0, where 1.0 disables it.</summary>
    /// <remarks>
    /// Lavalink only, NodeLink accepts the member but never applies it. Independent of the player volume
    /// (0-1000), values above 1.0 may clip.
    /// </remarks>
    [JsonPropertyName("volume")]
    public float? Volume { get; init; }

    /// <summary>Equalizer bands, band ids range from 0 to 14.</summary>
    [JsonPropertyName("equalizer")]
    public IReadOnlyList<EqualizerBand>? Equalizer { get; init; }

    /// <summary>Animates equalizer changes instead of stepping them.</summary>
    /// <remarks>
    /// NodeLink only. It travels inside the bands object as "transition", Lavalink parses the bare band array
    /// and knows no transition. The chain's converter reads and writes it, so serializing the chain with other
    /// options does not emit it as a member of its own.
    /// </remarks>
    [JsonIgnore]
    public FilterTransition? EqualizerTransition { get; init; }

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

    /// <summary>The distortion filter of the chain, null when the server did not apply one.</summary>
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

    /// <summary>Repeats the audio after a delay.</summary>
    /// <remarks>NodeLink only.</remarks>
    [JsonPropertyName("echo")]
    public EchoFilter? Echo { get; init; }

    /// <summary>Simulates a room's reflections.</summary>
    /// <remarks>NodeLink only.</remarks>
    [JsonPropertyName("reverb")]
    public ReverbFilter? Reverb { get; init; }

    /// <summary>Suppresses lower frequencies.</summary>
    /// <remarks>NodeLink only.</remarks>
    [JsonPropertyName("highpass")]
    public HighPassFilter? HighPass { get; init; }

    /// <summary>Layers slightly delayed copies for a thickening effect.</summary>
    /// <remarks>NodeLink only.</remarks>
    [JsonPropertyName("chorus")]
    public ChorusFilter? Chorus { get; init; }

    /// <summary>Sweeps a notch through the spectrum for a whooshing effect.</summary>
    /// <remarks>NodeLink only.</remarks>
    [JsonPropertyName("phaser")]
    public PhaserFilter? Phaser { get; init; }

    /// <summary>Sweeps a comb delay for a jet-plane effect.</summary>
    /// <remarks>NodeLink only.</remarks>
    [JsonPropertyName("flanger")]
    public FlangerFilter? Flanger { get; init; }

    /// <summary>Places the audio at a point in space around the listener.</summary>
    /// <remarks>NodeLink only, it ignores the three position members, they round-trip through the API.</remarks>
    [JsonPropertyName("spatial")]
    public SpatialFilter? Spatial { get; init; }

    /// <summary>Squashes the dynamic range of the audio.</summary>
    /// <remarks>NodeLink only.</remarks>
    [JsonPropertyName("compressor")]
    public CompressorFilter? Compressor { get; init; }

    /// <summary>Degrades the audio like a record player.</summary>
    /// <remarks>NodeLink only.</remarks>
    [JsonPropertyName("phonograph")]
    public PhonographFilter? Phonograph { get; init; }

    /// <summary>Rotates the audio through four dimensions.</summary>
    /// <remarks>NodeLink only.</remarks>
    [JsonPropertyName("tesseract")]
    public TesseractFilter? Tesseract { get; init; }

    /// <summary>Plugin-provided filters, keyed by plugin name.</summary>
    /// <remarks>
    /// Lavalink applies them through its plugin system, NodeLink has none and echoes the block back as sent,
    /// without applying it.
    /// </remarks>
    [JsonPropertyName("pluginFilters")]
    public IReadOnlyDictionary<string, JsonElement>? PluginFilters { get; init; }

    /// <summary>Indicates whether both chains configure the same filters and plugins.</summary>
    /// <param name="other">The chain to compare with.</param>
    /// <returns>Whether every member matches, the collections compared by content.</returns>
    public bool Equals(FilterChain? other) =>
        other is not null
        && EqualityComparer<float?>.Default.Equals(Volume, other.Volume)
        && ContentEquality.ListEquals(Equalizer, other.Equalizer)
        && EqualizerTransition == other.EqualizerTransition
        && Karaoke == other.Karaoke
        && Timescale == other.Timescale
        && Tremolo == other.Tremolo
        && Vibrato == other.Vibrato
        && Distortion == other.Distortion
        && Rotation == other.Rotation
        && ChannelMix == other.ChannelMix
        && LowPass == other.LowPass
        && Echo == other.Echo
        && Reverb == other.Reverb
        && HighPass == other.HighPass
        && Chorus == other.Chorus
        && Phaser == other.Phaser
        && Flanger == other.Flanger
        && Spatial == other.Spatial
        && Compressor == other.Compressor
        && Phonograph == other.Phonograph
        && Tesseract == other.Tesseract
        && ContentEquality.DictionaryDeepEquals(PluginFilters, other.PluginFilters);

    /// <summary>Returns a hash of the chain's comparable members, the collections stay out.</summary>
    /// <returns>The hash code.</returns>
    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Volume);
        hash.Add(EqualizerTransition);
        hash.Add(Karaoke);
        hash.Add(Timescale);
        hash.Add(Tremolo);
        hash.Add(Vibrato);
        hash.Add(Distortion);
        hash.Add(Rotation);
        hash.Add(ChannelMix);
        hash.Add(LowPass);
        hash.Add(Echo);
        hash.Add(Reverb);
        hash.Add(HighPass);
        hash.Add(Chorus);
        hash.Add(Phaser);
        hash.Add(Flanger);
        hash.Add(Spatial);
        hash.Add(Compressor);
        hash.Add(Phonograph);
        hash.Add(Tesseract);
        return hash.ToHashCode();
    }
}
