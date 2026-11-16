using System.Text.Json;
using System.Text.Json.Serialization;

namespace Nyra.Entities.Filters;

/// <summary>Fades a filter change instead of switching at once, NodeLink only.</summary>
/// <remarks>Removing a filter always switches at once.</remarks>
/// <param name="DurationMilliseconds">
/// How long the transition takes, in milliseconds. Zero or less switches at once or is rejected by the server,
/// null is sent as 0.
/// </param>
/// <param name="Curve">
/// Transition curve, <c>linear</c>, <c>exponential</c> or <c>sinusoidal</c>, case-sensitive. Null uses the
/// server default.
/// </param>
public sealed record FilterTransition(
    [property: JsonPropertyName("durationMs")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    [property: JsonConverter(typeof(TransitionDurationConverter))]
        int? DurationMilliseconds,
    [property: JsonPropertyName("curve")] string? Curve = null
);

internal sealed class TransitionDurationConverter : JsonConverter<int?>
{
    public override bool HandleNull => true;

    public override int? Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options
    )
    {
        return reader.TokenType switch
        {
            JsonTokenType.Null => null,
            JsonTokenType.String => JsonNumberParsing.TryParseUtf8Double(ref reader, out var parsed)
                ? Round(parsed)
                : throw new JsonException(
                    $"Cannot read the transition duration from '{reader.GetString()}'."
                ),
            JsonTokenType.Number => Round(reader.GetDouble()),
            _ => throw new JsonException(
                $"Cannot read the transition duration from token type '{reader.TokenType}'."
            ),
        };

        static int? Round(double value)
        {
            if (!double.IsFinite(value))
                throw new JsonException(
                    $"The transition duration must be a finite number, got {value}."
                );
            var rounded = JsonNumberParsing.RoundHalfUp(value);
            if (rounded is < -2_147_483_648.0 or > 2_147_483_647.0)
                throw new JsonException(
                    $"The transition duration {value} is outside the int32 range, the server sent garbage."
                );
            return (int)rounded;
        }
    }

    public override void Write(Utf8JsonWriter writer, int? value, JsonSerializerOptions options) =>
        writer.WriteNumberValue(value ?? 0);
}
