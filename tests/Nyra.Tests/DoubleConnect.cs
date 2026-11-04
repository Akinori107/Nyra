namespace Nyra.Tests;

public sealed class DoubleConnect
{
    [Test]
    public async Task DoubleConnectSettles()
    {
        var server = new FakeNodeServer { HandshakeDelay = TimeSpan.FromMilliseconds(300) };
        var bridge = new FakeBridge();
        bridge.GoReady();
        var extension = new NyraExtension(bridge);
        var node = new Node(
            new Endpoint("127.0.0.1", server.Port, server.Password),
            new NodeOptions
            {
                Name = "double",
                ReconnectDelay = TimeSpan.FromMilliseconds(80),
                MaxReconnectDelay = TimeSpan.FromMilliseconds(200),
                ReadyTimeout = TimeSpan.FromSeconds(5),
            }
        );
        extension.AddNode(node);

        var first = node.ConnectAsync(bridge, extension, CancellationToken.None);
        var second = node.ConnectAsync(bridge, extension, CancellationToken.None);

        var settled = 0;
        Exception? firstError = null;
        Exception? secondError = null;
        try
        {
            await first.WaitAsync(TimeSpan.FromSeconds(8)).ConfigureAwait(false);
            settled++;
        }
        catch (Exception exception)
        {
            firstError = exception;
        }
        try
        {
            await second.WaitAsync(TimeSpan.FromSeconds(8)).ConfigureAwait(false);
            settled++;
        }
        catch (Exception exception)
        {
            secondError = exception;
        }

        await Assert.That(settled).IsEqualTo(2);
        await Assert.That(firstError).IsNull();
        await Assert.That(secondError).IsNull();
        await Assert.That(server.WebsocketConnections).IsLessThanOrEqualTo(2);

        await extension.DisposeAsync().ConfigureAwait(false);
        server.Dispose();
    }
}
