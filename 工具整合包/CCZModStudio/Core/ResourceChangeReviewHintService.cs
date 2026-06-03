using System.Globalization;
using CCZModStudio.Models;

namespace CCZModStudio.Core;

/// <summary>
/// 资源整文件替换/还原后的复查提示。
/// 用于把“资源已写入”继续联动到资源诊断、关卡地图联动、报告和预览 PNG。
/// </summary>
public sealed class ResourceChangeReviewHintService
{
    public bool MayAffectScenarioMap(ResourceIndexItem item)
    {
        if (item.Category is "地图图片" or "E5S存档信息") return true;
        if (item.Name.Equals("Hexzmap.e5", StringComparison.OrdinalIgnoreCase)) return true;
        if (PathContainsSegment(item.Path, "Map") || PathContainsSegment(item.Path, "SV")) return true;
        return false;
    }

    public IReadOnlyList<ScenarioMapLinkInfo> FindAffectedScenarioMapLinks(ResourceIndexItem item, IEnumerable<ScenarioMapLinkInfo> links)
    {
        var linkList = links.ToList();
        if (item.Name.Equals("Hexzmap.e5", StringComparison.OrdinalIgnoreCase))
        {
            return linkList
                .Where(link => link.Status != "非普通关卡")
                .OrderBy(link => ScenarioSortKey(link.ScenarioId))
                .ThenBy(link => link.ScenarioFileName, StringComparer.CurrentCultureIgnoreCase)
                .Take(12)
                .ToList();
        }

        var fileName = Path.GetFileName(item.Path);
        var mapId = NormalizeMapId(FirstNonEmpty(item.Id, ExtractMapToken(item.Name), ExtractMapToken(item.Path)));
        return linkList
            .Where(link =>
                PathEquals(link.MapImagePath, item.Path) ||
                PathEquals(link.ScenarioPath, item.Path) ||
                (!string.IsNullOrWhiteSpace(fileName) &&
                 (link.MapImageName.Equals(fileName, StringComparison.OrdinalIgnoreCase) ||
                  link.ScenarioFileName.Equals(fileName, StringComparison.OrdinalIgnoreCase))) ||
                (!string.IsNullOrWhiteSpace(mapId) && link.MapId.Equals(mapId, StringComparison.OrdinalIgnoreCase)))
            .OrderBy(link => ScenarioSortKey(link.ScenarioId))
            .ThenBy(link => link.ScenarioFileName, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    public string BuildScenarioMapReviewHint(ResourceIndexItem changedItem, IEnumerable<ScenarioMapLinkInfo> links)
    {
        if (!MayAffectScenarioMap(changedItem)) return string.Empty;

        var linkList = links.ToList();
        if (linkList.Count == 0)
        {
            return "关卡地图联动复查：当前资源可能影响 SV/Map/Hexzmap 联动，但尚未生成联动索引。建议打开“关卡地图联动”页生成联动，再导出检查报告和预览 PNG。";
        }

        var normal = linkList.Count(link => link.Status != "非普通关卡");
        var complete = linkList.Count(link => link.Status == "完整候选");
        var incomplete = linkList.Count(link => link.Status.Contains("缺", StringComparison.Ordinal));
        var affected = FindAffectedScenarioMapLinks(changedItem, linkList);
        var affectedText = affected.Count == 0
            ? "未匹配到具体关卡行；如果这是备用素材、特殊命名地图或批量资源，请手动在关卡地图联动页按编号/文件名搜索确认。"
            : "关联关卡：" + string.Join("；", affected.Take(8).Select(link =>
                $"{link.ScenarioFileName}->{link.MapId}({link.Status})")) +
              (affected.Count > 8 ? $" 等 {affected.Count} 项" : string.Empty);

        return
            $"关卡地图联动复查：已因 {BuildChangedResourceLabel(changedItem)} 变更重新生成联动证据；普通关卡 {normal}，完整候选 {complete}，不完整 {incomplete}。\r\n" +
            $"{affectedText}\r\n" +
            "建议：打开“关卡地图联动”页定位关联行，必要时导出“检查报告”和“预览PNG”；发布前请实机确认地图底图、按地图分辨率/48 划分的地形格和 SV 脚本表现一致。";
    }

    private static string BuildChangedResourceLabel(ResourceIndexItem item)
        => string.IsNullOrWhiteSpace(item.Id)
            ? $"{item.Category}/{item.Name}"
            : $"{item.Category}/{item.Name}#{item.Id}";

    private static bool PathContainsSegment(string path, string segment)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;
        var normalized = path.Replace('/', '\\');
        return normalized.Contains("\\" + segment + "\\", StringComparison.OrdinalIgnoreCase);
    }

    private static bool PathEquals(string left, string right)
    {
        if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right)) return false;
        try
        {
            return Path.GetFullPath(left).Equals(Path.GetFullPath(right), StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return left.Equals(right, StringComparison.OrdinalIgnoreCase);
        }
    }

    private static string ExtractMapToken(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;
        for (var i = 0; i < text.Length - 1; i++)
        {
            if (text[i] != 'M' && text[i] != 'm') continue;
            var start = i + 1;
            var end = start;
            while (end < text.Length && char.IsDigit(text[end])) end++;
            if (end > start) return text[i..end];
        }

        return string.Empty;
    }

    private static string NormalizeMapId(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        value = value.Trim();
        if (int.TryParse(value.TrimStart('M', 'm'), NumberStyles.Integer, CultureInfo.InvariantCulture, out var number))
        {
            return "M" + number.ToString("000", CultureInfo.InvariantCulture);
        }

        return string.Empty;
    }

    private static int ScenarioSortKey(string id)
        => int.TryParse(id, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number) ? number : int.MaxValue;

    private static string FirstNonEmpty(params string[] values)
        => values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? string.Empty;
}
