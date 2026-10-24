namespace Nyra;

/// <summary>
/// Asynchronous event handler. Unlike <see cref="System.EventHandler{TEventArgs}"/>, a handler may await inside
/// its body without becoming <c>async void</c>, because the raiser awaits the returned task.
/// </summary>
/// <remarks>
/// Handlers of one event run in subscription order and one throwing does not stop the rest, the exception goes
/// to the raise's error callback, except an <see cref="OperationCanceledException"/>, which is swallowed as
/// cooperative cancellation. Raise by awaiting each entry of <see cref="Delegate.GetInvocationList"/>, a plain
/// multicast invoke observes only the last handler's task.
/// <para>
/// The events of one owner (a node, a guild connection, the extension) share one queue. A handler that never
/// returns stalls it, and once 4096 events are pending the next ones are dropped (the high-frequency ones
/// already past 512) with a single warning, so a stuck handler cannot grow the process without bound.
/// </para>
/// <para>
/// The type parameter is invariant on purpose. A variance conversion would let one delegate instance subscribe
/// to several events, and the multicast combine then throws an ArgumentException or a removal silently no-ops.
/// </para>
/// </remarks>
/// <typeparam name="TArgs">Event payload type.</typeparam>
/// <param name="sender">Origin of the event.</param>
/// <param name="args">Event payload.</param>
/// <returns>Task that completes when the handler finished its work.</returns>
public delegate Task AsyncEventHandler<TArgs>(object? sender, TArgs args)
    where TArgs : System.EventArgs;
