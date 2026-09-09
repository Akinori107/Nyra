using System.Text.Json;
using System.Text.Json.Serialization;
using Nyra.Entities;
using Nyra.Entities.Filters;

namespace Nyra.Internal;

/// <summary>
/// Converts <see cref="FilterChain"/> for one server kind. Writes the flat request shape both
/// servers accept, and reads it back. NodeLink responses additionally nest the state under a
/// <c>filters</c> member, mark disabled filters with a <c>_disabled</c> tombstone, wrap
/// equalizer bands in a <c>bands</c> object and echo an empty object for a never-applied
/// volume filter.
/// </summary>
internal sealed class FiltersConverter(bool isNodeLink) : JsonConverter<FilterChain>
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
            // NodeLink nests the flat state under a literal "filters" member, without any
            // filter state it answers an empty object, which parses as the flat shape too.
            FilterChain? chain = null;
            var root = reader; // copy for the envelope-less fallback
            while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
            {
                if (reader.ValueTextEquals("filters"u8))
                {
                    reader.Read();
                    if (reader.TokenType == JsonTokenType.StartObject)
                        chain = ReadMembers(ref reader, options);
                    else
                        reader.Skip();
                }
                else
                {
                    reader.Skip();
                }
            }

            return chain ?? ReadMembers(ref root, options);
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

        // Every filter is written, always with every member: Lavalink resets absent members to
        // their defaults while NodeLink merges a partial object into the previous state, so a
        // fully-specified object is the only form both servers interpret identically. Null
        // members become the neutral defaults, volume and equalizer cannot be null on the wire
        // (Lavalink answers 400), so their inactive form is volume 1.0 and an empty band list.
        writer.WritePropertyName("volume");
        writer.WriteNumberValue(value.Volume ?? 1.0f);

        writer.WritePropertyName("equalizer");
        if (value.Equalizer is { Count: > 0 })
            JsonSerializer.Serialize(writer, value.Equalizer, options);
        else
        {
            writer.WriteStartArray();
            writer.WriteEndArray();
        }

        if (value.Karaoke is { } karaoke)
        {
            writer.WritePropertyName("karaoke");
            writer.WriteStartObject();
            WriteMember(writer, "level", karaoke.Level, 1.0f);
            WriteMember(writer, "monoLevel", karaoke.MonoLevel, 1.0f);
            WriteMember(writer, "filterBand", karaoke.FilterBand, 220.0f);
            WriteMember(writer, "filterWidth", karaoke.FilterWidth, 100.0f);
            writer.WriteEndObject();
        }
        else
            WriteNull(writer, "karaoke");

        if (value.Timescale is { } timescale)
        {
            writer.WritePropertyName("timescale");
            writer.WriteStartObject();
            WriteMember(writer, "speed", timescale.Speed, 1.0);
            WriteMember(writer, "pitch", timescale.Pitch, 1.0);
            WriteMember(writer, "rate", timescale.Rate, 1.0);
            writer.WriteEndObject();
        }
        else
            WriteNull(writer, "timescale");

        if (value.Tremolo is { } tremolo)
        {
            writer.WritePropertyName("tremolo");
            writer.WriteStartObject();
            WriteMember(writer, "frequency", tremolo.Frequency, 2.0f);
            WriteMember(writer, "depth", tremolo.Depth, 0.5f);
            writer.WriteEndObject();
        }
        else
            WriteNull(writer, "tremolo");

        if (value.Vibrato is { } vibrato)
        {
            writer.WritePropertyName("vibrato");
            writer.WriteStartObject();
            WriteMember(writer, "frequency", vibrato.Frequency, 2.0f);
            WriteMember(writer, "depth", vibrato.Depth, 0.5f);
            writer.WriteEndObject();
        }
        else
            WriteNull(writer, "vibrato");

        if (value.Distortion is { } distortion)
        {
            writer.WritePropertyName("distortion");
            writer.WriteStartObject();
            WriteMember(writer, "sinOffset", distortion.SinOffset, 0.0f);
            WriteMember(writer, "sinScale", distortion.SinScale, 1.0f);
            WriteMember(writer, "cosOffset", distortion.CosOffset, 0.0f);
            WriteMember(writer, "cosScale", distortion.CosScale, 1.0f);
            WriteMember(writer, "tanOffset", distortion.TanOffset, 0.0f);
            WriteMember(writer, "tanScale", distortion.TanScale, 1.0f);
            WriteMember(writer, "offset", distortion.Offset, 0.0f);
            WriteMember(writer, "scale", distortion.Scale, 1.0f);
            writer.WriteEndObject();
        }
        else
            WriteNull(writer, "distortion");

        if (value.Rotation is { } rotation)
        {
            writer.WritePropertyName("rotation");
            writer.WriteStartObject();
            WriteMember(writer, "rotationHz", rotation.RotationHz, 0.0);
            writer.WriteEndObject();
        }
        else
            WriteNull(writer, "rotation");

        if (value.ChannelMix is { } channelMix)
        {
            writer.WritePropertyName("channelMix");
            writer.WriteStartObject();
            WriteMember(writer, "leftToLeft", channelMix.LeftToLeft, 1.0f);
            WriteMember(writer, "leftToRight", channelMix.LeftToRight, 0.0f);
            WriteMember(writer, "rightToLeft", channelMix.RightToLeft, 0.0f);
            WriteMember(writer, "rightToRight", channelMix.RightToRight, 1.0f);
            writer.WriteEndObject();
        }
        else
            WriteNull(writer, "channelMix");

        if (value.LowPass is { } lowPass)
        {
            writer.WritePropertyName("lowPass");
            writer.WriteStartObject();
            WriteMember(writer, "smoothing", lowPass.Smoothing, 20.0f);
            writer.WriteEndObject();
        }
        else
            WriteNull(writer, "lowPass");

        // The NodeLink-only filters follow, Lavalink ignores the members it does not know, and
        // NodeLink disables a filter whose member arrives null.
        if (value.Echo is { } echo)
        {
            writer.WritePropertyName("echo");
            writer.WriteStartObject();
            WriteMember(writer, "delay", echo.DelayMilliseconds, 0.0f);
            WriteMember(writer, "feedback", echo.Feedback, 0.0f);
            WriteMember(writer, "mix", echo.Mix, 0.0f);
            WriteTransition(writer, echo.Transition, options);
            writer.WriteEndObject();
        }
        else
            WriteNull(writer, "echo");

        if (value.Reverb is { } reverb)
        {
            writer.WritePropertyName("reverb");
            writer.WriteStartObject();
            WriteMember(writer, "roomSize", reverb.RoomSize, 0.5f);
            WriteMember(writer, "damping", reverb.Damping, 0.5f);
            WriteMember(writer, "wetLevel", reverb.WetLevel, 0.5f);
            WriteMember(writer, "dryLevel", reverb.DryLevel, 0.5f);
            WriteMember(writer, "width", reverb.Width, 1.0f);
            WriteMember(writer, "mix", reverb.Mix, 0.5f);
            WriteTransition(writer, reverb.Transition, options);
            writer.WriteEndObject();
        }
        else
            WriteNull(writer, "reverb");

        if (value.HighPass is { } highPass)
        {
            writer.WritePropertyName("highpass");
            writer.WriteStartObject();
            WriteMember(writer, "smoothing", highPass.Smoothing, 20.0f);
            WriteMember(writer, "targetAlpha", highPass.TargetAlpha, 0.0f);
            WriteTransition(writer, highPass.Transition, options);
            writer.WriteEndObject();
        }
        else
            WriteNull(writer, "highpass");

        if (value.Chorus is { } chorus)
        {
            writer.WritePropertyName("chorus");
            writer.WriteStartObject();
            WriteMember(writer, "rate", chorus.Rate, 0.0f);
            WriteMember(writer, "depth", chorus.Depth, 0.0f);
            WriteMember(writer, "feedback", chorus.Feedback, 0.0f);
            WriteMember(writer, "delay", chorus.DelayMilliseconds, 25.0f);
            WriteMember(writer, "mix", chorus.Mix, 0.5f);
            WriteTransition(writer, chorus.Transition, options);
            writer.WriteEndObject();
        }
        else
            WriteNull(writer, "chorus");

        if (value.Phaser is { } phaser)
        {
            writer.WritePropertyName("phaser");
            writer.WriteStartObject();
            WriteMember(writer, "rate", phaser.Rate, 0.0f);
            WriteMember(writer, "depth", phaser.Depth, 1.0f);
            WriteMember(writer, "feedback", phaser.Feedback, 0.0f);
            WriteMember(writer, "delay", phaser.DelayMilliseconds, 0.0f);
            WriteMember(writer, "mix", phaser.Mix, 0.5f);
            WriteMember(writer, "stages", phaser.Stages, 4);
            WriteMember(writer, "minFrequency", phaser.MinFrequency, 100.0f);
            WriteMember(writer, "maxFrequency", phaser.MaxFrequency, 2500.0f);
            WriteTransition(writer, phaser.Transition, options);
            writer.WriteEndObject();
        }
        else
            WriteNull(writer, "phaser");

        if (value.Flanger is { } flanger)
        {
            writer.WritePropertyName("flanger");
            writer.WriteStartObject();
            WriteMember(writer, "rate", flanger.Rate, 0.0f);
            WriteMember(writer, "depth", flanger.Depth, 0.0f);
            WriteMember(writer, "feedback", flanger.Feedback, 0.0f);
            WriteMember(writer, "delay", flanger.DelayMilliseconds, 0.0f);
            WriteMember(writer, "mix", flanger.Mix, 0.5f);
            WriteTransition(writer, flanger.Transition, options);
            writer.WriteEndObject();
        }
        else
            WriteNull(writer, "flanger");

        if (value.Spatial is { } spatial)
        {
            writer.WritePropertyName("spatial");
            writer.WriteStartObject();
            WriteMember(writer, "x", spatial.X, 0.0f);
            WriteMember(writer, "y", spatial.Y, 0.0f);
            WriteMember(writer, "z", spatial.Z, 0.0f);
            WriteMember(writer, "depth", spatial.Depth, 0.0f);
            WriteMember(writer, "rate", spatial.Rate, 0.0f);
            WriteTransition(writer, spatial.Transition, options);
            writer.WriteEndObject();
        }
        else
            WriteNull(writer, "spatial");

        if (value.Compressor is { } compressor)
        {
            writer.WritePropertyName("compressor");
            writer.WriteStartObject();
            WriteMember(writer, "threshold", compressor.Threshold, -24.0f);
            WriteMember(writer, "ratio", compressor.Ratio, 4.0f);
            WriteMember(writer, "attack", compressor.Attack, 0.01f);
            WriteMember(writer, "release", compressor.Release, 0.1f);
            WriteMember(writer, "gain", compressor.Gain, 0.0f);
            WriteMember(writer, "makeupGain", compressor.MakeupGain, 0.0f);
            WriteTransition(writer, compressor.Transition, options);
            writer.WriteEndObject();
        }
        else
            WriteNull(writer, "compressor");

        if (value.Phonograph is { } phonograph)
        {
            writer.WritePropertyName("phonograph");
            writer.WriteStartObject();
            WriteMember(writer, "frequency", phonograph.Frequency, 0.8f);
            WriteMember(writer, "depth", phonograph.Depth, 0.25f);
            WriteMember(writer, "crackle", phonograph.Crackle, 0.18f);
            WriteMember(writer, "flutter", phonograph.Flutter, 0.18f);
            WriteMember(writer, "room", phonograph.Room, 0.22f);
            WriteMember(writer, "micAgc", phonograph.MicAgc, 0.25f);
            WriteMember(writer, "drive", phonograph.Drive, 0.25f);
            WriteTransition(writer, phonograph.Transition, options);
            writer.WriteEndObject();
        }
        else
            WriteNull(writer, "phonograph");

        if (value.Tesseract is { } tesseract)
        {
            writer.WritePropertyName("tesseract");
            writer.WriteStartObject();
            WriteMember(writer, "rotationHz", tesseract.RotationHz, 0.0);
            WriteTransition(writer, tesseract.Transition, options);
            writer.WriteEndObject();
        }
        else
            WriteNull(writer, "tesseract");

        if (value.PluginFilters is not null)
        {
            writer.WritePropertyName("pluginFilters");
            JsonSerializer.Serialize(writer, value.PluginFilters, options);
        }

        writer.WriteEndObject();
    }

    private static void WriteNull(Utf8JsonWriter writer, string name)
    {
        writer.WritePropertyName(name);
        writer.WriteNullValue();
    }

    private static void WriteMember(
        Utf8JsonWriter writer,
        string name,
        double? value,
        double neutral
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
            JsonSerializer.Serialize(writer, transition, options);
    }

    private FilterChain ReadMembers(ref Utf8JsonReader reader, JsonSerializerOptions options)
    {
        // reader sits on the StartObject of the flat shape.
        float? volume = null;
        IReadOnlyList<EqualizerBand>? equalizer = null;
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

        while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
        {
            var name = reader; // copy at the property name
            reader.Read(); // move to the value

            if (name.ValueTextEquals("volume"u8))
            {
                // NodeLink echoes an empty object when the filter was never applied.
                if (reader.TokenType == JsonTokenType.Number)
                    volume = reader.GetSingle();
                else
                    reader.Skip();
            }
            else if (name.ValueTextEquals("equalizer"u8))
            {
                equalizer = ReadEqualizer(ref reader, options);
            }
            else if (name.ValueTextEquals("karaoke"u8))
            {
                karaoke = ReadFilter<KaraokeFilter>(ref reader, options);
            }
            else if (name.ValueTextEquals("timescale"u8))
            {
                timescale = ReadFilter<TimescaleFilter>(ref reader, options);
            }
            else if (name.ValueTextEquals("tremolo"u8))
            {
                tremolo = ReadFilter<TremoloFilter>(ref reader, options);
            }
            else if (name.ValueTextEquals("vibrato"u8))
            {
                vibrato = ReadFilter<VibratoFilter>(ref reader, options);
            }
            else if (name.ValueTextEquals("distortion"u8))
            {
                distortion = ReadFilter<DistortionFilter>(ref reader, options);
            }
            else if (name.ValueTextEquals("rotation"u8))
            {
                rotation = ReadFilter<RotationFilter>(ref reader, options);
            }
            else if (name.ValueTextEquals("channelMix"u8))
            {
                channelMix = ReadFilter<ChannelMixFilter>(ref reader, options);
            }
            else if (name.ValueTextEquals("lowPass"u8))
            {
                lowPass = ReadFilter<LowPassFilter>(ref reader, options);
            }
            else if (name.ValueTextEquals("echo"u8))
            {
                echo = ReadFilter<EchoFilter>(ref reader, options);
            }
            else if (name.ValueTextEquals("reverb"u8))
            {
                reverb = ReadFilter<ReverbFilter>(ref reader, options);
            }
            else if (name.ValueTextEquals("highpass"u8))
            {
                highPass = ReadFilter<HighPassFilter>(ref reader, options);
            }
            else if (name.ValueTextEquals("chorus"u8))
            {
                chorus = ReadFilter<ChorusFilter>(ref reader, options);
            }
            else if (name.ValueTextEquals("phaser"u8))
            {
                phaser = ReadFilter<PhaserFilter>(ref reader, options);
            }
            else if (name.ValueTextEquals("flanger"u8))
            {
                flanger = ReadFilter<FlangerFilter>(ref reader, options);
            }
            else if (name.ValueTextEquals("spatial"u8))
            {
                spatial = ReadFilter<SpatialFilter>(ref reader, options);
            }
            else if (name.ValueTextEquals("compressor"u8))
            {
                compressor = ReadFilter<CompressorFilter>(ref reader, options);
            }
            else if (name.ValueTextEquals("phonograph"u8))
            {
                phonograph = ReadFilter<PhonographFilter>(ref reader, options);
            }
            else if (name.ValueTextEquals("tesseract"u8))
            {
                tesseract = ReadFilter<TesseractFilter>(ref reader, options);
            }
            else if (name.ValueTextEquals("pluginFilters"u8))
            {
                if (reader.TokenType == JsonTokenType.StartObject)
                    pluginFilters = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(
                        ref reader,
                        options
                    );
                else
                    reader.Skip();
            }
            else
            {
                reader.Skip();
            }
        }

        return new FilterChain
        {
            Volume = volume,
            Equalizer = equalizer,
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

    private IReadOnlyList<EqualizerBand>? ReadEqualizer(
        ref Utf8JsonReader reader,
        JsonSerializerOptions options
    )
    {
        // Requests (the flat shape) send the bare array, NodeLink responses wrap it in a "bands" object.
        if (reader.TokenType == JsonTokenType.StartArray)
            return JsonSerializer.Deserialize<List<EqualizerBand>>(ref reader, options);

        if (!isNodeLink || reader.TokenType != JsonTokenType.StartObject)
        {
            reader.Skip();
            return null;
        }

        List<EqualizerBand>? bands = null;
        while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
        {
            if (reader.ValueTextEquals("bands"u8))
            {
                reader.Read();
                if (reader.TokenType == JsonTokenType.StartArray)
                    bands = JsonSerializer.Deserialize<List<EqualizerBand>>(ref reader, options);
                else
                    reader.Skip();
            }
            else
            {
                reader.Skip();
            }
        }

        return bands;
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
            return JsonSerializer.Deserialize<TFilter>(ref reader, options);

        // NodeLink marks disabled filters with a _disabled tombstone instead of nulling them,
        // scan a copy so the original reader stays positioned for the typed deserialize.
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
            {
                peek.Skip();
            }
        }

        return JsonSerializer.Deserialize<TFilter>(ref reader, options);
    }
}
