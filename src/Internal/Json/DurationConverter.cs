using System.Text.Json;
using System.Text.Json.Serialization;

namespace Nyra.Internal;

internal abstract class DurationConverter(string kind) : JsonConverter<int?>
{
    public override bool HandleNull => true;

    public override int? Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options
    ) =>
        reader.TokenType switch
        {
            JsonTokenType.Null => null,
            JsonTokenType.String => JsonNumberParsing.TryParseUtf8Double(ref reader, out var parsed)
                ? Round(parsed)
                : throw new JsonException(
                    $"Cannot read the {kind} duration from '{reader.GetString()}'."
                ),
            JsonTokenType.Number => Round(reader.GetDouble()),
            _ => throw new JsonException(
                $"Cannot read the {kind} duration from token type '{reader.TokenType}'."
            ),
        };

    protected abstract int? Round(double value);

    public override void Write(Utf8JsonWriter writer, int? value, JsonSerializerOptions options) =>
        writer.WriteNumberValue(value ?? 0);
}
