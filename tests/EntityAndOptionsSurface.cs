using System.Text.Json;
using Nyra.Entities;

namespace Nyra.Tests;

public sealed class EntityAndOptionsSurface
{
    [Test]
    public async Task Endpoint()
    {
        await Assert.That(new Endpoint("[::]", 3000, "pass").Host).IsEqualTo("[::]");
        await Assert.That(new Endpoint("[zzz]", 3000, "pass").Host).IsEqualTo("[zzz]");
    }

    [Test]
    public async Task VersionCompare()
    {
        _ = NodeVersion.TryParse("2.0.0", out var two);
        _ = NodeVersion.TryParse("1.9.9", out var older);
        _ = NodeVersion.TryParse("2.1.0", out var newerMinor);
        _ = NodeVersion.TryParse("2.0.1", out var newerPatch);
        using (Assert.Multiple())
        {
            await Assert.That(two.CompareTo(older)).IsGreaterThan(0);
            await Assert.That(two.CompareTo(newerMinor)).IsLessThan(0);
            await Assert.That(two.CompareTo(newerPatch)).IsLessThan(0);
            await Assert.That(two.CompareTo(two)).IsEqualTo(0);
        }
    }

    [Test]
    public async Task DuckingWithoutADurationOmitsItUnderPlainOptions()
    {
        var written = JsonSerializer.Serialize(new DuckingOptions { Enabled = true });

        await Assert.That(written).DoesNotContain("duration");
    }

    [Test]
    public async Task PlayerVoiceDiagnosticsPreserveMetadataWithoutCredentials()
    {
        const string token = "fixture-voice-token";
        const string session = "fixture-voice-session";
        var player = JsonSerializer.Deserialize<Player>(
            """{"guildId":123,"voice":{"token":"fixture-voice-token","sessionId":"fixture-voice-session","endpoint":"voice.example.com","channelId":987}}"""
        )!;
        var voice = player.Voice!;
        foreach (var diagnostic in new[] { voice.ToString(), player.ToString() })
        {
            await Assert.That(diagnostic).DoesNotContain(token);
            await Assert.That(diagnostic).DoesNotContain(session);
            await Assert.That(diagnostic).Contains("voice.example.com");
            await Assert.That(diagnostic).Contains("987");
        }

        var json = JsonSerializer.Serialize(voice);
        var roundTrip = JsonSerializer.Deserialize<PlayerVoiceState>(json);
        await Assert.That(roundTrip).IsEqualTo(voice);
        await Assert.That(roundTrip?.Token).IsEqualTo(token);
        await Assert.That(roundTrip?.SessionId).IsEqualTo(session);
    }
}
