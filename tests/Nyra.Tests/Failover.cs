using System.Text.Json;
using Microsoft.Extensions.Time.Testing;
using Nyra.Entities;
using Nyra.EventArgs;
using Nyra.Queue;
using static Nyra.Tests.NodeHarness;

namespace Nyra.Tests;

public sealed class Failover
{
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

    private static Track Parse(string json) =>
        JsonSerializer.Deserialize<Track>(json, NyraJson.SerializerOptions)!;

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

    private static string[] PatchesTo(FakeNodeServer server, ulong guild = 777) =>
        [
            .. server
                .RestCalls.Where(call => call.Method == "PATCH" && call.Path == Path(guild))
                .Select(call => call.Body ?? string.Empty),
        ];

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

    private static async Task<GuildConnection> PlayOnFirstAsync(Rig rig, ulong guild = 777)
    {
        var connection = await ConnectGuildAsync(rig.Bridge, rig.Nodes[0], rig.Servers[0], guild)
            .ConfigureAwait(false);
        Echo(rig.Servers[0], guild);
        await connection.PlayAsync(Parse(One)).ConfigureAwait(false);
        return connection;
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

    private static Task NextReady(NyraExtension extension, Node node)
    {
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        extension.NodeReady += (sender, _) =>
        {
            if (ReferenceEquals(sender, node))
                ready.TrySetResult();
            return Task.CompletedTask;
        };
        return ready.Task;
    }

    private static async Task KillAsync(FakeNodeServer server)
    {
        server.Down = true;
        await server.DropConnectionAsync(1000, "dead").ConfigureAwait(false);
    }

    [Test]
    public async Task MovesTheGuildWithItsTrackWithoutLeaving()
    {
        var rig = await RigAsync(TimeSpan.Zero, [true, true]).ConfigureAwait(false);
        await using (rig.ConfigureAwait(false))
        {
            var connection = await PlayOnFirstAsync(rig).ConfigureAwait(false);
            Echo(rig.Servers[1]);
            var migrated = Migrated(connection);

            await KillAsync(rig.Servers[0]).ConfigureAwait(false);
            var args = await migrated.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);

            await Assert.That(args.From).IsSameReferenceAs(rig.Nodes[0]);
            await Assert.That(args.To).IsSameReferenceAs(rig.Nodes[1]);
            await Assert.That(args.GuildId).IsEqualTo(777UL);
            await Assert.That(args.PlaybackResent).IsTrue();
            await Assert.That(args.Connection.CurrentTrack?.Encoded).IsEqualTo("QXRsZWFzdA==");
            await Assert.That(rig.Extension.GetConnection(777)).IsSameReferenceAs(args.Connection);
            await Assert
                .That(PatchesTo(rig.Servers[1]).Any(body => body.Contains("QXRsZWFzdA==")))
                .IsTrue();
            await Assert.That(rig.Bridge.VoiceSends.Any(send => send.ChannelId is null)).IsFalse();
        }
    }

    [Test]
    public async Task OnlyMovesToANodeOfTheSameKind()
    {
        var rig = await RigAsync(TimeSpan.Zero, [true, false, true], startDown: 2)
            .ConfigureAwait(false);
        await using (rig.ConfigureAwait(false))
        {
            var connection = await PlayOnFirstAsync(rig).ConfigureAwait(false);
            Echo(rig.Servers[1]);
            Echo(rig.Servers[2]);
            var migrated = Migrated(connection);

            await KillAsync(rig.Servers[0]).ConfigureAwait(false);
            await Task.Delay(200).ConfigureAwait(false);
            rig.Servers[2].Down = false;
            var args = await migrated.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);

            await Assert.That(args.To).IsSameReferenceAs(rig.Nodes[2]);
            await Assert.That(PatchesTo(rig.Servers[1])).IsEmpty();
        }
    }

    [Test]
    public async Task KeepsTheGuildWhenTheNodeIsBackInTime()
    {
        var time = new FakeTimeProvider();
        var rig = await RigAsync(TimeSpan.FromSeconds(5), [true, true], time: time)
            .ConfigureAwait(false);
        await using (rig.ConfigureAwait(false))
        {
            var connection = await PlayOnFirstAsync(rig).ConfigureAwait(false);
            Echo(rig.Servers[1]);
            var moved = false;
            connection.GuildConnectionMigrated += (_, _) =>
            {
                moved = true;
                return Task.CompletedTask;
            };
            var ready = NextReady(rig.Extension, rig.Nodes[0]);

            await rig.Servers[0].DropConnectionAsync(1000, "blip").ConfigureAwait(false);
            await AdvanceUntilAsync(
                    time,
                    ready,
                    TimeSpan.FromMilliseconds(50),
                    TimeSpan.FromSeconds(10)
                )
                .ConfigureAwait(false);
            await RunForAsync(time, TimeSpan.FromSeconds(10)).ConfigureAwait(false);

            await Assert.That(moved).IsFalse();
            await Assert.That(rig.Nodes[0].GetConnection(777)).IsSameReferenceAs(connection);
            await Assert.That(PatchesTo(rig.Servers[1])).IsEmpty();
        }
    }

    [Test]
    public async Task TheQueueFollowsTheGuild()
    {
        var rig = await RigAsync(TimeSpan.Zero, [true, true]).ConfigureAwait(false);
        await using (rig.ConfigureAwait(false))
        {
            var connection = await ConnectGuildAsync(rig.Bridge, rig.Nodes[0], rig.Servers[0])
                .ConfigureAwait(false);
            Echo(rig.Servers[0]);
            Echo(rig.Servers[1]);
            var queue = connection.UseQueue();
            await queue.EnqueueAsync([Parse(One), Parse(Two), Parse(Three)]).ConfigureAwait(false);
            var migrated = Migrated(connection);

            await KillAsync(rig.Servers[0]).ConfigureAwait(false);
            var args = await migrated.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);

            await Assert.That(queue.Connection).IsSameReferenceAs(args.Connection);
            await Assert.That(args.Connection.UseQueue()).IsSameReferenceAs(queue);
            await Assert.That(queue.Upcoming.Count).IsEqualTo(2);

            await rig.Servers[1]
                .PushAsync(
                    $$"""{"op":"event","guildId":"777","type":"TrackEndEvent","track":{{One}},"reason":"finished"}"""
                )
                .ConfigureAwait(false);
            await Assert
                .That(() => PatchesTo(rig.Servers[1]).Any(body => body.Contains("U2Vjb25k")))
                .WaitsFor(assert => assert.IsTrue(), TimeSpan.FromSeconds(10));
        }
    }

    [Test]
    public async Task MovesWithoutTheTrackTheNewNodeRefuses()
    {
        var rig = await RigAsync(TimeSpan.Zero, [true, true]).ConfigureAwait(false);
        await using (rig.ConfigureAwait(false))
        {
            var connection = await ConnectGuildAsync(rig.Bridge, rig.Nodes[0], rig.Servers[0])
                .ConfigureAwait(false);
            Echo(rig.Servers[0]);
            Echo(rig.Servers[1], intercept: call =>
                call.Body?.Contains("\"track\"") == true
                    ? new FakeRestResponse(400, """{"message":"Failed to decode track"}""")
                    : null
            );
            var queue = connection.UseQueue();
            await queue.EnqueueAsync([Parse(One), Parse(Two)]).ConfigureAwait(false);
            var migrated = Migrated(connection);

            await KillAsync(rig.Servers[0]).ConfigureAwait(false);
            var args = await migrated.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);

            await Assert.That(args.To).IsSameReferenceAs(rig.Nodes[1]);
            await Assert.That(args.PlaybackResent).IsFalse();
            await Assert.That(queue.Current).IsNull();
            await Assert.That(queue.Upcoming.Count).IsEqualTo(0);
        }
    }

    [Test]
    public async Task AFailedMoveRestoresOnTheNodeThatLostItsSession()
    {
        var rig = await RigAsync(TimeSpan.Zero, [true, true], resume: false).ConfigureAwait(false);
        await using (rig.ConfigureAwait(false))
        {
            var connection = await PlayOnFirstAsync(rig).ConfigureAwait(false);
            using var reached = new ManualResetEventSlim();
            using var release = new ManualResetEventSlim();
            Echo(rig.Servers[1], intercept: _ =>
            {
                reached.Set();
                release.Wait(TimeSpan.FromSeconds(30));
                return new FakeRestResponse(500, """{"message":"boom"}""");
            });
            var moved = false;
            connection.GuildConnectionMigrated += (_, _) =>
            {
                moved = true;
                return Task.CompletedTask;
            };
            var restored = new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously
            );
            connection.GuildConnectionRestored += (_, _) =>
            {
                restored.TrySetResult();
                return Task.CompletedTask;
            };

            try
            {
                await KillAsync(rig.Servers[0]).ConfigureAwait(false);
                await Assert
                    .That(() => reached.IsSet)
                    .WaitsFor(assert => assert.IsTrue(), TimeSpan.FromSeconds(10));
                var ready = NextReady(rig.Extension, rig.Nodes[0]);
                rig.Servers[0].Down = false;
                await ready.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
            }
            finally
            {
                release.Set();
            }

            await restored.Task.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
            await Assert.That(moved).IsFalse();
            await Assert.That(rig.Nodes[0].GetConnection(777)).IsSameReferenceAs(connection);
        }
    }

    [Test]
    public async Task TheOldNodeDestroysThePlayerItStillHas()
    {
        var rig = await RigAsync(TimeSpan.Zero, [true, true]).ConfigureAwait(false);
        await using (rig.ConfigureAwait(false))
        {
            var connection = await PlayOnFirstAsync(rig).ConfigureAwait(false);
            Echo(rig.Servers[1]);
            var migrated = Migrated(connection);

            await KillAsync(rig.Servers[0]).ConfigureAwait(false);
            await migrated.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
            rig.Servers[0]
                .On(
                    "GET",
                    "/v4/sessions/fake-session/players",
                    200,
                    """[{"guildId":"777","volume":100}]"""
                );
            rig.Servers[0].On("DELETE", Path(777), 204, string.Empty);
            rig.Servers[0].Down = false;

            await Assert
                .That(() =>
                    rig.Servers[0]
                        .RestCalls.Any(call => call.Method == "DELETE" && call.Path == Path(777))
                )
                .WaitsFor(assert => assert.IsTrue(), TimeSpan.FromSeconds(10));
            await Assert
                .That(rig.Extension.GetConnection(777)?.Node)
                .IsSameReferenceAs(rig.Nodes[1]);
        }
    }

    [Test]
    public async Task SpreadsTheGuildsOverTheReadyNodes()
    {
        ulong[] guilds = [1001, 1002, 1003, 1004];
        var rig = await RigAsync(TimeSpan.Zero, [true, true, true]).ConfigureAwait(false);
        await using (rig.ConfigureAwait(false))
        {
            var migrations = new List<Task<GuildConnectionMigratedEventArgs>>();
            foreach (var guild in guilds)
            {
                var connection = await PlayOnFirstAsync(rig, guild).ConfigureAwait(false);
                Echo(rig.Servers[1], guild);
                Echo(rig.Servers[2], guild);
                migrations.Add(Migrated(connection));
            }
            await rig.Servers[1].PushAsync(Stats(0)).ConfigureAwait(false);
            await rig.Servers[2].PushAsync(Stats(0)).ConfigureAwait(false);
            await Assert
                .That(() => rig.Nodes[1].Stats is not null && rig.Nodes[2].Stats is not null)
                .WaitsFor(assert => assert.IsTrue(), TimeSpan.FromSeconds(10));

            await KillAsync(rig.Servers[0]).ConfigureAwait(false);
            var moved = await Task.WhenAll(migrations)
                .WaitAsync(TimeSpan.FromSeconds(15))
                .ConfigureAwait(false);

            await Assert.That(moved.Count(args => args.To == rig.Nodes[1])).IsGreaterThan(0);
            await Assert.That(moved.Count(args => args.To == rig.Nodes[2])).IsGreaterThan(0);
        }
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
