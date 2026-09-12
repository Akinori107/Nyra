using System.Collections.Frozen;
using System.Globalization;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using Nyra.Internal;

namespace Nyra;

/// <summary>
/// Shared <see cref="JsonSerializerOptions"/> for user payloads (snowflakes as strings,
/// protocol enums by wire name). The response converters of <see cref="Entities.FilterChain"/>
/// and <see cref="Entities.LoadResult"/> are server-kind specific and live on the per-kind
/// options returned by <see cref="OptionsFor"/>.
/// </summary>
public static class NyraJson
{
    public static JsonSerializerOptions SerializerOptions { get; } = Create();

    private static readonly JsonSerializerOptions LavalinkOptions = Create(false);
    private static readonly JsonSerializerOptions NodeLinkOptions = Create(true);

    /// <summary>The options carrying the response converters of the detected server kind.</summary>
    internal static JsonSerializerOptions OptionsFor(bool isNodeLink) =>
        isNodeLink ? NodeLinkOptions : LavalinkOptions;

    private static JsonSerializerOptions Create(bool? isNodeLink = null)
    {
        var options = new JsonSerializerOptions
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            NumberHandling = JsonNumberHandling.AllowReadingFromString,
        };

        options.Converters.Add(new SnowflakeConverterFactory());
        options.Converters.Add(new ProtocolEnumConverterFactory());

        if (isNodeLink is { } nodelink)
        {
            options.Converters.Add(new FiltersConverter(nodelink));
            options.Converters.Add(new LoadResultConverter(nodelink));
        }

        return options;
    }
}

/// <summary>Serializes <see cref="ulong"/> as JSON strings and reads both strings and numbers.</summary>
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
            {
                // Hot path of every guild id in every message, parse without allocating.
                if (
                    !reader.HasValueSequence
                    && System.Buffers.Text.Utf8Parser.TryParse(
                        reader.ValueSpan,
                        out ulong parsed,
                        out _
                    )
                )
                    return parsed;

                var value = reader.GetString();
                if (ulong.TryParse(value, CultureInfo.InvariantCulture, out var parsedString))
                    return parsedString;

                break;
            }
            case JsonTokenType.Number:
                if (reader.TryGetUInt64(out var number))
                    return number;
                break;
        }

        throw new JsonException($"Cannot read snowflake value of token type '{reader.TokenType}'.");
    }

    public override void Write(Utf8JsonWriter writer, ulong value, JsonSerializerOptions options)
    {
        // Format straight into a stack buffer: no intermediate string allocation.
        Span<char> buffer = stackalloc char[20];
        value.TryFormat(buffer, out var written, provider: CultureInfo.InvariantCulture);
        writer.WriteStringValue(buffer[..written]);
    }
}

internal sealed class NullableSnowflakeConverter : JsonConverter<ulong?>
{
    private static readonly SnowflakeConverter Snowflake = new();

    public override ulong? Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options
    ) =>
        reader.TokenType == JsonTokenType.Null
            ? null
            : Snowflake.Read(ref reader, typeToConvert, options);

    public override void Write(Utf8JsonWriter writer, ulong? value, JsonSerializerOptions options)
    {
        if (value.HasValue)
            Snowflake.Write(writer, value.Value, options);
        else
            writer.WriteNullValue();
    }
}

/// <summary>
/// Reads a value as a string whether the server sent it as a JSON string or a JSON number.
/// Unlike <see cref="SnowflakeConverter"/> this isn't registered on the shared options: it only
/// applies where a <see cref="JsonConverterAttribute"/> targets it directly on the property, since
/// every other string in the protocol really is always a string.
/// </summary>
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
            JsonTokenType.Number => reader.GetInt64().ToString(CultureInfo.InvariantCulture),
            JsonTokenType.Null => null,
            _ => throw new JsonException(
                $"Cannot read string-or-number value from token type '{reader.TokenType}'."
            ),
        };

    public override void Write(Utf8JsonWriter writer, string? value, JsonSerializerOptions options)
    {
        if (value is null)
            writer.WriteNullValue();
        else
            writer.WriteStringValue(value);
    }
}

/// <summary>
/// Converts protocol enums through their <see cref="JsonPropertyNameAttribute"/> wire names,
/// unlike <see cref="JsonStringEnumConverter"/>, so members like <c>loadFailed</c> keep
/// idiomatic C# names. Unknown wire values fall back to the <c>Unknown</c> member when the
/// enum declares one, so a newer server adding a value cannot break message dispatch.
/// </summary>
internal sealed class ProtocolEnumConverterFactory : JsonConverterFactory
{
    public override bool CanConvert(Type typeToConvert) =>
        typeToConvert.IsEnum && typeToConvert.GetCustomAttribute<JsonConverterAttribute>() is null;

    public override JsonConverter CreateConverter(
        Type typeToConvert,
        JsonSerializerOptions options
    ) =>
        (JsonConverter)
            Activator.CreateInstance(
                typeof(ProtocolEnumConverter<>).MakeGenericType(typeToConvert),
                BindingFlags.Instance | BindingFlags.NonPublic,
                binder: null,
                args: null,
                culture: null
            )!;
}

internal sealed class ProtocolEnumConverter<TEnum> : JsonConverter<TEnum>
    where TEnum : struct, Enum
{
    private readonly KeyValuePair<string, TEnum>[] _readPairs;
    private readonly FrozenDictionary<TEnum, string> _writeCache;
    private readonly TEnum? _unknownValue;

    internal ProtocolEnumConverter()
    {
        var read = new List<KeyValuePair<string, TEnum>>();
        var write = new Dictionary<TEnum, string>();
        foreach (var field in typeof(TEnum).GetFields(BindingFlags.Public | BindingFlags.Static))
        {
            var wireName =
                field.GetCustomAttribute<JsonPropertyNameAttribute>()?.Name ?? field.Name;
            var value = (TEnum)field.GetValue(null)!;
            read.Add(new KeyValuePair<string, TEnum>(wireName, value));
            write[value] = wireName;

            if (wireName == "unknown")
                _unknownValue = value;
        }

        _readPairs = [.. read];
        _writeCache = write.ToFrozenDictionary();
    }

    public override TEnum Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options
    )
    {
        if (reader.TokenType != JsonTokenType.String)
            throw new JsonException(
                $"Cannot read enum '{typeof(TEnum).Name}' from token type '{reader.TokenType}'."
            );

        // Zero-allocation scan: protocol enums have few members, so a linear ordinal compare
        // of the raw UTF-8 beats a hash lookup that would have to allocate a string first.
        foreach (var pair in _readPairs)
        {
            if (reader.ValueTextEquals(pair.Key))
                return pair.Value;
        }

        if (_unknownValue is { } unknown)
            return unknown;

        throw new JsonException($"Unknown '{typeof(TEnum).Name}' value '{reader.GetString()}'.");
    }

    public override void Write(Utf8JsonWriter writer, TEnum value, JsonSerializerOptions options) =>
        writer.WriteStringValue(_writeCache[value]);
}
