namespace Nyra.Exceptions;

/// <summary>A malformed or unsupported encoded track.</summary>
public sealed class InvalidTrackException : NyraException
{
    public InvalidTrackException(string message)
        : base(message) { }

    public InvalidTrackException(string message, Exception innerException)
        : base(message, innerException) { }
}
