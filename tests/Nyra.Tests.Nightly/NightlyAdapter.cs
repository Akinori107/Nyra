using System.Text;
using DSharpPlus;
using Microsoft.Extensions.Logging;
using Nyra.Abstractions;
using Nyra.DSharpPlus.Nightly;
using DspChannel = DSharpPlus.Entities.DiscordChannel;
using DspChannelType = DSharpPlus.Entities.DiscordChannelType;
using DspGuild = DSharpPlus.Entities.DiscordGuild;
using DspGuildDeletedArgs = DSharpPlus.EventArgs.GuildDeletedEventArgs;
using DspServerArgs = DSharpPlus.EventArgs.VoiceServerUpdatedEventArgs;
using DspSessionArgs = DSharpPlus.EventArgs.SessionCreatedEventArgs;
using DspStartedArgs = DSharpPlus.EventArgs.ClientStartedEventArgs;
using DspUser = DSharpPlus.Entities.DiscordUser;
using DspVoiceState = DSharpPlus.Entities.DiscordVoiceState;
using DspVoiceStateArgs = DSharpPlus.EventArgs.VoiceStateUpdatedEventArgs;
using NightlyBridge = Nyra.DSharpPlus.Nightly.DSharpPlusBridge;
using NyraServerArgs = Nyra.Abstractions.VoiceServerUpdatedEventArgs;
using NyraStateArgs = Nyra.Abstractions.VoiceStateUpdatedEventArgs;

[assembly: NotInParallel]
[assembly: Timeout(300_000)]

namespace Nyra.Tests.Nightly;

public sealed class NightlyAdapter
{
    private sealed class Recording<T>
        where T : System.EventArgs
    {
        public int Count;
        public T? Last;

        public void Handler(object? sender, T args)
        {
            Count++;
            Last = args;
        }
    }

    private static DiscordClient NewOfflineClient(ulong selfId)
    {
        var builder = DiscordClientBuilder.CreateDefault(
            "offline",
            DiscordIntents.Guilds | DiscordIntents.GuildVoiceStates
        );
        var client = builder.Build();
        var self = Reflect.New<DspUser>();
        Reflect.Set(self, "Id", selfId);
        Reflect.Set(client, "CurrentUser", self);
        return client;
    }

    private static DspVoiceState NewState(
        ulong? guildId,
        ulong? channelId,
        ulong userId,
        string sessionId
    )
    {
        var state = Reflect.New<DspVoiceState>();
        Reflect.Set(state, "GuildId", guildId);
        Reflect.Set(state, "ChannelId", channelId);
        Reflect.Set(state, "UserId", userId);
        Reflect.Set(state, "SessionId", sessionId);
        return state;
    }

    private static DspVoiceStateArgs VoiceStateArgs(
        ulong? guildId,
        ulong? channelId,
        ulong userId,
        string sessionId
    )
    {
        var args = Reflect.New<DspVoiceStateArgs>();
        Reflect.Set(args, "After", NewState(guildId, channelId, userId, sessionId));
        Reflect.Set(args, "Before", (DspVoiceState?)null);
        Reflect.Set(args, "SessionId", sessionId);
        return args;
    }

    private static DspGuildDeletedArgs GuildDeletedArgs(ulong guildId, bool unavailable)
    {
        var guild = Reflect.New<DspGuild>();
        Reflect.Set(guild, "Id", guildId);

        var args = Reflect.New<DspGuildDeletedArgs>();
        Reflect.Set(args, "Guild", guild);
        Reflect.Set(args, "Unavailable", unavailable);
        return args;
    }

    private static DspServerArgs ServerArgs(ulong? guildId, string endpoint, string token)
    {
        var args = Reflect.New<DspServerArgs>();
        if (guildId is { } guild)
        {
            var guildObj = Reflect.New<DspGuild>();
            Reflect.Set(guildObj, "Id", guild);
            Reflect.Set(args, "Guild", guildObj);
        }
        else
        {
            Reflect.Set(args, "Guild", (DspGuild?)null);
        }

        Reflect.Set(args, "Endpoint", endpoint);
        Reflect.Set(args, "VoiceToken", token);
        return args;
    }

    [Test]
    public async Task Bridge()
    {
        var client = NewOfflineClient(9);
        using var bridge = new NightlyBridge();

        var unbound = false;
        try
        {
            bridge.SendVoiceUpdateAsync(5, 77).Wait(2000);
        }
        catch (AggregateException exception)
            when (exception.InnerException is InvalidOperationException)
        {
            unbound = true;
        }
        await Assert.That(unbound).IsTrue();

        bridge.Bind(client);

        var secondClient = NewOfflineClient(9);
        var rebound = false;
        try
        {
            bridge.Bind(secondClient);
        }
        catch (InvalidOperationException)
        {
            rebound = true;
        }
        await Assert.That(rebound).IsTrue();
        bridge.Bind(client);

        var states = new Recording<NyraStateArgs>();
        var servers = new Recording<NyraServerArgs>();
        var removals = new Recording<GuildRemovedEventArgs>();
        bridge.VoiceStateUpdated += states.Handler;
        bridge.VoiceServerUpdated += servers.Handler;
        bridge.GuildRemoved += removals.Handler;

        bridge.OnVoiceStateUpdated(client, VoiceStateArgs(5, 77, 9, "sess")).Wait(2000);
        await Assert.That(states.Count).IsEqualTo(1);
        await Assert.That(states.Last?.GuildId).IsEqualTo(5);
        await Assert.That(states.Last!.VoiceState.ChannelId).IsEqualTo(77);
        await Assert.That(states.Last!.VoiceState.SessionId).IsEqualTo("sess");

        bridge.OnVoiceStateUpdated(client, VoiceStateArgs(5, null, 9, "gone")).Wait(2000);
        await Assert.That(states.Count).IsEqualTo(2);
        await Assert.That(states.Last?.VoiceState.ChannelId).IsNull();

        bridge.OnVoiceStateUpdated(client, VoiceStateArgs(5, 77, 10, "other")).Wait(2000);
        await Assert.That(states.Count).IsEqualTo(2);

        bridge.OnVoiceStateUpdated(client, VoiceStateArgs(null, 1, 9, "dm")).Wait(2000);
        await Assert.That(states.Count).IsEqualTo(2);

        bridge.OnVoiceServerUpdated(client, ServerArgs(5, "wss://voice.example", "tok")).Wait(2000);
        await Assert.That(servers.Count).IsEqualTo(1);
        await Assert.That(servers.Last?.GuildId).IsEqualTo(5);
        await Assert.That(servers.Last!.VoiceServer.Token).IsEqualTo("tok");

        bridge.OnVoiceServerUpdated(client, ServerArgs(null, "wss://x", "t")).Wait(2000);
        bridge.OnVoiceServerUpdated(client, ServerArgs(5, "  ", "t")).Wait(2000);
        await Assert.That(servers.Count).IsEqualTo(1);

        bridge.OnGuildDeleted(client, GuildDeletedArgs(5, false)).Wait(2000);
        await Assert.That(removals.Count).IsEqualTo(1);
        await Assert.That(removals.Last?.GuildId).IsEqualTo(5);

        bridge.OnGuildDeleted(client, GuildDeletedArgs(5, true)).Wait(2000);
        await Assert.That(removals.Count).IsEqualTo(1);

        await DisposedGuards(client).ConfigureAwait(false);
    }

    [Test]
    public async Task UseNyraGate()
    {
        var first = DiscordClientBuilder.CreateDefault(
            "offline",
            DiscordIntents.Guilds | DiscordIntents.GuildVoiceStates
        );
        first.UseNyra();
        first.UseNyra();

        var built = first.Build();
        var extension = built.GetNyra();
        await Assert.That(extension).IsNotNull();
    }

    private static async Task DisposedGuards(DiscordClient client)
    {
        using var bridge = new NightlyBridge();
        bridge.Bind(client);
        var states = new Recording<NyraStateArgs>();
        bridge.VoiceStateUpdated += states.Handler;

        bridge.Dispose();
        bridge.OnVoiceStateUpdated(client, VoiceStateArgs(5, 77, 9, "late")).Wait(2000);
        await Assert.That(states.Count).IsEqualTo(0);

        var bareBuilder = DiscordClientBuilder.CreateDefault(
            "offline",
            DiscordIntents.Guilds | DiscordIntents.GuildVoiceStates
        );
        using var pending = new NightlyBridge();
        pending.Bind(bareBuilder.Build());
        pending.Dispose();
        var cancelled = false;
        try
        {
            pending.WaitForReadyAsync().Wait(1000);
        }
        catch (AggregateException exception)
            when (exception.InnerException is TaskCanceledException)
        {
            cancelled = true;
        }
        await Assert.That(cancelled).IsTrue();
    }

    private sealed class ThrowingLogger : ILogger
    {
        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter
        ) => throw new InvalidOperationException("the logger is down");

        public bool IsEnabled(LogLevel logLevel) => true;

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;
    }

    [Test]
    public async Task OfflineSend()
    {
        var client = NewOfflineClient(9);
        using var bridge = new NightlyBridge();
        bridge.Bind(client);

        var connecting = false;
        try
        {
            bridge.SendVoiceUpdateAsync(5, 77).Wait(3000);
        }
        catch (AggregateException exception)
            when (exception.InnerException is InvalidOperationException)
        {
            connecting = true;
        }
        await Assert.That(connecting).IsTrue();
    }

    [Test]
    public async Task HostileLoggerRebind()
    {
        var first = NewOfflineClient(9);
        Reflect.Set(first, "Logger", new ThrowingLogger());
        var second = NewOfflineClient(10);
        using var bridge = new NightlyBridge();
        bridge.Bind(first);

        var rejected = false;
        try
        {
            bridge.Bind(second);
        }
        catch (InvalidOperationException)
        {
            rejected = true;
        }
        await Assert.That(rejected).IsTrue();
    }

    [Test]
    public async Task ClientStarted()
    {
        var disposedClient = NewOfflineClient(9);
        using var disposedBridge = new NightlyBridge();
        disposedBridge.Dispose();
        disposedBridge.OnClientStarted(disposedClient, Reflect.New<DspStartedArgs>()).Wait(2000);
        await Assert.That(disposedBridge.Logger).IsNull();

        var noVoiceStates = DiscordClientBuilder
            .CreateDefault("offline", DiscordIntents.Guilds)
            .Build();
        using var warned = new NightlyBridge();
        warned.OnClientStarted(noVoiceStates, Reflect.New<DspStartedArgs>()).Wait(2000);
        await Assert.That(warned.Logger).IsNotNull();

        var hostile = DiscordClientBuilder.CreateDefault("offline", DiscordIntents.Guilds).Build();
        Reflect.Set(hostile, "Logger", new ThrowingLogger());
        using var hostileBridge = new NightlyBridge();
        var survived = true;
        try
        {
            hostileBridge.OnClientStarted(hostile, Reflect.New<DspStartedArgs>()).Wait(2000);
        }
        catch
        {
            survived = false;
        }
        await Assert.That(survived).IsTrue();

        var ready = NewOfflineClient(11);
        using var readyBridge = new NightlyBridge();
        readyBridge.OnClientStarted(ready, Reflect.New<DspStartedArgs>()).Wait(2000);
        var stillPending = !readyBridge.WaitForReadyAsync().Wait(500);
        readyBridge.OnSessionCreated(ready, Reflect.New<DspSessionArgs>()).Wait(2000);
        var info = readyBridge.WaitForReadyAsync().GetAwaiter().GetResult();
        await Assert.That(stillPending).IsTrue();
        await Assert.That(info.CurrentUserId).IsEqualTo(11);
    }

    [Test]
    public async Task SessionCreated()
    {
        var disposedClient = NewOfflineClient(9);
        using var disposedBridge = new NightlyBridge();
        disposedBridge.Dispose();
        disposedBridge.OnSessionCreated(disposedClient, Reflect.New<DspSessionArgs>()).Wait(2000);
        await Assert.That(disposedBridge.Logger).IsNull();

        var client = NewOfflineClient(12);
        using var bridge = new NightlyBridge();
        bridge.OnSessionCreated(client, Reflect.New<DspSessionArgs>()).Wait(2000);
        var info = bridge.WaitForReadyAsync().GetAwaiter().GetResult();
        await Assert.That(info.CurrentUserId).IsEqualTo(12);
    }

    [Test]
    public async Task DisposedLateForwards()
    {
        var client = NewOfflineClient(9);
        using var bridge = new NightlyBridge();
        bridge.Bind(client);
        var removals = 0;
        var servers = 0;
        bridge.GuildRemoved += (_, _) => removals++;
        bridge.VoiceServerUpdated += (_, _) => servers++;
        bridge.Dispose();

        bridge.OnGuildDeleted(client, GuildDeletedArgs(5, false)).Wait(2000);
        await Assert.That(removals).IsEqualTo(0);

        bridge.OnVoiceServerUpdated(client, ServerArgs(5, "wss://voice.example", "tok")).Wait(2000);
        await Assert.That(servers).IsEqualTo(0);
    }

    [Test]
    public async Task AdapterEdge()
    {
        var quiet = NewOfflineClient(9);
        Reflect.Set(quiet, "Logger", (ILogger?)null);
        using var quietBridge = new NightlyBridge();
        quietBridge.Bind(quiet);
        var rejectedQuiet = false;
        try
        {
            quietBridge.Bind(NewOfflineClient(10));
        }
        catch (InvalidOperationException)
        {
            rejectedQuiet = true;
        }
        await Assert.That(rejectedQuiet).IsTrue();

        var owner = NewOfflineClient(21);
        var stranger = NewOfflineClient(22);
        using var bridge = new NightlyBridge();
        bridge.Bind(owner);
        var removals = 0;
        bridge.GuildRemoved += (_, _) => removals++;
        bridge.OnGuildDeleted(stranger, GuildDeletedArgs(5, false)).Wait(2000);
        await Assert.That(removals).IsEqualTo(0);

        var guildless = Reflect.New<DspGuildDeletedArgs>();
        Reflect.Set(guildless, "Guild", (DspGuild?)null);
        Reflect.Set(guildless, "Unavailable", false);
        bridge.OnGuildDeleted(owner, guildless).Wait(2000);
        await Assert.That(removals).IsEqualTo(0);

        var ready = NewOfflineClient(31);
        using var fastBridge = new NightlyBridge();
        fastBridge.Bind(ready);
        var completed = fastBridge.WaitForReadyAsync().Wait(500);
        fastBridge.Bind(ready);
        await Assert.That(completed).IsTrue();

        var guardsClient = NewOfflineClient(41);
        var nullNode = false;
        try
        {
            _ = Nyra.DSharpPlus.Nightly.NodeExtensions.ConnectAsync(null!, null!);
        }
        catch (ArgumentNullException argumentNull) when (argumentNull.ParamName == "node")
        {
            nullNode = true;
        }
        await Assert.That(nullNode).IsTrue();

        var nullChannel = false;
        try
        {
            _ = Nyra.DSharpPlus.Nightly.NodeExtensions.ConnectAsync(
                new Nyra.Node(
                    new Nyra.Endpoint("127.0.0.1", 1, "x"),
                    new Nyra.NodeOptions { Name = "guards" }
                ),
                null!
            );
        }
        catch (ArgumentNullException argumentNull) when (argumentNull.ParamName == "channel")
        {
            nullChannel = true;
        }
        await Assert.That(nullChannel).IsTrue();

        var wrongShape = Reflect.New<DspChannel>();
        Reflect.Set(wrongShape, "Type", DspChannelType.Text);
        Reflect.Set(wrongShape, "GuildId", (ulong?)7);
        var rejectedType = false;
        try
        {
            _ = Nyra.DSharpPlus.Nightly.NodeExtensions.ConnectAsync(
                new Nyra.Node(
                    new Nyra.Endpoint("127.0.0.1", 1, "x"),
                    new Nyra.NodeOptions { Name = "guards" }
                ),
                wrongShape
            );
        }
        catch (ArgumentException)
        {
            rejectedType = true;
        }
        await Assert.That(rejectedType).IsTrue();

        Reflect.Set(wrongShape, "Type", DspChannelType.Voice);
        Reflect.Set(wrongShape, "GuildId", (ulong?)null);
        var rejectedGuild = false;
        try
        {
            _ = Nyra.DSharpPlus.Nightly.NodeExtensions.ConnectAsync(
                new Nyra.Node(
                    new Nyra.Endpoint("127.0.0.1", 1, "x"),
                    new Nyra.NodeOptions { Name = "guards" }
                ),
                wrongShape
            );
        }
        catch (ArgumentException)
        {
            rejectedGuild = true;
        }
        await Assert.That(rejectedGuild).IsTrue();

        Reflect.Set(wrongShape, "GuildId", (ulong?)7);
        Reflect.Set(wrongShape, "Id", (ulong)555);
        await Assert
            .That(() =>
            {
                _ = Nyra
                    .DSharpPlus.Nightly.NodeExtensions.ConnectAsync(
                        new Nyra.Node(
                            new Nyra.Endpoint("127.0.0.1", 1, "x"),
                            new Nyra.NodeOptions { Name = "guards" }
                        ),
                        wrongShape
                    )
                    .Wait(2000);
            })
            .Throws<AggregateException>();

        var nullLookup = false;
        try
        {
            Nyra.DSharpPlus.Nightly.DiscordClientBuilderExtensions.GetNyra(null!);
        }
        catch (ArgumentNullException argumentNull) when (argumentNull.ParamName == "client")
        {
            nullLookup = true;
        }
        await Assert.That(nullLookup).IsTrue();
        await Assert.That(guardsClient.GetNyra()).IsNull();

        var nullSharded = false;
        try
        {
            _ = Nyra.DSharpPlus.Nightly.DiscordClientBuilderExtensions.GetNyraAsync(null!);
        }
        catch (ArgumentNullException argumentNull) when (argumentNull.ParamName == "client")
        {
            nullSharded = true;
        }
        await Assert.That(nullSharded).IsTrue();

        guardsClient.GetNyraAsync().Wait(1000);
        var registered = false;
        var extensionsField =
            typeof(Nyra.DSharpPlus.Nightly.DiscordClientBuilderExtensions).GetField(
                "Extensions",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static
            );
        if (
            extensionsField?.GetValue(null)
            is System.Runtime.CompilerServices.ConditionalWeakTable<
                DiscordClient,
                Nyra.NyraExtension
            > registry
        )
        {
            using var extensionBridge = new NightlyBridge();
            registry.Add(guardsClient, new Nyra.NyraExtension(extensionBridge));
            var map = guardsClient.GetNyraAsync();
            map.Wait(1000);
            registered = map.Result.Count == 1 && map.Result[0] is { IsDisposed: false };
        }
        await Assert.That(registered).IsTrue();

        await VoiceUpdateRidesOrchestrator().ConfigureAwait(false);
    }

    private sealed class FakeOrchestrator : global::DSharpPlus.Clients.IShardOrchestrator
    {
        public readonly List<byte[]> Outbound = [];

        public bool AllShardsConnected => true;

        public int TotalShardCount => 1;

        public int ConnectedShardCount => 1;

        public ValueTask StartAsync(
            global::DSharpPlus.Entities.DiscordActivity? activity,
            global::DSharpPlus.Entities.DiscordUserStatus? status,
            DateTimeOffset? idleSince
        ) => ValueTask.CompletedTask;

        public ValueTask StopAsync() => ValueTask.CompletedTask;

        public ValueTask ReconnectAsync() => ValueTask.CompletedTask;

        public ValueTask SendOutboundEventAsync(byte[] payload, ulong guildId)
        {
            Outbound.Add(payload);
            return ValueTask.CompletedTask;
        }

        public ValueTask BroadcastOutboundEventAsync(byte[] payload) => ValueTask.CompletedTask;

        public bool IsConnected(ulong guildId) => true;

        public TimeSpan GetConnectionLatency(ulong guildId) => TimeSpan.FromMilliseconds(42);

        public IEnumerable<int> GetShardIds() => [0];

        public bool IsConnected(int shardId) => true;

        public TimeSpan GetConnectionLatency(int shardId) => TimeSpan.FromMilliseconds(42);
    }

    private static async Task VoiceUpdateRidesOrchestrator()
    {
        var client = NewOfflineClient(9);
        var orchestrator = new FakeOrchestrator();
        Reflect.Field(client, "orchestrator", orchestrator);
        using var bridge = new NightlyBridge();
        bridge.Bind(client);
        bridge.SendVoiceUpdateAsync(5, 77).Wait(2000);
        await Assert.That(orchestrator.Outbound.Count).IsEqualTo(1);
        await Assert
            .That(Encoding.UTF8.GetString(orchestrator.Outbound[0]))
            .Contains("\"channel_id\":\"77\"");
    }
}
