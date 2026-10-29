using static Nyra.Tests.NodeHarness;

namespace Nyra.Tests;

public sealed class TeardownDeadServer
{
    [Test]
    public async Task TeardownWithTheServerAlreadyDead()
    {
        var server = new FakeNodeServer();
        var bridge = new FakeBridge();
        bridge.GoReady();
        var extension = new NyraExtension(bridge);
        var node = new Node(
            new Endpoint("127.0.0.1", server.Port, server.Password),
            new NodeOptions
            {
                Name = "dead-teardown",
                Resuming = true,
                ReconnectDelay = TimeSpan.FromMilliseconds(50),
                MaxReconnectDelay = TimeSpan.FromMilliseconds(200),
                VoiceHandshakeTimeout = TimeSpan.FromSeconds(5),
                ReadyTimeout = TimeSpan.FromSeconds(5),
            }
        );
        extension.AddNode(node);
        await extension.ConnectAllNodesAsync().ConfigureAwait(false);

        await ConnectGuildAsync(bridge, node, server, 777).ConfigureAwait(false);
        await ConnectGuildAsync(bridge, node, server, 888).ConfigureAwait(false);
        await Assert.That(node.GetConnection(777)).IsNotNull();
        await Assert.That(node.GetConnection(888)).IsNotNull();

        server.Dispose();
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var disposed = extension.DisposeAsync().AsTask();
        var finished = await Task.WhenAny(disposed, Task.Delay(TimeSpan.FromSeconds(14)))
            .ConfigureAwait(false);
        sw.Stop();
        await Assert.That(finished).IsSameReferenceAs(disposed);
        await Assert.That(sw.Elapsed).IsLessThan(TimeSpan.FromSeconds(14));
    }
}
