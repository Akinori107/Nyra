namespace Nyra.Tests;

public sealed class ConnectProbeAndResume
{
    private static NyraExtension Rig()
    {
        var bridge = new FakeBridge();
        bridge.GoReady();
        return new NyraExtension(bridge);
    }

    [Test]
    public async Task ResumeAfterDrop()
    {
        using var server = new FakeNodeServer { ResumeOnReconnect = true };
        var extension = Rig();
        var node = new Node(
            new Endpoint("127.0.0.1", server.Port, server.Password),
            new NodeOptions
            {
                Name = "probe-resume",
                ReconnectDelay = TimeSpan.FromMilliseconds(20),
                MaxReconnectDelay = TimeSpan.FromMilliseconds(80),
                ReadyTimeout = TimeSpan.FromSeconds(5),
            }
        );
        extension.AddNode(node);
        await extension.ConnectAllNodesAsync().ConfigureAwait(false);
        await Assert.That(node.IsReady).IsTrue();

        await server.DropConnectionAsync(4000, "probe and resume: drop").ConfigureAwait(false);
        await Assert
            .That(() => server.WebsocketConnections)
            .WaitsFor(assert => assert.IsGreaterThanOrEqualTo(2), TimeSpan.FromSeconds(15));
        await Assert
            .That(() => node.IsReady)
            .WaitsFor(assert => assert.IsTrue(), TimeSpan.FromSeconds(15));
        await node.DisconnectAsync().ConfigureAwait(false);
    }

    [Test]
    public async Task FreshSessionAfterDrop()
    {
        using var server = new FakeNodeServer { ResumeOnReconnect = false };
        var extension = Rig();
        var node = new Node(
            new Endpoint("127.0.0.1", server.Port, server.Password),
            new NodeOptions
            {
                Name = "probe-fresh",
                ReconnectDelay = TimeSpan.FromMilliseconds(20),
                MaxReconnectDelay = TimeSpan.FromMilliseconds(80),
                ReadyTimeout = TimeSpan.FromSeconds(5),
            }
        );
        extension.AddNode(node);
        await extension.ConnectAllNodesAsync().ConfigureAwait(false);
        await Assert.That(node.IsReady).IsTrue();

        await server
            .DropConnectionAsync(4000, "probe and resume: fresh drop")
            .ConfigureAwait(false);
        await Assert
            .That(() => server.WebsocketConnections)
            .WaitsFor(assert => assert.IsGreaterThanOrEqualTo(2), TimeSpan.FromSeconds(15));
        await Assert
            .That(() => node.IsReady)
            .WaitsFor(assert => assert.IsTrue(), TimeSpan.FromSeconds(15));
        await Assert.That(server.LastResumeFlag).IsFalse();
        await node.DisconnectAsync().ConfigureAwait(false);
    }
}
