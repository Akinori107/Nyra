using Microsoft.Extensions.Logging;
using Nyra.Abstractions;

namespace Nyra.Tests;

public sealed class ReadyWatchdogAndSuppression
{
    [Test]
    public async Task ErrorSuppression()
    {
        var sink = new List<(LogLevel Level, string Message)>();
        var bridge = new FakeBridge { Logger = new DiscordNetBridgeTests.CapturingLogger(sink) };
        bridge.GoReady();
        var node = new Node(
            new Nyra.Endpoint("127.0.0.1", 1, "pass"),
            new Nyra.NodeOptions { Name = "watchdog-errors" }
        );
        Reflect.Invoke(
            node,
            "AttachClient",
            (IDiscordBridge)bridge,
            new ClientInformation(5UL),
            null,
            0
        );
        var boom = new InvalidOperationException("watchdog: same fault");
        await Assert
            .That(() =>
            {
                for (var i = 0; i < 60; i++)
                    node.HandleError(boom);
            })
            .ThrowsNothing();
        await Assert
            .That(sink.Count(entry => entry.Level == LogLevel.Error))
            .IsLessThanOrEqualTo(5);
        node.Dispose();
    }
}
