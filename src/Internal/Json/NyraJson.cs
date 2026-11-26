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

    internal static JsonSerializerOptions ComparisonOptions { get; } =
        Create(false, forComparison: true);

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

#if NET11_0_OR_GREATER
    private static JsonTypeInfo<TValue> TypeInfoOf<TValue>(JsonSerializerOptions options) =>
        options.GetTypeInfo<TValue>();
#else
    private static JsonTypeInfo<TValue> TypeInfoOf<TValue>(JsonSerializerOptions options) =>
        options.GetTypeInfo(typeof(TValue)) as JsonTypeInfo<TValue>
        ?? throw new NotSupportedException(
            $"The type '{typeof(TValue)}' is not covered by the options' serialization context."
        );
#endif

    private static JsonSerializerOptions Create(
        bool? isNodeLink = null,
        IReadOnlySet<string>? enabledFilters = null,
        bool forComparison = false
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
            options.Converters.Add(new FiltersConverter(nodelink, enabledFilters, forComparison));
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
        if (TryRead(ref reader, out var value))
            return value;

        throw new JsonException(
            reader.TokenType switch
            {
                JsonTokenType.String =>
                    "Cannot read a snowflake, the string does not parse as one.",
                JsonTokenType.Number => "Cannot read a snowflake, the number does not fit one.",
                JsonTokenType.Null => "Cannot read a snowflake, the server sent a JSON null.",
                _ => $"Cannot read snowflake value of token type '{reader.TokenType}'.",
            }
        );
    }

    internal static bool TryRead(ref Utf8JsonReader reader, out ulong value)
    {
        value = default;
        return reader.TokenType switch
        {
            JsonTokenType.String => JsonNumberParsing.TryParseUtf8UInt64(ref reader, out value),
            JsonTokenType.Number => reader.TryGetUInt64(out value),
            _ => false,
        };
    }

    public override void Write(Utf8JsonWriter writer, ulong value, JsonSerializerOptions options) =>
        WriteValue(writer, value);

    internal static void WriteValue(Utf8JsonWriter writer, ulong value)
    {
        Span<char> buffer = stackalloc char[20];
        value.TryFormat(buffer, out var written, provider: CultureInfo.InvariantCulture);
        writer.WriteStringValue(buffer[..written]);
    }
}

internal sealed class NullableSnowflakeConverter : JsonConverter<ulong?>
{
    private static readonly SnowflakeConverter Strict = new();

    public override ulong? Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options
    ) =>
        reader.TokenType == JsonTokenType.Null
            ? null
            : Strict.Read(ref reader, typeToConvert, options);

    public override void Write(Utf8JsonWriter writer, ulong? value, JsonSerializerOptions options)
    {
        if (value is { } id)
            SnowflakeConverter.WriteValue(writer, id);
        else
            writer.WriteNullValue();
    }
}

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
            _ => throw new JsonException(
                $"Cannot read string-or-number value from token type '{reader.TokenType}'."
            ),
        };

    private static string ReadNumberAsRawText(ref Utf8JsonReader reader) =>
        reader.TryGetInt64(out var value) ? value.ToString(CultureInfo.InvariantCulture)
        : reader.HasValueSequence ? Encoding.UTF8.GetString(reader.ValueSequence)
        : Encoding.UTF8.GetString(reader.ValueSpan);

    public override void Write(
        Utf8JsonWriter writer,
        string? value,
        JsonSerializerOptions options
    ) => writer.WriteStringValue(value);
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
        return rounded switch
        {
            9_223_372_036_854_775_808.0 => long.MaxValue,
            >= -9_223_372_036_854_775_808.0 and < 9_223_372_036_854_775_808.0 => (long)rounded,
            _ => throw new JsonException(
                $"Integral value {value} is outside the int64 range, the server sent garbage."
            ),
        };
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
        var value = reader.TokenType switch
        {
            JsonTokenType.Number => reader.GetSingle(),
            JsonTokenType.String
                when JsonNumberParsing.TryParseUtf8Double(ref reader, out var parsed) =>
                (float)parsed,
            _ => throw new JsonException(
                $"Cannot read a finite float from token type '{reader.TokenType}'."
            ),
        };
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
