using System.Text.Json;
using Nyra.Entities;

namespace Nyra.Tests;

public sealed class EntityAndOptionsSurface
{
    [Test]
    public async Task Endpoint()
    {
        await Assert.That(new Endpoint("[::]", 3000, "pass").Host).IsEqualTo("[::]");
        await Assert.That(new Endpoint("[zzz]", 3000, "pass").Host).IsEqualTo("[zzz]");
    }

    [Test]
    public async Task VersionCompare()
    {
        _ = NodeVersion.TryParse("2.0.0", out var two);
        _ = NodeVersion.TryParse("1.9.9", out var older);
        _ = NodeVersion.TryParse("2.1.0", out var newerMinor);
        _ = NodeVersion.TryParse("2.0.1", out var newerPatch);
        using (Assert.Multiple())
        {
            await Assert.That(two.CompareTo(older)).IsGreaterThan(0);
            await Assert.That(two.CompareTo(newerMinor)).IsLessThan(0);
            await Assert.That(two.CompareTo(newerPatch)).IsLessThan(0);
            await Assert.That(two.CompareTo(two)).IsEqualTo(0);
        }
    }

    [Test]
    public async Task DuckingWithoutADurationOmitsItUnderPlainOptions()
    {
        var written = JsonSerializer.Serialize(new DuckingOptions { Enabled = true });

        await Assert.That(written).DoesNotContain("duration");
    }
}
