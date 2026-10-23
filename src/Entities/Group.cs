using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Nyra.Entities;

/// <summary>A group of players under one id, from NodeLink's group routes.</summary>
/// <remarks>
/// A group only records its membership and drives the members' players, every operation fans out to each
/// guild's player through the regular per-player paths, so the effects arrive as the ordinary per-guild events
/// (player created, track started, and so on), there are no group-specific events. Deleting a group does not
/// touch the players, they keep playing on their own. NodeLink only.
/// </remarks>
public sealed record Group
{
    /// <summary>The caller-chosen identifier of the group.</summary>
    /// <remarks>
    /// Only ids of letters, digits, underscores and dashes are addressable in the group URL, the server
    /// accepts but leaves anything else unreachable. Absorbs a null id the server sends.
    /// </remarks>
    [JsonPropertyName("id"), JsonInclude]
    public string Id
    {
        get;
        internal set => field = value ?? string.Empty;
    } = string.Empty;

    /// <summary>The guilds of the group.</summary>
    /// <remarks>
    /// A guild may belong to any number of groups at once, membership is not exclusive. The server expects
    /// real Discord snowflake ids, a non-snowflake value is rejected.
    /// </remarks>
    [JsonPropertyName("guildIds"), JsonInclude]
    [JsonConverter(typeof(SnowflakeListConverter))]
    public IReadOnlyList<ulong> GuildIds { get; internal set; } = [];

    /// <summary>When the group was created, Unix epoch milliseconds.</summary>
    [JsonPropertyName("createdAt"), JsonInclude]
    public long CreatedAt { get; internal set; }

    /// <summary>When the group was created, as a date.</summary>
    /// <remarks>
    /// <see cref="DateTimeOffset.MinValue"/> when the epoch value is out of the convertible range, which the
    /// exact floor of the range reads as too, so the sentinel cannot be told apart from it.
    /// </remarks>
    [JsonIgnore]
    public DateTimeOffset CreatedAtTime => UnixTime.ToDateTimeOffset(CreatedAt);

    /// <summary>Indicates whether both groups carry the same id, creation time and membership.</summary>
    /// <param name="other">The group to compare with.</param>
    /// <returns>Whether the members match, the membership compared by content.</returns>
    public bool Equals(Group? other) =>
        other is not null
        && Id == other.Id
        && CreatedAt == other.CreatedAt
        && ContentEquality.ListEquals(GuildIds, other.GuildIds);

    /// <summary>Returns a hash of the group's comparable members.</summary>
    /// <returns>The hash code.</returns>
    public override int GetHashCode() => HashCode.Combine(Id, CreatedAt);
}

/// <summary>The result of a group update, the players it reached and the per-guild failures.</summary>
public sealed record GroupUpdateResult
{
    /// <summary>The id of the updated group.</summary>
    [JsonPropertyName("id"), JsonInclude]
    public string Id
    {
        get;
        internal set => field = value ?? string.Empty;
    } = string.Empty;

    /// <summary>The membership after the update's additions and removals were applied.</summary>
    [JsonPropertyName("guildIds"), JsonInclude]
    [JsonConverter(typeof(SnowflakeListConverter))]
    public IReadOnlyList<ulong> GuildIds { get; internal set; } = [];

    /// <summary>When the group was created, Unix epoch milliseconds.</summary>
    [JsonPropertyName("createdAt"), JsonInclude]
    public long CreatedAt { get; internal set; }

    /// <summary>When the group was created, as a date.</summary>
    /// <remarks>
    /// <see cref="DateTimeOffset.MinValue"/> when the epoch value is out of the convertible range, which the
    /// exact floor of the range reads as too, so the sentinel cannot be told apart from it.
    /// </remarks>
    [JsonIgnore]
    public DateTimeOffset CreatedAtTime => UnixTime.ToDateTimeOffset(CreatedAt);

    /// <summary>The players of the member guilds the update reached.</summary>
    /// <remarks>
    /// The reached players, a member that failed mid-update is in errors instead, membership itself is <see
    /// cref="Group.GuildIds"/>. Null entries the wire carries are dropped, a null list reads as empty.
    /// </remarks>
    [JsonPropertyName("players"), JsonInclude]
    public IReadOnlyList<Player> Players
    {
        get;
        internal set => field = value is null ? [] : [.. value.Where(v => v is not null)];
    } = [];

    /// <summary>Per-guild failures of the update.</summary>
    /// <remarks>
    /// The group update answers 200 even when individual guilds fail, each failure lands here with the
    /// server's error text, always check this list, an empty one means every member was reached. Null entries
    /// the wire carries are dropped, a null list reads as empty.
    /// </remarks>
    [JsonPropertyName("errors"), JsonInclude]
    public IReadOnlyList<GroupGuildError> Errors
    {
        get;
        internal set => field = value is null ? [] : [.. value.Where(v => v is not null)];
    } = [];

    /// <summary>Indicates whether both results carry the same id, membership, players and failures.</summary>
    /// <param name="other">The result to compare with.</param>
    /// <returns>Whether the members match, the collections compared by content.</returns>
    public bool Equals(GroupUpdateResult? other) =>
        other is not null
        && Id == other.Id
        && CreatedAt == other.CreatedAt
        && ContentEquality.ListEquals(GuildIds, other.GuildIds)
        && ContentEquality.ListEquals(Players, other.Players)
        && ContentEquality.ListEquals(Errors, other.Errors);

    /// <summary>Returns a hash of the result's comparable members.</summary>
    /// <returns>The hash code.</returns>
    public override int GetHashCode() => HashCode.Combine(Id, CreatedAt);
}

/// <summary>One guild's failure inside a <see cref="GroupUpdateResult"/>, the update continued past it.</summary>
public sealed record GroupGuildError
{
    /// <summary>The guild the update failed on.</summary>
    [JsonPropertyName("guildId"), JsonInclude]
    [JsonConverter(typeof(SnowflakeConverter))]
    public ulong GuildId { get; internal set; }

    /// <summary>The server's error text, such as <c>Player not found locally</c>.</summary>
    [JsonPropertyName("error"), JsonInclude]
    public string Error
    {
        get;
        internal set => field = value ?? string.Empty;
    } = string.Empty;
}

/// <summary>What a group update changes.</summary>
/// <remarks>
/// Every member is optional, the update applies only what is set, membership first, then the track and the
/// player state on every member guild, one guild at a time. A track always replaces what the
/// members play, there is no no-replace for groups. NodeLink only.
/// </remarks>
public sealed record GroupUpdate
{
    /// <summary>Membership changes applied before the update, additions first.</summary>
    [JsonPropertyName("players")]
    public GroupMembership? Players { get; init; }

    /// <summary>The track every member plays.</summary>
    /// <remarks>A null leaves the members' tracks alone, use <see cref="GroupTrack.Stop"/> to stop them.</remarks>
    [JsonPropertyName("track")]
    [JsonConverter(typeof(GroupTrackConverter))]
    public GroupTrack? Track { get; init; }

    /// <summary>
    /// Where the played track starts, in milliseconds, and, when the update carries no track, the position
    /// every member seeks to.
    /// </summary>
    /// <remarks>
    /// The server skips the seek for members with no track loaded (a freshly created member never seeks),
    /// members with a track, playing or paused, seek to it.
    /// </remarks>
    [JsonPropertyName("position")]
    [JsonConverter(typeof(RoundedInt64Converter))]
    public long? PositionMilliseconds { get; init; }

    /// <summary>Where the played track stops, in milliseconds.</summary>
    /// <remarks>
    /// Travels with a play when the update carries a track, otherwise the server seeks every member with a
    /// track loaded to their current position with this as the marker, members without a track only get the
    /// marker.
    /// </remarks>
    [JsonPropertyName("endTime")]
    [JsonConverter(typeof(RoundedInt64Converter))]
    public long? EndTimeMilliseconds { get; init; }

    /// <summary>The playback volume of every member, 0-1000, 100 is the neutral value.</summary>
    [JsonPropertyName("volume")]
    [JsonConverter(typeof(RoundedInt32Converter))]
    public int? Volume { get; init; }

    /// <summary>Whether every member pauses.</summary>
    /// <remarks>A null leaves each member's pause state alone.</remarks>
    [JsonPropertyName("paused")]
    public bool? Paused { get; init; }

    /// <summary>
    /// The filters applied to every member, same bounds as a per-player update, the kind gates differ, a
    /// volume member is rejected here.
    /// </summary>
    /// <remarks>Null leaves the members' filters alone.</remarks>
    [JsonPropertyName("filters")]
    public FilterChain? Filters { get; init; }

    /// <summary>The fade chain applied to every member, a sent chain replaces the whole configuration.</summary>
    [JsonPropertyName("fading")]
    public FadingOptions? Fading { get; init; }

    /// <summary>Whether every member runs the loudness normalizer.</summary>
    [JsonPropertyName("loudnessNormalizer")]
    public bool? LoudnessNormalizer { get; init; }

    /// <summary>Whether every member runs speaking-detection ducking.</summary>
    /// <remarks>
    /// NodeLink 3.9.0 answers this member with a 500, so the whole group update fails against that build. The
    /// stored configuration survives the failed write.
    /// </remarks>
    [JsonPropertyName("ducking")]
    public bool? Ducking { get; init; }
}

/// <summary>Membership changes of a <see cref="GroupUpdate"/>.</summary>
public sealed record GroupMembership
{
    /// <summary>The guilds added to the group.</summary>
    /// <remarks>
    /// Adding a guild neither creates nor connects its player, the group update that follows does. The server
    /// expects real Discord snowflake ids, a non-snowflake value is rejected.
    /// </remarks>
    [JsonPropertyName("add")]
    [JsonConverter(typeof(SnowflakeListConverter))]
    public IReadOnlyList<ulong>? Add { get; init; }

    /// <summary>The guilds removed from the group.</summary>
    /// <remarks>
    /// Removal only drops the membership, the guild's player keeps playing whatever it played. The server
    /// accepts any string here, only the add path validates snowflakes, a non-member id is a silent
    /// no-op.
    /// </remarks>
    [JsonPropertyName("remove")]
    [JsonConverter(typeof(SnowflakeListConverter))]
    public IReadOnlyList<ulong>? Remove { get; init; }

    /// <summary>Indicates whether both memberships add and remove the same guilds.</summary>
    /// <param name="other">The membership to compare with.</param>
    /// <returns>Whether the lists match by content.</returns>
    public bool Equals(GroupMembership? other) =>
        other is not null
        && ContentEquality.ListEquals(Add, other.Add)
        && ContentEquality.ListEquals(Remove, other.Remove);

    /// <summary>Returns a hash of the membership's comparable members.</summary>
    /// <returns>The hash code.</returns>
    public override int GetHashCode() => HashCode.Combine(Add?.Count ?? 0, Remove?.Count ?? 0);
}

/// <summary>The track a <see cref="GroupUpdate"/> makes every member play.</summary>
/// <remarks>
/// Exactly one of <see cref="Encoded"/> or <see cref="Identifier"/> must be set, or neither, the all-null form
/// is the stop, it stops every member, the same <see cref="Stop"/> instance.
/// </remarks>
public sealed record GroupTrack
{
    /// <summary>The base64 encoded track, from <see cref="Track.Encoded"/>.</summary>
    /// <remarks>Mutually exclusive with <see cref="Identifier"/>.</remarks>
    [JsonPropertyName("encoded")]
    public string? Encoded { get; init; }

    /// <summary>The load identifier, such as a search.</summary>
    /// <remarks>
    /// Mutually exclusive with <see cref="Encoded"/>, prefer <see cref="Encoded"/>. NodeLink, the only server
    /// with groups today, never resolves identifiers in its loader, so Nyra rejects this up front with <see
    /// cref="NotSupportedException"/>. The per-player path gates identifiers as Lavalink-only for the same
    /// reason.
    /// </remarks>
    [JsonPropertyName("identifier")]
    public string? Identifier { get; init; }

    /// <summary>Client data the server attaches to the track and echoes back.</summary>
    /// <remarks>Must be a JSON object when set.</remarks>
    [JsonPropertyName("userData")]
    public JsonElement? UserData { get; init; }

    /// <summary>Selects a dub or alternate audio track on sources that expose more than one.</summary>
    [JsonPropertyName("audioTrackId")]
    public string? AudioTrackId { get; init; }

    /// <summary>
    /// Selects the audio language on sources that expose more than one, takes precedence over <see
    /// cref="AudioTrackId"/>.
    /// </summary>
    [JsonPropertyName("language")]
    public string? Language { get; init; }

    /// <summary>Indicates whether both tracks select the same media with the same raw client data.</summary>
    /// <param name="other">The track to compare with.</param>
    /// <returns>Whether the members match, the raw client data compared by content.</returns>
    public bool Equals(GroupTrack? other) =>
        other is not null
        && Encoded == other.Encoded
        && Identifier == other.Identifier
        && AudioTrackId == other.AudioTrackId
        && Language == other.Language
        && ContentEquality.JsonElementEquals(UserData, other.UserData);

    /// <summary>Returns a hash of the track's comparable members.</summary>
    /// <returns>The hash code.</returns>
    public override int GetHashCode() =>
        HashCode.Combine(Encoded, Identifier, AudioTrackId, Language);

    /// <summary>The stop form, no track, every member stops.</summary>
    /// <remarks>Carries no other member.</remarks>
    [JsonIgnore]
    public static GroupTrack Stop { get; } = new();
}

internal sealed class GroupTrackConverter : JsonConverter<GroupTrack>
{
    public override GroupTrack? Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options
    )
    {
        if (reader.TokenType != JsonTokenType.StartObject)
        {
            reader.Skip();
            return null;
        }

        string? encoded = null;
        string? identifier = null;
        JsonElement? userData = null;
        string? audioTrackId = null;
        string? language = null;

        while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
        {
            if (reader.TokenType != JsonTokenType.PropertyName)
                continue;

            if (reader.ValueTextEquals("encoded"u8))
                encoded = ReadString(ref reader);
            else if (reader.ValueTextEquals("identifier"u8))
                identifier = ReadString(ref reader);
            else if (reader.ValueTextEquals("userData"u8))
            {
                reader.Read();
                userData = JsonElement.ParseValue(ref reader);
            }
            else if (reader.ValueTextEquals("audioTrackId"u8))
                audioTrackId = ReadString(ref reader);
            else if (reader.ValueTextEquals("language"u8))
                language = ReadString(ref reader);
            else
                reader.Skip();
        }

        return new GroupTrack
        {
            Encoded = encoded,
            Identifier = identifier,
            UserData = userData,
            AudioTrackId = audioTrackId,
            Language = language,
        };
    }

    private static string? ReadString(ref Utf8JsonReader reader)
    {
        reader.Read();
        if (reader.TokenType is JsonTokenType.String)
            return reader.GetString();

        reader.Skip();
        return null;
    }

    public override void Write(
        Utf8JsonWriter writer,
        GroupTrack value,
        JsonSerializerOptions options
    )
    {
        if (value.Identifier is not null && value.Encoded is not null)
            throw new ArgumentException(
                "The identifier and encoded members are mutually exclusive, both are set.",
                nameof(value)
            );

        writer.WriteStartObject();

        if (value.Identifier is { } identifier)
            writer.WriteString("identifier", identifier);
        else
        {
            writer.WritePropertyName("encoded");
            if (value.Encoded is { } encoded)
                writer.WriteStringValue(encoded);
            else
                writer.WriteNullValue();
        }

        if (value.UserData is { } userData)
        {
            writer.WritePropertyName("userData");
            userData.WriteTo(writer);
        }

        if (value.AudioTrackId is { } audioTrackId)
            writer.WriteString("audioTrackId", audioTrackId);

        if (value.Language is { } language)
            writer.WriteString("language", language);

        writer.WriteEndObject();
    }
}

internal sealed class SnowflakeListConverter : JsonConverter<IReadOnlyList<ulong>>
{
    public override bool HandleNull => true;

    public override IReadOnlyList<ulong> Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options
    )
    {
        if (reader.TokenType != JsonTokenType.StartArray)
        {
            reader.Skip();
            return [];
        }

        var ids = new List<ulong>();
        while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
            if (
                reader.TokenType is JsonTokenType.String
                && JsonNumberParsing.TryParseUtf8UInt64(ref reader, out var id)
            )
                ids.Add(id);
            else if (
                reader.TokenType is JsonTokenType.Number
                && reader.TryGetUInt64(out var number)
            )
                ids.Add(number);
            else
                reader.Skip();

        return ids;
    }

    public override void Write(
        Utf8JsonWriter writer,
        IReadOnlyList<ulong> value,
        JsonSerializerOptions options
    )
    {
        writer.WriteStartArray();
        Span<char> buffer = stackalloc char[20];
        foreach (var id in value)
        {
            id.TryFormat(buffer, out var written, provider: CultureInfo.InvariantCulture);
            writer.WriteStringValue(buffer[..written]);
        }
        writer.WriteEndArray();
    }
}

internal sealed record GroupCreatePayload(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("guildIds")]
    [property: JsonConverter(typeof(SnowflakeListConverter))]
        IReadOnlyList<ulong>? GuildIds = null
);
