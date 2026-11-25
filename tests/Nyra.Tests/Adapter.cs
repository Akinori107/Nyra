using Microsoft.Extensions.Logging;
using NetCord.Gateway;
using NetCord.Gateway.JsonModels;
using NetCord.JsonModels;
using Nyra.Abstractions;
using Nyra.NetCord;
using LogLevel = Microsoft.Extensions.Logging.LogLevel;
using NcGuildDeleteArgs = NetCord.Gateway.GuildDeleteEventArgs;
using NcServerArgs = NetCord.Gateway.VoiceServerUpdateEventArgs;

namespace Nyra.Tests;

public sealed class Adapter
{
    private static void Ignore<T>(T args) { }

    internal sealed class Recording<T>
        where T : System.EventArgs
    {
        public int Count;
        public T? Last;

        public void Handler(object? _, T args)
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

    private sealed class FakeGatewayBridge(
        Action<VoiceServerUpdatedEventArgs> onVoiceServerUpdated,
        Action<VoiceStateUpdatedEventArgs> onVoiceStateUpdated,
        Action<GuildRemovedEventArgs> onGuildRemoved,
        Action<ListenerVoiceStateUpdatedEventArgs> onListenerVoiceStateUpdated
    )
        : GatewayBridgeBase(
            onVoiceServerUpdated,
            onVoiceStateUpdated,
            onGuildRemoved,
            onListenerVoiceStateUpdated
        )
    {
        public void RaiseServer(NcServerArgs args) =>
            HandleVoiceServerUpdateAsync(args).AsTask().GetAwaiter().GetResult();

        public void RaiseState(VoiceState args, ulong self) =>
            HandleVoiceStateUpdateAsync(args, self, null!).AsTask().GetAwaiter().GetResult();

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
        string sessionId,
        bool? bot = null
    ) =>
        new(
            new JsonVoiceState
            {
                GuildId = guildId,
                ChannelId = channelId,
                UserId = userId,
                SessionId = sessionId,
                User = bot is { } isBot
                    ? new JsonGuildUser
                    {
                        User = new JsonUser { Id = userId, IsBot = isBot },
                    }
                    : null,
            },
            guildId,
            null!
        );

    [Test]
    public async Task NetCordListeners()
    {
        var states = new Recording<VoiceStateUpdatedEventArgs>();
        var listeners = new Recording<ListenerVoiceStateUpdatedEventArgs>();
        var bridge = new FakeGatewayBridge(Ignore, states.Record, Ignore, listeners.Record);

        bridge.RaiseState(WireState(5, 77, 10, "s1", bot: false), 9);
        await Assert.That(listeners.Last?.UserId).IsEqualTo(10UL);
        await Assert.That(listeners.Last?.ChannelId).IsEqualTo(77UL);

        bridge.RaiseState(WireState(5, 77, 11, "s2", bot: true), 9);
        await Assert.That(listeners.Count).IsEqualTo(1);

        bridge.RaiseState(WireState(5, 77, 9, "s4"), 9);

        using (Assert.Multiple())
        {
            await Assert.That(states.Count).IsEqualTo(1);
            await Assert.That(listeners.Count).IsEqualTo(1);
        }
    }
}
