namespace Nyra.Exceptions;

/// <summary>The connected server doesn't expose /v4/info, didn't report a version, or is below the minimum supported version.</summary>
public sealed class IncompatibleServerException : NyraException
{
    public IncompatibleServerException(string message)
        : base(message) { }

    public IncompatibleServerException(string message, Exception innerException)
        : base(message, innerException) { }
}
