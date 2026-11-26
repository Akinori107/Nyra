using System.Text.Json;
using Nyra.Entities;
using Nyra.Exceptions;

namespace Nyra.Tests;

public sealed class RestEdge
{
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
                Name = "rest-edges",
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
    public async Task RoutePlanner()
    {
        var stack = await StartAsync().ConfigureAwait(false);
        await using (stack.ConfigureAwait(false))
        {
            var (node, server) = (stack.Node, stack.Server);
            const string path = "/v4/routeplanner/status";

            server.On("GET", path, 204, string.Empty);
            var unconfigured = await Assert
                .That(async () => await node.RoutePlanner.GetStatusAsync().ConfigureAwait(false))
                .Throws<RestException>();
            await Assert.That(unconfigured!.StatusCode).IsEqualTo(204);

            server.On("GET", path, 200, string.Empty);
            var empty = await Assert
                .That(async () => await node.RoutePlanner.GetStatusAsync().ConfigureAwait(false))
                .Throws<RestException>();
            await Assert.That(empty!.StatusCode).IsEqualTo(200);

            server.On("GET", path, 200, """{"class":"RotatingIpRoutePlanner"}""");
            var status = await node.RoutePlanner.GetStatusAsync().ConfigureAwait(false);
            await Assert.That(status?.Class).IsEqualTo(RoutePlannerClass.Rotating);

            var freed = new TaskCompletionSource<string?>();
            server.On("POST", "/v4/routeplanner/free/address", call =>
            {
                freed.TrySetResult(call.Body);
                return new FakeRestResponse(204, string.Empty);
            });
            await node.RoutePlanner.FreeAddressAsync("/1.0.0.0").ConfigureAwait(false);
            await Assert
                .That(
                    (
                        await freed.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false)
                    )?.Contains("\"1.0.0.0\"")
                )
                .IsTrue();
            await Assert
                .That(stack.Server.RestCalls)
                .Contains(c => c is { Method: "POST", Path: "/v4/routeplanner/free/address" });

            server.On("POST", "/v4/routeplanner/free/all", 204, string.Empty);
            await node.RoutePlanner.FreeAllAddressesAsync().ConfigureAwait(false);
            await Assert
                .That(stack.Server.RestCalls)
                .Contains(c => c is { Method: "POST", Path: "/v4/routeplanner/free/all" });
            await Assert
                .That(async () =>
                {
                    await node.RoutePlanner.FreeAddressAsync(" ").ConfigureAwait(false);
                })
                .Throws<ArgumentException>();
        }
    }

    [Test]
    public async Task GroupUpdateGuards()
    {
        var stack = await StartAsync().ConfigureAwait(false);
        await using (stack.ConfigureAwait(false))
        {
            var node = stack.Node;

            var guards = new (GroupUpdate Update, Type Exception)[]
            {
                (
                    new GroupUpdate { PositionMilliseconds = 5000, EndTimeMilliseconds = 4000 },
                    typeof(ArgumentOutOfRangeException)
                ),
                (new GroupUpdate { Volume = -1 }, typeof(ArgumentOutOfRangeException)),
                (new GroupUpdate { Volume = 1001 }, typeof(ArgumentOutOfRangeException)),
                (
                    new GroupUpdate
                    {
                        Fading = new FadingOptions
                        {
                            Ducking = new DuckingOptions
                            {
                                Enabled = null,
                                DurationMilliseconds = null,
                                TargetVolume = 1.5f,
                                Curve = null,
                            },
                        },
                    },
                    typeof(ArgumentOutOfRangeException)
                ),
                (
                    new GroupUpdate { Filters = new FilterChain { Volume = 2f } },
                    typeof(NotSupportedException)
                ),
                (
                    new GroupUpdate { Track = new GroupTrack { Encoded = " " } },
                    typeof(ArgumentException)
                ),
                (
                    new GroupUpdate
                    {
                        Track = new GroupTrack { Encoded = "QXRsZWFzdA==", AudioTrackId = " " },
                    },
                    typeof(ArgumentException)
                ),
                (
                    new GroupUpdate
                    {
                        Track = new GroupTrack { Encoded = "QXRsZWFzdA==", Language = " " },
                    },
                    typeof(ArgumentException)
                ),
                (
                    new GroupUpdate
                    {
                        Track = new GroupTrack { Encoded = "Q", Identifier = "i" },
                    },
                    typeof(ArgumentException)
                ),
                (
                    new GroupUpdate { Track = new GroupTrack { Identifier = "i" } },
                    typeof(NotSupportedException)
                ),
                (
                    new GroupUpdate
                    {
                        Track = new GroupTrack
                        {
                            Encoded = "Q",
                            UserData = JsonDocument.Parse("[1]").RootElement,
                        },
                    },
                    typeof(ArgumentException)
                ),
            };
            foreach (var (update, exceptionType) in guards)
            {
                var rejected = await Assert
                    .That(async () =>
                        await node.Groups.UpdateAsync("g1", update).ConfigureAwait(false)
                    )
                    .Throws<Exception>();
                await Assert.That(rejected!.GetType()).IsEqualTo(exceptionType);
            }

            var patched = new TaskCompletionSource<string?>();
            stack.Server.On("PATCH", "/v4/sessions/fake-session/groups/g1", call =>
            {
                patched.TrySetResult(call.Body);
                return new FakeRestResponse(
                    200,
                    """{"id":"g1","guildIds":[777],"createdAt":123}"""
                );
            });
            var result = await node
                .Groups.UpdateAsync("g1", new GroupUpdate { Paused = true, Volume = 100 })
                .ConfigureAwait(false);
            await Assert.That(result?.Id).IsEqualTo("g1");
            await Assert
                .That(
                    (
                        await patched.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false)
                    )?.Contains("\"paused\":true")
                )
                .IsTrue();
        }
    }

    [Test]
    public async Task YouTubeConfig()
    {
        var stack = await StartAsync().ConfigureAwait(false);
        await using (stack.ConfigureAwait(false))
        {
            var (node, server) = (stack.Node, stack.Server);

            var guards = new (string? Token, string? Visitor)[]
            {
                (null, null),
                (null, " "),
                (" ", null),
            };
            foreach (var (token, visitor) in guards)
                await Assert
                    .That(async () =>
                    {
                        await node.YouTube.UpdateConfigAsync(token, visitor).ConfigureAwait(false);
                    })
                    .Throws<ArgumentException>();

            var patched = new TaskCompletionSource<string?>();
            server.On("PATCH", "/v4/youtube/config", call =>
            {
                patched.TrySetResult(call.Body);
                return new FakeRestResponse(
                    200,
                    """{"message":"applied","workersUpdated":3,"fieldsUpdated":["refreshToken"]}"""
                );
            });
            var update = await node.YouTube.UpdateConfigAsync("rtok", "vdat").ConfigureAwait(false);
            using (Assert.Multiple())
            {
                await Assert.That(update).IsNotNull();
                await Assert.That(update.Message).IsEqualTo("applied");
                await Assert.That(update.WorkersUpdated).IsEqualTo(3);
            }
            var patchBody = await patched
                .Task.WaitAsync(TimeSpan.FromSeconds(5))
                .ConfigureAwait(false);
            await Assert.That(patchBody?.Contains("rtok")).IsTrue();
            await Assert.That(patchBody).Contains("vdat");
        }
    }
}
