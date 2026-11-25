using System.Text.Json;
using Microsoft.Extensions.Time.Testing;
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
    )> ConnectAsync(
        bool pausedEcho,
        string? filtersEcho = null,
        FakeNodeServer? lavalink = null,
        TimeProvider? time = null
    )
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
                TimeProvider = time ?? TimeProvider.System,
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
        await WaitForJoinAsync(bridge).ConfigureAwait(false);
        bridge.FireVoiceState(777, 555, "vsess");
        bridge.FireVoiceServer(777, "tok", "127.0.0.1");
        var conn = await connect.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
        return (bridge, node, server, conn, extension);
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

    [Test]
    public async Task AResumeEventDoesNotDiscardAlreadyProjectedPlayback()
    {
        var time = new FakeTimeProvider();
        var (_, _, server, conn, extension) = await ConnectAsync(pausedEcho: false, time: time)
            .ConfigureAwait(false);
        using (server)
        await using (extension.ConfigureAwait(false))
        {
            await conn.PlayAsync(JsonSerializer.Deserialize<Track>(TrackJson)!)
                .ConfigureAwait(false);
            async Task PauseEvent(bool paused)
            {
                var observed = new TaskCompletionSource<bool>(
                    TaskCreationOptions.RunContinuationsAsynchronously
                );
                Task Handler(object? _, Nyra.EventArgs.PauseChangedEventArgs args)
                {
                    observed.TrySetResult(args.IsPaused);
                    return Task.CompletedTask;
                }
                conn.PauseChanged += Handler;
                try
                {
                    await server
                        .PushAsync(
                            $$"""{"op":"event","guildId":"777","type":"PauseEvent","paused":{{(paused ? "true" : "false")}}}"""
                        )
                        .ConfigureAwait(false);
                    await Assert
                        .That(
                            await observed
                                .Task.WaitAsync(TimeSpan.FromSeconds(5))
                                .ConfigureAwait(false)
                        )
                        .IsEqualTo(paused);
                }
                finally
                {
                    conn.PauseChanged -= Handler;
                }
            }
            time.Advance(TimeSpan.FromSeconds(5));
            await Assert.That(conn.Position).IsEqualTo(TimeSpan.FromSeconds(5));
            await PauseEvent(false).ConfigureAwait(false);
            await Assert.That(conn.Position).IsEqualTo(TimeSpan.FromSeconds(5));
            time.Advance(TimeSpan.FromSeconds(2));
            await PauseEvent(true).ConfigureAwait(false);
            time.Advance(TimeSpan.FromSeconds(3));
            await Assert.That(conn.Position).IsEqualTo(TimeSpan.FromSeconds(7));
            await PauseEvent(false).ConfigureAwait(false);
            time.Advance(TimeSpan.FromSeconds(2));
            await Assert.That(conn.Position).IsEqualTo(TimeSpan.FromSeconds(9));
        }
    }
}
