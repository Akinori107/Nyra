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

internal sealed class TransitionDurationConverter() : Internal.DurationConverter("transition")
{
    protected override int? Round(double value)
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
