using CCZModStudio.Models;

namespace CCZModStudio;

public sealed class MapPublishDialog : Form
{
    private readonly MapWorkbenchDraft _draft;
    private readonly int _nextMapNumber;
    private readonly int _directoryEntryCount;
    private readonly RadioButton _overwriteRadio = new();
    private readonly RadioButton _appendRadio = new();
    private readonly ComboBox _targetCombo = new();
    private readonly Label _appendTargetLabel = new();
    private readonly CheckBox _allowResizeCheckBox = new();
    private readonly CheckBox _confirmCropCheckBox = new();
    private readonly TextBox _summaryBox = new();
    private readonly TextBox _validationBox = new();
    private readonly Button _publishButton = new();
    private readonly List<MapSlotCatalogEntry> _validOverwriteSlots;
    private bool _initializing = true;

    public MapSlotPublishMode SelectedMode
        => _appendRadio.Checked ? MapSlotPublishMode.AppendNew : MapSlotPublishMode.OverwriteExisting;

    public string SelectedTargetMapId
        => (_targetCombo.SelectedItem as MapSlotCatalogEntry)?.MapId ?? string.Empty;

    public bool AllowResizeExisting => _allowResizeCheckBox.Checked;
    public bool ConfirmDestructiveCrop => _confirmCropCheckBox.Checked;

    public MapPublishDialog(
        MapWorkbenchDraft draft,
        IReadOnlyList<MapSlotCatalogEntry> slots,
        string preferredMapId,
        int nextMapNumber,
        int directoryEntryCount,
        MapSlotPublishMode defaultMode = MapSlotPublishMode.OverwriteExisting)
    {
        _draft = draft;
        _nextMapNumber = nextMapNumber;
        _directoryEntryCount = directoryEntryCount;

        Text = "发布地图";
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.Sizable;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        AutoScroll = true;
        ClientSize = new Size(760, 620);
        MinimumSize = new Size(760, 620);
        AutoScaleMode = AutoScaleMode.Dpi;

        var layout = new TableLayoutPanel
        {
            Name = "mapPublishLayout",
            Dock = DockStyle.Fill,
            Padding = new Padding(16),
            ColumnCount = 1,
            RowCount = 8
        };
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        Controls.Add(layout);

        var modePanel = new FlowLayoutPanel
        {
            Name = "mapPublishModePanel",
            AutoSize = true,
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Margin = new Padding(0, 0, 0, 10)
        };
        _overwriteRadio.Text = "覆盖已有地图";
        _overwriteRadio.Name = "mapPublishOverwriteRadio";
        _overwriteRadio.AutoSize = true;
        _appendRadio.Text = "末尾追加地图";
        _appendRadio.Name = "mapPublishAppendRadio";
        _appendRadio.AutoSize = true;
        _appendRadio.Margin = new Padding(24, 3, 3, 3);
        modePanel.Controls.Add(_overwriteRadio);
        modePanel.Controls.Add(_appendRadio);
        layout.Controls.Add(modePanel, 0, 0);

        var targetPanel = new TableLayoutPanel
        {
            Name = "mapPublishTargetPanel",
            AutoSize = true,
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            Margin = new Padding(0, 0, 0, 8)
        };
        targetPanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        targetPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        var targetLabel = new Label { Text = "发布目标：", AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 7, 8, 0) };
        _targetCombo.DropDownStyle = ComboBoxStyle.DropDownList;
        _targetCombo.Name = "mapPublishTargetCombo";
        _targetCombo.Dock = DockStyle.Fill;
        _targetCombo.DisplayMember = nameof(MapSlotCatalogEntry.DisplayName);
        var validSlots = slots.Where(slot => slot.CanOverwrite).OrderBy(slot => slot.MapNumber).ToList();
        _validOverwriteSlots = validSlots;
        _targetCombo.Items.Clear();
        foreach (var slot in _validOverwriteSlots)
        {
            _targetCombo.Items.Add(slot);
        }
        targetPanel.Controls.Add(targetLabel, 0, 0);
        targetPanel.Controls.Add(_targetCombo, 1, 0);
        layout.Controls.Add(targetPanel, 0, 1);

        _appendTargetLabel.Name = "mapPublishAppendTargetLabel";
        _appendTargetLabel.AutoSize = true;
        _appendTargetLabel.Dock = DockStyle.Fill;
        _appendTargetLabel.Padding = new Padding(0, 4, 0, 8);
        _appendTargetLabel.Text = nextMapNumber <= 99
            ? $"追加编号由 Hexzmap 目录末尾自动分配：M{nextMapNumber:000}"
            : "Hexzmap 目录已超过首版 M099 上限。";
        layout.Controls.Add(_appendTargetLabel, 0, 2);

        _allowResizeCheckBox.Text = "同步调整目标地图尺寸";
        _allowResizeCheckBox.Name = "mapPublishAllowResizeCheckBox";
        _allowResizeCheckBox.AutoSize = true;
        _allowResizeCheckBox.Visible = false;
        layout.Controls.Add(_allowResizeCheckBox, 0, 3);

        _confirmCropCheckBox.Text = "我已确认裁掉右侧/底部超出区域";
        _confirmCropCheckBox.Name = "mapPublishConfirmCropCheckBox";
        _confirmCropCheckBox.AutoSize = true;
        _confirmCropCheckBox.Visible = false;
        layout.Controls.Add(_confirmCropCheckBox, 0, 4);

        _summaryBox.Name = "mapPublishSummaryBox";
        _summaryBox.ReadOnly = true;
        _summaryBox.Multiline = true;
        _summaryBox.ScrollBars = ScrollBars.Vertical;
        _summaryBox.Dock = DockStyle.Fill;
        _summaryBox.MinimumSize = new Size(0, 220);
        _summaryBox.BackColor = SystemColors.Window;
        layout.Controls.Add(_summaryBox, 0, 5);

        var abnormalSlots = slots.Where(slot => slot.State != MapSlotState.Complete).ToList();
        _validationBox.Name = "mapPublishValidationBox";
        _validationBox.ReadOnly = true;
        _validationBox.Multiline = true;
        _validationBox.WordWrap = true;
        _validationBox.ScrollBars = ScrollBars.Vertical;
        _validationBox.BorderStyle = BorderStyle.FixedSingle;
        _validationBox.Dock = DockStyle.Fill;
        _validationBox.MinimumSize = new Size(0, 100);
        _validationBox.Height = 110;
        _validationBox.BackColor = SystemColors.Window;
        _validationBox.ForeColor = Color.Firebrick;
        _validationBox.Margin = new Padding(0, 8, 0, 4);
        _validationBox.TabStop = false;
        _validationBox.Visible = abnormalSlots.Count > 0;
        _validationBox.Text = BuildValidationText(abnormalSlots);
        layout.Controls.Add(_validationBox, 0, 6);

        var buttons = new FlowLayoutPanel
        {
            Name = "mapPublishButtonPanel",
            AutoSize = true,
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            Margin = new Padding(0, 8, 0, 0)
        };
        _publishButton.Text = "发布";
        _publishButton.Name = "mapPublishButton";
        _publishButton.AutoSize = true;
        _publishButton.MinimumSize = new Size(90, 32);
        _publishButton.DialogResult = DialogResult.OK;
        var cancelButton = new Button
        {
            Name = "mapPublishCancelButton",
            Text = "取消",
            AutoSize = true,
            MinimumSize = new Size(90, 32),
            DialogResult = DialogResult.Cancel
        };
        buttons.Controls.Add(_publishButton);
        buttons.Controls.Add(cancelButton);
        layout.Controls.Add(buttons, 0, 7);
        AcceptButton = _publishButton;
        CancelButton = cancelButton;

        var preferred = validSlots.FirstOrDefault(slot => slot.MapId.Equals(preferredMapId, StringComparison.OrdinalIgnoreCase));
        var preferredIndex = preferred == null ? -1 : _targetCombo.Items.IndexOf(preferred);
        _overwriteRadio.Enabled = _targetCombo.Items.Count > 0;
        if (_targetCombo.Items.Count == 0 || defaultMode == MapSlotPublishMode.AppendNew)
        {
            TrySelectComboIndex(_targetCombo, -1);
            _appendRadio.Checked = true;
        }
        else
        {
            TrySelectComboIndex(_targetCombo, preferredIndex >= 0 ? preferredIndex : 0);
            _overwriteRadio.Checked = true;
        }

        _overwriteRadio.CheckedChanged += (_, _) => UpdatePreview();
        _appendRadio.CheckedChanged += (_, _) => UpdatePreview();
        _targetCombo.SelectedIndexChanged += (_, _) => UpdatePreview();
        _allowResizeCheckBox.CheckedChanged += (_, _) => UpdatePreview();
        _confirmCropCheckBox.CheckedChanged += (_, _) => UpdatePreview();
        _initializing = false;
        UpdatePreview();
    }

    private void UpdatePreview()
    {
        if (_initializing) return;
        var append = _appendRadio.Checked;
        _targetCombo.Enabled = !append;
        _appendTargetLabel.Visible = append;
        var target = _targetCombo.SelectedItem as MapSlotCatalogEntry;
        if (!append && target == null)
        {
            _allowResizeCheckBox.Visible = false;
            _confirmCropCheckBox.Visible = false;
            _summaryBox.Text =
                "目标：未选择\r\n" +
                "模式：覆盖已有地图\r\n" +
                $"网格：未选择 -> {_draft.GridWidth}x{_draft.GridHeight}\r\n" +
                $"JPG：{_draft.PixelWidth}x{_draft.PixelHeight} 像素\r\n" +
                $"目录项：{_directoryEntryCount} -> {_directoryEntryCount}\r\n" +
                "请选择一个完整的地图槽，或切换到末尾追加地图。";
            _publishButton.Enabled = false;
            RefreshDynamicLayout();
            return;
        }

        var oldWidth = append ? 0 : target?.GridWidth ?? 0;
        var oldHeight = append ? 0 : target?.GridHeight ?? 0;
        var changesSize = !append && target != null && (oldWidth != _draft.GridWidth || oldHeight != _draft.GridHeight);
        var crops = changesSize && (_draft.GridWidth < oldWidth || _draft.GridHeight < oldHeight);
        _allowResizeCheckBox.Visible = changesSize;
        _confirmCropCheckBox.Visible = crops;

        var overlap = append ? 0 : Math.Min(oldWidth, _draft.GridWidth) * Math.Min(oldHeight, _draft.GridHeight);
        var added = append ? _draft.CellCount : Math.Max(0, _draft.CellCount - overlap);
        var removed = append ? 0 : Math.Max(0, oldWidth * oldHeight - overlap);
        var targetId = append ? $"M{_nextMapNumber:000}" : target?.MapId ?? "未选择";
        var oldSegment = append ? 0 : 2 + oldWidth * oldHeight;
        var newSegment = 2 + _draft.CellCount;
        _summaryBox.Text =
            $"目标：{targetId}\r\n" +
            $"模式：{(append ? "末尾追加，不覆盖任何已有编号" : "覆盖 Map/" + targetId + ".JPG 和对应地形块")}\r\n" +
            $"网格：{(append ? "新建" : oldWidth + "x" + oldHeight)} -> {_draft.GridWidth}x{_draft.GridHeight}\r\n" +
            $"JPG：{_draft.PixelWidth}x{_draft.PixelHeight} 像素\r\n" +
            $"Hexzmap 段长：{oldSegment} -> {newSegment} 字节\r\n" +
            $"新增格：{added}；裁剪格：{removed}\r\n" +
            $"目录项：{_directoryEntryCount} -> {(append ? _directoryEntryCount + 1 : _directoryEntryCount)}\r\n" +
            (crops ? "风险：缩小地图可能使明确引用该地图的剧本坐标越界；发布不会修改任何 S/R 文件。" : "发布不会创建或修改任何 S/R 文件。");

        _publishButton.Enabled = append
            ? _nextMapNumber <= 99
            : target != null && (!changesSize || _allowResizeCheckBox.Checked) && (!crops || _confirmCropCheckBox.Checked);
        RefreshDynamicLayout();
    }

    private void RefreshDynamicLayout()
    {
        _appendTargetLabel.Parent?.PerformLayout();
        _allowResizeCheckBox.Parent?.PerformLayout();
        _confirmCropCheckBox.Parent?.PerformLayout();
        _summaryBox.Parent?.PerformLayout();
        PerformLayout();
    }

    private static string BuildValidationText(IReadOnlyList<MapSlotCatalogEntry> abnormalSlots)
    {
        if (abnormalSlots.Count == 0) return string.Empty;
        return $"已排除 {abnormalSlots.Count} 个异常槽：\r\n" +
               string.Join("\r\n", abnormalSlots.Select(slot => $"{slot.MapId}：{slot.Detail}"));
    }

    private static bool TrySelectComboIndex(ComboBox combo, int index)
    {
        if (index < 0 || index >= combo.Items.Count)
        {
            combo.SelectedIndex = -1;
            return false;
        }

        combo.SelectedIndex = index;
        return true;
    }
}
