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
    [Arguments(204)]
    [Arguments(404)]
    public async Task RejectedPlayerDestroyRemainsAvailableForRetry(int retryStatus)
    {
        var (extension, node, bridge, server) = await StartAsync().ConfigureAwait(false);
        using (server)
        await using (extension.ConfigureAwait(false))
        {
            var conn = await ConnectGuildAsync(bridge, node, server).ConfigureAwait(false);

            try
            {
                server.On("DELETE", PlayerPath, 500, """{"message":"destroy failed"}""");
                await Assert.That(() => conn.DisconnectAsync()).Throws<RestException>();
                await Assert.That(node.GetConnection(777)).IsSameReferenceAs(conn);
                await Assert.That(() => conn.ResumeAsync()).Throws<InvalidOperationException>();

                server.On("DELETE", PlayerPath, retryStatus, string.Empty);
                await conn.DisconnectAsync().ConfigureAwait(false);
                await Assert.That(node.GetConnection(777)).IsNull();
                await Assert.That(conn.IsConnected).IsFalse();
                await Assert
                    .That(
                        server.RestCalls.Count(call =>
                            call.Method == "DELETE" && call.Path == PlayerPath
                        )
                    )
                    .IsEqualTo(2);
            }
            finally
            {
                server.On("DELETE", PlayerPath, 204, string.Empty);
            }
        }
    }
}
