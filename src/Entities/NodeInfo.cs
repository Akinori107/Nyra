using System.Text.Json.Serialization;

namespace Nyra.Entities;

/// <summary>Descriptive information about an audio node, from <c>GET /v4/info</c>.</summary>
/// <remarks>
/// Served by both servers, with server-specific members null on the other kind.
/// <para>
/// This read is strict beyond the number-in-string tolerance the shared options carry (a stringified build
/// time still parses). One member in an unexpected shape (a stringified boolean, a fractional build time, a
/// non-string list entry) fails the whole parse, surfaced as a <c>RestException</c> that carries the
/// response's OK status.
/// </para>
/// <para>
/// The connect probe reads the same endpoint tolerantly and only needs a readable <c>version.semver</c>, so a
/// non-conforming server can be connected and playing while this read of its info fails.
/// </para>
/// </remarks>
public sealed record NodeInfo
{
    /// <summary>The version of the server.</summary>
    [JsonPropertyName("version"), JsonInclude]
    public NodeInfoVersion Version
    {
        get;
        internal set => field = value ?? new();
    } = new();

    /// <summary>Build time in milliseconds since the Unix epoch.</summary>
    [JsonPropertyName("buildTime"), JsonInclude]
    public long BuildTimeMilliseconds { get; internal set; }

    /// <summary>
    /// Build time as a date, <see cref="DateTimeOffset.MinValue"/> when out of range or when the server
    /// reports its missing-value sentinel (-1 on Lavalink dev builds and NodeLink without build info).
    /// </summary>
    [JsonIgnore]
    public DateTimeOffset BuildTime =>
        BuildTimeMilliseconds is > 0 and <= UnixTime.MaxMilliseconds
            ? DateTimeOffset.FromUnixTimeMilliseconds(BuildTimeMilliseconds)
            : DateTimeOffset.MinValue;

    /// <summary>The git revision the server was built from.</summary>
    [JsonPropertyName("git"), JsonInclude]
    public NodeInfoGit Git
    {
        get;
        internal set => field = value ?? new();
    } = new();

    /// <summary>The JVM version the server runs on.</summary>
    /// <remarks>Lavalink only.</remarks>
    [JsonPropertyName("jvm"), JsonInclude]
    public string? Jvm { get; internal set; }

    /// <summary>Version of lavaplayer, the audio engine Lavalink plays through.</summary>
    /// <remarks>Lavalink only.</remarks>
    [JsonPropertyName("lavaplayer"), JsonInclude]
    public string? Lavaplayer { get; internal set; }

    /// <summary>The Node.js version the server runs on.</summary>
    /// <remarks>NodeLink only.</remarks>
    [JsonPropertyName("node"), JsonInclude]
    public string? Node { get; internal set; }

    /// <summary>The voice library the server speaks Discord voice through.</summary>
    /// <remarks>NodeLink only.</remarks>
    [JsonPropertyName("voice"), JsonInclude]
    public NodeInfoVoice? Voice { get; internal set; }

    /// <summary>
    /// Whether the server identified itself as NodeLink through <c>isNodelink</c>, null when absent, i.e. on
    /// Lavalink.
    /// </summary>
    [JsonPropertyName("isNodelink"), JsonInclude]
    public bool? IsNodeLink { get; internal set; }

    /// <summary>Source managers the server has enabled, such as youtube or soundcloud.</summary>
    [JsonPropertyName("sourceManagers"), JsonInclude]
    public IReadOnlyList<string> SourceManagers
    {
        get;
        internal set => field = value is null ? [] : [.. value.Where(v => v is not null)];
    } = [];

    /// <summary>Audio filters the server has enabled, such as equalizer or timescale.</summary>
    [JsonPropertyName("filters"), JsonInclude]
    public IReadOnlyList<string> Filters
    {
        get;
        internal set => field = value is null ? [] : [.. value.Where(v => v is not null)];
    } = [];

    /// <summary>The plugins the server loaded.</summary>
    [JsonPropertyName("plugins"), JsonInclude]
    public IReadOnlyList<NodeInfoPlugin> Plugins
    {
        get;
        internal set => field = value is null ? [] : [.. value.Where(v => v is not null)];
    } = [];

    /// <summary>
    /// Runtime version of the server, <see cref="Jvm"/> on Lavalink, <see cref="Node"/> on NodeLink.
    /// </summary>
    /// <remarks><see cref="Lavaplayer"/> and <see cref="Voice"/> differ in shape and stay separate.</remarks>
    [JsonIgnore]
    public string? Runtime => Jvm ?? Node;

    /// <summary>
    /// Indicates whether both infos describe the same server with the same sources, filters and plugins.
    /// </summary>
    /// <param name="other">The info to compare with.</param>
    /// <returns>Whether the members match, the collections compared by content.</returns>
    public bool Equals(NodeInfo? other) =>
        other is not null
        && Version == other.Version
        && BuildTimeMilliseconds == other.BuildTimeMilliseconds
        && Git == other.Git
        && Jvm == other.Jvm
        && Lavaplayer == other.Lavaplayer
        && Node == other.Node
        && Voice == other.Voice
        && IsNodeLink == other.IsNodeLink
        && ContentEquality.ListEquals(SourceManagers, other.SourceManagers)
        && ContentEquality.ListEquals(Filters, other.Filters)
        && ContentEquality.ListEquals(Plugins, other.Plugins);

    /// <summary>Returns a hash of the scalar members, the collections stay out of it.</summary>
    /// <returns>The hash code.</returns>
    public override int GetHashCode() =>
        HashCode.Combine(
            Version,
            BuildTimeMilliseconds,
            Git,
            Jvm,
            Lavaplayer,
            Node,
            Voice,
            IsNodeLink
        );
}

/// <summary>Version of a server.</summary>
/// <remarks>
/// The semver string is always bound, enforced by the connection gate (which rejects a server reporting none)
/// and not by this model, so a version object without it would parse with a null <see cref="Semver"/> outside
/// the gate.
/// <para>
/// The numeric members carry the base version only, a Lavalink dev build reports zeros there. A NodeLink dev
/// build's pre-release tag lives in <see cref="PreReleaseIdentifiers"/>, never in the numbers, and the string
/// carries the pre-release and build metadata too.
/// </para>
/// </remarks>
public sealed record NodeInfoVersion
{
    /// <summary>The semantic version of the server.</summary>
    [JsonPropertyName("semver"), JsonInclude]
    public string? Semver { get; internal set; }

    /// <summary>Major version, a Lavalink dev build reports zero.</summary>
    [JsonPropertyName("major"), JsonInclude]
    public int? Major { get; internal set; }

    /// <summary>Minor version, a Lavalink dev build reports zero.</summary>
    [JsonPropertyName("minor"), JsonInclude]
    public int? Minor { get; internal set; }

    /// <summary>Patch version, a Lavalink dev build reports zero.</summary>
    [JsonPropertyName("patch"), JsonInclude]
    public int? Patch { get; internal set; }

    /// <summary>Pre-release identifier.</summary>
    /// <remarks>
    /// Lavalink sends an empty string on release builds, so <c>null</c> and <c>""</c> both occur and a
    /// non-null test reads true on a release build. Lavalink only. NodeLink reports the array <see
    /// cref="PreReleaseIdentifiers"/> instead.
    /// </remarks>
    [JsonPropertyName("preRelease"), JsonInclude]
    public string? PreRelease { get; internal set; }

    /// <summary>
    /// The pre-release identifiers, null entries the wire carries are dropped, a null list reads as empty.
    /// </summary>
    [JsonPropertyName("prerelease"), JsonInclude]
    public IReadOnlyList<string> PreReleaseIdentifiers
    {
        get;
        internal set => field = value is null ? [] : [.. value.Where(v => v is not null)];
    } = [];

    /// <summary>Indicates whether both versions carry the same numbers and pre-release identifiers.</summary>
    /// <param name="other">The version to compare with.</param>
    /// <returns>Whether the members match, the identifiers compared by content.</returns>
    public bool Equals(NodeInfoVersion? other) =>
        other is not null
        && Semver == other.Semver
        && Major == other.Major
        && Minor == other.Minor
        && Patch == other.Patch
        && PreRelease == other.PreRelease
        && ContentEquality.ListEquals(PreReleaseIdentifiers, other.PreReleaseIdentifiers);

    /// <summary>Returns a hash of the scalar members, the identifiers stay out of it.</summary>
    /// <returns>The hash code.</returns>
    public override int GetHashCode() => HashCode.Combine(Semver, Major, Minor, Patch, PreRelease);

    /// <summary>
    /// The version parsed from <see cref="Semver"/>, null when it does not parse, such as a dev build's commit
    /// hash.
    /// </summary>
    [JsonIgnore]
    public NodeVersion? Parsed => NodeVersion.TryParse(Semver, out var parsed) ? parsed : null;
}

/// <summary>Git revision a server was built from.</summary>
public sealed record NodeInfoGit
{
    /// <summary>The branch the server was built from.</summary>
    [JsonPropertyName("branch"), JsonInclude]
    public string? Branch { get; internal set; }

    /// <summary>The commit the server was built from.</summary>
    [JsonPropertyName("commit"), JsonInclude]
    public string? Commit { get; internal set; }

    /// <summary>Commit time in milliseconds since the Unix epoch.</summary>
    [JsonPropertyName("commitTime"), JsonInclude]
    public long CommitTimeMilliseconds { get; internal set; }

    /// <summary>
    /// Commit time as a date, <see cref="DateTimeOffset.MinValue"/> when out of range or a missing-value
    /// sentinel.
    /// </summary>
    [JsonIgnore]
    public DateTimeOffset CommitTime =>
        CommitTimeMilliseconds is > 0 and <= UnixTime.MaxMilliseconds
            ? DateTimeOffset.FromUnixTimeMilliseconds(CommitTimeMilliseconds)
            : DateTimeOffset.MinValue;
}

/// <summary>The voice library a NodeLink server speaks Discord voice through.</summary>
public sealed record NodeInfoVoice
{
    /// <summary>The name of the voice implementation.</summary>
    [JsonPropertyName("name"), JsonInclude]
    public string? Name { get; internal set; }

    /// <summary>The version of the voice implementation.</summary>
    [JsonPropertyName("version"), JsonInclude]
    public string? Version { get; internal set; }
}

/// <summary>A plugin loaded by the server.</summary>
public sealed record NodeInfoPlugin
{
    /// <summary>The name of the plugin.</summary>
    [JsonPropertyName("name"), JsonInclude]
    public string? Name { get; internal set; }

    /// <summary>The version of the plugin.</summary>
    [JsonPropertyName("version"), JsonInclude]
    public string? Version { get; internal set; }

    /// <summary>Author declared by the plugin.</summary>
    /// <remarks>NodeLink only.</remarks>
    [JsonPropertyName("author"), JsonInclude]
    public string? Author { get; internal set; }

    /// <summary>Path the plugin was installed from.</summary>
    /// <remarks>NodeLink only.</remarks>
    [JsonPropertyName("path"), JsonInclude]
    public string? Path { get; internal set; }
}
