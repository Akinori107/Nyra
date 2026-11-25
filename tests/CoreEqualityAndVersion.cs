namespace Nyra.Tests;

public sealed class CoreEqualityAndVersion
{
    [Test]
    public async Task Version()
    {
        var parsed = new[]
        {
            ("v3.10.0", true, 3),
            ("V3.10.0", true, 3),
            ("3.10.0-beta.1", true, 3),
            ("3.10.0+build.5", true, 3),
            ("3.10", true, 3),
            ("3.10.0-dev.2026-09-27.1", true, 3),
            ("3", false, 0),
            ("3.10.0.1", false, 0),
            ("99999999999999999999.0.0", false, 0),
            ("x.10.0", false, 0),
        };
        foreach (var (text, takes, major) in parsed)
        {
            var read = NodeVersion.TryParse(text, out var version);
            await Assert.That(read).IsEqualTo(takes);
            await Assert.That(!takes || version.Major == major).IsTrue();
        }

        _ = NodeVersion.TryParse("3.10.0", out var plain);
        _ = NodeVersion.TryParse("3.10.0", out var same);
        _ = NodeVersion.TryParse("3.10.1", out var bump);
        using (Assert.Multiple())
        {
            await Assert.That(plain).IsEqualTo(same);
            await Assert.That(plain).IsNotEqualTo(bump);
            await Assert.That(plain.GetHashCode()).IsEqualTo(same.GetHashCode());
            await Assert.That(plain).IsNotEqualTo((object?)null);
            await Assert.That(plain).IsEqualTo((object)same);
        }

        _ = NodeVersion.TryParse("3.10.0", out var older);
        _ = NodeVersion.TryParse("3.11.0", out var newer);
        using (Assert.Multiple())
        {
            await Assert.That(older == same).IsTrue();
            await Assert.That(older != newer).IsTrue();
            await Assert.That(older < newer).IsTrue();
        }
        await Assert.That(newer > older).IsTrue();
        await Assert.That(older <= same).IsTrue();
        await Assert.That(same >= older).IsTrue();
        await Assert.That(newer >= older).IsTrue();

        await Assert.That(NodeVersion.TryParse("3.", out _)).IsFalse();
        await Assert.That(NodeVersion.TryParse("3.10.", out _)).IsFalse();
    }
}
