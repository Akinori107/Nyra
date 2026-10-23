using System.Globalization;

namespace Nyra;

/// <summary>A node server version (major.minor.patch), parsed from the string reported by the server.</summary>
public readonly struct NodeVersion(int major, int minor, int patch)
    : IEquatable<NodeVersion>,
        IComparable<NodeVersion>
{
    /// <summary>Oldest NodeLink version supported.</summary>
    public static NodeVersion MinimumNodeLink { get; } = new(3, 9, 0);

    /// <summary>Oldest Lavalink version supported.</summary>
    public static NodeVersion MinimumLavalink { get; } = new(4, 2, 0);

    /// <summary>The major version number.</summary>
    public int Major { get; } = major;

    /// <summary>The minor version number.</summary>
    public int Minor { get; } = minor;

    /// <summary>The patch version number.</summary>
    public int Patch { get; } = patch;

    /// <summary>
    /// Parses <c>major.minor.patch</c> out of the text, leniently.
    /// </summary>
    /// <remarks>
    /// Missing trailing dot-and-component pairs default to zero (<c>1.2</c> reads as 1.2.0), a dot with no
    /// digits after it is not a version, a trailing pre-release/build tag (<c>-</c>/<c>+</c>) is ignored, and a
    /// leading <c>v</c> or <c>V</c> (git describe style) is accepted.
    /// <para>
    /// A bare number is not a version, no supported server reports one without a dot, and an all-digit dev
    /// commit hash must stay unparsed so the dev-build policy keeps its null, a string that is not a version
    /// is treated as a dev build.
    /// </para>
    /// </remarks>
    /// <param name="text">The version text to parse.</param>
    /// <param name="version">The parsed version, default when the text is not a version.</param>
    /// <returns>Whether the text parsed into a version.</returns>
    public static bool TryParse(ReadOnlySpan<char> text, out NodeVersion version)
    {
        version = default;
        text = text.Trim();
        if (text.Length > 0 && (text[0] == 'v' || text[0] == 'V'))
            text = text[1..];
        if (text.Length == 0 || !char.IsAsciiDigit(text[0]))
            return false;
        var index = 0;
        if (!TryReadNumber(text, ref index, out var major))
            return false;
        var minor = 0;
        var patch = 0;
        var sawDot = false;
        if (ConsumeDot(text, ref index))
        {
            sawDot = true;
            if (!TryReadNumber(text, ref index, out minor))
                return false;
        }
        if (ConsumeDot(text, ref index))
        {
            sawDot = true;
            if (!TryReadNumber(text, ref index, out patch))
                return false;
        }
        if (!sawDot)
            return false;

        if (index < text.Length && text[index] is not ('-' or '+'))
            return false;
        version = new NodeVersion(major, minor, patch);
        return true;
    }

    /// <summary>Parses a version from a string, the span overload does the work.</summary>
    /// <param name="text">The version text to parse.</param>
    /// <param name="version">The parsed version, default when the text is not a version.</param>
    /// <returns>Whether the text parsed into a version.</returns>
    public static bool TryParse(string? text, out NodeVersion version) =>
        TryParse(text.AsSpan(), out version);

    /// <summary>Indicates whether both versions carry the same numbers.</summary>
    /// <param name="other">The version to compare with.</param>
    /// <returns>Whether the numbers match.</returns>
    public bool Equals(NodeVersion other) =>
        Major == other.Major && Minor == other.Minor && Patch == other.Patch;

    /// <summary>Compares this version with another, oldest first.</summary>
    /// <param name="other">The version to compare with.</param>
    /// <returns>Zero when equal, negative when this one is older, positive when newer.</returns>
    public int CompareTo(NodeVersion other) =>
        Major != other.Major ? Major.CompareTo(other.Major)
        : Minor != other.Minor ? Minor.CompareTo(other.Minor)
        : Patch.CompareTo(other.Patch);

    /// <summary>Indicates whether the object is a version with the same numbers.</summary>
    /// <param name="obj">The object to compare with.</param>
    /// <returns>Whether the object is an equal version.</returns>
    public override bool Equals(object? obj) => obj is NodeVersion other && Equals(other);

    /// <summary>Returns the hash code of the version.</summary>
    /// <returns>A hash of the version's numbers.</returns>
    public override int GetHashCode() => HashCode.Combine(Major, Minor, Patch);

    /// <summary>Returns the version as major.minor.patch.</summary>
    /// <returns>The dotted version text.</returns>
    public override string ToString() =>
        string.Create(CultureInfo.InvariantCulture, $"{Major}.{Minor}.{Patch}");

    /// <summary>Returns whether the versions are equal.</summary>
    /// <param name="left">The version on the left.</param>
    /// <param name="right">The version on the right.</param>
    /// <returns>Whether the versions are equal.</returns>
    public static bool operator ==(NodeVersion left, NodeVersion right) => left.Equals(right);

    /// <summary>Returns whether the versions differ.</summary>
    /// <param name="left">The version on the left.</param>
    /// <param name="right">The version on the right.</param>
    /// <returns>Whether the versions differ.</returns>
    public static bool operator !=(NodeVersion left, NodeVersion right) => !left.Equals(right);

    /// <summary>Returns whether the left version is older.</summary>
    /// <param name="left">The version on the left.</param>
    /// <param name="right">The version on the right.</param>
    /// <returns>Whether the left version is older.</returns>
    public static bool operator <(NodeVersion left, NodeVersion right) => left.CompareTo(right) < 0;

    /// <summary>Returns whether the left version is older or equal.</summary>
    /// <param name="left">The version on the left.</param>
    /// <param name="right">The version on the right.</param>
    /// <returns>Whether the left version is older or equal.</returns>
    public static bool operator <=(NodeVersion left, NodeVersion right) =>
        left.CompareTo(right) <= 0;

    /// <summary>Returns whether the left version is newer.</summary>
    /// <param name="left">The version on the left.</param>
    /// <param name="right">The version on the right.</param>
    /// <returns>Whether the left version is newer.</returns>
    public static bool operator >(NodeVersion left, NodeVersion right) => left.CompareTo(right) > 0;

    /// <summary>Returns whether the left version is newer or equal.</summary>
    /// <param name="left">The version on the left.</param>
    /// <param name="right">The version on the right.</param>
    /// <returns>Whether the left version is newer or equal.</returns>
    public static bool operator >=(NodeVersion left, NodeVersion right) =>
        left.CompareTo(right) >= 0;

    private static bool ConsumeDot(ReadOnlySpan<char> text, ref int index)
    {
        if (index >= text.Length || text[index] != '.')
            return false;
        index++;
        return true;
    }

    private static bool TryReadNumber(ReadOnlySpan<char> text, ref int index, out int value)
    {
        value = 0;
        var end = index;
        while (end < text.Length && char.IsAsciiDigit(text[end]))
            end++;
        if (
            end == index
            || !int.TryParse(
                text[index..end],
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out value
            )
        )
            return false;
        index = end;
        return true;
    }
}
