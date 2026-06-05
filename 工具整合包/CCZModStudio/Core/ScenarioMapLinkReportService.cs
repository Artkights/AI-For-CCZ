using System.Drawing;
using System.Globalization;
using System.Text;
using CCZModStudio.Models;

namespace CCZModStudio.Core;

/// <summary>
/// 关卡地图联动检查报告服务。
/// 只生成项目侧 Markdown 报告，用于创作者核对 SV 剧本、Map 底图和 Hexzmap 地形块，不修改任何游戏文件。
/// </summary>
public sealed class ScenarioMapLinkReportService
{
    public string WriteReport(
        CczProject project,
        IReadOnlyList<ScenarioMapLinkInfo> allLinks,
        IReadOnlyList<ScenarioMapLinkInfo>? visibleLinks = null,
        IReadOnlyDictionary<string, int>? creatorNoteCounts = null)
    {
        var reportRoot = Path.Combine(project.WorkspaceRoot, "CCZModStudio_Reports");
        Directory.CreateDirectory(reportRoot);
        var stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);
        var reportPath = Path.Combine(reportRoot, $"{stamp}_关卡地图联动检查报告.md");
        File.WriteAllText(reportPath, BuildReport(project, allLinks, visibleLinks, creatorNoteCounts), Encoding.UTF8);
        return reportPath;
    }

    public string BuildReport(
        CczProject project,
        IReadOnlyList<ScenarioMapLinkInfo> allLinks,
        IReadOnlyList<ScenarioMapLinkInfo>? visibleLinks = null,
        IReadOnlyDictionary<string, int>? creatorNoteCounts = null)
    {
        var shown = visibleLinks is { Count: > 0 } ? visibleLinks : allLinks;
        var normal = allLinks.Count(item => item.Status != "非普通关卡");
        var complete = allLinks.Count(item => item.Status == "完整候选");
        var incomplete = allLinks.Count(IsIncomplete);
        var withMap = allLinks.Count(item => item.MapImageExists);
        var withHex = allLinks.Count(item => item.HexzmapBlockExists);

        var builder = new StringBuilder();
        builder.AppendLine("# CCZModStudio 关卡地图联动检查报告");
        builder.AppendLine();
        builder.AppendLine($"- 生成时间：{DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        builder.AppendLine($"- 项目目录：`{Escape(project.GameRoot)}`");
        builder.AppendLine($"- 工作区：`{Escape(project.WorkspaceRoot)}`");
        builder.AppendLine($"- 当前模式：{(project.IsTestCopy ? "测试副本（可写，可做差异对比）" : "当前项目（可写）")}");
        builder.AppendLine($"- 报告范围：当前显示 {shown.Count} 行 / 全部 {allLinks.Count} 行");
        builder.AppendLine();
        builder.AppendLine("> 说明：本报告使用同编号候选规则 `SVxxx.E5S -> Map\\Mxxx.jpg/JPG -> Hexzmap Mxxx`，用于制作期排查和留证；它不是完整反汇编证明。报告只写入 `CCZModStudio_Reports`，不修改任何游戏文件。");
        builder.AppendLine();

        builder.AppendLine("## 1. 总览");
        builder.AppendLine();
        builder.AppendLine("| 项目 | 数量 | 创作者解释 |");
        builder.AppendLine("| --- | ---: | --- |");
        builder.AppendLine($"| SV 联动行 | {allLinks.Count} | 扫描到的 SV/E5S 文件联动候选。 |");
        builder.AppendLine($"| 普通关卡候选 | {normal} | 可按编号尝试对应地图与地形块的关卡。 |");
        builder.AppendLine($"| 完整候选 | {complete} | 同时存在地图图片和 Hexzmap 地形块，适合优先进入可视化核对。 |");
        builder.AppendLine($"| 不完整候选 | {incomplete} | 缺少地图图片或地形块，发布前应逐项确认。 |");
        builder.AppendLine($"| 存在地图图片 | {withMap} | 可在“地图浏览”页查看底图。 |");
        builder.AppendLine($"| 存在 Hexzmap 地形块 | {withHex} | 可在“Hexzmap地形探针”查看按地图分辨率/48 划分的地形格。 |");
        builder.AppendLine();

        builder.AppendLine("## 2. 状态分布");
        builder.AppendLine();
        builder.AppendLine("| 状态 | 数量 | 建议 |");
        builder.AppendLine("| --- | ---: | --- |");
        foreach (var group in allLinks.GroupBy(item => item.Status).OrderByDescending(g => g.Count()).ThenBy(g => g.Key, StringComparer.CurrentCultureIgnoreCase))
        {
            builder.AppendLine($"| {Escape(group.Key)} | {group.Count()} | {Escape(BuildStatusSuggestion(group.Key))} |");
        }
        builder.AppendLine();

        AppendPriorityItems(builder, shown, creatorNoteCounts);
        AppendCompleteSamples(builder, shown, creatorNoteCounts);
        AppendShownDetails(builder, shown, creatorNoteCounts);
        AppendWorkflowTips(builder);
        AppendSafetyBoundary(builder);
        return builder.ToString();
    }

    public static string BuildCreatorNoteTargetKey(ScenarioMapLinkInfo item)
        => $"{item.ScenarioFileName}->{item.MapId}";

    private static void AppendPriorityItems(
        StringBuilder builder,
        IReadOnlyList<ScenarioMapLinkInfo> shown,
        IReadOnlyDictionary<string, int>? creatorNoteCounts)
    {
        var priority = shown
            .Where(IsIncomplete)
            .OrderBy(item => item.ScenarioFileName, StringComparer.CurrentCultureIgnoreCase)
            .Take(40)
            .ToList();

        builder.AppendLine("## 3. 优先处理：不完整联动");
        builder.AppendLine();
        if (priority.Count == 0)
        {
            builder.AppendLine("当前显示范围内没有不完整联动。仍建议抽查完整候选的底图和地形格是否视觉对齐。");
            builder.AppendLine();
            return;
        }

        builder.AppendLine("| 关卡 | 标题 | 状态 | 地图图片 | Hexzmap | 主地形 | 备注 | 建议 |");
        builder.AppendLine("| --- | --- | --- | --- | --- | --- | ---: | --- |");
        foreach (var item in priority)
        {
            builder.AppendLine(BuildLinkRow(item, creatorNoteCounts));
        }
        builder.AppendLine();
    }

    private static void AppendCompleteSamples(
        StringBuilder builder,
        IReadOnlyList<ScenarioMapLinkInfo> shown,
        IReadOnlyDictionary<string, int>? creatorNoteCounts)
    {
        var samples = shown
            .Where(item => item.Status == "完整候选")
            .OrderBy(item => item.ScenarioFileName, StringComparer.CurrentCultureIgnoreCase)
            .Take(20)
            .ToList();

        builder.AppendLine("## 4. 完整候选示例");
        builder.AppendLine();
        if (samples.Count == 0)
        {
            builder.AppendLine("当前显示范围内没有完整候选。请先补齐 Map 图片或 Hexzmap 地形块，或确认该关卡是否使用非同编号资源。");
            builder.AppendLine();
            return;
        }

        builder.AppendLine("| 关卡 | 标题 | 地图图片 | 图片尺寸 | Hexzmap | 主地形 | 高频地形 | 备注 |");
        builder.AppendLine("| --- | --- | --- | --- | --- | --- | --- | ---: |");
        foreach (var item in samples)
        {
            builder.AppendLine(
                $"| {Escape(item.ScenarioFileName)} | {Escape(item.ScenarioTitle)} | {Escape(MapText(item))} | {Escape(ReadImageSizeText(item.MapImagePath))} | {Escape(HexText(item))} | {Escape(item.DominantTerrain)} | {Escape(Trim(item.TopTerrainNames, 80))} | {GetNoteCount(item, creatorNoteCounts)} |");
        }
        builder.AppendLine();
    }

    private static void AppendShownDetails(
        StringBuilder builder,
        IReadOnlyList<ScenarioMapLinkInfo> shown,
        IReadOnlyDictionary<string, int>? creatorNoteCounts)
    {
        builder.AppendLine("## 5. 当前显示明细");
        builder.AppendLine();
        builder.AppendLine("| 关卡 | 类型 | 标题 | 状态 | 地图 | Hexzmap | 主地形 | 备注目标 | 建议摘要 |");
        builder.AppendLine("| --- | --- | --- | --- | --- | --- | --- | --- | --- |");
        foreach (var item in shown.Take(120))
        {
            var targetKey = BuildCreatorNoteTargetKey(item);
            var noteCount = GetNoteCount(item, creatorNoteCounts);
            var noteText = noteCount > 0 ? $"{targetKey}（{noteCount}条）" : targetKey;
            builder.AppendLine(
                $"| {Escape(item.ScenarioFileName)} | {Escape(item.ScenarioKind)} | {Escape(item.ScenarioTitle)} | {Escape(item.Status)} | {Escape(MapText(item))} | {Escape(HexText(item))} | {Escape(item.DominantTerrain)} | {Escape(noteText)} | {Escape(Trim(item.Suggestion, 90))} |");
        }

        if (shown.Count > 120)
        {
            builder.AppendLine();
            builder.AppendLine($"当前显示明细超过 120 行，报告仅列出前 120 行；如需全部字段，请同时使用“导出联动CSV”。");
        }
        builder.AppendLine();
    }

    private static void AppendWorkflowTips(StringBuilder builder)
    {
        builder.AppendLine("## 6. 创作者操作建议");
        builder.AppendLine();
        builder.AppendLine("1. 先处理“不完整联动”：缺地图图时检查 `Map` 目录，缺地形块时检查 `Hexzmap.e5` 候选切分。");
        builder.AppendLine("2. 对完整候选，使用“跳到地图浏览”和“跳到地形块”核对底图与按地图分辨率/48 划分的地形格是否视觉对应。");
        builder.AppendLine("3. 使用“导出预览PNG”保存底图/地形叠加证据，便于与美术、剧情和关卡设计记录一起归档。");
        builder.AppendLine("4. 对每个需要确认的关卡，在“创作者备注”中记录用途、修改原因、风险、待办和实机验证结果。");
        builder.AppendLine("5. 发布前再生成本报告，与发布前综合报告、差异报告、备份历史一起检查。");
        builder.AppendLine();
    }

    private static void AppendSafetyBoundary(StringBuilder builder)
    {
        builder.AppendLine("## 7. 安全边界");
        builder.AppendLine();
        builder.AppendLine("- 本报告只读分析 SV、Map 和 Hexzmap 的候选关系，不写入 Data/Imsg/Star/Ekd5/SV/EEX/Hexzmap。");
        builder.AppendLine("- Hexzmap 地形格写回、Ls/E5/EEX 解包重封包仍属于未完全确认格式，当前应继续只读研究。");
        builder.AppendLine("- 真正修改 MOD 时应保留自动备份、结构化写入报告和实机验证记录；需要文件级对比时再创建测试副本。");
        builder.AppendLine();
    }

    private static string BuildLinkRow(ScenarioMapLinkInfo item, IReadOnlyDictionary<string, int>? creatorNoteCounts)
        => $"| {Escape(item.ScenarioFileName)} | {Escape(item.ScenarioTitle)} | {Escape(item.Status)} | {Escape(MapText(item))} | {Escape(HexText(item))} | {Escape(item.DominantTerrain)} | {GetNoteCount(item, creatorNoteCounts)} | {Escape(Trim(item.Suggestion, 100))} |";

    private static bool IsIncomplete(ScenarioMapLinkInfo item)
        => item.Status.Contains("缺", StringComparison.Ordinal);

    private static string MapText(ScenarioMapLinkInfo item)
        => item.MapImageExists ? $"{item.MapImageName}（存在）" : "缺失";

    private static string HexText(ScenarioMapLinkInfo item)
        => item.HexzmapBlockExists ? $"存在 {item.HexzmapOffsetHex}" : "缺失";

    private static int GetNoteCount(ScenarioMapLinkInfo item, IReadOnlyDictionary<string, int>? creatorNoteCounts)
        => creatorNoteCounts != null && creatorNoteCounts.TryGetValue(BuildCreatorNoteTargetKey(item), out var count) ? count : 0;

    private static string BuildStatusSuggestion(string status)
        => status switch
        {
            "完整候选" => "优先抽查底图与地形格是否对齐，并为重要关卡导出预览 PNG。",
            "有地图图，缺地形块" => "确认该关是否真的使用同编号底图；若要改地形，需继续定位真实 Hexzmap 块。",
            "有地形块，缺地图图" => "确认是否缺少同编号 JPG，或该关是否使用其他底图命名规则。",
            "缺地图图和地形块" => "新增或扩展关卡前，应先补齐资源或记录非同编号引用证据。",
            "非普通关卡" => "多为索引/配置文件，通常不按 Mxxx 地图建立候选联动。",
            _ => "请结合 SV 文本、地图图片、Hexzmap 探针和实机测试继续核对。"
        };

    private static string ReadImageSizeText(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return "-";
        try
        {
            using var image = Image.FromFile(path);
            return $"{image.Width}x{image.Height}";
        }
        catch
        {
            return "无法读取";
        }
    }

    private static string Escape(string value)
        => (value ?? string.Empty)
            .Replace("|", "\\|", StringComparison.Ordinal)
            .Replace("\r\n", " ", StringComparison.Ordinal)
            .Replace('\r', ' ')
            .Replace('\n', ' ')
            .Trim();

    private static string Trim(string value, int maxChars)
    {
        value = Escape(value);
        return value.Length <= maxChars ? value : value[..maxChars] + "…";
    }
}
