using System.Data;
using System.Globalization;
using CCZModStudio.Core;

namespace CCZModStudio;

internal sealed class JobEquipmentAttributeDialog : Form
{
    private readonly IReadOnlyList<JobEquipmentPermissionSlotDefinition> _slots;
    private readonly Dictionary<string, CheckBox> _checks = new(StringComparer.Ordinal);
    private readonly Label _statusLabel = new();
    private readonly ToolTip _toolTip = new();

    public JobEquipmentAttributeDialog(
        int jobId,
        string jobName,
        DataRow boundJobRow,
        IReadOnlyList<JobEquipmentPermissionSlotDefinition> slots)
    {
        JobId = jobId;
        BoundJobRow = boundJobRow;
        _slots = slots;

        Text = $"可装备类别：ID={jobId:D2} {jobName}";
        StartPosition = FormStartPosition.CenterParent;
        Size = new Size(620, 640);
        MinimumSize = new Size(520, 460);
        Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Regular, GraphicsUnit.Point);
        KeyPreview = true;

        BuildLayout(jobName);
        LoadCurrentValues();
        UpdateStatus();
    }

    public int JobId { get; }

    public DataRow BoundJobRow { get; }

    public IReadOnlyDictionary<string, int> EquipmentValues
        => _slots.ToDictionary(
            slot => slot.StorageColumnName,
            slot => _checks.TryGetValue(slot.StorageColumnName, out var check) && check.Checked ? 1 : 0,
            StringComparer.Ordinal);

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        DialogResult = DialogResult.OK;
        base.OnFormClosing(e);
    }

    private void BuildLayout(string jobName)
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Padding = new Padding(10)
        };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        Controls.Add(root);

        var title = new Label
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            Font = new Font(Font, FontStyle.Bold),
            Text = $"详细兵种 {JobId:D2} {jobName}"
        };
        root.Controls.Add(title, 0, 0);

        var scroll = new Panel
        {
            Dock = DockStyle.Fill,
            AutoScroll = true,
            BorderStyle = BorderStyle.FixedSingle,
            Padding = new Padding(8)
        };
        root.Controls.Add(scroll, 0, 1);

        var grid = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            ColumnCount = 2,
            RowCount = 0
        };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        scroll.Controls.Add(grid);

        for (var i = 0; i < _slots.Count; i++)
        {
            if (i % 2 == 0)
            {
                grid.RowCount++;
                grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            }

            var slot = _slots[i];
            var check = new CheckBox
            {
                AutoSize = true,
                Padding = new Padding(4, 6, 12, 6),
                Text = slot.CheckBoxText,
                Tag = slot.StorageColumnName
            };
            _toolTip.SetToolTip(check, slot.PreviewText + $"；原始列={slot.StorageColumnName}");
            check.CheckedChanged += (_, _) => UpdateStatus();
            _checks[slot.StorageColumnName] = check;
            grid.Controls.Add(check, i % 2, i / 2);
        }

        _statusLabel.Dock = DockStyle.Fill;
        _statusLabel.AutoSize = true;
        _statusLabel.Padding = new Padding(2, 8, 0, 0);
        root.Controls.Add(_statusLabel, 0, 2);

        KeyDown += (_, e) =>
        {
            if (e.KeyCode != Keys.Escape) return;
            Close();
            e.SuppressKeyPress = true;
        };
    }

    private void LoadCurrentValues()
    {
        foreach (var slot in _slots)
        {
            var column = slot.StorageColumnName;
            if (!_checks.TryGetValue(column, out var check) || !BoundJobRow.Table.Columns.Contains(column)) continue;
            var value = Convert.ToInt32(BoundJobRow[column], CultureInfo.InvariantCulture);
            check.Checked = value != 0;
        }
    }

    private void UpdateStatus()
    {
        var enabled = _checks.Values.Count(check => check.Checked);
        _statusLabel.Text = $"关闭窗口后同步到详细兵种表。当前已勾选：{enabled}/{_slots.Count}。底层仍写入 6.5-4-2 每行后 26 个原始许可字节。";
    }
}
