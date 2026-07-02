using CCZModStudio.Core;
using CCZModStudio.Formats;
using CCZModStudio.Models;
using System.ComponentModel;
using System.Data;
using System.Globalization;

namespace CCZModStudio;

public sealed partial class MainForm
{
    private enum UnsavedCloseChoice
    {
        SaveAll,
        DiscardAndClose,
        CancelClose
    }

    private sealed class UnsavedEditorItem
    {
        public required string Page { get; init; }
        public required string Target { get; init; }
        public required string Summary { get; init; }
        public required Func<Task> SaveAsync { get; init; }
        public Action? Discard { get; init; }

        public string DisplayText => $"{Page} - {Target}: {Summary}";
    }

    private sealed class ScriptEditorSession
    {
        public required string ProjectRoot { get; init; }
        public required ScenarioFileInfo Scenario { get; init; }
        public LegacyScenarioDocument? LegacyDocument { get; init; }
        public ScenarioStructureProbeResult? ProbeStructure { get; init; }
        public IReadOnlyList<ScenarioTextEntry> TextEntries { get; init; } = Array.Empty<ScenarioTextEntry>();
        public int? SelectedTextOffset { get; init; }
        public string TextEditorText { get; init; } = string.Empty;
        public LegacyScriptViewportSnapshot? Viewport { get; init; }
        public string SearchText { get; init; } = string.Empty;
        public int SearchResultIndex { get; init; } = -1;
        public bool StructureDirty { get; init; }
        public bool TextDirty { get; init; }
    }

    private sealed class BattlefieldEditorSession
    {
        public required string ProjectRoot { get; init; }
        public required ScenarioFileInfo Scenario { get; init; }
        public required BattlefieldEditorDocument Document { get; init; }
        public LegacyScenarioDocument? LegacyDocument { get; init; }
        public ScenarioStructureProbeResult? ScriptStructure { get; init; }
        public IReadOnlyList<ScenarioTextEntry> ScriptTextEntries { get; init; } = Array.Empty<ScenarioTextEntry>();
        public List<BattlefieldPlacedUnit> PlacedUnits { get; init; } = [];
        public int? SelectedScriptTextOffset { get; init; }
        public string ScriptTextEditorText { get; init; } = string.Empty;
        public string TitleText { get; init; } = string.Empty;
        public string ConditionsText { get; init; } = string.Empty;
        public LegacyScriptViewportSnapshot? Viewport { get; init; }
        public string SearchText { get; init; } = string.Empty;
        public int SearchResultIndex { get; init; } = -1;
        public bool StructureDirty { get; init; }
        public bool TextDirty { get; init; }
        public bool ScriptTextDirty { get; init; }
        public bool PlacementDirty { get; init; }
    }

    private sealed class RSceneEditorSession
    {
        public required string ProjectRoot { get; init; }
        public required ScenarioFileInfo Scenario { get; init; }
        public LegacyScenarioDocument? LegacyDocument { get; init; }
        public IReadOnlyList<LegacyScenarioDocument> PrecedingVariableDocuments { get; init; } = Array.Empty<LegacyScenarioDocument>();
        public ScenarioStructureProbeResult? ScriptStructure { get; init; }
        public IReadOnlyList<ScenarioTextEntry> ScriptTextEntries { get; init; } = Array.Empty<ScenarioTextEntry>();
        public IReadOnlyList<RSceneCommandCandidate> CommandCandidates { get; init; } = Array.Empty<RSceneCommandCandidate>();
        public IReadOnlyList<RSceneStateCandidate> StateCandidates { get; init; } = Array.Empty<RSceneStateCandidate>();
        public List<RScenePlacedActor> PlacedActors { get; init; } = [];
        public int BackgroundImageNumber { get; init; }
        public int GridSize { get; init; }
        public LegacyScriptViewportSnapshot? Viewport { get; init; }
        public string SearchText { get; init; } = string.Empty;
        public int SearchResultIndex { get; init; } = -1;
        public bool StructureDirty { get; init; }
        public bool DraftDirty { get; init; }
    }

    private sealed class TableEditorSession
    {
        public required string ProjectRoot { get; init; }
        public required int TableId { get; init; }
        public required TableReadResult Result { get; set; }
        public GridViewportSnapshot? Viewport { get; set; }
        public string ColumnFilterText { get; set; } = string.Empty;
        public bool DangerColumnsOnly { get; set; }
        public string RowFilterText { get; set; } = string.Empty;
        public bool ChangedRowsOnly { get; set; }
        public bool SearchVisibleColumnsOnly { get; set; } = true;
        public GridEditSession EditSession { get; set; } = new();
    }

    private readonly Dictionary<string, ScriptEditorSession> _scriptEditorSessions = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, BattlefieldEditorSession> _battlefieldEditorSessions = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, RSceneEditorSession> _rSceneEditorSessions = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, TableEditorSession> _tableEditorSessions = new(StringComparer.OrdinalIgnoreCase);
    private bool _scriptLegacyStructureDirty;
    private bool _battlefieldLegacyStructureDirty;
    private bool _rSceneLegacyStructureDirty;
    private bool _discardingUnsavedChangesForClose;

    private void SetLegacyStructureDirtyFlag(LegacyScriptEditorScope scope, bool dirty)
    {
        switch (scope)
        {
            case LegacyScriptEditorScope.Script:
                _scriptLegacyStructureDirty = dirty;
                break;
            case LegacyScriptEditorScope.Battlefield:
                _battlefieldLegacyStructureDirty = dirty;
                break;
            case LegacyScriptEditorScope.RScene:
                _rSceneLegacyStructureDirty = dirty;
                break;
        }
    }

    private bool IsLegacyStructureDirty(LegacyScriptEditorScope scope)
        => scope switch
        {
            LegacyScriptEditorScope.Script => _scriptLegacyStructureDirty,
            LegacyScriptEditorScope.Battlefield => _battlefieldLegacyStructureDirty,
            LegacyScriptEditorScope.RScene => _rSceneLegacyStructureDirty,
            _ => false
        };

    private string BuildEditorSessionKey(string scope, ScenarioFileInfo scenario)
        => _project == null
            ? string.Empty
            : $"{Path.GetFullPath(_project.GameRoot)}|{scope}|{scenario.FileName}";

    private void CommitActiveEditors()
    {
        TryCommitPendingBattlefieldConsoleChanges();
        foreach (var grid in EnumerateEditableGrids())
        {
            try
            {
                grid.EndEdit();
                if (grid.DataSource is BindingSource bindingSource) bindingSource.EndEdit();
                if (grid.BindingContext?[grid.DataSource] is CurrencyManager manager) manager.EndCurrentEdit();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("EndEdit failed for unsaved-change scan: " + ex.Message);
            }
        }

        SyncSelectedRoleTextDetailsIntoTables();
    }

    private IEnumerable<DataGridView> EnumerateEditableGrids()
    {
        yield return _dataGrid;
        yield return _roleEditorGrid;
        yield return _jobEditorGrid;
        yield return _jobTerrainGrid;
        yield return _jobRestraintGrid;
        yield return _jobStrategyEditorGrid;
        yield return _jobStrategyLearningEditorGrid;
        yield return _jobEffectEditorGrid;
        yield return _itemEditorGrid;
        yield return _shopEditorGrid;
        yield return _imageAssignmentGrid;
        yield return _scriptTextGrid;
        yield return _scenarioTextGrid;
        yield return _battlefieldScriptParameterGrid;
        yield return _rSceneCommandGrid;
    }

    private IReadOnlyList<UnsavedEditorItem> CollectUnsavedItems()
    {
        CommitActiveEditors();
        CacheCurrentScriptEditorSession();
        CacheCurrentBattlefieldEditorSession();
        CacheCurrentRSceneEditorSession();
        CacheCurrentTableEditorSession();

        var items = new List<UnsavedEditorItem>();
        AddDataTableUnsavedItems(items);
        AddScenarioTextUnsavedItems(items);
        AddCurrentMapWorkbenchUnsavedItem(items);
        AddScriptSessionUnsavedItems(items);
        AddBattlefieldSessionUnsavedItems(items);
        AddRSceneSessionUnsavedItems(items);
        return items;
    }

    private void AddDataTableUnsavedItems(List<UnsavedEditorItem> items)
    {
        if (_project == null) return;
        CommitJobEquipmentEditorChanges();
        if (!CommitJobStrategyLearningEditorChanges(showMessage: true, restoreSelectionOnFailure: true))
        {
            return;
        }

        if (_currentTableResult != null && CanEditTable(_currentTableResult) && HasChanges(_currentTableResult.Data))
        {
            var tableName = _currentTableResult.Table.TableName;
            items.Add(new UnsavedEditorItem
            {
                Page = "数据表编辑",
                Target = tableName,
                Summary = BuildDataTableChangeSummary(_currentTableResult.Data),
                SaveAsync = () => SaveCurrentTableSilentlyAsync(),
                Discard = () => LoadSelectedTable()
            });
        }

        AddDataTableItem(items, "角色设定", "人物/R/S", _currentRoleEditorData, () => SaveRoleEditorSilentlyAsync(), () => LoadRoleEditor());
        AddDataTableItem(items, "角色设定", "列传/台词", GetRoleTextCombinedChanges(), () => SaveRoleTextDetailsSilentlyAsync(), () => { LoadRoleTextTables(); if (_roleEditorGrid.CurrentRow != null && TryGetDataRow(_roleEditorGrid.CurrentRow) is { } row) ShowRoleTextDetails(row); });
        AddDataTableItem(items, "兵种设定", "详细兵种", _currentJobEditorData, () => SaveJobEditorSilentlyAsync(), () => LoadJobEditor());
        AddDataTableItem(items, "兵种设定", "兵种系/地形", _currentJobTerrainData, () => SaveJobTerrainEditorSilentlyAsync(), () => LoadJobTerrainEditor());
        AddDataTableItem(items, "兵种设定", "兵种相克矩阵", _jobRestraintRead?.Data, () => SaveJobMatrixEditorSilentlyAsync(), () => LoadJobMatrixEditor());
        AddDataTableItem(items, "兵种设定", "策略", _currentJobStrategyData, () => SaveJobStrategyEditorSilentlyAsync(), () => LoadJobStrategyEditor());
        AddDataTableItem(items, "兵种设定", "兵种特效", _currentJobEffectData, () => SaveJobEffectEditorSilentlyAsync(), () => LoadJobEffectEditor());
        AddDataTableItem(items, "宝物设定", "宝物/物品", _currentItemEditorData, () => SaveItemEditorSilentlyAsync(), () => LoadItemEditor());
        AddDataTableItem(items, "商店编辑", "商店", _currentShopEditorData, () => SaveShopEditorSilentlyAsync(), () => LoadShopEditor());
        AddDataTableItem(items, "图片设定", "人物形象设定", _currentImageAssignments, () => SaveImageAssignmentsSilentlyAsync(), () => LoadImageAssignments());
    }

    private void AddDataTableItem(
        List<UnsavedEditorItem> items,
        string page,
        string target,
        DataTable? table,
        Func<Task> saveAsync,
        Action discard)
    {
        if (!HasChanges(table)) return;
        items.Add(new UnsavedEditorItem
        {
            Page = page,
            Target = target,
            Summary = BuildDataTableChangeSummary(table!),
            SaveAsync = saveAsync,
            Discard = discard
        });
    }

    private static bool HasChanges(DataTable? table) => table?.GetChanges() != null;

    private static string BuildDataTableChangeSummary(DataTable table)
    {
        var changed = table.Rows.Cast<DataRow>().Count(row => row.RowState is not DataRowState.Unchanged and not DataRowState.Detached);
        return changed <= 0 ? "有未保存改动" : $"{changed} 行改动";
    }

    private DataTable? GetRoleTextCombinedChanges()
    {
        if (_roleBiographyRead == null || _roleCriticalQuoteRead == null || _roleRetreatQuoteRead == null) return null;
        if (!HasChanges(_roleBiographyRead.Data) && !HasChanges(_roleCriticalQuoteRead.Data) && !HasChanges(_roleRetreatQuoteRead.Data)) return null;

        var table = new DataTable("RoleTexts");
        table.Columns.Add("ID", typeof(int));
        table.Rows.Add(0);
        table.AcceptChanges();
        table.Rows[0]["ID"] = 1;
        return table;
    }

    private void AddScenarioTextUnsavedItems(List<UnsavedEditorItem> items)
    {
        if (_project == null || _currentScenarioTextEntries.Count == 0) return;
        var changed = _currentScenarioTextEntries.Where(IsScenarioTextChanged).ToList();
        if (changed.Count == 0) return;
        var target = GetSelectedScenarioFileItem()?.FileName ?? "R/S 文本";
        items.Add(new UnsavedEditorItem
        {
            Page = "高级探针",
            Target = target,
            Summary = $"{changed.Count} 条文本改动",
            SaveAsync = () => SaveScenarioTextsSilentlyAsync(),
            Discard = () => ProbeSelectedScenarioTexts()
        });
    }

    private void AddCurrentMapWorkbenchUnsavedItem(List<UnsavedEditorItem> items)
    {
        if (_project == null || _currentMapWorkbenchDraft == null || !IsCurrentMapWorkbenchDraftDirty()) return;
        items.Add(new UnsavedEditorItem
        {
            Page = "地图编辑",
            Target = string.IsNullOrWhiteSpace(_currentMapWorkbenchDraft.BoundMapId) ? _currentMapWorkbenchDraft.DraftId : _currentMapWorkbenchDraft.BoundMapId,
            Summary = "地图工作台草稿未保存",
            SaveAsync = () => SaveMapWorkbenchDraftSilentlyAsync(),
            Discard = () => LoadLastMapWorkbenchDraft()
        });
    }

    private bool IsCurrentMapWorkbenchDraftDirty()
        => _currentMapWorkbenchDraft != null &&
           (_mapMakerMapUndoStack.Count > 0 ||
            _mapMakerTerrainUndoStack.Count > 0 ||
            _mapMakerMapRedoStack.Count > 0 ||
            _mapMakerTerrainRedoStack.Count > 0 ||
            CountMapWorkbenchTerrainChangedCells() > 0);

    private void AddScriptSessionUnsavedItems(List<UnsavedEditorItem> items)
    {
        foreach (var session in _scriptEditorSessions.Values.Where(IsScriptSessionDirty))
        {
            items.Add(new UnsavedEditorItem
            {
                Page = "剧本编辑",
                Target = session.Scenario.FileName,
                Summary = BuildScriptSessionSummary(session),
                SaveAsync = async () =>
                {
                    RestoreScriptEditorSession(session);
                    await SaveCurrentScriptSessionSilentlyAsync();
                    _scriptEditorSessions.Remove(BuildEditorSessionKey("script", session.Scenario));
                },
                Discard = () => _scriptEditorSessions.Remove(BuildEditorSessionKey("script", session.Scenario))
            });
        }
    }

    private void AddBattlefieldSessionUnsavedItems(List<UnsavedEditorItem> items)
    {
        foreach (var session in _battlefieldEditorSessions.Values.Where(IsBattlefieldSessionDirty))
        {
            items.Add(new UnsavedEditorItem
            {
                Page = "战场编辑",
                Target = session.Scenario.FileName,
                Summary = BuildBattlefieldSessionSummary(session),
                SaveAsync = async () =>
                {
                    RestoreBattlefieldEditorSession(session);
                    await SaveCurrentBattlefieldSessionSilentlyAsync();
                    _battlefieldEditorSessions.Remove(BuildEditorSessionKey("battlefield", session.Scenario));
                },
                Discard = () => _battlefieldEditorSessions.Remove(BuildEditorSessionKey("battlefield", session.Scenario))
            });
        }
    }

    private void AddRSceneSessionUnsavedItems(List<UnsavedEditorItem> items)
    {
        foreach (var session in _rSceneEditorSessions.Values.Where(IsRSceneSessionDirty))
        {
            items.Add(new UnsavedEditorItem
            {
                Page = "场景编辑",
                Target = session.Scenario.FileName,
                Summary = BuildRSceneSessionSummary(session),
                SaveAsync = async () =>
                {
                    RestoreRSceneEditorSession(session);
                    await SaveCurrentRSceneSessionSilentlyAsync();
                    _rSceneEditorSessions.Remove(BuildEditorSessionKey("rscene", session.Scenario));
                },
                Discard = () => _rSceneEditorSessions.Remove(BuildEditorSessionKey("rscene", session.Scenario))
            });
        }
    }

    private static bool IsScriptSessionDirty(ScriptEditorSession session) => session.StructureDirty || session.TextDirty;
    private static bool IsBattlefieldSessionDirty(BattlefieldEditorSession session) => session.StructureDirty || session.TextDirty || session.ScriptTextDirty || session.PlacementDirty;
    private static bool IsRSceneSessionDirty(RSceneEditorSession session) => session.StructureDirty || session.DraftDirty;

    private static string BuildScriptSessionSummary(ScriptEditorSession session)
    {
        var parts = new List<string>();
        if (session.StructureDirty) parts.Add("结构改动");
        if (session.TextDirty) parts.Add("文本改动");
        return parts.Count == 0 ? "有未保存改动" : string.Join("、", parts);
    }

    private static string BuildBattlefieldSessionSummary(BattlefieldEditorSession session)
    {
        var parts = new List<string>();
        if (session.TextDirty) parts.Add("标题/胜败条件");
        if (session.ScriptTextDirty) parts.Add("S剧本文本");
        if (session.StructureDirty) parts.Add("S剧本结构");
        if (session.PlacementDirty) parts.Add("布阵草稿");
        return parts.Count == 0 ? "有未保存改动" : string.Join("、", parts);
    }

    private static string BuildRSceneSessionSummary(RSceneEditorSession session)
    {
        var parts = new List<string>();
        if (session.StructureDirty) parts.Add("R剧本结构");
        if (session.DraftDirty) parts.Add("场景草稿");
        return parts.Count == 0 ? "有未保存改动" : string.Join("、", parts);
    }

    private async Task<(bool Success, string? FailedItem, string? Error)> SaveAllUnsavedItemsAsync(IReadOnlyList<UnsavedEditorItem> items)
    {
        foreach (var item in items)
        {
            try
            {
                await item.SaveAsync();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("Save all unsaved item failed: " + ex);
                return (false, item.DisplayText, ex.Message);
            }
        }

        return (true, null, null);
    }

    private void DiscardAllUnsavedItems(IReadOnlyList<UnsavedEditorItem> items)
    {
        _discardingUnsavedChangesForClose = true;
        try
        {
            foreach (var item in items)
            {
                try
                {
                    item.Discard?.Invoke();
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine("Discard unsaved item failed: " + ex.Message);
                }
            }

            _scriptEditorSessions.Clear();
            _battlefieldEditorSessions.Clear();
            _rSceneEditorSessions.Clear();
        }
        finally
        {
            _discardingUnsavedChangesForClose = false;
        }
    }

    private async Task<bool> ConfirmUnsavedChangesBeforeCloseAsync()
    {
        var items = CollectUnsavedItems();
        if (items.Count == 0) return true;

        var choice = ShowUnsavedChangesDialog(items);
        switch (choice)
        {
            case UnsavedCloseChoice.DiscardAndClose:
                DiscardAllUnsavedItems(items);
                return true;
            case UnsavedCloseChoice.CancelClose:
                return false;
            case UnsavedCloseChoice.SaveAll:
                var result = await SaveAllUnsavedItemsAsync(items);
                if (result.Success) return true;
                MessageBox.Show(
                    this,
                    $"一键保存失败，窗口已保持打开。\r\n\r\n失败项：{result.FailedItem}\r\n错误：{result.Error}",
                    "未保存内容保存失败",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
                return false;
            default:
                return false;
        }
    }

    private UnsavedCloseChoice ShowUnsavedChangesDialog(IReadOnlyList<UnsavedEditorItem> items)
    {
        using var dialog = new Form
        {
            Text = "存在未保存内容",
            StartPosition = FormStartPosition.CenterParent,
            MinimizeBox = false,
            MaximizeBox = false,
            ShowInTaskbar = false,
            FormBorderStyle = FormBorderStyle.FixedDialog,
            Width = 620,
            Height = 420,
            Font = Font
        };

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            RowCount = 3,
            ColumnCount = 1,
            Padding = new Padding(12)
        };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        dialog.Controls.Add(root);

        root.Controls.Add(new Label
        {
            AutoSize = true,
            Dock = DockStyle.Fill,
            Text = $"检测到 {items.Count} 项未保存内容。请选择关闭前的处理方式。",
            Padding = new Padding(0, 0, 0, 8)
        }, 0, 0);

        var list = new ListBox
        {
            Dock = DockStyle.Fill,
            HorizontalScrollbar = true
        };
        list.Items.AddRange(items.Select(item => item.DisplayText).Cast<object>().ToArray());
        root.Controls.Add(list, 0, 1);

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            Padding = new Padding(0, 10, 0, 0)
        };
        root.Controls.Add(buttons, 0, 2);
#if false

        var cancelCloseButton = new Button { Text = "取消关闭", AutoSize = true, DialogResult = DialogResult.Cancel };
        var discardButton = new Button { Text = "放弃改动", AutoSize = true, DialogResult = DialogResult.No };
        var saveButton = new Button { Text = "保存全部", AutoSize = true, DialogResult = DialogResult.Yes };
#endif
        var cancelCloseButton = new Button { Text = "取消关闭", AutoSize = true, DialogResult = DialogResult.Cancel };
        var discardButton = new Button { Text = "放弃改动", AutoSize = true, DialogResult = DialogResult.No };
        var saveButton = new Button { Text = "保存全部", AutoSize = true, DialogResult = DialogResult.Yes };
        buttons.Controls.AddRange(new Control[] { cancelCloseButton, discardButton, saveButton });
        dialog.AcceptButton = saveButton;
        dialog.CancelButton = cancelCloseButton;

        return dialog.ShowDialog(this) switch
        {
            DialogResult.Yes => UnsavedCloseChoice.SaveAll,
            DialogResult.No => UnsavedCloseChoice.DiscardAndClose,
            _ => UnsavedCloseChoice.CancelClose
        };
    }

    private void SyncSelectedRoleTextDetailsIntoTables()
    {
        if (_project == null || _roleEditorGrid.CurrentRow == null ||
            _roleBiographyRead == null || _roleCriticalQuoteRead == null || _roleRetreatQuoteRead == null)
        {
            return;
        }

        var roleRow = TryGetDataRow(_roleEditorGrid.CurrentRow);
        if (roleRow == null) return;

        var roleId = Convert.ToInt32(roleRow["ID"], CultureInfo.InvariantCulture);
        var bioRow = FindRowById(_roleBiographyRead.Data, roleId);
        var criticalMapping = _roleQuoteMappingService.ResolveCriticalQuote(_project, roleRow, _roleCriticalQuoteRead.Data);
        var retreatMapping = _roleQuoteMappingService.ResolveRetreatQuote(roleRow, _roleRetreatQuoteRead.Data);

        bioRow["浠嬬粛"] = _roleBiographyBox.Text;
        ApplyCriticalQuoteEditorToRows(criticalMapping);

        if (retreatMapping.QuoteRow != null)
        {
            retreatMapping.QuoteRow["浠嬬粛"] = _roleRetreatQuoteBox.Text;
        }
    }

    private Task SaveCurrentTableSilentlyAsync()
    {
        if (_project == null || _currentTableResult == null) return Task.CompletedTask;
        if (!CanEditTable(_currentTableResult)) throw new InvalidOperationException("当前表不允许直接保存。");
        if (!HasChanges(_currentTableResult.Data)) return Task.CompletedTask;

        var savedTable = _currentTableResult.Table;
        var savedData = _currentTableResult.Data;
        var changedRows = GetChangedRows(savedData);
        var changedCells = GetChangedCellKeys(savedData);
        var viewport = CaptureGridViewport(_dataGrid);
        var result = _tableWriter.Save(_project, savedTable, savedData);
        var verifyRead = _tableReader.Read(_project, savedTable, _tables);
        if (!verifyRead.Validation.IsUsable)
        {
            throw new InvalidOperationException("保存后重新读取失败，请查看诊断和备份。");
        }

        VerifySavedTableMatchesCurrentData(savedTable, savedData, verifyRead.Data, changedRows);
        SyncVerifiedCellsByKey(savedData, verifyRead.Data, changedCells);
        savedData.AcceptChanges();
        RefreshChangedGridCells(_dataGrid, changedCells);
        RefreshChangedGridRowsOnly(_dataGrid, changedCells, RefreshDataGridRowStyle);
        RestoreGridViewport(_dataGrid, viewport);
        ClearGridEditSession(_dataGrid);
        ConfigureChartColumns(savedData);
        SetStatus($"数据表已保存：{savedTable.TableName}，变化 {result.ChangedBytes} 字节");
        return Task.CompletedTask;
    }

    private Task SaveRoleEditorSilentlyAsync()
    {
        if (_project == null || _currentRoleEditorData == null || !HasChanges(_currentRoleEditorData)) return Task.CompletedTask;
        var changedCells = GetChangedCellKeys(_currentRoleEditorData);
        var saves = SaveRoleEditorData(_project, _tables, _currentRoleEditorData);
        AcceptSavedDataTable(_currentRoleEditorData);
        RefreshRoleEditorCellsAfterEdit(changedCells);
        SetStatus($"角色设定已保存：{saves.Sum(x => x.ChangedBytes)} 字节变化");
        return Task.CompletedTask;
    }

    private Task SaveRoleTextDetailsSilentlyAsync()
    {
        if (_project == null || _roleBiographyRead == null || _roleCriticalQuoteRead == null || _roleRetreatQuoteRead == null) return Task.CompletedTask;
        var saves = SaveRoleTextDetailsCore();
        if (_roleEditorGrid.CurrentRow != null && TryGetDataRow(_roleEditorGrid.CurrentRow) is { } roleRow) ShowRoleTextDetails(roleRow);
        SetStatus($"角色文本已保存：{saves.Sum(x => x.ChangedBytes)} 字节变化");
        return Task.CompletedTask;
    }

    private Task SaveJobEditorSilentlyAsync()
    {
        if (_project == null || _currentJobEditorData == null) return Task.CompletedTask;
        if (!CommitJobDescriptionBoxEdit())
        {
            SetStatus("兵种介绍存在超长文本，已取消自动保存。");
            return Task.CompletedTask;
        }

        CommitJobEquipmentEditorChanges();
        if (!HasChanges(_currentJobEditorData)) return Task.CompletedTask;
        var changedCells = GetChangedCellKeys(_currentJobEditorData);
        var saves = SaveJobEditorData(_project, _currentJobEditorData);
        AcceptSavedDataTable(_currentJobEditorData);
        RefreshJobEditorCellsAfterCsvImport(changedCells);
        SetStatus($"兵种设定已保存：{saves.Sum(x => x.ChangedBytes)} 字节变化");
        return Task.CompletedTask;
    }

    private Task SaveJobTerrainEditorSilentlyAsync()
    {
        if (_project == null || _currentJobTerrainData == null || !HasChanges(_currentJobTerrainData)) return Task.CompletedTask;
        var changedCells = GetChangedCellKeys(_currentJobTerrainData);
        var saves = SaveJobTerrainEditorData(_project, _currentJobTerrainData);
        AcceptSavedDataTable(_currentJobTerrainData);
        RefreshJobTerrainCellsAfterEdit(changedCells);
        SetStatus($"兵种系/地形已保存：{saves.Sum(x => x.ChangedBytes)} 字节变化");
        return Task.CompletedTask;
    }

    private Task SaveJobMatrixEditorSilentlyAsync()
    {
        if (_project == null || _jobRestraintRead == null || !HasChanges(_jobRestraintRead.Data)) return Task.CompletedTask;
        var changedCells = GetChangedCellKeys(_jobRestraintRead.Data);
        var result = SaveChangedTableAndVerify(_jobRestraintRead)!;
        RefreshJobMatrixCellsAfterEdit(changedCells);
        SetStatus($"兵种相克矩阵已保存：{result.ChangedBytes} 字节变化");
        return Task.CompletedTask;
    }

    private Task SaveJobStrategyEditorSilentlyAsync()
    {
        if (_project == null || _currentJobStrategyData == null) return Task.CompletedTask;
        if (!CommitJobStrategyLearningEditorChanges(showMessage: false, restoreSelectionOnFailure: true)) throw new InvalidOperationException("策略学习等级右侧编辑区仍有无效改动，无法批量保存。");
        if (!CommitJobStrategyLearningDialogs()) throw new InvalidOperationException("策略学习等级仍有无效改动，无法批量保存。");
        if (!HasChanges(_currentJobStrategyData)) return Task.CompletedTask;
        var changedCells = GetChangedCellKeys(_currentJobStrategyData);
        var saves = SaveJobStrategyEditorData(_project, _currentJobStrategyData);
        AcceptSavedDataTable(_currentJobStrategyData);
        RefreshJobStrategyCellsAfterEdit(changedCells);
        SetStatus($"策略设定已保存：{saves.Sum(x => x.ChangedBytes)} 字节变化");
        return Task.CompletedTask;
    }

    private Task SaveJobEffectEditorSilentlyAsync()
    {
        if (_project == null || _currentJobEffectData == null || !HasChanges(_currentJobEffectData)) return Task.CompletedTask;
        var changedCells = GetChangedCellKeys(_currentJobEffectData);
        var saves = SaveJobEffectEditorData(_project, _currentJobEffectData);
        AcceptSavedDataTable(_currentJobEffectData);
        RefreshJobEffectCellsAfterEdit(changedCells);
        SetStatus($"兵种特效已保存：{saves.Sum(x => x.ChangedBytes)} 字节变化");
        return Task.CompletedTask;
    }

    private Task SaveItemEditorSilentlyAsync()
    {
        if (_project == null || _currentItemEditorData == null || !HasChanges(_currentItemEditorData)) return Task.CompletedTask;
        var changedCells = GetChangedCellKeys(_currentItemEditorData);
        var saves = SaveItemEditorData(_project, _currentItemEditorData);
        AcceptSavedDataTable(_currentItemEditorData);
        RefreshChangedGridCells(_itemEditorGrid, changedCells, UpdateItemEditorDerivedCells);
        RefreshChangedGridRowsOnly(_itemEditorGrid, changedCells, RefreshItemEditorRowStyle);
        ShowSelectedItemEditorCell();
        SetStatus($"宝物/物品已保存：{saves.Sum(x => x.ChangedBytes)} 字节变化");
        return Task.CompletedTask;
    }

    private Task SaveShopEditorSilentlyAsync()
    {
        if (_project == null || _currentShopEditorData == null || !HasChanges(_currentShopEditorData)) return Task.CompletedTask;
        var changedCells = GetChangedCellKeys(_currentShopEditorData);
        var saves = SaveShopEditorData(_project, _currentShopEditorData);
        AcceptSavedDataTable(_currentShopEditorData);
        RefreshShopEditorCellsAfterEdit(changedCells);
        SetStatus($"商店已保存：{saves.Sum(x => x.ChangedBytes)} 字节变化");
        return Task.CompletedTask;
    }

    private Task SaveImageAssignmentsSilentlyAsync()
    {
        if (_project == null || _currentImageAssignments == null || !HasChanges(_currentImageAssignments)) return Task.CompletedTask;
        var changedCells = GetChangedCellKeys(_currentImageAssignments);
        var result = _imageAssignmentService.Save(_project, _tables, _currentImageAssignments);
        RefreshImageAssignmentCellsAfterEdit(changedCells);
        SetStatus($"人物形象设定已保存：{result.ChangedBytes} 字节变化");
        return Task.CompletedTask;
    }

    private Task SaveMapWorkbenchDraftSilentlyAsync()
    {
        if (_project == null || _currentMapWorkbenchDraft == null) return Task.CompletedTask;
        SyncMapWorkbenchDraftFromEditor();
        _mapDraftService.SaveDraft(_project, _currentMapWorkbenchDraft);
        _mapWorkbenchSettings.LastDraftId = _currentMapWorkbenchDraft.DraftId;
        _mapWorkbenchSettings.LastBoundMapId = _currentMapWorkbenchDraft.BoundMapId;
        _mapWorkbenchSettings.LastMaterialRoot = _currentMapWorkbenchDraft.MaterialRoot;
        PersistCurrentTerrainMaterialPlan();
        SaveMapWorkbenchSettings();
        ResetMapWorkbenchHistory();
        SetStatus("地图工作台草稿已保存");
        return Task.CompletedTask;
    }

    private Task SaveScenarioTextsSilentlyAsync()
    {
        if (_project == null) return Task.CompletedTask;
        var item = GetSelectedScenarioFileItem() ?? throw new InvalidOperationException("没有选中的 R/S eex 文件。");
        var changed = GetScenarioTextEntriesFromGrid()
            .Where(IsScenarioTextChanged)
            .ToList();
        if (changed.Count == 0) return Task.CompletedTask;

        var validationErrors = changed
            .Select(entry => new { Entry = entry, Error = ValidateScenarioTextValue(entry, NormalizeScenarioTextForSave(entry.Text)) })
            .Where(x => x.Error != null)
            .Take(10)
            .Select(x => $"#{x.Entry.Index} {x.Entry.OffsetHex}: {x.Error}")
            .ToList();
        if (validationErrors.Count > 0)
        {
            throw new InvalidOperationException("存在无法保存的文本：" + string.Join("; ", validationErrors));
        }

        var relativePath = Path.Combine("RS", item.FileName);
        var result = _scenarioTextWriter.SaveInPlace(_project, relativePath, changed, "R/S eex 文本批量保存前自动备份");
        var reread = _scenarioTextReader.Read(result.FilePath);
        VerifyScenarioTextSave(changed, reread);
        MarkScenarioTextEntriesSaved(changed);
        RefreshScenarioTextRows(changed);
        _exportScenarioTextsButton.Enabled = _currentScenarioTextEntries.Count > 0;
        _saveScenarioTextsButton.Enabled = true;
        _scenarioTextFilterButton.Enabled = _currentScenarioTextEntries.Count > 0;
        _scenarioTextFilterClearButton.Enabled = _currentScenarioTextEntries.Count > 0;
        _scenarioTextChangedOnly.Enabled = _currentScenarioTextEntries.Count > 0;
        SetStatus($"剧本文本已保存：{result.ChangedBytes} 字节变化");
        return Task.CompletedTask;
    }
}
