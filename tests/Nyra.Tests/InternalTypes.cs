using Nyra.Abstractions;
using Nyra.EventArgs;
using Nyra.NetCord;

namespace Nyra.Tests;

public sealed class InternalTypes
{
    [Test]
    public async Task ClientInformation()
    {
        await Assert.That(new ClientInformation(1).CurrentUserId).IsEqualTo(1UL);
    }

    [Test]
    public async Task Guard()
    {
        var node = new Node(new Endpoint("127.0.0.1", 3000, "pass"));
        await Assert
            .That(() =>
            {
                _ = new NodeStatsEventArgs(null!);
            })
            .Throws<ArgumentNullException>()
            .WithParameterName("stats");
        await Assert
            .That(() =>
            {
                _ = new NodeErrorEventArgs(null!, node);
            })
            .Throws<ArgumentNullException>()
            .WithParameterName("exception");
        await Assert
            .That(() =>
            {
                _ = new NodeErrorEventArgs(new InvalidOperationException(), null!);
            })
            .Throws<ArgumentNullException>()
            .WithParameterName("node");
        await Assert
            .That(() =>
            {
                _ = new NodeConnectionStatusEventArgs(null!);
            })
            .Throws<ArgumentNullException>()
            .WithParameterName("status");
        await Assert
            .That(() =>
            {
                _ = ((global::NetCord.Gateway.GatewayClient)null!).UseNyra();
            })
            .Throws<ArgumentNullException>()
            .WithParameterName("client");
    }
}
