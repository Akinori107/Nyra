using Nyra.Exceptions;
using static Nyra.Tests.NodeHarness;

namespace Nyra.Tests;

public sealed class JoinCompensation
{
    private static async Task<Stack> StartAsync(
        TimeSpan? handshakeTimeout = null,
        FakeNodeServer? server = null
    )
    {
        server ??= new FakeNodeServer();
        var bridge = new FakeBridge();
        bridge.GoReady();
        var extension = new NyraExtension(bridge);
        var node = new Node(
            new Endpoint("127.0.0.1", server.Port, server.Password),
            new NodeOptions
            {
                Name = "owed",
                Resuming = true,
                ReconnectDelay = TimeSpan.FromMilliseconds(50),
                MaxReconnectDelay = TimeSpan.FromMilliseconds(200),
                VoiceHandshakeTimeout = handshakeTimeout ?? TimeSpan.FromSeconds(5),
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

    private static void ServePlayer(FakeNodeServer server) =>
        server.On("PATCH", PlayerPath, _ => new FakeRestResponse(200, PlayerBody()));

    [Test]
    public async Task AStatelessJoinCompensates()
    {
        var stack = await StartAsync().ConfigureAwait(false);
        await using (stack.ConfigureAwait(false))
        {
            var (bridge, node, server) = (stack.Bridge, stack.Node, stack.Server);
            ServePlayer(server);

            var connect = node.ConnectAsync(777, 555);
            await Task.Delay(100).ConfigureAwait(false);
            bridge.FireVoiceState(777, 555, string.Empty);
            bridge.FireVoiceServer(777, "tok", "127.0.0.1");

            Exception? surfaced = null;
            try
            {
                await connect.ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                surfaced = ex;
            }
            await Assert.That(surfaced is VoiceHandshakeException).IsTrue();
            var compensated = bridge.VoiceSends.Count(s => s is { GuildId: 777, ChannelId: null });
            await Assert.That(compensated).IsGreaterThanOrEqualTo(1);
        }
    }
}
