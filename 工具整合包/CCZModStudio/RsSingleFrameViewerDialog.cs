using CCZModStudio.Core;
using System.Drawing.Drawing2D;

namespace CCZModStudio;

internal sealed class RsSingleFrameViewerDialog : Form
{
    private const int DesiredPanel1WidthLogical = 480;
    private const int DesiredPanel1MinWidthLogical = 300;
    private const int DesiredPanel2MinWidthLogical = 300;
    private const int MinimumGroupWidthLogical = 280;
    private const int MinimumGroupFlowWidthLogical = 240;
    private const int ResponsiveWidthLogical = 900;
    private const int DesiredStackedPanel1HeightLogical = 340;
    private const int DesiredStackedPanel1MinHeightLogical = 220;
    private const int DesiredStackedPanel2MinHeightLogical = 220;

    private readonly Func<int, int, RsFrameCatalog> _catalogFactory;
    private readonly Func<RsFrameDescriptor, bool>? _editFrame;
    private readonly SplitContainer _mainSplit = new();
    private readonly FlowLayoutPanel _groupsPanel = new();
    private readonly RsFramePreviewControl _largePreview = new();
    private readonly Label _titleLabel = new();
    private readonly Label _metadataLabel = new();
    private readonly Label _messageLabel = new();
    private readonly Button _editButton = new();
    private readonly ComboBox _stageCombo = new();
    private readonly ComboBox _factionCombo = new();
    private readonly Label _stageLabel = new();
    private readonly Label _factionLabel = new();
    private readonly List<RsFrameThumbnailControl> _thumbnails = new();
    private RsFrameCatalog _catalog;
    private RsFrameDescriptor? _selectedFrame;
    private bool _updatingOptions;
    private bool _resourcesDisposed;
    private bool _mainSplitterInitialized;
    private bool _adjustingMainSplitter;
    private bool _splitterRetryScheduled;
    private bool _adjustingResponsiveLayout;
    private bool _layoutReady;
    private int? _preferredMainSplitterDistance;

    public RsSingleFrameViewerDialog(
        RsFrameCatalog catalog,
        Func<int, int, RsFrameCatalog> catalogFactory,
        Func<RsFrameDescriptor, bool>? editFrame)
    {
        _catalog = catalog;
        _catalogFactory = catalogFactory;
        _editFrame = editFrame;

        try
        {
            Text = "查看 R/S 单帧";
            StartPosition = FormStartPosition.CenterParent;
            ShowInTaskbar = false;
            MinimizeBox = false;
            AutoScaleMode = AutoScaleMode.Dpi;
            Width = 1120;
            Height = 760;
            MinimumSize = new Size(640, 480);

            Controls.Add(BuildLayout());
            _layoutReady = true;
            FormClosed += (_, _) => DisposeResources();
            Shown += (_, _) =>
            {
                ConstrainToWorkingArea();
                UpdateResponsiveLayout(useDesiredDistance: true);
            };
            DpiChanged += (_, _) => ScheduleResponsiveLayout();
            LocationChanged += (_, _) => ScheduleResponsiveLayout();
            LoadCatalogView(preferredGroup: null, preferredFrameIndex: 0);
        }
        catch
        {
            DisposeResources();
            base.Dispose();
            throw;
        }
    }

    internal static bool TryShowOwned(
        IWin32Window owner,
        Func<RsFrameCatalog> initialCatalogFactory,
        Func<int, int, RsFrameCatalog> catalogFactory,
        Func<RsFrameDescriptor, bool>? editFrame,
        string errorTitle,
        string errorSource)
    {
        var ownerControl = owner as Control;
        var previousCursor = ownerControl?.Cursor;
        try
        {
            if (ownerControl != null) ownerControl.Cursor = Cursors.WaitCursor;
            var initial = initialCatalogFactory();
            if (ownerControl != null) ownerControl.Cursor = previousCursor ?? Cursors.Default;

            // Ownership transfers as soon as the constructor is entered. The dialog also
            // disposes the catalog on a constructor failure, not only after ShowDialog.
            using var dialog = new RsSingleFrameViewerDialog(initial, catalogFactory, editFrame);
            dialog.ShowDialog(owner);
            return true;
        }
        catch (Exception ex)
        {
            var report = ApplicationErrorService.Report(ex, errorSource, notify: false);
            var logText = string.IsNullOrWhiteSpace(report.LogPath)
                ? "\r\n\r\n详细日志写入失败。"
                : $"\r\n\r\n详细日志：{report.LogPath}";
            MessageBox.Show(owner, ex.Message + logText, errorTitle, MessageBoxButtons.OK, MessageBoxIcon.Error);
            return false;
        }
        finally
        {
            if (ownerControl != null && !ownerControl.IsDisposed)
                ownerControl.Cursor = previousCursor ?? Cursors.Default;
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) DisposeResources();
        base.Dispose(disposing);
    }

    private Control BuildLayout()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            RowCount = 2,
            ColumnCount = 1,
            Padding = new Padding(8)
        };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var toolbar = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            WrapContents = true,
            Padding = new Padding(0, 0, 0, 6)
        };
        _titleLabel.AutoSize = true;
        _titleLabel.Font = new Font(Font, FontStyle.Bold);
        _titleLabel.Margin = new Padding(2, 7, 14, 0);
        toolbar.Controls.Add(_titleLabel);

        _stageLabel.Name = "RsSingleFrameStageLabel";
        _stageLabel.Text = "转级：";
        _stageLabel.AutoSize = true;
        _stageLabel.Margin = new Padding(0, 7, 2, 0);
        toolbar.Controls.Add(_stageLabel);
        _stageCombo.Name = "RsSingleFrameStageCombo";
        _stageCombo.DropDownStyle = ComboBoxStyle.DropDownList;
        _stageCombo.Width = 96;
        _stageCombo.SelectedIndexChanged += (_, _) => ReloadForSelectedOptions();
        toolbar.Controls.Add(_stageCombo);

        _factionLabel.Name = "RsSingleFrameFactionLabel";
        _factionLabel.Text = "阵营：";
        _factionLabel.AutoSize = true;
        _factionLabel.Margin = new Padding(10, 7, 2, 0);
        toolbar.Controls.Add(_factionLabel);
        _factionCombo.Name = "RsSingleFrameFactionCombo";
        _factionCombo.DropDownStyle = ComboBoxStyle.DropDownList;
        _factionCombo.Width = 88;
        _factionCombo.SelectedIndexChanged += (_, _) => ReloadForSelectedOptions();
        toolbar.Controls.Add(_factionCombo);

        root.Controls.Add(toolbar, 0, 0);

        _mainSplit.Name = "RsSingleFrameMainSplit";
        _mainSplit.Dock = DockStyle.Fill;
        _mainSplit.Orientation = Orientation.Vertical;
        _mainSplit.FixedPanel = FixedPanel.Panel1;
        _mainSplit.SplitterWidth = 6;
        _mainSplit.Panel1MinSize = 0;
        _mainSplit.Panel2MinSize = 0;
        _mainSplit.Panel1.AutoScroll = true;
        _mainSplit.Panel2.AutoScroll = true;
        _mainSplit.SplitterMoved += (_, _) =>
        {
            if (!_adjustingMainSplitter && _mainSplitterInitialized)
                _preferredMainSplitterDistance = _mainSplit.SplitterDistance;
        };
        _mainSplit.SizeChanged += (_, _) =>
        {
            if (_adjustingResponsiveLayout || !_layoutReady) return;
            if (TryUpdateResponsiveOrientation())
            {
                ApplyMainSplitterLayout(useDesiredDistance: true, allowRetry: true);
                ResizeGroupPanels();
                return;
            }
            if (_mainSplitterInitialized)
            {
                ApplyMainSplitterLayout(useDesiredDistance: false, allowRetry: true);
                // WinForms can make another FixedPanel/DPI adjustment after SizeChanged.
                // Re-apply once on the next message-loop turn using the saved user distance.
                ScheduleSplitterLayoutRetry(useDesiredDistance: false);
            }
        };

        var left = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoScroll = true,
            RowCount = 4,
            ColumnCount = 1,
            Padding = new Padding(2)
        };
        left.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        left.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        left.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        left.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        _largePreview.Dock = DockStyle.Fill;
        _largePreview.Name = "RsSingleFrameLargePreview";
        _largePreview.Margin = new Padding(0, 0, 6, 6);
        left.Controls.Add(_largePreview, 0, 0);

        _metadataLabel.Name = "RsSingleFrameMetadataLabel";
        _metadataLabel.AutoSize = true;
        _metadataLabel.MaximumSize = new Size(620, 0);
        _metadataLabel.Padding = new Padding(2, 4, 2, 4);
        left.Controls.Add(_metadataLabel, 0, 1);
        _messageLabel.AutoSize = true;
        _messageLabel.MaximumSize = new Size(620, 0);
        _messageLabel.ForeColor = Color.DarkOrange;
        _messageLabel.Padding = new Padding(2, 0, 2, 5);
        left.Controls.Add(_messageLabel, 0, 2);

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            FlowDirection = FlowDirection.LeftToRight
        };
        _editButton.Text = "编辑当前帧";
        _editButton.Name = "RsSingleFrameEditButton";
        _editButton.AutoSize = true;
        _editButton.MinimumSize = new Size(110, 32);
        _editButton.Click += (_, _) => EditSelectedFrame();
        buttons.Controls.Add(_editButton);
        var close = new Button
        {
            Name = "RsSingleFrameCloseButton",
            Text = "关闭",
            AutoSize = true,
            MinimumSize = new Size(80, 32),
            DialogResult = DialogResult.Cancel
        };
        buttons.Controls.Add(close);
        left.Controls.Add(buttons, 0, 3);
        _mainSplit.Panel1.Controls.Add(left);

        _groupsPanel.Name = "RsSingleFrameGroupsPanel";
        _groupsPanel.Dock = DockStyle.Fill;
        _groupsPanel.AutoScroll = true;
        _groupsPanel.FlowDirection = FlowDirection.TopDown;
        _groupsPanel.WrapContents = false;
        _groupsPanel.Padding = new Padding(4);
        _groupsPanel.BackColor = Color.FromArgb(244, 245, 247);
        _groupsPanel.SizeChanged += (_, _) => ResizeGroupPanels();
        _mainSplit.Panel2.Controls.Add(_groupsPanel);
        root.Controls.Add(_mainSplit, 0, 1);
        CancelButton = close;
        return root;
    }

    private void LoadCatalogView(string? preferredGroup, int preferredFrameIndex)
    {
        _titleLabel.Text = _catalog.Title;
        ConfigureOptions();
        BuildThumbnails();

        var selected = _catalog.Frames.FirstOrDefault(frame =>
                           frame.Group.Equals(preferredGroup, StringComparison.Ordinal) &&
                           frame.PhysicalFrameIndex == preferredFrameIndex)
                       ?? _catalog.Frames.FirstOrDefault(frame => frame.IsReadable)
                       ?? _catalog.Frames.FirstOrDefault();
        SelectFrame(selected);
    }

    private void ConfigureOptions()
    {
        _updatingOptions = true;
        try
        {
            _stageCombo.Items.Clear();
            foreach (var stage in _catalog.AvailableStages)
                _stageCombo.Items.Add(new ValueItem(stage, CharacterImageResourceService.BuildSImageStageText(stage)));
            _stageCombo.SelectedIndex = FindValueIndex(_stageCombo, _catalog.StageSlot);
            var showStages = _catalog.Kind == RsFrameResourceKind.S && _catalog.AvailableStages.Count > 1;
            _stageLabel.Visible = showStages;
            _stageCombo.Visible = showStages;

            _factionCombo.Items.Clear();
            foreach (var faction in _catalog.AvailableFactions)
                _factionCombo.Items.Add(new ValueItem(faction, CharacterImageResourceService.BuildSPreviewFactionText(faction)));
            _factionCombo.SelectedIndex = FindValueIndex(_factionCombo, _catalog.FactionSlot);
            var showFactions = _catalog.Kind == RsFrameResourceKind.S && _catalog.AvailableFactions.Count > 1;
            _factionLabel.Visible = showFactions;
            _factionCombo.Visible = showFactions;
        }
        finally
        {
            _updatingOptions = false;
        }
    }

    private void BuildThumbnails()
    {
        _groupsPanel.SuspendLayout();
        try
        {
            ClearThumbnailControls();
            _thumbnails.Clear();
            foreach (var group in _catalog.Frames.GroupBy(frame => frame.Group))
            {
                var box = new GroupBox
                {
                    Text = group.Key,
                    AutoSize = true,
                    AutoSizeMode = AutoSizeMode.GrowAndShrink,
                    Margin = new Padding(2, 2, 2, 10),
                    Padding = new Padding(8)
                };
                var flow = new FlowLayoutPanel
                {
                    AutoSize = true,
                    AutoSizeMode = AutoSizeMode.GrowAndShrink,
                    WrapContents = true,
                    Padding = new Padding(2),
                    Margin = Padding.Empty
                };
                foreach (var frame in group)
                {
                    var thumbnail = new RsFrameThumbnailControl(frame)
                    {
                        Margin = new Padding(3)
                    };
                    thumbnail.Click += (_, _) => SelectFrame(frame);
                    thumbnail.DoubleClick += (_, _) =>
                    {
                        SelectFrame(frame);
                        EditSelectedFrame();
                    };
                    _thumbnails.Add(thumbnail);
                    flow.Controls.Add(thumbnail);
                }
                box.Controls.Add(flow);
                _groupsPanel.Controls.Add(box);
            }
        }
        finally
        {
            _groupsPanel.ResumeLayout(true);
        }
        ResizeGroupPanels();
    }

    private void ResizeGroupPanels()
    {
        if (_groupsPanel.IsDisposed) return;
        var reservedWidth = SystemInformation.VerticalScrollBarWidth + ScaleLogical(18);
        if (_groupsPanel.ClientSize.Width <= reservedWidth) return;

        var width = Math.Max(ScaleLogical(MinimumGroupWidthLogical), _groupsPanel.ClientSize.Width - reservedWidth);
        foreach (GroupBox group in _groupsPanel.Controls)
        {
            group.MinimumSize = Size.Empty;
            group.MaximumSize = Size.Empty;
            group.MaximumSize = new Size(width, 0);
            group.MinimumSize = new Size(width, 0);
            if (group.Controls.Count > 0)
                group.Controls[0].MaximumSize = new Size(
                    Math.Max(ScaleLogical(MinimumGroupFlowWidthLogical), width - ScaleLogical(20)), 0);
        }
    }

    private bool ApplyMainSplitterLayout(bool useDesiredDistance, bool allowRetry)
    {
        if (_adjustingMainSplitter || _mainSplit.IsDisposed) return false;
        try
        {
            _adjustingMainSplitter = true;
            var totalLength = _mainSplit.Orientation == Orientation.Vertical
                ? _mainSplit.ClientSize.Width
                : _mainSplit.ClientSize.Height;
            var currentDistance = _preferredMainSplitterDistance ?? _mainSplit.SplitterDistance;
            var vertical = _mainSplit.Orientation == Orientation.Vertical;
            var layout = CalculateSplitterLayout(
                totalLength,
                _mainSplit.SplitterWidth,
                ScaleLogical(vertical ? DesiredPanel1WidthLogical : DesiredStackedPanel1HeightLogical),
                ScaleLogical(vertical ? DesiredPanel1MinWidthLogical : DesiredStackedPanel1MinHeightLogical),
                ScaleLogical(vertical ? DesiredPanel2MinWidthLogical : DesiredStackedPanel2MinHeightLogical),
                currentDistance,
                useDesiredDistance);
            if (!layout.HasUsableSpace) return false;

            // Clear both constraints before moving the splitter. WinForms validates every
            // property assignment immediately, including during transient DPI layouts.
            _mainSplit.Panel1MinSize = 0;
            _mainSplit.Panel2MinSize = 0;
            if (_mainSplit.SplitterDistance != layout.SplitterDistance)
                _mainSplit.SplitterDistance = layout.SplitterDistance;
            _mainSplit.Panel1MinSize = layout.Panel1MinSize;
            _mainSplit.Panel2MinSize = layout.Panel2MinSize;
            if (useDesiredDistance && !_preferredMainSplitterDistance.HasValue)
                _preferredMainSplitterDistance = layout.SplitterDistance;
            _mainSplitterInitialized = true;
            return true;
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentOutOfRangeException)
        {
            if (allowRetry) ScheduleSplitterLayoutRetry(useDesiredDistance);
            return false;
        }
        finally
        {
            _adjustingMainSplitter = false;
        }
    }

    private void ScheduleSplitterLayoutRetry(bool useDesiredDistance)
    {
        if (_splitterRetryScheduled || IsDisposed || Disposing || !IsHandleCreated) return;
        _splitterRetryScheduled = true;
        try
        {
            BeginInvoke(new Action(() =>
            {
                _splitterRetryScheduled = false;
                if (IsDisposed || Disposing) return;
                UpdateResponsiveLayout(useDesiredDistance);
            }));
        }
        catch (InvalidOperationException)
        {
            _splitterRetryScheduled = false;
        }
    }

    internal static RsSingleFrameSplitterLayout CalculateSplitterLayout(
        int totalLength,
        int splitterWidth,
        int desiredDistance,
        int desiredPanel1Min,
        int desiredPanel2Min,
        int currentDistance,
        bool useDesiredDistance)
    {
        var usableLength = totalLength - Math.Max(0, splitterWidth);
        if (usableLength <= 0)
            return new RsSingleFrameSplitterLayout(false, 0, 0, 0);

        desiredPanel1Min = Math.Max(0, desiredPanel1Min);
        desiredPanel2Min = Math.Max(0, desiredPanel2Min);
        var canUseRequestedMins = usableLength > desiredPanel1Min + desiredPanel2Min;
        var panel1Min = canUseRequestedMins ? desiredPanel1Min : 0;
        var panel2Min = canUseRequestedMins ? desiredPanel2Min : 0;
        var maximum = usableLength - panel2Min;
        var requested = useDesiredDistance ? desiredDistance : currentDistance;
        var target = Math.Clamp(requested, panel1Min, maximum);
        return new RsSingleFrameSplitterLayout(true, target, panel1Min, panel2Min);
    }

    private int ScaleLogical(int value)
        => Math.Max(1, (int)Math.Round(value * DeviceDpi / 96.0));

    private void UpdateResponsiveLayout(bool useDesiredDistance)
    {
        if (IsDisposed || Disposing) return;
        try
        {
            _adjustingResponsiveLayout = true;
            TryUpdateResponsiveOrientation();
            ApplyMainSplitterLayout(useDesiredDistance, allowRetry: true);
            ResizeGroupPanels();
        }
        finally
        {
            _adjustingResponsiveLayout = false;
        }
    }

    private bool TryUpdateResponsiveOrientation()
    {
        if (!_layoutReady || _mainSplit.IsDisposed || ClientSize.Width <= 0 || ClientSize.Height <= 0) return false;
        var logicalWidth = ClientSize.Width * 96d / Math.Max(96, DeviceDpi);
        var next = logicalWidth >= ResponsiveWidthLogical ? Orientation.Vertical : Orientation.Horizontal;
        if (_mainSplit.Orientation == next) return false;

        _mainSplit.Panel1MinSize = 0;
        _mainSplit.Panel2MinSize = 0;
        var targetLength = next == Orientation.Vertical ? _mainSplit.ClientSize.Width : _mainSplit.ClientSize.Height;
        var safeDistance = Math.Clamp(
            _mainSplit.SplitterDistance,
            0,
            Math.Max(0, targetLength - _mainSplit.SplitterWidth));
        if (_mainSplit.SplitterDistance != safeDistance)
            _mainSplit.SplitterDistance = safeDistance;
        _mainSplit.Orientation = next;
        _mainSplit.FixedPanel = FixedPanel.Panel1;
        _preferredMainSplitterDistance = null;
        _mainSplitterInitialized = false;
        return true;
    }

    private void ScheduleResponsiveLayout()
    {
        if (IsDisposed || Disposing || !IsHandleCreated || _splitterRetryScheduled) return;
        _splitterRetryScheduled = true;
        try
        {
            BeginInvoke(new Action(() =>
            {
                _splitterRetryScheduled = false;
                if (IsDisposed || Disposing) return;
                ConstrainToWorkingArea();
                UpdateResponsiveLayout(useDesiredDistance: true);
            }));
        }
        catch (InvalidOperationException)
        {
            _splitterRetryScheduled = false;
        }
    }

    private void ConstrainToWorkingArea()
    {
        if (!IsHandleCreated) return;
        var working = Screen.FromControl(this).WorkingArea;
        var scale = DeviceDpi / 96d;
        MinimumSize = new Size(
            Math.Min(working.Width, Math.Max(480, (int)Math.Round(640 * scale))),
            Math.Min(working.Height, Math.Max(360, (int)Math.Round(480 * scale))));
        var width = Math.Min(Width, working.Width);
        var height = Math.Min(Height, working.Height);
        var x = Math.Clamp(Left, working.Left, working.Right - width);
        var y = Math.Clamp(Top, working.Top, working.Bottom - height);
        Bounds = new Rectangle(x, y, width, height);
    }

    private void SelectFrame(RsFrameDescriptor? frame)
    {
        _selectedFrame = frame;
        _largePreview.Descriptor = frame;
        foreach (var thumbnail in _thumbnails)
        {
            thumbnail.Selected = ReferenceEquals(thumbnail.Descriptor, frame);
        }

        if (frame == null)
        {
            _metadataLabel.Text = "没有可显示的物理帧。";
            _messageLabel.Text = string.Join("\r\n", _catalog.Messages.Where(message => !string.IsNullOrWhiteSpace(message)));
            _editButton.Enabled = false;
            return;
        }

        var sMetadata = _catalog.Kind switch
        {
            RsFrameResourceKind.S when _catalog.ImageId == 0
                => $"\r\n阵营：{CharacterImageResourceService.BuildSPreviewFactionText(frame.FactionSlot)}",
            RsFrameResourceKind.S
                => $"\r\n转级：{CharacterImageResourceService.BuildSImageStageText(frame.StageSlot)}    阵营：{CharacterImageResourceService.BuildSPreviewFactionText(frame.FactionSlot)}",
            _ => string.Empty
        };
        _metadataLabel.Text =
            $"{_catalog.Title}    {frame.Group} / {frame.DisplayLabel}\r\n" +
            $"{Path.GetFileName(frame.TargetPath)}  图号 #{frame.ImageNumber}    物理帧索引 {frame.PhysicalFrameIndex}    " +
            $"尺寸 {frame.SourceRectangle.Width}x{frame.SourceRectangle.Height}" +
            sMetadata;
        var messages = new List<string>();
        if (_editFrame == null) messages.Add("当前来源以只读方式打开，没有可写项目 E5 目标；编辑当前帧已禁用。");
        if (!string.IsNullOrWhiteSpace(frame.Warning)) messages.Add(frame.Warning);
        messages.AddRange(_catalog.Messages.Where(message => !string.IsNullOrWhiteSpace(message)));
        _messageLabel.Text = string.Join("\r\n", messages.Distinct(StringComparer.Ordinal));
        _editButton.Enabled = _editFrame != null && frame.IsEditable;
        _editButton.Text = _editFrame == null ? "编辑当前帧（只读）" : "编辑当前帧";
    }

    private void ReloadForSelectedOptions()
    {
        if (_updatingOptions || _resourcesDisposed) return;
        var stage = (_stageCombo.SelectedItem as ValueItem)?.Value ?? _catalog.StageSlot;
        var faction = (_factionCombo.SelectedItem as ValueItem)?.Value ?? _catalog.FactionSlot;
        ReloadCatalog(stage, faction, _selectedFrame?.Group, _selectedFrame?.PhysicalFrameIndex ?? 0);
    }

    private void ReloadCatalog(int stage, int faction, string? group, int frameIndex)
    {
        RsFrameCatalog next;
        try
        {
            Cursor = Cursors.WaitCursor;
            next = _catalogFactory(stage, faction);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "读取 R/S 单帧失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }
        finally
        {
            Cursor = Cursors.Default;
        }

        var old = _catalog;
        var oldGroup = _selectedFrame?.Group;
        var oldFrameIndex = _selectedFrame?.PhysicalFrameIndex ?? 0;
        try
        {
            _largePreview.Descriptor = null;
            ClearThumbnailControls();
            _catalog = next;
            LoadCatalogView(group, frameIndex);
        }
        catch (Exception ex)
        {
            _largePreview.Descriptor = null;
            ClearThumbnailControls();
            _catalog = old;
            try
            {
                LoadCatalogView(oldGroup, oldFrameIndex);
            }
            catch
            {
                // Preserve ownership of the old catalog even if restoring its controls fails.
            }
            next.Dispose();
            var report = ApplicationErrorService.Report(ex, "Reload R/S single-frame catalog", notify: false);
            var logText = string.IsNullOrWhiteSpace(report.LogPath)
                ? string.Empty
                : $"\r\n\r\n详细日志：{report.LogPath}";
            MessageBox.Show(this, ex.Message + logText, "刷新 R/S 单帧失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }
        old.Dispose();
    }

    private void EditSelectedFrame()
    {
        var frame = _selectedFrame;
        if (frame == null || _editFrame == null || !frame.IsEditable)
        {
            var reason = frame?.Warning;
            if (string.IsNullOrWhiteSpace(reason)) reason = "当前帧不是可写项目 E5 中的完整物理帧。";
            MessageBox.Show(this, reason, "不能编辑当前帧", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var group = frame.Group;
        var frameIndex = frame.PhysicalFrameIndex;
        if (_editFrame(frame))
        {
            ReloadCatalog(_catalog.StageSlot, _catalog.FactionSlot, group, frameIndex);
        }
    }

    private void DisposeResources()
    {
        if (_resourcesDisposed) return;
        _resourcesDisposed = true;
        _largePreview.Descriptor = null;
        ClearThumbnailControls();
        _catalog.Dispose();
    }

    private void ClearThumbnailControls()
    {
        while (_groupsPanel.Controls.Count > 0)
        {
            var control = _groupsPanel.Controls[0];
            _groupsPanel.Controls.RemoveAt(0);
            control.Dispose();
        }
        _thumbnails.Clear();
    }

    private static int FindValueIndex(ComboBox combo, int value)
    {
        for (var i = 0; i < combo.Items.Count; i++)
        {
            if (combo.Items[i] is ValueItem item && item.Value == value) return i;
        }
        return combo.Items.Count > 0 ? 0 : -1;
    }

    private sealed record ValueItem(int Value, string Text)
    {
        public override string ToString() => Text;
    }
}

internal readonly record struct RsSingleFrameSplitterLayout(
    bool HasUsableSpace,
    int SplitterDistance,
    int Panel1MinSize,
    int Panel2MinSize);

internal readonly record struct RsFramePreviewLayout(
    Size LogicalCanvasSize,
    Rectangle CanvasDestination,
    Rectangle ContentDestination,
    double PixelScale);

internal sealed class RsFramePreviewControl : Control
{
    private static readonly Size SLogicalCanvasSize = new(64, 64);
    private RsFrameDescriptor? _descriptor;

    public RsFramePreviewControl()
    {
        DoubleBuffered = true;
        ResizeRedraw = true;
        BackColor = Color.FromArgb(38, 40, 44);
    }

    public RsFrameDescriptor? Descriptor
    {
        get => _descriptor;
        set
        {
            _descriptor = value;
            Invalidate();
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        e.Graphics.Clear(BackColor);
        var content = Rectangle.Inflate(ClientRectangle, -12, -12);
        if (content.Width <= 0 || content.Height <= 0) return;
        var bitmap = _descriptor?.Bitmap;
        if (bitmap == null)
        {
            TextRenderer.DrawText(e.Graphics,
                _descriptor?.Warning ?? "请选择右侧候选物理帧。",
                Font,
                content,
                Color.Gainsboro,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak);
            return;
        }

        DrawFrame(e.Graphics, content, _descriptor!.Kind, bitmap);
    }

    internal static void DrawFrame(Graphics graphics, Rectangle bounds, RsFrameResourceKind kind, Bitmap bitmap)
    {
        var layout = CalculatePreviewLayout(bounds, kind, bitmap.Size);
        var checkerUnit = layout.CanvasDestination.Width / Math.Max(1, layout.LogicalCanvasSize.Width);
        DrawChecker(graphics, layout.CanvasDestination, Math.Max(6, Math.Min(16, checkerUnit) * 2));
        graphics.InterpolationMode = InterpolationMode.NearestNeighbor;
        graphics.PixelOffsetMode = PixelOffsetMode.Half;
        graphics.DrawImage(bitmap, layout.ContentDestination, new Rectangle(Point.Empty, bitmap.Size), GraphicsUnit.Pixel);
    }

    internal static RsFramePreviewLayout CalculatePreviewLayout(Rectangle bounds, RsFrameResourceKind kind, Size source)
    {
        if (source.Width <= 0 || source.Height <= 0)
            return new RsFramePreviewLayout(source, bounds, bounds, 1d);

        if (kind != RsFrameResourceKind.S)
        {
            var destination = CalculateIntegerScaleDestination(bounds, source);
            return new RsFramePreviewLayout(
                source,
                destination,
                destination,
                destination.Width / (double)source.Width);
        }

        var canvasDestination = CalculateIntegerScaleDestination(bounds, SLogicalCanvasSize);
        var logicalContent = new Rectangle(
            (SLogicalCanvasSize.Width - source.Width) / 2,
            (SLogicalCanvasSize.Height - source.Height) / 2,
            source.Width,
            source.Height);
        var contentDestination = MapLogicalRectangle(canvasDestination, SLogicalCanvasSize, logicalContent);
        return new RsFramePreviewLayout(
            SLogicalCanvasSize,
            canvasDestination,
            contentDestination,
            canvasDestination.Width / (double)SLogicalCanvasSize.Width);
    }

    private static Rectangle MapLogicalRectangle(Rectangle destination, Size logicalSize, Rectangle logicalRectangle)
    {
        var left = destination.Left + (int)Math.Round(logicalRectangle.Left * destination.Width / (double)logicalSize.Width);
        var top = destination.Top + (int)Math.Round(logicalRectangle.Top * destination.Height / (double)logicalSize.Height);
        var right = destination.Left + (int)Math.Round(logicalRectangle.Right * destination.Width / (double)logicalSize.Width);
        var bottom = destination.Top + (int)Math.Round(logicalRectangle.Bottom * destination.Height / (double)logicalSize.Height);
        return Rectangle.FromLTRB(left, top, right, bottom);
    }

    internal static Rectangle CalculateIntegerScaleDestination(Rectangle bounds, Size source)
    {
        if (source.Width <= 0 || source.Height <= 0) return bounds;
        var integerScale = Math.Min(bounds.Width / source.Width, bounds.Height / source.Height);
        double scale = integerScale >= 1
            ? integerScale
            : Math.Min(bounds.Width / (double)source.Width, bounds.Height / (double)source.Height);
        var width = Math.Max(1, (int)Math.Round(source.Width * scale));
        var height = Math.Max(1, (int)Math.Round(source.Height * scale));
        return new Rectangle(bounds.Left + (bounds.Width - width) / 2, bounds.Top + (bounds.Height - height) / 2, width, height);
    }

    internal static void DrawChecker(Graphics graphics, Rectangle rectangle, int size)
    {
        size = Math.Max(2, size);
        using var light = new SolidBrush(Color.FromArgb(225, 225, 225));
        using var dark = new SolidBrush(Color.FromArgb(180, 180, 180));
        for (var y = rectangle.Top; y < rectangle.Bottom; y += size)
        for (var x = rectangle.Left; x < rectangle.Right; x += size)
        {
            var even = (((x - rectangle.Left) / size) + ((y - rectangle.Top) / size)) % 2 == 0;
            graphics.FillRectangle(even ? light : dark, x, y,
                Math.Min(size, rectangle.Right - x), Math.Min(size, rectangle.Bottom - y));
        }
    }
}

internal sealed class RsFrameThumbnailControl : Control
{
    private bool _selected;

    public RsFrameThumbnailControl(RsFrameDescriptor descriptor)
    {
        Descriptor = descriptor;
        Size = new Size(118, 126);
        DoubleBuffered = true;
        Cursor = Cursors.Hand;
        SetStyle(ControlStyles.Selectable | ControlStyles.StandardClick | ControlStyles.StandardDoubleClick, true);
    }

    public RsFrameDescriptor Descriptor { get; }
    public bool Selected
    {
        get => _selected;
        set
        {
            if (_selected == value) return;
            _selected = value;
            Invalidate();
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        e.Graphics.Clear(Color.White);
        var imageArea = new Rectangle(7, 7, Width - 14, 86);
        if (Descriptor.Bitmap != null)
        {
            var destination = RsFramePreviewControl.CalculateIntegerScaleDestination(imageArea, Descriptor.Bitmap.Size);
            RsFramePreviewControl.DrawChecker(e.Graphics, destination, 5);
            e.Graphics.InterpolationMode = InterpolationMode.NearestNeighbor;
            e.Graphics.PixelOffsetMode = PixelOffsetMode.Half;
            e.Graphics.DrawImage(Descriptor.Bitmap, destination, new Rectangle(Point.Empty, Descriptor.Bitmap.Size), GraphicsUnit.Pixel);
        }
        else
        {
            using var placeholder = new SolidBrush(Color.FromArgb(236, 238, 241));
            e.Graphics.FillRectangle(placeholder, imageArea);
            TextRenderer.DrawText(e.Graphics, "缺失", Font, imageArea, Color.Firebrick,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }

        var labelRect = new Rectangle(4, 96, Width - 8, Height - 99);
        TextRenderer.DrawText(e.Graphics, Descriptor.DisplayLabel, Font, labelRect, ForeColor,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.Top | TextFormatFlags.EndEllipsis | TextFormatFlags.WordBreak);
        using var border = new Pen(Selected ? Color.DodgerBlue : Color.FromArgb(155, 160, 166), Selected ? 3f : 1f)
        {
            Alignment = PenAlignment.Inset
        };
        e.Graphics.DrawRectangle(border, 0, 0, Width - 1, Height - 1);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        Focus();
        base.OnMouseDown(e);
    }
}
