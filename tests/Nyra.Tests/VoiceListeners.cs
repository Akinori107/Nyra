using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.Extensions.Time.Testing;
using Nyra.Abstractions;
using static Nyra.Tests.NodeHarness;

namespace Nyra.Tests;

public sealed class VoiceListeners
{
    private sealed class ListeningBridge : FakeBridge, IVoiceListenerSource
    {
        public ConcurrentDictionary<ulong, ulong> Cache { get; } = [];

        public bool Unavailable { get; set; }

        public event EventHandler<ListenerVoiceStateUpdatedEventArgs>? ListenerVoiceStateUpdated;

        public IReadOnlyCollection<ulong>? GetListeners(ulong guildId, ulong channelId) =>
            Unavailable
                ? null
                : [.. Cache.Where(pair => pair.Value == channelId).Select(pair => pair.Key)];

        public void Move(ulong userId, ulong? channelId)
        {
            if (channelId is { } channel)
                Cache[userId] = channel;
            else
                Cache.TryRemove(userId, out _);
            ListenerVoiceStateUpdated?.Invoke(
                this,
                new ListenerVoiceStateUpdatedEventArgs(777, userId, channelId)
            );
        }
    }

    private sealed class Rig : IAsyncDisposable
    {
        public required NyraExtension Extension { get; init; }

        public required FakeNodeServer Server { get; init; }

        public required ListeningBridge Bridge { get; init; }

        public required Node Node { get; init; }

        public required GuildConnection Connection { get; init; }

        public required FakeTimeProvider Time { get; init; }

        public ConcurrentQueue<string> Log { get; } = [];

        public bool Connected => Node.GetConnection(777) is not null;

        public async ValueTask DisposeAsync()
        {
            await Extension.DisposeAsync().ConfigureAwait(false);
            Server.Dispose();
        }
    }

    private static string TrackJson(string id) =>
        $$$"""{"encoded":"enc-{{{id}}}","info":{"identifier":"{{{id}}}","author":"a","title":"{{{id}}}","length":100000,"isStream":false,"isSeekable":true,"position":0}}""";

    private static Task<Rig> StartAsync(
        Func<NodeOptions, NodeOptions> configure,
        params ulong[] listeners
    )
    {
        var bridge = new ListeningBridge();
        foreach (var listener in listeners)
            bridge.Cache[listener] = 555;
        return StartAsync(bridge, configure);
    }

    private static async Task<Rig> StartAsync(
        ListeningBridge bridge,
        Func<NodeOptions, NodeOptions> configure
    )
    {
        var server = new FakeNodeServer();
        var time = new FakeTimeProvider();
        bridge.GoReady();
        var extension = new NyraExtension(bridge);
        var node = new Node(
            new Endpoint("127.0.0.1", server.Port, server.Password),
            configure(
                new NodeOptions
                {
                    Name = "alone",
                    TimeProvider = time,
                    VoiceHandshakeTimeout = TimeSpan.FromSeconds(5),
                    ReadyTimeout = TimeSpan.FromSeconds(5),
                }
            )
        );
        extension.AddNode(node);
        await extension.ConnectAllNodesAsync().ConfigureAwait(false);

        var connection = await ConnectGuildAsync(bridge, node, server).ConfigureAwait(false);

        var current = "null";
        var paused = false;
        server.On(
            "PATCH",
            PlayerPath,
            call =>
            {
                if (call.Body is { } body)
                {
                    using var document = JsonDocument.Parse(body);
                    var root = document.RootElement;
                    if (
                        root.TryGetProperty("track", out var track)
                        && track.ValueKind == JsonValueKind.Object
                        && track.GetProperty("encoded").GetString() is { } encoded
                    )
                        current = TrackJson(encoded[4..]);
                    if (root.TryGetProperty("paused", out var pause))
                        paused = pause.GetBoolean();
                }

                return new FakeRestResponse(
                    200,
                    $$$"""{"guildId":"777","track":{{{current}}},"volume":100,"paused":{{{(paused ? "true" : "false")}}},"state":{"time":0,"position":0,"connected":true,"ping":42},"voice":{"token":"tok","endpoint":"127.0.0.1","sessionId":"vsess"}}"""
                );
            }
        );
        server.On("DELETE", PlayerPath, _ => new FakeRestResponse(204, string.Empty));

        var rig = new Rig
        {
            Extension = extension,
            Server = server,
            Bridge = bridge,
            Node = node,
            Connection = connection,
            Time = time,
        };
        connection.ListenersChanged += (_, e) =>
        {
            rig.Log.Enqueue($"listeners:{e.Listeners}");
            return Task.CompletedTask;
        };
        return rig;
    }

    private static async Task Eventually(Func<bool> condition) =>
        await Assert.That(condition).WaitsFor(assert => assert.IsTrue(), TimeSpan.FromSeconds(5));

    [Test]
    public async Task CountsTheListenersInTheCacheAtConnect()
    {
        var rig = await StartAsync(options => options, 1, 2).ConfigureAwait(false);
        await using (rig.ConfigureAwait(false))
        {
            await Assert.That(rig.Connection.Listeners).IsEqualTo(2);
        }
    }

    [Test]
    public async Task FollowsJoinsLeavesAndMoves()
    {
        var rig = await StartAsync(options => options, 1).ConfigureAwait(false);
        await using (rig.ConfigureAwait(false))
        {
            rig.Bridge.Move(2, 555);
            await Assert.That(rig.Connection.Listeners).IsEqualTo(2);
            rig.Bridge.Move(1, 999);
            await Assert.That(rig.Connection.Listeners).IsEqualTo(1);
            rig.Bridge.Move(3, 999);
            await Assert.That(rig.Connection.Listeners).IsEqualTo(1);
            rig.Bridge.Move(2, null);
            await Assert.That(rig.Connection.Listeners).IsEqualTo(0);

            await Eventually(() => rig.Log.Count == 3).ConfigureAwait(false);
            await Assert
                .That(rig.Log)
                .IsEquivalentTo(["listeners:2", "listeners:1", "listeners:0"]);
        }
    }

    [Test]
    public async Task CountsTheNewChannelAfterAMove()
    {
        var rig = await StartAsync(options => options, 1).ConfigureAwait(false);
        await using (rig.ConfigureAwait(false))
        {
            rig.Bridge.Cache[2] = 999;
            rig.Bridge.Cache[3] = 999;

            rig.Bridge.FireVoiceState(777, 999, "vsess");

            await Eventually(() => rig.Connection.Listeners == 2).ConfigureAwait(false);
            await Assert.That(rig.Connection.ChannelId).IsEqualTo(999UL);
        }
    }

    [Test]
    public async Task StartsCountingWhenTheCacheCatchesUp()
    {
        var bridge = new ListeningBridge { Unavailable = true };
        var rig = await StartAsync(bridge, options => options).ConfigureAwait(false);
        await using (rig.ConfigureAwait(false))
        {
            await Assert.That(rig.Connection.Listeners).IsNull();

            bridge.Unavailable = false;
            bridge.Move(1, 555);

            await Assert.That(rig.Connection.Listeners).IsEqualTo(1);
        }
    }

    [Test]
    public async Task ABridgeWithoutListenersReportsNull()
    {
        var server = new FakeNodeServer();
        var bridge = new FakeBridge();
        bridge.GoReady();
        var extension = new NyraExtension(bridge);
        await using (extension.ConfigureAwait(false))
        using (server)
        {
            var node = new Node(
                new Endpoint("127.0.0.1", server.Port, server.Password),
                new NodeOptions
                {
                    VoiceHandshakeTimeout = TimeSpan.FromSeconds(5),
                    ReadyTimeout = TimeSpan.FromSeconds(5),
                }
            );
            extension.AddNode(node);
            await extension.ConnectAllNodesAsync().ConfigureAwait(false);
            var connection = await ConnectGuildAsync(bridge, node, server).ConfigureAwait(false);

            await Assert.That(connection.Listeners).IsNull();
        }
    }
}
