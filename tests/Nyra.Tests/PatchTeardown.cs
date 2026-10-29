using Nyra.Entities;
using Nyra.Exceptions;
using static Nyra.Tests.NodeHarness;

namespace Nyra.Tests;

public sealed class PatchTeardown
{
    private const string PlayerBody =
        """{"guildId":"777","track":null,"volume":100,"paused":false,"state":{"time":0,"position":0,"connected":true,"ping":42},"voice":{"token":"tok","endpoint":"127.0.0.1","sessionId":"vsess"}}""";

    private static async Task<(NyraExtension, Node, FakeBridge, FakeNodeServer)> StartAsync(
        FakeNodeServer? server = null
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
                Name = "patch-teardown",
                Resuming = true,
                ReconnectDelay = TimeSpan.FromMilliseconds(50),
                MaxReconnectDelay = TimeSpan.FromMilliseconds(200),
                VoiceHandshakeTimeout = TimeSpan.FromSeconds(5),
                ReadyTimeout = TimeSpan.FromSeconds(5),
            }
        );
        extension.AddNode(node);
        await extension.ConnectAllNodesAsync().ConfigureAwait(false);
        return (extension, node, bridge, server);
    }

    [Test]
    public async Task TeardownAbortsTheInFlightPatch()
    {
        var (extension, node, bridge, server) = await StartAsync().ConfigureAwait(false);
        var conn = await ConnectGuildAsync(bridge, node, server).ConfigureAwait(false);

        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        server.On(
            "PATCH",
            PlayerPath,
            _ =>
            {
                release.Task.Wait(TimeSpan.FromSeconds(3));
                return new FakeRestResponse(200, PlayerBody);
            }
        );
        var resumeCts = new CancellationTokenSource();
        var resuming = conn.ResumeAsync(resumeCts.Token);
        await Task.Delay(150).ConfigureAwait(false);

        var disconnectCts = new CancellationTokenSource();
        var disconnecting = conn.DisconnectAsync(disconnectCts.Token);
        await Task.Delay(150).ConfigureAwait(false);
        var playerCallsBefore = server.RestCalls.Count(c => c.Path == PlayerPath);
        disconnectCts.Cancel();

        await Assert
            .That(async () =>
            {
                await resuming.ConfigureAwait(false);
            })
            .Throws<OperationCanceledException>();

        await Assert
            .That(async () =>
            {
                await disconnecting.ConfigureAwait(false);
            })
            .Throws<OperationCanceledException>();

        await Assert
            .That(() => server.RestCalls.Count(c => c.Path == PlayerPath) > playerCallsBefore)
            .WaitsFor(assert => assert.IsTrue(), TimeSpan.FromSeconds(5));

        release.TrySetResult();
        await extension.DisposeAsync().ConfigureAwait(false);
        server.Dispose();
    }

    [Test]
    public async Task FailedResumePatchRethrows()
    {
        var (extension, node, bridge, server) = await StartAsync().ConfigureAwait(false);
        var conn = await ConnectGuildAsync(bridge, node, server).ConfigureAwait(false);

        server.On("PATCH", PlayerPath, 500, """{"message":"no"}""");
        await Assert
            .That(async () =>
            {
                await conn.ResumeAsync().ConfigureAwait(false);
            })
            .Throws<RestException>();

        await extension.DisposeAsync().ConfigureAwait(false);
        server.Dispose();
    }

    [Test]
    public async Task DuckingGuards()
    {
        var lavaServer = new FakeNodeServer { IsNodeLink = false, Semver = "4.2.0" };
        var (lavaExtension, lavaNode, lavaBridge, _) = await StartAsync(lavaServer)
            .ConfigureAwait(false);
        var lavaConn = await ConnectGuildAsync(lavaBridge, lavaNode, lavaServer)
            .ConfigureAwait(false);
        await Assert
            .That(async () =>
            {
                await lavaConn
                    .Effects.SetFadingAsync(
                        new FadingOptions
                        {
                            Ducking = new DuckingOptions
                            {
                                Enabled = null,
                                DurationMilliseconds = null,
                                TargetVolume = 0.5f,
                                Curve = null,
                            },
                        }
                    )
                    .ConfigureAwait(false);
            })
            .Throws<NotSupportedException>();
        await lavaExtension.DisposeAsync().ConfigureAwait(false);
        lavaServer.Dispose();

        var (extension, node, bridge, server) = await StartAsync().ConfigureAwait(false);
        var conn = await ConnectGuildAsync(bridge, node, server).ConfigureAwait(false);
        await Assert
            .That(async () =>
            {
                await conn
                    .Effects.SetFadingAsync(
                        new FadingOptions
                        {
                            Ducking = new DuckingOptions
                            {
                                Enabled = null,
                                DurationMilliseconds = null,
                                TargetVolume = 1.5f,
                                Curve = null,
                            },
                        }
                    )
                    .ConfigureAwait(false);
            })
            .Throws<ArgumentOutOfRangeException>();
        await extension.DisposeAsync().ConfigureAwait(false);
        server.Dispose();
    }

    [Test]
    public async Task ForeignCancellationIsNotBlamedOnTheTeardown()
    {
        var (extension, node, bridge, server) = await StartAsync().ConfigureAwait(false);
        var conn = await ConnectGuildAsync(bridge, node, server).ConfigureAwait(false);
        await Assert
            .That(async () =>
            {
                await conn
                    .Patches.SendAsync(
                        _ => throw new OperationCanceledException("a cancellation of its own"),
                        CancellationToken.None
                    )
                    .ConfigureAwait(false);
            })
            .Throws<OperationCanceledException>()
            .WithMessageContaining("a cancellation of its own");
        await extension.DisposeAsync().ConfigureAwait(false);
        server.Dispose();
    }

    [Test]
    public async Task NodeNotReadyExitCompensates()
    {
        var server = new FakeNodeServer();
        var bridge = new FakeBridge();
        bridge.GoReady();
        var extension = new NyraExtension(bridge);
        var node = new Node(
            new Endpoint("127.0.0.1", server.Port, server.Password),
            new NodeOptions
            {
                Name = "not-ready",
                Resuming = true,
                ReconnectDelay = TimeSpan.FromSeconds(10),
                MaxReconnectDelay = TimeSpan.FromSeconds(40),
                VoiceHandshakeTimeout = TimeSpan.FromSeconds(5),
                ReadyTimeout = TimeSpan.FromSeconds(5),
            }
        );
        extension.AddNode(node);
        await extension.ConnectAllNodesAsync().ConfigureAwait(false);

        server.On("PATCH", PlayerPath, _ => new FakeRestResponse(200, PlayerBody));
        var connect = node.ConnectAsync(777, 555);
        await Task.Delay(100).ConfigureAwait(false);
        await server.DropConnectionAsync(1000, "mid-join outage").ConfigureAwait(false);
        await Task.Delay(100).ConfigureAwait(false);
        var sendsBefore = bridge.VoiceSends.Count;
        bridge.FireVoiceState(777, 555, "vsess");
        bridge.FireVoiceServer(777, "tok", "127.0.0.1");

        await Assert
            .That(async () => await connect.ConfigureAwait(false))
            .Throws<InvalidOperationException>();

        await Assert
            .That(() =>
                bridge
                    .VoiceSends.Snapshot()
                    .Skip(sendsBefore)
                    .Any(s => s.GuildId == 777 && s.ChannelId is null)
            )
            .WaitsFor(assert => assert.IsTrue(), TimeSpan.FromSeconds(5));

        await extension.DisposeAsync().ConfigureAwait(false);
        server.Dispose();
    }

    [Test]
    public async Task FailedPlayerPatchExitCompensates()
    {
        var (extension, node, bridge, server) = await StartAsync().ConfigureAwait(false);

        server.On("PATCH", PlayerPath, 500, """{"message":"no"}""");
        var connect = node.ConnectAsync(777, 555);
        await Task.Delay(100).ConfigureAwait(false);
        bridge.FireVoiceState(777, 555, "vsess");
        bridge.FireVoiceServer(777, "tok", "127.0.0.1");

        await Assert
            .That(async () =>
            {
                await connect.ConfigureAwait(false);
            })
            .Throws<RestException>();

        await Assert
            .That(() => bridge.VoiceSends.Any(s => s.GuildId == 777 && s.ChannelId is null))
            .WaitsFor(assert => assert.IsTrue(), TimeSpan.FromSeconds(5));

        await extension.DisposeAsync().ConfigureAwait(false);
        server.Dispose();
    }

    [Test]
    public async Task TakeoverAbandonedMidDrain()
    {
        var (extension, node, bridge, server) = await StartAsync().ConfigureAwait(false);
        var conn = await ConnectGuildAsync(bridge, node, server).ConfigureAwait(false);

        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        server.On(
            "PATCH",
            PlayerPath,
            _ =>
            {
                release.Task.Wait(TimeSpan.FromSeconds(3));
                return new FakeRestResponse(200, PlayerBody);
            }
        );
        var resumeCts = new CancellationTokenSource();
        var resuming = conn.ResumeAsync(resumeCts.Token);
        await Task.Delay(150).ConfigureAwait(false);
        var tearingDown = conn.DisconnectAsync();
        await Task.Delay(150).ConfigureAwait(false);

        var connectCts = new CancellationTokenSource();
        var takingOver = node.ConnectAsync(777, 555, connectCts.Token);
        await Task.Delay(200).ConfigureAwait(false);
        var sendsBefore = bridge.VoiceSends.Count;
        connectCts.Cancel();

        await Assert
            .That(async () =>
            {
                await takingOver.ConfigureAwait(false);
            })
            .Throws<OperationCanceledException>();

        release.TrySetResult();
        try
        {
            await tearingDown.ConfigureAwait(false);
        }
        catch { }
        await Assert
            .That(() => bridge.VoiceSends.Count > sendsBefore)
            .WaitsFor(assert => assert.IsTrue(), TimeSpan.FromSeconds(5));

        await extension.DisposeAsync().ConfigureAwait(false);
        server.Dispose();
    }

    [Test]
    public async Task CancelledGateWaitReleases()
    {
        var (extension, node, bridge, server) = await StartAsync().ConfigureAwait(false);
        var conn = await ConnectGuildAsync(bridge, node, server).ConfigureAwait(false);

        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        server.On(
            "DELETE",
            PlayerPath,
            _ =>
            {
                release.Task.Wait(TimeSpan.FromSeconds(3));
                return new FakeRestResponse(204, string.Empty);
            }
        );
        var holding = node.Registry.DestroyPlayerExclusivelyAsync(conn, CancellationToken.None);
        await Task.Delay(150).ConfigureAwait(false);

        var waitingCts = new CancellationTokenSource();
        var waiting = node.Registry.DestroyPlayerExclusivelyAsync(conn, waitingCts.Token);
        await Task.Delay(150).ConfigureAwait(false);
        waitingCts.Cancel();

        await Assert
            .That(async () =>
            {
                await waiting.ConfigureAwait(false);
            })
            .Throws<OperationCanceledException>();

        release.TrySetResult();
        try
        {
            await holding.ConfigureAwait(false);
        }
        catch { }
        await extension.DisposeAsync().ConfigureAwait(false);
        server.Dispose();
    }
}
