using System.Data;
using System.Globalization;
using CCZModStudio.Core;
using CCZModStudio.Models;

namespace CCZModStudio.Formats;

/// <summary>
/// 数据表跨表引用诊断。
/// 面向 MOD 创作者，把人物、物品、兵种、商店、专属装备等常改字段扫描成可跳转的诊断项。
/// 这里只检查已经能够确定的表格编号关系；未确认的引擎内部枚举继续只给说明，不做越界判定。
/// </summary>
public sealed class TableReferenceDiagnosticService
{
    private const int MaxInvalidRowsPerRule = 40;
    private readonly HexTableReader _reader = new();
    private readonly Dictionary<string, DataTable> _dataCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly ItemEffectCatalogService _itemEffectCatalogService = new();

    public IReadOnlyList<ResourceDiagnosticItem> Analyze(CczProject project, IReadOnlyList<HexTableDefinition> tables)
    {
        _dataCache.Clear();
        var tableLookup = tables.ToDictionary(x => x.TableName, StringComparer.OrdinalIgnoreCase);
        var rules = BuildRules(tables)
            .Where(rule => tableLookup.TryGetValue(rule.SourceTableName, out var source) &&
                           source.Fields.Any(field => field.ColumnName == rule.FieldName))
            .ToList();

        var diagnostics = new List<ResourceDiagnosticItem>();
        var scanResults = new List<RuleScanResult>();

        foreach (var rule in rules)
        {
            try
            {
                scanResults.Add(AnalyzeRule(project, tables, tableLookup, rule));
            }
            catch (Exception ex)
            {
                diagnostics.Add(new ResourceDiagnosticItem
                {
                    Severity = "Info",
                    Category = $"表格引用/{rule.ReferenceKind}",
                    Rule = "跨表引用诊断跳过",
                    Id = "0",
                    Name = $"{rule.SourceTableName}/{rule.FieldName}",
                    Status = ex.Message,
                    Detail = $"源表：{rule.SourceTableName}；行 ID：0；字段：{rule.FieldName}；目标：{rule.TargetDisplayName}；跳过原因：{ex.Message}",
                    Suggestion = "该字段的跨表诊断暂时跳过；可先用数据表页查看中文字段说明，并结合原工具或实机继续确认。",
                    Path = TryResolveSourcePath(project, tableLookup, rule.SourceTableName)
                });
            }
        }

        var totalCells = scanResults.Sum(x => x.CheckedCells);
        var totalInvalid = scanResults.Sum(x => x.InvalidRows.Count);
        var totalEmpty = scanResults.Sum(x => x.EmptyLikeCells);
        diagnostics.Add(new ResourceDiagnosticItem
        {
            Severity = totalInvalid > 0 ? "Warn" : "Info",
            Category = "表格引用/数据表",
            Rule = "跨表引用总览",
            Id = "0",
            Name = "人物/物品/兵种/商店/专属装备",
            Status = $"规则 {scanResults.Count}，检查单元格 {totalCells}，越界/未命中 {totalInvalid}，空槽/无效果/特殊值 {totalEmpty}",
            Detail = "源表：6.5-0 人物；行 ID：0；字段：职业；目标：6.5-4 详细兵种；本总览汇总已确认的跨表编号关系，包括人物职业、台词、兵种特效分配、专属装备、商店物品槽位和装备特效号。",
            Suggestion = totalInvalid > 0
                ? "请优先筛选“越界引用/未命中引用”；选中后可点“跳到数据表”直接定位到具体单元格，再结合字段中文注释修正。"
                : "当前已确认跨表引用没有发现越界项。后续修改人物职业、商店物品、专属装备或装备特效号后，建议重新运行资源诊断复查。",
            Path = project.GameRoot
        });

        foreach (var result in scanResults)
        {
            diagnostics.Add(BuildRuleSummaryDiagnostic(project, tableLookup, result));
            diagnostics.AddRange(result.InvalidRows
                .Take(MaxInvalidRowsPerRule)
                .Select(row => BuildInvalidRowDiagnostic(project, tableLookup, result.Rule, row)));

            if (result.InvalidRows.Count > MaxInvalidRowsPerRule)
            {
                diagnostics.Add(new ResourceDiagnosticItem
                {
                    Severity = "Info",
                    Category = $"表格引用/{result.Rule.ReferenceKind}",
                    Rule = "越界引用已截断",
                    Id = "0",
                    Name = $"{result.Rule.SourceTableName}/{result.Rule.FieldName}",
                    Status = $"仅显示前 {MaxInvalidRowsPerRule} 条，另有 {result.InvalidRows.Count - MaxInvalidRowsPerRule} 条",
                    Detail = $"源表：{result.Rule.SourceTableName}；行 ID：0；字段：{result.Rule.FieldName}；目标：{result.Rule.TargetDisplayName}；为避免诊断表过长，剩余异常请在数据表页按字段筛选继续排查。",
                    Suggestion = "建议先处理已显示的异常；若是批量搬运造成，请回到测试副本中统一修正并再次运行资源诊断。",
                    Path = TryResolveSourcePath(project, tableLookup, result.Rule.SourceTableName)
                });
            }
        }

        return diagnostics
            .OrderByDescending(x => SeverityRank(x.Severity))
            .ThenBy(x => x.Category, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(x => x.Rule, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(x => x.Name, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(x => NaturalIdSortKey(x.Id))
            .ToList();
    }

    private RuleScanResult AnalyzeRule(
        CczProject project,
        IReadOnlyList<HexTableDefinition> tables,
        IReadOnlyDictionary<string, HexTableDefinition> tableLookup,
        ReferenceDiagnosticRule rule)
    {
        var sourceDefinition = tableLookup[rule.SourceTableName];
        var sourceData = ReadTable(project, tables, sourceDefinition);
        var validIds = BuildValidIdSet(project, tables, tableLookup, rule);
        var invalidRows = new List<InvalidReferenceRow>();
        var checkedCells = 0;
        var emptyLikeCells = 0;
        var namedRows = 0;

        foreach (DataRow row in sourceData.Rows)
        {
            if (!sourceData.Columns.Contains(rule.FieldName)) continue;
            var rowId = Convert.ToInt32(row["ID"], CultureInfo.InvariantCulture);
            var displayName = GetDisplayName(row);
            if (!string.IsNullOrWhiteSpace(displayName)) namedRows++;

            if (!TryConvertInt(row[rule.FieldName], out var value))
            {
                continue;
            }

            checkedCells++;
            if (rule.IsEmptyLike(value))
            {
                emptyLikeCells++;
                continue;
            }

            if (validIds.Contains(value))
            {
                continue;
            }

            if (!rule.ReportInvalidRows)
            {
                emptyLikeCells++;
                continue;
            }

            invalidRows.Add(new InvalidReferenceRow(rowId, displayName, value));
        }

        return new RuleScanResult(rule, checkedCells, emptyLikeCells, namedRows, invalidRows);
    }

    private static ResourceDiagnosticItem BuildRuleSummaryDiagnostic(
        CczProject project,
        IReadOnlyDictionary<string, HexTableDefinition> tableLookup,
        RuleScanResult result)
    {
        var invalidCount = result.InvalidRows.Count;
        return new ResourceDiagnosticItem
        {
            Severity = invalidCount > 0 ? "Warn" : "Info",
            Category = $"表格引用/{result.Rule.ReferenceKind}",
            Rule = "跨表引用概览",
            Id = "0",
            Name = $"{result.Rule.SourceTableName}/{result.Rule.FieldName}",
            Status = $"检查 {result.CheckedCells} 格，命名/有效行 {result.NamedRows}，空槽/无效果/特殊值 {result.EmptyLikeCells}，越界/未命中 {invalidCount}",
            Detail = $"源表：{result.Rule.SourceTableName}；行 ID：0；字段：{result.Rule.FieldName}；目标：{result.Rule.TargetDisplayName}；依据：{result.Rule.Reason}",
            Suggestion = invalidCount > 0
                ? "发现疑似越界或未命中的跨表编号；选中对应异常行后点“跳到数据表”，改为目标表已有编号或确认这是特殊值。"
                : $"该字段当前未发现越界引用。修改“{result.Rule.FieldName}”后建议重新运行诊断，避免编号存在但含义不符合策划。",
            Path = TryResolveSourcePath(project, tableLookup, result.Rule.SourceTableName)
        };
    }

    private static ResourceDiagnosticItem BuildInvalidRowDiagnostic(
        CczProject project,
        IReadOnlyDictionary<string, HexTableDefinition> tableLookup,
        ReferenceDiagnosticRule rule,
        InvalidReferenceRow row)
    {
        var rowName = string.IsNullOrWhiteSpace(row.RowName) ? "(未命名/无名称列)" : row.RowName;
        return new ResourceDiagnosticItem
        {
            Severity = "Warn",
            Category = $"表格引用/{rule.ReferenceKind}",
            Rule = "越界引用/未命中引用",
            Id = row.RowId.ToString(CultureInfo.InvariantCulture),
            Name = $"{rule.SourceTableName}/{rule.FieldName}",
            Status = $"值 {row.Value} 未在 {rule.TargetDisplayName} 中命中",
            Detail = $"源表：{rule.SourceTableName}；行 ID：{row.RowId}；字段：{rule.FieldName}；当前值：{row.Value}；对象：{rowName}；目标：{rule.TargetDisplayName}；依据：{rule.Reason}",
            Suggestion = "请跳到数据表单元格核对：若不是引擎特殊值，应改为目标表已有编号；若是特殊值，请用创作者备注记录用途并实机验证。",
            Path = TryResolveSourcePath(project, tableLookup, rule.SourceTableName)
        };
    }

    private HashSet<int> BuildValidIdSet(
        CczProject project,
        IReadOnlyList<HexTableDefinition> tables,
        IReadOnlyDictionary<string, HexTableDefinition> tableLookup,
        ReferenceDiagnosticRule rule)
    {
        if (rule.Kind == ReferenceDiagnosticKind.EquipmentEffect)
        {
            return BuildEquipmentEffectIds(project, tables, tableLookup);
        }

        var ids = new HashSet<int>();
        foreach (var tableName in rule.TargetTableNames)
        {
            if (!tableLookup.TryGetValue(tableName, out var definition)) continue;
            var data = ReadTable(project, tables, definition);
            foreach (DataRow row in data.Rows)
            {
                if (TryConvertInt(row["ID"], out var id))
                {
                    ids.Add(id);
                }
            }
        }

        return ids;
    }

    private HashSet<int> BuildEquipmentEffectIds(
        CczProject project,
        IReadOnlyList<HexTableDefinition> tables,
        IReadOnlyDictionary<string, HexTableDefinition> tableLookup)
    {
        var ids = new HashSet<int> { 0 };
        for (var id = 0; id <= 0x7F; id++)
        {
            ids.Add(id);
        }

        foreach (var tableName in new[] { "6.5-1-2 装备特效名称（1A-57）", "6.5-1-3 装备特效名称（58-7F）" })
        {
            if (!tableLookup.TryGetValue(tableName, out var definition)) continue;
            var data = ReadTable(project, tables, definition);
            foreach (DataColumn column in data.Columns)
            {
                if (column.ColumnName == "ID") continue;
                if (TryParseLeadingHexId(column.ColumnName, out var effectId))
                {
                    ids.Add(effectId);
                }
            }
        }

        var storePath = _itemEffectCatalogService.GetStorePath(project);
        if (File.Exists(storePath))
        {
            foreach (var entry in _itemEffectCatalogService.Load(project))
            {
                if (entry.EffectId is >= 0 and <= 255)
                {
                    ids.Add(entry.EffectId);
                }
            }
        }

        return ids;
    }

    private DataTable ReadTable(CczProject project, IReadOnlyList<HexTableDefinition> tables, HexTableDefinition definition)
    {
        var key = project.GameRoot + "|" + definition.TableName;
        if (_dataCache.TryGetValue(key, out var cached))
        {
            return cached;
        }

        var result = _reader.Read(project, definition, tables);
        _dataCache[key] = result.Data;
        return result.Data;
    }

    private static IReadOnlyList<ReferenceDiagnosticRule> BuildRules(IReadOnlyList<HexTableDefinition> tables)
    {
        var rules = new List<ReferenceDiagnosticRule>();

        rules.Add(ReferenceDiagnosticRule.Single("6.5-0 人物", "职业", "兵种", "6.5-4 详细兵种", "人物职业/兵种编号", "人物表“职业”通常引用详细兵种表。"));
        rules.Add(ReferenceDiagnosticRule.Single("6.5-0 人物", "暴击台词", "人物", "6.5-0-2 暴击台词", "暴击台词编号", "人物表“暴击台词”通常引用暴击台词表；部分 MOD 可能使用扩展台词编号，因此仅做概览和跳转。", reportInvalidRows: false));
        rules.Add(ReferenceDiagnosticRule.Single("6.5-0 人物", "撤退台词", "人物", "6.5-0-3 撤退台词", "撤退台词编号", "人物表“撤退台词”通常引用撤退台词表；部分 MOD 可能使用扩展台词编号，因此仅做概览和跳转。", reportInvalidRows: false));

        foreach (var field in new[] { "1号武将", "2号武将", "3号武将" })
        {
            rules.Add(ReferenceDiagnosticRule.Single("6.5-7-2 兵种特效分配", field, "人物", "6.5-0 人物", "人物/武将编号", "兵种特效分配表中的武将字段通常引用人物主表；高位数值可能是特殊条件或未命名扩展，因此仅做概览和跳转。", allow255AsEmpty: true, reportInvalidRows: false));
        }

        rules.Add(ReferenceDiagnosticRule.Single("6.5-7-2 兵种特效分配", "兵种", "兵种", "6.5-4 详细兵种", "详细兵种编号", "兵种特效分配表“兵种”通常引用详细兵种表。", allow255AsEmpty: true));

        foreach (var field in new[] { "武将1", "武将2" })
        {
            rules.Add(ReferenceDiagnosticRule.Single("6.5-7-3 人物专属、套装专属", field, "人物", "6.5-0 人物", "人物/武将编号", "专属规则中的武将字段通常引用人物主表。", allow255AsEmpty: true));
        }

        foreach (var field in new[] { "装备1", "装备2", "装备3-1", "装备3-2", "装备3-3", "装备4-1", "装备4-2", "装备4-3" })
        {
            rules.Add(ReferenceDiagnosticRule.Item("6.5-7-3 人物专属、套装专属", field, "专属/套装规则中的装备字段通常引用 6.5 物品表。", allow255AsEmpty: true));
        }

        foreach (var field in new[] { "开关仓库人物", "买卖物品人物" })
        {
            rules.Add(ReferenceDiagnosticRule.Single("6.5-8-1 商店数据", field, "人物", "6.5-0 人物", "人物/武将编号", "商店数据中的人物字段通常引用人物主表，用于开关仓库或买卖物品角色。", allow255AsEmpty: true));
        }

        var shopTable = tables.FirstOrDefault(x => x.TableName == "6.5-8-1 商店数据");
        if (shopTable != null)
        {
            foreach (var field in shopTable.Fields.Select(x => x.ColumnName)
                         .Where(name => name is not ("开关仓库人物" or "买卖物品人物")))
            {
                rules.Add(ReferenceDiagnosticRule.Item("6.5-8-1 商店数据", field, "商店装备/道具槽位通常引用 6.5 物品表；255 常作为空槽候选。", allow255AsEmpty: true));
            }
        }

        rules.Add(ReferenceDiagnosticRule.EquipmentEffect("6.5-1 物品（0-103）", "装备特效号", "物品表装备特效号当前优先引用项目侧宝物特效目录；未命中时回退到基础装备特效名称表；0 通常表示无特效。"));
        rules.Add(ReferenceDiagnosticRule.EquipmentEffect("6.5-2 物品（104-255）", "装备特效号", "物品表装备特效号当前优先引用项目侧宝物特效目录；未命中时回退到基础装备特效名称表；0 通常表示无特效。"));

        return rules;
    }

    private static string TryResolveSourcePath(CczProject project, IReadOnlyDictionary<string, HexTableDefinition> tableLookup, string sourceTableName)
    {
        try
        {
            return tableLookup.TryGetValue(sourceTableName, out var definition)
                ? project.ResolveGameFile(definition.FileName)
                : project.GameRoot;
        }
        catch
        {
            return project.GameRoot;
        }
    }

    private static string GetDisplayName(DataRow row)
    {
        if (row.Table.Columns.Contains("名称"))
        {
            return Convert.ToString(row["名称"], CultureInfo.InvariantCulture)?.Trim() ?? string.Empty;
        }

        return string.Empty;
    }

    private static bool TryConvertInt(object? value, out int result)
    {
        switch (value)
        {
            case byte b:
                result = b;
                return true;
            case ushort us:
                result = us;
                return true;
            case short s:
                result = s;
                return true;
            case int i:
                result = i;
                return true;
            case long l when l >= int.MinValue && l <= int.MaxValue:
                result = (int)l;
                return true;
            default:
                var text = Convert.ToString(value, CultureInfo.InvariantCulture)?.Trim() ?? string.Empty;
                if (text.Length == 0)
                {
                    result = 0;
                    return false;
                }

                if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                {
                    return int.TryParse(text[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out result);
                }

                return int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out result);
        }
    }

    private static bool TryParseLeadingHexId(string columnName, out int id)
    {
        id = 0;
        var text = new string(columnName.TakeWhile(IsHexChar).ToArray());
        return text.Length > 0 && int.TryParse(text, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out id);
    }

    private static bool IsHexChar(char ch) =>
        (ch >= '0' && ch <= '9') ||
        (ch >= 'a' && ch <= 'f') ||
        (ch >= 'A' && ch <= 'F');

    private static int NaturalIdSortKey(string id)
        => int.TryParse(id, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number) ? number : int.MaxValue;

    private static int SeverityRank(string severity) => severity switch
    {
        "Error" => 3,
        "Warn" => 2,
        "Info" => 1,
        _ => 0
    };

    private sealed record InvalidReferenceRow(int RowId, string RowName, int Value);

    private sealed record RuleScanResult(
        ReferenceDiagnosticRule Rule,
        int CheckedCells,
        int EmptyLikeCells,
        int NamedRows,
        IReadOnlyList<InvalidReferenceRow> InvalidRows);

    private sealed record ReferenceDiagnosticRule(
        string SourceTableName,
        string FieldName,
        string ReferenceKind,
        string TargetDisplayName,
        IReadOnlyList<string> TargetTableNames,
        ReferenceDiagnosticKind Kind,
        bool Allow255AsEmpty,
        bool AllowZeroAsEmpty,
        bool ReportInvalidRows,
        string Reason)
    {
        public bool IsEmptyLike(int value) =>
            (Allow255AsEmpty && value is 255 or 1024 or 65535) ||
            (AllowZeroAsEmpty && value == 0);

        public static ReferenceDiagnosticRule Single(
            string sourceTableName,
            string fieldName,
            string referenceKind,
            string targetTableName,
            string targetDisplayName,
            string reason,
            bool allow255AsEmpty = false,
            bool reportInvalidRows = true)
            => new(sourceTableName, fieldName, referenceKind, targetDisplayName, new[] { targetTableName }, ReferenceDiagnosticKind.SingleTable, allow255AsEmpty, false, reportInvalidRows, reason);

        public static ReferenceDiagnosticRule Item(
            string sourceTableName,
            string fieldName,
            string reason,
            bool allow255AsEmpty = false)
            => new(sourceTableName, fieldName, "物品", "6.5-1/2 物品表", new[] { "6.5-1 物品（0-103）", "6.5-2 物品（104-255）" }, ReferenceDiagnosticKind.Item, allow255AsEmpty, false, true, reason);

        public static ReferenceDiagnosticRule EquipmentEffect(
            string sourceTableName,
            string fieldName,
            string reason)
            => new(sourceTableName, fieldName, "装备特效", "项目侧宝物特效目录 / 6.5-1-2/6.5-1-3 装备特效名称", new[] { "6.5-1-2 装备特效名称（1A-57）", "6.5-1-3 装备特效名称（58-7F）" }, ReferenceDiagnosticKind.EquipmentEffect, true, true, true, reason);
    }

    private enum ReferenceDiagnosticKind
    {
        SingleTable,
        Item,
        EquipmentEffect
    }
}
