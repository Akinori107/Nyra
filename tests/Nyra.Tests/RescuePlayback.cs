using System.Text.Json;
using Nyra.Entities;
using Nyra.Entities.Filters;
using Nyra.EventArgs;
using static Nyra.Tests.NodeHarness;

namespace Nyra.Tests;

public sealed class RescuePlayback
{
    private const string TrackJson =
        """{"encoded":"QXRsZWFzdA==","info":{"identifier":"ph8qt3dhvwo","author":"SoAD - Topic","title":"ATWA","length":176000,"isStream":false,"isSeekable":true,"position":0},"userData":{"requester":42}}""";

    private static string PlayerBody(bool paused) =>
        $$$"""
            {"guildId":"777","track":{{{TrackJson}}},"volume":60,"paused":{{{(
                paused ? "true" : "false"
            )}}},"filters":{"timescale":{"speed":1.25,"pitch":1.0,"rate":1.0}},"state":{"time":0,"position":0,"connected":true,"ping":42},"voice":{"token":"tok","endpoint":"127.0.0.1","sessionId":"vsess"}}
            """;

    private static Track Track() =>
        JsonSerializer.Deserialize<Track>(TrackJson, NyraJson.SerializerOptions)!;

    private static async Task<(
        FakeBridge Bridge,
        Node Node,
        FakeNodeServer Server,
        GuildConnection Conn,
        NyraExtension Extension
    )> ConnectAsync(bool rescue, TimeSpan? rescueTimeout = null, bool paused = true)
    {
        var server = new FakeNodeServer { ResumeOnReconnect = false };
        var bridge = new FakeBridge();
        bridge.GoReady();
        var extension = new NyraExtension(bridge);
        var node = new Node(
            new Endpoint("127.0.0.1", server.Port, server.Password),
            new NodeOptions
            {
                Name = "rescue",
                RescuePlayback = rescue,
                RescueTimeout = rescueTimeout ?? TimeSpan.FromMinutes(3),
                ReconnectDelay = TimeSpan.FromMilliseconds(50),
                MaxReconnectDelay = TimeSpan.FromMilliseconds(200),
                VoiceHandshakeTimeout = TimeSpan.FromSeconds(5),
                ReadyTimeout = TimeSpan.FromSeconds(5),
            }
        );
        extension.AddNode(node);
        await extension.ConnectAllNodesAsync().ConfigureAwait(false);
        var conn = await ConnectGuildAsync(bridge, node, server).ConfigureAwait(false);
        server.On("PATCH", PlayerPath, _ => new FakeRestResponse(200, PlayerBody(paused)));
        return (bridge, node, server, conn, extension);
    }

    private static async Task<(GuildConnectionRestoredEventArgs Args, string[] Patches)> DropAsync(
        FakeNodeServer server,
        GuildConnection conn
    )
    {
        var restored = new TaskCompletionSource<GuildConnectionRestoredEventArgs>();
        conn.GuildConnectionRestored += (_, args) =>
        {
            restored.TrySetResult(args);
            return Task.CompletedTask;
        };
        var before = server.RestCalls.Count;
        await server.DropConnectionAsync(1000, "rescue").ConfigureAwait(false);
        var args = await restored.Task.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
        var patches = server
            .RestCalls.Skip(before)
            .Where(call => call.Method == "PATCH" && call.Path == PlayerPath)
            .Select(call => call.Body ?? string.Empty)
            .ToArray();
        return (args, patches);
    }

    private static async Task CleanUpAsync(
        FakeNodeServer server,
        GuildConnection conn,
        NyraExtension extension
    )
    {
        await conn.DisconnectAsync().ConfigureAwait(false);
        await extension.DisposeAsync().ConfigureAwait(false);
        server.Dispose();
    }

    [Test]
    public async Task ResendsThePlaybackInOneUpdate()
    {
        var (_, _, server, conn, extension) = await ConnectAsync(rescue: true)
            .ConfigureAwait(false);
        await conn.PlayAsync(Track()).ConfigureAwait(false);
        await conn
            .Filters.ApplyAsync(new FilterChain { Timescale = new TimescaleFilter(Speed: 1.25) })
            .ConfigureAwait(false);

        var (args, patches) = await DropAsync(server, conn).ConfigureAwait(false);

        await Assert.That(args.PlaybackResent).IsTrue();
        var trackPatches = patches.Where(patch => patch.Contains("\"track\"")).ToArray();
        await Assert.That(trackPatches.Length).IsEqualTo(1);
        await Assert.That(patches.Count(patch => patch.Contains("\"voice\""))).IsEqualTo(1);

        using var body = JsonDocument.Parse(trackPatches[0]);
        var root = body.RootElement;
        var track = root.GetProperty("track");
        await Assert.That(track.GetProperty("encoded").GetString()).IsEqualTo("QXRsZWFzdA==");
        await Assert
            .That(track.GetProperty("userData").GetProperty("requester").GetInt32())
            .IsEqualTo(42);
        await Assert.That(root.GetProperty("volume").GetInt32()).IsEqualTo(60);
        await Assert.That(root.GetProperty("paused").GetBoolean()).IsTrue();
        await Assert
            .That(
                root.GetProperty("filters")
                    .GetProperty("timescale")
                    .GetProperty("speed")
                    .GetDouble()
            )
            .IsEqualTo(1.25);
        await Assert.That(root.GetProperty("position").GetInt64()).IsLessThan(5000);

        await CleanUpAsync(server, conn, extension).ConfigureAwait(false);
    }

    [Test]
    [Arguments(false, 180_000)]
    [Arguments(true, 1)]
    public async Task ResendsNothingWhenOffOrExpired(bool rescue, int timeoutMilliseconds)
    {
        var (_, _, server, conn, extension) = await ConnectAsync(
                rescue,
                TimeSpan.FromMilliseconds(timeoutMilliseconds)
            )
            .ConfigureAwait(false);
        await conn.PlayAsync(Track()).ConfigureAwait(false);

        var (args, patches) = await DropAsync(server, conn).ConfigureAwait(false);

        await Assert.That(args.PlaybackResent).IsFalse();
        await Assert.That(patches.Count(patch => patch.Contains("\"track\""))).IsEqualTo(0);
        await Assert.That(patches.Count(patch => patch.Contains("\"voice\""))).IsEqualTo(1);

        await CleanUpAsync(server, conn, extension).ConfigureAwait(false);
    }

    [Test]
    public async Task ResendsFromTheLastMillisecondAtTheEnd()
    {
        var (_, _, server, conn, extension) = await ConnectAsync(rescue: true, paused: false)
            .ConfigureAwait(false);
        await conn.PlayAsync(Track()).ConfigureAwait(false);
        var updated = new TaskCompletionSource();
        conn.PlayerUpdated += (_, _) =>
        {
            updated.TrySetResult();
            return Task.CompletedTask;
        };
        await server
            .PushAsync(
                """{"op":"playerUpdate","guildId":"777","state":{"time":1500,"position":175900,"connected":true,"ping":50}}"""
            )
            .ConfigureAwait(false);
        await updated.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
        await Task.Delay(400).ConfigureAwait(false);

        var (args, patches) = await DropAsync(server, conn).ConfigureAwait(false);

        await Assert.That(args.PlaybackResent).IsTrue();
        using var body = JsonDocument.Parse(patches.Single(patch => patch.Contains("\"track\"")));
        await Assert.That(body.RootElement.GetProperty("position").GetInt64()).IsEqualTo(175_999);

        await CleanUpAsync(server, conn, extension).ConfigureAwait(false);
    }

    [Test]
    public async Task AStoppedNodeInvalidatesInsteadOfRestoring()
    {
        var (_, node, server, conn, extension) = await ConnectAsync(rescue: true)
            .ConfigureAwait(false);
        var invalidated = new TaskCompletionSource();
        var restored = false;
        conn.GuildConnectionInvalidated += (_, _) =>
        {
            invalidated.TrySetResult();
            return Task.CompletedTask;
        };
        conn.GuildConnectionRestored += (_, _) =>
        {
            restored = true;
            return Task.CompletedTask;
        };

        await node.DisconnectAsync().ConfigureAwait(false);
        await extension.ConnectAllNodesAsync().ConfigureAwait(false);
        await invalidated.Task.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);

        await Assert.That(restored).IsFalse();

        await extension.DisposeAsync().ConfigureAwait(false);
        server.Dispose();
    }
}
