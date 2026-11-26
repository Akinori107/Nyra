namespace Nyra.Tests;

public sealed class ConnectBounds
{
    [Test]
    public async Task SecondStopStaysQuiet()
    {
        var server = new FakeNodeServer();
        var bridge = new FakeBridge();
        bridge.GoReady();
        var extension = new NyraExtension(bridge);
        var node = new Node(
            new Endpoint("127.0.0.1", server.Port, server.Password),
            new NodeOptions
            {
                Name = "twice",
                ReconnectDelay = TimeSpan.FromMilliseconds(50),
                MaxReconnectDelay = TimeSpan.FromMilliseconds(200),
                ReadyTimeout = TimeSpan.FromSeconds(5),
                VoiceHandshakeTimeout = TimeSpan.FromSeconds(5),
            }
        );
        extension.AddNode(node);
        await extension.ConnectAllNodesAsync().ConfigureAwait(false);

        await Assert
            .That(async () => await node.DisconnectAsync().ConfigureAwait(false))
            .ThrowsNothing();
        await Assert.That(node.IsConnected).IsFalse();

        await Assert
            .That(async () => await node.DisconnectAsync().ConfigureAwait(false))
            .ThrowsNothing();
        await Assert.That(node.IsReady).IsFalse();
        await extension.DisposeAsync().ConfigureAwait(false);
        server.Dispose();
    }
}
