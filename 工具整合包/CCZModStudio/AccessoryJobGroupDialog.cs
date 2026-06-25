using System.Globalization;
using CCZModStudio.Core;
using CCZModStudio.Models;

namespace CCZModStudio;

internal sealed class AccessoryJobGroupDialog : Form
{
    private readonly Dictionary<int, string> _jobSeriesNames;
    private readonly List<List<int>> _groups;
    private readonly ListBox _groupList = new();
    private readonly CheckedListBox _jobList = new();
    private readonly TextBox _previewBox = new();
    private readonly Label _statusLabel = new();
    private bool _binding;

    public AccessoryJobGroupDialog(AccessoryJobGroupProfile profile, IReadOnlyDictionary<int, string> jobSeriesNames)
    {
        _jobSeriesNames = jobSeriesNames.ToDictionary(pair => pair.Key, pair => pair.Value);
        _groups = profile.Groups.Select(group => group.JobSeriesIds.ToList()).ToList();
        if (_groups.Count == 0) _groups.Add([]);

        Text = "辅助装备多兵种分组";
        StartPosition = FormStartPosition.CenterParent;
        Size = new Size(780, 620);
        MinimumSize = new Size(640, 480);
        Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Regular, GraphicsUnit.Point);
        KeyPreview = true;

        BuildLayout(profile);
        RefreshGroupList(selectIndex: 0);
        UpdatePreview();
    }

    public IReadOnlyList<IReadOnlyList<int>> Groups
        => _groups.Select(group => (IReadOnlyList<int>)group.ToArray()).ToArray();

    private void BuildLayout(AccessoryJobGroupProfile profile)
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 4,
            Padding = new Padding(10)
        };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 120));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        Controls.Add(root);

        root.Controls.Add(new Label
        {
            Text = profile.SummaryText,
            AutoSize = true,
            Dock = DockStyle.Fill,
            Font = new Font(Font, FontStyle.Bold),
            Padding = new Padding(0, 0, 0, 8)
        }, 0, 0);

        var body = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1
        };
        body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 42));
        body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 58));
        root.Controls.Add(body, 0, 1);

        var left = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Padding = new Padding(0, 0, 8, 0)
        };
        left.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        left.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        body.Controls.Add(left, 0, 0);

        var groupToolbar = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = true
        };
        var addButton = new Button { Text = "新增组", AutoSize = true };
        var removeButton = new Button { Text = "删除组", AutoSize = true };
        var upButton = new Button { Text = "上移", AutoSize = true };
        var downButton = new Button { Text = "下移", AutoSize = true };
        addButton.Click += (_, _) => AddGroup();
        removeButton.Click += (_, _) => RemoveSelectedGroup();
        upButton.Click += (_, _) => MoveSelectedGroup(-1);
        downButton.Click += (_, _) => MoveSelectedGroup(1);
        groupToolbar.Controls.AddRange(new Control[] { addButton, removeButton, upButton, downButton });
        left.Controls.Add(groupToolbar, 0, 0);

        _groupList.Dock = DockStyle.Fill;
        _groupList.IntegralHeight = false;
        _groupList.SelectedIndexChanged += (_, _) => BindSelectedGroup();
        left.Controls.Add(_groupList, 0, 1);

        var right = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2
        };
        right.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        right.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        body.Controls.Add(right, 1, 0);
        right.Controls.Add(new Label
        {
            Text = "组内兵种系（第一项为主兵种系）",
            AutoSize = true,
            Dock = DockStyle.Fill,
            Padding = new Padding(0, 0, 0, 6)
        }, 0, 0);

        _jobList.Dock = DockStyle.Fill;
        _jobList.CheckOnClick = true;
        _jobList.IntegralHeight = false;
        _jobList.ItemCheck += (_, e) =>
        {
            if (_binding) return;
            BeginInvoke(new Action(() =>
            {
                CaptureSelectedGroup();
                RefreshGroupList(_groupList.SelectedIndex);
                UpdatePreview();
            }));
        };
        for (var id = 0; id <= AccessoryJobGroupService.MaxJobSeriesId; id++)
        {
            _jobList.Items.Add(FormatJobSeries(id), false);
        }
        right.Controls.Add(_jobList, 0, 1);

        _previewBox.Dock = DockStyle.Fill;
        _previewBox.Multiline = true;
        _previewBox.ReadOnly = true;
        _previewBox.ScrollBars = ScrollBars.Vertical;
        _previewBox.WordWrap = true;
        root.Controls.Add(_previewBox, 0, 2);

        var footer = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1
        };
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        _statusLabel.AutoSize = true;
        _statusLabel.Dock = DockStyle.Fill;
        _statusLabel.Padding = new Padding(0, 8, 8, 0);
        footer.Controls.Add(_statusLabel, 0, 0);

        var okButton = new Button { Text = "确定", DialogResult = DialogResult.OK, AutoSize = true };
        var cancelButton = new Button { Text = "取消", DialogResult = DialogResult.Cancel, AutoSize = true };
        var buttons = new FlowLayoutPanel
        {
            AutoSize = true,
            FlowDirection = FlowDirection.LeftToRight,
            Dock = DockStyle.Fill
        };
        buttons.Controls.AddRange(new Control[] { okButton, cancelButton });
        footer.Controls.Add(buttons, 1, 0);
        root.Controls.Add(footer, 0, 3);
        AcceptButton = okButton;
        CancelButton = cancelButton;

        KeyDown += (_, e) =>
        {
            if (e.KeyCode != Keys.Escape) return;
            DialogResult = DialogResult.Cancel;
            Close();
            e.SuppressKeyPress = true;
        };
    }

    private void AddGroup()
    {
        CaptureSelectedGroup();
        _groups.Add([]);
        RefreshGroupList(_groups.Count - 1);
        UpdatePreview();
    }

    private void RemoveSelectedGroup()
    {
        var index = _groupList.SelectedIndex;
        if (index < 0 || index >= _groups.Count) return;
        if (_groups.Count == 1)
        {
            _groups[0].Clear();
            RefreshGroupList(0);
            UpdatePreview();
            return;
        }

        _groups.RemoveAt(index);
        RefreshGroupList(Math.Min(index, _groups.Count - 1));
        UpdatePreview();
    }

    private void MoveSelectedGroup(int delta)
    {
        var index = _groupList.SelectedIndex;
        var target = index + delta;
        if (index < 0 || target < 0 || index >= _groups.Count || target >= _groups.Count) return;
        CaptureSelectedGroup();
        (_groups[index], _groups[target]) = (_groups[target], _groups[index]);
        RefreshGroupList(target);
        UpdatePreview();
    }

    private void BindSelectedGroup()
    {
        if (_binding) return;
        var index = _groupList.SelectedIndex;
        _binding = true;
        try
        {
            for (var i = 0; i < _jobList.Items.Count; i++) _jobList.SetItemChecked(i, false);
            if (index >= 0 && index < _groups.Count)
            {
                foreach (var id in _groups[index].Where(id => id >= 0 && id < _jobList.Items.Count))
                {
                    _jobList.SetItemChecked(id, true);
                }
            }
        }
        finally
        {
            _binding = false;
        }
    }

    private void CaptureSelectedGroup()
    {
        var index = _groupList.SelectedIndex;
        if (index < 0 || index >= _groups.Count) return;
        _groups[index] = _jobList.CheckedIndices.Cast<int>().ToList();
    }

    private void RefreshGroupList(int selectIndex)
    {
        _binding = true;
        try
        {
            _groupList.Items.Clear();
            for (var i = 0; i < _groups.Count; i++)
            {
                _groupList.Items.Add(BuildGroupText(i, _groups[i]));
            }

            if (_groupList.Items.Count > 0)
            {
                _groupList.SelectedIndex = Math.Clamp(selectIndex, 0, _groupList.Items.Count - 1);
            }
        }
        finally
        {
            _binding = false;
        }

        BindSelectedGroup();
    }

    private void UpdatePreview()
    {
        var diagnostics = new List<string>();
        byte[] bytes;
        try
        {
            bytes = AccessoryJobGroupService.SerializeForTest(_groups.Where(group => group.Count > 0).Select(group => (IReadOnlyList<int>)group).ToArray());
        }
        catch (Exception ex)
        {
            bytes = [];
            diagnostics.Add(ex.Message);
        }

        var lines = new List<string>
        {
            "原始字节预览：",
            bytes.Length == 0 ? "<无法生成>" : HexDisplayFormatter.FormatByteList(bytes),
            string.Empty,
            "分组预览："
        };
        lines.AddRange(_groups.Select((group, index) => BuildGroupText(index, group)));
        if (diagnostics.Count > 0)
        {
            lines.Add(string.Empty);
            lines.AddRange(diagnostics);
        }

        _previewBox.Text = string.Join("\r\n", lines);
        _statusLabel.Text = $"有效分组 {_groups.Count(group => group.Count > 0)} 个；保存时按 FF 分隔并用 90 补齐安全区。";
    }

    private string BuildGroupText(int index, IReadOnlyList<int> group)
    {
        if (group.Count == 0) return $"组{index + 1:D2}：空";
        var items = group.Select(FormatJobSeries);
        return $"组{index + 1:D2}：" + string.Join("、", items);
    }

    private string FormatJobSeries(int id)
    {
        _jobSeriesNames.TryGetValue(id, out var name);
        return string.IsNullOrWhiteSpace(name)
            ? id.ToString("D2", CultureInfo.InvariantCulture)
            : $"{id:D2} {name}";
    }
}
