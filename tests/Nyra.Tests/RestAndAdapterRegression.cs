using System.Text.Json;
using System.Text.Json.Serialization;
using DSharpPlus;
using Microsoft.Extensions.Logging;
using Nyra.DSharpPlus;
using Nyra.Entities;
using Nyra.Exceptions;
using Nyra.Internal;
using Nyra.NetCord;
using static Nyra.Tests.NodeHarness;

namespace Nyra.Tests;

public sealed class RestAndAdapterRegression
{
    private sealed record SnowflakeNullProbe(
        [property: JsonPropertyName("g")]
        [property: JsonConverter(typeof(NullableSnowflakeConverter))]
            ulong? G
    );

    private sealed record StringOrNullProbe(
        [property: JsonPropertyName("s")]
        [property: JsonConverter(typeof(StringOrNumberConverter))]
            string? S
    );

    private sealed record TolerantStringProbe(
        [property: JsonPropertyName("s")]
        [property: JsonConverter(typeof(NullableTolerantStringConverter))]
            string? S
    );

    private sealed record TolerantBoolProbe(
        [property: JsonPropertyName("b")]
        [property: JsonConverter(typeof(NullableTolerantBooleanConverter))]
            bool? B
    );

    private static async Task<Stack> StartAsync(FakeNodeServer? server = null)
    {
        server ??= new FakeNodeServer();
        var bridge = new FakeBridge();
        bridge.GoReady();
        var extension = new NyraExtension(bridge);
        var node = new Node(
            new Endpoint("127.0.0.1", server.Port, server.Password),
            new NodeOptions
            {
                Name = "hunt-v89",
                ReconnectDelay = TimeSpan.FromMilliseconds(50),
                MaxReconnectDelay = TimeSpan.FromMilliseconds(200),
                ReadyTimeout = TimeSpan.FromSeconds(5),
            }
        );
        extension.AddNode(node);
        await extension.ConnectAllNodesAsync().ConfigureAwait(false);
        return new Stack
        {
            Extension = extension,
            Node = node,
            Bridge = bridge,
            Server = server,
        };
    }

    [Test]
    public async Task RestUnreachableGroupList()
    {
        var node = new Node(
            new Endpoint("127.0.0.1", DeadPort(), "unused"),
            new NodeOptions { Name = "dead-planner" }
        );
        var unreachable = await Assert
            .That(async () =>
                await node
                    .Rest.Info.GetRoutePlannerStatusAsync(CancellationToken.None)
                    .ConfigureAwait(false)
            )
            .Throws<NodeUnreachableException>()
            .WithMessageContaining("dead-planner");
        await Assert.That(unreachable!.InnerException).IsTypeOf<HttpRequestException>();
        await Assert.That(node.Rest.BaseUri.Host).IsEqualTo("127.0.0.1");
        await Assert.That(node.Rest.BaseUri.Port).IsNotEqualTo(0);
    }

    [Test]
    public async Task RestTrashRetryAfter()
    {
        var stack = await StartAsync().ConfigureAwait(false);
        await using (stack.ConfigureAwait(false))
        {
            stack.Server.On(
                "GET",
                "/v4/sessions/fake-session/groups",
                _ => new FakeRestResponse(429, """{"message":"slow down"}""", "not-a-date")
            );
            var throttled = await Assert
                .That(async () =>
                    await stack
                        .Node.Rest.Groups.ListAsync(CancellationToken.None)
                        .ConfigureAwait(false)
                )
                .Throws<RestException>()
                .WithMessageContaining("slow down");
            await Assert.That(throttled!.RetryAfter).IsNull();
        }
    }

    [Test]
    public async Task RestMissingSessionBody()
    {
        var stack = await StartAsync().ConfigureAwait(false);
        await using (stack.ConfigureAwait(false))
        {
            stack.Server.On(
                "GET",
                "/v4/sessions/fake-session/players/777",
                _ => new FakeRestResponse(404, """{"message":"session removed by an admin"}""")
            );
            var missing = await Assert
                .That(async () =>
                    await stack
                        .Node.Rest.Players.GetAsync(777, CancellationToken.None)
                        .ConfigureAwait(false)
                )
                .Throws<RestException>()
                .WithMessageContaining("session removed");
            await Assert.That(missing!.StatusCode).IsEqualTo(404);

            stack.Server.On(
                "GET",
                "/v4/sessions/fake-session/players/778",
                _ => new FakeRestResponse(404, """{"message":"player not found"}""")
            );
            var gone = await stack
                .Node.Rest.Players.GetAsync(778, CancellationToken.None)
                .ConfigureAwait(false);
            await Assert.That(gone).IsNull();
        }
    }

    [Test]
    public async Task LoadStreamErrorBranch()
    {
        var stack = await StartAsync().ConfigureAwait(false);
        await using (stack.ConfigureAwait(false))
        {
            var refused = await Assert
                .That(async () =>
                    await stack
                        .Node.Rest.Tracks.OpenLoadStreamAsync(
                            "QXRsZWFkdA==",
                            100,
                            0,
                            null,
                            LoadStreamFormat.Raw,
                            CancellationToken.None
                        )
                        .ConfigureAwait(false)
                )
                .Throws<RestException>();
            await Assert.That(refused!.StatusCode).IsEqualTo(404);
        }
    }

    [Test]
    public async Task JsonNullRoundTrips()
    {
        var options = new JsonSerializerOptions();

        var snowflakeNull = JsonSerializer.Deserialize<SnowflakeNullProbe>(
            """{"g":null}""",
            options
        );
        await Assert.That(snowflakeNull?.G).IsNull();
        var snowflakeValue = JsonSerializer.Deserialize<SnowflakeNullProbe>(
            """{"g":314}""",
            options
        );
        await Assert.That(snowflakeValue?.G).IsEqualTo(314);
        var snowflakeWritten = JsonSerializer.Serialize(new SnowflakeNullProbe(null), options);
        await Assert.That(snowflakeWritten).Contains("null");

        var stringNull = JsonSerializer.Deserialize<StringOrNullProbe>("""{"s":null}""", options);
        await Assert.That(stringNull?.S).IsNull();
        var stringWritten = JsonSerializer.Serialize(new StringOrNullProbe(null), options);
        await Assert.That(stringWritten).Contains("null");

        var tolerantNull = JsonSerializer.Deserialize<TolerantStringProbe>(
            """{"s":null}""",
            options
        );
        await Assert.That(tolerantNull?.S).IsNull();
        var tolerantWritten = JsonSerializer.Serialize(new TolerantStringProbe(null), options);
        await Assert.That(tolerantWritten).Contains("null");

        var boolWritten = JsonSerializer.Serialize(new TolerantBoolProbe(null), options);
        await Assert.That(boolWritten).Contains("null");
        var boolValue = JsonSerializer.Deserialize<TolerantBoolProbe>("""{"b":true}""", options);
        await Assert.That(boolValue?.B).IsTrue();
    }

    [Test]
    public async Task LyricsMissingRoute()
    {
        var server = new FakeNodeServer
        {
            IsNodeLink = false,
            Semver = "4.2.0",
            InfoBody =
                """{"version":{"semver":"4.2.0"},"plugins":[{"name":"lavalyrics-plugin"}]}""",
        };
        var stack = await StartAsync(server).ConfigureAwait(false);
        await using (stack.ConfigureAwait(false))
        {
            var connection = await ConnectGuildAsync(stack.Bridge, stack.Node, server)
                .ConfigureAwait(false);
            const string subscribePath = "/v4/sessions/fake-session/players/777/lyrics/subscribe";
            server.On(
                "PUT",
                subscribePath,
                _ => new FakeRestResponse(404, """{"message":"No endpoint"}""")
            );
            server.On(
                "POST",
                subscribePath,
                _ => new FakeRestResponse(404, """{"message":"No endpoint"}""")
            );
            var missing = await Assert
                .That(async () => await connection.Lyrics.SubscribeAsync().ConfigureAwait(false))
                .Throws<NotSupportedException>();
            var inner = await Assert.That(missing!.InnerException).IsTypeOf<RestException>();
            await Assert.That(inner!.StatusCode).IsEqualTo(404);
        }
    }

    [Test]
    public async Task LoadUnknownSearchType()
    {
        var stack = await StartAsync().ConfigureAwait(false);
        await using (stack.ConfigureAwait(false))
        {
            var unknown = await Assert
                .That(async () =>
                    await stack
                        .Node.LoadTracksAsync("anything", (SearchType)999)
                        .ConfigureAwait(false)
                )
                .Throws<ArgumentOutOfRangeException>();
            await Assert.That(unknown!.ActualValue).IsEqualTo((SearchType)999);

            stack.Server.On(
                "GET",
                "/v4/loadtracks",
                _ => new FakeRestResponse(200, """{"loadType":"empty","data":[],"pluginInfo":{}}""")
            );
            var plain = await stack
                .Node.LoadTracksAsync("no position marker in this one")
                .ConfigureAwait(false);
            await Assert.That(plain.LoadType).IsEqualTo(LoadType.Empty);
        }
    }

    [Test]
    public async Task RemoveNyraNull()
    {
        await Assert
            .That(async () => await ((DiscordClient)null!).RemoveNyraAsync().ConfigureAwait(false))
            .Throws<ArgumentNullException>()
            .WithParameterName("client");
    }

    [Test]
    public async Task NetCordWaitCancelled()
    {
        var bridge = Reflect.New<GatewayBridge>();
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        await Assert
            .That(async () =>
            {
                await bridge.WaitForReadyAsync(cancelled.Token).ConfigureAwait(false);
            })
            .Throws<OperationCanceledException>();
    }

    [Test]
    public async Task RaiseAsyncOnErrorHostile()
    {
        AsyncEventHandler<System.EventArgs>? handlers = null;
        handlers += (_, _) => throw new InvalidOperationException("handler boom");

        var seen = new List<Exception>();
        await handlers
            .RaiseAsync(
                new object(),
                System.EventArgs.Empty,
                (error, _) =>
                {
                    seen.Add(error);
                    return Task.CompletedTask;
                }
            )
            .ConfigureAwait(false);
        await Assert.That(seen.Count).IsEqualTo(1);
        await Assert.That(seen[0]).IsTypeOf<InvalidOperationException>();
        await Assert.That(seen[0].Message).IsEqualTo("handler boom");

        await Assert
            .That(async () =>
                await handlers
                    .RaiseAsync(
                        new object(),
                        System.EventArgs.Empty,
                        (_, _) => throw new InvalidOperationException("logger boom")
                    )
                    .ConfigureAwait(false)
            )
            .ThrowsNothing();
    }

    private sealed class RecordingLogger : ILogger
    {
        public List<(LogLevel Level, string Text)> Lines { get; } = [];

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter
        )
        {
            lock (Lines)
                Lines.Add((logLevel, formatter(state, exception)));
        }

        public bool IsEnabled(LogLevel logLevel) => true;

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;
    }
}
