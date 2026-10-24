namespace Nyra;

internal static class UnixTime
{
    public const long MinMilliseconds = -62_135_596_800_000;

    public const long MaxMilliseconds = 253_402_300_799_999;

    public static DateTimeOffset ToDateTimeOffset(long milliseconds) =>
        milliseconds is >= MinMilliseconds and <= MaxMilliseconds
            ? DateTimeOffset.FromUnixTimeMilliseconds(milliseconds)
            : DateTimeOffset.MinValue;
}
