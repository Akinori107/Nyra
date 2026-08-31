using Microsoft.Extensions.Logging;

namespace Nyra;

/// <summary>Log event ids used by Nyra.</summary>
public static class Events
{
    public static EventId Intents { get; } = new(1000, "Intents");

    public static EventId Node { get; } = new(1001, "Node");
}
