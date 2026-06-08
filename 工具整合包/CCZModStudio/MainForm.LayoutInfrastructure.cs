using CCZModStudio.Core;
using CCZModStudio.Formats;
using CCZModStudio.Models;
using System.ComponentModel;
using System.Data;
using System.Diagnostics;
using System.Drawing.Imaging;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace CCZModStudio;

public sealed partial class MainForm
{
    private TabPage BuildShopEditorPage()
    {
        var page = new TabPage("商店编辑");
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            RowCount = 2,
            ColumnCount = 1,
            Padding = new Padding(6)
        };
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        page.Controls.Add(layout);

        var toolbar = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = true
        };
        _loadShopEditorButton.Text = "读取商店";
        _loadShopEditorButton.AutoSize = true;
        _saveShopEditorButton.Text = "保存商店";
        _saveShopEditorButton.AutoSize = true;
        _saveShopEditorButton.Enabled = false;
        _openShopDataTableButton.Text = "通用商店表";
        _openShopDataTableButton.AutoSize = true;
        _shopEditorSearchBox.Width = 220;
        _shopEditorSearchBox.PlaceholderText = "关卡/人物/物品/编号";
        _filterShopEditorButton.Text = "筛选";
        _filterShopEditorButton.AutoSize = true;
        _clearShopEditorFilterButton.Text = "清除";
        _clearShopEditorFilterButton.AutoSize = true;
        _shopBatchScopeCombo.DropDownStyle = ComboBoxStyle.DropDownList;
        _shopBatchScopeCombo.Width = 108;
        _shopBatchScopeCombo.Items.AddRange(new object[] { "当前筛选行", "选中行", "全部行" });
        _shopBatchScopeCombo.SelectedIndex = 0;
        _shopBatchSlotCombo.DropDownStyle = ComboBoxStyle.DropDownList;
        _shopBatchSlotCombo.Width = 96;
        _shopBatchSetItemCombo.DropDownStyle = ComboBoxStyle.DropDownList;
        _shopBatchSetItemCombo.Width = 230;
        _shopBatchFindItemCombo.DropDownStyle = ComboBoxStyle.DropDownList;
        _shopBatchFindItemCombo.Width = 170;
        _shopBatchReplaceItemCombo.DropDownStyle = ComboBoxStyle.DropDownList;
        _shopBatchReplaceItemCombo.Width = 170;
        _shopBatchSetButton.Text = "批量填入";
        _shopBatchSetButton.AutoSize = true;
        _shopBatchSetButton.Enabled = false;
        _shopBatchClearButton.Text = "批量清空";
        _shopBatchClearButton.AutoSize = true;
        _shopBatchClearButton.Enabled = false;
        _shopBatchReplaceButton.Text = "批量替换";
        _shopBatchReplaceButton.AutoSize = true;
        _shopBatchReplaceButton.Enabled = false;
        toolbar.Controls.AddRange(new Control[]
        {
            _loadShopEditorButton,
            _saveShopEditorButton,
            new Label { Text = "搜索：", AutoSize = true, Padding = new Padding(12, 7, 0, 0) },
            _shopEditorSearchBox,
            _filterShopEditorButton,
            _clearShopEditorFilterButton,
            new Label { Text = "批量：", AutoSize = true, Padding = new Padding(12, 7, 0, 0) },
            _shopBatchScopeCombo,
            _shopBatchSlotCombo,
            _shopBatchSetItemCombo,
            _shopBatchSetButton,
            _shopBatchClearButton,
            new Label { Text = "替换：", AutoSize = true, Padding = new Padding(12, 7, 0, 0) },
            _shopBatchFindItemCombo,
            _shopBatchReplaceItemCombo,
            _shopBatchReplaceButton
        });
        layout.Controls.Add(toolbar, 0, 0);

        _shopEditorGrid.Dock = DockStyle.Fill;
        _shopEditorGrid.AllowUserToAddRows = false;
        _shopEditorGrid.AllowUserToDeleteRows = false;
        _shopEditorGrid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None;
        _shopEditorGrid.SelectionMode = DataGridViewSelectionMode.CellSelect;

        layout.Controls.Add(_shopEditorGrid, 0, 1);
        return page;
    }

    private TabPage BuildCoreWorkbenchPage()
    {
        var page = new TabPage("核心创作");
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            RowCount = 3,
            ColumnCount = 1,
            Padding = new Padding(10)
        };
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        page.Controls.Add(layout);

        var title = new Label
        {
            Text = "曹操传 MOD 核心创作",
            AutoSize = true,
            Font = new Font("Microsoft YaHei UI", 14F, FontStyle.Bold, GraphicsUnit.Point),
            Padding = new Padding(0, 0, 0, 6)
        };
        layout.Controls.Add(title, 0, 0);

        var toolbar = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = true
        };
        toolbar.Controls.Add(BuildCoreButton("打开项目", OpenProjectDialog));
        toolbar.Controls.Add(BuildCoreButton("创建测试副本", CreateTestCopy));
        toolbar.Controls.Add(BuildCoreButton("项目维护", () => SelectTabPageByText("项目维护")));
        layout.Controls.Add(toolbar, 0, 1);

        var grid = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            RowCount = 2,
            ColumnCount = 4,
            Padding = new Padding(0, 8, 0, 0)
        };
        var columnPercent = 100F / 4F;
        for (var i = 0; i < 4; i++)
        {
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, columnPercent));
        }
        for (var i = 0; i < 2; i++)
        {
            grid.RowStyles.Add(new RowStyle(SizeType.Percent, 50F));
        }

        grid.Controls.Add(BuildCoreModuleCard(
            "角色设定",
            "编辑人物基础数据、能力、职业、头像与关联编号。",
            ("角色编辑器", OpenRoleEditor),
            ("全局设定", OpenGlobalSettingsDialog)), 0, 0);
        grid.Controls.Add(BuildCoreModuleCard(
            "兵种设定",
            "集中处理兵种系、详细兵种、兵种策略和兵种特效。",
            ("兵种编辑器", OpenJobEditor),
            ("兵种系/地形", () => { SelectTabPageByText("兵种设定"); SelectTabPageByText("兵种系/地形"); }),
            ("兵种策略", () => { SelectTabPageByText("兵种设定"); LoadJobStrategyEditor(); }),
            ("兵种特效", OpenJobEffectEditor)), 1, 0);
        grid.Controls.Add(BuildCoreModuleCard(
            "宝物设定",
            "编辑物品/宝物名称、价格、图标、能力、特效、图鉴和说明。",
            ("宝物编辑器", OpenItemEditor)), 2, 0);
        grid.Controls.Add(BuildCoreModuleCard(
            "图片处理",
            "管理头像、R/S形象、图标、范围图和背景等图片资源。",
            ("图片资源", OpenCoreImageResources),
            ("人物R/S", OpenCoreImageAssignments),
            ("资源索引", () => SelectTabPageByText("游戏资源索引"))), 3, 0);
        grid.Controls.Add(BuildCoreModuleCard(
            "地图制作",
            "底图、地形层、网格坐标与画笔编辑集中到同一画布。",
            ("地图制作", () => SelectTabPageByText("地图制作")),
            ("地图联动", () => SelectTabPageByText("关卡地图联动"))), 0, 1);
        grid.Controls.Add(BuildCoreModuleCard(
            "战场制作",
            "围绕关卡、地图、出场单位、坐标、阵营和 AI 组织战场数据。",
            ("战场编辑器", () => SelectTabPageByText("战场制作"))), 1, 1);
        grid.Controls.Add(BuildCoreModuleCard(
            "剧本制作",
            "推进事件树、命令参数、每关名称和商店配置。",
            ("剧本编辑器", () => SelectTabPageByText("剧本制作")),
            ("R场景制作", () => SelectTabPageByText("R场景制作")),
            ("商店编辑", OpenShopEditor),
            ("命令字典", () => SelectTabPageByText("剧本字典"))), 2, 1);
        grid.Controls.Add(BuildCoreModuleCard(
            "项目维护",
            "诊断、报告、补丁、资源索引和发布辅助集中放在这里。",
            ("维护区", () => SelectTabPageByText("项目维护")),
            ("项目文件", OpenProjectFileStatusPage),
            ("创作者备注", () => SelectTabPageByText("创作者备注"))), 3, 1);

        layout.Controls.Add(grid, 0, 2);
        return page;
    }

    private TabPage BuildProjectFileStatusPage()
    {
        var page = new TabPage("项目文件检查");
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            RowCount = 2,
            ColumnCount = 1,
            Padding = new Padding(8)
        };
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        page.Controls.Add(layout);

        var toolbar = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = true
        };
        _refreshProjectFileStatusButton.Text = "刷新文件状态";
        _refreshProjectFileStatusButton.AutoSize = true;
        toolbar.Controls.AddRange(new Control[]
        {
            _refreshProjectFileStatusButton
        });
        layout.Controls.Add(toolbar, 0, 0);

        _projectFileSummaryBox.Dock = DockStyle.Fill;
        _projectFileSummaryBox.Multiline = true;
        _projectFileSummaryBox.ReadOnly = true;
        _projectFileSummaryBox.Height = 72;
        _projectFileSummaryBox.ScrollBars = ScrollBars.Vertical;
        _projectFileSummaryBox.WordWrap = true;
        _projectFileSummaryBox.Text = "尚未加载项目。";

        _fileGrid.Dock = DockStyle.Fill;
        _fileGrid.ReadOnly = true;
        _fileGrid.AllowUserToAddRows = false;
        _fileGrid.AllowUserToDeleteRows = false;
        _fileGrid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        _fileGrid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        layout.Controls.Add(_fileGrid, 0, 1);

        return page;
    }

    private static GroupBox BuildCoreModuleCard(string title, string description, params (string Text, Action Action)[] actions)
    {
        _ = description;

        var box = new GroupBox
        {
            Text = title,
            Dock = DockStyle.Fill,
            Padding = new Padding(8),
            Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Bold, GraphicsUnit.Point)
        };
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            RowCount = 1,
            ColumnCount = 1
        };
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        box.Controls.Add(layout);

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = true
        };
        foreach (var action in actions)
        {
            buttons.Controls.Add(BuildCoreButton(action.Text, action.Action));
        }
        layout.Controls.Add(buttons, 0, 0);
        return box;
    }

    private static Button BuildCoreButton(string text, Action action)
    {
        var button = new Button
        {
            Text = text,
            AutoSize = true,
            Margin = new Padding(0, 4, 6, 0)
        };
        button.Click += (_, _) => action();
        return button;
    }

    private void MoveAdvancedPagesIntoAdvancedTab()
    {
        var advancedNames = new[]
        {
            "项目文件检查",
            "制作向导",
            "诊断/日志",
            "项目体检/发布检查",
            "测试副本差异/发布",
            "备份历史/回滚",
            "创作者备注",
            "补丁导入",
            "搬运配置预览",
            "剧本字典",
            "素材库",
            "游戏资源索引",
            "资源诊断",
            "关卡地图联动"
        };

        var advancedTabs = new TabControl { Dock = DockStyle.Fill };
        foreach (var name in advancedNames)
        {
            var page = _mainTabs.TabPages
                .Cast<TabPage>()
                .FirstOrDefault(tab => tab.Text.Equals(name, StringComparison.Ordinal));
            if (page == null) continue;

            _mainTabs.TabPages.Remove(page);
            advancedTabs.TabPages.Add(page);
        }

        if (advancedTabs.TabPages.Count == 0) return;

        var advancedPage = new TabPage("项目维护");
        advancedPage.Controls.Add(advancedTabs);
        _mainTabs.TabPages.Add(advancedPage);
    }

    private void ReorderMainCreationTabs()
    {
        var preferred = new[]
        {
            "核心创作",
            "角色设定",
            "兵种设定",
            "宝物设定",
            "地图制作",
            "战场制作",
            "剧本制作",
            "商店编辑",
            "图片处理",
            "项目维护"
        };

        var pages = _mainTabs.TabPages.Cast<TabPage>().ToList();
        _mainTabs.TabPages.Clear();
        foreach (var name in preferred)
        {
            var page = pages.FirstOrDefault(tab => tab.Text.Equals(name, StringComparison.Ordinal));
            if (page == null) continue;
            _mainTabs.TabPages.Add(page);
            pages.Remove(page);
        }

        foreach (var page in pages)
        {
            _mainTabs.TabPages.Add(page);
        }
    }

    private TabPage BuildWorkflowGuidePage()
    {
        var page = new TabPage("制作向导");
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            RowCount = 2,
            ColumnCount = 1,
            Padding = new Padding(6)
        };
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        page.Controls.Add(layout);

        var toolbar = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = true
        };

        _workflowRefreshButton.Text = "刷新流程状态";
        _workflowRefreshButton.AutoSize = true;
        _workflowOpenProjectButton.Text = "打开项目";
        _workflowOpenProjectButton.AutoSize = true;
        _workflowCreateTestCopyButton.Text = "创建测试副本";
        _workflowCreateTestCopyButton.AutoSize = true;
        _workflowRunAuditButton.Text = "项目体检";
        _workflowRunAuditButton.AutoSize = true;
        _workflowLoadCreatorNotesButton.Text = "读取备注";
        _workflowLoadCreatorNotesButton.AutoSize = true;
        _workflowAnalyzeDiffButton.Text = "生成差异";
        _workflowAnalyzeDiffButton.AutoSize = true;
        _workflowWriteDeliveryReportButton.Text = "综合报告";
        _workflowWriteDeliveryReportButton.AutoSize = true;
        _workflowOpenTablePageButton.Text = "去角色设定";
        _workflowOpenTablePageButton.AutoSize = true;
        _workflowOpenResourcePageButton.Text = "去资源索引";
        _workflowOpenResourcePageButton.AutoSize = true;
        _workflowOpenScenarioPageButton.Text = "去剧本制作";
        _workflowOpenScenarioPageButton.AutoSize = true;
        _workflowOpenScenarioMapPageButton.Text = "去关卡地图联动";
        _workflowOpenScenarioMapPageButton.AutoSize = true;
        _workflowHandleActionButton.Text = "处理选中行动";
        _workflowHandleActionButton.AutoSize = true;
        _workflowHandleActionAndNoteButton.Text = "处理并建对象备注";
        _workflowHandleActionAndNoteButton.AutoSize = true;
        _workflowCreateActionNoteButton.Text = "为行动建备注";
        _workflowCreateActionNoteButton.AutoSize = true;
        _workflowHandleDashboardButton.Text = "处理选中工作台项";
        _workflowHandleDashboardButton.AutoSize = true;
        _workflowRefreshEvidenceButton.Text = "刷新证据";
        _workflowRefreshEvidenceButton.AutoSize = true;
        _workflowOpenEvidenceButton.Text = "打开选中证据";
        _workflowOpenEvidenceButton.AutoSize = true;
        _workflowLocateEvidenceButton.Text = "定位证据文件";
        _workflowLocateEvidenceButton.AutoSize = true;

        toolbar.Controls.AddRange(new Control[]
        {
            _workflowRefreshButton,
            _workflowOpenProjectButton,
            _workflowCreateTestCopyButton,
            _workflowRunAuditButton,
            _workflowLoadCreatorNotesButton,
            _workflowAnalyzeDiffButton,
            _workflowWriteDeliveryReportButton,
            _workflowOpenTablePageButton,
            _workflowOpenResourcePageButton,
            _workflowOpenScenarioPageButton,
            _workflowOpenScenarioMapPageButton,
            _workflowHandleActionButton,
            _workflowHandleActionAndNoteButton,
            _workflowCreateActionNoteButton,
            _workflowHandleDashboardButton,
            _workflowRefreshEvidenceButton,
            _workflowOpenEvidenceButton,
            _workflowLocateEvidenceButton
        });
        layout.Controls.Add(toolbar, 0, 0);

        _workflowGuideInfoBox.Dock = DockStyle.Fill;
        _workflowGuideInfoBox.Multiline = true;
        _workflowGuideInfoBox.Height = 96;
        _workflowGuideInfoBox.ReadOnly = true;
        _workflowGuideInfoBox.ScrollBars = ScrollBars.Vertical;
        _workflowGuideInfoBox.WordWrap = true;
        _workflowGuideInfoBox.Text = "制作向导：请先打开项目。本页会用中文步骤串起项目识别、可写编辑、备注、差异、备份和发布。";
        _workflowActionGrid.Dock = DockStyle.Fill;
        _workflowActionGrid.ReadOnly = true;
        _workflowActionGrid.AllowUserToAddRows = false;
        _workflowActionGrid.AllowUserToDeleteRows = false;
        _workflowActionGrid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.DisplayedCells;
        _workflowActionGrid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;

        _workflowDashboardGrid.Dock = DockStyle.Fill;
        _workflowDashboardGrid.ReadOnly = true;
        _workflowDashboardGrid.AllowUserToAddRows = false;
        _workflowDashboardGrid.AllowUserToDeleteRows = false;
        _workflowDashboardGrid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.DisplayedCells;
        _workflowDashboardGrid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;

        _workflowEvidenceGrid.Dock = DockStyle.Fill;
        _workflowEvidenceGrid.ReadOnly = true;
        _workflowEvidenceGrid.AllowUserToAddRows = false;
        _workflowEvidenceGrid.AllowUserToDeleteRows = false;
        _workflowEvidenceGrid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.DisplayedCells;
        _workflowEvidenceGrid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;

        _workflowGuideGrid.Dock = DockStyle.Fill;
        _workflowGuideGrid.ReadOnly = true;
        _workflowGuideGrid.AllowUserToAddRows = false;
        _workflowGuideGrid.AllowUserToDeleteRows = false;
        _workflowGuideGrid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.DisplayedCells;
        _workflowGuideGrid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        var workflowTopSplit = CreateResizableSplit("BuildWorkflowGuidePage.ActionDashboard", Orientation.Horizontal, 220);
        workflowTopSplit.Panel1.Controls.Add(_workflowActionGrid);
        workflowTopSplit.Panel2.Controls.Add(_workflowDashboardGrid);
        var workflowBottomSplit = CreateResizableSplit("BuildWorkflowGuidePage.EvidenceGuide", Orientation.Horizontal, 220);
        workflowBottomSplit.Panel1.Controls.Add(_workflowEvidenceGrid);
        workflowBottomSplit.Panel2.Controls.Add(_workflowGuideGrid);
        var workflowGridSplit = CreateResizableSplit("BuildWorkflowGuidePage.TopBottom", Orientation.Horizontal, 420);
        workflowGridSplit.Panel1.Controls.Add(workflowTopSplit);
        workflowGridSplit.Panel2.Controls.Add(workflowBottomSplit);
        layout.Controls.Add(workflowGridSplit, 0, 1);

        return page;
    }

    private static Label MakeHeader(string text) => new()
    {
        Text = text,
        AutoSize = true,
        Dock = DockStyle.Fill,
        Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Bold, GraphicsUnit.Point),
        Padding = new Padding(0, 8, 0, 4)
    };

    private static Control CreateTitledPanel(string title, Control content, Padding headerPadding)
    {
        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            RowCount = 2,
            ColumnCount = 1
        };
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        panel.Controls.Add(new Label
        {
            Text = title,
            AutoSize = true,
            Padding = headerPadding,
            Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Bold, GraphicsUnit.Point)
        }, 0, 0);
        panel.Controls.Add(content, 0, 1);
        return panel;
    }

    private void ApplyAdaptiveDefaultWindowLayout()
    {
        var workingArea = GetPreferredWorkingArea();
        MinimumSize = GetAdaptiveMinimumWindowSize(workingArea);
        Size = GetAdaptiveSize(new Size(DefaultWindowWidth, DefaultWindowHeight), MinimumSize, workingArea);
    }

    private void ApplyAdaptiveDialogSizing(Form dialog, Size preferredSize, Size requestedMinimumSize)
    {
        var ownerBounds = WindowState == FormWindowState.Normal ? Bounds : RestoreBounds;
        var workingArea = GetBestWorkingArea(ownerBounds);
        var minimumSize = GetAdaptiveMinimumSize(requestedMinimumSize, workingArea);

        dialog.AutoScaleMode = AutoScaleMode.Dpi;
        dialog.AutoScroll = true;
        dialog.Font = Font;
        dialog.MinimumSize = minimumSize;
        dialog.Size = GetAdaptiveSize(preferredSize, minimumSize, workingArea);
    }

    private static Rectangle GetPreferredWorkingArea()
        => Screen.FromPoint(Cursor.Position).WorkingArea;

    private static Rectangle GetBestWorkingArea(Rectangle bounds)
    {
        if (bounds.Width <= 0 || bounds.Height <= 0)
        {
            return GetPreferredWorkingArea();
        }

        return Screen.FromRectangle(bounds).WorkingArea;
    }

    private static Size GetAdaptiveMinimumWindowSize(Rectangle workingArea)
        => GetAdaptiveMinimumSize(new Size(MinimumWindowWidth, MinimumWindowHeight), workingArea);

    private static Size GetAdaptiveMinimumSize(Size requestedMinimumSize, Rectangle workingArea)
    {
        var maxSize = GetMaxWindowSize(workingArea);
        return new Size(
            Math.Min(Math.Max(requestedMinimumSize.Width, AbsoluteMinimumWindowWidth), maxSize.Width),
            Math.Min(Math.Max(requestedMinimumSize.Height, AbsoluteMinimumWindowHeight), maxSize.Height));
    }

    private static Size GetAdaptiveSize(Size preferredSize, Size minimumSize, Rectangle workingArea)
    {
        var maxSize = GetMaxWindowSize(workingArea);
        var maxWidth = Math.Max(minimumSize.Width, maxSize.Width);
        var maxHeight = Math.Max(minimumSize.Height, maxSize.Height);
        return new Size(
            Math.Clamp(preferredSize.Width, minimumSize.Width, maxWidth),
            Math.Clamp(preferredSize.Height, minimumSize.Height, maxHeight));
    }

    private static Size GetMaxWindowSize(Rectangle workingArea)
    {
        var horizontalMargin = Math.Min(WindowScreenMargin * 2, Math.Max(0, workingArea.Width - 1));
        var verticalMargin = Math.Min(WindowScreenMargin * 2, Math.Max(0, workingArea.Height - 1));
        return new Size(
            Math.Max(1, workingArea.Width - horizontalMargin),
            Math.Max(1, workingArea.Height - verticalMargin));
    }

    private static Rectangle FitBoundsIntoWorkingArea(Rectangle bounds, Rectangle workingArea)
    {
        var minimumSize = GetAdaptiveMinimumWindowSize(workingArea);
        var size = GetAdaptiveSize(bounds.Size, minimumSize, workingArea);
        var left = bounds.Left;
        var top = bounds.Top;

        if (left + size.Width > workingArea.Right)
        {
            left = workingArea.Right - size.Width;
        }

        if (top + size.Height > workingArea.Bottom)
        {
            top = workingArea.Bottom - size.Height;
        }

        left = Math.Max(workingArea.Left, left);
        top = Math.Max(workingArea.Top, top);

        return new Rectangle(new Point(left, top), size);
    }

    private static void EnableDoubleBuffering(Control control)
    {
        typeof(Control)
            .GetProperty("DoubleBuffered", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
            ?.SetValue(control, true);
    }

    private SplitContainer CreateResizableSplit(
        string layoutKey,
        Orientation orientation,
        int desiredDistance,
        int desiredPanel1Min = 25,
        int desiredPanel2Min = 25)
    {
        var split = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = orientation
        };
        ConfigureSplitContainerDistanceAfterLayout(
            split,
            layoutKey,
            desiredDistance,
            desiredPanel1Min,
            desiredPanel2Min);
        return split;
    }

    private void ConfigureSplitContainerDistanceAfterLayout(
        SplitContainer split,
        int desiredDistance,
        int desiredPanel1Min,
        int desiredPanel2Min,
        [CallerArgumentExpression(nameof(split))] string? splitExpression = null,
        [CallerMemberName] string? callerMemberName = null)
        => ConfigureSplitContainerDistanceAfterLayout(
            split,
            $"{callerMemberName ?? "Layout"}.{splitExpression ?? "split"}",
            desiredDistance,
            desiredPanel1Min,
            desiredPanel2Min);

    private void ConfigureSplitContainerDistanceAfterLayout(SplitContainer split, string layoutKey, int desiredDistance, int desiredPanel1Min, int desiredPanel2Min)
    {
        layoutKey = NormalizeUiLayoutKey(layoutKey);
        _uiLayoutSplits[layoutKey] = split;
        var applyingProgrammaticDistance = false;

        void Apply()
        {
            if (split.IsDisposed) return;
            var totalLength = split.Orientation == Orientation.Vertical ? split.Width : split.Height;
            if (totalLength <= split.SplitterWidth + 2) return;

            var canUseRequestedMins = totalLength > desiredPanel1Min + desiredPanel2Min + split.SplitterWidth;
            if (!canUseRequestedMins)
            {
                split.Panel1MinSize = 0;
                split.Panel2MinSize = 0;
            }

            var panel1Min = canUseRequestedMins ? desiredPanel1Min : 0;
            var panel2Min = canUseRequestedMins ? desiredPanel2Min : 0;
            var maxDistance = Math.Max(panel1Min, totalLength - panel2Min - split.SplitterWidth);
            var target = Math.Clamp(
                ResolveConfiguredSplitterDistance(layoutKey, totalLength, split.SplitterWidth, desiredDistance),
                panel1Min,
                maxDistance);
            if (split.SplitterDistance != target)
            {
                applyingProgrammaticDistance = true;
                try
                {
                    split.SplitterDistance = target;
                }
                finally
                {
                    applyingProgrammaticDistance = false;
                }
            }

            if (canUseRequestedMins)
            {
                split.Panel1MinSize = desiredPanel1Min;
                split.Panel2MinSize = desiredPanel2Min;
            }
        }

        split.SizeChanged += (_, _) => Apply();
        split.HandleCreated += (_, _) => Apply();
        split.SplitterMoved += (_, _) =>
        {
            if (applyingProgrammaticDistance) return;

            SaveSplitContainerRatio(layoutKey, split);
            SaveUiLayoutSettings();
        };
        if (split.IsHandleCreated)
        {
            split.BeginInvoke((Action)Apply);
        }
    }

    private static string NormalizeUiLayoutKey(string layoutKey)
        => layoutKey.Trim();

    private int ResolveConfiguredSplitterDistance(string layoutKey, int totalLength, int splitterWidth, int desiredDistance)
    {
        var usableLength = totalLength - splitterWidth;
        if (usableLength <= 0) return desiredDistance;

        if (_uiLayoutSettings.SplitRatios.TryGetValue(layoutKey, out var savedRatio)
            && !double.IsNaN(savedRatio)
            && !double.IsInfinity(savedRatio)
            && savedRatio > 0
            && savedRatio < 1)
        {
            return (int)Math.Round(usableLength * savedRatio);
        }

        return desiredDistance;
    }

    private void SaveSplitContainerRatio(string layoutKey, SplitContainer split)
    {
        if (split.IsDisposed) return;

        var totalLength = split.Orientation == Orientation.Vertical ? split.Width : split.Height;
        var usableLength = totalLength - split.SplitterWidth;
        if (usableLength <= 0) return;

        var ratio = Math.Clamp(split.SplitterDistance / (double)usableLength, 0.01, 0.99);
        _uiLayoutSettings.SplitRatios[layoutKey] = ratio;
    }

    private void SaveCurrentUiLayoutSettings()
    {
        CaptureWindowLayoutSettings();
        foreach (var pair in _uiLayoutSplits)
        {
            SaveSplitContainerRatio(pair.Key, pair.Value);
        }

        SaveUiLayoutSettings();
    }

    private void LoadUiLayoutSettings()
    {
        try
        {
            var path = GetUiLayoutSettingsPath();
            if (!File.Exists(path)) return;

            var settings = JsonSerializer.Deserialize<UiLayoutSettings>(
                File.ReadAllText(path),
                UiLayoutJsonOptions);
            if (settings == null) return;

            _uiLayoutSettings = settings;
            _uiLayoutSettings.SplitRatios ??= new Dictionary<string, double>(StringComparer.Ordinal);
        }
        catch
        {
            _uiLayoutSettings = new UiLayoutSettings();
        }
    }

    private void SaveUiLayoutSettings()
    {
        try
        {
            _uiLayoutSettings.Version = UiLayoutSettingsVersion;
            var path = GetUiLayoutSettingsPath();
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, JsonSerializer.Serialize(_uiLayoutSettings, UiLayoutJsonOptions));
        }
        catch
        {
            // Layout persistence is best effort; failed saves should not block editing.
        }
    }

    private static string GetUiLayoutSettingsPath()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        return string.IsNullOrWhiteSpace(appData)
            ? Path.Combine(AppContext.BaseDirectory, UiLayoutSettingsFileName)
            : Path.Combine(appData, "CCZModStudio", UiLayoutSettingsFileName);
    }

    private void CaptureWindowLayoutSettings()
    {
        var bounds = WindowState == FormWindowState.Normal ? Bounds : RestoreBounds;
        if (bounds.Width <= 0 || bounds.Height <= 0) return;

        _uiLayoutSettings.WindowLeft = bounds.Left;
        _uiLayoutSettings.WindowTop = bounds.Top;
        _uiLayoutSettings.WindowWidth = bounds.Width;
        _uiLayoutSettings.WindowHeight = bounds.Height;
        _uiLayoutSettings.WindowMaximized = WindowState == FormWindowState.Maximized;
    }

    private void ApplyWindowLayoutSettings()
    {
        if (_uiLayoutSettings.WindowWidth <= 0 || _uiLayoutSettings.WindowHeight <= 0) return;

        var savedBounds = new Rectangle(
            _uiLayoutSettings.WindowLeft,
            _uiLayoutSettings.WindowTop,
            _uiLayoutSettings.WindowWidth,
            _uiLayoutSettings.WindowHeight);
        var workingArea = GetBestWorkingArea(savedBounds);
        MinimumSize = GetAdaptiveMinimumWindowSize(workingArea);
        StartPosition = FormStartPosition.Manual;
        Bounds = FitBoundsIntoWorkingArea(savedBounds, workingArea);

        if (_uiLayoutSettings.WindowMaximized)
        {
            WindowState = FormWindowState.Maximized;
        }
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        _battlefieldUnitAnimationTimer.Stop();
        _battlefieldUnitAnimationTimer.Dispose();
        _rScenePlaybackTimer.Stop();
        _rScenePlaybackTimer.Dispose();
        ClearBattlefieldUnitFrameCache();
        ClearBattlefieldMapPreviewImages();
        SaveCurrentUiLayoutSettings();
        base.OnFormClosing(e);
    }

    private sealed class UiLayoutSettings
    {
        public int Version { get; set; } = UiLayoutSettingsVersion;
        public Dictionary<string, double> SplitRatios { get; set; } = new(StringComparer.Ordinal);
        public int WindowLeft { get; set; }
        public int WindowTop { get; set; }
        public int WindowWidth { get; set; }
        public int WindowHeight { get; set; }
        public bool WindowMaximized { get; set; }
    }
}
