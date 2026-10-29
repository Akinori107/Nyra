using System.Text.Json;
using Nyra.Entities;
using Nyra.EventArgs;
using Nyra.Internal;

namespace Nyra.Tests;

public sealed class NullToleranceRegression
{
    private static T? Parse<T>(string json, bool isNodeLink = true)
        where T : class => JsonSerializer.Deserialize<T>(json, NyraJson.OptionsFor(isNodeLink));

    [Test]
    public async Task NullTolerantCounters()
    {
        var segment = Parse<SponsorBlockSegment>(
            """
            {"uuid":null,"start":1000.5,"end":10000.5,"category":"sponsor","actionType":"skip",
             "description":"d","segment":[1.0,10.0],"votes":null,"locked":null,"videoDuration":null,
             "userAgent":"ua","shadowHidden":false}
            """
        );
        using (Assert.Multiple())
        {
            await Assert.That(segment).IsNotNull();
            await Assert.That(segment!.Votes).IsEqualTo(0);
            await Assert.That(segment!.Locked).IsFalse();
            await Assert.That(segment!.VideoDuration).IsEqualTo(0);
        }

        var fractional = Parse<SponsorBlockSegment>(
            """
            {"uuid":null,"start":1000,"end":10000,"category":"sponsor","actionType":"skip",
             "description":"d","segment":[1.0,10.0],"votes":3.4,"locked":false,"videoDuration":1.5e3,
             "userAgent":"ua","shadowHidden":false}
            """
        );
        using (Assert.Multiple())
        {
            await Assert.That(fractional).IsNotNull();
            await Assert.That(fractional!.Votes).IsEqualTo(3);
            await Assert.That(fractional!.VideoDuration).IsEqualTo(1500);
        }
    }

    [Test]
    public async Task FractionalTimestamps()
    {
        var stream = Parse<StreamMetadata>("""{"receivedAt":1712345678901.5,"icy":null}""");
        await Assert.That(stream?.ReceivedAtMilliseconds).IsEqualTo(1712345678902);

        var none = Parse<StreamMetadata>("""{"receivedAt":null,"icy":null}""");
        await Assert.That(none?.ReceivedAtMilliseconds).IsNull();

        var lyrics = JsonSerializer.Deserialize<SyncedLyricsLine>(
            """{"timestamp":12000.6,"duration":900.4,"line":"vocal"}""",
            NyraJson.OptionsFor(true)
        );
        using (Assert.Multiple())
        {
            await Assert.That(lyrics).IsNotNull();
            await Assert.That(lyrics!.Time).IsEqualTo(12001);
            await Assert.That(lyrics!.Duration).IsEqualTo(900);
        }

        var lava = Parse<LavaLyricsLine>(
            """{"timestamp":500.5,"duration":null,"line":"one"}""",
            isNodeLink: false
        );
        await Assert.That(lava?.Timestamp).IsEqualTo(501);
        await Assert.That(lava!.Duration).IsNull();
    }

    [Test]
    public async Task NullBooleansPerEntry()
    {
        var track = Parse<Track>(
            """
            {"encoded":"QQ==","info":{"identifier":"x","author":"a","title":"t","length":1,"isStream":false,"isSeekable":true,"position":0},
             "pluginInfo":{"audioTracks":"[{\"id\":\"en.0\",\"name\":\"English\",\"isDefault\":null,\"isAutoDubbed\":null},{\"id\":\"es.0\",\"name\":\"Spanish\",\"isDefault\":true}]"}}
            """
        );
        var audio = track?.GetYouTubeAudioTracks();
        await Assert.That(audio).IsNotNull();
        await Assert.That(audio!.Count).IsEqualTo(2);
        await Assert.That(audio![0] is { IsDefault: false, IsAutoDubbed: false }).IsTrue();
        await Assert.That(audio![1] is { IsDefault: true }).IsTrue();
    }

    [Test]
    public async Task NullVolumeFailsLoud()
    {
        await Assert
            .That(() =>
            {
                Parse<MixLayer>("""{"id":"m1","volume":null}""");
            })
            .Throws<JsonException>();
    }

    [Test]
    public async Task HandlerCancellationFunnels()
    {
        var seen = new List<Exception>();
        Task Handler(object? sender, System.EventArgs _) => throw new TaskCanceledException();
        Task OnError(Exception exception, System.EventArgs? args)
        {
            seen.Add(exception);
            return Task.CompletedTask;
        }

        var handler = new AsyncEventHandler<System.EventArgs>(Handler);
        var origin = new object();
        handler.RaiseAsync(origin, System.EventArgs.Empty, OnError).GetAwaiter().GetResult();
        await Assert.That(seen.Count).IsEqualTo(0);
    }

    [Test]
    public async Task NanChainReflexivity()
    {
        var chain = new FilterChain { Volume = float.NaN };
        await Assert.That(chain).IsEqualTo(chain);
        await Assert.That(chain.GetHashCode()).IsEqualTo(chain.GetHashCode());
        var twin = new FilterChain { Volume = float.NaN };
        await Assert.That(chain).IsEqualTo(twin);
    }

    [Test]
    public async Task HostileResumeShapes()
    {
        var quoted = Parse<NodeReadyEventArgs>("""{"resumed":"true","sessionId":"s"}""");
        await Assert.That(quoted?.Resumed).IsTrue();

        var numeric = Parse<NodeReadyEventArgs>("""{"resumed":1,"sessionId":"s"}""");
        await Assert.That(numeric?.Resumed).IsTrue();

        var garbage = Parse<NodeReadyEventArgs>("""{"resumed":[],"sessionId":"s"}""");
        await Assert.That(garbage?.Resumed).IsNull();
    }
}
