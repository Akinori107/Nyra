using Microsoft.Extensions.Logging;
using Nyra.Abstractions;
using LogLevel = Microsoft.Extensions.Logging.LogLevel;

namespace Nyra.Tests;

public sealed class Adapter
{
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
}
