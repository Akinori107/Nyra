using System.Text.Json.Serialization;

namespace Nyra.Entities.Filters;

/// <summary>Suppresses higher frequencies.</summary>
/// <remarks>
/// An unset smoothing is sent as 20, which is audible. To turn the filter off, remove it with
/// <see cref="GuildFilters.RemoveAsync(IFilter, CancellationToken)"/>. On Lavalink a smoothing of 1.0 or less
/// still runs while another filter is on.
/// </remarks>
/// <param name="Smoothing">Smoothing factor, 20 when unset. See the remarks for the values to avoid.</param>
/// <param name="TargetAlpha">
/// Direct coefficient override. NodeLink ignores it.
/// </param>
/// <param name="LogSmoothing">
/// Logarithmic smoothing. NodeLink ignores it.
/// </param>
/// <param name="Transition">Fades the change instead of switching at once, NodeLink only.</param>
public sealed record LowPassFilter(
    [property: JsonPropertyName("smoothing")] float? Smoothing = null,
    [property: JsonPropertyName("targetAlpha")] float? TargetAlpha = null,
    [property: JsonPropertyName("logSmoothing")] float? LogSmoothing = null,
    [property: JsonPropertyName("transition")] FilterTransition? Transition = null
) : IFilter, IFiniteCheck, IStripTransition<LowPassFilter>
{
    bool IFiniteCheck.HasNonFiniteNumber =>
        FilterNumbers.NonFinite(Smoothing)
        || FilterNumbers.NonFinite(TargetAlpha)
        || FilterNumbers.NonFinite(LogSmoothing);

    LowPassFilter IStripTransition<LowPassFilter>.WithoutTransition() =>
        Transition is null ? this : this with { Transition = null };
}
