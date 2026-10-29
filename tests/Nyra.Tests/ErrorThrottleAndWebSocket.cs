using Microsoft.Extensions.Logging;
using Nyra.Exceptions;
using Nyra.WebSocket;

namespace Nyra.Tests;

public sealed class ErrorThrottleAndWebSocket
{
    private sealed class CapturingLogger : ILogger
    {
        public List<(LogLevel Level, string Message, Exception? Exception)> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter
        ) => Entries.Add((logLevel, formatter(state, exception), exception));
    }

    [Test]
    public async Task ThrottleRollover()
    {
        var sink = new CapturingLogger();
        var refusing = false;
        var server = new FakeNodeServer();
        server.On(
            "PATCH",
            "/v4/sessions/fake-session/players/777",
            _ =>
                refusing
                    ? new FakeRestResponse(500, """{"error":"voice refused"}""")
                    : new FakeRestResponse(
                        200,
                        """{"guildId":"777","track":null,"volume":100,"paused":false,"state":{"time":0,"position":0,"connected":true,"ping":42},"voice":{"token":"tok","endpoint":"127.0.0.1","sessionId":"vsess"}}"""
                    )
        );
        var bridge = new FakeBridge { Logger = sink };
        bridge.GoReady();
        var extension = new NyraExtension(bridge);
        var node = new Node(
            new Nyra.Endpoint("127.0.0.1", server.Port, server.Password),
            new Nyra.NodeOptions
            {
                Name = "throttle-throttle",
                ReconnectDelay = TimeSpan.FromMilliseconds(50),
                MaxReconnectDelay = TimeSpan.FromMilliseconds(200),
                ReadyTimeout = TimeSpan.FromSeconds(5),
            }
        );
        extension.AddNode(node);
        await extension.ConnectAllNodesAsync().ConfigureAwait(false);
        var connect = node.ConnectAsync(777, 555);
        await Task.Delay(100).ConfigureAwait(false);
        bridge.FireVoiceState(777, 555, "vsess");
        bridge.FireVoiceServer(777, "tok", "127.0.0.1");
        await connect.ConfigureAwait(false);

        var reported = 0;
        node.Error += (_, args) =>
        {
            if (args.Exception.InnerException is RestException)
                reported++;
            return Task.CompletedTask;
        };

        long VoicePatches() =>
            server.RestCalls.Count(c =>
                c.Method == "PATCH" && (c.Body?.Contains("\"voice\"") ?? false)
            );

        refusing = true;
        var baseline = VoicePatches();
        for (var i = 0; i < 60; i++)
        {
            bridge.FireVoiceServer(777, $"tok-{i}", "127.0.0.1");
            var target = baseline + i + 1;
            await Assert
                .That(() => VoicePatches() >= target)
                .WaitsFor(assert => assert.IsTrue(), TimeSpan.FromSeconds(5));
        }

        await Task.Delay(300).ConfigureAwait(false);
        var logged = sink.Entries.Count(e =>
            e.Level == LogLevel.Error && e.Exception?.InnerException is RestException
        );
        await Assert.That(logged).IsEqualTo(5);
        await Assert.That(reported).IsEqualTo(5);

        await Task.Delay(31_000).ConfigureAwait(false);
        bridge.FireVoiceServer(777, "tok-rolled", "127.0.0.1");

        await extension.DisposeAsync().ConfigureAwait(false);
        server.Dispose();
    }

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
            ["User-Id"] = FakeBridge.UserId.ToString(),
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
