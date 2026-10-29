using System.Buffers;
using System.Text.Json;
using System.Text.Json.Serialization;
using Nyra.Entities;
using Nyra.Entities.Filters;

namespace Nyra.Tests;

public sealed class JsonEdge
{
    private static T Throws<T>(Action action)
        where T : Exception
    {
        try
        {
            action();
        }
        catch (T exception)
        {
            return exception;
        }
        throw new InvalidOperationException($"the expected {typeof(T).Name} never came");
    }

    private sealed record SnowflakeProbe(ulong G);

    private sealed record RoundedProbe(
        [property: JsonConverter(typeof(RoundedInt64Converter))] long L,
        [property: JsonConverter(typeof(TolerantRoundedInt64Converter))] long? LN,
        [property: JsonConverter(typeof(RoundedInt32Converter))] int I
    );

    [Test]
    public async Task RoundedRefuses()
    {
        var options = new JsonSerializerOptions();
        var longString = Throws<JsonException>(() =>
            JsonSerializer.Deserialize<RoundedProbe>("""{"L":"abc","LN":null,"I":0}""", options)
        );

        var longToken = Throws<JsonException>(() =>
            JsonSerializer.Deserialize<RoundedProbe>("""{"L":true,"LN":null,"I":0}""", options)
        );

        var intString = Throws<JsonException>(() =>
            JsonSerializer.Deserialize<RoundedProbe>("""{"L":0,"LN":null,"I":"abc"}""", options)
        );

        var written = JsonSerializer.Serialize(new RoundedProbe(0, null, 0), options);
        await Assert.That(written).Contains(""""LN":null"""");
    }

    [Test]
    public async Task FiniteFloatRefuses()
    {
        var options = NyraJson.SerializerOptions;
        var converter = new FiniteFloatConverter();

        var trueReader = new Utf8JsonReader("true"u8, isFinalBlock: true, state: default);
        trueReader.Read();
        JsonException? wrongToken = null;
        try
        {
            converter.Read(ref trueReader, typeof(float), options);
        }
        catch (JsonException ex)
        {
            wrongToken = ex;
        }
        await Assert.That(wrongToken).IsNotNull();

        var hugeReader = new Utf8JsonReader("1e40"u8, isFinalBlock: true, state: default);
        hugeReader.Read();
        JsonException? nonFinite = null;
        try
        {
            converter.Read(ref hugeReader, typeof(float), options);
        }
        catch (JsonException ex)
        {
            nonFinite = ex;
        }
        await Assert.That(nonFinite).IsNotNull();

        using var stream = new MemoryStream();
        using var writer = new Utf8JsonWriter(stream);
        var write = Throws<JsonException>(() => converter.Write(writer, float.NaN, options));
    }

    private sealed class PlainSegment : ReadOnlySequenceSegment<byte> { }

    [Test]
    public async Task ChainHash()
    {
        var one = new FilterChain
        {
            Volume = 0.9f,
            Tremolo = new TremoloFilter { Frequency = 8, Depth = 0.5f },
        };
        var same = new FilterChain
        {
            Volume = 0.9f,
            Tremolo = new TremoloFilter { Frequency = 8, Depth = 0.5f },
        };
        await Assert.That(one.GetHashCode()).IsEqualTo(same.GetHashCode());
    }

    [Test]
    public async Task TrackUserDataEquality()
    {
        var element = JsonSerializer.Deserialize<JsonElement>("""{"a":1}""");
        var elementAgain = JsonSerializer.Deserialize<JsonElement>("""{"a":1}""");

        var bare = new PlayerUpdateTrack { Encoded = "t" };
        var bareAgain = new PlayerUpdateTrack { Encoded = "t" };
        var carrying = new PlayerUpdateTrack { Encoded = "t", UserData = element };
        var carryingAgain = new PlayerUpdateTrack { Encoded = "t", UserData = elementAgain };

        await Assert.That(bare).IsEqualTo(bareAgain);
        await Assert.That(carrying).IsNotEqualTo(bare);
        await Assert.That(carrying).IsEqualTo(carryingAgain);
        await Assert.That(bare.GetHashCode() != 0 || bareAgain.GetHashCode() != 0).IsTrue();

        var identified = new PlayerIdentifierUpdateTrack { Identifier = "ph8" };
        var identifiedAgain = new PlayerIdentifierUpdateTrack { Identifier = "ph8" };
        var identifiedCarrying = new PlayerIdentifierUpdateTrack
        {
            Identifier = "ph8",
            UserData = element,
        };
        await Assert.That(identified).IsEqualTo(identifiedAgain);
        await Assert.That(identifiedCarrying).IsNotEqualTo(identified);
        await Assert
            .That(identified.GetHashCode() != 0 || identifiedAgain.GetHashCode() != 0)
            .IsTrue();
    }

    private static string PaddedInfo(int length)
    {
        const string head =
            """{"version":{"semver":"3.9.1"},"isNodelink":true,"plugins":[],"pad":" """;
        const string tail = "\"}";
        return head.TrimEnd()
            + new string('x', length - head.TrimEnd().Length - tail.Length)
            + tail;
    }

    [Test]
    public async Task SuccessBodyBudget()
    {
        const int budget = 64 * 1024 * 1024;
        var oversized = new FakeNodeServer { InfoBody = PaddedInfo(budget + 1) };
        var fitting = new FakeNodeServer { InfoBody = PaddedInfo(budget - 1024) };
        var bridge = new FakeBridge();
        bridge.GoReady();
        var extension = new NyraExtension(bridge);
        Node NodeFor(FakeNodeServer server, string name) =>
            new(
                new Endpoint("127.0.0.1", server.Port, server.Password),
                new NodeOptions
                {
                    Name = name,
                    ReconnectDelay = TimeSpan.FromMilliseconds(50),
                    MaxReconnectDelay = TimeSpan.FromMilliseconds(200),
                    ReadyTimeout = TimeSpan.FromSeconds(5),
                }
            );
        var tooBig = NodeFor(oversized, "json-edges-over");
        var justFits = NodeFor(fitting, "json-edges-fits");
        extension.AddNode(tooBig);
        extension.AddNode(justFits);
        try
        {
            await extension.ConnectAllNodesAsync().ConfigureAwait(false);
            await Assert.That(justFits.IsReady).IsTrue();
            await Assert.That(justFits.IsNodeLink).IsTrue();
            await Assert.That(tooBig.IsReady).IsFalse();
            await Assert.That(tooBig.Version).IsNull();
        }
        finally
        {
            await extension.DisposeAsync().ConfigureAwait(false);
            oversized.Dispose();
            fitting.Dispose();
        }
    }

    private sealed record StringOrNumberProbe(
        [property: JsonConverter(typeof(StringOrNumberConverter))] string? X
    );

    private sealed record NullableStringProbe(
        [property: JsonConverter(typeof(NullableTolerantStringConverter))] string? S
    );

    private sealed record NullableBooleanProbe(
        [property: JsonConverter(typeof(NullableTolerantBooleanConverter))] bool? B
    );

    private sealed record FiniteProbe(
        [property: JsonConverter(typeof(FiniteFloatConverter))] float F
    );

    [Test]
    public async Task ConverterEdges()
    {
        var options = new JsonSerializerOptions();

        var objectToken = Throws<JsonException>(() =>
            JsonSerializer.Deserialize<StringOrNumberProbe>("""{"X":{}}""", options)
        );

        var rawText = JsonSerializer.Deserialize<StringOrNumberProbe>("""{"X":1e400}""", options);
        await Assert.That(rawText?.X).IsEqualTo("1e400");

        var nullAsNull = JsonSerializer.Deserialize<NullableStringProbe>("""{"S":null}""", options);
        await Assert.That(nullAsNull?.S).IsNull();

        var nullableWritten = JsonSerializer.Serialize(new NullableStringProbe(null), options);
        await Assert.That(nullableWritten).Contains(""""S":null"""");

        var boolObject = JsonSerializer.Deserialize<NullableBooleanProbe>("""{"B":{}}""", options);
        await Assert.That(boolObject?.B is false or null).IsTrue();

        var boolWritten = JsonSerializer.Serialize(new NullableBooleanProbe(null), options);
        await Assert.That(boolWritten).Contains(""""B":null"""");

        var intBoundaries = JsonSerializer.Deserialize<RoundedProbe>(
            """{"L":9223372036854775807.5,"LN":null,"I":2.5}""",
            options
        );
        await Assert.That(intBoundaries?.L).IsEqualTo(long.MaxValue);
        await Assert.That(intBoundaries?.I).IsEqualTo(3);

        var intOverflow = Throws<JsonException>(() =>
            JsonSerializer.Deserialize<RoundedProbe>("""{"L":-9.3e18,"LN":null,"I":0}""", options)
        );

        var floatRefusesText = Throws<JsonException>(() =>
            JsonSerializer.Serialize(new FiniteProbe(float.PositiveInfinity), options)
        );
    }

    [Test]
    public async Task JsonToleratedShapes()
    {
        var options = new JsonSerializerOptions();
        var entityOptions = NyraJson.OptionsFor(true);

        var toleratedBool = JsonSerializer.Deserialize<SponsorBlockState>(
            """{"enabled":[],"categories":[]}""",
            entityOptions
        );
        await Assert.That(toleratedBool?.Enabled).IsFalse();

        var stringless = JsonSerializer.Deserialize<NullableStringProbe>(
            """{"S":[1,2]}""",
            options
        );
        await Assert.That(stringless?.S).IsNull();

        var roundedFromString = JsonSerializer.Deserialize<RoundedProbe>(
            """{"L":"9.5","LN":null,"I":"2.5"}""",
            options
        );
        await Assert.That(roundedFromString?.L).IsEqualTo(10);
        await Assert.That(roundedFromString!.I).IsEqualTo(3);

        var intBadToken = Throws<JsonException>(() =>
            JsonSerializer.Deserialize<RoundedProbe>("""{"L":0,"LN":null,"I":{}}""", options)
        );

        var finiteFromString = JsonSerializer.Deserialize<FiniteProbe>("""{"F":"1.5"}""", options);
        await Assert.That(finiteFromString?.F).IsEqualTo(1.5f);

        var finiteWritten = JsonSerializer.Serialize(new FiniteProbe(2.5f), options);
        await Assert.That(finiteWritten).Contains("2.5");

        var nullableWrittenWithValue = JsonSerializer.Serialize(new RoundedProbe(0, 5, 0), options);
        await Assert.That(nullableWrittenWithValue).Contains(""""LN":5"""");

        var snowflakeWritten = JsonSerializer.Serialize(new SnowflakeProbe(777), options);
        await Assert.That(snowflakeWritten).Contains("777");
    }
}
