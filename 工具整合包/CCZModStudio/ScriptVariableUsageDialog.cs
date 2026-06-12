using CCZModStudio.Models;
using System.ComponentModel;
using System.Globalization;

namespace CCZModStudio;

internal sealed class ScriptVariableUsageDialog : Form
{
    private readonly Func<ScriptVariableUsageSnapshot?> _loadCurrentScenario;
    private readonly Func<IProgress<ScriptVariableProjectScanProgress>, CancellationToken, Task<ScriptVariableProjectScanResult?>> _scanProjectAsync;
    private readonly Func<ScriptVariableOccurrence, bool, Task> _navigateOccurrenceAsync;
    private readonly Func<ScriptVariableOccurrence, Task> _editOccurrenceAsync;

    private readonly TabControl _tabs = new();
    private readonly VariableUsagePage _currentPage = new("当前剧本", "ScriptVariableUsageDialog.Current.SummaryOccurrences");
    private readonly VariableUsagePage _projectPage = new("当前项目", "ScriptVariableUsageDialog.Project.SummaryOccurrences");
    private readonly Label _statusLabel = new();
    private readonly Button _refreshButton = new();
    private readonly Button _scanProjectButton = new();
    private readonly Button _locateButton = new();
    private readonly Button _editButton = new();

    private CancellationTokenSource? _projectScanCancellation;

    public ScriptVariableUsageDialog(
        IWin32Window owner,
        Func<ScriptVariableUsageSnapshot?> loadCurrentScenario,
        Func<IProgress<ScriptVariableProjectScanProgress>, CancellationToken, Task<ScriptVariableProjectScanResult?>> scanProjectAsync,
        Func<ScriptVariableOccurrence, bool, Task> navigateOccurrenceAsync,
        Func<ScriptVariableOccurrence, Task> editOccurrenceAsync)
    {
        _loadCurrentScenario = loadCurrentScenario;
        _scanProjectAsync = scanProjectAsync;
        _navigateOccurrenceAsync = navigateOccurrenceAsync;
        _editOccurrenceAsync = editOccurrenceAsync;

        Text = "剧本变量";
        StartPosition = FormStartPosition.CenterParent;
        Size = new Size(1180, 720);
        MinimumSize = new Size(900, 560);
        Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Regular, GraphicsUnit.Point);
        KeyPreview = true;

        BuildLayout();
        WireEvents();
        RefreshCurrentScenario();
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        _projectScanCancellation?.Cancel();
        base.OnFormClosing(e);
    }

    private void BuildLayout()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Padding = new Padding(8)
        };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        Controls.Add(root);

        var toolbar = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = true
        };
        _refreshButton.Text = "刷新当前剧本";
        _refreshButton.AutoSize = true;
        _scanProjectButton.Text = "扫描当前项目";
        _scanProjectButton.AutoSize = true;
        _locateButton.Text = "定位";
        _locateButton.AutoSize = true;
        _editButton.Text = "编辑";
        _editButton.AutoSize = true;
        toolbar.Controls.AddRange(new Control[]
        {
            _refreshButton,
            _scanProjectButton,
            _locateButton,
            _editButton
        });
        root.Controls.Add(toolbar, 0, 0);

        _tabs.Dock = DockStyle.Fill;
        _tabs.TabPages.Add(_currentPage.TabPage);
        _tabs.TabPages.Add(_projectPage.TabPage);
        root.Controls.Add(_tabs, 0, 1);

        _statusLabel.Dock = DockStyle.Fill;
        _statusLabel.AutoSize = true;
        _statusLabel.Padding = new Padding(2, 6, 0, 0);
        root.Controls.Add(_statusLabel, 0, 2);
    }

    private void WireEvents()
    {
        _refreshButton.Click += (_, _) => RefreshCurrentScenario();
        _scanProjectButton.Click += async (_, _) => await ScanProjectAsync();
        _locateButton.Click += async (_, _) => await NavigateSelectedAsync(edit: false);
        _editButton.Click += async (_, _) => await EditSelectedAsync();
        _tabs.SelectedIndexChanged += (_, _) => UpdateCommandButtons();
        _currentPage.SelectionChanged += (_, _) => UpdateCommandButtons();
        _projectPage.SelectionChanged += (_, _) => UpdateCommandButtons();
        _currentPage.OccurrenceDoubleClicked += async (_, occurrence) => await NavigateOccurrenceAsync(occurrence, edit: false);
        _projectPage.OccurrenceDoubleClicked += async (_, occurrence) => await NavigateOccurrenceAsync(occurrence, edit: false);
        KeyDown += async (_, e) =>
        {
            if (e.KeyCode == Keys.F5)
            {
                if (_tabs.SelectedIndex == 0)
                {
                    RefreshCurrentScenario();
                }
                else
                {
                    await ScanProjectAsync();
                }

                e.SuppressKeyPress = true;
            }
            else if (e.KeyCode == Keys.Enter)
            {
                await NavigateSelectedAsync(edit: false);
                e.SuppressKeyPress = true;
            }
        };
    }

    public void RefreshCurrentScenario()
    {
        var snapshot = _loadCurrentScenario();
        if (snapshot == null)
        {
            _currentPage.SetSnapshot(new ScriptVariableUsageSnapshot { SourceLabel = "当前剧本" });
            _statusLabel.Text = "当前剧本未进入旧版完整树模式，无法扫描变量。";
            UpdateCommandButtons();
            return;
        }

        _currentPage.SetSnapshot(snapshot);
        _statusLabel.Text = BuildSnapshotStatus(snapshot);
        UpdateCommandButtons();
    }

    public void InvalidateProjectSnapshot()
    {
        _projectPage.SetSnapshot(new ScriptVariableUsageSnapshot { SourceLabel = "当前项目" });
    }

    private async Task ScanProjectAsync()
    {
        if (_projectScanCancellation != null)
        {
            _projectScanCancellation.Cancel();
            _projectScanCancellation = null;
            _scanProjectButton.Text = "扫描当前项目";
            _statusLabel.Text = "项目变量扫描已取消。";
            return;
        }

        _projectScanCancellation = new CancellationTokenSource();
        _scanProjectButton.Text = "取消扫描";
        _refreshButton.Enabled = false;
        var progress = new Progress<ScriptVariableProjectScanProgress>(value =>
        {
            _statusLabel.Text = value.Total <= 0
                ? "正在扫描当前项目..."
                : $"正在扫描当前项目：{value.Completed}/{value.Total} {value.CurrentScenario}";
        });

        try
        {
            var result = await _scanProjectAsync(progress, _projectScanCancellation.Token);
            if (result == null)
            {
                _statusLabel.Text = "当前项目无法扫描变量。";
                return;
            }

            _projectPage.SetSnapshot(result);
            _tabs.SelectedTab = _projectPage.TabPage;
            var failed = result.FailedScenarios.Count == 0 ? string.Empty : $"，失败 {result.FailedScenarios.Count} 个";
            _statusLabel.Text = $"{BuildSnapshotStatus(result)}，已扫描 {result.ScannedScenarioCount} 个剧本{failed}。";
        }
        catch (OperationCanceledException)
        {
            _statusLabel.Text = "项目变量扫描已取消。";
        }
        finally
        {
            _projectScanCancellation?.Dispose();
            _projectScanCancellation = null;
            _scanProjectButton.Text = "扫描当前项目";
            _refreshButton.Enabled = true;
            UpdateCommandButtons();
        }
    }

    private async Task NavigateSelectedAsync(bool edit)
    {
        var occurrence = ActivePage.SelectedOccurrence;
        if (occurrence == null) return;
        await NavigateOccurrenceAsync(occurrence, edit);
    }

    private async Task EditSelectedAsync()
    {
        var occurrence = ActivePage.SelectedOccurrence;
        if (occurrence == null) return;
        await _editOccurrenceAsync(occurrence);
        RefreshCurrentScenario();
        ActivePage.RefreshFilters();
    }

    private async Task NavigateOccurrenceAsync(ScriptVariableOccurrence occurrence, bool edit)
    {
        await _navigateOccurrenceAsync(occurrence, edit);
        if (edit)
        {
            RefreshCurrentScenario();
        }
    }

    private VariableUsagePage ActivePage => _tabs.SelectedIndex == 1 ? _projectPage : _currentPage;

    private void UpdateCommandButtons()
    {
        var occurrence = ActivePage.SelectedOccurrence;
        _locateButton.Enabled = occurrence != null;
        _editButton.Enabled = occurrence is { CanEdit: true };
    }

    private static string BuildSnapshotStatus(ScriptVariableUsageSnapshot snapshot)
        => $"{snapshot.SourceLabel}：变量 {snapshot.Summaries.Count} 个，出现 {snapshot.Occurrences.Count} 次。";

    private sealed class VariableUsagePage
    {
        private readonly BindingList<ScriptVariableSummary> _summaryRows = [];
        private readonly BindingList<ScriptVariableOccurrence> _occurrenceRows = [];
        private readonly DataGridView _summaryGrid = new();
        private readonly DataGridView _occurrenceGrid = new();
        private readonly TextBox _addressFilterBox = new();
        private readonly TextBox _scenarioFilterBox = new();
        private readonly TextBox _commandFilterBox = new();
        private readonly ComboBox _typeFilterCombo = new();
        private readonly CheckBox _editableOnlyCheckBox = new();
        private readonly Label _countLabel = new();
        private readonly string _splitLayoutKey;
        private ScriptVariableUsageSnapshot _snapshot = new();
        private ListSortDirection? _summaryUsageSortDirection;

        public VariableUsagePage(string title, string splitLayoutKey)
        {
            _splitLayoutKey = splitLayoutKey;
            TabPage = new TabPage(title);
            BuildLayout();
            WireEvents();
        }

        public event EventHandler? SelectionChanged;
        public event EventHandler<ScriptVariableOccurrence>? OccurrenceDoubleClicked;

        public TabPage TabPage { get; }

        public ScriptVariableOccurrence? SelectedOccurrence
            => _occurrenceGrid.SelectedRows.Count > 0
                ? _occurrenceGrid.SelectedRows[0].DataBoundItem as ScriptVariableOccurrence
                : _occurrenceGrid.CurrentRow?.DataBoundItem as ScriptVariableOccurrence;

        public void SetSnapshot(ScriptVariableUsageSnapshot snapshot)
        {
            _snapshot = snapshot;
            PopulateTypeFilter();
            RefreshFilters();
        }

        public void RefreshFilters()
        {
            var summaries = SortSummaries(FilterSummaries(_snapshot.Summaries)).ToList();
            BindSummaryRows(summaries);
            UpdateSummarySortGlyph();
            if (summaries.Count == 0)
            {
                BindOccurrenceRows(Array.Empty<ScriptVariableOccurrence>());
                _countLabel.Text = $"变量 0/{_snapshot.Summaries.Count}，出现 {_snapshot.Occurrences.Count} 次";
            }
        }

        private void BuildLayout()
        {
            var root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 2,
                Padding = new Padding(4)
            };
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            TabPage.Controls.Add(root);

            var filters = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                AutoSize = true,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = true
            };
            _typeFilterCombo.DropDownStyle = ComboBoxStyle.DropDownList;
            _typeFilterCombo.Width = 150;
            _addressFilterBox.Width = 110;
            _addressFilterBox.PlaceholderText = "地址/编号";
            _scenarioFilterBox.Width = 130;
            _scenarioFilterBox.PlaceholderText = "剧本";
            _commandFilterBox.Width = 90;
            _commandFilterBox.PlaceholderText = "命令ID";
            _editableOnlyCheckBox.Text = "只看可编辑";
            _editableOnlyCheckBox.AutoSize = true;
            _countLabel.AutoSize = true;
            _countLabel.Padding = new Padding(8, 6, 0, 0);
            filters.Controls.AddRange(new Control[]
            {
                new Label { Text = "类型", AutoSize = true, Padding = new Padding(0, 6, 0, 0) },
                _typeFilterCombo,
                _addressFilterBox,
                _scenarioFilterBox,
                _commandFilterBox,
                _editableOnlyCheckBox,
                _countLabel
            });
            root.Controls.Add(filters, 0, 0);

            var split = new SplitContainer
            {
                Dock = DockStyle.Fill,
                Orientation = Orientation.Horizontal,
            };
            ConfigurePersistentSplit(split, _splitLayoutKey, desiredDistance: 260, desiredPanel1Min: 120, desiredPanel2Min: 120);
            root.Controls.Add(split, 0, 1);

            ConfigureGrid(_summaryGrid);
            ConfigureGrid(_occurrenceGrid);
            ConfigureSummaryColumns();
            ConfigureOccurrenceColumns();
            _summaryGrid.DataSource = _summaryRows;
            _occurrenceGrid.DataSource = _occurrenceRows;
            split.Panel1.Controls.Add(_summaryGrid);
            split.Panel2.Controls.Add(_occurrenceGrid);
        }

        private void WireEvents()
        {
            _typeFilterCombo.SelectedIndexChanged += (_, _) => RefreshFilters();
            _addressFilterBox.TextChanged += (_, _) => RefreshFilters();
            _scenarioFilterBox.TextChanged += (_, _) => RefreshFilters();
            _commandFilterBox.TextChanged += (_, _) => RefreshFilters();
            _editableOnlyCheckBox.CheckedChanged += (_, _) => RefreshFilters();
            _summaryGrid.SelectionChanged += (_, _) => ShowSelectedSummaryOccurrences();
            _summaryGrid.ColumnHeaderMouseClick += (_, e) => HandleSummaryColumnHeaderClick(e.ColumnIndex);
            _occurrenceGrid.SelectionChanged += (_, _) => SelectionChanged?.Invoke(this, EventArgs.Empty);
            _occurrenceGrid.CellDoubleClick += (_, _) =>
            {
                if (SelectedOccurrence != null)
                {
                    OccurrenceDoubleClicked?.Invoke(this, SelectedOccurrence);
                }
            };
        }

        private void PopulateTypeFilter()
        {
            var selected = _typeFilterCombo.SelectedItem as string ?? "全部类型";
            var types = new[] { "全部类型" }
                .Concat(_snapshot.Summaries.Select(x => x.VariableType).Distinct().OrderBy(x => x, StringComparer.CurrentCultureIgnoreCase))
                .ToList();
            _typeFilterCombo.DataSource = types;
            _typeFilterCombo.SelectedItem = types.Contains(selected, StringComparer.OrdinalIgnoreCase) ? selected : "全部类型";
        }

        private IEnumerable<ScriptVariableSummary> FilterSummaries(IEnumerable<ScriptVariableSummary> source)
        {
            var type = _typeFilterCombo.SelectedItem as string;
            var address = NormalizeHexFilter(_addressFilterBox.Text.Trim());
            var scenario = _scenarioFilterBox.Text.Trim();
            var command = NormalizeHexFilter(_commandFilterBox.Text.Trim());
            foreach (var row in source)
            {
                if (!string.IsNullOrWhiteSpace(type) &&
                    !string.Equals(type, "全部类型", StringComparison.OrdinalIgnoreCase) &&
                    !row.VariableType.Equals(type, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (!string.IsNullOrWhiteSpace(address) &&
                    !NormalizeHexFilter(row.VariableAddressText).Contains(address, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (_editableOnlyCheckBox.Checked && !row.HasEditableOccurrence)
                {
                    continue;
                }

                var occurrences = _snapshot.Occurrences.Where(x => x.SummaryKey.Equals(row.Key, StringComparison.OrdinalIgnoreCase));
                if (!string.IsNullOrWhiteSpace(scenario) &&
                    !occurrences.Any(x => x.ScenarioFileName.Contains(scenario, StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }

                if (!string.IsNullOrWhiteSpace(command) &&
                    !occurrences.Any(x => NormalizeHexFilter(x.CommandIdHex).Contains(command, StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }

                yield return row;
            }
        }

        private IEnumerable<ScriptVariableOccurrence> FilterOccurrences(string summaryKey)
        {
            var scenario = _scenarioFilterBox.Text.Trim();
            var command = NormalizeHexFilter(_commandFilterBox.Text.Trim());
            foreach (var row in _snapshot.Occurrences.Where(x => x.SummaryKey.Equals(summaryKey, StringComparison.OrdinalIgnoreCase)))
            {
                if (!string.IsNullOrWhiteSpace(scenario) &&
                    !row.ScenarioFileName.Contains(scenario, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (!string.IsNullOrWhiteSpace(command) &&
                    !NormalizeHexFilter(row.CommandIdHex).Contains(command, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (_editableOnlyCheckBox.Checked && !row.CanEdit)
                {
                    continue;
                }

                yield return row;
            }
        }

        private void ShowSelectedSummaryOccurrences()
        {
            var selected = _summaryGrid.SelectedRows.Count > 0
                ? _summaryGrid.SelectedRows[0].DataBoundItem as ScriptVariableSummary
                : _summaryGrid.CurrentRow?.DataBoundItem as ScriptVariableSummary;
            if (selected == null)
            {
                BindOccurrenceRows(Array.Empty<ScriptVariableOccurrence>());
                return;
            }

            var rows = FilterOccurrences(selected.Key).ToList();
            BindOccurrenceRows(rows);
            _countLabel.Text = $"变量 {_summaryRows.Count}/{_snapshot.Summaries.Count}，当前出现 {rows.Count} 次";
        }

        private void BindSummaryRows(IEnumerable<ScriptVariableSummary> rows)
        {
            _summaryRows.Clear();
            foreach (var row in rows)
            {
                _summaryRows.Add(row);
            }

            if (_summaryGrid.Rows.Count > 0)
            {
                _summaryGrid.Rows[0].Selected = true;
                _summaryGrid.CurrentCell = _summaryGrid.Rows[0].Cells.Cast<DataGridViewCell>().First(cell => cell.Visible);
                ShowSelectedSummaryOccurrences();
            }
        }

        private void BindOccurrenceRows(IEnumerable<ScriptVariableOccurrence> rows)
        {
            _occurrenceRows.Clear();
            foreach (var row in rows)
            {
                _occurrenceRows.Add(row);
            }

            if (_occurrenceGrid.Rows.Count > 0)
            {
                _occurrenceGrid.Rows[0].Selected = true;
                _occurrenceGrid.CurrentCell = _occurrenceGrid.Rows[0].Cells.Cast<DataGridViewCell>().First(cell => cell.Visible);
            }

            SelectionChanged?.Invoke(this, EventArgs.Empty);
        }

        private static void ConfigureGrid(DataGridView grid)
        {
            grid.AutoGenerateColumns = false;
            grid.Dock = DockStyle.Fill;
            grid.ReadOnly = true;
            grid.AllowUserToAddRows = false;
            grid.AllowUserToDeleteRows = false;
            grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None;
            grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            grid.MultiSelect = false;
            grid.RowHeadersVisible = false;
            grid.BorderStyle = BorderStyle.FixedSingle;
        }

        private static void ConfigurePersistentSplit(
            SplitContainer split,
            string layoutKey,
            int desiredDistance,
            int desiredPanel1Min,
            int desiredPanel2Min)
        {
            layoutKey = UiLayoutSettingsStore.NormalizeKey(layoutKey);
            var applyingProgrammaticDistance = false;
            var userMovingSplitter = false;

            void Apply()
            {
                if (split.IsDisposed) return;
                var totalLength = split.Orientation == Orientation.Vertical ? split.Width : split.Height;
                if (totalLength <= split.SplitterWidth + 2) return;

                var canUseRequestedMins = totalLength > desiredPanel1Min + desiredPanel2Min + split.SplitterWidth;
                if (!canUseRequestedMins)
                {
                    split.Panel1MinSize = 0;
                    split.Panel2MinSize = 0;
                }

                var panel1Min = canUseRequestedMins ? desiredPanel1Min : 0;
                var panel2Min = canUseRequestedMins ? desiredPanel2Min : 0;
                var maxDistance = Math.Max(panel1Min, totalLength - panel2Min - split.SplitterWidth);
                var usableLength = totalLength - split.SplitterWidth;
                var savedRatio = UiLayoutSettingsStore.GetSplitRatio(UiLayoutSettingsStore.Load(), layoutKey);
                var target = savedRatio.HasValue && usableLength > 0
                    ? (int)Math.Round(usableLength * savedRatio.Value)
                    : desiredDistance;
                target = Math.Clamp(target, panel1Min, maxDistance);

                if (split.SplitterDistance != target)
                {
                    applyingProgrammaticDistance = true;
                    try
                    {
                        split.SplitterDistance = target;
                    }
                    finally
                    {
                        applyingProgrammaticDistance = false;
                    }
                }

                if (canUseRequestedMins)
                {
                    split.Panel1MinSize = desiredPanel1Min;
                    split.Panel2MinSize = desiredPanel2Min;
                }
            }

            split.SizeChanged += (_, _) => Apply();
            split.HandleCreated += (_, _) => Apply();
            split.SplitterMoving += (_, _) =>
            {
                if (applyingProgrammaticDistance) return;
                if (Control.MouseButtons == MouseButtons.None) return;

                userMovingSplitter = true;
            };
            split.SplitterMoved += (_, _) =>
            {
                if (applyingProgrammaticDistance) return;
                if (!userMovingSplitter) return;

                var totalLength = split.Orientation == Orientation.Vertical ? split.Width : split.Height;
                var usableLength = totalLength - split.SplitterWidth;
                if (usableLength <= 0) return;

                var ratio = Math.Clamp(split.SplitterDistance / (double)usableLength, 0.01, 0.99);
                UiLayoutSettingsStore.SaveSplitRatio(layoutKey, ratio);
                userMovingSplitter = false;
            };
        }

        private void ConfigureSummaryColumns()
        {
            _summaryGrid.Columns.Clear();
            _summaryGrid.Columns.AddRange(
                TextColumn(nameof(ScriptVariableSummary.VariableType), "类型", 110),
                TextColumn(nameof(ScriptVariableSummary.VariableAddressText), "地址/编号", 120),
                TextColumn(nameof(ScriptVariableSummary.FirstScenario), "首个剧本", 120),
                TextColumn(nameof(ScriptVariableSummary.CommandIds), "命令", 130),
                TextColumn(nameof(ScriptVariableSummary.Scope), "作用域", 100),
                TextColumn(nameof(ScriptVariableSummary.ScenarioCount), "剧本数", 90),
                TextColumn(nameof(ScriptVariableSummary.AccessKinds), "访问", 220),
                TextColumn(
                    nameof(ScriptVariableSummary.OccurrenceCount),
                    "使用次数",
                    95,
                    DataGridViewColumnSortMode.Programmatic));
        }

        private void ConfigureOccurrenceColumns()
        {
            _occurrenceGrid.Columns.Clear();
            _occurrenceGrid.Columns.AddRange(
                TextColumn(nameof(ScriptVariableOccurrence.ScenarioFileName), "剧本", 120),
                TextColumn(nameof(ScriptVariableOccurrence.CommandIdHex), "命令", 80),
                TextColumn(nameof(ScriptVariableOccurrence.CommandName), "命令名", 150),
                TextColumn(nameof(ScriptVariableOccurrence.VariableType), "类型", 110),
                TextColumn(nameof(ScriptVariableOccurrence.VariableAddressText), "地址/编号", 120),
                TextColumn(nameof(ScriptVariableOccurrence.Scope), "作用域", 100),
                TextColumn(nameof(ScriptVariableOccurrence.ParameterSlot), "槽位", 90),
                TextColumn(nameof(ScriptVariableOccurrence.AccessKind), "访问", 110),
                TextColumn(nameof(ScriptVariableOccurrence.RelatedValue), "关联值", 210));
        }

        private void HandleSummaryColumnHeaderClick(int columnIndex)
        {
            if (columnIndex < 0 || columnIndex >= _summaryGrid.Columns.Count) return;
            var column = _summaryGrid.Columns[columnIndex];
            if (column.DataPropertyName != nameof(ScriptVariableSummary.OccurrenceCount)) return;

            _summaryUsageSortDirection = _summaryUsageSortDirection == ListSortDirection.Ascending
                ? ListSortDirection.Descending
                : ListSortDirection.Ascending;
            RefreshFilters();
        }

        private IEnumerable<ScriptVariableSummary> SortSummaries(IEnumerable<ScriptVariableSummary> rows)
            => _summaryUsageSortDirection switch
            {
                ListSortDirection.Ascending => rows.OrderBy(x => x.OccurrenceCount),
                ListSortDirection.Descending => rows.OrderByDescending(x => x.OccurrenceCount),
                _ => rows
            };

        private void UpdateSummarySortGlyph()
        {
            foreach (DataGridViewColumn column in _summaryGrid.Columns)
            {
                column.HeaderCell.SortGlyphDirection = SortOrder.None;
            }

            if (_summaryUsageSortDirection == null) return;
            if (_summaryGrid.Columns[nameof(ScriptVariableSummary.OccurrenceCount)] is not { } usageColumn) return;

            usageColumn.HeaderCell.SortGlyphDirection = _summaryUsageSortDirection == ListSortDirection.Ascending
                ? SortOrder.Ascending
                : SortOrder.Descending;
        }

        private static DataGridViewTextBoxColumn TextColumn(
            string propertyName,
            string headerText,
            int width,
            DataGridViewColumnSortMode sortMode = DataGridViewColumnSortMode.NotSortable)
            => new()
            {
                Name = propertyName,
                DataPropertyName = propertyName,
                HeaderText = headerText,
                Width = width,
                ReadOnly = true,
                SortMode = sortMode
            };

        private static string NormalizeHexFilter(string value)
        {
            value = value.Trim();
            if (value.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            {
                value = value[2..];
            }

            return value.Trim();
        }
    }
}
