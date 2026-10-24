namespace Nyra.Internal;

internal sealed record ServerInfo(
    bool IsNodeLink,
    string RawVersion,
    NodeVersion? Version,
    IReadOnlySet<string> Plugins,
    IReadOnlySet<string>? EnabledFilters = null
)
{
    internal bool HasPlugin(string name) => Plugins.Contains(name);
}
