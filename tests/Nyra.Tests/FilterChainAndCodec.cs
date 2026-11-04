using System.Buffers.Binary;
using System.Text.Json;
using Nyra.Entities;
using Nyra.Entities.Filters;
using Nyra.Exceptions;

namespace Nyra.Tests;

public sealed class FilterChainAndCodec
{
    [Test]
    public async Task FilterChain()
    {
        var options = NyraJson.OptionsFor(isNodeLink: true);
        var chain = new FilterChain
        {
            Volume = 1.5f,
            Equalizer = [new EqualizerBand(0, 0.5f), new EqualizerBand(1, -0.25f)],
            Karaoke = new KaraokeFilter
            {
                Level = 0.8f,
                MonoLevel = 0.6f,
                FilterBand = 220,
                FilterWidth = 100,
            },
            Timescale = new TimescaleFilter
            {
                Speed = 1.2f,
                Rate = 1.1f,
                Pitch = 0.9f,
            },
            Tremolo = new TremoloFilter { Frequency = 8f, Depth = 0.5f },
            Vibrato = new VibratoFilter { Frequency = 10f, Depth = 0.4f },
            Distortion = new DistortionFilter
            {
                SinOffset = 1,
                SinScale = 2,
                CosOffset = 3,
                CosScale = 4,
                TanOffset = 5,
                TanScale = 6,
            },
            Rotation = new RotationFilter { RotationHz = 2.5 },
            ChannelMix = new ChannelMixFilter
            {
                LeftToLeft = 0.9f,
                LeftToRight = 0.1f,
                RightToLeft = 0.1f,
                RightToRight = 0.9f,
            },
            LowPass = new LowPassFilter { Smoothing = 2 },
            Echo = new EchoFilter { DelayMilliseconds = 500, Mix = 0.6f },
        };

        var round = RoundTrip(chain, options);
        await Assert.That(round).IsNotNull();
        await Assert
            .That(
                round is { Volume: 1.5f, Equalizer: { Count: 2 } bands }
                    && bands[1].BandId == 1
                    && bands[1].Gain == -0.25f
            )
            .IsTrue();
        await Assert.That(round?.Karaoke is { Level: 0.8f, FilterBand: 220 }).IsTrue();
        await Assert.That(round?.Timescale is { Speed: 1.2f, Pitch: 0.9f }).IsTrue();
        await Assert.That(round?.Tremolo is { Frequency: 8 }).IsTrue();
        await Assert.That(round?.Vibrato is { Frequency: 10 }).IsTrue();
        await Assert.That(round?.Distortion is { SinOffset: 1, TanScale: 6 }).IsTrue();
        await Assert.That(round?.Rotation is { RotationHz: 2.5 }).IsTrue();
        await Assert.That(round?.ChannelMix is { LeftToLeft: 0.9f }).IsTrue();
        await Assert.That(round?.LowPass is { Smoothing: 2 }).IsTrue();
        await Assert.That(round?.Echo is { DelayMilliseconds: 500, Mix: 0.6f }).IsTrue();

        var cleared = RoundTrip(new FilterChain { Volume = 1f }, options);
        using (Assert.Multiple())
        {
            await Assert.That(cleared?.Volume).IsEqualTo(1f);
            await Assert.That(cleared!.Karaoke).IsNull();
            await Assert.That(cleared!.Echo).IsNull();
            await Assert.That(cleared!.Equalizer).IsNull();
        }

        var rawEcho = JsonSerializer.Deserialize<FilterChain>("""{"volume":9.0}""", options);
        await Assert.That(rawEcho?.Volume).IsEqualTo(9f);
    }

    private static FilterChain? RoundTrip(FilterChain chain, JsonSerializerOptions options)
    {
        var json = JsonSerializer.Serialize(chain, options);
        return JsonSerializer.Deserialize<FilterChain>(json, options);
    }

    [Test]
    public async Task OptionsAndTime()
    {
        var defaults = new NodeOptions();
        using (Assert.Multiple())
        {
            await Assert.That(defaults.ResumeTimeout).IsEqualTo(TimeSpan.FromSeconds(60));
            await Assert.That(defaults.ReconnectDelay).IsEqualTo(TimeSpan.FromSeconds(5));
            await Assert.That(defaults.MaxReconnectDelay).IsEqualTo(TimeSpan.FromSeconds(60));
            await Assert.That(defaults.MaxReconnectAttempts).IsEqualTo(-1);
            await Assert.That(defaults.VoiceHandshakeTimeout).IsEqualTo(TimeSpan.FromSeconds(10));
            await Assert.That(defaults.ReadyTimeout).IsEqualTo(TimeSpan.FromSeconds(15));
        }

        var minDate = DateTimeOffset.FromUnixTimeMilliseconds(UnixTime.MinMilliseconds);
        var maxDate = DateTimeOffset.FromUnixTimeMilliseconds(UnixTime.MaxMilliseconds);
        await Assert.That(minDate.Year).IsEqualTo(1);
        await Assert.That(maxDate.Year).IsEqualTo(9999);

        var rest = new RestException(429, "/v4/stats", "body", TimeSpan.FromSeconds(1.5));
        using (Assert.Multiple())
        {
            await Assert.That(rest.StatusCode).IsEqualTo(429);
            await Assert.That(rest.ResponseBody).IsEqualTo("body");
            await Assert.That(rest.Path).IsEqualTo("/v4/stats");
            await Assert.That(rest.RetryAfter).IsEqualTo(TimeSpan.FromSeconds(1.5));
        }
    }
}

internal static class BigEndianBytes
{
    public static byte[] ToByteArrayBigEndian(this long value)
    {
        var bytes = new byte[8];
        BinaryPrimitives.WriteInt64BigEndian(bytes, value);
        return bytes;
    }

    public static byte[] ToByteArrayBigEndian(this int value)
    {
        var bytes = new byte[4];
        BinaryPrimitives.WriteInt32BigEndian(bytes, value);
        return bytes;
    }
}
