using Nyra.Exceptions;

namespace Nyra.Tests;

public sealed class Entity
{
    [Test]
    public async Task Exceptions()
    {
        var cause = new InvalidOperationException("the socket died");
        var unreachable = new NodeUnreachableException("the node never answered", cause);
        await Assert.That(unreachable.InnerException).IsSameReferenceAs(cause);
        await Assert.That(unreachable.Message).IsEqualTo("the node never answered");

        var incompatible = new IncompatibleServerException("no /v4/info", cause);
        await Assert.That(ReferenceEquals(incompatible.InnerException, cause)).IsTrue();

        var handshake = new VoiceHandshakeException("the voice update timed out");
        await Assert.That(handshake is NyraException { InnerException: null }).IsTrue();

        bool grouped;
        try
        {
            throw new VoiceHandshakeException("no voice");
        }
        catch (NyraException caught)
        {
            grouped = caught.Message == "no voice";
        }
        await Assert.That(grouped).IsTrue();
    }
}
