using System.Text.Json;
using Nyra.Entities;
using Nyra.Entities.Filters;

namespace Nyra.Tests;

public sealed class FilterEcho
{
    private static FilterChain? Read(string json) =>
        JsonSerializer.Deserialize<FilterChain>(json, NyraJson.OptionsFor(isNodeLink: true));

    private static FilterChain? ReadLavaLink(string json) =>
        JsonSerializer.Deserialize<FilterChain>(json, NyraJson.OptionsFor(isNodeLink: false));

    private static string Write(FilterChain chain) =>
        NyraJson.Serialize(chain, NyraJson.OptionsFor(isNodeLink: true));

    [Test]
    public async Task VolumeEchoShapes()
    {
        var quoted = Read("""{"filters":{"volume":"2.5"}}""");
        await Assert.That(quoted?.Volume).IsEqualTo(2.5f);

        var envelope = Read("""{"filters":{"volume":{"value":2.5}}}""");
        await Assert.That(envelope?.Volume).IsEqualTo(2.5f);

        var quotedEnvelope = Read("""{"filters":{"volume":{"value":"2.5"}}}""");
        await Assert.That(quotedEnvelope?.Volume).IsEqualTo(2.5f);

        var overflow = Read("""{"filters":{"volume":"1e40"}}""");
        await Assert.That(overflow?.Volume).IsNull();

        var envelopeOverflow = Read("""{"filters":{"volume":{"value":"1e40"}}}""");
        await Assert.That(envelopeOverflow?.Volume).IsNull();

        var envelopeJunk = Read("""{"filters":{"volume":{"value":true}}}""");
        await Assert.That(envelopeJunk?.Volume).IsNull();

        var exotic = Read("""{"filters":{"volume":true}}""");
        await Assert.That(exotic?.Volume).IsNull();
    }

    [Test]
    public async Task EqualizerEnvelope()
    {
        var enveloped = Read(
            """{"filters":{"equalizer":{"bands":[{"band":0,"gain":0.5}],"transition":{"durationMs":100,"curve":"linear"}}}}"""
        );
        await Assert.That(enveloped?.Equalizer is [{ BandId: 0, Gain: 0.5f }]).IsTrue();
        using (Assert.Multiple())
        {
            await Assert.That(enveloped!.EqualizerTransition).IsNotNull();
            await Assert.That(enveloped!.EqualizerTransition!.DurationMilliseconds).IsEqualTo(100);
            await Assert.That(enveloped!.EqualizerTransition!.Curve).IsEqualTo("linear");
        }

        var stringified = Read(
            """{"filters":{"equalizer":{"bands":[{"band":1,"gain":0.25}],"transition":"fast"}}}"""
        );
        await Assert.That(stringified?.Equalizer is [{ BandId: 1 }]).IsTrue();
        await Assert.That(stringified!.EqualizerTransition).IsNull();

        var plain = Read("""{"filters":{"equalizer":[{"band":2,"gain":0.1}]}}""");
        await Assert.That(plain?.Equalizer is [{ BandId: 2 }]).IsTrue();
        await Assert.That(plain!.EqualizerTransition).IsNull();
    }

    [Test]
    public async Task TransitionWrites()
    {
        var chain = new FilterChain
        {
            Karaoke = new KaraokeFilter(Level: 0.8f, Transition: new FilterTransition(100)),
        };
        var written = Write(chain);
        using (Assert.Multiple())
        {
            await Assert.That(written).Contains("\"karaoke\"");
            await Assert.That(written).Contains("\"transition\"");
            await Assert.That(written).Contains("\"durationMs\":100");
        }

        var bare = Write(new FilterChain());
        await Assert.That(bare).Contains("\"volume\":1");
        await Assert.That(bare).Contains("\"karaoke\":null");

        var pluginChain = new FilterChain
        {
            PluginFilters = new Dictionary<string, JsonElement>
            {
                ["custom"] = JsonDocument.Parse("""{"x":1}""").RootElement,
            },
        };
        var pluginWritten = Write(pluginChain);
        await Assert.That(pluginWritten).Contains("\"pluginFilters\"");
        await Assert.That(pluginWritten).Contains("\"custom\"");
    }

    [Test]
    public async Task DegenerateShapes()
    {
        await Assert
            .That(() =>
            {
                Read("""[1,2]""");
            })
            .Throws<JsonException>();

        var empty = Read("{}");
        using (Assert.Multiple())
        {
            await Assert.That(empty).IsNotNull();
            await Assert.That(empty!.Volume).IsNull();
            await Assert.That(empty!.Equalizer).IsNull();
        }

        var tailed = Read("""{"filters":{"volume":2},"junk":{"deep":[1,2]}}""");
        await Assert.That(tailed?.Volume).IsEqualTo(2.0f);

        var notAnObject = Read("""{"filters":5}""");
        await Assert.That(notAnObject).IsNotNull();
        await Assert.That(notAnObject!.Volume).IsNull();

        var junkFirst = Read("""{"junk":1,"filters":{"volume":1.5}}""");
        await Assert.That(junkFirst).IsNotNull();
        await Assert.That(junkFirst!.Volume).IsNull();
    }

    [Test]
    public async Task Degradation()
    {
        var foreignEnvelopeMember = Read("""{"filters":{"volume":{"junk":1}}}""");
        await Assert.That(foreignEnvelopeMember?.Volume).IsNull();

        var stringBand = Read("""{"filters":{"equalizer":{"bands":[{"band":"x","gain":1}]}}}""");
        await Assert.That(stringBand?.Equalizer).IsNull();

        var junkBands = Read("""{"filters":{"equalizer":{"bands":"junk"}}}""");
        await Assert.That(junkBands?.Equalizer).IsNull();

        var badTransition = Read(
            """{"filters":{"equalizer":{"bands":[{"band":0,"gain":0.5}],"transition":{"durationMs":"x","curve":"linear"}}}}"""
        );
        await Assert.That(badTransition?.Equalizer is [{ BandId: 0 }]).IsTrue();
        await Assert.That(badTransition!.EqualizerTransition).IsNull();

        var foreignEqualizerMember = Read(
            """{"filters":{"equalizer":{"bands":[{"band":0,"gain":0.5}],"junk":1}}}"""
        );
        await Assert.That(foreignEqualizerMember?.Equalizer is [{ BandId: 0 }]).IsTrue();

        var notDisabled = Read("""{"filters":{"timescale":{"_disabled":false,"speed":1.5}}}""");
        await Assert.That(notDisabled?.Timescale is { Speed: 1.5 }).IsTrue();

        var nullFilter = Read("""{"filters":{"karaoke":null}}""");
        await Assert.That(nullFilter?.Karaoke).IsNull();

        var lavalinkNull = ReadLavaLink("""{"filters":{"karaoke":null}}""");
        await Assert.That(lavalinkNull?.Karaoke).IsNull();

        var fatEnvelope = Read(
            """{"filters":{"pluginFilters":{"transition":{"durationMs":100,"curve":"linear","junk":1}}}}"""
        );
        await Assert
            .That(fatEnvelope?.PluginFilters is { Count: 1 } kept && kept.ContainsKey("transition"))
            .IsTrue();

        var emptyBands = Read("""{"filters":{"equalizer":{"bands":[]}}}""");
        await Assert.That(emptyBands?.Equalizer).IsNull();
    }

    [Test]
    public async Task PluginEnvelopeStrips()
    {
        var junk = Read("""{"filters":{"pluginFilters":"junk"}}""");
        await Assert.That(junk?.PluginFilters).IsNull();

        var tombstoned = Read(
            """{"filters":{"pluginFilters":{"_disabled":true,"transition":{"durationMs":100,"curve":"linear"},"custom":{"x":1}}}}"""
        );
        await Assert
            .That(tombstoned?.PluginFilters is { Count: 1 } chain && chain.ContainsKey("custom"))
            .IsTrue();

        var live = Read(
            """{"filters":{"pluginFilters":{"transition":{"durationMs":100,"curve":"linear"}}}}"""
        );
        await Assert.That(live?.PluginFilters).IsNull();

        var realPlugin = Read(
            """{"filters":{"pluginFilters":{"transition":{"durationMs":100}}}}"""
        );
        await Assert
            .That(realPlugin?.PluginFilters is { Count: 1 } kept && kept.ContainsKey("transition"))
            .IsTrue();

        var numeric = Read("""{"filters":{"pluginFilters":{"transition":5}}}""");
        await Assert
            .That(
                numeric?.PluginFilters is { Count: 1 } numericKept
                    && numericKept.ContainsKey("transition")
            )
            .IsTrue();
    }

    [Test]
    public async Task BandDegradations()
    {
        var degraded = Read(
            """{"filters":{"equalizer":[{"band":null,"gain":0.5},{"band":1.5,"gain":0.5},{"band":200,"gain":0.5},{"band":3,"gain":2.0},{"band":4,"gain":0.5}]}}"""
        );
        await Assert.That(degraded?.Equalizer is [{ BandId: 4, Gain: 0.5f }]).IsTrue();

        var junk = Read("""{"filters":{"equalizer":"junk"}}""");
        await Assert.That(junk?.Equalizer).IsNull();
    }
}
