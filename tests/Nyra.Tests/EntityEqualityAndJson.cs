using System.Text.Json;
using Nyra.Entities;
using Nyra.Exceptions;

namespace Nyra.Tests;

public sealed class EntityEqualityAndJson
{
    private sealed class ProbeException(string message) : NyraException(message) { }

    [Test]
    public async Task MessageOnlyException()
    {
        await Assert.That(new ProbeException("probe").Message).IsEqualTo("probe");
    }

    [Test]
    public async Task SettingsMargin()
    {
        var a = new SponsorBlockSettings
        {
            Enabled = true,
            Categories = ["sponsor"],
            ActionTypes = ["skip"],
            SkipMarginMilliseconds = 500,
        };
        var b = new SponsorBlockSettings
        {
            Enabled = true,
            Categories = ["sponsor"],
            ActionTypes = ["skip"],
            SkipMarginMilliseconds = 500,
        };
        var otherMargin = a with { SkipMarginMilliseconds = 600 };
        await Assert.That(a).IsEqualTo(b);
        await Assert.That(a).IsNotEqualTo(otherMargin);
    }

    [Test]
    public async Task PluginFilterMaps()
    {
        var left = new FilterChain
        {
            Volume = 1f,
            PluginFilters = new Dictionary<string, JsonElement>
            {
                ["plug"] = JsonDocument.Parse("""{"a":[1,2]}""").RootElement,
            },
        };
        var sameContent = new FilterChain
        {
            Volume = 1f,
            PluginFilters = new Dictionary<string, JsonElement>
            {
                ["plug"] = JsonDocument.Parse("""{"a":[1,2]}""").RootElement,
            },
        };
        await Assert.That(left).IsEqualTo(sameContent);
        await Assert.That(left).IsNotEqualTo(left with { PluginFilters = null });
        await Assert.That(left).IsEqualTo(left with { });
        var missingKey = new FilterChain
        {
            Volume = 1f,
            PluginFilters = new Dictionary<string, JsonElement>
            {
                ["plug"] = JsonDocument.Parse("""{"a":[1,2]}""").RootElement,
                ["extra"] = JsonDocument.Parse("{}").RootElement,
            },
        };
        await Assert.That(left).IsNotEqualTo(missingKey);
    }
}
