using System.Text.Json;
using Nyra.Entities;

namespace Nyra.Tests;

public sealed class GroupTrackTests
{
    private static string Serialize(GroupUpdate update) =>
        NyraJson.Serialize(update, NyraJson.OptionsFor(true));

    private static GroupUpdate? Deserialize(string json) =>
        JsonSerializer.Deserialize<GroupUpdate>(json, NyraJson.OptionsFor(true));

    [Test]
    public async Task EncodedForm()
    {
        var body = Serialize(
            new GroupUpdate { Track = new GroupTrack { Encoded = "QXRsZWFzdA==" } }
        );
        await Assert.That(body).Contains("\"track\":{\"encoded\":\"QXRsZWFzdA==\"}");
        var read = Deserialize(body);
        await Assert.That(read?.Track?.Encoded).IsEqualTo("QXRsZWFzdA==");
    }

    [Test]
    public async Task IdentifierForm()
    {
        var body = Serialize(
            new GroupUpdate { Track = new GroupTrack { Identifier = "ph8qt3dhvwo" } }
        );
        await Assert.That(body).Contains("\"track\":{\"identifier\":\"ph8qt3dhvwo\"}");
        var read = Deserialize(body);
        await Assert.That(read?.Track?.Identifier).IsEqualTo("ph8qt3dhvwo");
    }

    [Test]
    public async Task StopForm()
    {
        var body = Serialize(new GroupUpdate { Track = GroupTrack.Stop });
        await Assert.That(body).Contains("\"track\":{\"encoded\":null}");
        var read = Deserialize(body);
        await Assert.That(read?.Track is { Encoded: null, Identifier: null }).IsTrue();
    }

    [Test]
    public async Task AudioTrackForm()
    {
        var body = Serialize(
            new GroupUpdate
            {
                Track = new GroupTrack
                {
                    AudioTrackId = "at-1",
                    Language = "en",
                    Encoded = "QXRsZWFzdA==",
                },
            }
        );
        await Assert.That(body).Contains("\"audioTrackId\":\"at-1\"");
        await Assert.That(body).Contains("\"language\":\"en\"");
        var read = Deserialize(body);
        await Assert.That(read?.Track is { AudioTrackId: "at-1", Language: "en" }).IsTrue();
    }

    [Test]
    public async Task UserDataForm()
    {
        using var payload = JsonDocument.Parse("""{"key":"value"}""");
        var body = Serialize(
            new GroupUpdate
            {
                Track = new GroupTrack
                {
                    Encoded = "QXRsZWFzdA==",
                    UserData = payload.RootElement.Clone(),
                },
            }
        );
        await Assert.That(body).Contains("\"userData\":{\"key\":\"value\"}");
        var read = Deserialize(body);
        await Assert
            .That(
                read?.Track?.UserData is { } data
                    && data.TryGetProperty("key", out var value)
                    && value.GetString() == "value"
            )
            .IsTrue();
    }

    [Test]
    public async Task MutualExclusion()
    {
        await Assert
            .That(() =>
            {
                Serialize(
                    new GroupUpdate
                    {
                        Track = new GroupTrack
                        {
                            Encoded = "QXRsZWFzdA==",
                            Identifier = "ph8qt3dhvwo",
                        },
                    }
                );
            })
            .Throws<ArgumentException>();
    }

    [Test]
    public async Task ReadTolerance()
    {
        var read = Deserialize(
            """{"track":{"encoded":123,"identifier":true,"userData":[1,2],"language":null,"mystery":9}}"""
        );
        await Assert
            .That(read?.Track is { Encoded: null, Identifier: null, Language: null })
            .IsTrue();
        await Assert.That(read?.Track?.UserData is { ValueKind: JsonValueKind.Array }).IsTrue();

        var notAnObject = Deserialize("""{"track":"a string"}""");
        await Assert.That(notAnObject?.Track).IsNull();
    }
}
