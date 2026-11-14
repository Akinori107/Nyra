namespace Nyra.EventArgs;

/// <summary>Raised when a handler of any other event of the connection threw an exception, playback or not.</summary>
public sealed class HandlerErrorEventArgs : System.EventArgs
{
    /// <summary>The exception a handler of the connection threw.</summary>
    /// <remarks>
    /// A failure in your own handler, not a server error, those arrive as
    /// <see cref="TrackExceptionEventArgs.Exception"/>. An OperationCanceledException is ignored.
    /// </remarks>
    public Exception Exception { get; }

    /// <summary>
    /// The args of the event whose handler threw, to tell which handler failed.
    /// </summary>
    /// <remarks>
    /// Null only when you built the args yourself.
    /// </remarks>
    public System.EventArgs? Origin { get; }

    /// <summary>Initializes the event args with the reported values.</summary>
    /// <param name="exception">The exception a handler threw.</param>
    /// <param name="origin">
    /// The args of the event whose handler threw, null only when you build the args yourself.
    /// </param>
    public HandlerErrorEventArgs(Exception exception, System.EventArgs? origin = null)
    {
        ArgumentNullException.ThrowIfNull(exception);
        Exception = exception;
        Origin = origin;
    }
}
