using static Nyra.Tests.NodeHarness;

namespace Nyra.Tests;

public sealed class RestNullBody
{
    private const string Session = "fake-session";

    private sealed record Stack : IAsyncDisposable
    {
        public required NyraExtension Extension { get; init; }

        public required Node Node { get; init; }

        public required FakeNodeServer Server { get; init; }

        public async ValueTask DisposeAsync()
        {
            await Extension.DisposeAsync().ConfigureAwait(false);
            Server.Dispose();
        }
    }

    private static async Task<Stack> StartAsync()
    {
        var server = new FakeNodeServer();
        var bridge = new FakeBridge();
        bridge.GoReady();
        var extension = new NyraExtension(bridge);
        var node = new Node(
            new Endpoint("127.0.0.1", server.Port, server.Password),
            new NodeOptions
            {
                Name = "null-body",
                Resuming = true,
                ReconnectDelay = TimeSpan.FromMilliseconds(50),
                MaxReconnectDelay = TimeSpan.FromMilliseconds(200),
                VoiceHandshakeTimeout = TimeSpan.FromSeconds(5),
                ReadyTimeout = TimeSpan.FromSeconds(5),
            }
        );
        extension.AddNode(node);
        await extension.ConnectAllNodesAsync().ConfigureAwait(false);
        return new Stack
        {
            Extension = extension,
            Node = node,
            Server = server,
        };
    }

    private static void NullBody(FakeNodeServer server, string method, string path) =>
        server.On(method, path, 200, "null");

    [Test]
    public async Task EmptyBodies()
    {
        var stack = await StartAsync().ConfigureAwait(false);
        await using (stack.ConfigureAwait(false))
        {
            var (node, server) = (stack.Node, stack.Server);

            NullBody(server, "GET", "/v4/info");
            await AwaitRestAsync(() => node.Rest.Info.GetNodeInfoAsync(default))
                .ConfigureAwait(false);

            NullBody(server, "GET", "/v4/loadtracks");
            await AwaitRestAsync(() => node.Rest.Tracks.LoadAsync("x", default))
                .ConfigureAwait(false);

            NullBody(server, "GET", $"/v4/sessions/{Session}/groups");
            var nullGroups = await node.Rest.Groups.ListAsync(default).ConfigureAwait(false);
            await Assert.That(nullGroups.Count).IsEqualTo(0);

            NullBody(server, "POST", $"/v4/sessions/{Session}/groups");
            await AwaitRestAsync(() => node.Rest.Groups.CreateAsync("g1", null, default))
                .ConfigureAwait(false);

            NullBody(server, "GET", "/v4/stats");
            await AwaitRestAsync(() => node.Rest.Info.GetStatsAsync(default)).ConfigureAwait(false);

            NullBody(server, "GET", "/v4/connection");
            await AwaitRestAsync(() => node.Rest.Info.GetConnectionStatusAsync(default))
                .ConfigureAwait(false);

            NullBody(server, "GET", "/v4/routeplanner/status");
            await AwaitRestAsync(() => node.Rest.Info.GetRoutePlannerStatusAsync(default))
                .ConfigureAwait(false);

            NullBody(server, "GET", $"/v4/sessions/{Session}/players/777/sponsorblock");
            await AwaitRestAsync(() => node.Rest.SponsorBlock.GetAsync(777, default))
                .ConfigureAwait(false);

            NullBody(server, "POST", $"/v4/sessions/{Session}/players/777/mix");
            await AwaitRestAsync(() => node.Rest.Mix.CreateAsync(777, "AAA", 0.5f, default))
                .ConfigureAwait(false);

            NullBody(server, "PATCH", $"/v4/sessions/{Session}");
            await AwaitRestAsync(() => node.Rest.Session.UpdateAsync(true, 60, default))
                .ConfigureAwait(false);

            NullBody(server, "GET", "/v4/youtube/config");
            await AwaitRestAsync(() => node.Rest.YouTube.GetConfigAsync(false, default))
                .ConfigureAwait(false);

            NullBody(server, "POST", "/v4/youtube/oauth");
            await AwaitRestAsync(() => node.Rest.YouTube.RefreshTokenAsync("code", default))
                .ConfigureAwait(false);

            NullBody(server, "GET", "/v4/decodetrack");
            await AwaitRestAsync(() => node.Rest.Tracks.DecodeAsync("AAA", default))
                .ConfigureAwait(false);

            NullBody(server, "GET", "/v4/trackstream");
            await AwaitRestAsync(() => node.Rest.Tracks.GetStreamUrlAsync("AAA", null, default))
                .ConfigureAwait(false);

            return;
        }
    }
}
