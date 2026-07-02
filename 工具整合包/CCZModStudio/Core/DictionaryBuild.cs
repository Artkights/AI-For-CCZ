namespace CCZModStudio.Core;

public enum DuplicateKeyResolution
{
    First,
    Last
}

public sealed record DuplicateKeyDiagnostic<TKey>(TKey Key, int Count);

public static class DictionaryBuild
{
    public static Dictionary<TKey, TValue> ToDictionaryFirstByKey<TSource, TKey, TValue>(
        this IEnumerable<TSource> source,
        Func<TSource, TKey> keySelector,
        Func<TSource, TValue> valueSelector,
        IEqualityComparer<TKey>? comparer = null)
        where TKey : notnull
        => ToDictionaryWithDiagnostics(
            source,
            keySelector,
            valueSelector,
            DuplicateKeyResolution.First,
            out _,
            comparer);

    public static Dictionary<TKey, TValue> ToDictionaryLastByKey<TSource, TKey, TValue>(
        this IEnumerable<TSource> source,
        Func<TSource, TKey> keySelector,
        Func<TSource, TValue> valueSelector,
        IEqualityComparer<TKey>? comparer = null)
        where TKey : notnull
        => ToDictionaryWithDiagnostics(
            source,
            keySelector,
            valueSelector,
            DuplicateKeyResolution.Last,
            out _,
            comparer);

    public static Dictionary<TKey, TValue> ToDictionaryWithDiagnostics<TSource, TKey, TValue>(
        this IEnumerable<TSource> source,
        Func<TSource, TKey> keySelector,
        Func<TSource, TValue> valueSelector,
        DuplicateKeyResolution resolution,
        out IReadOnlyList<DuplicateKeyDiagnostic<TKey>> duplicates,
        IEqualityComparer<TKey>? comparer = null)
        where TKey : notnull
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(keySelector);
        ArgumentNullException.ThrowIfNull(valueSelector);

        var result = new Dictionary<TKey, TValue>(comparer);
        var counts = new Dictionary<TKey, int>(comparer);
        foreach (var item in source)
        {
            var key = keySelector(item);
            counts.TryGetValue(key, out var count);
            counts[key] = count + 1;

            if (count == 0 || resolution == DuplicateKeyResolution.Last)
            {
                result[key] = valueSelector(item);
            }
        }

        duplicates = counts
            .Where(pair => pair.Value > 1)
            .Select(pair => new DuplicateKeyDiagnostic<TKey>(pair.Key, pair.Value))
            .ToArray();
        return result;
    }

    public static IReadOnlyList<DuplicateKeyDiagnostic<TKey>> FindDuplicateKeys<TSource, TKey>(
        this IEnumerable<TSource> source,
        Func<TSource, TKey> keySelector,
        IEqualityComparer<TKey>? comparer = null)
        where TKey : notnull
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(keySelector);

        var counts = new Dictionary<TKey, int>(comparer);
        foreach (var item in source)
        {
            var key = keySelector(item);
            counts.TryGetValue(key, out var count);
            counts[key] = count + 1;
        }

        return counts
            .Where(pair => pair.Value > 1)
            .Select(pair => new DuplicateKeyDiagnostic<TKey>(pair.Key, pair.Value))
            .ToArray();
    }
}
