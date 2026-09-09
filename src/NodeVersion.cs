using System.Globalization;

namespace Nyra;

/// <summary>A node server version (major.minor.patch), parsed from the string reported by the server.</summary>
public readonly struct NodeVersion(int major, int minor, int patch)
    : IEquatable<NodeVersion>,
        IComparable<NodeVersion>
{
    /// <summary>Oldest NodeLink version supported.</summary>
    public static NodeVersion MinimumNodeLink { get; } = new(3, 8, 0);

    /// <summary>Oldest Lavalink version supported.</summary>
    public static NodeVersion MinimumLavalink { get; } = new(4, 2, 0);
    public int Major { get; } = major;
    public int Minor { get; } = minor;
    public int Patch { get; } = patch;

    /// <summary>
    /// Parses <c>major.minor.patch</c>, missing components default to zero and a trailing
    /// pre-release tag or patch metadata (<c>-</c>/<c>+</c>) is ignored. Returns false for
    /// anything else, such as the plain commit hash a dev patch reports.
    /// </summary>
    public static bool TryParse(ReadOnlySpan<char> text, out NodeVersion version)
    {
        version = default;
        text = text.Trim();
        if (text.Length == 0 || !char.IsAsciiDigit(text[0]))
            return false;
        var index = 0;
        if (!TryReadNumber(text, ref index, out var major))
            return false;
        var minor = 0;
        var patch = 0;
        if (ConsumeDot(text, ref index) && !TryReadNumber(text, ref index, out minor))
            return false;
        if (ConsumeDot(text, ref index) && !TryReadNumber(text, ref index, out patch))
            return false;
        // Only a pre-release tag or patch metadata may follow, 1.2.3.4 is not a version.
        if (index < text.Length && text[index] is not ('-' or '+'))
            return false;
        version = new NodeVersion(major, minor, patch);
        return true;
    }

    public static bool TryParse(string? text, out NodeVersion version) =>
        TryParse(text.AsSpan(), out version);

    public bool Equals(NodeVersion other) =>
        Major == other.Major && Minor == other.Minor && Patch == other.Patch;

    public int CompareTo(NodeVersion other) =>
        Major != other.Major ? Major.CompareTo(other.Major)
        : Minor != other.Minor ? Minor.CompareTo(other.Minor)
        : Patch.CompareTo(other.Patch);

    public override bool Equals(object? obj) => obj is NodeVersion other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(Major, Minor, Patch);

    public override string ToString() =>
        string.Create(CultureInfo.InvariantCulture, $"{Major}.{Minor}.{Patch}");

    public static bool operator ==(NodeVersion left, NodeVersion right) => left.Equals(right);

    public static bool operator !=(NodeVersion left, NodeVersion right) => !left.Equals(right);

    public static bool operator <(NodeVersion left, NodeVersion right) => left.CompareTo(right) < 0;

    public static bool operator <=(NodeVersion left, NodeVersion right) =>
        left.CompareTo(right) <= 0;

    public static bool operator >(NodeVersion left, NodeVersion right) => left.CompareTo(right) > 0;

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
