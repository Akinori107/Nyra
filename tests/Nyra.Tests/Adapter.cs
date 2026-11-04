using System.Collections.Concurrent;
using Discord;
using Discord.WebSocket;
using DSharpPlus;
using DSharpPlus.EventArgs;
using Microsoft.Extensions.Logging;
using NetCord;
using NetCord.Gateway;
using NetCord.Gateway.JsonModels;
using NetCord.Gateway.JsonModels.EventArgs;
using NetCord.JsonModels;
using Nyra.Abstractions;
using Nyra.DiscordNet;
using Nyra.NetCord;
using static Nyra.Tests.NodeHarness;
using DNetBridge = Nyra.DiscordNet.DiscordNetBridge;
using DspBridge = Nyra.DSharpPlus.DSharpPlusBridge;
using DspGuild = DSharpPlus.Entities.DiscordGuild;
using DspGuildDeletedArgs = DSharpPlus.EventArgs.GuildDeleteEventArgs;
using DspServerUpdateArgs = DSharpPlus.EventArgs.VoiceServerUpdateEventArgs;
using DspUser = DSharpPlus.Entities.DiscordUser;
using DspVoiceState = DSharpPlus.Entities.DiscordVoiceState;
using LogLevel = Microsoft.Extensions.Logging.LogLevel;
using NcGuildDeleteArgs = NetCord.Gateway.GuildDeleteEventArgs;
using NcServerArgs = NetCord.Gateway.VoiceServerUpdateEventArgs;

namespace Nyra.Tests;

public sealed class Adapter
{
    private static void Ignore<T>(T args) { }

    private static GatewayBridge QuietGateway(GatewayClient client) =>
        new(
            client,
            Ignore<VoiceServerUpdatedEventArgs>,
            Ignore<VoiceStateUpdatedEventArgs>,
            Ignore<GuildRemovedEventArgs>
        );

    private static ShardedGatewayBridge QuietSharded(ShardedGatewayClient client) =>
        new(
            client,
            Ignore<VoiceServerUpdatedEventArgs>,
            Ignore<VoiceStateUpdatedEventArgs>,
            Ignore<GuildRemovedEventArgs>
        );

    internal sealed class Recording<T>
        where T : System.EventArgs
    {
        public int Count;
        public T? Last;

        public void Handler(object? sender, T args)
        {
            Count++;
            Last = args;
        }

        public void Record(T args)
        {
            Count++;
            Last = args;
        }
    }

    private static bool CancelsWithin(Func<CancellationToken, Task> wait, int ms)
    {
        using var cts = new CancellationTokenSource(ms);
        try
        {
            wait(cts.Token).Wait(ms + 1000);
            return false;
        }
        catch (AggregateException exception)
            when (exception.InnerException is OperationCanceledException)
        {
            return true;
        }
    }

    private sealed class ThrowingLogger : ILogger
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
        ) => throw new InvalidOperationException("the logger broke");
    }

    [Test]
    public async Task BridgeIsolation()
    {
        BridgeEvent.RaiseIsolated(null, new object(), new GuildRemovedEventArgs(1), null);

        var seen = new List<int>();
        EventHandler<GuildRemovedEventArgs>? handler = null;
        handler += (_, args) => seen.Add((int)args.GuildId);
        handler += (_, _) => throw new InvalidOperationException("a subscriber broke");
        handler += (_, args) => seen.Add((int)args.GuildId * 10);
        BridgeEvent.RaiseIsolated(handler, new object(), new GuildRemovedEventArgs(7), null);
        await Assert.That(seen.Count).IsEqualTo(2);
        await Assert.That(seen[0]).IsEqualTo(7);
        await Assert.That(seen[1]).IsEqualTo(70);

        seen.Clear();
        BridgeEvent.RaiseIsolated(
            handler,
            new object(),
            new GuildRemovedEventArgs(3),
            new ThrowingLogger()
        );
        await Assert.That(seen.Count).IsEqualTo(2);

        var senderSeen = new List<object>();
        var owner = new object();
        EventHandler<GuildRemovedEventArgs>? tracked = null;
        tracked += (sender, _) => senderSeen.Add(sender!);
        BridgeEvent.RaiseIsolated(tracked, owner, new GuildRemovedEventArgs(2), null);
        await Assert.That(senderSeen.Count).IsEqualTo(1);
        await Assert.That(ReferenceEquals(senderSeen[0], owner)).IsTrue();
    }

    private sealed class TestGatewayBridge : GatewayBridgeBase
    {
        public TestGatewayBridge(
            Action<VoiceServerUpdatedEventArgs> onVoiceServerUpdated,
            Action<VoiceStateUpdatedEventArgs> onVoiceStateUpdated,
            Action<GuildRemovedEventArgs> onGuildRemoved
        )
            : base(onVoiceServerUpdated, onVoiceStateUpdated, onGuildRemoved) { }

        public void RaiseServer(NcServerArgs args) =>
            HandleVoiceServerUpdateAsync(args).AsTask().GetAwaiter().GetResult();

        public void RaiseState(VoiceState args, ulong self) =>
            HandleVoiceStateUpdateAsync(args, self).AsTask().GetAwaiter().GetResult();

        public void RaiseDelete(NcGuildDeleteArgs args) =>
            HandleGuildDeleteAsync(args).AsTask().GetAwaiter().GetResult();

        protected override void Unsubscribe() { }

        protected override GatewayClient GetClient(ulong guildId) =>
            throw new InvalidOperationException("the battery never sends on the gateway");
    }

    private static VoiceState WireState(
        ulong guildId,
        ulong? channelId,
        ulong userId,
        string sessionId
    ) =>
        new(
            new JsonVoiceState
            {
                GuildId = guildId,
                ChannelId = channelId,
                UserId = userId,
                SessionId = sessionId,
            },
            guildId,
            null!
        );

    [Test]
    public async Task NetCordEvents()
    {
        var servers = new Recording<VoiceServerUpdatedEventArgs>();
        var states = new Recording<VoiceStateUpdatedEventArgs>();
        var removals = new Recording<GuildRemovedEventArgs>();
        var bridge = new TestGatewayBridge(servers.Record, states.Record, removals.Record);

        bridge.RaiseServer(
            new NcServerArgs(
                new JsonVoiceServerUpdateEventArgs
                {
                    Token = "tok",
                    GuildId = 5,
                    Endpoint = "wss://voice.example",
                }
            )
        );
        using (Assert.Multiple())
        {
            await Assert.That(servers.Count).IsEqualTo(1);
            await Assert.That(servers.Last?.GuildId).IsEqualTo(5);
            await Assert.That(servers.Last!.VoiceServer.Token).IsEqualTo("tok");
            await Assert.That(servers.Last!.VoiceServer.Endpoint).IsEqualTo("wss://voice.example");
        }

        bridge.RaiseServer(
            new NcServerArgs(
                new JsonVoiceServerUpdateEventArgs
                {
                    Token = "t",
                    GuildId = 5,
                    Endpoint = null,
                }
            )
        );
        bridge.RaiseServer(
            new NcServerArgs(
                new JsonVoiceServerUpdateEventArgs
                {
                    Token = "t",
                    GuildId = 5,
                    Endpoint = "  ",
                }
            )
        );
        await Assert.That(servers.Count).IsEqualTo(1);

        bridge.RaiseState(WireState(5, 77, 9, "sess"), 9);
        using (Assert.Multiple())
        {
            await Assert.That(states.Count).IsEqualTo(1);
            await Assert.That(states.Last?.GuildId).IsEqualTo(5);
            await Assert.That(states.Last!.VoiceState.ChannelId).IsEqualTo(77);
            await Assert.That(states.Last!.VoiceState.SessionId).IsEqualTo("sess");
        }

        bridge.RaiseState(WireState(5, null, 9, "s2"), 9);
        await Assert.That(states.Count).IsEqualTo(2);
        await Assert.That(states.Last?.VoiceState.ChannelId).IsNull();

        bridge.RaiseState(WireState(5, 77, 10, "s3"), 9);
        await Assert.That(states.Count).IsEqualTo(2);

        bridge.RaiseState(WireState(0, 1, 9, "s4"), 9);
        await Assert.That(states.Count).IsEqualTo(2);

        bridge.RaiseDelete(new NcGuildDeleteArgs(new JsonGuild { Id = 5 }));
        await Assert.That(removals.Count).IsEqualTo(1);
        await Assert.That(removals.Last?.GuildId).IsEqualTo(5);

        bridge.RaiseDelete(new NcGuildDeleteArgs(new JsonGuild { Id = 5, IsUnavailable = true }));
        await Assert.That(removals.Count).IsEqualTo(1);

        await NetCordClientWiring().ConfigureAwait(false);
    }

    private static async Task NetCordClientWiring()
    {
        var client = new GatewayClient(new BotToken("MTIz.ab.cd"));
        var bridge = new Nyra.NetCord.NetCordBridge(client);

        var fiveEvents = new[]
        {
            "_voiceStateUpdate",
            "_voiceServerUpdate",
            "_ready",
            "_resume",
            "_guildDelete",
        };
        await Assert
            .That(fiveEvents.All(name => ListCount(Reflect.FieldValue(client, name)) >= 1))
            .IsTrue();

        var wrapperStates = new Recording<VoiceStateUpdatedEventArgs>();
        bridge.VoiceStateUpdated += wrapperStates.Handler;
        RaiseNetCord(client, "_voiceStateUpdate", WireState(5, 77, 123, "sess"));
        await Assert.That(wrapperStates.Count).IsEqualTo(1);
        await Assert.That(wrapperStates.Last?.GuildId).IsEqualTo(5);

        RaiseNetCord(client, "_voiceStateUpdate", WireState(5, 77, 999, "other"));
        await Assert.That(wrapperStates.Count).IsEqualTo(1);

        var removed = new Recording<GuildRemovedEventArgs>();
        bridge.GuildRemoved += removed.Handler;
        RaiseNetCord(client, "_guildDelete", new NcGuildDeleteArgs(new JsonGuild { Id = 5 }));
        await Assert.That(removed.Count).IsEqualTo(1);
        await Assert.That(removed.Last?.GuildId).IsEqualTo(5);

        bridge.Dispose();
        await Assert
            .That(fiveEvents.All(name => ListCount(Reflect.FieldValue(client, name)) == 0))
            .IsTrue();
        await Assert.That(CancelsWithin(token => bridge.WaitForReadyAsync(token), 200)).IsTrue();
    }

    private static void RaiseNetCord<T>(GatewayClient client, string fieldName, T payload)
    {
        if (Reflect.FieldValue(client, fieldName) is not System.Collections.IEnumerable handlers)
            return;
        foreach (var handler in handlers.Cast<Delegate>().ToArray())
            handler.DynamicInvoke(payload);
    }

    private static int ListCount(object? list)
    {
        if (list is null)
            return 0;
        if (list is System.Collections.ICollection collection)
            return collection.Count;
        return ((System.Collections.IEnumerable)list).Cast<object>().Count();
    }

    [Test]
    public async Task AdapterHoles()
    {
        var gateway = new GatewayClient(new BotToken("MTIz.ab.cd"));
        using (var bridge = QuietGateway(gateway))
        {
            RaiseNetCord0(gateway, "_resume");
            await Assert
                .That(async () =>
                    await bridge
                        .WaitForReadyAsync(CancellationToken.None)
                        .WaitAsync(TimeSpan.FromSeconds(2))
                        .ConfigureAwait(false)
                )
                .ThrowsNothing();

            var fatal = new DisconnectEventArgs(null, null, reconnect: false);
            var gateway2 = new GatewayClient(new BotToken("MTIz.ab.cd"));
            using var bridge2 = QuietGateway(gateway2);
            RaiseNetCord(gateway2, "_disconnect", fatal);
            await Assert
                .That(async () =>
                    await bridge2
                        .WaitForReadyAsync(CancellationToken.None)
                        .WaitAsync(TimeSpan.FromMilliseconds(2000))
                        .ConfigureAwait(false)
                )
                .Throws<InvalidOperationException>();
        }

        var sharded = new ShardedGatewayClient(new BotToken("MTIz.ab.cd"));
        using (var bridge = QuietSharded(sharded))
        {
            await Assert
                .That(async () =>
                    await bridge
                        .WaitForReadyAsync(CancellationToken.None)
                        .WaitAsync(TimeSpan.FromMilliseconds(100))
                        .ConfigureAwait(false)
                )
                .Throws<TimeoutException>();
        }

        var dspSharded = new global::DSharpPlus.DiscordShardedClient(new DiscordConfiguration());
        var dspBridge = new DspBridge(dspSharded);
        await Assert
            .That(async () =>
                await dspBridge
                    .SendVoiceUpdateAsync(123, 456)
                    .WaitAsync(TimeSpan.FromMilliseconds(2000))
                    .ConfigureAwait(false)
            )
            .Throws<InvalidOperationException>();

        dspBridge.Dispose();
        await Assert
            .That(async () =>
                await dspBridge
                    .SendVoiceUpdateAsync(123, 456)
                    .WaitAsync(TimeSpan.FromMilliseconds(2000))
                    .ConfigureAwait(false)
            )
            .Throws<InvalidOperationException>();
    }

    private static void RaiseNetCord0(GatewayClient client, string fieldName)
    {
        if (Reflect.FieldValue(client, fieldName) is not System.Collections.IEnumerable handlers)
            return;
        foreach (var handler in handlers.Cast<Delegate>().ToArray())
            handler.DynamicInvoke([]);
    }

    private sealed class CapturingLogger(List<LogLevel> sink) : ILogger
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
                sink.Add(logLevel);
        }
    }

    private static int SubscriptionCount(DiscordSocketClient client, string fieldName)
    {
        var container = Reflect.FieldValue(client, fieldName);
        var subscriptions = container?.GetType().GetProperty("Subscriptions");
        return subscriptions is null ? 0 : ListCount(subscriptions.GetValue(container));
    }

    [Test]
    public async Task DiscordNetEvents()
    {
        using var client = new DiscordSocketClient();
        var fiveEvents = new[]
        {
            "_voiceServerUpdatedEvent",
            "_userVoiceStateUpdatedEvent",
            "_readyEvent",
            "_logEvent",
            "_leftGuildEvent",
        };

        using (var bridge = new DNetBridge(client))
        {
            await Assert
                .That(fiveEvents.All(name => SubscriptionCount(client, name) >= 1))
                .IsTrue();

            await Assert
                .That(async () =>
                    await bridge
                        .SendVoiceUpdateAsync(123, 456)
                        .WaitAsync(TimeSpan.FromMilliseconds(2000))
                        .ConfigureAwait(false)
                )
                .Throws<InvalidOperationException>();

            await Assert
                .That(async () =>
                    await bridge
                        .SendVoiceUpdateAsync(123, null)
                        .WaitAsync(TimeSpan.FromSeconds(2))
                        .ConfigureAwait(false)
                )
                .ThrowsNothing();

            var removals = new Recording<GuildRemovedEventArgs>();
            bridge.GuildRemoved += removals.Handler;
            var audioChannels =
                (ConcurrentDictionary<ulong, IAudioChannel>)
                    Reflect.FieldValue(bridge, "_audioChannels")!;
            audioChannels[123] = null!;

            var guild = Reflect.New<SocketGuild>();
            Reflect.Field(guild, "<Id>k__BackingField", 123UL);
            Reflect.Invoke(bridge, "OnLeftGuild", guild);
            await Assert.That(removals.Count).IsEqualTo(1);
            await Assert.That(removals.Last?.GuildId).IsEqualTo(123);
            await Assert.That(audioChannels.ContainsKey(123)).IsFalse();

            var servers = new Recording<VoiceServerUpdatedEventArgs>();
            bridge.VoiceServerUpdated += servers.Handler;

            Reflect.Invoke(bridge, "OnVoiceServerUpdated", Reflect.New<SocketVoiceServer>());
            await Assert.That(servers.Count).IsEqualTo(0);

            var server = Reflect.New<SocketVoiceServer>();
            Reflect.Set(server, "Guild", new Cacheable<IGuild, ulong>(null!, 123, true, null!));
            Reflect.Set(server, "Endpoint", "wss://voice.example");
            Reflect.Set(server, "Token", "tok");
            Reflect.Invoke(bridge, "OnVoiceServerUpdated", server);
            using (Assert.Multiple())
            {
                await Assert.That(servers.Count).IsEqualTo(1);
                await Assert.That(servers.Last?.GuildId).IsEqualTo(123);
                await Assert.That(servers.Last!.VoiceServer.Token).IsEqualTo("tok");
            }

            var logged = new List<LogLevel>();
            using var loggedBridge = new DNetBridge(client, new CapturingLogger(logged));
            Reflect.Invoke(
                loggedBridge,
                "OnClientLog",
                new LogMessage(LogSeverity.Warning, "Gateway", "hello", null)
            );
            await Assert.That(logged.Count).IsEqualTo(1);
            await Assert.That(logged[0]).IsEqualTo(LogLevel.Warning);

            await Assert
                .That(CancelsWithin(token => loggedBridge.WaitForReadyAsync(token), 200))
                .IsTrue();
        }

        var afterDispose = fiveEvents.ToDictionary(
            name => name,
            name => SubscriptionCount(client, name)
        );
        using (Assert.Multiple())
        {
            await Assert.That(afterDispose["_voiceServerUpdatedEvent"]).IsEqualTo(0);
            await Assert.That(afterDispose["_userVoiceStateUpdatedEvent"]).IsEqualTo(0);
            await Assert.That(afterDispose["_readyEvent"]).IsEqualTo(0);
            await Assert.That(afterDispose["_logEvent"]).IsEqualTo(0);
            await Assert.That(afterDispose["_leftGuildEvent"]).IsEqualTo(1);
        }
    }

    internal static DiscordClient NewOfflineClient(ulong selfId)
    {
        var client = new DiscordClient(new DiscordConfiguration { Token = "offline" });
        var self = Reflect.New<DspUser>();
        Reflect.Set(self, "Id", selfId);
        Reflect.Set(client, "CurrentUser", self);
        return client;
    }

    private static int HandlerListCount(DiscordClient client, string fieldName) =>
        ListCount(Reflect.FieldValue(Reflect.FieldValue(client, fieldName)!, "_handlers"));

    internal static void RaiseDsp<TArgs>(object client, string fieldName, TArgs args) =>
        RaiseDsp(client, fieldName, client, args);

    internal static void RaiseDsp<TArgs>(object owner, string fieldName, object sender, TArgs args)
    {
        var container = Reflect.FieldValue(owner, fieldName)!;
        ((Task)Reflect.Invoke(container, "InvokeAsync", sender, args)!).Wait(2000);
    }

    internal static VoiceStateUpdateEventArgs VoiceStateArgs(
        ulong? guildId,
        ulong userId,
        string sessionId
    )
    {
        var state = Reflect.New<DspVoiceState>();
        Reflect.Set(state, "GuildId", guildId);
        Reflect.Set(state, "UserId", userId);
        Reflect.Set(state, "SessionId", sessionId);

        var user = Reflect.New<DspUser>();
        Reflect.Set(user, "Id", userId);

        var args = Reflect.New<VoiceStateUpdateEventArgs>();
        Reflect.Set(args, "User", user);
        Reflect.Set(args, "Guild", guildId is { } guild ? FabGuild(guild) : (DspGuild?)null);
        Reflect.Set(args, "After", state);
        Reflect.Set(args, "Before", (DspVoiceState?)null);
        Reflect.Set(args, "SessionId", sessionId);
        return args;
    }

    internal static DspGuild FabGuild(ulong guildId)
    {
        var guild = Reflect.New<DspGuild>();
        Reflect.Set(guild, "Id", guildId);
        return guild;
    }

    private static DspServerUpdateArgs ServerArgs(ulong? guildId, string endpoint, string token)
    {
        var args = Reflect.New<DspServerUpdateArgs>();
        Reflect.Set(args, "Guild", guildId is { } guild ? FabGuild(guild) : (DspGuild?)null);
        Reflect.Set(args, "Endpoint", endpoint);
        Reflect.Set(args, "VoiceToken", token);
        return args;
    }

    internal static DspGuildDeletedArgs GuildDeletedArgs(ulong guildId, bool unavailable)
    {
        var args = Reflect.New<DspGuildDeletedArgs>();
        Reflect.Set(args, "Guild", FabGuild(guildId));
        Reflect.Set(args, "Unavailable", unavailable);
        return args;
    }

    [Test]
    public async Task DSharpPlusEvents()
    {
        using var client = NewOfflineClient(9);
        var sixEvents = new[]
        {
            "_voiceStateUpdated",
            "_voiceServerUpdated",
            "_ready",
            "_guildDeleted",
            "_socketOpened",
            "_socketClosed",
        };

        using var bare = new DiscordClient(new DiscordConfiguration { Token = "offline" });
        using var bareBridge = new DspBridge(bare);
        await Assert
            .That(
                (
                    (ConcurrentDictionary<DiscordClient, byte>)
                        Reflect.FieldValue(bareBridge, "_gatewaySocketsUp")!
                ).IsEmpty
            )
            .IsTrue();

        using var bridge = new DspBridge(client);
        await Assert.That(sixEvents.All(name => HandlerListCount(client, name) >= 1)).IsTrue();

        var socketsTable =
            (ConcurrentDictionary<DiscordClient, byte>)
                Reflect.FieldValue(bridge, "_gatewaySocketsUp")!;
        await Assert.That(socketsTable.IsEmpty).IsFalse();

        var states = new Recording<VoiceStateUpdatedEventArgs>();
        var servers = new Recording<VoiceServerUpdatedEventArgs>();
        var removals = new Recording<GuildRemovedEventArgs>();
        bridge.VoiceStateUpdated += states.Handler;
        bridge.VoiceServerUpdated += servers.Handler;
        bridge.GuildRemoved += removals.Handler;

        RaiseDsp(client, "_voiceStateUpdated", VoiceStateArgs(5, 9, "sess"));
        using (Assert.Multiple())
        {
            await Assert.That(states.Count).IsEqualTo(1);
            await Assert.That(states.Last?.GuildId).IsEqualTo(5);
            await Assert.That(states.Last!.VoiceState.SessionId).IsEqualTo("sess");
            await Assert.That(states.Count).IsEqualTo(1);
            await Assert.That(states.Last?.VoiceState.ChannelId).IsNull();
        }

        RaiseDsp(client, "_voiceStateUpdated", VoiceStateArgs(5, 10, "other"));
        await Assert.That(states.Count).IsEqualTo(1);

        RaiseDsp(client, "_voiceStateUpdated", VoiceStateArgs(null, 9, "dm"));
        await Assert.That(states.Count).IsEqualTo(1);

        RaiseDsp(client, "_voiceServerUpdated", ServerArgs(5, "wss://voice.example", "tok"));
        using (Assert.Multiple())
        {
            await Assert.That(servers.Count).IsEqualTo(1);
            await Assert.That(servers.Last?.GuildId).IsEqualTo(5);
            await Assert.That(servers.Last!.VoiceServer.Endpoint).IsEqualTo("wss://voice.example");
        }

        RaiseDsp(client, "_voiceServerUpdated", ServerArgs(null, "wss://x", "t"));
        await Assert.That(servers.Count).IsEqualTo(1);

        RaiseDsp(client, "_voiceServerUpdated", ServerArgs(5, "  ", "t"));
        await Assert.That(servers.Count).IsEqualTo(1);

        RaiseDsp(client, "_guildDeleted", GuildDeletedArgs(5, false));
        await Assert.That(removals.Count).IsEqualTo(1);
        await Assert.That(removals.Last?.GuildId).IsEqualTo(5);

        RaiseDsp(client, "_guildDeleted", GuildDeletedArgs(5, true));
        await Assert.That(removals.Count).IsEqualTo(1);

        await ShardedMath(client, sixEvents).ConfigureAwait(false);
    }

    private static async Task ShardedMath(DiscordClient client, string[] sixEvents)
    {
        var sharded = new DspBridge(client, shardCount: 2);

        var foreign = (1UL << 22) + 1;
        await Assert
            .That(async () =>
                await sharded
                    .SendVoiceUpdateAsync(foreign, 1)
                    .WaitAsync(TimeSpan.FromMilliseconds(2000))
                    .ConfigureAwait(false)
            )
            .Throws<InvalidOperationException>();

        var own = 2UL << 22;
        var connectingSharded = new DspBridge(
            new DiscordClient(new DiscordConfiguration { Token = "offline" }),
            shardCount: 2
        );
        await Assert
            .That(async () =>
                await connectingSharded
                    .SendVoiceUpdateAsync(own, 1)
                    .WaitAsync(TimeSpan.FromMilliseconds(2000))
                    .ConfigureAwait(false)
            )
            .Throws<InvalidOperationException>();

        var singleOffline = new DspBridge(
            new DiscordClient(new DiscordConfiguration { Token = "offline" }),
            shardCount: 1
        );
        await Assert
            .That(async () =>
                await singleOffline
                    .SendVoiceUpdateAsync(foreign, 1)
                    .WaitAsync(TimeSpan.FromMilliseconds(2000))
                    .ConfigureAwait(false)
            )
            .Throws<InvalidOperationException>();

        var single = new DspBridge(client, shardCount: 1);

        var information = await single
            .WaitForReadyAsync()
            .WaitAsync(TimeSpan.FromSeconds(1))
            .ConfigureAwait(false);
        await Assert.That(information.CurrentUserId).IsEqualTo(9UL);

        single.Dispose();
        sharded.Dispose();
        connectingSharded.Dispose();
        singleOffline.Dispose();
        await Assert.That(sixEvents.All(name => HandlerListCount(client, name) == 1)).IsTrue();
    }

    [Test]
    public async Task NetCordSingleton()
    {
        var token = new BotToken("MTIz.ab.cd");

        var readyClient = new GatewayClient(token);
        ForceReady(readyClient);
        using (var readyBridge = QuietGateway(readyClient))
        {
            var settled = readyBridge.WaitForReadyAsync(CancellationToken.None).Wait(1000);
            await Assert.That(settled).IsTrue();
        }

        var firingClient = new GatewayClient(token);
        using var firingBridge = QuietGateway(firingClient);
        Reflect.Invoke(
            firingBridge,
            "OnClientReady",
            Reflect.New<global::NetCord.Gateway.ReadyEventArgs>()
        );
        var fired = firingBridge.WaitForReadyAsync(CancellationToken.None).Wait(1000);
        await Assert.That(fired).IsTrue();

        var forwarded = 0;
        var forwardingClient = new GatewayClient(token);
        using (
            var forwardingBridge = new GatewayBridge(
                forwardingClient,
                _ => forwarded++,
                Ignore<VoiceStateUpdatedEventArgs>,
                Ignore<GuildRemovedEventArgs>
            )
        )
        {
            Reflect.Invoke(
                forwardingBridge,
                "HandleVoiceServerUpdateAsync",
                new NcServerArgs(
                    new JsonVoiceServerUpdateEventArgs
                    {
                        Token = "tok",
                        GuildId = 5,
                        Endpoint = "wss://voice.example",
                    }
                )
            );
        }
        await Assert.That(forwarded).IsEqualTo(1);

        var shardToken = new BotToken("MTIz.ab.cd");
        var shardedOne = new GatewayClient(
            shardToken,
            new GatewayClientConfiguration { Shard = new Shard(0, 2) }
        );
        using var shardBridge = QuietGateway(shardedOne);
        await Assert
            .That(async () =>
                await shardBridge
                    .SendVoiceUpdateAsync(1 << 22, 77, false, false, CancellationToken.None)
                    .WaitAsync(TimeSpan.FromMilliseconds(2000))
                    .ConfigureAwait(false)
            )
            .Throws<InvalidOperationException>();

        var deadClient = new GatewayClient(token);
        var deadBridge = QuietGateway(deadClient);
        deadBridge.Dispose();
        await Assert
            .That(async () =>
                await deadBridge
                    .WaitForReadyAsync(CancellationToken.None)
                    .WaitAsync(TimeSpan.FromMilliseconds(500))
                    .ConfigureAwait(false)
            )
            .Throws<OperationCanceledException>();

        var wrapper = new NetCordBridge(new GatewayClient(token));
        var wrapperHeard = 0;
        wrapper.VoiceServerUpdated += (_, _) => wrapperHeard++;
        Reflect.Invoke(
            wrapper,
            "RaiseVoiceServerUpdated",
            Reflect.New<VoiceServerUpdatedEventArgs>()
        );
        await Assert.That(wrapperHeard).IsEqualTo(1);

        await Assert
            .That(async () =>
                await wrapper
                    .SendVoiceUpdateAsync(5, 77, false, false, CancellationToken.None)
                    .WaitAsync(TimeSpan.FromMilliseconds(2000))
                    .ConfigureAwait(false)
            )
            .Throws<InvalidOperationException>();

        await Assert
            .That(() =>
            {
                _ = ((ShardedGatewayClient)null!).UseNyra();
            })
            .Throws<ArgumentNullException>();

        var useSharded = new ShardedGatewayClient(token);
        var first = useSharded.UseNyra();
        var second = useSharded.UseNyra();
        await Assert.That(ReferenceEquals(first, second)).IsTrue();
        first.Dispose();
        var third = useSharded.UseNyra();
        await Assert.That(ReferenceEquals(first, third)).IsFalse();
        third.Dispose();
    }

    [Test]
    public async Task DSharpPlusEventHandlers()
    {
        using var client = NewOfflineClient(31);
        using var bridge = new DspBridge(client);
        RaiseDsp(client, "_ready", Reflect.New<global::DSharpPlus.EventArgs.ReadyEventArgs>());
        var ready = bridge.WaitForReadyAsync(CancellationToken.None).Wait(1000);
        await Assert.That(ready).IsTrue();

        RaiseDsp(
            client,
            "_socketOpened",
            Reflect.New<global::DSharpPlus.EventArgs.SocketEventArgs>()
        );
        var sockets =
            (ConcurrentDictionary<DiscordClient, byte>)
                Reflect.FieldValue(bridge, "_gatewaySocketsUp")!;
        await Assert.That(sockets.ContainsKey(client)).IsTrue();

        RaiseDsp(
            client,
            "_socketClosed",
            Reflect.New<global::DSharpPlus.EventArgs.SocketCloseEventArgs>()
        );
        await Assert.That(sockets.ContainsKey(client)).IsFalse();

        var sharded = new global::DSharpPlus.DiscordShardedClient(
            new DiscordConfiguration { Token = "offline" }
        );
        var shardedUser = Reflect.New<DspUser>();
        Reflect.Set(shardedUser, "Id", 41UL);
        Reflect.Set(sharded, "CurrentUser", shardedUser);
        var shards =
            (ConcurrentDictionary<int, DiscordClient>)Reflect.FieldValue(sharded, "_shards")!;
        var liveShard = NewOfflineClient(41);
        var bareShard = new DiscordClient(new DiscordConfiguration { Token = "offline" });
        shards[0] = liveShard;
        shards[1] = bareShard;
        using var shardedBridge = new DspBridge(sharded);
        var shardedReady = shardedBridge.WaitForReadyAsync(CancellationToken.None).Wait(1000);
        var shardedSockets =
            (ConcurrentDictionary<DiscordClient, byte>)
                Reflect.FieldValue(shardedBridge, "_gatewaySocketsUp")!;
        await Assert.That(shardedReady).IsTrue();
        await Assert.That(shardedSockets.ContainsKey(liveShard)).IsTrue();
        await Assert.That(shardedSockets.ContainsKey(bareShard)).IsFalse();

        var emptySharded = new global::DSharpPlus.DiscordShardedClient(
            new DiscordConfiguration { Token = "offline" }
        );
        using var emptyBridge = new DspBridge(emptySharded);
        RaiseDsp(
            emptySharded,
            "_socketOpened",
            bareShard,
            Reflect.New<global::DSharpPlus.EventArgs.SocketEventArgs>()
        );
        await Assert
            .That(async () =>
                await emptyBridge
                    .SendVoiceUpdateAsync(5, 77, false, false, CancellationToken.None)
                    .WaitAsync(TimeSpan.FromMilliseconds(2000))
                    .ConfigureAwait(false)
            )
            .Throws<InvalidOperationException>();
    }

    [Test]
    public async Task DiscordNetEventHandlers()
    {
        var logSink = new List<LogLevel>();
        using (var client = new DiscordSocketClient())
        using (var bridge = new DNetBridge(client, new CapturingLogger(logSink)))
        {
            Reflect.Invoke(
                bridge,
                "OnClientLog",
                new Discord.LogMessage(Discord.LogSeverity.Warning, "probe", "watch this")
            );
            await Assert.That(logSink).Contains(LogLevel.Warning);

            Reflect.Invoke(bridge, "PublishUserId", 111UL);
            Reflect.Invoke(bridge, "PublishUserId", 222UL);
            await Assert.That(bridge.WaitForReadyAsync(CancellationToken.None).Wait(500)).IsTrue();
        }

        using (var quietClient = new DiscordSocketClient())
        using (var quietBridge = new DNetBridge(quietClient))
        {
            await Assert
                .That(() =>
                {
                    Reflect.Invoke(
                        quietBridge,
                        "OnClientLog",
                        new Discord.LogMessage(Discord.LogSeverity.Error, "probe", "quiet")
                    );
                })
                .ThrowsNothing();
        }

        var guild = Reflect.New<Discord.WebSocket.SocketGuild>();
        Reflect.Set(guild, "Id", 5UL);
        var channel = Reflect.New<Discord.WebSocket.SocketVoiceChannel>();
        Reflect.Set(channel, "Guild", guild);
        Reflect.Set(channel, "Id", 77UL);

        await Assert
            .That(() =>
            {
                _ = ((Nyra.Node)null!).ConnectAsync((Discord.IVoiceChannel)channel);
            })
            .Throws<ArgumentNullException>();

        var node = new Nyra.Node(
            new Nyra.Endpoint("127.0.0.1", 3099, "youshallnotpass"),
            new Nyra.NodeOptions { Name = "dnet-connect" }
        );
        await Assert
            .That(() =>
            {
                _ = node.ConnectAsync((Discord.IVoiceChannel)null!);
            })
            .Throws<ArgumentNullException>();

        await Assert
            .That(async () =>
                await node.ConnectAsync((Discord.IVoiceChannel)channel)
                    .WaitAsync(TimeSpan.FromSeconds(2))
                    .ConfigureAwait(false)
            )
            .Throws<InvalidOperationException>();
        node.Dispose();
    }

    [Test]
    public async Task ShardedNetCord()
    {
        var token = new BotToken("MTIz.ab.cd");

        var readyShard = new GatewayClient(
            token,
            new GatewayClientConfiguration { Shard = new Shard(0, 1) }
        );
        ForceReady(readyShard);
        await Assert.That(readyShard.Status).IsEqualTo(WebSocketStatus.Ready);
        var probeSharded = new ShardedGatewayClient(token);
        InjectShards(probeSharded, readyShard);
        using (var probeBridge = QuietSharded(probeSharded))
        {
            var probed = probeBridge.WaitForReadyAsync(CancellationToken.None).Wait(1000);
            await Assert.That(probed).IsTrue();
        }

        var shard = new GatewayClient(
            token,
            new GatewayClientConfiguration { Shard = new Shard(0, 1) }
        );

        var readySharded = new ShardedGatewayClient(token);
        using var readyBridge = QuietSharded(readySharded);
        Reflect.Invoke(
            readyBridge,
            "OnShardReady",
            shard,
            Reflect.New<global::NetCord.Gateway.ReadyEventArgs>()
        );
        var readyInfo = readyBridge
            .WaitForReadyAsync(CancellationToken.None)
            .GetAwaiter()
            .GetResult();
        await Assert.That(readyInfo is { CurrentUserId: 123 }).IsTrue();

        var resumeSharded = new ShardedGatewayClient(token);
        using var resumeBridge = QuietSharded(resumeSharded);
        Reflect.Invoke(resumeBridge, "OnShardResume", shard);
        var resumed = resumeBridge.WaitForReadyAsync(CancellationToken.None).Wait(1000);
        await Assert.That(resumed).IsTrue();

        var fatalSharded = new ShardedGatewayClient(token);
        using var fatalBridge = QuietSharded(fatalSharded);
        Reflect.Invoke(
            fatalBridge,
            "OnShardDisconnect",
            shard,
            new DisconnectEventArgs(null, null, reconnect: false)
        );
        await Assert
            .That(async () =>
                await fatalBridge
                    .WaitForReadyAsync(CancellationToken.None)
                    .WaitAsync(TimeSpan.FromMilliseconds(1000))
                    .ConfigureAwait(false)
            )
            .Throws<InvalidOperationException>();

        Reflect.Invoke(
            fatalBridge,
            "OnShardDisconnect",
            shard,
            new DisconnectEventArgs(null, null, reconnect: true)
        );
        await Assert
            .That(async () =>
                await fatalBridge
                    .WaitForReadyAsync(CancellationToken.None)
                    .WaitAsync(TimeSpan.FromMilliseconds(1000))
                    .ConfigureAwait(false)
            )
            .Throws<InvalidOperationException>();

        var emptySharded = new ShardedGatewayClient(token);
        using var emptyBridge = QuietSharded(emptySharded);
        await Assert
            .That(async () =>
                await emptyBridge
                    .SendVoiceUpdateAsync(5, 77, false, false, CancellationToken.None)
                    .WaitAsync(TimeSpan.FromMilliseconds(2000))
                    .ConfigureAwait(false)
            )
            .Throws<InvalidOperationException>();

        var staggerSharded = new ShardedGatewayClient(token);
        InjectShards(staggerSharded, shard);
        using var staggerBridge = QuietSharded(staggerSharded);
        await Assert
            .That(async () =>
                await staggerBridge
                    .SendVoiceUpdateAsync(5, 77, false, false, CancellationToken.None)
                    .WaitAsync(TimeSpan.FromMilliseconds(2000))
                    .ConfigureAwait(false)
            )
            .Throws<InvalidOperationException>();

        var foreignShard = new GatewayClient(
            token,
            new GatewayClientConfiguration { Shard = new Shard(2, 4) }
        );
        var rangeSharded = new ShardedGatewayClient(token);
        InjectShards(rangeSharded, foreignShard);
        using var rangeBridge = QuietSharded(rangeSharded);
        await Assert
            .That(async () =>
                await rangeBridge
                    .SendVoiceUpdateAsync(0, 77, false, false, CancellationToken.None)
                    .WaitAsync(TimeSpan.FromMilliseconds(2000))
                    .ConfigureAwait(false)
            )
            .Throws<InvalidOperationException>();

        var connectingShard = new GatewayClient(
            token,
            new GatewayClientConfiguration { Shard = new Shard(0, 1) }
        );
        Reflect.Field(connectingShard, "_state", NewWsState(ready: false));
        await Assert.That(connectingShard.Status).IsEqualTo(WebSocketStatus.Connecting);
        var connectingSharded = new ShardedGatewayClient(token);
        InjectShards(connectingSharded, connectingShard);
        using var connectingBridge = QuietSharded(connectingSharded);
        using var cancelled = new CancellationTokenSource(300);
        await Assert
            .That(async () =>
                await connectingBridge
                    .SendVoiceUpdateAsync(5, 77, false, false, cancelled.Token)
                    .WaitAsync(TimeSpan.FromMilliseconds(3000))
                    .ConfigureAwait(false)
            )
            .Throws<OperationCanceledException>();

        var states = new Recording<VoiceStateUpdatedEventArgs>();
        var servers = new Recording<VoiceServerUpdatedEventArgs>();
        var removals = new Recording<GuildRemovedEventArgs>();
        var forwardSharded = new ShardedGatewayClient(token);
        using var forwardBridge = new ShardedGatewayBridge(
            forwardSharded,
            servers.Record,
            states.Record,
            removals.Record
        );

        Reflect.Invoke(forwardBridge, "OnVoiceStateUpdate", shard, WireState(5, 77, 123, "sess"));
        await Assert.That(states.Count).IsEqualTo(1);
        await Assert.That(states.Last?.GuildId).IsEqualTo(5);

        Reflect.Invoke(
            forwardBridge,
            "OnVoiceServerUpdate",
            shard,
            new NcServerArgs(
                new JsonVoiceServerUpdateEventArgs
                {
                    Token = "tok",
                    GuildId = 5,
                    Endpoint = "wss://voice.example",
                }
            )
        );
        await Assert.That(servers.Count).IsEqualTo(1);
        await Assert.That(servers.Last?.GuildId).IsEqualTo(5);

        Reflect.Invoke(
            forwardBridge,
            "OnShardGuildDelete",
            shard,
            new NcGuildDeleteArgs(new JsonGuild { Id = 5 })
        );
        await Assert.That(removals.Count).IsEqualTo(1);
        await Assert.That(removals.Last?.GuildId).IsEqualTo(5);

        var deadSharded = new ShardedGatewayClient(token);
        var deadBridge = QuietSharded(deadSharded);
        deadSharded.Dispose();
        await Assert.That(() => deadBridge.Dispose()).ThrowsNothing();
    }
}
