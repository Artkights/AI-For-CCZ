using System.Globalization;
using System.Data;
using System.Text;
using System.Text.RegularExpressions;
using CCZModStudio.Models;
using CCZModStudio.Formats;

namespace CCZModStudio.Core;

/// <summary>
/// 把 SV/E5S 结构草图中的单个节点整理成创作者可读的中文解释。
/// 当前只做“命令候选、文本线索、地图”的只读汇总，不作为剧本写回依据。
/// </summary>
public sealed class ScenarioStructureNodeDetailService
{
    private readonly ScenarioCommandParameterTemplateService _commandParameterTemplateService = new();

    public string BuildDetail(
        ScenarioStructureRow row,
        string scenarioFileName,
        IReadOnlyList<ScenarioTextEntry> textEntries,
        CczProject? project = null,
        IReadOnlyList<HexTableDefinition>? tables = null,
        int maxTextItems = 6)
    {
        var builder = new StringBuilder();
        builder.AppendLine($"剧本节点详情：{scenarioFileName}");
        builder.AppendLine($"节点类型：{row.NodeType}    Scene：{FormatIndex(row.SceneIndex)}    Section：{FormatIndex(row.SectionIndex)}    Command：{FormatIndex(row.CommandIndex)}");
        builder.AppendLine($"偏移：{ValueOrDash(row.OffsetHex)}    命令：{ValueOrDash(row.CommandIdHex)} {ValueOrDash(row.CommandName)}    置信度：{ValueOrDash(row.Confidence)}");

        if (!string.IsNullOrWhiteSpace(row.ParameterPreview))
        {
            builder.AppendLine($"参数预览：{row.ParameterPreview}");
        }
        if (!string.IsNullOrWhiteSpace(row.RawContextWordsHex) && !string.Equals(row.RawContextWordsHex, row.ParameterPreview, StringComparison.Ordinal))
        {
            builder.AppendLine($"原始上下文词：{row.RawContextWordsHex}");
        }
        if (!string.IsNullOrWhiteSpace(row.LegacyParameterLayout))
        {
            builder.AppendLine($"旧版参数布局：{row.LegacyParameterLayout}");
        }
        AppendLegacyStructureFlags(builder, row);
        AppendParameterGroups(builder, row, BuildNameLookups(project, tables));
        builder.AppendLine(_commandParameterTemplateService.BuildTemplateDetail(row, project, tables));
        if (!string.IsNullOrWhiteSpace(row.ReferenceHint))
        {
            builder.AppendLine($"跨表/资源候选：{row.ReferenceHint}");
        }
        if (!string.IsNullOrWhiteSpace(row.Annotation))
        {
            builder.AppendLine($"中文注释：{row.Annotation}");
        }

        AppendScenarioTextHints(builder, row, textEntries, maxTextItems);
        builder.AppendLine();
        builder.AppendLine("创作提示：该详情用于定位剧情、文本、地图和资源候选。由于 SV/E5S 完整命令参数长度尚未完全确认，当前结论只读参考；若要改文字，请优先使用“文本线索”页的原地短写回；若要改地图/地形，请先核对“关卡地图”和 Hexzmap 探针。");
        return builder.ToString();
    }

    private static void AppendParameterGroups(StringBuilder builder, ScenarioStructureRow row, NameLookups lookups)
    {
        var words = ScenarioStructureParameterExtractor.ExtractLogicalWords(row).Take(16).ToList();
        if (words.Count == 0)
        {
            return;
        }

        builder.AppendLine();
        builder.AppendLine("参数分组解释（只读候选）：");
        builder.AppendLine("- 逻辑参数候选：" + string.Join(" ", words.Select(word => $"0x{word:X4}({word})")));
        var rawWords = ScenarioStructureParameterExtractor.ExtractRawContextWords(row).Take(16).ToList();
        if (rawWords.Count > 0)
        {
            builder.AppendLine("- 原始上下文 16 位词：" + string.Join(" ", rawWords.Select(word => $"0x{word:X4}({word})")));
        }
        builder.AppendLine("- 说明：当前尚未确认每条命令的真实参数长度；若旧版解析器已识别逻辑参数，则优先展示逻辑参数，同时保留原始上下文词供交叉核对。");

        var personRefs = BuildKnownReferences(words, lookups.PersonNames, "人物");
        if (personRefs.Count > 0)
        {
            builder.AppendLine("- 人物候选：" + string.Join(" / ", personRefs));
        }

        var itemRefs = BuildKnownReferences(words, lookups.ItemNames, "物品");
        if (itemRefs.Count > 0)
        {
            builder.AppendLine("- 物品/装备候选：" + string.Join(" / ", itemRefs));
        }

        var strategyRefs = BuildKnownReferences(words, lookups.StrategyNames, "策略");
        if (strategyRefs.Count > 0)
        {
            builder.AppendLine("- 策略候选：" + string.Join(" / ", strategyRefs));
        }

        var coordinatePairs = words
            .Zip(words.Skip(1), (x, y) => new { X = x, Y = y })
            .Where(pair => pair.X <= 60 && pair.Y <= 60)
            .DistinctBy(pair => (pair.X, pair.Y))
            .Take(8)
            .Select(pair => $"({pair.X},{pair.Y})")
            .ToList();
        if (coordinatePairs.Count > 0)
        {
            builder.AppendLine("- 坐标候选：" + string.Join(" / ", coordinatePairs) + "；若该命令与地图、移动、入场或区域判断有关，请重点核对。");
        }

        var mapRefs = words
            .Where(word => word is >= 0 and <= 999)
            .Distinct()
            .Take(8)
            .Select(word => "M" + word.ToString("000", CultureInfo.InvariantCulture))
            .ToList();
        if (mapRefs.Count > 0)
        {
            builder.AppendLine("- 地图编号候选：" + string.Join(" / ", mapRefs));
        }

        var flowValues = words
            .Where(word => word <= 0x00FF)
            .Distinct()
            .Take(10)
            .Select(word => $"0x{word:X2}/{word}")
            .ToList();
        if (flowValues.Count > 0)
        {
            builder.AppendLine("- 流程/变量/开关小数值候选：" + string.Join(" / ", flowValues) + "；条件测试、变量赋值、case/else 类命令尤其需要注意。");
        }
    }

    private static void AppendScenarioTextHints(StringBuilder builder, ScenarioStructureRow row, IReadOnlyList<ScenarioTextEntry> textEntries, int maxTextItems)
    {
        if (textEntries.Count == 0)
        {
            builder.AppendLine("同文件文本线索：未扫描到可展示的 GBK 文本线索。");
            return;
        }

        var isTextRelated = IsTextRelated(row);
        var commandOffset = TryParseHexOffset(row.OffsetHex);
        var selected = textEntries
            .OrderByDescending(entry => ScoreTextEntry(row, entry, isTextRelated))
            .ThenBy(entry => commandOffset.HasValue ? Math.Abs(entry.Offset - commandOffset.Value) : entry.Offset)
            .ThenBy(entry => entry.Index)
            .Take(Math.Max(1, maxTextItems))
            .ToList();

        builder.AppendLine();
        builder.AppendLine(isTextRelated
            ? $"同文件文本线索（该命令疑似与文本/剧情演出有关，列出 {selected.Count}/{textEntries.Count} 条优先候选）："
            : $"同文件关键文本概览（用于确认当前关卡标题、胜败条件或主要说明，列出 {selected.Count}/{textEntries.Count} 条）：");

        foreach (var text in selected)
        {
            builder.AppendLine($"- #{text.Index} {text.Kind} {text.OffsetHex} 容量 {text.ByteLength}B：{Trim(text.Preview, 80)}");
            if (!string.IsNullOrWhiteSpace(text.Annotation))
            {
                builder.AppendLine($"  说明：{Trim(text.Annotation, 120)}");
            }
        }
    }
    private static int ScoreTextEntry(ScenarioStructureRow row, ScenarioTextEntry entry, bool isTextRelated)
    {
        var score = 0;
        if (isTextRelated) score += 20;
        if (entry.Kind.Contains("标题", StringComparison.Ordinal) || entry.Kind.Contains("场所", StringComparison.Ordinal)) score += 12;
        if (entry.Kind.Contains("胜败条件", StringComparison.Ordinal)) score += 10;
        if (entry.Kind.Contains("对白", StringComparison.Ordinal) || entry.Kind.Contains("说明", StringComparison.Ordinal)) score += 8;
        if (entry.HasNewLines) score += 3;
        if (row.CommandName.Contains("胜利", StringComparison.Ordinal) && entry.Text.Contains("胜利", StringComparison.Ordinal)) score += 20;
        if (row.CommandName.Contains("失败", StringComparison.Ordinal) && entry.Text.Contains("失败", StringComparison.Ordinal)) score += 20;
        if (row.CommandName.Contains("场所", StringComparison.Ordinal) && entry.Kind.Contains("场所", StringComparison.Ordinal)) score += 20;
        return score;
    }

    private static bool IsTextRelated(ScenarioStructureRow row)
        => row.ReferenceHint.Contains("文本线索", StringComparison.Ordinal)
           || row.CommandName.Contains("对话", StringComparison.Ordinal)
           || row.CommandName.Contains("信息", StringComparison.Ordinal)
           || row.CommandName.Contains("旁白", StringComparison.Ordinal)
           || row.CommandName.Contains("场所", StringComparison.Ordinal)
           || row.CommandName.Contains("胜利条件", StringComparison.Ordinal)
           || row.CommandName.Contains("文字", StringComparison.Ordinal)
           || row.CommandName.Contains("剧情", StringComparison.Ordinal);

    private static bool IsMapRelated(ScenarioStructureRow row)
        => row.ReferenceHint.Contains("地图", StringComparison.Ordinal)
           || row.ReferenceHint.Contains("坐标", StringComparison.Ordinal)
           || row.CommandName.Contains("地图", StringComparison.Ordinal)
           || row.CommandName.Contains("绘图", StringComparison.Ordinal)
           || row.CommandName.Contains("背景", StringComparison.Ordinal)
           || row.CommandName.Contains("物体", StringComparison.Ordinal)
           || row.CommandName.Contains("地点", StringComparison.Ordinal)
           || row.CommandName.Contains("坐标", StringComparison.Ordinal);

    private static int? TryParseHexOffset(string offsetHex)
    {
        if (string.IsNullOrWhiteSpace(offsetHex)) return null;
        offsetHex = offsetHex.Trim();
        if (offsetHex.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            offsetHex = offsetHex[2..];
        }
        return int.TryParse(offsetHex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var value)
            ? value
            : null;
    }

    private static string FormatIndex(int value) => value > 0 ? value.ToString(CultureInfo.InvariantCulture) : "-";

    private static string ValueOrDash(string value) => string.IsNullOrWhiteSpace(value) ? "-" : value;

    private static IReadOnlyList<string> BuildKnownReferences(IReadOnlyList<int> words, IReadOnlyDictionary<int, string> names, string label)
    {
        if (names.Count == 0) return Array.Empty<string>();
        return words
            .Where(names.ContainsKey)
            .Distinct()
            .Take(8)
            .Select(id => $"{label}{id}:{names[id]}")
            .ToList();
    }

    private static NameLookups BuildNameLookups(CczProject? project, IReadOnlyList<HexTableDefinition>? tables)
    {
        if (project == null || tables == null || tables.Count == 0)
        {
            return NameLookups.Empty;
        }

        var reader = new HexTableReader();
        var itemNames = new Dictionary<int, string>();
        foreach (var table in HexTableNameResolver.ResolveItemTables(project, tables))
        {
            foreach (var pair in LoadNameMap(project, tables, reader, table.TableName))
            {
                itemNames[pair.Key] = pair.Value;
            }
        }

        return new NameLookups(
            LoadNameMap(project, tables, reader, "6.5-0 人物"),
            itemNames,
            LoadNameMap(project, tables, reader, "6.5-5 策略"));
    }

    private static IReadOnlyDictionary<int, string> LoadNameMap(CczProject project, IReadOnlyList<HexTableDefinition> tables, HexTableReader reader, string tableName)
    {
        try
        {
            if (!HexTableNameResolver.TryResolveForProject(project, tables, tableName, out var table)) return new Dictionary<int, string>();
            var read = reader.Read(project, table, tables);
            if (!read.Validation.IsUsable || !read.Data.Columns.Contains("ID")) return new Dictionary<int, string>();
            var nameColumn = read.Data.Columns.Contains("名称")
                ? "名称"
                : read.Data.Columns.Cast<DataColumn>().FirstOrDefault(column => column.ColumnName.Contains("名", StringComparison.Ordinal))?.ColumnName;
            if (string.IsNullOrWhiteSpace(nameColumn)) return new Dictionary<int, string>();

            var result = new Dictionary<int, string>();
            foreach (DataRow row in read.Data.Rows)
            {
                var id = Convert.ToInt32(row["ID"], CultureInfo.InvariantCulture);
                var name = Convert.ToString(row[nameColumn], CultureInfo.InvariantCulture)?.Trim() ?? string.Empty;
                if (!string.IsNullOrWhiteSpace(name))
                {
                    result[id] = name;
                }
            }
            return result;
        }
        catch
        {
            return new Dictionary<int, string>();
        }
    }

    private static string Trim(string value, int maxChars)
    {
        value = (value ?? string.Empty).Replace("\r\n", "\\n", StringComparison.Ordinal).Replace('\r', '\n').Replace("\n", "\\n", StringComparison.Ordinal);
        return value.Length <= maxChars ? value : value[..maxChars] + "...";
    }

    private static void AppendLegacyStructureFlags(StringBuilder builder, ScenarioStructureRow row)
    {
        var flags = new List<string>();
        if (row.StartsBodyBlock) flags.Add("正文根");
        if (row.OpensSubEventBlock) flags.Add("子事件载体");
        if (row.EndsSubEventBlock) flags.Add("子事件结束");
        if (flags.Count == 0) return;

        builder.AppendLine($"旧版结构标记：{string.Join(" / ", flags)}");
    }

    private sealed record NameLookups(
        IReadOnlyDictionary<int, string> PersonNames,
        IReadOnlyDictionary<int, string> ItemNames,
        IReadOnlyDictionary<int, string> StrategyNames)
    {
        public static NameLookups Empty { get; } = new(
            new Dictionary<int, string>(),
            new Dictionary<int, string>(),
            new Dictionary<int, string>());
    }
}
