using System.Text.Json;
using System.Text.Json.Serialization;
using Nyra.Internal;

namespace Nyra.Entities;

/// <summary>What a fade acts on, the server default is <see cref="Volume"/>.</summary>
public enum FadeType
{
    /// <summary>Fades the playback volume.</summary>
    Volume,

    /// <summary>Simulates a tape start/stop ramp.</summary>
    Tape,

    /// <summary>Simulates a vinyl scratch ramp.</summary>
    Scratch,

    /// <summary>Both the volume fade and the tape ramp together.</summary>
    Both,

    /// <summary>A fade type the library does not know, reported by a newer server.</summary>
    /// <remarks>
    /// Sent back, a stock build ignores it and the section keeps the volume type, one running the validation
    /// patches rejects it with 400.
    /// </remarks>
    Unknown,
}

/// <summary>One fade of a <see cref="FadingOptions"/> chain.</summary>
public sealed record FadeSection
{
    /// <summary>Fade duration in milliseconds, 0 disables this fade.</summary>
    /// <remarks>
    /// A null still writes an explicit zero, an omitted member would fall into the server's own default
    /// handling instead, the explicit form is the one both behaved with.
    /// <para>
    /// A duration a co-client stored outside the int32 range clamps to the nearest bound on read, a
    /// non-finite one reads as null, the echo of another client's write must not kill the session read.
    /// </para>
    /// </remarks>
    [JsonPropertyName("duration")]
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    [JsonConverter(typeof(FadeDurationConverter))]
    public int? DurationMilliseconds { get; init; }

    /// <summary>Fade curve by name, case-sensitive.</summary>
    /// <remarks>
    /// Allowed curves per type: volume takes linear, exponential, logarithmic or s-curve, tape takes linear,
    /// exponential or sinusoidal, and both takes only the shared linear and exponential.
    /// <para>
    /// Scratch reuses this field as a style instead: wash, backspin, baby, start, stop or random on
    /// trackStart, and wash, backspin, baby or stop on the other running fades. Seek and resume always run
    /// start and never read this field.
    /// </para>
    /// <para>
    /// A null curve is stored as "linear". For scratch that runs as a plain ramp on trackStart and as wash on
    /// the other stages, and an empty string reaches the random default. The explicit linear is written
    /// because an omitted member would fall into the server's own default instead.
    /// </para>
    /// </remarks>
    [JsonPropertyName("curve")]
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    [JsonConverter(typeof(FadeCurveConverter))]
    public string? Curve { get; init; }

    /// <summary>What the fade changes, null fades the volume.</summary>
    /// <remarks>
    /// A null still writes an explicit volume, an omitted member would fall into the server's own default and
    /// merge.
    /// </remarks>
    [JsonPropertyName("type")]
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    [JsonConverter(typeof(FadeTypeConverter))]
    public FadeType? Type { get; init; }
}

/// <summary>Depth and timing of speaking-detection ducking, part of <see cref="FadingOptions"/>.</summary>
/// <remarks>
/// Configuring this does not by itself enable ducking. The switch is <see cref="GuildEffects.SetDuckingAsync"/>,
/// which picks up these values, and <see cref="Enabled"/> only travels with the stored configuration.
/// NodeLink only.
/// <para>
/// NodeLink 3.9.0 answers the switch itself with a 500. The stored configuration survives the failed write.
/// </para>
/// </remarks>
public sealed record DuckingOptions
{
    /// <summary>Whether ducking lowers the volume of the main track.</summary>
    [JsonPropertyName("enabled")]
    public bool? Enabled { get; init; }

    /// <summary>How long the ducking takes, in milliseconds, the server default is 500.</summary>
    [JsonPropertyName("duration")]
    [JsonConverter(typeof(RoundedInt32Converter))]
    public int? DurationMilliseconds { get; init; }

    /// <summary>Volume to duck to, 0-1, the server default is 0.3.</summary>
    /// <remarks>The converter fails an out-of-range echo inside the read (1e40 reads back non-finite).</remarks>
    [JsonPropertyName("targetVolume")]
    [JsonConverter(typeof(FiniteFloatConverter))]
    public float? TargetVolume { get; init; }

    /// <summary>Ducking curve by name, case-sensitive, linear, exponential, logarithmic or s-curve.</summary>
    /// <remarks>Null uses the engine default.</remarks>
    [JsonPropertyName("curve")]
    public string? Curve { get; init; }
}

/// <summary>Fades around the player lifecycle, applied with <see cref="GuildEffects.SetFadingAsync"/>.</summary>
/// <remarks>
/// A sent chain replaces the whole configuration, sections left null fall back to no fade, same as <see
/// cref="FilterChain"/>. NodeLink only.
/// </remarks>
public sealed record FadingOptions
{
    /// <summary>Whether the fade chain is active.</summary>
    /// <remarks>
    /// Left null, the server stores the sections but keeps the chain disabled, only trackEnd still runs
    /// (scheduled before the enabled check), everything else is silently ignored. Set true to activate.
    /// </remarks>
    [JsonPropertyName("enabled")]
    public bool? Enabled { get; init; }

    /// <summary>The fade applied when a track starts.</summary>
    [JsonPropertyName("trackStart")]
    public FadeSection? TrackStart { get; init; }

    /// <summary>The fade applied when a track ends.</summary>
    [JsonPropertyName("trackEnd")]
    public FadeSection? TrackEnd { get; init; }

    /// <summary>The fade applied when the track is stopped.</summary>
    [JsonPropertyName("trackStop")]
    public FadeSection? TrackStop { get; init; }

    /// <summary>The fade applied when the position is sought.</summary>
    [JsonPropertyName("seek")]
    public FadeSection? Seek { get; init; }

    /// <summary>The fade applied when playback pauses.</summary>
    [JsonPropertyName("pause")]
    public FadeSection? Pause { get; init; }

    /// <summary>The fade applied when playback resumes.</summary>
    [JsonPropertyName("resume")]
    public FadeSection? Resume { get; init; }

    /// <summary>Speaking-detection ducking.</summary>
    /// <remarks>NodeLink only.</remarks>
    [JsonPropertyName("ducking")]
    public DuckingOptions? Ducking { get; init; }
}

internal sealed class FadeDurationConverter : JsonConverter<int?>
{
    public override bool HandleNull => true;

    public override int? Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options
    )
    {
        if (reader.TokenType is JsonTokenType.Null)
            return null;

        if (reader.TokenType is JsonTokenType.String)
            return JsonNumberParsing.TryParseUtf8Double(ref reader, out var parsed)
                ? Round(parsed)
                : throw new JsonException(
                    $"Cannot read the fade duration from '{reader.GetString()}'."
                );

        if (reader.TokenType is JsonTokenType.Number)
            return Round(reader.GetDouble());

        throw new JsonException(
            $"Cannot read the fade duration from token type '{reader.TokenType}'."
        );

        static int? Round(double value)
        {
            if (!double.IsFinite(value))
                return null;
            var rounded = JsonNumberParsing.RoundHalfUp(value);
            return rounded < -2_147_483_648.0 ? int.MinValue
                : rounded > 2_147_483_647.0 ? int.MaxValue
                : (int)rounded;
        }
    }

    public override void Write(Utf8JsonWriter writer, int? value, JsonSerializerOptions options) =>
        writer.WriteNumberValue(value ?? 0);
}

internal sealed class FadeCurveConverter : JsonConverter<string?>
{
    public override bool HandleNull => true;

    public override string? Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options
    ) =>
        reader.TokenType switch
        {
            JsonTokenType.String => reader.GetString(),
            JsonTokenType.Null => null,
            _ => throw new JsonException(
                $"Cannot read the fade curve from token type '{reader.TokenType}'."
            ),
        };

    public override void Write(
        Utf8JsonWriter writer,
        string? value,
        JsonSerializerOptions options
    ) => writer.WriteStringValue(value ?? "linear");
}

internal sealed class FadeTypeConverter : JsonConverter<FadeType?>
{
    private static readonly WireEnumConverter<FadeTypeWire, FadeType> Wire = new();

    public override bool HandleNull => true;

    public override FadeType? Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options
    ) =>
        reader.TokenType is JsonTokenType.Null
            ? null
            : Wire.Read(ref reader, typeof(FadeType), options);

    public override void Write(
        Utf8JsonWriter writer,
        FadeType? value,
        JsonSerializerOptions options
    )
    {
        if (value is { } type)
            Wire.Write(writer, type, options);
        else
            writer.WriteStringValue("volume");
    }
}
