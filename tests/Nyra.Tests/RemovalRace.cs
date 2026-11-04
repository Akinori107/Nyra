using static Nyra.Tests.NodeHarness;

namespace Nyra.Tests;

public sealed class RemovalRace
{
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
                Name = "race",
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
            Bridge = bridge,
            Extension = extension,
            Node = node,
            Server = server,
        };
    }

    private static string PlayerBody() =>
        """
            {"guildId":"777","track":null,"volume":100,"paused":false,"state":{"time":0,"position":0,"connected":true,"ping":42},"voice":{"token":"tok","endpoint":"127.0.0.1","sessionId":"vsess"}}
            """;

    [Test]
    public async Task KickDuringTheHandshake()
    {
        var stack = await StartAsync().ConfigureAwait(false);
        await using (stack.ConfigureAwait(false))
        {
            var (bridge, node, server) = (stack.Bridge, stack.Node, stack.Server);
            server.On("PATCH", PlayerPath, _ => new FakeRestResponse(200, PlayerBody()));

            var connect = node.ConnectAsync(777, 555);
            await Task.Delay(100).ConfigureAwait(false);

            bridge.FireGuildRemoved(777);
            bridge.FireVoiceState(777, 555, "vsess");
            bridge.FireVoiceServer(777, "tok", "127.0.0.1");

            Nyra.Exceptions.VoiceHandshakeException? kicked = null;
            try
            {
                await connect.ConfigureAwait(false);
            }
            catch (Nyra.Exceptions.VoiceHandshakeException exception)
            {
                kicked = exception;
            }
            await Assert
                .That(server.RestCalls.Any(c => c is { Method: "PATCH", Path: PlayerPath }))
                .IsFalse();
        }
    }

    [Test]
    public async Task DisposeDuringTheGatewayWait()
    {
        var server = new FakeNodeServer();
        var bridge = new FakeBridge();
        var extension = new NyraExtension(bridge);
        var node = new Node(
            new Endpoint("127.0.0.1", server.Port, server.Password),
            new NodeOptions
            {
                Name = "dispose-race",
                ReconnectDelay = TimeSpan.FromMilliseconds(50),
                MaxReconnectDelay = TimeSpan.FromMilliseconds(200),
                ReadyTimeout = TimeSpan.FromSeconds(5),
            }
        );
        extension.AddNode(node);

        var connect = extension.ConnectAllNodesAsync();
        await Task.Delay(100).ConfigureAwait(false);
        await extension.DisposeAsync().ConfigureAwait(false);

        await Assert
            .That(async () => await connect.ConfigureAwait(false))
            .Throws<ObjectDisposedException>();
        await Assert.That(node.IsReady).IsFalse();
        server.Dispose();
    }
}
