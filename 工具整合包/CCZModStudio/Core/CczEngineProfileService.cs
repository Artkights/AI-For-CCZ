using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Collections.Concurrent;
using System.Text.Json;
using CCZModStudio.Formats;
using CCZModStudio.Models;

namespace CCZModStudio.Core;

public sealed class CczEngineProfileService
{
    public const long Version65ExeSize = 1_196_032;
    public const long Version66ExeSize = 1_130_496;
    private const string Known66Sha256 = "4A4FD8DDBF83E5F0B769D1B97BF8F6E6431C3AB42892024A354228212D3D06A4";
    private const string ProfileCacheVersion = "engine-profile-v2";
    private static readonly ConcurrentDictionary<ProjectResourceFingerprint, Lazy<string>> ProfileCache = new();
    private static readonly ConcurrentDictionary<ProjectResourceFingerprint, CczEngineDetectionEvidence[]> CmfEvidenceCache = new();
    private static readonly JsonSerializerOptions ProfileJsonOptions = new() { IncludeFields = true };

    public CczEngineProfile Detect(CczProject project)
    {
        var exePath = project.ResolveGameFile("Ekd5.exe");
        var fingerprint = ProjectResourceFingerprint.Create(exePath, ProfileCacheVersion);
        PruneStaleProfiles(fingerprint);
        var lazy = ProfileCache.GetOrAdd(fingerprint, _ => new Lazy<string>(
            () => JsonSerializer.Serialize(BuildProjectProfile(project, exePath), ProfileJsonOptions),
            LazyThreadSafetyMode.ExecutionAndPublication));
        try
        {
            var created = !lazy.IsValueCreated;
            var json = lazy.Value;
            PerformanceMetrics.Increment(created ? "EngineProfile.CacheMisses" : "EngineProfile.CacheHits");
            return JsonSerializer.Deserialize<CczEngineProfile>(json, ProfileJsonOptions)
                   ?? throw new InvalidOperationException("引擎版本检测缓存无法还原。");
        }
        catch
        {
            ProfileCache.TryRemove(new KeyValuePair<ProjectResourceFingerprint, Lazy<string>>(fingerprint, lazy));
            throw;
        }
    }

    public static void Invalidate(string exePath)
    {
        var normalized = ProjectResourceFingerprint.Normalize(exePath);
        ProjectResourceFingerprint.Invalidate(normalized);
        foreach (var key in ProfileCache.Keys.Where(key => key.Path.Equals(normalized, StringComparison.OrdinalIgnoreCase)))
            ProfileCache.TryRemove(key, out _);
        PerformanceMetrics.Increment("EngineProfile.Invalidations");
    }

    public static void ClearCache()
    {
        ProfileCache.Clear();
        CmfEvidenceCache.Clear();
    }

    private static CczEngineProfile BuildProjectProfile(CczProject project, string exePath)
    {
        using var operation = PerformanceMetrics.Begin("EngineProfile.Build");
        var exeSize = TryGetLength(exePath);
        var versionInfo = TryReadVersionInfo(exePath);
        var pathHint = Extract6xVersionHint(project.GameRoot) ?? Extract6xVersionHint(project.WorkspaceRoot);
        var sha256 = TryComputeSha256(exePath);
        var profile = BuildProfile(exeSize, versionInfo.Text, versionInfo.LowWord, pathHint, sha256);

        profile.ExePath = File.Exists(exePath) ? exePath : string.Empty;
        profile.ExeSize = exeSize;
        profile.ExeSha256 = sha256 ?? string.Empty;
        profile.VersionResourceText = versionInfo.Text ?? string.Empty;
        profile.VersionResourceLowWord = versionInfo.LowWord;
        profile.PathHint = pathHint ?? string.Empty;
        profile.DataSize = TryGetLength(project.ResolveGameFile("Data.e5"));
        profile.ImsgSize = TryGetLength(project.ResolveGameFile("Imsg.e5"));
        profile.StarSize = TryGetLength(project.ResolveGameFile("Star.e5"));
        profile.ItemSize = TryGetLength(project.ResolveGameFile("Item.e5"));
        AddCmfEvidenceIfAvailable(project, profile);

        if (!profile.IsKnown)
        {
            profile.Warnings.Add("未能可靠识别 6.x 引擎版本；表读取将使用语义匹配和内置 6.5 兜底，写入前必须复核目标表定义。");
        }
        else if (profile.VersionHint == "6.4" && profile.TableVersionPrefix == "6.5")
        {
            profile.Warnings.Add("旧 6.4 运行时可识别，但当前工具包没有独立 6.4 离线 HexTable；Data/物品/特效表读取会使用 6.5 表语义兜底，写入前必须另行确认。");
        }

        if (profile.VersionHint == "6.5" && profile.ExeSize is not null and not Version65ExeSize)
        {
            profile.Warnings.Add($"路径或版本提示为 6.5，但 Ekd5.exe 大小为 {profile.ExeSize.Value.ToString(CultureInfo.InvariantCulture)}，与参考大小 {Version65ExeSize} 不一致。");
        }

        if (profile.VersionHint == "6.6" && profile.ExeSize is not null and not Version66ExeSize)
        {
            profile.Warnings.Add($"路径或版本提示为 6.6，但 Ekd5.exe 大小为 {profile.ExeSize.Value.ToString(CultureInfo.InvariantCulture)}，与参考大小 {Version66ExeSize} 不一致。");
        }

        if (LegacyHmMapReader.HasLegacyHmLayout(project))
        {
            profile.Warnings.Add("检测到旧式 Hm 战场地图布局：项目无 Map 目录，存在 HmNN.e5、Hexzmap.e5、Spalet.e5。战场底图将以只读 LegacyHmRaw 方式索引和预览，暂不开放重封包写入。");
        }

        return profile;
    }

    private static void PruneStaleProfiles(ProjectResourceFingerprint current)
    {
        var stale = ProfileCache.Keys
            .Where(key => key.Path.Equals(current.Path, StringComparison.OrdinalIgnoreCase) && key != current)
            .OrderByDescending(key => key.LastWriteTimeUtcTicks)
            .Skip(1)
            .ToArray();
        foreach (var key in stale) ProfileCache.TryRemove(key, out _);
    }

    private static void AddCmfEvidenceIfAvailable(CczProject project, CczEngineProfile profile)
    {
        if (!Ccz66RevisedLayout.Is66(profile)) return;
        var key = string.IsNullOrWhiteSpace(project.WorkspaceRoot)
            ? project.GameRoot
            : project.WorkspaceRoot;
        if (string.IsNullOrWhiteSpace(key) || !Directory.Exists(key)) return;

        var sample = CheatMakerCmfProbe.FindDefaultStar66XSample(key);
        if (string.IsNullOrWhiteSpace(sample)) return;
        var fingerprint = ProjectResourceFingerprint.Create(sample, "cmf-evidence-v2");
        var evidence = CmfEvidenceCache.GetOrAdd(fingerprint, _ => BuildCmfEvidence(key));
        profile.DetectionEvidence.AddRange(evidence);
    }

    private static CczEngineDetectionEvidence[] BuildCmfEvidence(string workspaceRoot)
    {
        try
        {
            var sample = CheatMakerCmfProbe.FindDefaultStar66XSample(workspaceRoot);
            if (string.IsNullOrWhiteSpace(sample)) return Array.Empty<CczEngineDetectionEvidence>();

            var baseline = CheatMakerCmfProbe.FindDefaultStar66KBaseline(workspaceRoot);
            var probe = new CheatMakerCmfProbe().Probe(sample, baseline, "6.6X CheatMaker CMF evidence sample");
            if (!probe.Exists) return Array.Empty<CczEngineDetectionEvidence>();

            return new[]
            {
                new CczEngineDetectionEvidence
                {
                    Kind = "CheatMaker CMF evidence",
                    Value = $"{probe.Sha256}; length={probe.Length.ToString(CultureInfo.InvariantCulture)}; utf16Crlf={probe.Utf16CrlfCount.ToString(CultureInfo.InvariantCulture)}",
                    VersionHint = "6.6",
                    Priority = 90,
                    Note = "Evidence-only 6.6X modifier project sample; not used as runtime engine identity or writable offset source."
                }
            };
        }
        catch
        {
            return Array.Empty<CczEngineDetectionEvidence>();
        }
    }

    public static string? MapOldWrenchLowWordToVersion(int? lowWord)
        => lowWord switch
        {
            1 => "6.1",
            2 => "6.2",
            3 => "6.3",
            4 => "6.4",
            5 => "6.5",
            6 => "6.6",
            _ => null
        };

    public static string? InferVersionFromExeSize(long? exeSize)
        => exeSize switch
        {
            Version66ExeSize => "6.6",
            Version65ExeSize => "6.5",
            _ => null
        };

    public static string? InferVersionFromSha256(string? sha256)
        => sha256?.ToUpperInvariant() switch
        {
            EngineEffectProfileRegistry.Canonical65Sha256 => "6.5",
            EngineEffectProfileRegistry.Known65VariantSha256 => "6.5",
            Known66Sha256 => "6.6",
            _ => null
        };

    public static CczEngineProfile BuildProfileForTest(long? exeSize, string? versionText, int? versionLowWord, string? pathHint, string? sha256 = null)
        => BuildProfile(exeSize, versionText, versionLowWord, pathHint, sha256);

    private static CczEngineProfile BuildProfile(long? exeSize, string? versionText, int? versionLowWord, string? pathHint, string? sha256)
    {
        var evidence = BuildDetectionEvidence(exeSize, versionText, versionLowWord, pathHint, sha256);
        var selected = evidence
            .Where(item => !string.IsNullOrWhiteSpace(item.VersionHint))
            .OrderBy(item => item.Priority)
            .FirstOrDefault();
        var versionHint = selected?.VersionHint;
        var source = selected?.Kind ?? string.Empty;

        if (versionHint is "6.1" or "6.2" or "6.3" or "6.4" or "6.5" or "6.6")
        {
            var known = CreateKnown(versionHint, source);
            known.DetectionEvidence = evidence;
            AddConflictWarnings(known, evidence);
            return known;
        }

        var profile = CreateKnown("6.5", string.IsNullOrWhiteSpace(source) ? "fallback" : source);
        profile.EngineKey = "unknown";
        profile.DisplayName = "未知 6.x 引擎（6.5 表兜底）";
        profile.VersionHint = versionHint ?? "unknown";
        profile.IsKnown = false;
        profile.DetectionSource = string.IsNullOrWhiteSpace(source) ? "fallback" : source;
        profile.LegacyRuntimeLayout = null;
        profile.DetectionEvidence = evidence;
        return profile;
    }

    private static List<CczEngineDetectionEvidence> BuildDetectionEvidence(long? exeSize, string? versionText, int? versionLowWord, string? pathHint, string? sha256)
    {
        var evidence = new List<CczEngineDetectionEvidence>();

        var shaVersion = InferVersionFromSha256(sha256);
        if (!string.IsNullOrWhiteSpace(sha256))
        {
            evidence.Add(new CczEngineDetectionEvidence
            {
                Kind = "Ekd5.exe SHA256",
                Value = sha256,
                VersionHint = shaVersion ?? string.Empty,
                Priority = 5,
                Note = shaVersion is null ? "未命中内置 SHA 样本。" : $"命中内置 {shaVersion} Ekd5.exe 样本。"
            });
        }

        var sizeVersion = InferVersionFromExeSize(exeSize);
        if (exeSize is not null)
        {
            evidence.Add(new CczEngineDetectionEvidence
            {
                Kind = "Ekd5.exe size",
                Value = exeSize.Value.ToString(CultureInfo.InvariantCulture),
                VersionHint = sizeVersion ?? string.Empty,
                Priority = 30,
                Note = sizeVersion is null ? "未命中内置 EXE 大小样本。" : "EXE 大小命中内置版本样本。"
            });
        }

        var textVersion = NormalizeVersionHint(versionText);
        if (!string.IsNullOrWhiteSpace(versionText))
        {
            evidence.Add(new CczEngineDetectionEvidence
            {
                Kind = "Ekd5.exe version resource",
                Value = versionText,
                VersionHint = textVersion ?? string.Empty,
                Priority = 20,
                Note = textVersion is null ? "版本资源没有可解析的 6.x 文本。" : "版本资源含明确 6.x 文本。"
            });
        }

        var lowWordVersion = MapOldWrenchLowWordToVersion(versionLowWord);
        if (versionLowWord is not null)
        {
            evidence.Add(new CczEngineDetectionEvidence
            {
                Kind = "old-wrench FileVersionLS low word",
                Value = versionLowWord.Value.ToString(CultureInfo.InvariantCulture),
                VersionHint = lowWordVersion ?? string.Empty,
                Priority = 8,
                Note = lowWordVersion is null
                    ? "版本资源低位值不在旧扳手 6.1-6.6 映射范围内。"
                    : "按旧扳手自动识别规则映射 6.1-6.6。"
            });
        }

        var pathVersion = NormalizeVersionHint(pathHint);
        if (!string.IsNullOrWhiteSpace(pathHint))
        {
            evidence.Add(new CczEngineDetectionEvidence
            {
                Kind = "path hint",
                Value = pathHint,
                VersionHint = pathVersion ?? string.Empty,
                Priority = 40,
                Note = pathVersion is null ? "路径提示没有可解析的 6.x 文本。" : "从路径名提取的版本提示。"
            });
        }

        return evidence;
    }

    private static void AddConflictWarnings(CczEngineProfile profile, IReadOnlyList<CczEngineDetectionEvidence> evidence)
    {
        var conflicting = evidence
            .Where(item => !string.IsNullOrWhiteSpace(item.VersionHint))
            .Where(item => !item.VersionHint.Equals(profile.VersionHint, StringComparison.OrdinalIgnoreCase))
            .Select(item => $"{item.Kind}={item.Value}->{item.VersionHint}")
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (conflicting.Count > 0)
        {
            profile.Warnings.Add($"版本识别证据存在冲突，已按优先级选择 {profile.VersionHint}（{profile.DetectionSource}）；冲突证据：{string.Join("；", conflicting)}。");
        }
    }

    private static CczEngineProfile CreateKnown(string version, string source)
    {
        var tableVersionPrefix = ResolveTableVersionPrefix(version);
        var profile = new CczEngineProfile
        {
            EngineKey = version.Replace(".", string.Empty, StringComparison.Ordinal),
            DisplayName = $"曹操传加强版 {version}",
            VersionHint = version,
            TableVersionPrefix = tableVersionPrefix,
            IsKnown = true,
            DetectionSource = string.IsNullOrWhiteSpace(source) ? "known default" : source,
            TableHints = BuildTableHints(version)
        };

        profile.LegacyRuntimeLayout = version switch
        {
            "6.1" => BuildLegacyRuntimeLayout(version, merit: 0x508400, talent: 0x5089B0, exclusive: 0x50E800, war: 0x4B2C50, warLen: 0x24, revive: 0x40930F, bisha: 0x508800, itemCapacity: 154),
            "6.2" => BuildLegacyRuntimeLayout(version, merit: 0x508400, talent: 0x5089B0, exclusive: 0x50E800, war: 0x4B2C50, warLen: 0x24, revive: 0x4092E0, bisha: 0x508800, itemCapacity: 154),
            "6.3" => BuildLegacyRuntimeLayout(version, merit: 0x508400, talent: 0x5089B0, exclusive: 0x50E400, war: 0x4B2C50, warLen: 0x24, revive: 0x4092E0, bisha: 0x508800, itemCapacity: 154),
            "6.4" => BuildLegacyRuntimeLayout(version, merit: 0x508000, talent: 0x508998, exclusive: 0x50E400, war: 0x4A7B20, warLen: 0x30, revive: 0x4092C7, bisha: 0x511800, itemCapacity: 255),
            "6.5" => new CczLegacyRuntimeMemoryLayout
            {
                Source = "current-project-debug-and-old-wrench-comparison",
                WarArrayAddress = 0x4A7B20,
                WarRecordSize = 0x30,
                AllyCapacity = 20,
                FriendlyCapacity = 40,
                EnemyCapacity = 190,
                ItemCapacity = 255,
                SpecialSkillCatalogOffset = 0x9E800,
                UnitDataIdOffset = EngineRuntimeSemanticRegistry.TacticalUnitDataIdOffset,
                UnitDataIdByteWidth = EngineRuntimeSemanticRegistry.TacticalUnitDataIdWidth,
                UnitDataIdContainerByteWidth = EngineRuntimeSemanticRegistry.TacticalUnitDataIdContainerWidth,
                UnitBattleSpriteIdOffset = EngineRuntimeSemanticRegistry.TacticalUnitBattleSpriteIdOffset,
                UnitBattleSpriteIdByteWidth = EngineRuntimeSemanticRegistry.TacticalUnitBattleSpriteIdWidth,
                UnitPackedDisplayStateOffset = EngineRuntimeSemanticRegistry.TacticalUnitPackedDisplayStateOffset,
                UnitPackedDisplayStateByteWidth = EngineRuntimeSemanticRegistry.TacticalUnitPackedDisplayStateWidth,
                UnitCurrentHpByteWidth = EngineRuntimeSemanticRegistry.TacticalUnitCurrentValueWidth,
                UnitCurrentMpByteWidth = EngineRuntimeSemanticRegistry.TacticalUnitCurrentValueWidth,
                DetailedJobTableAddress = EngineRuntimeSemanticRegistry.DetailedJobRecordBaseAddress,
                DetailedJobRecordSize = EngineRuntimeSemanticRegistry.DetailedJobRecordStride,
                DetailedJobCount = EngineRuntimeSemanticRegistry.DetailedJobRecordMaximumId + 1,
                JobFamilyTerrainTableAddress = EngineRuntimeSemanticRegistry.JobFamilyTerrainRecordBaseAddress,
                JobFamilyTerrainRecordSize = EngineRuntimeSemanticRegistry.JobFamilyTerrainRecordStride,
                JobFamilyTerrainCount = EngineRuntimeSemanticRegistry.JobFamilyTerrainRecordMaximumId + 1,
                ConsumableCountArrayAddress = EngineRuntimeSemanticRegistry.ConsumableCountArrayAddress,
                ConsumableMinimumItemId = EngineRuntimeSemanticRegistry.ConsumableItemMinimumId,
                ConsumableMaximumItemId = EngineRuntimeSemanticRegistry.ConsumableItemMaximumId,
                Applicability = "当前 6.5 已验证战场数组/HP 几何接近旧 6.4；其它运行时地址仍必须动态验证。"
            },
            _ => null
        };

        return profile;
    }

    private static string ResolveTableVersionPrefix(string version)
        => version switch
        {
            "6.3" => "6.3",
            "6.6" => "6.6",
            _ => "6.5"
        };

    private static CczEngineTableHints BuildTableHints(string version)
    {
        var prefix = version switch
        {
            "6.3" => "6.3",
            "6.6" => "6.6",
            _ => "6.5"
        };

        return new CczEngineTableHints
        {
            PersonTable = $"{prefix}-0 人物",
            BiographyTable = $"{prefix}-0-1 人物列传",
            CriticalQuoteTable = $"{prefix}-0-2 暴击台词",
            RetreatQuoteTable = $"{prefix}-0-3 撤退台词",
            ItemLowTable = $"{prefix}-1 物品（0-103）",
            ItemHighTable = $"{prefix}-2 物品（104-255）",
            JobTable = $"{prefix}-3 兵种系",
            JobSeriesTable = $"{prefix}-3 兵种系",
            DetailedJobTable = $"{prefix}-4 详细兵种",
            ItemEffectNameLowTable = $"{prefix}-1-2 装备特效名称（1A-57）",
            ItemEffectNameHighTable = $"{prefix}-1-3 装备特效名称（58-7F）",
            JobEffectNameTable = $"{prefix}-7 兵种特效",
            JobEffectDescriptionTable = $"{prefix}-7-1 兵种特效说明",
            JobEffectAssignmentTable = $"{prefix}-7-2 兵种特效分配",
            PersonalEffectTable = $"{prefix}-7-3 人物专属、套装专属",
            CampaignNameTable = $"{prefix}-8 战役名称",
            ShopDataTable = $"{prefix}-8-1 商店数据"
        };
    }

    private static CczLegacyRuntimeMemoryLayout BuildLegacyRuntimeLayout(
        string version,
        uint merit,
        uint talent,
        uint exclusive,
        uint war,
        int warLen,
        uint revive,
        uint bisha,
        int itemCapacity)
        => new()
        {
            Source = $"old-wrench-source-{version}",
            WarArrayAddress = war,
            WarRecordSize = warLen,
            AllyCapacity = version == "6.4" ? 20 : 16,
            FriendlyCapacity = version == "6.4" ? 40 : 19,
            EnemyCapacity = version == "6.4" ? 190 : 80,
            ItemCapacity = itemCapacity,
            UnitDataIdOffset = version == "6.4" ? EngineRuntimeSemanticRegistry.TacticalUnitDataIdOffset : 0x04,
            UnitDataIdByteWidth = version == "6.4" ? EngineRuntimeSemanticRegistry.TacticalUnitDataIdWidth : 1,
            UnitDataIdContainerByteWidth = version == "6.4" ? EngineRuntimeSemanticRegistry.TacticalUnitDataIdContainerWidth : 1,
            UnitBattleSpriteIdOffset = 0x04,
            UnitBattleSpriteIdByteWidth = 1,
            UnitPackedDisplayStateOffset = 0x04,
            UnitPackedDisplayStateByteWidth = version == "6.4" ? 4 : 1,
            UnitCurrentHpByteWidth = version == "6.4" ? EngineRuntimeSemanticRegistry.TacticalUnitCurrentValueWidth : 2,
            UnitCurrentMpByteWidth = version == "6.4" ? EngineRuntimeSemanticRegistry.TacticalUnitCurrentValueWidth : 2,
            CharacterMaxHpOffset = version is "6.4" ? 0x1B : 0x1C,
            CharacterMaxHpByteWidth = 4,
            CharacterMaxMpOffset = version is "6.4" ? 0x1F : 0x20,
            CharacterMaxMpByteWidth = version is "6.4" ? 2 : 1,
            ConsumableNameTableAddress = version == "6.4" ? 0x4A1FE6u : 0x4A19BFu,
            ConsumableCount = version == "6.4" ? 105 : 43,
            ConsumableEncoding = version == "6.4" ? "gbk" : "gb18030",
            DetailedJobTableAddress = version == "6.4" ? EngineRuntimeSemanticRegistry.DetailedJobRecordBaseAddress : 0,
            DetailedJobRecordSize = version == "6.4" ? EngineRuntimeSemanticRegistry.DetailedJobRecordStride : 0,
            DetailedJobCount = version == "6.4" ? EngineRuntimeSemanticRegistry.DetailedJobRecordMaximumId + 1 : 0,
            JobFamilyTerrainTableAddress = version == "6.4" ? EngineRuntimeSemanticRegistry.JobFamilyTerrainRecordBaseAddress : 0,
            JobFamilyTerrainRecordSize = version == "6.4" ? EngineRuntimeSemanticRegistry.JobFamilyTerrainRecordStride : 0,
            JobFamilyTerrainCount = version == "6.4" ? EngineRuntimeSemanticRegistry.JobFamilyTerrainRecordMaximumId + 1 : 0,
            ConsumableCountArrayAddress = version == "6.4" ? EngineRuntimeSemanticRegistry.ConsumableCountArrayAddress : 0,
            ConsumableMinimumItemId = version == "6.4" ? EngineRuntimeSemanticRegistry.ConsumableItemMinimumId : 0,
            ConsumableMaximumItemId = version == "6.4" ? EngineRuntimeSemanticRegistry.ConsumableItemMaximumId : 0,
            TalentNameTableAddress = version switch
            {
                "6.4" => 0x4FF560u,
                "6.2" => 0x4FF960u,
                "6.1" => 0x4FFA80u,
                _ => 0x4FF7A0u
            },
            TalentCount = version switch
            {
                "6.4" => 180,
                "6.3" => 144,
                "6.2" => 91,
                "6.1" => 88,
                _ => 144
            },
            KillNameTableAddress = version == "6.4" ? 0x4A7510u : version is "6.1" or "6.2" ? 0x4FF710u : 0x4FF551u,
            KillCount = version == "6.4" ? 80 : version == "6.3" ? 36 : 0,
            ReviveFunctionAddress = revive,
            BishaTableAddress = bisha,
            TalentTableAddress = talent,
            ExclusiveSetTableAddress = exclusive,
            MeritTableAddress = merit,
            SpecialSkillCatalogOffset = 0x9E800
        };

    private static long? TryGetLength(string path)
    {
        try
        {
            return File.Exists(path) ? new FileInfo(path).Length : null;
        }
        catch
        {
            return null;
        }
    }

    private static (string? Text, int? LowWord) TryReadVersionInfo(string path)
    {
        try
        {
            if (!File.Exists(path)) return (null, null);
            var info = FileVersionInfo.GetVersionInfo(path);
            var text = FirstNonEmpty(info.FileVersion, info.ProductVersion);
            var lowWord = info.FilePrivatePart != 0 || info.FileBuildPart != 0 || info.FileMinorPart != 0 || info.FileMajorPart != 0
                ? info.FilePrivatePart
                : (int?)null;
            return (text, lowWord);
        }
        catch
        {
            return (null, null);
        }
    }

    private static string? TryComputeSha256(string path)
    {
        try
        {
            if (!File.Exists(path)) return null;
            return ExecutableAnalysisSnapshotCache.Shared.GetBase(path).Sha256;
        }
        catch
        {
            return null;
        }
    }

    private static string? NormalizeVersionHint(string? value)
    {
        var extracted = Extract6xVersionHint(value ?? string.Empty);
        if (string.IsNullOrWhiteSpace(extracted)) return null;

        var suffix = extracted[2..];
        var digits = new string(suffix.TakeWhile(char.IsDigit).ToArray());
        if (string.IsNullOrWhiteSpace(digits)) return null;
        return "6." + digits;
    }

    private static string? Extract6xVersionHint(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        for (var i = 0; i < value.Length - 2; i++)
        {
            if (value[i] != '6' || value[i + 1] != '.' || !char.IsLetterOrDigit(value[i + 2])) continue;
            var end = i + 3;
            while (end < value.Length && char.IsLetterOrDigit(value[end])) end++;
            return value[i..end];
        }

        return null;
    }

    private static string? FirstNonEmpty(params string?[] values)
        => values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
}
