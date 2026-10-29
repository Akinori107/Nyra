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
            errors++;
            return Task.CompletedTask;
        };

        await extension.ConnectAllNodesAsync().ConfigureAwait(false);
        await Assert.That(node.IsReady).IsFalse();
        await Assert.That(server.ReadySent).IsFalse();

        var observed = false;
        for (var i = 0; i < 40 && !observed; i++)
        {
            await Task.Delay(50).ConfigureAwait(false);
            observed = errors >= 1;
        }
        await Assert.That(observed).IsTrue();

        await extension.DisposeAsync().ConfigureAwait(false);
        server.Dispose();
    }

    [Test]
    public async Task FreshSessionInvalidation()
    {
        var server = new FakeNodeServer { ResumeOnReconnect = false };
        var stack = await StartAsync(server).ConfigureAwait(false);
        await using (stack.ConfigureAwait(false))
        {
            var (bridge, node, sv) = (stack.Bridge, stack.Node, stack.Server);
            sv.On(
                "PATCH",
                "/v4/sessions/fake-session/players/777",
                _ => new FakeRestResponse(
                    200,
                    """{"guildId":"777","track":null,"volume":100,"paused":false,"state":{"time":0,"position":0,"connected":true,"ping":42},"voice":{"token":"tok","endpoint":"127.0.0.1","sessionId":"vsess"}}"""
                )
            );
            var connect = node.ConnectAsync(777, 555);
            await Task.Delay(100).ConfigureAwait(false);
            bridge.FireVoiceState(777, 555, "vsess");
            bridge.FireVoiceServer(777, "tok", "127.0.0.1");
            var conn = await connect.ConfigureAwait(false);
            await Assert.That(node.GetConnection(777)).IsNotNull();

            var invalidated = new TaskCompletionSource<ulong>();
            conn.GuildConnectionInvalidated += (_, args) =>
            {
                invalidated.TrySetResult(args.GuildId);
                return Task.CompletedTask;
            };

            await sv.DropConnectionAsync(1000, "fresh session test").ConfigureAwait(false);
            await Assert
                .That(
                    await invalidated.Task.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false)
                )
                .IsEqualTo(777);
            var dropped = false;
            for (var i = 0; i < 60 && !dropped; i++)
            {
                await Task.Delay(50).ConfigureAwait(false);
                dropped = node.GetConnection(777) is null;
            }
            await Assert.That(dropped).IsTrue();
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
