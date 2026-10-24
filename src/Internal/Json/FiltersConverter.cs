using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Serialization;
using Nyra.Entities;
using Nyra.Entities.Filters;

namespace Nyra.Internal;

internal sealed class FiltersConverter(bool isNodeLink, IReadOnlySet<string>? enabledFilters = null)
    : JsonConverter<FilterChain>
{
    public override FilterChain Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options
    )
    {
        if (reader.TokenType != JsonTokenType.StartObject)
            throw new JsonException("Filters payload must be an object.");

        if (isNodeLink)
        {
            var root = reader;
            if (!reader.Read() || reader.TokenType == JsonTokenType.EndObject)
                return ReadMembers(ref root, options);

            if (reader.ValueTextEquals("filters"u8))
            {
                reader.Read();
                if (reader.TokenType == JsonTokenType.StartObject)
                {
                    var chain = ReadMembers(ref reader, options);
                    while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
                        reader.Skip();
                    return chain;
                }

                reader.Skip();
                if (!reader.Read() || reader.TokenType == JsonTokenType.EndObject)
                    return ReadMembers(ref root, options);
            }

            return ReadMembers(ref reader, options, atFirstMemberName: true);
        }

        return ReadMembers(ref reader, options);
    }

    public override void Write(
        Utf8JsonWriter writer,
        FilterChain value,
        JsonSerializerOptions options
    )
    {
        writer.WriteStartObject();

        if (ShouldWrite("volume"))
        {
            writer.WritePropertyName("volume");
            writer.WriteNumberValue(value.Volume ?? 1.0f);
        }

        if (ShouldWrite("equalizer"))
        {
            writer.WritePropertyName("equalizer");
            if (isNodeLink && value.EqualizerTransition is { } equalizerTransition)
            {
                writer.WriteStartObject();
                writer.WritePropertyName("bands");
                WriteBands(writer, value.Equalizer, options);
                writer.WritePropertyName("transition");
                NyraJson.Serialize(writer, equalizerTransition, options);
                writer.WriteEndObject();
            }
            else
                WriteBands(writer, value.Equalizer, options);
        }

        if (BeginFilter(writer, "karaoke", value.Karaoke, out var karaoke))
        {
            WriteMember(writer, "level", karaoke.Level, 1.0f);
            WriteMember(writer, "monoLevel", karaoke.MonoLevel, 1.0f);
            WriteMember(writer, "filterBand", karaoke.FilterBand, 220.0f);
            WriteMember(writer, "filterWidth", karaoke.FilterWidth, 100.0f);
            EndFilter(writer, karaoke.Transition, options);
        }

        if (BeginFilter(writer, "timescale", value.Timescale, out var timescale))
        {
            WriteMember(writer, "speed", timescale.Speed, 1.0);
            WriteMember(writer, "pitch", timescale.Pitch, 1.0);
            WriteMember(writer, "rate", timescale.Rate, 1.0);
            EndFilter(writer, timescale.Transition, options);
        }

        if (BeginFilter(writer, "tremolo", value.Tremolo, out var tremolo))
        {
            WriteMember(writer, "frequency", tremolo.Frequency, 2.0f);
            WriteMember(writer, "depth", tremolo.Depth, 0.5f);
            EndFilter(writer, tremolo.Transition, options);
        }

        if (BeginFilter(writer, "vibrato", value.Vibrato, out var vibrato))
        {
            WriteMember(writer, "frequency", vibrato.Frequency, 2.0f);
            WriteMember(writer, "depth", vibrato.Depth, 0.5f);
            EndFilter(writer, vibrato.Transition, options);
        }

        if (BeginFilter(writer, "distortion", value.Distortion, out var distortion))
        {
            WriteMember(writer, "sinOffset", distortion.SinOffset, 0.0f);
            WriteMember(writer, "sinScale", distortion.SinScale, 1.0f);
            WriteMember(writer, "cosOffset", distortion.CosOffset, 0.0f);
            WriteMember(writer, "cosScale", distortion.CosScale, 1.0f);
            WriteMember(writer, "tanOffset", distortion.TanOffset, 0.0f);
            WriteMember(writer, "tanScale", distortion.TanScale, 1.0f);
            WriteMember(writer, "offset", distortion.Offset, 0.0f);
            WriteMember(writer, "scale", distortion.Scale, 1.0f);
            EndFilter(writer, distortion.Transition, options);
        }

        if (BeginFilter(writer, "rotation", value.Rotation, out var rotation))
        {
            WriteMember(writer, "rotationHz", rotation.RotationHz, 0.0);
            EndFilter(writer, rotation.Transition, options);
        }

        if (BeginFilter(writer, "channelMix", value.ChannelMix, out var channelMix))
        {
            WriteMember(writer, "leftToLeft", channelMix.LeftToLeft, 1.0f);
            WriteMember(writer, "leftToRight", channelMix.LeftToRight, 0.0f);
            WriteMember(writer, "rightToLeft", channelMix.RightToLeft, 0.0f);
            WriteMember(writer, "rightToRight", channelMix.RightToRight, 1.0f);
            EndFilter(writer, channelMix.Transition, options);
        }

        if (BeginFilter(writer, "lowPass", value.LowPass, out var lowPass))
        {
            WriteMember(writer, "smoothing", lowPass.Smoothing, 20.0f);
            WriteMember(writer, "targetAlpha", lowPass.TargetAlpha, 0.0f);
            WriteMember(writer, "logSmoothing", lowPass.LogSmoothing, 0.0f);
            EndFilter(writer, lowPass.Transition, options);
        }

        if (BeginFilter(writer, "echo", value.Echo, out var echo))
        {
            WriteMember(writer, "delay", echo.DelayMilliseconds, 0.0f);
            WriteMember(writer, "feedback", echo.Feedback, 0.0f);
            WriteMember(writer, "mix", echo.Mix, 0.0f);
            EndFilter(writer, echo.Transition, options);
        }

        if (BeginFilter(writer, "reverb", value.Reverb, out var reverb))
        {
            WriteMember(writer, "roomSize", reverb.RoomSize, 0.5f);
            WriteMember(writer, "damping", reverb.Damping, 0.5f);
            WriteMember(writer, "wetLevel", reverb.WetLevel, 0.5f);
            WriteMember(writer, "dryLevel", reverb.DryLevel, 0.5f);
            WriteMember(writer, "width", reverb.Width, 1.0f);
            WriteMember(writer, "mix", reverb.Mix, 0.5f);
            EndFilter(writer, reverb.Transition, options);
        }

        if (BeginFilter(writer, "highpass", value.HighPass, out var highPass))
        {
            WriteMember(writer, "smoothing", highPass.Smoothing, 0.0f);
            WriteMember(writer, "targetAlpha", highPass.TargetAlpha, 0.0f);
            EndFilter(writer, highPass.Transition, options);
        }

        if (BeginFilter(writer, "chorus", value.Chorus, out var chorus))
        {
            WriteMember(writer, "rate", chorus.Rate, 0.0f);
            WriteMember(writer, "depth", chorus.Depth, 0.0f);
            WriteMember(writer, "feedback", chorus.Feedback, 0.0f);
            WriteMember(writer, "delay", chorus.DelayMilliseconds, 25.0f);
            WriteMember(writer, "mix", chorus.Mix, 0.5f);
            EndFilter(writer, chorus.Transition, options);
        }

        if (BeginFilter(writer, "phaser", value.Phaser, out var phaser))
        {
            WriteMember(writer, "rate", phaser.Rate, 0.0f);
            WriteMember(writer, "depth", phaser.Depth, 1.0f);
            WriteMember(writer, "feedback", phaser.Feedback, 0.0f);
            WriteMember(writer, "delay", phaser.DelayMilliseconds, 0.0f);
            WriteMember(writer, "mix", phaser.Mix, 0.5f);
            WriteMember(writer, "stages", phaser.Stages, 4);
            WriteMember(writer, "minFrequency", phaser.MinFrequency, 100.0f);
            WriteMember(writer, "maxFrequency", phaser.MaxFrequency, 2500.0f);
            EndFilter(writer, phaser.Transition, options);
        }

        if (BeginFilter(writer, "flanger", value.Flanger, out var flanger))
        {
            WriteMember(writer, "rate", flanger.Rate, 0.0f);
            WriteMember(writer, "depth", flanger.Depth, 0.0f);
            WriteMember(writer, "feedback", flanger.Feedback, 0.0f);
            WriteMember(writer, "delay", flanger.DelayMilliseconds, 0.0f);
            WriteMember(writer, "mix", flanger.Mix, 0.5f);
            EndFilter(writer, flanger.Transition, options);
        }

        if (BeginFilter(writer, "spatial", value.Spatial, out var spatial))
        {
            WriteMember(writer, "x", spatial.X, 0.0f);
            WriteMember(writer, "y", spatial.Y, 0.0f);
            WriteMember(writer, "z", spatial.Z, 0.0f);
            WriteMember(writer, "depth", spatial.Depth, 0.0f);
            WriteMember(writer, "rate", spatial.Rate, 0.0f);
            EndFilter(writer, spatial.Transition, options);
        }

        if (BeginFilter(writer, "compressor", value.Compressor, out var compressor))
        {
            WriteMember(writer, "threshold", compressor.Threshold, -24.0f);
            WriteMember(writer, "ratio", compressor.Ratio, 4.0f);
            WriteMember(writer, "attack", compressor.Attack, 0.01f);
            WriteMember(writer, "release", compressor.Release, 0.1f);
            WriteMember(writer, "gain", compressor.Gain, 0.0f);
            WriteMember(writer, "makeupGain", compressor.MakeupGain, 0.0f);
            EndFilter(writer, compressor.Transition, options);
        }

        if (BeginFilter(writer, "phonograph", value.Phonograph, out var phonograph))
        {
            WriteMember(writer, "frequency", phonograph.Frequency, 0.8f);
            WriteMember(writer, "depth", phonograph.Depth, 0.25f);
            WriteMember(writer, "crackle", phonograph.Crackle, 0.18f);
            WriteMember(writer, "flutter", phonograph.Flutter, 0.18f);
            WriteMember(writer, "room", phonograph.Room, 0.22f);
            WriteMember(writer, "micAgc", phonograph.MicAgc, 0.25f);
            WriteMember(writer, "drive", phonograph.Drive, 0.25f);
            EndFilter(writer, phonograph.Transition, options);
        }

        if (BeginFilter(writer, "tesseract", value.Tesseract, out var tesseract))
        {
            WriteMember(writer, "rotationHz", tesseract.RotationHz, 0.0);
            EndFilter(writer, tesseract.Transition, options);
        }

        if (value.PluginFilters is not null)
        {
            writer.WritePropertyName("pluginFilters");
            NyraJson.Serialize(writer, value.PluginFilters, options);
        }

        writer.WriteEndObject();
    }

    private bool BeginFilter<T>(
        Utf8JsonWriter writer,
        string key,
        T? filter,
        [NotNullWhen(true)] out T? written
    )
        where T : class
    {
        written = null;
        if (!ShouldWrite(key))
            return false;

        if (filter is null)
        {
            WriteNull(writer, key);
            return false;
        }

        written = filter;
        writer.WritePropertyName(key);
        writer.WriteStartObject();
        return true;
    }

    private static void EndFilter(
        Utf8JsonWriter writer,
        FilterTransition? transition,
        JsonSerializerOptions options
    )
    {
        WriteTransition(writer, transition, options);
        writer.WriteEndObject();
    }

    private static void WriteBands(
        Utf8JsonWriter writer,
        IReadOnlyList<EqualizerBand>? bands,
        JsonSerializerOptions options
    )
    {
        if (bands is { Count: > 0 })
            NyraJson.Serialize(writer, bands, options);
        else
        {
            writer.WriteStartArray();
            writer.WriteEndArray();
        }
    }

    private static void WriteNull(Utf8JsonWriter writer, string name)
    {
        writer.WritePropertyName(name);
        writer.WriteNullValue();
    }

    private bool ShouldWrite(string key) => enabledFilters is null || enabledFilters.Contains(key);

    private static void WriteMember(
        Utf8JsonWriter writer,
        string name,
        double? value,
        double neutral
    ) => writer.WriteNumber(name, value ?? neutral);

    private static void WriteMember(
        Utf8JsonWriter writer,
        string name,
        float? value,
        float neutral
    ) => writer.WriteNumber(name, value ?? neutral);

    private static void WriteTransition(
        Utf8JsonWriter writer,
        FilterTransition? transition,
        JsonSerializerOptions options
    )
    {
        writer.WritePropertyName("transition");
        if (transition is null)
            writer.WriteNullValue();
        else
            NyraJson.Serialize(writer, transition, options);
    }

    private FilterChain ReadMembers(
        ref Utf8JsonReader reader,
        JsonSerializerOptions options,
        bool atFirstMemberName = false
    )
    {
        float? volume = null;
        IReadOnlyList<EqualizerBand>? equalizer = null;
        FilterTransition? equalizerTransition = null;
        KaraokeFilter? karaoke = null;
        TimescaleFilter? timescale = null;
        TremoloFilter? tremolo = null;
        VibratoFilter? vibrato = null;
        DistortionFilter? distortion = null;
        RotationFilter? rotation = null;
        ChannelMixFilter? channelMix = null;
        LowPassFilter? lowPass = null;
        EchoFilter? echo = null;
        ReverbFilter? reverb = null;
        HighPassFilter? highPass = null;
        ChorusFilter? chorus = null;
        PhaserFilter? phaser = null;
        FlangerFilter? flanger = null;
        SpatialFilter? spatial = null;
        CompressorFilter? compressor = null;
        PhonographFilter? phonograph = null;
        TesseractFilter? tesseract = null;
        Dictionary<string, JsonElement>? pluginFilters = null;

        var first = atFirstMemberName;
        while (first || (reader.Read() && reader.TokenType != JsonTokenType.EndObject))
        {
            first = false;
            var name = reader;
            reader.Read();

            if (name.ValueTextEquals("volume"u8))
                if (reader.TokenType == JsonTokenType.StartObject)
                    while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
                    {
                        if (reader.ValueTextEquals("value"u8))
                        {
                            reader.Read();
                            ReadVolumeScalar(ref reader, ref volume);
                        }
                        else
                            reader.Skip();
                    }
                else
                    ReadVolumeScalar(ref reader, ref volume);
            else if (name.ValueTextEquals("equalizer"u8))
                (equalizer, equalizerTransition) = ReadEqualizer(ref reader, options);
            else if (name.ValueTextEquals("karaoke"u8))
                karaoke = ReadFilter<KaraokeFilter>(ref reader, options);
            else if (name.ValueTextEquals("timescale"u8))
                timescale = ReadFilter<TimescaleFilter>(ref reader, options);
            else if (name.ValueTextEquals("tremolo"u8))
                tremolo = ReadFilter<TremoloFilter>(ref reader, options);
            else if (name.ValueTextEquals("vibrato"u8))
                vibrato = ReadFilter<VibratoFilter>(ref reader, options);
            else if (name.ValueTextEquals("distortion"u8))
                distortion = ReadFilter<DistortionFilter>(ref reader, options);
            else if (name.ValueTextEquals("rotation"u8))
                rotation = ReadFilter<RotationFilter>(ref reader, options);
            else if (name.ValueTextEquals("channelMix"u8))
                channelMix = ReadFilter<ChannelMixFilter>(ref reader, options);
            else if (name.ValueTextEquals("lowPass"u8) || name.ValueTextEquals("lowpass"u8))
                lowPass = ReadFilter<LowPassFilter>(ref reader, options);
            else if (name.ValueTextEquals("echo"u8))
                echo = ReadFilter<EchoFilter>(ref reader, options);
            else if (name.ValueTextEquals("reverb"u8))
                reverb = ReadFilter<ReverbFilter>(ref reader, options);
            else if (name.ValueTextEquals("highpass"u8))
                highPass = ReadFilter<HighPassFilter>(ref reader, options);
            else if (name.ValueTextEquals("chorus"u8))
                chorus = ReadFilter<ChorusFilter>(ref reader, options);
            else if (name.ValueTextEquals("phaser"u8))
                phaser = ReadFilter<PhaserFilter>(ref reader, options);
            else if (name.ValueTextEquals("flanger"u8))
                flanger = ReadFilter<FlangerFilter>(ref reader, options);
            else if (name.ValueTextEquals("spatial"u8))
                spatial = ReadFilter<SpatialFilter>(ref reader, options);
            else if (name.ValueTextEquals("compressor"u8))
                compressor = ReadFilter<CompressorFilter>(ref reader, options);
            else if (name.ValueTextEquals("phonograph"u8))
                phonograph = ReadFilter<PhonographFilter>(ref reader, options);
            else if (name.ValueTextEquals("tesseract"u8))
                tesseract = ReadFilter<TesseractFilter>(ref reader, options);
            else if (name.ValueTextEquals("pluginFilters"u8))
            {
                if (reader.TokenType == JsonTokenType.StartObject)
                {
                    var plugins = TryRead<Dictionary<string, JsonElement>>(ref reader, options);

                    if (isNodeLink && plugins is not null)
                    {
                        plugins.Remove("_disabled");
                        if (
                            plugins.TryGetValue("transition", out var injected)
                            && IsInjectedTransitionEnvelope(injected)
                        )
                            plugins.Remove("transition");
                    }
                    pluginFilters = plugins is { Count: > 0 } ? plugins : null;
                }
                else
                    reader.Skip();
            }
            else
                reader.Skip();
        }

        return new FilterChain
        {
            Volume = volume,
            Equalizer = equalizer,
            EqualizerTransition = equalizerTransition,
            Karaoke = karaoke,
            Timescale = timescale,
            Tremolo = tremolo,
            Vibrato = vibrato,
            Distortion = distortion,
            Rotation = rotation,
            ChannelMix = channelMix,
            LowPass = lowPass,
            Echo = echo,
            Reverb = reverb,
            HighPass = highPass,
            Chorus = chorus,
            Phaser = phaser,
            Flanger = flanger,
            Spatial = spatial,
            Compressor = compressor,
            Phonograph = phonograph,
            Tesseract = tesseract,
            PluginFilters = pluginFilters,
        };
    }

    internal readonly record struct RawEqualizerBand(
        [property: JsonPropertyName("band")] double? Band,
        [property: JsonPropertyName("gain")] float? Gain
    );

    private static IReadOnlyList<EqualizerBand>? ReadBands(
        ref Utf8JsonReader reader,
        JsonSerializerOptions options
    )
    {
        if (TryRead<List<RawEqualizerBand>>(ref reader, options) is not { Count: > 0 } raw)
            return null;

        List<EqualizerBand>? bands = null;
        foreach (var band in raw)
        {
            if (
                band.Band is not { } number
                || number != Math.Floor(number)
                || (int)number is < 0 or >= EqualizerBand.BandCount
                || band.Gain is not { } gain
                || !float.IsFinite(gain)
                || gain is < -0.25f or > 1.0f
            )
                continue;

            (bands ??= []).Add(new EqualizerBand((int)number, gain));
        }

        return bands;
    }

    private static void ReadVolumeScalar(ref Utf8JsonReader reader, ref float? volume)
    {
        if (reader.TokenType == JsonTokenType.Number)
        {
            var echoed = reader.GetSingle();
            if (float.IsFinite(echoed))
                volume = echoed;
        }
        else if (reader.TokenType == JsonTokenType.String)
        {
            if (
                JsonNumberParsing.TryParseUtf8Double(ref reader, out var parsed)
                && float.IsFinite((float)parsed)
            )
                volume = (float)parsed;
        }
        else
            reader.Skip();
    }

    private (IReadOnlyList<EqualizerBand>? Bands, FilterTransition? Transition) ReadEqualizer(
        ref Utf8JsonReader reader,
        JsonSerializerOptions options
    )
    {
        if (reader.TokenType == JsonTokenType.StartArray)
            return (ReadBands(ref reader, options), null);

        if (!isNodeLink || reader.TokenType != JsonTokenType.StartObject)
        {
            reader.Skip();
            return (null, null);
        }

        IReadOnlyList<EqualizerBand>? bands = null;
        FilterTransition? transition = null;
        while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
        {
            if (reader.ValueTextEquals("bands"u8))
            {
                reader.Read();
                if (reader.TokenType == JsonTokenType.StartArray)
                    bands = ReadBands(ref reader, options);
                else
                    reader.Skip();
            }
            else if (reader.ValueTextEquals("transition"u8))
            {
                reader.Read();
                if (reader.TokenType == JsonTokenType.StartObject)
                    transition = TryRead<FilterTransition>(ref reader, options);
                else
                    reader.Skip();
            }
            else
                reader.Skip();
        }

        return (bands, bands is null ? null : transition);
    }

    private TFilter? ReadFilter<TFilter>(ref Utf8JsonReader reader, JsonSerializerOptions options)
        where TFilter : class
    {
        if (reader.TokenType != JsonTokenType.StartObject)
        {
            reader.Skip();
            return null;
        }

        if (!isNodeLink)
            return ReadFiniteFilter<TFilter>(ref reader, options);

        var peek = reader;
        while (peek.Read() && peek.TokenType != JsonTokenType.EndObject)
        {
            if (peek.ValueTextEquals("_disabled"u8))
            {
                peek.Read();
                if (peek.TokenType is JsonTokenType.True)
                {
                    reader.Skip();
                    return null;
                }

                peek.Skip();
            }
            else
                peek.Skip();
        }

        return ReadFiniteFilter<TFilter>(ref reader, options);
    }

    private static TFilter? ReadFiniteFilter<TFilter>(
        ref Utf8JsonReader reader,
        JsonSerializerOptions options
    )
        where TFilter : class =>
        TryRead<TFilter>(ref reader, options) is { } filter
        && filter is not IFiniteCheck { HasNonFiniteNumber: true }
            ? filter
            : null;

    private static T? TryRead<T>(ref Utf8JsonReader reader, JsonSerializerOptions options)
        where T : class
    {
        var copy = reader;
        reader.Skip();
        try
        {
            return NyraJson.Deserialize<T>(ref copy, options);
        }
        catch (Exception e)
            when (e is JsonException or InvalidOperationException or NotSupportedException)
        {
            return null;
        }
    }

    private static bool IsInjectedTransitionEnvelope(JsonElement envelope)
    {
        if (envelope.ValueKind != JsonValueKind.Object)
            return false;

        var durationMs = false;
        var curve = false;
        foreach (var member in envelope.EnumerateObject())
            if (member.Name == "durationMs" && member.Value.ValueKind == JsonValueKind.Number)
                durationMs = true;
            else if (member.Name == "curve" && member.Value.ValueKind == JsonValueKind.String)
                curve = true;
            else
                return false;

        return durationMs && curve;
    }
}
