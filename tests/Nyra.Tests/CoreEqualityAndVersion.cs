using System.Text.Json;
using Nyra.Entities;
using Nyra.Internal;

namespace Nyra.Tests;

public sealed class CoreEqualityAndVersion
{
    [Test]
    public async Task ContentEqualityCases()
    {
        List<string>? nullList = null;
        var list = new List<string> { "a" };
        var otherList = new List<string> { "b" };
        var shorter = new List<string>();

        using (Assert.Multiple())
        {
            await Assert.That(ContentEquality.ListEquals(nullList, nullList)).IsTrue();
            await Assert.That(ContentEquality.ListEquals(list, list)).IsTrue();
            await Assert.That(ContentEquality.ListEquals(nullList, list)).IsFalse();
            await Assert.That(ContentEquality.ListEquals(list, shorter)).IsFalse();
            await Assert.That(ContentEquality.ListEquals(list, otherList)).IsFalse();
        }

        Dictionary<string, int>? nullMap = null;
        var map = new Dictionary<string, int> { ["a"] = 1 };
        var sameMap = new Dictionary<string, int> { ["a"] = 1 };
        var otherMap = new Dictionary<string, int> { ["b"] = 1 };
        using (Assert.Multiple())
        {
            await Assert.That(ContentEquality.DictionaryEquals(nullMap, nullMap)).IsTrue();
            await Assert.That(ContentEquality.DictionaryEquals(map, sameMap)).IsTrue();
            await Assert.That(ContentEquality.DictionaryEquals(map, otherMap)).IsFalse();
        }

        var left = new Dictionary<string, JsonElement>
        {
            ["a"] = JsonDocument.Parse("1").RootElement,
        };
        var right = new Dictionary<string, JsonElement>
        {
            ["a"] = JsonDocument.Parse("1").RootElement,
        };
        var deepUnequal = new Dictionary<string, JsonElement>
        {
            ["a"] = JsonDocument.Parse("2").RootElement,
        };
        var missingKey = new Dictionary<string, JsonElement>
        {
            ["b"] = JsonDocument.Parse("1").RootElement,
        };
        using (Assert.Multiple())
        {
            await Assert.That(ContentEquality.DictionaryDeepEquals(left, right)).IsTrue();
            await Assert.That(ContentEquality.DictionaryDeepEquals(left, deepUnequal)).IsFalse();
            await Assert.That(ContentEquality.DictionaryDeepEquals(left, missingKey)).IsFalse();
            await Assert.That(ContentEquality.DictionaryDeepEquals(left, null)).IsFalse();
        }
    }

    [Test]
    public async Task Version()
    {
        var parsed = new[]
        {
            ("v3.10.0", true, 3),
            ("V3.10.0", true, 3),
            ("3.10.0-beta.1", true, 3),
            ("3.10.0+build.5", true, 3),
            ("3.10", true, 3),
            ("3.10.0-dev.2026-09-27.1", true, 3),
            ("3", false, 0),
            ("3.10.0.1", false, 0),
            ("99999999999999999999.0.0", false, 0),
            ("x.10.0", false, 0),
        };
        foreach (var (text, takes, major) in parsed)
        {
            var read = NodeVersion.TryParse(text, out var version);
            await Assert.That(read).IsEqualTo(takes);
            await Assert.That(!takes || version.Major == major).IsTrue();
        }

        NodeVersion.TryParse("3.10.0", out var plain);
        NodeVersion.TryParse("3.10.0", out var same);
        NodeVersion.TryParse("3.10.1", out var bump);
        using (Assert.Multiple())
        {
            await Assert.That(plain).IsEqualTo(same);
            await Assert.That(plain).IsNotEqualTo(bump);
            await Assert.That(plain.GetHashCode()).IsEqualTo(same.GetHashCode());
            await Assert.That(plain).IsNotEqualTo((object?)null);
            await Assert.That(plain).IsEqualTo((object)same);
        }

        NodeVersion.TryParse("3.10.0", out var older);
        NodeVersion.TryParse("3.11.0", out var newer);
        using (Assert.Multiple())
        {
            await Assert.That(older == same).IsTrue();
            await Assert.That(older != newer).IsTrue();
            await Assert.That(older < newer).IsTrue();
        }
        await Assert.That(newer > older).IsTrue();
        await Assert.That(older <= same).IsTrue();
        await Assert.That(same >= older).IsTrue();
        await Assert.That(newer >= older).IsTrue();

        await Assert.That(NodeVersion.TryParse("3.", out _)).IsFalse();
        await Assert.That(NodeVersion.TryParse("3.10.", out _)).IsFalse();
    }

    [Test]
    [Arguments("https://x?t=45s", 45_000L)]
    [Arguments("https://x?t=1m30s", 90_000L)]
    [Arguments("https://x?t=1h2m3s", 3_723_000L)]
    [Arguments("https://x?t=1:23", 83_000L)]
    [Arguments("https://x?t=1:02:03", 3_723_000L)]
    [Arguments("https://x?t=1m30", null)]
    [Arguments("https://x?t=0s", null)]
    [Arguments("https://x#t=45s", 45_000L)]
    public async Task UrlStart(string url, long? milliseconds)
    {
        await Assert.That(UrlStartPosition.ParseMilliseconds(url)).IsEqualTo(milliseconds);
    }

    [Test]
    public async Task RoutePlanner()
    {
        var options = NyraJson.SerializerOptions;
        const string full =
            """{"class":"RotatingIpRoutePlanner","details":{"ipBlock":{"type":"Inet4Address","size":256},"strategy":"RotateOnBan","rotateIndex":"2","ipIndex":"3","currentAddress":"1.2.3.4","currentAddressIndex":"5","blockIndex":"1","failingAddresses":[{"failingAddress":"/1.2.3.4","failingTimestamp":1700000000000,"failingTime":"then"},null]}}""";

        var status = JsonSerializer.Deserialize<RoutePlannerStatus>(full, options);
        var details = status?.Details;
        using (Assert.Multiple())
        {
            await Assert.That(details).IsNotNull();
            await Assert.That(details!.IpBlock).IsNotNull();
            await Assert.That(details!.IpBlock!.Type).IsEqualTo(IpBlockType.Inet4);
            await Assert.That(details!.IpBlock!.Size).IsEqualTo("256");
            await Assert.That(details!.Strategy).IsEqualTo("RotateOnBan");
            await Assert.That(details!.RotateIndex).IsEqualTo("2");
            await Assert.That(details!.CurrentAddress).IsEqualTo("1.2.3.4");
        }
        await Assert
            .That(details!.FailingAddresses is [{ Address: "/1.2.3.4", FailingTime: "then" }])
            .IsTrue();
        await Assert
            .That(details?.FailingAddresses?[0].FailingTimestamp)
            .IsEqualTo(DateTimeOffset.FromUnixTimeMilliseconds(1_700_000_000_000));

        const string quoted =
            """{"class":"NanoIpRoutePlanner","details":{"ipBlock":{"type":"Inet6Address","size":"512"},"failingAddresses":[{"failingAddress":"/::1","failingTimestamp":99999999999999999}]}}""";
        var quotedStatus = JsonSerializer.Deserialize<RoutePlannerStatus>(quoted, options);
        await Assert
            .That(quotedStatus?.Details?.IpBlock is { Type: IpBlockType.Inet6, Size: "512" })
            .IsTrue();
        await Assert.That(quotedStatus!.Details?.FailingAddresses is [{ }]).IsTrue();
        await Assert
            .That(
                quotedStatus?.Details?.FailingAddresses is [{ FailingTimestamp: var clamped }]
                    && clamped == DateTimeOffset.MinValue
            )
            .IsTrue();

        var again = JsonSerializer.Deserialize<RoutePlannerStatus>(full, options);
        using (Assert.Multiple())
        {
            await Assert.That(status).IsEqualTo(again);
            await Assert.That(status!.Details).IsEqualTo(again!.Details);
            await Assert.That(status.GetHashCode()).IsEqualTo(again.GetHashCode());
            await Assert.That(status.Details).IsNotEqualTo(quotedStatus!.Details);
            await Assert
                .That(status.Details!.GetHashCode())
                .IsEqualTo(again!.Details!.GetHashCode());
        }

        await Assert
            .That(() =>
            {
                _ = JsonSerializer.Deserialize<RoutePlannerStatus>(
                    """{"class":"RotatingIpRoutePlanner","details":{"ipBlock":{"type":"Inet4Address","size":true}}}""",
                    options
                );
            })
            .Throws<JsonException>();

        const string fractional =
            """{"class":"BalancingIpRoutePlanner","details":{"ipBlock":{"type":"Inet4Address","size":1.5}}}""";
        var fractionalStatus = JsonSerializer.Deserialize<RoutePlannerStatus>(fractional, options);
        await Assert.That(fractionalStatus?.Details?.IpBlock?.Size).IsEqualTo("1.5");
    }

    [Test]
    public async Task EqualityFamilies()
    {
        var options = NyraJson.SerializerOptions;

        var chapterA = JsonSerializer.Deserialize<Chapter>(
            """{"startTime":100,"duration":500}""",
            options
        );
        var chapterB = JsonSerializer.Deserialize<Chapter>(
            """{"startTime":100,"duration":500}""",
            options
        );
        var chapterC = JsonSerializer.Deserialize<Chapter>(
            """{"startTime":100,"duration":600}""",
            options
        );
        await Assert.That(chapterA).IsEqualTo(chapterB);
        await Assert.That(chapterA).IsNotEqualTo(chapterC);

        var groupA = JsonSerializer.Deserialize<GroupUpdateResult>(
            """{"id":"g1","guildIds":[777],"createdAt":123}""",
            options
        );
        var groupB = JsonSerializer.Deserialize<GroupUpdateResult>(
            """{"id":"g1","guildIds":["777"],"createdAt":123}""",
            options
        );
        var groupC = JsonSerializer.Deserialize<GroupUpdateResult>(
            """{"id":"g1","guildIds":[777],"createdAt":124}""",
            options
        );
        await Assert.That(groupA).IsEqualTo(groupB);
        await Assert.That(groupA).IsNotEqualTo(groupC);

        var trackA = JsonSerializer.Deserialize<Track>(
            """{"encoded":"QXRsZWFzdA==","info":{"identifier":"i","author":"A","title":"T","length":1000,"isStream":false,"isSeekable":true,"position":0}}""",
            options
        );
        var trackB = JsonSerializer.Deserialize<Track>(
            """{"encoded":"QXRsZWFzdA==","info":{"identifier":"i","author":"A","title":"T","length":1000,"isStream":false,"isSeekable":true,"position":0}}""",
            options
        );
        var trackC = JsonSerializer.Deserialize<Track>(
            """{"encoded":"QXRsZWFzdA==","info":{"identifier":"i","author":"B","title":"T","length":1000,"isStream":false,"isSeekable":true,"position":0}}""",
            options
        );
        await Assert.That(trackA).IsEqualTo(trackB);
        await Assert.That(trackA).IsNotEqualTo(trackC);

        var optionsA = new PlayOptions(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(9));
        var optionsB = new PlayOptions(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(9));
        var optionsC = new PlayOptions(TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(9));
        await Assert.That(optionsA).IsEqualTo(optionsB);
        await Assert.That(optionsA).IsNotEqualTo(optionsC);

        var groupTrackA = JsonSerializer.Deserialize<GroupTrack>(
            """{"encoded":"QXRsZWFzdA=="}""",
            options
        );
        var groupTrackB = JsonSerializer.Deserialize<GroupTrack>(
            """{"encoded":"QXRsZWFzdA=="}""",
            options
        );
        var groupTrackC = JsonSerializer.Deserialize<GroupTrack>(
            """{"encoded":"T3RoZXI="}""",
            options
        );
        await Assert.That(groupTrackA).IsEqualTo(groupTrackB);
        await Assert.That(groupTrackA).IsNotEqualTo(groupTrackC);
    }
}
