using System.Collections.Concurrent;
using System.Diagnostics;
using CCZModStudio.Models;

namespace CCZModStudio.Core;

/// <summary>
/// Process-wide complete effect analysis. All pages and MCP callers can await the
/// same Lazy&lt;Task&gt; and receive only a fully committed snapshot.
/// </summary>
public sealed class EffectAnalysisCoordinator
{
    public static EffectAnalysisCoordinator Shared { get; } = new();
    private readonly ConcurrentDictionary<string, Lazy<Task<EffectAnalysisSnapshot>>> _entries = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, long> _accessOrder = new(StringComparer.OrdinalIgnoreCase);
    private long _accessSequence;

    public EffectAnalysisSnapshot Scan(CczProject project, string targetFile = "Ekd5.exe")
        => ScanAsync(project, targetFile).GetAwaiter().GetResult();

    public async Task<EffectAnalysisSnapshot> ScanAsync(
        CczProject project,
        string targetFile = "Ekd5.exe",
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var path = project.ResolveGameFile(targetFile);
        var fingerprint = ProjectResourceFingerprint.Create(path, "effect-analysis-session-v1");
        var key = BuildSessionKey(project, fingerprint);
        var candidate = new Lazy<Task<EffectAnalysisSnapshot>>(
            () => BuildAsync(project, targetFile, fingerprint, progress), LazyThreadSafetyMode.ExecutionAndPublication);
        var lazy = _entries.GetOrAdd(key, candidate);
        _accessOrder[key] = Interlocked.Increment(ref _accessSequence);
        var miss = ReferenceEquals(lazy, candidate);
        try
        {
            var snapshot = await lazy.Value.WaitAsync(cancellationToken).ConfigureAwait(false);
            PerformanceMetrics.Increment(miss ? "EffectAnalysis.CacheMisses" : "EffectAnalysis.CacheHits");
            Trim(fingerprint);
            return snapshot;
        }
        catch
        {
            if (lazy.IsValueCreated && lazy.Value.IsFaulted)
            {
                _entries.TryRemove(new KeyValuePair<string, Lazy<Task<EffectAnalysisSnapshot>>>(key, lazy));
                _accessOrder.TryRemove(key, out _);
            }
            throw;
        }
    }

    public void Invalidate(IEnumerable<string> paths)
    {
        var normalized = paths.Select(ProjectResourceFingerprint.Normalize).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var key in _entries.Keys.Where(key => normalized.Any(path => key.StartsWith(path + "|", StringComparison.OrdinalIgnoreCase))).ToArray())
        {
            _entries.TryRemove(key, out _);
            _accessOrder.TryRemove(key, out _);
        }
    }

    public void ResetCachesForTests(CczProject project, string targetFile = "Ekd5.exe")
    {
        var path = project.ResolveGameFile(targetFile);
        _entries.Clear();
        _accessOrder.Clear();
        ExecutableAnalysisSnapshotCache.Shared.Clear();
        ExecutableProfileAuditService.Invalidate([path]);
        InjectedEffectDiscoveryService.Invalidate(path);
        EffectInventoryService.Invalidate(project);
        EffectIdLocationIndexService.Invalidate(project);
        CczEngineProfileService.ClearCache();
        EffectReleaseConsistencyService.Invalidate();
    }

    private static async Task<EffectAnalysisSnapshot> BuildAsync(
        CczProject project,
        string targetFile,
        ProjectResourceFingerprint fingerprint,
        IProgress<string>? progress)
    {
        var started = Stopwatch.GetTimestamp();
        var before = PerformanceMetrics.GetSnapshot();
        progress?.Report("分析 EXE");
        var executable = await ExecutableAnalysisSnapshotCache.Shared.GetBaseAsync(project, targetFile).ConfigureAwait(false);
        progress?.Report("读取表和特效目录");
        var auditTask = new ExecutableProfileAuditService().AuditAsync(project, targetFile);
        var inventoryTask = Task.Run(() => new EffectInventoryService().Scan(project, targetFile));
        await Task.WhenAll(auditTask, inventoryTask).ConfigureAwait(false);
        progress?.Report("构建位置索引");
        var locationTask = Task.Run(() => new EffectIdLocationIndexService().Scan(project, targetFile, exportReports: false));
        var mechanismTask = Task.Run(() => new EngineEffectMechanismService().Build(project, targetFile));
        var contractTask = Task.Run(() => new HookExecutionContractService().BuildContracts(project));
        await Task.WhenAll(locationTask, mechanismTask, contractTask).ConfigureAwait(false);
        progress?.Report("检查写入契约和发布组件");
        var moduleTask = Task.Run(() => new EffectModuleCatalogService().Build(project));
        var releaseTask = Task.Run(() => new EffectReleaseConsistencyService().Read());
        await Task.WhenAll(moduleTask, releaseTask).ConfigureAwait(false);
        var after = PerformanceMetrics.GetSnapshot();
        var counters = after.Counters.ToDictionary(pair => pair.Key,
            pair => pair.Value - before.Counters.GetValueOrDefault(pair.Key), StringComparer.Ordinal);
        return new EffectAnalysisSnapshot
        {
            AnalysisFingerprint = $"{fingerprint.Length}:{fingerprint.LastWriteTimeUtcTicks}:{fingerprint.ChangeGeneration}",
            TargetFilePath = fingerprint.Path,
            FullExeSha256 = executable.Sha256,
            ExeLength = executable.Bytes.LongLength,
            ImageBase = executable.PeImage.ImageBase,
            ExecutableBytes = executable.Bytes,
            CacheState = "CompleteSharedSnapshot",
            CompletedStages = ["快速指纹", "EXE 完整读取/SHA/PE/反汇编", "表与目录", "特效库存", "位置索引", "Hook 契约", "模块目录", "发布一致性"],
            ProfileAudit = await auditTask.ConfigureAwait(false),
            Inventory = await inventoryTask.ConfigureAwait(false),
            LocationIndex = await locationTask.ConfigureAwait(false),
            MechanismProfile = await mechanismTask.ConfigureAwait(false),
            HookContracts = (await contractTask.ConfigureAwait(false)).ToList(),
            ModuleCatalog = await moduleTask.ConfigureAwait(false),
            ReleaseConsistency = await releaseTask.ConfigureAwait(false),
            Performance = new EffectAnalysisPerformance
            {
                TotalMilliseconds = (Stopwatch.GetTimestamp() - started) * 1000d / Stopwatch.Frequency,
                CacheState = "Built",
                Counters = counters
            }
        };
    }

    private void Trim(ProjectResourceFingerprint current)
    {
        foreach (var key in _accessOrder.Where(pair => pair.Key.StartsWith(current.Path + "|", StringComparison.OrdinalIgnoreCase))
                     .OrderByDescending(pair => pair.Value).Skip(2).Select(pair => pair.Key).ToArray())
        {
            _entries.TryRemove(key, out _);
            _accessOrder.TryRemove(key, out _);
        }
    }

    private static string BuildSessionKey(CczProject project, ProjectResourceFingerprint executable)
    {
        var builder = new System.Text.StringBuilder().Append(executable.Path).Append('|')
            .Append(executable.Length).Append('|').Append(executable.LastWriteTimeUtcTicks).Append('|').Append(executable.ChangeGeneration);
        foreach (var path in new[]
                 {
                     project.ResolveGameFile("Data.e5"), project.ResolveGameFile("Star.e5"), project.ResolveGameFile("Imsg.e5"),
                     project.HexTableXmlPath
                 }) AddFile(builder, path);
        foreach (var root in new[]
                 {
                     ProjectPatchIdentityService.EffectManifestRoot(project), ProjectPatchIdentityService.CompositeManifestRoot(project),
                     ProjectPatchIdentityService.ModularManifestRoot(project), ProjectPatchIdentityService.DispatcherManifestRoot(project),
                      Path.Combine(project.GameRoot, "CCZModStudio_Notes", "EffectContractEvidence"),
                      Path.Combine(project.GameRoot, "CCZModStudio_Notes", "EffectContractEvidenceV2"),
                      Path.Combine(project.GameRoot, "CCZModStudio_Notes", "EffectContractEvidenceV3")
                  }) AddDirectory(builder, root);
        return builder.ToString();
    }

    private static void AddFile(System.Text.StringBuilder builder, string path)
    {
        var fingerprint = ProjectResourceFingerprint.Create(path);
        builder.Append('|').Append(fingerprint.Path).Append(':').Append(fingerprint.Length).Append(':')
            .Append(fingerprint.LastWriteTimeUtcTicks).Append(':').Append(fingerprint.ChangeGeneration);
    }

    private static void AddDirectory(System.Text.StringBuilder builder, string root)
    {
        builder.Append('|').Append(ProjectResourceFingerprint.Normalize(root));
        if (!Directory.Exists(root)) { builder.Append(":missing"); return; }
        foreach (var path in Directory.EnumerateFiles(root, "*.json", SearchOption.AllDirectories).OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
            AddFile(builder, path);
    }
}
