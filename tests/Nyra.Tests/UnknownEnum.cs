using System.Text.Json;
using Nyra.Entities;
using Nyra.EventArgs;

namespace Nyra.Tests;

public sealed class UnknownEnum
{
    private static readonly JsonSerializerOptions NodeLink = NyraJson.OptionsFor(true);
    private static readonly JsonSerializerOptions Lavalink = NyraJson.OptionsFor(false);

    private static T? Parse<T>(string json)
        where T : class => JsonSerializer.Deserialize<T>(json, NodeLink);

    [Test]
    public async Task FadeSection()
    {
        var player = JsonSerializer.Deserialize<Player>(
            """{"guildId":"777","fading":{"trackStart":{"duration":100,"curve":"linear","type":"brand-new"}}}""",
            NodeLink
        );
        await Assert.That(player?.Fading?.TrackStart?.Type).IsEqualTo(FadeType.Unknown);
    }

    [Test]
    public async Task Loads()
    {
        var load = JsonSerializer.Deserialize<LoadResult>(
            """{"loadType":"brand-new","data":null}""",
            Lavalink
        );
        await Assert.That(load).IsTypeOf<LoadResult.LoadFailed>();
    }

    [Test]
    public async Task Meanings()
    {
        var meaning = Parse<MeaningResult>("""{"loadType":"brand-new","data":null}""");
        await Assert.That(meaning?.LoadType).IsEqualTo(MeaningLoadType.Unknown);
    }

    [Test]
    public async Task MixEnd()
    {
        var ended = JsonSerializer.Deserialize<MixEndedEventArgs>(
            """{"guildId":"777","mixId":"mx1","reason":"brand-new"}""",
            NodeLink
        );
        await Assert.That(ended?.EndReason).IsEqualTo(MixEndReason.Unknown);
    }

    [Test]
    public async Task EternalBox()
    {
        var jumped = JsonSerializer.Deserialize<EternalBoxJumpedEventArgs>(
            """{"guildId":"777","eternalbox":{"type":"brand-new","fromBeat":1,"toBeat":4}}""",
            NodeLink
        );
        await Assert.That(jumped?.Jump?.Type).IsEqualTo(EternalBoxJumpType.Unknown);
    }
}
