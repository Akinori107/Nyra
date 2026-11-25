using System.Numerics;

namespace Nyra.Entities.Filters;

/// <summary>Marks an audio filter applicable to a player.</summary>
/// <remarks>
/// Only the filter records of this library work, your own implementation throws
/// <see cref="ArgumentException"/>.
/// </remarks>
public interface IFilter;

internal interface IFiniteCheck
{
    bool HasNonFiniteNumber { get; }
}

internal static class FilterNumbers
{
    public static bool NonFinite<T>(T? value)
        where T : struct, IFloatingPointIeee754<T> =>
        value.HasValue && !T.IsFinite(value.GetValueOrDefault());
}
