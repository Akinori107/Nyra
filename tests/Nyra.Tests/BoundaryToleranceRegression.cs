using System.Text.Json;
using Nyra.Entities;
using Nyra.Exceptions;

namespace Nyra.Tests;

public sealed class BoundaryToleranceRegression
{
    [Test]
    public async Task RoundedInt64ExactBoundary()
    {
        var options = NyraJson.SerializerOptions;
        var chapter = JsonSerializer.Deserialize<Chapter>(
            """{"startTime":0,"duration":9007199254740993}""",
            options
        );
        await Assert.That(chapter?.Duration).IsEqualTo(9_007_199_254_740_993L);
        var fractional = JsonSerializer.Deserialize<Chapter>(
            """{"startTime":0,"duration":1500.6}""",
            options
        );
        await Assert.That(fractional?.Duration).IsEqualTo(1501);
    }

    [Test]
    public async Task TolerantBooleanStringCase()
    {
        var options = NyraJson.SerializerOptions;
        var fromTrueString = JsonSerializer.Deserialize<SponsorBlockState>(
            """{"enabled":"true"}""",
            options
        );
        await Assert.That(fromTrueString?.Enabled).IsTrue();

        var fromFalseString = JsonSerializer.Deserialize<SponsorBlockState>(
            """{"enabled":"false"}""",
            options
        );
        await Assert.That(fromFalseString?.Enabled).IsFalse();

        var fromNumber = JsonSerializer.Deserialize<SponsorBlockState>(
            """{"enabled":1}""",
            options
        );
        await Assert.That(fromNumber?.Enabled).IsTrue();

        var fromGarbage = JsonSerializer.Deserialize<SponsorBlockState>(
            """{"enabled":"x"}""",
            options
        );
        await Assert.That(fromGarbage?.Enabled).IsFalse();
    }

    [Test]
    public async Task SponsorBlockNullTolerance()
    {
        var options = NyraJson.SerializerOptions;
        var poisoned = JsonSerializer.Deserialize<SponsorBlockState>(
            """{"enabled":null,"skipMarginMs":null}""",
            options
        );
        using (Assert.Multiple())
        {
            await Assert.That(poisoned).IsNotNull();
            await Assert.That(poisoned!.Enabled).IsFalse();
            await Assert.That(poisoned!.SkipMarginMilliseconds).IsEqualTo(0);
        }
        var fromStringMargin = JsonSerializer.Deserialize<SponsorBlockState>(
            """{"enabled":true,"skipMarginMs":"2500"}""",
            options
        );
        await Assert.That(fromStringMargin?.SkipMarginMilliseconds).IsEqualTo(2500);
    }

    [Test]
    public async Task BlackHolePatchBoundedByHandshakeTimeout()
    {
        var server = new FakeNodeServer();
        var bridge = new FakeBridge();
        bridge.GoReady();
        var extension = new NyraExtension(bridge);
        var node = new Node(
            new Endpoint("127.0.0.1", server.Port, server.Password),
            new NodeOptions
            {
                Name = "black-hole",
                ReconnectDelay = TimeSpan.FromMilliseconds(50),
                MaxReconnectDelay = TimeSpan.FromMilliseconds(200),
                VoiceHandshakeTimeout = TimeSpan.FromSeconds(1),
                ReadyTimeout = TimeSpan.FromSeconds(5),
            }
        );
        extension.AddNode(node);
        await extension.ConnectAllNodesAsync().ConfigureAwait(false);

        server.On(
            "PATCH",
            "/v4/sessions/fake-session/players/777",
            call =>
            {
                Thread.Sleep(4000);
                return new FakeRestResponse(
                    200,
                    """{"guildId":"777","track":null,"volume":100,"paused":false,"state":{"time":0,"position":0,"connected":true,"ping":42},"voice":{"token":"tok","endpoint":"127.0.0.1","sessionId":"vsess"}}"""
                );
            }
        );

        var connect = node.ConnectAsync(777, 555);
        await Task.Delay(150).ConfigureAwait(false);
        bridge.FireVoiceState(777, 555, "vsess");
        bridge.FireVoiceServer(777, "tok", "127.0.0.1");

        await Assert
            .That(async () =>
            {
                await connect.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
            })
            .Throws<VoiceHandshakeException>();

        await Assert.That(node.GetConnection(777)).IsNull();
        await Assert
            .That(server.RestCalls)
            .Contains(c =>
                c is { Method: "DELETE", Path: "/v4/sessions/fake-session/players/777" }
            );
        await Assert.That(bridge.VoiceSends).Contains(s => s.GuildId == 777 && s.ChannelId is null);

        await extension.DisposeAsync().ConfigureAwait(false);
        server.Dispose();
    }

    [Test]
    public async Task TrackStreamWhitespaceUrlRejects()
    {
        var server = new FakeNodeServer();
        server.On("GET", "/v4/trackstream", 200, """{"protocol":"http","url":"   "}""");
        var bridge = new FakeBridge();
        bridge.GoReady();
        var extension = new NyraExtension(bridge);
        var node = new Node(
            new Endpoint("127.0.0.1", server.Port, server.Password),
            new NodeOptions
            {
                Name = "stream-blank",
                ReconnectDelay = TimeSpan.FromMilliseconds(50),
                MaxReconnectDelay = TimeSpan.FromMilliseconds(200),
                ReadyTimeout = TimeSpan.FromSeconds(5),
            }
        );
        extension.AddNode(node);
        await extension.ConnectAllNodesAsync().ConfigureAwait(false);

        var track = JsonSerializer.Deserialize<Track>(
            """{"encoded":"AAA","title":"T"}""",
            NyraJson.SerializerOptions
        )!;
        await Assert
            .That(async () =>
            {
                await node.Tracks.GetStreamUrlAsync(track, itag: 140).ConfigureAwait(false);
            })
            .Throws<RestException>();

        await extension.DisposeAsync().ConfigureAwait(false);
        server.Dispose();
    }
}
