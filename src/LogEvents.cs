using Microsoft.Extensions.Logging;

namespace Nyra;

/// <summary>Log event ids used by Nyra.</summary>
public static class LogEvents
{
    /// <summary>The event id of the gateway intents a bridge requires.</summary>
    public static EventId Intents { get; } = new(1000, "NyraIntents");

    /// <summary>The event id of node activity.</summary>
    public static EventId Node { get; } = new(1001, "Node");

    /// <summary>The event id of shard routing.</summary>
    public static EventId Sharding { get; } = new(1002, "Sharding");

    /// <summary>The event id of per-guild playback activity.</summary>
    public static EventId Playback { get; } = new(1003, "Playback");

    /// <summary>The event id of the extension's own activity.</summary>
    public static EventId Extension { get; } = new(1004, "Extension");

    /// <summary>The event id of the Discord bridge adapters.</summary>
    public static EventId Bridge { get; } = new(1005, "Bridge");
}
