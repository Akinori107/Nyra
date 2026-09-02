namespace Nyra.EventArgs;

/// <summary>Raised when a handler of another playback event threw an exception.</summary>
public sealed class PlaybackErrorEventArgs : System.EventArgs
{
    public Exception Exception { get; }

    public PlaybackErrorEventArgs(Exception exception)
    {
        Exception = exception;
    }
}
