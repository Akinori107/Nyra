using Microsoft.Extensions.Logging;

namespace Nyra.Abstractions;

/// <summary>
/// Bridge event raises with per-handler isolation, a plain multicast invoke lets one throwing subscriber block
/// every handler subscribed after it, and the wiring's routing is one of those handlers.
/// </summary>
/// <remarks>
/// Each subscriber runs in its own try, a crash is reported to the logger when one is provided and the rest
/// still run. Bridge implementations raise their events through this helper instead of a plain invoke.
/// </remarks>
public static class BridgeEvent
{
    /// <summary>
    /// Raises <paramref name="handler"/> one subscriber at a time, a throwing subscriber is reported to the
    /// logger when one is provided and the remaining subscribers still run.
    /// </summary>
    /// <typeparam name="TArgs">The type of the event payload.</typeparam>
    /// <param name="handler">The multicast delegate to invoke, null raises nothing.</param>
    /// <param name="sender">The sender every subscriber receives.</param>
    /// <param name="args">The payload every subscriber receives.</param>
    /// <param name="logger">Where a throwing subscriber's exception is reported, null ignores it.</param>
    public static void RaiseIsolated<TArgs>(
        EventHandler<TArgs>? handler,
        object sender,
        TArgs args,
        ILogger? logger
    )
        where TArgs : System.EventArgs
    {
        if (handler is null)
            return;

        foreach (EventHandler<TArgs> invocation in handler.GetInvocationList())
            try
            {
                invocation(sender, args);
            }
            catch (Exception exception)
            {
                try
                {
                    logger?.LogError(
                        LogEvents.Bridge,
                        exception,
                        "An event handler of the Discord bridge threw handling {Event}, the remaining handlers still ran.",
                        typeof(TArgs).Name
                    );
                }
                catch { }
            }
    }
}
