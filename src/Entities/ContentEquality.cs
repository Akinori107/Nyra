using System.Text.Json;

namespace Nyra.Entities;

internal static class ContentEquality
{
    public static bool ListEquals<T>(IReadOnlyList<T>? left, IReadOnlyList<T>? right)
    {
        if (ReferenceEquals(left, right))
            return true;
        if (left is null || right is null || left.Count != right.Count)
            return false;
        for (var i = 0; i < left.Count; i++)
            if (!EqualityComparer<T>.Default.Equals(left[i], right[i]))
                return false;
        return true;
    }

    public static bool DictionaryEquals<TKey, TValue>(
        IReadOnlyDictionary<TKey, TValue>? left,
        IReadOnlyDictionary<TKey, TValue>? right
    )
        where TKey : notnull
    {
        if (ReferenceEquals(left, right))
            return true;
        if (left is null || right is null || left.Count != right.Count)
            return false;
        foreach (var (key, value) in left)
            if (
                !right.TryGetValue(key, out var other)
                || !EqualityComparer<TValue>.Default.Equals(value, other)
            )
                return false;
        return true;
    }

    public static bool DictionaryDeepEquals(
        IReadOnlyDictionary<string, JsonElement>? left,
        IReadOnlyDictionary<string, JsonElement>? right
    )
    {
        if (ReferenceEquals(left, right))
            return true;
        if (left is null || right is null || left.Count != right.Count)
            return false;
        foreach (var (key, value) in left)
            if (!right.TryGetValue(key, out var other) || !JsonElement.DeepEquals(value, other))
                return false;
        return true;
    }

    public static bool JsonElementEquals(JsonElement? left, JsonElement? right) =>
        left is null
            ? right is null
            : right is { } element && JsonElement.DeepEquals(left.Value, element);
}
