# Lavalink and NodeLink

Both servers use the same API. A call the server can't do throws before anything is sent.

## Which server is it

Nyra reads `GET /v4/info` every time a node connects, so you never say which server it is. `node.IsNodeLink` is `true` for NodeLink, `node.Version` is the version, and `node.HasPlugin("lavasrc-plugin")` checks for a plugin. These come from the last connect, so before the first connect `IsNodeLink` is `false` and `Version` is `null`.

A `null` version means a dev build. Dev builds count as the newest version, so treat `null` as new when you compare (`node.Version >= x` is `false` when it is `null`):

```csharp
if (node.Version is not { } version || version >= new NodeVersion(3, 10, 0))
{
    // the server is 3.10.0 or newer, or a dev build
}
```

Nyra needs Lavalink 4.2.0 or newer and NodeLink 3.9.0 or newer. Calls on an older server throw `IncompatibleServerException`. More info: [Node](../api/Nyra.Node.yml) and [NodeVersion](../api/Nyra.NodeVersion.yml).

## What only one server can do

Calling these on the wrong server throws `NotSupportedException` with the feature's name. If your bot runs on both servers, check `node.IsNodeLink` first.

**Only NodeLink**

- These filters: `EchoFilter`, `ReverbFilter`, `ChorusFilter`, `PhaserFilter`, `FlangerFilter`, `HighPassFilter`, `CompressorFilter`, `SpatialFilter`, `PhonographFilter`, `TesseractFilter`. Also filter transitions
- Crossfade, fading, ducking, loudness normalizer and loading the next track ahead
- Mix layers
- SponsorBlock and YouTube settings
- Player groups (`node.Groups`)
- Chapters and track meanings, `node.Tracks.EncodeAsync`, stream URLs and audio streams
- Live stream info and EternalBox events
- `PlayOptions.AudioTrackId` and `node.GetConnectionStatusAsync()`
- Some search sources. Each `SearchType` value says which

**Only Lavalink**

- `connection.PlayIdentifierAsync`
- `connection.Filters.SetVolumeFilterAsync`

**NodeLink, or Lavalink with a plugin**

- Lyrics need NodeLink, or Lavalink with the LavaLyrics plugin
- Some search sources need NodeLink, or Lavalink with the LavaSrc plugin

When the two servers do the same thing in different ways, the `remarks` of that member in the [API reference](../api/Nyra.yml) say how.
