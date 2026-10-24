namespace Nyra.EventArgs;

/// <summary>Raised when a handler of any other event of the connection threw an exception, playback or not.</summary>
public sealed class HandlerErrorEventArgs : System.EventArgs
{
    /// <summary>The exception a handler of the connection threw.</summary>
    /// <remarks>
    /// This is a local failure of this client's own handler, not the server's error, the error the server
    /// reported for a track arrives as <see cref="TrackExceptionEventArgs.Exception"/> instead, a handler that
    /// threw is always this client's own bug. An OperationCanceledException a handler throws is swallowed as
    /// cooperative cancellation and never arrives here.
    /// </remarks>
    public Exception Exception { get; }

    /// <summary>
    /// The args of the event whose handler threw, the origin of the failure, match it to tell a lyrics, mix or
    /// lifecycle handler apart without parsing a stack trace.
    /// </summary>
    /// <remarks>
    /// Null only when the args were built manually without one, the library's raises
    /// always supply it.
    /// </remarks>
    public System.EventArgs? Origin { get; }

    /// <summary>Initializes the event args with the reported values.</summary>
    /// <param name="exception">The exception a handler threw.</param>
    /// <param name="origin">
    /// The args of the event whose handler threw, null only when the args were built manually without
    /// one.
    /// </param>
    public HandlerErrorEventArgs(Exception exception, System.EventArgs? origin = null)
    {
        ArgumentNullException.ThrowIfNull(exception);
        Exception = exception;
        Origin = origin;
    }
}
