using System.Collections.Concurrent;
using System.Reflection;
using Discord;
using Discord.WebSocket;
using Microsoft.Extensions.Logging;
using Nyra.Abstractions;

namespace Nyra.Tests;

public sealed class DiscordNetBridgeTests
{
#pragma warning disable CA1852
    private class RecordingProxy : DispatchProxy
    {
        internal static readonly List<string> Calls = [];

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            lock (Calls)
                Calls.Add(targetMethod!.Name);
            if (targetMethod.ReturnType == typeof(Task))
                return Task.CompletedTask;
            if (
                targetMethod.ReturnType.IsGenericType
                && targetMethod.ReturnType.GetGenericTypeDefinition() == typeof(Task<>)
            )
                return Activator.CreateInstance(
                    targetMethod.ReturnType,
                    targetMethod.ReturnType.GetGenericArguments()[0].IsValueType
                        ? Activator.CreateInstance(targetMethod.ReturnType.GetGenericArguments()[0])
                        : null
                );
            return targetMethod.ReturnType.IsValueType
                ? Activator.CreateInstance(targetMethod.ReturnType)
                : null;
        }
    }

    internal sealed class CapturingLogger(List<(LogLevel Level, string Message)> sink) : ILogger
    {
        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter
        )
        {
            lock (sink)
                sink.Add((logLevel, formatter(state, exception)));
        }

        public bool IsEnabled(LogLevel logLevel) => true;

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;
    }

    internal static void SetCurrentUser(BaseSocketClient client, ulong id)
    {
        var self = Reflect.New<SocketSelfUser>();
        Reflect.Set(self, "Id", id);
        typeof(BaseSocketClient)
            .GetProperty(
                "CurrentUser",
                BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly
            )!
            .SetValue(client, self);
    }

    private static object StateOf(DiscordSocketClient client) =>
        typeof(DiscordSocketClient)
            .GetProperty("State", BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(client)!;

    private static void InjectGuild(DiscordSocketClient client, SocketGuild guild)
    {
        var guilds =
            (ConcurrentDictionary<ulong, SocketGuild>)
                Reflect.FieldValue(StateOf(client), "_guilds")!;
        guilds[guild.Id] = guild;
    }

    private static void InjectChannel(DiscordSocketClient client, SocketChannel channel)
    {
        var channels =
            (ConcurrentDictionary<ulong, SocketChannel>)
                Reflect.FieldValue(StateOf(client), "_channels")!;
        channels[channel.Id] = channel;
    }

    private static SocketGuild NewGuild(DiscordSocketClient client, ulong id) =>
        (SocketGuild)
            Activator.CreateInstance(
                typeof(SocketGuild),
                BindingFlags.Instance | BindingFlags.NonPublic,
                null,
                new object[] { client, id },
                null
            )!;

    internal static SocketVoiceChannel NewVoiceChannel(
        DiscordSocketClient client,
        ulong id,
        SocketGuild guild
    ) =>
        (SocketVoiceChannel)
            Activator.CreateInstance(
                typeof(SocketVoiceChannel),
                BindingFlags.Instance | BindingFlags.NonPublic,
                null,
                new object[] { client, id, guild },
                null
            )!;

    private static SocketTextChannel NewTextChannel(
        DiscordSocketClient client,
        ulong id,
        SocketGuild guild
    ) =>
        (SocketTextChannel)
            Activator.CreateInstance(
                typeof(SocketTextChannel),
                BindingFlags.Instance | BindingFlags.NonPublic,
                null,
                new object[] { client, id, guild },
                null
            )!;

    internal static SocketVoiceState NewVoiceState(SocketVoiceChannel channel) =>
        (SocketVoiceState)
            Activator.CreateInstance(
                typeof(SocketVoiceState),
                BindingFlags.Instance | BindingFlags.NonPublic,
                null,
                new object[]
                {
                    channel,
                    null!,
                    "sess",
                    false,
                    false,
                    false,
                    false,
                    false,
                    false,
                    false,
                },
                null
            )!;

    private static DiscordSocketClient NewIntentClient() =>
        new(
            new DiscordSocketConfig
            {
                GatewayIntents = GatewayIntents.Guilds | GatewayIntents.GuildVoiceStates,
            }
        );

    [Test]
    public async Task PreLoggedConstructors()
    {
        using var socket = new DiscordSocketClient();
        SetCurrentUser(socket, 42);
        using var bridge = new Nyra.DiscordNet.DiscordNetBridge(socket);
        var info = bridge.WaitForReadyAsync().GetAwaiter().GetResult();
        await Assert.That(info.CurrentUserId).IsEqualTo(42);

        using var carrier = new DiscordSocketClient();
        SetCurrentUser(carrier, 7);
        using var sharded = new DiscordShardedClient();
        Reflect.Field(sharded, "_shards", new DiscordSocketClient[] { carrier });
        using var shardedBridge = new Nyra.DiscordNet.DiscordNetBridge(sharded);
        var shardedInfo = shardedBridge.WaitForReadyAsync().GetAwaiter().GetResult();
        await Assert.That(shardedInfo.CurrentUserId).IsEqualTo(7);
    }

    [Test]
    public async Task JoinGuards()
    {
        using var client = NewIntentClient();
        SetCurrentUser(client, 42);
        using var bridge = new Nyra.DiscordNet.DiscordNetBridge(client);
        var guild = NewGuild(client, 777);
        InjectGuild(client, guild);
        var channels =
            (ConcurrentDictionary<ulong, IAudioChannel>)
                Reflect.FieldValue(bridge, "_audioChannels")!;

        await Assert
            .That(async () => await bridge.SendVoiceUpdateAsync(777, 555).ConfigureAwait(false))
            .Throws<InvalidOperationException>();

        var text = NewTextChannel(client, 556, guild);
        InjectChannel(client, text);
        await Assert
            .That(async () => await bridge.SendVoiceUpdateAsync(777, 556).ConfigureAwait(false))
            .Throws<ArgumentException>();

        await Assert.That(channels).IsEmpty();
    }

    [Test]
    public async Task KickDuringJoin()
    {
        using var client = NewIntentClient();
        SetCurrentUser(client, 42);
        using var bridge = new Nyra.DiscordNet.DiscordNetBridge(client);
        var guild = NewGuild(client, 777);
        InjectGuild(client, guild);
        var voice = NewVoiceChannel(client, 555, guild);
        InjectChannel(client, voice);
        var fence = (GuildRemovalFence)Reflect.FieldValue(bridge, "_removalFence")!;
        var ticks = (ConcurrentDictionary<ulong, long>)Reflect.FieldValue(fence, "_ticks")!;
        ticks[777] = Environment.TickCount64 + 60_000;

        await Assert
            .That(async () => await bridge.SendVoiceUpdateAsync(777, 555).ConfigureAwait(false))
            .Throws<InvalidOperationException>();
        var channels =
            (ConcurrentDictionary<ulong, IAudioChannel>)
                Reflect.FieldValue(bridge, "_audioChannels")!;
        await Assert.That(channels.ContainsKey(777)).IsFalse();
    }

    [Test]
    public async Task JoinRecordsAndConnects()
    {
        using var client = NewIntentClient();
        SetCurrentUser(client, 42);
        using var bridge = new Nyra.DiscordNet.DiscordNetBridge(client);
        var guild = NewGuild(client, 777);
        InjectGuild(client, guild);
        var voice = NewVoiceChannel(client, 555, guild);
        InjectChannel(client, voice);

        await Assert
            .That(async () => await bridge.SendVoiceUpdateAsync(777, 555).ConfigureAwait(false))
            .Throws<InvalidOperationException>();
        var channels =
            (ConcurrentDictionary<ulong, IAudioChannel>)
                Reflect.FieldValue(bridge, "_audioChannels")!;
        await Assert.That(channels[777]).IsSameReferenceAs(voice);
    }

    [Test]
    public async Task LeaveFallbackThroughLiveState()
    {
        using var client = NewIntentClient();
        SetCurrentUser(client, 42);
        using var bridge = new Nyra.DiscordNet.DiscordNetBridge(client);
        var guild = NewGuild(client, 777);
        InjectGuild(client, guild);
        var voice = NewVoiceChannel(client, 555, guild);
        InjectChannel(client, voice);

        var member = Reflect.New<SocketGuildUser>();
        Reflect.Set(member, "Id", 42UL);
        Reflect.Set(member, "Guild", guild);
        Reflect.Field(
            guild,
            "_members",
            new ConcurrentDictionary<ulong, SocketGuildUser> { [42] = member }
        );
        Reflect.Field(
            guild,
            "_voiceStates",
            new ConcurrentDictionary<ulong, SocketVoiceState> { [42] = NewVoiceState(voice) }
        );

        var channels =
            (ConcurrentDictionary<ulong, IAudioChannel>)
                Reflect.FieldValue(bridge, "_audioChannels")!;
        await Assert
            .That(async () => await bridge.SendVoiceUpdateAsync(777, null).ConfigureAwait(false))
            .Throws<InvalidOperationException>();
        await Assert.That(channels).IsEmpty();
    }

    [Test]
    public async Task NullRefLookupIsASilentNoOp()
    {
        using var carrier = new DiscordSocketClient();
        SetCurrentUser(carrier, 42);
        using var sharded = new DiscordShardedClient();
        Reflect.Field(sharded, "_shards", new DiscordSocketClient[] { carrier });
        Reflect.Field(sharded, "_totalShards", 1);
        using var bridge = new Nyra.DiscordNet.DiscordNetBridge(sharded);

        await Assert
            .That(async () => await bridge.SendVoiceUpdateAsync(777, null).ConfigureAwait(false))
            .ThrowsNothing();
    }

#pragma warning disable CA1852
    private class HangingChannelProxy : DispatchProxy
    {
        internal TaskCompletionSource _hang = null!;

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            if (targetMethod!.Name == "DisconnectAsync")
                return _hang.Task;
            if (targetMethod.ReturnType == typeof(Task))
                return Task.CompletedTask;
            if (
                targetMethod.ReturnType.IsGenericType
                && targetMethod.ReturnType.GetGenericTypeDefinition() == typeof(Task<>)
            )
                return Activator.CreateInstance(
                    targetMethod.ReturnType,
                    targetMethod.ReturnType.GetGenericArguments()[0].IsValueType
                        ? Activator.CreateInstance(targetMethod.ReturnType.GetGenericArguments()[0])
                        : null
                );
            return targetMethod.ReturnType.IsValueType
                ? Activator.CreateInstance(targetMethod.ReturnType)
                : null;
        }
    }

    [Test]
    public async Task MidSendCancellation()
    {
        using var client = new DiscordSocketClient();
        using var bridge = new Nyra.DiscordNet.DiscordNetBridge(client);
        var channels =
            (ConcurrentDictionary<ulong, IAudioChannel>)
                Reflect.FieldValue(bridge, "_audioChannels")!;
        var hang = DispatchProxy.Create<IAudioChannel, HangingChannelProxy>();
        ((HangingChannelProxy)(object)hang)._hang = new TaskCompletionSource();
        channels[888] = hang;

        using var cancelled = new CancellationTokenSource(150);
        await Assert
            .That(async () =>
            {
                await bridge
                    .SendVoiceUpdateAsync(888, null, cancellationToken: cancelled.Token)
                    .ConfigureAwait(false);
            })
            .Throws<OperationCanceledException>();
        await Assert.That(channels.ContainsKey(888)).IsFalse();

        ((HangingChannelProxy)(object)hang)._hang.SetException(
            new InvalidOperationException("late fault")
        );
        await Task.Delay(50).ConfigureAwait(false);
        await Assert.That(((HangingChannelProxy)(object)hang)._hang.Task.IsFaulted).IsTrue();
    }

    [Test]
    public async Task ReadyPublishers()
    {
        using var client = new DiscordSocketClient();
        var sink = new List<(LogLevel, string)>();
        using var bridge = new Nyra.DiscordNet.DiscordNetBridge(client, new CapturingLogger(sink));
        SetCurrentUser(client, 42);

        Reflect.Invoke(bridge, "OnClientReady");
        var info = bridge.WaitForReadyAsync().GetAwaiter().GetResult();
        await Assert.That(info.CurrentUserId).IsEqualTo(42);

        using var shard = new DiscordSocketClient();
        SetCurrentUser(shard, 99);
        Reflect.Invoke(bridge, "OnShardReady", shard);
        await Assert
            .That(bridge.WaitForReadyAsync().GetAwaiter().GetResult().CurrentUserId)
            .IsEqualTo(42);
    }

    [Test]
    public async Task GuildlessStateIsFiltered()
    {
        using var client = new DiscordSocketClient();
        SetCurrentUser(client, 42);
        using var bridge = new Nyra.DiscordNet.DiscordNetBridge(client);
        var states = 0;
        bridge.VoiceStateUpdated += (_, _) => states++;

        var guildless = Reflect.New<SocketGuildUser>();
        Reflect.Set(guildless, "Id", 42UL);
        Reflect.Invoke(
            bridge,
            "OnVoiceStateUpdated",
            guildless,
            default(SocketVoiceState),
            default(SocketVoiceState)
        );
        await Assert.That(states).IsEqualTo(0);
    }

    private sealed class ThrowingLogger : ILogger
    {
        public int Calls;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter
        )
        {
            Interlocked.Increment(ref Calls);
            throw new InvalidOperationException("the logger is down");
        }

        public bool IsEnabled(LogLevel logLevel) => true;

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;
    }

    [Test]
    public async Task ThrowingLoggerSurvives()
    {
        using var client = new DiscordSocketClient();
        var logger = new ThrowingLogger();
        using var bridge = new Nyra.DiscordNet.DiscordNetBridge(client, logger);

        await Assert
            .That(() =>
            {
                Reflect.Invoke(
                    bridge,
                    "OnClientLog",
                    new LogMessage(LogSeverity.Error, "Gateway", "boom", null)
                );
            })
            .ThrowsNothing();
        await Assert.That(logger.Calls).IsEqualTo(1);
    }

    [Test]
    public async Task RecordedChannelLeave()
    {
        using var client = new DiscordSocketClient();
        using var bridge = new Nyra.DiscordNet.DiscordNetBridge(client);
        var channels =
            (ConcurrentDictionary<ulong, IAudioChannel>)
                Reflect.FieldValue(bridge, "_audioChannels")!;
        RecordingProxy.Calls.Clear();
        var fake = DispatchProxy.Create<IAudioChannel, RecordingProxy>();
        channels[123] = fake;

        await bridge.SendVoiceUpdateAsync(123, null).ConfigureAwait(false);
        await Assert.That(RecordingProxy.Calls).Contains("DisconnectAsync");
        await Assert.That(channels.ContainsKey(123)).IsFalse();

        channels[456] = fake;
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        await Assert
            .That(async () =>
            {
                await bridge
                    .SendVoiceUpdateAsync(456, null, cancellationToken: cancelled.Token)
                    .ConfigureAwait(false);
            })
            .Throws<OperationCanceledException>();
        await Assert.That(channels.ContainsKey(456)).IsTrue();
    }

    [Test]
    public async Task ShardedLookup()
    {
        using var sharded = new DiscordShardedClient();
        using var bridge = new Nyra.DiscordNet.DiscordNetBridge(sharded);

        await Assert
            .That(async () => await bridge.SendVoiceUpdateAsync(123, 456).ConfigureAwait(false))
            .Throws<InvalidOperationException>();
    }

    [Test]
    public async Task PublishAndDrift()
    {
        using var client = new DiscordSocketClient();
        var sink = new List<(LogLevel, string)>();
        using var bridge = new Nyra.DiscordNet.DiscordNetBridge(client, new CapturingLogger(sink));

        Reflect.Invoke(bridge, "PublishUserId", 42UL);
        var settled = bridge.WaitForReadyAsync().Wait(1000);
        Reflect.Invoke(bridge, "PublishUserId", 99UL);
        await Assert.That(settled).IsTrue();
        await Assert
            .That(bridge.WaitForReadyAsync().GetAwaiter().GetResult().CurrentUserId)
            .IsEqualTo(42);
        await Assert.That(sink.Count).IsEqualTo(0);
    }

    [Test]
    public async Task RemovalSweep()
    {
        using var client = new DiscordSocketClient();
        using var bridge = new Nyra.DiscordNet.DiscordNetBridge(client);
        var fence = (GuildRemovalFence)Reflect.FieldValue(bridge, "_removalFence")!;
        var ticks = (ConcurrentDictionary<ulong, long>)Reflect.FieldValue(fence, "_ticks")!;
        var now = Environment.TickCount64;
        ticks[111] = now - (11 * 60 * 1000);
        ticks[222] = now;

        fence.Sweep();
        await Assert.That(ticks.ContainsKey(111)).IsFalse();
        await Assert.That(ticks.ContainsKey(222)).IsTrue();
    }

    [Test]
    [Arguments(LogSeverity.Critical, LogLevel.Critical)]
    [Arguments(LogSeverity.Error, LogLevel.Error)]
    [Arguments(LogSeverity.Warning, LogLevel.Warning)]
    [Arguments(LogSeverity.Info, LogLevel.Information)]
    [Arguments(LogSeverity.Verbose, LogLevel.Debug)]
    [Arguments(LogSeverity.Debug, LogLevel.Trace)]
    public async Task Severity(LogSeverity severity, LogLevel expected)
    {
        using var client = new DiscordSocketClient();
        var sink = new List<(LogLevel, string)>();
        using var bridge = new Nyra.DiscordNet.DiscordNetBridge(client, new CapturingLogger(sink));

        Reflect.Invoke(bridge, "OnClientLog", new LogMessage(severity, "Gateway", "hello", null));
        await Assert.That(sink.Select(entry => entry.Item1)).IsEquivalentTo([expected]);
        await Assert.That(sink[0].Item2).Contains("hello");
    }

    [Test]
    public async Task VoiceServerShapes()
    {
        using var client = new DiscordSocketClient();
        using var bridge = new Nyra.DiscordNet.DiscordNetBridge(client);
        var raised = 0;
        bridge.VoiceServerUpdated += (_, _) => raised++;

        var partial = Reflect.New<SocketVoiceServer>();
        Reflect.Set(partial, "Guild", new Cacheable<IGuild, ulong>(null!, 123, true, null!));
        Reflect.Set(partial, "Endpoint", "wss://voice.example");
        Reflect.Invoke(bridge, "OnVoiceServerUpdated", partial);
        await Assert.That(raised).IsEqualTo(0);

        var foreignStates = 0;
        bridge.VoiceStateUpdated += (_, _) => foreignStates++;
        Reflect.Invoke(
            bridge,
            "OnVoiceStateUpdated",
            Reflect.New<SocketGuildUser>(),
            default,
            default
        );
        await Assert.That(foreignStates).IsEqualTo(0);

        await Task.CompletedTask.ConfigureAwait(false);
    }

    [Test]
    public async Task RemovalAndState()
    {
        using var client = new DiscordSocketClient();
        var removed = 0;
        ulong removedGuild = 0;
        var states = 0;
        Nyra.Abstractions.DiscordVoiceState? carried = null;
        using var bridge = new Nyra.DiscordNet.DiscordNetBridge(client);
        bridge.GuildRemoved += (_, args) =>
        {
            removed++;
            removedGuild = args.GuildId;
        };
        bridge.VoiceStateUpdated += (_, args) =>
        {
            states++;
            carried = args.VoiceState;
        };

        var self = Reflect.New<SocketSelfUser>();
        Reflect.Set(self, "Id", 42UL);
        typeof(BaseSocketClient)
            .GetProperty(
                "CurrentUser",
                BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly
            )!
            .SetValue(client, self);

        var guild = Reflect.New<SocketGuild>();
        Reflect.Set(guild, "Id", 777UL);
        var channels =
            (ConcurrentDictionary<ulong, IAudioChannel>)
                Reflect.FieldValue(bridge, "_audioChannels")!;
        channels[777] = DispatchProxy.Create<IAudioChannel, RecordingProxy>();

        Reflect.Invoke(bridge, "OnLeftGuild", guild);
        var fence = (GuildRemovalFence)Reflect.FieldValue(bridge, "_removalFence")!;
        var ticks = (ConcurrentDictionary<ulong, long>)Reflect.FieldValue(fence, "_ticks")!;
        await Assert.That(removed).IsEqualTo(1);
        await Assert.That(removedGuild).IsEqualTo(777UL);
        await Assert.That(ticks.ContainsKey(777)).IsTrue();
        await Assert.That(channels.ContainsKey(777)).IsFalse();

        fence.Sweep();
        await Assert.That(ticks.ContainsKey(777)).IsTrue();

        var member = Reflect.New<SocketGuildUser>();
        Reflect.Set(member, "Id", 42UL);
        Reflect.Set(member, "Guild", guild);
        Reflect.Invoke(
            bridge,
            "OnVoiceStateUpdated",
            member,
            default(SocketVoiceState),
            default(SocketVoiceState)
        );
        await Assert.That(states).IsEqualTo(1);

        var wireState = System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(
            typeof(SocketVoiceState)
        );
        var flagsType = typeof(SocketVoiceState)
            .GetField("_voiceStates", BindingFlags.NonPublic | BindingFlags.Instance)!
            .FieldType;
        Reflect.Set(wireState, "VoiceChannelId", 555UL);
        Reflect.Set(wireState, "VoiceSessionId", "vsess");
        Reflect.Field(wireState, "_voiceStates", Enum.ToObject(flagsType, 16));
        Reflect.Invoke(bridge, "OnVoiceStateUpdated", member, default(SocketVoiceState), wireState);
        await Assert
            .That(
                states == 2
                    && carried
                        is { ChannelId: 555, SessionId: "vsess", SelfDeaf: true, SelfMute: false }
            )
            .IsTrue();

        var foreign = Reflect.New<SocketGuildUser>();
        Reflect.Set(foreign, "Id", 99UL);
        Reflect.Set(foreign, "Guild", guild);
        Reflect.Invoke(
            bridge,
            "OnVoiceStateUpdated",
            foreign,
            default(SocketVoiceState),
            default(SocketVoiceState)
        );
        await Assert.That(states).IsEqualTo(2);

        await Task.CompletedTask.ConfigureAwait(false);
    }
}
