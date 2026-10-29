using System.Text.Json;
using Nyra.Entities;

[assembly: NotInParallel]
[assembly: Timeout(300_000)]

namespace Nyra.Tests;

public sealed class VersionEndpointAndReader
{
    [Test]
    public async Task Versions()
    {
        await Assert.That(NodeVersion.TryParse("3.10.0", out var plain)).IsTrue();
        await Assert.That(plain.Major).IsEqualTo(3);
        await Assert.That(NodeVersion.TryParse("3.10.0-dev.2026-09-27.1", out var dev)).IsTrue();
        await Assert.That(dev.Minor).IsEqualTo(10);
        using (Assert.Multiple())
        {
            await Assert.That(NodeVersion.TryParse("not-a-version", out _)).IsFalse();
            await Assert.That(NodeVersion.TryParse(string.Empty, out _)).IsFalse();
            await Assert.That(NodeVersion.TryParse((string?)null, out _)).IsFalse();
        }
    }

    [Test]
    public async Task Endpoints()
    {
        await Assert.That(new Endpoint("127.0.0.1", 3000, "pass").Port).IsEqualTo(3000);
        await Assert.That(new Endpoint("%zz", 3000, "pass").Host).IsEqualTo("%zz");
        await Assert
            .That(() =>
            {
                _ = new Endpoint("127.0.0.1", 3000, string.Empty);
            })
            .Throws<ArgumentException>();
    }

    [Test]
    public async Task JsonReaders()
    {
        var options = NyraJson.SerializerOptions;
        var player = JsonSerializer.Deserialize<Player>("""{"guildId": 123}""", options);
        await Assert.That(player?.GuildId).IsEqualTo(123);
        player = JsonSerializer.Deserialize<Player>("""{"guildId": "456"}""", options);
        await Assert.That(player?.GuildId).IsEqualTo(456);

        await Assert
            .That(() =>
            {
                _ = JsonSerializer.Deserialize<Player>("""{"guildId": "12x3"}""", options);
            })
            .Throws<JsonException>();

        await Assert
            .That(() =>
            {
                _ = JsonSerializer.Deserialize<Player>("""{"guildId": true}""", options);
            })
            .Throws<JsonException>();

        var rounded = JsonSerializer.Deserialize<Player>("""{"volume": 2.5}""", options);
        await Assert.That(rounded?.Volume).IsEqualTo(3);
        rounded = JsonSerializer.Deserialize<Player>("""{"volume": "500.5"}""", options);
        await Assert.That(rounded?.Volume).IsEqualTo(501);
        await Assert
            .That(() =>
            {
                _ = JsonSerializer.Deserialize<Player>("""{"volume": "abc"}""", options);
            })
            .Throws<JsonException>();
    }

    [Test]
    public async Task Equality()
    {
        var options = NyraJson.SerializerOptions;
        const string infoJson =
            """{"version":{"semver":"3.10.0","source":"a","commit":"c","buildTime":100,"java":"j","lavaplayer":"l","node":"n"},"sourceManagers":[],"filters":[],"plugins":[]}""";
        var infoA = JsonSerializer.Deserialize<NodeInfo>(infoJson, options);
        var infoB = JsonSerializer.Deserialize<NodeInfo>(infoJson, options);
        await Assert.That(infoA).IsNotNull();
        await Assert.That(infoA).IsEqualTo(infoB);
        var infoC = JsonSerializer.Deserialize<NodeInfo>(
            infoJson.Replace("3.10.0", "3.10.1"),
            options
        );
        await Assert.That(infoA).IsNotNull();
        await Assert.That(infoA).IsNotEqualTo(infoC);

        const string statsJson =
            """{"players":1,"playing":1,"uptime":100,"memory":{"free":1,"used":1,"allocated":1,"reservable":1},"cpu":{"cores":2,"systemLoad":0.1,"lavalinkLoad":0.2,"nodelinkLoad":0.3}}""";
        var statsA = JsonSerializer.Deserialize<Stats>(statsJson, options);
        var statsB = JsonSerializer.Deserialize<Stats>(statsJson, options);
        await Assert.That(statsA).IsNotNull();
        await Assert.That(statsA).IsEqualTo(statsB);
    }
}
