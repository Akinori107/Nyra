namespace Nyra;

/// <summary>Unix epoch millisecond bounds of the <see cref="DateTimeOffset"/> range, shared by the guards that convert epoch milliseconds to dates.</summary>
internal static class UnixTime
{
    /// <summary>Unix epoch milliseconds of <see cref="DateTimeOffset.MinValue"/>.</summary>
    public const long MinMilliseconds = -62_135_596_800_000;

    /// <summary>Unix epoch milliseconds of <see cref="DateTimeOffset.MaxValue"/>.</summary>
    public const long MaxMilliseconds = 253_402_300_799_999;
}
