using System.Globalization;
using CCZModStudio.Core;
using CCZModStudio.Models;

namespace CCZModStudio;

internal sealed class SImageReplaceDialog : Form
{
    private readonly TextBox _folderBox = new();
    private readonly Button _browseButton = new();
    private readonly FlowLayoutPanel _stageChecksPanel = new();
    private readonly Button _selectAllStagesButton = new();
    private readonly Label _statusLabel = new();
    private readonly Button _okButton = new();
    private readonly Button _cancelButton = new();
    private readonly List<(int StageSlot, CheckBox Check)> _stageChecks = [];

    public SImageReplaceDialog(int sImageId, int? jobId, int factionSlot, string displayName)
    {
        SImageId = sImageId;
        JobId = jobId;
        FactionSlot = factionSlot;
        DisplayName = displayName;

        Text = $"一键替换人物 S 形象：S={sImageId}";
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(800, 380);
        MinimumSize = new Size(760, 420);
        Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Regular, GraphicsUnit.Point);
        KeyPreview = true;

        BuildLayout();
        UpdateStatus();
    }

    public int SImageId { get; }

    public int? JobId { get; }

    public int FactionSlot { get; }

    public string DisplayName { get; }

    public string MaterialFolder => _folderBox.Text.Trim();

    public IReadOnlyList<int> StageSlots
        => _stageChecks
            .Where(item => item.Check.Checked)
            .Select(item => item.StageSlot)
            .OrderBy(slot => slot)
            .ToArray();

    private void BuildLayout()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 4,
            Padding = new Padding(12)
        };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 50));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 54));
        Controls.Add(root);

        var mapping = CharacterImageResourceService.ResolveSUnitImageMapping(SImageId, JobId, FactionSlot);
        var title = new Label
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            Font = new Font(Font, FontStyle.Bold),
            Text = string.IsNullOrWhiteSpace(DisplayName)
                ? $"S={SImageId}    {mapping.ShortText}"
                : $"{DisplayName}    S={SImageId}    {mapping.ShortText}"
        };
        root.Controls.Add(title, 0, 0);

        var folderPanel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = false,
            ColumnCount = 3,
            RowCount = 1,
            Padding = new Padding(0, 8, 0, 0),
            Margin = Padding.Empty
        };
        folderPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 88));
        folderPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        folderPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 96));
        folderPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.Controls.Add(folderPanel, 0, 1);

        folderPanel.Controls.Add(new Label
        {
            Text = "素材目录：",
            Dock = DockStyle.Fill,
            AutoSize = false,
            TextAlign = ContentAlignment.MiddleLeft
        }, 0, 0);
        _folderBox.Dock = DockStyle.Fill;
        _folderBox.Margin = new Padding(0, 5, 8, 0);
        _folderBox.PlaceholderText = "选择包含 mov.bmp / atk.bmp / spc.bmp，或 turn1/turn2/turn3 子目录的目录";
        _folderBox.TextChanged += (_, _) => UpdateStatus();
        folderPanel.Controls.Add(_folderBox, 1, 0);
        _browseButton.Text = "浏览...";
        _browseButton.AutoSize = false;
        _browseButton.Dock = DockStyle.Fill;
        _browseButton.Margin = new Padding(0, 3, 0, 3);
        _browseButton.MinimumSize = new Size(88, 30);
        _browseButton.Click += (_, _) => BrowseFolder();
        folderPanel.Controls.Add(_browseButton, 2, 0);

        var selectionPanel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = false,
            ColumnCount = 1,
            RowCount = 3,
            Padding = new Padding(0, 14, 0, 0)
        };
        selectionPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        selectionPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        selectionPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.Controls.Add(selectionPanel, 0, 2);

        selectionPanel.Controls.Add(new Label
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            Text = "选择要导入的转："
        }, 0, 0);

        _stageChecksPanel.Dock = DockStyle.Top;
        _stageChecksPanel.AutoSize = true;
        _stageChecksPanel.FlowDirection = FlowDirection.LeftToRight;
        _stageChecksPanel.WrapContents = true;
        _stageChecksPanel.Padding = new Padding(0, 8, 0, 0);
        selectionPanel.Controls.Add(_stageChecksPanel, 0, 1);

        foreach (var target in CharacterImageResourceService.ResolveSImageStageTargets(mapping, Array.Empty<int>(), defaultAllStages: true))
        {
            var check = new CheckBox
            {
                AutoSize = true,
                Padding = new Padding(0, 2, 18, 2),
                Text = $"{target.DisplayName}  #{target.ImageNumber.ToString(CultureInfo.InvariantCulture)}",
                Checked = target.StageSlot == 1
            };
            check.CheckedChanged += (_, _) => UpdateStatus();
            _stageChecks.Add((target.StageSlot, check));
            _stageChecksPanel.Controls.Add(check);
        }

        _selectAllStagesButton.Text = "全选";
        _selectAllStagesButton.AutoSize = true;
        _selectAllStagesButton.Enabled = _stageChecks.Count > 1;
        _selectAllStagesButton.Click += (_, _) =>
        {
            foreach (var item in _stageChecks)
            {
                item.Check.Checked = true;
            }

            UpdateStatus();
        };
        _stageChecksPanel.Controls.Add(_selectAllStagesButton);

        _statusLabel.Dock = DockStyle.Top;
        _statusLabel.AutoSize = true;
        _statusLabel.Padding = new Padding(0, 10, 0, 0);
        selectionPanel.Controls.Add(_statusLabel, 0, 2);

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = false,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            Padding = new Padding(0, 8, 0, 0)
        };
        root.Controls.Add(buttons, 0, 3);

        _okButton.Text = "确定";
        _okButton.AutoSize = true;
        _okButton.MinimumSize = new Size(88, 30);
        _okButton.Click += (_, _) => Confirm();
        _cancelButton.Text = "取消";
        _cancelButton.AutoSize = true;
        _cancelButton.MinimumSize = new Size(88, 30);
        _cancelButton.DialogResult = DialogResult.Cancel;
        buttons.Controls.AddRange(new Control[] { _okButton, _cancelButton });

        AcceptButton = _okButton;
        CancelButton = _cancelButton;
        KeyDown += (_, e) =>
        {
            if (e.KeyCode != Keys.Escape) return;
            DialogResult = DialogResult.Cancel;
            Close();
            e.SuppressKeyPress = true;
        };
    }

    private void BrowseFolder()
    {
        using var dialog = new FolderBrowserDialog
        {
            Description = "选择 S 形象素材目录",
            UseDescriptionForTitle = true
        };
        if (Directory.Exists(MaterialFolder)) dialog.SelectedPath = MaterialFolder;
        if (dialog.ShowDialog(this) == DialogResult.OK) _folderBox.Text = dialog.SelectedPath;
    }

    private void Confirm()
    {
        if (string.IsNullOrWhiteSpace(MaterialFolder) || !Directory.Exists(MaterialFolder))
        {
            MessageBox.Show(this, "请选择有效的素材目录。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        if (StageSlots.Count == 0)
        {
            MessageBox.Show(this, "请至少选择一个转。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        DialogResult = DialogResult.OK;
        Close();
    }

    private void UpdateStatus()
    {
        var stageText = StageSlots.Count == 0
            ? "未选择"
            : string.Join("、", StageSlots.Select(CharacterImageResourceService.BuildSImageStageText));
        var detected = Directory.Exists(MaterialFolder)
            ? DetectMaterialSummary(MaterialFolder, StageSlots)
            : "未选择目录";
        _statusLabel.Text =
            $"将写入：{stageText}。\r\n" +
            $"素材检测：{detected}\r\n" +
            "每个转优先读取 turnN 子目录；没有对应文件时回退读取根目录 mov.bmp / atk.bmp / spc.bmp。";
        _okButton.Enabled = Directory.Exists(MaterialFolder) && StageSlots.Count > 0;
    }

    private static string DetectMaterialSummary(string folder, IReadOnlyList<int> stageSlots)
    {
        var names = new[] { "mov.bmp", "atk.bmp", "spc.bmp" };
        var detected = new List<string>();
        foreach (var stageSlot in stageSlots.DefaultIfEmpty(1))
        {
            var stageFolder = Path.Combine(folder, $"turn{stageSlot.ToString(CultureInfo.InvariantCulture)}");
            var existing = names.Where(file =>
                File.Exists(Path.Combine(stageFolder, file)) ||
                File.Exists(Path.Combine(folder, file))).ToArray();
            if (existing.Length > 0)
            {
                detected.Add($"{CharacterImageResourceService.BuildSImageStageText(stageSlot)}:{string.Join("/", existing)}");
            }
        }

        return detected.Count == 0 ? "未检测到 mov.bmp / atk.bmp / spc.bmp" : string.Join("；", detected);
    }
}

internal sealed class SImageStageSelectionDialog : Form
{
    private readonly List<(int StageSlot, CheckBox Check)> _stageChecks = [];
    private readonly Button _okButton = new();

    public SImageStageSelectionDialog(string title, IReadOnlyList<int> availableStages, string description)
    {
        Text = title;
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MinimizeBox = false;
        MaximizeBox = false;
        ClientSize = new Size(360, 210);
        Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Regular, GraphicsUnit.Point);
        BuildLayout(availableStages, description);
    }

    public IReadOnlyList<int> StageSlots
        => _stageChecks
            .Where(item => item.Check.Checked)
            .Select(item => item.StageSlot)
            .OrderBy(slot => slot)
            .ToArray();

    private void BuildLayout(IReadOnlyList<int> availableStages, string description)
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 4,
            Padding = new Padding(12)
        };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        Controls.Add(root);

        root.Controls.Add(new Label
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            Text = description
        }, 0, 0);

        var checks = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            Padding = new Padding(0, 10, 0, 0)
        };
        root.Controls.Add(checks, 0, 1);

        foreach (var stage in availableStages.Distinct().OrderBy(stage => stage))
        {
            var check = new CheckBox
            {
                AutoSize = true,
                Checked = stage == 1,
                Text = CharacterImageResourceService.BuildSImageStageText(stage)
            };
            check.CheckedChanged += (_, _) => _okButton.Enabled = StageSlots.Count > 0;
            _stageChecks.Add((stage, check));
            checks.Controls.Add(check);
        }

        var selectAll = new Button
        {
            Text = "全选",
            AutoSize = true,
            Enabled = _stageChecks.Count > 1
        };
        selectAll.Click += (_, _) =>
        {
            foreach (var item in _stageChecks)
            {
                item.Check.Checked = true;
            }
        };
        root.Controls.Add(selectAll, 0, 2);

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            FlowDirection = FlowDirection.RightToLeft
        };
        root.Controls.Add(buttons, 0, 3);

        _okButton.Text = "确定";
        _okButton.AutoSize = true;
        _okButton.Enabled = StageSlots.Count > 0;
        _okButton.Click += (_, _) =>
        {
            if (StageSlots.Count == 0)
            {
                MessageBox.Show(this, "请至少选择一个转。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            DialogResult = DialogResult.OK;
            Close();
        };
        var cancel = new Button
        {
            Text = "取消",
            AutoSize = true,
            DialogResult = DialogResult.Cancel
        };
        buttons.Controls.AddRange(new Control[] { _okButton, cancel });
        AcceptButton = _okButton;
        CancelButton = cancel;
    }
}
