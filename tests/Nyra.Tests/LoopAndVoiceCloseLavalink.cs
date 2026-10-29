using Nyra.Abstractions;
using Nyra.Internal;
using static Nyra.Tests.NodeHarness;

namespace Nyra.Tests;

public sealed class LoopAndVoiceCloseLavalink
{
    private static async Task<SupervisionLoopRace.Stack> StartAsync(
        string name,
        FakeNodeServer? server = null,
        bool resuming = false
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
                Name = name,
                Resuming = resuming,
                ReconnectDelay = TimeSpan.FromMilliseconds(50),
                MaxReconnectDelay = TimeSpan.FromMilliseconds(250),
                VoiceHandshakeTimeout = TimeSpan.FromSeconds(5),
                ReadyTimeout = TimeSpan.FromSeconds(5),
            }
        );
        extension.AddNode(node);
        await extension.ConnectAllNodesAsync().ConfigureAwait(false);
        return new SupervisionLoopRace.Stack
        {
            Extension = extension,
            Node = node,
            Bridge = bridge,
            Server = server,
        };
    }

    [Test]
    public async Task VoiceServerAfterARemoval()
    {
        var bridge = new FakeBridge();
        var coordinator = new VoiceHandshakeCoordinator(TimeSpan.FromSeconds(5));
        var wait = coordinator.WaitForUpdatesAsync(999, 555, bridge, CancellationToken.None);
        _ = wait;
        Thread.Sleep(150);

        coordinator.FailPendingGuildRemoval(999);
        var refused = coordinator.HandleVoiceServerUpdate(
            bridge,
            new VoiceServerUpdatedEventArgs(999, new VoiceServer("tok", "127.0.0.1"))
        );

        await Assert.That(refused).IsFalse();
    }

    [Test]
    public async Task VoiceCloseReentryOnLavalink()
    {
        var server = new FakeNodeServer { IsNodeLink = false, Semver = "4.2.0" };
        var stack = await StartAsync("close-reentry", server).ConfigureAwait(false);
        await using (stack.ConfigureAwait(false))
        {
            var (bridge, node, sv) = (stack.Bridge, stack.Node, stack.Server);
            await ConnectGuildAsync(bridge, node, sv).ConfigureAwait(false);
            var joinsBefore = bridge.VoiceSends.Count(s => s.ChannelId == 555);
            bridge.HangVoiceSends = true;

            await sv.PushAsync(
                    """{"op":"event","guildId":"777","type":"WebSocketClosedEvent","code":1000,"reason":"first","byRemote":true}"""
                )
                .ConfigureAwait(false);
            await Task.Delay(400).ConfigureAwait(false);
            await sv.PushAsync(
                    """{"op":"event","guildId":"777","type":"WebSocketClosedEvent","code":1000,"reason":"second","byRemote":true}"""
                )
                .ConfigureAwait(false);
            await Task.Delay(3200).ConfigureAwait(false);

            var rescueSends = bridge.VoiceSends.Count(s => s.ChannelId == 555) - joinsBefore;
            await Assert.That(rescueSends).IsEqualTo(1);
            bridge.HangVoiceSends = false;
            await Task.Delay(10_500).ConfigureAwait(false);
        }
    }
}

internal static class LoopAndVoiceCloseLavalinkExtensions
{
    internal static void Forget(this Task task) => _ = task;
}
