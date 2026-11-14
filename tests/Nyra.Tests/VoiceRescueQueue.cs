using Microsoft.Extensions.Time.Testing;
using static Nyra.Tests.NodeHarness;

namespace Nyra.Tests;

public sealed class VoiceRescueQueue
{
    private sealed class QueueingBridge : FakeBridge
    {
        private readonly TaskCompletionSource _release = new(
            TaskCreationOptions.RunContinuationsAsynchronously
        );

        private int _canceledJoins;

        public volatile bool Holding;

        public int CanceledJoins => Volatile.Read(ref _canceledJoins);

        public void Release() => _release.TrySetResult();

        public override async Task SendVoiceUpdateAsync(
            ulong guildId,
            ulong? channelId,
            bool selfDeaf = false,
            bool selfMute = false,
            CancellationToken cancellationToken = default
        )
        {
            if (!Holding || channelId is null)
            {
                await base.SendVoiceUpdateAsync(
                        guildId,
                        channelId,
                        selfDeaf,
                        selfMute,
                        cancellationToken
                    )
                    .ConfigureAwait(false);
                return;
            }

            VoiceSends.Add((guildId, channelId));
            try
            {
                await _release.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                Interlocked.Increment(ref _canceledJoins);
                throw;
            }
        }
    }

    private static async Task<(
        NyraExtension Extension,
        Node Node,
        FakeNodeServer Server,
        GuildConnection Connection
    )> StartAsync(QueueingBridge bridge, FakeTimeProvider time)
    {
        var server = new FakeNodeServer { IsNodeLink = false, Semver = "4.2.0" };
        bridge.GoReady();
        var extension = new NyraExtension(bridge, time);
        var node = new Node(
            new Endpoint("127.0.0.1", server.Port, server.Password),
            new NodeOptions
            {
                Name = "rescue-queue",
                TimeProvider = time,
                ReconnectDelay = TimeSpan.FromMilliseconds(50),
                MaxReconnectDelay = TimeSpan.FromMilliseconds(250),
                ReadyTimeout = TimeSpan.FromSeconds(5),
            }
        );
        extension.AddNode(node);
        await extension.ConnectAllNodesAsync().ConfigureAwait(false);
        var connection = await ConnectGuildAsync(bridge, node, server).ConfigureAwait(false);
        await RunForAsync(time, TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(1))
            .ConfigureAwait(false);
        return (extension, node, server, connection);
    }

    private static int Joins(FakeBridge bridge) =>
        bridge.VoiceSends.Count(send => send.ChannelId == 555);

    private static Task DropVoiceAsync(FakeNodeServer server) =>
        server.PushAsync(
            """{"op":"event","guildId":"777","type":"WebSocketClosedEvent","code":4022,"reason":"Disconnected: Call terminated.","byRemote":true}"""
        );

    [Test]
    public async Task ARescueJoinWaitingInTheGatewayQueueIsNotAbandoned()
    {
        var time = new FakeTimeProvider();
        var bridge = new QueueingBridge();
        var (extension, node, server, connection) = await StartAsync(bridge, time)
            .ConfigureAwait(false);
        using (server)
        await using (extension.ConfigureAwait(false))
        {
            var joinsBefore = Joins(bridge);
            bridge.Holding = true;

            await DropVoiceAsync(server).ConfigureAwait(false);
            await RunForAsync(time, TimeSpan.FromSeconds(60), TimeSpan.FromSeconds(1))
                .ConfigureAwait(false);

            await Assert.That(Joins(bridge) - joinsBefore).IsEqualTo(1);
            await Assert.That(bridge.VoiceSends.Any(send => send.ChannelId is null)).IsFalse();
            await Assert.That(node.GetConnection(777)).IsSameReferenceAs(connection);

            bridge.Holding = false;
            bridge.Release();
            bridge.FireVoiceState(777, 555, "vsess2");
            bridge.FireVoiceServer(777, "tok2", "127.0.0.1");
            await RunForAsync(time, TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(1))
                .ConfigureAwait(false);

            await Assert.That(node.GetConnection(777)).IsSameReferenceAs(connection);
            await Assert.That(connection.ChannelId).IsEqualTo(555UL);
            await Assert.That(bridge.VoiceSends.Any(send => send.ChannelId is null)).IsFalse();
        }
    }

    [Test]
    public async Task DisconnectingReleasesARescueJoinStillInTheQueue()
    {
        var time = new FakeTimeProvider();
        var bridge = new QueueingBridge();
        var (extension, _, server, connection) = await StartAsync(bridge, time)
            .ConfigureAwait(false);
        using (server)
        await using (extension.ConfigureAwait(false))
        {
            var joinsBefore = Joins(bridge);
            bridge.Holding = true;

            await DropVoiceAsync(server).ConfigureAwait(false);
            await AdvanceUntilAsync(
                    time,
                    () => Joins(bridge) > joinsBefore,
                    TimeSpan.FromSeconds(10)
                )
                .ConfigureAwait(false);

            await connection.DisconnectAsync().ConfigureAwait(false);

            await Assert
                .That(() => bridge.CanceledJoins)
                .WaitsFor(assert => assert.IsEqualTo(1), TimeSpan.FromSeconds(5));
        }
    }
}
