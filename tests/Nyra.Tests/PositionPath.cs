using System.Text.Json;
using Nyra.Entities;
using static Nyra.Tests.NodeHarness;

namespace Nyra.Tests;

public sealed class PositionPath
{
    private const string TrackJson =
        """{"encoded":"QXRsZWFzdA==","info":{"identifier":"ph8qt3dhvwo","author":"SoAD - Topic","title":"ATWA","length":176000,"isStream":false,"isSeekable":true,"position":0}}""";

    private static string PlayerBody(
        bool paused,
        string? filters = null,
        long positionMilliseconds = 0
    ) =>
        $$$"""
            {"guildId":"777","track":{{{TrackJson}}},"volume":100,"paused":{{{(
                paused ? "true" : "false"
            )}}}{{{(filters is null ? string.Empty : ",\"filters\":" + filters)}}},"state":{"time":0,"position":{{{positionMilliseconds}}},"connected":true,"ping":42},"voice":{"token":"tok","endpoint":"127.0.0.1","sessionId":"vsess"}}
            """;

    private static async Task<(
        FakeBridge Bridge,
        Node Node,
        FakeNodeServer Server,
        GuildConnection Conn,
        NyraExtension Extension
    )> ConnectAsync(bool pausedEcho, string? filtersEcho = null, FakeNodeServer? lavalink = null)
    {
        var server = lavalink ?? new FakeNodeServer();
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
        server.On("PATCH", PlayerPath, _ => new FakeRestResponse(
            200,
            PlayerBody(pausedEcho, filtersEcho)
        ));
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
        var (_, _, server, conn, extension) = await ConnectAsync(pausedEcho: false)
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
    [Arguments(170_000L, null, false, 0, 5)]
    [Arguments(170_000L, 30, false, 30, 35)]
    [Arguments(90_000L, null, true, 90, 95)]
    public async Task PlayAnchorsTheStartInsteadOfTheResponse(
        long serverPosition,
        int? startSeconds,
        bool noReplace,
        int fromSeconds,
        int toSeconds
    )
    {
        var (_, _, server, conn, extension) = await ConnectAsync(pausedEcho: false)
            .ConfigureAwait(false);
        server.On("PATCH", PlayerPath, _ => new FakeRestResponse(
            200,
            PlayerBody(paused: false, positionMilliseconds: serverPosition)
        ));

        await conn.PlayAsync(
                Track(),
                new PlayOptions(
                    StartPosition: startSeconds is { } start ? TimeSpan.FromSeconds(start) : null,
                    NoReplace: noReplace
                )
            )
            .ConfigureAwait(false);

        await Assert
            .That(
                conn.Position >= TimeSpan.FromSeconds(fromSeconds)
                    && conn.Position < TimeSpan.FromSeconds(toSeconds)
            )
            .IsTrue();

        await conn.DisconnectAsync().ConfigureAwait(false);
        await extension.DisposeAsync().ConfigureAwait(false);
        server.Dispose();
    }

    [Test]
    public async Task PlayIdentifierAnchorsAtTheStartPosition()
    {
        var (_, _, server, conn, extension) = await ConnectAsync(
                pausedEcho: false,
                lavalink: new FakeNodeServer { IsNodeLink = false, Semver = "4.2.0" }
            )
            .ConfigureAwait(false);
        server.On("PATCH", PlayerPath, _ => new FakeRestResponse(
            200,
            PlayerBody(paused: false, positionMilliseconds: 170_000)
        ));

        await conn.PlayIdentifierAsync(
                "https://example.test/a.mp3",
                new PlayOptions(StartPosition: TimeSpan.FromSeconds(20))
            )
            .ConfigureAwait(false);

        await Assert
            .That(
                conn.Position >= TimeSpan.FromSeconds(20)
                    && conn.Position < TimeSpan.FromSeconds(25)
            )
            .IsTrue();

        await conn.DisconnectAsync().ConfigureAwait(false);
        await extension.DisposeAsync().ConfigureAwait(false);
        server.Dispose();
    }
}
