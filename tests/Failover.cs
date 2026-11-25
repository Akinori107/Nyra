using System.Text.Json;
using Nyra.Entities;
using Nyra.EventArgs;
using Nyra.Queue;
using static Nyra.Tests.NodeHarness;

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

    private const string One =
        """{"encoded":"QXRsZWFzdA==","info":{"identifier":"one","author":"a","title":"One","length":176000,"isStream":false,"isSeekable":true,"position":0}}""";

    private const string Two =
        """{"encoded":"U2Vjb25k","info":{"identifier":"two","author":"a","title":"Two","length":176000,"isStream":false,"isSeekable":true,"position":0}}""";

    private const string Three =
        """{"encoded":"VGhpcmQ=","info":{"identifier":"three","author":"a","title":"Three","length":176000,"isStream":false,"isSeekable":true,"position":0}}""";

    private static readonly Dictionary<string, string> TracksByEncoded = new()
    {
        ["QXRsZWFzdA=="] = One,
        ["U2Vjb25k"] = Two,
        ["VGhpcmQ="] = Three,
    };

    private static Track Parse(string json) => JsonSerializer.Deserialize<Track>(json)!;

    private static string Path(ulong guild) => $"/v4/sessions/fake-session/players/{guild}";

    private static void Echo(
        FakeNodeServer server,
        ulong guild = 777,
        Func<FakeRestCall, FakeRestResponse?>? intercept = null
    )
    {
        string? current = null;
        server.On("PATCH", Path(guild), call =>
        {
            if (intercept?.Invoke(call) is { } response)
                return response;

            using var body = JsonDocument.Parse(string.IsNullOrEmpty(call.Body) ? "{}" : call.Body);
            if (
                body.RootElement.TryGetProperty("track", out var track)
                && track.ValueKind == JsonValueKind.Object
            )
                current = track.GetProperty("encoded").GetString();

            var json = current is not null ? TracksByEncoded[current] : "null";
            return new FakeRestResponse(
                200,
                $$$"""{"guildId":"{{{guild}}}","track":{{{json}}},"volume":100,"paused":false,"state":{"time":0,"position":0,"connected":true,"ping":1},"voice":{"token":"tok","endpoint":"127.0.0.1","sessionId":"vsess"}}"""
            );
        });
    }

    private static Task<GuildConnectionMigratedEventArgs> Migrated(GuildConnection connection)
    {
        var migrated = new TaskCompletionSource<GuildConnectionMigratedEventArgs>(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        connection.GuildConnectionMigrated += (_, args) =>
        {
            migrated.TrySetResult(args);
            return Task.CompletedTask;
        };
        return migrated.Task;
    }

    private static async Task KillAsync(FakeNodeServer server)
    {
        server.Down = true;
        await server.DropConnectionAsync(1000, "dead").ConfigureAwait(false);
    }

    [Test]
    [Arguments("gapless")]
    [Arguments("crossfading")]
    public async Task AnOldNodeTransitionDoesNotAdvanceTheMigratedQueue(string reason)
    {
        var rig = await RigAsync(TimeSpan.Zero, [true, true]).ConfigureAwait(false);
        await using (rig.ConfigureAwait(false))
        {
            var connection = await ConnectGuildAsync(rig.Bridge, rig.Nodes[0], rig.Servers[0])
                .ConfigureAwait(false);
            Echo(rig.Servers[0]);
            Echo(rig.Servers[1]);
            var entered = new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously
            );
            var release = new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously
            );
            connection.TrackFinished += async (_, _) =>
            {
                entered.TrySetResult();
                await release.Task.ConfigureAwait(false);
            };
            var queue = connection.UseQueue();
            await queue.EnqueueAsync([Parse(One), Parse(Two), Parse(Three)]).ConfigureAwait(false);
            var migrated = Migrated(connection);
            try
            {
                await rig.Servers[0]
                    .PushAsync(
                        $$"""{"op":"event","guildId":"777","type":"TrackEndEvent","track":{{One}},"reason":"{{reason}}"}"""
                    )
                    .ConfigureAwait(false);
                await entered.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
                await KillAsync(rig.Servers[0]).ConfigureAwait(false);
                await Assert
                    .That(() => ReferenceEquals(queue.Connection.Node, rig.Nodes[1]))
                    .WaitsFor(assert => assert.IsTrue(), TimeSpan.FromSeconds(10));
            }
            finally
            {
                release.TrySetResult();
            }
            var args = await migrated.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
            await Assert.That(args.PlaybackResent).IsTrue();
            await Assert.That(queue.Current?.Info.Identifier).IsEqualTo("one");
            await Assert.That(queue.Upcoming.Count).IsEqualTo(2);
            await Assert.That(queue.History.Count).IsEqualTo(0);
            await rig.Servers[1]
                .PushAsync(
                    $$"""{"op":"event","guildId":"777","type":"TrackEndEvent","track":{{One}},"reason":"finished"}"""
                )
                .ConfigureAwait(false);
            await Assert
                .That(() => queue.Current?.Info.Identifier)
                .WaitsFor(assert => assert.IsEqualTo("two"), TimeSpan.FromSeconds(5));
        }
    }
}
