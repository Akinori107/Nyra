using System.Text.Json;

namespace Nyra.Entities;

internal static class ContentEquality
{
    public static IReadOnlyList<T> WithoutNulls<T>(IReadOnlyList<T>? list) =>
        list is null ? [] : [.. list.Where(item => item is not null)];

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
        where TKey : notnull =>
        DictionaryContentEquals(left, right, static (a, b) =>
            EqualityComparer<TValue>.Default.Equals(a, b)
        );

    public static bool DictionaryDeepEquals(
        IReadOnlyDictionary<string, JsonElement>? left,
        IReadOnlyDictionary<string, JsonElement>? right
    ) => DictionaryContentEquals(left, right, JsonElement.DeepEquals);

    private static bool DictionaryContentEquals<TKey, TValue>(
        IReadOnlyDictionary<TKey, TValue>? left,
        IReadOnlyDictionary<TKey, TValue>? right,
        Func<TValue, TValue, bool> equals
    )
        where TKey : notnull
    {
        if (ReferenceEquals(left, right))
            return true;
        if (left is null || right is null || left.Count != right.Count)
            return false;

        var comparer = EqualityComparer<TKey>.Default;
        if (
            left is Dictionary<TKey, TValue> leftMap
            && ReferenceEquals(leftMap.Comparer, comparer)
            && right is Dictionary<TKey, TValue> rightMap
            && ReferenceEquals(rightMap.Comparer, comparer)
        )
        {
            foreach (var (key, value) in leftMap)
                if (!rightMap.TryGetValue(key, out var other) || !equals(value, other))
                    return false;
            return true;
        }

        var remaining = new Dictionary<TKey, TValue>(left.Count, comparer);
        foreach (var (key, value) in left)
            if (!remaining.TryAdd(key, value))
                return false;
        foreach (var (key, value) in right)
            if (!remaining.Remove(key, out var other) || !equals(other, value))
                return false;
        return remaining.Count == 0;
    }

    public static bool JsonElementEquals(JsonElement? left, JsonElement? right) =>
        left is null
            ? right is null
            : right is { } element && JsonElement.DeepEquals(left.Value, element);
}
