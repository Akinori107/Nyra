using NetCord;
using NetCord.Gateway;
using Nyra.NetCord;

namespace Nyra.Tests;

public sealed class AdapterEdge
{
    [Test]
    public async Task NetCord()
    {
        var token = new BotToken("MTIz.ab.cd");
        var client = new GatewayClient(token);
        var bridge = new GatewayBridge(client, _ => { }, _ => { }, _ => { }, _ => { });
        await Assert.That(bridge.Dispose).ThrowsNothing();

        await Assert
            .That(() =>
            {
                _ = ((Node)null!).ConnectAsync(null!);
            })
            .Throws<ArgumentNullException>();

        var node = new Node(
            new Endpoint("127.0.0.1", 3098, "youshallnotpass"),
            new NodeOptions { Name = "adapter-edge-connect" }
        );
        await Assert
            .That(() =>
            {
                _ = node.ConnectAsync(null!);
            })
            .Throws<ArgumentNullException>();
        node.Dispose();

        var sharded = new ShardedGatewayClient(token);
        var extensionA = sharded.UseNyra();
        var extensionB = sharded.UseNyra();
        await Assert.That(ReferenceEquals(extensionA, extensionB)).IsTrue();
        extensionA.Dispose();
    }
}
