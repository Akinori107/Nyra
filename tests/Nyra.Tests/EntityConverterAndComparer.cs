using System.Text.Json;
using Nyra.Entities;
using Nyra.Entities.Filters;
using Nyra.EventArgs;

namespace Nyra.Tests;

public sealed class EntityConverterAndComparer
{
    private static readonly JsonSerializerOptions Options = NyraJson.SerializerOptions;

    private static T? Parse<T>(string json)
        where T : class => JsonSerializer.Deserialize<T>(json, Options);

    private sealed class SnowflakeBox
    {
        [System.Text.Json.Serialization.JsonPropertyName("id")]
        public ulong Id { get; set; }
    }

    private static readonly JsonSerializerOptions SnowflakeOptions = new()
    {
        Converters = { new SnowflakeConverter() },
    };

    [Test]
    public async Task SnowflakeEdges()
    {
        await Assert
            .That(() =>
            {
                _ = JsonSerializer.Deserialize<SnowflakeBox>(
                    """{"id":"not-a-number"}""",
                    SnowflakeOptions
                );
            })
            .Throws<JsonException>();

        var escaped = JsonSerializer.Deserialize<SnowflakeBox>(
            """{"id":"1\u0032"}""",
            SnowflakeOptions
        );
        await Assert.That(escaped?.Id).IsEqualTo(12);
    }

    private sealed class NullableBox
    {
        [System.Text.Json.Serialization.JsonPropertyName("flag")]
        public bool? Flag { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("name")]
        public string? Name { get; set; }
    }

    private static readonly JsonSerializerOptions NullableOptions = new()
    {
        Converters =
        {
            new NullableTolerantBooleanConverter(),
            new NullableTolerantStringConverter(),
        },
    };

    [Test]
    public async Task NullableAndNullWrites()
    {
        var nullFlag = JsonSerializer.Deserialize<NullableBox>(
            """{"flag":null}""",
            NullableOptions
        );
        await Assert.That(nullFlag?.Flag).IsNull();

        var written = JsonSerializer.Serialize(new NullableBox(), NullableOptions);
        await Assert.That(written).IsEqualTo("""{"flag":null,"name":null}""");

        var emptyName = JsonSerializer.Serialize(new NullableBox { Name = null }, NullableOptions);
        await Assert.That(emptyName).IsEqualTo("""{"flag":null,"name":null}""");

        var named = JsonSerializer.Serialize(new NullableBox { Name = "probe" }, NullableOptions);
        await Assert.That(named).Contains("probe");
    }

    private sealed class NotInTheContext
    {
        public int Value { get; set; }
    }

    [Test]
    public async Task TypeInfoRefuses()
    {
        await Assert
            .That(() =>
            {
                _ = NyraJson.Deserialize<NotInTheContext>("""{"value":1}""", Options);
            })
            .Throws<NotSupportedException>()
            .WithMessageContaining("NotInTheContext");
    }

    private sealed class MessageBox
    {
        [System.Text.Json.Serialization.JsonPropertyName("message")]
        public string? Message { get; set; }
    }

    private static readonly JsonSerializerOptions MessageOptions = new()
    {
        Converters = { new StringOrNumberConverter() },
    };

    [Test]
    public async Task StringOrNumber()
    {
        var asNumber = JsonSerializer.Deserialize<MessageBox>(
            """{"message":105}""",
            MessageOptions
        );
        await Assert.That(asNumber?.Message).IsEqualTo("105");

        var asNull = JsonSerializer.Deserialize<MessageBox>("""{"message":null}""", MessageOptions);
        await Assert.That(asNull?.Message).IsNull();

        await Assert
            .That(() =>
            {
                _ = JsonSerializer.Deserialize<MessageBox>("""{"message":true}""", MessageOptions);
            })
            .Throws<JsonException>();

        var writtenNull = JsonSerializer.Serialize(new MessageBox(), MessageOptions);
        await Assert.That(writtenNull).IsEqualTo("""{"message":null}""");
    }

    private sealed class FlagBox
    {
        [System.Text.Json.Serialization.JsonPropertyName("flag")]
        public bool Flag { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("name")]
        public string? Name { get; set; }
    }

    private static readonly JsonSerializerOptions TolerantOptions = new()
    {
        Converters = { new TolerantBooleanConverter(), new NullableTolerantStringConverter() },
    };

    [Test]
    public async Task Skipper()
    {
        var fromNumber = JsonSerializer.Deserialize<FlagBox>("""{"flag":7}""", TolerantOptions);
        await Assert.That(fromNumber?.Flag).IsTrue();

        var fromZero = JsonSerializer.Deserialize<FlagBox>("""{"flag":0}""", TolerantOptions);
        await Assert.That(fromZero?.Flag).IsFalse();

        var skipped = JsonSerializer.Deserialize<FlagBox>("""{"name":{"x":1}}""", TolerantOptions);
        await Assert.That(skipped?.Name).IsNull();

        var volume = Parse<FilterChain>("""{"volume":7}""");
        await Assert.That(volume?.Volume).IsEqualTo(7f);
    }

    [Test]
    public async Task MeaningShapes()
    {
        var translation = Parse<MeaningTranslation>("""{"paragraphs":null}""");
        await Assert.That(translation?.Paragraphs.Count).IsEqualTo(0);

        var data = Parse<MeaningData>("""{"paragraphs":["one",null,"two"]}""");
        await Assert.That(data?.Paragraphs.Count).IsEqualTo(2);
    }

    [Test]
    public async Task HashAndEquality()
    {
        var plainTrack = Parse<Track>(
            """{"encoded":"QXRsZWFzdA==","info":{"identifier":"i","author":"a","title":"t","length":1000,"isStream":false,"isSeekable":true,"position":0}}"""
        );
        await Assert.That(plainTrack).IsNotNull();

        var groupA = Parse<Group>("""{"id":"1","name":"g"}""");
        var groupB = Parse<Group>("""{"id":"1","name":"g"}""");
        await Assert.That(groupA?.GetHashCode()).IsEqualTo(groupB?.GetHashCode());

        var noMatches = new LoadResult.NoMatches();
        var failed = new LoadResult.LoadFailed(
            Parse<ErrorInfo>("""{"message":"m"}""") ?? new ErrorInfo()
        );
        await Assert.That(noMatches.GetHashCode()).IsEqualTo((noMatches with { }).GetHashCode());
        await Assert.That(failed.GetHashCode()).IsEqualTo((failed with { }).GetHashCode());

        var playA = new PlayOptions();
        var playB = new PlayOptions();
        await Assert.That(playA).IsEqualTo(playB);

        var sponsorA = Parse<SponsorBlockState>("""{"enabled":true}""");
        var sponsorB = Parse<SponsorBlockState>("""{"enabled":true}""");
        await Assert.That(sponsorA?.Equals(sponsorB)).IsTrue();

        var youA = Parse<YouTubeConfigUpdate>("""{"fieldsUpdated":["a"]}""");
        var youB = Parse<YouTubeConfigUpdate>("""{"fieldsUpdated":["a"]}""");
        await Assert.That(youA?.Equals(youB)).IsTrue();
    }

    [Test]
    public async Task SettersNormalize()
    {
        var stats = Parse<DetailedStats>("""{"sources":null,"playback":null,"frameStats":null}""");
        await Assert.That(stats?.Sources).IsNull();

        var droppedSource = Parse<DetailedStats>(
            """{"sources":{"youtube":null},"playback":null,"frameStats":null}"""
        );
        await Assert.That(droppedSource?.Sources is { Count: 0 }).IsTrue();

        var metadata = Parse<IcyMetadata>("""{"fields":null}""");
        await Assert.That(metadata?.Fields).IsNull();

        var droppedField = Parse<IcyMetadata>("""{"fields":{"a":null}}""");
        await Assert.That(droppedField?.Fields is { Count: 0 }).IsTrue();

        var youtube = Parse<YouTubeConfigUpdate>("""{"fieldsUpdated":null}""");
        await Assert.That(youtube?.FieldsUpdated.Count).IsEqualTo(0);

        var nodeInfo = Parse<NodeInfo>(
            """{"version":{"semver":"3.10.0","preReleaseIdentifiers":null},"filters":null,"sourceManagers":null}"""
        );
        using (Assert.Multiple())
        {
            await Assert.That(nodeInfo).IsNotNull();
            await Assert.That(nodeInfo!.Filters.Count).IsEqualTo(0);
            await Assert.That(nodeInfo!.SourceManagers.Count).IsEqualTo(0);
            await Assert.That(nodeInfo!.Version).IsNotNull();
            await Assert.That(nodeInfo!.Version!.PreReleaseIdentifiers.Count).IsEqualTo(0);
        }

        var lines = Parse<SubscribedLyrics>("""{"lines":null}""");
        await Assert.That(lines?.Lines is { Count: 0 }).IsTrue();

        var segments = Parse<SponsorBlockSegmentsLoadedEventArgs>("""{"segments":null}""");
        await Assert.That(segments?.Segments.Count).IsEqualTo(0);

        var sponsor = Parse<SponsorBlockState>("""{"actionTypes":null,"segments":null}""");
        using (Assert.Multiple())
        {
            await Assert.That(sponsor).IsNotNull();
            await Assert.That(sponsor!.ActionTypes.Count).IsEqualTo(0);
            await Assert.That(sponsor!.Segments.Count).IsEqualTo(0);
        }
    }

    [Test]
    public async Task YouTubeAudioTrack()
    {
        var noPlugin = Parse<Track>(
            """{"encoded":"QXRsZWFzdA==","info":{"identifier":"i","author":"a","title":"t","length":1000,"isStream":false,"isSeekable":true,"position":0}}"""
        );
        await Assert.That(noPlugin?.GetYouTubeAudioTracks()).IsNull();

        var garbage = Parse<Track>(
            """{"encoded":"QXRsZWFzdA==","info":{"identifier":"i","author":"a","title":"t","length":1000,"isStream":false,"isSeekable":true,"position":0},"pluginInfo":{"audioTracks":"garbage["}}"""
        );
        await Assert.That(garbage?.GetYouTubeAudioTracks()).IsNull();

        var audio = Parse<Track>(
            """{"encoded":"QXRsZWFzdA==","info":{"identifier":"i","author":"a","title":"t","length":1000,"isStream":false,"isSeekable":true,"position":0},"pluginInfo":{"audioTracks":"[{\"id\":\"a1\",\"locale\":\"en\"},{\"id\":null,\"locale\":\"fr\"}]"}}"""
        );
        var tracks = audio?.GetYouTubeAudioTracks();
        await Assert.That(tracks).IsNotNull();
        await Assert.That(tracks!.Count).IsEqualTo(1);
        await Assert.That(tracks![0].Id).IsEqualTo("a1");
    }

    [Test]
    public async Task FilterComparer()
    {
        var left = Parse<FilterChain>("""{"volume":0.5}""");
        var rightNull = Parse<FilterChain>("""{"volume":0.5}""");
        await Assert.That(left?.Equals(rightNull)).IsTrue();

        var emptyBands = Parse<FilterChain>("""{"equalizer":[]}""");
        var emptyAgain = Parse<FilterChain>("""{"equalizer":[]}""");
        await Assert.That(emptyBands?.Equals(emptyAgain)).IsTrue();

        var liveBands = Parse<FilterChain>("""{"equalizer":[{"band":0,"gain":0.1}]}""");
        await Assert.That(emptyBands?.Equals(liveBands)).IsFalse();
        await Assert.That(liveBands?.Equals(emptyBands)).IsFalse();

        var sameBands = Parse<FilterChain>("""{"equalizer":[{"band":0,"gain":0.1}]}""");
        await Assert.That(liveBands?.Equals(sameBands)).IsTrue();

        var otherBands = Parse<FilterChain>("""{"equalizer":[{"band":1,"gain":0.1}]}""");
        await Assert.That(liveBands?.Equals(otherBands)).IsFalse();

        var pluginsLeft = Parse<FilterChain>("""{"volume":0.5,"pluginFilters":{"mine":{"a":1}}}""");
        var pluginsMissingKey = Parse<FilterChain>(
            """{"volume":0.5,"pluginFilters":{"mine":{"b":1}}}"""
        );
        await Assert.That(pluginsLeft?.Equals(pluginsMissingKey)).IsFalse();
    }

    [Test]
    public async Task FilterBounds()
    {
        await Assert
            .That(() =>
            {
                Nyra.Internal.FilterValidation.ValidateFilterBounds(
                    new FilterChain { Timescale = new TimescaleFilter { Speed = 0.0 } },
                    false
                );
            })
            .Throws<System.ArgumentOutOfRangeException>();

        await Assert
            .That(() =>
            {
                Nyra.Internal.FilterValidation.ValidateFilterBounds(
                    new FilterChain { Timescale = new TimescaleFilter { Speed = -1.0 } },
                    false
                );
            })
            .Throws<System.ArgumentOutOfRangeException>();

        await Assert
            .That(() =>
            {
                Nyra.Internal.FilterValidation.ValidateFilterBounds(
                    new FilterChain { Timescale = new TimescaleFilter { Speed = double.NaN } },
                    false
                );
            })
            .Throws<System.ArgumentOutOfRangeException>();

        Nyra.Internal.FilterValidation.ValidateFilterBounds(
            new FilterChain { Timescale = new TimescaleFilter { Speed = 1.5 } },
            false
        );
    }
}
