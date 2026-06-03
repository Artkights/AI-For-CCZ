using System.Data;
using System.Globalization;
using System.Text.RegularExpressions;
using CCZModStudio.Formats;
using CCZModStudio.Models;

namespace CCZModStudio.Core;

/// <summary>
/// SV 命令参数引用导航服务。
/// 把结构草图中的后续 16 位词整理为可点击候选：人物、物品、策略、文本线索、关卡地图/坐标。
/// 当前仅用于可视化核对和创作者备注，不推断完整命令长度，不写回 SV/E5S。
/// </summary>
public sealed class ScenarioCommandReferenceNavigationService
{
    private const int MaxWords = 16;
    private readonly HexTableReader _reader = new();
    private readonly Dictionary<string, DataTable> _tableCache = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyList<ScenarioCommandReferenceTarget> Analyze(
        CczProject project,
        IReadOnlyList<HexTableDefinition> tables,
        ScenarioStructureRow row,
        string scenarioFileName,
        ScenarioMapLinkInfo? mapLink,
        IReadOnlyList<ScenarioTextEntry> textEntries)
    {
        if (row.NodeType != "Command候选")
        {
            return Array.Empty<ScenarioCommandReferenceTarget>();
        }

        var words = ScenarioStructureParameterExtractor.ExtractLogicalWords(row).Take(MaxWords).ToList();
        if (words.Count == 0)
        {
            return Array.Empty<ScenarioCommandReferenceTarget>();
        }

        var targets = new List<ScenarioCommandReferenceTarget>();
        AppendNamedTargets(project, tables, row, scenarioFileName, words, targets);
        AppendTextTarget(row, scenarioFileName, textEntries, targets);
        AppendCoordinateTargets(row, scenarioFileName, mapLink, words, targets);
        AppendMapTarget(row, scenarioFileName, mapLink, words, targets);

        return targets
            .GroupBy(BuildDistinctKey, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .OrderBy(target => KindRank(target.Kind))
            .ThenBy(target => target.RawValue ?? int.MaxValue)
            .ThenBy(target => target.DisplayText, StringComparer.CurrentCultureIgnoreCase)
            .Take(18)
            .ToList();
    }

    public string BuildSummary(IReadOnlyList<ScenarioCommandReferenceTarget> targets, int maxItems = 10)
    {
        if (targets.Count == 0)
        {
            return "可跳转引用候选：当前命令没有命中人物、物品、策略、文本或地图/坐标候选。";
        }

        var lines = new List<string> { $"可跳转引用候选：{targets.Count} 项（只读候选，需结合原工具和实机确认）" };
        foreach (var target in targets.Take(maxItems))
        {
            var actions = new List<string>();
            if (target.CanJumpDataTable) actions.Add("数据表");
            if (target.CanJumpScenarioText) actions.Add("文本线索");
            if (target.CanJumpScenarioMap) actions.Add("地图联动");
            lines.Add($"- [{target.Kind}] {target.DisplayText}；可跳：{(actions.Count == 0 ? "无" : string.Join("/", actions))}；依据：{target.Evidence}");
        }

        if (targets.Count > maxItems)
        {
            lines.Add($"- ……另有 {targets.Count - maxItems} 项候选，请使用右上角“命令引用”下拉框查看。");
        }

        lines.Add("安全边界：这些候选来自固定窗口内的 16 位词扫描，不代表已确认命令参数长度；不要据此直接改 SV 结构，只用于定位、备注和实机核对。");
        return string.Join(Environment.NewLine, lines);
    }

    private void AppendNamedTargets(
        CczProject project,
        IReadOnlyList<HexTableDefinition> tables,
        ScenarioStructureRow row,
        string scenarioFileName,
        IReadOnlyList<int> words,
        List<ScenarioCommandReferenceTarget> targets)
    {
        var personNames = LoadNameMap(project, tables, "6.5-0 人物");
        var strategyNames = LoadNameMap(project, tables, "6.5-5 策略");
        var itemNames = LoadItemNameMap(project, tables);

        foreach (var value in words.Distinct().Where(value => value >= 0))
        {
            if (personNames.TryGetValue(value, out var personName))
            {
                targets.Add(BuildTableTarget(
                    row,
                    scenarioFileName,
                    "人物",
                    value,
                    personName,
                    "6.5-0 人物",
                    "名称",
                    "参数值命中人物主表 ID，可能是出场、对话、测试、单挑或剧情对象。"));
            }

            if (itemNames.TryGetValue(value, out var item))
            {
                targets.Add(BuildTableTarget(
                    row,
                    scenarioFileName,
                    "物品",
                    value,
                    item.Name,
                    item.TableName,
                    "名称",
                    "参数值命中 6.5 物品表 ID，可能是获得、检查、商店、装备或奖励对象。"));
            }

            if (strategyNames.TryGetValue(value, out var strategyName))
            {
                targets.Add(BuildTableTarget(
                    row,
                    scenarioFileName,
                    "策略",
                    value,
                    strategyName,
                    "6.5-5 策略",
                    "名称",
                    "参数值命中策略主表 ID，可能是策略演出、AI、教学或效果相关对象。"));
            }
        }
    }

    private static ScenarioCommandReferenceTarget BuildTableTarget(
        ScenarioStructureRow row,
        string scenarioFileName,
        string kind,
        int value,
        string name,
        string tableName,
        string fieldName,
        string evidence)
        => new()
        {
            Kind = kind,
            DisplayText = $"{kind}{value}：{name} -> {tableName}/ID={value}/{fieldName}",
            Evidence = evidence,
            SafetyNote = "SV 命令参数长度尚未完全确认，该值只作为引用候选；跳到表格后请结合命令名、文本、地图和实机验证。",
            ScenarioFileName = scenarioFileName,
            CommandIndex = row.CommandIndex,
            CommandOffsetHex = row.OffsetHex,
            RawValue = value,
            TableName = tableName,
            RowId = value.ToString(CultureInfo.InvariantCulture),
            FieldName = fieldName
        };

    private static void AppendTextTarget(
        ScenarioStructureRow row,
        string scenarioFileName,
        IReadOnlyList<ScenarioTextEntry> textEntries,
        List<ScenarioCommandReferenceTarget> targets)
    {
        if (textEntries.Count == 0 || !IsTextRelated(row))
        {
            return;
        }

        var commandOffset = TryParseHexOffset(row.OffsetHex);
        var selected = textEntries
            .OrderByDescending(entry => ScoreTextEntry(row, entry))
            .ThenBy(entry => commandOffset.HasValue ? Math.Abs(entry.Offset - commandOffset.Value) : entry.Offset)
            .ThenBy(entry => entry.Index)
            .FirstOrDefault();
        if (selected == null)
        {
            return;
        }

        targets.Add(new ScenarioCommandReferenceTarget
        {
            Kind = "文本",
            DisplayText = $"文本#{selected.Index} {selected.Kind} {selected.OffsetHex}：{Trim(selected.Preview, 48)}",
            Evidence = $"命令“{row.CommandName}”疑似文本/剧情相关，按文本类型和偏移距离选出优先候选。",
            SafetyNote = "若要修改文本，请使用“文本线索”页原地短写回，并检查 GBK 字节容量。",
            ScenarioFileName = scenarioFileName,
            CommandIndex = row.CommandIndex,
            CommandOffsetHex = row.OffsetHex,
            TextIndex = selected.Index,
            TextOffsetHex = selected.OffsetHex
        });
    }

    private static void AppendCoordinateTargets(
        ScenarioStructureRow row,
        string scenarioFileName,
        ScenarioMapLinkInfo? mapLink,
        IReadOnlyList<int> words,
        List<ScenarioCommandReferenceTarget> targets)
    {
        if (!IsMapRelated(row))
        {
            return;
        }

        var pairs = words.Zip(words.Skip(1), (x, y) => new { X = x, Y = y })
            .Where(pair => pair.X <= 60 && pair.Y <= 60)
            .DistinctBy(pair => (pair.X, pair.Y))
            .Take(5);
        foreach (var pair in pairs)
        {
            targets.Add(new ScenarioCommandReferenceTarget
            {
                Kind = "坐标",
                DisplayText = $"坐标候选 ({pair.X},{pair.Y})" + (mapLink == null ? string.Empty : $" -> {mapLink.MapId}"),
                Evidence = "相邻 16 位词落在常见战场坐标范围，且命令/引用提示与地图、地点、移动或区域判断有关。",
                SafetyNote = "坐标候选需要对照地图底图和按地图分辨率/48 划分的 Hexzmap 地形格；当前不推断真实参数长度。",
                ScenarioFileName = scenarioFileName,
                CommandIndex = row.CommandIndex,
                CommandOffsetHex = row.OffsetHex,
                MapId = mapLink?.MapId ?? string.Empty,
                CoordinateX = pair.X,
                CoordinateY = pair.Y
            });
        }
    }

    private static void AppendMapTarget(
        ScenarioStructureRow row,
        string scenarioFileName,
        ScenarioMapLinkInfo? mapLink,
        IReadOnlyList<int> words,
        List<ScenarioCommandReferenceTarget> targets)
    {
        if (mapLink != null && IsMapRelated(row))
        {
            targets.Add(new ScenarioCommandReferenceTarget
            {
                Kind = "地图",
                DisplayText = $"{scenarioFileName} -> {mapLink.MapId}（{mapLink.Status}）",
                Evidence = "当前命令疑似地图/坐标相关；同关卡已有关卡地图联动候选。",
                SafetyNote = "请在关卡地图联动页同时核对 SV、Map 图片和 Hexzmap 地形块。",
                ScenarioFileName = scenarioFileName,
                CommandIndex = row.CommandIndex,
                CommandOffsetHex = row.OffsetHex,
                MapId = mapLink.MapId
            });
            return;
        }

        var mapValue = words.FirstOrDefault(value => value is > 0 and <= 999);
        if (mapValue > 0 && row.CommandName.Contains("地图", StringComparison.Ordinal))
        {
            var mapId = "M" + mapValue.ToString("000", CultureInfo.InvariantCulture);
            targets.Add(new ScenarioCommandReferenceTarget
            {
                Kind = "地图",
                DisplayText = $"地图编号候选 {mapId}",
                Evidence = "命令名含地图，参数值落在 M000-M999 编号范围。",
                SafetyNote = "未确认该值一定是地图编号；请在关卡地图联动页核对。",
                ScenarioFileName = scenarioFileName,
                CommandIndex = row.CommandIndex,
                CommandOffsetHex = row.OffsetHex,
                MapId = mapId
            });
        }
    }

    private IReadOnlyDictionary<int, string> LoadNameMap(CczProject project, IReadOnlyList<HexTableDefinition> tables, string tableName)
    {
        try
        {
            var table = tables.FirstOrDefault(item => item.TableName == tableName);
            if (table == null) return new Dictionary<int, string>();
            var data = ReadTable(project, tables, table);
            if (!data.Columns.Contains("ID")) return new Dictionary<int, string>();
            var nameColumn = data.Columns.Contains("名称")
                ? "名称"
                : data.Columns.Cast<DataColumn>().FirstOrDefault(column => column.ColumnName.Contains("名", StringComparison.Ordinal))?.ColumnName;
            if (string.IsNullOrWhiteSpace(nameColumn)) return new Dictionary<int, string>();

            var result = new Dictionary<int, string>();
            foreach (DataRow row in data.Rows)
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

    private IReadOnlyDictionary<int, ItemReference> LoadItemNameMap(CczProject project, IReadOnlyList<HexTableDefinition> tables)
    {
        var result = new Dictionary<int, ItemReference>();
        foreach (var tableName in new[] { "6.5-1 物品（0-103）", "6.5-2 物品（104-255）" })
        {
            foreach (var pair in LoadNameMap(project, tables, tableName))
            {
                result[pair.Key] = new ItemReference(tableName, pair.Value);
            }
        }

        return result;
    }

    private DataTable ReadTable(CczProject project, IReadOnlyList<HexTableDefinition> tables, HexTableDefinition table)
    {
        var key = project.GameRoot + "|" + table.TableName;
        if (_tableCache.TryGetValue(key, out var cached)) return cached;
        var read = _reader.Read(project, table, tables);
        _tableCache[key] = read.Data;
        return read.Data;
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
           || row.CommandName.Contains("坐标", StringComparison.Ordinal)
           || row.CommandName.Contains("进入", StringComparison.Ordinal)
           || row.CommandName.Contains("移动", StringComparison.Ordinal);

    private static int ScoreTextEntry(ScenarioStructureRow row, ScenarioTextEntry entry)
    {
        var score = 0;
        if (entry.Kind.Contains("标题", StringComparison.Ordinal) || entry.Kind.Contains("场所", StringComparison.Ordinal)) score += 12;
        if (entry.Kind.Contains("胜败条件", StringComparison.Ordinal)) score += 10;
        if (entry.Kind.Contains("对白", StringComparison.Ordinal) || entry.Kind.Contains("说明", StringComparison.Ordinal)) score += 8;
        if (entry.HasNewLines) score += 3;
        if (row.CommandName.Contains("胜利", StringComparison.Ordinal) && entry.Text.Contains("胜利", StringComparison.Ordinal)) score += 20;
        if (row.CommandName.Contains("失败", StringComparison.Ordinal) && entry.Text.Contains("失败", StringComparison.Ordinal)) score += 20;
        if (row.CommandName.Contains("场所", StringComparison.Ordinal) && entry.Kind.Contains("场所", StringComparison.Ordinal)) score += 20;
        if (row.CommandName.Contains("信息", StringComparison.Ordinal) && entry.Kind.Contains("标题", StringComparison.Ordinal)) score += 8;
        return score;
    }

    private static int? TryParseHexOffset(string offsetHex)
    {
        if (string.IsNullOrWhiteSpace(offsetHex)) return null;
        var text = offsetHex.Trim();
        if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) text = text[2..];
        return int.TryParse(text, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var value) ? value : null;
    }

    private static string Trim(string value, int maxChars)
    {
        value = (value ?? string.Empty).Replace("\r\n", "\\n", StringComparison.Ordinal).Replace('\r', '\n').Replace("\n", "\\n", StringComparison.Ordinal);
        return value.Length <= maxChars ? value : value[..maxChars] + "…";
    }

    private static string BuildDistinctKey(ScenarioCommandReferenceTarget target)
        => $"{target.Kind}|{target.TableName}|{target.RowId}|{target.FieldName}|{target.MapId}|{target.TextIndex}|{target.TextOffsetHex}|{target.CoordinateX}|{target.CoordinateY}";

    private static int KindRank(string kind) => kind switch
    {
        "文本" => 0,
        "地图" => 1,
        "坐标" => 2,
        "人物" => 3,
        "物品" => 4,
        "策略" => 5,
        _ => 9
    };

    private sealed record ItemReference(string TableName, string Name);
}
