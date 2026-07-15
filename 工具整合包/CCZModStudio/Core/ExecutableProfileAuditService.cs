using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using CCZModStudio.Models;

namespace CCZModStudio.Core;

public sealed class ExecutableProfileAuditService
{
    private static readonly ConcurrentDictionary<ProjectResourceFingerprint, Lazy<Task<ExecutableProfileAuditResult>>> Cache = new();

    public ExecutableProfileAuditResult Audit(CczProject project, string targetFile = "Ekd5.exe")
        => AuditAsync(project, targetFile).GetAwaiter().GetResult();

    public async Task<ExecutableProfileAuditResult> AuditAsync(CczProject project, string targetFile = "Ekd5.exe", CancellationToken cancellationToken = default)
    {
        var path = project.ResolveGameFile(targetFile);
        if (!File.Exists(path)) return Missing(path);
        var fingerprint = ProjectResourceFingerprint.Create(path, "effect-profile-audit-v1");
        var candidate = new Lazy<Task<ExecutableProfileAuditResult>>(
            async () => AuditSnapshot(await ExecutableAnalysisSnapshotCache.Shared.GetBaseAsync(project, targetFile).ConfigureAwait(false)),
            LazyThreadSafetyMode.ExecutionAndPublication);
        var lazy = Cache.GetOrAdd(fingerprint, candidate);
        var miss = ReferenceEquals(lazy, candidate);
        try
        {
            var result = await lazy.Value.WaitAsync(cancellationToken).ConfigureAwait(false);
            PerformanceMetrics.Increment(miss ? "ExecutableProfileAudit.CacheMisses" : "ExecutableProfileAudit.CacheHits");
            Trim(path);
            return result;
        }
        catch
        {
            Cache.TryRemove(new KeyValuePair<ProjectResourceFingerprint, Lazy<Task<ExecutableProfileAuditResult>>>(fingerprint, lazy));
            throw;
        }
    }

    public ExecutableProfileAuditResult AuditFresh(string path)
    {
        var bytes = File.ReadAllBytes(path);
        PerformanceMetrics.Increment("ExecutableProfileAudit.FreshReadCount");
        return AuditBytes(bytes, Convert.ToHexString(SHA256.HashData(bytes)));
    }

    internal ExecutableProfileAuditResult AuditSnapshot(ExecutableAnalysisSnapshot snapshot)
        => AuditBytes(snapshot.Bytes, snapshot.Sha256, snapshot.PeImage);

    internal ExecutableProfileAuditResult AuditBytes(byte[] bytes, string? fullSha = null, ExeCodeCaveScanner.PeImage? parsedPe = null)
    {
        using var operation = PerformanceMetrics.Begin("ExecutableProfileAudit.Build");
        fullSha ??= Convert.ToHexString(SHA256.HashData(bytes));
        var profile = EngineEffectProfileRegistry.Current65;
        var result = new ExecutableProfileAuditResult
        {
            ProfileId = profile.ProfileId,
            EngineVersion = profile.EngineVersion,
            CanonicalSha256 = profile.CanonicalSha256,
            CurrentSha256 = fullSha
        };
        if (bytes.LongLength != profile.FileLength)
        {
            Block(result, "EXE_LENGTH_MISMATCH", "文件长度与 6.5 特效档案不一致。");
            return Finish(result);
        }
        ExeCodeCaveScanner.PeImage pe;
        try { pe = parsedPe ?? ExeCodeCaveScanner.ParsePe(bytes); }
        catch (Exception ex) { Block(result, "PE_LAYOUT_INVALID", "PE 布局无法验证：" + ex.Message); return Finish(result); }
        if (pe.ImageBase != profile.ImageBase)
        {
            Block(result, "IMAGE_BASE_MISMATCH", "ImageBase 与 6.5 档案不一致。");
            return Finish(result);
        }

        var normalized = (byte[])bytes.Clone();
        foreach (var field in profile.RegisteredFields)
        {
            var canonical = EffectPatchByteService.ParseHex(field.CanonicalValueHex);
            if (canonical.Length != field.Width || field.FileOffset < 0 || field.FileOffset + field.Width > normalized.LongLength)
            {
                Block(result, "PROFILE_FIELD_INVALID", "档案中的登记字段定义无效：" + field.FieldId);
                return Finish(result);
            }
            var current = bytes.AsSpan(checked((int)field.FileOffset), field.Width).ToArray();
            if (!current.SequenceEqual(canonical))
            {
                var allowed = IsAllowed(field, current);
                result.RegisteredDifferences.Add(new ExecutableProfileDifference
                {
                    FileOffset = field.FileOffset,
                    VirtualAddress = field.VirtualAddress,
                    Width = field.Width,
                    OldBytesHex = field.CanonicalValueHex,
                    NewBytesHex = Convert.ToHexString(current),
                    FieldId = field.FieldId,
                    MeaningZh = field.DisplayNameZh,
                    IsRegisteredField = true,
                    ValueAllowed = allowed
                });
                if (!allowed) Block(result, "REGISTERED_VALUE_INVALID", $"登记字段“{field.DisplayNameZh}”的新值不在允许范围内。");
            }
            canonical.CopyTo(normalized, checked((int)field.FileOffset));
        }

        result.NormalizedProfileIdentity = Convert.ToHexString(SHA256.HashData(normalized));
        result.ChangedByteCount = result.RegisteredDifferences.Sum(item => CountDifferentBytes(item.OldBytesHex, item.NewBytesHex));
        result.ChangedRangeCount = result.RegisteredDifferences.Count;
        if (!result.NormalizedProfileIdentity.Equals(profile.NormalizedIdentitySha256, StringComparison.OrdinalIgnoreCase))
        {
            result.UnknownDifferences.Add(new ExecutableProfileDifference
            {
                FileOffset = -1,
                Width = 0,
                MeaningZh = "存在登记字段以外的未知变化；规范化摘要不匹配。",
                IsRegisteredField = false,
                ValueAllowed = false
            });
            Block(result, "UNKNOWN_DIFFERENCE", "即使未知变化只有 1 字节，也不能按差异数量自动信任。");
        }
        if (result.ChangedByteCount > profile.MaximumChangedBytes)
            Block(result, "DIFFERENCE_BYTES_LIMIT", $"登记字段变化超过 {profile.MaximumChangedBytes} 字节上限。");
        if (result.ChangedRangeCount > profile.MaximumChangedRanges)
            Block(result, "DIFFERENCE_RANGES_LIMIT", $"登记字段变化超过 {profile.MaximumChangedRanges} 个范围上限。");

        if (result.BlockerCodes.Count == 0)
        {
            var exact = fullSha.Equals(profile.CanonicalSha256, StringComparison.OrdinalIgnoreCase);
            result.TrustStatus = exact ? ExecutableProfileTrustStatus.ExactCanonical : ExecutableProfileTrustStatus.AutoDerivedDataOnly;
            result.CanWriteRegisteredData = true;
            result.CanReuseCodeContracts = true;
            result.ReasonsZh.Add(exact
                ? "完整 SHA 与规范 6.5 特效基底一致。"
                : "全部差异仅位于已登记定宽数据字段，相关机器码身份保持不变。");
        }
        return Finish(result);
    }

    public ExecutableProfileAuditResult AuditBytesForTest(byte[] bytes)
        => AuditBytes(bytes);

    public static void Invalidate(IEnumerable<string> paths)
    {
        var normalized = paths.Select(ProjectResourceFingerprint.Normalize).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var key in Cache.Keys.Where(key => normalized.Contains(key.Path)).ToArray()) Cache.TryRemove(key, out _);
    }

    private static bool IsAllowed(ExecutableRegisteredField field, byte[] valueBytes)
    {
        var hex = Convert.ToHexString(valueBytes);
        if (field.AllowedValuesHex.Count > 0 && !field.AllowedValuesHex.Contains(hex, StringComparer.OrdinalIgnoreCase)) return false;
        ulong value = 0;
        for (var index = 0; index < valueBytes.Length; index++) value |= (ulong)valueBytes[index] << (index * 8);
        if (field.AllowedBitMask.HasValue && (value & ~field.AllowedBitMask.Value) != 0) return false;
        if (field.Minimum.HasValue && value < (ulong)field.Minimum.Value) return false;
        if (field.Maximum.HasValue && value > (ulong)field.Maximum.Value) return false;
        return true;
    }

    private static int CountDifferentBytes(string oldHex, string newHex)
    {
        var oldBytes = EffectPatchByteService.ParseHex(oldHex);
        var newBytes = EffectPatchByteService.ParseHex(newHex);
        return oldBytes.Zip(newBytes).Count(pair => pair.First != pair.Second) + Math.Abs(oldBytes.Length - newBytes.Length);
    }

    private static void Block(ExecutableProfileAuditResult result, string code, string reason)
    {
        if (!result.BlockerCodes.Contains(code, StringComparer.Ordinal)) result.BlockerCodes.Add(code);
        if (!result.ReasonsZh.Contains(reason, StringComparer.Ordinal)) result.ReasonsZh.Add(reason);
        result.TrustStatus = ExecutableProfileTrustStatus.ReadOnlyUnknownDifference;
    }

    private static ExecutableProfileAuditResult Finish(ExecutableProfileAuditResult result)
    {
        var text = string.Join("|", result.ProfileId, result.CanonicalSha256, result.CurrentSha256,
            result.NormalizedProfileIdentity, result.TrustStatus,
            string.Join(";", result.RegisteredDifferences.Select(item => $"{item.FileOffset:X}:{item.NewBytesHex}:{item.ValueAllowed}")),
            string.Join(";", result.BlockerCodes));
        result.AuditSummaryHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
        result.SummaryZh = result.CanWriteRegisteredData
            ? (result.TrustStatus == ExecutableProfileTrustStatus.ExactCanonical ? "规范 6.5 特效档案，可写。" : "已登记数据型派生版本；只复用代码身份，不信任未知机器码变化。")
            : "当前 EXE 只读：" + string.Join("；", result.ReasonsZh.Take(6));
        return result;
    }

    private static ExecutableProfileAuditResult Missing(string path)
        => Finish(new ExecutableProfileAuditResult
        {
            TrustStatus = ExecutableProfileTrustStatus.MissingExecutable,
            BlockerCodes = ["EXE_MISSING"],
            ReasonsZh = ["找不到目标 EXE：" + path]
        });

    private static void Trim(string path)
    {
        var keys = Cache.Keys.Where(key => key.Path.Equals(ProjectResourceFingerprint.Normalize(path), StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(key => key.ChangeGeneration).ThenByDescending(key => key.LastWriteTimeUtcTicks).Skip(2).ToArray();
        foreach (var key in keys) Cache.TryRemove(key, out _);
    }
}
