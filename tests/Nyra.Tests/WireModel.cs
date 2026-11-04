using System.Text.Json;
using Nyra.Entities;
using Nyra.Entities.Filters;
using Nyra.EventArgs;
using Nyra.Internal;

namespace Nyra.Tests;

public sealed class WireModel
{
    [Test]
    public async Task TrackSought()
    {
        var options = NyraJson.SerializerOptions;
        var plain = JsonSerializer.Deserialize<TrackSoughtEventArgs>(
            """{"guildId":77,"position":1500,"duration":500}""",
            options
        );
        await Assert.That(plain!.Position).IsEqualTo(TimeSpan.FromSeconds(1.5));
        await Assert.That(plain.Delta).IsEqualTo(TimeSpan.FromMilliseconds(500));
        var negative = JsonSerializer.Deserialize<TrackSoughtEventArgs>(
            """{"guildId":1,"position":-5,"duration":null}""",
            options
        );
        await Assert.That(negative!.Position).IsEqualTo(TimeSpan.Zero);
        await Assert.That(negative.Delta).IsEqualTo(TimeSpan.Zero);
        var overflow = JsonSerializer.Deserialize<TrackSoughtEventArgs>(
            """{"guildId":1,"position":999999999999999,"duration":999999999999999}""",
            options
        );
        await Assert.That(overflow!.Position).IsEqualTo(TimeSpan.Zero);
        await Assert.That(overflow.Delta).IsEqualTo(TimeSpan.Zero);
    }

    [Test]
    public async Task Meaning()
    {
        var options = NyraJson.SerializerOptions;
        var translation = JsonSerializer.Deserialize<MeaningTranslation>(
            """{"language":{"source":"en","target":"pt"},"title":"T","description":"D","paragraphs":["a",null,"b"]}""",
            options
        );
        await Assert.That(translation!.Paragraphs.Count).IsEqualTo(2);
        await Assert.That(translation.Paragraphs[1]).IsEqualTo("b");
        var translationEcho = JsonSerializer.Deserialize<MeaningTranslation>(
            """{"language":{"source":"en","target":"pt"},"title":"T","description":"D","paragraphs":["a","b"]}""",
            options
        );
        using (Assert.Multiple())
        {
            await Assert.That(translation).IsEqualTo(translationEcho);
            await Assert.That(translation.GetHashCode()).IsEqualTo(translationEcho!.GetHashCode());
            await Assert.That(translation.Equals(null)).IsFalse();
        }
        await Assert
            .That(translation)
            .IsNotEqualTo(
                JsonSerializer.Deserialize<MeaningTranslation>(
                    """{"language":{"source":"en","target":"es"},"title":"X","description":"D","paragraphs":["a"]}""",
                    options
                )
            );

        var meaning = JsonSerializer.Deserialize<MeaningData>(
            """{"title":"M","description":"DD","paragraphs":[null,"p"],"translation":{"language":{"source":"pt","target":"en"},"title":"T2","description":null,"paragraphs":[]},"meta":{"viewCount":1}}""",
            options
        );
        using (Assert.Multiple())
        {
            await Assert.That(meaning!.Paragraphs.Count).IsEqualTo(1);
            await Assert.That(meaning.Translation).IsNotEqualTo(translation);
        }
        var meaningEcho = JsonSerializer.Deserialize<MeaningData>(
            """{"title":"M","description":"DD","paragraphs":["p"],"translation":{"language":{"source":"pt","target":"en"},"title":"T2","description":null,"paragraphs":[]},"meta":{"viewCount":1}}""",
            options
        );
        using (Assert.Multiple())
        {
            await Assert.That(meaning).IsEqualTo(meaningEcho);
            await Assert.That(meaning.GetHashCode()).IsEqualTo(meaningEcho!.GetHashCode());
            await Assert.That(meaning.Equals(null)).IsFalse();
        }
    }

    [Test]
    public async Task ConnectionStatus()
    {
        var options = NyraJson.SerializerOptions;
        var both = JsonSerializer.Deserialize<ConnectionStatus>(
            """{"status":"ok","reason":null,"metrics":null}""",
            options
        );
        await Assert.That(both).IsEqualTo(both);
        var one = JsonSerializer.Deserialize<ConnectionStatus>(
            """{"status":"ok","reason":null,"metrics":{"cpu":1}}""",
            options
        );
        await Assert.That(one).IsNotEqualTo(both);
        var sameA = JsonSerializer.Deserialize<ConnectionStatus>(
            """{"status":"ok","reason":"r","metrics":{"cpu":1,"load":2}}""",
            options
        );
        var sameB = JsonSerializer.Deserialize<ConnectionStatus>(
            """{"status":"ok","reason":"r","metrics":{"cpu":1,"load":2}}""",
            options
        );
        await Assert.That(sameA).IsEqualTo(sameB);
        var diff = JsonSerializer.Deserialize<ConnectionStatus>(
            """{"status":"ok","reason":"r","metrics":{"cpu":9}}""",
            options
        );
        using (Assert.Multiple())
        {
            await Assert.That(sameA).IsNotEqualTo(diff);
            await Assert.That(sameA!.Equals(null)).IsFalse();
            await Assert.That(sameA!.GetHashCode()).IsEqualTo(sameB!.GetHashCode());
        }
    }

    [Test]
    public async Task Stats()
    {
        var options = NyraJson.SerializerOptions;
        var stats = JsonSerializer.Deserialize<DetailedStats>(
            """{"api":{"requests":{"v4":3},"errors":{}},"playback":{"events":{"TrackStartEvent":2}},"sources":{"youtube":{"players":1},"soundcloud":null}}""",
            options
        );
        using (Assert.Multiple())
        {
            await Assert.That(stats!.Api!.Requests!["v4"]).IsEqualTo(3);
            await Assert.That(stats.Api!.Errors!.Count).IsEqualTo(0);
            await Assert.That(stats.Playback!.Events!["TrackStartEvent"]).IsEqualTo(2);
            await Assert.That(stats.Sources!.Count).IsEqualTo(1);
        }
        var echo = JsonSerializer.Deserialize<DetailedStats>(
            """{"api":{"requests":{"v4":3},"errors":{}},"playback":{"events":{"TrackStartEvent":2}},"sources":{"youtube":{"players":1}}}""",
            options
        );
        using (Assert.Multiple())
        {
            await Assert.That(stats.Api).IsEqualTo(echo!.Api);
            await Assert.That(stats.Playback).IsEqualTo(echo.Playback);
            await Assert.That(stats).IsEqualTo(echo);
            await Assert.That(stats.Api.Equals(null)).IsFalse();
            await Assert.That(stats.Playback.Equals(null)).IsFalse();
            await Assert.That(stats.Equals(null)).IsFalse();
        }
        var api = stats!.Api;
        var playback = stats.Playback;
        await Assert.That(api!.GetHashCode()).IsEqualTo(0);
        await Assert.That(playback!.GetHashCode()).IsEqualTo(0);
        await Assert.That(stats.GetHashCode()).IsEqualTo(echo.GetHashCode());
    }

    [Test]
    public async Task Mix()
    {
        var options = NyraJson.SerializerOptions;
        var trackJson =
            """{"encoded":"QXRsZWFzdA==","info":{"identifier":"i","author":"a","title":"t","length":1000,"isStream":false,"isSeekable":true,"position":0},"volume":100}""";
        var layer = JsonSerializer.Deserialize<MixLayer>(
            $$"""{"id":"layer-1","track":{{trackJson}},"volume":0.5,"position":120,"startTime":45000}""",
            options
        );
        await Assert
            .That(layer!.StartTime)
            .IsEqualTo(DateTimeOffset.FromUnixTimeMilliseconds(45000));
        var zero = JsonSerializer.Deserialize<MixLayer>(
            $$"""{"id":"layer-2","track":{{trackJson}},"volume":0,"position":0,"startTime":0}""",
            options
        );
        await Assert.That(zero!.StartTime).IsNull();
        var huge = JsonSerializer.Deserialize<MixLayer>(
            $$"""{"id":"layer-3","track":{{trackJson}},"volume":0,"position":0,"startTime":999999999999999}""",
            options
        );
        await Assert.That(huge!.StartTime).IsNull();
        var layerEcho = JsonSerializer.Deserialize<MixLayer>(
            $$"""{"id":"layer-1","track":{{trackJson}},"volume":0.5,"position":120,"startTime":45000}""",
            options
        );
        using (Assert.Multiple())
        {
            await Assert.That(layer).IsEqualTo(layerEcho);
            await Assert.That(layer).IsNotEqualTo(zero);
            await Assert.That(layer.Equals(null)).IsFalse();
            await Assert.That(layer!.GetHashCode()).IsEqualTo(layerEcho!.GetHashCode());
        }

        var started = JsonSerializer.Deserialize<MixStartedEventArgs>(
            """{"mixId":null,"guildId":5,"track":null}""",
            options
        );
        await Assert.That(started!.MixId).IsEqualTo(string.Empty);
        await Assert.That(started.Track).IsNotNull();
        var ended = JsonSerializer.Deserialize<MixEndedEventArgs>(
            """{"mixId":null,"guildId":5}""",
            options
        );
        await Assert.That(ended!.MixId).IsEqualTo(string.Empty);
    }

    [Test]
    public async Task Transition()
    {
        var options = NyraJson.SerializerOptions;
        await Assert
            .That(
                JsonSerializer
                    .Deserialize<FilterTransition>("""{"durationMs": null}""", options)!
                    .DurationMilliseconds
            )
            .IsNull();
        await Assert
            .That(
                JsonSerializer
                    .Deserialize<FilterTransition>("""{"durationMs": "250"}""", options)!
                    .DurationMilliseconds
            )
            .IsEqualTo(250);
        await Assert
            .That(
                JsonSerializer
                    .Deserialize<FilterTransition>("""{"durationMs": 250.5}""", options)!
                    .DurationMilliseconds
            )
            .IsEqualTo(251);
        await Assert
            .That(
                JsonSerializer
                    .Deserialize<FilterTransition>("""{"durationMs": 250.4}""", options)!
                    .DurationMilliseconds
            )
            .IsEqualTo(250);
        await Assert
            .That(() =>
            {
                _ = JsonSerializer.Deserialize<FilterTransition>(
                    """{"durationMs": "abc"}""",
                    options
                );
            })
            .Throws<JsonException>();
        await Assert
            .That(() =>
            {
                _ = JsonSerializer.Deserialize<FilterTransition>(
                    """{"durationMs": "1e400"}""",
                    options
                );
            })
            .Throws<JsonException>();
        await Assert
            .That(() =>
            {
                _ = JsonSerializer.Deserialize<FilterTransition>(
                    """{"durationMs": 3e9}""",
                    options
                );
            })
            .Throws<JsonException>();
        await Assert
            .That(() =>
            {
                _ = JsonSerializer.Deserialize<FilterTransition>(
                    """{"durationMs": true}""",
                    options
                );
            })
            .Throws<JsonException>();
    }

    [Test]
    public async Task FiniteCheck()
    {
        var distortion = new DistortionFilter(SinOffset: float.NaN);
        await Assert.That(((IFiniteCheck)distortion).HasNonFiniteNumber).IsTrue();
        var distortionClean = new DistortionFilter(SinOffset: 1, Scale: 2);
        await Assert.That(((IFiniteCheck)distortionClean).HasNonFiniteNumber).IsFalse();
        var mix = new ChannelMixFilter(LeftToLeft: float.PositiveInfinity);
        await Assert.That(((IFiniteCheck)mix).HasNonFiniteNumber).IsTrue();
        var mixClean = new ChannelMixFilter(LeftToLeft: 0.5f);
        await Assert.That(((IFiniteCheck)mixClean).HasNonFiniteNumber).IsFalse();
    }

    [Test]
    public async Task IcyMetadata()
    {
        var options = NyraJson.SerializerOptions;
        var meta = JsonSerializer.Deserialize<IcyMetadata>(
            """{"raw":"StreamTitle='x';","streamTitle":"x","streamUrl":null,"fields":{"StreamTitle":"x","dead":null}}""",
            options
        );
        await Assert.That(meta!.Fields!.Count).IsEqualTo(1);
        await Assert.That(meta.Fields["StreamTitle"]).IsEqualTo("x");
        var echo = JsonSerializer.Deserialize<IcyMetadata>(
            """{"raw":"StreamTitle='x';","streamTitle":"x","streamUrl":null,"fields":{"StreamTitle":"x"}}""",
            options
        );
        using (Assert.Multiple())
        {
            await Assert.That(meta).IsEqualTo(echo);
            await Assert.That(meta.Equals(null)).IsFalse();
            await Assert.That(meta!.GetHashCode()).IsEqualTo(echo!.GetHashCode());
        }
    }

    [Test]
    public async Task WireEnum()
    {
        var options = NyraJson.SerializerOptions;
        var curveUnknown = JsonSerializer.Deserialize<Player>(
            """{"crossfade":{"mode":"preload","curve":"bogus"}}""",
            options
        );
        await Assert.That(curveUnknown!.Crossfade!.Curve).IsEqualTo(CrossfadeCurve.Unknown);
        var curveNonString = JsonSerializer.Deserialize<Player>(
            """{"crossfade":{"mode":"preload","curve":5}}""",
            options
        );
        await Assert.That(curveNonString!.Crossfade!.Curve).IsEqualTo(CrossfadeCurve.Unknown);
        var modeUnknown = JsonSerializer.Deserialize<Player>(
            """{"crossfade":{"mode":true,"curve":"equalPower"}}""",
            options
        );
        await Assert.That(modeUnknown!.Crossfade!.Mode).IsEqualTo(CrossfadeMode.Unknown);
        await Assert
            .That(() =>
            {
                JsonSerializer.Serialize(
                    new CrossfadeOptions { Curve = (CrossfadeCurve)42 },
                    options
                );
            })
            .Throws<ArgumentException>();
        await Assert
            .That(() =>
            {
                _ = WireEnumRegistry.For(typeof(string));
            })
            .Throws<KeyNotFoundException>();
        await Assert
            .That(() =>
            {
                _ = WireEnumRegistry.For(typeof(SearchType));
            })
            .Throws<KeyNotFoundException>();
    }

    [Test]
    public async Task UrlStart()
    {
        using (Assert.Multiple())
        {
            await Assert
                .That(UrlStartPosition.ParseMilliseconds("https://x?t=abc&start=90"))
                .IsEqualTo(90_000);
            await Assert
                .That(UrlStartPosition.ParseMilliseconds("https://x?start=oops#t=45"))
                .IsEqualTo(45_000);
            await Assert.That(UrlStartPosition.ParseMilliseconds("https://x?t=")).IsNull();
            await Assert
                .That(UrlStartPosition.ParseMilliseconds("https://x?t=1:23"))
                .IsEqualTo(83_000);
            await Assert
                .That(UrlStartPosition.ParseMilliseconds("https://x?t=1:02:03"))
                .IsEqualTo(3_723_000);
            await Assert
                .That(UrlStartPosition.ParseMilliseconds("https://x?t=123:45:00"))
                .IsEqualTo(445_500_000);
            await Assert.That(UrlStartPosition.ParseMilliseconds("https://x?t=123:45")).IsNull();
            await Assert.That(UrlStartPosition.ParseMilliseconds("https://x?t=1:")).IsNull();
            await Assert.That(UrlStartPosition.ParseMilliseconds("https://x?t=-0:30")).IsNull();
            await Assert.That(UrlStartPosition.ParseMilliseconds("https://x?t=1:2:3:4")).IsNull();
            await Assert
                .That(UrlStartPosition.ParseMilliseconds("https://x?t=9999999999:99"))
                .IsNull();
            await Assert
                .That(UrlStartPosition.ParseMilliseconds("https://x?t=123456789012"))
                .IsNull();
        }
    }
}
