using System.Collections.Concurrent;
using System.Diagnostics;

namespace CCZModStudio.Core;

/// <summary>
/// Lightweight process-wide counters and timings used by UI diagnostics and smoke tests.
/// Recording must never make a user operation fail.
/// </summary>
public static class PerformanceMetrics
{
    private static readonly ConcurrentDictionary<string, long> Counters = new(StringComparer.Ordinal);
    private static readonly ConcurrentDictionary<string, TimingAccumulator> Timings = new(StringComparer.Ordinal);

    public static PerformanceOperationScope Begin(string operation, IReadOnlyDictionary<string, string>? dimensions = null)
        => new(operation, dimensions);

    public static void Increment(string counter, long value = 1)
        => Counters.AddOrUpdate(counter, value, (_, current) => current + value);

    public static void Record(string operation, long elapsedTicks)
    {
        var accumulator = Timings.GetOrAdd(operation, _ => new TimingAccumulator());
        accumulator.Record(elapsedTicks);
    }

    public static PerformanceMetricsSnapshot GetSnapshot()
    {
        var timings = Timings.ToDictionary(
            pair => pair.Key,
            pair => pair.Value.Snapshot(),
            StringComparer.Ordinal);
        return new PerformanceMetricsSnapshot(
            DateTime.UtcNow,
            Counters.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal),
            timings,
            Environment.WorkingSet,
            GC.GetTotalMemory(forceFullCollection: false),
            Process.GetCurrentProcess().Threads.Count);
    }

    public static void Reset()
    {
        Counters.Clear();
        Timings.Clear();
    }

    private sealed class TimingAccumulator
    {
        private long _count;
        private long _totalTicks;
        private long _maximumTicks;

        public void Record(long ticks)
        {
            Interlocked.Increment(ref _count);
            Interlocked.Add(ref _totalTicks, ticks);
            long current;
            while (ticks > (current = Interlocked.Read(ref _maximumTicks)) &&
                   Interlocked.CompareExchange(ref _maximumTicks, ticks, current) != current)
            {
            }
        }

        public PerformanceTimingSnapshot Snapshot()
            => new(
                Interlocked.Read(ref _count),
                StopwatchTicksToMilliseconds(Interlocked.Read(ref _totalTicks)),
                StopwatchTicksToMilliseconds(Interlocked.Read(ref _maximumTicks)));
    }

    internal static double StopwatchTicksToMilliseconds(long ticks)
        => ticks * 1000d / Stopwatch.Frequency;
}

public sealed class PerformanceOperationScope : IDisposable
{
    private readonly string _operation;
    private readonly long _started = Stopwatch.GetTimestamp();
    private int _disposed;

    internal PerformanceOperationScope(string operation, IReadOnlyDictionary<string, string>? dimensions)
    {
        _operation = operation;
        PerformanceMetrics.Increment(operation + ".Started");
        if (dimensions != null)
        {
            foreach (var pair in dimensions)
                PerformanceMetrics.Increment($"{operation}.Dimension.{pair.Key}.{pair.Value}");
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        PerformanceMetrics.Record(_operation, Stopwatch.GetTimestamp() - _started);
        PerformanceMetrics.Increment(_operation + ".Completed");
    }
}

public sealed record PerformanceMetricsSnapshot(
    DateTime CapturedAtUtc,
    IReadOnlyDictionary<string, long> Counters,
    IReadOnlyDictionary<string, PerformanceTimingSnapshot> Timings,
    long WorkingSetBytes,
    long ManagedHeapBytes,
    int ThreadCount);

public sealed record PerformanceTimingSnapshot(long Count, double TotalMilliseconds, double MaximumMilliseconds);
