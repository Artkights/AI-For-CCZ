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

    private readonly Dictionary<string, ScriptEditorSession> _scriptEditorSessions = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, BattlefieldEditorSession> _battlefieldEditorSessions = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, RSceneEditorSession> _rSceneEditorSessions = new(StringComparer.OrdinalIgnoreCase);
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

        if (_currentTableResult != null && CanEditTable(_currentTableResult) && HasChanges(_currentTableResult.Data))
        {
            var tableName = _currentTableResult.Table.TableName;
            items.Add(new UnsavedEditorItem
            {
                Page = "鏁版嵁琛ㄧ紪杈?",
                Target = tableName,
                Summary = BuildDataTableChangeSummary(_currentTableResult.Data),
                SaveAsync = () => SaveCurrentTableSilentlyAsync(),
                Discard = () => LoadSelectedTable()
            });
        }

        AddDataTableItem(items, "瑙掕壊璁惧畾", "浜虹墿/R/S", _currentRoleEditorData, () => SaveRoleEditorSilentlyAsync(), () => LoadRoleEditor());
        AddDataTableItem(items, "瑙掕壊璁惧畾", "鍒椾紶/鍙拌瘝", GetRoleTextCombinedChanges(), () => SaveRoleTextDetailsSilentlyAsync(), () => { LoadRoleTextTables(); if (_roleEditorGrid.CurrentRow != null && TryGetDataRow(_roleEditorGrid.CurrentRow) is { } row) ShowRoleTextDetails(row); });
        AddDataTableItem(items, "鍏电璁惧畾", "璇︾粏鍏电", _currentJobEditorData, () => SaveJobEditorSilentlyAsync(), () => LoadJobEditor());
        AddDataTableItem(items, "鍏电璁惧畾", "鍏电绯?鍦板舰", _currentJobTerrainData, () => SaveJobTerrainEditorSilentlyAsync(), () => LoadJobTerrainEditor());
        AddDataTableItem(items, "鍏电璁惧畾", "鍏电鐩稿厠鐭╅樀", _jobRestraintRead?.Data, () => SaveJobMatrixEditorSilentlyAsync(), () => LoadJobMatrixEditor());
        AddDataTableItem(items, "鍏电璁惧畾", "绛栫暐", _currentJobStrategyData, () => SaveJobStrategyEditorSilentlyAsync(), () => LoadJobStrategyEditor());
        AddDataTableItem(items, "鍏电璁惧畾", "鍏电鐗规晥", _currentJobEffectData, () => SaveJobEffectEditorSilentlyAsync(), () => LoadJobEffectEditor());
        AddDataTableItem(items, "瀹濈墿璁惧畾", "瀹濈墿/鐗╁搧", _currentItemEditorData, () => SaveItemEditorSilentlyAsync(), () => LoadItemEditor());
        AddDataTableItem(items, "鍟嗗簵缂栬緫", "鍟嗗簵", _currentShopEditorData, () => SaveShopEditorSilentlyAsync(), () => LoadShopEditor());
        AddDataTableItem(items, "鍥剧墖璁惧畾", "浜虹墿R/S鎸囧畾", _currentImageAssignments, () => SaveImageAssignmentsSilentlyAsync(), () => LoadImageAssignments());
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
        return changed <= 0 ? "鏈夋湭淇濆瓨鏀瑰姩" : $"{changed} 琛屾敼鍔?";
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
        var target = GetSelectedScenarioFileItem()?.FileName ?? "R/S 鏂囨湰";
        items.Add(new UnsavedEditorItem
        {
            Page = "楂樼骇鎺㈤拡",
            Target = target,
            Summary = $"{changed.Count} 鏉℃枃鏈敼鍔?",
            SaveAsync = () => SaveScenarioTextsSilentlyAsync(),
            Discard = () => ProbeSelectedScenarioTexts()
        });
    }

    private void AddCurrentMapWorkbenchUnsavedItem(List<UnsavedEditorItem> items)
    {
        if (_project == null || _currentMapWorkbenchDraft == null || !IsCurrentMapWorkbenchDraftDirty()) return;
        items.Add(new UnsavedEditorItem
        {
            Page = "鍦板浘缂栬緫",
            Target = string.IsNullOrWhiteSpace(_currentMapWorkbenchDraft.BoundMapId) ? _currentMapWorkbenchDraft.DraftId : _currentMapWorkbenchDraft.BoundMapId,
            Summary = "鍦板浘宸ヤ綔鍙拌崏绋挎湭淇濆瓨",
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
                Page = "鍓ф湰缂栬緫",
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
                Page = "鎴樺満缂栬緫",
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
                Page = "鍦烘櫙缂栬緫",
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
        if (session.StructureDirty) parts.Add("缁撴瀯鏀瑰姩");
        if (session.TextDirty) parts.Add("鏂囨湰鏀瑰姩");
        return parts.Count == 0 ? "鏈夋湭淇濆瓨鏀瑰姩" : string.Join("銆?, parts");
    }

    private static string BuildBattlefieldSessionSummary(BattlefieldEditorSession session)
    {
        var parts = new List<string>();
        if (session.TextDirty) parts.Add("鏍囬/鑳滆触鏉′欢");
        if (session.ScriptTextDirty) parts.Add("S鍓ф湰鏂囨湰");
        if (session.StructureDirty) parts.Add("S鍓ф湰缁撴瀯");
        if (session.PlacementDirty) parts.Add("甯冮樀鑽夌");
        return parts.Count == 0 ? "鏈夋湭淇濆瓨鏀瑰姩" : string.Join("銆?, parts");
    }

    private static string BuildRSceneSessionSummary(RSceneEditorSession session)
    {
        var parts = new List<string>();
        if (session.StructureDirty) parts.Add("R鍓ф湰缁撴瀯");
        if (session.DraftDirty) parts.Add("鍦烘櫙鑽夌");
        return parts.Count == 0 ? "鏈夋湭淇濆瓨鏀瑰姩" : string.Join("銆?, parts");
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
                    $"涓€閿繚瀛樺け璐ワ紝绐楀彛宸蹭繚鎸佹墦寮€銆俓r\n\r\n澶辫触椤癸細{result.FailedItem}\r\n閿欒锛歿result.Error}}",
                    "鏈繚瀛樺唴瀹逛繚瀛樺け璐?",
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
            Text = "瀛樺湪鏈繚瀛樺唴瀹?",
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
            Text = $"妫€娴嬪埌 {items.Count} 椤规湭淇濆瓨鍐呭銆傝閫夋嫨鍏抽棴鍓嶇殑澶勭悊鏂瑰紡銆?",
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

        var cancelCloseButton = new Button { Text = "鍙栨秷鍏抽棴", AutoSize = true, DialogResult = DialogResult.Cancel };
        var discardButton = new Button { Text = "涓€閿彇娑?, AutoSize = true, DialogResult = DialogResult.No }";
        var saveButton = new Button { Text = "涓€閿繚瀛?, AutoSize = true, DialogResult = DialogResult.Yes }";
#endif
        var cancelCloseButton = new Button { Text = "Cancel", AutoSize = true, DialogResult = DialogResult.Cancel };
        var discardButton = new Button { Text = "Discard", AutoSize = true, DialogResult = DialogResult.No };
        var saveButton = new Button { Text = "Save", AutoSize = true, DialogResult = DialogResult.Yes };
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
        if (!CanEditTable(_currentTableResult)) throw new InvalidOperationException("褰撳墠琛ㄤ笉鍏佽鐩存帴淇濆瓨銆?");
        if (!HasChanges(_currentTableResult.Data)) return Task.CompletedTask;

        var savedTable = _currentTableResult.Table;
        var result = _tableWriter.Save(_project, savedTable, _currentTableResult.Data);
        var verifyRead = _tableReader.Read(_project, savedTable, _tables);
        if (!verifyRead.Validation.IsUsable)
        {
            throw new InvalidOperationException("淇濆瓨鍚庨噸鏂拌鍙栧け璐ワ紝璇锋煡鐪嬭瘖鏂拰澶囦唤銆?");
        }

        _currentTableResult = verifyRead;
        _dataGrid.DataSource = verifyRead.Data;
        ConfigureDataGrid(verifyRead);
        ConfigureChartColumns(verifyRead.Data);
        SetStatus($"鏁版嵁琛ㄥ凡淇濆瓨锛歿savedTable.TableName}}锛屽彉鍖?{result.ChangedBytes} 瀛楄妭");
        return Task.CompletedTask;
    }

    private Task SaveRoleEditorSilentlyAsync()
    {
        if (_project == null || _currentRoleEditorData == null || !HasChanges(_currentRoleEditorData)) return Task.CompletedTask;
        var saves = SaveRoleEditorData(_project, _tables, _currentRoleEditorData);
        LoadRoleEditor();
        SetStatus($"瑙掕壊璁惧畾宸蹭繚瀛橈細{saves.Sum(x => x.ChangedBytes)} 瀛楄妭鍙樺寲");
        return Task.CompletedTask;
    }

    private Task SaveRoleTextDetailsSilentlyAsync()
    {
        if (_project == null || _roleBiographyRead == null || _roleCriticalQuoteRead == null || _roleRetreatQuoteRead == null) return Task.CompletedTask;
        var saves = new List<TableSaveResult>();
        if (HasChanges(_roleBiographyRead.Data)) saves.Add(_tableWriter.Save(_project, _roleBiographyRead.Table, _roleBiographyRead.Data));
        if (HasChanges(_roleCriticalQuoteRead.Data)) saves.Add(_tableWriter.Save(_project, _roleCriticalQuoteRead.Table, _roleCriticalQuoteRead.Data));
        if (HasChanges(_roleRetreatQuoteRead.Data)) saves.Add(_tableWriter.Save(_project, _roleRetreatQuoteRead.Table, _roleRetreatQuoteRead.Data));
        LoadRoleTextTables();
        if (_roleEditorGrid.CurrentRow != null && TryGetDataRow(_roleEditorGrid.CurrentRow) is { } roleRow) ShowRoleTextDetails(roleRow);
        SetStatus($"瑙掕壊鏂囨湰宸蹭繚瀛橈細{saves.Sum(x => x.ChangedBytes)} 瀛楄妭鍙樺寲");
        return Task.CompletedTask;
    }

    private Task SaveJobEditorSilentlyAsync()
    {
        if (_project == null || _currentJobEditorData == null || !HasChanges(_currentJobEditorData)) return Task.CompletedTask;
        var saves = SaveJobEditorData(_project, _currentJobEditorData);
        LoadJobEditor();
        SetStatus($"鍏电璁惧畾宸蹭繚瀛橈細{saves.Sum(x => x.ChangedBytes)} 瀛楄妭鍙樺寲");
        return Task.CompletedTask;
    }

    private Task SaveJobTerrainEditorSilentlyAsync()
    {
        if (_project == null || _currentJobTerrainData == null || !HasChanges(_currentJobTerrainData)) return Task.CompletedTask;
        var saves = SaveJobTerrainEditorData(_project, _currentJobTerrainData);
        LoadJobTerrainEditor();
        SetStatus($"鍏电绯?鍦板舰宸蹭繚瀛橈細{saves.Sum(x => x.ChangedBytes)} 瀛楄妭鍙樺寲");
        return Task.CompletedTask;
    }

    private Task SaveJobMatrixEditorSilentlyAsync()
    {
        if (_project == null || _jobRestraintRead == null || !HasChanges(_jobRestraintRead.Data)) return Task.CompletedTask;
        var result = _tableWriter.Save(_project, _jobRestraintRead.Table, _jobRestraintRead.Data);
        LoadJobMatrixEditor();
        SetStatus($"鍏电鐩稿厠鐭╅樀宸蹭繚瀛橈細{result.ChangedBytes} 瀛楄妭鍙樺寲");
        return Task.CompletedTask;
    }

    private Task SaveJobStrategyEditorSilentlyAsync()
    {
        if (_project == null || _currentJobStrategyData == null) return Task.CompletedTask;
        if (!CommitJobStrategyLearningDialogs()) throw new InvalidOperationException("绛栫暐瀛︿範寮圭獥浠嶆湁鏃犳晥鏀瑰姩锛屾棤娉曟壒閲忎繚瀛樸€?");
        if (!HasChanges(_currentJobStrategyData)) return Task.CompletedTask;
        var saves = SaveJobStrategyEditorData(_project, _currentJobStrategyData);
        LoadJobStrategyEditor();
        SetStatus($"绛栫暐璁惧畾宸蹭繚瀛橈細{saves.Sum(x => x.ChangedBytes)} 瀛楄妭鍙樺寲");
        return Task.CompletedTask;
    }

    private Task SaveJobEffectEditorSilentlyAsync()
    {
        if (_project == null || _currentJobEffectData == null || !HasChanges(_currentJobEffectData)) return Task.CompletedTask;
        var saves = SaveJobEffectEditorData(_project, _currentJobEffectData);
        LoadJobEffectEditor();
        SetStatus($"鍏电鐗规晥宸蹭繚瀛橈細{saves.Sum(x => x.ChangedBytes)} 瀛楄妭鍙樺寲");
        return Task.CompletedTask;
    }

    private Task SaveItemEditorSilentlyAsync()
    {
        if (_project == null || _currentItemEditorData == null || !HasChanges(_currentItemEditorData)) return Task.CompletedTask;
        var saves = SaveItemEditorData(_project, _currentItemEditorData);
        LoadItemEditor();
        SetStatus($"瀹濈墿/鐗╁搧宸蹭繚瀛橈細{saves.Sum(x => x.ChangedBytes)} 瀛楄妭鍙樺寲");
        return Task.CompletedTask;
    }

    private Task SaveShopEditorSilentlyAsync()
    {
        if (_project == null || _currentShopEditorData == null || !HasChanges(_currentShopEditorData)) return Task.CompletedTask;
        var saves = SaveShopEditorData(_project, _currentShopEditorData);
        LoadShopEditor();
        SetStatus($"鍟嗗簵宸蹭繚瀛橈細{saves.Sum(x => x.ChangedBytes)} 瀛楄妭鍙樺寲");
        return Task.CompletedTask;
    }

    private Task SaveImageAssignmentsSilentlyAsync()
    {
        if (_project == null || _currentImageAssignments == null || !HasChanges(_currentImageAssignments)) return Task.CompletedTask;
        var result = _imageAssignmentService.Save(_project, _tables, _currentImageAssignments);
        _currentImageAssignments = _imageAssignmentService.Load(_project, _tables);
        _imageAssignmentGrid.DataSource = _currentImageAssignments;
        ColorImageAssignmentResourceRows();
        ShowSelectedImageAssignmentDetail();
        SetStatus($"浜虹墿R/S宸蹭繚瀛橈細{result.ChangedBytes} 瀛楄妭鍙樺寲");
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
        SetStatus("鍦板浘宸ヤ綔鍙拌崏绋垮凡淇濆瓨");
        return Task.CompletedTask;
    }

    private Task SaveScenarioTextsSilentlyAsync()
    {
        if (_project == null) return Task.CompletedTask;
        var item = GetSelectedScenarioFileItem() ?? throw new InvalidOperationException("娌℃湁閫変腑鐨?R/S eex 鏂囦欢銆?");
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
            throw new InvalidOperationException("瀛樺湪鏃犳硶淇濆瓨鐨勬枃鏈細" + string.Join("; ", validationErrors));
        }

        var relativePath = Path.Combine("RS", item.FileName);
        var result = _scenarioTextWriter.SaveInPlace(_project, relativePath, changed, "R/S eex 鏂囨湰鎵归噺淇濆瓨鍓嶈嚜鍔ㄥ浠?");
        var reread = _scenarioTextReader.Read(result.FilePath);
        VerifyScenarioTextSave(changed, reread);
        _currentScenarioTextEntries = reread;
        BindScenarioTextEntries(_currentScenarioTextEntries);
        _exportScenarioTextsButton.Enabled = _currentScenarioTextEntries.Count > 0;
        _saveScenarioTextsButton.Enabled = true;
        _scenarioTextFilterButton.Enabled = _currentScenarioTextEntries.Count > 0;
        _scenarioTextFilterClearButton.Enabled = _currentScenarioTextEntries.Count > 0;
        _scenarioTextChangedOnly.Enabled = _currentScenarioTextEntries.Count > 0;
        SetStatus($"鍓ф湰鏂囨湰宸蹭繚瀛橈細{result.ChangedBytes} 瀛楄妭鍙樺寲");
        return Task.CompletedTask;
    }
}
