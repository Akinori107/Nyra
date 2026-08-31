using System.Text.Json.Serialization;

namespace Nyra.Entities;

/// <summary>A single equalizer band adjustment.</summary>
public readonly struct EqualizerBand
{
    /// <summary>The band to adjust, 0 to 14.</summary>
    [JsonPropertyName("band")]
    public int BandId { get; }

    /// <summary>The band gain, -0.25 (muted) to +1.0; +0.25 doubles the band.</summary>
    [JsonPropertyName("gain")]
    public float Gain { get; }

    [JsonConstructor]
    public EqualizerBand(int bandId, float gain)
    {
        if (bandId is < 0 or > 14)
            throw new ArgumentOutOfRangeException(
                nameof(bandId),
                bandId,
                "Band ID must be between 0 and 14."
            );

        if (gain is < (-0.25f) or > 1.0f)
            throw new ArgumentOutOfRangeException(
                nameof(gain),
                gain,
                "Gain must be between -0.25 and 1.0."
            );

        BandId = bandId;
        Gain = gain;
    }
}
