# Nyra

![NuGet](https://img.shields.io/nuget/vpre/Nyra?color=blueviolet) ![Downloads](https://img.shields.io/nuget/dt/Nyra?color=darkgreen) ![License](https://img.shields.io/badge/license-BlueOak--1.0.0-blue)

Nyra is a .NET 10 client for [Lavalink v4+](https://github.com/lavalink-devs/Lavalink) and [NodeLink v3+](https://github.com/PerformanC/NodeLink), built on [DSharpPlus 4.5.x](https://github.com/DSharpPlus/DSharpPlus) (maybe [Discord.Net](https://github.com/discord-net/Discord.Net) in the future). Load tracks, control playback, apply filters, manage voice connections, one API for both servers. Where they genuinely behave differently, some of the differences are listed in [NodeLink compatibility notes](#nodelink-compatibility-notes).

## Supported servers

|  Server  | Minimum | Recommended |
|:--------:|:-------:|:-----------:|
| Lavalink |  4.2.0  |    4.2.2    |
| NodeLink |  3.8.0  | 3.9.0 (dev) |

There's no config needed, Nyra probes `GET /v4/info` on connect and figures out which server you're talking to. See more on [Runtime Detection](#runtime-detection).

## Requirements

- .NET 10 or newer
- DSharpPlus 4.5.x

## Installation

```
dotnet add package Nyra
```

## Minimal Example

```csharp
using DSharpPlus;
using DSharpPlus.Entities;
using DSharpPlus.SlashCommands;
using Nyra;
using Nyra.Entities;

var discord = new DiscordClient(new DiscordConfiguration
{
    Token = Environment.GetEnvironmentVariable("DISCORD_TOKEN"),
    TokenType = TokenType.Bot,
    // AllUnprivileged is all a music bot needs (at least for a slash-command bot).
    Intents = DiscordIntents.AllUnprivileged
});

var nyra = discord.UseNyra();
nyra.AddNode(new Node(
    new Endpoint("localhost", 2333, "youshallnotpass"),
    new NodeOptions { Name = "main", Resuming = true }));

// A node that fails to connect logs a warning and keeps retrying in the background.
discord.Ready += async (_, _) => await nyra.ConnectAllNodesAsync();

var slash = discord.UseSlashCommands();
slash.RegisterCommands<MusicCommands>();

await discord.ConnectAsync();
await Task.Delay(-1);
```

`MusicCommands` is your own slash-command module.

## Runtime detection

On the first contact with a node, or an early `LoadTracksAsync`, Nyra probes `GET /v4/info` once:

```csharp
var node = nyra.GetNode()!;
Console.WriteLine($"{node.Name}: {(node.IsNodeLink ? "NodeLink" : "Lavalink")} {node.Version}");
```

The detected kind picks the right response converters and gates the handful of operations that only make sense on one server. `SetVolumeFilterAsync`, for instance, throws `NotSupportedException` on NodeLink, since it accepts the field but never implements it, so failing loudly beats failing silently.

## NodeLink compatibility notes

The two servers agree on most of the protocol and disagree on the rest in ways that aren't always documented. Here's what Nyra handles for you:

**Load results:** NodeLink's extra types (`episode`, `album`, `artist`, `station`, `podcast`, `show`) map onto track/playlist results; its `exception` shape maps onto `LoadFailed`. A load type Nyra doesn't recognize degrades to `LoadFailed` instead of throwing.

**Updating the player:** Nyra always sends `track: { encoded }` since it's the only form both servers accept, and NodeLink rejects the older top-level `encodedTrack`. Stopping sends `track: { encoded: null }`.

**Timestamped links:** Both servers ignore the `t`/`start` parameter of a link (e.g. `...&t=90`). Nyra parses it, carries it on `Track.StartPosition`, and applies it as the position of the track's first `PlayAsync`, though an explicit `PlayOptions.StartPosition` wins instead. Only the selected entry of a playlist gets it. NodeLink doesn't restart a re-sent identical track, so Nyra stops it first there to force a restart.

**Filters:** Every filter goes out fully specified, unset members included as their neutral defaults. This matters because Lavalink resets what you don't send while NodeLink merges a partial object into whatever was there before, so the full form is the only one that behaves the same on both. Filters cleared with an explicit `null` (or the neutral value where the servers reject an actual null, like `volume`/`equalizer`) get the same treatment. On the read side, NodeLink nests filter state under an extra `filters` key, wraps the equalizer in `bands`, and marks disabled filters with `_disabled`, all of which Nyra normalizes. The volume *filter* specifically only exists on Lavalink; asking for it on NodeLink throws rather than doing nothing.

**Filter timing:** NodeLink drops a filter change if no track is loaded; Lavalink queues it for the next one. Nyra remembers what NodeLink couldn't take and resends it when the next track starts, so a filter set between tracks behaves consistently either way. `AppliedFilters` reflects what the server actually confirmed, not what you last sent.

**Stats:** Load is reported under `nodelinkLoad` on NodeLink and `lavalinkLoad` on Lavalink; both are exposed, plus a normalized `NodeLoad` that doesn't care which one you're on.

**Events:** NodeLink sends event types Lavalink doesn't, including player lifecycle, seek, volume, and more. Unknown ones are ignored rather than faulting the connection; unknown enum values inside known events fall back to an `Unknown` member instead of dropping the whole event.

**Handshake:** NodeLink requires `Client-Name` on the websocket upgrade (Lavalink just warns if it's missing), so Nyra always sends it (`Nyra/<version>`). Partial Discord voice server updates (empty endpoint) are held back until the complete one arrives, since both servers 400 on an incomplete triple anyway.

**Track decoding:** Handles Lavalink v1–v3 encodings plus NodeLink's `details` extension. NodeLink's legacy `NLK:seekable` trailer gets detected structurally and trimmed so position decodes correctly instead of coming out wrong by 16 bytes.

**Pausing mid-play:** Playing a track while paused force-unpauses on Lavalink but leaves NodeLink paused. `IsPaused` always reflects what the server confirmed, so check it after `PlayAsync` if you called it on a paused connection.

**Losing a session:** A reconnect that didn't resume means the old session and every player on it is gone. Nyra invalidates the affected guild connections (they stop claiming to be connected) and logs a warning instead of leaving zombies around. Call `RefreshAsync` to re-read the authoritative player state at any point, resumed or not.

## Example bot

A minimal slash-command bot lives in [`examples`](examples), with two files so you can try it out. More commands may be added eventually (no promises, lol).

```
cd examples
dotnet run
```

Please don't forget to replace `"your token here"` with your bot token
