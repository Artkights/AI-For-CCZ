using System.Globalization;
using System.Text;
using CCZModStudio.Models;

namespace CCZModStudio.Core;

/// <summary>
/// R/S eex 命令引用候选核对清单。
/// 把结构草图里的“可跳转引用候选”导出为中文 Markdown，供逐项核对人物、物品、策略、文本、地图/坐标与核对记录。
/// 报告只写入 CCZModStudio_Reports，不修改任何游戏文件。
/// </summary>
public sealed class ScenarioCommandReferenceChecklistService
{
    private readonly ScenarioCommandReferenceNavigationService _referenceNavigationService = new();

    public string WriteReport(
        CczProject project,
        IReadOnlyList<HexTableDefinition> tables,
        ScenarioStructureProbeResult structure,
        IReadOnlyList<ScenarioStructureRow>? rows,
        IReadOnlyList<ScenarioTextEntry> textEntries)
    {
        var reportRoot = Path.Combine(project.WorkspaceRoot, "CCZModStudio_Reports");
        Directory.CreateDirectory(reportRoot);
        var stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);
        var baseName = Path.GetFileNameWithoutExtension(structure.FileName);
        var reportPath = Path.Combine(reportRoot, $"{stamp}_{MakeSafeFileName(baseName)}_RS命令引用核对清单.md");
        File.WriteAllText(reportPath, BuildReport(project, tables, structure, rows, textEntries), Encoding.UTF8);
        return reportPath;
    }

    public string BuildReport(
        CczProject project,
        IReadOnlyList<HexTableDefinition> tables,
        ScenarioStructureProbeResult structure,
        IReadOnlyList<ScenarioStructureRow>? rows,
        IReadOnlyList<ScenarioTextEntry> textEntries)
    {
        var shownRows = rows is { Count: > 0 } ? rows : structure.Rows;
        var commandRows = shownRows.Where(row => row.NodeType == "Command候选").ToList();
        var allCommandRows = structure.Rows.Where(row => row.NodeType == "Command候选").ToList();        var commandTargets = BuildCommandTargets(project, tables, structure, commandRows, textEntries);
        var commandTargetCount = commandTargets.Sum(item => item.Targets.Count);
        var commandsWithTargets = commandTargets.Count(item => item.Targets.Count > 0);

        var builder = new StringBuilder();
        builder.AppendLine("# CCZModStudio R/S eex 命令引用核对清单");
        builder.AppendLine();
        builder.AppendLine($"- 生成时间：{DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        builder.AppendLine($"- 项目目录：`{Escape(project.GameRoot)}`");
        builder.AppendLine($"- 工作区：`{Escape(project.WorkspaceRoot)}`");
        builder.AppendLine($"- 剧本文件：`{Escape(structure.FileName)}`");
        builder.AppendLine($"- 当前模式：{(project.IsTestCopy ? "测试副本（可写，可做差异对比）" : "当前项目（可写）")}");
        builder.AppendLine($"- 报告范围：命令 {commandRows.Count}/{allCommandRows.Count}，引用候选 {commandTargetCount}，有候选命令 {commandsWithTargets}");
        builder.AppendLine();
        builder.AppendLine("> 安全边界：本清单来自结构草图的固定窗口 16 位词扫描，只用于定位、核对记录、对照旧工具和实机验证；它不证明完整命令长度，不作为 R/S eex 完整结构写回依据。报告只写入 `CCZModStudio_Reports`，不修改任何游戏文件。");
        builder.AppendLine();

        AppendOverview(builder, commandRows, commandTargets);
        AppendPriorityChecklist(builder, commandTargets);
        AppendCommandDetails(builder, commandTargets);
        AppendProductionTips(builder);
        return builder.ToString();
    }

    private List<CommandTargets> BuildCommandTargets(
        CczProject project,
        IReadOnlyList<HexTableDefinition> tables,
        ScenarioStructureProbeResult structure,
        IReadOnlyList<ScenarioStructureRow> commandRows,
        IReadOnlyList<ScenarioTextEntry> textEntries)
    {
        var result = new List<CommandTargets>();
        foreach (var row in commandRows)
        {
            var targets = _referenceNavigationService.Analyze(project, tables, row, structure.FileName, textEntries);
            result.Add(new CommandTargets(row, targets));
        }

        return result;
    }

    private static void AppendOverview(StringBuilder builder, IReadOnlyList<ScenarioStructureRow> rows, IReadOnlyList<CommandTargets> commandTargets)
    {
        var targetKinds = commandTargets
            .SelectMany(item => item.Targets)
            .GroupBy(target => target.Kind)
            .OrderByDescending(group => group.Count())
            .ThenBy(group => group.Key, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
        var commandsWithText = commandTargets.Count(item => item.Targets.Any(target => target.CanJumpScenarioText));
        var commandsWithMap = commandTargets.Count(item => item.Targets.Any(target => target.CanJumpScenarioMap));
        var commandsWithTable = commandTargets.Count(item => item.Targets.Any(target => target.CanJumpDataTable));

        builder.AppendLine("## 1. 总览");
        builder.AppendLine();
        builder.AppendLine("| 项目 | 数量 | 创作者解释 |");
        builder.AppendLine("| --- | ---: | --- |");
        builder.AppendLine($"| 当前命令行 | {rows.Count} | 本次参与核对的 Command 候选数量。 |");
        builder.AppendLine($"| 有数据表候选的命令 | {commandsWithTable} | 可跳到人物/物品/策略等数据表核对含义。 |");
        builder.AppendLine($"| 有文本候选的命令 | {commandsWithText} | 可跳到同文件文本线索核对剧情文字。 |");
        builder.AppendLine($"| 有地图/坐标候选的命令 | {commandsWithMap} | 可跳到关卡地图核对底图、地形和坐标。 |");
        builder.AppendLine();

        builder.AppendLine("### 候选类型分布");
        builder.AppendLine();
        builder.AppendLine("| 类型 | 数量 | 建议 |");
        builder.AppendLine("| --- | ---: | --- |");
        if (targetKinds.Count == 0)
        {
            builder.AppendLine("| 无 | 0 | 当前范围没有命中可跳转候选，可扩大结构筛选或换关卡继续分析。 |");
        }
        else
        {
            foreach (var group in targetKinds)
            {
                builder.AppendLine($"| {Escape(group.Key)} | {group.Count()} | {Escape(BuildKindSuggestion(group.Key))} |");
            }
        }
        builder.AppendLine();
    }

    private static void AppendPriorityChecklist(StringBuilder builder, IReadOnlyList<CommandTargets> commandTargets)
    {
        var rows = commandTargets
            .Where(item => item.Targets.Count > 0)
            .OrderByDescending(item => item.Targets.Any(target => target.CanJumpScenarioMap))
            .ThenByDescending(item => item.Targets.Any(target => target.CanJumpScenarioText))
            .ThenByDescending(item => item.Targets.Count)
            .ThenBy(item => item.Row.CommandIndex)
            .Take(80)
            .ToList();

        builder.AppendLine("## 2. 优先核对清单");
        builder.AppendLine();
        builder.AppendLine("| 命令 | 候选 | 可跳转 | 核对记录 | 核对建议 |");
        builder.AppendLine("| --- | --- | --- | ---: | --- |");
        if (rows.Count == 0)
        {
            builder.AppendLine("| - | 未命中可跳转引用候选 | - | 0 | 可先查看结构草图详情和原工具命令解释。 |");
            builder.AppendLine();
            return;
        }

        foreach (var item in rows)
        {
            var firstTargets = item.Targets.Take(4).ToList();
            var targetText = string.Join("<br>", firstTargets.Select(target => Escape(target.DisplayText)));
            if (item.Targets.Count > firstTargets.Count)
            {
                targetText += $"<br>……另有 {item.Targets.Count - firstTargets.Count} 项";
            }

            var jumpText = string.Join(" / ", item.Targets
                .SelectMany(BuildJumpKinds)
                .Distinct(StringComparer.Ordinal)
                .DefaultIfEmpty("-"));
            var noteCount = 0;
            builder.AppendLine(
                $"| {Escape(BuildCommandLabel(item.Row))} | {targetText} | {Escape(jumpText)} | {noteCount} | {Escape(BuildCommandSuggestion(item.Targets))} |");
        }
        builder.AppendLine();
    }

    private static void AppendCommandDetails(StringBuilder builder, IReadOnlyList<CommandTargets> commandTargets)
    {
        builder.AppendLine("## 3. 命令明细");
        builder.AppendLine();
        foreach (var item in commandTargets.Where(item => item.Targets.Count > 0).Take(120))
        {
            var noteCount = 0;
            builder.AppendLine($"### {Escape(BuildCommandLabel(item.Row))}");
            builder.AppendLine();
            builder.AppendLine($"- 参数预览：`{Escape(item.Row.ParameterPreview)}`");
            builder.AppendLine($"- 模板提示：{Escape(string.IsNullOrWhiteSpace(item.Row.CommandTemplateHint) ? "暂无专用模板" : item.Row.CommandTemplateHint)}");
            builder.AppendLine($"- 引用提示：{Escape(string.IsNullOrWhiteSpace(item.Row.ReferenceHint) ? "暂无" : item.Row.ReferenceHint)}");
            builder.AppendLine($"- ：{noteCount} 条");
            builder.AppendLine();
            builder.AppendLine("| 类型 | 目标 | 依据 | 安全提示 |");
            builder.AppendLine("| --- | --- | --- | --- |");
            foreach (var target in item.Targets)
            {
                builder.AppendLine($"| {Escape(target.Kind)} | {Escape(target.DisplayText)} | {Escape(target.Evidence)} | {Escape(target.SafetyNote)} |");
            }
            builder.AppendLine();
        }
    }

    private static void AppendProductionTips(StringBuilder builder)
    {
        builder.AppendLine("## 4. 推荐工作流");
        builder.AppendLine();
        builder.AppendLine("1. 在工具中选中同一命令行，先用 `命令引用` 下拉框逐项跳转核对。");
        builder.AppendLine("2. 数据表候选：检查 ID、名称、说明、跨表引用是否符合剧情设计。");
        builder.AppendLine("3. 文本候选：确认 GBK 字节容量，必要时使用原地等长/缩短写回。");
        builder.AppendLine("4. 地图/坐标候选：打开关卡地图预览，核对 Map 底图与按地图分辨率/48 划分的 Hexzmap 地形格。");
        builder.AppendLine("5. 对无法确认的候选补充外部制作记录，记录旧工具截图、实机测试结果和备份文件。");
        builder.AppendLine();
    }
    private static IEnumerable<string> BuildJumpKinds(ScenarioCommandReferenceTarget target)
    {
        if (target.CanJumpDataTable) yield return "数据表";
        if (target.CanJumpScenarioText) yield return "文本线索";
        if (target.CanJumpScenarioMap) yield return "地图";
    }

    private static string BuildCommandLabel(ScenarioStructureRow row)
        => $"#{row.CommandIndex} {row.OffsetHex} {row.CommandIdHex}/{row.CommandName}";

    private static string BuildKindSuggestion(string kind) => kind switch
    {
        "人物" => "核对出场、对话对象、单挑或条件测试是否指向正确人物。",
        "物品" => "核对奖励、获得、装备或商店相关设计，并检查物品说明和特效。",
        "策略" => "核对策略演出、AI 或教学相关设计，并进战场测试效果。",
        "文本" => "跳到文本线索核对对白、标题、场所或胜败条件。",
        "地图" or "坐标" => "跳到关卡地图核对底图、地形块和坐标范围。",
        _ => "结合命令名、参数模板、原工具和实机验证。"
    };

    private static string BuildCommandSuggestion(IReadOnlyList<ScenarioCommandReferenceTarget> targets)
    {
        if (targets.Any(target => target.CanJumpScenarioMap)) return "优先核对地图/坐标，再确认关联人物或物品。";
        if (targets.Any(target => target.CanJumpScenarioText)) return "优先核对文本线索和 GBK 容量。";
        if (targets.Any(target => target.Kind == "人物")) return "优先确认人物 ID 是否符合剧情对象。";
        if (targets.Any(target => target.Kind == "物品")) return "优先确认物品 ID、说明和特效。";
        return "逐项跳转核对，并为不确定项添加。";
    }

    private static string Escape(string? value)
    {
        value ??= string.Empty;
        return value
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("|", "\\|", StringComparison.Ordinal)
            .Replace("\r\n", "<br>", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Replace("\n", "<br>", StringComparison.Ordinal);
    }

    private static string MakeSafeFileName(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var chars = value.Select(ch => invalid.Contains(ch) ? '_' : ch).ToArray();
        var text = new string(chars).Trim();
        return string.IsNullOrWhiteSpace(text) ? "SV" : text;
    }

    private sealed record CommandTargets(ScenarioStructureRow Row, IReadOnlyList<ScenarioCommandReferenceTarget> Targets);
}
