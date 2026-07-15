using System.Globalization;
using CCZModStudio.Core;

namespace CCZModStudio;

internal enum JobSImageBatchDialogMode
{
    Import,
    Export
}

internal sealed class JobSImageBatchDialog : Form
{
    private readonly JobSImageBatchDialogMode _mode;
    private readonly IReadOnlySet<int> _jobIds;
    private readonly TextBox _folderBox = new();
    private readonly Button _browseButton = new();
    private readonly CheckBox _allyCheck = new();
    private readonly CheckBox _friendlyCheck = new();
    private readonly CheckBox _enemyCheck = new();
    private readonly CheckBox _overwriteCheck = new();
    private readonly TextBox _statusBox = new();
    private readonly Button _okButton = new();
    private readonly Button _cancelButton = new();
    private bool _updatingFactionChecks;

    public JobSImageBatchDialog(JobSImageBatchDialogMode mode, IReadOnlyCollection<int> jobIds)
    {
        _mode = mode;
        _jobIds = jobIds.ToHashSet();

        Text = mode == JobSImageBatchDialogMode.Export ? "导出兵种 S" : "批量导入兵种 S";
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = false;
        MaximizeBox = true;
        ShowInTaskbar = false;
        KeyPreview = true;
        ImportExportDialogLayout.Apply(this, new Size(800, 480), new Size(680, 400));

        BuildLayout();
        _allyCheck.Checked = true;
        UpdateStatus();
    }

    public string SelectedFolder => _folderBox.Text.Trim();

    public bool OverwriteExisting => _mode == JobSImageBatchDialogMode.Export && _overwriteCheck.Checked;

    public IReadOnlyList<int> FactionSlots
    {
        get
        {
            var result = new List<int>();
            if (_allyCheck.Checked) result.Add(1);
            if (_friendlyCheck.Checked) result.Add(2);
            if (_enemyCheck.Checked) result.Add(3);
            return result;
        }
    }

    private void BuildLayout()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 5,
            Padding = new Padding(16)
        };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        Controls.Add(root);

        root.Controls.Add(new Label
        {
            Name = "JobSBatchSummaryLabel",
            Dock = DockStyle.Fill,
            AutoSize = true,
            Font = new Font(Font, FontStyle.Bold),
            Text = $"已选择 {_jobIds.Count.ToString(CultureInfo.InvariantCulture)} 个详细兵种"
        }, 0, 0);

        var folderPanel = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            ColumnCount = 3,
            Padding = new Padding(0, 12, 0, 0)
        };
        folderPanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        folderPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        folderPanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        root.Controls.Add(folderPanel, 0, 1);

        folderPanel.Controls.Add(new Label
        {
            AutoSize = true,
            Padding = new Padding(0, 7, 10, 0),
            Text = _mode == JobSImageBatchDialogMode.Export ? "导出根目录：" : "素材根目录："
        }, 0, 0);
        _folderBox.Name = "JobSBatchFolderBox";
        _folderBox.Dock = DockStyle.Fill;
        _folderBox.PlaceholderText = _mode == JobSImageBatchDialogMode.Export
            ? "将创建 Job<ID>/FactionN/mov.bmp、atk.bmp、spc.bmp"
            : "选择包含 Job<ID>/FactionN 的导出根目录";
        _folderBox.TextChanged += (_, _) =>
        {
            if (_mode == JobSImageBatchDialogMode.Import) SelectDetectedFactionSlots();
            UpdateStatus();
        };
        folderPanel.Controls.Add(_folderBox, 1, 0);

        _browseButton.Name = "JobSBatchBrowseButton";
        _browseButton.Text = "浏览...";
        _browseButton.AutoSize = true;
        _browseButton.MinimumSize = new Size(110, 40);
        _browseButton.Click += (_, _) => BrowseFolder();
        folderPanel.Controls.Add(_browseButton, 2, 0);

        var options = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = true,
            Padding = new Padding(0, 16, 0, 8)
        };
        root.Controls.Add(options, 0, 2);

        ConfigureFactionCheck(_allyCheck, 1);
        ConfigureFactionCheck(_friendlyCheck, 2);
        ConfigureFactionCheck(_enemyCheck, 3);
        options.Controls.AddRange([_allyCheck, _friendlyCheck, _enemyCheck]);

        _overwriteCheck.Name = "JobSBatchOverwriteCheckBox";
        _overwriteCheck.Text = "覆盖已有 BMP";
        _overwriteCheck.AutoSize = true;
        _overwriteCheck.Padding = new Padding(24, 3, 0, 3);
        _overwriteCheck.Visible = _mode == JobSImageBatchDialogMode.Export;
        _overwriteCheck.CheckedChanged += (_, _) => UpdateStatus();
        options.Controls.Add(_overwriteCheck);

        _statusBox.Name = "JobSBatchStatusBox";
        _statusBox.Dock = DockStyle.Fill;
        _statusBox.Multiline = true;
        _statusBox.ReadOnly = true;
        _statusBox.ScrollBars = ScrollBars.Vertical;
        _statusBox.WordWrap = true;
        root.Controls.Add(_statusBox, 0, 3);

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            Padding = new Padding(0, 12, 0, 0)
        };
        _okButton.Name = "JobSBatchOkButton";
        _okButton.Text = _mode == JobSImageBatchDialogMode.Export ? "导出" : "预览导入";
        _okButton.AutoSize = true;
        _okButton.MinimumSize = new Size(120, 42);
        _okButton.Click += (_, _) => Confirm();
        _cancelButton.Name = "JobSBatchCancelButton";
        _cancelButton.Text = "取消";
        _cancelButton.AutoSize = true;
        _cancelButton.MinimumSize = new Size(110, 42);
        _cancelButton.DialogResult = DialogResult.Cancel;
        buttons.Controls.AddRange([_okButton, _cancelButton]);
        root.Controls.Add(buttons, 0, 4);

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

    private void ConfigureFactionCheck(CheckBox check, int factionSlot)
    {
        check.Name = $"JobSBatchFaction{factionSlot}CheckBox";
        check.Text = $"Faction{factionSlot} {CharacterImageResourceService.BuildSPreviewFactionText(factionSlot)}";
        check.AutoSize = true;
        check.Padding = new Padding(0, 3, 20, 3);
        check.CheckedChanged += (_, _) =>
        {
            if (!_updatingFactionChecks) UpdateStatus();
        };
    }

    private void BrowseFolder()
    {
        using var dialog = new FolderBrowserDialog
        {
            Description = _mode == JobSImageBatchDialogMode.Export
                ? "选择兵种 S 标准格式导出根目录"
                : "选择包含 Job<ID>/FactionN 的兵种 S 素材根目录",
            UseDescriptionForTitle = true
        };
        if (Directory.Exists(SelectedFolder)) dialog.SelectedPath = SelectedFolder;
        if (dialog.ShowDialog(this) == DialogResult.OK) _folderBox.Text = dialog.SelectedPath;
    }

    private void SelectDetectedFactionSlots()
    {
        var detected = DetectCanonicalFactionSlots();
        _updatingFactionChecks = true;
        try
        {
            _allyCheck.Checked = detected.Count == 0 || detected.Contains(1);
            _friendlyCheck.Checked = detected.Contains(2);
            _enemyCheck.Checked = detected.Contains(3);
        }
        finally
        {
            _updatingFactionChecks = false;
        }
    }

    private IReadOnlySet<int> DetectCanonicalFactionSlots()
    {
        var result = new HashSet<int>();
        if (!Directory.Exists(SelectedFolder)) return result;
        foreach (var jobFolder in Directory.EnumerateDirectories(SelectedFolder))
        {
            if (!JobSImageMaterialLayout.TryParseJobFolder(jobFolder, out var jobId) || !_jobIds.Contains(jobId)) continue;
            foreach (var factionFolder in Directory.EnumerateDirectories(jobFolder))
            {
                if (JobSImageMaterialLayout.TryParseFactionFolder(factionFolder, out var factionSlot)) result.Add(factionSlot);
            }
        }

        return result;
    }

    private void UpdateStatus()
    {
        var factionText = FactionSlots.Count == 0
            ? "未选择"
            : string.Join("、", FactionSlots.Select(slot => $"Faction{slot} {CharacterImageResourceService.BuildSPreviewFactionText(slot)}"));
        var folderState = string.IsNullOrWhiteSpace(SelectedFolder)
            ? "未选择目录"
            : Directory.Exists(SelectedFolder)
                ? "目录已存在"
                : _mode == JobSImageBatchDialogMode.Export ? "目录将在导出时创建" : "目录不存在";

        if (_mode == JobSImageBatchDialogMode.Export)
        {
            var fileCount = _jobIds.Count * FactionSlots.Count * JobSImageMaterialLayout.RequiredFileNames.Count;
            _statusBox.Text =
                $"阵营：{factionText}\r\n" +
                $"预计导出：{fileCount.ToString(CultureInfo.InvariantCulture)} 个 BMP。\r\n" +
                "目录格式：Job<ID>/FactionN/mov.bmp、atk.bmp、spc.bmp。\r\n" +
                $"已有同名文件：{(OverwriteExisting ? "覆盖" : "跳过")}。\r\n" +
                $"目录状态：{folderState}";
        }
        else
        {
            var canonicalSlots = DetectCanonicalFactionSlots();
            var legacyJobs = CountLegacyJobs();
            _statusBox.Text =
                $"导入阵营：{factionText}\r\n" +
                $"检测到标准阵营：{(canonicalSlots.Count == 0 ? "无" : string.Join("、", canonicalSlots.OrderBy(slot => slot).Select(slot => "Faction" + slot)))}。\r\n" +
                $"检测到旧式平铺 Job：{legacyJobs.ToString(CultureInfo.InvariantCulture)} 个。\r\n" +
                "标准目录按自身 FactionN 写入；旧式平铺目录会把同一组三文件写入所选阵营。\r\n" +
                $"目录状态：{folderState}";
        }

        _okButton.Enabled = _jobIds.Count > 0 &&
                            FactionSlots.Count > 0 &&
                            !string.IsNullOrWhiteSpace(SelectedFolder) &&
                            (_mode == JobSImageBatchDialogMode.Export || Directory.Exists(SelectedFolder));
    }

    private int CountLegacyJobs()
    {
        if (!Directory.Exists(SelectedFolder)) return 0;
        var count = 0;
        foreach (var jobFolder in Directory.EnumerateDirectories(SelectedFolder))
        {
            if (!JobSImageMaterialLayout.TryParseJobFolder(jobFolder, out var jobId) || !_jobIds.Contains(jobId)) continue;
            var hasCanonical = Directory.EnumerateDirectories(jobFolder)
                .Any(factionFolder => JobSImageMaterialLayout.TryParseFactionFolder(factionFolder, out _));
            if (!hasCanonical && JobSImageMaterialLayout.HasCompleteTriplet(jobFolder)) count++;
        }

        return count;
    }

    private void Confirm()
    {
        if (FactionSlots.Count == 0)
        {
            MessageBox.Show(this, "请至少选择一个阵营。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        if (_mode == JobSImageBatchDialogMode.Import && !Directory.Exists(SelectedFolder))
        {
            MessageBox.Show(this, "请选择有效的兵种 S 素材根目录。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        DialogResult = DialogResult.OK;
        Close();
    }
}
