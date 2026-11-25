using Nyra.Entities;

namespace Nyra.Tests;

public sealed class NullToleranceRegression
{
    [Test]
    public async Task NanChainReflexivity()
    {
        var chain = new FilterChain { Volume = float.NaN };
        await Assert.That(chain).IsEqualTo(chain);
        await Assert.That(chain.GetHashCode()).IsEqualTo(chain.GetHashCode());
        var twin = new FilterChain { Volume = float.NaN };
        await Assert.That(chain).IsEqualTo(twin);
    }
}
