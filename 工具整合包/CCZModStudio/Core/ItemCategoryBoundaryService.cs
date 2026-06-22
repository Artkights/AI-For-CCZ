using System.Globalization;
using CCZModStudio.Formats;
using CCZModStudio.Models;

namespace CCZModStudio.Core;

public sealed record ItemCategoryBoundary(
    int WeaponStartId,
    int DefenseStartId,
    int AccessoryStartId,
    string Source,
    bool IsFallback)
{
    public int WeaponCount => Math.Max(0, DefenseStartId - WeaponStartId);
    public int DefenseCount => Math.Max(0, AccessoryStartId - DefenseStartId);
    public int AccessoryCount => Math.Max(0, 256 - AccessoryStartId);

    public string DisplayText =>
        $"DefID={DefenseStartId}, AssID={AccessoryStartId}；AssID..255 为辅助段编码范围，实际需按物品行区分辅助装备/道具 ({Source})";

    public string GetMajorCategory(int itemId)
    {
        if (itemId < DefenseStartId) return "武器";
        if (itemId < AccessoryStartId) return "防具";
        return "辅助段";
    }
}

public static class ItemCategoryBoundaryService
{
    public const int DefaultDefenseStartId = 70;
    public const int DefaultAccessoryStartId = 109;
    public const int MinItemId = 0;
    public const int MaxItemId = 255;

    public static ItemCategoryBoundary Resolve(CczProject project)
    {
        ArgumentNullException.ThrowIfNull(project);

        var path = ResolveSystemIniPath(project);
        if (path != null && File.Exists(path))
        {
            int? defId = null;
            int? assId = null;
            foreach (var rawLine in File.ReadLines(path, EncodingService.Gbk))
            {
                var line = rawLine.Split(';')[0].Trim();
                if (line.StartsWith("DefID=", StringComparison.OrdinalIgnoreCase) &&
                    int.TryParse(line["DefID=".Length..].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedDef))
                {
                    defId = parsedDef;
                }
                else if (line.StartsWith("AssID=", StringComparison.OrdinalIgnoreCase) &&
                         int.TryParse(line["AssID=".Length..].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedAss))
                {
                    assId = parsedAss;
                }
            }

            if (defId.HasValue && assId.HasValue && IsValid(defId.Value, assId.Value))
            {
                return new ItemCategoryBoundary(MinItemId, defId.Value, assId.Value, path, IsFallback: false);
            }

            return Fallback($"默认：B形象指定器 System.ini 边界缺失或无效 DefID={FormatNullable(defId)}, AssID={FormatNullable(assId)}，来源 {path}");
        }

        return Fallback("默认：B形象指定器 System.ini 未找到");
    }

    public static bool IsValid(int defenseStartId, int accessoryStartId)
        => defenseStartId >= MinItemId &&
           defenseStartId <= accessoryStartId &&
           accessoryStartId <= MaxItemId;

    private static ItemCategoryBoundary Fallback(string source)
        => new(MinItemId, DefaultDefenseStartId, DefaultAccessoryStartId, source, IsFallback: true);

    private static string FormatNullable(int? value)
        => value.HasValue ? value.Value.ToString(CultureInfo.InvariantCulture) : "缺失";

    private static string? ResolveSystemIniPath(CczProject project)
        => !string.IsNullOrWhiteSpace(project.ImageAssignerSystemIniPath) && File.Exists(project.ImageAssignerSystemIniPath)
            ? project.ImageAssignerSystemIniPath
            : ProjectDetector.FindPortableFile(
                project,
                "System.ini",
                Path.Combine("老版游戏制作工具", "B形象指定器", "6.6x形象指定器", "System.ini"),
                Path.Combine("B形象指定器", "6.6x形象指定器", "System.ini"),
                Path.Combine("老版游戏制作工具", "B形象指定器", "形象指定器6.5", "System.ini"),
                Path.Combine("B形象指定器", "形象指定器6.5", "System.ini"));
}
