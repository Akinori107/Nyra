using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using static Nyra.Tests.NodeHarness;

namespace Nyra.Tests;

public sealed class GenerationStormAndPlayback
{
    private static string PlayerJson(
        string? trackJson,
        bool paused = false,
        int volume = 100,
        string? stateJson = null
    ) => $$"""
        {
            "guildId": "777",
            "track": {{trackJson ?? "null"}},
            "volume": {{volume}},
            "paused": {{(paused ? "true" : "false")}},
            "state": {{stateJson ?? "null"}},
            "voice": {"token": "tok", "endpoint": "127.0.0.1", "sessionId": "vsess"}
        }
        """;

    private sealed class Rig : IAsyncDisposable
    {
        public required FakeBridge Bridge { get; init; }
        public required NyraExtension Extension { get; init; }
        public required Node Node { get; init; }
        public required FakeNodeServer Server { get; init; }
        public required GuildConnection Connection { get; init; }
        public required List<string> Reply { get; init; }
        public required List<TaskCompletionSource?> Gate { get; init; }
        public string PlayerReply
        {
            get => Reply[0];
            set => Reply[0] = value;
        }

        public TaskCompletionSource? GetGate
        {
            get => Gate[0];
            set => Gate[0] = value;
        }

        public int RestCalls => Server.RestCalls.Count;

        public async ValueTask DisposeAsync()
        {
            await Extension.DisposeAsync().ConfigureAwait(false);
            Server.Dispose();
        }
    }

    private static async Task<Rig> RigAsync(
        bool lavalink = false,
        bool multiGuild = false,
        TimeSpan? handshakeTimeout = null,
        TimeSpan? readyTimeout = null,
        TimeProvider? time = null
    )
    {
        var server = new FakeNodeServer
        {
            IsNodeLink = !lavalink,
            Semver = lavalink ? "4.2.0" : "3.9.1",
        };
        var holder = new List<string> { PlayerJson(null) };
        var gate = new List<TaskCompletionSource?> { null };
        server.On("PATCH", "/v4/sessions/fake-session/players/777", _ => new FakeRestResponse(
            200,
            holder[0]
        ));
        server.OnRaw("GET", "/v4/sessions/fake-session/players/777", async (_, stream) =>
        {
            var pending = gate[0];
            if (pending is not null)
                await pending.Task.ConfigureAwait(false);
            var body = System.Text.Encoding.UTF8.GetBytes(holder[0]);
            var head =
                $"HTTP/1.1 200 OK\r\nContent-Type: application/json\r\nContent-Length: {body.Length}\r\nConnection: close\r\n\r\n";
            await stream.WriteAsync(System.Text.Encoding.UTF8.GetBytes(head)).ConfigureAwait(false);
            await stream.WriteAsync(body).ConfigureAwait(false);
            await stream.FlushAsync().ConfigureAwait(false);
            stream.Close();
        });
        if (multiGuild)
            for (var guild = 0; guild < 12; guild++)
            {
                var guildId = 900_000UL + (ulong)guild;
                server.On(
                    "PATCH",
                    $"/v4/sessions/fake-session/players/{guildId}",
                    _ => new FakeRestResponse(200, PlayerJson(null))
                );
                server.On(
                    "GET",
                    $"/v4/sessions/fake-session/players/{guildId}",
                    _ => new FakeRestResponse(200, PlayerJson(null))
                );
            }
        var bridge = new FakeBridge();
        bridge.GoReady();
        var extension = new NyraExtension(bridge, time);
        var node = new Node(
            new Endpoint("127.0.0.1", server.Port, server.Password),
            new NodeOptions
            {
                Name = "storm",
                TimeProvider = time ?? TimeProvider.System,
                Resuming = true,
                ReconnectDelay = TimeSpan.FromMilliseconds(50),
                MaxReconnectDelay = TimeSpan.FromMilliseconds(200),
                VoiceHandshakeTimeout = handshakeTimeout ?? TimeSpan.FromSeconds(5),
                ReadyTimeout = readyTimeout ?? TimeSpan.FromSeconds(5),
            }
        );
        extension.AddNode(node);
        await extension.ConnectAllNodesAsync().ConfigureAwait(false);
        var connect = node.ConnectAsync(777, 555);
        await WaitForJoinAsync(bridge).ConfigureAwait(false);
        bridge.FireVoiceState(777, 555, "vsess");
        bridge.FireVoiceServer(777, "tok", "127.0.0.1");
        var connection = await connect.ConfigureAwait(false);
        return new Rig
        {
            Bridge = bridge,
            Extension = extension,
            Node = node,
            Server = server,
            Connection = connection,
            Reply = holder,
            Gate = gate,
        };
    }

    [Test]
    public async Task HandshakeStorm()
    {
        var time = new FakeTimeProvider();
        var rig = await RigAsync(handshakeTimeout: TimeSpan.FromMilliseconds(250), time: time)
            .ConfigureAwait(false);
        await using (rig.ConfigureAwait(false))
        {
            var raced = 0;
            var failed = 0;
            for (var round = 0; round < 10; round++)
            {
                var connects = new Task[6];
                for (var attempt = 0; attempt < connects.Length; attempt++)
                    connects[attempt] = rig.Node.ConnectAsync(888_000UL, 555);
                var all = Task.WhenAll(connects);
                await AdvanceUntilAsync(
                        time,
                        all,
                        TimeSpan.FromMilliseconds(100),
                        TimeSpan.FromSeconds(10)
                    )
                    .ConfigureAwait(false);
                try
                {
                    await all.ConfigureAwait(false);
                }
                catch
                {
                    failed++;
                }
                if (connects.Any(c => c.IsFaulted))
                    raced++;
            }
            await Assert.That(failed).IsEqualTo(10);
            await Assert.That(raced).IsEqualTo(10);
            await RunForAsync(time, TimeSpan.FromMilliseconds(600)).ConfigureAwait(false);
            await Assert
                .That(() => rig.Node.GetConnection(888_000UL) is null)
                .WaitsFor(assert => assert.IsTrue(), TimeSpan.FromSeconds(5));
        }
    }

    private sealed class ThrowingLogger : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter
        ) => throw new InvalidOperationException("generation storm: the logger itself dies");
    }
}
