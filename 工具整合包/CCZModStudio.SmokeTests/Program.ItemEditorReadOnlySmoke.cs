using CCZModStudio;
using CCZModStudio.Core;
using CCZModStudio.Models;
using System.Data;
using System.Drawing;
using System.Globalization;
using System.Reflection;
using System.Text;
using System.Windows.Forms;

internal partial class Program
{
    private static readonly string[] ItemReadOnlySmokeDerivedColumns =
    [
        "物品大类",
        "项目类型",
        "类型样例",
        "类型来源",
        "类型说明",
        "价格显示",
        "装备特效名",
        "实际效果号",
        "实际效果说明",
        "特效提示"
    ];

    static void RunItemEditorReadOnlySmoke(CczProject project, IReadOnlyList<HexTableDefinition> tables)
    {
        var buildItemEditorData = typeof(MainForm).GetMethod("BuildItemEditorData", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new MissingMethodException("MainForm.BuildItemEditorData");
        var configureItemEditorGrid = typeof(MainForm).GetMethod("ConfigureItemEditorGrid", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new MissingMethodException("MainForm.ConfigureItemEditorGrid");
        var refreshItemEditorDerivedCells = typeof(MainForm).GetMethod("RefreshItemEditorDerivedCells", BindingFlags.Instance | BindingFlags.NonPublic, [typeof(DataRow)])
            ?? throw new MissingMethodException("MainForm.RefreshItemEditorDerivedCells(DataRow)");
        var isItemEditorUserEditableColumn = typeof(MainForm).GetMethod("IsItemEditorUserEditableColumn", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new MissingMethodException("MainForm.IsItemEditorUserEditableColumn");
        var currentItemEditorDataField = typeof(MainForm).GetField("_currentItemEditorData", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new MissingFieldException("MainForm", "_currentItemEditorData");
        var itemEditorGridField = typeof(MainForm).GetField("_itemEditorGrid", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new MissingFieldException("MainForm", "_itemEditorGrid");
        var projectField = typeof(MainForm).GetField("_project", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new MissingFieldException("MainForm", "_project");

        projectField.SetValue(smokeForm, project);
        var data = TryBuildItemEditorDataForReadOnlySmoke(buildItemEditorData, project, tables)
            ?? BuildSyntheticItemEditorDataForReadOnlySmoke();

        foreach (var columnName in ItemReadOnlySmokeDerivedColumns)
        {
            if (!data.Columns.Contains(columnName))
            {
                throw new InvalidOperationException("宝物设定缺少派生列：" + columnName);
            }

            if (data.Columns[columnName]!.ReadOnly)
            {
                throw new InvalidOperationException("派生列不应再使用 DataColumn.ReadOnly：" + columnName);
            }
        }

        foreach (var columnName in new[] { "ID", "分段", "大类", "来源文件", "介绍" })
        {
            if (data.Columns.Contains(columnName) && data.Columns[columnName]!.ReadOnly)
            {
                throw new InvalidOperationException("宝物设定用户只读列不应依赖 DataColumn.ReadOnly：" + columnName);
            }
        }

        currentItemEditorDataField.SetValue(smokeForm, data);
        var grid = itemEditorGridField.GetValue(smokeForm) as DataGridView
            ?? throw new InvalidOperationException("无法读取宝物设定表格。");
        grid.DataSource = data;
        configureItemEditorGrid.Invoke(smokeForm, Array.Empty<object>());

        foreach (var columnName in ItemReadOnlySmokeDerivedColumns.Concat(["ID", "分段", "大类", "来源文件"]))
        {
            if (!grid.Columns.Contains(columnName)) continue;
            var column = grid.Columns[columnName]!;
            if (!column.ReadOnly)
            {
                throw new InvalidOperationException("宝物设定只读列没有在 UI 层锁定：" + columnName);
            }

            if (column.DefaultCellStyle.BackColor != Color.FromArgb(245, 245, 245))
            {
                throw new InvalidOperationException("宝物设定只读列没有灰底显示：" + columnName);
            }
        }

        foreach (var editableColumnName in new[] { "名称", "图标", "类型", "价格（/100）", "装备特效号", "宝物图鉴" })
        {
            if (!grid.Columns.Contains(editableColumnName)) continue;
            if (grid.Columns[editableColumnName]!.ReadOnly)
            {
                throw new InvalidOperationException("宝物设定可编辑列被错误锁定：" + editableColumnName);
            }
        }

        var row = data.Rows.Cast<DataRow>().FirstOrDefault(r => r.RowState != DataRowState.Detached)
            ?? throw new InvalidOperationException("宝物设定没有可测试的数据行。");
        var oldProjectType = Convert.ToString(row["项目类型"], CultureInfo.InvariantCulture) ?? string.Empty;
        refreshItemEditorDerivedCells.Invoke(smokeForm, new object[] { row });
        var refreshedProjectType = Convert.ToString(row["项目类型"], CultureInfo.InvariantCulture) ?? string.Empty;
        if (string.IsNullOrWhiteSpace(refreshedProjectType))
        {
            throw new InvalidOperationException("宝物设定派生列刷新后项目类型为空。旧值：" + oldProjectType);
        }

        RunItemEditorCsvFilterSmoke(data, isItemEditorUserEditableColumn);

        Console.WriteLine($"ITEM_EDITOR_READONLY_SMOKE_OK rows={data.Rows.Count}");
    }

    private static DataTable? TryBuildItemEditorDataForReadOnlySmoke(MethodInfo buildItemEditorData, CczProject project, IReadOnlyList<HexTableDefinition> tables)
    {
        try
        {
            return buildItemEditorData.Invoke(smokeForm, new object[] { project, tables }) as DataTable
                   ?? throw new InvalidOperationException("宝物设定聚合数据构建失败。");
        }
        catch (TargetInvocationException ex) when (ex.InnerException is InvalidOperationException)
        {
            Console.WriteLine("ITEM_EDITOR_READONLY_SMOKE_USING_SYNTHETIC_DATA reason=" + ex.InnerException.Message);
            return null;
        }
    }

    private static DataTable BuildSyntheticItemEditorDataForReadOnlySmoke()
    {
        var table = new DataTable("宝物设定");
        table.Columns.Add("ID", typeof(int));
        table.Columns.Add("分段", typeof(string));
        table.Columns.Add("大类", typeof(string));
        table.Columns.Add("物品大类", typeof(string));
        table.Columns.Add("名称", typeof(string));
        table.Columns.Add("图标", typeof(byte));
        table.Columns.Add("类型", typeof(byte));
        table.Columns.Add("初始能力", typeof(byte));
        table.Columns.Add("升级能力成长", typeof(byte));
        table.Columns.Add("价格（/100）", typeof(short));
        table.Columns.Add("装备特效号", typeof(byte));
        table.Columns.Add("项目类型", typeof(string));
        table.Columns.Add("类型样例", typeof(string));
        table.Columns.Add("类型来源", typeof(string));
        table.Columns.Add("类型说明", typeof(string));
        table.Columns.Add("价格显示", typeof(string));
        table.Columns.Add("装备特效名", typeof(string));
        table.Columns.Add("实际效果号", typeof(string));
        table.Columns.Add("实际效果说明", typeof(string));
        table.Columns.Add("特效提示", typeof(string));
        table.Columns.Add("装备特效号-效果值", typeof(byte));
        table.Columns.Add("宝物图鉴", typeof(byte));
        table.Columns.Add("介绍", typeof(string));
        table.Columns.Add("来源文件", typeof(string));

        table.Rows.Add(
            1,
            "0-103",
            "武器",
            "武器",
            "SmokeItem",
            (byte)1,
            (byte)1,
            (byte)10,
            (byte)1,
            (short)50,
            (byte)0,
            "短兵",
            "SmokeItem",
            "烟测",
            "短兵",
            "50",
            "无",
            "0",
            "无",
            "无",
            (byte)0,
            (byte)1,
            "Smoke description",
            "synthetic");
        table.AcceptChanges();
        return table;
    }

    private static void RunItemEditorCsvFilterSmoke(DataTable source, MethodInfo isItemEditorUserEditableColumn)
    {
        var table = source.Clone();
        var sourceRow = source.Rows.Cast<DataRow>().First();
        table.ImportRow(sourceRow);
        table.AcceptChanges();

        var row = table.Rows[0];
        var id = Convert.ToString(row["ID"], CultureInfo.InvariantCulture) ?? "0";
        var originalName = Convert.ToString(row["名称"], CultureInfo.InvariantCulture) ?? string.Empty;
        var originalProjectType = Convert.ToString(row["项目类型"], CultureInfo.InvariantCulture) ?? string.Empty;
        var originalEffectName = Convert.ToString(row["装备特效名"], CultureInfo.InvariantCulture) ?? string.Empty;
        var newName = string.Equals(originalName, "CsvSmokeName", StringComparison.Ordinal)
            ? "CsvSmokeName2"
            : "CsvSmokeName";

        var smokeDir = Path.Combine(Path.GetTempPath(), "CCZModStudio_ItemEditorReadOnlySmoke_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(smokeDir);
        var csvPath = Path.Combine(smokeDir, "item-readonly.csv");
        File.WriteAllText(
            csvPath,
            "ID,名称,项目类型,装备特效名\r\n" +
            "字段说明,名称,只读项目类型,只读特效名\r\n" +
            $"{CsvEscapeForItemReadOnlySmoke(id)},{CsvEscapeForItemReadOnlySmoke(newName)},CSV不应写入项目类型,CSV不应写入特效名\r\n",
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));

        var result = CsvService.ImportIntoWithChanges(
            table,
            csvPath,
            allowPartialColumns: true,
            matchByIdWhenPresent: true,
            columnName => (bool)isItemEditorUserEditableColumn.Invoke(smokeForm, new object[] { columnName })!);

        if (result.ImportedRows != 1)
        {
            throw new InvalidOperationException($"宝物设定 CSV 过滤导入行数不正确：{result.ImportedRows}");
        }

        if (result.SkippedReadOnlyCells != 3)
        {
            throw new InvalidOperationException($"宝物设定 CSV 应跳过 ID+2 个只读单元格，实际跳过：{result.SkippedReadOnlyCells}");
        }

        var changedColumns = result.ChangedCells.Select(cell => cell.ColumnName).ToList();
        if (!changedColumns.SequenceEqual(["名称"], StringComparer.Ordinal))
        {
            throw new InvalidOperationException("宝物设定 CSV 过滤后的改动列不正确：" + string.Join(",", changedColumns));
        }

        if (!string.Equals(Convert.ToString(row["名称"], CultureInfo.InvariantCulture), newName, StringComparison.Ordinal) ||
            !string.Equals(Convert.ToString(row["项目类型"], CultureInfo.InvariantCulture), originalProjectType, StringComparison.Ordinal) ||
            !string.Equals(Convert.ToString(row["装备特效名"], CultureInfo.InvariantCulture), originalEffectName, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("宝物设定 CSV 过滤没有正确保持只读派生列。");
        }
    }

    private static string CsvEscapeForItemReadOnlySmoke(string value)
    {
        if (!value.Contains(",") &&
            !value.Contains("\"") &&
            !value.Contains("\r") &&
            !value.Contains("\n"))
        {
            return value;
        }

        return "\"" + value.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
    }
}
