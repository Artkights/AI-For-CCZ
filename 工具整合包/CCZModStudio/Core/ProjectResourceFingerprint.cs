using System.Collections.Concurrent;
using System.Security.Cryptography;

namespace CCZModStudio.Core;

public readonly record struct ProjectResourceFingerprint(
    string Path,
    long Length,
    long LastWriteTimeUtcTicks,
    long ChangeGeneration,
    string AlgorithmVersion,
    string RangeSha256 = "")
{
    private static readonly ConcurrentDictionary<string, long> Generations = new(StringComparer.OrdinalIgnoreCase);

    public static ProjectResourceFingerprint Create(string path, string algorithmVersion = "v1")
    {
        var fullPath = Normalize(path);
        try
        {
            var info = new FileInfo(fullPath);
            return new(fullPath, info.Exists ? info.Length : -1, info.Exists ? info.LastWriteTimeUtc.Ticks : 0,
                Generations.GetValueOrDefault(fullPath), algorithmVersion);
        }
        catch
        {
            return new(fullPath, -1, 0, Generations.GetValueOrDefault(fullPath), algorithmVersion);
        }
    }

    public static ProjectResourceFingerprint CreateRange(string path, long offset, int length, string algorithmVersion = "v1")
    {
        var fast = Create(path, algorithmVersion);
        if (fast.Length < 0 || offset < 0 || length <= 0 || offset + length > fast.Length) return fast;
        try
        {
            using var stream = new FileStream(fast.Path, FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete, 4096, FileOptions.RandomAccess);
            stream.Position = offset;
            var bytes = new byte[length];
            stream.ReadExactly(bytes);
            PerformanceMetrics.Increment("File.RangeRead.Count");
            PerformanceMetrics.Increment("File.RangeRead.Bytes", bytes.Length);
            return fast with { RangeSha256 = Convert.ToHexString(SHA256.HashData(bytes)) };
        }
        catch
        {
            return fast;
        }
    }

    public static long Invalidate(string path)
    {
        var fullPath = Normalize(path);
        return Generations.AddOrUpdate(fullPath, 1, (_, value) => value + 1);
    }

    public static string Normalize(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return string.Empty;
        try { return System.IO.Path.GetFullPath(path).TrimEnd(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar); }
        catch { return path.Trim(); }
    }
}
