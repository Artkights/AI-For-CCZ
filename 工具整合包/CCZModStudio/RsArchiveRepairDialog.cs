using CCZModStudio.Models;

namespace CCZModStudio;

internal sealed class RsArchiveRepairDialog : Form
{
    private readonly RsArchiveRepairPreview _preview;
    private readonly DataGridView _grid = new();
    private readonly TextBox _diagnostic = new();
    private readonly ComboBox _mode = new();
    private readonly ComboBox _backup = new();
    private readonly Button _execute = new();

    public RsArchiveRepairDialog(RsArchiveRepairPreview preview)
    {
        _preview = preview;
        Text = "修复/整理 R/S 档案（预览）";
        Name = "RsArchiveRepairDialog";
        StartPosition = FormStartPosition.CenterParent;
        MinimumSize = new Size(980, 620);
        Size = new Size(1180, 760);
        BuildUi();
        LoadPreview();
    }

    public RsArchiveRepairMode SelectedMode => _mode.SelectedIndex switch
    {
        1 => RsArchiveRepairMode.CompactOnly,
        2 => RsArchiveRepairMode.RestoreWholeBackup,
        3 => RsArchiveRepairMode.LegacyIndexShiftRecovery,
        _ => RsArchiveRepairMode.PreservePixelsAndRestoreFormat
    };

    public string? SelectedBackupPath => _backup.SelectedItem as string;

    private void BuildUi()
    {
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 4, ColumnCount = 1, Padding = new Padding(10) };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 58));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 42));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        Controls.Add(root);

        var warning = new Label
        {
            AutoSize = true,
            MaximumSize = new Size(1120, 0),
            Text = "此工具不会自动猜测 PNG 的原格式。只有备份链证明 RAW/BMP→PNG 且尺寸和编码复读通过的条目，才会列为格式恢复候选。执行前会再次完整备份当前 E5。"
        };
        root.Controls.Add(warning, 0, 0);

        _grid.Dock = DockStyle.Fill;
        _grid.AllowUserToAddRows = false;
        _grid.AllowUserToDeleteRows = false;
        _grid.AutoGenerateColumns = false;
        _grid.RowHeadersVisible = false;
        _grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _grid.Columns.Add(new DataGridViewCheckBoxColumn { Name = "Selected", HeaderText = "恢复", Width = 54 });
        AddColumn("Archive", "档案", 120);
        AddColumn("Image", "图号", 64);
        AddColumn("Format", "当前 → 恢复", 120);
        AddColumn("Size", "当前 → 预计长度", 150);
        AddColumn("Dimensions", "尺寸", 90);
        AddColumn("Nearest", "RAW最近色", 90);
        AddColumn("Backup", "证据备份", 260);
        AddColumn("Warning", "提示/损失警告", 260);
        root.Controls.Add(_grid, 0, 1);

        _diagnostic.Dock = DockStyle.Fill;
        _diagnostic.Multiline = true;
        _diagnostic.ReadOnly = true;
        _diagnostic.ScrollBars = ScrollBars.Both;
        _diagnostic.Font = new Font("Consolas", 9F);
        root.Controls.Add(_diagnostic, 0, 2);

        var actions = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight };
        actions.Controls.Add(new Label { Text = "执行方式：", AutoSize = true, Margin = new Padding(0, 8, 4, 0) });
        _mode.DropDownStyle = ComboBoxStyle.DropDownList;
        _mode.Width = 360;
        _mode.Items.AddRange([
            "保留当前像素并恢复有证据的原格式（推荐）",
            "仅紧凑整理活动载荷",
            "整档恢复到所选备份点",
            "修复旧工具索引平移并迁移 S241 到 Unit #545"
        ]);
        _mode.SelectedIndex = _preview.CanExecuteLegacyIndexShiftRecovery ? 3 : 0;
        _mode.SelectedIndexChanged += (_, _) => UpdateModeUi();
        actions.Controls.Add(_mode);
        _backup.DropDownStyle = ComboBoxStyle.DropDownList;
        _backup.Width = 330;
        actions.Controls.Add(_backup);
        _execute.Text = "执行所选修复";
        _execute.AutoSize = true;
        _execute.Click += (_, _) => ConfirmExecute();
        actions.Controls.Add(_execute);
        var close = new Button { Text = "关闭", AutoSize = true, DialogResult = DialogResult.Cancel };
        actions.Controls.Add(close);
        root.Controls.Add(actions, 0, 3);
        CancelButton = close;
    }

    private void LoadPreview()
    {
        foreach (var archive in _preview.Archives)
        {
            foreach (var candidate in archive.Candidates)
            {
                var row = _grid.Rows[_grid.Rows.Add(candidate.Selected, archive.FileName, candidate.ImageNumber,
                    $"{candidate.CurrentKind} → {candidate.RestoreKind}", $"{candidate.CurrentLength:N0} → {candidate.RestoredLength:N0}",
                    $"{candidate.Width}x{candidate.Height}", candidate.NearestPalettePixels.ToString("N0"),
                    candidate.BackupPath, candidate.Warning)];
                row.Tag = candidate;
            }
        }
        var compatible = _preview.Archives.SelectMany(archive => archive.CompatibleBackups).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        _backup.Items.AddRange(compatible);
        if (_backup.Items.Count > 0) _backup.SelectedIndex = 0;
        _diagnostic.Text = string.Join("\r\n\r\n", _preview.Archives.Select(archive =>
            $"[{archive.FileName}]\r\n路径：{archive.TargetPath}\r\n条目：{archive.EntryCount:N0}\r\n当前大小：{archive.CurrentSize:N0}\r\n活动载荷紧凑预计：{archive.CompactSize:N0}\r\n可清理孤立/间隙字节：{archive.OrphanBytes:N0}\r\n兼容备份：{archive.CompatibleBackups.Count}\r\n相关写回报告：{archive.EvidenceReports.Count}\r\n候选：{archive.Candidates.Count}\r\n{archive.Diagnostic}"));
        UpdateModeUi();
    }

    private void UpdateModeUi()
    {
        _backup.Visible = SelectedMode == RsArchiveRepairMode.RestoreWholeBackup;
        _grid.Enabled = SelectedMode == RsArchiveRepairMode.PreservePixelsAndRestoreFormat;
        _execute.Enabled = SelectedMode switch
        {
            RsArchiveRepairMode.PreservePixelsAndRestoreFormat => _preview.CandidateCount > 0,
            RsArchiveRepairMode.CompactOnly => _preview.TotalOrphanBytes > 0,
            RsArchiveRepairMode.RestoreWholeBackup => _backup.Items.Count > 0,
            RsArchiveRepairMode.LegacyIndexShiftRecovery => _preview.CanExecuteLegacyIndexShiftRecovery,
            _ => false
        };
    }

    private void ConfirmExecute()
    {
        foreach (DataGridViewRow row in _grid.Rows)
            if (row.Tag is RsArchiveRepairCandidate candidate)
                candidate.Selected = Convert.ToBoolean(row.Cells["Selected"].Value ?? false);
        var description = SelectedMode switch
        {
            RsArchiveRepairMode.CompactOnly => "仅重建活动载荷并清理孤立数据",
            RsArchiveRepairMode.RestoreWholeBackup => "用所选备份替换整档",
            RsArchiveRepairMode.LegacyIndexShiftRecovery =>
                "恢复三个 Unit 档案的完整 556 项索引、还原误改的 Unit #241，并把当前编辑结果迁移到 S241 的真实 Unit #545",
            _ => "保留当前编辑后的像素，并按备份证据恢复 RAW/BMP 原格式后紧凑重建"
        };
        if (MessageBox.Show(this,
                $"即将执行：{description}。\r\n\r\n程序会先完整备份当前 E5，再使用临时文件写入并复读校验。是否继续？",
                "确认修复/整理 R/S 档案", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
        DialogResult = DialogResult.OK;
        Close();
    }

    private void AddColumn(string name, string text, int width)
        => _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = name, HeaderText = text, Width = width, ReadOnly = true });
}
