namespace Nyra;

/// <summary>An event handler that can await, Nyra awaits the returned task.</summary>
/// <remarks>
/// Handlers run in the order they were added, and one that throws does not stop the others. The events of a
/// node, a guild connection or the extension share one queue, so a handler that never returns holds up the
/// rest, and past 4096 pending events new ones are dropped with a warning.
/// </remarks>
/// <typeparam name="TArgs">Event payload type.</typeparam>
/// <param name="sender">Origin of the event.</param>
/// <param name="args">Event payload.</param>
/// <returns>Task that completes when the handler finished its work.</returns>
public delegate Task AsyncEventHandler<TArgs>(object? sender, TArgs args)
    where TArgs : System.EventArgs;
