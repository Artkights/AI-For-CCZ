using CCZModStudio;
using CCZModStudio.Models;
using System.Data;
using System.Globalization;
using System.Reflection;
using System.Windows.Forms;

internal partial class Program
{
    static void RunJobAreaDropdownSmoke(CczProject project, IReadOnlyList<HexTableDefinition> tables)
    {
        var buildJobEditorData = typeof(MainForm).GetMethod("BuildJobEditorData", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new MissingMethodException("MainForm.BuildJobEditorData");
        var configureJobEditorGrid = typeof(MainForm).GetMethod("ConfigureJobEditorGrid", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new MissingMethodException("MainForm.ConfigureJobEditorGrid");
        var currentJobEditorDataField = typeof(MainForm).GetField("_currentJobEditorData", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new MissingFieldException("MainForm", "_currentJobEditorData");
        var jobEditorGridField = typeof(MainForm).GetField("_jobEditorGrid", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new MissingFieldException("MainForm", "_jobEditorGrid");

        var data = buildJobEditorData.Invoke(smokeForm, new object[] { project, tables }) as DataTable
            ?? throw new InvalidOperationException("详细兵种聚合数据构建失败。");
        if (!data.Columns.Contains("攻击范围") || !data.Columns.Contains("穿透"))
        {
            throw new InvalidOperationException("详细兵种聚合数据缺少攻击范围或穿透列。");
        }

        currentJobEditorDataField.SetValue(smokeForm, data);
        var grid = jobEditorGridField.GetValue(smokeForm) as DataGridView
            ?? throw new InvalidOperationException("无法读取详细兵种表格。");
        grid.DataSource = data;
        configureJobEditorGrid.Invoke(smokeForm, Array.Empty<object>());

        AssertJobAreaComboColumn(grid, data, "攻击范围", "0：四格", "1：九宫");
        AssertJobAreaComboColumn(grid, data, "穿透", "0：不穿透", "2：九宫穿透");

        Console.WriteLine($"JOB_AREA_DROPDOWN_SMOKE_OK rows={data.Rows.Count}");
    }

    private static void AssertJobAreaComboColumn(DataGridView grid, DataTable data, string columnName, params string[] expectedDisplays)
    {
        if (grid.Columns[columnName] is not DataGridViewComboBoxColumn combo)
        {
            throw new InvalidOperationException($"{columnName} 列没有转换为下拉列。");
        }

        if (combo.ValueMember != "Value" || combo.DisplayMember != "Text")
        {
            throw new InvalidOperationException($"{columnName} 下拉列绑定字段不正确：value={combo.ValueMember}, display={combo.DisplayMember}");
        }

        var firstValue = data.Rows.Cast<DataRow>()
            .Select(row => row[columnName])
            .FirstOrDefault(value => value is int);
        if (firstValue is not int)
        {
            throw new InvalidOperationException($"{columnName} 底层值不再是 int。");
        }

        var displays = ((IEnumerable<object>?)combo.DataSource)?.Select(item => Convert.ToString(item, CultureInfo.InvariantCulture) ?? string.Empty).ToList()
            ?? new List<string>();
        foreach (var expected in expectedDisplays)
        {
            if (!displays.Contains(expected, StringComparer.Ordinal))
            {
                throw new InvalidOperationException($"{columnName} 下拉候选缺少：{expected}；actual={string.Join(",", displays.Take(12))}");
            }
        }
    }
}
