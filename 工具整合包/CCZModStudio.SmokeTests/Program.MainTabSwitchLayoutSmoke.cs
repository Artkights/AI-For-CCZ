using CCZModStudio;
using System.Data;
using System.Reflection;
using System.Windows.Forms;

internal partial class Program
{
    static void RunMainTabSwitchLayoutSmoke()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                Application.SetHighDpiMode(HighDpiMode.SystemAware);
                using var form = new MainForm();
                form.Show();
                Application.DoEvents();
                form.PerformLayout();
                DrainMainTabSwitchLayoutEvents();

                var tabs = GetPrivateFieldForMainTabSwitchLayoutSmoke<TabControl>(form, "_mainTabs");
                if (tabs.TabPages.Count < 6)
                {
                    throw new InvalidOperationException("Main tab switch smoke needs the primary feature tabs.");
                }

                var tabNames = tabs.TabPages.Cast<TabPage>().Select(page => page.Text).ToList();
                var itemTabIndex = tabNames.IndexOf("\u5b9d\u7269\u8bbe\u5b9a");
                var effectInjectionTabIndex = tabNames.IndexOf("\u7279\u6548\u6ce8\u5165");
                if (effectInjectionTabIndex < 0)
                {
                    throw new InvalidOperationException("Main tabs do not include the effect injection tab.");
                }

                if (itemTabIndex >= 0 && effectInjectionTabIndex != itemTabIndex + 1)
                {
                    throw new InvalidOperationException($"Effect injection tab should be immediately after item settings. item={itemTabIndex} effectInjection={effectInjectionTabIndex}");
                }

                AssertImagePageHierarchy(form, tabs);
                AssertImageAssignmentEditSurvivesMainTabSwitch(form, tabs);
                AssertCmEditSurvivesMainTabSwitch(form, tabs);

                var initialRequests = GetPrivateStaticIntForMainTabSwitchLayoutSmoke("CompactToolbarLayoutRequestCount");
                var initialFlushes = GetPrivateStaticIntForMainTabSwitchLayoutSmoke("CompactToolbarLayoutFlushCount");

                var round1 = SwitchMainTabsForLayoutSmoke(form, tabs);
                var round2 = SwitchMainTabsForLayoutSmoke(form, tabs);
                var finalFlushes = GetPrivateStaticIntForMainTabSwitchLayoutSmoke("CompactToolbarLayoutFlushCount");
                var lastProcessed = GetPrivateStaticIntForMainTabSwitchLayoutSmoke("CompactToolbarLayoutLastFlushProcessedCount");
                var lastSkipped = GetPrivateStaticIntForMainTabSwitchLayoutSmoke("CompactToolbarLayoutLastFlushSkippedCount");

                var allowedRound2 = Math.Max(round1 + 40, round1 * 2 + 20);
                if (round2 > allowedRound2)
                {
                    throw new InvalidOperationException($"Compact toolbar layout requests grew across tab switch rounds. round1={round1} round2={round2} allowed={allowedRound2}");
                }

                if (finalFlushes <= initialFlushes)
                {
                    throw new InvalidOperationException($"Compact toolbar layout queue did not flush. initial={initialFlushes} final={finalFlushes}");
                }

                var afterIdle = GetPrivateStaticIntForMainTabSwitchLayoutSmoke("CompactToolbarLayoutFlushCount");
                DrainMainTabSwitchLayoutEvents();
                var afterSecondIdle = GetPrivateStaticIntForMainTabSwitchLayoutSmoke("CompactToolbarLayoutFlushCount");
                if (afterSecondIdle > afterIdle + 2)
                {
                    throw new InvalidOperationException($"Compact toolbar layout queue kept appending after idle. before={afterIdle} after={afterSecondIdle}");
                }

                form.Close();
                Console.WriteLine($"MAIN_TAB_SWITCH_LAYOUT_SMOKE_OK tabs={tabs.TabPages.Count} initialRequests={initialRequests} round1={round1} round2={round2} flushes={finalFlushes - initialFlushes} last={lastProcessed}/{lastSkipped}");
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
            throw new InvalidOperationException("Main tab switch layout smoke failed.", failure);
        }
    }

    private static void AssertImagePageHierarchy(MainForm form, TabControl tabs)
    {
        var imagePage = tabs.TabPages.Cast<TabPage>().SingleOrDefault(page => page.Text == "图片设定")
            ?? throw new InvalidOperationException("Main tabs do not include image settings.");
        var imageAssignmentPage = tabs.TabPages.Cast<TabPage>().SingleOrDefault(page => page.Text == "形象设定")
            ?? throw new InvalidOperationException("Main tabs do not include image assignments.");
        var imagePageIndex = tabs.TabPages.IndexOf(imagePage);
        var imageAssignmentPageIndex = tabs.TabPages.IndexOf(imageAssignmentPage);
        if (imageAssignmentPageIndex != imagePageIndex + 1)
        {
            throw new InvalidOperationException($"Image assignments should immediately follow image settings. image={imagePageIndex} assignment={imageAssignmentPageIndex}");
        }

        var allTabPageNames = EnumerateControlsForMainTabSwitchLayoutSmoke(form)
            .OfType<TabControl>()
            .SelectMany(tab => tab.TabPages.Cast<TabPage>())
            .Select(page => page.Text)
            .ToList();
        if (allTabPageNames.Contains("人物形象设定", StringComparer.Ordinal))
        {
            throw new InvalidOperationException("Legacy image assignment tab is still present.");
        }

        var imageResourceGrid = GetPrivateFieldForMainTabSwitchLayoutSmoke<DataGridView>(form, "_imageResourceFileGrid");
        var imageAssignmentGrid = GetPrivateFieldForMainTabSwitchLayoutSmoke<DataGridView>(form, "_imageAssignmentGrid");
        var imageAssignmentPreview = GetPrivateFieldForMainTabSwitchLayoutSmoke<PictureBox>(form, "_imageAssignmentFacePreviewBox");
        var loadImageAssignmentsButton = GetPrivateFieldForMainTabSwitchLayoutSmoke<Button>(form, "_loadImageAssignmentsButton");
        if (!loadImageAssignmentsButton.Text.Equals("读取形象设定", StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Unexpected image assignment load button text: {loadImageAssignmentsButton.Text}");
        }
        if (!IsDescendantForMainTabSwitchLayoutSmoke(imageResourceGrid, imagePage))
        {
            throw new InvalidOperationException("Image resource controls are not hosted by image settings.");
        }
        if (!IsDescendantForMainTabSwitchLayoutSmoke(imageAssignmentGrid, imageAssignmentPage) ||
            !IsDescendantForMainTabSwitchLayoutSmoke(imageAssignmentPreview, imageAssignmentPage))
        {
            throw new InvalidOperationException("Image assignment controls are not hosted by image assignments.");
        }
        if (EnumerateControlsForMainTabSwitchLayoutSmoke(imagePage).OfType<TabControl>().Any())
        {
            throw new InvalidOperationException("Image settings still contains a redundant nested tab control.");
        }
    }

    private static void AssertImageAssignmentEditSurvivesMainTabSwitch(MainForm form, TabControl tabs)
    {
        var imageAssignmentPage = tabs.TabPages.Cast<TabPage>().Single(page => page.Text == "形象设定");
        var otherPage = tabs.TabPages.Cast<TabPage>().First(page => page.Text == "图片设定");
        var grid = GetPrivateFieldForMainTabSwitchLayoutSmoke<DataGridView>(form, "_imageAssignmentGrid");
        var table = new DataTable("image-assignment-tab-switch-cache");
        table.Columns.Add("ID", typeof(int));
        table.Columns.Add("头像编号", typeof(int));
        table.Rows.Add(1, 2);
        table.AcceptChanges();

        tabs.SelectedTab = imageAssignmentPage;
        grid.ReadOnly = false;
        grid.DataSource = table;
        Application.DoEvents();
        var faceColumn = grid.Columns.Cast<DataGridViewColumn>().Single(column => column.DataPropertyName == "头像编号");
        grid.CurrentCell = grid.Rows[0].Cells[faceColumn.Index];
        if (!grid.BeginEdit(true) || grid.EditingControl is not DataGridViewTextBoxEditingControl editor)
        {
            throw new InvalidOperationException("Image assignment grid did not enter edit mode.");
        }

        editor.Text = "7";
        Application.DoEvents();
        tabs.SelectedTab = otherPage;
        Application.DoEvents();

        var actual = Convert.ToInt32(table.Rows[0]["头像编号"]);
        if (actual != 7 || table.Rows[0].RowState != DataRowState.Modified)
        {
            throw new InvalidOperationException($"Image assignment edit was lost after switching main tabs. value={actual} state={table.Rows[0].RowState}");
        }
    }

    private static void AssertCmEditSurvivesMainTabSwitch(MainForm form, TabControl tabs)
    {
        var globalTab = tabs.TabPages.Cast<TabPage>().FirstOrDefault(page => page.Text == "全局设定")
            ?? throw new InvalidOperationException("Main tabs do not include global settings.");
        var otherTab = tabs.TabPages.Cast<TabPage>().FirstOrDefault(page => page.Text == "角色设定")
            ?? throw new InvalidOperationException("Main tabs do not include role settings.");
        var session = GetPrivateFieldForMainTabSwitchLayoutSmoke<object>(form, "_cmSettingsPageSession");
        var grid = (DataGridView)(session.GetType().GetProperty("GlobalNumericGrid")?.GetValue(session)
            ?? throw new InvalidOperationException("CM settings session does not expose its numeric grid."));

        var table = new DataTable("tab-switch-cache");
        table.Columns.Add("Key", typeof(string));
        table.Columns.Add("名称", typeof(string));
        table.Columns.Add("当前值", typeof(string));
        table.Columns.Add("新值", typeof(string));
        table.Columns.Add("MinValue", typeof(int));
        table.Columns.Add("MaxValue", typeof(int));
        table.Rows.Add("smoke", "切页缓存", "12", string.Empty, 0, 255);
        table.AcceptChanges();
        grid.DataSource = table;

        tabs.SelectedTab = globalTab;
        Application.DoEvents();
        grid.CurrentCell = grid.Rows[0].Cells.Cast<DataGridViewCell>()
            .First(cell => grid.Columns[cell.ColumnIndex].DataPropertyName == "新值");
        table.Rows[0]["新值"] = "37";

        var deselectingCount = 0;
        tabs.Deselecting += (_, _) => deselectingCount++;
        tabs.SelectedTab = otherTab;
        Application.DoEvents();
        tabs.SelectedTab = globalTab;
        Application.DoEvents();
        var cachedValue = Convert.ToString(table.Rows[0]["新值"]);
        if (!string.Equals(cachedValue, "37", StringComparison.Ordinal) || table.Rows[0].RowState != DataRowState.Modified)
        {
            throw new InvalidOperationException($"CM grid edit was lost after switching main tabs. value={cachedValue ?? "<null>"} state={table.Rows[0].RowState}");
        }
        if (deselectingCount == 0)
        {
            throw new InvalidOperationException("Main tab switch did not invoke the pre-deselect cache event.");
        }
    }

    private static int SwitchMainTabsForLayoutSmoke(MainForm form, TabControl tabs)
    {
        var before = GetPrivateStaticIntForMainTabSwitchLayoutSmoke("CompactToolbarLayoutRequestCount");
        foreach (TabPage page in tabs.TabPages)
        {
            tabs.SelectedTab = page;
            Application.DoEvents();
            form.PerformLayout();
            DrainMainTabSwitchLayoutEvents();
        }

        return GetPrivateStaticIntForMainTabSwitchLayoutSmoke("CompactToolbarLayoutRequestCount") - before;
    }

    private static void DrainMainTabSwitchLayoutEvents()
    {
        for (var i = 0; i < 4; i++)
        {
            Application.DoEvents();
            Thread.Sleep(1);
        }
    }

    private static T GetPrivateFieldForMainTabSwitchLayoutSmoke<T>(object instance, string name)
    {
        var field = instance.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException($"Missing field {name}.");
        return (T)field.GetValue(instance)!;
    }

    private static int GetPrivateStaticIntForMainTabSwitchLayoutSmoke(string name)
    {
        var property = typeof(MainForm).GetProperty(name, BindingFlags.Static | BindingFlags.NonPublic);
        if (property != null)
        {
            return (int)property.GetValue(null)!;
        }

        var field = typeof(MainForm).GetField(name, BindingFlags.Static | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException($"Missing static counter {name}.");
        return (int)field.GetValue(null)!;
    }

    private static IEnumerable<Control> EnumerateControlsForMainTabSwitchLayoutSmoke(Control root)
    {
        foreach (Control child in root.Controls)
        {
            yield return child;
            foreach (var nested in EnumerateControlsForMainTabSwitchLayoutSmoke(child))
            {
                yield return nested;
            }
        }
    }

    private static bool IsDescendantForMainTabSwitchLayoutSmoke(Control control, Control ancestor)
    {
        for (var current = control.Parent; current != null; current = current.Parent)
        {
            if (ReferenceEquals(current, ancestor)) return true;
        }

        return false;
    }
}
