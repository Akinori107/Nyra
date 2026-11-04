using System.Text.Json;
using Nyra.Entities;

namespace Nyra.Tests;

public sealed class InfoEntity
{
    private static readonly JsonSerializerOptions NodeLink = NyraJson.OptionsFor(true);

    private static Player? ParsePlayer(string json) =>
        JsonSerializer.Deserialize<Player>(json, NodeLink);

    [Test]
    public async Task FadeRead()
    {
        var rounded = ParsePlayer(
            """{"guildId":"777","fading":{"trackStart":{"duration":1500.6,"curve":"linear","type":"volume"}}}"""
        );
        await Assert.That(rounded?.Fading?.TrackStart?.DurationMilliseconds).IsEqualTo(1501);
        var fromString = ParsePlayer(
            """{"guildId":"777","fading":{"trackEnd":{"duration":"2500","curve":null,"type":null}}}"""
        );
        await Assert
            .That(
                fromString?.Fading?.TrackEnd
                    is { DurationMilliseconds: 2500, Curve: null, Type: null }
            )
            .IsTrue();

        await Assert
            .That(
                ParsePlayer(
                    """{"guildId":"777","fading":{"seek":{"duration":"NaN"}}}"""
                )?.Fading?.Seek?.DurationMilliseconds
            )
            .IsNull();
        await Assert
            .That(
                ParsePlayer(
                    """{"guildId":"777","fading":{"seek":{"duration":"3000000000"}}}"""
                )?.Fading?.Seek?.DurationMilliseconds
            )
            .IsEqualTo(int.MaxValue);
        await Assert
            .That(() =>
            {
                _ = ParsePlayer("""{"guildId":"777","fading":{"seek":{"duration":true}}}""");
            })
            .Throws<JsonException>();
        await Assert
            .That(() =>
            {
                _ = ParsePlayer("""{"guildId":"777","fading":{"pause":{"curve":5}}}""");
            })
            .Throws<JsonException>();
    }

    [Test]
    public async Task NodeInfoMembers()
    {
        var lava = JsonSerializer.Deserialize<NodeInfo>(
            """{"version":{"semver":"4.2.0","major":4,"minor":2,"patch":0,"preRelease":""},"buildTime":1720000000000,"git":{"branch":"master","commit":"abc123","commitTime":1720000000000},"jvm":"17.0","lavaplayer":"1.1","sourceManagers":["youtube","http"],"filters":["volume"],"plugins":[{"name":"p","version":"1"}]}""",
            NodeLink
        );
        var nodelink = JsonSerializer.Deserialize<NodeInfo>(
            """{"version":{"semver":"3.9.1","major":3,"minor":9,"patch":1,"prerelease":["beta",null,"rc"]},"buildTime":1000,"git":{"branch":"main","commit":"def456","commitTime":1000},"node":"22.0","voice":{"name":"nyla","version":"1"},"sourceManagers":["youtube"],"filters":["volume"],"plugins":null}""",
            NodeLink
        );

        await Assert.That(lava?.Runtime).IsEqualTo("17.0");
        using (Assert.Multiple())
        {
            await Assert.That(nodelink?.Runtime).IsEqualTo("22.0");
            await Assert
                .That(nodelink?.Version.PreReleaseIdentifiers.SequenceEqual(["beta", "rc"]))
                .IsTrue();
            await Assert.That(nodelink?.Version.PreReleaseIdentifiers.Count).IsEqualTo(2);
        }

        var lavaEcho = JsonSerializer.Deserialize<NodeInfo>(
            """{"version":{"semver":"4.2.0","major":4,"minor":2,"patch":0,"preRelease":""},"buildTime":1720000000000,"git":{"branch":"master","commit":"abc123","commitTime":1720000000000},"jvm":"17.0","lavaplayer":"1.1","sourceManagers":["youtube","http"],"filters":["volume"],"plugins":[{"name":"p","version":"1"}]}""",
            NodeLink
        );
        using (Assert.Multiple())
        {
            await Assert.That(lava).IsEqualTo(lavaEcho);
            await Assert.That(lava).IsNotEqualTo(nodelink);
            await Assert.That(lava!.GetHashCode()).IsEqualTo(lavaEcho!.GetHashCode());
        }
    }

    [Test]
    public async Task NodeInfoVersionMembers()
    {
        var version = JsonSerializer.Deserialize<NodeInfoVersion>(
            """{"semver":"3.9.1-beta.1","major":3,"minor":9,"patch":1,"prerelease":["beta","1"]}""",
            NodeLink
        );
        var echo = JsonSerializer.Deserialize<NodeInfoVersion>(
            """{"semver":"3.9.1-beta.1","major":3,"minor":9,"patch":1,"prerelease":["beta","1"]}""",
            NodeLink
        );
        var other = JsonSerializer.Deserialize<NodeInfoVersion>(
            """{"semver":"3.9.1","major":3,"minor":9,"patch":1}""",
            NodeLink
        );

        using (Assert.Multiple())
        {
            await Assert.That(version).IsEqualTo(echo);
            await Assert.That(version).IsNotEqualTo(other);
            await Assert.That(version!.Equals(null)).IsFalse();
            await Assert.That(version!.GetHashCode()).IsEqualTo(echo!.GetHashCode());
        }
        var parsed = version!.Parsed;
        await Assert.That(parsed?.ToString().StartsWith("3.9.1")).IsTrue();
        var dev = JsonSerializer.Deserialize<NodeInfoVersion>(
            """{"semver":"commit-hash","major":0,"minor":0,"patch":0}""",
            NodeLink
        );
        await Assert.That(dev?.Parsed).IsNull();
    }

    [Test]
    public async Task NodeInfoGitTimes()
    {
        var valid = JsonSerializer.Deserialize<NodeInfoGit>(
            """{"branch":"master","commit":"abc","commitTime":1720000000000}""",
            NodeLink
        );
        await Assert.That(valid?.CommitTime.ToUnixTimeMilliseconds()).IsEqualTo(1720000000000);
    }

    [Test]
    [Arguments(0L)]
    [Arguments(-1L)]
    [Arguments(8_000_000_000_000_000L)]
    public async Task NodeInfoGitSentinelTimes(long sentinel)
    {
        var git = JsonSerializer.Deserialize<NodeInfoGit>(
            $$"""{"branch":"master","commit":"abc","commitTime":{{sentinel}}}""",
            NodeLink
        );
        await Assert.That(git?.CommitTime).IsEqualTo(DateTimeOffset.MinValue);
    }
}
