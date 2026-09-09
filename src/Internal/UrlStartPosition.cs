using System.Globalization;

namespace Nyra.Internal;

/// <summary>
/// Parses a media link's <c>t</c>/<c>start</c> offset, since both servers discard it.
/// Handles plain seconds (<c>?t=90</c>), timecodes (<c>?t=1m30s</c>), <c>#</c>-prefixed
/// params (SoundCloud), and the legacy <c>start=</c> param.
/// </summary>
internal static class UrlStartPosition
{
    /// <summary>Milliseconds from the URL's own <c>t</c>/<c>start</c> parameter, or null.</summary>
    public static long? ParseMilliseconds(string url)
    {
        // Only the URL's own parameters count, not a t= inside another parameter's value.
        var query = url.AsSpan();
        var cut = query.IndexOfAny('?', '#');
        if (cut < 0)
            return null;
        query = query[(cut + 1)..];

        while (!query.IsEmpty)
        {
            var end = query.IndexOfAny('&', '#');
            var parameter = end >= 0 ? query[..end] : query;
            if (parameter.StartsWith("t="))
                return ParseValue(parameter[2..]);
            if (parameter.StartsWith("start="))
                return ParseValue(parameter[6..]);

            if (end < 0)
                break;
            query = query[(end + 1)..];
        }

        return null;
    }

    /// <summary>Parses <c>90</c>, <c>45s</c>, <c>1m30s</c>, <c>1h2m3s</c> into milliseconds, zero or invalid → null.</summary>
    private static long? ParseValue(ReadOnlySpan<char> value)
    {
        var end = value.IndexOfAny('&', '#');
        if (end >= 0)
            value = value[..end];
        if (value.IsEmpty || value.Length > 11)
            return null;

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
                // A bare number after a unit (1m30) isn't a real form, only allow it as the whole value.
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
