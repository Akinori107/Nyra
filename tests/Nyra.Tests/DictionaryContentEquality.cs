using System.Text.Json;
using Nyra.Entities;

namespace Nyra.Tests;

public sealed class DictionaryContentEquality
{
    [Test]
    public async Task PluginFilterEqualityUsesExactKeysInBothDirections()
    {
        using var document = JsonDocument.Parse("""{"gain":1}""");
        var value = document.RootElement;
        var lower = new FilterChain
        {
            PluginFilters = new Dictionary<string, JsonElement> { ["plugin"] = value },
        };
        var upper = new FilterChain
        {
            PluginFilters = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase)
            {
                ["PLUGIN"] = value,
            },
        };
        var matching = new FilterChain
        {
            PluginFilters = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase)
            {
                ["plugin"] = value,
            },
        };

        await Assert.That(lower.Equals(upper)).IsFalse();
        await Assert.That(upper.Equals(lower)).IsFalse();
        await Assert.That(lower.Equals(matching)).IsTrue();
        await Assert.That(matching.Equals(lower)).IsTrue();
        await Assert.That(lower.GetHashCode()).IsEqualTo(matching.GetHashCode());
    }

    [Test]
    public async Task CustomComparerMapsCompareEveryExactEntry()
    {
        var left = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            ["a"] = 1,
            ["b"] = 2,
        };
        var right = new Dictionary<string, int>(StringComparer.Ordinal) { ["b"] = 2, ["a"] = 1 };

        await Assert.That(ContentEquality.DictionaryEquals(left, right)).IsTrue();
        await Assert.That(ContentEquality.DictionaryEquals(right, left)).IsTrue();
        right["a"] = 3;
        await Assert.That(ContentEquality.DictionaryEquals(left, right)).IsFalse();
        await Assert.That(ContentEquality.DictionaryEquals(right, left)).IsFalse();
    }

    [Test]
    public async Task DuplicateExactKeysCannotMatchOneEntryTwice()
    {
        var duplicate = new Dictionary<string, int>(ReferenceEqualityComparer.Instance)
        {
            [new string('a', 1)] = 1,
            [new string('a', 1)] = 1,
        };
        var unique = new Dictionary<string, int> { ["a"] = 1, ["b"] = 1 };

        await Assert.That(ContentEquality.DictionaryEquals(duplicate, unique)).IsFalse();
        await Assert.That(ContentEquality.DictionaryEquals(unique, duplicate)).IsFalse();
    }

    [Test]
    public async Task CounterEqualityDoesNotDependOnDictionaryComparer()
    {
        var lower = new ApiCounters { Requests = new Dictionary<string, long> { ["api"] = 1 } };
        var upper = new ApiCounters
        {
            Requests = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase)
            {
                ["API"] = 1,
            },
        };
        var matching = new ApiCounters
        {
            Requests = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase)
            {
                ["api"] = 1,
            },
        };

        await Assert.That(lower.Equals(upper)).IsFalse();
        await Assert.That(upper.Equals(lower)).IsFalse();
        await Assert.That(lower.Equals(matching)).IsTrue();
        await Assert.That(matching.Equals(lower)).IsTrue();
    }
}
