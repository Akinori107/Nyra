# Adapters

Each adapter gives you `UseNyra()`, `GetNyra()`, and a `ConnectAsync` on `Node` that takes the library's own voice channel type.

## DSharpPlus

Package `Nyra.DSharpPlus`.

```csharp
using Nyra.DSharpPlus;

var nyra = client.UseNyra();
```

Sharded clients, and one shard per process, have their own overloads. More info: [DiscordClientExtensions](../api/Nyra.DSharpPlus.DiscordClientExtensions.yml).

## DSharpPlus nightly

Package `Nyra.DSharpPlus.Nightly`. This adapter is not in the API reference, so its details are here.

The nightly version only accepts event handlers while the client is being built. So you turn Nyra on in the builder, not in the client. It also doesn't turn any intent on by itself, so `intents` must include `Guilds` and `GuildVoiceStates`:

```csharp
using Nyra.DSharpPlus.Nightly;

var builder = DiscordClientBuilder.CreateDefault(token, intents);
builder.UseNyra();

var client = builder.Build();
var nyra = client.GetNyra();
```

- Build the builder only once. A second `Build` makes a client that Nyra is not attached to, and its voice events never reach Nyra.
- A sharded client (`CreateSharded`) needs exactly one `UseNyra()`. `GetNyraAsync()` returns one entry under key 0, because one extension covers every shard.
- Close it yourself. Call `nyra.DisposeAsync()` when the bot stops.
- You can't remove handlers from a builder. To turn Nyra on again after closing it, use a new builder.

## Discord.Net

Package `Nyra.DiscordNet`.

```csharp
using Nyra.DiscordNet;

var nyra = client.UseNyra(logger);
```

Discord.Net has no `Microsoft.Extensions.Logging`, so pass an `ILogger` or Nyra logs nothing.

More info: [DiscordSocketClientExtensions](../api/Nyra.DiscordNet.DiscordSocketClientExtensions.yml).

## NetCord

Package `Nyra.NetCord`.

```csharp
using Nyra.NetCord;

var nyra = client.UseNyra(logger);
```

NetCord logs through its own logger, so pass an `ILogger` here to see Nyra's messages.

More info: [GatewayClientExtensions](../api/Nyra.NetCord.GatewayClientExtensions.yml).
