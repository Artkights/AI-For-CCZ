using System.Collections.Concurrent;

namespace CCZModStudio.Core;

[Flags]
public enum ProjectResourceKind
{
    None = 0,
    Executable = 1,
    HexTable = 2,
    Image = 4,
    Palette = 8,
    Script = 16,
    Material = 32,
    Dictionary = 64,
    EffectMetadata = 128,
    ReleaseComponents = 256,
    All = int.MaxValue
}

public sealed record ProjectResourceInvalidatedEventArgs(
    IReadOnlyList<string> Paths,
    ProjectResourceKind Kinds,
    DateTime InvalidatedAtUtc);

/// <summary>Central notification point used after a successful verified write.</summary>
public static class ProjectResourceInvalidationBus
{
    private static readonly ConcurrentDictionary<long, Action<ProjectResourceInvalidatedEventArgs>> Handlers = new();
    private static long _nextId;

    public static IDisposable Subscribe(Action<ProjectResourceInvalidatedEventArgs> handler)
    {
        var id = Interlocked.Increment(ref _nextId);
        Handlers[id] = handler;
        return new Subscription(id);
    }

    public static void Publish(IEnumerable<string> paths, ProjectResourceKind kinds)
    {
        var normalized = paths.Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(ProjectResourceFingerprint.Normalize)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        foreach (var path in normalized) ProjectResourceFingerprint.Invalidate(path);
        var args = new ProjectResourceInvalidatedEventArgs(normalized, kinds, DateTime.UtcNow);
        PerformanceMetrics.Increment("ResourceInvalidation.Published");
        foreach (var handler in Handlers.Values)
        {
            try { handler(args); }
            catch { PerformanceMetrics.Increment("ResourceInvalidation.HandlerFailures"); }
        }
    }

    private sealed class Subscription(long id) : IDisposable
    {
        private long _id = id;
        public void Dispose()
        {
            var value = Interlocked.Exchange(ref _id, 0);
            if (value != 0) Handlers.TryRemove(value, out _);
        }
    }
}
