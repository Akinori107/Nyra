using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.Extensions.Time.Testing;
using Nyra.Abstractions;
using Nyra.Entities;
using static Nyra.Tests.NodeHarness;

namespace Nyra.Tests;

public sealed class VoiceListeners
{
    private static readonly TimeSpan Timeout5 = TimeSpan.FromMinutes(5);

    private sealed class ListeningBridge : FakeBridge, IVoiceListenerSource
    {
        public ConcurrentDictionary<ulong, ulong> Cache { get; } = [];

        public bool Unavailable { get; set; }

        public event EventHandler<ListenerVoiceStateUpdatedEventArgs>? ListenerVoiceStateUpdated;

        public IReadOnlyCollection<ulong>? GetListeners(ulong guildId, ulong channelId) =>
            Unavailable
                ? null
                : [.. Cache.Where(pair => pair.Value == channelId).Select(pair => pair.Key)];

        public void Move(ulong userId, ulong? channelId)
        {
            if (channelId is { } channel)
                Cache[userId] = channel;
            else
                Cache.TryRemove(userId, out _);
            ListenerVoiceStateUpdated?.Invoke(
                this,
                new ListenerVoiceStateUpdatedEventArgs(777, userId, channelId)
            );
        }
    }

    private sealed class Rig : IAsyncDisposable
    {
        public required NyraExtension Extension { get; init; }

        public required FakeNodeServer Server { get; init; }

        public required ListeningBridge Bridge { get; init; }

        public required Node Node { get; init; }

        public required GuildConnection Connection { get; init; }

        public required FakeTimeProvider Time { get; init; }

        public ConcurrentQueue<string> Log { get; } = [];

        public Action<string>? BeforePatch { get; set; }

        public Action LosePlayer { get; init; } = () => { };

        public bool Connected => Node.GetConnection(777) is not null;

        public async ValueTask DisposeAsync()
        {
            await Extension.DisposeAsync().ConfigureAwait(false);
            Server.Dispose();
        }
    }

    private static string TrackJson(string id) =>
        $$$"""{"encoded":"enc-{{{id}}}","info":{"identifier":"{{{id}}}","author":"a","title":"{{{id}}}","length":100000,"isStream":false,"isSeekable":true,"position":0}}""";

    private static Track Make(string id) =>
        JsonSerializer.Deserialize<Track>(TrackJson(id), NyraJson.SerializerOptions)!;

    private static Task<Rig> StartAsync(
        Func<NodeOptions, NodeOptions> configure,
        params ulong[] listeners
    )
    {
        var bridge = new ListeningBridge();
        foreach (var listener in listeners)
            bridge.Cache[listener] = 555;
        return StartAsync(bridge, configure);
    }

    private static async Task<Rig> StartAsync(
        ListeningBridge bridge,
        Func<NodeOptions, NodeOptions> configure
    )
    {
        var server = new FakeNodeServer();
        var time = new FakeTimeProvider();
        bridge.GoReady();
        var extension = new NyraExtension(bridge);
        var node = new Node(
            new Endpoint("127.0.0.1", server.Port, server.Password),
            configure(
                new NodeOptions
                {
                    Name = "alone",
                    TimeProvider = time,
                    VoiceHandshakeTimeout = TimeSpan.FromSeconds(5),
                    ReadyTimeout = TimeSpan.FromSeconds(5),
                }
            )
        );
        extension.AddNode(node);
        await extension.ConnectAllNodesAsync().ConfigureAwait(false);

        var connection = await ConnectGuildAsync(bridge, node, server).ConfigureAwait(false);

        var current = "null";
        var paused = false;
        Rig? rig = null;
        server.On("PATCH", PlayerPath, call =>
        {
            rig?.BeforePatch?.Invoke(call.Body ?? string.Empty);
            if (call.Body is { } body)
            {
                using var document = JsonDocument.Parse(body);
                var root = document.RootElement;
                if (
                    root.TryGetProperty("track", out var track)
                    && track.ValueKind == JsonValueKind.Object
                    && track.GetProperty("encoded").GetString() is { } encoded
                )
                    current = TrackJson(encoded[4..]);
                if (root.TryGetProperty("paused", out var pause))
                    paused = pause.GetBoolean();
            }

            return new FakeRestResponse(
                200,
                $$$"""{"guildId":"777","track":{{{current}}},"volume":100,"paused":{{{(paused ? "true" : "false")}}},"state":{"time":0,"position":0,"connected":true,"ping":42},"voice":{"token":"tok","endpoint":"127.0.0.1","sessionId":"vsess"}}"""
            );
        });
        server.On("DELETE", PlayerPath, _ => new FakeRestResponse(204, string.Empty));

        rig = new Rig
        {
            Extension = extension,
            Server = server,
            Bridge = bridge,
            Node = node,
            Connection = connection,
            Time = time,
            LosePlayer = () =>
            {
                current = "null";
                paused = false;
            },
        };
        connection.ListenersChanged += (_, e) =>
        {
            rig.Log.Enqueue($"listeners:{e.Listeners}");
            return Task.CompletedTask;
        };
        connection.AloneTimedOut += (_, e) =>
        {
            rig.Log.Enqueue($"alone:{e.GuildId}:{e.ChannelId}:{rig.Connected}");
            return Task.CompletedTask;
        };
        return rig;
    }

    private static NodeOptions Leave(NodeOptions options) =>
        options with
        {
            AloneTimeout = Timeout5,
        };

    private static NodeOptions Pause(NodeOptions options) => options with { PauseWhenAlone = true };

    private static async Task Eventually(Func<bool> condition) =>
        await Assert.That(condition).WaitsFor(assert => assert.IsTrue(), TimeSpan.FromSeconds(5));

    [Test]
    public async Task CountsTheListenersInTheCacheAtConnect()
    {
        var rig = await StartAsync(options => options, 1, 2).ConfigureAwait(false);
        await using (rig.ConfigureAwait(false))
        {
            await Assert.That(rig.Connection.Listeners).IsEqualTo(2);
        }
    }

    [Test]
    public async Task FollowsJoinsLeavesAndMoves()
    {
        var rig = await StartAsync(options => options, 1).ConfigureAwait(false);
        await using (rig.ConfigureAwait(false))
        {
            rig.Bridge.Move(2, 555);
            await Assert.That(rig.Connection.Listeners).IsEqualTo(2);
            rig.Bridge.Move(1, 999);
            await Assert.That(rig.Connection.Listeners).IsEqualTo(1);
            rig.Bridge.Move(3, 999);
            await Assert.That(rig.Connection.Listeners).IsEqualTo(1);
            rig.Bridge.Move(2, null);
            await Assert.That(rig.Connection.Listeners).IsEqualTo(0);

            await Eventually(() => rig.Log.Count == 3).ConfigureAwait(false);
            await Assert
                .That(rig.Log)
                .IsEquivalentTo(["listeners:2", "listeners:1", "listeners:0"]);
        }
    }

    [Test]
    public async Task LeavesAfterTheTimeoutAlone()
    {
        var rig = await StartAsync(Leave).ConfigureAwait(false);
        await using (rig.ConfigureAwait(false))
        {
            await RunForAsync(rig.Time, TimeSpan.FromMinutes(4.9)).ConfigureAwait(false);
            await Assert.That(rig.Connected).IsTrue();

            var left = await AdvanceUntilAsync(
                    rig.Time,
                    () => !rig.Connected,
                    TimeSpan.FromMinutes(1)
                )
                .ConfigureAwait(false);

            await Assert.That(left).IsTrue();
            await Assert.That(rig.Log.Contains("alone:777:555:True")).IsTrue();
            await Assert.That(rig.Bridge.VoiceSends.Contains((777, null))).IsTrue();
        }
    }

    [Test]
    public async Task SomeoneJoiningStopsTheCount()
    {
        var rig = await StartAsync(Leave).ConfigureAwait(false);
        await using (rig.ConfigureAwait(false))
        {
            await RunForAsync(rig.Time, TimeSpan.FromMinutes(4)).ConfigureAwait(false);
            rig.Bridge.Move(1, 555);

            await RunForAsync(rig.Time, TimeSpan.FromMinutes(12)).ConfigureAwait(false);

            await Assert.That(rig.Connected).IsTrue();
            await Assert
                .That(rig.Log.Any(entry => entry.StartsWith("alone", StringComparison.Ordinal)))
                .IsFalse();
        }
    }

    [Test]
    public async Task TheCacheIsCheckedAgainBeforeLeaving()
    {
        var rig = await StartAsync(Leave).ConfigureAwait(false);
        await using (rig.ConfigureAwait(false))
        {
            rig.Bridge.Cache[1] = 555;

            await RunForAsync(rig.Time, TimeSpan.FromMinutes(12)).ConfigureAwait(false);

            await Assert.That(rig.Connected).IsTrue();
            await Assert.That(rig.Connection.Listeners).IsEqualTo(1);
        }
    }

    [Test]
    public async Task PausesWhenAloneAndResumesWhenSomeoneJoins()
    {
        var rig = await StartAsync(Pause, 1).ConfigureAwait(false);
        await using (rig.ConfigureAwait(false))
        {
            await rig.Connection.PlayAsync(Make("a")).ConfigureAwait(false);

            rig.Bridge.Move(1, null);
            await Eventually(() => rig.Connection.IsPaused).ConfigureAwait(false);

            rig.Bridge.Move(2, 555);
            await Eventually(() => !rig.Connection.IsPaused).ConfigureAwait(false);
        }
    }

    [Test]
    public async Task APauseByTheAppIsNotResumed()
    {
        var rig = await StartAsync(Pause, 1).ConfigureAwait(false);
        await using (rig.ConfigureAwait(false))
        {
            await rig.Connection.PlayAsync(Make("a")).ConfigureAwait(false);
            await rig.Connection.PauseAsync().ConfigureAwait(false);

            rig.Bridge.Move(1, null);
            rig.Bridge.Move(2, 555);
            await RunForAsync(rig.Time, TimeSpan.FromSeconds(5)).ConfigureAwait(false);

            await Assert.That(rig.Connection.IsPaused).IsTrue();
        }
    }

    [Test]
    public async Task NothingPlayingIsNotPaused()
    {
        var rig = await StartAsync(Pause, 1).ConfigureAwait(false);
        await using (rig.ConfigureAwait(false))
        {
            var patches = rig.Server.RestCalls.Count(call => call.Method == "PATCH");

            rig.Bridge.Move(1, null);
            await RunForAsync(rig.Time, TimeSpan.FromSeconds(5)).ConfigureAwait(false);

            await Assert
                .That(rig.Server.RestCalls.Count(call => call.Method == "PATCH"))
                .IsEqualTo(patches);
        }
    }

    [Test]
    public async Task CountsTheNewChannelAfterAMove()
    {
        var rig = await StartAsync(options => options, 1).ConfigureAwait(false);
        await using (rig.ConfigureAwait(false))
        {
            rig.Bridge.Cache[2] = 999;
            rig.Bridge.Cache[3] = 999;

            rig.Bridge.FireVoiceState(777, 999, "vsess");

            await Eventually(() => rig.Connection.Listeners == 2).ConfigureAwait(false);
            await Assert.That(rig.Connection.ChannelId).IsEqualTo(999UL);
        }
    }

    [Test]
    public async Task StartsCountingWhenTheCacheCatchesUp()
    {
        var bridge = new ListeningBridge { Unavailable = true };
        var rig = await StartAsync(bridge, options => options).ConfigureAwait(false);
        await using (rig.ConfigureAwait(false))
        {
            await Assert.That(rig.Connection.Listeners).IsNull();

            bridge.Unavailable = false;
            bridge.Move(1, 555);

            await Assert.That(rig.Connection.Listeners).IsEqualTo(1);
        }
    }

    [Test]
    public async Task AJoinDuringARestoreResumesAfterIt()
    {
        var rig = await StartAsync(
                options =>
                    options with
                    {
                        PauseWhenAlone = true,
                        RescuePlayback = true,
                        TimeProvider = TimeProvider.System,
                        ReconnectDelay = TimeSpan.FromMilliseconds(50),
                        MaxReconnectDelay = TimeSpan.FromMilliseconds(200),
                    },
                1
            )
            .ConfigureAwait(false);
        await using (rig.ConfigureAwait(false))
        {
            await rig.Connection.PlayAsync(Make("a")).ConfigureAwait(false);
            rig.Bridge.Move(1, null);
            await Eventually(() => rig.Connection.IsPaused).ConfigureAwait(false);

            var restoring = new TaskCompletionSource();
            var restored = new TaskCompletionSource();
            using var release = new ManualResetEventSlim();
            rig.Connection.GuildConnectionRestored += (_, _) =>
            {
                restored.TrySetResult();
                return Task.CompletedTask;
            };
            rig.BeforePatch = body =>
            {
                if (!body.Contains("\"voice\"", StringComparison.Ordinal))
                    return;

                restoring.TrySetResult();
                release.Wait(TimeSpan.FromSeconds(10));
            };
            rig.Server.ResumeOnReconnect = false;
            var before = rig.Server.RestCalls.Count;

            await rig.Server.DropConnectionAsync(1000, "restore").ConfigureAwait(false);
            await restoring.Task.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
            rig.Bridge.Move(2, 555);
            release.Set();
            await restored.Task.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);

            await Eventually(() =>
                    !rig.Connection.IsPaused
                    && rig.Server.RestCalls.Skip(before)
                        .Any(call =>
                            call.Body?.Contains("\"paused\":false", StringComparison.Ordinal)
                            == true
                        )
                )
                .ConfigureAwait(false);
        }
    }

    [Test]
    public async Task AJoinWhileTheNodeIsDownResumesAfterItReconnects()
    {
        var rig = await StartAsync(
                options =>
                    options with
                    {
                        PauseWhenAlone = true,
                        TimeProvider = TimeProvider.System,
                        ReconnectDelay = TimeSpan.FromSeconds(1),
                        MaxReconnectDelay = TimeSpan.FromSeconds(2),
                    },
                1
            )
            .ConfigureAwait(false);
        await using (rig.ConfigureAwait(false))
        {
            await rig.Connection.PlayAsync(Make("a")).ConfigureAwait(false);
            rig.Bridge.Move(1, null);
            await Eventually(() => rig.Connection.IsPaused).ConfigureAwait(false);
            var errors = 0;
            rig.Node.Error += (_, e) =>
            {
                if (e.Exception is InvalidOperationException)
                    Interlocked.Increment(ref errors);
                return Task.CompletedTask;
            };
            var before = rig.Server.RestCalls.Count;

            await rig.Server.DropConnectionAsync(1000, "down").ConfigureAwait(false);
            await Eventually(() => !rig.Node.IsReady).ConfigureAwait(false);
            rig.Bridge.Move(2, 555);

            await Assert
                .That(() =>
                    rig.Node.IsReady
                    && !rig.Connection.IsPaused
                    && rig.Server.RestCalls.Skip(before)
                        .Any(call =>
                            call.Body?.Contains("\"paused\":false", StringComparison.Ordinal)
                            == true
                        )
                )
                .WaitsFor(assert => assert.IsTrue(), TimeSpan.FromSeconds(10));
            await Assert.That(errors).IsEqualTo(0);
        }
    }

    [Test]
    public async Task AJoinWhileTheNodeIsDownRestoresThePlaybackInOneUpdate()
    {
        var rig = await StartAsync(
                options =>
                    options with
                    {
                        PauseWhenAlone = true,
                        RescuePlayback = true,
                        TimeProvider = TimeProvider.System,
                        ReconnectDelay = TimeSpan.FromSeconds(1),
                        MaxReconnectDelay = TimeSpan.FromSeconds(2),
                    },
                1
            )
            .ConfigureAwait(false);
        await using (rig.ConfigureAwait(false))
        {
            await rig.Connection.PlayAsync(Make("a")).ConfigureAwait(false);
            rig.Bridge.Move(1, null);
            await Eventually(() => rig.Connection.IsPaused).ConfigureAwait(false);

            var restored = new TaskCompletionSource();
            rig.Connection.GuildConnectionRestored += (_, _) =>
            {
                restored.TrySetResult();
                return Task.CompletedTask;
            };
            rig.Server.ResumeOnReconnect = false;
            var before = rig.Server.RestCalls.Count;

            rig.LosePlayer();
            await rig.Server.DropConnectionAsync(1000, "down").ConfigureAwait(false);
            await Eventually(() => !rig.Node.IsReady).ConfigureAwait(false);
            rig.Bridge.Move(2, 555);
            await restored.Task.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);

            var patches = rig
                .Server.RestCalls.Skip(before)
                .Where(call =>
                    call.Method == "PATCH"
                    && call.Path.EndsWith("/players/777", StringComparison.Ordinal)
                )
                .Select(call => call.Body ?? string.Empty)
                .ToList();
            await Assert.That(rig.Connection.IsPaused).IsFalse();
            await Assert.That(patches.Count).IsEqualTo(1);
            await Assert.That(patches[0]).Contains("\"voice\"");
            await Assert.That(patches[0]).Contains("\"track\"");
            await Assert.That(patches[0]).DoesNotContain("\"paused\"");
        }
    }

    [Test]
    public async Task ALeaveWhileTheNodeIsDownPausesAfterTheRestore()
    {
        var rig = await StartAsync(
                options =>
                    options with
                    {
                        PauseWhenAlone = true,
                        RescuePlayback = true,
                        TimeProvider = TimeProvider.System,
                        ReconnectDelay = TimeSpan.FromSeconds(1),
                        MaxReconnectDelay = TimeSpan.FromSeconds(2),
                    },
                1
            )
            .ConfigureAwait(false);
        await using (rig.ConfigureAwait(false))
        {
            await rig.Connection.PlayAsync(Make("a")).ConfigureAwait(false);

            var restored = new TaskCompletionSource();
            rig.Connection.GuildConnectionRestored += (_, _) =>
            {
                restored.TrySetResult();
                return Task.CompletedTask;
            };
            rig.Server.ResumeOnReconnect = false;
            var before = rig.Server.RestCalls.Count;

            rig.LosePlayer();
            await rig.Server.DropConnectionAsync(1000, "down").ConfigureAwait(false);
            await Eventually(() => !rig.Node.IsReady).ConfigureAwait(false);
            rig.Bridge.Move(1, null);
            await restored.Task.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
            await Eventually(() => rig.Connection.IsPaused).ConfigureAwait(false);

            var patches = rig
                .Server.RestCalls.Skip(before)
                .Where(call =>
                    call.Method == "PATCH"
                    && call.Path.EndsWith("/players/777", StringComparison.Ordinal)
                )
                .Select(call => call.Body ?? string.Empty)
                .ToList();
            await Assert.That(patches[0]).Contains("\"voice\"");
            await Assert.That(patches[0]).Contains("\"track\"");
            await Assert.That(patches[0]).DoesNotContain("\"paused\"");
            await Assert
                .That(
                    patches
                        .Skip(1)
                        .Any(body => body.Contains("\"paused\":true", StringComparison.Ordinal))
                )
                .IsTrue();

            rig.Bridge.Move(2, 555);
            await Eventually(() => !rig.Connection.IsPaused).ConfigureAwait(false);
        }
    }

    [Test]
    public async Task ALeaveDuringTheResumePausesAgain()
    {
        var rig = await StartAsync(Pause, 1).ConfigureAwait(false);
        await using (rig.ConfigureAwait(false))
        {
            await rig.Connection.PlayAsync(Make("a")).ConfigureAwait(false);
            rig.Bridge.Move(1, null);
            await Eventually(() => rig.Connection.IsPaused).ConfigureAwait(false);

            var resuming = new TaskCompletionSource();
            using var release = new ManualResetEventSlim();
            rig.BeforePatch = body =>
            {
                if (!body.Contains("\"paused\":false", StringComparison.Ordinal))
                    return;

                resuming.TrySetResult();
                release.Wait(TimeSpan.FromSeconds(10));
            };

            rig.Bridge.Move(2, 555);
            await resuming.Task.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
            rig.Bridge.Move(2, null);
            release.Set();

            await Eventually(() =>
                    rig.Connection.IsPaused
                    && rig.Server.RestCalls.LastOrDefault(call =>
                            call.Body?.Contains("\"paused\":", StringComparison.Ordinal) == true
                        )
                        ?.Body?.Contains("\"paused\":true", StringComparison.Ordinal) == true
                )
                .ConfigureAwait(false);
        }
    }

    [Test]
    public async Task ABridgeWithoutListenersReportsNull()
    {
        var server = new FakeNodeServer();
        var time = new FakeTimeProvider();
        var bridge = new FakeBridge();
        bridge.GoReady();
        var extension = new NyraExtension(bridge);
        await using (extension.ConfigureAwait(false))
        using (server)
        {
            var node = new Node(
                new Endpoint("127.0.0.1", server.Port, server.Password),
                new NodeOptions
                {
                    TimeProvider = time,
                    AloneTimeout = Timeout5,
                    VoiceHandshakeTimeout = TimeSpan.FromSeconds(5),
                    ReadyTimeout = TimeSpan.FromSeconds(5),
                }
            );
            extension.AddNode(node);
            await extension.ConnectAllNodesAsync().ConfigureAwait(false);
            var connection = await ConnectGuildAsync(bridge, node, server).ConfigureAwait(false);

            await RunForAsync(time, TimeSpan.FromMinutes(12)).ConfigureAwait(false);

            await Assert.That(connection.Listeners).IsNull();
            await Assert.That(node.GetConnection(777)).IsSameReferenceAs(connection);
        }
    }

    [Test]
    [Arguments(0)]
    [Arguments(-1000)]
    public async Task ANonPositiveTimeoutIsRejected(int milliseconds)
    {
        var options = new NodeOptions { AloneTimeout = TimeSpan.FromMilliseconds(milliseconds) };

        await Assert
            .That(() => new Node(new Endpoint("127.0.0.1", 1, "p"), options))
            .Throws<ArgumentOutOfRangeException>();
    }
}
