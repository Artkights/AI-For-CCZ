using System.Globalization;
using CCZModStudio.Models;

namespace CCZModStudio.Core;

public sealed class CharacterImageResourceService
{
    // Tou.dll true-color face resource id = Face.e5 small-face number + 300 (lang=2052)
    public const int FaceTrueColorResourceBase = 300;
    public const int TrueColorLanguageId = 2052;
    public const int FirstEmbeddedSImageId = 241;

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
                "R=0 表示使用普通形象（与兵种/初始设定相关），不是错误；当前工具只定位编号和 Pmapobj.e5，不显示未经 Ls12 解包验证的候选图。");
        }
        return new CharacterImageResourceStatus(
            "R",
            rImageId,
            exists ? "已定位" : "未定位",
            $"Pmapobj.e5 图{front}/{back}",
            path,
            $"R 形象 {rImageId} 对应 Pmapobj.e5 第 {front} 张正面、第 {back} 张反面（教程口径）。注意：Pmapobj.e5 是 Ls 封包，必须解析真实目录/解压后才能确认图像；当前工具不再显示裸扫 JPEG 候选图。");
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
                "S=0 表示使用普通形象（与兵种/初始设定相关），不是错误；普通 S 仍需解析 Unit_* 的 Ls12 条目后才能显示。");
        }
        var mapping = BuildSMappingText(sImageId);
        return new CharacterImageResourceStatus(
            "S",
            sImageId,
            status,
            mapping.ShortText,
            string.Join(";", unitFiles),
            mapping.Detail + " 资源候选：Unit_atk.e5 / Unit_mov.e5 / Unit_spc.e5。注意：S 形象最终显示通常与兵种、动作、朝向和帧选择相关；当前只对 S>=241 且三套 Unit 文件中存在明文 BMP 扩展条目的编号显示首帧预览。");
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
        if (sImageId is > 0 and < FirstEmbeddedSImageId)
        {
            return new($"基础S{sImageId}", "本地 6.4 形象对应表显示基础 S 形象覆盖 1-240；这些条目仍需按 Ls12 目录/解压和动作帧选择读取，不能按明文 BMP 出现顺序预览。");
        }

        var entryIndex = checked(sImageId - FirstEmbeddedSImageId);
        return new(
            $"扩展S{sImageId} Unit明文#{entryIndex + 1}",
            $"本地 6.4 形象对应表显示 S=241 起进入特殊/扩展区；当前项目 Unit_*.e5 内存在一批明文 BMP 扩展条目，可按 S-{FirstEmbeddedSImageId} 作为零基条目下标做只读首帧预览。");
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
