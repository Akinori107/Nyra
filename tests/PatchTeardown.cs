using Nyra.Entities;
using Nyra.Exceptions;
using static Nyra.Tests.NodeHarness;

namespace Nyra.Tests;

public sealed class PatchTeardown
{
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
    [Arguments(false, false)]
    [Arguments(false, true)]
    [Arguments(true, false)]
    [Arguments(true, true)]
    public async Task OldSessionPlayerPatchDoesNotCaptureState(bool nodeLink, bool seek)
    {
        var fixture = new FakeNodeServer
        {
            IsNodeLink = nodeLink,
            Semver = nodeLink ? "3.9.0" : "4.2.2",
        };
        var (extension, node, bridge, server) = await StartAsync(fixture).ConfigureAwait(false);
        using (server)
        await using (extension.ConfigureAwait(false))
        {
            var conn = await ConnectGuildAsync(bridge, node, server).ConfigureAwait(false);
            const string body =
                """{"guildId":"777","track":null,"volume":100,"paused":false,"state":{"time":0,"position":0,"connected":true,"ping":42},"voice":{"token":"tok","endpoint":"127.0.0.1","sessionId":"vsess"}}""";
            var entered = new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously
            );
            var release = new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously
            );
            var ready = new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously
            );
            node.Ready += (_, _) =>
            {
                ready.TrySetResult();
                return Task.CompletedTask;
            };
            var secondEntered = new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously
            );
            var secondRelease = new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously
            );
            var requests = 0;
            server.On("PATCH", PlayerPath, _ =>
            {
                if (Interlocked.Increment(ref requests) == 1)
                {
                    entered.TrySetResult();
                    release.Task.WaitAsync(TimeSpan.FromSeconds(15)).GetAwaiter().GetResult();
                    return new FakeRestResponse(
                        200,
                        body.Replace("100", "25", StringComparison.Ordinal)
                    );
                }
                secondEntered.TrySetResult();
                secondRelease.Task.WaitAsync(TimeSpan.FromSeconds(15)).GetAwaiter().GetResult();
                return new FakeRestResponse(200, body);
            });
            server.On("PATCH", "/v4/sessions/new-session/players/777", 200, body);
            var pending = seek ? conn.SeekAsync(TimeSpan.FromSeconds(15)) : conn.SetVolumeAsync(25);
            Task? second = null;
            try
            {
                await entered.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
                second = conn.SetVolumeAsync(100);
                await secondEntered.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
                server.SessionId = "new-session";
                server.ResumeOnReconnect = false;
                await server.DropConnectionAsync(1012, "restart").ConfigureAwait(false);
                await Assert
                    .That(() => node.SessionId)
                    .WaitsFor(assert => assert.IsEqualTo("new-session"), TimeSpan.FromSeconds(5));
                release.TrySetResult();
                await pending.ConfigureAwait(false);
                await Assert.That(conn.Volume).IsEqualTo(100);
            }
            finally
            {
                release.TrySetResult();
                secondRelease.TrySetResult();
                if (second is not null)
                    await second.ConfigureAwait(false);
                await pending.ConfigureAwait(false);
            }
            await ready.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
            await Assert.That(node.GetConnection(777)).IsSameReferenceAs(conn);
        }
    }
}
