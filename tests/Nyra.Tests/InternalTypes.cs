using System.Text.Json;
using Nyra.Abstractions;
using Nyra.Entities;
using Nyra.EventArgs;
using Nyra.Internal;
using Nyra.NetCord;

namespace Nyra.Tests;

public sealed class InternalTypes
{
    [Test]
    public async Task ClientInformation()
    {
        await Assert.That(new ClientInformation(1).CurrentUserId).IsEqualTo(1UL);
    }

    [Test]
    public async Task Guard()
    {
        var node = new Node(new Endpoint("127.0.0.1", 3000, "pass"));
        await Assert
            .That(() =>
            {
                _ = new NodeStatsEventArgs(null!);
            })
            .Throws<ArgumentNullException>()
            .WithParameterName("stats");
        await Assert
            .That(() =>
            {
                _ = new NodeErrorEventArgs(null!, node);
            })
            .Throws<ArgumentNullException>()
            .WithParameterName("exception");
        await Assert
            .That(() =>
            {
                _ = new NodeErrorEventArgs(new InvalidOperationException(), null!);
            })
            .Throws<ArgumentNullException>()
            .WithParameterName("node");
        await Assert
            .That(() =>
            {
                _ = new NodeConnectionStatusEventArgs(null!);
            })
            .Throws<ArgumentNullException>()
            .WithParameterName("status");
        await Assert
            .That(() =>
            {
                _ = ((global::NetCord.Gateway.GatewayClient)null!).UseNyra();
            })
            .Throws<ArgumentNullException>()
            .WithParameterName("client");
    }

    [Test]
    public async Task UrlStartPositionCases()
    {
        using (Assert.Multiple())
        {
            await Assert
                .That(UrlStartPosition.ParseMilliseconds("https://x?v=1&t=90"))
                .IsEqualTo(90_000);
            await Assert
                .That(UrlStartPosition.ParseMilliseconds("https://x?start=45"))
                .IsEqualTo(45_000);
            await Assert.That(UrlStartPosition.ParseMilliseconds("https://x")).IsNull();
            await Assert.That(UrlStartPosition.ParseMilliseconds("https://x?t=abc")).IsNull();
            await Assert.That(UrlStartPosition.ParseMilliseconds("https://x?t=-5")).IsNull();
        }
    }

    [Test]
    public async Task JsonNumberParsing()
    {
        var options = NyraJson.SerializerOptions;
        await Assert
            .That(() =>
            {
                _ = JsonSerializer.Deserialize<Player>("""{"volume": "2,5"}""", options);
            })
            .Throws<JsonException>();
        await Assert
            .That(() =>
            {
                _ = JsonSerializer.Deserialize<Player>("""{"volume": "1e40"}""", options);
            })
            .Throws<JsonException>();
    }

    [Test]
    public async Task RoundedInt64()
    {
        var options = NyraJson.SerializerOptions;
        var chapter = JsonSerializer.Deserialize<Chapter>(
            """{"startTime":0,"duration": 9223372036854775807}""",
            options
        );
        await Assert.That(chapter?.Duration).IsEqualTo(long.MaxValue);
        await Assert
            .That(() =>
            {
                _ = JsonSerializer.Deserialize<Chapter>(
                    """{"startTime":0,"duration": 1e19}""",
                    options
                );
            })
            .Throws<JsonException>();
    }

    [Test]
    public async Task LoadResultBranch()
    {
        var options = NyraJson.OptionsFor(isNodeLink: true);
        await Assert
            .That(() =>
            {
                _ = JsonSerializer.Deserialize<LoadResult>(
                    """{"loadType": "search", "data": null}""",
                    options
                );
            })
            .Throws<JsonException>();
        await Assert
            .That(() =>
            {
                _ = JsonSerializer.Deserialize<LoadResult>(
                    """{"loadType": "track", "data": null}""",
                    options
                );
            })
            .Throws<JsonException>();
        await Assert
            .That(() =>
            {
                _ = JsonSerializer.Deserialize<LoadResult>("""{"loadType": "search"}""", options);
            })
            .Throws<JsonException>();
        var unknown = JsonSerializer.Deserialize<LoadResult>(
            """{"loadType": "brand-new-type", "data": null}""",
            options
        );
    }

    [Test]
    public async Task PlaybackStamp()
    {
        var playback = new GuildPlayback(null!);
        var stamp = playback.StampPlayRequested();
        await Assert.That(stamp.Kind).IsEqualTo(GuildPlayback.RequestKind.Play);
        playback.RestoreRequestTicks(stamp);
        var first = playback.StampPauseRequested();
        playback.StampPauseRequested();
        playback.RestoreRequestTicks(first);
    }

    [Test]
    public async Task ContentEqualityCases()
    {
        var left = new Dictionary<string, long> { ["a"] = 1, ["b"] = 2 };
        var right = new Dictionary<string, long> { ["b"] = 2, ["a"] = 1 };
        await Assert.That(ContentEquality.DictionaryEquals(left, right)).IsTrue();
        await Assert
            .That(
                ContentEquality.DictionaryEquals(left, new Dictionary<string, long> { ["a"] = 1 })
            )
            .IsFalse();
        var listA = new[] { "x", "y" };
        var listB = new[] { "x", "y" };
        var listC = new[] { "x", "z" };
        await Assert.That(ContentEquality.ListEquals(listA, listB)).IsTrue();
        await Assert.That(ContentEquality.ListEquals(listA, listC)).IsFalse();
    }

    [Test]
    public async Task FilterEcho()
    {
        var options = NyraJson.OptionsFor(isNodeLink: true);
        var chain = JsonSerializer.Deserialize<FilterChain>(
            """{"filters":{"equalizer":[{"band":0,"gain":0.5}]}}""",
            options
        );
        await Assert.That(chain?.Equalizer).Count().IsEqualTo(1);
        await Assert.That(chain?.Equalizer?[0].Gain).IsEqualTo(0.5f);
        var impossible = JsonSerializer.Deserialize<FilterChain>(
            """{"filters":{"equalizer":[{"band":0,"gain":2.0}]}}""",
            options
        );
        await Assert.That(impossible?.Equalizer is not { Count: > 0 }).IsTrue();
        var transition = JsonSerializer.Deserialize<FilterChain>(
            """{"tremolo":{"frequency":8,"depth":0.5}}""",
            options
        );
        await Assert.That(transition?.Tremolo?.Frequency).IsEqualTo(8);
    }
}
