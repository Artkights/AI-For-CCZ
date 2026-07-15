using CCZModStudio;
using CCZModStudio.Models;
using System.ComponentModel;
using System.Reflection;
using System.Windows.Forms;

internal partial class Program
{
    private sealed record GridViewportSmokeRow(int Id);

    static void RunGridViewportSmoke()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                Application.SetHighDpiMode(HighDpiMode.SystemAware);
                AssertSafeGridScrollingAtViewportBoundaries();
                AssertScaledGridMetrics();
                AssertRSceneHiddenCandidateGridLink();
                AssertApplicationErrorActions();
                Console.WriteLine("GRID_VIEWPORT_SMOKE_OK");
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
            throw new InvalidOperationException("Grid viewport smoke failed.", failure);
        }
    }

    private static void AssertSafeGridScrollingAtViewportBoundaries()
    {
        using var host = new Form { Width = 520, Height = 360, StartPosition = FormStartPosition.Manual };
        var tabs = new TabControl { Dock = DockStyle.Fill };
        var visiblePage = new TabPage("visible");
        var hiddenPage = new TabPage("hidden");
        tabs.TabPages.Add(visiblePage);
        tabs.TabPages.Add(hiddenPage);
        host.Controls.Add(tabs);

        var hiddenGrid = CreateGrid(100);
        hiddenGrid.Dock = DockStyle.Fill;
        hiddenPage.Controls.Add(hiddenGrid);

        var zeroHeightGrid = CreateGrid(8);
        zeroHeightGrid.SetBounds(8, 8, 220, 0);
        visiblePage.Controls.Add(zeroHeightGrid);

        var shortGrid = CreateGrid(8);
        shortGrid.SetBounds(8, 20, 220, 10);
        visiblePage.Controls.Add(shortGrid);

        var headerOnlyGrid = CreateGrid(8);
        headerOnlyGrid.SetBounds(8, 40, 220, headerOnlyGrid.ColumnHeadersHeight + 1);
        visiblePage.Controls.Add(headerOnlyGrid);

        var normalGrid = CreateGrid(100);
        normalGrid.SetBounds(250, 8, 240, 260);
        visiblePage.Controls.Add(normalGrid);

        host.Show();
        Application.DoEvents();

        if (MainForm.TryScrollGridRowIntoView(hiddenGrid, 90))
        {
            throw new InvalidOperationException("Hidden grid should defer scrolling.");
        }

        var selected = MainForm.SelectGridRow<GridViewportSmokeRow>(hiddenGrid, row => row.Id == 90);
        if (!selected || hiddenGrid.CurrentRow?.DataBoundItem is not GridViewportSmokeRow { Id: 90 })
        {
            throw new InvalidOperationException("Hidden grid selection must succeed even when scrolling is deferred.");
        }

        if (MainForm.SelectGridRow<GridViewportSmokeRow>(hiddenGrid, row => row.Id == 1000))
        {
            throw new InvalidOperationException("An unmatched grid predicate must return false.");
        }

        if (MainForm.TryScrollGridRowIntoView(zeroHeightGrid, 2) ||
            MainForm.TryScrollGridRowIntoView(shortGrid, 2) ||
            MainForm.TryScrollGridRowIntoView(headerOnlyGrid, 2))
        {
            throw new InvalidOperationException("A viewport without a displayable data row must not scroll.");
        }

        if (MainForm.TryScrollGridRowIntoView(normalGrid, -1) ||
            MainForm.TryScrollGridRowIntoView(normalGrid, normalGrid.Rows.Count))
        {
            throw new InvalidOperationException("Out-of-range grid rows must be rejected.");
        }

        normalGrid.Rows[25].Visible = false;
        if (MainForm.TryScrollGridRowIntoView(normalGrid, 25))
        {
            throw new InvalidOperationException("Invisible grid rows must not be scrolled into view.");
        }

        if (!MainForm.TryScrollGridRowIntoView(normalGrid, 80) || !IsGridRowDisplayed(normalGrid, 80))
        {
            throw new InvalidOperationException("A normal visible grid must scroll the target row into view.");
        }

        using var disposedGrid = CreateGrid(1);
        disposedGrid.Dispose();
        if (MainForm.TryScrollGridRowIntoView(disposedGrid, 0))
        {
            throw new InvalidOperationException("Disposed grids must be rejected.");
        }

        tabs.SelectedTab = hiddenPage;
        Application.DoEvents();
        if (!MainForm.TryScrollGridRowIntoView(hiddenGrid, 90) || !IsGridRowDisplayed(hiddenGrid, 90))
        {
            throw new InvalidOperationException("Deferred selection must become scrollable after its tab is shown.");
        }
    }

    private static void AssertScaledGridMetrics()
    {
        using var host = new Form { Width = 520, Height = 420, StartPosition = FormStartPosition.Manual };
        host.Show();
        Application.DoEvents();

        foreach (var dpi in new[] { 96, 120, 144, 192 })
        {
            using var grid = CreateGrid(60);
            var scale = dpi / 96F;
            grid.RowTemplate.Height = Math.Max(1, (int)Math.Round(23 * scale));
            grid.ColumnHeadersHeight = Math.Max(1, (int)Math.Round(23 * scale));
            grid.SetBounds(8, 8, 360, grid.ColumnHeadersHeight + 1);
            host.Controls.Add(grid);
            Application.DoEvents();

            if (MainForm.TryScrollGridRowIntoView(grid, 40))
            {
                throw new InvalidOperationException($"DPI {dpi}: header-only scaled viewport must defer scrolling.");
            }

            grid.Height = grid.ColumnHeadersHeight + grid.RowTemplate.Height * 4;
            Application.DoEvents();
            if (!MainForm.TryScrollGridRowIntoView(grid, 40) || !IsGridRowDisplayed(grid, 40))
            {
                throw new InvalidOperationException($"DPI {dpi}: scaled viewport did not reveal the target row.");
            }

            host.Controls.Remove(grid);
        }
    }

    private static void AssertRSceneHiddenCandidateGridLink()
    {
        var previousLazySetting = Environment.GetEnvironmentVariable("CCZMODSTUDIO_DISABLE_LAZY_UI");
        var previousLayoutPath = Environment.GetEnvironmentVariable("CCZMODSTUDIO_UI_LAYOUT_PATH");
        var layoutRoot = Path.Combine(Path.GetTempPath(), "CCZModStudio_GridViewportSmoke_" + Guid.NewGuid().ToString("N"));
        var layoutPath = Path.Combine(layoutRoot, "ui-layout.json");
        Environment.SetEnvironmentVariable("CCZMODSTUDIO_DISABLE_LAZY_UI", "1");
        Environment.SetEnvironmentVariable("CCZMODSTUDIO_UI_LAYOUT_PATH", layoutPath);
        var layoutSettings = new UiLayoutSettings();
        layoutSettings.SplitRatios["BuildRSceneEditorPage.ScriptScene"] = 0.01;
        layoutSettings.SplitRatios["BuildRSceneEditorPage.ActorListPreview"] = 0.99;
        layoutSettings.SplitRatios["BuildRSceneEditorPage.CanvasParameter"] = 0.01;
        UiLayoutSettingsStore.Save(layoutSettings);
        ApplicationErrorReport? reportedError = null;
        void CaptureError(ApplicationErrorReport report) => reportedError = report;
        ApplicationErrorService.ErrorReported += CaptureError;
        try
        {
            using var form = new MainForm();
            form.Show();
            Application.DoEvents();

            var mainTabs = GetPrivateFieldForGridViewportSmoke<TabControl>(form, "_mainTabs");
            var scenePage = mainTabs.TabPages.Cast<TabPage>().First(page => page.Text == "场景编辑");
            mainTabs.SelectedTab = scenePage;
            DrainGridViewportEvents();

            var grid = GetPrivateFieldForGridViewportSmoke<DataGridView>(form, "_rSceneCommandGrid");
            var commandPage = FindAncestor<TabPage>(grid)
                ?? throw new InvalidOperationException("R scene command grid is not hosted in a tab page.");
            var leftTabs = commandPage.Parent as TabControl
                ?? throw new InvalidOperationException("R scene command page is not hosted in a tab control.");
            var scriptPage = leftTabs.TabPages.Cast<TabPage>().First(page => page.Text == "R剧本");
            leftTabs.SelectedTab = scriptPage;
            DrainGridViewportEvents();

            var candidates = Enumerable.Range(0, 100)
                .Select(index => new RSceneStateCandidate
                {
                    Index = index,
                    SceneTitle = "Scene 0",
                    SceneIndex = 0,
                    SectionIndex = 0,
                    StartCommandIndex = index,
                    CurrentCommandIndex = index,
                    EndCommandIndex = index,
                    OffsetHex = $"0x{index:X8}",
                    Summary = "grid viewport smoke"
                })
                .ToList();
            SetPrivateFieldForGridViewportSmoke(form, "_currentRSceneStateCandidates", candidates);
            InvokePrivateForGridViewportSmoke(form, "BindRSceneStateCandidates", candidates);

            var windowStates = new[]
            {
                (State: FormWindowState.Normal, Size: form.MinimumSize),
                (State: FormWindowState.Normal, Size: new Size(1280, 760)),
                (State: FormWindowState.Maximized, Size: form.Size)
            };
            for (var index = 0; index < windowStates.Length; index++)
            {
                form.WindowState = windowStates[index].State;
                if (form.WindowState == FormWindowState.Normal)
                {
                    form.Size = windowStates[index].Size;
                }
                form.PerformLayout();
                DrainGridViewportEvents();

                var commandIndex = 90 + index;
                InvokePrivateForGridViewportSmoke(form, "SelectRSceneStateCandidateForCommand", new ScenarioStructureRow
                {
                    NodeType = "Command候选",
                    SceneIndex = 0,
                    SectionIndex = 0,
                    CommandIndex = commandIndex
                });
            }
            form.WindowState = FormWindowState.Normal;

            if (reportedError != null)
            {
                throw new InvalidOperationException("Hidden R scene candidate linking reported an application error: " + reportedError.Details);
            }

            if (grid.CurrentRow?.DataBoundItem is not RSceneStateCandidate { StartCommandIndex: 92 })
            {
                throw new InvalidOperationException("Hidden R scene candidate row was not selected.");
            }

            if (GetPrivateFieldForGridViewportSmoke<bool>(form, "_bindingRSceneCommandSelection"))
            {
                throw new InvalidOperationException("R scene selection re-entry guard was not restored.");
            }

            leftTabs.SelectedTab = commandPage;
            DrainGridViewportEvents();
            if (!IsGridRowDisplayed(grid, 92))
            {
                throw new InvalidOperationException("Selected R scene candidate was not revealed after opening its tab.");
            }

            leftTabs.SelectedTab = scriptPage;
            leftTabs.SelectedTab = commandPage;
            leftTabs.SelectedTab = scriptPage;
            leftTabs.SelectedTab = commandPage;
            DrainGridViewportEvents();
            if (reportedError != null || GetPrivateFieldForGridViewportSmoke<bool>(form, "_bindingRSceneCommandSelection"))
            {
                throw new InvalidOperationException("Rapid R scene sub-tab switching caused an error or left the guard set.");
            }
        }
        finally
        {
            ApplicationErrorService.ErrorReported -= CaptureError;
            Environment.SetEnvironmentVariable("CCZMODSTUDIO_DISABLE_LAZY_UI", previousLazySetting);
            Environment.SetEnvironmentVariable("CCZMODSTUDIO_UI_LAYOUT_PATH", previousLayoutPath);
            try
            {
                if (Directory.Exists(layoutRoot)) Directory.Delete(layoutRoot, recursive: true);
            }
            catch
            {
            }
        }
    }

    private static void AssertApplicationErrorActions()
    {
        using var form = new MainForm();
        form.Show();
        Application.DoEvents();

        var report = new ApplicationErrorReport(
            DateTimeOffset.Now,
            "Grid viewport smoke",
            "smoke failure",
            Path.Combine(Path.GetTempPath(), "missing-cczmodstudio-smoke.log"),
            typeof(InvalidOperationException).FullName!,
            "smoke failure",
            "Version: smoke" + Environment.NewLine + "Stack:" + Environment.NewLine + "smoke stack");
        InvokePrivateForGridViewportSmoke(form, "ShowApplicationErrorNotification", report);

        var openLogButton = GetPrivateFieldForGridViewportSmoke<ToolStripButton>(form, "_statusErrorOpenLogButton");
        var copyButton = GetPrivateFieldForGridViewportSmoke<ToolStripButton>(form, "_statusErrorCopyButton");
        var closeButton = GetPrivateFieldForGridViewportSmoke<ToolStripButton>(form, "_statusErrorCloseButton");
        var statusLabel = GetPrivateFieldForGridViewportSmoke<ToolStripStatusLabel>(form, "_statusLabel");
        var statusText = statusLabel.Text ?? string.Empty;
        if (!openLogButton.Visible || !copyButton.Visible || !closeButton.Visible ||
            !statusText.Contains("smoke failure", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Application error support actions were not shown.");
        }

        InvokePrivateForGridViewportSmoke(form, "ClearApplicationErrorNotification");
        if (openLogButton.Visible || copyButton.Visible || closeButton.Visible)
        {
            throw new InvalidOperationException("Application error support actions were not cleared.");
        }
    }

    private static DataGridView CreateGrid(int rowCount)
    {
        var grid = new DataGridView
        {
            AllowUserToAddRows = false,
            AutoGenerateColumns = true,
            DataSource = new BindingList<GridViewportSmokeRow>(
                Enumerable.Range(0, rowCount).Select(index => new GridViewportSmokeRow(index)).ToList())
        };
        return grid;
    }

    private static bool IsGridRowDisplayed(DataGridView grid, int rowIndex)
    {
        if (rowIndex < 0 || rowIndex >= grid.Rows.Count || grid.DisplayedRowCount(true) <= 0)
        {
            return false;
        }

        var first = grid.FirstDisplayedScrollingRowIndex;
        if (first < 0)
        {
            return false;
        }

        var displayedRows = grid.DisplayedRowCount(true);
        var visiblePosition = 0;
        for (var index = first; index <= rowIndex; index++)
        {
            if (grid.Rows[index].Visible)
            {
                visiblePosition++;
            }
        }

        return visiblePosition > 0 && visiblePosition <= displayedRows;
    }

    private static T? FindAncestor<T>(Control? control) where T : Control
    {
        for (var current = control?.Parent; current != null; current = current.Parent)
        {
            if (current is T match)
            {
                return match;
            }
        }

        return null;
    }

    private static void DrainGridViewportEvents()
    {
        for (var i = 0; i < 6; i++)
        {
            Application.DoEvents();
            Thread.Sleep(1);
        }
    }

    private static T GetPrivateFieldForGridViewportSmoke<T>(object instance, string fieldName)
    {
        var field = instance.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("Missing field " + fieldName);
        return (T)field.GetValue(instance)!;
    }

    private static void SetPrivateFieldForGridViewportSmoke<T>(object instance, string fieldName, T value)
    {
        var field = instance.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("Missing field " + fieldName);
        field.SetValue(instance, value);
    }

    private static void InvokePrivateForGridViewportSmoke(object instance, string methodName, params object?[] arguments)
    {
        var method = instance.GetType().GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("Missing method " + methodName);
        try
        {
            method.Invoke(instance, arguments);
        }
        catch (TargetInvocationException ex) when (ex.InnerException != null)
        {
            throw ex.InnerException;
        }
    }
}
