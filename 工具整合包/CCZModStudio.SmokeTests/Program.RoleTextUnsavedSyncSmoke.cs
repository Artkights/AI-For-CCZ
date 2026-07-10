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
    private const string BadRoleTextColumnName = "浠嬬粛";

    static void RunRoleTextUnsavedSyncSmoke(CczProject project, IReadOnlyList<HexTableDefinition> tables)
    {
        project = ResolveRoleTextUnsavedSyncSmokeProject(project);

        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                using var form = new MainForm();
                form.Show();
                Application.DoEvents();

                AssertRoleTextUnsavedSync(form, project, tables);
                Console.WriteLine("ROLE_TEXT_UNSAVED_SYNC_SMOKE_OK");
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
            throw new InvalidOperationException("Role text unsaved-sync smoke failed.", failure);
        }
    }

    private static CczProject ResolveRoleTextUnsavedSyncSmokeProject(CczProject project)
    {
        if (File.Exists(Path.Combine(project.GameRoot, "Data.e5")) &&
            File.Exists(Path.Combine(project.GameRoot, "Imsg.e5")) &&
            File.Exists(Path.Combine(project.GameRoot, "Ekd5.exe")))
        {
            return project;
        }

        var base65 = Path.Combine(project.WorkspaceRoot, "基底", "加强版6.5未加密版");
        if (File.Exists(Path.Combine(base65, "Data.e5")) &&
            File.Exists(Path.Combine(base65, "Imsg.e5")) &&
            File.Exists(Path.Combine(base65, "Ekd5.exe")))
        {
            return new ProjectDetector().CreateProjectFromGameRoot(base65);
        }

        throw new InvalidOperationException($"角色文本未保存同步烟测需要 Data.e5、Imsg.e5 和 Ekd5.exe。当前 GameRoot={project.GameRoot}，也未找到 {base65}");
    }

    private static void AssertRoleTextUnsavedSync(MainForm form, CczProject project, IReadOnlyList<HexTableDefinition> tables)
    {
        var buildRoleEditorData = GetRoleTextUnsavedSyncMethod("BuildRoleEditorData");
        var configureRoleEditorGrid = GetRoleTextUnsavedSyncMethod("ConfigureRoleEditorGrid");
        var showRoleTextDetails = GetRoleTextUnsavedSyncMethod("ShowRoleTextDetails");
        var syncSelectedRoleTextDetailsIntoTables = GetRoleTextUnsavedSyncMethod("SyncSelectedRoleTextDetailsIntoTables");
        var projectField = GetRoleTextUnsavedSyncField("_project");
        var tablesField = GetRoleTextUnsavedSyncField("_tables");
        var currentRoleEditorDataField = GetRoleTextUnsavedSyncField("_currentRoleEditorData");
        var roleEditorGridField = GetRoleTextUnsavedSyncField("_roleEditorGrid");
        var roleBiographyReadField = GetRoleTextUnsavedSyncField("_roleBiographyRead");
        var roleCriticalQuoteReadField = GetRoleTextUnsavedSyncField("_roleCriticalQuoteRead");
        var roleRetreatQuoteReadField = GetRoleTextUnsavedSyncField("_roleRetreatQuoteRead");
        var roleBiographyBoxField = GetRoleTextUnsavedSyncField("_roleBiographyBox");
        var roleRetreatQuoteBoxField = GetRoleTextUnsavedSyncField("_roleRetreatQuoteBox");

        projectField.SetValue(form, project);
        tablesField.SetValue(form, tables);

        var roleData = buildRoleEditorData.Invoke(form, [project, tables]) as DataTable
            ?? throw new InvalidOperationException("角色设定聚合表构建失败。");
        currentRoleEditorDataField.SetValue(form, roleData);

        var reader = new HexTableReader();
        var biographyRead = reader.Read(project, FindSmokeTable(tables, "6.5-0-1 人物列传"), tables);
        var criticalQuoteRead = reader.Read(project, FindSmokeTable(tables, "6.5-0-2 暴击台词"), tables);
        var retreatQuoteRead = reader.Read(project, FindSmokeTable(tables, "6.5-0-3 撤退台词"), tables);
        roleBiographyReadField.SetValue(form, biographyRead);
        roleCriticalQuoteReadField.SetValue(form, criticalQuoteRead);
        roleRetreatQuoteReadField.SetValue(form, retreatQuoteRead);

        var grid = roleEditorGridField.GetValue(form) as DataGridView
            ?? throw new InvalidOperationException("无法读取角色设定表格。");
        grid.DataSource = roleData;
        configureRoleEditorGrid.Invoke(form, Array.Empty<object>());

        var roleRow = roleData.Rows.Cast<DataRow>()
            .FirstOrDefault(row =>
            {
                var id = Convert.ToInt32(row["ID"], CultureInfo.InvariantCulture);
                return id >= 0 && id < RoleQuoteMappingService.RetreatQuoteCount;
            })
            ?? throw new InvalidOperationException("角色设定聚合表没有可用于烟测的人物行。");
        var roleId = Convert.ToInt32(roleRow["ID"], CultureInfo.InvariantCulture);
        var rowIndex = roleData.Rows.IndexOf(roleRow);
        grid.CurrentCell = grid.Rows[rowIndex].Cells["名称"];

        showRoleTextDetails.Invoke(form, [roleRow]);

        var biographyBox = roleBiographyBoxField.GetValue(form) as TextBox
            ?? throw new InvalidOperationException("无法读取人物列传编辑框。");
        var retreatBox = roleRetreatQuoteBoxField.GetValue(form) as TextBox
            ?? throw new InvalidOperationException("无法读取撤退台词编辑框。");
        var biographyText = $"列传未保存同步烟测 #{roleId}";
        var retreatText = $"撤退未保存同步烟测 #{roleId}";
        biographyBox.Text = biographyText;

        var retreatMapping = new RoleQuoteMappingService().ResolveRetreatQuote(roleRow, retreatQuoteRead.Data);
        if (retreatMapping.QuoteRow != null)
        {
            retreatBox.Text = retreatText;
        }

        syncSelectedRoleTextDetailsIntoTables.Invoke(form, Array.Empty<object>());

        if (biographyRead.Data.Columns.Contains(BadRoleTextColumnName) ||
            retreatQuoteRead.Data.Columns.Contains(BadRoleTextColumnName))
        {
            throw new InvalidOperationException($"角色文本表不应包含错误列名：{BadRoleTextColumnName}");
        }

        var actualBiography = Convert.ToString(FindSmokeRowById(biographyRead.Data, roleId)["介绍"], CultureInfo.InvariantCulture) ?? string.Empty;
        if (!string.Equals(actualBiography, biographyText, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"人物列传未同步到介绍列：expected={biographyText}, actual={actualBiography}");
        }

        if (retreatMapping.QuoteRow != null)
        {
            var actualRetreat = Convert.ToString(retreatMapping.QuoteRow["介绍"], CultureInfo.InvariantCulture) ?? string.Empty;
            if (!string.Equals(actualRetreat, retreatText, StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"撤退台词未同步到介绍列：expected={retreatText}, actual={actualRetreat}");
            }
        }
    }

    private static MethodInfo GetRoleTextUnsavedSyncMethod(string name)
        => typeof(MainForm).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)
           ?? throw new MissingMethodException("MainForm", name);

    private static FieldInfo GetRoleTextUnsavedSyncField(string name)
        => typeof(MainForm).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)
           ?? throw new MissingFieldException("MainForm", name);
}
