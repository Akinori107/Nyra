namespace Nyra.Exceptions;

/// <summary>
/// The node's transport failed before an answer arrived, the server never spoke, unlike <see
/// cref="RestException"/>, which carries a reply.
/// </summary>
/// <remarks>
/// A request that died in flight may still have landed, the inner exception holds the transport's own cause.
/// </remarks>
/// <param name="message">The message that describes the error.</param>
/// <param name="innerException">The transport exception that caused this one.</param>
public sealed class NodeUnreachableException(string message, Exception innerException)
    : NyraException(message, innerException) { }
