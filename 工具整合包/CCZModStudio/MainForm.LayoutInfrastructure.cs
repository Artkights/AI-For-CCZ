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
    private static int _compactToolbarLayoutRequestCount;
    private static int _compactToolbarLayoutFlushCount;
    private static int _compactToolbarLayoutLastFlushProcessedCount;
    private static int _compactToolbarLayoutLastFlushSkippedCount;
    private static readonly object CompactToolbarLayoutSync = new();
    private static readonly HashSet<Control> PendingCompactToolbarLayouts = new();
    private static bool _compactToolbarLayoutFlushScheduled;

    private static IDisposable TracePerf(string name)
        => new PerfTraceScope(name);

    private static int CompactToolbarLayoutRequestCount
        => Volatile.Read(ref _compactToolbarLayoutRequestCount);

    private static int CompactToolbarLayoutFlushCount
        => Volatile.Read(ref _compactToolbarLayoutFlushCount);

    private static int CompactToolbarLayoutLastFlushProcessedCount
        => Volatile.Read(ref _compactToolbarLayoutLastFlushProcessedCount);

    private static int CompactToolbarLayoutLastFlushSkippedCount
        => Volatile.Read(ref _compactToolbarLayoutLastFlushSkippedCount);

    private static void RecordCompactToolbarLayoutRequest()
        => Interlocked.Increment(ref _compactToolbarLayoutRequestCount);

    private static void ScheduleCompactToolbarLayout(Control control)
    {
        if (control.IsDisposed || !control.IsHandleCreated) return;
        if (!IsControlInSelectedTabPath(control)) return;

        RecordCompactToolbarLayoutRequest();

        var shouldSchedule = false;
        lock (CompactToolbarLayoutSync)
        {
            PendingCompactToolbarLayouts.Add(control);
            if (!_compactToolbarLayoutFlushScheduled)
            {
                _compactToolbarLayoutFlushScheduled = true;
                shouldSchedule = true;
            }
        }

        if (!shouldSchedule) return;

        try
        {
            control.BeginInvoke((Action)FlushCompactToolbarLayouts);
        }
        catch (InvalidOperationException)
        {
            CancelCompactToolbarLayout(control, clearScheduleIfEmpty: true);
        }
    }

    private static void CancelCompactToolbarLayout(Control control, bool clearScheduleIfEmpty = false)
    {
        lock (CompactToolbarLayoutSync)
        {
            PendingCompactToolbarLayouts.Remove(control);
            if (clearScheduleIfEmpty && PendingCompactToolbarLayouts.Count == 0)
            {
                _compactToolbarLayoutFlushScheduled = false;
            }
        }
    }

    private static void FlushCompactToolbarLayouts()
    {
        var stopwatch = Stopwatch.StartNew();
        Control[] controls;
        lock (CompactToolbarLayoutSync)
        {
            controls = PendingCompactToolbarLayouts.ToArray();
            PendingCompactToolbarLayouts.Clear();
            _compactToolbarLayoutFlushScheduled = false;
        }

        var processed = 0;
        var skipped = 0;
        MainForm? owner = null;
        TabPage? selectedMainTab = null;
        foreach (var control in controls)
        {
            if (control.IsDisposed || !control.IsHandleCreated || !control.Visible || !IsControlInSelectedTabPath(control))
            {
                skipped++;
                continue;
            }

            if (owner == null)
            {
                owner = control.FindForm() as MainForm;
                selectedMainTab = owner?._mainTabs.SelectedTab;
            }

            var changed = false;
            switch (control)
            {
                case CompactToolbarStackPanel stack:
                    changed = stack.RunScheduledCompactLayout();
                    break;
                case CompactToolbarRowPanel row:
                    changed = row.RunScheduledCompactLayout();
                    break;
            }

            if (changed)
            {
                processed++;
            }
            else
            {
                skipped++;
            }
        }

        if (processed > 0 && selectedMainTab?.IsDisposed == false && selectedMainTab.IsHandleCreated)
        {
            selectedMainTab.PerformLayout();
        }

        lock (CompactToolbarLayoutSync)
        {
            if (PendingCompactToolbarLayouts.Count > 0 && !_compactToolbarLayoutFlushScheduled)
            {
                var nextControl = PendingCompactToolbarLayouts.FirstOrDefault(control =>
                    !control.IsDisposed && control.IsHandleCreated && IsControlInSelectedTabPath(control));
                if (nextControl != null)
                {
                    _compactToolbarLayoutFlushScheduled = true;
                    try
                    {
                        nextControl.BeginInvoke((Action)FlushCompactToolbarLayouts);
                    }
                    catch (InvalidOperationException)
                    {
                        _compactToolbarLayoutFlushScheduled = false;
                    }
                }
            }
        }

        Interlocked.Increment(ref _compactToolbarLayoutFlushCount);
        Volatile.Write(ref _compactToolbarLayoutLastFlushProcessedCount, processed);
        Volatile.Write(ref _compactToolbarLayoutLastFlushSkippedCount, skipped);
        stopwatch.Stop();
        var tabName = selectedMainTab?.Text ?? "<none>";
        Debug.WriteLine($"[PERF] CompactToolbar.Flush: controls={controls.Length} processed={processed} skipped={skipped} elapsed={stopwatch.ElapsedMilliseconds}ms tab={tabName}");
    }

    private static bool IsControlInSelectedTabPath(Control control)
    {
        for (Control? current = control; current != null; current = current.Parent)
        {
            if (current is TabPage page && page.Parent is TabControl tabs && !ReferenceEquals(tabs.SelectedTab, page))
            {
                return false;
            }
        }

        return true;
    }

    private sealed class PerfTraceScope : IDisposable
    {
        private readonly string _name;
        private readonly Stopwatch _stopwatch = Stopwatch.StartNew();

        public PerfTraceScope(string name)
        {
            _name = name;
        }

        public void Dispose()
        {
            _stopwatch.Stop();
            Debug.WriteLine($"[PERF] {_name}: {_stopwatch.ElapsedMilliseconds} ms");
        }
    }

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

        var toolbar = CreateToolbarStack(3);
        _loadShopEditorButton.Text = "读取商店";
        ConfigureToolbarButton(_loadShopEditorButton, 88);
        _saveShopEditorButton.Text = "保存商店";
        ConfigureToolbarButton(_saveShopEditorButton, 88);
        _saveShopEditorButton.Enabled = false;
        _exportShopEditorCsvButton.Text = "导出CSV";
        ConfigureToolbarButton(_exportShopEditorCsvButton, 88);
        _exportShopEditorCsvButton.Enabled = false;
        _importShopEditorCsvButton.Text = "导入CSV";
        ConfigureToolbarButton(_importShopEditorCsvButton, 88);
        _importShopEditorCsvButton.Enabled = false;
        _copyShopEditorSelectionButton.Text = "复制";
        ConfigureToolbarButton(_copyShopEditorSelectionButton, 72);
        _pasteShopEditorSelectionButton.Text = "粘贴";
        ConfigureToolbarButton(_pasteShopEditorSelectionButton, 72);
        _batchFillShopEditorColumnButton.Text = "批量填列";
        ConfigureToolbarButton(_batchFillShopEditorColumnButton, 88);
        ConfigureToolbarInput(_shopEditorSearchBox, 220, 150);
        _shopEditorSearchBox.PlaceholderText = "关卡/人物/物品/编号";
        _filterShopEditorButton.Text = "筛选";
        ConfigureToolbarButton(_filterShopEditorButton, 72);
        _clearShopEditorFilterButton.Text = "清除";
        ConfigureToolbarButton(_clearShopEditorFilterButton, 72);
        _shopBatchScopeCombo.DropDownStyle = ComboBoxStyle.DropDownList;
        ConfigureToolbarInput(_shopBatchScopeCombo, 108, 100);
        _shopBatchScopeCombo.Items.AddRange(new object[] { "当前筛选行", "选中行", "全部行" });
        _shopBatchScopeCombo.SelectedIndex = 0;
        _shopBatchSlotCombo.DropDownStyle = ComboBoxStyle.DropDownList;
        ConfigureToolbarInput(_shopBatchSlotCombo, 96, 88);
        _shopBatchSetItemCombo.DropDownStyle = ComboBoxStyle.DropDownList;
        ConfigureToolbarInput(_shopBatchSetItemCombo, 230, 150);
        _shopBatchFindItemCombo.DropDownStyle = ComboBoxStyle.DropDownList;
        ConfigureToolbarInput(_shopBatchFindItemCombo, 170, 130);
        _shopBatchReplaceItemCombo.DropDownStyle = ComboBoxStyle.DropDownList;
        ConfigureToolbarInput(_shopBatchReplaceItemCombo, 170, 130);
        _shopBatchSetButton.Text = "批量填入";
        ConfigureToolbarButton(_shopBatchSetButton, 88);
        _shopBatchSetButton.Enabled = false;
        _shopBatchClearButton.Text = "批量清空";
        ConfigureToolbarButton(_shopBatchClearButton, 88);
        _shopBatchClearButton.Enabled = false;
        _shopBatchReplaceButton.Text = "批量替换";
        ConfigureToolbarButton(_shopBatchReplaceButton, 88);
        _shopBatchReplaceButton.Enabled = false;
        AddToolbarRow(toolbar, 0,
            _loadShopEditorButton,
            _saveShopEditorButton,
            _exportShopEditorCsvButton,
            _importShopEditorCsvButton,
            _copyShopEditorSelectionButton,
            _pasteShopEditorSelectionButton,
            _batchFillShopEditorColumnButton,
            CreateToolbarLabel("搜索："),
            _shopEditorSearchBox,
            _filterShopEditorButton,
            _clearShopEditorFilterButton);
        AddToolbarRow(toolbar, 1,
            CreateToolbarLabel("批量：", 0),
            _shopBatchScopeCombo,
            _shopBatchSlotCombo,
            _shopBatchSetItemCombo,
            _shopBatchSetButton,
            _shopBatchClearButton);
        AddToolbarRow(toolbar, 2,
            CreateToolbarLabel("替换：", 0),
            _shopBatchFindItemCombo,
            _shopBatchReplaceItemCombo,
            _shopBatchReplaceButton);
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
            "特效注入",
            "全局设定",
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

        try
        {
            split.Panel1MinSize = 0;
            split.Panel2MinSize = 0;
            if (split.SplitterDistance != target)
            {
                split.SplitterDistance = target;
            }

            split.Panel1MinSize = panel1Min;
            split.Panel2MinSize = panel2Min;
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentOutOfRangeException)
        {
            // SplitContainer can briefly report inconsistent bounds during DPI/layout changes.
        }
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
            try
            {
                if (split.IsDisposed) return;
                var totalLength = split.Orientation == Orientation.Vertical ? split.Width : split.Height;
                if (totalLength <= split.SplitterWidth + 2) return;
                if (TryApplyCollapsedSplitDistance(split)) return;

                split.Panel1MinSize = 0;
                split.Panel2MinSize = 0;

                var usableLength = totalLength - split.SplitterWidth;
                if (usableLength <= 0) return;

                var canUseRequestedMins = usableLength > desiredPanel1Min + desiredPanel2Min;
                var panel1Min = canUseRequestedMins ? desiredPanel1Min : 0;
                var panel2Min = canUseRequestedMins ? desiredPanel2Min : 0;
                var maxDistance = Math.Max(panel1Min, usableLength - panel2Min);
                if (maxDistance < panel1Min)
                {
                    return;
                }

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
            catch (Exception ex) when (ex is InvalidOperationException or ArgumentOutOfRangeException)
            {
                applyingProgrammaticDistance = false;
                // SplitContainer can briefly report inconsistent bounds during DPI/layout changes.
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

    private sealed class CompactToolbarStackPanel : TableLayoutPanel
    {
        private bool _requestingCompactLayout;
        private bool _selfVisible = true;
        private Control? _observedParent;
        private int _lastMeasuredWidth = -1;
        private int _lastAppliedHeight = -1;

        public bool IsSelfVisible => _selfVisible;

        public override Size GetPreferredSize(Size proposedSize)
            => MeasureToolbarStack(this, ResolveToolbarMeasureWidth(this, proposedSize.Width));

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            AttachCompactLayoutObservers();
            ScheduleCompactHeightUpdate();
        }

        protected override void OnControlAdded(ControlEventArgs e)
        {
            base.OnControlAdded(e);
            InvalidateCompactLayoutCache();
            ScheduleCompactHeightUpdate();
        }

        protected override void OnControlRemoved(ControlEventArgs e)
        {
            base.OnControlRemoved(e);
            InvalidateCompactLayoutCache();
            ScheduleCompactHeightUpdate();
        }

        protected override void OnLayout(LayoutEventArgs levent)
        {
            base.OnLayout(levent);
            ScheduleCompactHeightUpdate();
        }

        protected override void OnParentChanged(EventArgs e)
        {
            DetachCompactLayoutObservers();
            base.OnParentChanged(e);
            AttachCompactLayoutObservers();
            InvalidateCompactLayoutCache();
            ScheduleCompactHeightUpdate();
        }

        protected override void OnSizeChanged(EventArgs e)
        {
            base.OnSizeChanged(e);
            ScheduleCompactHeightUpdate();
        }

        protected override void OnVisibleChanged(EventArgs e)
        {
            base.OnVisibleChanged(e);
            if (Visible)
            {
                AttachCompactLayoutObservers();
            }
            else
            {
                CancelCompactToolbarLayout(this);
                DetachCompactLayoutObservers();
            }
            ScheduleCompactHeightUpdate();
        }

        protected override void SetVisibleCore(bool value)
        {
            _selfVisible = value;
            base.SetVisibleCore(value);
            if (value)
            {
                AttachCompactLayoutObservers();
            }
            else
            {
                CancelCompactToolbarLayout(this);
                DetachCompactLayoutObservers();
            }
            ScheduleCompactHeightUpdate();
        }

        protected override void OnHandleDestroyed(EventArgs e)
        {
            CancelCompactToolbarLayout(this);
            DetachCompactLayoutObservers();
            base.OnHandleDestroyed(e);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                CancelCompactToolbarLayout(this);
                DetachCompactLayoutObservers();
            }

            base.Dispose(disposing);
        }

        private void ScheduleCompactHeightUpdate()
        {
            if (!IsSelfVisible || !Visible || IsDisposed || !IsHandleCreated) return;
            if (!IsControlInSelectedTabPath(this)) return;
            ScheduleCompactToolbarLayout(this);
        }

        internal bool RunScheduledCompactLayout()
            => RequestCompactLayout();

        private bool RequestCompactLayout()
        {
            if (_requestingCompactLayout || IsDisposed) return false;
            if (!IsSelfVisible || !Visible || !IsControlInSelectedTabPath(this)) return false;

            try
            {
                _requestingCompactLayout = true;
                var changed = ApplyCompactHeight();
                Invalidate();
                return changed;
            }
            finally
            {
                _requestingCompactLayout = false;
            }
        }

        private void AttachCompactLayoutObservers()
        {
            DetachCompactLayoutObservers();

            _observedParent = Parent;
            if (_observedParent is not null)
            {
                _observedParent.SizeChanged += ObservedSizeChanged;
                _observedParent.Layout += ObservedLayout;
            }
        }

        private void DetachCompactLayoutObservers()
        {
            if (_observedParent is not null)
            {
                _observedParent.SizeChanged -= ObservedSizeChanged;
                _observedParent.Layout -= ObservedLayout;
                _observedParent = null;
            }
        }

        private void ObservedSizeChanged(object? sender, EventArgs e)
            => ScheduleCompactHeightUpdate();

        private void ObservedLayout(object? sender, LayoutEventArgs e)
            => ScheduleCompactHeightUpdate();

        private bool ApplyCompactHeight()
        {
            ReleaseToolbarMeasuredHeight(this);
            ResetToolbarParentRowAutoSize(this);

            if (!IsSelfVisible)
            {
                return false;
            }

            var width = ResolveToolbarMeasureWidth(this, Parent?.ClientSize.Width ?? 0);
            var availableWidth = Math.Max(1, width - Padding.Horizontal);
            var preferredHeight = MeasureToolbarStack(this, width).Height;
            if (width == _lastMeasuredWidth && preferredHeight == _lastAppliedHeight)
            {
                return false;
            }

            var changed = preferredHeight != _lastAppliedHeight;

            SuspendLayout();
            try
            {
                foreach (Control control in Controls)
                {
                    ReleaseToolbarMeasuredHeight(control);
                    ResetToolbarParentRowAutoSize(control);
                    if (!control.Visible) continue;

                    var childWidth = Math.Max(1, availableWidth - control.Margin.Horizontal);
                    if (control.Width != childWidth)
                    {
                        control.Width = childWidth;
                        changed = true;
                    }
                }
            }
            finally
            {
                ResumeLayout(false);
            }

            _lastMeasuredWidth = width;
            _lastAppliedHeight = preferredHeight;
            return changed;
        }

        private void InvalidateCompactLayoutCache()
        {
            _lastMeasuredWidth = -1;
            _lastAppliedHeight = -1;
        }
    }

    private sealed class CompactToolbarRowPanel : FlowLayoutPanel
    {
        private bool _requestingCompactLayout;
        private bool _selfVisible = true;
        private Control? _observedParent;
        private int _lastMeasuredWidth = -1;
        private int _lastAppliedHeight = -1;

        public bool IsSelfVisible => _selfVisible;

        public override Size GetPreferredSize(Size proposedSize)
            => MeasureToolbarRow(this, ResolveToolbarMeasureWidth(this, proposedSize.Width));

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            AttachCompactLayoutObservers();
            ScheduleCompactHeightUpdate();
        }

        protected override void OnControlAdded(ControlEventArgs e)
        {
            base.OnControlAdded(e);
            InvalidateCompactLayoutCache();
            ScheduleCompactHeightUpdate();
        }

        protected override void OnControlRemoved(ControlEventArgs e)
        {
            base.OnControlRemoved(e);
            InvalidateCompactLayoutCache();
            ScheduleCompactHeightUpdate();
        }

        protected override void OnLayout(LayoutEventArgs levent)
        {
            base.OnLayout(levent);
            ScheduleCompactHeightUpdate();
        }

        protected override void OnParentChanged(EventArgs e)
        {
            DetachCompactLayoutObservers();
            base.OnParentChanged(e);
            AttachCompactLayoutObservers();
            InvalidateCompactLayoutCache();
            ScheduleCompactHeightUpdate();
        }

        protected override void OnSizeChanged(EventArgs e)
        {
            base.OnSizeChanged(e);
            ScheduleCompactHeightUpdate();
        }

        protected override void OnVisibleChanged(EventArgs e)
        {
            base.OnVisibleChanged(e);
            if (Visible)
            {
                AttachCompactLayoutObservers();
            }
            else
            {
                CancelCompactToolbarLayout(this);
                DetachCompactLayoutObservers();
            }
            ScheduleCompactHeightUpdate();
        }

        protected override void SetVisibleCore(bool value)
        {
            _selfVisible = value;
            base.SetVisibleCore(value);
            if (value)
            {
                AttachCompactLayoutObservers();
            }
            else
            {
                CancelCompactToolbarLayout(this);
                DetachCompactLayoutObservers();
            }
            ScheduleCompactHeightUpdate();
        }

        protected override void OnHandleDestroyed(EventArgs e)
        {
            CancelCompactToolbarLayout(this);
            DetachCompactLayoutObservers();
            base.OnHandleDestroyed(e);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                CancelCompactToolbarLayout(this);
                DetachCompactLayoutObservers();
            }

            base.Dispose(disposing);
        }

        private void ScheduleCompactHeightUpdate()
        {
            if (!IsSelfVisible || !Visible || IsDisposed || !IsHandleCreated) return;
            if (!IsControlInSelectedTabPath(this)) return;
            ScheduleCompactToolbarLayout(this);
        }

        internal bool RunScheduledCompactLayout()
            => RequestCompactLayout();

        private bool RequestCompactLayout()
        {
            if (_requestingCompactLayout || IsDisposed) return false;
            if (!IsSelfVisible || !Visible || !IsControlInSelectedTabPath(this)) return false;

            try
            {
                _requestingCompactLayout = true;
                var changed = ApplyCompactHeight();
                Invalidate();
                return changed;
            }
            finally
            {
                _requestingCompactLayout = false;
            }
        }

        private void AttachCompactLayoutObservers()
        {
            DetachCompactLayoutObservers();

            _observedParent = Parent;
            if (_observedParent is not null)
            {
                _observedParent.SizeChanged += ObservedSizeChanged;
                _observedParent.Layout += ObservedLayout;
            }
        }

        private void DetachCompactLayoutObservers()
        {
            if (_observedParent is not null)
            {
                _observedParent.SizeChanged -= ObservedSizeChanged;
                _observedParent.Layout -= ObservedLayout;
                _observedParent = null;
            }
        }

        private void ObservedSizeChanged(object? sender, EventArgs e)
            => ScheduleCompactHeightUpdate();

        private void ObservedLayout(object? sender, LayoutEventArgs e)
            => ScheduleCompactHeightUpdate();

        private bool ApplyCompactHeight()
        {
            ReleaseToolbarMeasuredHeight(this);
            ResetToolbarParentRowAutoSize(this);

            if (!IsSelfVisible)
            {
                return false;
            }

            var width = ResolveToolbarMeasureWidth(this, Parent?.ClientSize.Width ?? 0);
            var preferredHeight = MeasureToolbarRow(this, width).Height;
            if (width == _lastMeasuredWidth && preferredHeight == _lastAppliedHeight)
            {
                return false;
            }

            PerformLayout();
            var changed = preferredHeight != _lastAppliedHeight || width != _lastMeasuredWidth;
            _lastMeasuredWidth = width;
            _lastAppliedHeight = preferredHeight;
            return changed;
        }

        private void InvalidateCompactLayoutCache()
        {
            _lastMeasuredWidth = -1;
            _lastAppliedHeight = -1;
        }
    }

    private static int ResolveToolbarMeasureWidth(Control control, int proposedWidth)
    {
        const int MinimumStableToolbarWidth = 240;
        const int DefaultToolbarMeasureWidth = 1600;
        static bool IsRealWidth(int width) => width is > 1 and < 100000;
        static bool IsStableWidth(int width) => width is >= MinimumStableToolbarWidth and < 100000;

        var widestMeasuredWidth = 0;
        void RememberWidth(int width)
        {
            if (IsRealWidth(width))
            {
                widestMeasuredWidth = Math.Max(widestMeasuredWidth, width);
            }
        }

        for (var current = control; current.Parent is not null; current = current.Parent)
        {
            var parent = current.Parent;
            if (parent is TableLayoutPanel tableLayout)
            {
                var position = tableLayout.GetPositionFromControl(current);
                var columnWidths = tableLayout.GetColumnWidths();
                if (position.Column >= 0 && position.Column < columnWidths.Length)
                {
                    var cellWidth = columnWidths[position.Column] - current.Margin.Horizontal;
                    if (IsStableWidth(cellWidth) && parent is not CompactToolbarStackPanel) return cellWidth;
                    RememberWidth(cellWidth);
                }

                if (parent.IsHandleCreated)
                {
                    var tableWidth = parent.ClientSize.Width - parent.Padding.Horizontal - current.Margin.Horizontal;
                    if (tableLayout.ColumnCount == 1 && IsStableWidth(tableWidth) && parent is not CompactToolbarStackPanel) return tableWidth;
                    RememberWidth(tableWidth);
                }
            }

            if (parent.IsHandleCreated)
            {
                var width = parent.ClientSize.Width - parent.Padding.Horizontal - current.Margin.Horizontal;
                if (IsStableWidth(width) && parent is not CompactToolbarStackPanel) return width;
                RememberWidth(width);
            }
        }

        var form = control.FindForm();
        var formWidth = form?.ClientSize.Width ?? 0;
        if (form?.IsHandleCreated == true && IsStableWidth(formWidth)) return formWidth;
        RememberWidth(formWidth);

        if (IsStableWidth(proposedWidth)) return proposedWidth;
        RememberWidth(proposedWidth);

        if (IsStableWidth(control.ClientSize.Width)) return control.ClientSize.Width;
        RememberWidth(control.ClientSize.Width);

        if (control.Parent is not null && IsStableWidth(control.Width)) return control.Width;
        RememberWidth(control.Width);

        if (widestMeasuredWidth > 0) return widestMeasuredWidth;

        return DefaultToolbarMeasureWidth;
    }

    private static void ReleaseToolbarMeasuredHeight(Control control)
    {
        var minimumSize = new Size(control.MinimumSize.Width, 0);
        var maximumSize = new Size(control.MaximumSize.Width, 0);

        if (control.MinimumSize != minimumSize) control.MinimumSize = minimumSize;
        if (control.MaximumSize != maximumSize) control.MaximumSize = maximumSize;
    }

    private static void ResetToolbarParentRowAutoSize(Control control)
    {
        if (control.Parent is not TableLayoutPanel tableLayout) return;

        var position = tableLayout.GetPositionFromControl(control);
        if (position.Row < 0 || position.Row >= tableLayout.RowStyles.Count) return;

        var rowStyle = tableLayout.RowStyles[position.Row];
        if (rowStyle.SizeType == SizeType.Percent) return;

        if (rowStyle.SizeType == SizeType.AutoSize && Math.Abs(rowStyle.Height) < 0.5f) return;

        rowStyle.SizeType = SizeType.AutoSize;
        rowStyle.Height = 0;
    }

    private static bool IsToolbarControlSelfVisible(Control control)
        => control switch
        {
            CompactToolbarStackPanel stack => stack.IsSelfVisible,
            CompactToolbarRowPanel row => row.IsSelfVisible,
            _ => control.Visible
        };

    private static Size MeasureToolbarStack(TableLayoutPanel stack, int width)
    {
        var availableWidth = Math.Max(1, width - stack.Padding.Horizontal);
        var totalHeight = stack.Padding.Vertical;

        foreach (Control control in stack.Controls)
        {
            if (!control.Visible) continue;

            var childWidth = Math.Max(1, availableWidth - control.Margin.Horizontal);
            var preferred = control is FlowLayoutPanel row
                ? MeasureToolbarRow(row, childWidth)
                : control.GetPreferredSize(new Size(childWidth, 0));
            totalHeight += preferred.Height + control.Margin.Vertical;
        }

        return new Size(width, totalHeight);
    }

    private static Size MeasureToolbarRow(FlowLayoutPanel row, int width)
    {
        var availableWidth = Math.Max(1, width - row.Padding.Horizontal);
        var totalHeight = row.Padding.Vertical;
        var lineWidth = 0;
        var lineHeight = 0;
        var maxLineWidth = 0;
        var hasVisibleControls = false;

        foreach (Control control in row.Controls)
        {
            if (!control.Visible) continue;

            hasVisibleControls = true;
            var preferred = control.GetPreferredSize(Size.Empty);
            var itemWidth = Math.Max(Math.Max(preferred.Width, control.Width), control.MinimumSize.Width) + control.Margin.Horizontal;
            var itemHeight = Math.Max(Math.Max(preferred.Height, control.Height), control.MinimumSize.Height) + control.Margin.Vertical;

            if (row.WrapContents && lineWidth > 0 && lineWidth + itemWidth > availableWidth)
            {
                totalHeight += lineHeight;
                maxLineWidth = Math.Max(maxLineWidth, lineWidth);
                lineWidth = 0;
                lineHeight = 0;
            }

            lineWidth += itemWidth;
            lineHeight = Math.Max(lineHeight, itemHeight);
        }

        if (!hasVisibleControls)
        {
            return new Size(width, row.Padding.Vertical);
        }

        totalHeight += lineHeight;
        maxLineWidth = Math.Max(maxLineWidth, lineWidth);

        return new Size(Math.Max(1, width), totalHeight);
    }

    private static TableLayoutPanel CreateToolbarStack(int rowCount)
    {
        var layout = new CompactToolbarStackPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            RowCount = rowCount,
            ColumnCount = 1,
            Margin = Padding.Empty,
            Padding = Padding.Empty
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        for (var i = 0; i < rowCount; i++)
        {
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        }

        return layout;
    }

    private static FlowLayoutPanel CreateToolbarRow() => new CompactToolbarRowPanel
    {
        Dock = DockStyle.Top,
        AutoSize = true,
        AutoSizeMode = AutoSizeMode.GrowAndShrink,
        FlowDirection = FlowDirection.LeftToRight,
        WrapContents = true,
        Margin = Padding.Empty,
        Padding = Padding.Empty
    };

    private static Label CreateToolbarLabel(string text, int leftPadding = 12) => new()
    {
        Text = text,
        AutoSize = true,
        Padding = new Padding(leftPadding, 7, 0, 0),
        Margin = new Padding(3)
    };

    private static void ConfigureToolbarButton(Button button, int minWidth = 72)
    {
        button.AutoSize = true;
        button.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        button.MinimumSize = new Size(minWidth, 28);
        button.Margin = new Padding(2, 1, 2, 1);
    }

    private static void ConfigureToolbarInput(Control control, int width, int minWidth = 120)
    {
        control.Width = width;
        control.MinimumSize = new Size(minWidth, 0);
        control.Margin = new Padding(3);
    }

    private static void ConfigureToolbarCheckBox(CheckBox checkBox)
    {
        checkBox.AutoSize = true;
        checkBox.Margin = new Padding(3, 6, 8, 3);
    }

    private static void ConfigureToolbarCheckBox(Button button)
        => ConfigureToolbarButton(button, 118);

    private static void AddToolbarRow(TableLayoutPanel stack, int rowIndex, params Control[] controls)
    {
        var row = CreateToolbarRow();
        row.Controls.AddRange(controls);
        stack.Controls.Add(row, 0, rowIndex);
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
        if (!_unsavedCloseConfirmed)
        {
            var unsavedItems = CollectUnsavedItems();
            if (unsavedItems.Count > 0)
            {
                e.Cancel = true;
                BeginCloseAfterUnsavedCheck(unsavedItems);
                return;
            }

            _unsavedCloseConfirmed = true;
        }

        ApplicationErrorService.ErrorReported -= OnApplicationErrorReported;
        _errorStatusClearTimer.Stop();
        _errorStatusClearTimer.Dispose();
        _battlefieldUnitAnimationTimer.Stop();
        _battlefieldUnitAnimationTimer.Dispose();
        _rScenePlaybackTimer.Stop();
        _rScenePlaybackTimer.Dispose();
        ClearJobStrategyAnimationPreview();
        _jobStrategyAnimationTimer.Stop();
        _jobStrategyAnimationTimer.Dispose();
        _mapMakerDirtyBaseRefreshTimer.Stop();
        _mapMakerDirtyBaseRefreshTimer.Dispose();
        CancelPendingMapMakerBeautify();
        _terrainVisualSynthesisService.Dispose();
        _terrainRenderService.Dispose();
        _mapMakerConfirmedFinalRender?.Dispose();
        _mapMakerConfirmedFinalRender = null;
        ClearMapWorkbenchMaterialThumbnailCache();
        ClearBattlefieldUnitFrameCache();
        ClearBattlefieldMapPreviewImages();
        _imageResourceLoadCts?.Cancel();
        _imageResourceLoadCts?.Dispose();
        _imageResourceLoadCts = null;
        _asyncLoadCoordinator.Dispose();
        _debouncedUiAction.Dispose();
        _materialLibraryCache.Dispose();
        SaveCurrentUiLayoutSettings();
        base.OnFormClosing(e);
    }

}

