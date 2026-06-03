using System.Globalization;
using CCZModStudio.Models;

namespace CCZModStudio.Core;

public sealed class CharacterImageResourceService
{
    // Tou.dll true-color face resource id = Face.e5 small-face number + 300 (lang=2052)
    public const int FaceTrueColorResourceBase = 300;
    public const int TrueColorLanguageId = 2052;

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
                "R=0 表示使用普通形象（与兵种/初始设定相关），不是错误；若需强制指定 R 剧本形象，请改为非 0 编号。");
        }
        return new CharacterImageResourceStatus(
            "R",
            rImageId,
            exists ? "已定位" : "未定位",
            $"Pmapobj.e5 图{front}/{back}",
            path,
            $"R 形象 {rImageId} 对应 Pmapobj.e5 第 {front} 张正面、第 {back} 张反面（教程口径）。注意：Pmapobj.e5 可能包含地图对象等其它资源，且封包条目边界/顺序未完全确认，当前预览仅用于粗定位。");
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
                "S=0 表示使用普通形象（与兵种/初始设定相关），不是错误；若需强制指定 S 形象，请改为非 0 编号。资源候选：Unit_atk.e5 / Unit_mov.e5 / Unit_spc.e5。");
        }
        var mapping = BuildSMappingText(sImageId);
        return new CharacterImageResourceStatus(
            "S",
            sImageId,
            status,
            mapping.ShortText,
            string.Join(";", unitFiles),
            mapping.Detail + " 资源候选：Unit_atk.e5 / Unit_mov.e5 / Unit_spc.e5。注意：S 形象最终显示通常与兵种/动作帧选择相关，当前预览仅为候选切片，不保证与实机一致。");
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

    public static string BuildSMappingShortText(int sImageId) => BuildSMappingText(sImageId).ShortText;

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

    private static SImageMappingText BuildSMappingText(int sImageId)
    {
        // Special groups (old tutorials): 140-156.
        if (sImageId is >= 140 and <= 142) return new("特殊S01 图140-142", "S 形象值落在特殊形象 01：Unit_* 第 140-142 号图，通常用于三转形象。");
        if (sImageId is >= 143 and <= 145) return new("特殊S02 图143-145", "S 形象值落在特殊形象 02：Unit_* 第 143-145 号图，通常用于三转形象。");
        if (sImageId is >= 146 and <= 148) return new("特殊S03 图146-148", "S 形象值落在特殊形象 03：Unit_* 第 146-148 号图，通常用于三转形象。");
        if (sImageId is >= 149 and <= 151) return new("特殊S04 图149-151", "S 形象值落在特殊形象 04：Unit_* 第 149-151 号图，通常用于三转形象。");
        if (sImageId is >= 152 and <= 154) return new("特殊S05 图152-154", "S 形象值落在特殊形象 05：Unit_* 第 152-154 号图，通常用于三转形象。");
        if (sImageId == 155) return new("特殊S06 图155", "S 形象值落在特殊形象 06：Unit_* 第 155 号图。");
        if (sImageId == 156) return new("特殊S07 图156", "S 形象值落在特殊形象 07：Unit_* 第 156 号图。");

        if (sImageId is >= 0 and <= 139)
        {
            return new($"普通S{sImageId}", "S 形象值在 0-139 普通范围内；最终战场帧还要结合人物职业/兵种与 Unit_* 资源读取。");
        }

        return new($"扩展/待确认S{sImageId}", "S 形象值超出已记录的普通 0-139 与特殊 140-156 范围；需对照 6.5 形象指定器与实机确认。");
    }

    private sealed record SImageMappingText(string ShortText, string Detail);
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
