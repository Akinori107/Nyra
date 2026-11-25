[assembly: Timeout(300_000)]

namespace Nyra.Tests;

public sealed class VersionEndpointAndReader
{
    [Test]
    public async Task Versions()
    {
        await Assert.That(NodeVersion.TryParse("3.10.0", out var plain)).IsTrue();
        await Assert.That(plain.Major).IsEqualTo(3);
        await Assert.That(NodeVersion.TryParse("3.10.0-dev.2026-09-27.1", out var dev)).IsTrue();
        await Assert.That(dev.Minor).IsEqualTo(10);
        using (Assert.Multiple())
        {
            await Assert.That(NodeVersion.TryParse("not-a-version", out _)).IsFalse();
            await Assert.That(NodeVersion.TryParse(string.Empty, out _)).IsFalse();
            await Assert.That(NodeVersion.TryParse(null, out _)).IsFalse();
        }
    }

    [Test]
    public async Task Endpoints()
    {
        await Assert.That(new Endpoint("127.0.0.1", 3000, "pass").Port).IsEqualTo(3000);
        await Assert.That(new Endpoint("%zz", 3000, "pass").Host).IsEqualTo("%zz");
        await Assert
            .That(() =>
            {
                _ = new Endpoint("127.0.0.1", 3000, string.Empty);
            })
            .Throws<ArgumentException>();
    }
}
