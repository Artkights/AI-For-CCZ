using System.Collections.Concurrent;
using System.Globalization;
using System.Security.Cryptography;
using CCZModStudio.Models;

namespace CCZModStudio.Core;

public sealed class CharacterImageLayoutService
{
    public const int DefaultJobCount = 80;
    public const int DefaultFactionSlots = 3;
    public const int DefaultThreeStageSpecialCount = 32;
    public const int DefaultUnitImageStart = DefaultJobCount * DefaultFactionSlots;
    public const int DefaultOneStageSpecialStart = DefaultUnitImageStart + DefaultThreeStageSpecialCount * 3;

    private const string Known65PlShsgz2026Sha256 = "93CE9CC4F22E2B654973822A40F12E6853F3DA8FDA7DDE24E339C4723579243B";

    private static readonly ConcurrentDictionary<string, CharacterImageLayout> Cache = new(StringComparer.OrdinalIgnoreCase);
    private readonly E5ImageReplaceService _e5 = new();

    public CharacterImageLayout Resolve(CczProject project)
    {
        var key = BuildCacheKey(project);
        return Cache.GetOrAdd(key, _ => ResolveCore(project));
    }

    public static CharacterImageLayout Default => new()
    {
        ProfileName = "6.5 default image layout",
        REntryCount = 0,
        UnitEntryCount = 0,
        RMaxId = 0,
        SMaxId = 0,
        DefaultJobCount = DefaultJobCount,
        DefaultFactionSlots = DefaultFactionSlots,
        DefaultUnitImageCount = DefaultUnitImageStart,
        ThreeStageSpecialCount = DefaultThreeStageSpecialCount,
        OneStageSpecialStartImageNumber = DefaultOneStageSpecialStart + 1,
        Evidence = "Fallback layout: 80 jobs * 3 factions, 32 three-stage special S images."
    };

    public SUnitImageMapping ResolveSUnitImageMapping(
        CczProject project,
        int sImageId,
        int? jobId = null,
        int factionSlot = CharacterImageResourceService.DefaultSPreviewFactionSlot)
        => ResolveSUnitImageMapping(Resolve(project), sImageId, jobId, factionSlot);

    public static SUnitImageMapping ResolveSUnitImageMapping(
        CharacterImageLayout layout,
        int sImageId,
        int? jobId = null,
        int factionSlot = CharacterImageResourceService.DefaultSPreviewFactionSlot)
    {
        if (sImageId < 0)
        {
            return new SUnitImageMapping(
                sImageId,
                jobId,
                NormalizeSPreviewFactionSlot(factionSlot, layout),
                Array.Empty<int>(),
                $"S{sImageId} 无效",
                $"S 形象 {sImageId} 小于 0，无法映射 Unit 图号。");
        }

        var slot = NormalizeSPreviewFactionSlot(factionSlot, layout);
        if (sImageId == 0)
        {
            if (!jobId.HasValue || jobId.Value < 0)
            {
                return new SUnitImageMapping(
                    sImageId,
                    jobId,
                    slot,
                    Array.Empty<int>(),
                    "S0 默认兵种",
                    "S=0 表示使用默认兵种形象；需要人物表“职业”和预览阵营才能计算 Unit 图号。");
            }

            var imageNumber = checked(jobId.Value * layout.DefaultFactionSlots + slot);
            var faction = BuildSPreviewFactionText(slot);
            return new SUnitImageMapping(
                sImageId,
                jobId,
                slot,
                new[] { imageNumber },
                $"S0 职业{jobId.Value}{faction}图{imageNumber}",
                $"S=0 默认兵种形象：Unit 图号 = 职业({jobId.Value}) * 阵营数({layout.DefaultFactionSlots}) + 阵营槽({slot}, {faction}) = {imageNumber}。");
        }

        if (sImageId <= layout.ThreeStageSpecialCount)
        {
            var first = checked(layout.DefaultUnitImageCount + (sImageId - 1) * 3 + 1);
            var numbers = new[] { first, first + 1, first + 2 };
            return new SUnitImageMapping(
                sImageId,
                jobId,
                slot,
                numbers,
                $"S{sImageId} 特殊图{first}-{first + 2}",
                $"S 形象 {sImageId} 属于三转特殊形象：对应 Unit 图 {first}/{first + 1}/{first + 2}。布局={layout.ProfileName}。");
        }

        var oneStageImageNumber = checked(layout.OneStageSpecialStartImageNumber + (sImageId - layout.ThreeStageSpecialCount - 1));
        return new SUnitImageMapping(
            sImageId,
            jobId,
            slot,
            new[] { oneStageImageNumber },
            $"S{sImageId} 特殊图{oneStageImageNumber}",
            $"S 形象 {sImageId} 属于一转特殊形象：对应 Unit 图 {oneStageImageNumber}。布局={layout.ProfileName}。");
    }

    public static IReadOnlyList<int> GetAvailableSImageStageSlots(CharacterImageLayout layout, int sImageId)
        => sImageId >= 1 && sImageId <= layout.ThreeStageSpecialCount ? new[] { 1, 2, 3 } : new[] { 1 };

    public static IReadOnlyList<int> NormalizeSImageStageSlots(
        CharacterImageLayout layout,
        int sImageId,
        IReadOnlyList<int> selectedStages,
        bool defaultAllStages)
    {
        var available = GetAvailableSImageStageSlots(layout, sImageId);
        if (selectedStages.Count == 0)
        {
            return defaultAllStages ? available.ToArray() : new[] { available[0] };
        }

        return selectedStages
            .Distinct()
            .OrderBy(slot => slot)
            .Where(available.Contains)
            .ToArray();
    }

    public static IReadOnlyList<SImageStageTarget> ResolveSImageStageTargets(
        CharacterImageLayout layout,
        SUnitImageMapping mapping,
        IReadOnlyList<int> selectedStages,
        bool defaultAllStages)
    {
        var stages = NormalizeSImageStageSlots(layout, mapping.SImageId, selectedStages, defaultAllStages);
        var targets = new List<SImageStageTarget>();
        foreach (var stage in stages)
        {
            var index = stage - 1;
            if (index < 0 || index >= mapping.ImageNumbers.Count) continue;

            targets.Add(new SImageStageTarget(
                stage,
                mapping.ImageNumbers[index],
                CharacterImageResourceService.BuildSImageStageText(stage)));
        }

        return targets.ToArray();
    }

    public static int NormalizeSPreviewFactionSlot(int factionSlot, CharacterImageLayout? layout = null)
    {
        layout ??= Default;
        return factionSlot >= 1 && factionSlot <= layout.DefaultFactionSlots
            ? factionSlot
            : CharacterImageResourceService.DefaultSPreviewFactionSlot;
    }

    public static string BuildSPreviewFactionText(int factionSlot)
        => factionSlot switch
        {
            1 => "我军",
            2 => "友军",
            3 => "敌军",
            _ => $"阵营{factionSlot}"
        };

    private CharacterImageLayout ResolveCore(CczProject project)
    {
        var pmapPath = CharacterImageResourceService.ResolveGameFile(project, "Pmapobj.e5");
        var movPath = CharacterImageResourceService.ResolveGameFile(project, "Unit_mov.e5");
        var atkPath = CharacterImageResourceService.ResolveGameFile(project, "Unit_atk.e5");
        var spcPath = CharacterImageResourceService.ResolveGameFile(project, "Unit_spc.e5");

        var rEntryCount = File.Exists(pmapPath) ? _e5.ReadIndex(pmapPath).Count : 0;
        var unitCounts = new[]
        {
            File.Exists(movPath) ? _e5.ReadIndex(movPath).Count : 0,
            File.Exists(atkPath) ? _e5.ReadIndex(atkPath).Count : 0,
            File.Exists(spcPath) ? _e5.ReadIndex(spcPath).Count : 0
        };
        var unitEntryCount = unitCounts.Where(count => count > 0).DefaultIfEmpty(0).Min();
        var exePath = project.ResolveGameFile("Ekd5.exe");
        var exeSha = TryComputeSha256(exePath);
        var profileName = ResolveProfileName(exeSha, rEntryCount, unitEntryCount);

        var rMax = rEntryCount >= 2 ? rEntryCount / 2 - 1 : 0;
        var sMax = unitEntryCount > 0
            ? Math.Max(0, DefaultThreeStageSpecialCount + unitEntryCount - DefaultOneStageSpecialStart)
            : Default.SMaxId;

        var evidence = string.Join("; ", new[]
        {
            $"Pmapobj entries={rEntryCount.ToString(CultureInfo.InvariantCulture)}",
            $"Unit shared entries={unitEntryCount.ToString(CultureInfo.InvariantCulture)}",
            $"R max={rMax.ToString(CultureInfo.InvariantCulture)}",
            $"S max={sMax.ToString(CultureInfo.InvariantCulture)}",
            string.IsNullOrWhiteSpace(exeSha) ? string.Empty : $"Ekd5 SHA256={exeSha}"
        }.Where(item => !string.IsNullOrWhiteSpace(item)));

        return new CharacterImageLayout
        {
            ProfileName = profileName,
            REntryCount = rEntryCount,
            UnitEntryCount = unitEntryCount,
            RMaxId = rMax,
            SMaxId = sMax,
            DefaultJobCount = DefaultJobCount,
            DefaultFactionSlots = DefaultFactionSlots,
            DefaultUnitImageCount = DefaultUnitImageStart,
            ThreeStageSpecialCount = DefaultThreeStageSpecialCount,
            OneStageSpecialStartImageNumber = DefaultOneStageSpecialStart + 1,
            Evidence = evidence
        };
    }

    private static string ResolveProfileName(string? exeSha, int rEntryCount, int unitEntryCount)
    {
        if (string.Equals(exeSha, Known65PlShsgz2026Sha256, StringComparison.OrdinalIgnoreCase))
        {
            return "6.5pl 神话三国志 2026 新春版";
        }

        if (rEntryCount > 0 || unitEntryCount > 0)
        {
            return "data-derived image layout";
        }

        return Default.ProfileName;
    }

    private static string BuildCacheKey(CczProject project)
    {
        var paths = new[]
        {
            project.ResolveGameFile("Ekd5.exe"),
            CharacterImageResourceService.ResolveGameFile(project, "Pmapobj.e5"),
            CharacterImageResourceService.ResolveGameFile(project, "Unit_mov.e5"),
            CharacterImageResourceService.ResolveGameFile(project, "Unit_atk.e5"),
            CharacterImageResourceService.ResolveGameFile(project, "Unit_spc.e5")
        };

        return string.Join("|", paths.Select(BuildFileCacheKey));
    }

    private static string BuildFileCacheKey(string path)
    {
        var fullPath = Path.GetFullPath(path);
        if (!File.Exists(fullPath)) return fullPath + "|missing";

        try
        {
            var info = new FileInfo(fullPath);
            return $"{fullPath}|{info.Length}|{info.LastWriteTimeUtc.Ticks}";
        }
        catch
        {
            return fullPath + "|unknown";
        }
    }

    private static string? TryComputeSha256(string path)
    {
        try
        {
            if (!File.Exists(path)) return null;
            using var stream = File.OpenRead(path);
            return Convert.ToHexString(SHA256.HashData(stream));
        }
        catch
        {
            return null;
        }
    }
}

public sealed class CharacterImageLayout
{
    public string ProfileName { get; init; } = string.Empty;
    public int REntryCount { get; init; }
    public int UnitEntryCount { get; init; }
    public int RMaxId { get; init; }
    public int SMaxId { get; init; }
    public int DefaultJobCount { get; init; }
    public int DefaultFactionSlots { get; init; }
    public int DefaultUnitImageCount { get; init; }
    public int ThreeStageSpecialCount { get; init; }
    public int OneStageSpecialStartImageNumber { get; init; }
    public string Evidence { get; init; } = string.Empty;
}
