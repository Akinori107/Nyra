using System.Text.Json.Serialization;

namespace Nyra.Entities.Filters;

/// <summary>Suppresses higher frequencies.</summary>
/// <remarks>
/// An unset smoothing is sent as 20 (the protocol default, an active filter).
/// <para>
/// NodeLink disables the filter at a smoothing of 1.0 or less. Lavalink removes it only when no other filter is
/// active, otherwise it still runs and only exactly 1.0 is a passthrough, lower values break the filter. Use
/// <see cref="GuildFilters.RemoveAsync(IFilter, CancellationToken)"/> to keep the filter off, never a smoothing
/// below 1.0.
/// </para>
/// </remarks>
/// <param name="Smoothing">Smoothing factor, 20 when unset. See the remarks for the values to avoid.</param>
/// <param name="TargetAlpha">
/// Direct coefficient override in the settings schema, but NodeLink ignores it (its DSP has no alpha, the
/// cutoff comes from log10(smoothing)), the server just echoes it back.
/// </param>
/// <param name="LogSmoothing">
/// Logarithmic smoothing in the settings schema, ignored like <paramref name="TargetAlpha"/>, the server just
/// echoes it back.
/// </param>
/// <param name="Transition">
/// Fades the filter in or out instead of stepping, NodeLink only. A smoothing change sent with a transition
/// animates the cutoff too.
/// </param>
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
