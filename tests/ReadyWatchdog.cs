namespace Nyra.Tests;

public sealed class ReadyWatchdog
{
    private static async Task<Stack> StartAsync(
        FakeNodeServer? server = null,
        TimeSpan? readyTimeout = null
    )
    {
        server ??= new FakeNodeServer();
        var bridge = new FakeBridge();
        bridge.GoReady();
        var extension = new NyraExtension(bridge);
        var node = new Node(
            new Endpoint("127.0.0.1", server.Port, server.Password),
            new NodeOptions
            {
                Name = "watchdog",
                Resuming = true,
                ReconnectDelay = TimeSpan.FromMilliseconds(50),
                MaxReconnectDelay = TimeSpan.FromMilliseconds(200),
                VoiceHandshakeTimeout = TimeSpan.FromSeconds(5),
                ReadyTimeout = readyTimeout ?? TimeSpan.FromSeconds(5),
                MaxReconnectAttempts = 1,
            }
        );
        extension.AddNode(node);
        await extension.ConnectAllNodesAsync().ConfigureAwait(false);
        return new Stack
        {
            Bridge = bridge,
            Extension = extension,
            Node = node,
            Server = server,
        };
    }

    [Test]
    public async Task MissingReadyOp()
    {
        var server = new FakeNodeServer { SuppressReady = true };
        var bridge = new FakeBridge();
        bridge.GoReady();
        var extension = new NyraExtension(bridge);
        var node = new Node(
            new Endpoint("127.0.0.1", server.Port, server.Password),
            new NodeOptions
            {
                Name = "silent",
                ReconnectDelay = TimeSpan.FromMilliseconds(50),
                MaxReconnectDelay = TimeSpan.FromMilliseconds(200),
                ReadyTimeout = TimeSpan.FromMilliseconds(600),
                MaxReconnectAttempts = 1,
            }
        );
        extension.AddNode(node);

        var errors = 0;
        node.Error += (_, _) =>
        {
            Interlocked.Increment(ref errors);
            return Task.CompletedTask;
        };

        await extension.ConnectAllNodesAsync().ConfigureAwait(false);
        await Assert.That(node.IsReady).IsFalse();
        await Assert.That(server.ReadySent).IsFalse();

        await Assert
            .That(() => Volatile.Read(ref errors) >= 1)
            .WaitsFor(assert => assert.IsTrue(), TimeSpan.FromSeconds(2));

        await extension.DisposeAsync().ConfigureAwait(false);
        server.Dispose();
    }

    [Test]
    public async Task ANewSessionStartsFromTheOptionsAgain()
    {
        var server = new FakeNodeServer { ResumeOnReconnect = false };
        var stack = await StartAsync(server).ConfigureAwait(false);
        await using (stack.ConfigureAwait(false))
        {
            var (node, sv) = (stack.Node, stack.Server);
            var sessionBodies = new System.Collections.Concurrent.ConcurrentQueue<string>();
            sv.On("PATCH", "/v4/sessions/fake-session", call =>
            {
                sessionBodies.Enqueue(call.Body ?? string.Empty);
                return new FakeRestResponse(200, """{"resuming":true,"timeout":60}""");
            });
            await node.UpdateSessionAsync(false, TimeSpan.FromSeconds(30)).ConfigureAwait(false);
            sessionBodies.Clear();

            var ready = new TaskCompletionSource();
            node.Ready += (_, _) =>
            {
                ready.TrySetResult();
                return Task.CompletedTask;
            };
            await sv.DropConnectionAsync(1000, "new session").ConfigureAwait(false);
            await ready.Task.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);

            await Assert
                .That(() => sessionBodies.Any(body => body.Contains("\"resuming\":true")))
                .WaitsFor(assert => assert.IsTrue(), TimeSpan.FromSeconds(5));
        }
    }

    [Test]
    public async Task WorkerFailureNoGuilds()
    {
        var stack = await StartAsync().ConfigureAwait(false);
        await using (stack.ConfigureAwait(false))
        {
            var (node, server) = (stack.Node, stack.Server);

            var failed = new TaskCompletionSource<string?>();
            node.WorkerFailed += (_, args) =>
            {
                failed.TrySetResult(args.Message);
                return Task.CompletedTask;
            };

            await server
                .PushAsync(
                    """{"op":"event","type":"WorkerFailedEvent","workerId":7,"message":"the worker died"}"""
                )
                .ConfigureAwait(false);
            await Assert
                .That(await failed.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false))
                .IsEqualTo("the worker died");
        }
    }
}
