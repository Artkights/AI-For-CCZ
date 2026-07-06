using CCZModStudio;
using CCZModStudio.Core;
using CCZModStudio.Models;
using System.Data;
using System.Globalization;
using System.Reflection;
using System.Windows.Forms;

internal partial class Program
{
    static void RunRoleCriticalQuoteUiSmoke(CczProject project)
    {
        RunRoleCriticalQuoteMappingServiceSmoke(project);

        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                Application.SetHighDpiMode(HighDpiMode.SystemAware);
                using var form = new MainForm();

                var genericRows = BuildCriticalQuoteRows((24, "一鼓作气"), (25, "乘胜追击"), (26, "破阵斩将"));
                var genericMapping = new RoleCriticalQuoteMapping(
                    RoleId: 100,
                    FieldValue: 1,
                    QuoteIds: genericRows.Select(row => Convert.ToInt32(row["ID"], CultureInfo.InvariantCulture)).ToArray(),
                    QuoteRows: genericRows,
                    IsSpecialRoleQuote: false,
                    Explanation: "generic smoke");

                InvokePrivateForRoleCriticalQuoteSmoke(form, "ShowCriticalQuoteEditor", genericMapping);
                var boxes = GetPrivateFieldForRoleCriticalQuoteSmoke<TextBox[]>(form, "_roleCriticalQuoteBoxes");
                var labels = GetPrivateFieldForRoleCriticalQuoteSmoke<Label[]>(form, "_roleCriticalQuoteLabels");
                AssertCriticalQuoteBox(boxes, labels, 0, "第1句 #24", "一鼓作气", enabled: true);
                AssertCriticalQuoteBox(boxes, labels, 1, "第2句 #25", "乘胜追击", enabled: true);
                AssertCriticalQuoteBox(boxes, labels, 2, "第3句 #26", "破阵斩将", enabled: true);

                boxes[0].Text = "改一";
                boxes[1].Text = "改二";
                boxes[2].Text = "改三";
                InvokePrivateForRoleCriticalQuoteSmoke(form, "ApplyCriticalQuoteEditorToRows", genericMapping);
                AssertRowText(genericRows[0], "改一");
                AssertRowText(genericRows[1], "改二");
                AssertRowText(genericRows[2], "改三");

                var specialRows = BuildCriticalQuoteRows((7, "特殊一击"));
                var specialMapping = new RoleCriticalQuoteMapping(
                    RoleId: 7,
                    FieldValue: 0,
                    QuoteIds: new[] { 7 },
                    QuoteRows: specialRows,
                    IsSpecialRoleQuote: true,
                    Explanation: "special smoke");
                InvokePrivateForRoleCriticalQuoteSmoke(form, "ShowCriticalQuoteEditor", specialMapping);
                AssertCriticalQuoteBox(boxes, labels, 0, "特殊台词 #7", "特殊一击", enabled: true);
                AssertCriticalQuoteBox(boxes, labels, 1, "第2句", string.Empty, enabled: false);
                AssertCriticalQuoteBox(boxes, labels, 2, "第3句", string.Empty, enabled: false);

                var criticalQuotes = BuildCriticalQuoteTable();
                var roleRow = BuildRoleRow(id: 100, name: "烟测武将", criticalType: 1);
                InvokePrivateForRoleCriticalQuoteSmoke(form, "ShowRoleCriticalQuoteAssignmentControls", roleRow, new RoleCriticalQuoteSelection(RoleCriticalQuoteMode.Generic, 1));
                InvokePrivateForRoleCriticalQuoteSmoke(form, "ShowCriticalQuoteEditor", new RoleQuoteMappingService().ResolveCriticalQuoteSelection(roleRow, criticalQuotes, new RoleCriticalQuoteSelection(RoleCriticalQuoteMode.Generic, 1)));

                var modeCombo = GetPrivateFieldForRoleCriticalQuoteSmoke<ComboBox>(form, "_roleCriticalQuoteModeCombo");
                var assignmentCombo = GetPrivateFieldForRoleCriticalQuoteSmoke<ComboBox>(form, "_roleCriticalQuoteAssignmentCombo");
                if (Convert.ToInt32(modeCombo.SelectedValue, CultureInfo.InvariantCulture) != (int)RoleCriticalQuoteMode.Generic ||
                    Convert.ToInt32(assignmentCombo.SelectedValue, CultureInfo.InvariantCulture) != 1)
                {
                    throw new InvalidOperationException("Generic critical assignment controls were not initialized correctly.");
                }

                InvokePrivateForRoleCriticalQuoteSmoke(form, "ShowRoleCriticalQuoteAssignmentControls", roleRow, new RoleCriticalQuoteSelection(RoleCriticalQuoteMode.Special, 4));
                if (Convert.ToInt32(modeCombo.SelectedValue, CultureInfo.InvariantCulture) != (int)RoleCriticalQuoteMode.Special ||
                    Convert.ToInt32(assignmentCombo.SelectedValue, CultureInfo.InvariantCulture) != 4)
                {
                    throw new InvalidOperationException("Special critical assignment controls were not initialized correctly.");
                }

                var retreatBox = GetPrivateFieldForRoleCriticalQuoteSmoke<TextBox>(form, "_roleRetreatQuoteBox");
                InvokePrivateForRoleCriticalQuoteSmoke(form, "ShowRetreatQuoteRule", new RoleRetreatQuoteMapping(50, 0, null, null, "high role"));
                if (retreatBox.Enabled || !retreatBox.ReadOnly)
                {
                    throw new InvalidOperationException("High-id retreat quote rule was not shown as S-event-only.");
                }

                Console.WriteLine("ROLE_CRITICAL_QUOTE_UI_SMOKE_OK generic=3 special=1 assignment=ok retreat=ok");
            }
            catch (Exception ex)
            {
                failure = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (failure != null)
        {
            throw new InvalidOperationException("Role critical quote UI smoke failed.", failure);
        }
    }

    private static void RunRoleCriticalQuoteMappingServiceSmoke(CczProject project)
    {
        var testRoot = new BackupManager().CreateTestCopy(project);
        var testProject = new CczProject
        {
            WorkspaceRoot = project.WorkspaceRoot,
            GameRoot = testRoot,
            HexTableXmlPath = project.HexTableXmlPath,
            SceneDictionaryPath = project.SceneDictionaryPath,
            SceneEditorDirectory = project.SceneEditorDirectory,
            ImageAssignerDirectory = project.ImageAssignerDirectory,
            ImageAssignerSystemIniPath = project.ImageAssignerSystemIniPath,
            MaterialLibraryRoot = project.MaterialLibraryRoot,
            PatchConfigRoot = project.PatchConfigRoot,
            PathDiagnostics = project.PathDiagnostics
        };
        var service = new RoleQuoteMappingService();
        var originalIds = service.ReadSpecialCriticalRoleIds(testProject);
        if (originalIds.Count != RoleQuoteMappingService.CriticalSpecialQuoteCount)
        {
            throw new InvalidOperationException("Special critical role id table did not read 21 slots.");
        }

        var updated = originalIds.ToArray();
        updated[0] = 100;
        updated[1] = RoleQuoteMappingService.CriticalSpecialEmptyRoleId;
        var save = service.SaveSpecialCriticalRoleIds(testProject, updated)
            ?? throw new InvalidOperationException("Special critical role id smoke did not produce a save result.");
        if (save.ChangedBytes <= 0 || string.IsNullOrWhiteSpace(save.BackupPath) || !File.Exists(save.BackupPath) || !File.Exists(save.ReportJsonPath))
        {
            throw new InvalidOperationException("Special critical role id save result missing backup/report evidence.");
        }

        var reread = service.ReadSpecialCriticalRoleIds(testProject);
        if (!reread.SequenceEqual(updated))
        {
            throw new InvalidOperationException("Special critical role id reread did not match written ids.");
        }

        var criticalQuotes = BuildCriticalQuoteTable();
        var specialRow = BuildRoleRow(id: 100, name: "烟测武将", criticalType: 7);
        var specialMapping = service.ResolveCriticalQuote(testProject, specialRow, criticalQuotes);
        if (!specialMapping.IsSpecialRoleQuote || specialMapping.QuoteIds.Count != 1 || specialMapping.QuoteIds[0] != 0)
        {
            throw new InvalidOperationException("Special critical role mapping did not resolve to slot #0.");
        }

        var genericRow = BuildRoleRow(id: 101, name: "普通武将", criticalType: 2);
        var genericMapping = service.ResolveCriticalQuote(testProject, genericRow, criticalQuotes);
        if (genericMapping.IsSpecialRoleQuote || !genericMapping.QuoteIds.SequenceEqual(new[] { 27, 28, 29 }))
        {
            throw new InvalidOperationException("Generic critical role mapping did not resolve to type 2 rows #27..#29.");
        }

        try
        {
            _ = RoleQuoteMappingService.FirstGenericCriticalQuoteId(RoleQuoteMappingService.CriticalGenericTypeCount);
            throw new InvalidOperationException("Out-of-range generic critical type did not fail.");
        }
        catch (ArgumentOutOfRangeException)
        {
        }
    }

    private static List<DataRow> BuildCriticalQuoteRows(params (int Id, string Text)[] rows)
    {
        var table = new DataTable("CriticalQuoteSmoke");
        table.Columns.Add("ID", typeof(int));
        table.Columns.Add("介绍", typeof(string));

        var result = new List<DataRow>();
        foreach (var row in rows)
        {
            var dataRow = table.NewRow();
            dataRow["ID"] = row.Id;
            dataRow["介绍"] = row.Text;
            table.Rows.Add(dataRow);
            result.Add(dataRow);
        }

        table.AcceptChanges();
        return result;
    }

    private static DataTable BuildCriticalQuoteTable()
    {
        var table = new DataTable("CriticalQuoteTableSmoke");
        table.Columns.Add("ID", typeof(int));
        table.Columns.Add("介绍", typeof(string));
        for (var id = 0; id < 99; id++)
        {
            var row = table.NewRow();
            row["ID"] = id;
            row["介绍"] = $"暴击{id}";
            table.Rows.Add(row);
        }

        table.AcceptChanges();
        return table;
    }

    private static DataRow BuildRoleRow(int id, string name, int criticalType)
    {
        var table = new DataTable("RoleSmoke");
        table.Columns.Add("ID", typeof(int));
        table.Columns.Add("名称", typeof(string));
        table.Columns.Add("撤退台词", typeof(int));
        table.Columns.Add("暴击台词", typeof(int));
        var row = table.NewRow();
        row["ID"] = id;
        row["名称"] = name;
        row["撤退台词"] = 0;
        row["暴击台词"] = criticalType;
        table.Rows.Add(row);
        table.AcceptChanges();
        return row;
    }

    private static void AssertCriticalQuoteBox(TextBox[] boxes, Label[] labels, int index, string expectedLabel, string expectedText, bool enabled)
    {
        if (!string.Equals(labels[index].Text, expectedLabel, StringComparison.Ordinal) ||
            !string.Equals(boxes[index].Text, expectedText, StringComparison.Ordinal) ||
            boxes[index].Enabled != enabled ||
            boxes[index].ReadOnly == enabled)
        {
            throw new InvalidOperationException(
                $"Critical quote box mismatch index={index}: label={labels[index].Text}, text={boxes[index].Text}, enabled={boxes[index].Enabled}, readOnly={boxes[index].ReadOnly}");
        }
    }

    private static void AssertRowText(DataRow row, string expected)
    {
        var actual = Convert.ToString(row["介绍"], CultureInfo.InvariantCulture) ?? string.Empty;
        if (!string.Equals(actual, expected, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Critical quote row mismatch: expected={expected}, actual={actual}");
        }
    }

    private static T GetPrivateFieldForRoleCriticalQuoteSmoke<T>(MainForm form, string fieldName)
    {
        var field = typeof(MainForm).GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("Field not found: " + fieldName);
        return (T)field.GetValue(form)!;
    }

    private static void InvokePrivateForRoleCriticalQuoteSmoke(MainForm form, string methodName, params object?[] args)
    {
        var method = typeof(MainForm).GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("Method not found: " + methodName);
        method.Invoke(form, args);
    }
}
