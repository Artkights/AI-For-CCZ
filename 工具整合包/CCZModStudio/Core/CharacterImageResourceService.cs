using System.Globalization;
using CCZModStudio.Models;

namespace CCZModStudio.Core;

public sealed class CharacterImageResourceService
{
    // Tou.dll true-color face resource id = Face.e5 small-face number + 300 (lang=2052)
    public const int FaceTrueColorResourceBase = 300;
    public const int TrueColorLanguageId = 2052;
    public const int DefaultSPreviewFactionSlot = 1;

    public CharacterFaceMapping MapFaceId(int dataFaceId)
    {
        // Tutorial rule: Data faceId 0 uses Face.e5 #1-#8 (legacy multi-expression slot).
        if (dataFaceId <= 0)
        {
            return new CharacterFaceMapping(
                dataFaceId,
                Enumerable.Range(1, 8).ToArray(),
                Enumerable.Range(1 + FaceTrueColorResourceBase, 8).ToArray(),
                "Data 头像号 0 使用 Face.e5 的 1-8 号小头像（遗留多表情规则）。");
        }

        // Data faceId 1 -> Face.e5 #9; 2 -> #10 ...
        var faceImageNumber = checked(dataFaceId + 8);
        return new CharacterFaceMapping(
            dataFaceId,
            new[] { faceImageNumber },
            new[] { faceImageNumber + FaceTrueColorResourceBase },
            $"Data 头像号 {dataFaceId} -> Face.e5 #{faceImageNumber}；Tou.dll 资源 #{faceImageNumber + FaceTrueColorResourceBase} (lang {TrueColorLanguageId})。");
    }

    public CharacterImageResourceStatus BuildRStatus(CczProject project, int rImageId)
    {
        var path = ResolveGameFile(project, "Pmapobj.e5");
        var front = checked(rImageId * 2 + 1);
        var back = checked(rImageId * 2 + 2);
        var exists = File.Exists(path);
        if (rImageId == 0)
        {
            return new CharacterImageResourceStatus(
                "R",
                rImageId,
                exists ? "已定位" : "未定位",
                "Pmapobj.e5 (默认/普通形象)",
                path,
                "R=0 表示使用普通形象（与兵种/初始设定相关），不是错误；当前按 Pmapobj.e5 的 0x110 索引表读取第 1 张正面图作为预览。");
        }
        return new CharacterImageResourceStatus(
            "R",
            rImageId,
            exists ? "已定位" : "未定位",
            $"Pmapobj.e5 图{front}/{back}",
            path,
            $"R 形象 {rImageId} 对应 Pmapobj.e5 第 {front} 张正面、第 {back} 张反面（教程口径）；当前按 0x110 起始的 12 字节大端索引表读取，不再按裸扫出现顺序取图。");
    }

    public CharacterImageResourceStatus BuildSStatus(CczProject project, int sImageId)
    {
        var unitFiles = ResolveUnitFiles(project);
        var existingFiles = unitFiles.Where(File.Exists).ToList();
        var status = existingFiles.Count == unitFiles.Length ? "已定位" : existingFiles.Count > 0 ? "部分定位" : "未定位";
        if (sImageId == 0)
        {
            return new CharacterImageResourceStatus(
                "S",
                sImageId,
                status,
                "Unit_* (默认/普通形象)",
                string.Join(";", unitFiles),
                "S=0 表示使用普通兵种形象，不是错误；预览时按人物职业和预览阵营计算 Unit 图号。");
        }
        var mapping = ResolveSUnitImageMapping(sImageId);
        return new CharacterImageResourceStatus(
            "S",
            sImageId,
            status,
            mapping.ShortText,
            string.Join(";", unitFiles),
            mapping.Detail + " 资源候选：Unit_atk.e5 / Unit_mov.e5 / Unit_spc.e5。当前按 0x110 起始的 12 字节大端索引表读取，不再按裸扫出现顺序取图。");
    }

    public string BuildFaceHint(CczProject project, int dataFaceId)
    {
        var mapping = MapFaceId(dataFaceId);
        var facePath = ResolveFaceFile(project) ?? Path.Combine(project.GameRoot, "E5", "Face.e5");
        var touPath = ResolveGameFile(project, "Tou.dll");

        var faceText = mapping.FaceImageNumbers.Count == 1
            ? mapping.FaceImageNumbers[0].ToString(CultureInfo.InvariantCulture)
            : $"{mapping.FaceImageNumbers.First()}-{mapping.FaceImageNumbers.Last()}";
        var trueColorText = mapping.TrueColorResourceIds.Count == 1
            ? mapping.TrueColorResourceIds[0].ToString(CultureInfo.InvariantCulture)
            : $"{mapping.TrueColorResourceIds.First()}-{mapping.TrueColorResourceIds.Last()}";

        var faceStatus = File.Exists(facePath) ? "Face.e5已定位" : "Face.e5未定位";
        var touStatus = File.Exists(touPath) ? "Tou.dll已定位" : "Tou.dll未定位";
        return $"Data头像号 {dataFaceId} -> Face.e5图 {faceText}；Tou.dll资源 {trueColorText}，语言{TrueColorLanguageId}；{faceStatus}，{touStatus}";
    }

    public static bool IsMissingStatus(string status) =>
        status.StartsWith("未定位", StringComparison.Ordinal) ||
        status.StartsWith("部分定位", StringComparison.Ordinal);

    public static string BuildSMappingShortText(int sImageId) => ResolveSUnitImageMapping(sImageId).ShortText;

    public static IReadOnlyList<int> GetAvailableSImageStageSlots(int sImageId)
        => sImageId is >= 1 and <= 32 ? new[] { 1, 2, 3 } : new[] { 1 };

    public static string BuildSImageStageText(int stageSlot)
        => stageSlot switch
        {
            1 => "第一转",
            2 => "第二转",
            3 => "第三转",
            _ => $"第{stageSlot}转"
        };

    public static IReadOnlyList<int> NormalizeSImageStageSlots(
        int sImageId,
        IReadOnlyList<int> selectedStages,
        bool defaultAllStages)
    {
        var available = GetAvailableSImageStageSlots(sImageId);
        if (selectedStages.Count == 0)
        {
            return defaultAllStages ? available.ToArray() : new[] { available[0] };
        }

        var normalized = selectedStages
            .Distinct()
            .OrderBy(slot => slot)
            .Where(available.Contains)
            .ToArray();
        return normalized;
    }

    public static IReadOnlyList<SImageStageTarget> ResolveSImageStageTargets(
        SUnitImageMapping mapping,
        IReadOnlyList<int> selectedStages,
        bool defaultAllStages)
    {
        var stages = NormalizeSImageStageSlots(mapping.SImageId, selectedStages, defaultAllStages);
        var targets = new List<SImageStageTarget>();
        foreach (var stage in stages)
        {
            var index = stage - 1;
            if (index < 0 || index >= mapping.ImageNumbers.Count)
            {
                continue;
            }

            targets.Add(new SImageStageTarget(
                stage,
                mapping.ImageNumbers[index],
                BuildSImageStageText(stage)));
        }

        return targets.ToArray();
    }

    public static int NormalizeSPreviewFactionSlot(int factionSlot) =>
        factionSlot is >= 1 and <= 3 ? factionSlot : DefaultSPreviewFactionSlot;

    public static string BuildSPreviewFactionText(int factionSlot) =>
        NormalizeSPreviewFactionSlot(factionSlot) switch
        {
            1 => "我军",
            2 => "友军",
            3 => "敌军",
            _ => "我军"
        };

    public static SUnitImageMapping ResolveSUnitImageMapping(int sImageId, int? jobId = null, int factionSlot = DefaultSPreviewFactionSlot)
    {
        if (sImageId < 0)
        {
            return new SUnitImageMapping(
                sImageId,
                jobId,
                NormalizeSPreviewFactionSlot(factionSlot),
                Array.Empty<int>(),
                $"S{sImageId} 无效",
                $"S 形象 {sImageId} 小于 0，无法映射 Unit 图号。");
        }

        if (sImageId == 0)
        {
            var slot = NormalizeSPreviewFactionSlot(factionSlot);
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

            var imageNumber = checked(jobId.Value * 3 + slot);
            var faction = BuildSPreviewFactionText(slot);
            return new SUnitImageMapping(
                sImageId,
                jobId,
                slot,
                new[] { imageNumber },
                $"S0 职业{jobId.Value}{faction}图{imageNumber}",
                $"S=0 默认兵种形象：Unit 图号 = 职业({jobId.Value}) * 3 + 阵营槽({slot}, {faction}) = {imageNumber}。");
        }

        if (sImageId <= 32)
        {
            var first = checked(240 + (sImageId - 1) * 3 + 1);
            var numbers = new[] { first, first + 1, first + 2 };
            return new SUnitImageMapping(
                sImageId,
                jobId,
                NormalizeSPreviewFactionSlot(factionSlot),
                numbers,
                $"S{sImageId} 特殊图{first}-{first + 2}",
                $"S 形象 {sImageId} 属于三转特殊形象：对应 Unit 图 {first}/{first + 1}/{first + 2}。");
        }

        var oneStageImageNumber = checked(336 + (sImageId - 32));
        return new SUnitImageMapping(
            sImageId,
            jobId,
            NormalizeSPreviewFactionSlot(factionSlot),
            new[] { oneStageImageNumber },
            $"S{sImageId} 特殊图{oneStageImageNumber}",
            $"S 形象 {sImageId} 属于一转特殊形象：对应 Unit 图 {oneStageImageNumber}。");
    }

    public static string? ResolveFaceFile(CczProject project)
    {
        var candidates = new[]
        {
            Path.Combine(project.GameRoot, "E5", "Face.e5"),
            Path.Combine(project.GameRoot, "e5", "Face.e5"),
            Path.Combine(project.GameRoot, "Face.e5")
        };
        return candidates.FirstOrDefault(File.Exists);
    }

    public static string ResolveGameFile(CczProject project, string fileName)
    {
        var direct = Path.Combine(project.GameRoot, fileName);
        if (File.Exists(direct)) return direct;
        var e5 = Path.Combine(project.GameRoot, "E5", fileName);
        if (File.Exists(e5)) return e5;
        return direct;
    }

    private static string[] ResolveUnitFiles(CczProject project) =>
    [
        ResolveGameFile(project, "Unit_atk.e5"),
        ResolveGameFile(project, "Unit_mov.e5"),
        ResolveGameFile(project, "Unit_spc.e5")
    ];

}

public sealed record CharacterFaceMapping(
    int DataFaceId,
    IReadOnlyList<int> FaceImageNumbers,
    IReadOnlyList<int> TrueColorResourceIds,
    string Explanation);

public sealed record CharacterImageResourceStatus(
    string Kind,
    int Id,
    string Status,
    string ResourceName,
    string Path,
    string Detail);

public sealed record SUnitImageMapping(
    int SImageId,
    int? JobId,
    int FactionSlot,
    IReadOnlyList<int> ImageNumbers,
    string ShortText,
    string Detail);
