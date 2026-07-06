using CCZModStudio.Core;
using CCZModStudio.Formats;
using CCZModStudio.Models;
using System.Data;
using System.Globalization;

internal partial class Program
{
    static void RunTableDerivedDisplaySmoke(CczProject project, IReadOnlyList<HexTableDefinition> tables)
    {
        var service = new TableDerivedDisplayService();
        VerifyRowAlignedDerivedDisplayRefresh(project, tables, service);
        VerifyExplicitReferenceDisplayRefresh(project, tables, service);
        Console.WriteLine("TABLE_DERIVED_DISPLAY_SMOKE_OK");
    }

    private static void VerifyRowAlignedDerivedDisplayRefresh(
        CczProject project,
        IReadOnlyList<HexTableDefinition> tables,
        TableDerivedDisplayService service)
    {
        var table = HexTableNameResolver.ResolveForProject(project, tables, "6.5-7-2 兵种特效分配");
        var read = new HexTableReader().Read(project, table, tables);
        if (!read.Validation.IsUsable)
        {
            throw new InvalidOperationException("兵种特效分配表不可读取，无法执行派生列烟测。");
        }

        var row = read.Data.Rows.Cast<DataRow>().FirstOrDefault(r =>
            !string.IsNullOrWhiteSpace(Convert.ToString(r["名称"], CultureInfo.InvariantCulture)) &&
            !Convert.ToString(r["名称"], CultureInfo.InvariantCulture)!.StartsWith("#", StringComparison.Ordinal))
                  ?? throw new InvalidOperationException("兵种特效分配表没有可用于验证的名称行。");
        var originalName = Convert.ToString(row["名称"], CultureInfo.InvariantCulture) ?? string.Empty;
        row["名称"] = "__STALE_DERIVED_NAME__";

        var changed = service.RefreshRow(project, tables, table, row);
        var refreshedName = Convert.ToString(row["名称"], CultureInfo.InvariantCulture) ?? string.Empty;
        if (!changed.Contains("名称", StringComparer.Ordinal) ||
            !string.Equals(refreshedName, originalName, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"行对齐派生列未刷新：expected={originalName}, actual={refreshedName}.");
        }
    }

    private static void VerifyExplicitReferenceDisplayRefresh(
        CczProject project,
        IReadOnlyList<HexTableDefinition> tables,
        TableDerivedDisplayService service)
    {
        var itemTable = HexTableNameResolver.ResolveForProject(project, tables, "6.5-1 物品（0-103）");
        var itemRead = new HexTableReader().Read(project, itemTable, tables);
        var effectIds = itemRead.Data.Rows
            .Cast<DataRow>()
            .Select(row => Convert.ToInt32(row["装备特效号"], CultureInfo.InvariantCulture))
            .Where(id => id is not 0 and not 255)
            .Distinct()
            .Take(2)
            .ToList();
        if (effectIds.Count < 2)
        {
            effectIds = new List<int> { 0, 255 };
        }

        var row = BuildSyntheticDisplayRow();
        row["ID"] = 0;
        row["名称"] = "Smoke";
        row["类型"] = 0;
        row["装备特效号"] = effectIds[0];
        row["装备特效号-效果值"] = 1;
        row["升级能力成长"] = 0;
        row["宝物图鉴"] = 0;
        row["物品大类"] = "武器";
        row["装备特效名"] = "__STALE_EFFECT__";
        row["实际效果号"] = "__STALE_EFFECT_ID__";
        row["实际效果说明"] = "__STALE_EFFECT_DESC__";
        row["特效提示"] = "__STALE_EFFECT_HINT__";
        row["1号武将"] = 0;
        row["1号武将名"] = "__STALE_PERSON__";
        row["兵种"] = 0;
        row["兵种名称"] = "__STALE_JOB__";
        row["装备1"] = 0;
        row["装备1名"] = "__STALE_ITEM__";
        row.Table.Rows.Add(row);
        MarkSyntheticDisplayColumnsReadOnly(row.Table);
        row.Table.AcceptChanges();

        var changed = service.RefreshRow(project, tables, itemTable, row);
        AssertChangedAndNotStale(row, changed, "装备特效名", "__STALE_EFFECT__");
        AssertChangedAndNotStale(row, changed, "实际效果号", "__STALE_EFFECT_ID__");
        AssertChangedAndNotStale(row, changed, "实际效果说明", "__STALE_EFFECT_DESC__");
        AssertChangedAndNotStale(row, changed, "特效提示", "__STALE_EFFECT_HINT__");
        AssertChangedAndNotStale(row, changed, "1号武将名", "__STALE_PERSON__");
        AssertChangedAndNotStale(row, changed, "兵种名称", "__STALE_JOB__");
        AssertChangedAndNotStale(row, changed, "装备1名", "__STALE_ITEM__");

        var firstEffectName = Convert.ToString(row["装备特效名"], CultureInfo.InvariantCulture) ?? string.Empty;
        row["装备特效号"] = effectIds[1];
        var changedAgain = service.RefreshRow(project, tables, itemTable, row);
        var secondEffectName = Convert.ToString(row["装备特效名"], CultureInfo.InvariantCulture) ?? string.Empty;
        if (!changedAgain.Contains("装备特效名", StringComparer.Ordinal) ||
            string.Equals(firstEffectName, secondEffectName, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"装备特效名未随装备特效号变化：before={firstEffectName}, after={secondEffectName}.");
        }
    }

    private static DataRow BuildSyntheticDisplayRow()
    {
        var table = new DataTable("TableDerivedDisplaySmoke");
        table.Columns.Add("ID", typeof(int));
        table.Columns.Add("名称", typeof(string));
        table.Columns.Add("类型", typeof(int));
        table.Columns.Add("装备特效号", typeof(int));
        table.Columns.Add("装备特效号-效果值", typeof(int));
        table.Columns.Add("升级能力成长", typeof(int));
        table.Columns.Add("宝物图鉴", typeof(int));
        table.Columns.Add("物品大类", typeof(string));
        table.Columns.Add("装备特效名", typeof(string));
        table.Columns.Add("实际效果号", typeof(string));
        table.Columns.Add("实际效果说明", typeof(string));
        table.Columns.Add("特效提示", typeof(string));
        table.Columns.Add("1号武将", typeof(int));
        table.Columns.Add("1号武将名", typeof(string));
        table.Columns.Add("兵种", typeof(int));
        table.Columns.Add("兵种名称", typeof(string));
        table.Columns.Add("装备1", typeof(int));
        table.Columns.Add("装备1名", typeof(string));
        return table.NewRow();
    }

    private static void MarkSyntheticDisplayColumnsReadOnly(DataTable table)
    {
        foreach (DataColumn column in table.Columns)
        {
            if (column.ColumnName.EndsWith("名", StringComparison.Ordinal) ||
                column.ColumnName is "实际效果号" or "实际效果说明" or "特效提示")
            {
                column.ReadOnly = true;
            }
        }
    }

    private static void AssertChangedAndNotStale(DataRow row, IReadOnlyList<string> changed, string columnName, string staleValue)
    {
        var value = Convert.ToString(row[columnName], CultureInfo.InvariantCulture) ?? string.Empty;
        if (!changed.Contains(columnName, StringComparer.Ordinal) ||
            string.IsNullOrWhiteSpace(value) ||
            string.Equals(value, staleValue, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"{columnName} 未刷新：{value}");
        }
    }
}
