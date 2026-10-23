namespace Nyra.Entities.Filters;

/// <summary>Marks an audio filter applicable to a player.</summary>
/// <remarks>
/// Implementations are limited to the sealed filter records of this library, applied one at a time by <see
/// cref="GuildFilters.AddAsync"/> or as a member of a FilterChain through GuildFilters.ApplyAsync, and
/// removed by the GuildFilters.RemoveAsync overloads. A user-defined implementation is rejected with <see
/// cref="ArgumentException"/>.
/// </remarks>
public interface IFilter { }

internal interface IFiniteCheck
{
    bool HasNonFiniteNumber { get; }
}

internal static class FilterNumbers
{
    public static bool NonFinite(float? value) =>
        value.HasValue && !float.IsFinite(value.GetValueOrDefault());

    public static bool NonFinite(double? value) =>
        value.HasValue && !double.IsFinite(value.GetValueOrDefault());
}
