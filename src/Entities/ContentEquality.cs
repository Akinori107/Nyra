using System.Text.Json;

namespace Nyra.Entities;

internal static class ContentEquality
{
    private static readonly EqualityComparer<JsonElement> DeepJsonComparer =
        EqualityComparer<JsonElement>.Create(JsonElement.DeepEquals);

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
        IReadOnlyDictionary<TKey, TValue>? right,
        IEqualityComparer<TValue>? comparer = null
    )
        where TKey : notnull
    {
        if (ReferenceEquals(left, right))
            return true;
        if (left is null || right is null || left.Count != right.Count)
            return false;
        comparer ??= EqualityComparer<TValue>.Default;
        foreach (var (key, value) in left)
            if (!right.TryGetValue(key, out var other) || !comparer.Equals(value, other))
                return false;
        return true;
    }

    public static bool DictionaryDeepEquals(
        IReadOnlyDictionary<string, JsonElement>? left,
        IReadOnlyDictionary<string, JsonElement>? right
    ) => DictionaryEquals(left, right, DeepJsonComparer);

    public static bool JsonElementEquals(JsonElement? left, JsonElement? right) =>
        left is null
            ? right is null
            : right is { } element && JsonElement.DeepEquals(left.Value, element);
}
