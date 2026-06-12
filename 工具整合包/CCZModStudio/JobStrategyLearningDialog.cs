using System.Data;
using System.Globalization;

namespace CCZModStudio;

internal sealed class JobStrategyLearningDialog : Form
{
    private readonly DataTable _data = new("兵种策略学习等级");
    private readonly DataGridView _grid = new();
    private readonly Label _statusLabel = new();

    public JobStrategyLearningDialog(
        int strategyId,
        string strategyName,
        DataRow boundStrategyRow,
        IReadOnlyDictionary<int, string> jobNames,
        IReadOnlyDictionary<int, int> learningLevels)
    {
        StrategyId = strategyId;
        BoundStrategyRow = boundStrategyRow;
        ApplyChangesOnClose = true;
        Text = $"策略学习等级：ID={strategyId} {strategyName}";
        StartPosition = FormStartPosition.CenterParent;
        Size = new Size(720, 760);
        MinimumSize = new Size(560, 480);
        Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Regular, GraphicsUnit.Point);
        KeyPreview = true;

        BuildData(jobNames, learningLevels);
        BuildLayout();
        WireEvents();
        UpdateStatus();
    }

    public int StrategyId { get; }

    public DataRow BoundStrategyRow { get; }

    public bool ApplyChangesOnClose { get; set; }

    public IReadOnlyDictionary<int, int> LearningLevels
        => _data.Rows
            .Cast<DataRow>()
            .ToDictionary(
                row => Convert.ToInt32(row["兵种ID"], CultureInfo.InvariantCulture),
                row => Convert.ToInt32(row["学会等级"], CultureInfo.InvariantCulture));

    public bool CommitPendingChanges()
    {
        if (_grid.IsCurrentCellInEditMode && !_grid.EndEdit()) return false;
        var bindingContext = BindingContext;
        bindingContext?[_data].EndCurrentEdit();
        return ValidateAllRows(showMessage: true);
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (ApplyChangesOnClose && !CommitPendingChanges())
        {
            e.Cancel = true;
            return;
        }

        DialogResult = ApplyChangesOnClose ? DialogResult.OK : DialogResult.Cancel;
        base.OnFormClosing(e);
    }

    private void BuildData(IReadOnlyDictionary<int, string> jobNames, IReadOnlyDictionary<int, int> learningLevels)
    {
        _data.Columns.Add("兵种ID", typeof(int));
        _data.Columns.Add("兵种名称", typeof(string));
        _data.Columns.Add("学会等级", typeof(int));

        foreach (var jobId in learningLevels.Keys.OrderBy(static id => id))
        {
            var row = _data.NewRow();
            row["兵种ID"] = jobId;
            row["兵种名称"] = jobNames.TryGetValue(jobId, out var name) && !string.IsNullOrWhiteSpace(name)
                ? name
                : "未找到兵种名";
            row["学会等级"] = learningLevels[jobId];
            _data.Rows.Add(row);
        }

        _data.Columns["兵种ID"]!.ReadOnly = true;
        _data.Columns["兵种名称"]!.ReadOnly = true;
        _data.AcceptChanges();
    }

    private void BuildLayout()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Padding = new Padding(8)
        };
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        Controls.Add(root);

        _grid.Dock = DockStyle.Fill;
        _grid.AllowUserToAddRows = false;
        _grid.AllowUserToDeleteRows = false;
        _grid.AutoGenerateColumns = true;
        _grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        _grid.DataSource = _data;
        _grid.SelectionMode = DataGridViewSelectionMode.CellSelect;
        _grid.MultiSelect = false;
        root.Controls.Add(_grid, 0, 0);

        _statusLabel.Dock = DockStyle.Fill;
        _statusLabel.AutoSize = true;
        _statusLabel.Padding = new Padding(2, 6, 0, 0);
        root.Controls.Add(_statusLabel, 0, 1);

        if (_grid.Columns["兵种ID"] is { } idColumn)
        {
            idColumn.FillWeight = 24;
            idColumn.ReadOnly = true;
            idColumn.DefaultCellStyle.BackColor = Color.FromArgb(245, 245, 245);
        }

        if (_grid.Columns["兵种名称"] is { } nameColumn)
        {
            nameColumn.FillWeight = 56;
            nameColumn.ReadOnly = true;
            nameColumn.DefaultCellStyle.BackColor = Color.FromArgb(245, 245, 245);
        }

        if (_grid.Columns["学会等级"] is { } levelColumn)
        {
            levelColumn.FillWeight = 30;
            levelColumn.DefaultCellStyle.BackColor = Color.FromArgb(244, 250, 255);
        }
    }

    private void WireEvents()
    {
        _grid.CellValidating += (_, e) => ValidateLevelCell(e);
        _grid.DataError += (_, e) =>
        {
            e.ThrowException = false;
            _statusLabel.Text = $"学会等级无效：{e.Exception?.Message}";
        };
        _grid.CellEndEdit += (_, e) =>
        {
            if (e.RowIndex >= 0 && e.ColumnIndex >= 0)
            {
                _grid.Rows[e.RowIndex].Cells[e.ColumnIndex].ErrorText = string.Empty;
            }

            UpdateStatus();
        };
        KeyDown += (_, e) =>
        {
            if (e.KeyCode != Keys.Escape) return;
            Close();
            e.SuppressKeyPress = true;
        };
    }

    private void ValidateLevelCell(DataGridViewCellValidatingEventArgs e)
    {
        if (e.RowIndex < 0 || e.ColumnIndex < 0) return;
        var column = _grid.Columns[e.ColumnIndex];
        if (column.DataPropertyName != "学会等级") return;

        var value = Convert.ToString(e.FormattedValue, CultureInfo.InvariantCulture) ?? string.Empty;
        var error = ValidateLevelValue(value);
        _grid.Rows[e.RowIndex].Cells[e.ColumnIndex].ErrorText = error ?? string.Empty;
        if (error == null) return;

        e.Cancel = true;
        _statusLabel.Text = error;
    }

    private static string? ValidateLevelValue(string value)
    {
        if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var level))
        {
            return "学会等级必须是 0..255 的整数。";
        }

        return level is < 0 or > byte.MaxValue ? "学会等级必须在 0..255 之间。" : null;
    }

    private bool ValidateAllRows(bool showMessage)
    {
        foreach (DataGridViewRow row in _grid.Rows)
        {
            var cell = row.Cells["学会等级"];
            var value = Convert.ToString(cell.Value, CultureInfo.InvariantCulture) ?? string.Empty;
            var error = ValidateLevelValue(value);
            cell.ErrorText = error ?? string.Empty;
            if (error == null) continue;

            _grid.CurrentCell = cell;
            _statusLabel.Text = error;
            if (showMessage) MessageBox.Show(this, error, "学习等级无效", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return false;
        }

        return true;
    }

    private void UpdateStatus()
    {
        var learned = _data.Rows.Cast<DataRow>().Count(row => Convert.ToInt32(row["学会等级"], CultureInfo.InvariantCulture) > 0);
        _statusLabel.Text = $"关闭窗口后同步到主表可学摘要。当前可学习兵种：{learned}/80。";
    }
}
