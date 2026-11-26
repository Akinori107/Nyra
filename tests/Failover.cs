namespace Nyra.Tests;

public sealed class Failover
{
    private static string Stats(int playing, string? frames = null) =>
        $$"""{"op":"stats","players":{{playing}},"playingPlayers":{{playing}},"uptime":1,"memory":{"free":1,"used":1,"allocated":1,"reservable":1},"cpu":{"cores":2,"systemLoad":0.1,"lavalinkLoad":0.1}{{(
            frames is null ? string.Empty : $",\"frameStats\":{frames}"
        )}}}""";

    private sealed class Rig : IAsyncDisposable
    {
        public required FakeBridge Bridge { get; init; }

        public required NyraExtension Extension { get; init; }

        public required Node[] Nodes { get; init; }

        public required FakeNodeServer[] Servers { get; init; }

        public async ValueTask DisposeAsync()
        {
            await Extension.DisposeAsync().ConfigureAwait(false);
            foreach (var server in Servers)
            {
                server.Down = false;
                server.Dispose();
            }
        }
    }

    private static async Task<Rig> RigAsync(
        TimeSpan failoverAfter,
        bool[] nodeLink,
        bool resume = true,
        TimeProvider? time = null,
        int startDown = -1
    )
    {
        var bridge = new FakeBridge();
        bridge.GoReady();
        var extension = new NyraExtension(bridge, time);
        var servers = new FakeNodeServer[nodeLink.Length];
        var nodes = new Node[nodeLink.Length];
        for (var i = 0; i < nodeLink.Length; i++)
        {
            servers[i] = new FakeNodeServer
            {
                IsNodeLink = nodeLink[i],
                Semver = nodeLink[i] ? "3.9.1" : "4.2.0",
                ResumeOnReconnect = resume,
                Down = i == startDown,
            };
            nodes[i] = new Node(
                new Endpoint("127.0.0.1", servers[i].Port, servers[i].Password),
                new NodeOptions
                {
                    Name = $"node-{i}",
                    FailoverAfter = failoverAfter,
                    ReconnectDelay = TimeSpan.FromMilliseconds(50),
                    MaxReconnectDelay = TimeSpan.FromMilliseconds(200),
                    VoiceHandshakeTimeout = TimeSpan.FromSeconds(5),
                    ReadyTimeout = TimeSpan.FromSeconds(5),
                    TimeProvider = time ?? TimeProvider.System,
                }
            );
            extension.AddNode(nodes[i]);
        }

        await extension.ConnectAllNodesAsync().ConfigureAwait(false);
        return new Rig
        {
            Bridge = bridge,
            Extension = extension,
            Nodes = nodes,
            Servers = servers,
        };
    }

    [Test]
    public async Task GetNodeAvoidsALavalinkNodeThatDropsFrames()
    {
        var rig = await RigAsync(Timeout.InfiniteTimeSpan, [false, false]).ConfigureAwait(false);
        await using (rig.ConfigureAwait(false))
        {
            await rig.Servers[0]
                .PushAsync(Stats(0, """{"sent":100,"nulled":2900,"deficit":2900}"""))
                .ConfigureAwait(false);
            await rig.Servers[1]
                .PushAsync(Stats(0, """{"sent":3000,"nulled":0,"deficit":0}"""))
                .ConfigureAwait(false);
            await Assert
                .That(() => rig.Nodes[0].Stats is not null && rig.Nodes[1].Stats is not null)
                .WaitsFor(assert => assert.IsTrue(), TimeSpan.FromSeconds(10));

            await Assert.That(rig.Extension.GetNode()).IsSameReferenceAs(rig.Nodes[1]);
        }
    }
}
