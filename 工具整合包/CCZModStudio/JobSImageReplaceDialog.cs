using System.Globalization;
using CCZModStudio.Core;

namespace CCZModStudio;

internal sealed class JobSImageReplaceDialog : Form
{
    private readonly TextBox _folderBox = new();
    private readonly Button _browseButton = new();
    private readonly CheckBox _allyCheck = new();
    private readonly CheckBox _friendlyCheck = new();
    private readonly CheckBox _enemyCheck = new();
    private readonly Label _statusLabel = new();
    private readonly Button _okButton = new();
    private readonly Button _cancelButton = new();

    public JobSImageReplaceDialog(int jobId, string jobName)
    {
        JobId = jobId;
        JobName = jobName;

        Text = $"一键替换兵种形象：ID={jobId:D2} {jobName}";
        StartPosition = FormStartPosition.CenterParent;
        Size = new Size(640, 300);
        MinimumSize = new Size(560, 260);
        Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Regular, GraphicsUnit.Point);
        KeyPreview = true;

        BuildLayout();
        UpdateStatus();
    }

    public int JobId { get; }

    public string JobName { get; }

    public string MaterialFolder => _folderBox.Text.Trim();

    public IReadOnlyList<int> FactionSlots
    {
        get
        {
            var slots = new List<int>();
            if (_allyCheck.Checked) slots.Add(1);
            if (_friendlyCheck.Checked) slots.Add(2);
            if (_enemyCheck.Checked) slots.Add(3);
            return slots;
        }
    }

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
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        Controls.Add(root);

        var title = new Label
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            Font = new Font(Font, FontStyle.Bold),
            Text = $"详细兵种 {JobId:D2} {JobName}"
        };
        root.Controls.Add(title, 0, 0);

        var folderPanel = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            ColumnCount = 3,
            Padding = new Padding(0, 10, 0, 0)
        };
        folderPanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        folderPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        folderPanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        root.Controls.Add(folderPanel, 0, 1);

        folderPanel.Controls.Add(new Label { Text = "素材目录：", AutoSize = true, Padding = new Padding(0, 7, 8, 0) }, 0, 0);
        _folderBox.Dock = DockStyle.Fill;
        _folderBox.PlaceholderText = "选择包含 mov.bmp / atk.bmp / spc.bmp 的目录";
        _folderBox.TextChanged += (_, _) => UpdateStatus();
        folderPanel.Controls.Add(_folderBox, 1, 0);
        _browseButton.Text = "浏览...";
        _browseButton.AutoSize = true;
        _browseButton.Click += (_, _) => BrowseFolder();
        folderPanel.Controls.Add(_browseButton, 2, 0);

        var selectionPanel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            ColumnCount = 1,
            Padding = new Padding(0, 14, 0, 0)
        };
        root.Controls.Add(selectionPanel, 0, 2);

        selectionPanel.Controls.Add(new Label
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            Text = "选择要导入的阵营槽："
        }, 0, 0);

        var checks = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = true,
            Padding = new Padding(0, 8, 0, 0)
        };
        selectionPanel.Controls.Add(checks, 0, 1);

        ConfigureFactionCheck(_allyCheck, 1);
        ConfigureFactionCheck(_friendlyCheck, 2);
        ConfigureFactionCheck(_enemyCheck, 3);
        _allyCheck.Checked = true;
        checks.Controls.AddRange(new Control[] { _allyCheck, _friendlyCheck, _enemyCheck });

        _statusLabel.Dock = DockStyle.Top;
        _statusLabel.AutoSize = true;
        _statusLabel.Padding = new Padding(0, 10, 0, 0);
        selectionPanel.Controls.Add(_statusLabel, 0, 2);

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            FlowDirection = FlowDirection.RightToLeft
        };
        root.Controls.Add(buttons, 0, 3);

        _okButton.Text = "确定";
        _okButton.AutoSize = true;
        _okButton.Click += (_, _) => Confirm();
        _cancelButton.Text = "取消";
        _cancelButton.AutoSize = true;
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

    private void ConfigureFactionCheck(CheckBox check, int slot)
    {
        var imageNumber = JobId * 3 + slot;
        check.AutoSize = true;
        check.Padding = new Padding(0, 2, 18, 2);
        check.Text = $"{CharacterImageResourceService.BuildSPreviewFactionText(slot)}  #{imageNumber.ToString(CultureInfo.InvariantCulture)}";
        check.CheckedChanged += (_, _) => UpdateStatus();
    }

    private void BrowseFolder()
    {
        using var dialog = new FolderBrowserDialog
        {
            Description = "选择包含 mov.bmp / atk.bmp / spc.bmp 的 S 形象素材目录",
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

        if (FactionSlots.Count == 0)
        {
            MessageBox.Show(this, "请至少选择一个阵营槽。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        DialogResult = DialogResult.OK;
        Close();
    }

    private void UpdateStatus()
    {
        var files = new[] { "mov.bmp", "atk.bmp", "spc.bmp" };
        var existing = Directory.Exists(MaterialFolder)
            ? files.Where(file => File.Exists(Path.Combine(MaterialFolder, file))).ToArray()
            : Array.Empty<string>();
        var slotText = FactionSlots.Count == 0
            ? "未选择"
            : string.Join("、", FactionSlots.Select(CharacterImageResourceService.BuildSPreviewFactionText));
        _statusLabel.Text =
            $"将写入：{slotText}。\r\n" +
            $"素材检测：{(existing.Length == 0 ? "未检测到 mov.bmp / atk.bmp / spc.bmp" : string.Join("、", existing))}。\r\n" +
            "允许只提供其中一部分素材；只会写入实际存在的 mov/atk/spc 文件。";
        _okButton.Enabled = Directory.Exists(MaterialFolder) && FactionSlots.Count > 0;
    }
}
