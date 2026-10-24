using System.Globalization;

namespace Nyra.Internal;

internal static class UrlStartPosition
{
    public static long? ParseMilliseconds(string url)
    {
        var query = url.AsSpan();
        var cut = query.IndexOfAny('?', '#');
        if (cut < 0)
            return null;
        query = query[(cut + 1)..];

        while (!query.IsEmpty)
        {
            var end = query.IndexOfAny('&', '#');
            var parameter = end >= 0 ? query[..end] : query;
            if (
                parameter.StartsWith("t=", StringComparison.Ordinal)
                && ParseValue(parameter[2..]) is { } tMilliseconds
            )
                return tMilliseconds;
            if (
                parameter.StartsWith("start=", StringComparison.Ordinal)
                && ParseValue(parameter[6..]) is { } startMilliseconds
            )
                return startMilliseconds;

            if (end < 0)
                break;
            query = query[(end + 1)..];
        }

        return null;
    }

    private static long? ParseValue(ReadOnlySpan<char> value)
    {
        if (value.IsEmpty || value.Length > 11)
            return null;

        var colonCount = value.Count(':');
        if (colonCount > 0)
            return ParseColonTimecode(value, colonCount);

        long seconds = 0;
        var hasUnit = false;

        while (!value.IsEmpty)
        {
            var digits = TakeDigits(value, out var rest);
            if (digits.IsEmpty)
                return null;

            var unit = rest.IsEmpty ? '\0' : rest[0];
            var factor = unit switch
            {
                'h' => 3600,
                'm' => 60,
                's' => 1,
                '\0' when !hasUnit => 1,
                _ => -1,
            };
            if (
                factor < 0
                || !long.TryParse(
                    digits,
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out var amount
                )
            )
                return null;

            seconds += amount * factor;
            hasUnit = unit != '\0';
            value = unit == '\0' ? rest : rest[1..];
        }

        return seconds > 0 && seconds <= TimeSpan.MaxValue.TotalSeconds ? seconds * 1000 : null;
    }

    private static long? ParseColonTimecode(ReadOnlySpan<char> value, int colonCount)
    {
        if (colonCount > 2)
            return null;

        long seconds = 0;
        var remaining = colonCount + 1;
        while (remaining > 0)
        {
            var cut = value.IndexOf(':');
            var segment = cut >= 0 ? value[..cut] : value;
            if (
                segment.IsEmpty
                || segment.Length > (remaining == 3 ? 3 : 2)
                || !long.TryParse(
                    segment,
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out var part
                )
            )
                return null;
            seconds = (seconds * 60) + part;
            remaining--;
            if (cut < 0)
                break;
            value = value[(cut + 1)..];
        }

        return seconds > 0 && seconds <= TimeSpan.MaxValue.TotalSeconds ? seconds * 1000 : null;
    }

    private static ReadOnlySpan<char> TakeDigits(
        ReadOnlySpan<char> value,
        out ReadOnlySpan<char> rest
    )
    {
        var length = 0;
        while (length < value.Length && char.IsAsciiDigit(value[length]))
            length++;

        rest = value[length..];
        return value[..length];
    }
}
