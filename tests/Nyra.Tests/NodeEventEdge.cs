using Microsoft.Extensions.Logging;
using static Nyra.Tests.NodeHarness;

namespace Nyra.Tests;

public sealed class NodeEventEdge
{
    private static readonly string StatsJson =
        """{"op":"stats","players":1,"playingPlayers":1,"uptime":500,"memory":{"free":1,"used":2,"allocated":3,"reservable":4},"cpu":{"cores":2,"systemLoad":0.1,"lavalinkLoad":0.2}}""";

    private sealed class CapturingLogger(List<string> sink) : ILogger
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
        )
        {
            lock (sink)
                sink.Add($"{logLevel}: {formatter(state, exception)}");
        }
    }

    [Test]
    public async Task ResumedReadyResendsPendingVoice()
    {
        var loggerSink = new List<string>();
        var server = new FakeNodeServer();
        server.On("PATCH", "/v4/sessions/fake-session", 200, """{"resuming":true,"timeout":60}""");
        var bridge = new FakeBridge { Logger = new CapturingLogger(loggerSink) };
        bridge.GoReady();
        var extension = new NyraExtension(bridge);
        var node = new Node(
            new Endpoint("127.0.0.1", server.Port, server.Password),
            new NodeOptions
            {
                Name = "events-edges",
                Resuming = true,
                ReconnectDelay = TimeSpan.FromMilliseconds(50),
                MaxReconnectDelay = TimeSpan.FromMilliseconds(200),
                ReadyTimeout = TimeSpan.FromSeconds(5),
            }
        );
        extension.AddNode(node);
        await extension.ConnectAllNodesAsync().ConfigureAwait(false);
        var sessionPatchSeen = false;
        for (var i = 0; i < 100 && !sessionPatchSeen; i++)
        {
            await Task.Delay(50).ConfigureAwait(false);
            sessionPatchSeen = server.RestCalls.Any(c =>
                c.Method == "PATCH" && c.Path.EndsWith("/sessions/fake-session")
            );
        }
        await Assert.That(sessionPatchSeen).IsTrue();
        {
            bool lockedCheck;
            lock (loggerSink)
            {
                lockedCheck = !loggerSink.Any(l => l.Contains("resuming"));
            }
            await Assert.That(lockedCheck).IsTrue();
        }

        var conn = await ConnectGuildAsync(bridge, node, server).ConfigureAwait(false);

        var forwardFailed = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        node.Error += (_, args) =>
        {
            forwardFailed.TrySetResult(true);
            return Task.CompletedTask;
        };
        server.On("PATCH", PlayerPath, _ => new FakeRestResponse(500, "{}"));
        bridge.FireVoiceServer(777, "tok2", "127.0.0.1");
        await Assert
            .That(await forwardFailed.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false))
            .IsTrue();

        var statsSeen = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        extension.NodeStatsUpdated += (_, _) =>
        {
            statsSeen.TrySetResult(true);
            return Task.CompletedTask;
        };

        AcceptPlayerPatch(server);
        server.SuppressReady = true;
        var patchesWithTok2 = server.RestCalls.Count(c =>
            c.Method == "PATCH" && c.Path == PlayerPath && c.Body?.Contains("tok2") == true
        );
        var connectionsBefore = server.WebsocketConnections;
        await server.DropConnectionAsync(1000, "resume-cycle").ConfigureAwait(false);
        var deadline = Environment.TickCount64 + 5000;
        while (
            Environment.TickCount64 < deadline && server.WebsocketConnections <= connectionsBefore
        )
            await Task.Delay(50).ConfigureAwait(false);
        await Assert.That(server.WebsocketConnections).IsGreaterThan(connectionsBefore);

        await server
            .PushAsync("""{"op":"ready","resumed":true,"sessionId":"fake-session"}""")
            .ConfigureAwait(false);
        await server.PushAsync(StatsJson).ConfigureAwait(false);

        var statsRaised = statsSeen.Task.Wait(TimeSpan.FromSeconds(5));
        await Assert.That(statsRaised).IsTrue();

        var resent = false;
        for (var i = 0; i < 100 && !resent; i++)
        {
            await Task.Delay(50).ConfigureAwait(false);
            resent =
                server.RestCalls.Count(c =>
                    c.Method == "PATCH" && c.Path == PlayerPath && c.Body?.Contains("tok2") == true
                ) > patchesWithTok2;
        }
        await Assert.That(resent).IsTrue();
        await Assert.That(conn.IsConnected).IsTrue();

        await conn.DisconnectAsync().ConfigureAwait(false);
        await extension.DisposeAsync().ConfigureAwait(false);
        server.Dispose();
    }
}
