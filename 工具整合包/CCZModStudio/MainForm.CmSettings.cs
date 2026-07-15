using CCZModStudio.Core;
using CCZModStudio.Models;
using System.Data;
using System.Globalization;

namespace CCZModStudio;

public sealed partial class MainForm
{
    private const string GlobalNumericKeyColumn = "Key";
    private const string GlobalNumericNameColumn = "名称";
    private const string GlobalNumericCurrentValueColumn = "当前值";
    private const string GlobalNumericNewValueColumn = "新值";
    private const string GlobalNumericMinValueColumn = "MinValue";
    private const string GlobalNumericMaxValueColumn = "MaxValue";
    private const string CmSettingSourceKindColumn = "SourceKind";
    private const string CmSettingSourceKindCm = "CmSetting";
    private const string CmSettingSourceKindGlobalNumeric = "GlobalNumeric";
    private const string CmSettingOriginalValueColumn = "OriginalValue";

    private static readonly string[] EquipmentExperienceGlobalNumericKeys =
    [
        "EquipmentLevelLimitNormal",
        "EquipmentLevelLimitSpecial",
        "EquipmentLevelRaiseNormal",
        "EquipmentLevelRaiseSpecial"
    ];

    private sealed class CmSettingsPageSession
    {
        public string LoadedGameRoot { get; set; } = string.Empty;
        public GlobalSettingsService GlobalSettingsService { get; } = new();
        public GlobalSettingsDocument? GlobalDocument { get; set; }
        public required Dictionary<string, DataGridView> SettingGrids { get; init; }
        public required DataGridView GlobalNumericGrid { get; init; }
        public required TextBox TitleBox { get; init; }
        public required Label TitleCapacityLabel { get; init; }
        public required Button TitleSaveButton { get; init; }
        public required DataGridView TerrainGrid { get; init; }
        public required TabControl Tabs { get; init; }
        public required TabPage TerrainPage { get; init; }
        public required TextBox StatusBox { get; init; }
    }

    private CmSettingsPageSession? _cmSettingsPageSession;

    private TabPage BuildCmSettingsPage()
    {
        var page = new TabPage("全局设定");
        var settingGrids = new Dictionary<string, DataGridView>(StringComparer.OrdinalIgnoreCase);

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            RowCount = 3,
            ColumnCount = 1,
            Padding = new Padding(6)
        };
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 54));
        page.Controls.Add(layout);

        var toolbar = CreateToolbarRow();
        var reloadButton = new Button { Text = "重新读取", AutoSize = false, Width = 88, Height = 28 };
        var saveCmButton = new Button { Text = "保存参数", AutoSize = false, Width = 104, Height = 28 };
        toolbar.Controls.Add(reloadButton);
        toolbar.Controls.Add(saveCmButton);
        layout.Controls.Add(toolbar, 0, 0);

        var statusBox = new TextBox
        {
            Dock = DockStyle.Fill,
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Vertical,
            WordWrap = true,
            Text = "请选择项目后读取全局设定。"
        };

        var tabs = new TabControl { Dock = DockStyle.Fill };
        layout.Controls.Add(tabs, 0, 1);
        layout.Controls.Add(statusBox, 0, 2);

        var globalNumericGrid = CreateGlobalNumericGrid();
        var titleBox = new TextBox { Width = 420, Anchor = AnchorStyles.Left | AnchorStyles.Top };
        var titleCapacityLabel = new Label { AutoSize = true, Padding = new Padding(0, 6, 0, 6) };
        var titlePage = CreateGlobalTitlePage(titleBox, titleCapacityLabel, SaveCmGameTitle, out var titleSaveButton);
        var growthGrid = CreateCmSettingGrid(useCheckBoxValue: false);
        var equipmentGrid = CreateMixedCmSettingGrid();
        var battleGrid = CreateCmSettingGrid(useCheckBoxValue: false);
        var abnormalGrid = CreateCmSettingGrid(useCheckBoxValue: false);
        var terrainGrid = CreateCmTerrainGrid();

        settingGrids["growth"] = growthGrid;
        settingGrids["equipment-exp"] = equipmentGrid;
        settingGrids["battle-formula"] = battleGrid;
        settingGrids["abnormal-state"] = abnormalGrid;

        var terrainPage = CreateGridTabPage("地形策略", terrainGrid);
        var session = new CmSettingsPageSession
        {
            SettingGrids = settingGrids,
            GlobalNumericGrid = globalNumericGrid,
            TitleBox = titleBox,
            TitleCapacityLabel = titleCapacityLabel,
            TitleSaveButton = titleSaveButton,
            TerrainGrid = terrainGrid,
            Tabs = tabs,
            TerrainPage = terrainPage,
            StatusBox = statusBox
        };
        _cmSettingsPageSession = session;

        tabs.TabPages.Add(CreateGlobalNumericPage(globalNumericGrid, SaveCmGlobalNumericSettings));
        tabs.TabPages.Add(titlePage);
        tabs.TabPages.Add(CreateGridTabPage("成长", growthGrid));
        tabs.TabPages.Add(CreateGridTabPage("装备经验", equipmentGrid));
        tabs.TabPages.Add(CreateGridTabPage("战斗公式", battleGrid));
        tabs.TabPages.Add(CreateGridTabPage("异常状态", abnormalGrid));
        tabs.TabPages.Add(terrainPage);

        titleBox.TextChanged += (_, _) => RefreshGlobalTitleCapacityLabel(session.GlobalDocument, titleBox, titleCapacityLabel);
        reloadButton.Click += (_, _) =>
        {
            if (ConfirmPageReloadIfUnsaved("全局设定")) ReloadCmSettingsPage();
        };
        saveCmButton.Click += (_, _) => SaveCmParameters();
        page.Enter += (_, _) =>
        {
            if (_project != null && !session.LoadedGameRoot.Equals(_project.GameRoot, StringComparison.OrdinalIgnoreCase))
            {
                ReloadCmSettingsPage();
            }
        };

        return page;
    }

    private void ReloadCmSettingsPage(bool showErrorMessage = true)
    {
        var session = _cmSettingsPageSession;
        if (session == null) return;
        if (_project == null)
        {
            session.StatusBox.Text = "请选择项目后读取全局设定。";
            return;
        }

        try
        {
            if (_tables.Count == 0)
            {
                ReloadCurrentProject();
                if (_tables.Count == 0) return;
            }

            LoadCmGlobalSection(session);
            var document = _cmSettingsService.Load(_project);
            foreach (var group in document.Groups)
            {
                if (!session.SettingGrids.TryGetValue(group.GroupKey, out var grid)) continue;
                grid.DataSource = group.GroupKey.Equals("equipment-exp", StringComparison.OrdinalIgnoreCase)
                    ? BuildEquipmentExperienceTable(session.GlobalDocument, group)
                    : BuildCmSettingTable(group);
            }

            session.TerrainGrid.DataSource = BuildCmTerrainTable(document.TerrainStrategyRows);
            SetTabPageVisible(session.Tabs, session.TerrainPage, document.TerrainStrategyRows.Count > 0);
            session.LoadedGameRoot = _project.GameRoot;
            session.StatusBox.Text = "已读取全局设定。";
            SetStatus("已读取全局设定");
        }
        catch (Exception ex)
        {
            session.StatusBox.Text = ex.Message;
            if (showErrorMessage)
            {
                MessageBox.Show(this, ex.Message, "读取全局设定失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }

    private void LoadCmGlobalSection(CmSettingsPageSession session)
    {
        if (_project == null) return;
        session.GlobalDocument = session.GlobalSettingsService.Load(_project, _tables);
        session.GlobalNumericGrid.DataSource = BuildGlobalNumericTable(session.GlobalDocument.NumericSettings);
        session.TitleBox.Text = session.GlobalDocument.GameTitle.Title;
        session.TitleBox.ReadOnly = !session.GlobalDocument.GameTitle.CanEdit;
        session.TitleSaveButton.Enabled = session.GlobalDocument.GameTitle.CanEdit;
        RefreshGlobalTitleCapacityLabel(session.GlobalDocument, session.TitleBox, session.TitleCapacityLabel);
    }

    private bool EnsureCmGlobalDocument(CmSettingsPageSession session)
    {
        if (session.GlobalDocument != null) return true;
        ReloadCmSettingsPage();
        return session.GlobalDocument != null;
    }

    private void SaveCmGlobalNumericSettings()
    {
        var session = _cmSettingsPageSession;
        if (_project == null || session == null)
        {
            MessageBox.Show(this, "请先打开 MOD 项目目录。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        if (!EnsureCmGlobalDocument(session)) return;

        try
        {
            CommitCmSettingsEditors();
            var updates = session.GlobalNumericGrid.DataSource is DataTable table
                ? CollectGlobalNumericUpdates(table)
                : new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var result = session.GlobalSettingsService.SaveNumericSettings(_project, session.GlobalDocument!, updates);
            LoadCmGlobalSection(session);
            session.StatusBox.Text = BuildGlobalSettingsSaveStatus(result);
            SetStatus(result.Summary);
            MessageBox.Show(this, result.Summary, "保存全局参数", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            session.StatusBox.Text = ex.Message;
            MessageBox.Show(this, ex.Message, "保存全局参数失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void SaveCmGameTitle()
    {
        var session = _cmSettingsPageSession;
        if (_project == null || session == null)
        {
            MessageBox.Show(this, "请先打开 MOD 项目目录。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        if (!EnsureCmGlobalDocument(session)) return;

        var originalTitle = session.GlobalDocument!.GameTitle.Title;
        try
        {
            session.GlobalSettingsService.ValidateGameTitleUpdate(session.GlobalDocument.GameTitle, session.TitleBox.Text);
            session.GlobalDocument.GameTitle.Title = session.TitleBox.Text;
            var result = session.GlobalSettingsService.Save(
                _project,
                _tables,
                session.GlobalDocument,
                saveJobSeries: false,
                saveDetailedJobs: false,
                saveGameTitle: true);
            LoadCmGlobalSection(session);
            session.StatusBox.Text = BuildGlobalSettingsSaveStatus(result);
            SetStatus(result.Summary);
            MessageBox.Show(this, result.Summary, "保存游戏标题", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            session.GlobalDocument.GameTitle.Title = originalTitle;
            session.StatusBox.Text = ex.Message;
            MessageBox.Show(this, ex.Message, "保存游戏标题失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void SaveCmParameters()
    {
        var session = _cmSettingsPageSession;
        if (_project == null || session == null)
        {
            MessageBox.Show(this, "请先打开 MOD 项目目录。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        try
        {
            CommitCmSettingsEditors();
            if (!EnsureCmGlobalDocument(session)) return;
            var update = BuildCmSettingsUpdate(session);
            var cmPreview = _cmSettingsService.Preview(_project, update);
            var globalNumericUpdates = CollectCmEquipmentExperienceGlobalNumericUpdates(session);

            CmSettingsSaveResult? cmResult = null;
            GlobalSettingsSaveResult? globalResult = null;
            if (cmPreview.Count > 0) cmResult = _cmSettingsService.Save(_project, update);
            if (globalNumericUpdates.Count > 0)
            {
                globalResult = session.GlobalSettingsService.SaveNumericSettings(_project, session.GlobalDocument!, globalNumericUpdates);
            }

            var summary = BuildParameterSaveStatus(cmResult, globalResult);
            ReloadCmSettingsPage();
            session.StatusBox.Text = summary;
            SetStatus(summary);
            MessageBox.Show(this, summary, "保存参数", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            session.StatusBox.Text = ex.Message;
            MessageBox.Show(this, ex.Message, "保存参数失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void CommitCmSettingsEditors()
    {
        var session = _cmSettingsPageSession;
        if (session == null) return;
        foreach (var grid in session.SettingGrids.Values.Append(session.GlobalNumericGrid).Append(session.TerrainGrid))
        {
            if (grid.IsCurrentCellDirty) grid.CommitEdit(DataGridViewDataErrorContexts.Commit);
            grid.EndEdit();
            if (grid.DataSource is BindingSource bindingSource) bindingSource.EndEdit();
            if (grid.DataSource != null && grid.BindingContext?[grid.DataSource] is CurrencyManager manager) manager.EndCurrentEdit();
        }
    }

    private static CmSettingsUpdate BuildCmSettingsUpdate(CmSettingsPageSession session)
    {
        var update = new CmSettingsUpdate();
        foreach (var grid in session.SettingGrids.Values)
        {
            if (grid.DataSource is not DataTable table) continue;
            foreach (DataRow row in table.Rows)
            {
                if (IsGlobalNumericSettingRow(row)) continue;
                var key = Convert.ToString(row["Key"], CultureInfo.InvariantCulture) ?? string.Empty;
                if (string.IsNullOrWhiteSpace(key)) continue;
                var value = row["Value"] is bool boolValue
                    ? (boolValue ? "true" : "false")
                    : Convert.ToString(row["Value"], CultureInfo.InvariantCulture) ?? string.Empty;
                update.Values[key] = value;
            }
        }

        if (session.TerrainGrid.DataSource is DataTable terrainTable)
        {
            foreach (DataRow row in terrainTable.Rows)
            {
                var terrainId = Convert.ToInt32(row["TerrainId"], CultureInfo.InvariantCulture);
                update.TerrainStrategy[terrainId] = new CmTerrainStrategyUpdate
                {
                    Fire = Convert.ToBoolean(row["火"], CultureInfo.InvariantCulture),
                    Water = Convert.ToBoolean(row["水"], CultureInfo.InvariantCulture),
                    Wind = Convert.ToBoolean(row["风"], CultureInfo.InvariantCulture),
                    Earth = Convert.ToBoolean(row["地"], CultureInfo.InvariantCulture)
                };
            }
        }

        return update;
    }

    private static Dictionary<string, int> CollectCmEquipmentExperienceGlobalNumericUpdates(CmSettingsPageSession session)
        => session.SettingGrids.TryGetValue("equipment-exp", out var equipmentGrid) && equipmentGrid.DataSource is DataTable equipmentTable
            ? CollectEquipmentExperienceGlobalNumericUpdates(equipmentTable)
            : new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

    private static Dictionary<string, int> CollectAllCmGlobalNumericUpdates(CmSettingsPageSession session)
    {
        var updates = session.GlobalNumericGrid.DataSource is DataTable globalTable
            ? CollectGlobalNumericUpdates(globalTable)
            : new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in CollectCmEquipmentExperienceGlobalNumericUpdates(session)) updates[pair.Key] = pair.Value;
        return updates;
    }

    private bool IsCmSettingsDirty(out string summary)
    {
        summary = string.Empty;
        var session = _cmSettingsPageSession;
        if (_project == null || session?.GlobalDocument == null ||
            !session.LoadedGameRoot.Equals(_project.GameRoot, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        try
        {
            CommitCmSettingsEditors();
            var parts = new List<string>();
            var globalUpdates = CollectAllCmGlobalNumericUpdates(session);
            if (globalUpdates.Count > 0) parts.Add($"{globalUpdates.Count} 个全局参数");
            if (!string.Equals(session.TitleBox.Text, session.GlobalDocument.GameTitle.Title, StringComparison.Ordinal))
            {
                parts.Add("游戏标题");
            }

            var cmChanges = _cmSettingsService.Preview(_project, BuildCmSettingsUpdate(session));
            if (cmChanges.Count > 0) parts.Add($"{cmChanges.Count} 个 CM 参数");
            summary = string.Join("、", parts);
            return parts.Count > 0;
        }
        catch (Exception ex)
        {
            summary = "存在未提交或无效编辑：" + ex.Message;
            return true;
        }
    }

    private Task SaveCmSettingsSilentlyAsync()
    {
        var session = _cmSettingsPageSession;
        if (_project == null || session?.GlobalDocument == null) return Task.CompletedTask;

        CommitCmSettingsEditors();
        var globalUpdates = CollectAllCmGlobalNumericUpdates(session);
        var cmUpdate = BuildCmSettingsUpdate(session);
        var cmChanges = _cmSettingsService.Preview(_project, cmUpdate);
        var summaries = new List<string>();

        if (!string.Equals(session.TitleBox.Text, session.GlobalDocument.GameTitle.Title, StringComparison.Ordinal))
        {
            var originalTitle = session.GlobalDocument.GameTitle.Title;
            try
            {
                session.GlobalSettingsService.ValidateGameTitleUpdate(session.GlobalDocument.GameTitle, session.TitleBox.Text);
                session.GlobalDocument.GameTitle.Title = session.TitleBox.Text;
                var result = session.GlobalSettingsService.Save(
                    _project,
                    _tables,
                    session.GlobalDocument,
                    saveJobSeries: false,
                    saveDetailedJobs: false,
                    saveGameTitle: true);
                summaries.Add(result.Summary);
            }
            catch
            {
                session.GlobalDocument.GameTitle.Title = originalTitle;
                throw;
            }
        }

        if (globalUpdates.Count > 0)
        {
            summaries.Add(session.GlobalSettingsService.SaveNumericSettings(_project, session.GlobalDocument, globalUpdates).Summary);
        }
        if (cmChanges.Count > 0) summaries.Add(_cmSettingsService.Save(_project, cmUpdate).Summary);

        ReloadCmSettingsPage(showErrorMessage: false);
        var status = summaries.Count == 0 ? "没有检测到需要保存的全局设定。" : string.Join("\r\n", summaries);
        session.StatusBox.Text = status;
        SetStatus(status);
        return Task.CompletedTask;
    }

    private static TabPage CreateGlobalNumericPage(DataGridView grid, Action saveAction)
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

        var toolbar = CreateToolbarRow();
        var saveButton = new Button { Text = "保存全局参数", AutoSize = false, Width = 112, Height = 28 };
        saveButton.Click += (_, _) => saveAction();
        toolbar.Controls.Add(saveButton);
        layout.Controls.Add(toolbar, 0, 0);
        layout.Controls.Add(grid, 0, 1);
        page.Controls.Add(layout);
        return page;
    }

    private static TabPage CreateGlobalTitlePage(TextBox titleBox, Label capacityLabel, Action saveAction, out Button saveButton)
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
            Font = new Font(SystemFonts.DefaultFont, FontStyle.Bold)
        }, 0, 0);
        layout.Controls.Add(titleBox, 0, 1);
        layout.Controls.Add(capacityLabel, 0, 2);

        saveButton = new Button { Text = "保存游戏标题", AutoSize = true };
        saveButton.Click += (_, _) => saveAction();
        layout.Controls.Add(saveButton, 0, 3);

        var info = new TextBox
        {
            Dock = DockStyle.Fill,
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Vertical,
            WordWrap = true,
            Text = "游戏标题按固定 GBK 容量写入。超出容量会阻止保存。"
        };
        layout.Controls.Add(info, 0, 4);
        page.Controls.Add(layout);
        return page;
    }

    private static DataGridView CreateGlobalNumericGrid()
    {
        var grid = new DataGridView
        {
            Dock = DockStyle.Fill,
            AutoGenerateColumns = false,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
            SelectionMode = DataGridViewSelectionMode.CellSelect,
            MultiSelect = false,
            RowHeadersVisible = false
        };

        grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            DataPropertyName = GlobalNumericNameColumn,
            HeaderText = "名称",
            ReadOnly = true,
            FillWeight = 50
        });
        grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            DataPropertyName = GlobalNumericCurrentValueColumn,
            HeaderText = "当前值",
            ReadOnly = true,
            FillWeight = 25
        });
        grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            DataPropertyName = GlobalNumericNewValueColumn,
            HeaderText = "新值",
            FillWeight = 25
        });

        grid.DataError += (_, e) => { e.ThrowException = false; };
        return grid;
    }

    private static DataTable BuildGlobalNumericTable(IEnumerable<GlobalNumericSetting> settings)
    {
        var table = new DataTable("全局参数");
        table.Columns.Add(GlobalNumericKeyColumn, typeof(string));
        table.Columns.Add(GlobalNumericNameColumn, typeof(string));
        table.Columns.Add(GlobalNumericCurrentValueColumn, typeof(string));
        table.Columns.Add(GlobalNumericNewValueColumn, typeof(string));
        table.Columns.Add(GlobalNumericMinValueColumn, typeof(int));
        table.Columns.Add(GlobalNumericMaxValueColumn, typeof(int));

        foreach (var setting in settings.Where(setting => setting.CanEdit && !IsEquipmentExperienceGlobalNumericKey(setting.Key)))
        {
            var row = table.NewRow();
            row[GlobalNumericKeyColumn] = setting.Key;
            row[GlobalNumericNameColumn] = setting.DisplayName;
            row[GlobalNumericCurrentValueColumn] = setting.CurrentValueText;
            row[GlobalNumericNewValueColumn] = string.Empty;
            row[GlobalNumericMinValueColumn] = setting.MinValue;
            row[GlobalNumericMaxValueColumn] = setting.MaxValue;
            table.Rows.Add(row);
        }

        return table;
    }

    private static bool IsEquipmentExperienceGlobalNumericKey(string key)
        => EquipmentExperienceGlobalNumericKeys.Contains(key, StringComparer.OrdinalIgnoreCase);

    private static Dictionary<string, int> CollectGlobalNumericUpdates(DataTable table)
    {
        var updates = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (DataRow row in table.Rows)
        {
            var key = Convert.ToString(row[GlobalNumericKeyColumn], CultureInfo.InvariantCulture) ?? string.Empty;
            var name = Convert.ToString(row[GlobalNumericNameColumn], CultureInfo.InvariantCulture) ?? key;
            var current = Convert.ToString(row[GlobalNumericCurrentValueColumn], CultureInfo.InvariantCulture) ?? string.Empty;
            var valueText = Convert.ToString(row[GlobalNumericNewValueColumn], CultureInfo.InvariantCulture) ?? string.Empty;
            if (string.IsNullOrWhiteSpace(valueText) || string.Equals(valueText, current, StringComparison.Ordinal)) continue;
            if (!int.TryParse(valueText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
            {
                throw new InvalidOperationException($"全局参数“{name}”的新值不是整数：{valueText}");
            }

            var minValue = Convert.ToInt32(row[GlobalNumericMinValueColumn], CultureInfo.InvariantCulture);
            var maxValue = Convert.ToInt32(row[GlobalNumericMaxValueColumn], CultureInfo.InvariantCulture);
            if (value < minValue || value > maxValue)
            {
                throw new InvalidOperationException($"全局参数“{name}”必须在 {minValue}..{maxValue}。");
            }

            updates[key] = value;
        }

        return updates;
    }

    private static Dictionary<string, int> CollectEquipmentExperienceGlobalNumericUpdates(DataTable table)
    {
        var updates = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (DataRow row in table.Rows)
        {
            if (!IsGlobalNumericSettingRow(row)) continue;
            var key = Convert.ToString(row["Key"], CultureInfo.InvariantCulture) ?? string.Empty;
            var name = Convert.ToString(row["Name"], CultureInfo.InvariantCulture) ?? key;
            var current = Convert.ToString(row[CmSettingOriginalValueColumn], CultureInfo.InvariantCulture) ?? string.Empty;
            var valueText = Convert.ToString(row["Value"], CultureInfo.InvariantCulture) ?? string.Empty;
            if (string.IsNullOrWhiteSpace(valueText) || string.Equals(valueText, current, StringComparison.Ordinal)) continue;
            if (!int.TryParse(valueText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
            {
                throw new InvalidOperationException($"装备经验“{name}”的数值不是整数：{valueText}");
            }

            var minValue = Convert.ToInt32(row[GlobalNumericMinValueColumn], CultureInfo.InvariantCulture);
            var maxValue = Convert.ToInt32(row[GlobalNumericMaxValueColumn], CultureInfo.InvariantCulture);
            if (value < minValue || value > maxValue)
            {
                throw new InvalidOperationException($"装备经验“{name}”必须在 {minValue}..{maxValue}。");
            }

            updates[key] = value;
        }

        return updates;
    }

    private static bool IsGlobalNumericSettingRow(DataRow row)
        => row.Table.Columns.Contains(CmSettingSourceKindColumn) &&
           (Convert.ToString(row[CmSettingSourceKindColumn], CultureInfo.InvariantCulture) ?? string.Empty)
           .Equals(CmSettingSourceKindGlobalNumeric, StringComparison.OrdinalIgnoreCase);

    private static void RefreshGlobalTitleCapacityLabel(GlobalSettingsDocument? document, TextBox titleBox, Label label)
    {
        if (document == null)
        {
            label.Text = string.Empty;
            return;
        }

        var used = EncodingService.GetGbkByteCount(titleBox.Text);
        if (!document.GameTitle.CanEdit)
        {
            label.Text = "游戏标题不可编辑：" + document.GameTitle.Diagnostic;
            label.ForeColor = Color.Firebrick;
            return;
        }

        label.Text = $"{document.GameTitle.EngineVersion} / {document.GameTitle.LayoutKey}    GBK {used}/{document.GameTitle.CapacityBytes} 字节    " +
                     $"位置：{document.GameTitle.FileName}@{HexDisplayFormatter.FormatOffset(document.GameTitle.Offset)}";
        label.ForeColor = used <= document.GameTitle.CapacityBytes ? SystemColors.ControlText : Color.Firebrick;
    }

    private static string BuildGlobalSettingsSaveStatus(GlobalSettingsSaveResult result)
        => result.Summary + "\r\n" +
           "ChangedBytes=" + result.ChangedBytes.ToString(CultureInfo.InvariantCulture) + "\r\n" +
           "Backups=" + string.Join("; ", result.BackupPaths.Select(Path.GetFileName)) + "\r\n" +
           "Reports=" + string.Join("; ", result.ReportJsonPaths.Select(Path.GetFileName));

    private static string BuildParameterSaveStatus(CmSettingsSaveResult? cmResult, GlobalSettingsSaveResult? globalResult)
    {
        var summaries = new List<string>();
        if (globalResult != null)
        {
            summaries.Add(globalResult.Summary);
        }

        if (cmResult != null)
        {
            summaries.Add(cmResult.Summary);
        }

        return summaries.Count == 0
            ? "没有检测到需要保存的参数。"
            : string.Join("\r\n", summaries);
    }

    private static TabPage CreateGridTabPage(string title, Control grid)
    {
        var page = new TabPage(title);
        page.Controls.Add(grid);
        return page;
    }

    private static void SetTabPageVisible(TabControl tabs, TabPage page, bool visible)
    {
        var contains = tabs.TabPages.Contains(page);
        if (visible && !contains)
        {
            tabs.TabPages.Add(page);
        }
        else if (!visible && contains)
        {
            tabs.TabPages.Remove(page);
        }
    }

    private static DataGridView CreateCmSettingGrid(bool useCheckBoxValue)
    {
        var grid = new DataGridView
        {
            Dock = DockStyle.Fill,
            AutoGenerateColumns = false,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
            SelectionMode = DataGridViewSelectionMode.CellSelect,
            MultiSelect = false,
            RowHeadersVisible = false
        };

        grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            DataPropertyName = "Name",
            HeaderText = "名称",
            ReadOnly = true,
            FillWeight = 70
        });

        if (useCheckBoxValue)
        {
            grid.Columns.Add(new DataGridViewCheckBoxColumn
            {
                DataPropertyName = "Value",
                HeaderText = "数值",
                FillWeight = 30
            });
        }
        else
        {
            grid.Columns.Add(new DataGridViewTextBoxColumn
            {
                DataPropertyName = "Value",
                HeaderText = "数值",
                FillWeight = 30
            });
        }

        grid.DataError += (_, e) => { e.ThrowException = false; };
        return grid;
    }

    private static DataGridView CreateMixedCmSettingGrid()
    {
        var grid = CreateCmSettingGrid(useCheckBoxValue: false);
        grid.DataBindingComplete += (_, _) => ConfigureMixedCmSettingCells(grid);
        grid.CurrentCellDirtyStateChanged += (_, _) =>
        {
            if (grid.IsCurrentCellDirty && grid.CurrentCell is DataGridViewCheckBoxCell)
            {
                grid.CommitEdit(DataGridViewDataErrorContexts.Commit);
            }
        };
        grid.CellValueChanged += (_, e) =>
        {
            var valueColumnIndex = FindValueColumnIndex(grid);
            if (e.RowIndex < 0 || e.ColumnIndex != valueColumnIndex) return;
            var row = grid.Rows[e.RowIndex];
            if (row.DataBoundItem is DataRowView view)
            {
                view.Row["Value"] = row.Cells[e.ColumnIndex].Value ?? DBNull.Value;
            }
        };
        return grid;
    }

    private static void ConfigureMixedCmSettingCells(DataGridView grid)
    {
        var valueColumnIndex = FindValueColumnIndex(grid);
        if (valueColumnIndex < 0) return;

        foreach (DataGridViewRow row in grid.Rows)
        {
            if (row.IsNewRow || row.DataBoundItem is not DataRowView view) continue;
            var kind = Convert.ToString(view.Row["Kind"], CultureInfo.InvariantCulture) ?? string.Empty;
            var rawValue = view.Row["Value"];
            DataGridViewCell replacement = kind.Equals(CmSettingValueKind.ToggleByte.ToString(), StringComparison.OrdinalIgnoreCase)
                ? new DataGridViewCheckBoxCell
                {
                    ValueType = typeof(bool),
                    TrueValue = true,
                    FalseValue = false,
                    Value = CoerceSettingBool(rawValue)
                }
                : new DataGridViewTextBoxCell
                {
                    Value = Convert.ToString(rawValue, CultureInfo.InvariantCulture) ?? string.Empty
                };
            row.Cells[valueColumnIndex] = replacement;
        }
    }

    private static int FindValueColumnIndex(DataGridView grid)
        => grid.Columns
            .Cast<DataGridViewColumn>()
            .FirstOrDefault(column => column.DataPropertyName.Equals("Value", StringComparison.OrdinalIgnoreCase))
            ?.Index ?? -1;

    private static bool CoerceSettingBool(object? value)
    {
        if (value is bool boolValue) return boolValue;
        var text = Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty;
        return text.Equals("true", StringComparison.OrdinalIgnoreCase) ||
               text.Equals("1", StringComparison.OrdinalIgnoreCase) ||
               text.Equals("启用", StringComparison.OrdinalIgnoreCase) ||
               text.Equals("勾选", StringComparison.OrdinalIgnoreCase);
    }

    private static DataGridView CreateCmTerrainGrid()
    {
        var grid = new DataGridView
        {
            Dock = DockStyle.Fill,
            AutoGenerateColumns = false,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
            SelectionMode = DataGridViewSelectionMode.CellSelect,
            MultiSelect = false,
            RowHeadersVisible = false
        };

        grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            DataPropertyName = "地形",
            HeaderText = "地形",
            ReadOnly = true,
            FillWeight = 45
        });

        foreach (var name in new[] { "火", "水", "风", "地" })
        {
            grid.Columns.Add(new DataGridViewCheckBoxColumn
            {
                DataPropertyName = name,
                HeaderText = name,
                FillWeight = 15
            });
        }

        grid.DataError += (_, e) => { e.ThrowException = false; };
        return grid;
    }

    private static DataTable BuildCmSettingTable(CmSettingGroup group)
    {
        var table = new DataTable(group.DisplayName);
        table.Columns.Add("Key", typeof(string));
        table.Columns.Add("Kind", typeof(string));
        table.Columns.Add("Name", typeof(string));
        var useBoolValue = group.Items.All(item => item.ValueKind == CmSettingValueKind.ToggleByte);
        table.Columns.Add("Value", useBoolValue ? typeof(bool) : typeof(string));

        foreach (var item in group.Items)
        {
            var row = table.NewRow();
            row["Key"] = item.Key;
            row["Kind"] = item.ValueKind.ToString();
            row["Name"] = item.DisplayName;
            row["Value"] = item.ValueKind == CmSettingValueKind.ToggleByte
                ? item.CurrentBoolValue
                : item.CurrentValueText;
            table.Rows.Add(row);
        }

        return table;
    }

    private static DataTable BuildEquipmentExperienceTable(GlobalSettingsDocument? globalDocument, CmSettingGroup group)
    {
        var table = new DataTable(group.DisplayName);
        table.Columns.Add("Key", typeof(string));
        table.Columns.Add(CmSettingSourceKindColumn, typeof(string));
        table.Columns.Add("Kind", typeof(string));
        table.Columns.Add("Name", typeof(string));
        table.Columns.Add("Value", typeof(object));
        table.Columns.Add(CmSettingOriginalValueColumn, typeof(string));
        table.Columns.Add(GlobalNumericMinValueColumn, typeof(int));
        table.Columns.Add(GlobalNumericMaxValueColumn, typeof(int));

        var globalSettings = (globalDocument?.NumericSettings ?? Array.Empty<GlobalNumericSetting>())
            .ToDictionary(setting => setting.Key, StringComparer.OrdinalIgnoreCase);
        foreach (var key in EquipmentExperienceGlobalNumericKeys)
        {
            if (!globalSettings.TryGetValue(key, out var setting) || !setting.CanEdit) continue;
            var row = table.NewRow();
            row["Key"] = setting.Key;
            row[CmSettingSourceKindColumn] = CmSettingSourceKindGlobalNumeric;
            row["Kind"] = CmSettingValueKind.DecimalByte.ToString();
            row["Name"] = setting.DisplayName;
            row["Value"] = setting.CurrentValueText;
            row[CmSettingOriginalValueColumn] = setting.CurrentValueText;
            row[GlobalNumericMinValueColumn] = setting.MinValue;
            row[GlobalNumericMaxValueColumn] = setting.MaxValue;
            table.Rows.Add(row);
        }

        foreach (var item in group.Items)
        {
            var row = table.NewRow();
            row["Key"] = item.Key;
            row[CmSettingSourceKindColumn] = CmSettingSourceKindCm;
            row["Kind"] = item.ValueKind.ToString();
            row["Name"] = item.DisplayName;
            row["Value"] = item.ValueKind == CmSettingValueKind.ToggleByte
                ? item.CurrentBoolValue
                : item.CurrentValueText;
            row[CmSettingOriginalValueColumn] = item.ValueKind == CmSettingValueKind.ToggleByte
                ? (item.CurrentBoolValue ? "true" : "false")
                : item.CurrentValueText;
            row[GlobalNumericMinValueColumn] = 0;
            row[GlobalNumericMaxValueColumn] = 255;
            table.Rows.Add(row);
        }

        return table;
    }

    private static DataTable BuildCmTerrainTable(IEnumerable<CmTerrainStrategyRow> rows)
    {
        var table = new DataTable("地形策略");
        table.Columns.Add("TerrainId", typeof(int));
        table.Columns.Add("地形", typeof(string));
        table.Columns.Add("火", typeof(bool));
        table.Columns.Add("水", typeof(bool));
        table.Columns.Add("风", typeof(bool));
        table.Columns.Add("地", typeof(bool));

        foreach (var item in rows)
        {
            var row = table.NewRow();
            row["TerrainId"] = item.TerrainId;
            row["地形"] = item.TerrainIdHex[2..] + " " + item.TerrainName;
            row["火"] = item.Fire;
            row["水"] = item.Water;
            row["风"] = item.Wind;
            row["地"] = item.Earth;
            table.Rows.Add(row);
        }

        return table;
    }
}
