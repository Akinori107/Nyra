namespace Nyra;

/// <summary>
/// Guards shared by operations that require Lavalink or NodeLink specifically,
/// an operation exclusive to one kind throws a <see cref="NotSupportedException"/> naming the
/// feature when used against the other.
/// </summary>
internal static class NodeGuards
{
    /// <summary>The name Lavalink reports for the LavaLyrics plugin in GET /v4/info.</summary>
    internal const string LavaLyricsPlugin = "lavalyrics-plugin";

    /// <summary>The name Lavalink reports for the LavaSrc plugin in GET /v4/info.</summary>
    internal const string LavaSrcPlugin = "lavasrc-plugin";

    /// <summary>Throws when the node is not NodeLink, or runs a version older than the feature requires.</summary>
    internal static void RequireNodeLink(
        Node node,
        string? feature = null,
        NodeVersion? minimum = null
    )
    {
        if (!node.IsNodeLink)
            throw new NotSupportedException(
                $"{feature ?? "This operation"} requires NodeLink, the node '{node.Name}' is Lavalink."
            );

        if (minimum is { } required && node.Version is { } version && version < required)
            throw new NotSupportedException(
                $"{feature ?? "This operation"} requires NodeLink {required} or newer, the node '{node.Name}' runs {version}."
            );
    }

    /// <summary>
    /// Throws when the node is NodeLink and runs a version older than <paramref name="minimum"/>,
    /// a no-op for Lavalink. Unlike <see cref="RequireNodeLink"/>, this is for a feature both
    /// server kinds support where NodeLink alone lags behind on a specific version.
    /// </summary>
    internal static void RequireMinimumIfNodeLink(
        Node node,
        NodeVersion minimum,
        string? feature = null
    )
    {
        if (node.IsNodeLink && node.Version is { } version && version < minimum)
            throw new NotSupportedException(
                $"{feature ?? "This operation"} requires NodeLink {minimum} or newer, the node '{node.Name}' runs {version}."
            );
    }

    /// <summary>Throws when the node is neither NodeLink nor Lavalink with the given plugin loaded.</summary>
    internal static void RequireNodeLinkOrPlugin(
        Node node,
        string pluginName,
        string? feature = null
    )
    {
        if (node.IsNodeLink)
            return;

        if (!node.HasPlugin(pluginName))
            throw new NotSupportedException(
                $"{feature ?? "This operation"} requires NodeLink or the '{pluginName}' Lavalink plugin, the node '{node.Name}' has neither."
            );
    }

    /// <summary>Throws when the node is not Lavalink.</summary>
    internal static void RequireLavalink(Node node, string? feature = null)
    {
        if (node.IsNodeLink)
            throw new NotSupportedException(
                $"{feature ?? "This operation"} requires Lavalink, the node '{node.Name}' is NodeLink."
            );
    }
}
