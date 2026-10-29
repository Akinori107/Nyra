namespace Nyra.Tests;

public sealed class SupervisionLoop
{
    private static async Task<Node> ConnectNodeAsync(
        FakeNodeServer server,
        NyraExtension extension,
        string name,
        int maxReconnectAttempts = -1
    )
    {
        var node = new Node(
            new Endpoint("127.0.0.1", server.Port, server.Password),
            new NodeOptions
            {
                Name = name,
                Resuming = true,
                ReconnectDelay = TimeSpan.FromMilliseconds(50),
                MaxReconnectDelay = TimeSpan.FromMilliseconds(200),
                VoiceHandshakeTimeout = TimeSpan.FromSeconds(5),
                ReadyTimeout = TimeSpan.FromSeconds(5),
                MaxReconnectAttempts = maxReconnectAttempts,
            }
        );
        extension.AddNode(node);
        await extension.ConnectAllNodesAsync().ConfigureAwait(false);
        return node;
    }

    [Test]
    public async Task DropAndReconnect()
    {
        var server = new FakeNodeServer();
        var bridge = new FakeBridge();
        bridge.GoReady();
        var extension = new NyraExtension(bridge);
        var node = await ConnectNodeAsync(server, extension, "drop").ConfigureAwait(false);
        await Assert.That(node.IsReady).IsTrue();

        var disconnected = new TaskCompletionSource<bool>();
        node.Disconnected += (_, args) =>
        {
            if (!args.Final)
                disconnected.TrySetResult(true);
            return Task.CompletedTask;
        };
        var reconnected = new TaskCompletionSource<bool>();
        node.Connected += (_, _) =>
        {
            reconnected.TrySetResult(true);
            return Task.CompletedTask;
        };

        await server.DropConnectionAsync(1000, "test drop").ConfigureAwait(false);
        await Assert
            .That(await disconnected.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false))
            .IsTrue();
        await Assert
            .That(await reconnected.Task.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false))
            .IsTrue();
        await Assert.That(node.IsReady).IsTrue();
        await Assert.That(server.LastResumeFlag).IsTrue();

        await extension.DisposeAsync().ConfigureAwait(false);
        server.Dispose();
    }

    [Test]
    public async Task ExhaustedAttempts()
    {
        var server = new FakeNodeServer();
        var bridge = new FakeBridge();
        bridge.GoReady();
        var extension = new NyraExtension(bridge);
        var node = await ConnectNodeAsync(server, extension, "hostile", maxReconnectAttempts: 2)
            .ConfigureAwait(false);
        await Assert.That(node.IsReady).IsTrue();

        var final = new TaskCompletionSource<Exception?>();
        node.Disconnected += (_, args) =>
        {
            if (args.Final)
                final.TrySetResult(args.Exception);
            return Task.CompletedTask;
        };

        server.InfoStatus = 401;
        await server.DropConnectionAsync(1000, "turning hostile").ConfigureAwait(false);

        await Assert
            .That(() => final.Task.IsCompleted)
            .WaitsFor(assert => assert.IsTrue(), TimeSpan.FromSeconds(15));
        await Assert.That(node.IsConnected).IsFalse();
        await Assert.That(node.IsReady).IsFalse();

        await extension.DisposeAsync().ConfigureAwait(false);
        server.Dispose();
    }

    [Test]
    public async Task NeverConnected()
    {
        var deadServer = new FakeNodeServer();
        var port = deadServer.Port;
        deadServer.Dispose();
        var bridge = new FakeBridge();
        bridge.GoReady();
        var extension = new NyraExtension(bridge);
        var node = new Node(
            new Endpoint("127.0.0.1", port, "pass"),
            new NodeOptions
            {
                Name = "dead",
                ReconnectDelay = TimeSpan.FromMilliseconds(50),
                MaxReconnectDelay = TimeSpan.FromMilliseconds(200),
                ReadyTimeout = TimeSpan.FromMilliseconds(500),
                MaxReconnectAttempts = 1,
            }
        );
        extension.AddNode(node);
        try
        {
            await extension.ConnectAllNodesAsync().ConfigureAwait(false);
        }
        catch { }

        var final = new TaskCompletionSource<Exception?>();
        node.Disconnected += (_, args) =>
        {
            if (args.Final)
                final.TrySetResult(args.Exception);
            return Task.CompletedTask;
        };

        await Assert
            .That(() => final.Task.IsCompleted)
            .WaitsFor(assert => assert.IsTrue(), TimeSpan.FromSeconds(15));
        await Assert.That(node.IsConnected).IsFalse();

        await extension.DisposeAsync().ConfigureAwait(false);
    }

    [Test]
    public async Task CleanStop()
    {
        var server = new FakeNodeServer();
        var bridge = new FakeBridge();
        bridge.GoReady();
        var extension = new NyraExtension(bridge);
        var node = await ConnectNodeAsync(server, extension, "stopped").ConfigureAwait(false);
        var finalRaised = false;
        node.Disconnected += (_, args) =>
        {
            if (args.Final)
                finalRaised = true;
            return Task.CompletedTask;
        };

        await node.DisconnectAsync().ConfigureAwait(false);
        await Assert.That(node.IsConnected).IsFalse();
        await Task.Delay(300).ConfigureAwait(false);
        await Assert.That(finalRaised).IsFalse();

        await extension.ConnectAllNodesAsync().ConfigureAwait(false);
        await Assert.That(node.IsReady).IsTrue();
        await extension.DisposeAsync().ConfigureAwait(false);
        server.Dispose();
    }

    [Test]
    public async Task TolerantDestroy()
    {
        var server = new FakeNodeServer();
        var bridge = new FakeBridge();
        bridge.GoReady();
        var extension = new NyraExtension(bridge);
        var node = await ConnectNodeAsync(server, extension, "tolerant").ConfigureAwait(false);
        server.On(
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
        server.On(
            "DELETE",
            "/v4/sessions/fake-session/players/777",
            _ => new FakeRestResponse(404, "{\"message\":\"no player\"}")
        );
        await conn.DisconnectAsync().ConfigureAwait(false);
        await Assert.That(node.GetConnection(777)).IsNull();
        await extension.DisposeAsync().ConfigureAwait(false);
        server.Dispose();
    }
}
