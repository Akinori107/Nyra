using System.Net;
using Nyra.Internal;

namespace Nyra.Tests;

public sealed class RestBodyBound
{
    private sealed class UnknownLengthContent(byte[] bytes) : HttpContent
    {
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) =>
            stream.WriteAsync(bytes).AsTask();

        protected override bool TryComputeLength(out long length)
        {
            length = 0;
            return false;
        }
    }

    [Test]
    public async Task ReadsABodyInsideTheBound()
    {
        using var content = new ByteArrayContent([1, 2, 3, 4]);

        var bytes = await RestClient
            .ReadBoundedAsync(content, 4, CancellationToken.None)
            .ConfigureAwait(false);

        await Assert.That(bytes).IsEquivalentTo(new byte[] { 1, 2, 3, 4 });
    }

    [Test]
    public async Task RefusesABodyDeclaredBeyondTheBound()
    {
        using var content = new ByteArrayContent([1, 2, 3, 4, 5]);

        var bytes = await RestClient
            .ReadBoundedAsync(content, 4, CancellationToken.None)
            .ConfigureAwait(false);

        await Assert.That(bytes).IsNull();
    }

    [Test]
    public async Task RefusesAStreamedBodyBeyondTheBound()
    {
        using var content = new UnknownLengthContent([1, 2, 3, 4, 5]);

        var bytes = await RestClient
            .ReadBoundedAsync(content, 4, CancellationToken.None)
            .ConfigureAwait(false);

        await Assert.That(bytes).IsNull();
    }
}
