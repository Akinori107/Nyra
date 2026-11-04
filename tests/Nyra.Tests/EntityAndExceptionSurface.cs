using System.Text.Json;
using Nyra.Abstractions;
using Nyra.Entities;
using Nyra.Exceptions;
using Nyra.Internal;

namespace Nyra.Tests;

public sealed class EntityAndExceptionSurface
{
    private static T? Throws<T>(Action action)
        where T : Exception
    {
        try
        {
            action();
        }
        catch (T exception)
        {
            return exception;
        }
        throw new InvalidOperationException($"the expected {typeof(T).Name} never came");
    }

    [Test]
    public async Task EntitySurfaces()
    {
        var data = new LyricsData { Lines = [], Message = "ok" };
        var resultA = new LyricsResult { LoadType = LyricsLoadType.Lyrics, Data = data };
        var resultB = new LyricsResult { LoadType = LyricsLoadType.Lyrics, Data = data };
        await Assert.That(resultA).IsEqualTo(resultB);
        await Assert.That(resultA.GetHashCode()).IsEqualTo(resultB.GetHashCode());
        var state = new SponsorBlockState { Enabled = true, Categories = ["music"] };
        await Assert.That(state).IsEqualTo(state with { });
        await Assert.That(state.GetHashCode()).IsEqualTo((state with { }).GetHashCode());
        var settings = new SponsorBlockSettings { Categories = ["music"] };
        await Assert.That(settings.GetHashCode()).IsEqualTo((settings with { }).GetHashCode());
        var overrideSeg = new SponsorBlockSegmentOverride([new SponsorBlockSegment { Uuid = "u" }]);
        await Assert
            .That(overrideSeg.GetHashCode())
            .IsEqualTo((overrideSeg with { }).GetHashCode());
        var group = new Group { Id = "g" };
        await Assert.That(group.CreatedAtTime).IsEqualTo(group.CreatedAtTime);
        var groupErr = new GroupGuildError { GuildId = 777 };
        await Assert.That(groupErr.GetHashCode()).IsEqualTo((groupErr with { }).GetHashCode());
        var yt = new YouTubeTokenRefresh("super-secret");
        await Assert.That(yt.ToString()).DoesNotContain("super-secret");
    }

    [Test]
    public async Task Endpoint()
    {
        var ipv4 = new Endpoint("127.0.0.1", 3000, "pass");
        await Assert.That(ipv4.Host).IsEqualTo("127.0.0.1");
        var bracketedShort = new Endpoint("[::1]", 3000, "pass");
        await Assert.That(bracketedShort.Host).IsEqualTo("[::1]");
        var bracketedFull = new Endpoint("[0000:0000:0000:0000:0000:0000:0000:0001]", 3000, "pass");
        await Assert
            .That(bracketedFull.Host)
            .IsEqualTo("[0000:0000:0000:0000:0000:0000:0000:0001]");
        var host = new Endpoint("example.com", 3000, "pass");
        await Assert.That(host.Host).IsEqualTo("example.com");

        var spacedHost = Throws<ArgumentException>(() => new Endpoint("a b", 1, "p"));
        var urlHost = Throws<ArgumentException>(() => new Endpoint("http://x", 1, "p"));
        var colonHost = Throws<ArgumentException>(() => new Endpoint("host:1", 1, "p"));
        using (Assert.Multiple())
        {
            await Assert
                .That(new Endpoint("192.168.001.1", 1, "p").Host)
                .IsEqualTo("192.168.001.1");
            await Assert.That(new Endpoint("0.0.0.0", 1, "p").Host).IsEqualTo("0.0.0.0");
            await Assert.That(new Endpoint("[::]", 1, "p").Host).IsEqualTo("[::]");
            await Assert.That(new Endpoint("[not-an-ip]", 1, "p").Host).IsEqualTo("[not-an-ip]");
            await Assert
                .That(new Endpoint("[fe80::1%zone]", 1, "p").Host)
                .IsEqualTo("[fe80::1%zone]");
            await Assert.That(new Endpoint("[[::1]]", 1, "p").Host).IsEqualTo("[[::1]]");
        }
        var lowPort = Throws<ArgumentOutOfRangeException>(() => new Endpoint("127.0.0.1", 0, "p"));
        await Assert.That(lowPort).IsNotNull();
        var highPort = Throws<ArgumentOutOfRangeException>(() =>
            new Endpoint("127.0.0.1", 65536, "p")
        );
        await Assert.That(highPort).IsNotNull();
        await Assert.That(new Endpoint("127.0.0.1", 1, "ção").Password).IsEqualTo("ção");
        var blankHost = Throws<ArgumentException>(() => new Endpoint("  ", 1, "p"));
        await Assert.That(blankHost).IsNotNull();
        var blankPassword = Throws<ArgumentException>(() => new Endpoint("127.0.0.1", 1, " "));
        await Assert.That(blankPassword).IsNotNull();
    }

    private static readonly WireEnumConverter<LoadTypeWire, Nyra.Entities.LoadType> LoadTypeConv =
        new();

    private static readonly WireEnumConverter<
        LyricsLoadTypeWire,
        Nyra.Entities.LyricsLoadType
    > LyricsLoadTypeConv = new();

    [Test]
    public async Task WireEnumFallbacks()
    {
        var rawReader = new Utf8JsonReader(System.Text.Encoding.UTF8.GetBytes("{}"));
        rawReader.Read();
        var objectToken = LoadTypeConv.Read(
            ref rawReader,
            typeof(Nyra.Entities.LoadType),
            new JsonSerializerOptions()
        );
        await Assert.That(objectToken).IsEqualTo(Nyra.Entities.LoadType.Unknown);
        var unknownReader = new Utf8JsonReader(System.Text.Encoding.UTF8.GetBytes("\"bogus\""));
        unknownReader.Read();
        var unknownString = LoadTypeConv.Read(
            ref unknownReader,
            typeof(Nyra.Entities.LoadType),
            new JsonSerializerOptions()
        );
        await Assert.That(unknownString).IsEqualTo(Nyra.Entities.LoadType.Unknown);
        var unknownSearchReader = new Utf8JsonReader(
            System.Text.Encoding.UTF8.GetBytes("\"bogus\"")
        );
        unknownSearchReader.Read();
        var unknownSearch = LyricsLoadTypeConv.Read(
            ref unknownSearchReader,
            typeof(Nyra.Entities.LyricsLoadType),
            new JsonSerializerOptions()
        );
        await Assert.That(unknownSearch).IsEqualTo(Nyra.Entities.LyricsLoadType.Unknown);
    }

    [Test]
    public async Task ClientInformation()
    {
        var ok = new ClientInformation(1);
        await Assert.That(ok.CurrentUserId).IsEqualTo(1);
    }

    [Test]
    [Arguments("[::1")]
    [Arguments("::1]")]
    [Arguments("[::1]x")]
    [Arguments("x[::1]")]
    public async Task HalfBracketedHostsReject(string host)
    {
        await Assert
            .That(() =>
            {
                _ = new Endpoint(host, 3000, "pass");
            })
            .Throws<ArgumentException>();
    }

    private static T? LavaParse<T>(string json)
        where T : class => JsonSerializer.Deserialize<T>(json, NyraJson.OptionsFor(false));

    private sealed class ProbeException(string message, Exception? inner)
        : NyraException(message, inner);

    [Test]
    public async Task JsonAndLoad()
    {
        var forwardNodelink = NyraJson.OptionsFor(true);
        await Assert.That(forwardNodelink).IsNotNull();
        var forwardLavalink = NyraJson.OptionsFor(false);
        await Assert.That(forwardLavalink).IsNotNull();
        var forwardSetLava = NyraJson.OptionsFor(new HashSet<string> { "volume" });
        await Assert.That(forwardSetLava).IsNotNull();

        var probeInner = new ProbeException("plain", null);
        await Assert.That(probeInner.Message).IsEqualTo("plain");
        var probeWithInner = new ProbeException("wrapped", new InvalidOperationException("cause"));
        await Assert.That(probeWithInner.InnerException is InvalidOperationException).IsTrue();

        var nonObject = Throws<JsonException>(() => LavaParse<LoadResult>("5"));

        var errorFallback = JsonSerializer.Deserialize<LoadResult>(
            """{"loadType":"error","data":{"message":"nope"}}""",
            NyraJson.OptionsFor(true)
        );
        await Assert
            .That(
                errorFallback is LoadResult.LoadFailed fallback && fallback.Error.Message == "nope"
            )
            .IsTrue();
        var errorRefuses = Throws<JsonException>(() =>
            JsonSerializer.Deserialize<LoadResult>(
                """{"loadType":"error","data":5}""",
                NyraJson.OptionsFor(true)
            )
        );
        var errorLava = JsonSerializer.Deserialize<LoadResult>(
            """{"loadType":"error","data":{"message":"lava"}}""",
            NyraJson.OptionsFor(false)
        );
        await Assert
            .That(errorLava is LoadResult.LoadFailed lava && lava.Error.Message == "lava")
            .IsTrue();

        const string trackJson =
            """{"encoded":"QXRsZWFzdA==","info":{"identifier":"ph8qt3dhvwo","author":"SoAD - Topic","title":"ATWA","length":176000,"isStream":false,"isSeekable":true,"position":0}}""";
        var nullTrack = Throws<JsonException>(() =>
            LavaParse<LoadResult>(
                $$$"""{"loadType":"playlist","data":{"info":{},"tracks":[{{{trackJson}}},null]}}"""
            )
        );

        var unknownData = LavaParse<LoadResult>(
            $$$"""{"loadType":"playlist","data":{"extra":1,"info":{"name":123},"tracks":[{{{trackJson}}}]}}"""
        );
        await Assert.That(unknownData).IsNotNull();

        var errorLoad = LavaParse<LoadResult>(
            """{"loadType":"error","data":{"message":"x","severity":"fault","cause":"y","exception":{"message":"boom","severity":"fault","cause":"upstream"}}}"""
        );
        await Assert
            .That(
                errorLoad is LoadResult.LoadFailed { LoadType: var lt }
                    && lt == Nyra.Entities.LoadType.Error
            )
            .IsTrue();

        var serialized = Throws<JsonException>(() =>
            System.Text.Json.JsonSerializer.Serialize(errorLoad, NyraJson.OptionsFor(false))
        );
    }
}
