using Nyra.Entities;
using static Nyra.Tests.NodeHarness;

namespace Nyra.Tests;

public sealed class GuildFeature
{
    private static async Task<Stack> StartAsync(FakeNodeServer? server = null)
    {
        server ??= new FakeNodeServer();
        var bridge = new FakeBridge();
        bridge.GoReady();
        var extension = new NyraExtension(bridge);
        var node = new Node(
            new Endpoint("127.0.0.1", server.Port, server.Password),
            new NodeOptions
            {
                Name = "features",
                ReconnectDelay = TimeSpan.FromMilliseconds(50),
                MaxReconnectDelay = TimeSpan.FromMilliseconds(200),
                VoiceHandshakeTimeout = TimeSpan.FromSeconds(5),
                ReadyTimeout = TimeSpan.FromSeconds(5),
            }
        );
        extension.AddNode(node);
        await extension.ConnectAllNodesAsync().ConfigureAwait(false);
        server.On("PATCH", PlayerPath, _ => new FakeRestResponse(
            200,
            """{"guildId":"777","track":null,"volume":100,"paused":false,"state":{"time":0,"position":0,"connected":true,"ping":42},"voice":{"token":"tok","endpoint":"127.0.0.1","sessionId":"vsess"}}"""
        ));
        var connect = node.ConnectAsync(777, 555);
        await WaitForJoinAsync(bridge).ConfigureAwait(false);
        bridge.FireVoiceState(777, 555, "vsess");
        bridge.FireVoiceServer(777, "tok", "127.0.0.1");
        await connect.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
        return new Stack
        {
            Bridge = bridge,
            Extension = extension,
            Node = node,
            Server = server,
        };
    }

    [Test]
    public async Task SponsorBlockGuards()
    {
        var stack = await StartAsync().ConfigureAwait(false);
        await using (stack.ConfigureAwait(false))
        {
            var conn = stack.Node.GetConnection(777)!;
            var path = "/v4/sessions/fake-session/players/777/sponsorblock";

            await Assert
                .That(async () =>
                {
                    await conn.SponsorBlock.SetAsync(null!).ConfigureAwait(false);
                })
                .Throws<ArgumentNullException>();

            await Assert
                .That(async () =>
                {
                    await conn
                        .SponsorBlock.SetAsync(
                            new SponsorBlockSettings { SkipMarginMilliseconds = -1 }
                        )
                        .ConfigureAwait(false);
                })
                .Throws<ArgumentOutOfRangeException>();

            stack.Server.On("PATCH", path, _ => new FakeRestResponse(
                200,
                """{"enabled":true,"categories":["sponsor"]}"""
            ));
            var state = await conn
                .SponsorBlock.SetAsync(
                    new SponsorBlockSettings { Enabled = true, Categories = ["sponsor"] }
                )
                .ConfigureAwait(false);
            await Assert.That(state is { Enabled: true, Categories: ["sponsor"] }).IsTrue();

            var segments = new SponsorBlockSegment[][]
            {
                [null!],
                [new SponsorBlockSegment { Start = -1, End = 5 }],
                [new SponsorBlockSegment { Start = 1, End = -5 }],
                [new SponsorBlockSegment { Start = 10, End = 5 }],
            };
            foreach (var list in segments)
                await Assert
                    .That(async () =>
                    {
                        await conn.SponsorBlock.SetSegmentsAsync(list).ConfigureAwait(false);
                    })
                    .Throws<ArgumentException>();

            stack.Server.On("POST", path, _ => new FakeRestResponse(
                200,
                """{"enabled":true,"segments":[]}"""
            ));
            await conn
                .SponsorBlock.SetSegmentsAsync([
                    new SponsorBlockSegment
                    {
                        Uuid = "u1",
                        Start = 1,
                        End = 2,
                    },
                ])
                .ConfigureAwait(false);
        }
    }

    [Test]
    public async Task LyricsSubscription()
    {
        var stack = await StartAsync().ConfigureAwait(false);
        await using (stack.ConfigureAwait(false))
        {
            var conn = stack.Node.GetConnection(777)!;
            var subPath = "/v4/sessions/fake-session/players/777/lyrics/subscribe";

            var subscribed = new TaskCompletionSource<string?>();
            stack.Server.On("POST", subPath, call =>
            {
                subscribed.TrySetResult(call.Query);
                return new FakeRestResponse(204, string.Empty);
            });
            await conn.Lyrics.SubscribeAsync(skipTrackSource: true).ConfigureAwait(false);
            await Assert
                .That(
                    await subscribed.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false)
                )
                .Contains("skipTrackSource=true");
            await Assert
                .That(stack.Server.RestCalls.Count(c => c.Method == "POST" && c.Path == subPath))
                .IsEqualTo(1);

            stack.Server.On("DELETE", subPath, _ => new FakeRestResponse(204, string.Empty));
            await conn.Lyrics.UnsubscribeAsync().ConfigureAwait(false);
            await Assert
                .That(stack.Server.RestCalls.Count(c => c.Method == "DELETE" && c.Path == subPath))
                .IsEqualTo(1);
        }
    }
}
