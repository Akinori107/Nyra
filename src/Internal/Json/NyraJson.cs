using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Nyra.Internal;

namespace Nyra;

internal static class NyraJson
{
    internal static JsonSerializerOptions SerializerOptions { get; } = Create();

    private static readonly JsonSerializerOptions LavalinkOptions = Create(false);
    private static readonly JsonSerializerOptions NodeLinkOptions = Create(true);

    internal static JsonSerializerOptions OptionsFor(bool isNodeLink) =>
        isNodeLink ? NodeLinkOptions : LavalinkOptions;

    internal static JsonSerializerOptions OptionsFor(IReadOnlySet<string> enabledFilters) =>
        Create(false, enabledFilters);

    internal static byte[] SerializeToUtf8Bytes<TValue>(
        TValue value,
        JsonSerializerOptions options
    ) => JsonSerializer.SerializeToUtf8Bytes(value, TypeInfoOf<TValue>(options));

    internal static string Serialize<TValue>(TValue value, JsonSerializerOptions options) =>
        JsonSerializer.Serialize(value, TypeInfoOf<TValue>(options));

    internal static void Serialize<TValue>(
        Utf8JsonWriter writer,
        TValue value,
        JsonSerializerOptions options
    ) => JsonSerializer.Serialize(writer, value, TypeInfoOf<TValue>(options));

    internal static TValue? Deserialize<TValue>(string json, JsonSerializerOptions options) =>
        JsonSerializer.Deserialize(json, TypeInfoOf<TValue>(options));

    internal static TValue? Deserialize<TValue>(
        ReadOnlySpan<byte> json,
        JsonSerializerOptions options
    ) => JsonSerializer.Deserialize(json, TypeInfoOf<TValue>(options));

    internal static TValue? Deserialize<TValue>(
        ref Utf8JsonReader reader,
        JsonSerializerOptions options
    ) => JsonSerializer.Deserialize(ref reader, TypeInfoOf<TValue>(options));

    private static JsonTypeInfo<TValue> TypeInfoOf<TValue>(JsonSerializerOptions options) =>
        options.GetTypeInfo(typeof(TValue)) is JsonTypeInfo<TValue> typeInfo
            ? typeInfo
            : throw new NotSupportedException(
                $"The type '{typeof(TValue)}' is not covered by the options' serialization context."
            );

    private static JsonSerializerOptions Create(
        bool? isNodeLink = null,
        IReadOnlySet<string>? enabledFilters = null
    )
    {
        var options = new JsonSerializerOptions
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            NumberHandling = JsonNumberHandling.AllowReadingFromString,
            TypeInfoResolver = NyraJsonContext.Default,
        };

        options.Converters.Add(new SnowflakeConverterFactory());
        options.Converters.Add(new ProtocolEnumConverterFactory());

        if (isNodeLink is { } nodelink)
        {
            options.Converters.Add(new FiltersConverter(nodelink, enabledFilters));
            options.Converters.Add(new LoadResultConverter(nodelink));
        }

        options.MakeReadOnly();
        return options;
    }
}

internal sealed class SnowflakeConverterFactory : JsonConverterFactory
{
    public override bool CanConvert(Type typeToConvert) =>
        typeToConvert == typeof(ulong) || typeToConvert == typeof(ulong?);

    public override JsonConverter CreateConverter(
        Type typeToConvert,
        JsonSerializerOptions options
    ) =>
        typeToConvert == typeof(ulong)
            ? new SnowflakeConverter()
            : new NullableSnowflakeConverter();
}

internal sealed class SnowflakeConverter : JsonConverter<ulong>
{
    public override ulong Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options
    )
    {
        switch (reader.TokenType)
        {
            case JsonTokenType.String:
                if (JsonNumberParsing.TryParseUtf8UInt64(ref reader, out var parsed))
                    return parsed;

                throw new JsonException(
                    "Cannot read a snowflake, the string does not parse as one."
                );
            case JsonTokenType.Number:
                if (reader.TryGetUInt64(out var number))
                    return number;

                throw new JsonException("Cannot read a snowflake, the number does not fit one.");
            case JsonTokenType.Null:
                throw new JsonException("Cannot read a snowflake, the server sent a JSON null.");
        }

        throw new JsonException($"Cannot read snowflake value of token type '{reader.TokenType}'.");
    }

    public override void Write(Utf8JsonWriter writer, ulong value, JsonSerializerOptions options)
    {
        Span<char> buffer = stackalloc char[20];
        value.TryFormat(buffer, out var written, provider: CultureInfo.InvariantCulture);
        writer.WriteStringValue(buffer[..written]);
    }
}

internal abstract class NullAsDefaultConverter<T>(JsonConverter<T> strict) : JsonConverter<T>
    where T : struct
{
    public override T Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options
    ) =>
        reader.TokenType == JsonTokenType.Null
            ? default
            : strict.Read(ref reader, typeToConvert, options);

    public override void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions options) =>
        strict.Write(writer, value, options);
}

internal abstract class NullAsNullConverter<T>(JsonConverter<T> strict) : JsonConverter<T?>
    where T : struct
{
    public override T? Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options
    ) =>
        reader.TokenType == JsonTokenType.Null
            ? null
            : strict.Read(ref reader, typeToConvert, options);

    public override void Write(Utf8JsonWriter writer, T? value, JsonSerializerOptions options)
    {
        if (value.HasValue)
            strict.Write(writer, value.Value, options);
        else
            writer.WriteNullValue();
    }
}

internal sealed class TolerantSnowflakeConverter()
    : NullAsDefaultConverter<ulong>(new SnowflakeConverter());

internal sealed class NullableSnowflakeConverter()
    : NullAsNullConverter<ulong>(new SnowflakeConverter());

internal sealed class TolerantRoundedInt32Converter()
    : NullAsDefaultConverter<int>(new RoundedInt32Converter());

internal sealed class TolerantRoundedInt64Converter()
    : NullAsDefaultConverter<long>(new RoundedInt64Converter());

internal sealed class StringOrNumberConverter : JsonConverter<string?>
{
    public override string? Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options
    ) =>
        reader.TokenType switch
        {
            JsonTokenType.String => reader.GetString(),
            JsonTokenType.Number => ReadNumberAsRawText(ref reader),
            JsonTokenType.Null => null,
            _ => throw new JsonException(
                $"Cannot read string-or-number value from token type '{reader.TokenType}'."
            ),
        };

    private static string ReadNumberAsRawText(ref Utf8JsonReader reader)
    {
        try
        {
            return reader.GetInt64().ToString(CultureInfo.InvariantCulture);
        }
        catch (FormatException)
        {
            if (!reader.HasValueSequence)
                return Encoding.UTF8.GetString(reader.ValueSpan);
            var sequence = reader.ValueSequence;
            var bytes = new byte[(int)sequence.Length];
            var remaining = bytes.AsSpan();
            foreach (var segment in sequence)
            {
                segment.Span.CopyTo(remaining);
                remaining = remaining[segment.Length..];
            }
            return Encoding.UTF8.GetString(bytes);
        }
    }

    public override void Write(Utf8JsonWriter writer, string? value, JsonSerializerOptions options)
    {
        if (value is null)
            writer.WriteNullValue();
        else
            writer.WriteStringValue(value);
    }
}

internal static class JsonNumberParsing
{
    private const NumberStyles DoubleStyles =
        NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint | NumberStyles.AllowExponent;

    public static double RoundHalfUp(double value)
    {
        var floor = Math.Floor(value);
        return value - floor >= 0.5 ? floor + 1 : floor;
    }

    public static bool TryParseUtf8UInt64(ref Utf8JsonReader reader, out ulong value) =>
        !reader.HasValueSequence && !reader.ValueIsEscaped
            ? ulong.TryParse(
                reader.ValueSpan,
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out value
            )
            : ulong.TryParse(
                reader.GetString(),
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out value
            );

    public static bool TryParseUtf8Double(ref Utf8JsonReader reader, out double value) =>
        !reader.HasValueSequence && !reader.ValueIsEscaped
            ? double.TryParse(
                reader.ValueSpan,
                DoubleStyles,
                CultureInfo.InvariantCulture,
                out value
            )
            : double.TryParse(
                reader.GetString(),
                DoubleStyles,
                CultureInfo.InvariantCulture,
                out value
            );
}

internal sealed class RoundedInt64Converter : JsonConverter<long>
{
    public override long Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options
    ) =>
        reader.TokenType switch
        {
            JsonTokenType.Number => reader.TryGetInt64(out var exact)
                ? exact
                : RoundToInt64(reader.GetDouble()),
            JsonTokenType.String
                when JsonNumberParsing.TryParseUtf8Double(ref reader, out var parsed) =>
                RoundToInt64(parsed),
            JsonTokenType.String => throw new JsonException(
                "Cannot read an integral value, the string does not parse as a number."
            ),
            _ => throw new JsonException(
                $"Cannot read integral value from token type '{reader.TokenType}'."
            ),
        };

    public override void Write(Utf8JsonWriter writer, long value, JsonSerializerOptions options) =>
        writer.WriteNumberValue(value);

    private static long RoundToInt64(double value)
    {
        var rounded = JsonNumberParsing.RoundHalfUp(value);
        if (rounded == 9_223_372_036_854_775_808.0)
            return long.MaxValue;
        if (rounded is >= -9_223_372_036_854_775_808.0 and < 9_223_372_036_854_775_808.0)
            return (long)rounded;
        throw new JsonException(
            $"Integral value {value} is outside the int64 range, the server sent garbage."
        );
    }
}

internal sealed class TolerantBooleanConverter : JsonConverter<bool>
{
    private static readonly NullableTolerantBooleanConverter Nullable = new();

    public override bool Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options
    ) => Nullable.Read(ref reader, typeToConvert, options) ?? false;

    public override void Write(Utf8JsonWriter writer, bool value, JsonSerializerOptions options) =>
        writer.WriteBooleanValue(value);
}

internal sealed class NullableTolerantStringConverter : JsonConverter<string?>
{
    public override string? Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options
    ) =>
        reader.TokenType switch
        {
            JsonTokenType.String => reader.GetString(),
            JsonTokenType.Null => null,
            _ => SkipAsStringless(ref reader),
        };

    private static string? SkipAsStringless(ref Utf8JsonReader reader)
    {
        reader.Skip();
        return null;
    }

    public override void Write(Utf8JsonWriter writer, string? value, JsonSerializerOptions options)
    {
        if (value is null)
            writer.WriteNullValue();
        else
            writer.WriteStringValue(value);
    }
}

internal sealed class NullableTolerantBooleanConverter : JsonConverter<bool?>
{
    public override bool? Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options
    ) =>
        reader.TokenType switch
        {
            JsonTokenType.True => true,
            JsonTokenType.False => false,
            JsonTokenType.Null => null,
            JsonTokenType.Number => reader.GetDouble() != 0,
            JsonTokenType.String when bool.TryParse(reader.GetString(), out var parsed) => parsed,
            _ => SkipAsUnknown(ref reader),
        };

    private static bool? SkipAsUnknown(ref Utf8JsonReader reader)
    {
        reader.Skip();
        return null;
    }

    public override void Write(Utf8JsonWriter writer, bool? value, JsonSerializerOptions options)
    {
        if (value.HasValue)
            writer.WriteBooleanValue(value.Value);
        else
            writer.WriteNullValue();
    }
}

internal sealed class RoundedInt32Converter : JsonConverter<int>
{
    public override int Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options
    ) =>
        reader.TokenType switch
        {
            JsonTokenType.Number => RoundToInt32(reader.GetDouble()),
            JsonTokenType.String
                when JsonNumberParsing.TryParseUtf8Double(ref reader, out var parsed) =>
                RoundToInt32(parsed),
            JsonTokenType.String => throw new JsonException(
                "Cannot read an integral value, the string does not parse as a number."
            ),
            _ => throw new JsonException(
                $"Cannot read integral value from token type '{reader.TokenType}'."
            ),
        };

    public override void Write(Utf8JsonWriter writer, int value, JsonSerializerOptions options) =>
        writer.WriteNumberValue(value);

    private static int RoundToInt32(double value)
    {
        var rounded = JsonNumberParsing.RoundHalfUp(value);
        if (rounded is >= -2_147_483_648.0 and <= 2_147_483_647.0)
            return (int)rounded;
        throw new JsonException(
            $"Integral value {value} is outside the int32 range, the server sent garbage."
        );
    }
}

internal sealed class ProtocolEnumConverterFactory : JsonConverterFactory
{
    public override bool CanConvert(Type typeToConvert) => WireEnumRegistry.Covers(typeToConvert);

    public override JsonConverter CreateConverter(
        Type typeToConvert,
        JsonSerializerOptions options
    ) => WireEnumRegistry.For(typeToConvert);
}

internal sealed class FiniteFloatConverter : JsonConverter<float>
{
    public override float Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options
    )
    {
        float value;
        if (reader.TokenType is JsonTokenType.Number)
            value = reader.GetSingle();
        else if (
            reader.TokenType is JsonTokenType.String
            && JsonNumberParsing.TryParseUtf8Double(ref reader, out var parsed)
        )
            value = (float)parsed;
        else
            throw new JsonException(
                $"Cannot read a finite float from token type '{reader.TokenType}'."
            );
        if (!float.IsFinite(value))
            throw new JsonException($"The value must be a finite number, got {value}.");
        return value;
    }

    public override void Write(Utf8JsonWriter writer, float value, JsonSerializerOptions options)
    {
        if (!float.IsFinite(value))
            throw new JsonException($"The value must be a finite number, got {value}.");
        writer.WriteNumberValue(value);
    }
}
