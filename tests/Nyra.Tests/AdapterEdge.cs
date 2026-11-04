using DSharpPlus;
using DSharpPlus.EventArgs;
using NetCord;
using NetCord.Gateway;
using Nyra.Abstractions;
using Nyra.NetCord;
using DspBridge = Nyra.DSharpPlus.DSharpPlusBridge;
using DspUser = DSharpPlus.Entities.DiscordUser;

namespace Nyra.Tests;

public sealed class AdapterEdge
{
    [Test]
    public async Task DspStable()
    {
        using var client = Adapter.NewOfflineClient(9);
        using var bridge = new DspBridge(client);

        var userless = Reflect.New<VoiceStateUpdateEventArgs>();
        Reflect.Set(userless, "Guild", Adapter.FabGuild(5));
        Reflect.Set(userless, "User", (DspUser?)null);
        var states = new Adapter.Recording<VoiceStateUpdatedEventArgs>();
        bridge.VoiceStateUpdated += states.Handler;
        Adapter.RaiseDsp(client, "_voiceStateUpdated", userless);
        await Assert.That(states.Count).IsEqualTo(0);

        using var quietClient = Adapter.NewOfflineClient(9);
        using var quietBridge = new DspBridge(quietClient);
        await Assert
            .That(() =>
            {
                Adapter.RaiseDsp(
                    quietClient,
                    "_voiceStateUpdated",
                    Adapter.VoiceStateArgs(5, 9, "bare")
                );
            })
            .ThrowsNothing();

        var sharded = new DiscordShardedClient(
            new DiscordConfiguration { Token = "offline", ShardCount = 2 }
        );
        var shards = (System.Collections.Concurrent.ConcurrentDictionary<int, DiscordClient>)
            Reflect.FieldValue(sharded, "_shards")!;
        shards[0] = Adapter.NewOfflineClient(41);
        using var shardedBridge = new DspBridge(sharded);
        await Assert
            .That(() =>
            {
                Reflect.Invoke(shardedBridge, "GetClientForGuild", 3UL << 22);
            })
            .Throws<System.Reflection.TargetInvocationException>()
            .WithInnerException<InvalidOperationException>();

        var plain = Adapter.NewOfflineClient(9);
        using var plainBridge = new DspBridge(plain);
        Reflect.Field(plainBridge, "_client", new object());
        await Assert
            .That(() =>
            {
                plainBridge.Dispose();
            })
            .ThrowsNothing();
    }

    [Test]
    public async Task DNet()
    {
        using var client = new Discord.WebSocket.DiscordSocketClient();
        using var bridge = new Nyra.DiscordNet.DiscordNetBridge(client);
        DiscordNetBridgeTests.SetCurrentUser(client, 42UL);
        await Assert
            .That(() =>
            {
                Reflect.Invoke(bridge, "PublishUserId", 111UL);
                Reflect.Invoke(bridge, "PublishUserId", 222UL);
            })
            .ThrowsNothing();

        var raised = 0;
        bridge.VoiceStateUpdated += (_, _) => raised++;

        var guild = Reflect.New<Discord.WebSocket.SocketGuild>();
        Reflect.Set(guild, "Id", 777UL);
        var oldChannel = DiscordNetBridgeTests.NewVoiceChannel(client, 555, guild);
        var oldState = DiscordNetBridgeTests.NewVoiceState(oldChannel);
        var member = Reflect.New<Discord.WebSocket.SocketGuildUser>();
        Reflect.Set(member, "Id", 42UL);
        Reflect.Set(member, "Guild", guild);
        Reflect.Invoke(
            bridge,
            "OnVoiceStateUpdated",
            member,
            oldState,
            default(Discord.WebSocket.SocketVoiceState)
        );
        await Assert.That(raised).IsEqualTo(1);

        var guildlessChannel = DiscordNetBridgeTests.NewVoiceChannel(client, 556, guild);
        Reflect.Set(guildlessChannel, "Guild", (Discord.WebSocket.SocketGuild?)null);
        var danglingOld = DiscordNetBridgeTests.NewVoiceState(guildlessChannel);
        var newChannel = DiscordNetBridgeTests.NewVoiceChannel(client, 557, guild);
        var newState = DiscordNetBridgeTests.NewVoiceState(newChannel);
        Reflect.Invoke(bridge, "OnVoiceStateUpdated", member, danglingOld, newState);
        await Assert.That(raised).IsEqualTo(2);

        var guildlessNew = DiscordNetBridgeTests.NewVoiceState(guildlessChannel);
        Reflect.Invoke(
            bridge,
            "OnVoiceStateUpdated",
            member,
            default(Discord.WebSocket.SocketVoiceState),
            guildlessNew
        );
        await Assert.That(raised).IsEqualTo(3);

        var plainUser = Reflect.New<Discord.WebSocket.SocketSelfUser>();
        Reflect.Set(plainUser, "Id", 42UL);
        Reflect.Invoke(
            bridge,
            "OnVoiceStateUpdated",
            plainUser,
            default(Discord.WebSocket.SocketVoiceState),
            default(Discord.WebSocket.SocketVoiceState)
        );
        await Assert.That(raised).IsEqualTo(3);
    }

    [Test]
    public async Task NetCord()
    {
        var token = new BotToken("MTIz.ab.cd");
        var client = new GatewayClient(token);
        var bridge = new GatewayBridge(client, _ => { }, _ => { }, _ => { });
        await Assert
            .That(() =>
            {
                bridge.Dispose();
            })
            .ThrowsNothing();

        await Assert
            .That(() =>
            {
                _ = ((Nyra.Node)null!).ConnectAsync((IVoiceGuildChannel)null!);
            })
            .Throws<System.ArgumentNullException>();

        var node = new Nyra.Node(
            new Nyra.Endpoint("127.0.0.1", 3098, "youshallnotpass"),
            new Nyra.NodeOptions { Name = "adapter-edge-connect" }
        );
        await Assert
            .That(() =>
            {
                _ = node.ConnectAsync((IVoiceGuildChannel)null!);
            })
            .Throws<System.ArgumentNullException>();
        node.Dispose();

        var sharded = new ShardedGatewayClient(token);
        var extensionA = sharded.UseNyra();
        var extensionB = sharded.UseNyra();
        await Assert.That(ReferenceEquals(extensionA, extensionB)).IsTrue();
        extensionA.Dispose();
    }
}
