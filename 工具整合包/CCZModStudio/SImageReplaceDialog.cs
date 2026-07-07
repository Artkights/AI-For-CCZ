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
    private readonly CczProject? _project;

    public SImageReplaceDialog(int sImageId, int? jobId, int factionSlot, string displayName)
        : this(null, sImageId, jobId, factionSlot, displayName)
    {
    }

    public SImageReplaceDialog(CczProject? project, int sImageId, int? jobId, int factionSlot, string displayName)
    {
        _project = project;
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

        var mapping = ResolveMapping();
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

        foreach (var target in ResolveStageTargets(mapping))
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

    private SUnitImageMapping ResolveMapping()
        => _project == null
            ? CharacterImageResourceService.ResolveSUnitImageMapping(SImageId, JobId, FactionSlot)
            : CharacterImageResourceService.ResolveSUnitImageMapping(_project, SImageId, JobId, FactionSlot);

    private IReadOnlyList<SImageStageTarget> ResolveStageTargets(SUnitImageMapping mapping)
        => _project == null
            ? CharacterImageResourceService.ResolveSImageStageTargets(mapping, Array.Empty<int>(), defaultAllStages: true)
            : CharacterImageResourceService.ResolveSImageStageTargets(_project, mapping, Array.Empty<int>(), defaultAllStages: true);

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

internal sealed class SImageExportDialog : Form
{
    private readonly TextBox _folderBox = new();
    private readonly Button _browseButton = new();
    private readonly FlowLayoutPanel _stageChecksPanel = new();
    private readonly Button _selectAllStagesButton = new();
    private readonly CheckBox _overwriteExistingCheckBox = new();
    private readonly Label _statusLabel = new();
    private readonly Button _okButton = new();
    private readonly Button _cancelButton = new();
    private readonly List<(int StageSlot, CheckBox Check)> _stageChecks = [];
    private readonly CczProject? _project;
    private readonly IReadOnlyList<BmpExportTarget> _targets;
    private readonly int _factionSlot;

    public SImageExportDialog(CczProject? project, IReadOnlyList<BmpExportTarget> targets, int factionSlot)
    {
        _project = project;
        _targets = targets;
        _factionSlot = CharacterImageResourceService.NormalizeSPreviewFactionSlot(factionSlot);

        Text = "导出人物 S 形象 BMP";
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(800, 380);
        MinimumSize = new Size(760, 420);
        Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Regular, GraphicsUnit.Point);
        KeyPreview = true;

        BuildLayout();
        UpdateStatus();
    }

    public string OutputFolder => _folderBox.Text.Trim();

    public bool OverwriteExisting => _overwriteExistingCheckBox.Checked;

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

        root.Controls.Add(new Label
        {
            Name = "SImageExportSummaryLabel",
            Dock = DockStyle.Fill,
            AutoSize = true,
            Font = new Font(Font, FontStyle.Bold),
            Text = BuildSummaryText()
        }, 0, 0);

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
            Text = "导出目录：",
            Dock = DockStyle.Fill,
            AutoSize = false,
            TextAlign = ContentAlignment.MiddleLeft
        }, 0, 0);

        _folderBox.Name = "SImageExportOutputFolderBox";
        _folderBox.Dock = DockStyle.Fill;
        _folderBox.Margin = new Padding(0, 5, 8, 0);
        _folderBox.PlaceholderText = "选择 S 形象 BMP 导出目录；多行导出会在此目录下创建 S 编号子目录";
        _folderBox.TextChanged += (_, _) => UpdateStatus();
        folderPanel.Controls.Add(_folderBox, 1, 0);

        _browseButton.Name = "SImageExportBrowseButton";
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
            RowCount = 4,
            Padding = new Padding(0, 14, 0, 0)
        };
        selectionPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        selectionPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        selectionPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        selectionPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.Controls.Add(selectionPanel, 0, 2);

        selectionPanel.Controls.Add(new Label
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            Text = "选择要导出的转："
        }, 0, 0);

        _stageChecksPanel.Name = "SImageExportStageChecksPanel";
        _stageChecksPanel.Dock = DockStyle.Top;
        _stageChecksPanel.AutoSize = true;
        _stageChecksPanel.FlowDirection = FlowDirection.LeftToRight;
        _stageChecksPanel.WrapContents = true;
        _stageChecksPanel.Padding = new Padding(0, 8, 0, 0);
        selectionPanel.Controls.Add(_stageChecksPanel, 0, 1);

        foreach (var stage in new[] { 1, 2, 3 })
        {
            var check = new CheckBox
            {
                Name = $"SImageExportStage{stage}CheckBox",
                AutoSize = true,
                Padding = new Padding(0, 2, 18, 2),
                Text = CharacterImageResourceService.BuildSImageStageText(stage),
                Checked = stage == 1
            };
            check.CheckedChanged += (_, _) => UpdateStatus();
            _stageChecks.Add((stage, check));
            _stageChecksPanel.Controls.Add(check);
        }

        _selectAllStagesButton.Name = "SImageExportSelectAllStagesButton";
        _selectAllStagesButton.Text = "全选";
        _selectAllStagesButton.AutoSize = true;
        _selectAllStagesButton.MinimumSize = new Size(88, 30);
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

        _overwriteExistingCheckBox.Name = "SImageExportOverwriteExistingCheckBox";
        _overwriteExistingCheckBox.Text = "覆盖已有 BMP";
        _overwriteExistingCheckBox.AutoSize = true;
        _overwriteExistingCheckBox.Padding = new Padding(0, 8, 0, 0);
        _overwriteExistingCheckBox.CheckedChanged += (_, _) => UpdateStatus();
        selectionPanel.Controls.Add(_overwriteExistingCheckBox, 0, 2);

        _statusLabel.Name = "SImageExportStatusLabel";
        _statusLabel.Dock = DockStyle.Top;
        _statusLabel.AutoSize = true;
        _statusLabel.Padding = new Padding(0, 10, 0, 0);
        selectionPanel.Controls.Add(_statusLabel, 0, 3);

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = false,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            Padding = new Padding(0, 8, 0, 0)
        };
        root.Controls.Add(buttons, 0, 3);

        _okButton.Name = "SImageExportOkButton";
        _okButton.Text = "确定";
        _okButton.AutoSize = true;
        _okButton.MinimumSize = new Size(88, 30);
        _okButton.Click += (_, _) => Confirm();

        _cancelButton.Name = "SImageExportCancelButton";
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
            Description = "选择 S 形象 BMP 导出目录",
            UseDescriptionForTitle = true
        };
        if (Directory.Exists(OutputFolder)) dialog.SelectedPath = OutputFolder;
        if (dialog.ShowDialog(this) == DialogResult.OK) _folderBox.Text = dialog.SelectedPath;
    }

    private void Confirm()
    {
        if (string.IsNullOrWhiteSpace(OutputFolder))
        {
            MessageBox.Show(this, "请选择有效的导出目录。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
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
        var outputMode = _targets.Count == 1
            ? "单行导出会直接在导出目录写入 mov.bmp / atk.bmp / spc.bmp。"
            : $"批量导出会为 {_targets.Count.ToString(CultureInfo.InvariantCulture)} 行创建可回导素材子目录。";
        var folderText = string.IsNullOrWhiteSpace(OutputFolder)
            ? "未选择目录"
            : Directory.Exists(OutputFolder)
                ? "目录已存在"
                : "目录不存在，导出时会尝试创建";
        var overwriteText = OverwriteExisting
            ? "已有同名 BMP 会被覆盖。"
            : "已有同名 BMP 会跳过。";

        _statusLabel.Text =
            $"将导出：{stageText}。\r\n" +
            $"{outputMode}\r\n" +
            $"{overwriteText}\r\n" +
            $"目录状态：{folderText}\r\n" +
            "单转 S 只会导出第一转；若选择了第二/第三转，导出结果会提示已忽略。";
        _okButton.Enabled = !string.IsNullOrWhiteSpace(OutputFolder) && StageSlots.Count > 0;
    }

    private string BuildSummaryText()
    {
        if (_targets.Count == 1)
        {
            var target = _targets[0];
            var mapping = ResolveMapping(target);
            return string.IsNullOrWhiteSpace(target.DisplayName)
                ? $"S={target.FieldValue.ToString(CultureInfo.InvariantCulture)}    {mapping.ShortText}"
                : $"{target.DisplayName}    S={target.FieldValue.ToString(CultureInfo.InvariantCulture)}    {mapping.ShortText}";
        }

        var sCount = _targets.Select(target => target.FieldValue).Distinct().Count();
        return $"已选择 {_targets.Count.ToString(CultureInfo.InvariantCulture)} 行人物 S 形象    S 编号 {sCount.ToString(CultureInfo.InvariantCulture)} 个    S预览阵营={CharacterImageResourceService.BuildSPreviewFactionText(_factionSlot)}";
    }

    private SUnitImageMapping ResolveMapping(BmpExportTarget target)
        => _project == null
            ? CharacterImageResourceService.ResolveSUnitImageMapping(target.FieldValue, target.JobId, _factionSlot)
            : CharacterImageResourceService.ResolveSUnitImageMapping(_project, target.FieldValue, target.JobId, _factionSlot);
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
        ClientSize = new Size(800, 260);
        MinimumSize = new Size(760, 300);
        Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Regular, GraphicsUnit.Point);
        KeyPreview = true;
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
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 54));
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
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = true,
            Padding = new Padding(0, 10, 0, 0)
        };
        root.Controls.Add(checks, 0, 1);

        foreach (var stage in availableStages.Distinct().OrderBy(stage => stage))
        {
            var check = new CheckBox
            {
                AutoSize = true,
                Padding = new Padding(0, 2, 18, 2),
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
            MinimumSize = new Size(88, 30),
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
            AutoSize = false,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            Padding = new Padding(0, 8, 0, 0)
        };
        root.Controls.Add(buttons, 0, 3);

        _okButton.Text = "确定";
        _okButton.AutoSize = true;
        _okButton.MinimumSize = new Size(88, 30);
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
            MinimumSize = new Size(88, 30),
            DialogResult = DialogResult.Cancel
        };
        buttons.Controls.AddRange(new Control[] { _okButton, cancel });
        AcceptButton = _okButton;
        CancelButton = cancel;
        KeyDown += (_, e) =>
        {
            if (e.KeyCode != Keys.Escape) return;
            DialogResult = DialogResult.Cancel;
            Close();
            e.SuppressKeyPress = true;
        };
    }
}
