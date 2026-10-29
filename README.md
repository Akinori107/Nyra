# Nyra

[![NuGet](https://img.shields.io/nuget/vpre/Nyra?color=4f46e5&labelColor=2e363e)](https://www.nuget.org/packages/Nyra) [![Downloads](https://img.shields.io/nuget/dt/Nyra?color=14b8a6&labelColor=2e363e)](https://www.nuget.org/packages/Nyra) [![Build](https://github.com/Akinori107/Nyra/actions/workflows/checks.yml/badge.svg?branch=dev)](https://github.com/Akinori107/Nyra/actions/workflows/checks.yml) [![Codecov](https://img.shields.io/codecov/c/github/Akinori107/Nyra/dev?logo=codecov&label=Codecov&color=9bc405&labelColor=2e363e)](https://codecov.io/gh/Akinori107/Nyra) [![License](https://img.shields.io/badge/license-BlueOak--1.0.0-blue?labelColor=2e363e)](LICENSE.md)

Nyra is a .NET 10 client for [Lavalink v4.2+](https://github.com/lavalink-devs/Lavalink) and [NodeLink v3.9+](https://github.com/PerformanC/NodeLink), with adapters for [DSharpPlus](https://github.com/DSharpPlus/DSharpPlus), [Discord.Net](https://github.com/discord-net/Discord.Net) and [NetCord](https://github.com/NetCordDev/NetCord). One API works with both servers, finds out which one you're using, and gives you what only NodeLink has, like crossfade, ducking and exclusive filters such as reverb, chorus and phonograph. The two servers don't always behave the same, and the [NodeLink compatibility notes](#nodelink-compatibility-notes) cover some of the differences. Guides and the full API reference are on the [docs site](https://nyradocs.pages.dev).

## Supported servers

|  Server  | Minimum | Recommended  |
|:--------:|:-------:|:------------:|
| Lavalink |  4.2.0  |    4.2.2     |
| NodeLink |  3.9.0  | 3.10.0 (dev) |

You don't need any config, Nyra figures out which server it's talking to. More on [Runtime detection](#runtime-detection).

## Requirements

- .NET 10 or newer
- One of the supported Discord libraries, see [Adapters](#adapters)

## Installation

Install the adapter for your Discord library, it brings the core `Nyra` package along:

```sh
dotnet add package Nyra.DSharpPlus
```

## Adapters

| Library | Package | Entry point |
|:--|:--|:--|
| DSharpPlus | `Nyra.DSharpPlus` | `DiscordClient.UseNyra()` |
| DSharpPlus (nightly) | `Nyra.DSharpPlus.Nightly` | `DiscordClientBuilder.UseNyra()`, then `client.GetNyra()` |
| Discord.Net | `Nyra.DiscordNet` | `DiscordSocketClient.UseNyra()` |
| NetCord | `Nyra.NetCord` | `GatewayClient.UseNyra()` |

Each adapter lives in its own namespace (`Nyra.DSharpPlus`, `Nyra.DSharpPlus.Nightly`, `Nyra.DiscordNet`, `Nyra.NetCord`), and the sharded clients have their own overloads.

## Minimal example

```csharp
using Nyra;
using Nyra.Entities;

var nyra = client.UseNyra();
nyra.AddNode(new Node(
    new Endpoint("localhost", 2333, "youshallnotpass"),
    new NodeOptions { Name = "main", Resuming = true }));

await nyra.ConnectAllNodesAsync();

var node = nyra.GetNode()!;
var connection = await node.ConnectAsync(guildId, voiceChannelId);

if (await node.LoadTracksAsync("never gonna give you up") is LoadResult.SearchLoaded { Tracks: [var track, ..] })
    await connection.PlayAsync(track);
```

`client` is the client from your Discord library. `UseNyra()` comes from the [adapter](#adapters) you installed, so add its `using` too.

## Runtime detection

The server type is read from `GET /v4/info` each time a node connects:

```csharp
var node = nyra.GetNode()!;
Console.WriteLine($"{node.Name}: {(node.IsNodeLink ? "NodeLink" : "Lavalink")} {node.Version}");
```

A few methods only work on one server and throw `NotSupportedException` on the other. `SetVolumeFilterAsync`, for example, throws on NodeLink, because NodeLink accepts the field but never applies it. An error is better than silently doing nothing.

## NodeLink compatibility notes

**Re-sent tracks:** Playing the track that is already playing restarts it on Lavalink, but NodeLink keeps it going. Nyra stops it first on NodeLink, so it restarts on both.

**Filter timing:** NodeLink drops a filter change when no track is loaded, while Lavalink keeps it for the next track. Nyra remembers what NodeLink dropped and sends it again when the next track starts, so a filter set between tracks works the same on both. `connection.Filters.Applied` shows what the server confirmed, not what you sent last.

## Example bot

A minimal slash-command bot lives in [`examples`](examples), with two files so you can try it out. It uses the DSharpPlus adapter. More commands may be added eventually.

Create a `.env` file inside `examples` with `DISCORD_TOKEN`, `NODE_HOST`, `NODE_PORT`, `NODE_PASSWORD` and optionally `NODE_SECURE`, then run it:

```sh
cd examples
dotnet run
```
