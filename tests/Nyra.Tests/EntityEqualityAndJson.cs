using System.Text.Json;
using Nyra.Entities;
using Nyra.Entities.Filters;
using Nyra.Exceptions;
using Nyra.Internal;

namespace Nyra.Tests;

public sealed class EntityEqualityAndJson
{
    private static readonly JsonSerializerOptions Options = NyraJson.SerializerOptions;

    private sealed class ProbeException : NyraException
    {
        public ProbeException(string message)
            : base(message) { }
    }

    [Test]
    public async Task MessageOnlyException()
    {
        await Assert.That(new ProbeException("probe").Message).IsEqualTo("probe");
    }

    [Test]
    public async Task UpdateResultHash()
    {
        const string json =
            """{"id":"g1","guildIds":[10,20],"createdAt":5000,"players":[],"errors":[]}""";
        var a = JsonSerializer.Deserialize<GroupUpdateResult>(json, Options);
        var b = JsonSerializer.Deserialize<GroupUpdateResult>(json, Options);
        await Assert.That(a).IsNotNull();
        await Assert.That(b).IsNotNull();
        await Assert.That(a).IsEqualTo(b);
        await Assert.That(a!.GetHashCode()).IsEqualTo(b!.GetHashCode());
    }

    [Test]
    public async Task CreatedTimeConverts()
    {
        const string json =
            """{"id":"g1","guildIds":[],"createdAt":5000,"players":[],"errors":[]}""";
        var update = JsonSerializer.Deserialize<GroupUpdateResult>(json, Options);
        await Assert.That(update?.CreatedAtTime.ToUnixTimeMilliseconds()).IsEqualTo(5000);
    }

    [Test]
    public async Task GroupTrackHash()
    {
        const string json =
            """{"encoded":"AAA","identifier":null,"audioTrackId":null,"language":null,"userData":{"k":1}}""";
        var a = JsonSerializer.Deserialize<GroupTrack>(json, Options);
        var b = JsonSerializer.Deserialize<GroupTrack>(json, Options);
        await Assert.That(a).IsNotNull();
        await Assert.That(b).IsNotNull();
        await Assert.That(a).IsEqualTo(b);
        await Assert.That(a!.GetHashCode()).IsEqualTo(b!.GetHashCode());
    }

    [Test]
    public async Task LavaLyricsEquality()
    {
        var a = new LavaLyricsLyrics(
            "source",
            "prov",
            "text",
            [new LavaLyricsLine(10, 20, "line")]
        );
        var b = new LavaLyricsLyrics(
            "source",
            "prov",
            "text",
            [new LavaLyricsLine(10, 20, "line")]
        );
        var otherText = new LavaLyricsLyrics("source", "prov", "other", []);
        using (Assert.Multiple())
        {
            await Assert.That(a).IsEqualTo(b);
            await Assert.That(a).IsNotEqualTo(otherText);
            await Assert.That(a.GetHashCode()).IsEqualTo(b.GetHashCode());
        }
    }

    [Test]
    public async Task SettingsMargin()
    {
        var a = new SponsorBlockSettings
        {
            Enabled = true,
            Categories = ["sponsor"],
            ActionTypes = ["skip"],
            SkipMarginMilliseconds = 500,
        };
        var b = new SponsorBlockSettings
        {
            Enabled = true,
            Categories = ["sponsor"],
            ActionTypes = ["skip"],
            SkipMarginMilliseconds = 500,
        };
        var otherMargin = a with { SkipMarginMilliseconds = 600 };
        await Assert.That(a).IsEqualTo(b);
        await Assert.That(a).IsNotEqualTo(otherMargin);
    }

    [Test]
    public async Task NullFilterMember()
    {
        var chain = JsonSerializer.Deserialize<FilterChain>(
            """{"karaoke":null,"volume":0.5}""",
            Options
        );
        await Assert.That(chain?.Karaoke).IsNull();
    }

    [Test]
    public async Task PluginFilterMaps()
    {
        var left = new FilterChain
        {
            Volume = 1f,
            PluginFilters = new Dictionary<string, JsonElement>
            {
                ["plug"] = JsonDocument.Parse("""{"a":[1,2]}""").RootElement,
            },
        };
        var sameContent = new FilterChain
        {
            Volume = 1f,
            PluginFilters = new Dictionary<string, JsonElement>
            {
                ["plug"] = JsonDocument.Parse("""{"a":[1,2]}""").RootElement,
            },
        };
        await Assert.That(left).IsEqualTo(sameContent);
        await Assert.That(left).IsNotEqualTo(left with { PluginFilters = null });
        await Assert.That(left).IsEqualTo(left with { });
        var missingKey = new FilterChain
        {
            Volume = 1f,
            PluginFilters = new Dictionary<string, JsonElement>
            {
                ["plug"] = JsonDocument.Parse("""{"a":[1,2]}""").RootElement,
                ["extra"] = JsonDocument.Parse("{}").RootElement,
            },
        };
        await Assert.That(left).IsNotEqualTo(missingKey);
    }

    [Test]
    public async Task HostileAndDepth()
    {
        var hostile = JsonSerializer.Deserialize<FilterChain>(
            """{"pluginFilters":{"weird":{"a":[1,null,"x"],"b":{}}}}""",
            Options
        );
        await Assert.That(hostile?.PluginFilters).Count().IsEqualTo(1);

        static string Nested(int levels) =>
            "{\"pluginFilters\":{\"p\":" + new string('[', levels) + new string(']', levels) + "}}";

        var shallow = JsonSerializer.Deserialize<FilterChain>(Nested(40), Options);
        await Assert.That(shallow?.PluginFilters).Count().IsEqualTo(1);

        await Assert
            .That(() =>
            {
                _ = JsonSerializer.Deserialize<FilterChain>(Nested(80), Options);
            })
            .Throws<JsonException>();
    }

    [Test]
    public async Task PhaserInvertedRange()
    {
        await Assert
            .That(() =>
            {
                FilterValidation.ValidateFilterBounds(
                    new PhaserFilter
                    {
                        Rate = 1,
                        Depth = 0.5f,
                        Mix = 0.5f,
                        MinFrequency = 2500,
                        MaxFrequency = 100,
                    },
                    nodeLink: true
                );
            })
            .Throws<ArgumentOutOfRangeException>();
    }

    private sealed record GhostFilter : IFilter, IFiniteCheck
    {
        public float? Poison { get; init; }

        bool IFiniteCheck.HasNonFiniteNumber => float.IsNaN(Poison ?? 0f);
    }

    [Test]
    public async Task SwitchUnknownFilter()
    {
        await Assert
            .That(() =>
            {
                FilterValidation.ValidateFilterBounds(new GhostFilter { Poison = float.NaN }, true);
            })
            .Throws<ArgumentOutOfRangeException>();
    }

    [Test]
    public async Task LoadResultMembers()
    {
        var nodeLink = NyraJson.OptionsFor(true);
        var load = JsonSerializer.Deserialize<LoadResult>(
            """{"loadType":"track","data":{"encoded":"AAA","info":{}},"whatever":123}""",
            nodeLink
        );
        await Assert.That(load is LoadResult.TrackLoaded).IsTrue();

        var playlist = JsonSerializer.Deserialize<LoadResult>(
            """{"loadType":"playlist","data":{"info":{"name":"x","selectedTrack":-1},"tracks":[],"pluginInfo":5}}""",
            nodeLink
        );
        var plugin = playlist as LoadResult.PlaylistLoaded;
        await Assert.That(plugin?.PluginInfo).IsNull();
    }

    [Test]
    public async Task LyricDurationOmitted()
    {
        var line = new LavaLyricsLine(10, null, "text");
        var written = JsonSerializer.Serialize(line, Options);
        await Assert.That(written).Contains("\"timestamp\":10");
        await Assert.That(written).DoesNotContain("duration");
    }

    private sealed class ProbeEventArgs : System.EventArgs { }
}
