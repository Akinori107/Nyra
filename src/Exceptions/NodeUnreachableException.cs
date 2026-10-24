namespace Nyra.Exceptions;

/// <summary>
/// The node's transport failed before an answer arrived, the server never spoke, unlike <see
/// cref="RestException"/>, which carries a reply.
/// </summary>
/// <remarks>
/// A request that died in flight may still have landed, the inner exception holds the transport's own cause.
/// </remarks>
public sealed class NodeUnreachableException : NyraException
{
    /// <summary>Creates the exception.</summary>
    /// <param name="message">The message that describes the error.</param>
    /// <param name="innerException">The transport exception that caused this one.</param>
    public NodeUnreachableException(string message, Exception innerException)
        : base(message, innerException) { }
}
