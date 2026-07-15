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
        var exists = File.Exists(path);
        if (rImageId < 0)
        {
            return new CharacterImageResourceStatus(
                "R",
                rImageId,
                "编号无效",
                "Pmapobj.e5",
                path,
                $"R 形象编号不能小于 0：{rImageId}。");
        }

        var layout = new CharacterImageLayoutService().Resolve(project);
        if (exists && layout.REntryCount > 0 && rImageId > layout.RMaxId)
        {
            return new CharacterImageResourceStatus(
                "R",
                rImageId,
                "索引越界",
                $"Pmapobj.e5 上限 R{layout.RMaxId}",
                path,
                $"R 形象 {rImageId} 超出当前项目可用范围；{layout.Evidence}。");
        }

        var front = checked(rImageId * 2 + 1);
        var back = checked(rImageId * 2 + 2);
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
        if (sImageId < 0)
        {
            return new CharacterImageResourceStatus(
                "S",
                sImageId,
                "编号无效",
                "Unit_*",
                string.Join(";", unitFiles),
                $"S 形象编号不能小于 0：{sImageId}。");
        }

        var layout = new CharacterImageLayoutService().Resolve(project);
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

        if (layout.UnitEntryCount > 0 && sImageId > layout.SMaxId)
        {
            return new CharacterImageResourceStatus(
                "S",
                sImageId,
                "索引越界",
                $"Unit_* 上限 S{layout.SMaxId}",
                string.Join(";", unitFiles),
                $"S 形象 {sImageId} 超出当前项目可用范围；{layout.Evidence}。");
        }

        var mapping = ResolveSUnitImageMapping(project, sImageId);
        if (layout.UnitEntryCount > 0 &&
            mapping.ImageNumbers.Any(number => number <= 0 || number > layout.UnitEntryCount))
        {
            return new CharacterImageResourceStatus(
                "S",
                sImageId,
                "索引越界",
                mapping.ShortText,
                string.Join(";", unitFiles),
                $"{mapping.Detail} 但当前项目 Unit_* 共同条目数为 {layout.UnitEntryCount}；{layout.Evidence}。");
        }

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
    public static string BuildSMappingShortText(CczProject project, int sImageId)
        => new CharacterImageLayoutService().ResolveSUnitImageMapping(project, sImageId).ShortText;

    public static IReadOnlyList<int> GetAvailableSImageStageSlots(int sImageId)
        => CharacterImageLayoutService.GetAvailableSImageStageSlots(CharacterImageLayoutService.Default, sImageId);

    public static IReadOnlyList<int> GetAvailableSImageStageSlots(CczProject project, int sImageId)
    {
        var layout = new CharacterImageLayoutService().Resolve(project);
        return CharacterImageLayoutService.GetAvailableSImageStageSlots(layout, sImageId);
    }

    public static SImagePreviewStageResolution ResolveSPreviewStage(
        CczProject project,
        int sImageId,
        int? jobId,
        int factionSlot,
        int requestedStageSlot)
    {
        requestedStageSlot = Math.Clamp(requestedStageSlot, 1, 3);
        var mapping = ResolveSUnitImageMapping(project, sImageId, jobId, factionSlot);
        var availableStages = GetAvailableSImageStageSlots(project, sImageId);
        var oneStageFallback = availableStages.Count == 1 && requestedStageSlot != 1;
        var effectiveStageSlot = oneStageFallback ? 1 : requestedStageSlot;
        var target = ResolveSImageStageTargets(
                project,
                mapping,
                new[] { effectiveStageSlot },
                defaultAllStages: false)
            .FirstOrDefault();
        return new SImagePreviewStageResolution(
            requestedStageSlot,
            effectiveStageSlot,
            oneStageFallback,
            mapping,
            target);
    }

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
        return CharacterImageLayoutService.NormalizeSImageStageSlots(
            CharacterImageLayoutService.Default,
            sImageId,
            selectedStages,
            defaultAllStages);
    }

    public static IReadOnlyList<int> NormalizeSImageStageSlots(
        CczProject project,
        int sImageId,
        IReadOnlyList<int> selectedStages,
        bool defaultAllStages)
    {
        var layout = new CharacterImageLayoutService().Resolve(project);
        return CharacterImageLayoutService.NormalizeSImageStageSlots(layout, sImageId, selectedStages, defaultAllStages);
    }

    public static IReadOnlyList<SImageStageTarget> ResolveSImageStageTargets(
        SUnitImageMapping mapping,
        IReadOnlyList<int> selectedStages,
        bool defaultAllStages)
    {
        return CharacterImageLayoutService.ResolveSImageStageTargets(
            CharacterImageLayoutService.Default,
            mapping,
            selectedStages,
            defaultAllStages);
    }

    public static IReadOnlyList<SImageStageTarget> ResolveSImageStageTargets(
        CczProject project,
        SUnitImageMapping mapping,
        IReadOnlyList<int> selectedStages,
        bool defaultAllStages)
    {
        var layout = new CharacterImageLayoutService().Resolve(project);
        return CharacterImageLayoutService.ResolveSImageStageTargets(layout, mapping, selectedStages, defaultAllStages);
    }

    public static int NormalizeSPreviewFactionSlot(int factionSlot) =>
        CharacterImageLayoutService.NormalizeSPreviewFactionSlot(factionSlot);

    public static string BuildSPreviewFactionText(int factionSlot) =>
        CharacterImageLayoutService.BuildSPreviewFactionText(NormalizeSPreviewFactionSlot(factionSlot));

    public static SUnitImageMapping ResolveSUnitImageMapping(CczProject project, int sImageId, int? jobId = null, int factionSlot = DefaultSPreviewFactionSlot)
        => new CharacterImageLayoutService().ResolveSUnitImageMapping(project, sImageId, jobId, factionSlot);

    public static SUnitImageMapping ResolveSUnitImageMapping(int sImageId, int? jobId = null, int factionSlot = DefaultSPreviewFactionSlot)
        => CharacterImageLayoutService.ResolveSUnitImageMapping(CharacterImageLayoutService.Default, sImageId, jobId, factionSlot);

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

public sealed record SImagePreviewStageResolution(
    int RequestedStageSlot,
    int EffectiveStageSlot,
    bool IsOneStageFallback,
    SUnitImageMapping Mapping,
    SImageStageTarget? Target)
{
    public string FallbackDetail => IsOneStageFallback
        ? $"请求{CharacterImageResourceService.BuildSImageStageText(RequestedStageSlot)}，实际使用{CharacterImageResourceService.BuildSImageStageText(EffectiveStageSlot)}。"
        : string.Empty;
}
