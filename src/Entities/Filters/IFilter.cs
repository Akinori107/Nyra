namespace Nyra.Entities.Filters;

/// <summary>Marks an audio filter applicable to a player.</summary>
/// <remarks>
/// Only the filter records of this library work, your own implementation throws
/// <see cref="ArgumentException"/>.
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
