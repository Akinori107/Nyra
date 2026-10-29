using System.Text.Json;
using Nyra.Entities;
using Nyra.Entities.Filters;
using static Nyra.Tests.NodeHarness;

namespace Nyra.Tests;

public sealed class PositionPath
{
    private const string TrackJson =
        """{"encoded":"QXRsZWFzdA==","info":{"identifier":"ph8qt3dhvwo","author":"SoAD - Topic","title":"ATWA","length":176000,"isStream":false,"isSeekable":true,"position":0}}""";

    private static string PlayerBody(bool paused, string? filters = null) =>
        $$$"""
            {"guildId":"777","track":{{{TrackJson}}},"volume":100,"paused":{{{(
                paused ? "true" : "false"
            )}}}{{{(filters is null ? string.Empty : ",\"filters\":" + filters)}}},"state":{"time":0,"position":0,"connected":true,"ping":42},"voice":{"token":"tok","endpoint":"127.0.0.1","sessionId":"vsess"}}
            """;

    private static async Task<(
        FakeBridge Bridge,
        Node Node,
        FakeNodeServer Server,
        GuildConnection Conn,
        NyraExtension Extension
    )> ConnectAsync(bool pausedEcho, string? filtersEcho = null)
    {
        var server = new FakeNodeServer();
        var bridge = new FakeBridge();
        bridge.GoReady();
        var extension = new NyraExtension(bridge);
        var node = new Node(
            new Endpoint("127.0.0.1", server.Port, server.Password),
            new NodeOptions
            {
                Name = "position",
                ReconnectDelay = TimeSpan.FromMilliseconds(50),
                MaxReconnectDelay = TimeSpan.FromMilliseconds(200),
                VoiceHandshakeTimeout = TimeSpan.FromSeconds(5),
                ReadyTimeout = TimeSpan.FromSeconds(5),
            }
        );
        extension.AddNode(node);
        await extension.ConnectAllNodesAsync().ConfigureAwait(false);
        server.On(
            "PATCH",
            PlayerPath,
            _ => new FakeRestResponse(200, PlayerBody(pausedEcho, filtersEcho))
        );
        var connect = node.ConnectAsync(777, 555);
        await Task.Delay(100).ConfigureAwait(false);
        bridge.FireVoiceState(777, 555, "vsess");
        bridge.FireVoiceServer(777, "tok", "127.0.0.1");
        var conn = await connect.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
        return (bridge, node, server, conn, extension);
    }

    private static Track Track() =>
        JsonSerializer.Deserialize<Track>(TrackJson, NyraJson.SerializerOptions)!;

    [Test]
    public async Task AnchorAndRegression()
    {
        var (bridge, node, server, conn, extension) = await ConnectAsync(pausedEcho: false)
            .ConfigureAwait(false);
        await conn.PlayAsync(Track()).ConfigureAwait(false);
        await Assert.That(conn.CurrentTrack).IsNotNull();

        var updated = new TaskCompletionSource<long>();
        conn.PlayerUpdated += (_, args) =>
        {
            updated.TrySetResult(args.State.PositionMilliseconds);
            return Task.CompletedTask;
        };
        await server
            .PushAsync(
                """{"op":"playerUpdate","guildId":"777","state":{"time":1500,"position":60000,"connected":true,"ping":50}}"""
            )
            .ConfigureAwait(false);
        await Assert
            .That(await updated.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false))
            .IsEqualTo(60000);
        var anchored = conn.Position;
        await Assert
            .That(
                anchored >= TimeSpan.FromMilliseconds(60_000)
                    && anchored < TimeSpan.FromMilliseconds(63_000)
            )
            .IsTrue();

        var stale = new TaskCompletionSource<long>();
        conn.PlayerUpdated += (_, args) =>
        {
            stale.TrySetResult(args.State.PositionMilliseconds);
            return Task.CompletedTask;
        };
        await server
            .PushAsync(
                """{"op":"playerUpdate","guildId":"777","state":{"time":1000,"position":10,"connected":true,"ping":50}}"""
            )
            .ConfigureAwait(false);
        await stale.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
        await Assert.That(conn.Position >= anchored).IsTrue();

        await conn.DisconnectAsync().ConfigureAwait(false);
        await extension.DisposeAsync().ConfigureAwait(false);
        server.Dispose();
    }

    [Test]
    public async Task PauseFreezeAndSeek()
    {
        var (bridge, node, server, conn, extension) = await ConnectAsync(pausedEcho: false)
            .ConfigureAwait(false);
        await conn.PlayAsync(Track()).ConfigureAwait(false);
        await Assert.That(conn.CurrentTrack).IsNotNull();

        server.On("PATCH", PlayerPath, _ => new FakeRestResponse(200, PlayerBody(true)));
        await conn.PauseAsync().ConfigureAwait(false);
        await Assert.That(conn.IsPaused).IsTrue();
        var frozen = new TaskCompletionSource<long>();
        conn.PlayerUpdated += (_, args) =>
        {
            frozen.TrySetResult(args.State.PositionMilliseconds);
            return Task.CompletedTask;
        };
        await server
            .PushAsync(
                """{"op":"playerUpdate","guildId":"777","state":{"time":2000,"position":65000,"connected":true,"ping":50}}"""
            )
            .ConfigureAwait(false);
        await Assert
            .That(await frozen.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false))
            .IsEqualTo(65000);
        await Assert.That(conn.Position).IsEqualTo(TimeSpan.FromMilliseconds(65_000));

        await server
            .PushAsync("""{"op":"event","guildId":"777","type":"SeekEvent","position":90000}""")
            .ConfigureAwait(false);
        await Task.Delay(150).ConfigureAwait(false);
        await Assert.That(conn.Position).IsEqualTo(TimeSpan.FromMilliseconds(90_000));

        await conn.DisconnectAsync().ConfigureAwait(false);
        await extension.DisposeAsync().ConfigureAwait(false);
        server.Dispose();
    }

    [Test]
    public async Task PaceFold()
    {
        var (bridge, node, server, conn, extension) = await ConnectAsync(
                pausedEcho: false,
                filtersEcho: """{"timescale":{"speed":2}}"""
            )
            .ConfigureAwait(false);
        await conn.PlayAsync(Track()).ConfigureAwait(false);
        await conn
            .Filters.ApplyAsync(new FilterChain { Timescale = new TimescaleFilter { Speed = 2 } })
            .ConfigureAwait(false);
        await Assert.That(conn.Filters.Applied.Timescale).IsNotNull();
        await Assert.That(conn.Filters.Applied.Timescale!.Speed).IsEqualTo(2);

        var updated = new TaskCompletionSource<long>();
        conn.PlayerUpdated += (_, args) =>
        {
            updated.TrySetResult(args.State.PositionMilliseconds);
            return Task.CompletedTask;
        };
        await server
            .PushAsync(
                """{"op":"playerUpdate","guildId":"777","state":{"time":1500,"position":60000,"connected":true,"ping":50}}"""
            )
            .ConfigureAwait(false);
        await updated.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
        await Task.Delay(100).ConfigureAwait(false);

        await server
            .PushAsync("""{"op":"event","guildId":"777","type":"PauseEvent","paused":true}""")
            .ConfigureAwait(false);
        await Task.Delay(150).ConfigureAwait(false);
        await Assert.That(conn.Position > TimeSpan.FromMilliseconds(60_000)).IsTrue();
        await Assert.That(conn.Position <= TimeSpan.FromMilliseconds(176_000)).IsTrue();

        await conn.DisconnectAsync().ConfigureAwait(false);
        await extension.DisposeAsync().ConfigureAwait(false);
        server.Dispose();
    }

    [Test]
    public async Task EndOfTrackClamp()
    {
        var (bridge, node, server, conn, extension) = await ConnectAsync(pausedEcho: false)
            .ConfigureAwait(false);
        await conn.PlayAsync(Track()).ConfigureAwait(false);

        var updated = new TaskCompletionSource<long>();
        conn.PlayerUpdated += (_, args) =>
        {
            updated.TrySetResult(args.State.PositionMilliseconds);
            return Task.CompletedTask;
        };
        await server
            .PushAsync(
                """{"op":"playerUpdate","guildId":"777","state":{"time":1500,"position":175900,"connected":true,"ping":50}}"""
            )
            .ConfigureAwait(false);
        await updated.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
        await Task.Delay(400).ConfigureAwait(false);
        await Assert.That(conn.Position).IsEqualTo(TimeSpan.FromMilliseconds(176_000));

        updated = new TaskCompletionSource<long>();
        await server
            .PushAsync(
                """{"op":"playerUpdate","guildId":"777","state":{"time":3000,"position":200000,"connected":true,"ping":50}}"""
            )
            .ConfigureAwait(false);
        await updated.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
        await Assert.That(conn.Position <= TimeSpan.FromMilliseconds(176_000)).IsTrue();

        await conn.DisconnectAsync().ConfigureAwait(false);
        await extension.DisposeAsync().ConfigureAwait(false);
        server.Dispose();
    }
}
