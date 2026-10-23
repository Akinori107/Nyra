using System.Text.Json;

namespace Nyra.Entities;

/// <summary>
/// Options of a playback request issued through <see cref="GuildConnection.PlayAsync"/> or <see
/// cref="GuildConnection.PlayIdentifierAsync"/>.
/// </summary>
/// <param name="StartPosition">
/// Starts playback at the given position, null falls back to the track's own start offset (a timestamped link)
/// or the beginning when it has none. Must not be negative and must come before the end time, every play entry
/// point validates and throws up front.
/// </param>
/// <param name="EndTime">
/// Stops playback at the given position, null plays to the end. Must be greater than zero and after the start
/// position, every play entry point validates and throws up front.
/// </param>
/// <param name="NoReplace">When true, does not replace a track that is already playing.</param>
/// <param name="UserData">
/// Client data the server attaches to the track and echoes back. Must be a JSON object when set. Lavalink
/// rejects any other kind, so the play methods throw <see cref="ArgumentException"/> up front.
/// </param>
/// <param name="AudioTrackId">
/// Selects a dub or alternate audio track on sources that expose more than one. NodeLink only, not with <see
/// cref="GuildConnection.PlayIdentifierAsync"/>, which is Lavalink only.
/// </param>
public sealed record PlayOptions(
    TimeSpan? StartPosition = null,
    TimeSpan? EndTime = null,
    bool NoReplace = false,
    JsonElement? UserData = null,
    string? AudioTrackId = null
)
{
    /// <summary>Indicates whether both options select the same playback with the same raw client data.</summary>
    /// <param name="other">The options to compare with.</param>
    /// <returns>Whether the members match, the raw data compared by content.</returns>
    public bool Equals(PlayOptions? other) =>
        other is not null
        && StartPosition == other.StartPosition
        && EndTime == other.EndTime
        && NoReplace == other.NoReplace
        && AudioTrackId == other.AudioTrackId
        && ContentEquality.JsonElementEquals(UserData, other.UserData);

    /// <summary>Returns a hash of the comparable members, the raw data stays out of it.</summary>
    /// <returns>The hash code.</returns>
    public override int GetHashCode() =>
        HashCode.Combine(StartPosition, EndTime, NoReplace, AudioTrackId);
}
