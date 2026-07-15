using System.Collections.Concurrent;
using System.Security.Cryptography;
using CCZModStudio.Models;

namespace CCZModStudio.Core;

internal sealed record ExecutableAnalysisSnapshot(
    ProjectResourceFingerprint Fingerprint,
    byte[] Bytes,
    string Sha256,
    ExeCodeCaveScanner.PeImage PeImage,
    X86ScanResult InstructionScan)
{
    public ReadOnlyMemory<byte> Image => Bytes;

    public long VirtualAddressToFileOffset(uint virtualAddress)
    {
        if (virtualAddress < PeImage.ImageBase)
            throw new InvalidOperationException($"虚拟地址 0x{virtualAddress:X8} 小于 ImageBase 0x{PeImage.ImageBase:X8}。");
        var rva = virtualAddress - PeImage.ImageBase;
        foreach (var section in PeImage.Sections)
        {
            var size = Math.Max(section.VirtualSize, section.RawSize);
            if (rva >= section.VirtualAddress && rva < section.VirtualAddress + size)
                return checked((long)section.RawPointer + rva - section.VirtualAddress);
        }
        throw new InvalidOperationException($"无法将虚拟地址 0x{virtualAddress:X8} 映射到当前 PE 文件偏移。");
    }

    public uint FileOffsetToVirtualAddress(long fileOffset)
    {
        if (fileOffset < 0) throw new ArgumentOutOfRangeException(nameof(fileOffset));
        foreach (var section in PeImage.Sections)
        {
            if (fileOffset >= section.RawPointer && fileOffset < (long)section.RawPointer + section.RawSize)
                return checked(PeImage.ImageBase + section.VirtualAddress + (uint)(fileOffset - section.RawPointer));
        }
        throw new InvalidOperationException($"无法将文件偏移 0x{fileOffset:X} 映射到当前 PE 虚拟地址。");
    }

    public ReadOnlyMemory<byte> ReadFileRange(long offset, int length)
    {
        if (offset < 0 || length < 0 || offset + length > Bytes.LongLength)
            throw new InvalidOperationException("EXE 读取范围越界。");
        return Bytes.AsMemory(checked((int)offset), length);
    }

    public ReadOnlyMemory<byte> ReadVirtualRange(uint address, int length)
        => ReadFileRange(VirtualAddressToFileOffset(address), length);
}

/// <summary>One immutable EXE read, hash, PE parse and instruction scan per resource fingerprint.</summary>
internal sealed class ExecutableAnalysisSnapshotCache
{
    public static ExecutableAnalysisSnapshotCache Shared { get; } = new();

    private readonly ConcurrentDictionary<ProjectResourceFingerprint, Lazy<Task<ExecutableAnalysisSnapshot>>> _entries = new();

    public async Task<ExecutableAnalysisSnapshot> GetBaseAsync(
        CczProject project,
        string targetFile = "Ekd5.exe",
        CancellationToken cancellationToken = default)
    {
        var path = project.ResolveGameFile(targetFile);
        var fingerprint = ProjectResourceFingerprint.Create(path, "executable-analysis-v1");
        var candidate = new Lazy<Task<ExecutableAnalysisSnapshot>>(
            () => Task.Run(() => Build(fingerprint), CancellationToken.None),
            LazyThreadSafetyMode.ExecutionAndPublication);
        var lazy = _entries.GetOrAdd(fingerprint, candidate);
        var miss = ReferenceEquals(lazy, candidate);
        try
        {
            var value = await lazy.Value.WaitAsync(cancellationToken).ConfigureAwait(false);
            PerformanceMetrics.Increment(miss ? "ExecutableAnalysis.CacheMisses" : "ExecutableAnalysis.CacheHits");
            TrimOlderVersions(fingerprint);
            return value;
        }
        catch
        {
            if (lazy.IsValueCreated && lazy.Value.IsFaulted)
                _entries.TryRemove(new KeyValuePair<ProjectResourceFingerprint, Lazy<Task<ExecutableAnalysisSnapshot>>>(fingerprint, lazy));
            throw;
        }
    }

    public ExecutableAnalysisSnapshot GetBase(CczProject project, string targetFile = "Ekd5.exe")
        => GetBaseAsync(project, targetFile).GetAwaiter().GetResult();

    public ExecutableAnalysisSnapshot GetBase(string path)
    {
        var fingerprint = ProjectResourceFingerprint.Create(path, "executable-analysis-v1");
        var candidate = new Lazy<Task<ExecutableAnalysisSnapshot>>(
            () => Task.Run(() => Build(fingerprint), CancellationToken.None),
            LazyThreadSafetyMode.ExecutionAndPublication);
        var lazy = _entries.GetOrAdd(fingerprint, candidate);
        var miss = ReferenceEquals(lazy, candidate);
        try
        {
            var value = lazy.Value.GetAwaiter().GetResult();
            PerformanceMetrics.Increment(miss ? "ExecutableAnalysis.CacheMisses" : "ExecutableAnalysis.CacheHits");
            TrimOlderVersions(fingerprint);
            return value;
        }
        catch
        {
            _entries.TryRemove(new KeyValuePair<ProjectResourceFingerprint, Lazy<Task<ExecutableAnalysisSnapshot>>>(fingerprint, lazy));
            throw;
        }
    }

    public void Invalidate(IEnumerable<string> paths)
    {
        var normalized = paths.Select(ProjectResourceFingerprint.Normalize).ToArray();
        foreach (var key in _entries.Keys.Where(key => normalized.Contains(key.Path, StringComparer.OrdinalIgnoreCase)).ToArray())
            _entries.TryRemove(key, out _);
    }

    public void Clear() => _entries.Clear();

    private void TrimOlderVersions(ProjectResourceFingerprint current)
    {
        var obsolete = _entries.Keys
            .Where(key => key.Path.Equals(current.Path, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(key => key.ChangeGeneration)
            .ThenByDescending(key => key.LastWriteTimeUtcTicks)
            .ThenByDescending(key => key.Length)
            .Skip(2)
            .ToArray();
        foreach (var key in obsolete) _entries.TryRemove(key, out _);
    }

    private static ExecutableAnalysisSnapshot Build(ProjectResourceFingerprint fingerprint)
    {
        using var operation = PerformanceMetrics.Begin("ExecutableAnalysis.Build");
        var bytes = File.ReadAllBytes(fingerprint.Path);
        PerformanceMetrics.Increment("ExecutableAnalysis.FullReadCount");
        PerformanceMetrics.Increment("ExecutableAnalysis.FullReadBytes", bytes.Length);
        var sha = Convert.ToHexString(SHA256.HashData(bytes));
        PerformanceMetrics.Increment("ExecutableAnalysis.HashCount");
        var pe = ExeCodeCaveScanner.ParsePe(bytes);
        PerformanceMetrics.Increment("ExecutableAnalysis.PeParseCount");
        var scan = new X86InstructionScanner().Scan(bytes, pe.ImageBase, pe.Sections);
        PerformanceMetrics.Increment("ExecutableAnalysis.InstructionScanCount");
        return new ExecutableAnalysisSnapshot(fingerprint, bytes, sha, pe, scan);
    }
}
