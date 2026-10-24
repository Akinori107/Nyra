using System.Text.Json;
using System.Text.Json.Serialization;

namespace Nyra.Entities.Filters;

/// <summary>Fades a filter in or out (a wet/dry blend) instead of stepping, NodeLink only.</summary>
/// <remarks>
/// Parameter changes animate them towards the new values on the filters whose parameters the server
/// animates, the rest accept the transition but step their parameters. Which filters animate is the
/// server's to decide per release, this library sends the transition uniformly. A removal never carries
/// a transition, it snaps.
/// </remarks>
/// <param name="DurationMilliseconds">
/// How long the transition takes, in milliseconds. There is no client-side guard, the value is the server's to
/// judge. A non-positive one makes the change snap instantly or is rejected with 400. A null still writes an
/// explicit zero, an omitted member (a third-party form) leaves the transition unable to animate, the change
/// snaps.
/// </param>
/// <param name="Curve">
/// Transition curve by name, <c>linear</c>, <c>exponential</c> or <c>sinusoidal</c>, case-sensitive. The server
/// replaces any other name with sinusoidal or rejects it with 400. A null curve is an omitted member on the
/// wire, it never travels as a JSON null, so a fresh apply lands on the server's default (sinusoidal) and a
/// server that merges the previous transition retains the earlier curve.
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
        if (reader.TokenType is JsonTokenType.Null)
            return null;

        if (reader.TokenType is JsonTokenType.String)
            return JsonNumberParsing.TryParseUtf8Double(ref reader, out var parsed)
                ? Round(parsed)
                : throw new JsonException(
                    $"Cannot read the transition duration from '{reader.GetString()}'."
                );

        if (reader.TokenType is JsonTokenType.Number)
            return Round(reader.GetDouble());

        throw new JsonException(
            $"Cannot read the transition duration from token type '{reader.TokenType}'."
        );

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

internal interface IStripTransition<out TSelf>
    where TSelf : IStripTransition<TSelf>
{
    TSelf WithoutTransition();
}
