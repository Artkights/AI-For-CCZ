using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using System.Collections.Concurrent;
using CCZModStudio.Models;

namespace CCZModStudio.Core;

public sealed class EffectReleaseConsistencyService
{
    public const string ManifestFileName = "effect-release-manifest.json";
    private static readonly string[] RequiredComponents = ["desktop-core", "runtime", "mcp", "gamedebug"];
    private static readonly ConcurrentDictionary<string, Lazy<EffectReleaseConsistencyReport>> Cache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly ConcurrentDictionary<string, long> CacheAccessOrder = new(StringComparer.OrdinalIgnoreCase);
    private static long _cacheAccessSequence;

    public EffectReleaseConsistencyReport Read(string? startDirectory = null, bool forceRefresh = false)
    {
        var manifestPath = FindManifest(startDirectory);
        var key = BuildCacheKey(manifestPath);
        if (forceRefresh)
        {
            Cache.TryRemove(key, out _);
            CacheAccessOrder.TryRemove(key, out _);
            PerformanceMetrics.Increment("EffectReleaseConsistency.ForcedRefreshes");
        }
        var candidate = new Lazy<EffectReleaseConsistencyReport>(
            () => ReadCore(startDirectory, manifestPath), LazyThreadSafetyMode.ExecutionAndPublication);
        var lazy = Cache.GetOrAdd(key, candidate);
        CacheAccessOrder[key] = Interlocked.Increment(ref _cacheAccessSequence);
        var miss = ReferenceEquals(lazy, candidate);
        try
        {
            var report = lazy.Value;
            PerformanceMetrics.Increment(miss ? "EffectReleaseConsistency.CacheMisses" : "EffectReleaseConsistency.CacheHits");
            Trim(manifestPath == null ? "development-no-manifest|" + AppContext.BaseDirectory : Path.GetFullPath(manifestPath) + "|");
            return report;
        }
        catch
        {
            Cache.TryRemove(new KeyValuePair<string, Lazy<EffectReleaseConsistencyReport>>(key, lazy));
            CacheAccessOrder.TryRemove(key, out _);
            throw;
        }
    }

    private static EffectReleaseConsistencyReport ReadCore(string? startDirectory, string? manifestPath)
    {
        var report = new EffectReleaseConsistencyReport
        {
            SchemaVersion = EffectCapabilityVersion.SchemaVersion,
            BuildChannel = EffectCapabilityVersion.BuildChannel,
            BuildIdentity = EffectCapabilityVersion.BuildIdentity
        };
        if (manifestPath == null)
        {
            report.IsConsistent = true;
            report.StatusZh = "开发构建";
            report.ReasonZh = "未检测到正式特效发布清单；仍执行 EXE SHA、旧字节锁和一次性凭据校验。";
            return report;
        }

        report.HasReleaseManifest = true;
        report.ManifestPath = manifestPath;
        report.ReleaseRoot = Path.GetDirectoryName(manifestPath)!;
        EffectReleaseManifest? manifest;
        try
        {
            manifest = JsonSerializer.Deserialize<EffectReleaseManifest>(File.ReadAllText(manifestPath));
        }
        catch (Exception ex)
        {
            report.WarningsZh.Add("特效发布清单无法读取：" + ex.Message);
            return Finish(report);
        }
        if (manifest == null)
        {
            report.WarningsZh.Add("特效发布清单内容为空。");
            return Finish(report);
        }
        report.Components = manifest.Components.ToList();
        if (manifest.SchemaVersion != "effect-release-manifest-v1") report.WarningsZh.Add("特效发布清单 schema 不受支持。");
        if (manifest.EffectCapabilitySchemaVersion != EffectCapabilityVersion.SchemaVersion)
            report.WarningsZh.Add("桌面/Core 与发布清单的特效能力 schema 不一致。");
        if (manifest.BuildChannel != EffectCapabilityVersion.BuildChannel)
            report.WarningsZh.Add("桌面/Core 与发布清单的构建通道不一致。");
        if (manifest.BuildIdentity != EffectCapabilityVersion.BuildIdentity)
            report.WarningsZh.Add("桌面/Core 与发布清单的构建标识不一致。");
        foreach (var required in RequiredComponents.Where(id => manifest.Components.All(item => !item.ComponentId.Equals(id, StringComparison.OrdinalIgnoreCase))))
            report.WarningsZh.Add("发布清单缺少组件：" + required + "。");
        foreach (var duplicate in manifest.Components.GroupBy(item => item.ComponentId, StringComparer.OrdinalIgnoreCase).Where(group => group.Count() > 1))
            report.WarningsZh.Add("发布清单包含重复组件：" + duplicate.Key + "。");

        var rootPrefix = report.ReleaseRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        foreach (var component in manifest.Components)
        {
            try
            {
                var path = Path.GetFullPath(Path.Combine(report.ReleaseRoot, component.RelativePath));
                if (!path.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase) || !File.Exists(path))
                {
                    report.WarningsZh.Add(component.DisplayNameZh + "文件缺失或越出发布目录。");
                    continue;
                }
                var info = new FileInfo(path);
                var hash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
                var version = FileVersionInfo.GetVersionInfo(path);
                if (info.Length != component.Length || !hash.Equals(component.Sha256, StringComparison.OrdinalIgnoreCase))
                    report.WarningsZh.Add(component.DisplayNameZh + "文件摘要与发布清单不一致。");
                if (!NormalizeVersion(version.FileVersion).Equals(NormalizeVersion(component.FileVersion), StringComparison.OrdinalIgnoreCase))
                    report.WarningsZh.Add(component.DisplayNameZh + "文件版本与发布清单不一致。");
                if (!string.Equals(version.ProductVersion, component.BuildIdentity, StringComparison.Ordinal) ||
                    !string.Equals(component.BuildIdentity, manifest.BuildIdentity, StringComparison.Ordinal))
                    report.WarningsZh.Add(component.DisplayNameZh + "构建标识与同批发布组件不一致。");
            }
            catch (Exception ex)
            {
                report.WarningsZh.Add(component.DisplayNameZh + "完整性校验失败：" + ex.Message);
            }
        }
        return Finish(report);
    }

    public void EnsureWriteAllowed(string? startDirectory = null, bool forceRefresh = false)
    {
        var report = Read(startDirectory, forceRefresh);
        if (!report.CanWrite)
            throw new InvalidOperationException("特效组件发布不一致，已禁止写入：" + report.ReasonZh);
    }

    public static void Invalidate()
    {
        Cache.Clear();
        CacheAccessOrder.Clear();
    }

    private static void Trim(string prefix)
    {
        foreach (var key in CacheAccessOrder.Where(pair => pair.Key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                     .OrderByDescending(pair => pair.Value).Skip(2).Select(pair => pair.Key).ToArray())
        {
            Cache.TryRemove(key, out _);
            CacheAccessOrder.TryRemove(key, out _);
        }
    }

    private static string BuildCacheKey(string? manifestPath)
    {
        if (manifestPath == null) return "development-no-manifest|" + AppContext.BaseDirectory;
        var builder = new System.Text.StringBuilder();
        AddIdentity(builder, manifestPath);
        try
        {
            var manifest = JsonSerializer.Deserialize<EffectReleaseManifest>(File.ReadAllText(manifestPath));
            var root = Path.GetDirectoryName(manifestPath)!;
            if (manifest != null)
                foreach (var component in manifest.Components.OrderBy(item => item.ComponentId, StringComparer.OrdinalIgnoreCase))
                    AddIdentity(builder, Path.Combine(root, component.RelativePath));
        }
        catch { }
        return builder.ToString();
    }

    private static void AddIdentity(System.Text.StringBuilder builder, string path)
    {
        var info = new FileInfo(path);
        builder.Append(Path.GetFullPath(path)).Append('|').Append(info.Exists ? info.Length : -1).Append('|')
            .Append(info.Exists ? info.LastWriteTimeUtc.Ticks : 0).Append(';');
    }

    private static EffectReleaseConsistencyReport Finish(EffectReleaseConsistencyReport report)
    {
        report.IsConsistent = report.WarningsZh.Count == 0;
        report.StatusZh = report.IsConsistent ? "组件一致" : "组件不一致";
        report.ReasonZh = report.IsConsistent
            ? "桌面/Core、Runtime、MCP 与 GameDebug 来自同一完整发布。"
            : string.Join("；", report.WarningsZh.Take(8));
        return report;
    }

    private static string? FindManifest(string? startDirectory)
    {
        var configured = Environment.GetEnvironmentVariable("CCZ_EFFECT_RELEASE_MANIFEST");
        if (!string.IsNullOrWhiteSpace(configured) && File.Exists(configured)) return Path.GetFullPath(configured);
        var current = new DirectoryInfo(Path.GetFullPath(startDirectory ?? AppContext.BaseDirectory));
        for (var depth = 0; current != null && depth < 5; depth++, current = current.Parent)
        {
            var candidate = Path.Combine(current.FullName, ManifestFileName);
            if (File.Exists(candidate)) return candidate;
        }
        return null;
    }

    private static string NormalizeVersion(string? version)
        => (version ?? string.Empty).Trim().TrimEnd('0').TrimEnd('.');
}
