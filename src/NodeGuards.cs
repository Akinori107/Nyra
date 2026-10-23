using Nyra.Internal;

namespace Nyra;

internal static class NodeGuards
{
    internal const string LavaLyricsPlugin = "lavalyrics-plugin";

    internal const string LavaSrcPlugin = "lavasrc-plugin";

    internal static void RequireNodeLink(Node node, string feature, NodeVersion? minimum = null) =>
        RequireNodeLink(node.ServerInfo, node, feature, minimum);

    internal static void RequireNodeLink(
        ServerInfo? info,
        Node node,
        string feature,
        NodeVersion? minimum = null
    )
    {
        if (info is { IsNodeLink: false })
            throw new NotSupportedException(
                $"{feature} requires NodeLink, the node '{node.Name}' is Lavalink."
            );

        if (info is { Version: { } version } held && minimum is { } required && version < required)
            throw new NotSupportedException(
                $"{feature} requires NodeLink {required} or newer, the node '{node.Name}' runs {held.RawVersion}."
            );
    }

    internal static void RequireNodeLinkOrPlugin(Node node, string pluginName, string feature)
    {
        var info = node.ServerInfo;

        if (info?.IsNodeLink is true)
            return;

        if (info is { } held && !held.HasPlugin(pluginName))
            throw new NotSupportedException(
                $"{feature} requires NodeLink or the '{pluginName}' Lavalink plugin, the node '{node.Name}' has neither."
            );
    }

    internal static void RequireLavalink(Node node, string feature)
    {
        if (node.IsNodeLink)
            throw new NotSupportedException(
                $"{feature} requires Lavalink, the node '{node.Name}' is NodeLink."
            );
    }
}
