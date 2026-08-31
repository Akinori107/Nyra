namespace Nyra;

/// <summary>
/// Guards shared by operations that require Lavalink or NodeLink specifically;
/// an operation exclusive to one kind throws a <see cref="NotSupportedException"/> naming the
/// feature when used against the other.
/// </summary>
internal static class NodeGuards
{
    /// <summary>Throws when the node is not NodeLink, or runs a version older than the feature requires.</summary>
    internal static void RequireNodeLink(
        Node node,
        string? feature = null,
        NodeVersion? minimum = null
    )
    {
        if (!node.IsNodeLink)
            throw new NotSupportedException(
                $"{feature ?? "This operation"} requires NodeLink; the node '{node.Name}' is Lavalink."
            );

        if (minimum is { } required && node.Version is { } version && version < required)
            throw new NotSupportedException(
                $"{feature ?? "This operation"} requires NodeLink {required} or newer; the node '{node.Name}' runs {version}."
            );
    }

    /// <summary>Throws when the node is not Lavalink.</summary>
    internal static void RequireLavalink(Node node, string? feature = null)
    {
        if (node.IsNodeLink)
            throw new NotSupportedException(
                $"{feature ?? "This operation"} requires Lavalink; the node '{node.Name}' is NodeLink."
            );
    }
}
