using Nyra.Entities;
using Nyra.Entities.Filters;

namespace Nyra.Tests;

public sealed class JsonEdge
{
    [Test]
    public async Task ChainHash()
    {
        var one = new FilterChain
        {
            Volume = 0.9f,
            Tremolo = new TremoloFilter { Frequency = 8, Depth = 0.5f },
        };
        var same = new FilterChain
        {
            Volume = 0.9f,
            Tremolo = new TremoloFilter { Frequency = 8, Depth = 0.5f },
        };
        await Assert.That(one.GetHashCode()).IsEqualTo(same.GetHashCode());
    }

    private static string PaddedInfo(int length)
    {
        const string head =
            """{"version":{"semver":"3.9.1"},"isNodelink":true,"plugins":[],"pad":" """;
        const string tail = "\"}";
        return head.TrimEnd()
            + new string('x', length - head.TrimEnd().Length - tail.Length)
            + tail;
    }

    [Test]
    public async Task SuccessBodyBudget()
    {
        const int budget = 64 * 1024 * 1024;
        var oversized = new FakeNodeServer { InfoBody = PaddedInfo(budget + 1) };
        var fitting = new FakeNodeServer { InfoBody = PaddedInfo(budget - 1024) };
        var bridge = new FakeBridge();
        bridge.GoReady();
        var extension = new NyraExtension(bridge);
        static Node NodeFor(FakeNodeServer server, string name) =>
            new(
                new Endpoint("127.0.0.1", server.Port, server.Password),
                new NodeOptions
                {
                    Name = name,
                    ReconnectDelay = TimeSpan.FromMilliseconds(50),
                    MaxReconnectDelay = TimeSpan.FromMilliseconds(200),
                    ReadyTimeout = TimeSpan.FromSeconds(5),
                }
            );
        var tooBig = NodeFor(oversized, "json-edges-over");
        var justFits = NodeFor(fitting, "json-edges-fits");
        extension.AddNode(tooBig);
        extension.AddNode(justFits);
        try
        {
            await extension.ConnectAllNodesAsync().ConfigureAwait(false);
            await Assert.That(justFits.IsReady).IsTrue();
            await Assert.That(justFits.IsNodeLink).IsTrue();
            await Assert.That(tooBig.IsReady).IsFalse();
            await Assert.That(tooBig.Version).IsNull();
        }
        finally
        {
            await extension.DisposeAsync().ConfigureAwait(false);
            oversized.Dispose();
            fitting.Dispose();
        }
    }
}
