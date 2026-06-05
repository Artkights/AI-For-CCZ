using System.ComponentModel;
using System.Drawing;
using System.Windows.Forms;

namespace CCZModStudio;

internal sealed class LegacyScriptParameterEditDialog : Form
{
    private readonly BindingList<LegacyScriptParameterEditRow> _rows;
    private readonly DataGridView _grid = new();
    private readonly TextBox _valueBox = new();
    private readonly TextBox _detailBox = new();
    private bool _bindingValueBox;

    public LegacyScriptParameterEditDialog(
        string commandTitle,
        string commandLocation,
        IReadOnlyList<LegacyScriptParameterEditRow> rows,
        int? preferredParameterIndex)
    {
        _rows = new BindingList<LegacyScriptParameterEditRow>(rows.ToList());

        Text = "修改命令参数";
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = false;
        MaximizeBox = true;
        ShowIcon = false;

        BuildLayout(commandTitle, commandLocation);
        _grid.DataSource = _rows;

        Shown += (_, _) =>
        {
            SelectInitialRow(preferredParameterIndex);
            ShowCurrentRowDetail();
        };
    }

    public IReadOnlyList<LegacyScriptParameterEditResult> ChangedParameters
        => _rows
            .Where(row => row.CanEdit && !string.Equals(row.EditableValue, row.CurrentValue, StringComparison.Ordinal))
            .Select(row => new LegacyScriptParameterEditResult(row.Index, row.EditableValue))
            .ToList();

    private void BuildLayout(string commandTitle, string commandLocation)
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            RowCount = 4,
            ColumnCount = 1,
            Padding = new Padding(8)
        };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 58));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 42));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        Controls.Add(root);

        var header = new Label
        {
            AutoSize = true,
            Dock = DockStyle.Fill,
            Padding = new Padding(0, 0, 0, 8),
            Text = $"{commandTitle}\r\n{commandLocation}"
        };
        root.Controls.Add(header, 0, 0);

        ConfigureGrid();
        root.Controls.Add(_grid, 0, 1);

        var lowerSplit = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Vertical,
            SplitterWidth = 6
        };
        lowerSplit.Panel1.Controls.Add(CreateTitledPanel("选中槽位值", _valueBox));
        lowerSplit.Panel2.Controls.Add(CreateTitledPanel("参数说明", _detailBox));
        lowerSplit.SizeChanged += (_, _) => ApplyLowerSplitterDistance(lowerSplit);
        root.Controls.Add(lowerSplit, 0, 2);

        _valueBox.Dock = DockStyle.Fill;
        _valueBox.Multiline = true;
        _valueBox.AcceptsReturn = true;
        _valueBox.ScrollBars = ScrollBars.Vertical;
        _valueBox.WordWrap = true;
        _valueBox.BorderStyle = BorderStyle.FixedSingle;
        _valueBox.TextChanged += (_, _) =>
        {
            if (_bindingValueBox) return;
            if (GetCurrentRow() is not { CanEdit: true } row) return;
            row.EditableValue = _valueBox.Text;
            var currentIndex = _grid.CurrentRow?.Index ?? -1;
            if (currentIndex >= 0)
            {
                _grid.InvalidateRow(currentIndex);
            }
        };

        _detailBox.Dock = DockStyle.Fill;
        _detailBox.Multiline = true;
        _detailBox.ReadOnly = true;
        _detailBox.ScrollBars = ScrollBars.Vertical;
        _detailBox.WordWrap = true;
        _detailBox.BorderStyle = BorderStyle.FixedSingle;
        _detailBox.BackColor = Color.FromArgb(250, 250, 250);

        var okButton = new Button
        {
            Text = "确定",
            AutoSize = true,
            DialogResult = DialogResult.OK
        };
        okButton.Click += (_, _) => CommitCurrentRowValue();

        var cancelButton = new Button
        {
            Text = "取消",
            AutoSize = true,
            DialogResult = DialogResult.Cancel
        };

        var buttonPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            FlowDirection = FlowDirection.RightToLeft,
            Padding = new Padding(0, 8, 0, 0)
        };
        buttonPanel.Controls.Add(cancelButton);
        buttonPanel.Controls.Add(okButton);
        root.Controls.Add(buttonPanel, 0, 3);

        AcceptButton = okButton;
        CancelButton = cancelButton;
    }

    private static void ApplyLowerSplitterDistance(SplitContainer split)
    {
        var availableWidth = split.ClientSize.Width - split.SplitterWidth;
        if (availableWidth <= 0)
        {
            return;
        }

        var minimumPanelWidth = Math.Min(260, Math.Max(0, availableWidth / 3));
        var maxDistance = split.ClientSize.Width - split.SplitterWidth - minimumPanelWidth;
        if (maxDistance < minimumPanelWidth)
        {
            return;
        }

        var target = Math.Clamp(420, minimumPanelWidth, maxDistance);
        if (split.SplitterDistance != target)
        {
            try
            {
                split.SplitterDistance = target;
            }
            catch (InvalidOperationException)
            {
                // SplitContainer can briefly report inconsistent bounds during DPI/layout changes.
            }
        }
    }

    private void ConfigureGrid()
    {
        _grid.Dock = DockStyle.Fill;
        _grid.AllowUserToAddRows = false;
        _grid.AllowUserToDeleteRows = false;
        _grid.AllowUserToResizeRows = false;
        _grid.AutoGenerateColumns = false;
        _grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None;
        _grid.BorderStyle = BorderStyle.FixedSingle;
        _grid.MultiSelect = false;
        _grid.RowHeadersVisible = false;
        _grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _grid.EditMode = DataGridViewEditMode.EditOnKeystrokeOrF2;

        _grid.Columns.Add(TextColumn(nameof(LegacyScriptParameterEditRow.Index), "槽", 48));
        _grid.Columns.Add(TextColumn(nameof(LegacyScriptParameterEditRow.SlotName), "参数名", 150));
        _grid.Columns.Add(TextColumn(nameof(LegacyScriptParameterEditRow.Kind), "类型", 90));
        _grid.Columns.Add(TextColumn(nameof(LegacyScriptParameterEditRow.RawHex), "位置", 110));
        _grid.Columns.Add(TextColumn(nameof(LegacyScriptParameterEditRow.CurrentValue), "原值", 150));
        _grid.Columns.Add(TextColumn(nameof(LegacyScriptParameterEditRow.EditableValue), "修改值", 170, readOnly: false));
        _grid.Columns.Add(TextColumn(nameof(LegacyScriptParameterEditRow.DecodedValue), "解释", 240));
        _grid.Columns.Add(TextColumn(nameof(LegacyScriptParameterEditRow.Meaning), "含义", 180));
        _grid.Columns.Add(TextColumn(nameof(LegacyScriptParameterEditRow.EditNote), "编辑提示", 260));

        foreach (DataGridViewColumn column in _grid.Columns)
        {
            column.SortMode = DataGridViewColumnSortMode.NotSortable;
        }

        _grid.CellBeginEdit += (_, e) =>
        {
            if (e.RowIndex < 0) return;
            if (_rows[e.RowIndex].CanEdit) return;
            e.Cancel = true;
        };
        _grid.CellFormatting += (_, e) =>
        {
            if (e.RowIndex < 0 || e.RowIndex >= _rows.Count) return;
            if (_rows[e.RowIndex].CanEdit) return;

            var style = e.CellStyle;
            if (style == null) return;
            style.ForeColor = SystemColors.GrayText;
            style.BackColor = Color.FromArgb(245, 245, 245);
        };
        _grid.CellValueChanged += (_, _) => ShowCurrentRowDetail();
        _grid.SelectionChanged += (_, _) => ShowCurrentRowDetail();
    }

    private static DataGridViewTextBoxColumn TextColumn(string propertyName, string headerText, int width, bool readOnly = true)
        => new()
        {
            Name = propertyName,
            DataPropertyName = propertyName,
            HeaderText = headerText,
            Width = width,
            ReadOnly = readOnly,
            DefaultCellStyle = new DataGridViewCellStyle
            {
                WrapMode = DataGridViewTriState.False
            }
        };

    private static Control CreateTitledPanel(string title, Control content)
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
            Padding = new Padding(0, 0, 0, 4),
            Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Bold, GraphicsUnit.Point)
        }, 0, 0);
        panel.Controls.Add(content, 0, 1);
        return panel;
    }

    private void SelectInitialRow(int? preferredParameterIndex)
    {
        if (_grid.Rows.Count == 0) return;

        var rowIndex = 0;
        if (preferredParameterIndex.HasValue)
        {
            for (var i = 0; i < _rows.Count; i++)
            {
                if (_rows[i].Index != preferredParameterIndex.Value) continue;
                rowIndex = i;
                break;
            }
        }

        _grid.ClearSelection();
        _grid.Rows[rowIndex].Selected = true;
        _grid.CurrentCell = _grid.Rows[rowIndex].Cells[nameof(LegacyScriptParameterEditRow.EditableValue)];
    }

    private void ShowCurrentRowDetail()
    {
        if (GetCurrentRow() is not { } row)
        {
            _bindingValueBox = true;
            _valueBox.Clear();
            _valueBox.ReadOnly = true;
            _detailBox.Clear();
            _bindingValueBox = false;
            return;
        }

        _bindingValueBox = true;
        _valueBox.Text = row.EditableValue;
        _valueBox.ReadOnly = !row.CanEdit;
        _bindingValueBox = false;

        _detailBox.Text =
            $"槽位：{row.Index}\r\n" +
            $"参数名：{row.SlotName}\r\n" +
            $"类型：{row.Kind}\r\n" +
            $"位置：{row.RawHex}\r\n" +
            $"原值：{row.CurrentValue}\r\n" +
            $"解释：{row.DecodedValue}\r\n" +
            $"含义：{row.Meaning}\r\n" +
            $"风险/边界：{row.Risk}\r\n" +
            $"编辑提示：{row.EditNote}";
    }

    private LegacyScriptParameterEditRow? GetCurrentRow()
        => _grid.CurrentRow?.DataBoundItem as LegacyScriptParameterEditRow;

    private void CommitCurrentRowValue()
    {
        Validate();
        _grid.EndEdit();
        if (GetCurrentRow() is { CanEdit: true } row)
        {
            row.EditableValue = _valueBox.Text;
        }
    }
}

internal sealed class LegacyScriptParameterEditRow
{
    public int Index { get; init; }
    public string SlotName { get; init; } = string.Empty;
    public string Kind { get; init; } = string.Empty;
    public string RawHex { get; init; } = string.Empty;
    public string CurrentValue { get; init; } = string.Empty;
    public string EditableValue { get; set; } = string.Empty;
    public string DecodedValue { get; init; } = string.Empty;
    public string Meaning { get; init; } = string.Empty;
    public string Risk { get; init; } = string.Empty;
    public bool CanEdit { get; init; }
    public string EditNote { get; init; } = string.Empty;
}

internal sealed record LegacyScriptParameterEditResult(int Index, string Value);
