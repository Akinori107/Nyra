using System.Globalization;
using Nyra.WebSocket;

namespace Nyra.Tests;

public sealed class ErrorThrottleAndWebSocket
{
    [Test]
    public async Task WebSocketSurface()
    {
        using var server = new FakeNodeServer();
        var client = new WebSocketClient();
        await Assert.That(client.IsConnected).IsFalse();

        client.StartReceiving();
        await Assert.That(client.IsConnected).IsFalse();

        var closed = 0;
        client.Closed += (_, _, _) => closed++;

        var headers = new Dictionary<string, string>
        {
            ["Authorization"] = server.Password,
            ["User-Id"] = FakeBridge.UserId.ToString(CultureInfo.InvariantCulture),
            ["Client-Name"] = "websocket-surface/1",
        };
        await client
            .ConnectAsync(
                new Uri($"ws://127.0.0.1:{server.Port}/v4/websocket"),
                headers,
                CancellationToken.None
            )
            .ConfigureAwait(false);
        client.StartReceiving();
        await Assert.That(client.IsConnected).IsTrue();

        await server.DropConnectionAsync(4000, "throttle: close").ConfigureAwait(false);
        await Assert
            .That(() => closed == 1)
            .WaitsFor(assert => assert.IsTrue(), TimeSpan.FromSeconds(5));

        client.Closed += (_, _, _) => throw new InvalidOperationException("hostile listener");

        await client
            .ConnectAsync(
                new Uri($"ws://127.0.0.1:{server.Port}/v4/websocket"),
                headers,
                CancellationToken.None
            )
            .ConfigureAwait(false);
        client.StartReceiving();
        await server.DropConnectionAsync(4001, "throttle: hostile").ConfigureAwait(false);
        await Assert
            .That(() => closed == 2)
            .WaitsFor(assert => assert.IsTrue(), TimeSpan.FromSeconds(5));
        await Assert.That(client.IsConnected).IsFalse();

        await Assert
            .That(() =>
            {
                client.Dispose();
                client.Dispose();
            })
            .ThrowsNothing();
    }
}
