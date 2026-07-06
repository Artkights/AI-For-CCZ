using System.ComponentModel;
using System.Data;
using System.Globalization;
using CCZModStudio.Core;
using CCZModStudio.Models;

namespace CCZModStudio;

public sealed class GlobalSettingsDialog : Form
{
    private readonly CczProject _project;
    private readonly IReadOnlyList<HexTableDefinition> _tables;
    private readonly GlobalSettingsService _service = new();
    private GlobalSettingsDocument _document;

    private readonly DataGridView _numericGrid = new();
    private readonly DataGridView _jobSeriesGrid = new();
    private readonly DataGridView _detailedJobGrid = new();
    private readonly TextBox _titleBox = new();
    private readonly Label _titleCapacityLabel = new();
    private readonly DataGridView _cmfCandidateGrid = new();
    private readonly DataGridView _evidenceGrid = new();
    private readonly TextBox _statusBox = new();
    private readonly Button _saveJobSeriesButton = new();
    private readonly Button _saveDetailedJobsButton = new();
    private readonly Button _saveTitleButton = new();
    private readonly Button _saveNumericButton = new();
    private readonly Button _discoverNumericButton = new();
    private readonly Button _reloadButton = new();

    public GlobalSettingsDialog(CczProject project, IReadOnlyList<HexTableDefinition> tables)
    {
        _project = project;
        _tables = tables;
        _document = _service.Load(project, tables);

        Text = "全局设定";
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = false;
        MaximizeBox = true;
        ShowInTaskbar = false;
        Width = 1040;
        Height = 720;
        Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Regular, GraphicsUnit.Point);

        BuildLayout();
        BindDocument();
    }

    public string LastSaveSummary { get; private set; } = string.Empty;

    private void BuildLayout()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            RowCount = 3,
            ColumnCount = 1,
            Padding = new Padding(8)
        };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 76));
        Controls.Add(root);

        var toolbar = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = true
        };
        _reloadButton.Text = "重新读取";
        _reloadButton.AutoSize = true;
        _reloadButton.Click += (_, _) => ReloadDocument();
        toolbar.Controls.Add(_reloadButton);
        toolbar.Controls.Add(new Label
        {
            Text = "全局数值参数目前只展示证据状态；名称和标题已接入安全写回。",
            AutoSize = true,
            Padding = new Padding(8, 7, 0, 0)
        });
        root.Controls.Add(toolbar, 0, 0);

        var tabs = new TabControl { Dock = DockStyle.Fill };
        tabs.TabPages.Add(BuildNumericPage());
        tabs.TabPages.Add(BuildCmfCandidatePage());
        tabs.TabPages.Add(BuildJobSeriesPage());
        tabs.TabPages.Add(BuildDetailedJobPage());
        tabs.TabPages.Add(BuildTitlePage());
        tabs.TabPages.Add(BuildEvidencePage());
        root.Controls.Add(tabs, 0, 1);

        _statusBox.Dock = DockStyle.Fill;
        _statusBox.Multiline = true;
        _statusBox.ReadOnly = true;
        _statusBox.ScrollBars = ScrollBars.Vertical;
        _statusBox.WordWrap = true;
        _statusBox.Text = "已读取全局设定。";
        root.Controls.Add(_statusBox, 0, 2);
    }

    private TabPage BuildNumericPage()
    {
        var page = new TabPage("全局参数");
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            RowCount = 2,
            ColumnCount = 1,
            Padding = new Padding(6)
        };
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var toolbar = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = true
        };
        _saveNumericButton.Text = "保存全局参数";
        _saveNumericButton.AutoSize = true;
        _saveNumericButton.Click += (_, _) => SaveNumericSettings();
        toolbar.Controls.Add(_saveNumericButton);

        _discoverNumericButton.Text = "生成定位证据";
        _discoverNumericButton.AutoSize = true;
        _discoverNumericButton.Click += (_, _) => PrepareNumericDiscovery();
        toolbar.Controls.Add(_discoverNumericButton);

        toolbar.Controls.Add(new Label
        {
            Text = "只有完成官方单字段 diff、复读和运行时验证的字段允许编辑；未验证字段保持灰显。",
            AutoSize = true,
            Padding = new Padding(8, 7, 0, 0)
        });
        layout.Controls.Add(toolbar, 0, 0);

        _numericGrid.Dock = DockStyle.Fill;
        _numericGrid.ReadOnly = false;
        _numericGrid.AllowUserToAddRows = false;
        _numericGrid.AllowUserToDeleteRows = false;
        _numericGrid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        _numericGrid.SelectionMode = DataGridViewSelectionMode.CellSelect;
        _numericGrid.CellValidating += ValidateNumericCell;
        _numericGrid.DataError += (_, e) => { e.ThrowException = false; };
        layout.Controls.Add(_numericGrid, 0, 1);
        page.Controls.Add(layout);
        return page;
    }

    private TabPage BuildCmfCandidatePage()
    {
        var page = new TabPage("CMF候选");
        _cmfCandidateGrid.Dock = DockStyle.Fill;
        _cmfCandidateGrid.ReadOnly = true;
        _cmfCandidateGrid.AllowUserToAddRows = false;
        _cmfCandidateGrid.AllowUserToDeleteRows = false;
        _cmfCandidateGrid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        _cmfCandidateGrid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        page.Controls.Add(_cmfCandidateGrid);
        return page;
    }

    private TabPage BuildJobSeriesPage()
    {
        var page = new TabPage("兵种名");
        page.Controls.Add(BuildEditableNamePage(_jobSeriesGrid, _saveJobSeriesButton, "保存兵种名", () => Save(saveJobSeries: true, saveDetailedJobs: false, saveGameTitle: false)));
        return page;
    }

    private TabPage BuildDetailedJobPage()
    {
        var page = new TabPage("职业名");
        page.Controls.Add(BuildEditableNamePage(_detailedJobGrid, _saveDetailedJobsButton, "保存职业名", () => Save(saveJobSeries: false, saveDetailedJobs: true, saveGameTitle: false)));
        return page;
    }

    private Control BuildEditableNamePage(DataGridView grid, Button saveButton, string saveText, Action saveAction)
    {
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            RowCount = 2,
            ColumnCount = 1,
            Padding = new Padding(6)
        };
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var toolbar = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = true
        };
        saveButton.Text = saveText;
        saveButton.AutoSize = true;
        saveButton.Click += (_, _) => saveAction();
        toolbar.Controls.Add(saveButton);
        toolbar.Controls.Add(new Label
        {
            Text = "只编辑“名称”列；保存前自动备份，保存后复读校验。",
            AutoSize = true,
            Padding = new Padding(8, 7, 0, 0)
        });
        layout.Controls.Add(toolbar, 0, 0);

        grid.Dock = DockStyle.Fill;
        grid.AllowUserToAddRows = false;
        grid.AllowUserToDeleteRows = false;
        grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.DisplayedCells;
        grid.SelectionMode = DataGridViewSelectionMode.CellSelect;
        grid.CellValidating += ValidateNameCell;
        layout.Controls.Add(grid, 0, 1);
        return layout;
    }

    private TabPage BuildTitlePage()
    {
        var page = new TabPage("游戏标题");
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            RowCount = 5,
            ColumnCount = 1,
            Padding = new Padding(12)
        };
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        layout.Controls.Add(new Label
        {
            Text = "游戏标题",
            AutoSize = true,
            Font = new Font(Font, FontStyle.Bold)
        }, 0, 0);

        _titleBox.Width = 420;
        _titleBox.Anchor = AnchorStyles.Left | AnchorStyles.Top;
        _titleBox.TextChanged += (_, _) => RefreshTitleCapacityLabel();
        layout.Controls.Add(_titleBox, 0, 1);

        _titleCapacityLabel.AutoSize = true;
        _titleCapacityLabel.Padding = new Padding(0, 6, 0, 6);
        layout.Controls.Add(_titleCapacityLabel, 0, 2);

        _saveTitleButton.Text = "保存游戏标题";
        _saveTitleButton.AutoSize = true;
        _saveTitleButton.Click += (_, _) => Save(saveJobSeries: false, saveDetailedJobs: false, saveGameTitle: true);
        layout.Controls.Add(_saveTitleButton, 0, 3);

        var info = new TextBox
        {
            Dock = DockStyle.Fill,
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Vertical,
            WordWrap = true,
            Text = "标题保存到 Ekd5.exe 的固定 GBK 文本区。超过容量会阻止保存。"
        };
        layout.Controls.Add(info, 0, 4);
        page.Controls.Add(layout);
        return page;
    }

    private TabPage BuildEvidencePage()
    {
        var page = new TabPage("证据");
        _evidenceGrid.Dock = DockStyle.Fill;
        _evidenceGrid.ReadOnly = true;
        _evidenceGrid.AllowUserToAddRows = false;
        _evidenceGrid.AllowUserToDeleteRows = false;
        _evidenceGrid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        _evidenceGrid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        page.Controls.Add(_evidenceGrid);
        return page;
    }

    private void BindDocument()
    {
        _numericGrid.DataSource = ToNumericGrid(_document.NumericSettings);
        _jobSeriesGrid.DataSource = _document.JobSeriesNames;
        _detailedJobGrid.DataSource = _document.DetailedJobNames;
        _titleBox.Text = _document.GameTitle.Title;
        _cmfCandidateGrid.DataSource = ToCmfCandidateGrid(_document.CmfCandidates);
        _evidenceGrid.DataSource = ToEvidenceGrid(_document.Evidence);
        ConfigureNumericGrid();
        ConfigureNameGrid(_jobSeriesGrid);
        ConfigureNameGrid(_detailedJobGrid);
        RefreshTitleCapacityLabel();
    }

    private void ReloadDocument()
    {
        try
        {
            Cursor = Cursors.WaitCursor;
            _document = _service.Load(_project, _tables);
            BindDocument();
            _statusBox.Text = "已重新读取全局设定。";
        }
        catch (Exception ex)
        {
            _statusBox.Text = ex.ToString();
            MessageBox.Show(this, ex.Message, "读取全局设定失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            Cursor = Cursors.Default;
        }
    }

    private void Save(bool saveJobSeries, bool saveDetailedJobs, bool saveGameTitle)
    {
        try
        {
            Validate();
            if (saveGameTitle)
            {
                _document.GameTitle.Title = _titleBox.Text;
                _ = EncodingService.EncodeFixedString(_document.GameTitle.Title, _document.GameTitle.CapacityBytes);
            }

            var result = _service.Save(_project, _tables, _document, saveJobSeries, saveDetailedJobs, saveGameTitle);
            LastSaveSummary = result.Summary;
            _statusBox.Text =
                $"{result.Summary}\r\n变化字节：{result.ChangedBytes}\r\n" +
                $"备份：{string.Join("；", result.BackupPaths.Select(Path.GetFileName))}\r\n" +
                $"报告：{string.Join("；", result.ReportJsonPaths.Select(Path.GetFileName))}";
            MessageBox.Show(this, "全局设定保存完成并已复读。\r\n" + result.Summary, "保存完成", MessageBoxButtons.OK, MessageBoxIcon.Information);
            _document = _service.Load(_project, _tables);
            BindDocument();
        }
        catch (Exception ex)
        {
            _statusBox.Text = ex.ToString();
            MessageBox.Show(this, ex.Message, "保存全局设定失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void SaveNumericSettings()
    {
        try
        {
            Validate();
            var updates = CollectNumericUpdates();
            var result = _service.SaveNumericSettings(_project, _document, updates);
            LastSaveSummary = result.Summary;
            _statusBox.Text = BuildNumericSaveStatus(result);
            MessageBox.Show(this, result.Summary, "保存全局参数", MessageBoxButtons.OK, MessageBoxIcon.Information);
            _document = _service.Load(_project, _tables);
            BindDocument();
        }
        catch (Exception ex)
        {
            _statusBox.Text = ex.ToString();
            MessageBox.Show(this, ex.Message, "保存全局参数失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void PrepareNumericDiscovery()
    {
        try
        {
            Cursor = Cursors.WaitCursor;
            var report = new GlobalNumericDiscoveryService().PrepareManualDiffExperiment(_project, _service.GetNumericDefinitions());
            var lowRisk = new GlobalNumericDiscoveryService().PrepareLowRiskManualDiffExperiment(_project);
            var query = new GlobalNumericQueryService().Query(_project, _service.GetNumericDefinitions());
            _statusBox.Text =
                "已生成全局数字项定位证据目录。\r\n" +
                "状态：" + report.Status + "\r\n" +
                "报告：" + report.ReportPath + "\r\n" +
                "低风险批量实验：" + lowRisk.ReportPath + "\r\n" +
                "静态查询：" + query.ReportPath + "\r\n" +
                "官方工具副本：" + report.OfficialToolRoot + "\r\n" +
                "低风险 noop 工具：" + lowRisk.NoopOfficialToolRoot + "\r\n" +
                "测试项目副本：" + report.OfficialCaseRoot;
            MessageBox.Show(this, "已生成定位证据报告：\r\n" + report.ReportPath + "\r\n\r\n低风险批量实验：\r\n" + lowRisk.ReportPath + "\r\n\r\n静态查询报告：\r\n" + query.ReportPath, "全局参数定位", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            _statusBox.Text = ex.ToString();
            MessageBox.Show(this, ex.Message, "生成定位证据失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            Cursor = Cursors.Default;
        }
    }

    private Dictionary<string, int> CollectNumericUpdates()
    {
        var updates = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        if (_numericGrid.DataSource is not DataTable table) return updates;
        foreach (DataRow row in table.Rows)
        {
            var canEdit = row.Field<bool>("CanEdit");
            if (!canEdit) continue;

            var key = Convert.ToString(row["Key"], CultureInfo.InvariantCulture) ?? string.Empty;
            var current = Convert.ToString(row["CurrentValue"], CultureInfo.InvariantCulture) ?? string.Empty;
            var valueText = Convert.ToString(row["NewValue"], CultureInfo.InvariantCulture) ?? string.Empty;
            if (string.IsNullOrWhiteSpace(valueText) || string.Equals(valueText, current, StringComparison.Ordinal))
            {
                continue;
            }

            if (!int.TryParse(valueText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
            {
                throw new InvalidOperationException($"全局参数 {key} 的新值不是整数：{valueText}");
            }

            updates[key] = value;
        }

        return updates;
    }

    private static string BuildNumericSaveStatus(GlobalSettingsSaveResult result)
        => result.Summary + "\r\n" +
           "ChangedBytes=" + result.ChangedBytes.ToString(CultureInfo.InvariantCulture) + "\r\n" +
           "Backups=" + string.Join("; ", result.BackupPaths.Select(Path.GetFileName)) + "\r\n" +
           "Reports=" + string.Join("; ", result.ReportJsonPaths.Select(Path.GetFileName));

    private void ConfigureNumericGrid()
    {
        _saveNumericButton.Enabled = _document.NumericSettings.Any(setting => setting.CanEdit);
        if (_numericGrid.Columns.Count == 0) return;

        foreach (DataGridViewColumn column in _numericGrid.Columns)
        {
            column.ReadOnly = column.DataPropertyName != "NewValue";
            if (column.DataPropertyName == "Detail") column.AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
            if (column.DataPropertyName is "Key" or "CanEdit" or "ByteLength") column.MinimumWidth = 72;
            if (column.DataPropertyName == "NewValue") column.HeaderText = "新值";
        }

        foreach (DataGridViewRow row in _numericGrid.Rows)
        {
            var canEdit = row.Cells["CanEdit"].Value is bool editable && editable;
            row.ReadOnly = !canEdit;
            row.DefaultCellStyle.ForeColor = canEdit ? SystemColors.ControlText : SystemColors.GrayText;
            row.DefaultCellStyle.BackColor = canEdit ? SystemColors.Window : SystemColors.Control;
            if (_numericGrid.Columns.Contains("NewValue"))
            {
                row.Cells["NewValue"].ReadOnly = !canEdit;
            }
        }
    }

    private void ValidateNumericCell(object? sender, DataGridViewCellValidatingEventArgs e)
    {
        if (sender is not DataGridView grid || e.RowIndex < 0 || e.ColumnIndex < 0) return;
        var column = grid.Columns[e.ColumnIndex];
        if (column.DataPropertyName != "NewValue") return;

        var row = grid.Rows[e.RowIndex];
        var canEdit = row.Cells["CanEdit"].Value is bool editable && editable;
        if (!canEdit) return;

        var valueText = Convert.ToString(e.FormattedValue, CultureInfo.InvariantCulture) ?? string.Empty;
        if (string.IsNullOrWhiteSpace(valueText)) return;
        if (!int.TryParse(valueText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
        {
            e.Cancel = true;
            row.ErrorText = "新值必须是整数。";
            _statusBox.Text = row.ErrorText;
            return;
        }

        var min = Convert.ToInt32(row.Cells["MinValue"].Value, CultureInfo.InvariantCulture);
        var max = Convert.ToInt32(row.Cells["MaxValue"].Value, CultureInfo.InvariantCulture);
        if (min != max && (value < min || value > max))
        {
            e.Cancel = true;
            row.ErrorText = $"新值超出范围：{value}，允许 {min}..{max}。";
            _statusBox.Text = row.ErrorText;
            return;
        }

        row.ErrorText = string.Empty;
    }

    private void ConfigureNameGrid(DataGridView grid)
    {
        foreach (DataGridViewColumn column in grid.Columns)
        {
            column.ReadOnly = column.DataPropertyName != "名称";
            if (column.DataPropertyName == "ID") column.MinimumWidth = 64;
            if (column.DataPropertyName == "名称") column.AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
        }
    }

    private void ValidateNameCell(object? sender, DataGridViewCellValidatingEventArgs e)
    {
        if (sender is not DataGridView grid || e.RowIndex < 0 || e.ColumnIndex < 0) return;
        var column = grid.Columns[e.ColumnIndex];
        if (column.DataPropertyName != "名称") return;

        try
        {
            var value = Convert.ToString(e.FormattedValue, CultureInfo.InvariantCulture) ?? string.Empty;
            var capacity = GetNameCapacity(grid);
            _ = EncodingService.EncodeFixedString(value, capacity);
        }
        catch (Exception ex)
        {
            e.Cancel = true;
            grid.Rows[e.RowIndex].ErrorText = ex.Message;
            _statusBox.Text = ex.Message;
        }
    }

    private int GetNameCapacity(DataGridView grid)
    {
        var tableName = grid == _jobSeriesGrid ? "6.5-3 兵种系" : "6.5-4 详细兵种";
        var table = _tables.First(t => t.TableName.Equals(tableName, StringComparison.Ordinal));
        var field = table.Fields.First(f => f.ColumnName == "名称");
        return field.Size;
    }

    private void RefreshTitleCapacityLabel()
    {
        if (_document == null) return;
        var count = EncodingService.GetGbkByteCount(_titleBox.Text);
        _titleCapacityLabel.Text = $"GBK 容量：{count}/{_document.GameTitle.CapacityBytes} 字节    位置：{_document.GameTitle.FileName}@{HexDisplayFormatter.FormatOffset(_document.GameTitle.Offset)}";
        _titleCapacityLabel.ForeColor = count > _document.GameTitle.CapacityBytes ? Color.DarkRed : SystemColors.ControlText;
    }

    private static DataTable ToNumericGrid(IReadOnlyList<GlobalNumericSetting> settings)
    {
        var table = new DataTable("全局参数");
        table.Columns.Add("Key", typeof(string));
        table.Columns.Add("CanEdit", typeof(bool));
        table.Columns.Add("项目", typeof(string));
        table.Columns.Add("CurrentValue", typeof(string));
        table.Columns.Add("NewValue", typeof(string));
        table.Columns.Add("默认/截图值", typeof(string));
        table.Columns.Add("目标文件", typeof(string));
        table.Columns.Add("偏移", typeof(string));
        table.Columns.Add("运行时VA", typeof(string));
        table.Columns.Add("ByteLength", typeof(int));
        table.Columns.Add("ValueKind", typeof(string));
        table.Columns.Add("MinValue", typeof(int));
        table.Columns.Add("MaxValue", typeof(int));
        table.Columns.Add("状态", typeof(string));
        table.Columns.Add("Oracle覆盖", typeof(string));
        table.Columns.Add("Detail", typeof(string));
        foreach (var setting in settings)
        {
            table.Rows.Add(
                setting.Key,
                setting.CanEdit,
                setting.DisplayName,
                setting.CurrentValueText,
                string.Empty,
                setting.SuggestedDefaultText,
                setting.TargetFileName,
                FormatNumericTargets(setting),
                setting.RuntimeAddress == 0 ? string.Empty : "0x" + setting.RuntimeAddress.ToString("X", CultureInfo.InvariantCulture),
                setting.ByteLength,
                setting.ValueKind.ToString(),
                setting.MinValue,
                setting.MaxValue,
                setting.Status,
                setting.OracleCoverage,
                setting.Detail);
        }

        return table;
    }

    private static string FormatNumericTargets(GlobalNumericSetting setting)
    {
        if (setting.WriteTargets.Count == 0)
        {
            return setting.Offset == 0 ? string.Empty : HexDisplayFormatter.FormatOffset(setting.Offset);
        }

        var offsets = setting.WriteTargets
            .Select(target => HexDisplayFormatter.FormatOffset(target.FileOffset))
            .ToArray();
        return offsets.Length == 1
            ? offsets[0]
            : $"{offsets[0]} 等 {offsets.Length} 处";
    }

    private static DataTable ToCmfCandidateGrid(IReadOnlyList<CmfFeatureCandidate> candidates)
    {
        var table = new DataTable("CMF候选");
        table.Columns.Add("功能候选", typeof(string));
        table.Columns.Add("类别", typeof(string));
        table.Columns.Add("版本范围", typeof(string));
        table.Columns.Add("来源CMF", typeof(string));
        table.Columns.Add("目标模块", typeof(string));
        table.Columns.Add("转换状态", typeof(string));
        table.Columns.Add("写入条件", typeof(string));
        foreach (var candidate in candidates)
        {
            table.Rows.Add(
                candidate.Name,
                candidate.Category,
                candidate.VersionScope,
                candidate.SourceCmfRelativePath,
                candidate.TargetSubsystem,
                candidate.ConversionStatus,
                candidate.WritePolicy);
        }

        return table;
    }

    private static DataTable ToEvidenceGrid(IReadOnlyList<GlobalSettingEvidence> evidence)
    {
        var table = new DataTable("证据");
        table.Columns.Add("区域", typeof(string));
        table.Columns.Add("项目", typeof(string));
        table.Columns.Add("目标", typeof(string));
        table.Columns.Add("偏移", typeof(string));
        table.Columns.Add("长度", typeof(string));
        table.Columns.Add("状态", typeof(string));
        table.Columns.Add("来源", typeof(string));
        table.Columns.Add("备注", typeof(string));
        foreach (var item in evidence)
        {
            table.Rows.Add(item.Area, item.Item, item.Target, item.OffsetText, item.LengthText, item.Status, item.Source, item.Note);
        }

        return table;
    }
}
