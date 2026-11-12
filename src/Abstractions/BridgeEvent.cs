using Microsoft.Extensions.Logging;
using Nyra.Internal;

namespace Nyra.Abstractions;

/// <summary>
/// Raises bridge events so a subscriber that throws does not stop the others. Bridges raise their events
/// through it.
/// </summary>
/// <remarks>
/// A crash is logged when there is a logger, at most five times per thirty seconds for the same kind.
/// </remarks>
public static class BridgeEvent
{
    private static readonly ErrorThrottle Errors = new();

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
                if (Errors.TryAdmit(exception, out _))
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
