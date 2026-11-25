using static Nyra.Tests.NodeHarness;

namespace Nyra.Tests;

public sealed class RegistryTakeover
{
    private static async Task<Stack> StartAsync(FakeNodeServer? server = null)
    {
        server ??= new FakeNodeServer();
        server.On("PATCH", "/v4/sessions/fake-session", 200, """{"resuming":true,"timeout":60}""");
        var bridge = new FakeBridge();
        bridge.GoReady();
        var extension = new NyraExtension(bridge);
        var node = new Node(
            new Endpoint("127.0.0.1", server.Port, server.Password),
            new NodeOptions
            {
                Name = "registry",
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

    [Test]
    public async Task ConnectGuards()
    {
        var orphan = new Node(
            new Endpoint("127.0.0.1", 1, "pass"),
            new NodeOptions { Name = "orphan" }
        );
        await Assert
            .That(async () =>
            {
                await orphan.ConnectAsync(777, 555).ConfigureAwait(false);
            })
            .Throws<InvalidOperationException>();

        var deadServer = new FakeNodeServer();
        var port = deadServer.Port;
        deadServer.Dispose();
        var idleBridge = new FakeBridge();
        idleBridge.GoReady();
        var idleExtension = new NyraExtension(idleBridge);
        var idleNode = new Node(
            new Endpoint("127.0.0.1", port, "pass"),
            new NodeOptions
            {
                Name = "idle",
                ReconnectDelay = TimeSpan.FromMilliseconds(50),
                MaxReconnectDelay = TimeSpan.FromMilliseconds(200),
                ReadyTimeout = TimeSpan.FromMilliseconds(500),
            }
        );
        idleExtension.AddNode(idleNode);
        try
        {
            await idleExtension.ConnectAllNodesAsync().ConfigureAwait(false);
        }
        catch { }
        await Assert
            .That(async () =>
            {
                await idleNode.ConnectAsync(777, 555).ConfigureAwait(false);
            })
            .Throws<InvalidOperationException>();
        await idleExtension.DisposeAsync().ConfigureAwait(false);

        var stack = await StartAsync().ConfigureAwait(false);
        await using (stack.ConfigureAwait(false))
        {
            AcceptPlayerPatch(stack.Server);
            await stack.Extension.DisposeAsync().ConfigureAwait(false);
            await Assert
                .That(async () =>
                {
                    await stack.Node.ConnectAsync(777, 555).ConfigureAwait(false);
                })
                .Throws<ObjectDisposedException>();
            stack.Server.Dispose();
        }
    }
}
