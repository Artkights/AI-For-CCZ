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
        _exportShopEditorCsvButton.Text = "导出CSV";
        _exportShopEditorCsvButton.AutoSize = true;
        _exportShopEditorCsvButton.Enabled = false;
        _importShopEditorCsvButton.Text = "导入CSV";
        _importShopEditorCsvButton.AutoSize = true;
        _importShopEditorCsvButton.Enabled = false;
        _copyShopEditorSelectionButton.Text = "复制";
        _copyShopEditorSelectionButton.AutoSize = true;
        _pasteShopEditorSelectionButton.Text = "粘贴";
        _pasteShopEditorSelectionButton.AutoSize = true;
        _batchFillShopEditorColumnButton.Text = "批量填列";
        _batchFillShopEditorColumnButton.AutoSize = true;
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
            _exportShopEditorCsvButton,
            _importShopEditorCsvButton,
            _copyShopEditorSelectionButton,
            _pasteShopEditorSelectionButton,
            _batchFillShopEditorColumnButton,
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

    private void ReorderMainCreationTabs()
    {
        var preferred = new[]
        {
            "小暗的话",
            "角色设定",
            "兵种设定",
            "宝物设定",
            "图片设定",
            "地图编辑",
            "剧本编辑",
            "场景编辑",
            "战场编辑",
            "商店编辑"
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

    private sealed class CollapsiblePaneState
    {
        public required string Key { get; init; }
        public required TableLayoutPanel Container { get; init; }
        public required Control Content { get; init; }
        public required Button ToggleButton { get; init; }
        public required Label TitleLabel { get; init; }
        public SplitContainer? OwnerSplit { get; init; }
        public int SplitPanelIndex { get; init; }
        public bool Collapsed { get; set; }
        public int? SavedSplitterDistance { get; set; }
        public int SavedPanel1MinSize { get; set; }
        public int SavedPanel2MinSize { get; set; }
    }

    private const int CollapsiblePaneHeaderHeight = 28;
    private const int CollapsiblePaneCollapsedWidth = 32;
    private const int CollapsiblePaneCollapsedHeight = CollapsiblePaneHeaderHeight;
    private readonly Dictionary<string, CollapsiblePaneState> _collapsiblePanes = new(StringComparer.Ordinal);

    private Control CreateCollapsiblePane(
        string title,
        Control content,
        string paneKey,
        SplitContainer? ownerSplit = null,
        int splitPanelIndex = 0)
    {
        paneKey = NormalizeUiLayoutKey(paneKey);
        content.Dock = DockStyle.Fill;

        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            RowCount = 2,
            ColumnCount = 1,
            Margin = Padding.Empty,
            Padding = Padding.Empty
        };
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, CollapsiblePaneHeaderHeight));
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var header = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            RowCount = 1,
            ColumnCount = 2,
            Cursor = Cursors.Hand,
            BackColor = Color.FromArgb(238, 238, 238),
            Margin = Padding.Empty,
            Padding = new Padding(2, 2, 4, 2)
        };
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 24));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        var toggleButton = new Button
        {
            Text = "v",
            Dock = DockStyle.Fill,
            Margin = Padding.Empty,
            Padding = Padding.Empty,
            FlatStyle = FlatStyle.Flat,
            TabStop = false,
            UseVisualStyleBackColor = false,
            BackColor = Color.FromArgb(248, 248, 248),
            AccessibleName = $"{title} 折叠"
        };
        toggleButton.FlatAppearance.BorderSize = 0;

        var titleLabel = new Label
        {
            Text = title,
            Dock = DockStyle.Fill,
            AutoEllipsis = true,
            TextAlign = ContentAlignment.MiddleLeft,
            Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Bold, GraphicsUnit.Point),
            Margin = new Padding(4, 0, 0, 0)
        };

        header.Controls.Add(toggleButton, 0, 0);
        header.Controls.Add(titleLabel, 1, 0);
        panel.Controls.Add(header, 0, 0);
        panel.Controls.Add(content, 0, 1);

        var state = new CollapsiblePaneState
        {
            Key = paneKey,
            Container = panel,
            Content = content,
            ToggleButton = toggleButton,
            TitleLabel = titleLabel,
            OwnerSplit = ownerSplit,
            SplitPanelIndex = splitPanelIndex
        };
        _collapsiblePanes[paneKey] = state;

        void Toggle() => ToggleCollapsiblePane(state);
        toggleButton.Click += (_, _) => Toggle();
        titleLabel.Click += (_, _) => Toggle();
        header.Click += (_, _) => Toggle();

        return panel;
    }

    private void AddCollapsibleSplitPanel(
        SplitContainer split,
        int splitPanelIndex,
        string title,
        Control content,
        string paneKey)
    {
        var wrapper = CreateCollapsiblePane(title, content, paneKey, split, splitPanelIndex);
        var panel = splitPanelIndex == 1 ? split.Panel1 : split.Panel2;
        panel.Controls.Add(wrapper);
    }

    private void ToggleCollapsiblePane(CollapsiblePaneState state)
    {
        if (state.Collapsed)
        {
            ExpandCollapsiblePane(state);
        }
        else
        {
            CollapseCollapsiblePane(state);
        }
    }

    private void CollapseCollapsiblePane(CollapsiblePaneState state)
    {
        if (state.OwnerSplit is { } split)
        {
            foreach (var sibling in _collapsiblePanes.Values)
            {
                if (ReferenceEquals(sibling, state)) continue;
                if (!ReferenceEquals(sibling.OwnerSplit, split)) continue;
                if (sibling.Collapsed)
                {
                    ExpandCollapsiblePane(sibling);
                }
            }

            state.SavedSplitterDistance = split.SplitterDistance;
            state.SavedPanel1MinSize = split.Panel1MinSize;
            state.SavedPanel2MinSize = split.Panel2MinSize;
        }

        state.Collapsed = true;
        ApplyCollapsiblePaneVisualState(state);
        ApplyCollapsedSplitDistance(state);
    }

    private void ExpandCollapsiblePane(CollapsiblePaneState state)
    {
        state.Collapsed = false;
        ApplyCollapsiblePaneVisualState(state);
        RestoreCollapsedSplitDistance(state);
    }

    private void ApplyCollapsiblePaneVisualState(CollapsiblePaneState state)
    {
        state.ToggleButton.Text = state.Collapsed ? ">" : "v";
        state.ToggleButton.AccessibleName = state.Collapsed
            ? $"{state.TitleLabel.Text} 展开"
            : $"{state.TitleLabel.Text} 折叠";
        state.Content.Visible = !state.Collapsed;

        if (state.Container.RowStyles.Count > 1)
        {
            state.Container.RowStyles[1].SizeType = state.Collapsed ? SizeType.Absolute : SizeType.Percent;
            state.Container.RowStyles[1].Height = state.Collapsed ? 0 : 100;
        }

        state.Container.PerformLayout();
    }

    private bool TryApplyCollapsedSplitDistance(SplitContainer split)
    {
        foreach (var state in _collapsiblePanes.Values)
        {
            if (!state.Collapsed) continue;
            if (!ReferenceEquals(state.OwnerSplit, split)) continue;

            ApplyCollapsedSplitDistance(state);
            return true;
        }

        return false;
    }

    private void ApplyCollapsedSplitDistance(CollapsiblePaneState state)
    {
        if (state.OwnerSplit is not { } split || split.IsDisposed) return;

        var totalLength = split.Orientation == Orientation.Vertical ? split.Width : split.Height;
        var usableLength = totalLength - split.SplitterWidth;
        if (usableLength <= 0) return;

        var collapsedExtent = split.Orientation == Orientation.Vertical
            ? CollapsiblePaneCollapsedWidth
            : CollapsiblePaneCollapsedHeight;
        var target = state.SplitPanelIndex == 1
            ? Math.Min(collapsedExtent, usableLength)
            : Math.Max(0, usableLength - collapsedExtent);

        SetSplitterDistanceSafely(split, target, panel1MinSize: 0, panel2MinSize: 0);
    }

    private static void RestoreCollapsedSplitDistance(CollapsiblePaneState state)
    {
        if (state.OwnerSplit is not { } split || split.IsDisposed) return;
        if (state.SavedSplitterDistance is not { } savedDistance) return;

        SetSplitterDistanceSafely(
            split,
            savedDistance,
            state.SavedPanel1MinSize,
            state.SavedPanel2MinSize);
        state.SavedSplitterDistance = null;
    }

    private static void SetSplitterDistanceSafely(
        SplitContainer split,
        int targetDistance,
        int panel1MinSize,
        int panel2MinSize)
    {
        var totalLength = split.Orientation == Orientation.Vertical ? split.Width : split.Height;
        var usableLength = totalLength - split.SplitterWidth;
        if (usableLength <= 0) return;

        var canUseRequestedMins = usableLength > panel1MinSize + panel2MinSize;
        var panel1Min = canUseRequestedMins ? panel1MinSize : 0;
        var panel2Min = canUseRequestedMins ? panel2MinSize : 0;
        var maxDistance = Math.Max(panel1Min, usableLength - panel2Min);
        var target = Math.Clamp(targetDistance, panel1Min, maxDistance);

        split.Panel1MinSize = 0;
        split.Panel2MinSize = 0;
        if (split.SplitterDistance != target)
        {
            split.SplitterDistance = target;
        }

        split.Panel1MinSize = panel1Min;
        split.Panel2MinSize = panel2Min;
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

    private void ConfigureSplitContainerDistanceAfterLayout(
        string layoutKey,
        SplitContainer split,
        int desiredDistance,
        int desiredPanel1Min,
        int desiredPanel2Min)
        => ConfigureSplitContainerDistanceAfterLayout(
            split,
            layoutKey,
            desiredDistance,
            desiredPanel1Min,
            desiredPanel2Min);

    private void ConfigureSplitContainerDistanceAfterLayout(SplitContainer split, string layoutKey, int desiredDistance, int desiredPanel1Min, int desiredPanel2Min)
    {
        layoutKey = NormalizeUiLayoutKey(layoutKey);
        _uiLayoutSplits[layoutKey] = split;
        var applyingProgrammaticDistance = false;
        var userMovingSplitter = false;

        void Apply()
        {
            if (split.IsDisposed) return;
            var totalLength = split.Orientation == Orientation.Vertical ? split.Width : split.Height;
            if (totalLength <= split.SplitterWidth + 2) return;
            if (TryApplyCollapsedSplitDistance(split)) return;

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
        split.SplitterMoving += (_, _) =>
        {
            if (applyingProgrammaticDistance) return;
            if (Control.MouseButtons == MouseButtons.None) return;

            userMovingSplitter = true;
        };
        split.SplitterMoved += (_, _) =>
        {
            if (applyingProgrammaticDistance) return;
            if (!userMovingSplitter) return;

            SaveSplitContainerRatio(layoutKey, split);
            SaveUiLayoutSettings(updateWindow: false);
            userMovingSplitter = false;
        };
        if (split.IsHandleCreated)
        {
            split.BeginInvoke((Action)Apply);
        }
    }

    private static string NormalizeUiLayoutKey(string layoutKey)
        => UiLayoutSettingsStore.NormalizeKey(layoutKey);

    private int ResolveConfiguredSplitterDistance(string layoutKey, int totalLength, int splitterWidth, int desiredDistance)
    {
        var usableLength = totalLength - splitterWidth;
        if (usableLength <= 0) return desiredDistance;

        if (UiLayoutSettingsStore.GetSplitRatio(_uiLayoutSettings, layoutKey) is { } savedRatio)
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
        SaveUiLayoutSettings();
    }

    private void LoadUiLayoutSettings()
        => _uiLayoutSettings = UiLayoutSettingsStore.Load();

    private void SaveUiLayoutSettings(bool updateWindow = true)
        => UiLayoutSettingsStore.Save(_uiLayoutSettings, _uiLayoutSplits.Keys, updateWindow);

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

}

