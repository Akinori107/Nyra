using Discord.WebSocket;
using DSharpPlus;
using Microsoft.Extensions.Logging;
using NetCord;
using NetCord.Gateway;
using Nyra.DiscordNet;
using Nyra.DSharpPlus;
using Nyra.NetCord;
using DnetSharded = Discord.WebSocket.DiscordShardedClient;

namespace Nyra.Tests;

public sealed class ClientEntryPoint
{
    [Test]
    public async Task DiscordNetEntryPoints()
    {
        using (var client = new DiscordSocketClient())
        {
            var first = client.UseNyra();
            await Assert.That(first).IsNotNull();
            await Assert.That(ReferenceEquals(client.UseNyra(), first)).IsTrue();
            await Assert.That(ReferenceEquals(client.GetNyra(), first)).IsTrue();

            first!.Dispose();
            await Assert.That(client.GetNyra()).IsNull();
            var second = client.UseNyra();
            await Assert.That(second).IsNotNull();
            await Assert.That(ReferenceEquals(second, first)).IsFalse();

            using (var other = new DiscordSocketClient())
            {
                var otherExtension = other.UseNyra();
                await Assert.That(otherExtension).IsNotNull();
                await Assert.That(ReferenceEquals(otherExtension, second)).IsFalse();
                await Assert.That(other.GetNyra()).IsSameReferenceAs(otherExtension);
                await Assert.That(client.GetNyra()).IsSameReferenceAs(second);
                otherExtension!.Dispose();
            }

            second!.Dispose();
        }

        await Assert.That(new DiscordSocketClient().GetNyra()).IsNull();
        await Assert
            .That(() =>
            {
                Nyra.DiscordNet.DiscordSocketClientExtensions.UseNyra((DiscordSocketClient)null!);
            })
            .Throws<ArgumentNullException>();
        await Assert
            .That(() =>
            {
                Nyra.DiscordNet.DiscordSocketClientExtensions.GetNyra((DiscordSocketClient)null!);
            })
            .Throws<ArgumentNullException>();

        using (var sharded = new DnetSharded())
        {
            var shardedFirst = sharded.UseNyra();
            await Assert.That(shardedFirst).IsNotNull();
            await Assert.That(ReferenceEquals(sharded.UseNyra(), shardedFirst)).IsTrue();
            await Assert.That(ReferenceEquals(sharded.GetNyra(), shardedFirst)).IsTrue();
            shardedFirst!.Dispose();
            await Assert.That(sharded.GetNyra()).IsNull();
            var shardedSecond = sharded.UseNyra();
            await Assert.That(shardedSecond).IsNotNull();
            await Assert.That(ReferenceEquals(shardedSecond, shardedFirst)).IsFalse();
            shardedSecond!.Dispose();
        }

        await Assert
            .That(() =>
            {
                Nyra.DiscordNet.DiscordSocketClientExtensions.UseNyra((DnetSharded)null!);
            })
            .Throws<ArgumentNullException>();
        await Assert
            .That(() =>
            {
                Nyra.DiscordNet.DiscordSocketClientExtensions.GetNyra((DnetSharded)null!);
            })
            .Throws<ArgumentNullException>();
    }

    [Test]
    public async Task DSharpPlusEntryPoints()
    {
        using (var client = new DiscordClient(new DiscordConfiguration { Token = "offline" }))
        {
            var first = client.UseNyra();
            await Assert.That(first).IsNotNull();
            await Assert.That(ReferenceEquals(client.UseNyra(), first)).IsTrue();
            await Assert.That(ReferenceEquals(client.GetNyra(), first)).IsTrue();

            await Assert.That(ReferenceEquals(client.UseNyra(shardCount: 4), first)).IsTrue();

            first!.Dispose();
            await Assert.That(client.GetNyra()).IsNull();
            var second = client.UseNyra();
            await Assert.That(second).IsNotNull();
            await Assert.That(ReferenceEquals(second, first)).IsFalse();

            client.RemoveNyra();
            await Assert.That(client.GetNyra()).IsNull();
            client.RemoveNyra();

            await Assert
                .That(new DiscordClient(new DiscordConfiguration { Token = "offline" }).GetNyra())
                .IsNull();
            await Assert
                .That(() =>
                {
                    Nyra.DSharpPlus.DiscordClientExtensions.UseNyra(null!);
                })
                .Throws<ArgumentNullException>();
        }

        using (var client = new DiscordClient(new DiscordConfiguration { Token = "offline" }))
        {
            var extension = client.UseNyra();
            await Assert.That(extension).IsNotNull();
            extension!.Dispose();
        }

        var intentsLogger = new RecordingLogger();
        using (
            var client = new DiscordClient(
                new DiscordConfiguration
                {
                    Token = "offline",
                    Intents = DiscordIntents.DirectMessages,
                    LoggerFactory = new SingleLoggerFactory(intentsLogger),
                }
            )
        )
        {
            var extension = client.UseNyra();
            extension!.Dispose();
        }

        foreach (
            var (intents, expectLog) in new[]
            {
                (DiscordIntents.Guilds | DiscordIntents.GuildVoiceStates, false),
                (DiscordIntents.GuildVoiceStates, true),
                (DiscordIntents.Guilds, true),
            }
        )
        {
            var logger = new RecordingLogger();
            using var client = new DiscordClient(
                new DiscordConfiguration
                {
                    Token = "offline",
                    Intents = intents,
                    LoggerFactory = new SingleLoggerFactory(logger),
                }
            );
            var extension = client.UseNyra();
            extension!.Dispose();
        }

        using (
            var client = new DiscordClient(
                new DiscordConfiguration
                {
                    Token = "offline",
                    Intents = DiscordIntents.DirectMessages,
                    LoggerFactory = new SingleLoggerFactory(new ThrowingLogger()),
                }
            )
        )
        {
            var extension = client.UseNyra();
            await Assert.That(extension).IsNotNull();
            extension!.Dispose();
        }

        using (
            var client = new DiscordClient(
                new DiscordConfiguration
                {
                    Token = "offline",
                    LoggerFactory = new SingleLoggerFactory(new ThrowingLogger()),
                }
            )
        )
        {
            var first = client.UseNyra(shardCount: 2);
            await Assert.That(ReferenceEquals(client.UseNyra(shardCount: 4), first)).IsTrue();
            first!.Dispose();
        }
    }

    private static void SeedShards(global::DSharpPlus.DiscordShardedClient client, int count)
    {
        var shards = new System.Collections.Concurrent.ConcurrentDictionary<
            int,
            global::DSharpPlus.DiscordClient
        >();
        for (var i = 0; i < count; i++)
        {
            var shard = new global::DSharpPlus.DiscordClient(
                new DiscordConfiguration
                {
                    Token = "offline",
                    ShardId = i,
                    ShardCount = count,
                    Intents = DiscordIntents.All,
                }
            );
            shards[i] = shard;
        }
        Reflect.Field(client, "_shards", shards);
        Reflect.Set(client, "ShardClients", shards);
    }

    [Test]
    public async Task DSharpPlusShardedEntryPoints()
    {
        var client = new global::DSharpPlus.DiscordShardedClient(
            new DiscordConfiguration
            {
                Token = "offline",
                ShardCount = 2,
                Intents = DiscordIntents.All,
            }
        );
        SeedShards(client, 2);

        var extensions = client.UseNyraAsync().GetAwaiter().GetResult();
        using (Assert.Multiple())
        {
            await Assert.That(extensions.Count).IsEqualTo(2);
            await Assert.That(extensions.ContainsKey(0)).IsTrue();
            await Assert.That(extensions.ContainsKey(1)).IsTrue();
            await Assert.That(extensions.Values.All(e => e is not null && !e.IsDisposed)).IsTrue();
        }

        var again = client.UseNyraAsync().GetAwaiter().GetResult();
        await Assert.That(again.Count).IsEqualTo(2);
        await Assert.That(ReferenceEquals(again[0], extensions[0])).IsTrue();
        await Assert.That(ReferenceEquals(again[1], extensions[1])).IsTrue();

        var read = client.GetNyraAsync().GetAwaiter().GetResult();
        await Assert.That(read.Count).IsEqualTo(2);
        await Assert.That(ReferenceEquals(read[0], extensions[0])).IsTrue();
        await Assert.That(ReferenceEquals(read[1], extensions[1])).IsTrue();

        var bare = new global::DSharpPlus.DiscordShardedClient(
            new DiscordConfiguration
            {
                Token = "offline",
                ShardCount = 1,
                Intents = DiscordIntents.All,
            }
        );
        SeedShards(bare, 1);
        var empty = bare.GetNyraAsync().GetAwaiter().GetResult();
        await Assert.That(empty.Count).IsEqualTo(0);

        foreach (var extension in extensions.Values)
            extension.Dispose();
    }

    [Test]
    public async Task NetCordEntryPoints()
    {
        var gateway = new GatewayClient(new BotToken("MTIz.ab.cd"));
        var first = gateway.UseNyra();
        await Assert.That(first).IsNotNull();
        await Assert.That(ReferenceEquals(gateway.UseNyra(), first)).IsTrue();
        await Assert.That(ReferenceEquals(gateway.GetNyra(), first)).IsTrue();
        first!.Dispose();
        await Assert.That(gateway.GetNyra()).IsNull();
        var second = gateway.UseNyra();
        await Assert.That(second).IsNotNull();
        await Assert.That(ReferenceEquals(second, first)).IsFalse();
        second!.Dispose();

        await Assert
            .That(() =>
            {
                Nyra.NetCord.GatewayClientExtensions.UseNyra((GatewayClient)null!);
            })
            .Throws<ArgumentNullException>();

        var sharded = new ShardedGatewayClient(new BotToken("MTIz.ab.cd"));
        var shardedFirst = sharded.UseNyra();
        await Assert.That(shardedFirst).IsNotNull();
        await Assert.That(ReferenceEquals(sharded.UseNyra(), shardedFirst)).IsTrue();
        await Assert.That(ReferenceEquals(sharded.GetNyra(), shardedFirst)).IsTrue();
        shardedFirst!.Dispose();
        await Assert.That(sharded.GetNyra()).IsNull();

        await Task.CompletedTask.ConfigureAwait(false);
    }

    private sealed class SingleLoggerFactory(ILogger logger) : ILoggerFactory
    {
        public ILogger CreateLogger(string categoryName) => logger;

        public void AddProvider(ILoggerProvider provider) { }

        public void Dispose() { }
    }

    private sealed class RecordingLogger : Microsoft.Extensions.Logging.ILogger
    {
        public List<string> Messages { get; } = [];

        public void Log<TState>(
            Microsoft.Extensions.Logging.LogLevel logLevel,
            Microsoft.Extensions.Logging.EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter
        ) => Messages.Add(formatter(state, exception));

        public bool IsEnabled(Microsoft.Extensions.Logging.LogLevel logLevel) => true;

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;
    }

    private sealed class ThrowingLogger : Microsoft.Extensions.Logging.ILogger
    {
        public void Log<TState>(
            Microsoft.Extensions.Logging.LogLevel logLevel,
            Microsoft.Extensions.Logging.EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter
        ) => throw new InvalidOperationException("the logger itself is broken");

        public bool IsEnabled(Microsoft.Extensions.Logging.LogLevel logLevel) => false;

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;
    }
}
