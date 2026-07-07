using CCZModStudio.Core;
using CCZModStudio.Models;
using System.Globalization;
using System.Windows.Forms;

namespace CCZModStudio;

public sealed partial class MainForm
{
    private bool _unsavedCloseConfirmed;
    private bool _unsavedClosePromptRunning;

    private void BeginCloseAfterUnsavedCheck()
    {
        var items = CollectUnsavedItems();
        if (items.Count > 0)
        {
            BeginCloseAfterUnsavedCheck(items);
            return;
        }

        _unsavedCloseConfirmed = true;
        Close();
    }

    private void BeginCloseAfterUnsavedCheck(IReadOnlyList<UnsavedEditorItem> items)
    {
        if (_unsavedClosePromptRunning) return;
        _unsavedClosePromptRunning = true;
        BeginInvoke(async () =>
        {
            try
            {
                if (await ConfirmUnsavedItemsBeforeCloseAsync(items))
                {
                    _unsavedCloseConfirmed = true;
                    Close();
                }
            }
            finally
            {
                _unsavedClosePromptRunning = false;
            }
        });
    }

    private void ClearEditorSessionCaches()
    {
        _scriptEditorSessions.Clear();
        _battlefieldEditorSessions.Clear();
        _rSceneEditorSessions.Clear();
        _tableEditorSessions.Clear();
        _gridEditSessions.Clear();
        SetLegacyStructureDirtyFlag(LegacyScriptEditorScope.Script, false);
        SetLegacyStructureDirtyFlag(LegacyScriptEditorScope.Battlefield, false);
        SetLegacyStructureDirtyFlag(LegacyScriptEditorScope.RScene, false);
    }

    private void CacheCurrentScriptEditorSession()
    {
        if (_discardingUnsavedChangesForClose || _project == null || _currentScriptScenario == null || _currentScriptStructure == null) return;
        CommitScriptTextEditorToCurrentEntry();

        var key = BuildEditorSessionKey("script", _currentScriptScenario);
        if (string.IsNullOrWhiteSpace(key)) return;

        _scriptEditorSessions[key] = new ScriptEditorSession
        {
            ProjectRoot = Path.GetFullPath(_project.GameRoot),
            Scenario = _currentScriptScenario,
            LegacyDocument = _currentLegacyScriptDocument,
            ProbeStructure = _currentScriptStructure,
            TextEntries = _currentScriptTextEntries,
            SelectedTextOffset = _selectedScriptTextEntry?.Offset,
            TextEditorText = _scriptTextEditorBox.Text,
            Viewport = CaptureLegacyScriptViewport(LegacyScriptEditorScope.Script),
            SearchText = _scriptSearchBox.Text,
            SearchResultIndex = _currentScriptSearchResultIndex,
            StructureDirty = IsLegacyStructureDirty(LegacyScriptEditorScope.Script),
            TextDirty = HasChangedScenarioTextEntries(_currentScriptTextEntries)
        };
    }

    private bool TryRestoreScriptEditorSession(ScenarioFileInfo scenario)
    {
        var key = BuildEditorSessionKey("script", scenario);
        if (string.IsNullOrWhiteSpace(key) || !_scriptEditorSessions.TryGetValue(key, out var session)) return false;
        RestoreScriptEditorSession(session);
        return true;
    }

    private void RestoreScriptEditorSession(ScriptEditorSession session)
    {
        SelectScriptScenarioComboSilently(session.Scenario);
        _bindingScriptDocument = true;
        try
        {
            _currentScriptScenario = session.Scenario;
            _selectedScriptCommandRow = null;
            _selectedScriptTextEntry = null;
            _currentLegacyScriptDocument = session.LegacyDocument;
            ClearLegacyScenarioHistory(LegacyScriptEditorScope.Script);
            SetLegacyStructureDirtyFlag(LegacyScriptEditorScope.Script, session.StructureDirty);

            if (_currentLegacyScriptDocument != null)
            {
                _currentScriptStructure = BuildLegacyScriptStructureResult(_currentLegacyScriptDocument);
                BuildLegacyScriptTextEntries(_currentLegacyScriptDocument);
                _currentScriptTextEntries = session.TextEntries;
                RebindLegacyScriptTextEntryMap(_legacyScriptTextEntryByOffset, _currentScriptTextEntries);
            }
            else
            {
                _legacyScriptCommandByKey.Clear();
                _legacyScriptRowByKey.Clear();
                _legacyScriptItemDataByCommand.Clear();
                _legacyScriptItemDataByRow.Clear();
                _legacyScriptTextByOffset.Clear();
                _legacyScriptTextEntryByOffset.Clear();
                _currentScriptStructure = session.ProbeStructure;
                _currentScriptTextEntries = session.TextEntries;
            }

            if (_currentScriptStructure == null) return;
            BuildScriptTree(_currentScriptStructure, _currentScriptTextEntries);
            BindScriptCommandRows(Array.Empty<ScenarioStructureRow>());
            BindScriptParameterRows(Array.Empty<ScenarioCommandParameterRow>());
            BindScriptTextRows(_currentScriptTextEntries);
            RestoreLegacyScriptSearch(LegacyScriptEditorScope.Script, session.SearchText, session.SearchResultIndex);
            _scriptTextEditorBox.Clear();
            _saveScriptTextButton.Enabled = false;
            _saveScriptStructureButton.Enabled = _currentLegacyScriptDocument != null && session.StructureDirty;
            _jumpScriptBattlefieldButton.Enabled = true;
            _showScriptVariablesButton.Enabled = _currentLegacyScriptDocument != null;
            _locateScriptCommandButton.Enabled = true;
            _copyScriptCommandButton.Enabled = true;
            _previewPasteScriptCommandButton.Enabled = _scriptCommandClipboardItem != null ||
                                                       _legacyScriptCommandClipboardItems.Count > 0 ||
                                                       _legacyScriptSectionClipboardItems.Count > 0;
            UpdateScriptStructureEditButtons();
        }
        finally
        {
            _bindingScriptDocument = false;
        }

        RestoreLegacyScriptViewport(session.Viewport);
        if (TryRestoreLegacyScriptSelectedNode(session.Viewport))
        {
            ShowSelectedScriptTreeNode();
        }
        else if (session.SelectedTextOffset.HasValue && TrySelectScriptTextByOffset(session.SelectedTextOffset.Value))
        {
            ShowSelectedScriptText();
        }
        else if (SelectDefaultScriptTreeNode())
        {
            ShowSelectedScriptTreeNode();
        }
        else if (_currentScriptStructure != null)
        {
            _scriptDetailBox.Text = BuildScriptOverview(_currentScriptStructure, _currentScriptTextEntries);
            _scriptPreviewBox.Text = BuildScriptOverviewPreview(_currentScriptStructure, _currentScriptTextEntries);
        }

        if (session.SelectedTextOffset.HasValue && _selectedScriptTextEntry?.Offset == session.SelectedTextOffset.Value)
        {
            _scriptTextEditorBox.Text = session.TextEditorText;
            CommitScriptTextEditorToCurrentEntry();
            UpdateScriptTextCapacityLabel();
        }

        _scriptVariableUsageDialog?.RefreshCurrentScenario();
        SetStatus($"Script editor: restored session cache {session.Scenario.FileName}");
    }

    private void CacheCurrentBattlefieldEditorSession()
    {
        if (_discardingUnsavedChangesForClose || _project == null || _currentBattlefieldDocument == null) return;
        CommitBattlefieldScriptTextEditorToCurrentEntry();

        var key = BuildEditorSessionKey("battlefield", _currentBattlefieldDocument.Scenario);
        if (string.IsNullOrWhiteSpace(key)) return;

        _battlefieldEditorSessions[key] = new BattlefieldEditorSession
        {
            ProjectRoot = Path.GetFullPath(_project.GameRoot),
            Scenario = _currentBattlefieldDocument.Scenario,
            Document = _currentBattlefieldDocument,
            LegacyDocument = _currentBattlefieldLegacyScriptDocument,
            ScriptStructure = _currentBattlefieldScriptStructure,
            ScriptTextEntries = _currentBattlefieldScriptTextEntries,
            PlacedUnits = _battlefieldPlacedUnits.Select(CloneBattlefieldPlacedUnit).ToList(),
            SelectedScriptTextOffset = _selectedBattlefieldScriptTextEntry?.Offset,
            ScriptTextEditorText = _battlefieldScriptTextBox.Text,
            TitleText = _battlefieldTitleBox.Text,
            ConditionsText = _battlefieldConditionsBox.Text,
            Viewport = CaptureLegacyScriptViewport(LegacyScriptEditorScope.Battlefield),
            SearchText = _battlefieldScriptSearchBox.Text,
            SearchResultIndex = _currentBattlefieldScriptSearchResultIndex,
            StructureDirty = IsLegacyStructureDirty(LegacyScriptEditorScope.Battlefield),
            TextDirty = IsBattlefieldTitleConditionsDirty(_currentBattlefieldDocument, _battlefieldTitleBox.Text, _battlefieldConditionsBox.Text),
            ScriptTextDirty = HasChangedScenarioTextEntries(_currentBattlefieldScriptTextEntries),
            PlacementDirty = HasBattlefieldReviewOrPlacementChanges()
        };
    }

    private bool TryRestoreBattlefieldEditorSession(ScenarioFileInfo scenario)
    {
        var key = BuildEditorSessionKey("battlefield", scenario);
        if (string.IsNullOrWhiteSpace(key) || !_battlefieldEditorSessions.TryGetValue(key, out var session)) return false;
        RestoreBattlefieldEditorSession(session);
        return true;
    }

    private void RestoreBattlefieldEditorSession(BattlefieldEditorSession session)
    {
        SelectBattlefieldScenarioComboSilently(session.Scenario);
        _currentBattlefieldDocument = session.Document;
        _currentBattlefieldLegacyScriptDocument = session.LegacyDocument;
        _currentBattlefieldScriptStructure = session.ScriptStructure;
        _currentBattlefieldScriptTextEntries = session.ScriptTextEntries;
        ClearLegacyScenarioHistory(LegacyScriptEditorScope.Battlefield);
        SetLegacyStructureDirtyFlag(LegacyScriptEditorScope.Battlefield, session.StructureDirty);
        ClearBattlefieldInstructionPreviewState();
        ClearBattlefieldPlacedUnitSelection();
        _battlefieldPlacedUnits.Clear();
        _battlefieldPlacedUnits.AddRange(session.PlacedUnits.Select(CloneBattlefieldPlacedUnit));
        LoadBattlefieldUnitPalette();
        LoadBattlefieldAllyDeploymentSlots(session.Scenario, _currentSceneStringDocument ?? TryReadSceneDictionaryForProbe());
        PopulateBattlefieldUnitCategoryFilter(_currentBattlefieldDocument.UnitCandidates);
        BindBattlefieldUnitCandidates(GetBattlefieldUnitCandidatesForDisplay());
        BindBattlefieldCommandCandidates(GetBattlefieldCommandCandidatesForDisplay());
        if (_currentBattlefieldScriptStructure != null)
        {
            if (_currentBattlefieldLegacyScriptDocument != null)
            {
                _battlefieldScriptCommandByKey.Clear();
                BuildBattlefieldLegacyScriptStructureResult(_currentBattlefieldLegacyScriptDocument);
                BuildBattlefieldLegacyScriptTextEntries(_currentBattlefieldLegacyScriptDocument);
                RebindLegacyScriptTextEntryMap(_battlefieldScriptTextEntryByOffset, _currentBattlefieldScriptTextEntries);
            }

            BuildBattlefieldScriptTree(_currentBattlefieldScriptStructure, _currentBattlefieldScriptTextEntries);
        }
        _battlefieldTitleBox.Text = session.TitleText;
        _battlefieldTitleBox.ReadOnly = !_currentBattlefieldDocument.CanWriteCampaignTitle;
        _battlefieldConditionsBox.Text = session.ConditionsText;
        _battlefieldConditionsBox.ReadOnly = _currentBattlefieldDocument.ConditionEntry == null;
        RestoreLegacyScriptSearch(LegacyScriptEditorScope.Battlefield, session.SearchText, session.SearchResultIndex);
        RenderBattlefieldMapPreview(_currentBattlefieldDocument);
        UpdateBattlefieldCapacityLabels();
        _saveBattlefieldTextsButton.Enabled = _currentBattlefieldDocument.CanWriteCampaignTitle || _currentBattlefieldDocument.ConditionEntry != null;
        _saveBattlefieldUnitReviewsButton.Enabled = _currentBattlefieldDocument.UnitCandidates.Count > 0;
        _saveBattlefieldScriptStructureButton.Enabled = _currentBattlefieldLegacyScriptDocument != null && session.StructureDirty;
        _showBattlefieldVariablesButton.Enabled = _currentBattlefieldLegacyScriptDocument != null;
        UpdateBattlefieldDeploymentWriteButton();
        _jumpBattlefieldMapButton.Enabled = HasBattlefieldMapResource(_currentBattlefieldDocument);
        _jumpBattlefieldScenarioButton.Enabled = true;

        RestoreLegacyScriptViewport(session.Viewport);
        if (TryRestoreLegacyScriptSelectedNode(session.Viewport))
        {
            ShowSelectedBattlefieldScriptNode();
        }
        else if (session.SelectedScriptTextOffset.HasValue && TrySelectBattlefieldScriptTextByOffset(session.SelectedScriptTextOffset.Value))
        {
            ShowSelectedBattlefieldScriptNode();
        }
        else
        {
            ClearBattlefieldScriptTextSelection();
        }

        if (session.SelectedScriptTextOffset.HasValue && _selectedBattlefieldScriptTextEntry?.Offset == session.SelectedScriptTextOffset.Value)
        {
            _battlefieldScriptTextBox.Text = session.ScriptTextEditorText;
            CommitBattlefieldScriptTextEditorToCurrentEntry();
            UpdateBattlefieldScriptTextCapacityLabel();
        }

        _battlefieldInfoBox.Text = BuildBattlefieldInfo(_currentBattlefieldDocument);
        SetStatus($"Battlefield editor: restored session cache {session.Scenario.FileName}");
    }

    private void CacheCurrentRSceneEditorSession()
    {
        if (_discardingUnsavedChangesForClose || _project == null || _currentRSceneScenario == null || _currentRSceneScriptStructure == null) return;
        var key = BuildEditorSessionKey("rscene", _currentRSceneScenario);
        if (string.IsNullOrWhiteSpace(key)) return;

        _rSceneEditorSessions[key] = new RSceneEditorSession
        {
            ProjectRoot = Path.GetFullPath(_project.GameRoot),
            Scenario = _currentRSceneScenario,
            LegacyDocument = _currentRSceneLegacyScriptDocument,
            PrecedingVariableDocuments = _currentRScenePrecedingVariableDocuments,
            ScriptStructure = _currentRSceneScriptStructure,
            ScriptTextEntries = _currentRSceneScriptTextEntries,
            CommandCandidates = _currentRSceneCommandCandidates,
            StateCandidates = _currentRSceneStateCandidates,
            PlacedActors = _rScenePlacedActors.Select(CloneRScenePlacedActor).ToList(),
            BackgroundImageNumber = (_rSceneBackgroundCombo.SelectedItem as RSceneBackgroundComboItem)?.ImageNumber ?? 0,
            GridSize = GetRSceneGridSize(),
            Viewport = CaptureLegacyScriptViewport(LegacyScriptEditorScope.RScene),
            SearchText = _rSceneScriptSearchBox.Text,
            SearchResultIndex = _currentRSceneScriptSearchResultIndex,
            StructureDirty = IsLegacyStructureDirty(LegacyScriptEditorScope.RScene),
            DraftDirty = HasCurrentRSceneDraftChanges()
        };
    }

    private bool TryRestoreRSceneEditorSession(ScenarioFileInfo scenario)
    {
        var key = BuildEditorSessionKey("rscene", scenario);
        if (string.IsNullOrWhiteSpace(key) || !_rSceneEditorSessions.TryGetValue(key, out var session)) return false;
        RestoreRSceneEditorSession(session);
        return true;
    }

    private void RestoreRSceneEditorSession(RSceneEditorSession session)
    {
        SelectRSceneScenarioComboSilently(session.Scenario);
        ResetRScenePlayback();
        ClearRScenePreviewLock();
        _currentRSceneScenario = session.Scenario;
        _currentRSceneLegacyScriptDocument = session.LegacyDocument;
        _currentRScenePrecedingVariableDocuments = session.PrecedingVariableDocuments;
        _currentRSceneScriptStructure = session.ScriptStructure;
        _currentRSceneScriptTextEntries = session.ScriptTextEntries;
        _currentRSceneCommandCandidates = session.CommandCandidates;
        _currentRSceneStateCandidates = session.StateCandidates;
        ClearLegacyScenarioHistory(LegacyScriptEditorScope.RScene);
        SetLegacyStructureDirtyFlag(LegacyScriptEditorScope.RScene, session.StructureDirty);
        _rScenePlacedActors.Clear();
        _rScenePlacedActors.AddRange(session.PlacedActors.Select(CloneRScenePlacedActor));
        _rSceneMapFaces.Clear();
        _selectedRScenePlacedActor = null;
        _editingRScenePlacedActor = null;
        _draggingRScenePlacedActor = null;
        _rScenePreviewCurrentRow = null;
        if (_currentRSceneLegacyScriptDocument != null)
        {
            _rSceneScriptCommandByKey.Clear();
            _currentRSceneScriptStructure = BuildRSceneLegacyScriptStructureResult(_currentRSceneLegacyScriptDocument);
            BuildRSceneLegacyScriptTextEntries(_currentRSceneLegacyScriptDocument);
            _currentRSceneScriptTextEntries = session.ScriptTextEntries;
        }
        if (_currentRSceneScriptStructure != null)
        {
            BuildRSceneScriptTree(_currentRSceneScriptStructure);
        }
        BindRSceneStateCandidates(_currentRSceneStateCandidates);
        using (SuppressRSceneCanvasRender())
        {
            _rSceneGridSizeInput.Value = Math.Clamp(session.GridSize, (int)_rSceneGridSizeInput.Minimum, (int)_rSceneGridSizeInput.Maximum);
        }
        if (session.BackgroundImageNumber > 0)
        {
            SelectRSceneBackgroundImageNumber(session.BackgroundImageNumber);
        }
        RestoreLegacyScriptSearch(LegacyScriptEditorScope.RScene, session.SearchText, session.SearchResultIndex);
        _saveRSceneDraftButton.Enabled = true;
        _saveRSceneScriptStructureButton.Enabled = _currentRSceneLegacyScriptDocument != null && session.StructureDirty;
        _showRSceneVariablesButton.Enabled = _currentRSceneLegacyScriptDocument != null;
        _jumpRSceneScriptButton.Enabled = true;
        _rScenePlaybackButton.Enabled = _currentRSceneLegacyScriptDocument != null;
        UpdateRScenePreviewLockButton();
        RestoreLegacyScriptViewport(session.Viewport);
        if (TryRestoreLegacyScriptSelectedNode(session.Viewport))
        {
            ShowSelectedRSceneScriptNode();
        }
        RenderRSceneCanvas();
        SetStatus($"R scene editor: restored session cache {session.Scenario.FileName}");
    }

    private async Task SaveCurrentScriptSessionSilentlyAsync()
    {
        if (_project == null || _currentScriptScenario == null) return;
        CommitScriptTextEditorToCurrentEntry();
        if (_currentLegacyScriptDocument != null)
        {
            if (!IsLegacyStructureDirty(LegacyScriptEditorScope.Script) && !HasChangedScenarioTextEntries(_currentScriptTextEntries)) return;
            var dictionary = _currentSceneStringDocument ?? TryReadSceneDictionaryForProbe()
                ?? throw new InvalidOperationException("Missing CczString.ini; cannot save the legacy script tree.");
            var result = await Task.Run(() => _legacyScenarioWriter.Save(
                _project,
                BuildScenarioRelativePath(_currentScriptScenario),
                _currentLegacyScriptDocument,
                dictionary,
                "Session save: legacy script"));
            MarkScenarioTextEntriesSaved(_currentScriptTextEntries);
            MarkLegacyScriptSavedInPlace(result);
            return;
        }

        var changed = _currentScriptTextEntries.Where(IsScenarioTextEntryDirty).ToList();
        if (changed.Count == 0) return;
        ValidateScenarioTextEntriesForInPlaceSave(changed);
        var saveResult = _scenarioTextWriter.SaveInPlace(_project, BuildScenarioRelativePath(_currentScriptScenario), changed, "Session save: script text");
        var reread = _scenarioTextReader.Read(saveResult.FilePath);
        VerifyScenarioTextSave(changed, reread);
        MarkScenarioTextEntriesSaved(changed);
        RefreshScriptTextRows(changed);
        SetStatus($"Script editor: saved {changed.Count} changed text entries");
    }

    private async Task SaveCurrentBattlefieldSessionSilentlyAsync()
    {
        if (_project == null || _currentBattlefieldDocument == null) return;
        CommitBattlefieldScriptTextEditorToCurrentEntry();
        var textDirty = IsBattlefieldTitleConditionsDirty(_currentBattlefieldDocument, _battlefieldTitleBox.Text, _battlefieldConditionsBox.Text);
        var scriptTextDirty = HasChangedScenarioTextEntries(_currentBattlefieldScriptTextEntries);
        var structureDirty = IsLegacyStructureDirty(LegacyScriptEditorScope.Battlefield);

        if (_currentBattlefieldLegacyScriptDocument != null && (structureDirty || textDirty || scriptTextDirty))
        {
            ApplyBattlefieldTitleConditionsToLegacyDocument();
            var dictionary = _currentSceneStringDocument ?? TryReadSceneDictionaryForProbe()
                ?? throw new InvalidOperationException("Missing CczString.ini; cannot save the legacy battlefield script tree.");
            var result = await Task.Run(() => _legacyScenarioWriter.Save(
                _project,
                BuildScenarioRelativePath(_currentBattlefieldDocument.Scenario),
                _currentBattlefieldLegacyScriptDocument,
                dictionary,
                "Session save: battlefield legacy script"));
            MarkScenarioTextEntriesSaved(_currentBattlefieldScriptTextEntries);
            MarkBattlefieldTitleConditionEntriesSaved();
            MarkLegacyScriptEditorSavedInPlace(LegacyScriptEditorScope.Battlefield, result);
        }
        else
        {
            var changed = BuildChangedBattlefieldTextEntries().ToList();
            if (changed.Count > 0)
            {
                ValidateScenarioTextEntriesForInPlaceSave(changed);
                var saveResult = _scenarioTextWriter.SaveInPlace(_project, BuildScenarioRelativePath(_currentBattlefieldDocument.Scenario), changed, "Session save: battlefield text");
                var reread = _scenarioTextReader.Read(saveResult.FilePath);
                foreach (var entry in changed)
                {
                    VerifyBattlefieldText(reread, entry.Offset, entry.Text, entry.Kind);
                }
                MarkScenarioTextEntriesSaved(changed);
                MarkBattlefieldTitleConditionEntriesSaved();
            }
        }

        if (HasBattlefieldReviewOrPlacementChanges())
        {
            SaveBattlefieldUnitReviewsSilently();
        }
    }

    private async Task SaveCurrentRSceneSessionSilentlyAsync()
    {
        if (_project == null || _currentRSceneScenario == null) return;
        if (_currentRSceneLegacyScriptDocument != null && IsLegacyStructureDirty(LegacyScriptEditorScope.RScene))
        {
            var dictionary = _currentSceneStringDocument ?? TryReadSceneDictionaryForProbe()
                ?? throw new InvalidOperationException("Missing CczString.ini; cannot save the legacy R scene script tree.");
            var result = await Task.Run(() => _legacyScenarioWriter.Save(
                _project,
                BuildScenarioRelativePath(_currentRSceneScenario),
                _currentRSceneLegacyScriptDocument,
                dictionary,
                "Session save: R scene legacy script"));
            MarkLegacyScriptEditorSavedInPlace(LegacyScriptEditorScope.RScene, result);
        }

        if (HasCurrentRSceneDraftChanges())
        {
            SaveRSceneDraftSilently();
        }
    }

    private void CommitScriptTextEditorToCurrentEntry()
    {
        var entry = _selectedScriptTextEntry;
        if (entry == null) return;
        var text = BattlefieldEditorService.NormalizeText(_scriptTextEditorBox.Text);
        UpdateScenarioTextEntryText(entry, text);
        if (_legacyScriptTextByOffset.TryGetValue(entry.Offset, out var legacyText))
        {
            ApplyLegacyTextParameter(legacyText.Parameter, text);
        }
    }

    private void CommitBattlefieldScriptTextEditorToCurrentEntry()
    {
        var entry = _selectedBattlefieldScriptTextEntry;
        if (entry == null) return;
        var text = BattlefieldEditorService.NormalizeText(_battlefieldScriptTextBox.Text);
        UpdateScenarioTextEntryText(entry, text);
        if (_battlefieldScriptTextByOffset.TryGetValue(entry.Offset, out var legacyText))
        {
            ApplyLegacyTextParameter(legacyText.Parameter, text);
        }
    }

    private static void UpdateScenarioTextEntryText(ScenarioTextEntry entry, string text)
    {
        entry.Text = text;
        entry.CharLength = text.Length;
        entry.HasNewLines = text.Contains('\n') || text.Contains('\r');
        entry.Preview = text.Length > 60 ? text[..60] : text;
    }

    private static void ApplyLegacyTextParameter(LegacyScenarioCommandParameter parameter, string text)
    {
        parameter.Text = text;
        parameter.ByteLength = EncodingService.GetGbkByteCount(text) + 1;
    }

    private static bool HasChangedScenarioTextEntries(IEnumerable<ScenarioTextEntry> entries)
        => entries.Any(IsScenarioTextEntryDirty);

    private static bool IsScenarioTextEntryDirty(ScenarioTextEntry entry)
        => !string.Equals(BattlefieldEditorService.NormalizeText(entry.Text), BattlefieldEditorService.NormalizeText(entry.OriginalText), StringComparison.Ordinal);

    private void ValidateScenarioTextEntriesForInPlaceSave(IEnumerable<ScenarioTextEntry> entries)
    {
        var errors = entries
            .Select(entry => new { Entry = entry, Error = ValidateScenarioTextValue(entry, NormalizeScenarioTextForSave(entry.Text)) })
            .Where(x => x.Error != null)
            .Take(10)
            .Select(x => $"#{x.Entry.Index} {x.Entry.OffsetHex}: {x.Error}")
            .ToList();
        if (errors.Count > 0)
        {
            throw new InvalidOperationException("Cannot save text entries: " + string.Join("; ", errors));
        }
    }

    private static void MarkScenarioTextEntriesSaved(IEnumerable<ScenarioTextEntry> entries)
    {
        foreach (var entry in entries)
        {
            entry.OriginalText = entry.Text;
            entry.CharLength = entry.Text.Length;
            entry.HasNewLines = entry.Text.Contains('\n') || entry.Text.Contains('\r');
            entry.Preview = entry.Text.Length > 60 ? entry.Text[..60] : entry.Text;
        }
    }

    private static void RebindLegacyScriptTextEntryMap(Dictionary<int, ScenarioTextEntry> map, IEnumerable<ScenarioTextEntry> entries)
    {
        map.Clear();
        foreach (var entry in entries)
        {
            map[entry.Offset] = entry;
        }
    }

    private bool TrySelectScriptTextByOffset(int offset)
    {
        var entry = _currentScriptTextEntries.FirstOrDefault(item => item.Offset == offset);
        return entry != null && SelectScriptTextTreeNode(entry, suppressEvents: true);
    }

    private bool TrySelectBattlefieldScriptTextByOffset(int offset)
    {
        var entry = _currentBattlefieldScriptTextEntries.FirstOrDefault(item => item.Offset == offset);
        if (entry == null) return false;
        foreach (TreeNode root in _battlefieldScriptTree.Nodes)
        {
            var node = EnumerateScriptTreeNodes(root).FirstOrDefault(candidate => candidate.Tag is ScenarioTextEntry text && text.Offset == offset);
            if (node == null) continue;
            _battlefieldScriptTree.SelectedNode = node;
            node.EnsureVisible();
            return true;
        }

        _selectedBattlefieldScriptTextEntry = entry;
        _selectedBattlefieldScriptCommandRow = null;
        _battlefieldScriptTextBox.Text = entry.Text;
        return true;
    }

    private void SelectScriptScenarioComboSilently(ScenarioFileInfo scenario)
    {
        _updatingScriptScenarioSelection = true;
        try { SelectComboScenarioByFileName(_scriptScenarioCombo, scenario.FileName); }
        finally { _updatingScriptScenarioSelection = false; }
    }

    private void SelectBattlefieldScenarioComboSilently(ScenarioFileInfo scenario)
    {
        _updatingBattlefieldScenarioSelection = true;
        try { SelectComboScenarioByFileName(_battlefieldScenarioCombo, scenario.FileName); }
        finally { _updatingBattlefieldScenarioSelection = false; }
    }

    private void SelectRSceneScenarioComboSilently(ScenarioFileInfo scenario)
    {
        _updatingRSceneScenarioSelection = true;
        try { SelectComboScenarioByFileName(_rSceneScenarioCombo, scenario.FileName); }
        finally { _updatingRSceneScenarioSelection = false; }
    }

    private static void SelectComboScenarioByFileName(ComboBox combo, string fileName)
    {
        foreach (var item in combo.Items)
        {
            if (item is ScenarioFileInfo scenario && scenario.FileName.Equals(fileName, StringComparison.OrdinalIgnoreCase))
            {
                combo.SelectedItem = item;
                return;
            }
        }
    }

    private static bool IsBattlefieldTitleConditionsDirty(BattlefieldEditorDocument document, string titleText, string conditionsText)
    {
        var dirty = false;
        if (document.CanWriteCampaignTitle)
        {
            var originalTitle = document.TitleEntry?.OriginalText ?? document.OriginalCampaignTitle;
            dirty |= !string.Equals(BattlefieldEditorService.NormalizeText(titleText), BattlefieldEditorService.NormalizeText(originalTitle), StringComparison.Ordinal);
        }

        if (document.ConditionEntry != null)
        {
            dirty |= !string.Equals(BattlefieldEditorService.NormalizeText(conditionsText), BattlefieldEditorService.NormalizeText(document.ConditionEntry.OriginalText), StringComparison.Ordinal);
        }

        return dirty;
    }

    private IEnumerable<ScenarioTextEntry> BuildChangedBattlefieldTextEntries()
    {
        if (_currentBattlefieldDocument == null) yield break;
        if (_currentBattlefieldDocument.TitleEntry != null)
        {
            UpdateScenarioTextEntryText(_currentBattlefieldDocument.TitleEntry, BattlefieldEditorService.NormalizeText(_battlefieldTitleBox.Text));
            if (IsScenarioTextEntryDirty(_currentBattlefieldDocument.TitleEntry)) yield return _currentBattlefieldDocument.TitleEntry;
        }

        if (_currentBattlefieldDocument.ConditionEntry != null)
        {
            UpdateScenarioTextEntryText(_currentBattlefieldDocument.ConditionEntry, BattlefieldEditorService.NormalizeText(_battlefieldConditionsBox.Text));
            if (IsScenarioTextEntryDirty(_currentBattlefieldDocument.ConditionEntry)) yield return _currentBattlefieldDocument.ConditionEntry;
        }

        foreach (var entry in _currentBattlefieldScriptTextEntries.Where(IsScenarioTextEntryDirty))
        {
            yield return entry;
        }
    }

    private void ApplyBattlefieldTitleConditionsToLegacyDocument()
    {
        if (_currentBattlefieldDocument == null || _currentBattlefieldLegacyScriptDocument == null) return;
        if (_currentBattlefieldDocument.TitleEntry != null)
        {
            var title = BattlefieldEditorService.NormalizeText(_battlefieldTitleBox.Text);
            UpdateScenarioTextEntryText(_currentBattlefieldDocument.TitleEntry, title);
            var parameter = FindLegacyTextParameterByOffset(_currentBattlefieldLegacyScriptDocument, _currentBattlefieldDocument.TitleEntry.Offset)
                ?? throw new InvalidOperationException("Cannot find battlefield title text parameter in the legacy tree.");
            ApplyLegacyTextParameter(parameter, title);
        }

        if (_currentBattlefieldDocument.ConditionEntry != null)
        {
            var conditions = BattlefieldEditorService.NormalizeText(_battlefieldConditionsBox.Text);
            UpdateScenarioTextEntryText(_currentBattlefieldDocument.ConditionEntry, conditions);
            var parameter = FindLegacyTextParameterByOffset(_currentBattlefieldLegacyScriptDocument, _currentBattlefieldDocument.ConditionEntry.Offset)
                ?? throw new InvalidOperationException("Cannot find battlefield condition text parameter in the legacy tree.");
            ApplyLegacyTextParameter(parameter, conditions);
        }
    }

    private static LegacyScenarioCommandParameter? FindLegacyTextParameterByOffset(LegacyScenarioDocument document, int offset)
        => document.EnumerateCommands().SelectMany(command => command.TextParameters).FirstOrDefault(parameter => parameter.FileOffset == offset);

    private void MarkBattlefieldTitleConditionEntriesSaved()
    {
        if (_currentBattlefieldDocument?.TitleEntry != null) MarkScenarioTextEntriesSaved(new[] { _currentBattlefieldDocument.TitleEntry });
        if (_currentBattlefieldDocument?.ConditionEntry != null) MarkScenarioTextEntriesSaved(new[] { _currentBattlefieldDocument.ConditionEntry });
    }

    private bool HasBattlefieldReviewOrPlacementChanges()
    {
        if (_project == null || _currentBattlefieldDocument == null) return false;
        var persistedReviews = _battlefieldUnitReviewService.Load(_project)
            .Where(review => !review.IsPlacement && review.ScenarioFileName.Equals(_currentBattlefieldDocument.Scenario.FileName, StringComparison.OrdinalIgnoreCase))
            .ToDictionaryFirstByKey(review => review.TargetKey, review => review, StringComparer.OrdinalIgnoreCase);
        foreach (var candidate in GetBattlefieldUnitCandidatesForDisplay())
        {
            persistedReviews.TryGetValue(candidate.TargetKey, out var review);
            if (!NormalizedEquals(candidate.ReviewStatus, review?.ReviewStatus) || !NormalizedEquals(candidate.ReviewNote, review?.ReviewNote)) return true;
            persistedReviews.Remove(candidate.TargetKey);
        }

        if (persistedReviews.Count > 0) return true;
        var currentPlacements = _battlefieldPlacedUnits
            .Where(unit => !BattlefieldDeploymentWriteService.IsScriptPlacementWritable(unit))
            .Select(BuildBattlefieldPlacementComparisonKey)
            .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var persistedPlacements = _battlefieldUnitReviewService.LoadPlacements(_project, _currentBattlefieldDocument)
            .Select(BuildBattlefieldPlacementComparisonKey)
            .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
            .ToList();
        return !currentPlacements.SequenceEqual(persistedPlacements, StringComparer.OrdinalIgnoreCase);
    }

    private void SaveBattlefieldUnitReviewsSilently()
    {
        if (_project == null || _currentBattlefieldDocument == null) return;
        _battlefieldUnitGrid.EndEdit();
        var rows = GetBattlefieldUnitCandidatesForDisplay().ToList();
        var path = _battlefieldUnitReviewService.Save(_project, _currentBattlefieldDocument, rows, _battlefieldPlacedUnits);
        SetStatus($"Battlefield editor: saved review/draft {path}");
    }

    private static string BuildBattlefieldPlacementComparisonKey(BattlefieldPlacedUnit unit)
        => string.Join("|",
            unit.TargetKey?.Trim() ?? string.Empty,
            unit.PersonId.ToString(CultureInfo.InvariantCulture),
            unit.Name?.Trim() ?? string.Empty,
            unit.JobId?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
            unit.JobName?.Trim() ?? string.Empty,
            unit.SImageId.ToString(CultureInfo.InvariantCulture),
            unit.RImageId.ToString(CultureInfo.InvariantCulture),
            unit.Faction?.Trim() ?? string.Empty,
            unit.LevelOffset.ToString(CultureInfo.InvariantCulture),
            unit.LevelMode?.Trim() ?? string.Empty,
            unit.AiMode?.Trim() ?? string.Empty,
            unit.Hidden ? "1" : "0",
            unit.Reinforcement ? "1" : "0",
            unit.Direction?.Trim() ?? string.Empty,
            unit.GridX.ToString(CultureInfo.InvariantCulture),
            unit.GridY.ToString(CultureInfo.InvariantCulture),
            unit.Source?.Trim() ?? string.Empty,
            unit.PlacementNote?.Trim() ?? string.Empty);

    private bool HasCurrentRSceneDraftChanges()
    {
        if (_project == null || _currentRSceneScenario == null) return false;
        var persisted = _rSceneDraftService.LoadDraft(_project, _currentRSceneScenario.FileName);
        var backgroundNumber = (_rSceneBackgroundCombo.SelectedItem as RSceneBackgroundComboItem)?.ImageNumber ?? 0;
        if (persisted.BackgroundImageNumber != backgroundNumber) return true;
        if (persisted.GridSize != GetRSceneGridSize()) return true;
        var currentActors = _rScenePlacedActors.Select(BuildRSceneActorComparisonKey).OrderBy(value => value, StringComparer.OrdinalIgnoreCase).ToList();
        var persistedActors = persisted.Actors.Select(BuildRSceneActorComparisonKey).OrderBy(value => value, StringComparer.OrdinalIgnoreCase).ToList();
        return !currentActors.SequenceEqual(persistedActors, StringComparer.OrdinalIgnoreCase);
    }

    private void SaveRSceneDraftSilently()
    {
        if (_project == null || _currentRSceneScenario == null) return;
        var backgroundNumber = (_rSceneBackgroundCombo.SelectedItem as RSceneBackgroundComboItem)?.ImageNumber ?? 0;
        var path = _rSceneDraftService.SaveDraft(_project, _currentRSceneScenario.FileName, backgroundNumber, GetRSceneGridSize(), _rScenePlacedActors);
        SetStatus($"R scene editor: saved draft {path}");
    }

    private static RScenePlacedActor CloneRScenePlacedActor(RScenePlacedActor actor)
        => new()
        {
            TargetKey = actor.TargetKey,
            PersonId = actor.PersonId,
            Name = actor.Name,
            JobId = actor.JobId,
            JobName = actor.JobName,
            RImageId = actor.RImageId,
            SImageId = actor.SImageId,
            Facing = actor.Facing,
            FrameIndex = actor.FrameIndex,
            GridX = actor.GridX,
            GridY = actor.GridY,
            PixelX = actor.PixelX,
            PixelY = actor.PixelY,
            Source = actor.Source,
            ActorNote = actor.ActorNote,
            LastActionTargetKey = actor.LastActionTargetKey
        };

    private static string BuildRSceneActorComparisonKey(RScenePlacedActor actor)
        => string.Join("|",
            actor.TargetKey?.Trim() ?? string.Empty,
            actor.PersonId.ToString(CultureInfo.InvariantCulture),
            actor.Name?.Trim() ?? string.Empty,
            actor.JobId?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
            actor.JobName?.Trim() ?? string.Empty,
            actor.RImageId.ToString(CultureInfo.InvariantCulture),
            actor.SImageId.ToString(CultureInfo.InvariantCulture),
            actor.Facing?.Trim() ?? string.Empty,
            actor.FrameIndex.ToString(CultureInfo.InvariantCulture),
            actor.GridX.ToString(CultureInfo.InvariantCulture),
            actor.GridY.ToString(CultureInfo.InvariantCulture),
            actor.Source?.Trim() ?? string.Empty,
            actor.ActorNote?.Trim() ?? string.Empty,
            actor.LastActionTargetKey?.Trim() ?? string.Empty);

    private static bool NormalizedEquals(string? left, string? right)
        => string.Equals((left ?? string.Empty).Trim(), (right ?? string.Empty).Trim(), StringComparison.Ordinal);
}
