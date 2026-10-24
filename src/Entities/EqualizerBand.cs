using System.Text.Json.Serialization;

namespace Nyra.Entities;

/// <summary>A single equalizer band adjustment.</summary>
/// <remarks>
/// Band ids are map keys, a chain carrying the same id twice sends both entries, the server collapses them,
/// the last one wins.
/// </remarks>
public readonly record struct EqualizerBand
{
    /// <summary>Number of equalizer bands, ids run 0 to <see cref="BandCount"/> - 1.</summary>
    public const int BandCount = 15;

    /// <summary>The band to adjust, 0 to <see cref="BandCount"/> - 1.</summary>
    [JsonPropertyName("band")]
    public int BandId { get; }

    /// <summary>The band gain, -0.25 to +1.0, 0.0 is unchanged.</summary>
    /// <remarks>
    /// Lavalink, -0.25 mutes and +0.25 doubles the band, NodeLink applies gain as a linear offset around unity
    /// instead.
    /// </remarks>
    [JsonPropertyName("gain")]
    public float Gain { get; }

    /// <summary>Creates a band adjustment.</summary>
    /// <param name="bandId">The id of the band to adjust.</param>
    /// <param name="gain">The gain to apply, from -0.25 to 1.0, 0.0 leaves the band unchanged.</param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="bandId"/> is not between 0 and <see cref="BandCount"/> - 1, or <paramref name="gain"/>
    /// is not a finite number between -0.25 and 1.0.
    /// </exception>
    [JsonConstructor]
    public EqualizerBand(int bandId, float gain)
    {
        if (bandId is < 0 or >= BandCount)
            throw new ArgumentOutOfRangeException(
                nameof(bandId),
                bandId,
                $"Band ID must be between 0 and {BandCount - 1}."
            );

        if (!float.IsFinite(gain) || gain is < -0.25f or > 1.0f)
            throw new ArgumentOutOfRangeException(
                nameof(gain),
                gain,
                "Gain must be between -0.25 and 1.0."
            );

        BandId = bandId;
        Gain = gain;
    }
}
