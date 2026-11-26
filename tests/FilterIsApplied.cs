using System.Text.Json;
using Nyra.Entities.Filters;
using static Nyra.Tests.NodeHarness;

namespace Nyra.Tests;

public sealed class FilterIsApplied
{
    private const string PlayerHead =
        """{"guildId":"777","track":null,"volume":100,"paused":false,"state":{"time":0,"position":0,"connected":true,"ping":42},"voice":{"token":"tok","endpoint":"127.0.0.1","sessionId":"vsess"},"filters":{""";

    private sealed record FakeFilter : IFilter;

    private static FakeRestResponse EchoFilters(FakeRestCall call)
    {
        using var request = JsonDocument.Parse(call.Body ?? "{}");
        var held =
            request.RootElement.TryGetProperty("filters", out var filters)
            && filters.ValueKind == JsonValueKind.Object
                ? string.Join(
                    ",",
                    filters
                        .EnumerateObject()
                        .Where(member => member.Value.ValueKind != JsonValueKind.Null)
                        .Select(member => $"\"{member.Name}\":{member.Value.GetRawText()}")
                )
                : string.Empty;
        return new FakeRestResponse(200, PlayerHead + held + "}}");
    }

    private static async Task<(Stack Stack, GuildConnection Connection)> StartAsync()
    {
        var server = new FakeNodeServer();
        var bridge = new FakeBridge();
        bridge.GoReady();
        var extension = new NyraExtension(bridge);
        var node = new Node(
            new Endpoint("127.0.0.1", server.Port, server.Password),
            new NodeOptions
            {
                Name = "applied",
                ReconnectDelay = TimeSpan.FromMilliseconds(50),
                MaxReconnectDelay = TimeSpan.FromMilliseconds(200),
                VoiceHandshakeTimeout = TimeSpan.FromSeconds(5),
                ReadyTimeout = TimeSpan.FromSeconds(5),
            }
        );
        extension.AddNode(node);
        await extension.ConnectAllNodesAsync().ConfigureAwait(false);
        var connection = await ConnectGuildAsync(bridge, node, server).ConfigureAwait(false);
        server.On("PATCH", PlayerPath, EchoFilters);
        var stack = new Stack
        {
            Bridge = bridge,
            Extension = extension,
            Node = node,
            Server = server,
        };
        return (stack, connection);
    }

    [Test]
    public async Task NothingAppliedYet()
    {
        var (stack, conn) = await StartAsync().ConfigureAwait(false);
        await using (stack.ConfigureAwait(false))
            await Assert
                .That(conn.Filters.IsApplied(new TremoloFilter { Frequency = 8, Depth = 0.5f }))
                .IsFalse();
    }

    [Test]
    public async Task HoldsTheAppliedFilter()
    {
        var (stack, conn) = await StartAsync().ConfigureAwait(false);
        await using (stack.ConfigureAwait(false))
        {
            await conn
                .Filters.AddAsync(new TremoloFilter { Frequency = 8, Depth = 0.5f })
                .ConfigureAwait(false);

            await Assert
                .That(conn.Filters.IsApplied(new TremoloFilter { Frequency = 8, Depth = 0.5f }))
                .IsTrue();
        }
    }

    [Test]
    public async Task DifferentValueOrKindIsNotApplied()
    {
        var (stack, conn) = await StartAsync().ConfigureAwait(false);
        await using (stack.ConfigureAwait(false))
        {
            await conn
                .Filters.AddAsync(new TremoloFilter { Frequency = 8, Depth = 0.5f })
                .ConfigureAwait(false);

            using (Assert.Multiple())
            {
                await Assert
                    .That(conn.Filters.IsApplied(new TremoloFilter { Frequency = 5, Depth = 0.5f }))
                    .IsFalse();
                await Assert
                    .That(conn.Filters.IsApplied(new TremoloFilter { Frequency = 8, Depth = 0.9f }))
                    .IsFalse();
                await Assert
                    .That(conn.Filters.IsApplied(new VibratoFilter { Frequency = 8, Depth = 0.5f }))
                    .IsFalse();
            }
        }
    }

    [Test]
    public async Task UnsetMembersAreNotCompared()
    {
        var (stack, conn) = await StartAsync().ConfigureAwait(false);
        await using (stack.ConfigureAwait(false))
        {
            await conn
                .Filters.AddAsync(new TremoloFilter { Frequency = 8, Depth = 0.9f })
                .ConfigureAwait(false);

            using (Assert.Multiple())
            {
                await Assert
                    .That(conn.Filters.IsApplied(new TremoloFilter { Frequency = 8 }))
                    .IsTrue();
                await Assert
                    .That(conn.Filters.IsApplied(new TremoloFilter { Depth = 0.9f }))
                    .IsTrue();
                await Assert
                    .That(conn.Filters.IsApplied(new TremoloFilter { Frequency = 5 }))
                    .IsFalse();
                await Assert
                    .That(conn.Filters.IsApplied(new TremoloFilter { Depth = 0.5f }))
                    .IsFalse();
            }
        }
    }

    [Test]
    public async Task TransitionIsNotCompared()
    {
        var (stack, conn) = await StartAsync().ConfigureAwait(false);
        await using (stack.ConfigureAwait(false))
        {
            await conn
                .Filters.AddAsync(
                    new TremoloFilter
                    {
                        Frequency = 8,
                        Depth = 0.9f,
                        Transition = new FilterTransition(500, "linear"),
                    }
                )
                .ConfigureAwait(false);

            using (Assert.Multiple())
            {
                await Assert
                    .That(conn.Filters.IsApplied(new TremoloFilter { Frequency = 8, Depth = 0.9f }))
                    .IsTrue();
                await Assert
                    .That(
                        conn.Filters.IsApplied(
                            new TremoloFilter
                            {
                                Frequency = 8,
                                Depth = 0.9f,
                                Transition = new FilterTransition(2000, "sinusoidal"),
                            }
                        )
                    )
                    .IsTrue();
                await Assert
                    .That(
                        conn.Filters.IsApplied(
                            new TremoloFilter
                            {
                                Frequency = 5,
                                Depth = 0.9f,
                                Transition = new FilterTransition(500, "linear"),
                            }
                        )
                    )
                    .IsFalse();
            }
        }
    }

    [Test]
    public async Task ReplacingKeepsOnlyTheLatest()
    {
        var (stack, conn) = await StartAsync().ConfigureAwait(false);
        await using (stack.ConfigureAwait(false))
        {
            await conn
                .Filters.AddAsync(new TremoloFilter { Frequency = 8, Depth = 0.5f })
                .ConfigureAwait(false);
            await conn
                .Filters.AddAsync(new TremoloFilter { Frequency = 5, Depth = 0.5f })
                .ConfigureAwait(false);

            using (Assert.Multiple())
            {
                await Assert
                    .That(conn.Filters.IsApplied(new TremoloFilter { Frequency = 8, Depth = 0.5f }))
                    .IsFalse();
                await Assert
                    .That(conn.Filters.IsApplied(new TremoloFilter { Frequency = 5, Depth = 0.5f }))
                    .IsTrue();
            }
        }
    }

    [Test]
    public async Task RemovedOrClearedIsNotApplied()
    {
        var (stack, conn) = await StartAsync().ConfigureAwait(false);
        await using (stack.ConfigureAwait(false))
        {
            var tremolo = new TremoloFilter { Frequency = 8, Depth = 0.5f };

            await conn.Filters.AddAsync(tremolo).ConfigureAwait(false);
            await conn.Filters.RemoveAsync<TremoloFilter>().ConfigureAwait(false);
            await Assert.That(conn.Filters.IsApplied(tremolo)).IsFalse();

            await conn.Filters.AddAsync(tremolo).ConfigureAwait(false);
            await conn.Filters.ClearAsync().ConfigureAwait(false);
            await Assert.That(conn.Filters.IsApplied(tremolo)).IsFalse();
        }
    }

    [Test]
    public async Task RejectsNullAndUserDefinedFilters()
    {
        var (stack, conn) = await StartAsync().ConfigureAwait(false);
        await using (stack.ConfigureAwait(false))
        {
            await Assert
                .That(() =>
                {
                    _ = conn.Filters.IsApplied(null!);
                })
                .Throws<ArgumentNullException>();
            await Assert
                .That(() =>
                {
                    _ = conn.Filters.IsApplied(new FakeFilter());
                })
                .Throws<ArgumentException>();
        }
    }
}
