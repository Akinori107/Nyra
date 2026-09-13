using System.Text.Json.Serialization;

namespace Nyra.Entities;

/// <summary>
/// Descriptive information about an audio node, fetched through <c>GET /v4/info</c>: its
/// version, build, runtime and the sources, filters and plugins it has enabled. Served by
/// both servers, with server-specific members null on the other kind.
/// </summary>
public sealed record NodeInfo
{
    /// <summary>Version of the server.</summary>
    [JsonPropertyName("version")]
    public NodeInfoVersion Version { get; init; } = new();

    /// <summary>Build time in milliseconds since the Unix epoch.</summary>
    [JsonPropertyName("buildTime")]
    public long BuildTimeMilliseconds { get; init; }

    /// <summary>Build time as a date, <see cref="DateTimeOffset.MinValue"/> when out of range.</summary>
    [JsonIgnore]
    public DateTimeOffset BuildTime =>
        BuildTimeMilliseconds is >= UnixTime.MinMilliseconds and <= UnixTime.MaxMilliseconds
            ? DateTimeOffset.FromUnixTimeMilliseconds(BuildTimeMilliseconds)
            : DateTimeOffset.MinValue;

    /// <summary>Git revision the server was built from.</summary>
    [JsonPropertyName("git")]
    public NodeInfoGit Git { get; init; } = new();

    /// <summary>The JVM version the server runs on. Lavalink only.</summary>
    [JsonPropertyName("jvm")]
    public string? Jvm { get; init; }

    /// <summary>Version of lavaplayer, the audio engine Lavalink plays through. Lavalink only.</summary>
    [JsonPropertyName("lavaplayer")]
    public string? Lavaplayer { get; init; }

    /// <summary>The Node.js version the server runs on. NodeLink only.</summary>
    [JsonPropertyName("node")]
    public string? Node { get; init; }

    /// <summary>The voice library the server speaks Discord voice through. NodeLink only.</summary>
    [JsonPropertyName("voice")]
    public NodeInfoVoice? Voice { get; init; }

    /// <summary>Source managers the server has enabled, such as youtube or soundcloud.</summary>
    [JsonPropertyName("sourceManagers")]
    public IReadOnlyList<string> SourceManagers { get; init; } = [];

    /// <summary>Audio filters the server has enabled, such as equalizer or timescale.</summary>
    [JsonPropertyName("filters")]
    public IReadOnlyList<string> Filters { get; init; } = [];

    /// <summary>Plugins the server has loaded.</summary>
    [JsonPropertyName("plugins")]
    public IReadOnlyList<NodeInfoPlugin> Plugins { get; init; } = [];

    /// <summary>
    /// Runtime version of the server, <see cref="Jvm"/> on Lavalink and <see cref="Node"/> on
    /// NodeLink. Only these two collapse into one member, they are the same concept in the same
    /// shape, while <see cref="Lavaplayer"/> (the audio engine, a string) and <see cref="Voice"/>
    /// (the voice transport, an object) are neither and stay separate.
    /// </summary>
    [JsonIgnore]
    public string? Runtime => Jvm ?? Node;
}

/// <summary>
/// Version of a server. Only the semver string is bound: the pre-release and build members
/// differ in shape between the servers, and the string carries them anyway.
/// </summary>
public sealed record NodeInfoVersion
{
    /// <summary>The full version string of the server, such as 4.2.0 or 3.9.0.</summary>
    [JsonPropertyName("semver")]
    public string? Semver { get; init; }

    /// <summary>
    /// The version parsed from <see cref="Semver"/>, null when it does not parse, such as the
    /// commit hash a dev build reports.
    /// </summary>
    [JsonIgnore]
    public NodeVersion? Parsed => NodeVersion.TryParse(Semver, out var parsed) ? parsed : null;
}

/// <summary>Git revision a server was built from.</summary>
public sealed record NodeInfoGit
{
    [JsonPropertyName("branch")]
    public string? Branch { get; init; }

    [JsonPropertyName("commit")]
    public string? Commit { get; init; }

    /// <summary>Commit time in milliseconds since the Unix epoch.</summary>
    [JsonPropertyName("commitTime")]
    public long CommitTimeMilliseconds { get; init; }

    /// <summary>Commit time as a date, <see cref="DateTimeOffset.MinValue"/> when out of range.</summary>
    [JsonIgnore]
    public DateTimeOffset CommitTime =>
        CommitTimeMilliseconds is >= UnixTime.MinMilliseconds and <= UnixTime.MaxMilliseconds
            ? DateTimeOffset.FromUnixTimeMilliseconds(CommitTimeMilliseconds)
            : DateTimeOffset.MinValue;
}

/// <summary>The voice library a NodeLink server speaks Discord voice through.</summary>
public sealed record NodeInfoVoice
{
    [JsonPropertyName("name")]
    public string? Name { get; init; }

    [JsonPropertyName("version")]
    public string? Version { get; init; }
}

/// <summary>A plugin loaded by the server.</summary>
public sealed record NodeInfoPlugin
{
    [JsonPropertyName("name")]
    public string? Name { get; init; }

    [JsonPropertyName("version")]
    public string? Version { get; init; }

    /// <summary>Author declared by the plugin. NodeLink only.</summary>
    [JsonPropertyName("author")]
    public string? Author { get; init; }

    /// <summary>Path the plugin was installed from. NodeLink only.</summary>
    [JsonPropertyName("path")]
    public string? Path { get; init; }
}
