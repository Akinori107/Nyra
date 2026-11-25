using Nyra.Entities;
using static Nyra.Tests.NodeHarness;

namespace Nyra.Tests;

public sealed class PlayPath
{
    private const string TrackJson =
        """{"encoded":"QXRsZWFzdA==","info":{"identifier":"ph8qt3dhvwo","author":"SoAD - Topic","title":"ATWA","length":176000,"isStream":false,"isSeekable":true,"position":0}}""";

    private static string PlayerWithTrack =>
        $$$"""{"guildId":"777","track":{{{TrackJson}}},"volume":100,"paused":false,"state":{"time":0,"position":0,"connected":true,"ping":42},"voice":{"token":"tok","endpoint":"127.0.0.1","sessionId":"vsess"}}""";

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
                Name = "play",
                ReconnectDelay = TimeSpan.FromMilliseconds(50),
                MaxReconnectDelay = TimeSpan.FromMilliseconds(200),
                VoiceHandshakeTimeout = TimeSpan.FromSeconds(5),
                ReadyTimeout = TimeSpan.FromSeconds(5),
            }
        );
        extension.AddNode(node);
        await extension.ConnectAllNodesAsync().ConfigureAwait(false);
        server.On("PATCH", PlayerPath, _ => new FakeRestResponse(200, PlayerWithTrack));
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
    public async Task IdentifierGuards()
    {
        var stack = await StartAsync(new FakeNodeServer { IsNodeLink = false, Semver = "4.2.0" })
            .ConfigureAwait(false);
        await using (stack.ConfigureAwait(false))
        {
            var conn = stack.Node.GetConnection(777)!;

            await Assert
                .That(async () => await conn.PlayIdentifierAsync(" ").ConfigureAwait(false))
                .Throws<ArgumentException>();

            await Assert
                .That(async () =>
                    await conn.PlayIdentifierAsync("ph8", new PlayOptions { AudioTrackId = "x" })
                        .ConfigureAwait(false)
                )
                .Throws<ArgumentException>();
        }
    }

    [Test]
    public async Task HandlerErrorFloor()
    {
        var stack = await StartAsync().ConfigureAwait(false);
        await using (stack.ConfigureAwait(false))
        {
            var conn = stack.Node.GetConnection(777)!;

            var visits = 0;
            var errorVisits = 0;
            conn.TrackStarted += (_, _) =>
            {
                visits++;
                throw new InvalidOperationException("the started handler broke");
            };
            conn.TrackStarted += (_, _) =>
            {
                visits += 10;
                return Task.CompletedTask;
            };
            conn.HandlerError += (_, _) =>
            {
                errorVisits++;
                throw new InvalidOperationException("the error handler broke");
            };

            await stack
                .Server.PushAsync(
                    $$"""{"op":"event","guildId":"777","type":"TrackStartEvent","track":{{TrackJson}}}"""
                )
                .ConfigureAwait(false);
            await Assert
                .That(() => Volatile.Read(ref visits) == 11)
                .WaitsFor(assert => assert.IsTrue(), TimeSpan.FromSeconds(5));
            await Assert
                .That(() => Volatile.Read(ref errorVisits) == 1)
                .WaitsFor(assert => assert.IsTrue(), TimeSpan.FromSeconds(5));
        }
    }
}
