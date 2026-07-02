using CCZModStudio;
using CCZModStudio.Core;
using CCZModStudio.Formats;
using CCZModStudio.Models;
using System.Data;
using System.Globalization;
using System.Reflection;
using System.Windows.Forms;

internal partial class Program
{
    static void RunRoleDefaultEquipmentSmoke(CczProject project, IReadOnlyList<HexTableDefinition> tables)
    {
        project = ResolveRoleDefaultEquipmentSmokeProject(project);
        var personTable = HexTableNameResolver.ResolveForProject(project, tables, "6.5-0 人物");
        foreach (var columnName in new[] { "武器", "防具", "辅助" })
        {
            if (!personTable.Columns.Contains(columnName))
            {
                throw new InvalidOperationException($"人物表缺少默认装备列：{columnName}");
            }
        }

        if (personTable.RowSize != 32 || personTable.PositiveBytesSum != 32)
        {
            throw new InvalidOperationException($"人物表字节定义不完整：RowSize={personTable.RowSize}, PositiveBytesSum={personTable.PositiveBytesSum}");
        }

        var reader = new HexTableReader();
        var read = reader.Read(project, personTable, tables);
        if (!read.Validation.IsUsable || read.Validation.PaddingBytes != 0)
        {
            throw new InvalidOperationException($"人物表不可用或仍存在未命名字节：usable={read.Validation.IsUsable}, padding={read.Validation.PaddingBytes}");
        }

        var sampleRow = FindSmokeRowById(read.Data, 162);
        AssertRoleDefaultEquipmentValue(sampleRow, "武器", 46, "source weapon");
        AssertRoleDefaultEquipmentValue(sampleRow, "防具", 90, "source armor");
        AssertRoleDefaultEquipmentValue(sampleRow, "辅助", 143, "source assist");

        var buildRoleEditorData = typeof(MainForm).GetMethod("BuildRoleEditorData", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
            ?? throw new MissingMethodException("MainForm.BuildRoleEditorData");
        var roleEditorData = buildRoleEditorData.Invoke(smokeForm, new object[] { project, tables }) as DataTable
            ?? throw new InvalidOperationException("角色设定聚合表构建失败。");
        foreach (var columnName in new[] { "武器", "防具", "辅助", "武器名", "防具名", "辅助名" })
        {
            if (!roleEditorData.Columns.Contains(columnName))
            {
                throw new InvalidOperationException($"角色设定聚合表缺少列：{columnName}");
            }
        }
        var roleRow = FindSmokeRowById(roleEditorData, 162);
        AssertRoleDefaultEquipmentValue(roleRow, "武器", 46, "role editor weapon");
        AssertRoleDefaultEquipmentValue(roleRow, "防具", 90, "role editor armor");
        AssertRoleDefaultEquipmentValue(roleRow, "辅助", 143, "role editor assist");
        if (string.IsNullOrWhiteSpace(Convert.ToString(roleRow["武器名"], CultureInfo.InvariantCulture)) ||
            string.IsNullOrWhiteSpace(Convert.ToString(roleRow["防具名"], CultureInfo.InvariantCulture)) ||
            string.IsNullOrWhiteSpace(Convert.ToString(roleRow["辅助名"], CultureInfo.InvariantCulture)))
        {
            throw new InvalidOperationException("角色设定默认装备名称列为空。");
        }
        AssertRoleEquipmentDerivedColumnsRefresh(buildRoleEditorData, project, tables);
        AssertRoleEquipmentDetailUi(buildRoleEditorData, project, tables);

        var smokeRoot = Path.Combine(
            project.WorkspaceRoot,
            "CCZModStudio_TestCopies",
            "RoleDefaultEquipmentSmoke_" + DateTime.Now.ToString("yyyyMMdd_HHmmss_fff", CultureInfo.InvariantCulture));
        Directory.CreateDirectory(smokeRoot);
        foreach (var coreFile in new[] { "Ekd5.exe", "Data.e5", "Star.e5", "Imsg.e5" })
        {
            var source = Path.Combine(project.GameRoot, coreFile);
            if (File.Exists(source))
            {
                File.Copy(source, Path.Combine(smokeRoot, coreFile), overwrite: false);
            }
        }
        File.WriteAllText(
            Path.Combine(smokeRoot, "_CCZModStudio_TestCopy.txt"),
            $"CreatedAt={DateTime.Now:yyyy-MM-dd HH:mm:ss}\r\nSource={project.GameRoot}\r\nPurpose=Role default equipment smoke\r\n");

        var testProject = new ProjectDetector().CreateProjectFromGameRoot(smokeRoot);
        var testRead = reader.Read(testProject, personTable, tables);
        var testRow = FindSmokeRowById(testRead.Data, 162);
        var originalWeapon = Convert.ToInt32(testRow["武器"], CultureInfo.InvariantCulture);
        var originalArmor = Convert.ToInt32(testRow["防具"], CultureInfo.InvariantCulture);
        var originalAssist = Convert.ToInt32(testRow["辅助"], CultureInfo.InvariantCulture);

        testRow["武器"] = 0;
        testRow["防具"] = 70;
        testRow["辅助"] = 109;
        var save = new HexTableWriter().SaveToTestCopy(testProject, personTable, testRead.Data);
        if (save.ChangedBytes != 3 || string.IsNullOrWhiteSpace(save.BackupPath) || !File.Exists(save.BackupPath))
        {
            throw new InvalidOperationException($"默认装备写回烟测保存结果异常：changed={save.ChangedBytes}, backup={save.BackupPath}");
        }

        AssertRoleDefaultEquipmentBytes(testProject, personTable, 162, 0, 70, 109);
        var verify = reader.Read(testProject, personTable, tables);
        var verifyRow = FindSmokeRowById(verify.Data, 162);
        AssertRoleDefaultEquipmentValue(verifyRow, "武器", 0, "verify weapon");
        AssertRoleDefaultEquipmentValue(verifyRow, "防具", 70, "verify armor");
        AssertRoleDefaultEquipmentValue(verifyRow, "辅助", 109, "verify assist");

        verifyRow["武器"] = originalWeapon;
        verifyRow["防具"] = originalArmor;
        verifyRow["辅助"] = originalAssist;
        var restore = new HexTableWriter().SaveToTestCopy(testProject, personTable, verify.Data);
        if (restore.ChangedBytes != 3)
        {
            throw new InvalidOperationException($"默认装备恢复写回字节数异常：changed={restore.ChangedBytes}");
        }
        AssertRoleDefaultEquipmentBytes(testProject, personTable, 162, originalWeapon, originalArmor, originalAssist);

        Console.WriteLine($"ROLE_DEFAULT_EQUIPMENT_SMOKE_OK root={smokeRoot} row=162 original={originalWeapon},{originalArmor},{originalAssist}");
    }

    private static CczProject ResolveRoleDefaultEquipmentSmokeProject(CczProject project)
    {
        if (File.Exists(Path.Combine(project.GameRoot, "Data.e5")))
        {
            return project;
        }

        var base65 = Path.Combine(project.WorkspaceRoot, "基底", "加强版6.5未加密版");
        if (File.Exists(Path.Combine(base65, "Data.e5")))
        {
            return new ProjectDetector().CreateProjectFromGameRoot(base65);
        }

        throw new InvalidOperationException($"默认装备烟测需要可读 Data.e5。当前 GameRoot={project.GameRoot}，也未找到 {base65}");
    }

    private static void AssertRoleEquipmentDerivedColumnsRefresh(
        System.Reflection.MethodInfo buildRoleEditorData,
        CczProject project,
        IReadOnlyList<HexTableDefinition> tables)
    {
        var refreshRoleEditorDerivedCells = typeof(MainForm).GetMethod("RefreshRoleEditorDerivedCells", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic, [typeof(DataRow)])
            ?? throw new MissingMethodException("MainForm.RefreshRoleEditorDerivedCells(DataRow)");
        var projectField = typeof(MainForm).GetField("_project", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
            ?? throw new MissingFieldException("MainForm", "_project");
        var tablesField = typeof(MainForm).GetField("_tables", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
            ?? throw new MissingFieldException("MainForm", "_tables");
        var currentRoleEditorDataField = typeof(MainForm).GetField("_currentRoleEditorData", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
            ?? throw new MissingFieldException("MainForm", "_currentRoleEditorData");

        projectField.SetValue(smokeForm, project);
        tablesField.SetValue(smokeForm, tables);
        var data = buildRoleEditorData.Invoke(smokeForm, new object[] { project, tables }) as DataTable
            ?? throw new InvalidOperationException("角色设定聚合表构建失败。");
        currentRoleEditorDataField.SetValue(smokeForm, data);
        var row = FindSmokeRowById(data, 162);
        row["武器"] = 0;
        row["防具"] = 70;
        row["辅助"] = 109;

        refreshRoleEditorDerivedCells.Invoke(smokeForm, new object[] { row });

        AssertRoleEquipmentNameContains(row, "武器名", "0：");
        AssertRoleEquipmentNameContains(row, "防具名", "70：");
        AssertRoleEquipmentNameContains(row, "辅助名", "109：");
        if (row.RowState != DataRowState.Modified)
        {
            throw new InvalidOperationException($"修改默认装备后角色行状态异常：{row.RowState}");
        }
        foreach (var derivedColumn in new[] { "武器名", "防具名", "辅助名" })
        {
            if (!data.Columns[derivedColumn]!.ReadOnly)
            {
                throw new InvalidOperationException($"{derivedColumn} 应保持只读派生列。");
            }
        }
    }

    private static void AssertRoleEquipmentDetailUi(
        MethodInfo buildRoleEditorData,
        CczProject project,
        IReadOnlyList<HexTableDefinition> tables)
    {
        var configureRoleEditorGrid = typeof(MainForm).GetMethod("ConfigureRoleEditorGrid", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new MissingMethodException("MainForm.ConfigureRoleEditorGrid");
        var configureRoleEquipmentDetailControls = typeof(MainForm).GetMethod("ConfigureRoleEquipmentDetailControls", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new MissingMethodException("MainForm.ConfigureRoleEquipmentDetailControls");
        var showRoleTextDetails = typeof(MainForm).GetMethod("ShowRoleTextDetails", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new MissingMethodException("MainForm.ShowRoleTextDetails");
        var applyRoleEquipmentDetailSelection = typeof(MainForm).GetMethod("ApplyRoleEquipmentDetailSelection", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new MissingMethodException("MainForm.ApplyRoleEquipmentDetailSelection");
        var projectField = typeof(MainForm).GetField("_project", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new MissingFieldException("MainForm", "_project");
        var tablesField = typeof(MainForm).GetField("_tables", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new MissingFieldException("MainForm", "_tables");
        var currentRoleEditorDataField = typeof(MainForm).GetField("_currentRoleEditorData", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new MissingFieldException("MainForm", "_currentRoleEditorData");
        var roleEditorGridField = typeof(MainForm).GetField("_roleEditorGrid", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new MissingFieldException("MainForm", "_roleEditorGrid");
        var roleWeaponComboField = typeof(MainForm).GetField("_roleWeaponCombo", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new MissingFieldException("MainForm", "_roleWeaponCombo");
        var roleArmorComboField = typeof(MainForm).GetField("_roleArmorCombo", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new MissingFieldException("MainForm", "_roleArmorCombo");
        var roleAssistComboField = typeof(MainForm).GetField("_roleAssistCombo", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new MissingFieldException("MainForm", "_roleAssistCombo");
        var roleBiographyReadField = typeof(MainForm).GetField("_roleBiographyRead", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new MissingFieldException("MainForm", "_roleBiographyRead");
        var roleCriticalQuoteReadField = typeof(MainForm).GetField("_roleCriticalQuoteRead", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new MissingFieldException("MainForm", "_roleCriticalQuoteRead");
        var roleRetreatQuoteReadField = typeof(MainForm).GetField("_roleRetreatQuoteRead", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new MissingFieldException("MainForm", "_roleRetreatQuoteRead");

        projectField.SetValue(smokeForm, project);
        tablesField.SetValue(smokeForm, tables);
        var data = buildRoleEditorData.Invoke(smokeForm, new object[] { project, tables }) as DataTable
            ?? throw new InvalidOperationException("角色设定聚合表构建失败。");
        currentRoleEditorDataField.SetValue(smokeForm, data);

        var grid = roleEditorGridField.GetValue(smokeForm) as DataGridView
            ?? throw new InvalidOperationException("无法读取角色设定表格。");
        grid.DataSource = data;
        configureRoleEditorGrid.Invoke(smokeForm, Array.Empty<object>());
        foreach (var hiddenColumn in new[] { "R形象编号", "S形象编号", "武器", "防具", "辅助" })
        {
            if (!grid.Columns.Contains(hiddenColumn) || grid.Columns[hiddenColumn].Visible)
            {
                throw new InvalidOperationException($"角色设定主表列未按计划隐藏：{hiddenColumn}");
            }
        }

        configureRoleEquipmentDetailControls.Invoke(smokeForm, Array.Empty<object>());
        roleBiographyReadField.SetValue(smokeForm, new HexTableReader().Read(project, FindSmokeTable(tables, "6.5-0-1 人物列传"), tables));
        roleCriticalQuoteReadField.SetValue(smokeForm, new HexTableReader().Read(project, FindSmokeTable(tables, "6.5-0-2 暴击台词"), tables));
        roleRetreatQuoteReadField.SetValue(smokeForm, new HexTableReader().Read(project, FindSmokeTable(tables, "6.5-0-3 撤退台词"), tables));

        var row = FindSmokeRowById(data, 162);
        var rowIndex = data.Rows.IndexOf(row);
        grid.CurrentCell = grid.Rows[rowIndex].Cells["名称"];
        showRoleTextDetails.Invoke(smokeForm, new object[] { row });

        var weaponCombo = roleWeaponComboField.GetValue(smokeForm) as ComboBox
            ?? throw new InvalidOperationException("无法读取默认武器下拉。");
        var armorCombo = roleArmorComboField.GetValue(smokeForm) as ComboBox
            ?? throw new InvalidOperationException("无法读取默认防具下拉。");
        var assistCombo = roleAssistComboField.GetValue(smokeForm) as ComboBox
            ?? throw new InvalidOperationException("无法读取默认辅助下拉。");
        AssertComboValue(weaponCombo, 46, "weapon detail combo");
        AssertComboValue(armorCombo, 90, "armor detail combo");
        AssertComboValue(assistCombo, 143, "assist detail combo");
        if (!weaponCombo.Enabled || !armorCombo.Enabled || !assistCombo.Enabled)
        {
            throw new InvalidOperationException("右侧默认装备下拉未启用。");
        }

        weaponCombo.SelectedValue = 0;
        applyRoleEquipmentDetailSelection.Invoke(smokeForm, new object[] { "武器", weaponCombo });
        AssertRoleDefaultEquipmentValue(row, "武器", 0, "detail weapon edit");
        AssertRoleEquipmentNameContains(row, "武器名", "0：");
        if (row.RowState != DataRowState.Modified)
        {
            throw new InvalidOperationException($"右侧默认装备修改后角色行状态异常：{row.RowState}");
        }

        row.RejectChanges();
        showRoleTextDetails.Invoke(smokeForm, new object[] { row });
        AssertComboValue(weaponCombo, 46, "weapon detail combo restored");
    }

    private static void AssertComboValue(ComboBox combo, int expected, string label)
    {
        var actual = Convert.ToInt32(combo.SelectedValue, CultureInfo.InvariantCulture);
        if (actual != expected)
        {
            throw new InvalidOperationException($"{label} mismatch: expected={expected}, actual={actual}");
        }
    }

    private static void AssertRoleEquipmentNameContains(DataRow row, string columnName, string expected)
    {
        var text = Convert.ToString(row[columnName], CultureInfo.InvariantCulture) ?? string.Empty;
        if (!text.Contains(expected, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"{columnName} 未随默认装备刷新：expected contains={expected}, actual={text}");
        }
    }

    private static void AssertRoleDefaultEquipmentValue(DataRow row, string columnName, int expected, string label)
    {
        var actual = Convert.ToInt32(row[columnName], CultureInfo.InvariantCulture);
        if (actual != expected)
        {
            throw new InvalidOperationException($"{label} mismatch: expected={expected}, actual={actual}");
        }
    }

    private static void AssertRoleDefaultEquipmentBytes(
        CczProject project,
        HexTableDefinition personTable,
        int rowId,
        int expectedWeapon,
        int expectedArmor,
        int expectedAssist)
    {
        var path = project.ResolveGameFile(personTable.FileName);
        var bytes = File.ReadAllBytes(path);
        var offset = checked((int)(personTable.DataPos + (long)(rowId - personTable.BeginId) * personTable.RowSize + 29));
        if (bytes[offset] != expectedWeapon || bytes[offset + 1] != expectedArmor || bytes[offset + 2] != expectedAssist)
        {
            throw new InvalidOperationException(
                $"默认装备字节不符合预期：expected={expectedWeapon},{expectedArmor},{expectedAssist}; actual={bytes[offset]},{bytes[offset + 1]},{bytes[offset + 2]}");
        }
    }
}
