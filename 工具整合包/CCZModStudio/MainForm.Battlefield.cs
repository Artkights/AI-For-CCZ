using CCZModStudio.Core;
using CCZModStudio.Formats;
using CCZModStudio.Models;
using System.ComponentModel;
using System.Data;
using System.Diagnostics;
using System.Drawing.Imaging;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace CCZModStudio;

public sealed partial class MainForm
{
    internal static Action<LegacyScenarioCommandNode>? BattlefieldScriptCommandEditInterceptForSmoke { get; set; }
    internal static Action<string>? BattlefieldConsoleCommitStageInterceptForSmoke { get; set; }

    [Flags]
    private enum BattlefieldConsoleDirtyKind
    {
        None = 0,
        Placement = 1,
        Equipment = 2,
        RuntimeAbility = 4,
        Mixed = Placement | Equipment | RuntimeAbility
    }

    private enum BattlefieldBatchEditField
    {
        Faction,
        Hidden,
        LevelOffset,
        LevelMode,
        AiMode,
        Direction,
        Weapon,
        WeaponLevel,
        Armor,
        ArmorLevel,
        Assist,
        Job,
        Ability
    }

    private enum BattlefieldRightPreviewMode
    {
        Overview,
        Script,
        Console
    }

    private enum BattlefieldConsoleCommitStatus
    {
        Committed,
        NoChanges,
        DraftOnly,
        ValidationFailed,
        WriteFailedRolledBack,
        BatchWriteFailedRolledBack,
        CommitInProgress
    }

    private sealed record BattlefieldConsoleCommitResult(
        BattlefieldConsoleCommitStatus Status,
        string Message,
        string TargetKey,
        BattlefieldConsoleDirtyKind DirtyKind,
        bool AllowsNavigation,
        bool RetainsDraft,
        string? LogPath = null,
        Control? FocusTarget = null)
    {
        public bool Success => AllowsNavigation;
    }

    private enum BattlefieldConsoleDeltaBuildStatus
    {
        Ready,
        NoChanges,
        DraftOnly,
        ValidationFailed
    }

    private sealed record BattlefieldConsoleDeltaBuildResult(
        BattlefieldConsoleDeltaBuildStatus Status,
        BattlefieldUnitStatusDraft? Delta,
        string Message,
        Control? FocusTarget = null);

    private sealed class BattlefieldUnsyncedDraftState
    {
        public bool IsBatch { get; init; }
        public string ScenarioFileName { get; init; } = string.Empty;
        public string Stage { get; init; } = string.Empty;
        public string ErrorMessage { get; init; } = string.Empty;
        public string LogPath { get; init; } = string.Empty;
        public BattlefieldConsoleDirtyKind DirtyKind { get; init; }
        public List<string> TargetKeys { get; init; } = [];
        public List<BattlefieldPlacedUnit> BeforeUnits { get; init; } = [];
        public List<BattlefieldPlacedUnit> DraftUnits { get; init; } = [];
        public List<BattlefieldPlacedUnit> OldPlacements { get; init; } = [];
        public List<BattlefieldUnitStatusDraft> StatusDeltas { get; init; } = [];
        public List<string> Failures { get; init; } = [];
    }

    private readonly record struct BattlefieldScriptCommandTargetKey(
        int SceneIndex,
        int SectionIndex,
        int CommandIndex,
        int CommandId,
        int FileOffset,
        string OffsetHex,
        string CommandIdHex);

    private async Task<bool> EnsureBattlefieldBaseDataLoadedAsync()
    {
        if (_project == null)
        {
            MessageBox.Show(this, "\u8bf7\u5148\u52a0\u8f7d\u9879\u76ee\u3002", "\u63d0\u793a", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return false;
        }

        var project = _project;
        var dictionary = _currentSceneStringDocument ?? TryReadSceneDictionaryForProbe();
        var existingScenarios = _currentScenarioFiles;
        var existingMapResources = _currentMapResources;
        var existingTerrainLookup = _terrainEditorTerrainLookup;
        var existingHexzmap = _currentHexzmapProbe;

        var result = await Task.Run(() =>
        {
            var scenarios = existingScenarios.Count > 0
                ? existingScenarios
                : new ScenarioFileReader().ReadAllIndex(project);
            var mapResources = existingMapResources.Count > 0
                ? existingMapResources
                : new MapResourceIndexer().Index(project);
            IReadOnlyDictionary<byte, string> terrainLookup = existingTerrainLookup.Count > 0
                ? existingTerrainLookup
                : BuildTerrainNameLookupForBackground(project);
            var hexzmap = existingHexzmap ?? new HexzmapProbeReader().Read(project, terrainLookup);
            return (scenarios, mapResources, terrainLookup, hexzmap);
        });

        _currentScenarioFiles = result.scenarios;
        _currentMapResources = result.mapResources;
        _terrainEditorTerrainLookup = result.terrainLookup;
        _currentHexzmapProbe = result.hexzmap;

        return true;
    }

    private IReadOnlyDictionary<byte, string> BuildTerrainNameLookupForBackground(CczProject project)
    {
        try
        {
            var materials = _materialLibraryCache.GetOrIndexExplicitRoot(MaterialLibraryIndexer.ResolveMaterialLibraryRoot(project));
            return HexzmapProbeReader.BuildTerrainNameLookup(materials);
        }
        catch
        {
            return new Dictionary<byte, string>();
        }
    }

    private async Task LoadBattlefieldScenariosAsync()
    {
        if (_loadingBattlefieldScenarioList) return;
        if (_project == null)
        {
            MessageBox.Show(this, "\u8bf7\u5148\u52a0\u8f7d\u9879\u76ee\u3002", "\u63d0\u793a", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        try
        {
            _loadingBattlefieldScenarioList = true;
            _loadBattlefieldButton.Enabled = false;
            _battlefieldScenarioCombo.Enabled = false;
            Cursor = Cursors.WaitCursor;
            SetStatus("\u6218\u573a\u5236\u4f5c\uff1a\u6b63\u5728\u540e\u53f0\u8bfb\u53d6\u57fa\u7840\u6570\u636e...");
            if (!await EnsureBattlefieldBaseDataLoadedAsync()) return;

            var rows = _currentScenarioFiles
                .Where(x => ScenarioFileReader.IsBattlefieldScriptFile(x.FileName))
                .OrderBy(x => x.FileName, StringComparer.CurrentCultureIgnoreCase)
                .ToList();

            _updatingBattlefieldScenarioSelection = true;
            _battlefieldScenarioCombo.DataSource = new BindingList<ScenarioFileInfo>(rows);
            _battlefieldScenarioCombo.DisplayMember = nameof(ScenarioFileInfo.FileName);
            _battlefieldScenarioCombo.ValueMember = nameof(ScenarioFileInfo.FileName);
            if (rows.Count > 0)
            {
                var selectedIndex = rows.FindIndex(x => x.FileName.Equals("S_00.eex", StringComparison.OrdinalIgnoreCase));
                _battlefieldScenarioCombo.SelectedIndex = selectedIndex >= 0 ? selectedIndex : 0;
            }
            _updatingBattlefieldScenarioSelection = false;

            if (rows.Count > 0)
            {
                await LoadSelectedBattlefieldScenarioAsync();
            }
            else
            {
                _battlefieldInfoBox.Text = "\u6218\u573a\u5236\u4f5c\uff1a\u6ca1\u6709\u627e\u5230 S_XX.eex \u6218\u573a\u5267\u672c\u6587\u4ef6\u3002";
            }

            SetStatus($"\u6218\u573a\u5236\u4f5c\uff1a\u5df2\u8bfb\u53d6\u6218\u573a\u5267\u672c {rows.Count} \u4e2a");
        }
        catch (Exception ex)
        {
            _updatingBattlefieldScenarioSelection = false;
            _battlefieldInfoBox.Text = ex.ToString();
            System.Diagnostics.Debug.WriteLine("Load battlefield scenarios failed: " + ex);
            MessageBox.Show(this, ex.Message, "\u8bfb\u53d6\u6218\u573a\u5236\u4f5c\u5931\u8d25", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            _loadingBattlefieldScenarioList = false;
            _loadBattlefieldButton.Enabled = true;
            _battlefieldScenarioCombo.Enabled = true;
            Cursor = Cursors.Default;
        }
    }

    private async Task LoadSelectedBattlefieldScenarioAsync()
    {
        if (_updatingBattlefieldScenarioSelection) return;
        if (!TryCommitPendingBattlefieldConsoleChanges()) return;
        if (_loadingBattlefieldScenarioDocument)
        {
            _reloadBattlefieldScenarioAfterCurrentLoad = true;
            return;
        }
        if (_project == null) return;
        if (_battlefieldScenarioCombo.SelectedItem is not ScenarioFileInfo scenario) return;

        var reloadAfterCurrent = false;
        try
        {
            _loadingBattlefieldScenarioDocument = true;
            _reloadBattlefieldScenarioAfterCurrentLoad = false;
            _battlefieldScenarioCombo.Enabled = false;
            Cursor = Cursors.WaitCursor;
            SetStatus($"\u6218\u573a\u5236\u4f5c\uff1a\u6b63\u5728\u8bfb\u53d6 {scenario.FileName}...");
            if (!await EnsureBattlefieldBaseDataLoadedAsync()) return;

            var project = _project;
            var tables = _tables;
            var dictionary = _currentSceneStringDocument ?? TryReadSceneDictionaryForProbe();
            var document = await Task.Run(() => new BattlefieldEditorService().Load(project, scenario, dictionary, tables));
            _currentBattlefieldDocument = document;
            ClearBattlefieldManualMarker();
            ClearBattlefieldCommand25Markers();
            ClearBattlefieldInstructionPreviewState();
            ResetBattlefieldDeploymentPreviewFilter();
            _battlefieldUnitReviewService.Apply(_project, _currentBattlefieldDocument);
            _battlefieldPlacedUnits.Clear();
            _battlefieldPlacedUnits.AddRange(_battlefieldUnitReviewService.LoadPlacements(_project, _currentBattlefieldDocument));
            ClearBattlefieldBatchEditingState(syncControls: false);
            ClearBattlefieldPlacedUnitSelection();
            LoadBattlefieldUnitPalette();
            LoadBattlefieldAllyDeploymentSlots(scenario, dictionary);
            MergeBattlefieldScriptPlacements(_currentBattlefieldDocument);
            if (dictionary != null)
            {
                LoadBattlefieldScriptView(scenario, dictionary);
            }
            _battlefieldTitleBox.Text = _currentBattlefieldDocument.TitleEntry?.Text ?? scenario.TitleHint;
            _battlefieldTitleBox.ReadOnly = _currentBattlefieldDocument.TitleEntry == null;
            _battlefieldConditionsBox.Text = _currentBattlefieldDocument.ConditionEntry?.Text ?? string.Empty;
            _battlefieldConditionsBox.ReadOnly = _currentBattlefieldDocument.ConditionEntry == null;
            PopulateBattlefieldUnitCategoryFilter(_currentBattlefieldDocument.UnitCandidates);
            BindBattlefieldUnitCandidates(GetBattlefieldUnitCandidatesForDisplay());
            BindBattlefieldCommandCandidates(GetBattlefieldCommandCandidatesForDisplay());
            RenderBattlefieldMapPreview(_currentBattlefieldDocument);
            UpdateBattlefieldCapacityLabels();
            _saveBattlefieldTextsButton.Enabled = _currentBattlefieldDocument.TitleEntry != null || _currentBattlefieldDocument.ConditionEntry != null;
            _saveBattlefieldUnitReviewsButton.Enabled = _currentBattlefieldDocument.UnitCandidates.Count > 0;
            UpdateBattlefieldDeploymentWriteButton();
            _jumpBattlefieldMapButton.Enabled = HasBattlefieldMapResource(_currentBattlefieldDocument);
            _jumpBattlefieldScenarioButton.Enabled = true;
            SetBattlefieldOverviewPreview(BuildBattlefieldInfo(_currentBattlefieldDocument));
            SetStatus($"\u6218\u573a\u5236\u4f5c\uff1a{scenario.FileName}");
        }
        catch (Exception ex)
        {
            _battlefieldInfoBox.Text = ex.ToString();
            System.Diagnostics.Debug.WriteLine("Load battlefield document failed: " + ex);
            MessageBox.Show(this, ex.Message, "\u8bfb\u53d6\u6218\u573a\u5236\u4f5c\u5931\u8d25", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            _loadingBattlefieldScenarioDocument = false;
            _battlefieldScenarioCombo.Enabled = true;
            Cursor = Cursors.Default;
            reloadAfterCurrent = _reloadBattlefieldScenarioAfterCurrentLoad;
            _reloadBattlefieldScenarioAfterCurrentLoad = false;
        }

        if (reloadAfterCurrent)
        {
            await LoadSelectedBattlefieldScenarioAsync();
        }
    }

    private void LoadBattlefieldScenarios()
    {
        if (_project == null)
        {
            MessageBox.Show(this, "请先加载项目。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        try
        {
            Cursor = Cursors.WaitCursor;
            EnsureBattlefieldBaseDataLoaded();
            var rows = _currentScenarioFiles
                .Where(x => x.Kind.Contains("关卡", StringComparison.Ordinal) || x.FileName.StartsWith("RS", StringComparison.OrdinalIgnoreCase))
                .OrderBy(x => x.FileName, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
            _updatingBattlefieldScenarioSelection = true;
            _battlefieldScenarioCombo.DataSource = new BindingList<ScenarioFileInfo>(rows);
            _battlefieldScenarioCombo.DisplayMember = nameof(ScenarioFileInfo.FileName);
            _battlefieldScenarioCombo.ValueMember = nameof(ScenarioFileInfo.FileName);
            _updatingBattlefieldScenarioSelection = false;
            if (rows.Count > 0)
            {
                _battlefieldScenarioCombo.SelectedIndex = rows.FindIndex(x => x.FileName.Equals("S_00.eex", StringComparison.OrdinalIgnoreCase));
                if (_battlefieldScenarioCombo.SelectedIndex < 0) _battlefieldScenarioCombo.SelectedIndex = 0;
                LoadSelectedBattlefieldScenario();
            }
            else
            {
                _battlefieldInfoBox.Text = "战场制作：没有找到 R/S eex 关卡文件。";
            }

            SetStatus($"战场制作：已读取关卡 {rows.Count} 个");
        }
        catch (Exception ex)
        {
            _updatingBattlefieldScenarioSelection = false;
            _battlefieldInfoBox.Text = ex.ToString();
            System.Diagnostics.Debug.WriteLine("读取战场制作关卡失败：" + ex);
            MessageBox.Show(this, ex.Message, "读取战场制作失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            Cursor = Cursors.Default;
        }
    }

    private void EnsureBattlefieldBaseDataLoaded()
    {
        if (_project == null) throw new InvalidOperationException("请先加载项目。");
        var dictionary = _currentSceneStringDocument ?? TryReadSceneDictionaryForProbe();
        if (_currentScenarioFiles.Count == 0)
        {
            _currentScenarioFiles = _scenarioFileReader.ReadAllIndex(_project);
        }

        if (_currentMapResources.Count == 0)
        {
            _currentMapResources = _mapResourceIndexer.Index(_project);
        }

        if (_terrainEditorTerrainLookup.Count == 0)
        {
            _terrainEditorTerrainLookup = BuildTerrainNameLookupForCurrentProject();
        }

        if (_currentHexzmapProbe == null)
        {
            _currentHexzmapProbe = _hexzmapProbeReader.Read(_project, _terrainEditorTerrainLookup);
        }
    }

    private void LoadSelectedBattlefieldScenario()
    {
        if (_updatingBattlefieldScenarioSelection) return;
        if (_project == null) return;
        if (_battlefieldScenarioCombo.SelectedItem is not ScenarioFileInfo scenario) return;

        try
        {
            Cursor = Cursors.WaitCursor;
            EnsureBattlefieldBaseDataLoaded();
            var dictionary = _currentSceneStringDocument ?? TryReadSceneDictionaryForProbe();
            _currentBattlefieldDocument = _battlefieldEditorService.Load(_project, scenario, dictionary, _tables);
            ClearBattlefieldManualMarker();
            ClearBattlefieldCommand25Markers();
            ClearBattlefieldInstructionPreviewState();
            _battlefieldUnitReviewService.Apply(_project, _currentBattlefieldDocument);
            _battlefieldPlacedUnits.Clear();
            _battlefieldPlacedUnits.AddRange(_battlefieldUnitReviewService.LoadPlacements(_project, _currentBattlefieldDocument));
            ClearBattlefieldBatchEditingState(syncControls: false);
            ClearBattlefieldPlacedUnitSelection();
            LoadBattlefieldUnitPalette();
            LoadBattlefieldAllyDeploymentSlots(scenario, dictionary);
            MergeBattlefieldScriptPlacements(_currentBattlefieldDocument);
            if (dictionary != null)
            {
                LoadBattlefieldScriptView(scenario, dictionary);
            }
            _battlefieldTitleBox.Text = _currentBattlefieldDocument.TitleEntry?.Text ?? scenario.TitleHint;
            _battlefieldTitleBox.ReadOnly = _currentBattlefieldDocument.TitleEntry == null;
            _battlefieldConditionsBox.Text = _currentBattlefieldDocument.ConditionEntry?.Text ?? string.Empty;
            _battlefieldConditionsBox.ReadOnly = _currentBattlefieldDocument.ConditionEntry == null;
            PopulateBattlefieldUnitCategoryFilter(_currentBattlefieldDocument.UnitCandidates);
            BindBattlefieldUnitCandidates(GetBattlefieldUnitCandidatesForDisplay());
            BindBattlefieldCommandCandidates(GetBattlefieldCommandCandidatesForDisplay());
            RenderBattlefieldMapPreview(_currentBattlefieldDocument);
            UpdateBattlefieldCapacityLabels();
            _saveBattlefieldTextsButton.Enabled = _currentBattlefieldDocument.TitleEntry != null || _currentBattlefieldDocument.ConditionEntry != null;
            _saveBattlefieldUnitReviewsButton.Enabled = _currentBattlefieldDocument.UnitCandidates.Count > 0;
            UpdateBattlefieldDeploymentWriteButton();
            _jumpBattlefieldMapButton.Enabled = HasBattlefieldMapResource(_currentBattlefieldDocument);
            _jumpBattlefieldScenarioButton.Enabled = true;
            SetBattlefieldOverviewPreview(BuildBattlefieldInfo(_currentBattlefieldDocument));
            SetStatus($"战场制作：{scenario.FileName}");
        }
        catch (Exception ex)
        {
            _battlefieldInfoBox.Text = ex.ToString();
            System.Diagnostics.Debug.WriteLine("读取战场制作文档失败：" + ex);
            MessageBox.Show(this, ex.Message, "读取战场制作失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            Cursor = Cursors.Default;
        }
    }

    private void BindBattlefieldUnitCandidates(IReadOnlyList<BattlefieldUnitCandidate> rows)
    {
        _bindingBattlefieldUnits = true;
        try
        {
            _battlefieldUnitGrid.DataSource = new BindingList<BattlefieldUnitCandidate>(rows.ToList());
        }
        finally
        {
            _bindingBattlefieldUnits = false;
        }

        foreach (DataGridViewColumn column in _battlefieldUnitGrid.Columns)
        {
            column.HeaderText = column.DataPropertyName switch
            {
                nameof(BattlefieldUnitCandidate.BattlefieldNumber) => "战场编号",
                nameof(BattlefieldUnitCandidate.SourceCommandDisplay) => "来源命令",
                nameof(BattlefieldUnitCandidate.PersonDisplay) => "人物/部队",
                nameof(BattlefieldUnitCandidate.CoordinateDisplay) => "坐标",
                nameof(BattlefieldUnitCandidate.FactionDisplay) => "阵营",
                nameof(BattlefieldUnitCandidate.AiDisplay) => "AI",
                nameof(BattlefieldUnitCandidate.LevelJobDisplay) => "等级/兵种级",
                nameof(BattlefieldUnitCandidate.DeploymentStatusDisplay) => "部署状态",
                nameof(BattlefieldUnitCandidate.PersonRawCodeDisplay) => "原始码",
                nameof(BattlefieldUnitCandidate.DirectionDisplay) => "朝向",
                nameof(BattlefieldUnitCandidate.HiddenDisplay) => "隐藏",
                nameof(BattlefieldUnitCandidate.ReinforcementDisplay) => "援军",
                nameof(BattlefieldUnitCandidate.PersonId) => "人物ID",
                nameof(BattlefieldUnitCandidate.PersonRawCode) => "Person2码",
                nameof(BattlefieldUnitCandidate.IsPersonVariable) => "变量人物",
                nameof(BattlefieldUnitCandidate.Index) => "序号",
                nameof(BattlefieldUnitCandidate.Category) => "类型",
                nameof(BattlefieldUnitCandidate.SourceCommand) => "来源命令",
                nameof(BattlefieldUnitCandidate.SceneSection) => "位置",
                nameof(BattlefieldUnitCandidate.OffsetHex) => "偏移",
                nameof(BattlefieldUnitCandidate.PersonHint) => "人物/部队",
                nameof(BattlefieldUnitCandidate.CoordinateHint) => "坐标",
                nameof(BattlefieldUnitCandidate.FactionHint) => "阵营",
                nameof(BattlefieldUnitCandidate.AiHint) => "AI",
                nameof(BattlefieldUnitCandidate.LevelOrStateHint) => "等级/状态",
                nameof(BattlefieldUnitCandidate.Annotation) => "中文注释",
                nameof(BattlefieldUnitCandidate.TargetKey) => "内部键",
                nameof(BattlefieldUnitCandidate.ReviewStatus) => "核对状态",
                nameof(BattlefieldUnitCandidate.ReviewNote) => "核对记录",
                _ => column.HeaderText
            };
            column.ToolTipText = column.DataPropertyName switch
            {
                nameof(BattlefieldUnitCandidate.BattlefieldNumber) => "出场设定旧编号：我军 0-19，友军 20-59，敌军 60-299。",
                nameof(BattlefieldUnitCandidate.PersonDisplay) => "人物/部队编号和可解析到的角色名。",
                nameof(BattlefieldUnitCandidate.CoordinateDisplay) => "出场设定中的地图格坐标。",
                nameof(BattlefieldUnitCandidate.FactionDisplay) => "按 4B/46/47 推断的阵营。",
                nameof(BattlefieldUnitCandidate.AiDisplay) => "与战场控制台一致的 AI 状态。",
                nameof(BattlefieldUnitCandidate.LevelJobDisplay) => "出场设定里的等级修正和兵种级别，和双击 0x46/0x47 出场设定弹窗一致。",
                nameof(BattlefieldUnitCandidate.DeploymentStatusDisplay) => "按 46/47/4B 结构化槽位读取的隐藏、援军和预览范围状态。",
                nameof(BattlefieldUnitCandidate.PersonRawCodeDisplay) => "46/47 原始 Person2 剧本码；写回时仍使用该编码体系。",
                nameof(BattlefieldUnitCandidate.DirectionDisplay) => "按朝向槽读取：0=上，1=右，2=下，3=左。",
                nameof(BattlefieldUnitCandidate.Annotation) => "面向创作者的中文说明和安全边界。",
                nameof(BattlefieldUnitCandidate.ReviewStatus) => "可编辑项目侧状态，例如：待核对、已核对、需改、已实测。不写入游戏文件。",
                nameof(BattlefieldUnitCandidate.ReviewNote) => "可编辑核对记录，用于记录旧工具对照、计划修改、实机结果。不写入游戏文件。",
                _ => column.ToolTipText
            };
            column.ReadOnly = column.DataPropertyName is not (nameof(BattlefieldUnitCandidate.ReviewStatus) or nameof(BattlefieldUnitCandidate.ReviewNote));
            if (column.DataPropertyName is nameof(BattlefieldUnitCandidate.ReviewStatus) or nameof(BattlefieldUnitCandidate.ReviewNote))
            {
                column.DefaultCellStyle.BackColor = Color.LightYellow;
            }
            if (column.DataPropertyName == nameof(BattlefieldUnitCandidate.TargetKey))
            {
                column.Visible = false;
            }
            if (column.DataPropertyName is nameof(BattlefieldUnitCandidate.Index)
                or nameof(BattlefieldUnitCandidate.PersonId)
                or nameof(BattlefieldUnitCandidate.PersonRawCode)
                or nameof(BattlefieldUnitCandidate.IsPersonVariable)
                or nameof(BattlefieldUnitCandidate.Category)
                or nameof(BattlefieldUnitCandidate.SourceCommand)
                or nameof(BattlefieldUnitCandidate.SceneSection)
                or nameof(BattlefieldUnitCandidate.OffsetHex)
                or nameof(BattlefieldUnitCandidate.PersonHint)
                or nameof(BattlefieldUnitCandidate.CoordinateHint)
                or nameof(BattlefieldUnitCandidate.FactionHint)
                or nameof(BattlefieldUnitCandidate.AiHint)
                or nameof(BattlefieldUnitCandidate.LevelOrStateHint))
            {
                column.Visible = false;
            }
            if (column.DataPropertyName is nameof(BattlefieldUnitCandidate.PersonDisplay)
                or nameof(BattlefieldUnitCandidate.Annotation)
                or nameof(BattlefieldUnitCandidate.ReviewNote))
            {
                column.Width = 320;
            }
            else if (column.DataPropertyName is nameof(BattlefieldUnitCandidate.DeploymentStatusDisplay)
                     or nameof(BattlefieldUnitCandidate.PersonRawCodeDisplay)
                     or nameof(BattlefieldUnitCandidate.DirectionDisplay)
                     or nameof(BattlefieldUnitCandidate.HiddenDisplay)
                     or nameof(BattlefieldUnitCandidate.ReinforcementDisplay))
            {
                column.Width = 86;
            }
        }
    }

    private void ClearBattlefieldInstructionPreviewState()
    {
        _battlefieldUnitCandidatePreviewOverrides.Clear();
        _battlefieldCommandCandidatePreviewOverrides.Clear();
        _battlefieldScriptPreviewPlacementsByTargetKey.Clear();
    }

    private IReadOnlyList<BattlefieldUnitCandidate> GetBattlefieldUnitCandidatesForDisplay()
    {
        if (_currentBattlefieldDocument == null) return Array.Empty<BattlefieldUnitCandidate>();
        if (_battlefieldUnitCandidatePreviewOverrides.Count == 0) return _currentBattlefieldDocument.UnitCandidates;

        var rows = _currentBattlefieldDocument.UnitCandidates
            .Select(candidate => _battlefieldUnitCandidatePreviewOverrides.TryGetValue(candidate.TargetKey, out var preview)
                ? preview
                : candidate)
            .ToList();
        var existingKeys = rows.Select(row => row.TargetKey).ToHashSet(StringComparer.OrdinalIgnoreCase);
        rows.AddRange(_battlefieldUnitCandidatePreviewOverrides.Values
            .Where(preview => existingKeys.Add(preview.TargetKey))
            .OrderBy(preview => preview.Category, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(preview => preview.Index));
        return rows;
    }

    private IReadOnlyList<BattlefieldCommandCandidate> GetBattlefieldCommandCandidatesForDisplay()
    {
        if (_currentBattlefieldDocument == null) return Array.Empty<BattlefieldCommandCandidate>();
        if (_battlefieldCommandCandidatePreviewOverrides.Count == 0) return _currentBattlefieldDocument.CommandCandidates;

        return _currentBattlefieldDocument.CommandCandidates
            .Select(candidate => _battlefieldCommandCandidatePreviewOverrides.TryGetValue(BuildBattlefieldCommandPreviewKey(candidate), out var preview)
                ? preview
                : candidate)
            .ToList();
    }

    private static string BuildBattlefieldCommandPreviewKey(BattlefieldCommandCandidate candidate)
        => BuildBattlefieldCommandPreviewKey(
            candidate.SceneIndex,
            candidate.SectionIndex,
            candidate.CommandIndex,
            candidate.OffsetHex,
            candidate.CommandIdHex);

    private static string BuildBattlefieldCommandPreviewKey(ScenarioStructureRow row)
        => BuildBattlefieldCommandPreviewKey(
            row.SceneIndex,
            row.SectionIndex,
            row.CommandIndex,
            row.OffsetHex,
            row.CommandIdHex);

    private static string BuildBattlefieldCommandPreviewKey(int scene, int section, int command, string offsetHex, string commandIdHex)
        => $"Scene={scene.ToString(CultureInfo.InvariantCulture)};Section={section.ToString(CultureInfo.InvariantCulture)};Command={command.ToString(CultureInfo.InvariantCulture)};Offset={offsetHex};Id={commandIdHex}";

    private static bool IsBattlefieldScriptPlacementTargetKeyWritable(string targetKey)
        => BattlefieldDeploymentWriteService.IsScriptPlacementWritable(new BattlefieldPlacedUnit
        {
            TargetKey = targetKey,
            PersonId = 0,
            GridX = 0,
            GridY = 0
        });

    private static bool TryParseBattlefieldTargetKey(
        string targetKey,
        out int scene,
        out int section,
        out int command,
        out string offsetHex,
        out string commandIdHex,
        out int recordIndex)
    {
        recordIndex = -1;
        var candidate = new BattlefieldUnitCandidate
        {
            TargetKey = targetKey
        };
        if (!BattlefieldEditorService.TryParseScriptCommandLocator(candidate, out scene, out section, out command, out offsetHex, out commandIdHex))
        {
            return false;
        }

        foreach (var part in (targetKey ?? string.Empty).Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var index = part.IndexOf('=');
            if (index <= 0) continue;
            if (!part[..index].Trim().Equals("Record", StringComparison.OrdinalIgnoreCase)) continue;
            int.TryParse(part[(index + 1)..].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out recordIndex);
            break;
        }

        return true;
    }

    private static BattlefieldPlacedUnit CloneBattlefieldPlacedUnit(BattlefieldPlacedUnit unit)
        => new()
        {
            TargetKey = unit.TargetKey,
            PersonId = unit.PersonId,
            PersonRawCode = unit.PersonRawCode,
            Name = unit.Name,
            JobId = unit.JobId,
            JobName = unit.JobName,
            SImageId = unit.SImageId,
            RImageId = unit.RImageId,
            Faction = unit.Faction,
            LevelOffset = unit.LevelOffset,
            LevelMode = unit.LevelMode,
            AiMode = unit.AiMode,
            Hidden = unit.Hidden,
            Reinforcement = unit.Reinforcement,
            Direction = unit.Direction,
            GridX = unit.GridX,
            GridY = unit.GridY,
            Source = unit.Source,
            PlacementNote = unit.PlacementNote
        };

    private void PopulateBattlefieldUnitCategoryFilter(IReadOnlyList<BattlefieldUnitCandidate> rows)
    {
        var current = _battlefieldUnitCategoryFilterCombo.SelectedItem?.ToString();
        _battlefieldUnitCategoryFilterCombo.Items.Clear();
        _battlefieldUnitCategoryFilterCombo.Items.Add("全部类型");
        foreach (var category in rows.Select(x => x.Category).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.CurrentCultureIgnoreCase).OrderBy(x => x, StringComparer.CurrentCultureIgnoreCase))
        {
            _battlefieldUnitCategoryFilterCombo.Items.Add(category);
        }

        var selectedIndex = 0;
        if (!string.IsNullOrWhiteSpace(current))
        {
            for (var i = 0; i < _battlefieldUnitCategoryFilterCombo.Items.Count; i++)
            {
                if (!_battlefieldUnitCategoryFilterCombo.Items[i]!.ToString()!.Equals(current, StringComparison.CurrentCultureIgnoreCase)) continue;
                selectedIndex = i;
                break;
            }
        }

        _battlefieldUnitCategoryFilterCombo.SelectedIndex = selectedIndex;
    }

    private void ApplyBattlefieldUnitFilter()
    {
        if (_currentBattlefieldDocument == null) return;
        _battlefieldUnitGrid.EndEdit();
        var category = _battlefieldUnitCategoryFilterCombo.SelectedItem?.ToString() ?? "全部类型";
        var keyword = _battlefieldUnitFilterBox.Text.Trim();
        var displayRows = GetBattlefieldUnitCandidatesForDisplay();
        var rows = displayRows
            .Where(x => category == "全部类型" || x.Category.Equals(category, StringComparison.CurrentCultureIgnoreCase))
            .Where(x => string.IsNullOrWhiteSpace(keyword) || BattlefieldUnitMatchesKeyword(x, keyword))
            .ToList();
        BindBattlefieldUnitCandidates(rows);
        _battlefieldInfoBox.Text = BuildBattlefieldInfo(_currentBattlefieldDocument) +
                                   $"\r\n\r\n出场/坐标候选筛选：类型={category}，关键字={(string.IsNullOrWhiteSpace(keyword) ? "（无）" : keyword)}，显示 {rows.Count}/{displayRows.Count} 条。";
        SetStatus($"战场制作候选筛选：{rows.Count}/{displayRows.Count}");
    }

    private void ClearBattlefieldUnitFilter()
    {
        _battlefieldUnitFilterBox.Clear();
        if (_battlefieldUnitCategoryFilterCombo.Items.Count > 0) _battlefieldUnitCategoryFilterCombo.SelectedIndex = 0;
        if (_currentBattlefieldDocument != null)
        {
            BindBattlefieldUnitCandidates(GetBattlefieldUnitCandidatesForDisplay());
            _battlefieldInfoBox.Text = BuildBattlefieldInfo(_currentBattlefieldDocument);
        }
    }

    private static bool BattlefieldUnitMatchesKeyword(BattlefieldUnitCandidate item, string keyword)
    {
        return ContainsKeyword(item.BattlefieldNumber?.ToString(CultureInfo.InvariantCulture) ?? string.Empty, keyword) ||
               ContainsKeyword(item.SourceCommandDisplay, keyword) ||
               ContainsKeyword(item.PersonDisplay, keyword) ||
               ContainsKeyword(item.CoordinateDisplay, keyword) ||
               ContainsKeyword(item.FactionDisplay, keyword) ||
               ContainsKeyword(item.AiDisplay, keyword) ||
               ContainsKeyword(item.LevelJobDisplay, keyword) ||
               ContainsKeyword(item.Category, keyword) ||
               ContainsKeyword(item.SourceCommand, keyword) ||
               ContainsKeyword(item.SceneSection, keyword) ||
               ContainsKeyword(item.OffsetHex, keyword) ||
               ContainsKeyword(item.PersonHint, keyword) ||
               ContainsKeyword(item.CoordinateHint, keyword) ||
               ContainsKeyword(item.FactionHint, keyword) ||
               ContainsKeyword(item.AiHint, keyword) ||
               ContainsKeyword(item.LevelOrStateHint, keyword) ||
               ContainsKeyword(item.Annotation, keyword) ||
               ContainsKeyword(item.ReviewStatus, keyword) ||
               ContainsKeyword(item.ReviewNote, keyword);
    }

    private BattlefieldUnitCandidate? GetSelectedBattlefieldUnitCandidate()
    {
        if (_battlefieldUnitGrid.SelectedRows.Count > 0 && _battlefieldUnitGrid.SelectedRows[0].DataBoundItem is BattlefieldUnitCandidate selected) return selected;
        if (_battlefieldUnitGrid.CurrentRow?.DataBoundItem is BattlefieldUnitCandidate current) return current;
        return null;
    }

    private BattlefieldUnitCandidate? GetBattlefieldUnitCandidateFromRow(int rowIndex)
    {
        if (rowIndex >= 0 &&
            rowIndex < _battlefieldUnitGrid.Rows.Count &&
            _battlefieldUnitGrid.Rows[rowIndex].DataBoundItem is BattlefieldUnitCandidate candidate)
        {
            return candidate;
        }

        return GetSelectedBattlefieldUnitCandidate();
    }

    private BattlefieldCommandCandidate? GetBattlefieldCommandCandidateFromRow(int rowIndex)
    {
        if (rowIndex >= 0 &&
            rowIndex < _battlefieldCommandGrid.Rows.Count &&
            _battlefieldCommandGrid.Rows[rowIndex].DataBoundItem is BattlefieldCommandCandidate candidate)
        {
            return candidate;
        }

        if (_battlefieldCommandGrid.SelectedRows.Count > 0 &&
            _battlefieldCommandGrid.SelectedRows[0].DataBoundItem is BattlefieldCommandCandidate selected)
        {
            return selected;
        }

        return _battlefieldCommandGrid.CurrentRow?.DataBoundItem as BattlefieldCommandCandidate;
    }

    private void SelectBattlefieldUnitCandidateInScriptTree(int rowIndex)
    {
        var candidate = GetBattlefieldUnitCandidateFromRow(rowIndex);
        if (candidate == null) return;
        if (_currentBattlefieldScriptStructure == null)
        {
            SetStatus("战场制作：左侧 S 剧本尚未读取，无法定位候选命令。");
            return;
        }

        var row = BattlefieldEditorService.FindScriptCommandForCandidate(_currentBattlefieldScriptStructure.Rows, candidate);
        if (row == null)
        {
            SetStatus($"战场制作：未在左侧 S 剧本找到候选命令 {candidate.SourceCommand} {candidate.OffsetHex}");
            return;
        }

        SelectBattlefieldScriptCommandRow(
            row,
            "从右侧出圀坐标候选双击定位：\r\n" +
            $"{DisplayOrFallback(candidate.FactionDisplay, candidate.Category)} / {DisplayOrFallback(candidate.PersonDisplay, candidate.PersonHint)} / {DisplayOrFallback(candidate.CoordinateDisplay, candidate.CoordinateHint)}");

        OpenBattlefieldDeploymentDialogForCandidate(candidate);
    }

    private void SelectBattlefieldCommandCandidateInScriptTree(int rowIndex)
    {
        var candidate = GetBattlefieldCommandCandidateFromRow(rowIndex);
        if (candidate == null) return;
        if (_currentBattlefieldScriptStructure == null)
        {
            SetStatus("战场制作：左侧 S 剧本尚未读取，无法定位命令。");
            return;
        }

        var row = FindBattlefieldScriptCommand(candidate);
        if (row == null)
        {
            SetStatus($"战场制作：未在左侧 S 剧本找到命令 {candidate.CommandIdHex} {candidate.OffsetHex}");
            return;
        }

        SelectBattlefieldScriptCommandRow(
            row,
            "从右侧命令表双击定位：\r\n" +
            $"{candidate.CommandIdHex} {candidate.CommandName} / Scene {candidate.SceneIndex} Section {candidate.SectionIndex} Cmd {candidate.CommandIndex}");
    }

    private void OpenBattlefieldDeploymentDialogForCandidate(BattlefieldUnitCandidate candidate)
    {
        if (_editingBattlefieldLegacyCommandDialog)
        {
            SetStatus("战场制作：旧版指令修改窗口已打开。");
            return;
        }

        if (!TryParseBattlefieldTargetKey(candidate.TargetKey, out _, out _, out _, out _, out var commandIdHex, out var recordIndex) ||
            !TryParseBattlefieldCommandId(commandIdHex, out var commandId))
        {
            SetStatus("战场制作：已定位候选命令，但无法解析出场设定命令号。");
            return;
        }

        if (!TryGetSelectedBattlefieldLegacyItemData(out var itemData) || itemData.Command == null)
        {
            SetStatus("战场制作：已定位候选命令，但无法取得旧版命令数据。");
            return;
        }

        if (itemData.Id != commandId)
        {
            SetStatus($"战场制作：已定位候选命令，但当前命令 {itemData.Command.CommandIdHex} 与候选 {commandIdHex} 不一致。");
            return;
        }

        if (commandId is 0x46 or 0x47)
        {
            var definition = BattlefieldDeploymentRecordDefinition.FromCommandId(commandId);
            var preferredParameterIndex = definition == null || recordIndex < 0
                ? (int?)null
                : recordIndex * definition.GroupSize;
            EditSelectedBattlefieldScriptParameters(candidate.TargetKey, preferredParameterIndex);
            return;
        }

        if (commandId == 0x4B)
        {
            EditSelectedBattlefieldScriptParameters(candidate.TargetKey);
        }
    }

    private static bool TryParseBattlefieldCommandId(string commandIdHex, out int commandId)
    {
        var text = (commandIdHex ?? string.Empty).Trim();
        if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) text = text[2..];
        return int.TryParse(text, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out commandId);
    }

    private ScenarioStructureRow? FindBattlefieldScriptCommand(BattlefieldCommandCandidate candidate)
    {
        return _currentBattlefieldScriptStructure?.Rows.FirstOrDefault(row =>
            row.NodeType == "Command候选" &&
            row.SceneIndex == candidate.SceneIndex &&
            row.SectionIndex == candidate.SectionIndex &&
            row.CommandIndex == candidate.CommandIndex &&
            (string.IsNullOrWhiteSpace(candidate.OffsetHex) || row.OffsetHex.Equals(candidate.OffsetHex, StringComparison.OrdinalIgnoreCase)) &&
            (string.IsNullOrWhiteSpace(candidate.CommandIdHex) || row.CommandIdHex.Equals(candidate.CommandIdHex, StringComparison.OrdinalIgnoreCase)));
    }

    private void SelectBattlefieldScriptCommandRow(ScenarioStructureRow row, string prefix)
    {
        var scriptTab = _battlefieldLeftTabs.TabPages
            .Cast<TabPage>()
            .FirstOrDefault(page => page.Text.Equals("S剧本", StringComparison.Ordinal));
        if (scriptTab != null)
        {
            _battlefieldLeftTabs.SelectedTab = scriptTab;
        }

        if (!SelectBattlefieldScriptTreeNode(row))
        {
            SetStatus($"战场制作：左侧 S 剧本树中没有找到 {row.CommandName} {row.OffsetHex}");
            return;
        }

        _selectedBattlefieldScriptCommandRow = row;
        _selectedBattlefieldScriptTextEntry = null;
        _battlefieldScriptTextBox.Clear();
        UpdateBattlefieldScriptTextCapacityLabel();
        _battlefieldScriptDetailBox.Text = prefix + "\r\n\r\n" + BuildBattlefieldScriptRowDetailWithPreview(row);
        SetBattlefieldScriptPreview(row, null, prefix);
        SetStatus($"战场制作：已定位左侧 S 剧本命令 {row.CommandName} {row.OffsetHex}");
    }

    private bool SelectBattlefieldScriptTreeNode(ScenarioStructureRow target, bool ensureVisible = true)
    {
        TreeNode? found = null;
        foreach (TreeNode root in _battlefieldScriptTree.Nodes)
        {
            found = FindScriptTreeNode(root, target);
            if (found != null) break;
        }

        if (found == null && target.NodeType == "Command候选")
        {
            foreach (TreeNode root in _battlefieldScriptTree.Nodes)
            {
                found = FindScriptOwnerTreeNode(root, target, "Section候选");
                if (found != null) break;
            }
        }

        if (found == null && target.NodeType == "Command候选")
        {
            foreach (TreeNode root in _battlefieldScriptTree.Nodes)
            {
                found = FindScriptOwnerTreeNode(root, target, "Scene候选");
                if (found != null) break;
            }
        }

        if (found == null) return false;

        using (SuppressBattlefieldScriptSelectionCommit())
        {
            _battlefieldScriptTree.SelectedNode = found;
        }
        if (ensureVisible)
        {
            found.EnsureVisible();
        }
        return true;
    }

    private bool SelectBattlefieldUnitCandidateGridRow(string targetKey, bool updatePreview = true)
    {
        if (string.IsNullOrWhiteSpace(targetKey)) return false;
        if (TrySelectBattlefieldUnitCandidateGridRow(targetKey, updatePreview)) return true;

        if (_currentBattlefieldDocument == null ||
            !GetBattlefieldUnitCandidatesForDisplay().Any(candidate => candidate.TargetKey.Equals(targetKey, StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        BindBattlefieldUnitCandidates(GetBattlefieldUnitCandidatesForDisplay());
        return TrySelectBattlefieldUnitCandidateGridRow(targetKey, updatePreview);
    }

    private bool TrySelectBattlefieldUnitCandidateGridRow(string targetKey, bool updatePreview = true)
    {
        var previousPreviewMode = _battlefieldRightPreviewMode;
        var previousPreviewText = _battlefieldInfoBox.Text;
        foreach (DataGridViewRow gridRow in _battlefieldUnitGrid.Rows)
        {
            if (gridRow.DataBoundItem is not BattlefieldUnitCandidate candidate ||
                !candidate.TargetKey.Equals(targetKey, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            gridRow.Selected = true;
            var visibleCell = gridRow.Cells.Cast<DataGridViewCell>().FirstOrDefault(cell => cell.Visible);
            if (visibleCell != null)
            {
                _battlefieldUnitGrid.CurrentCell = visibleCell;
            }

            if (!updatePreview)
            {
                _battlefieldRightPreviewMode = previousPreviewMode;
                _battlefieldInfoBox.Text = previousPreviewText;
                ApplyBattlefieldRightPreviewMode();
            }

            return true;
        }

        return false;
    }

    private void MarkSelectedBattlefieldUnit(string status)
    {
        var candidate = GetSelectedBattlefieldUnitCandidate();
        if (candidate == null)
        {
            MessageBox.Show(this, "请先选择一条出场/坐标候选。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        candidate.ReviewStatus = status;
        candidate.ReviewNote = BattlefieldUnitReviewService.AppendReviewLine(
            candidate.ReviewNote,
            BattlefieldUnitReviewService.BuildQuickReviewNote(candidate, status));
        _battlefieldUnitGrid.Refresh();
        ShowSelectedBattlefieldUnitCandidate();
        SetStatus($"战场候选已标记：{status}");
    }

    private void BeginBattlefieldPlacedUnitInteraction(MouseEventArgs e)
    {
        if (e.Button is not (MouseButtons.Left or MouseButtons.Right)) return;


        if (_currentBattlefieldDocument == null) return;
        if (!TryMapPreviewPointToGrid(e.Location, out var x, out var y))
        {
            SetStatus("战场布阵：点击位置不在地图显示区域内。");
            return;
        }

        if (e.Button == MouseButtons.Left && ModifierKeys.HasFlag(Keys.Shift))
        {
            BeginBattlefieldBatchSelection(x, y);
            return;
        }

        if (e.Button == MouseButtons.Left &&
            _battlefieldCommand25PreviewEnabled &&
            TryHitBattlefieldCommand25Marker(e.Location, out var command25Marker))
        {
            JumpToBattlefieldCommand25Marker(command25Marker);
            return;
        }

        if (!TryHitBattlefieldPlacedUnit(e.Location, out var unit))
        {
            var slot = _battlefieldAllyDeploymentSlots.LastOrDefault(item => item.GridX == x && item.GridY == y);
            if (slot != null)
            {
                ClearBattlefieldPlacedUnitSelection();
                RefreshBattlefieldMapDynamicPreview();
                _battlefieldInfoBox.Text =
                    BuildBattlefieldInfo(_currentBattlefieldDocument) +
                    $"\r\n\r\n我军候选出战位：#{slot.DisplayOrder}  坐标=({slot.GridX},{slot.GridY})  {(slot.IsForced ? "强制出战" : "候选")}\r\n" +
                    (slot.IsForced
                        ? $"强制角色：{slot.PersonId} {slot.Name}  职业={slot.JobId?.ToString(CultureInfo.InvariantCulture) ?? "?"} {slot.JobName}  S={slot.SImageId}\r\n"
                        : "未绑定强制角色，保留为战前候选位。\r\n") +
                    $"来源：{slot.Source}\r\n" +
                    $"命令：{slot.SourceFileName} / {slot.SourceLocator}\r\n" +
                    $"隐藏：{slot.Hidden}\r\n" +
                    $"原始 4B 槽值：{slot.SourceValues}";
                SetStatus($"战场布阵：已选中我军候选出战位 #{slot.DisplayOrder} ({x},{y})");
                return;
            }

            if (e.Button == MouseButtons.Left)
            {
                ClearBattlefieldPlacedUnitSelection();
                _battlefieldMapPreviewSelectedUnit = GetSelectedBattlefieldUnitCandidate();
                RefreshBattlefieldMapDynamicPreview();
            }
            SetStatus($"战场布阵：({x},{y}) 没有已摆放单位。");
            return;
        }

        var enterEdit = e.Button == MouseButtons.Right;
        ClearBattlefieldBatchEditingState(syncControls: false);
        SelectBattlefieldPlacedUnit(unit, enterEdit);

        if (ReferenceEquals(_editingBattlefieldPlacedUnit, unit))
        {
            _draggingBattlefieldPlacedUnit = unit;
            _battlefieldPlacedUnitDragStart = e.Location;
            _battlefieldPlacedUnitOriginalGrid = new Point(unit.GridX, unit.GridY);
            _battlefieldPlacedUnitDragMoved = false;
            _battlefieldMapPreviewBox.Capture = true;
            _battlefieldMapPreviewBox.Cursor = Cursors.SizeAll;
        }
    }

    private void OpenBattlefieldUnitStatusDialog(MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left) return;
        if (_project == null || _currentBattlefieldDocument == null)
        {
            MessageBox.Show(this, "请先读取一个战场关卡。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        if (_currentBattlefieldLegacyScriptDocument == null)
        {
            MessageBox.Show(this, "Current S script is not in legacy full-tree mode; unit status cannot be edited from the left script tree.", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        if (!TryHitBattlefieldPlacedUnit(e.Location, out var unit))
        {
            if (TryMapPreviewPointToGrid(e.Location, out var x, out var y))
            {
                SetStatus($"战场单位状态：({x},{y}) 没有已摆放单位。");
            }
            return;
        }

        SelectBattlefieldPlacedUnit(unit, enterEdit: false);
        if (!BattlefieldUnitStatusWriteService.IsWritableStatusTarget(unit))
        {
            MessageBox.Show(this,
                BattlefieldUnitStatusWriteService.IsScene2PlusStatusTarget(unit)
                    ? BattlefieldUnitStatusWriteService.Scene2PlusStatusWriteDisabledMessage
                    : "该单位没有绑定到 Scene1 的 0x46 友军出场设定或 0x47 敌军出场设定，不能直接写回状态。\r\n\r\n请双击由 S 剧本 Scene1 46/47 自动加载或拖放时已绑定到 46/47 记录的友军/敌军单位。",
                "不能写回状态",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return;
        }

        try
        {
            Cursor = Cursors.WaitCursor;
            var scenarioFileName = _currentBattlefieldDocument.Scenario.FileName;
            if (_currentBattlefieldLegacyScriptDocument == null)
            {
                throw new InvalidOperationException("当前 S 剧本没有进入旧版完整树模式，无法从左侧树读写战场单位状态。");
            }
            var dictionary = _currentSceneStringDocument ?? TryReadSceneDictionaryForProbe()
                ?? throw new InvalidOperationException("缺少 CczString.ini，无法按旧版完整树写回并校验 S 剧本。");
            var draft = _battlefieldUnitStatusWriteService.LoadDraft(
                _currentBattlefieldLegacyScriptDocument,
                scenarioFileName,
                unit);
            var itemBoundary = ItemCategoryBoundaryService.Resolve(_project);
            draft.EquipmentBoundarySummary = itemBoundary.DisplayText;
            var jobItems = _battlefieldUnitStatusWriteService.BuildJobItems(_project, _tables);
            var weaponItems = _battlefieldUnitStatusWriteService.BuildItemItems(
                _project,
                _tables,
                start: itemBoundary.WeaponStartId,
                count: itemBoundary.WeaponCount,
                categoryName: "武器",
                defaultText: "默认装备",
                unequipText: "卸去装备");
            var armorItems = _battlefieldUnitStatusWriteService.BuildItemItems(
                _project,
                _tables,
                start: itemBoundary.DefenseStartId,
                count: itemBoundary.DefenseCount,
                categoryName: "防具",
                defaultText: "默认装备",
                unequipText: "卸去装备");
            var assistItems = _battlefieldUnitStatusWriteService.BuildItemItems(
                _project,
                _tables,
                start: itemBoundary.AccessoryStartId,
                count: itemBoundary.AccessoryCount,
                categoryName: "辅助装备段",
                defaultText: "默认装备",
                unequipText: "卸去装备");
            Cursor = Cursors.Default;

            using var dialog = new BattlefieldUnitStatusDialog(draft, jobItems, weaponItems, armorItems, assistItems);
            if (dialog.ShowDialog(this) != DialogResult.OK)
            {
                return;
            }

            if (MessageBox.Show(this,
                    $"即将写入 RS\\{scenarioFileName}。\r\n\r\n46/47 等级加成、兵种级、AI 方针是出场记录字段；48 装备按部署段写入＀2 兵种咀38 五维是脚本运行指令，不是出场记录戀Data.e5 永久人物表字段。2 会按旧资料自动包裀4081 能力重算开关，但战场初始显示是否变化仍取决于该 Section/子事件是否在单位生成后执行。\r\n保存前会自动备份，保存后复读校验脚本结构。是否继续？",
                    "确认写回战场单位状态",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Question) != DialogResult.Yes)
            {
                return;
            }

            Cursor = Cursors.WaitCursor;
            var result = _battlefieldUnitStatusWriteService.Save(
                _project,
                _currentBattlefieldDocument.Scenario,
                dictionary,
                _currentBattlefieldLegacyScriptDocument,
                dialog.Draft);

            ReloadBattlefieldScenarioAfterWrite(scenarioFileName, dictionary);
            SelectBattlefieldUnitStatusWritebackNode(dialog.Draft);
            _battlefieldInfoBox.Text =
                BuildBattlefieldInfo(_currentBattlefieldDocument!) +
                $"\r\n\r\n战场单位状态已写回 RS\\{scenarioFileName}。\r\n" +
                $"更新命令：{result.UpdatedCommandCount}，插入命令：{result.InsertedCommandCount}，变化字节：{result.ChangedBytes}\r\n" +
                $"校验：{result.ValidationSummary}\r\n" +
                $"备份：{result.BackupPath}\r\n" +
                $"报告：{result.ReportJsonPath}\r\n" +
                BuildBattlefieldUnitStatusWriteDetail(result);
            SetStatus($"战场单位状态：已写囀{unit.Name}({unit.PersonId}) -> {scenarioFileName}");
            MessageBox.Show(this,
                $"写回完成。\r\n校验：{result.ValidationSummary}\r\n备份：{result.BackupPath}\r\n报告：{result.ReportJsonPath}",
                "战场单位状态写回完成",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine("战场单位状态写回失败：" + ex);
            MessageBox.Show(this, ex.Message, "战场单位状态写回失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            Cursor = Cursors.Default;
        }
    }

    private void FocusBattlefieldConsoleFromMapDoubleClick(MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left) return;
        if (_currentBattlefieldDocument == null) return;
        if (!TryHitBattlefieldPlacedUnit(e.Location, out var unit))
        {
            if (TryMapPreviewPointToGrid(e.Location, out var x, out var y))
            {
                SetStatus($"战场控制台：({x},{y}) 没有已摆放单位。");
            }
            return;
        }

        SelectBattlefieldPlacedUnit(unit, enterEdit: true);
        _battlefieldConsoleAbilityGrid.Focus();
        SetStatus($"战场控制台：已选中 {unit.Name}，可直接在右侧控制台修改 Data 默认差异覆盖。");
    }

    private static string BuildBattlefieldUnitStatusWriteDetail(BattlefieldUnitStatusWriteResult result)
    {
        if (result.Changes.Count == 0) return string.Empty;
        var rows = new List<string> { "状态写回明细：" };
        rows.AddRange(result.Changes.Take(16).Select(change => "- " + change));
        if (result.Changes.Count > 16)
        {
            rows.Add($"- ... 其余 {result.Changes.Count - 16} 条略。");
        }

        return "\r\n" + string.Join("\r\n", rows);
    }

    private void SelectBattlefieldUnitStatusWritebackNode(BattlefieldUnitStatusDraft draft)
    {
        if (_currentBattlefieldLegacyScriptDocument == null)
        {
            return;
        }

        var target = FindBattlefieldUnitStatusWritebackBlock(draft)
                     ?? FindBattlefieldDeploymentSourceCommand(draft.TargetKey);
        if (target == null || !TrySelectBattlefieldLegacyScriptCommand(target))
        {
            return;
        }

        ShowSelectedBattlefieldScriptNode();
    }

    private LegacyScenarioCommandNode? FindBattlefieldUnitStatusWritebackBlock(BattlefieldUnitStatusDraft draft)
    {
        if (!TryParseBattlefieldTargetKey(draft.TargetKey, out var scene, out var section, out _, out _, out _, out _))
        {
            return null;
        }

        var commandId = draft.CommandId;
        if (commandId == 0 &&
            TryParseBattlefieldTargetKey(draft.TargetKey, out _, out _, out _, out _, out var commandIdHex, out _) &&
            TryParseBattlefieldCommandId(commandIdHex, out var parsedCommandId))
        {
            commandId = parsedCommandId;
        }

        var wroteEquipment = draft.Weapon.HasValue ||
                              draft.WeaponLevel.HasValue ||
                              draft.Armor.HasValue ||
                              draft.ArmorLevel.HasValue ||
                              draft.Assist.HasValue;
        var wroteRuntime = draft.JobId.HasValue || draft.Abilities.Any(ability => ability.Value.HasValue);
        var preferredTitle = wroteEquipment
            ? BattlefieldUnitStatusWriteService.GetEquipmentStatusBlockTitle(commandId)
            : wroteRuntime
                ? BattlefieldUnitStatusWriteService.GetRuntimeStatusBlockTitle(commandId)
                : string.Empty;
        var titles = new[]
        {
            preferredTitle,
            BattlefieldUnitStatusWriteService.GetEquipmentStatusBlockTitle(commandId),
            BattlefieldUnitStatusWriteService.GetRuntimeStatusBlockTitle(commandId),
            BattlefieldUnitStatusWriteService.EquipmentStatusBlockTitle,
            BattlefieldUnitStatusWriteService.RuntimeStatusBlockTitle,
            BattlefieldUnitStatusWriteService.CombinedStatusBlockTitle
        }.Where(title => !string.IsNullOrWhiteSpace(title)).Distinct(StringComparer.Ordinal).ToList();
        return _currentBattlefieldLegacyScriptDocument?
            .EnumerateCommands()
            .Where(command =>
                command.CommandId == 0x02 &&
                command.SceneIndex == scene &&
                command.SectionIndex == section &&
                command.ChildBlock != null &&
                titles.Contains(command.TextParameters.FirstOrDefault()?.Text.Trim() ?? string.Empty, StringComparer.Ordinal))
            .FirstOrDefault(command => InternalInfoBlockContainsBattlefieldUnitStatus(command, draft.PersonId));
    }

    private static bool InternalInfoBlockContainsBattlefieldUnitStatus(LegacyScenarioCommandNode blockCommand, int personId)
        => blockCommand.ChildBlock?.Commands.Any(command =>
               command.CommandId is 0x48 or 0x52 or 0x38 &&
               command.Parameters.Count > 0 &&
               command.Parameters[0].IntValue == personId) == true;

    private LegacyScenarioCommandNode? FindBattlefieldDeploymentSourceCommand(string targetKey)
    {
        if (_currentBattlefieldLegacyScriptDocument == null ||
            !TryParseBattlefieldTargetKey(targetKey, out var scene, out var section, out var commandIndex, out var offsetHex, out var commandIdHex, out _))
        {
            return null;
        }

        return _currentBattlefieldLegacyScriptDocument.EnumerateCommands().FirstOrDefault(command =>
            command.SceneIndex == scene &&
            command.SectionIndex == section &&
            command.CommandIndex == commandIndex &&
            (string.IsNullOrWhiteSpace(offsetHex) ||
             HexDisplayFormatter.EqualsText(HexDisplayFormatter.FormatOffset(command.FileOffset), offsetHex)) &&
            (string.IsNullOrWhiteSpace(commandIdHex) ||
             string.Equals(command.CommandIdHex, commandIdHex, StringComparison.OrdinalIgnoreCase)));
    }

    private void ContinueBattlefieldPlacedUnitInteraction(MouseEventArgs e)
        => ContinueBattlefieldPlacedUnitInteraction(e, null);

    private void ContinueBattlefieldPlacedUnitInteraction(MouseEventArgs e, Point? mappedGrid)
    {
        if (_battlefieldBatchSelecting)
        {
            ContinueBattlefieldBatchSelection(e.Location);
            return;
        }

        if (_draggingBattlefieldPlacedUnit == null || _battlefieldPlacedUnitDragStart == null) return;
        if ((e.Button & (MouseButtons.Left | MouseButtons.Right)) == 0) return;
        int x;
        int y;
        if (mappedGrid.HasValue)
        {
            x = mappedGrid.Value.X;
            y = mappedGrid.Value.Y;
        }
        else if (!TryMapPreviewPointToGrid(e.Location, out x, out y))
        {
            return;
        }
        if (_draggingBattlefieldPlacedUnit.GridX == x && _draggingBattlefieldPlacedUnit.GridY == y) return;

        _draggingBattlefieldPlacedUnit.GridX = x;
        _draggingBattlefieldPlacedUnit.GridY = y;
        _battlefieldPlacedUnitDragMoved = true;
        SetStatus($"战场布阵：拖劀{_draggingBattlefieldPlacedUnit.Name} -> ({x},{y})");
    }

    private void EndBattlefieldPlacedUnitInteraction(Point? location)
    {
        if (_battlefieldBatchSelecting)
        {
            if (!location.HasValue)
            {
                CancelBattlefieldBatchSelection();
                return;
            }

            EndBattlefieldBatchSelection(location);
            return;
        }

        if (_draggingBattlefieldPlacedUnit == null)
        {
            _battlefieldMapPreviewBox.Cursor = Cursors.Default;
            return;
        }

        var unit = _draggingBattlefieldPlacedUnit;
        var oldGrid = _battlefieldPlacedUnitOriginalGrid;
        var moved = _battlefieldPlacedUnitDragMoved && (unit.GridX != oldGrid.X || unit.GridY != oldGrid.Y);
        _draggingBattlefieldPlacedUnit = null;
        _battlefieldPlacedUnitDragStart = null;
        _battlefieldPlacedUnitDragMoved = false;
        _battlefieldMapPreviewBox.Capture = false;
        _battlefieldMapPreviewBox.Cursor = Cursors.Default;

        if (!moved)
        {
            TryCommitPendingBattlefieldConsoleChanges(finalizeBatchTransaction: false);
            return;
        }

        var occupied = _battlefieldPlacedUnits.FirstOrDefault(item =>
            !ReferenceEquals(item, unit) &&
            item.GridX == unit.GridX &&
            item.GridY == unit.GridY);
        if (occupied != null)
        {
            unit.GridX = oldGrid.X;
            unit.GridY = oldGrid.Y;
            if (_currentBattlefieldDocument != null)
            {
                var dirtyRegion = UnionBattlefieldDirtyRegion(
                    GetBattlefieldMapGridClientRectangle(oldGrid),
                    GetBattlefieldMapGridClientRectangle(new Point(occupied.GridX, occupied.GridY)));
                InvalidateBattlefieldMapDynamicRegion(dirtyRegion);
            }
            TryCommitPendingBattlefieldConsoleChanges(finalizeBatchTransaction: false);
            SetStatus($"战场布阵：目标格 ({occupied.GridX},{occupied.GridY}) 已有 {occupied.Name}，已取消移动。");
            return;
        }

        unit.PlacementNote = BattlefieldUnitReviewService.AppendReviewLine(
            unit.PlacementNote,
            $"地图拖拽：({oldGrid.X},{oldGrid.Y}) -> ({unit.GridX},{unit.GridY})。");
        if (!_battlefieldConsoleDirty)
        {
            var beforeMove = CloneBattlefieldPlacedUnit(unit);
            beforeMove.GridX = oldGrid.X;
            beforeMove.GridY = oldGrid.Y;
            _battlefieldConsoleBeforeEditSnapshot = beforeMove;
        }
        MarkBattlefieldConsoleDirty(BattlefieldConsoleDirtyKind.Placement);
        var synced = TryCommitPendingBattlefieldConsoleChanges(finalizeBatchTransaction: false) &&
                     _currentBattlefieldLegacyScriptDocument != null &&
                     BattlefieldDeploymentWriteService.IsScriptPlacementWritable(unit);
        if (_currentBattlefieldDocument != null)
        {
            _battlefieldMapPreviewSelectedUnit = GetSelectedBattlefieldUnitCandidate();
            RefreshBattlefieldMapDynamicPreview();
            _battlefieldInfoBox.Text =
                BuildBattlefieldInfo(_currentBattlefieldDocument) +
                $"\r\n\r\n已移动地图单位：\r\n" +
                $"{unit.PersonId} {unit.Name}  坐标=({oldGrid.X},{oldGrid.Y}) -> ({unit.GridX},{unit.GridY})  阵营={unit.Faction}\r\n" +
                $"职业={unit.JobId?.ToString(CultureInfo.InvariantCulture) ?? "?"} {unit.JobName}  R={unit.RImageId}  S={unit.SImageId}\r\n" +
                $"等级={unit.LevelMode}+{unit.LevelOffset}  AI={unit.AiMode}  隐藏={unit.Hidden}  援军={unit.Reinforcement}  转向={unit.Direction}\r\n" +
                (synced
                    ? "已写入左侧 S 剧本树；点击“写回出场到S剧本”后完整保存到文件。"
                    : "未绑定到可写 46/47/4B 出场设置；只能保存为布阵草稿记录。");
        }

        _saveBattlefieldUnitReviewsButton.Enabled = true;
        UpdateBattlefieldDeploymentWriteButton();
        SetStatus(synced
            ? $"战场布阵：已移动 {unit.Name} -> ({unit.GridX},{unit.GridY})，已同步到左侧 S 剧本树，尚未保存。"
            : $"战场布阵：已移动 {unit.Name} -> ({unit.GridX},{unit.GridY})；未绑定可写出场设置。");
    }

    private void SelectBattlefieldPlacedUnit(BattlefieldPlacedUnit unit, bool enterEdit, bool updatePreview = true)
    {
        var oldSelected = _selectedBattlefieldPlacedUnit;
        if (_selectedBattlefieldPlacedUnit != null &&
            !ReferenceEquals(_selectedBattlefieldPlacedUnit, unit))
        {
            if (!TryCommitPendingBattlefieldConsoleChanges()) return;
        }
        var document = _currentBattlefieldDocument;
        if (document == null) return;

        _selectedBattlefieldPlacedUnit = unit;
        if (enterEdit)
        {
            _editingBattlefieldPlacedUnit = unit;
        }
        else if (!ReferenceEquals(_editingBattlefieldPlacedUnit, unit))
        {
            _editingBattlefieldPlacedUnit = null;
        }
        SyncBattlefieldControlPanelFromPlacedUnit(unit);
        SelectBattlefieldUnitCandidateGridRow(unit.TargetKey, updatePreview);
        _battlefieldMapPreviewSelectedUnit = GetSelectedBattlefieldUnitCandidate();
        var dirtyRegion = GetBattlefieldMapGridClientRectangle(new Point(unit.GridX, unit.GridY));
        if (oldSelected != null && !ReferenceEquals(oldSelected, unit))
        {
            dirtyRegion = UnionBattlefieldDirtyRegion(
                dirtyRegion,
                GetBattlefieldMapGridClientRectangle(new Point(oldSelected.GridX, oldSelected.GridY)));
        }
        InvalidateBattlefieldMapDynamicRegion(dirtyRegion);
        if (updatePreview)
        {
            SetBattlefieldConsolePreview(
                BuildBattlefieldInfo(document) +
                $"\r\n\r\n当前地图单位：\r\n" +
                $"{unit.PersonId} {unit.Name}  坐标=({unit.GridX},{unit.GridY})  阵营={unit.Faction}\r\n" +
                $"职业={unit.JobId?.ToString(CultureInfo.InvariantCulture) ?? "?"} {unit.JobName}  R={unit.RImageId}  S={unit.SImageId}\r\n" +
                $"等级={unit.LevelMode}+{unit.LevelOffset}  AI={unit.AiMode}  隐藏={unit.Hidden}  援军={unit.Reinforcement}  转向={unit.Direction}\r\n" +
                $"状态：{(ReferenceEquals(unit, _editingBattlefieldPlacedUnit) ? "可编辑，拖拽后可同步 46/47/4B 出场设置预览" : "已选中，右键进入可编辑状态")}\r\n" +
                $"来源：{unit.Source}\r\n" +
                $"布阵记录：{unit.PlacementNote}");
        }
        SetStatus(enterEdit
            ? $"战场布阵：{unit.Name} 已进入可编辑状态。"
            : $"战场布阵：已选中 {unit.Name} ({unit.GridX},{unit.GridY})");
    }

    private bool TryHitBattlefieldPlacedUnit(Point location, out BattlefieldPlacedUnit unit)
    {
        unit = null!;
        if (!TryMapPreviewPointToGrid(location, out var x, out var y)) return false;
        for (var index = _battlefieldPlacedUnits.Count - 1; index >= 0; index--)
        {
            var candidate = _battlefieldPlacedUnits[index];
            if (candidate.GridX != x || candidate.GridY != y) continue;
            unit = candidate;
            return true;
        }

        return false;
    }

    private void ClearBattlefieldPlacedUnitSelection()
    {
        if (!TryCommitPendingBattlefieldConsoleChanges()) return;
        ClearBattlefieldBatchEditingState(syncControls: false);
        _selectedBattlefieldPlacedUnit = null;
        _editingBattlefieldPlacedUnit = null;
        _draggingBattlefieldPlacedUnit = null;
        _battlefieldPlacedUnitDragStart = null;
        _battlefieldPlacedUnitDragMoved = false;
        _battlefieldMapPreviewBox.Capture = false;
        _battlefieldMapPreviewBox.Cursor = Cursors.Default;
    }

    private void BeginBattlefieldBatchSelection(int x, int y)
    {
        if (!TryCommitPendingBattlefieldConsoleChanges()) return;

        _battlefieldBatchSelecting = true;
        _battlefieldBatchSelectionStartGrid = new Point(x, y);
        _battlefieldBatchSelectionEndGrid = new Point(x, y);
        _draggingBattlefieldPlacedUnit = null;
        _battlefieldPlacedUnitDragStart = null;
        _battlefieldPlacedUnitDragMoved = false;
        _battlefieldMapPreviewBox.Capture = true;
        _battlefieldMapPreviewBox.Cursor = Cursors.Cross;
        RefreshBattlefieldMapDynamicPreview();
        SetStatus($"战场批量编辑：框选起点 ({x},{y})，松开 Shift+左键后选中矩形内单位。");
    }

    private void ContinueBattlefieldBatchSelection(Point location)
    {
        if (!_battlefieldBatchSelecting) return;
        if (!TryMapPreviewPointToGrid(location, out var x, out var y)) return;
        if (_battlefieldBatchSelectionEndGrid.HasValue &&
            _battlefieldBatchSelectionEndGrid.Value.X == x &&
            _battlefieldBatchSelectionEndGrid.Value.Y == y)
        {
            return;
        }

        _battlefieldBatchSelectionEndGrid = new Point(x, y);
        RefreshBattlefieldMapDynamicPreview();
    }

    private void CancelBattlefieldBatchSelection()
    {
        if (!_battlefieldBatchSelecting) return;

        _battlefieldBatchSelecting = false;
        _battlefieldBatchSelectionStartGrid = null;
        _battlefieldBatchSelectionEndGrid = null;
        _battlefieldMapPreviewBox.Capture = false;
        _battlefieldMapPreviewBox.Cursor = Cursors.Default;
        RefreshBattlefieldMapDynamicPreview();
        SetStatus("战场批量编辑：已取消框选。");
    }

    private void EndBattlefieldBatchSelection(Point? location)
    {
        if (!_battlefieldBatchSelecting) return;

        if (location.HasValue && TryMapPreviewPointToGrid(location.Value, out var x, out var y))
        {
            _battlefieldBatchSelectionEndGrid = new Point(x, y);
        }

        var start = _battlefieldBatchSelectionStartGrid;
        var end = _battlefieldBatchSelectionEndGrid;
        _battlefieldBatchSelecting = false;
        _battlefieldBatchSelectionStartGrid = null;
        _battlefieldBatchSelectionEndGrid = null;
        _battlefieldMapPreviewBox.Capture = false;
        _battlefieldMapPreviewBox.Cursor = Cursors.Default;

        if (!start.HasValue || !end.HasValue)
        {
            ClearBattlefieldBatchEditingState(syncControls: true);
            RefreshBattlefieldMapDynamicPreview();
            return;
        }

        var minX = Math.Min(start.Value.X, end.Value.X);
        var maxX = Math.Max(start.Value.X, end.Value.X);
        var minY = Math.Min(start.Value.Y, end.Value.Y);
        var maxY = Math.Max(start.Value.Y, end.Value.Y);
        var selected = _battlefieldPlacedUnits
            .Where(unit => unit.GridX >= minX && unit.GridX <= maxX && unit.GridY >= minY && unit.GridY <= maxY)
            .OrderBy(unit => unit.GridY)
            .ThenBy(unit => unit.GridX)
            .ToList();

        if (selected.Count == 0)
        {
            ClearBattlefieldBatchEditingState(syncControls: true);
            if (_currentBattlefieldDocument != null)
            {
                RefreshBattlefieldMapDynamicPreview();
            }
            SetStatus($"战场批量编辑：矩形 ({minX},{minY})-({maxX},{maxY}) 内没有已摆放单位。");
            return;
        }

        SelectBattlefieldBatchUnits(selected);
    }

    private void ClearBattlefieldBatchEditingState(bool syncControls)
    {
        _ = FinalizeBattlefieldBatchTransaction();
        _batchEditingBattlefieldTargetKeys.Clear();
        _battlefieldBatchSelecting = false;
        _battlefieldBatchSelectionStartGrid = null;
        _battlefieldBatchSelectionEndGrid = null;
        _bindingBattlefieldBatchControlPanel = false;
        if (syncControls)
        {
            if (_selectedBattlefieldPlacedUnit != null && _battlefieldPlacedUnits.Contains(_selectedBattlefieldPlacedUnit))
            {
                SyncBattlefieldControlPanelFromPlacedUnit(_selectedBattlefieldPlacedUnit);
            }
            else
            {
                _battlefieldHiddenCheckBox.ThreeState = false;
            }
        }
    }

    private bool IsBattlefieldBatchEditingActive
        => _batchEditingBattlefieldTargetKeys.Count > 0;

    private IReadOnlyList<BattlefieldPlacedUnit> GetBattlefieldBatchEditingUnits()
        => _batchEditingBattlefieldTargetKeys.Count == 0
            ? Array.Empty<BattlefieldPlacedUnit>()
            : _battlefieldPlacedUnits
                .Where(unit => _batchEditingBattlefieldTargetKeys.Contains(unit.TargetKey))
                .ToList();

    private void SelectBattlefieldBatchUnits(IReadOnlyList<BattlefieldPlacedUnit> units)
    {
        if (units.Count == 0) return;

        _batchEditingBattlefieldTargetKeys.Clear();
        foreach (var unit in units)
        {
            _batchEditingBattlefieldTargetKeys.Add(unit.TargetKey);
        }

        _selectedBattlefieldPlacedUnit = units[0];
        _editingBattlefieldPlacedUnit = null;
        _draggingBattlefieldPlacedUnit = null;
        _battlefieldPlacedUnitDragStart = null;
        _battlefieldPlacedUnitDragMoved = false;
        SyncBattlefieldControlPanelFromBatchUnits(units);
        SelectBattlefieldUnitCandidateGridRow(units[0].TargetKey, updatePreview: false);
        if (_currentBattlefieldDocument != null)
        {
            RefreshBattlefieldMapDynamicPreview();
            SetBattlefieldConsolePreview(
                BuildBattlefieldInfo(_currentBattlefieldDocument) +
                "\r\n\r\n" +
                BuildBattlefieldBatchSummaryText(units));
        }
        SetStatus($"战场批量编辑：已框选 {units.Count} 个单位，修改右侧控制台字段会立即应用到这些单位。");
    }

    private bool TryMapPreviewPointToGrid(Point point, out int x, out int y)
    {
        x = 0;
        y = 0;
        var image = _battlefieldMapPreviewBox.Image;
        if (image == null || _battlefieldMapPreviewBox.ClientSize.Width <= 0 || _battlefieldMapPreviewBox.ClientSize.Height <= 0)
        {
            return false;
        }

        if (point.X < 0 || point.Y < 0 || point.X >= _battlefieldMapPreviewBox.Width || point.Y >= _battlefieldMapPreviewBox.Height)
        {
            return false;
        }

        var (gridWidth, gridHeight) = GetCurrentBattlefieldMapGridSize();
        if (gridWidth <= 0 || gridHeight <= 0)
        {
            return false;
        }

        x = Math.Clamp((int)Math.Floor(point.X / Math.Max(1f, _battlefieldMapPreviewBox.Width) * gridWidth), 0, gridWidth - 1);
        y = Math.Clamp((int)Math.Floor(point.Y / Math.Max(1f, _battlefieldMapPreviewBox.Height) * gridHeight), 0, gridHeight - 1);
        return true;
    }

    private (int Width, int Height) GetCurrentBattlefieldMapGridSize()
        => GetCurrentBattlefieldMapGridSize(_battlefieldMapPreviewBox.Image);

    private static RectangleF GetZoomedImageRectangle(Size boxSize, Size imageSize)
    {
        if (boxSize.Width <= 0 || boxSize.Height <= 0 || imageSize.Width <= 0 || imageSize.Height <= 0)
        {
            return RectangleF.Empty;
        }

        var ratio = Math.Min(boxSize.Width / (float)imageSize.Width, boxSize.Height / (float)imageSize.Height);
        var width = imageSize.Width * ratio;
        var height = imageSize.Height * ratio;
        return new RectangleF((boxSize.Width - width) / 2f, (boxSize.Height - height) / 2f, width, height);
    }

    private void ClearBattlefieldManualMarker()
    {
        _battlefieldManualMarkerTargetKey = string.Empty;
        _battlefieldManualMarkerX = -1;
        _battlefieldManualMarkerY = -1;
    }

    private void ClearBattlefieldCommand25Markers()
    {
        _battlefieldCommand25Markers.Clear();
        _battlefieldCommand25PreviewEnabled = false;
    }

    private void ToggleBattlefieldCommand25Preview()
    {
        if (_battlefieldCommand25PreviewEnabled)
        {
            ClearBattlefieldCommand25Markers();
            RefreshBattlefieldMapDynamicPreview();
            SetBattlefieldMapHint("指定地点测试预览：已取消。");
            SetStatus("战场制作：已取消指定地点测试预览");
            return;
        }

        ShowBattlefieldCommand25Preview();
    }

    private void ShowBattlefieldCommand25Preview()
    {
        if (_currentBattlefieldDocument == null)
        {
            MessageBox.Show(this, "请先读取一个战场剧本。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        if (_currentBattlefieldLegacyScriptDocument == null)
        {
            MessageBox.Show(this, "当前 S 剧本旧版结构尚未读取成功，无法解析指定地点测试。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var (gridWidth, gridHeight) = GetCurrentBattlefieldMapGridSize();
        var markers = _currentBattlefieldLegacyScriptDocument
            .EnumerateCommands()
            .Where(command => command.CommandId == 0x25)
            .Select(command => TryBuildBattlefieldCommand25Marker(command, gridWidth, gridHeight, out var marker) ? marker : null)
            .Where(marker => marker != null)
            .Select(marker => marker!)
            .GroupBy(marker => BuildBattlefieldGridKey(marker.GridX, marker.GridY), StringComparer.Ordinal)
            .Select(group =>
            {
                var first = group.OrderBy(marker => marker.Command.CommandOrdinal).First();
                return first with { Count = group.Count() };
            })
            .OrderBy(marker => marker.GridY)
            .ThenBy(marker => marker.GridX)
            .ToList();

        _battlefieldCommand25Markers.Clear();
        _battlefieldCommand25Markers.AddRange(markers);
        _battlefieldCommand25PreviewEnabled = true;
        RefreshBattlefieldMapDynamicPreview();
        SetBattlefieldMapHint(markers.Count == 0
            ? "指定地点测试预览：当前剧本没有可标记的坐标。"
            : $"指定地点测试预览：已标记 {markers.Count} 个坐标；点击标记可跳到第一条对应指令。");
        SetStatus($"战场制作：指定地点测试预览已显示 {markers.Count} 个坐标");
    }

    private static bool TryBuildBattlefieldCommand25Marker(
        LegacyScenarioCommandNode command,
        int gridWidth,
        int gridHeight,
        out BattlefieldCommand25Marker? marker)
    {
        marker = null;
        if (command.Parameters.Count < 3) return false;
        var x = command.Parameters[1].IntValue;
        var y = command.Parameters[2].IntValue;
        if (x < 0 || y < 0) return false;
        if (gridWidth > 0 && x >= gridWidth) return false;
        if (gridHeight > 0 && y >= gridHeight) return false;
        marker = new BattlefieldCommand25Marker(x, y, command, 1);
        return true;
    }

    private async Task JumpSelectedBattlefieldUnitToScriptCommandAsync()
    {
        if (_currentBattlefieldDocument == null)
        {
            MessageBox.Show(this, "请先读取一个战场关卡。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var candidate = GetSelectedBattlefieldUnitCandidate();
        if (candidate == null)
        {
            MessageBox.Show(this, "请先选择一条出场/坐标候选。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        if (!await SelectScriptScenarioByNameAsync(_currentBattlefieldDocument.Scenario.FileName))
        {
            MessageBox.Show(this, "剧本编辑页没有找到对应关卡：" + _currentBattlefieldDocument.Scenario.FileName, "无法跳转", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        if (_currentScriptStructure == null)
        {
            MessageBox.Show(this, "剧本编辑结构尚未读取成功。", "无法跳转", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        var row = BattlefieldEditorService.FindScriptCommandForCandidate(_currentScriptStructure.Rows, candidate);
        if (row == null)
        {
            MessageBox.Show(this,
                "没有在剧本编辑结构中找到该战场候选对应命令。\r\n" +
                $"候选来源：{candidate.SourceCommand} / {candidate.SceneSection} / {candidate.OffsetHex}",
                "未找到对应命令",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return;
        }

        SelectTabPageByText("剧本编辑");
        SelectScriptTreeNode(row, suppressEvents: true);
        _selectedScriptCommandRow = row;
        _selectedScriptTextEntry = null;
        BindScriptParameterRows(BuildScriptParameterRows(row));
        _scriptTextEditorBox.Clear();
        UpdateScriptTextCapacityLabel();
        _scriptDetailBox.Text =
            "从战场编辑跳转：\r\n" +
            $"战场候选：{candidate.Category} / {candidate.PersonHint} / {candidate.CoordinateHint}\r\n" +
            $"核对状态：{candidate.ReviewStatus}\r\n" +
            $"核对记录：{candidate.ReviewNote}\r\n\r\n" +
            BuildScriptRowDetail(row);
        SetStatus($"已从战场候选跳到剧本命令：{row.CommandName} {row.OffsetHex}");
    }

    private async Task<bool> SelectScriptScenarioByNameAsync(string fileName)
    {
        SelectTabPageByText("剧本编辑");
        return await EnsureScriptScenarioLoadedAsync(fileName);
    }
    private void ShowSelectedBattlefieldUnitCandidate()
    {
        if (_bindingBattlefieldUnits) return;
        if (_currentBattlefieldDocument == null) return;
        var candidate = GetSelectedBattlefieldUnitCandidate();
        _battlefieldMapPreviewSelectedUnit = candidate;
        RefreshBattlefieldMapDynamicPreview();
        if (candidate == null) return;

        var numberText = candidate.BattlefieldNumber.HasValue
            ? candidate.BattlefieldNumber.Value.ToString(CultureInfo.InvariantCulture)
            : "-";
        SetBattlefieldConsolePreview(
            BuildBattlefieldInfo(_currentBattlefieldDocument) +
            $"\r\n\r\n当前出场/坐标候选：\r\n" +
            $"战场编号：{numberText}\r\n" +
            $"来源：{DisplayOrFallback(candidate.SourceCommandDisplay, candidate.SourceCommand)}\r\n" +
            $"人物/部队：{DisplayOrFallback(candidate.PersonDisplay, candidate.PersonHint)}\r\n" +
            $"坐标：{DisplayOrFallback(candidate.CoordinateDisplay, candidate.CoordinateHint)}\r\n" +
            $"阵营：{DisplayOrFallback(candidate.FactionDisplay, candidate.FactionHint)}\r\n" +
            $"AI：{DisplayOrFallback(candidate.AiDisplay, candidate.AiHint)}\r\n" +
            $"等级/兵种级：{DisplayOrFallback(candidate.LevelJobDisplay, candidate.LevelOrStateHint)}\r\n" +
            $"核对状态：{candidate.ReviewStatus}\r\n" +
            $"核对记录：{candidate.ReviewNote}\r\n" +
            $"中文注释：{candidate.Annotation}");
    }

    private static string DisplayOrFallback(string display, string fallback)
        => string.IsNullOrWhiteSpace(display) ? fallback : display;

    private void BindBattlefieldCommandCandidates(IReadOnlyList<BattlefieldCommandCandidate> rows)
    {
        _battlefieldCommandGrid.DataSource = new BindingList<BattlefieldCommandCandidate>(rows.ToList());
        foreach (DataGridViewColumn column in _battlefieldCommandGrid.Columns)
        {
            column.ToolTipText = column.DataPropertyName switch
            {
                nameof(BattlefieldCommandCandidate.RoleHint) => "工具按命令名、参数模板和引用提示推断的战场用途分类。",
                nameof(BattlefieldCommandCandidate.ParameterPreview) => "命令后续 16 位词预览；当前用于人工核对，不直接写回未知参数。",
                nameof(BattlefieldCommandCandidate.RawContextWordsHex) => "完整原始参数词，仅供工具内部解析部署大块。",
                nameof(BattlefieldCommandCandidate.LegacyParameterLayout) => "旧编辑器参数布局，仅供工具内部解析部署大块。",
                nameof(BattlefieldCommandCandidate.CommandTemplateHint) => "来自命令模板的中文参数说明，便于识别出场、坐标、AI、等级等字段。",
                nameof(BattlefieldCommandCandidate.ReferenceHint) => "可关联的数据表、文本或地图候选。",
                nameof(BattlefieldCommandCandidate.Annotation) => "战场制作中文注释。未知结构保持原样，不强写。",
                _ => column.ToolTipText
            };
            if (column.DataPropertyName is nameof(BattlefieldCommandCandidate.RawContextWordsHex)
                or nameof(BattlefieldCommandCandidate.LegacyParameterLayout))
            {
                column.Visible = false;
            }
            if (column.DataPropertyName is nameof(BattlefieldCommandCandidate.CommandTemplateHint)
                or nameof(BattlefieldCommandCandidate.ReferenceHint)
                or nameof(BattlefieldCommandCandidate.Annotation))
            {
                column.Width = 360;
            }
        }
    }

    private void RenderBattlefieldMapPreview(BattlefieldEditorDocument document, BattlefieldUnitCandidate? selectedUnit = null)
    {
        var mapReference = document.MapReference;
        var mapId = mapReference.MapId;
        if (string.IsNullOrWhiteSpace(mapId))
        {
            ClearBattlefieldMapPreviewImages();
            SetBattlefieldMapHint(mapReference, "当前剧本无法解析有效地图号。");
            return;
        }

        try
        {
            var map = FindBattlefieldMapResourceByMapId(mapId);
            var block = _currentHexzmapProbe?.Blocks.FirstOrDefault(x => x.MapId.Equals(mapId, StringComparison.OrdinalIgnoreCase));
            var mapIdentity = BuildBattlefieldMapResourceIdentity(map);
            var hexzmapIdentity = BuildBattlefieldHexzmapIdentity(block);
            var slotIdentity = BuildBattlefieldStaticSlotIdentity();
            if (_battlefieldMapStaticPreviewImage != null &&
                _battlefieldMapStaticMapId.Equals(mapId, StringComparison.OrdinalIgnoreCase) &&
                _battlefieldMapStaticResourceIdentity.Equals(mapIdentity, StringComparison.Ordinal) &&
                _battlefieldMapStaticHexzmapIdentity.Equals(hexzmapIdentity, StringComparison.Ordinal) &&
                _battlefieldMapStaticSlotIdentity.Equals(slotIdentity, StringComparison.Ordinal))
            {
                _battlefieldMapPreviewSelectedUnit = selectedUnit;
                SetPictureBoxImage(_battlefieldMapPreviewBox, _battlefieldMapStaticPreviewImage);
                ApplyBattlefieldMapZoom();
                RefreshBattlefieldMapDynamicPreview();
                return;
            }

            using var operation = PerformanceMetrics.Begin("Battlefield.StaticMap.Build");
            ClearBattlefieldMapPreviewImages();
            if (_currentHexzmapProbe != null && block != null)
            {
                using var decodeOperation = PerformanceMetrics.Begin("Battlefield.Hexzmap.Decode");
                var cells = _hexzmapProbeReader.GetBlockCells(_currentHexzmapProbe, block);
                if (cells.Length == block.BytesRead)
                {
                    _battlefieldMapTerrainCells = cells;
                    _battlefieldMapTerrainGridSize = (block.Width, block.Height);
                    var preview = map != null && File.Exists(map.Path) && !map.SourceKind.Equals("LegacyHmRaw", StringComparison.OrdinalIgnoreCase)
                        ? _hexzmapTerrainRenderService.RenderOverlay(cells, block.Width, block.Height, map.Path, 45)
                        : RenderHexzmapCells(cells, block.Width, block.Height);
                    SetBattlefieldMapPreviewImage(preview, selectedUnit, map, mapReference, mapIdentity, hexzmapIdentity, slotIdentity);
                    return;
                }
            }

            if (map != null && File.Exists(map.Path))
            {
                using var image = RenderBattlefieldBaseMap(map);
                SetBattlefieldMapPreviewImage(new Bitmap(image), selectedUnit, map, mapReference, mapIdentity, hexzmapIdentity, slotIdentity);
                return;
            }

            SetBattlefieldMapHint(mapReference, "未找到对应的 Map/Mxxx.jpg 或 Hexzmap 地形块。");
        }
        catch (Exception ex)
        {
            ClearBattlefieldMapPreviewImages();
            SetBattlefieldMapHint(mapReference, "预览生成失败：" + ex.Message);
            System.Diagnostics.Debug.WriteLine("战场制作地图预览失败：" + ex);
        }
    }

    private Image RenderBattlefieldBaseMap(MapResourceItem map)
    {
        if (_project != null && map.SourceKind.Equals("LegacyHmRaw", StringComparison.OrdinalIgnoreCase))
        {
            return _legacyHmMapReader.RenderPreview(_project, map);
        }

        return Image.FromFile(map.Path);
    }

    private MapResourceItem? FindBattlefieldMapResourceByMapId(string mapId)
    {
        if (_project != null && _currentMapResources.Count == 0)
        {
            _currentMapResources = _mapResourceIndexer.Index(_project);
        }

        return _currentMapResources
            .FirstOrDefault(x => GetMapIdForMapResource(x).Equals(mapId, StringComparison.OrdinalIgnoreCase));
    }

    private bool HasBattlefieldMapResource(BattlefieldEditorDocument? document)
    {
        if (document == null) return false;
        var mapId = document.MapReference.MapId;
        return !string.IsNullOrWhiteSpace(mapId) && FindBattlefieldMapResourceByMapId(mapId) != null;
    }

    private void SetBattlefieldMapPreviewImage(
        Image image,
        BattlefieldUnitCandidate? selectedUnit,
        MapResourceItem? map,
        BattlefieldMapReference mapReference,
        string mapIdentity,
        string hexzmapIdentity,
        string slotIdentity)
    {
        _battlefieldMapPreviewSelectedUnit = selectedUnit;
        var (gridWidth, gridHeight) = map != null && map.GridWidth > 0 && map.GridHeight > 0
            ? (map.GridWidth, map.GridHeight)
            : GetCurrentBattlefieldMapGridSize(image);
        if (gridWidth > 0 && gridHeight > 0)
        {
            DrawBattlefieldGrid(image, gridWidth, gridHeight);
            DrawBattlefieldAllyDeploymentSlots(image, gridWidth, gridHeight);
        }

        if (selectedUnit != null &&
            selectedUnit.TargetKey.Equals(_battlefieldManualMarkerTargetKey, StringComparison.OrdinalIgnoreCase) &&
            _battlefieldManualMarkerX >= 0 &&
            _battlefieldManualMarkerY >= 0)
        {
            if (_battlefieldManualMarkerX < gridWidth && _battlefieldManualMarkerY < gridHeight)
            {
                SetBattlefieldMapHint(mapReference, $"地图点选记录：{selectedUnit.Category} {selectedUnit.SourceCommand} -> 坐标 ({_battlefieldManualMarkerX},{_battlefieldManualMarkerY})。");
            }
        }
        else if (selectedUnit != null && BattlefieldEditorService.TryExtractFirstCoordinate(selectedUnit, out var gridX, out var gridY))
        {
            if (gridX >= 0 && gridX < gridWidth && gridY >= 0 && gridY < gridHeight)
            {
                SetBattlefieldMapHint(mapReference, $"地图标记：{selectedUnit.Category} {selectedUnit.SourceCommand} -> 坐标 ({gridX},{gridY})。");
            }
            else
            {
                SetBattlefieldMapHint(mapReference, $"地图标记：解析到坐标 ({gridX},{gridY})，但超出当前地图格数范围，未绘制。");
            }
        }
        else
        {
            var allySlotText = _battlefieldAllyDeploymentSlots.Count == 0
                ? string.Empty
                : $"；我军候选出战位 {_battlefieldAllyDeploymentSlots.Count} 个（强制 {_battlefieldAllyDeploymentSlots.Count(slot => slot.IsForced)} 个）";
            SetBattlefieldMapHint(mapReference, $"{gridWidth}x{gridHeight} 格，已摆放 {_battlefieldPlacedUnits.Count} 个单位{allySlotText}。");
        }

        _battlefieldMapStaticPreviewImage = image as Bitmap ?? new Bitmap(image);
        if (!ReferenceEquals(_battlefieldMapStaticPreviewImage, image))
        {
            image.Dispose();
        }

        _battlefieldMapStaticGridSize = (gridWidth, gridHeight);
        _battlefieldMapStaticMapId = mapReference.MapId;
        _battlefieldMapStaticResourceIdentity = mapIdentity;
        _battlefieldMapStaticHexzmapIdentity = hexzmapIdentity;
        _battlefieldMapStaticSlotIdentity = slotIdentity;
        SetPictureBoxImage(_battlefieldMapPreviewBox, _battlefieldMapStaticPreviewImage);
        ApplyBattlefieldMapZoom();
        RefreshBattlefieldMapDynamicPreview();
    }

    private void RefreshBattlefieldMapDynamicPreview()
    {
        if (_battlefieldMapStaticPreviewImage == null)
        {
            SetPictureBoxImage(_battlefieldMapPreviewBox, null);
            ApplyBattlefieldMapZoom();
            return;
        }
        PerformanceMetrics.Increment("Battlefield.DynamicOverlay.Invalidate");
        _battlefieldMapPreviewBox.Invalidate();
    }

    private void InvalidateBattlefieldMapDynamicRegion(Rectangle dirtyRegion)
    {
        if (_battlefieldMapStaticPreviewImage == null || dirtyRegion.IsEmpty) return;
        PerformanceMetrics.Increment("Battlefield.DynamicOverlay.Invalidate");
        _battlefieldMapPreviewBox.Invalidate(dirtyRegion);
    }

    private Rectangle GetBattlefieldMapGridClientRectangle(Point grid)
        => GetBattlefieldMapGridRangeClientRectangle(grid, grid);

    private Rectangle GetBattlefieldMapGridRangeClientRectangle(Point first, Point second)
    {
        var (gridWidth, gridHeight) = _battlefieldMapStaticGridSize;
        if (gridWidth <= 0 || gridHeight <= 0 ||
            _battlefieldMapPreviewBox.ClientSize.Width <= 0 ||
            _battlefieldMapPreviewBox.ClientSize.Height <= 0)
        {
            return Rectangle.Empty;
        }

        var minX = Math.Clamp(Math.Min(first.X, second.X), 0, gridWidth - 1);
        var maxX = Math.Clamp(Math.Max(first.X, second.X), 0, gridWidth - 1);
        var minY = Math.Clamp(Math.Min(first.Y, second.Y), 0, gridHeight - 1);
        var maxY = Math.Clamp(Math.Max(first.Y, second.Y), 0, gridHeight - 1);
        var clientWidth = _battlefieldMapPreviewBox.ClientSize.Width;
        var clientHeight = _battlefieldMapPreviewBox.ClientSize.Height;
        var left = (int)Math.Floor(minX * clientWidth / (double)gridWidth);
        var top = (int)Math.Floor(minY * clientHeight / (double)gridHeight);
        var right = (int)Math.Ceiling((maxX + 1) * clientWidth / (double)gridWidth);
        var bottom = (int)Math.Ceiling((maxY + 1) * clientHeight / (double)gridHeight);
        var rect = Rectangle.FromLTRB(left, top, right, bottom);
        rect.Inflate(6, 6);
        return Rectangle.Intersect(_battlefieldMapPreviewBox.ClientRectangle, rect);
    }

    private static Rectangle UnionBattlefieldDirtyRegion(Rectangle current, Rectangle addition)
        => current.IsEmpty ? addition : addition.IsEmpty ? current : Rectangle.Union(current, addition);

    private void PaintBattlefieldMapDynamicOverlay(PaintEventArgs e)
    {
        if (_battlefieldMapStaticPreviewImage == null) return;
        var (gridWidth, gridHeight) = _battlefieldMapStaticGridSize;
        if (gridWidth <= 0 || gridHeight <= 0) return;

        using var operation = PerformanceMetrics.Begin("Battlefield.DynamicOverlay.Paint");
        var imageSize = _battlefieldMapStaticPreviewImage.Size;
        if (imageSize.Width <= 0 || imageSize.Height <= 0) return;

        var state = e.Graphics.Save();
        try
        {
            e.Graphics.ScaleTransform(
                _battlefieldMapPreviewBox.ClientSize.Width / (float)imageSize.Width,
                _battlefieldMapPreviewBox.ClientSize.Height / (float)imageSize.Height);
            DrawBattlefieldForcedAllyDeploymentUnits(e.Graphics, imageSize, gridWidth, gridHeight);
            DrawBattlefieldPlacedUnits(e.Graphics, imageSize, gridWidth, gridHeight);
            DrawBattlefieldAllyDeploymentOrderBadges(e.Graphics, imageSize, gridWidth, gridHeight);
            DrawBattlefieldSelectedCoordinateMarker(e.Graphics, imageSize, gridWidth, gridHeight);
            DrawBattlefieldCommand25Markers(e.Graphics, imageSize, gridWidth, gridHeight);
            DrawBattlefieldHoverCell(e.Graphics, imageSize, gridWidth, gridHeight);
            DrawBattlefieldBatchSelectionOverlay(e.Graphics, imageSize, gridWidth, gridHeight);
        }
        finally
        {
            e.Graphics.Restore(state);
        }
    }

    private void UpdateBattlefieldMapHover(Point location)
    {
        if (!TryMapPreviewPointToGrid(location, out var x, out var y))
        {
            ClearBattlefieldMapHover();
            return;
        }

        if (_battlefieldHoverGridX == x && _battlefieldHoverGridY == y)
        {
            return;
        }

        _battlefieldHoverGridX = x;
        _battlefieldHoverGridY = y;
        UpdateBattlefieldMapHoverLabel();
        RefreshBattlefieldMapDynamicPreview();
    }

    private void HandleBattlefieldMapMouseMove(MouseEventArgs e)
    {
        using var operation = PerformanceMetrics.Begin("Battlefield.MouseMove");
        if (!TryMapPreviewPointToGrid(e.Location, out var x, out var y))
        {
            ClearBattlefieldMapHover();
            return;
        }

        var dirtyRegion = Rectangle.Empty;
        var oldHover = _battlefieldHoverGridX >= 0 && _battlefieldHoverGridY >= 0
            ? new Point(_battlefieldHoverGridX, _battlefieldHoverGridY)
            : (Point?)null;
        var changed = !oldHover.HasValue || oldHover.Value.X != x || oldHover.Value.Y != y;
        if (changed)
        {
            if (oldHover.HasValue)
            {
                dirtyRegion = UnionBattlefieldDirtyRegion(dirtyRegion, GetBattlefieldMapGridClientRectangle(oldHover.Value));
            }
            _battlefieldHoverGridX = x;
            _battlefieldHoverGridY = y;
            UpdateBattlefieldMapHoverLabel();
            dirtyRegion = UnionBattlefieldDirtyRegion(dirtyRegion, GetBattlefieldMapGridClientRectangle(new Point(x, y)));
        }

        if (_battlefieldBatchSelecting)
        {
            var oldEnd = _battlefieldBatchSelectionEndGrid;
            if (!_battlefieldBatchSelectionEndGrid.HasValue ||
                _battlefieldBatchSelectionEndGrid.Value.X != x ||
                _battlefieldBatchSelectionEndGrid.Value.Y != y)
            {
                _battlefieldBatchSelectionEndGrid = new Point(x, y);
                changed = true;
                if (_battlefieldBatchSelectionStartGrid.HasValue)
                {
                    if (oldEnd.HasValue)
                    {
                        dirtyRegion = UnionBattlefieldDirtyRegion(
                            dirtyRegion,
                            GetBattlefieldMapGridRangeClientRectangle(_battlefieldBatchSelectionStartGrid.Value, oldEnd.Value));
                    }
                    dirtyRegion = UnionBattlefieldDirtyRegion(
                        dirtyRegion,
                        GetBattlefieldMapGridRangeClientRectangle(_battlefieldBatchSelectionStartGrid.Value, new Point(x, y)));
                }
            }
            if (changed)
            {
                InvalidateBattlefieldMapDynamicRegion(dirtyRegion);
            }
            return;
        }

        var oldGrid = _draggingBattlefieldPlacedUnit == null
            ? (Point?)null
            : new Point(_draggingBattlefieldPlacedUnit.GridX, _draggingBattlefieldPlacedUnit.GridY);
        ContinueBattlefieldPlacedUnitInteraction(e, new Point(x, y));
        changed |= oldGrid.HasValue && _draggingBattlefieldPlacedUnit != null &&
                   (oldGrid.Value.X != _draggingBattlefieldPlacedUnit.GridX ||
                    oldGrid.Value.Y != _draggingBattlefieldPlacedUnit.GridY);
        if (oldGrid.HasValue && _draggingBattlefieldPlacedUnit != null &&
            (oldGrid.Value.X != _draggingBattlefieldPlacedUnit.GridX ||
             oldGrid.Value.Y != _draggingBattlefieldPlacedUnit.GridY))
        {
            dirtyRegion = UnionBattlefieldDirtyRegion(dirtyRegion, GetBattlefieldMapGridClientRectangle(oldGrid.Value));
            dirtyRegion = UnionBattlefieldDirtyRegion(
                dirtyRegion,
                GetBattlefieldMapGridClientRectangle(new Point(_draggingBattlefieldPlacedUnit.GridX, _draggingBattlefieldPlacedUnit.GridY)));
        }
        if (changed)
        {
            InvalidateBattlefieldMapDynamicRegion(dirtyRegion);
        }
    }

    private void ClearBattlefieldMapHover()
    {
        if (_battlefieldHoverGridX < 0 && _battlefieldHoverGridY < 0)
        {
            SetBattlefieldMapHint(string.Empty);
            return;
        }

        var oldHover = new Point(_battlefieldHoverGridX, _battlefieldHoverGridY);
        _battlefieldHoverGridX = -1;
        _battlefieldHoverGridY = -1;
        SetBattlefieldMapHint(string.Empty);
        InvalidateBattlefieldMapDynamicRegion(GetBattlefieldMapGridClientRectangle(oldHover));
    }

    private void UpdateBattlefieldMapHoverLabel()
    {
        if (_battlefieldHoverGridX < 0 || _battlefieldHoverGridY < 0)
        {
            SetBattlefieldMapHint(string.Empty);
            return;
        }

        var terrain = TryGetBattlefieldHoverTerrain(_battlefieldHoverGridX, _battlefieldHoverGridY, out var text)
            ? text
            : "未知";
        SetBattlefieldMapHint($"地形：{terrain} · 坐标：({_battlefieldHoverGridX}, {_battlefieldHoverGridY})");
    }

    private void SetBattlefieldMapHint(string detail)
        => SetBattlefieldMapHint(_currentBattlefieldDocument?.MapReference, detail);

    private void SetBattlefieldMapHint(BattlefieldMapReference? mapReference, string detail)
    {
        var source = mapReference?.DisplayText ?? BattlefieldMapReference.Unresolved.DisplayText;
        _battlefieldMapHintLabel.Text = string.IsNullOrWhiteSpace(detail)
            ? source
            : source + " · " + detail;
    }

    private bool TryGetBattlefieldHoverTerrain(int x, int y, out string terrain)
    {
        terrain = string.Empty;
        var (width, height) = _battlefieldMapTerrainGridSize;
        if (_battlefieldMapTerrainCells.Length == 0 || x < 0 || y < 0 || x >= width || y >= height) return false;
        var index = y * width + x;
        if (index < 0 || index >= _battlefieldMapTerrainCells.Length) return false;
        terrain = FormatTerrainValue(_battlefieldMapTerrainCells[index]);
        return true;
    }

    private void DrawBattlefieldHoverCell(Graphics graphics, Size imageSize, int gridWidth, int gridHeight)
    {
        if (_battlefieldHoverGridX < 0 || _battlefieldHoverGridY < 0) return;
        if (_battlefieldHoverGridX >= gridWidth || _battlefieldHoverGridY >= gridHeight) return;

        var cellWidth = imageSize.Width / (float)gridWidth;
        var cellHeight = imageSize.Height / (float)gridHeight;
        var rect = new RectangleF(
            _battlefieldHoverGridX * cellWidth,
            _battlefieldHoverGridY * cellHeight,
            cellWidth,
            cellHeight);
        using var fill = new SolidBrush(Color.FromArgb(55, Color.Gold));
        using var outer = new Pen(Color.FromArgb(230, Color.Gold), 2);
        using var inner = new Pen(Color.FromArgb(210, Color.Black), 1);
        graphics.FillRectangle(fill, rect);
        graphics.DrawRectangle(outer, rect.X, rect.Y, Math.Max(1, rect.Width - 1), Math.Max(1, rect.Height - 1));
        graphics.DrawRectangle(inner, rect.X + 2, rect.Y + 2, Math.Max(1, rect.Width - 5), Math.Max(1, rect.Height - 5));
    }

    private void ClearBattlefieldMapPreviewImages()
    {
        // SetPictureBoxImage owns and disposes the image previously assigned to the box.
        // Clear the cache reference first so the same bitmap is never disposed twice.
        _battlefieldMapStaticPreviewImage = null;
        SetPictureBoxImage(_battlefieldMapPreviewBox, null);
        _battlefieldMapStaticGridSize = default;
        _battlefieldMapStaticMapId = string.Empty;
        _battlefieldMapStaticResourceIdentity = string.Empty;
        _battlefieldMapStaticHexzmapIdentity = string.Empty;
        _battlefieldMapStaticSlotIdentity = string.Empty;
        _battlefieldMapTerrainCells = Array.Empty<byte>();
        _battlefieldMapTerrainGridSize = default;
        _battlefieldMapPreviewSelectedUnit = null;
        _battlefieldHoverGridX = -1;
        _battlefieldHoverGridY = -1;
        ApplyBattlefieldMapZoom();
    }

    private void InvalidateBattlefieldStaticMapCache(bool rebuildCurrentPreview)
    {
        ClearBattlefieldMapPreviewImages();
        if (rebuildCurrentPreview && _currentBattlefieldDocument != null && !IsDisposed)
        {
            RenderBattlefieldMapPreview(_currentBattlefieldDocument, GetSelectedBattlefieldUnitCandidate());
        }
    }

    private static string BuildBattlefieldMapResourceIdentity(MapResourceItem? map)
    {
        if (map == null) return string.Empty;
        var lastWriteTicks = File.Exists(map.Path) ? File.GetLastWriteTimeUtc(map.Path).Ticks : 0L;
        var length = File.Exists(map.Path) ? new FileInfo(map.Path).Length : -1L;
        return string.Join("|", map.Path, map.SourceKind, map.GridWidth, map.GridHeight, lastWriteTicks, length);
    }

    private string BuildBattlefieldHexzmapIdentity(HexzmapBlockInfo? block)
    {
        if (_currentHexzmapProbe == null || block == null) return string.Empty;
        var lastWriteTicks = File.Exists(_currentHexzmapProbe.Path)
            ? File.GetLastWriteTimeUtc(_currentHexzmapProbe.Path).Ticks
            : 0L;
        var length = File.Exists(_currentHexzmapProbe.Path)
            ? new FileInfo(_currentHexzmapProbe.Path).Length
            : -1L;
        return string.Join("|", _currentHexzmapProbe.Path, block.Index, block.Width, block.Height, block.BytesRead, lastWriteTicks, length);
    }

    private string BuildBattlefieldStaticSlotIdentity()
        => string.Join(
            ";",
            _battlefieldAllyDeploymentSlots
                .OrderBy(slot => slot.Order)
                .ThenBy(slot => slot.GridY)
                .ThenBy(slot => slot.GridX)
                .Select(slot => string.Join(",", slot.Order, slot.GridX, slot.GridY, slot.IsForced, slot.PersonId)));

    private void HandleBattlefieldMapMouseWheel(MouseEventArgs e)
    {
        if (_battlefieldMapPreviewBox.Image == null || e.Delta == 0) return;

        var oldZoom = Math.Max(0.01, _battlefieldMapZoomPercent / 100.0);
        var panelPoint = _battlefieldMapScrollPanel.PointToClient(Control.MousePosition);
        var imagePointX = (panelPoint.X - _battlefieldMapPreviewBox.Left) / oldZoom;
        var imagePointY = (panelPoint.Y - _battlefieldMapPreviewBox.Top) / oldZoom;
        var step = ModifierKeys.HasFlag(Keys.Control) ? 25 : 10;
        var nextZoom = _battlefieldMapZoomPercent + (e.Delta > 0 ? step : -step);
        _battlefieldMapZoomPercent = Math.Clamp(nextZoom, 25, 800);
        ApplyBattlefieldMapZoom();

        var newZoom = _battlefieldMapZoomPercent / 100.0;
        var scrollX = Math.Max(0, (int)Math.Round(imagePointX * newZoom - panelPoint.X));
        var scrollY = Math.Max(0, (int)Math.Round(imagePointY * newZoom - panelPoint.Y));
        _battlefieldMapScrollPanel.AutoScrollPosition = new Point(scrollX, scrollY);
    }

    private void ResetBattlefieldMapZoom()
    {
        _battlefieldMapZoomPercent = 100;
        ApplyBattlefieldMapZoom();
        _battlefieldMapScrollPanel.AutoScrollPosition = Point.Empty;
    }

    private void ApplyBattlefieldMapZoom()
    {
        if (!TryGetPictureBoxImageSize(_battlefieldMapPreviewBox, out var imageSize))
        {
            _battlefieldMapZoomPercent = Math.Clamp(_battlefieldMapZoomPercent, 25, 800);
            _battlefieldMapPreviewBox.Size = Size.Empty;
            _battlefieldMapZoomLabel.Text = $"缩放 {_battlefieldMapZoomPercent}%";
            return;
        }

        var zoom = Math.Clamp(_battlefieldMapZoomPercent, 25, 800) / 100.0;
        _battlefieldMapZoomPercent = (int)Math.Round(zoom * 100);
        _battlefieldMapPreviewBox.Size = new Size(
            Math.Max(1, (int)Math.Round(imageSize.Width * zoom)),
            Math.Max(1, (int)Math.Round(imageSize.Height * zoom)));
        _battlefieldMapZoomLabel.Text = $"缩放 {_battlefieldMapZoomPercent}%";
    }

    private (int Width, int Height) GetCurrentBattlefieldMapGridSize(Image? image)
    {
        if (TryGetImageSize(image, out var imageSize) &&
            imageSize.Width % MapResourceItem.MapTilePixelSize == 0 &&
            imageSize.Height % MapResourceItem.MapTilePixelSize == 0)
        {
            return (imageSize.Width / MapResourceItem.MapTilePixelSize, imageSize.Height / MapResourceItem.MapTilePixelSize);
        }

        return (0, 0);
    }

    private static bool TryGetImageSize(Image? image, out Size size)
    {
        size = Size.Empty;
        if (image == null) return false;

        try
        {
            var width = image.Width;
            var height = image.Height;
            if (width <= 0 || height <= 0) return false;
            size = new Size(width, height);
            return true;
        }
        catch (Exception ex) when (ex is ArgumentException or ObjectDisposedException)
        {
            ApplicationErrorService.Report(ex, "Image size", notify: false);
            return false;
        }
    }

    private static void DrawBattlefieldGrid(Image image, int gridWidth, int gridHeight)
    {
        if (gridWidth <= 0 || gridHeight <= 0) return;
        using var graphics = Graphics.FromImage(image);
        using var pen = new Pen(Color.FromArgb(120, Color.White), 1);
        var cellWidth = image.Width / (float)gridWidth;
        var cellHeight = image.Height / (float)gridHeight;
        for (var x = 0; x <= gridWidth; x++)
        {
            var px = x * cellWidth;
            graphics.DrawLine(pen, px, 0, px, image.Height);
        }

        for (var y = 0; y <= gridHeight; y++)
        {
            var py = y * cellHeight;
            graphics.DrawLine(pen, 0, py, image.Width, py);
        }
    }

    private void DrawBattlefieldPlacedUnits(Graphics graphics, Size imageSize, int gridWidth, int gridHeight)
    {
        if (_project == null || gridWidth <= 0 || gridHeight <= 0) return;
        var cellWidth = imageSize.Width / (float)gridWidth;
        var cellHeight = imageSize.Height / (float)gridHeight;

        foreach (var unit in _battlefieldPlacedUnits)
        {
            if (unit.GridX < 0 || unit.GridX >= gridWidth || unit.GridY < 0 || unit.GridY >= gridHeight) continue;
            var rect = new RectangleF(unit.GridX * cellWidth, unit.GridY * cellHeight, cellWidth, cellHeight);
            var factionColor = unit.Faction switch
            {
                "敌军" => Color.FromArgb(220, 245, 84, 84),
                "友军" => Color.FromArgb(220, 92, 160, 255),
                _ => Color.FromArgb(220, 88, 210, 110)
            };
            var isEditing = ReferenceEquals(unit, _editingBattlefieldPlacedUnit);
            var isSelected = ReferenceEquals(unit, _selectedBattlefieldPlacedUnit);
            var isBatchSelected = _batchEditingBattlefieldTargetKeys.Contains(unit.TargetKey);
            using var borderPen = new Pen(
                isEditing ? Color.Orange : isSelected ? Color.Yellow : isBatchSelected ? Color.Cyan : factionColor,
                isEditing ? 5 : isSelected ? 4 : isBatchSelected ? 4 : 2);
            graphics.DrawRectangle(borderPen, rect.X, rect.Y, rect.Width, rect.Height);

            var preview = TryGetBattlefieldSImageFrame(unit.SImageId, unit.JobId, GetBattlefieldFactionSlot(unit.Faction), unit.Direction, unit.LevelMode, _battlefieldUnitAnimationPhase);
            if (preview != null)
            {
                var drawRect = FitImageIntoRect(preview.Size, Rectangle.Round(rect));
                if (unit.Hidden)
                {
                    DrawImageWithOpacity(graphics, preview, drawRect, 0.55f);
                }
                else
                {
                    graphics.DrawImage(preview, drawRect);
                }
            }
            else
            {
                using var labelFont = new Font(Font.FontFamily, Math.Max(8, Math.Min(14, rect.Height / 4f)), FontStyle.Bold);
                using var textBrush = new SolidBrush(Color.White);
                graphics.DrawString(unit.Name.Length > 0 ? unit.Name[..Math.Min(2, unit.Name.Length)] : unit.PersonId.ToString(CultureInfo.InvariantCulture), labelFont, textBrush, rect.X + 3, rect.Y + 3);
            }

            if (unit.Hidden)
            {
                var badge = new RectangleF(rect.Right - 16, rect.Top + 2, 14, 14);
                using var hiddenBrush = new SolidBrush(Color.FromArgb(170, Color.Black));
                graphics.FillEllipse(hiddenBrush, badge);
                graphics.DrawString("隐", Font, Brushes.White, badge.X - 1, badge.Y - 2);
            }
            if (unit.Reinforcement)
            {
                var badge = new RectangleF(rect.Left + 2, rect.Top + 2, 14, 14);
                using var reinforcementBrush = new SolidBrush(Color.FromArgb(190, 30, 95, 180));
                graphics.FillEllipse(reinforcementBrush, badge);
                graphics.DrawString("援", Font, Brushes.White, badge.X - 1, badge.Y - 2);
            }
        }
    }

    private void DrawBattlefieldAllyDeploymentSlots(Image image, int gridWidth, int gridHeight)
    {
        if (_battlefieldAllyDeploymentSlots.Count == 0 || gridWidth <= 0 || gridHeight <= 0) return;

        using var graphics = Graphics.FromImage(image);
        var cellWidth = image.Width / (float)gridWidth;
        var cellHeight = image.Height / (float)gridHeight;
        var orderedSlots = _battlefieldAllyDeploymentSlots
            .OrderBy(slot => slot.Order)
            .ThenBy(slot => slot.GridY)
            .ThenBy(slot => slot.GridX)
            .ToList();

        foreach (var slot in orderedSlots)
        {
            if (slot.GridX < 0 || slot.GridX >= gridWidth || slot.GridY < 0 || slot.GridY >= gridHeight) continue;

            var rect = new RectangleF(slot.GridX * cellWidth, slot.GridY * cellHeight, cellWidth, cellHeight);
            var markerRect = RectangleF.Inflate(rect, -Math.Max(2f, cellWidth * 0.12f), -Math.Max(2f, cellHeight * 0.12f));
            if (slot.IsForced)
            {
                DrawBattlefieldForcedAllyDeploymentSlot(graphics, slot, rect, markerRect);
            }
            else
            {
                DrawBattlefieldCandidateAllyDeploymentSlot(graphics, slot, markerRect);
            }
        }
    }

    private void DrawBattlefieldForcedAllyDeploymentSlot(Graphics graphics, BattlefieldAllyDeploymentSlot slot, RectangleF rect, RectangleF markerRect)
    {
        using var fillBrush = new SolidBrush(Color.FromArgb(90, 30, 160, 80));
        using var borderPen = new Pen(Color.FromArgb(230, 75, 220, 120), 3);
        graphics.FillRectangle(fillBrush, markerRect);
        graphics.DrawRectangle(borderPen, markerRect.X, markerRect.Y, markerRect.Width, markerRect.Height);
    }

    private void DrawBattlefieldForcedAllyDeploymentUnits(Graphics graphics, Size imageSize, int gridWidth, int gridHeight)
    {
        if (_project == null || _battlefieldAllyDeploymentSlots.Count == 0 || gridWidth <= 0 || gridHeight <= 0) return;

        var cellWidth = imageSize.Width / (float)gridWidth;
        var cellHeight = imageSize.Height / (float)gridHeight;
        var placedAllyPersonIds = _battlefieldPlacedUnits
            .Where(unit => unit.PersonId >= 0 && unit.Faction.Equals("我军", StringComparison.Ordinal))
            .Select(unit => unit.PersonId)
            .ToHashSet();
        foreach (var slot in _battlefieldAllyDeploymentSlots.Where(slot => slot.IsForced))
        {
            if (slot.GridX < 0 || slot.GridX >= gridWidth || slot.GridY < 0 || slot.GridY >= gridHeight) continue;
            if (slot.PersonId.HasValue && placedAllyPersonIds.Contains(slot.PersonId.Value)) continue;

            var rect = new RectangleF(slot.GridX * cellWidth, slot.GridY * cellHeight, cellWidth, cellHeight);
            var preview = TryGetBattlefieldSImageFrame(
                slot.SImageId ?? 0,
                slot.JobId,
                GetBattlefieldFactionSlot("我军"),
                slot.Direction,
                "初级",
                _battlefieldUnitAnimationPhase);
            if (preview != null)
            {
                var drawRect = FitImageIntoRect(preview.Size, Rectangle.Round(rect));
                if (slot.Hidden)
                {
                    DrawImageWithOpacity(graphics, preview, drawRect, 0.55f);
                    var badge = new RectangleF(rect.Right - 16, rect.Top + 2, 14, 14);
                    using var hiddenBrush = new SolidBrush(Color.FromArgb(170, Color.Black));
                    graphics.FillEllipse(hiddenBrush, badge);
                    graphics.DrawString("隐", Font, Brushes.White, badge.X - 1, badge.Y - 2);
                }
                else
                {
                    graphics.DrawImage(preview, drawRect);
                }
                continue;
            }

            using var labelFont = new Font(Font.FontFamily, Math.Max(8, Math.Min(14, rect.Height / 4f)), FontStyle.Bold);
            using var textBrush = new SolidBrush(Color.White);
            var label = string.IsNullOrWhiteSpace(slot.Name)
                ? slot.PersonId?.ToString(CultureInfo.InvariantCulture) ?? "强"
                : slot.Name[..Math.Min(2, slot.Name.Length)];
            graphics.DrawString(label, labelFont, textBrush, rect.X + 3, rect.Y + 3);
        }
    }

    private void DrawBattlefieldAllyDeploymentOrderBadges(Graphics graphics, Size imageSize, int gridWidth, int gridHeight)
    {
        if (_battlefieldAllyDeploymentSlots.Count == 0 || gridWidth <= 0 || gridHeight <= 0) return;

        var cellWidth = imageSize.Width / (float)gridWidth;
        var cellHeight = imageSize.Height / (float)gridHeight;
        foreach (var slot in _battlefieldAllyDeploymentSlots.OrderBy(slot => slot.Order).ThenBy(slot => slot.GridY).ThenBy(slot => slot.GridX))
        {
            if (slot.GridX < 0 || slot.GridX >= gridWidth || slot.GridY < 0 || slot.GridY >= gridHeight) continue;
            var rect = new RectangleF(slot.GridX * cellWidth, slot.GridY * cellHeight, cellWidth, cellHeight);
            DrawBattlefieldAllyDeploymentOrderBadge(graphics, slot, rect);
        }
    }

    private void DrawBattlefieldCandidateAllyDeploymentSlot(Graphics graphics, BattlefieldAllyDeploymentSlot slot, RectangleF markerRect)
    {
        var radius = Math.Min(markerRect.Width, markerRect.Height) / 2f;
        var centerX = markerRect.X + markerRect.Width / 2f;
        var centerY = markerRect.Y + markerRect.Height / 2f;
        using var fillBrush = new SolidBrush(Color.FromArgb(90, 30, 180, 110));
        using var shadowPen = new Pen(Color.FromArgb(170, Color.Black), 4);
        using var borderPen = new Pen(Color.FromArgb(235, 60, 230, 150), 2);
        graphics.FillEllipse(fillBrush, centerX - radius, centerY - radius, radius * 2, radius * 2);
        graphics.DrawEllipse(shadowPen, centerX - radius, centerY - radius, radius * 2, radius * 2);
        graphics.DrawEllipse(borderPen, centerX - radius, centerY - radius, radius * 2, radius * 2);
    }

    private void DrawBattlefieldAllyDeploymentOrderBadge(Graphics graphics, BattlefieldAllyDeploymentSlot slot, RectangleF rect)
    {
        var badgeSize = Math.Max(16f, Math.Min(rect.Width, rect.Height) * 0.38f);
        var badge = new RectangleF(rect.Left + 2, rect.Top + 2, badgeSize, badgeSize);
        using var badgeBrush = new SolidBrush(slot.IsForced ? Color.FromArgb(230, 20, 120, 55) : Color.FromArgb(225, 20, 90, 70));
        using var badgePen = new Pen(Color.White, 1);
        graphics.FillEllipse(badgeBrush, badge);
        graphics.DrawEllipse(badgePen, badge);

        var label = slot.DisplayOrder.ToString(CultureInfo.InvariantCulture);
        using var font = new Font(Font.FontFamily, Math.Max(7f, badgeSize * 0.45f), FontStyle.Bold);
        var size = graphics.MeasureString(label, font);
        graphics.DrawString(
            label,
            font,
            Brushes.White,
            badge.X + (badge.Width - size.Width) / 2f,
            badge.Y + (badge.Height - size.Height) / 2f);
    }

    private static void DrawImageWithOpacity(Graphics graphics, Image image, Rectangle destination, float opacity)
    {
        using var attributes = new ImageAttributes();
        var matrix = new ColorMatrix { Matrix33 = Math.Clamp(opacity, 0f, 1f) };
        attributes.SetColorMatrix(matrix, ColorMatrixFlag.Default, ColorAdjustType.Bitmap);
        graphics.DrawImage(
            image,
            destination,
            0,
            0,
            image.Width,
            image.Height,
            GraphicsUnit.Pixel,
            attributes);
    }

    private static Rectangle FitImageIntoRect(Size imageSize, Rectangle rect)
    {
        if (imageSize.Width <= 0 || imageSize.Height <= 0 || rect.Width <= 0 || rect.Height <= 0) return rect;
        var scale = Math.Min(rect.Width / (float)imageSize.Width, rect.Height / (float)imageSize.Height);
        var width = Math.Max(1, (int)Math.Round(imageSize.Width * scale));
        var height = Math.Max(1, (int)Math.Round(imageSize.Height * scale));
        return new Rectangle(rect.Left + (rect.Width - width) / 2, rect.Top + (rect.Height - height) / 2, width, height);
    }

    private Bitmap? TryGetBattlefieldSImageFrame(int sImageId, int? jobId, int factionSlot, string direction, string levelMode, int framePhase)
    {
        if (_project == null) return null;
        var normalizedDirection = NormalizeBattlefieldDirection(direction);
        var normalizedLevelMode = NormalizeBattlefieldLevelMode(levelMode);
        var phase = Math.Abs(framePhase) % 2;
        var cacheKey = string.Join(
            "|",
            Path.GetFullPath(_project.GameRoot),
            sImageId.ToString(CultureInfo.InvariantCulture),
            jobId?.ToString() ?? string.Empty,
            factionSlot.ToString(CultureInfo.InvariantCulture),
            normalizedDirection,
            normalizedLevelMode,
            phase.ToString(CultureInfo.InvariantCulture));
        if (_battlefieldUnitFrameCache.TryGetValue(cacheKey, out var cached))
        {
            return cached;
        }

        try
        {
            var frame = _imageAssignmentPreviewService.TryRenderBattlefieldMoveIdleFrame(
                _project,
                sImageId,
                jobId,
                factionSlot,
                normalizedDirection,
                phase,
                normalizedLevelMode,
                out _);
            if (frame == null) return null;

            _battlefieldUnitFrameCache[cacheKey] = frame;
            return frame;
        }
        catch
        {
            return null;
        }
    }

    private Bitmap? CloneBattlefieldSImageFrame(int sImageId, int? jobId, int factionSlot, string direction, string levelMode, int framePhase)
    {
        var frame = TryGetBattlefieldSImageFrame(sImageId, jobId, factionSlot, direction, levelMode, framePhase);
        return frame == null ? null : new Bitmap(frame);
    }

    private void ClearBattlefieldUnitFrameCache()
    {
        foreach (var frame in _battlefieldUnitFrameCache.Values)
        {
            frame.Dispose();
        }

        _battlefieldUnitFrameCache.Clear();
    }

    private void AdvanceBattlefieldUnitAnimation()
    {
        if (_project == null || !IsBattlefieldEditorTabActive()) return;
        if (_draggingBattlefieldPlacedUnit != null || _battlefieldBatchSelecting) return;
        var hasPalettePreview = _battlefieldUnitListBox.SelectedItem is BattlefieldUnitPaletteItem;
        var hasMapUnits = _currentBattlefieldDocument != null &&
                          (_battlefieldPlacedUnits.Count > 0 || _battlefieldAllyDeploymentSlots.Any(slot => slot.IsForced));
        if (!hasPalettePreview && !hasMapUnits) return;

        _battlefieldUnitAnimationPhase = 1 - _battlefieldUnitAnimationPhase;
        try
        {
            if (hasPalettePreview)
            {
                RefreshBattlefieldPaletteUnitPreview(_battlefieldUnitListBox.SelectedItem as BattlefieldUnitPaletteItem);
            }

            if (hasMapUnits && _currentBattlefieldDocument != null)
            {
                if (_battlefieldMapStaticPreviewImage != null)
                {
                    var dirtyRegion = Rectangle.Empty;
                    foreach (var unit in _battlefieldPlacedUnits)
                    {
                        dirtyRegion = UnionBattlefieldDirtyRegion(
                            dirtyRegion,
                            GetBattlefieldMapGridClientRectangle(new Point(unit.GridX, unit.GridY)));
                    }
                    foreach (var slot in _battlefieldAllyDeploymentSlots.Where(slot => slot.IsForced))
                    {
                        dirtyRegion = UnionBattlefieldDirtyRegion(
                            dirtyRegion,
                            GetBattlefieldMapGridClientRectangle(new Point(slot.GridX, slot.GridY)));
                    }
                    InvalidateBattlefieldMapDynamicRegion(dirtyRegion);
                }
                else
                {
                    RenderBattlefieldMapPreview(_currentBattlefieldDocument, GetSelectedBattlefieldUnitCandidate());
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine("战场待机帧刷新失败：" + ex.Message);
        }
    }

    private bool IsBattlefieldEditorTabActive()
        => _mainTabs.SelectedTab?.Text == "战场编辑";

    private static string NormalizeBattlefieldDirection(string direction)
        => direction switch
        {
            "上" => "上",
            "右" => "右",
            "左" => "左",
            _ => "下"
        };

    private static string NormalizeBattlefieldLevelMode(string levelMode)
        => levelMode switch
        {
            "中级" => "中级",
            "高级" => "高级",
            _ => "初级"
        };

    private static int GetBattlefieldFactionSlot(string faction)
        => faction switch
        {
            "友军" => 2,
            "敌军" => 3,
            _ => 1
        };

    private string GetSelectedBattlefieldFaction()
    {
        if (_battlefieldFactionEnemyRadio.Checked) return "敌军";
        if (_battlefieldFactionFriendRadio.Checked) return "友军";
        return "我军";
    }

    private void BeginBattlefieldUnitDrag(Point location)
    {
        _battlefieldUnitDragStart = null;
        _battlefieldUnitDragItem = null;
        var index = _battlefieldUnitListBox.IndexFromPoint(location);
        if (index < 0) return;
        _battlefieldUnitListBox.SelectedIndex = index;
        if (_battlefieldUnitListBox.Items[index] is not BattlefieldUnitPaletteItem item) return;

        _selectedBattlefieldPaletteItem = item;
        RefreshBattlefieldPaletteUnitPreview(item);
        _battlefieldUnitDragStart = location;
        _battlefieldUnitDragItem = item;
    }

    private void ContinueBattlefieldUnitDrag(Point location, MouseButtons buttons)
    {
        if (buttons != MouseButtons.Left || _battlefieldUnitDragStart == null || _battlefieldUnitDragItem == null) return;

        var start = _battlefieldUnitDragStart.Value;
        var dragSize = SystemInformation.DragSize;
        var dragRect = new Rectangle(
            start.X - dragSize.Width / 2,
            start.Y - dragSize.Height / 2,
            dragSize.Width,
            dragSize.Height);
        if (dragRect.Contains(location)) return;

        var item = _battlefieldUnitDragItem;
        ClearBattlefieldUnitDrag();
        _battlefieldUnitListBox.DoDragDrop(item, DragDropEffects.Copy);
    }

    private void ClearBattlefieldUnitDrag()
    {
        _battlefieldUnitDragStart = null;
        _battlefieldUnitDragItem = null;
    }

    private static void HandleBattlefieldMapDragEnter(DragEventArgs e)
    {
        e.Effect = e.Data?.GetDataPresent(typeof(BattlefieldUnitPaletteItem)) == true
            ? DragDropEffects.Copy
            : DragDropEffects.None;
    }

    private void HandleBattlefieldMapDragDrop(DragEventArgs e)
    {
        if (_currentBattlefieldDocument == null) return;
        if (e.Data?.GetData(typeof(BattlefieldUnitPaletteItem)) is not BattlefieldUnitPaletteItem item) return;
        var point = _battlefieldMapPreviewBox.PointToClient(new Point(e.X, e.Y));
        if (!TryMapPreviewPointToGrid(point, out var x, out var y))
        {
            SetStatus("战场布阵：拖放位置不在地图格内。");
            return;
        }

        var faction = GetSelectedBattlefieldFaction();
        var existing = _battlefieldPlacedUnits.FirstOrDefault(unit => unit.GridX == x && unit.GridY == y);
        var scriptBacked = TryResolveBattlefieldDropScriptBinding(item, x, y, faction, existing, out var scriptTargetKey, out var scriptCandidate, out var scriptPlacement);
        if (!scriptBacked)
        {
            scriptTargetKey = string.Empty;
            scriptCandidate = null;
            scriptPlacement = null;
        }

        if (existing != null)
        {
            _battlefieldPlacedUnits.Remove(existing);
        }

        if (scriptPlacement != null && !ReferenceEquals(scriptPlacement, existing))
        {
            _battlefieldPlacedUnits.Remove(scriptPlacement);
        }

        var placed = new BattlefieldPlacedUnit
        {
            TargetKey = scriptBacked
                ? scriptTargetKey
                : $"Placement#{_currentBattlefieldDocument.Scenario.FileName}#{x},{y}#{item.PersonId}",
            PersonId = item.PersonId,
            PersonRawCode = scriptBacked && !IsBattlefieldAllyDeploymentTargetKey(scriptTargetKey)
                ? BattlefieldEditorService.EncodePerson2ScriptCode(item.PersonId)
                : null,
            Name = item.Name,
            JobId = item.JobId,
            JobName = item.JobName,
            RImageId = item.RImageId,
            SImageId = item.SImageId,
            Faction = scriptPlacement?.Faction ?? (scriptCandidate != null ? InferBattlefieldFaction(scriptCandidate) : faction),
            LevelOffset = (int)_battlefieldLevelOffsetInput.Value,
            LevelMode = _battlefieldLevelModeCombo.SelectedItem?.ToString() ?? "初级",
            AiMode = _battlefieldAiModeCombo.SelectedItem?.ToString() ?? "被动",
            Hidden = _battlefieldHiddenCheckBox.Checked,
            Reinforcement = scriptPlacement?.Reinforcement ?? scriptCandidate?.ReinforcementDisplay.Contains("援军", StringComparison.Ordinal) == true,
            Direction = _battlefieldDirectionCombo.SelectedItem?.ToString() ?? "下",
            GridX = x,
            GridY = y,
            Source = scriptBacked ? "S剧本出场设置(拖放调整)" : "拖放",
            PlacementNote = scriptBacked && scriptCandidate != null
                ? BuildBattlefieldScriptBoundPlacementNote(item, x, y, scriptCandidate)
                : BuildBattlefieldPlacementNote(item, x, y)
        };
        _battlefieldPlacedUnits.Add(placed);
        ClearBattlefieldBatchEditingState(syncControls: false);
        _selectedBattlefieldPlacedUnit = placed;
        _editingBattlefieldPlacedUnit = null;
        _draggingBattlefieldPlacedUnit = null;
        _battlefieldPlacedUnitDragStart = null;
        _battlefieldPlacedUnitDragMoved = false;
        SyncBattlefieldControlPanelFromPlacedUnit(placed);
        _battlefieldMapPreviewSelectedUnit = null;
        RefreshBattlefieldMapDynamicPreview();
        _saveBattlefieldUnitReviewsButton.Enabled = true;
        UpdateBattlefieldDeploymentWriteButton();
        SetStatus($"战场布阵：{item.DisplayText} -> ({x},{y})，地图已更新，正在同步左侧 S 剧本树...");
        _pendingBattlefieldDropSynchronizations.Add(placed);
        BeginInvoke(new Action(() => SynchronizeBattlefieldDropAfterPaint(placed, item.DisplayText)));
    }

    private void SynchronizeBattlefieldDropAfterPaint(BattlefieldPlacedUnit placed, string displayText)
    {
        if (!_pendingBattlefieldDropSynchronizations.Remove(placed)) return;
        if (IsDisposed || !_battlefieldPlacedUnits.Contains(placed)) return;
        var synced = ApplyBattlefieldPlacementToCurrentScript(placed, "拖放");
        SetStatus(synced
            ? $"战场布阵：{displayText} 已写入左侧 S 剧本树，尚未完整保存。"
            : $"战场布阵：{displayText} 未找到可用的 46/47/4B 出场设置槽，需保存为布阵草稿。");
    }

    private void FlushPendingBattlefieldDropSynchronizations()
    {
        if (_flushingBattlefieldDropSynchronizations || _pendingBattlefieldDropSynchronizations.Count == 0) return;
        _flushingBattlefieldDropSynchronizations = true;
        try
        {
            foreach (var placed in _pendingBattlefieldDropSynchronizations.ToArray())
            {
                SynchronizeBattlefieldDropAfterPaint(placed, placed.Name);
            }
        }
        finally
        {
            _flushingBattlefieldDropSynchronizations = false;
        }
    }

    private string BuildBattlefieldPlacementNote(BattlefieldUnitPaletteItem item, int x, int y)
        => $"地图摆放：{item.DisplayText} ({GetSelectedBattlefieldFaction()}) 坐标=({x},{y})，等级={_battlefieldLevelModeCombo.SelectedItem}+{_battlefieldLevelOffsetInput.Value}，AI={_battlefieldAiModeCombo.SelectedItem}，隐藏={_battlefieldHiddenCheckBox.Checked}，转向={_battlefieldDirectionCombo.SelectedItem}。请在 S 剧本命令参数确认后再写入游戏文件。";

    private string BuildBattlefieldScriptBoundPlacementNote(BattlefieldUnitPaletteItem item, int x, int y, BattlefieldUnitCandidate candidate)
        => $"地图拖放预览：{item.DisplayText} 绑定 S 剧本记录 {candidate.SourceCommand} / {candidate.SceneSection} / {candidate.OffsetHex}，预览人物={item.PersonId}，坐标=({x},{y})，AI={_battlefieldAiModeCombo.SelectedItem}。点击“写回出场到S剧本”前不会修改原文件。";

    private bool TryResolveBattlefieldDropScriptBinding(
        BattlefieldUnitPaletteItem item,
        int gridX,
        int gridY,
        string faction,
        BattlefieldPlacedUnit? existingAtDropGrid,
        out string targetKey,
        out BattlefieldUnitCandidate? candidate,
        out BattlefieldPlacedUnit? placement)
    {
        targetKey = string.Empty;
        candidate = null;
        placement = null;

        if (existingAtDropGrid != null && BattlefieldDeploymentWriteService.IsScriptPlacementWritable(existingAtDropGrid))
        {
            targetKey = existingAtDropGrid.TargetKey;
            candidate = FindBattlefieldUnitCandidateByTargetKey(targetKey);
            placement = existingAtDropGrid;
            return candidate != null;
        }

        if (_selectedBattlefieldPlacedUnit != null &&
            _selectedBattlefieldPlacedUnit.PersonId == item.PersonId &&
            _selectedBattlefieldPlacedUnit.Faction.Equals(faction, StringComparison.Ordinal) &&
            BattlefieldDeploymentWriteService.IsScriptPlacementWritable(_selectedBattlefieldPlacedUnit))
        {
            targetKey = _selectedBattlefieldPlacedUnit.TargetKey;
            candidate = FindBattlefieldUnitCandidateByTargetKey(targetKey);
            placement = _selectedBattlefieldPlacedUnit;
            return candidate != null;
        }

        var selectedCandidate = GetSelectedBattlefieldUnitCandidate();
        if (selectedCandidate != null &&
            CanUseBattlefieldSelectedCandidateForDrop(selectedCandidate, item, faction, existingAtDropGrid))
        {
            targetKey = selectedCandidate.TargetKey;
            candidate = FindBattlefieldUnitCandidateByTargetKey(targetKey) ?? selectedCandidate;
            var selectedTargetKey = targetKey;
            placement = _battlefieldPlacedUnits.FirstOrDefault(unit => unit.TargetKey.Equals(selectedTargetKey, StringComparison.OrdinalIgnoreCase));
            return true;
        }

        var autoCandidate = FindBestBattlefieldDeploymentCandidateForDrop(item, gridX, gridY, faction);
        if (autoCandidate == null) return false;

        targetKey = autoCandidate.TargetKey;
        candidate = autoCandidate;
        var autoTargetKey = targetKey;
        placement = _battlefieldPlacedUnits.FirstOrDefault(unit => unit.TargetKey.Equals(autoTargetKey, StringComparison.OrdinalIgnoreCase));
        return true;
    }

    private bool CanUseBattlefieldSelectedCandidateForDrop(
        BattlefieldUnitCandidate selectedCandidate,
        BattlefieldUnitPaletteItem item,
        string faction,
        BattlefieldPlacedUnit? existingAtDropGrid)
    {
        if (!IsBattlefieldScriptPlacementTargetKeyWritable(selectedCandidate.TargetKey)) return false;

        var slot = FindBattlefieldDeploymentSlotInfo(selectedCandidate.TargetKey);
        if (slot == null) return false;
        if (!slot.Category.Equals(GetBattlefieldDeploymentCategoryForFaction(faction), StringComparison.Ordinal)) return false;

        var occupiedPlacement = _battlefieldPlacedUnits.FirstOrDefault(unit =>
            unit.TargetKey.Equals(selectedCandidate.TargetKey, StringComparison.OrdinalIgnoreCase));
        if (occupiedPlacement != null &&
            !ReferenceEquals(occupiedPlacement, existingAtDropGrid) &&
            occupiedPlacement.PersonId != item.PersonId)
        {
            return false;
        }

        return slot.IsAllySlot ||
               slot.IsBlank ||
               slot.PersonOrOrder == item.PersonId;
    }

    private BattlefieldUnitCandidate? FindBattlefieldUnitCandidateByTargetKey(string targetKey)
    {
        if (_currentBattlefieldDocument == null || string.IsNullOrWhiteSpace(targetKey)) return null;
        return _currentBattlefieldDocument.UnitCandidates.FirstOrDefault(candidate => candidate.TargetKey.Equals(targetKey, StringComparison.OrdinalIgnoreCase))
               ?? BuildBattlefieldDeploymentCandidateFromSlot(targetKey);
    }

    private BattlefieldDeploymentSlotInfo? FindBattlefieldDeploymentSlotInfo(string targetKey)
    {
        if (_currentBattlefieldDocument == null || string.IsNullOrWhiteSpace(targetKey)) return null;
        return BattlefieldEditorService.BuildDeploymentSlotInfos(_currentBattlefieldDocument)
            .FirstOrDefault(slot => slot.TargetKey.Equals(targetKey, StringComparison.OrdinalIgnoreCase));
    }

    private BattlefieldUnitCandidate? FindBestBattlefieldDeploymentCandidateForDrop(
        BattlefieldUnitPaletteItem item,
        int gridX,
        int gridY,
        string faction)
    {
        if (_currentBattlefieldDocument == null) return null;

        var occupiedByTarget = _battlefieldPlacedUnits
            .Where(unit => !string.IsNullOrWhiteSpace(unit.TargetKey))
            .GroupBy(unit => unit.TargetKey, StringComparer.OrdinalIgnoreCase)
            .ToDictionaryFirstByKey(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
        var occupiedGrids = _battlefieldPlacedUnits
            .Select(unit => BuildBattlefieldGridKey(unit.GridX, unit.GridY))
            .ToHashSet(StringComparer.Ordinal);
        var dropGridKey = BuildBattlefieldGridKey(gridX, gridY);
        var desiredCategory = GetBattlefieldDeploymentCategoryForFaction(faction);

        var slots = BattlefieldEditorService.BuildDeploymentSlotInfos(_currentBattlefieldDocument)
            .Where(slot => slot.Category.Equals(desiredCategory, StringComparison.Ordinal))
            .Where(slot => IsBattlefieldScriptPlacementTargetKeyWritable(slot.TargetKey))
            .ToList();
        if (slots.Count == 0) return null;

        bool TargetAvailableForAuto(BattlefieldDeploymentSlotInfo slot)
            => !occupiedByTarget.TryGetValue(slot.TargetKey, out var occupiedPlacement) ||
               occupiedPlacement.PersonId == item.PersonId;

        var existingPerson = slots
            .Where(slot => !slot.IsAllySlot && !slot.IsBlank && slot.PersonOrOrder == item.PersonId)
            .Where(TargetAvailableForAuto)
            .OrderBy(slot => CoordinateDistance(slot.GridX, slot.GridY, gridX, gridY))
            .FirstOrDefault();
        if (existingPerson != null) return FindBattlefieldUnitCandidateByTargetKey(existingPerson.TargetKey);

        var sameGridSlot = slots
            .Where(slot => slot.IsAllySlot && slot.GridX == gridX && slot.GridY == gridY)
            .Where(TargetAvailableForAuto)
            .OrderBy(slot => Math.Max(0, slot.PersonOrOrder))
            .FirstOrDefault();
        if (sameGridSlot != null) return FindBattlefieldUnitCandidateByTargetKey(sameGridSlot.TargetKey);

        var emptySlot = slots
            .Where(slot => !slot.IsAllySlot && slot.IsBlank)
            .Where(TargetAvailableForAuto)
            .OrderBy(slot => slot.RecordIndex)
            .FirstOrDefault();
        if (emptySlot != null) return FindBattlefieldUnitCandidateByTargetKey(emptySlot.TargetKey);

        var freeAllySlot = slots
            .Where(slot => slot.IsAllySlot)
            .Where(TargetAvailableForAuto)
            .Where(slot => !occupiedGrids.Contains(BuildBattlefieldGridKey(slot.GridX, slot.GridY)) || BuildBattlefieldGridKey(slot.GridX, slot.GridY).Equals(dropGridKey, StringComparison.Ordinal))
            .OrderBy(slot => CoordinateDistance(slot.GridX, slot.GridY, gridX, gridY))
            .ThenBy(slot => Math.Max(0, slot.PersonOrOrder))
            .FirstOrDefault();
        if (freeAllySlot != null) return FindBattlefieldUnitCandidateByTargetKey(freeAllySlot.TargetKey);

        return null;
    }

    private BattlefieldUnitCandidate? BuildBattlefieldDeploymentCandidateFromSlot(string targetKey)
    {
        if (_currentBattlefieldDocument == null) return null;

        var slot = BattlefieldEditorService.BuildDeploymentSlotInfos(_currentBattlefieldDocument)
            .FirstOrDefault(item => item.TargetKey.Equals(targetKey, StringComparison.OrdinalIgnoreCase));
        if (slot == null) return null;

        var command = _currentBattlefieldDocument.CommandCandidates.FirstOrDefault(candidate =>
            TryParseBattlefieldTargetKey(slot.TargetKey, out var scene, out var section, out var commandIndex, out var offsetHex, out var commandIdHex, out _) &&
            candidate.SceneIndex == scene &&
            candidate.SectionIndex == section &&
            candidate.CommandIndex == commandIndex &&
            (string.IsNullOrWhiteSpace(offsetHex) || HexDisplayFormatter.EqualsText(candidate.OffsetHex, offsetHex)) &&
            (string.IsNullOrWhiteSpace(commandIdHex) || candidate.CommandIdHex.Equals(commandIdHex, StringComparison.OrdinalIgnoreCase)));

        return new BattlefieldUnitCandidate
        {
            Index = _currentBattlefieldDocument.UnitCandidates.Count + slot.RecordIndex + 1,
            BattlefieldNumber = slot.BattlefieldNumber,
            PersonId = slot.PersonId,
            PersonRawCode = slot.PersonRawCode,
            SourceCommandDisplay = $"{BuildBattlefieldDeploymentSourceDisplay(command?.CommandName, slot.Category)} 第 {slot.RecordIndex + 1} 条",
            PersonDisplay = slot.IsAllySlot
                ? slot.PersonOrOrder.ToString(CultureInfo.InvariantCulture)
                : slot.IsBlank ? string.Empty : slot.PersonId.ToString(CultureInfo.InvariantCulture),
            CoordinateDisplay = $"({slot.GridX},{slot.GridY})",
            FactionDisplay = slot.Category.Replace("出场", string.Empty, StringComparison.Ordinal),
            AiDisplay = slot.AiMode,
            LevelJobDisplay = string.Join(' ', new[] { FormatBattlefieldLevelOffset(slot.LevelOffset), slot.JobLevel }.Where(part => !string.IsNullOrWhiteSpace(part))),
            DeploymentStatusDisplay = string.Join("/", new[] { slot.IsInitialDeployment ? "初始" : "剧情", slot.Reinforcement ? "援" : string.Empty, slot.Hidden ? "隐" : string.Empty, slot.IsBlank ? "空" : string.Empty }.Where(part => !string.IsNullOrWhiteSpace(part))),
            PersonRawCodeDisplay = slot.IsAllySlot ? $"顺序={slot.PersonRawCode}" : $"raw={slot.PersonRawCode}",
            DirectionDisplay = slot.Direction,
            HiddenDisplay = slot.Hidden ? "隐藏" : "正常",
            ReinforcementDisplay = slot.Reinforcement ? "援军" : string.Empty,
            Category = slot.Category,
            SourceCommand = $"{command?.CommandIdHex ?? HexDisplayFormatter.Format(slot.CommandId, 2)} {command?.CommandName ?? slot.Category} 第 {slot.RecordIndex + 1} 条",
            SceneSection = command == null
                ? $"Record {slot.RecordIndex}"
                : $"Scene {command.SceneIndex} / Section {command.SectionIndex} / Cmd {command.CommandIndex} / 第 {slot.RecordIndex + 1} 条",
            OffsetHex = command?.OffsetHex ?? string.Empty,
            PersonHint = slot.IsAllySlot
                ? $"我军出战顺序：{slot.PersonOrOrder}（地图标注显示为笀{Math.Max(0, slot.PersonOrOrder + 1)} 位）"
                : slot.IsBlank ? $"空出场槽：可由地图拖放写入人物；原始 Person2 码={slot.PersonRawCode}" : $"人物/部队：{slot.PersonId}；原始 Person2 码={slot.PersonRawCode}",
            CoordinateHint = $"坐标候选：({slot.GridX},{slot.GridY})",
            FactionHint = $"阵营候选：{slot.Category.Replace("出场", string.Empty, StringComparison.Ordinal)}",
            AiHint = slot.WritesAi ? "AI/方针槽可随拖放控制面板写回。" : "无直接 AI 方针槽。",
            LevelOrStateHint = slot.IsAllySlot
                ? $"4B 我军出战位：方向={slot.DirectionCode}({slot.Direction})，隐藏={(slot.Hidden ? 1 : 0)}；写回坐标/方向/隐藏标志，不改出战顺序。"
                : $"结构化部署状态：隐藏={(slot.Hidden ? 1 : 0)}，援军={(slot.Reinforcement ? 1 : 0)}，方向={slot.DirectionCode}({slot.Direction})，等级={slot.LevelOffset}，兵种级={slot.JobLevelCode}({slot.JobLevel})，AI={slot.AiPolicyCode}({slot.AiMode})。",
            Annotation = "由地图拖放自动绑定到 S 剧本出场设置槽；点击写回前仅作为预览覆盖。",
            TargetKey = slot.TargetKey
        };
    }

    private static string FormatBattlefieldLevelOffset(int value)
        => value >= 0
            ? "+" + value.ToString(CultureInfo.InvariantCulture) + "级"
            : value.ToString(CultureInfo.InvariantCulture) + "级";

    private static int CoordinateDistance(int x1, int y1, int x2, int y2)
        => Math.Abs(x1 - x2) + Math.Abs(y1 - y2);

    private static string BuildBattlefieldDeploymentSourceDisplay(string? commandName, string category)
    {
        var text = Regex.Replace(commandName ?? string.Empty, @"\b0x[0-9A-Fa-f]+\b", string.Empty, RegexOptions.CultureInvariant);
        text = Regex.Replace(text, @"^\s*[0-9A-Fa-f]{2}\s+", string.Empty, RegexOptions.CultureInvariant).Trim();
        return string.IsNullOrWhiteSpace(text) || text.Equals("Command", StringComparison.OrdinalIgnoreCase)
            ? category
            : text;
    }

    private static string GetBattlefieldDeploymentCategoryForFaction(string faction)
        => faction switch
        {
            "友军" => "友军出场",
            "敌军" => "敌军出场",
            _ => "我军出场"
        };

    private bool SyncBattlefieldInstructionPreviewAfterPlacement(BattlefieldPlacedUnit placed, string action)
    {
        if (_currentBattlefieldDocument == null) return false;
        if (!BattlefieldDeploymentWriteService.IsScriptPlacementWritable(placed))
        {
            return false;
        }

        var originalCandidate = FindBattlefieldUnitCandidateByTargetKey(placed.TargetKey);
        if (originalCandidate == null) return false;

        var previewUnit = BuildBattlefieldUnitCandidatePreview(originalCandidate, placed, action);
        _battlefieldUnitCandidatePreviewOverrides[placed.TargetKey] = previewUnit;

        if (TryParseBattlefieldTargetKey(placed.TargetKey, out var scene, out var section, out var command, out var offsetHex, out var commandIdHex, out _))
        {
            var commandKey = BuildBattlefieldCommandPreviewKey(scene, section, command, offsetHex, commandIdHex);
            var originalCommand = _currentBattlefieldDocument.CommandCandidates.FirstOrDefault(candidate =>
                candidate.SceneIndex == scene &&
                candidate.SectionIndex == section &&
                candidate.CommandIndex == command &&
                (string.IsNullOrWhiteSpace(offsetHex) || candidate.OffsetHex.Equals(offsetHex, StringComparison.OrdinalIgnoreCase)) &&
                (string.IsNullOrWhiteSpace(commandIdHex) || candidate.CommandIdHex.Equals(commandIdHex, StringComparison.OrdinalIgnoreCase)));
            if (originalCommand != null)
            {
                _battlefieldCommandCandidatePreviewOverrides[commandKey] = BuildBattlefieldCommandCandidatePreview(originalCommand, placed, action);
            }
        }

        _battlefieldScriptPreviewPlacementsByTargetKey[placed.TargetKey] = CloneBattlefieldPlacedUnit(placed);
        RefreshBattlefieldInstructionPreviewBindings(placed.TargetKey);
        return true;
    }

    private bool ApplyBattlefieldPlacementToCurrentScript(BattlefieldPlacedUnit placed, string action)
    {
        if (_currentBattlefieldDocument == null)
        {
            return false;
        }

        if (_currentBattlefieldLegacyScriptDocument == null)
        {
            ClearBattlefieldInstructionPreviewForTarget(placed.TargetKey);
            SetStatus($"战场布阵：{action}没有可写入的左侧 S 剧本完整树，仅保留为布阵草稿。");
            return false;
        }

        if (!BattlefieldDeploymentWriteService.IsScriptPlacementWritable(placed))
        {
            ClearBattlefieldInstructionPreviewForTarget(placed.TargetKey);
            return false;
        }

        var beforeEdit = CaptureLegacyScenarioHistorySnapshot(LegacyScriptEditorScope.Battlefield, _currentBattlefieldLegacyScriptDocument);
        BattlefieldDeploymentWriteResult result;
        try
        {
            result = _battlefieldDeploymentWriteService.ApplyScriptPlacements(
                _currentBattlefieldLegacyScriptDocument,
                new[] { placed });
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Apply battlefield placement to current S tree failed: {ex}");
            ClearBattlefieldInstructionPreviewForTarget(placed.TargetKey);
            SetStatus($"战场布阵：{action}未能写入左侧 S 剧本树：{ex.Message}；仅保留为布阵草稿。");
            return false;
        }

        if (result.Changes.Count == 0)
        {
            ClearBattlefieldInstructionPreviewForTarget(placed.TargetKey);
            SetStatus($"战场布阵：{action}没有实际 S 剧本字段变化。");
            return false;
        }

        PushLegacyScenarioUndoSnapshot(LegacyScriptEditorScope.Battlefield, beforeEdit);
        ClearBattlefieldInstructionPreviewForTarget(placed.TargetKey);
        var changed = FindBattlefieldDeploymentSourceCommand(placed.TargetKey);
        if (changed != null && !RefreshLegacyEditorCommandInPlace(LegacyScriptEditorScope.Battlefield, changed))
        {
            RefreshBattlefieldLegacyScriptView(changed);
        }
        RefreshBattlefieldInstructionPreviewBindings(placed.TargetKey);
        MarkLegacyScriptStructureDirty(LegacyScriptEditorScope.Battlefield);
        UpdateBattlefieldDeploymentWriteButton();

        var summary = result.Changes.FirstOrDefault()?.Summary;
        SetStatus(string.IsNullOrWhiteSpace(summary)
            ? $"战场布阵：{action}已写入左侧 S 剧本树，尚未完整保存。"
            : $"战场布阵：{action}已写入左侧 S 剧本树：{summary}，尚未完整保存。");
        return true;
    }

    private BattlefieldUnitCandidate BuildBattlefieldUnitCandidatePreview(BattlefieldUnitCandidate original, BattlefieldPlacedUnit placed, string action)
    {
        var reviewStatus = string.IsNullOrWhiteSpace(original.ReviewStatus) ? "已调整待写回" : original.ReviewStatus + " / 已调整待写回";
        var isAllySlot = IsBattlefieldAllyDeploymentTargetKey(original.TargetKey);
        var personPreviewText = isAllySlot
            ? $"预览角色：{placed.PersonId} {placed.Name}（仅用于地图标注＀B 写回不改出战顺序/人物槽）"
            : $"预览人物/部队：{placed.PersonId} {placed.Name}，Person2码={placed.PersonRawCode ?? BattlefieldEditorService.EncodePerson2ScriptCode(placed.PersonId)}";
        var aiPreviewText = isAllySlot
            ? $"4B 旀AI 写回；原候选：{original.AiHint}"
            : $"预览 AI：{placed.AiMode}；原候选：{original.AiHint}";
        var memoLine = isAllySlot
            ? $"地图{action}预览：角色={placed.PersonId} {placed.Name}，坐标=({placed.GridX},{placed.GridY})，阵营={placed.Faction}；4B 写回只改坐标/方向/隐藏，尚未写回 S 剧本。"
            : $"地图{action}预览：人物={placed.PersonId} {placed.Name}，坐标=({placed.GridX},{placed.GridY})，阵营={placed.Faction}，AI={placed.AiMode}；尚未写回 S 剧本。";
        return new BattlefieldUnitCandidate
        {
            Index = original.Index,
            BattlefieldNumber = original.BattlefieldNumber,
            SourceCommandDisplay = original.SourceCommandDisplay,
            PersonDisplay = $"{placed.PersonId} {placed.Name}".Trim(),
            CoordinateDisplay = $"({placed.GridX},{placed.GridY})",
            FactionDisplay = placed.Faction,
            AiDisplay = isAllySlot ? string.Empty : placed.AiMode,
            LevelJobDisplay = BuildBattlefieldPreviewLevelJobDisplay(placed),
            DeploymentStatusDisplay = string.Join("/", new[] { placed.Reinforcement ? "援" : string.Empty, placed.Hidden ? "隐" : string.Empty }.Where(part => !string.IsNullOrWhiteSpace(part))),
            PersonRawCodeDisplay = isAllySlot ? string.Empty : $"raw={placed.PersonRawCode ?? BattlefieldEditorService.EncodePerson2ScriptCode(placed.PersonId)}",
            DirectionDisplay = placed.Direction,
            HiddenDisplay = placed.Hidden ? "隐藏" : "正常",
            ReinforcementDisplay = placed.Reinforcement ? "援军" : string.Empty,
            Category = original.Category,
            SourceCommand = original.SourceCommand + " [地图预览已调整]",
            SceneSection = original.SceneSection,
            OffsetHex = original.OffsetHex,
            PersonHint = $"{personPreviewText}；原候选：{original.PersonHint}",
            CoordinateHint = $"预览坐标＀{placed.GridX},{placed.GridY})；原候选：{original.CoordinateHint}",
            FactionHint = $"预览阵营：{placed.Faction}；原候选：{original.FactionHint}",
            AiHint = aiPreviewText,
            LevelOrStateHint = original.LevelOrStateHint,
            Annotation = BattlefieldUnitReviewService.AppendReviewLine(
                original.Annotation,
                "地图拖放预览已调整；点击“写回出场到S剧本”前不会修改原文件。"),
            TargetKey = original.TargetKey,
            ReviewStatus = reviewStatus,
            ReviewNote = BattlefieldUnitReviewService.AppendReviewLine(original.ReviewNote, memoLine)
        };
    }

    private static string BuildBattlefieldPreviewLevelJobDisplay(BattlefieldPlacedUnit placed)
    {
        var levelName = placed.LevelOffset >= 0
            ? "+" + placed.LevelOffset.ToString(CultureInfo.InvariantCulture) + "级"
            : placed.LevelOffset.ToString(CultureInfo.InvariantCulture) + "级";
        return string.IsNullOrWhiteSpace(placed.JobName)
            ? levelName
            : $"{levelName} {placed.JobName}";
    }

    private BattlefieldCommandCandidate BuildBattlefieldCommandCandidatePreview(BattlefieldCommandCandidate original, BattlefieldPlacedUnit placed, string action)
        => new()
        {
            Index = original.Index,
            SceneIndex = original.SceneIndex,
            SectionIndex = original.SectionIndex,
            CommandIndex = original.CommandIndex,
            OffsetHex = original.OffsetHex,
            CommandIdHex = original.CommandIdHex,
            CommandName = original.CommandName + " [地图预览已调整]",
            RoleHint = original.RoleHint,
            ParameterPreview = IsBattlefieldAllyDeploymentTargetKey(placed.TargetKey)
                ? $"{original.ParameterPreview} | 预览{action}: 4B坐标=({placed.GridX},{placed.GridY}), 方向={placed.Direction}, 隐藏={placed.Hidden}, 不改出战顺序/人物槽"
                : $"{original.ParameterPreview} | 预览{action}: 人物={placed.PersonId}, 坐标=({placed.GridX},{placed.GridY}), AI={placed.AiMode}",
            RawContextWordsHex = original.RawContextWordsHex,
            LegacyParameterLayout = original.LegacyParameterLayout,
            CommandTemplateHint = original.CommandTemplateHint,
            ReferenceHint = original.ReferenceHint,
            Annotation = BattlefieldUnitReviewService.AppendReviewLine(
                original.Annotation,
                "地图拖放预览已调整；点击“写回出场到S剧本”前不会修改原文件。")
        };

    private static bool IsBattlefieldAllyDeploymentTargetKey(string targetKey)
        => TryParseBattlefieldTargetKey(targetKey, out _, out _, out _, out _, out var commandIdHex, out _) &&
           commandIdHex.Equals("0x4B", StringComparison.OrdinalIgnoreCase);

    private void RefreshBattlefieldInstructionPreviewBindings(string targetKey)
    {
        if (_currentBattlefieldDocument == null) return;

        var selectedTargetKey = GetSelectedBattlefieldUnitCandidate()?.TargetKey;
        BindBattlefieldUnitCandidates(GetBattlefieldUnitCandidatesForDisplay());
        if (!SelectBattlefieldUnitCandidateGridRow(targetKey, updatePreview: false) && !string.IsNullOrWhiteSpace(selectedTargetKey))
        {
            SelectBattlefieldUnitCandidateGridRow(selectedTargetKey, updatePreview: false);
        }

        BindBattlefieldCommandCandidates(GetBattlefieldCommandCandidatesForDisplay());
        RefreshBattlefieldScriptPreviewNode(targetKey);
    }

    private void RefreshBattlefieldScriptPreviewNode(string targetKey)
    {
        if (_currentBattlefieldScriptStructure == null || !TryParseBattlefieldTargetKey(targetKey, out var scene, out var section, out var command, out var offsetHex, out var commandIdHex, out _))
        {
            return;
        }

        var row = _currentBattlefieldScriptStructure.Rows.FirstOrDefault(item =>
            item.NodeType == "Command候选" &&
            item.SceneIndex == scene &&
            item.SectionIndex == section &&
            item.CommandIndex == command &&
            (string.IsNullOrWhiteSpace(offsetHex) || item.OffsetHex.Equals(offsetHex, StringComparison.OrdinalIgnoreCase)) &&
            (string.IsNullOrWhiteSpace(commandIdHex) || item.CommandIdHex.Equals(commandIdHex, StringComparison.OrdinalIgnoreCase)));
        if (row == null) return;

        var node = FindBattlefieldScriptTreeNode(row);
        if (node != null)
        {
            ApplyBattlefieldScriptPreviewToNode(node, row);
        }

        if (_selectedBattlefieldScriptCommandRow != null && IsSameScriptCommand(_selectedBattlefieldScriptCommandRow, row))
        {
            _battlefieldScriptDetailBox.Text = BuildBattlefieldScriptRowDetailWithPreview(row);
        }
    }

    private TreeNode? FindBattlefieldScriptTreeNode(ScenarioStructureRow row)
    {
        foreach (TreeNode root in _battlefieldScriptTree.Nodes)
        {
            var found = FindScriptTreeNode(root, row);
            if (found != null) return found;
        }

        return null;
    }

    private void ApplyBattlefieldScriptPreviewToNode(TreeNode node, ScenarioStructureRow row)
    {
        var preview = GetBattlefieldScriptPreviewForRow(row);
        var baseText = BuildBattlefieldScriptCommandNodeText(node, row);
        var baseToolTip = BuildBattlefieldScriptCommandTreeToolTip(row);
        node.Text = baseText;
        if (preview == null)
        {
            node.ToolTipText = baseToolTip;
            node.ForeColor = GetScriptCommandColor(row.CommandId);
            return;
        }

        node.ToolTipText = baseToolTip + "\r\n" + BuildBattlefieldScriptPreviewText(preview);
        node.ForeColor = Color.DarkOrange;
    }

    private string BuildBattlefieldScriptCommandNodeText(TreeNode node, ScenarioStructureRow row)
    {
        if (node.Tag is LegacyScenarioItemData { Command: { } command })
        {
            return BuildLegacyScriptCommandSummary(row, command, includeIdentity: false, maxVisibleValues: 6);
        }

        return BuildScriptCommandSummary(row, includeIdentity: true, maxVisibleValues: 6);
    }

    private void RefreshBattlefieldScriptPreviewTree()
    {
        foreach (TreeNode root in _battlefieldScriptTree.Nodes)
        {
            foreach (var node in EnumerateScriptTreeNodes(root))
            {
                if (TryGetBattlefieldScriptCommandRowFromNode(node, out var row))
                {
                    ApplyBattlefieldScriptPreviewToNode(node, row);
                }
            }
        }

        if (_selectedBattlefieldScriptCommandRow != null)
        {
            _battlefieldScriptDetailBox.Text = BuildBattlefieldScriptRowDetailWithPreview(_selectedBattlefieldScriptCommandRow);
        }
    }

    private static bool TryGetBattlefieldScriptCommandRowFromNode(TreeNode node, out ScenarioStructureRow row)
    {
        if (node.Tag is LegacyScenarioItemData { UiRow: ScenarioStructureRow itemRow } && itemRow.NodeType == "Command候选")
        {
            row = itemRow;
            return true;
        }

        if (node.Tag is ScenarioStructureRow directRow && directRow.NodeType == "Command候选")
        {
            row = directRow;
            return true;
        }

        row = null!;
        return false;
    }

    private BattlefieldPlacedUnit? GetBattlefieldScriptPreviewForRow(ScenarioStructureRow row)
    {
        if (_battlefieldScriptPreviewPlacementsByTargetKey.Count == 0) return null;
        return _battlefieldScriptPreviewPlacementsByTargetKey.Values.FirstOrDefault(placement =>
            TryParseBattlefieldTargetKey(placement.TargetKey, out var scene, out var section, out var command, out var offsetHex, out var commandIdHex, out _) &&
            row.SceneIndex == scene &&
            row.SectionIndex == section &&
            row.CommandIndex == command &&
            (string.IsNullOrWhiteSpace(offsetHex) || row.OffsetHex.Equals(offsetHex, StringComparison.OrdinalIgnoreCase)) &&
            (string.IsNullOrWhiteSpace(commandIdHex) || row.CommandIdHex.Equals(commandIdHex, StringComparison.OrdinalIgnoreCase)));
    }

    private static string BuildBattlefieldScriptPreviewText(BattlefieldPlacedUnit preview)
        => IsBattlefieldAllyDeploymentTargetKey(preview.TargetKey)
            ? $"地图预览：4B 我军出战位坐标=({preview.GridX},{preview.GridY})，方向={preview.Direction}，隐藏={preview.Hidden}；人物={preview.PersonId} {preview.Name} 仅用于地图标注，写回不改出战顺序/人物槽，尚未写回 S 剧本。"
            : $"地图预览：人物={preview.PersonId} {preview.Name}，Person2={preview.PersonRawCode ?? BattlefieldEditorService.EncodePerson2ScriptCode(preview.PersonId)}，坐标=({preview.GridX},{preview.GridY})，阵营={preview.Faction}，AI={preview.AiMode}，隐藏={preview.Hidden}，援军={preview.Reinforcement}；尚未写回 S 剧本。";

    private void ApplyBattlefieldControlPanelToSelectedUnit()
    {
        MarkBattlefieldConsolePlacementDirty();
    }
    private void MarkBattlefieldConsolePlacementDirty(BattlefieldBatchEditField? batchField = null)
    {
        if (_bindingBattlefieldControlPanel || _battlefieldBatchSelecting || _bindingBattlefieldBatchControlPanel || _battlefieldConsoleCommitInProgress) return;
        if (IsBattlefieldBatchEditingActive)
        {
            ApplyBattlefieldBatchPlacementField(batchField ?? ResolveBattlefieldPlacementFieldFromFocusedControl());
            return;
        }
        if (_selectedBattlefieldPlacedUnit == null) return;
        MarkBattlefieldConsoleDirty(BattlefieldConsoleDirtyKind.Placement);
        UpdateSelectedBattlefieldPlacedUnitFromConsoleControls();
        InvalidateBattlefieldMapDynamicRegion(
            GetBattlefieldMapGridClientRectangle(
                new Point(_selectedBattlefieldPlacedUnit.GridX, _selectedBattlefieldPlacedUnit.GridY)));
        _saveBattlefieldUnitReviewsButton.Enabled = true;
        UpdateBattlefieldDeploymentWriteButton();
    }

    private void MarkBattlefieldConsoleStatusDirty(BattlefieldConsoleDirtyKind kind, BattlefieldBatchEditField? batchField = null)
    {
        if (_bindingBattlefieldControlPanel || _battlefieldBatchSelecting || _bindingBattlefieldBatchControlPanel || _battlefieldConsoleCommitInProgress) return;
        if (IsBattlefieldBatchEditingActive)
        {
            ApplyBattlefieldBatchStatusField(batchField ?? ResolveBattlefieldStatusFieldFromFocusedControl(kind));
            return;
        }
        if (_selectedBattlefieldPlacedUnit == null) return;
        MarkBattlefieldConsoleDirty(kind);
    }

    private void MarkBattlefieldConsoleDirty(BattlefieldConsoleDirtyKind kind)
    {
        if (_selectedBattlefieldPlacedUnit == null) return;
        if (!_battlefieldConsoleDirty)
        {
            _battlefieldConsoleBeforeEditSnapshot = CloneBattlefieldPlacedUnit(_selectedBattlefieldPlacedUnit);
        }
        _battlefieldConsoleDirty = true;
        _battlefieldConsoleDirtyTargetKey = _selectedBattlefieldPlacedUnit.TargetKey;
        _battlefieldConsoleDirtyKind |= kind;
    }

    private void ClearBattlefieldConsoleDirty(bool clearBeforeEditSnapshot = true)
    {
        _battlefieldConsoleDirty = false;
        _battlefieldConsoleDirtyTargetKey = string.Empty;
        _battlefieldConsoleDirtyKind = BattlefieldConsoleDirtyKind.None;
        if (clearBeforeEditSnapshot)
        {
            _battlefieldConsoleBeforeEditSnapshot = null;
        }
    }

    private void UpdateSelectedBattlefieldPlacedUnitFromConsoleControls()
    {
        if (_selectedBattlefieldPlacedUnit == null) return;
        _selectedBattlefieldPlacedUnit.LevelOffset = (int)_battlefieldLevelOffsetInput.Value;
        _selectedBattlefieldPlacedUnit.LevelMode = _battlefieldLevelModeCombo.SelectedItem?.ToString() ?? _selectedBattlefieldPlacedUnit.LevelMode;
        _selectedBattlefieldPlacedUnit.AiMode = _battlefieldAiModeCombo.SelectedItem?.ToString() ?? _selectedBattlefieldPlacedUnit.AiMode;
        _selectedBattlefieldPlacedUnit.Hidden = _battlefieldHiddenCheckBox.Checked;
        _selectedBattlefieldPlacedUnit.Direction = _battlefieldDirectionCombo.SelectedItem?.ToString() ?? _selectedBattlefieldPlacedUnit.Direction;
    }

    private void RegisterBattlefieldConsoleDeferredCommitHandlers()
    {
        foreach (var control in EnumerateBattlefieldConsoleEditControls())
        {
            control.Leave += (_, _) => QueueBattlefieldConsoleCommitWhenFocusLeaves();
        }
    }

    private void DrawBattlefieldBatchSelectionOverlay(Graphics graphics, Size imageSize, int gridWidth, int gridHeight)
    {
        if (!_battlefieldBatchSelecting ||
            !_battlefieldBatchSelectionStartGrid.HasValue ||
            !_battlefieldBatchSelectionEndGrid.HasValue ||
            gridWidth <= 0 ||
            gridHeight <= 0)
        {
            return;
        }

        var start = _battlefieldBatchSelectionStartGrid.Value;
        var end = _battlefieldBatchSelectionEndGrid.Value;
        var minX = Math.Clamp(Math.Min(start.X, end.X), 0, gridWidth - 1);
        var maxX = Math.Clamp(Math.Max(start.X, end.X), 0, gridWidth - 1);
        var minY = Math.Clamp(Math.Min(start.Y, end.Y), 0, gridHeight - 1);
        var maxY = Math.Clamp(Math.Max(start.Y, end.Y), 0, gridHeight - 1);
        var cellWidth = imageSize.Width / (float)gridWidth;
        var cellHeight = imageSize.Height / (float)gridHeight;
        var rect = new RectangleF(
            minX * cellWidth,
            minY * cellHeight,
            (maxX - minX + 1) * cellWidth,
            (maxY - minY + 1) * cellHeight);

        using var fill = new SolidBrush(Color.FromArgb(55, Color.Cyan));
        using var border = new Pen(Color.FromArgb(230, Color.Cyan), 3);
        using var inner = new Pen(Color.FromArgb(210, Color.Black), 1);
        graphics.FillRectangle(fill, rect);
        graphics.DrawRectangle(border, rect.X, rect.Y, Math.Max(1, rect.Width - 1), Math.Max(1, rect.Height - 1));
        graphics.DrawRectangle(inner, rect.X + 2, rect.Y + 2, Math.Max(1, rect.Width - 5), Math.Max(1, rect.Height - 5));
    }

    private IEnumerable<Control> EnumerateBattlefieldConsoleEditControls()
    {
        yield return _battlefieldFactionAllyRadio;
        yield return _battlefieldFactionFriendRadio;
        yield return _battlefieldFactionEnemyRadio;
        yield return _battlefieldHiddenCheckBox;
        yield return _battlefieldLevelOffsetInput;
        yield return _battlefieldLevelModeCombo;
        yield return _battlefieldAiModeCombo;
        yield return _battlefieldDirectionCombo;
        yield return _battlefieldConsoleWeaponCombo;
        yield return _battlefieldConsoleWeaponLevelInput;
        yield return _battlefieldConsoleArmorCombo;
        yield return _battlefieldConsoleArmorLevelInput;
        yield return _battlefieldConsoleAssistCombo;
        yield return _battlefieldConsoleJobCombo;
        yield return _battlefieldConsoleAbilityGrid;
    }

    private bool BattlefieldConsoleContainsFocus()
        => EnumerateBattlefieldConsoleEditControls().Any(control => control.ContainsFocus);

    private void QueueBattlefieldConsoleCommitWhenFocusLeaves()
    {
        if ((!_battlefieldConsoleDirty && _battlefieldBatchTransactionBeforeEdit == null) || IsDisposed) return;
        BeginInvoke(new Action(() =>
        {
            if (!BattlefieldConsoleContainsFocus())
            {
                TryCommitPendingBattlefieldConsoleChanges(finalizeBatchTransaction: true);
            }
        }));
    }

    private void HandleBattlefieldFactionChanged()
    {
        if (_bindingBattlefieldControlPanel || _bindingBattlefieldBatchControlPanel) return;
        if (IsBattlefieldBatchEditingActive)
        {
            ApplyBattlefieldBatchPlacementField(BattlefieldBatchEditField.Faction);
            return;
        }
        if (_selectedBattlefieldPlacedUnit != null)
        {
            ApplyBattlefieldFactionChangeToSelectedUnit(GetSelectedBattlefieldFaction());
            return;
        }

        RefreshBattlefieldPaletteUnitPreview(_battlefieldUnitListBox.SelectedItem as BattlefieldUnitPaletteItem);
    }

    private BattlefieldBatchEditField ResolveBattlefieldPlacementFieldFromFocusedControl()
    {
        if (_battlefieldHiddenCheckBox.ContainsFocus) return BattlefieldBatchEditField.Hidden;
        if (_battlefieldLevelOffsetInput.ContainsFocus) return BattlefieldBatchEditField.LevelOffset;
        if (_battlefieldLevelModeCombo.ContainsFocus) return BattlefieldBatchEditField.LevelMode;
        if (_battlefieldAiModeCombo.ContainsFocus) return BattlefieldBatchEditField.AiMode;
        if (_battlefieldDirectionCombo.ContainsFocus) return BattlefieldBatchEditField.Direction;
        if (_battlefieldFactionAllyRadio.ContainsFocus || _battlefieldFactionFriendRadio.ContainsFocus || _battlefieldFactionEnemyRadio.ContainsFocus)
        {
            return BattlefieldBatchEditField.Faction;
        }

        return BattlefieldBatchEditField.Hidden;
    }

    private BattlefieldBatchEditField ResolveBattlefieldStatusFieldFromFocusedControl(BattlefieldConsoleDirtyKind kind)
    {
        if (_battlefieldConsoleWeaponCombo.ContainsFocus) return BattlefieldBatchEditField.Weapon;
        if (_battlefieldConsoleWeaponLevelInput.ContainsFocus) return BattlefieldBatchEditField.WeaponLevel;
        if (_battlefieldConsoleArmorCombo.ContainsFocus) return BattlefieldBatchEditField.Armor;
        if (_battlefieldConsoleArmorLevelInput.ContainsFocus) return BattlefieldBatchEditField.ArmorLevel;
        if (_battlefieldConsoleAssistCombo.ContainsFocus) return BattlefieldBatchEditField.Assist;
        if (_battlefieldConsoleJobCombo.ContainsFocus) return BattlefieldBatchEditField.Job;
        if (_battlefieldConsoleAbilityGrid.ContainsFocus) return BattlefieldBatchEditField.Ability;
        return (kind & BattlefieldConsoleDirtyKind.Equipment) != 0
            ? BattlefieldBatchEditField.Weapon
            : BattlefieldBatchEditField.Ability;
    }

    private void ApplyBattlefieldBatchPlacementField(BattlefieldBatchEditField field)
    {
        var units = GetBattlefieldBatchEditingUnits().ToList();
        if (units.Count == 0)
        {
            ClearBattlefieldBatchEditingState(syncControls: true);
            return;
        }

        EnsureBattlefieldBatchTransaction(units);

        var changedUnits = new List<BattlefieldPlacedUnit>();
        var oldFactionSnapshots = new List<BattlefieldPlacedUnit>();
        var failures = new List<string>();

        if (field == BattlefieldBatchEditField.Faction)
        {
            var requestedFaction = GetSelectedBattlefieldFaction();
            ApplyBattlefieldBatchFactionChange(units, requestedFaction, failures, changedUnits, oldFactionSnapshots);
        }
        else
        {
            foreach (var unit in units)
            {
                if (ApplyBattlefieldBatchPlacementValue(unit, field))
                {
                    changedUnits.Add(unit);
                }
            }
        }

        var wrotePlacement = false;
        var wroteStatus = false;
        if (changedUnits.Count > 0)
        {
            if (field is BattlefieldBatchEditField.LevelOffset or BattlefieldBatchEditField.LevelMode)
            {
                wroteStatus = ApplyBattlefieldBatchDeploymentStatusFields(changedUnits, field, failures);
            }
            else
            {
                wrotePlacement = ApplyBattlefieldBatchScriptPlacements(changedUnits, $"批量{FormatBattlefieldBatchFieldName(field)}", failures, oldFactionSnapshots);
            }
        }

        RefreshBattlefieldBatchAfterApply(units, changedUnits.Count, wrotePlacement || wroteStatus, failures, $"批量{FormatBattlefieldBatchFieldName(field)}");
    }

    private bool ApplyBattlefieldBatchPlacementValue(BattlefieldPlacedUnit unit, BattlefieldBatchEditField field)
    {
        switch (field)
        {
            case BattlefieldBatchEditField.Hidden:
                if (_battlefieldHiddenCheckBox.CheckState == CheckState.Indeterminate) return false;
                var hidden = _battlefieldHiddenCheckBox.Checked;
                if (unit.Hidden == hidden) return false;
                unit.Hidden = hidden;
                unit.PlacementNote = BattlefieldUnitReviewService.AppendReviewLine(unit.PlacementNote, $"批量编辑：隐藏={hidden}。");
                return true;
            case BattlefieldBatchEditField.LevelOffset:
                var levelOffset = (int)_battlefieldLevelOffsetInput.Value;
                if (unit.LevelOffset == levelOffset) return false;
                unit.LevelOffset = levelOffset;
                unit.PlacementNote = BattlefieldUnitReviewService.AppendReviewLine(unit.PlacementNote, $"批量编辑：等级修正 {levelOffset}。");
                return true;
            case BattlefieldBatchEditField.LevelMode:
                var levelMode = _battlefieldLevelModeCombo.SelectedItem?.ToString();
                if (string.IsNullOrWhiteSpace(levelMode) || levelMode == "多值" || unit.LevelMode == levelMode) return false;
                unit.LevelMode = levelMode;
                unit.PlacementNote = BattlefieldUnitReviewService.AppendReviewLine(unit.PlacementNote, $"批量编辑：等级阶段 {levelMode}。");
                return true;
            case BattlefieldBatchEditField.AiMode:
                var aiMode = _battlefieldAiModeCombo.SelectedItem?.ToString();
                if (string.IsNullOrWhiteSpace(aiMode) || aiMode == "多值" || unit.AiMode == aiMode) return false;
                unit.AiMode = aiMode;
                unit.PlacementNote = BattlefieldUnitReviewService.AppendReviewLine(unit.PlacementNote, $"批量编辑：AI={aiMode}。");
                return true;
            case BattlefieldBatchEditField.Direction:
                var direction = _battlefieldDirectionCombo.SelectedItem?.ToString();
                if (string.IsNullOrWhiteSpace(direction) || direction == "多值" || unit.Direction == direction) return false;
                unit.Direction = direction;
                unit.PlacementNote = BattlefieldUnitReviewService.AppendReviewLine(unit.PlacementNote, $"批量编辑：方向 {direction}。");
                return true;
            default:
                return false;
        }
    }

    private void ApplyBattlefieldBatchFactionChange(
        IReadOnlyList<BattlefieldPlacedUnit> units,
        string requestedFaction,
        List<string> failures,
        List<BattlefieldPlacedUnit> changedUnits,
        List<BattlefieldPlacedUnit> oldSnapshots)
    {
        if (_currentBattlefieldDocument == null || _currentBattlefieldLegacyScriptDocument == null)
        {
            failures.Add("当前没有可写入的 S 剧本树，不能批量迁移阵营槽。");
            return;
        }

        foreach (var unit in units)
        {
            if (unit.Faction.Equals(requestedFaction, StringComparison.Ordinal))
            {
                continue;
            }

            var oldFaction = unit.Faction;
            var oldTargetKey = unit.TargetKey;
            var oldSnapshot = CloneBattlefieldPlacedUnit(unit);
            var candidate = FindBattlefieldMigrationCandidate(unit, requestedFaction);
            if (candidate == null)
            {
                failures.Add($"{unit.Name}({unit.GridX},{unit.GridY})：没有可绑定的 {requestedFaction} S 剧本出场槽。");
                continue;
            }

            var targetKey = candidate.TargetKey;
            var replaced = _battlefieldPlacedUnits.FirstOrDefault(item =>
                !ReferenceEquals(item, unit) &&
                item.TargetKey.Equals(targetKey, StringComparison.OrdinalIgnoreCase));
            if (replaced != null)
            {
                failures.Add($"{unit.Name}({unit.GridX},{unit.GridY})：目标槽已被 {replaced.Name} 占用。");
                continue;
            }

        unit.TargetKey = targetKey;
        unit.Faction = InferBattlefieldFaction(candidate);
        unit.PersonRawCode = unit.Faction.Equals("我军", StringComparison.Ordinal)
            ? null
            : BattlefieldEditorService.EncodePerson2ScriptCode(unit.PersonId);
        unit.Source = "S剧本出场设置(批量阵营迁移)";
            unit.PlacementNote = BattlefieldUnitReviewService.AppendReviewLine(
                unit.PlacementNote,
                $"批量阵营迁移：{oldFaction} -> {unit.Faction}，绑定 {targetKey}。");
            changedUnits.Add(unit);
            if (BattlefieldDeploymentWriteService.IsFriendOrEnemyScriptPlacementWritable(oldSnapshot))
            {
                oldSnapshots.Add(oldSnapshot);
            }
            _batchEditingBattlefieldTargetKeys.Remove(oldTargetKey);
            _batchEditingBattlefieldTargetKeys.Add(targetKey);
        }
    }

    private void ApplyBattlefieldFactionChangeToSelectedUnit(string requestedFaction)
    {
        var unit = _selectedBattlefieldPlacedUnit;
        if (unit == null) return;
        if (unit.Faction.Equals(requestedFaction, StringComparison.Ordinal))
        {
            ApplyBattlefieldControlPanelToSelectedUnit();
            return;
        }

        var oldFaction = unit.Faction;
        var oldTargetKey = unit.TargetKey;
        if (_currentBattlefieldDocument == null ||
            _currentBattlefieldLegacyScriptDocument == null)
        {
            RestoreBattlefieldFactionSelection(oldFaction);
            SetStatus("战场控制台：当前没有可写入的 S 剧本树，不能迁移阵营槽。");
            return;
        }

        var candidate = FindBattlefieldMigrationCandidate(unit, requestedFaction);
        if (candidate == null)
        {
            RestoreBattlefieldFactionSelection(oldFaction);
            SetStatus($"战场控制台：没有可绑定的 {requestedFaction} S 剧本出场槽，阵营未改变。");
            return;
        }

        var targetKey = candidate.TargetKey;
        var oldSnapshot = CloneBattlefieldPlacedUnit(unit);
        var replaced = _battlefieldPlacedUnits
            .FirstOrDefault(item => !ReferenceEquals(item, unit) &&
                                    item.TargetKey.Equals(targetKey, StringComparison.OrdinalIgnoreCase));
        if (replaced != null)
        {
            _battlefieldPlacedUnits.Remove(replaced);
        }

        unit.TargetKey = targetKey;
        unit.Faction = InferBattlefieldFaction(candidate);
        unit.PersonRawCode = unit.Faction.Equals("我军", StringComparison.Ordinal)
            ? null
            : BattlefieldEditorService.EncodePerson2ScriptCode(unit.PersonId);
        unit.Source = "S剧本出场设置(控制台阵营迁秀";
        unit.PlacementNote = BattlefieldUnitReviewService.AppendReviewLine(
            unit.PlacementNote,
            $"控制台阵营迁移：{oldFaction} -> {unit.Faction}，绑定 {targetKey}。");
        unit.LevelOffset = (int)_battlefieldLevelOffsetInput.Value;
        unit.LevelMode = _battlefieldLevelModeCombo.SelectedItem?.ToString() ?? unit.LevelMode;
        unit.AiMode = _battlefieldAiModeCombo.SelectedItem?.ToString() ?? unit.AiMode;
        unit.Hidden = _battlefieldHiddenCheckBox.Checked;
        unit.Direction = _battlefieldDirectionCombo.SelectedItem?.ToString() ?? unit.Direction;

        var beforeEdit = CaptureLegacyScenarioHistorySnapshot(LegacyScriptEditorScope.Battlefield, _currentBattlefieldLegacyScriptDocument);
        try
        {
            _battlefieldDeploymentWriteService.ApplyScriptPlacements(
                _currentBattlefieldLegacyScriptDocument,
                new[] { unit });

            if (BattlefieldDeploymentWriteService.IsFriendOrEnemyScriptPlacementWritable(oldSnapshot))
            {
                _battlefieldDeploymentWriteService.ClearFriendEnemyScriptPlacements(
                    _currentBattlefieldLegacyScriptDocument,
                    new[] { oldSnapshot });
            }

            PushLegacyScenarioUndoSnapshot(LegacyScriptEditorScope.Battlefield, beforeEdit);
            ClearBattlefieldInstructionPreviewForTarget(oldTargetKey);
            ClearBattlefieldInstructionPreviewForTarget(targetKey);
            var changed = FindBattlefieldDeploymentSourceCommand(targetKey)
                          ?? FindBattlefieldDeploymentSourceCommand(oldTargetKey);
            RefreshBattlefieldLegacyScriptView(changed);
            RefreshBattlefieldDocumentFromLegacyScript(targetKey);
            MarkLegacyScriptStructureDirty(LegacyScriptEditorScope.Battlefield);
            _saveBattlefieldUnitReviewsButton.Enabled = true;
            UpdateBattlefieldDeploymentWriteButton();
            SelectBattlefieldPlacedUnitByTargetKey(targetKey, enterEdit: true);
            SetStatus($"战场控制台：{unit.Name} 已迁移到 {unit.Faction} 出场槽，左侧 S 剧本树已更新，尚未完整保存。");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine("Battlefield faction migration failed: " + ex);
            RestoreLegacyScenarioSnapshot(LegacyScriptEditorScope.Battlefield, beforeEdit, "阵营迁移失败，已恢复操作前 S 剧本树。");
            unit.TargetKey = oldSnapshot.TargetKey;
            unit.Faction = oldSnapshot.Faction;
            unit.Source = oldSnapshot.Source;
            unit.PlacementNote = oldSnapshot.PlacementNote;
            if (replaced != null && !_battlefieldPlacedUnits.Contains(replaced))
            {
                _battlefieldPlacedUnits.Add(replaced);
            }

            RestoreBattlefieldFactionSelection(oldFaction);
            RefreshBattlefieldMapDynamicPreview();
            SetStatus("战场控制台：阵营迁移失败：" + ex.Message);
        }
    }

    private BattlefieldUnitCandidate? FindBattlefieldMigrationCandidate(BattlefieldPlacedUnit unit, string requestedFaction)
    {
        var item = new BattlefieldUnitPaletteItem
        {
            PersonId = unit.PersonId,
            Name = unit.Name,
            JobId = unit.JobId,
            JobName = unit.JobName,
            RImageId = unit.RImageId,
            SImageId = unit.SImageId
        };

        return FindBestBattlefieldDeploymentCandidateForDrop(item, unit.GridX, unit.GridY, requestedFaction);
    }

    private void RestoreBattlefieldFactionSelection(string faction)
    {
        _bindingBattlefieldControlPanel = true;
        try
        {
            _battlefieldFactionAllyRadio.Checked = faction == "我军";
            _battlefieldFactionFriendRadio.Checked = faction == "友军";
            _battlefieldFactionEnemyRadio.Checked = faction == "敌军";
        }
        finally
        {
            _bindingBattlefieldControlPanel = false;
        }

        RefreshBattlefieldPaletteUnitPreview(_battlefieldUnitListBox.SelectedItem as BattlefieldUnitPaletteItem);
    }

    private bool ApplyBattlefieldDeploymentStatusFieldsToCurrentScript(BattlefieldPlacedUnit unit, string action)
    {
        if (_project == null ||
            _currentBattlefieldLegacyScriptDocument == null ||
            !BattlefieldUnitStatusWriteService.IsWritableStatusTarget(unit))
        {
            return false;
        }

        try
        {
            var draft = _battlefieldUnitStatusWriteService.LoadDraft(
                _project,
                _tables,
                _currentBattlefieldLegacyScriptDocument,
                _currentBattlefieldDocument?.Scenario.FileName ?? string.Empty,
                unit);
            var originalLevelBonus = draft.LevelBonus;
            var originalJobLevel = draft.JobLevel;
            var originalAiPolicy = draft.AiPolicy;
            draft.LevelBonus = unit.LevelOffset;
            draft.JobLevel = MapBattlefieldJobLevel(unit.LevelMode);
            draft.AiPolicy = MapBattlefieldAiMode(unit.AiMode);
            draft.Weapon = null;
            draft.WeaponLevel = null;
            draft.Armor = null;
            draft.ArmorLevel = null;
            draft.Assist = null;
            draft.JobId = null;
            draft.RemoveEquipmentOverride = false;
            draft.RemoveJobOverride = false;
            draft.RemoveAbilityOverrides.Clear();
            foreach (var ability in draft.Abilities)
            {
                ability.Value = null;
                ability.Operation = null;
                ability.RemoveOverride = false;
            }

            if (draft.LevelBonus == originalLevelBonus &&
                draft.JobLevel == originalJobLevel &&
                draft.AiPolicy == originalAiPolicy)
            {
                return false;
            }

            var beforeEdit = CaptureLegacyScenarioHistorySnapshot(LegacyScriptEditorScope.Battlefield, _currentBattlefieldLegacyScriptDocument);
            var result = _battlefieldUnitStatusWriteService.Apply(_project, _currentBattlefieldLegacyScriptDocument, draft);
            PushLegacyScenarioUndoSnapshot(LegacyScriptEditorScope.Battlefield, beforeEdit);
            var changed = FindBattlefieldDeploymentSourceCommand(unit.TargetKey);
            if (changed != null && !RefreshLegacyEditorCommandInPlace(LegacyScriptEditorScope.Battlefield, changed))
            {
                RefreshBattlefieldLegacyScriptView(changed);
            }
            MarkLegacyScriptStructureDirty(LegacyScriptEditorScope.Battlefield);
            ReloadBattlefieldConsoleStatusAfterScriptChange(unit.TargetKey);
            SetStatus($"战场控制台：{action}已写入 46/47 出场字段，尚未完整保存。");
            return true;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine("Apply battlefield deployment status fields failed: " + ex);
            SetStatus($"战场控制台：{action}未能写入 46/47 出场字段：" + ex.Message);
            return false;
        }
    }

    private bool ApplyBattlefieldBatchScriptPlacements(
        IReadOnlyList<BattlefieldPlacedUnit> units,
        string action,
        List<string> failures,
        IReadOnlyList<BattlefieldPlacedUnit>? oldFactionSnapshots = null)
    {
        if (_currentBattlefieldLegacyScriptDocument == null)
        {
            failures.Add("当前没有可写入的 S 剧本完整树，布阵字段仅保留在内存草稿。");
            return false;
        }

        var writable = units.Where(BattlefieldDeploymentWriteService.IsScriptPlacementWritable).ToList();
        failures.AddRange(units
            .Where(unit => !BattlefieldDeploymentWriteService.IsScriptPlacementWritable(unit))
            .Select(unit => $"{unit.Name}({unit.GridX},{unit.GridY})：未绑定可写 46/47/4B 出场记录，布阵字段只保留在内存草稿。"));
        if (writable.Count == 0)
        {
            failures.Add("框选单位没有可写回的 46/47/4B 出场记录。");
            return false;
        }

        EnsureBattlefieldBatchTransaction(units);
        foreach (var unit in writable)
        {
            _battlefieldBatchTransactionTargetKeys.Add(unit.TargetKey);
        }
        if (oldFactionSnapshots is { Count: > 0 })
        {
            _battlefieldBatchTransactionOldPlacements.AddRange(oldFactionSnapshots.Select(CloneBattlefieldPlacedUnit));
        }
        BattlefieldDeploymentWriteResult result;
        try
        {
            result = _battlefieldDeploymentWriteService.ApplyScriptPlacements(_currentBattlefieldLegacyScriptDocument, writable);
            if (oldFactionSnapshots is { Count: > 0 })
            {
                try
                {
                    _battlefieldDeploymentWriteService.ClearFriendEnemyScriptPlacements(_currentBattlefieldLegacyScriptDocument, oldFactionSnapshots);
                }
                catch (Exception ex)
                {
                    _battlefieldBatchTransactionFailed = true;
                    var failure = "批量阵营迁移旧槽清理失败：" + ex.Message;
                    failures.Add(failure);
                    _battlefieldBatchTransactionFailures.Add(failure);
                }
            }
        }
        catch (Exception ex)
        {
            _battlefieldBatchTransactionFailed = true;
            var failure = "批量出场字段写入失败：" + ex.Message;
            failures.Add(failure);
            _battlefieldBatchTransactionFailures.Add(failure);
            return false;
        }

        if (result.Changes.Count == 0)
        {
            failures.AddRange(result.SkippedReasons.Take(8));
            return false;
        }

        foreach (var unit in writable)
        {
            MarkBattlefieldBatchTransactionChanged(unit.TargetKey);
        }
        if (oldFactionSnapshots is { Count: > 0 })
        {
            foreach (var oldSnapshot in oldFactionSnapshots)
            {
                MarkBattlefieldBatchTransactionChanged(oldSnapshot.TargetKey);
            }
        }
        failures.AddRange(result.SkippedReasons.Take(8));
        return true;
    }

    private bool ApplyBattlefieldBatchDeploymentStatusFields(
        IReadOnlyList<BattlefieldPlacedUnit> units,
        BattlefieldBatchEditField field,
        List<string> failures)
    {
        if (_project == null || _currentBattlefieldLegacyScriptDocument == null)
        {
            failures.Add("当前没有可写入的 S 剧本树，等级/兵种级/AI 字段只保留在内存草稿。");
            return false;
        }

        var writable = units.Where(BattlefieldUnitStatusWriteService.IsWritableStatusTarget).ToList();
        failures.AddRange(units
            .Where(unit => !BattlefieldUnitStatusWriteService.IsWritableStatusTarget(unit))
            .Select(unit => $"{unit.Name}({unit.GridX},{unit.GridY})：不是可写 Scene1 46/47 等级/兵种级/AI 目标。"));
        if (writable.Count == 0)
        {
            failures.Add("框选单位没有可写回 46/47 等级/兵种级/AI 字段的目标。");
            return false;
        }

        EnsureBattlefieldBatchTransaction(units);
        foreach (var unit in writable)
        {
            _battlefieldBatchTransactionTargetKeys.Add(unit.TargetKey);
        }
        var changed = 0;
        foreach (var unit in writable)
        {
            try
            {
                var draft = _battlefieldUnitStatusWriteService.LoadDraft(
                    _project,
                    _tables,
                    _currentBattlefieldLegacyScriptDocument,
                    _currentBattlefieldDocument?.Scenario.FileName ?? string.Empty,
                    unit);
                if (draft.PersonId != 0 && draft.PersonId != unit.PersonId)
                {
                    throw new InvalidOperationException(
                        $"绑定的出场记录人物已变化：地图草稿人物={unit.PersonId}，当前 S 剧本 Record 人物={draft.PersonId}。禁止批量覆盖其他人物。");
                }
                draft.Weapon = null;
                draft.WeaponLevel = null;
                draft.Armor = null;
                draft.ArmorLevel = null;
                draft.Assist = null;
                draft.JobId = null;
                draft.RemoveEquipmentOverride = false;
                draft.RemoveJobOverride = false;
                draft.RemoveAbilityOverrides.Clear();
                foreach (var ability in draft.Abilities)
                {
                    ability.Value = null;
                    ability.Operation = null;
                    ability.RemoveOverride = false;
                }

                var shouldWrite = false;
                if (field == BattlefieldBatchEditField.LevelOffset && draft.LevelBonus != unit.LevelOffset)
                {
                    draft.LevelBonus = unit.LevelOffset;
                    shouldWrite = true;
                }
                else
                {
                    draft.LevelBonus = null;
                }

                if (field == BattlefieldBatchEditField.LevelMode && draft.JobLevel != MapBattlefieldJobLevel(unit.LevelMode))
                {
                    draft.JobLevel = MapBattlefieldJobLevel(unit.LevelMode);
                    shouldWrite = true;
                }
                else
                {
                    draft.JobLevel = null;
                }

                if (field == BattlefieldBatchEditField.AiMode && draft.AiPolicy != MapBattlefieldAiMode(unit.AiMode))
                {
                    draft.AiPolicy = MapBattlefieldAiMode(unit.AiMode);
                    shouldWrite = true;
                }
                else
                {
                    draft.AiPolicy = null;
                }

                if (!shouldWrite) continue;
                _battlefieldBatchTransactionStatusDeltas.Add(CloneBattlefieldUnitStatusDelta(draft));
                _battlefieldUnitStatusWriteService.Apply(_project, _currentBattlefieldLegacyScriptDocument, draft);
                changed++;
            }
            catch (Exception ex)
            {
                _battlefieldBatchTransactionFailed = true;
                var failure = $"{unit.Name}({unit.GridX},{unit.GridY})：{ex.Message}";
                failures.Add(failure);
                _battlefieldBatchTransactionFailures.Add(failure);
            }
        }

        if (changed == 0) return false;

        foreach (var unit in writable) MarkBattlefieldBatchTransactionChanged(unit.TargetKey);
        return true;
    }

    private void ApplyBattlefieldBatchStatusField(BattlefieldBatchEditField field)
    {
        var units = GetBattlefieldBatchEditingUnits().ToList();
        if (units.Count == 0 || _project == null || _currentBattlefieldLegacyScriptDocument == null)
        {
            return;
        }

        var writable = units.Where(BattlefieldUnitStatusWriteService.IsWritableStatusTarget).ToList();
        var failures = units
            .Where(unit => !BattlefieldUnitStatusWriteService.IsWritableStatusTarget(unit))
            .Select(unit => $"{unit.Name}({unit.GridX},{unit.GridY})：不是可写 Scene1 46/47 状态目标。")
            .ToList();
        if (writable.Count == 0)
        {
            RefreshBattlefieldBatchAfterApply(units, 0, wroteScript: false, failures, $"批量{FormatBattlefieldBatchFieldName(field)}");
            return;
        }

        EnsureBattlefieldBatchTransaction(units);
        foreach (var unit in writable)
        {
            _battlefieldBatchTransactionTargetKeys.Add(unit.TargetKey);
        }
        var changed = 0;
        foreach (var unit in writable)
        {
            try
            {
                var delta = BuildBattlefieldBatchStatusDelta(unit, field);
                if (delta == null || !BattlefieldConsoleDeltaHasChanges(delta))
                {
                    continue;
                }

                _battlefieldBatchTransactionStatusDeltas.Add(CloneBattlefieldUnitStatusDelta(delta));
                _battlefieldUnitStatusWriteService.Apply(_project, _currentBattlefieldLegacyScriptDocument, delta);
                changed++;
            }
            catch (Exception ex)
            {
                _battlefieldBatchTransactionFailed = true;
                var failure = $"{unit.Name}({unit.GridX},{unit.GridY})：{ex.Message}";
                failures.Add(failure);
                _battlefieldBatchTransactionFailures.Add(failure);
            }
        }

        if (changed > 0)
        {
            foreach (var unit in writable) MarkBattlefieldBatchTransactionChanged(unit.TargetKey);
        }

        RefreshBattlefieldBatchAfterApply(units, changed, changed > 0, failures, $"批量{FormatBattlefieldBatchFieldName(field)}");
    }

    private BattlefieldUnitStatusDraft? BuildBattlefieldBatchStatusDelta(BattlefieldPlacedUnit unit, BattlefieldBatchEditField field)
    {
        if (_project == null || _currentBattlefieldLegacyScriptDocument == null)
        {
            return null;
        }

        var current = _battlefieldUnitStatusWriteService.LoadDraft(
            _project,
            _tables,
            _currentBattlefieldLegacyScriptDocument,
            _currentBattlefieldDocument?.Scenario.FileName ?? string.Empty,
            unit);
        if (current.PersonId != 0 && current.PersonId != unit.PersonId)
        {
            throw new InvalidOperationException(
                $"绑定的出场记录人物已变化：地图草稿人物={unit.PersonId}，当前 S 剧本 Record 人物={current.PersonId}。禁止批量覆盖其他人物。");
        }
        var dataDefaults = current.DataDefaults;
        if (dataDefaults == null || !dataDefaults.Found)
        {
            throw new InvalidOperationException("Data.e5 默认值未读取到，不能计算差异写回。");
        }

        var boundary = ItemCategoryBoundaryService.Resolve(_project);
        var weapon = ResolveCurrentBattlefieldEquipmentEffectiveId(current, dataDefaults, boundary, BattlefieldEquipmentSlot.Weapon);
        var armor = ResolveCurrentBattlefieldEquipmentEffectiveId(current, dataDefaults, boundary, BattlefieldEquipmentSlot.Armor);
        var assist = ResolveCurrentBattlefieldEquipmentEffectiveId(current, dataDefaults, boundary, BattlefieldEquipmentSlot.Assist);
        var weaponLevel = current.HasEquipmentCommand ? current.WeaponLevel ?? 0 : dataDefaults.WeaponLevel ?? 0;
        var armorLevel = current.HasEquipmentCommand ? current.ArmorLevel ?? 0 : dataDefaults.ArmorLevel ?? 0;
        var jobId = current.HasJobCommand ? current.JobId : dataDefaults.JobId;
        var abilities = current.Abilities.ToDictionary(
            ability => ability.AbilityId,
            ability =>
            {
                var operation = ability.HasCommand ? ability.Operation ?? 0 : 0;
                var value = ability.HasCommand && ability.Value.HasValue
                    ? ability.Value
                    : dataDefaults.GetAbility(ability.AbilityId);
                return (Operation: operation, Value: value);
            });

        switch (field)
        {
            case BattlefieldBatchEditField.Weapon:
                if (IsBattlefieldBatchMixedComboSelection(_battlefieldConsoleWeaponCombo)) return null;
                weapon = GetSelectedBatchEquipmentLookupValue(_battlefieldConsoleWeaponCombo, dataDefaults.WeaponId);
                break;
            case BattlefieldBatchEditField.WeaponLevel:
                weaponLevel = (int)_battlefieldConsoleWeaponLevelInput.Value;
                break;
            case BattlefieldBatchEditField.Armor:
                if (IsBattlefieldBatchMixedComboSelection(_battlefieldConsoleArmorCombo)) return null;
                armor = GetSelectedBatchEquipmentLookupValue(_battlefieldConsoleArmorCombo, dataDefaults.ArmorId);
                break;
            case BattlefieldBatchEditField.ArmorLevel:
                armorLevel = (int)_battlefieldConsoleArmorLevelInput.Value;
                break;
            case BattlefieldBatchEditField.Assist:
                if (IsBattlefieldBatchMixedComboSelection(_battlefieldConsoleAssistCombo)) return null;
                assist = GetSelectedBatchEquipmentLookupValue(_battlefieldConsoleAssistCombo, dataDefaults.AssistId);
                break;
            case BattlefieldBatchEditField.Job:
                if (IsBattlefieldBatchMixedComboSelection(_battlefieldConsoleJobCombo)) return null;
                jobId = GetSelectedLookupValue(_battlefieldConsoleJobCombo);
                break;
            case BattlefieldBatchEditField.Ability:
                ApplyBattlefieldBatchAbilityValue(abilities);
                break;
            default:
                return null;
        }

        return _battlefieldUnitStatusWriteService.BuildDeltaDraftFromEffectiveValues(
            current,
            dataDefaults,
            boundary,
            weapon,
            weaponLevel,
            armor,
            armorLevel,
            assist,
            jobId,
            abilities);
    }

    private static int? ResolveCurrentBattlefieldEquipmentEffectiveId(
        BattlefieldUnitStatusDraft current,
        BattlefieldUnitDataDefaults dataDefaults,
        ItemCategoryBoundary boundary,
        BattlefieldEquipmentSlot slot)
    {
        if (current.HasEquipmentCommand)
        {
            var scriptCode = slot switch
            {
                BattlefieldEquipmentSlot.Weapon => current.Weapon,
                BattlefieldEquipmentSlot.Armor => current.Armor,
                BattlefieldEquipmentSlot.Assist => current.Assist,
                _ => null
            };
            var dataDefault = slot switch
            {
                BattlefieldEquipmentSlot.Weapon => dataDefaults.WeaponId,
                BattlefieldEquipmentSlot.Armor => dataDefaults.ArmorId,
                BattlefieldEquipmentSlot.Assist => dataDefaults.AssistId,
                _ => null
            };
            return BattlefieldUnitDataDefaultService.FromScriptEquipmentCode(scriptCode, boundary, slot, dataDefault);
        }

        return slot switch
        {
            BattlefieldEquipmentSlot.Weapon => BattlefieldUnitDataDefaultService.NormalizeDataEquipmentId(dataDefaults.WeaponId),
            BattlefieldEquipmentSlot.Armor => BattlefieldUnitDataDefaultService.NormalizeDataEquipmentId(dataDefaults.ArmorId),
            BattlefieldEquipmentSlot.Assist => BattlefieldUnitDataDefaultService.NormalizeDataEquipmentId(dataDefaults.AssistId),
            _ => null
        };
    }

    private void ApplyBattlefieldBatchAbilityValue(Dictionary<int, (int Operation, int? Value)> abilities)
    {
        _battlefieldConsoleAbilityGrid.EndEdit();
        var row = _battlefieldConsoleAbilityGrid.CurrentRow?.DataBoundItem as BattlefieldConsoleAbilityRow;
        if (row == null)
        {
            return;
        }

        int? value = null;
        if (!string.IsNullOrWhiteSpace(row.Value))
        {
            if (!int.TryParse(row.Value.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
            {
                throw new InvalidOperationException($"{row.Name} 必须是整数。");
            }
            value = parsed;
        }

        abilities[row.AbilityId] = (ConsoleTextToOperation(row.Operation), value);
    }

    private static int? GetSelectedBatchEquipmentLookupValue(ComboBox combo, int? dataDefaultItemId)
    {
        if (combo.SelectedItem is BattlefieldUnitStatusLookupItem item)
        {
            return item.Value == int.MinValue
                ? BattlefieldUnitDataDefaultService.NormalizeDataEquipmentId(dataDefaultItemId)
                : item.Value;
        }

        return BattlefieldUnitDataDefaultService.NormalizeDataEquipmentId(dataDefaultItemId);
    }

    private static bool IsBattlefieldBatchMixedComboSelection(ComboBox combo)
        => string.Equals(combo.SelectedItem?.ToString(), "多值", StringComparison.Ordinal);

    private void EnsureBattlefieldBatchTransaction(IEnumerable<BattlefieldPlacedUnit>? beforeUnits = null)
    {
        if (_currentBattlefieldLegacyScriptDocument == null)
        {
            return;
        }

        if (_battlefieldBatchTransactionBeforeEdit != null)
        {
            // Some status controls start their change event after the editor has already
            // opened the batch transaction. Fill the map snapshot once when the concrete
            // targets become available so a later rollback can still restore every unit.
            if (_battlefieldBatchTransactionBeforeUnits.Count == 0 && beforeUnits != null)
            {
                _battlefieldBatchTransactionBeforeUnits.AddRange(beforeUnits.Select(CloneBattlefieldPlacedUnit));
            }
            return;
        }

        _battlefieldBatchTransactionBeforeEdit = CaptureLegacyScenarioHistorySnapshot(
            LegacyScriptEditorScope.Battlefield,
            _currentBattlefieldLegacyScriptDocument);
        _battlefieldBatchTransactionTargetKeys.Clear();
        _battlefieldBatchTransactionDirty = false;
        _battlefieldBatchTransactionFailed = false;
        _battlefieldBatchTransactionStatusDeltas.Clear();
        _battlefieldBatchTransactionOldPlacements.Clear();
        _battlefieldBatchTransactionFailures.Clear();
        _battlefieldBatchTransactionBeforeUnits.Clear();
        if (beforeUnits != null)
        {
            _battlefieldBatchTransactionBeforeUnits.AddRange(beforeUnits.Select(CloneBattlefieldPlacedUnit));
        }
    }

    private void MarkBattlefieldBatchTransactionChanged(string targetKey)
    {
        if (_battlefieldBatchTransactionBeforeEdit == null) return;
        _battlefieldBatchTransactionDirty = true;
        if (!string.IsNullOrWhiteSpace(targetKey))
        {
            _battlefieldBatchTransactionTargetKeys.Add(targetKey);
        }
    }

    private bool FinalizeBattlefieldBatchTransaction()
    {
        var beforeEdit = _battlefieldBatchTransactionBeforeEdit;
        if (beforeEdit == null) return true;

        var targetKeys = _battlefieldBatchTransactionTargetKeys.ToArray();
        var dirty = _battlefieldBatchTransactionDirty;
        var failed = _battlefieldBatchTransactionFailed;
        var beforeUnits = _battlefieldBatchTransactionBeforeUnits.Select(CloneBattlefieldPlacedUnit).ToList();
        var statusDeltas = _battlefieldBatchTransactionStatusDeltas.Select(CloneBattlefieldUnitStatusDelta).ToList();
        var oldPlacements = _battlefieldBatchTransactionOldPlacements.Select(CloneBattlefieldPlacedUnit).ToList();
        var failures = _battlefieldBatchTransactionFailures.ToList();
        var draftUnits = targetKeys
            .Select(targetKey => _battlefieldPlacedUnits.FirstOrDefault(unit => unit.TargetKey.Equals(targetKey, StringComparison.OrdinalIgnoreCase)))
            .Where(unit => unit != null)
            .Select(unit => CloneBattlefieldPlacedUnit(unit!))
            .ToList();

        // Clear the session before refreshing the tree. Tree selection events can re-enter
        // battlefield commit paths and must not finalize the same transaction twice.
        _battlefieldBatchTransactionBeforeEdit = null;
        _battlefieldBatchTransactionTargetKeys.Clear();
        _battlefieldBatchTransactionDirty = false;
        _battlefieldBatchTransactionFailed = false;
        _battlefieldBatchTransactionBeforeUnits.Clear();
        _battlefieldBatchTransactionStatusDeltas.Clear();
        _battlefieldBatchTransactionOldPlacements.Clear();
        _battlefieldBatchTransactionFailures.Clear();

        using var operation = PerformanceMetrics.Begin("Battlefield.Batch.Transaction");
        if (failed)
        {
            var restored = CloneLegacyScenarioDocument(beforeEdit.Document);
            SetCurrentLegacyScriptDocument(LegacyScriptEditorScope.Battlefield, restored);
            var preferredSelection = FindLegacyScenarioHistorySelection(restored, beforeEdit);
            using (SuppressBattlefieldScriptSelectionCommit())
            {
                RefreshBattlefieldLegacyScriptView(preferredSelection);
            }
            foreach (var targetKey in targetKeys)
            {
                ClearBattlefieldInstructionPreviewForTarget(targetKey);
                RefreshBattlefieldInstructionPreviewBindings(targetKey);
            }

            _saveBattlefieldUnitReviewsButton.Enabled = true;
            UpdateBattlefieldDeploymentWriteButton();
            if (beforeUnits.Count == 0)
            {
                beforeUnits = draftUnits.Select(CloneBattlefieldPlacedUnit).ToList();
            }
            _battlefieldMapPreviewSelectedUnit = GetSelectedBattlefieldUnitCandidate();
            RefreshBattlefieldMapDynamicPreview();
            var reason = failures.Count == 0
                ? "批量事务中至少一个目标写回失败。"
                : string.Join("；", failures.Take(8));
            var representative = draftUnits.FirstOrDefault() ?? new BattlefieldPlacedUnit
            {
                TargetKey = targetKeys.FirstOrDefault() ?? string.Empty,
                Name = "批量目标"
            };
            var contextual = BuildBattlefieldConsoleCommitException(
                new InvalidOperationException(reason),
                representative,
                BattlefieldConsoleDirtyKind.Mixed,
                "FinalizeBatch",
                rollbackSucceeded: true,
                isBatch: true);
            var report = ApplicationErrorService.Report(contextual, "Battlefield batch console transaction", notify: false);
            _battlefieldUnsyncedDraftState = new BattlefieldUnsyncedDraftState
            {
                IsBatch = true,
                ScenarioFileName = _currentBattlefieldDocument?.Scenario.FileName ?? string.Empty,
                Stage = "FinalizeBatch",
                ErrorMessage = reason,
                LogPath = report.LogPath,
                DirtyKind = BattlefieldConsoleDirtyKind.Mixed,
                TargetKeys = targetKeys.ToList(),
                BeforeUnits = beforeUnits,
                DraftUnits = draftUnits,
                OldPlacements = oldPlacements,
                StatusDeltas = statusDeltas,
                Failures = failures
            };
            UpdateBattlefieldUnsyncedDraftActions();
            if (_currentBattlefieldDocument != null)
            {
                _battlefieldInfoBox.Text = BuildBattlefieldInfo(_currentBattlefieldDocument) +
                    "\r\n\r\n批量编辑写回失败：S 剧本树已完整回滚；地图与控制台中的修改保留为未同步草稿，可再次尝试写回。";
            }
            SetStatus("战场批量编辑：写回失败，S 剧本树已恢复到本次编辑前；地图与控制台值保留为未同步草稿。日志：" + report.LogPath);
            return false;
        }

        if (!dirty)
        {
            return true;
        }

        PushLegacyScenarioUndoSnapshot(LegacyScriptEditorScope.Battlefield, beforeEdit);
        _battlefieldUnsyncedDraftState = null;
        UpdateBattlefieldUnsyncedDraftActions();
        var changed = targetKeys
            .Select(FindBattlefieldDeploymentSourceCommand)
            .FirstOrDefault(command => command != null);

        // A batch can touch many commands. Rebuild the left tree once at the transaction
        // boundary instead of rebuilding metadata once for every edited unit or field.
        RefreshBattlefieldLegacyScriptView(changed);
        foreach (var targetKey in targetKeys)
        {
            ClearBattlefieldInstructionPreviewForTarget(targetKey);
            RefreshBattlefieldInstructionPreviewBindings(targetKey);
        }

        MarkLegacyScriptStructureDirty(LegacyScriptEditorScope.Battlefield);
        _saveBattlefieldUnitReviewsButton.Enabled = true;
        UpdateBattlefieldDeploymentWriteButton();
        var activeUnits = GetBattlefieldBatchEditingUnits().ToList();
        if (activeUnits.Count > 0)
        {
            SyncBattlefieldControlPanelFromBatchUnits(activeUnits);
        }
        _battlefieldMapPreviewSelectedUnit = GetSelectedBattlefieldUnitCandidate();
        RefreshBattlefieldMapDynamicPreview();
        SetStatus($"战场批量编辑：已合并提交 {targetKeys.Length} 个目标，本次控制台会话只生成一个撤销步骤，尚未完整保存到文件。");
        return true;
    }

    private void RefreshBattlefieldBatchAfterApply(
        IReadOnlyList<BattlefieldPlacedUnit> originalUnits,
        int changedCount,
        bool wroteScript,
        IReadOnlyList<string> failures,
        string action)
    {
        _saveBattlefieldUnitReviewsButton.Enabled = true;
        UpdateBattlefieldDeploymentWriteButton();
        var activeUnits = GetBattlefieldBatchEditingUnits().ToList();
        if (activeUnits.Count == 0)
        {
            activeUnits = originalUnits.Where(unit => _battlefieldPlacedUnits.Contains(unit)).ToList();
            foreach (var unit in activeUnits)
            {
                _batchEditingBattlefieldTargetKeys.Add(unit.TargetKey);
            }
        }

        if (activeUnits.Count > 0)
        {
            SyncBattlefieldControlPanelFromBatchUnits(activeUnits);
        }

        if (_currentBattlefieldDocument != null)
        {
            _battlefieldMapPreviewSelectedUnit = GetSelectedBattlefieldUnitCandidate();
            RefreshBattlefieldMapDynamicPreview();
            _battlefieldInfoBox.Text =
                BuildBattlefieldInfo(_currentBattlefieldDocument) +
                "\r\n\r\n" +
                BuildBattlefieldBatchApplySummary(action, activeUnits.Count, changedCount, wroteScript, failures);
        }

        SetStatus($"{action}：已处理 {changedCount} 个单位" +
                  (wroteScript ? "，已写入左侧 S 剧本树，尚未完整保存。" : "。") +
                  (failures.Count > 0 ? $" 跳过/失败 {failures.Count} 项。" : string.Empty));
    }

    private static string BuildBattlefieldBatchApplySummary(
        string action,
        int selectedCount,
        int changedCount,
        bool wroteScript,
        IReadOnlyList<string> failures)
    {
        var rows = new List<string>
        {
            $"批量编辑结果：{action}",
            $"框选单位：{selectedCount}；实际变更：{changedCount}；S 剧本树写入：{(wroteScript ? "是" : "否或无变化")}"
        };
        if (failures.Count > 0)
        {
            rows.Add("跳过/失败：");
            rows.AddRange(failures.Take(12).Select(item => "- " + item));
            if (failures.Count > 12)
            {
                rows.Add($"- ... 其余 {failures.Count - 12} 项略。");
            }
        }

        return string.Join("\r\n", rows);
    }

    private static string FormatBattlefieldBatchFieldName(BattlefieldBatchEditField field)
        => field switch
        {
            BattlefieldBatchEditField.Faction => "阵营",
            BattlefieldBatchEditField.Hidden => "隐藏",
            BattlefieldBatchEditField.LevelOffset => "等级修正",
            BattlefieldBatchEditField.LevelMode => "等级阶段",
            BattlefieldBatchEditField.AiMode => "AI",
            BattlefieldBatchEditField.Direction => "方向",
            BattlefieldBatchEditField.Weapon => "武器",
            BattlefieldBatchEditField.WeaponLevel => "武器等级",
            BattlefieldBatchEditField.Armor => "防具",
            BattlefieldBatchEditField.ArmorLevel => "防具等级",
            BattlefieldBatchEditField.Assist => "辅助",
            BattlefieldBatchEditField.Job => "兵种",
            BattlefieldBatchEditField.Ability => "五维",
            _ => "字段"
        };

    private static int MapBattlefieldJobLevel(string levelMode)
        => levelMode switch
        {
            "中级" => 1,
            "高级" => 2,
            _ => 0
        };

    private static int MapBattlefieldAiMode(string aiMode)
        => aiMode switch
        {
            "主动" => 1,
            "坚守" => 2,
            "攻击" => 3,
            "到点" => 4,
            "跟随" => 5,
            "逃离" => 6,
            _ => 0
        };

    private bool SelectBattlefieldPlacedUnitByTargetKey(string targetKey, bool enterEdit, bool updatePreview = true)
    {
        var unit = _battlefieldPlacedUnits.FirstOrDefault(item =>
            item.TargetKey.Equals(targetKey, StringComparison.OrdinalIgnoreCase));
        if (unit == null) return false;
        SelectBattlefieldPlacedUnit(unit, enterEdit, updatePreview);
        return true;
    }

    private void SyncBattlefieldControlPanelFromPlacedUnit(BattlefieldPlacedUnit unit)
    {
        _bindingBattlefieldControlPanel = true;
        try
        {
            SetBattlefieldConsolePlacementControlsEnabled(true);
            RemoveBattlefieldBatchMixedComboItems();
            _battlefieldHiddenCheckBox.ThreeState = false;
            _battlefieldFactionAllyRadio.Checked = unit.Faction == "我军";
            _battlefieldFactionFriendRadio.Checked = unit.Faction == "友军";
            _battlefieldFactionEnemyRadio.Checked = unit.Faction == "敌军";
            _battlefieldHiddenCheckBox.Checked = unit.Hidden;
            _battlefieldLevelOffsetInput.Value = Math.Clamp(unit.LevelOffset, (int)_battlefieldLevelOffsetInput.Minimum, (int)_battlefieldLevelOffsetInput.Maximum);
            SelectComboText(_battlefieldLevelModeCombo, unit.LevelMode);
            SelectComboText(_battlefieldAiModeCombo, unit.AiMode);
            SelectComboText(_battlefieldDirectionCombo, unit.Direction);
            LoadBattlefieldConsoleStatusFromPlacedUnit(unit);
        }
        finally
        {
            _bindingBattlefieldControlPanel = false;
        }

        RefreshBattlefieldPaletteUnitPreview(_battlefieldUnitListBox.SelectedItem as BattlefieldUnitPaletteItem);
    }

    private void SyncBattlefieldControlPanelFromBatchUnits(IReadOnlyList<BattlefieldPlacedUnit> units)
    {
        if (units.Count == 0) return;

        _bindingBattlefieldControlPanel = true;
        _bindingBattlefieldBatchControlPanel = true;
        try
        {
            RemoveBattlefieldBatchMixedComboItems();
            SetBattlefieldConsolePlacementControlsEnabled(true);
            var anchor = units[0];
            var writableStatusUnits = units.Where(BattlefieldUnitStatusWriteService.IsWritableStatusTarget).ToList();
            var statusAnchor = writableStatusUnits.FirstOrDefault() ?? anchor;
            var faction = GetCommonBattlefieldText(units, unit => unit.Faction);
            _battlefieldFactionAllyRadio.Checked = faction == "我军";
            _battlefieldFactionFriendRadio.Checked = faction == "友军";
            _battlefieldFactionEnemyRadio.Checked = faction == "敌军";

            _battlefieldHiddenCheckBox.ThreeState = true;
            var hidden = GetCommonBattlefieldBool(units, unit => unit.Hidden);
            _battlefieldHiddenCheckBox.CheckState = hidden.HasValue
                ? hidden.Value ? CheckState.Checked : CheckState.Unchecked
                : CheckState.Indeterminate;

            _battlefieldLevelOffsetInput.Value = Math.Clamp(
                GetCommonBattlefieldInt(units, unit => unit.LevelOffset) ?? anchor.LevelOffset,
                (int)_battlefieldLevelOffsetInput.Minimum,
                (int)_battlefieldLevelOffsetInput.Maximum);
            SelectComboTextOrBatchMixed(_battlefieldLevelModeCombo, GetCommonBattlefieldText(units, unit => unit.LevelMode));
            SelectComboTextOrBatchMixed(_battlefieldAiModeCombo, GetCommonBattlefieldText(units, unit => unit.AiMode));
            SelectComboTextOrBatchMixed(_battlefieldDirectionCombo, GetCommonBattlefieldText(units, unit => unit.Direction));

            LoadBattlefieldConsoleStatusFromPlacedUnit(statusAnchor);
            var statusMixedText = BuildBattlefieldBatchStatusMixedText(units, writableStatusUnits);
            _battlefieldConsoleStatusPreviewBox.Text =
                writableStatusUnits.Count == 0
                    ? "批量编辑：框选中没有可写 Scene1 46/47 状态单位；装备、兵种、五维不会写入。"
                    : (statusAnchor == anchor
                        ? "批量编辑：右侧装备、兵种、五维控件使用第一个单位的数据源；修改任一字段后只把该字段即时应用到当前框选单位。"
                        : $"批量编辑：右侧装备、兵种、五维控件使用第一个可写状态单位 {statusAnchor.PersonId} {statusAnchor.Name} 的数据源；修改任一字段后只把该字段即时应用到当前框选单位。") +
                      statusMixedText;
        }
        finally
        {
            _bindingBattlefieldBatchControlPanel = false;
            _bindingBattlefieldControlPanel = false;
        }

        RefreshBattlefieldPaletteUnitPreview(_battlefieldUnitListBox.SelectedItem as BattlefieldUnitPaletteItem);
    }

    private string BuildBattlefieldBatchStatusMixedText(
        IReadOnlyList<BattlefieldPlacedUnit> units,
        IReadOnlyList<BattlefieldPlacedUnit> writableStatusUnits)
    {
        if (writableStatusUnits.Count == 0)
        {
            return string.Empty;
        }

        if (_project == null || _currentBattlefieldLegacyScriptDocument == null)
        {
            return "\r\n状态字段：当前没有可写入的 S 剧本树。";
        }

        var boundary = ItemCategoryBoundaryService.Resolve(_project);
        var states = new List<(
            BattlefieldPlacedUnit Unit,
            int? Weapon,
            int WeaponLevel,
            int? Armor,
            int ArmorLevel,
            int? Assist,
            int? JobId,
            Dictionary<int, (int Operation, int? Value)> Abilities)>();
        var failures = new List<string>();

        foreach (var unit in writableStatusUnits)
        {
            try
            {
                var draft = _battlefieldUnitStatusWriteService.LoadDraft(
                    _project,
                    _tables,
                    _currentBattlefieldLegacyScriptDocument,
                    _currentBattlefieldDocument?.Scenario.FileName ?? string.Empty,
                    unit);
                var defaults = draft.DataDefaults;
                if (defaults == null || !defaults.Found)
                {
                    failures.Add($"{unit.Name}({unit.GridX},{unit.GridY})：Data.e5 默认值未读取到。");
                    continue;
                }

                states.Add((
                    unit,
                    ResolveCurrentBattlefieldEquipmentEffectiveId(draft, defaults, boundary, BattlefieldEquipmentSlot.Weapon),
                    draft.HasEquipmentCommand ? draft.WeaponLevel ?? 0 : defaults.WeaponLevel ?? 0,
                    ResolveCurrentBattlefieldEquipmentEffectiveId(draft, defaults, boundary, BattlefieldEquipmentSlot.Armor),
                    draft.HasEquipmentCommand ? draft.ArmorLevel ?? 0 : defaults.ArmorLevel ?? 0,
                    ResolveCurrentBattlefieldEquipmentEffectiveId(draft, defaults, boundary, BattlefieldEquipmentSlot.Assist),
                    draft.HasJobCommand ? draft.JobId : defaults.JobId,
                    draft.Abilities.ToDictionary(
                        ability => ability.AbilityId,
                        ability =>
                        {
                            var operation = ability.HasCommand ? ability.Operation ?? 0 : 0;
                            var value = ability.HasCommand && ability.Value.HasValue
                                ? ability.Value
                                : defaults.GetAbility(ability.AbilityId);
                            return (Operation: operation, Value: value);
                        })));
            }
            catch (Exception ex)
            {
                failures.Add($"{unit.Name}({unit.GridX},{unit.GridY})：{ex.Message}");
            }
        }

        if (states.Count == 0)
        {
            return "\r\n状态字段：未能读取任何可写状态单位的当前值；装备、兵种、五维批量写入会被跳过。";
        }

        var mixedFields = new List<string>();
        if (HasMixedNullableInt(states.Select(state => state.Weapon)))
        {
            SelectComboBatchMixed(_battlefieldConsoleWeaponCombo);
            mixedFields.Add("武器");
        }
        if (HasMixedInt(states.Select(state => state.WeaponLevel)))
        {
            mixedFields.Add("武器等级");
        }
        if (HasMixedNullableInt(states.Select(state => state.Armor)))
        {
            SelectComboBatchMixed(_battlefieldConsoleArmorCombo);
            mixedFields.Add("防具");
        }
        if (HasMixedInt(states.Select(state => state.ArmorLevel)))
        {
            mixedFields.Add("防具等级");
        }
        if (HasMixedNullableInt(states.Select(state => state.Assist)))
        {
            SelectComboBatchMixed(_battlefieldConsoleAssistCombo);
            mixedFields.Add("辅助");
        }
        if (HasMixedNullableInt(states.Select(state => state.JobId)))
        {
            SelectComboBatchMixed(_battlefieldConsoleJobCombo);
            mixedFields.Add("兵种");
        }

        var mixedAbilities = states
            .SelectMany(state => state.Abilities.Keys)
            .Distinct()
            .OrderBy(id => id)
            .Where(id => HasMixedAbilityState(states, id))
            .Select(GetBattlefieldAbilityName)
            .ToList();

        var rows = new List<string>
        {
            $"状态写入目标：可写 {states.Count} 个；跳过 {units.Count - states.Count} 个。"
        };
        if (mixedFields.Count > 0 || mixedAbilities.Count > 0)
        {
            var parts = new List<string>();
            if (mixedFields.Count > 0)
            {
                parts.Add(string.Join("。", mixedFields));
            }
            if (mixedAbilities.Count > 0)
            {
                parts.Add("五维：" + string.Join("。", mixedAbilities));
            }
            rows.Add("多值字段：" + string.Join("；", parts) + "。未修改这些字段时，各单位保留原值。");
        }
        else
        {
            rows.Add("状态字段：可写单位当前有效值一致。");
        }

        if (failures.Count > 0)
        {
            rows.Add("状态读取跳过：" + string.Join("；", failures.Take(4)) + (failures.Count > 4 ? $"；其余 {failures.Count - 4} 项略。" : "。"));
        }

        return "\r\n" + string.Join("\r\n", rows);
    }

    private static bool HasMixedNullableInt(IEnumerable<int?> values)
    {
        var initialized = false;
        int? first = null;
        foreach (var value in values)
        {
            if (!initialized)
            {
                first = value;
                initialized = true;
                continue;
            }

            if (!Nullable.Equals(first, value))
            {
                return true;
            }
        }

        return false;
    }

    private static bool HasMixedInt(IEnumerable<int> values)
    {
        var initialized = false;
        var first = 0;
        foreach (var value in values)
        {
            if (!initialized)
            {
                first = value;
                initialized = true;
                continue;
            }

            if (first != value)
            {
                return true;
            }
        }

        return false;
    }

    private static bool HasMixedAbilityState(
        IReadOnlyList<(BattlefieldPlacedUnit Unit, int? Weapon, int WeaponLevel, int? Armor, int ArmorLevel, int? Assist, int? JobId, Dictionary<int, (int Operation, int? Value)> Abilities)> states,
        int abilityId)
    {
        if (states.Count <= 1)
        {
            return false;
        }

        var first = GetBattlefieldAbilityState(states[0].Abilities, abilityId);
        for (var index = 1; index < states.Count; index++)
        {
            var current = GetBattlefieldAbilityState(states[index].Abilities, abilityId);
            if (first.Operation != current.Operation || !Nullable.Equals(first.Value, current.Value))
            {
                return true;
            }
        }

        return false;
    }

    private static (int Operation, int? Value) GetBattlefieldAbilityState(
        IReadOnlyDictionary<int, (int Operation, int? Value)> abilities,
        int abilityId)
        => abilities.TryGetValue(abilityId, out var value) ? value : (0, null);

    private static string? GetCommonBattlefieldText(IReadOnlyList<BattlefieldPlacedUnit> units, Func<BattlefieldPlacedUnit, string> selector)
    {
        if (units.Count == 0) return null;
        var value = selector(units[0]);
        for (var index = 1; index < units.Count; index++)
        {
            if (!string.Equals(value, selector(units[index]), StringComparison.Ordinal))
            {
                return null;
            }
        }

        return value;
    }

    private static int? GetCommonBattlefieldInt(IReadOnlyList<BattlefieldPlacedUnit> units, Func<BattlefieldPlacedUnit, int> selector)
    {
        if (units.Count == 0) return null;
        var value = selector(units[0]);
        for (var index = 1; index < units.Count; index++)
        {
            if (value != selector(units[index]))
            {
                return null;
            }
        }

        return value;
    }

    private static bool? GetCommonBattlefieldBool(IReadOnlyList<BattlefieldPlacedUnit> units, Func<BattlefieldPlacedUnit, bool> selector)
    {
        if (units.Count == 0) return null;
        var value = selector(units[0]);
        for (var index = 1; index < units.Count; index++)
        {
            if (value != selector(units[index]))
            {
                return null;
            }
        }

        return value;
    }

    private static void SelectComboTextOrBatchMixed(ComboBox combo, string? text)
    {
        if (!string.IsNullOrWhiteSpace(text))
        {
            SelectComboText(combo, text);
            return;
        }

        SelectComboBatchMixed(combo);
    }

    private static void SelectComboBatchMixed(ComboBox combo)
    {
        const string mixedText = "多值";
        if (!combo.Items.Cast<object>().Any(item => string.Equals(item?.ToString(), mixedText, StringComparison.Ordinal)))
        {
            combo.Items.Insert(0, mixedText);
        }
        combo.SelectedItem = mixedText;
    }

    private void RemoveBattlefieldBatchMixedComboItems()
    {
        RemoveBattlefieldBatchMixedComboItem(_battlefieldLevelModeCombo);
        RemoveBattlefieldBatchMixedComboItem(_battlefieldAiModeCombo);
        RemoveBattlefieldBatchMixedComboItem(_battlefieldDirectionCombo);
        RemoveBattlefieldBatchMixedComboItem(_battlefieldConsoleWeaponCombo);
        RemoveBattlefieldBatchMixedComboItem(_battlefieldConsoleArmorCombo);
        RemoveBattlefieldBatchMixedComboItem(_battlefieldConsoleAssistCombo);
        RemoveBattlefieldBatchMixedComboItem(_battlefieldConsoleJobCombo);
    }

    private static void RemoveBattlefieldBatchMixedComboItem(ComboBox combo)
    {
        for (var index = combo.Items.Count - 1; index >= 0; index--)
        {
            if (string.Equals(combo.Items[index]?.ToString(), "多值", StringComparison.Ordinal))
            {
                combo.Items.RemoveAt(index);
            }
        }
    }

    private static string BuildBattlefieldBatchSummaryText(IReadOnlyList<BattlefieldPlacedUnit> units)
    {
        var rows = new List<string> { $"批量编辑：{units.Count} 个单位" };
        rows.AddRange(units.Take(10).Select(unit =>
            $"- {unit.PersonId} {unit.Name}  坐标=({unit.GridX},{unit.GridY})  阵营={unit.Faction}  绑定={unit.TargetKey}"));
        if (units.Count > 10)
        {
            rows.Add($"- ... 其余 {units.Count - 10} 个略。");
        }

        rows.Add("修改右侧控制台字段会立即应用到这些单位；未修改字段保持各单位原值。");
        return string.Join("\r\n", rows);
    }

    private void LoadBattlefieldConsoleStatusFromPlacedUnit(BattlefieldPlacedUnit unit)
    {
        _currentBattlefieldConsoleStatusDraft = null;
        _currentBattlefieldConsoleDataDefaults = null;
        _battlefieldDataDefaultsBox.Text = "Data.e5 默认值：未读取。";
        _battlefieldConsoleStatusPreviewBox.Text = "脚本覆盖摘要：当前单位没有可编辑的 Scene1 46/47 状态绑定。";
        SetBattlefieldConsoleStatusControlsEnabled(false);
        BindBattlefieldConsoleAbilityGrid(Array.Empty<BattlefieldConsoleAbilityRow>());

        if (_project == null)
        {
            _battlefieldDataDefaultsBox.Text = "Data.e5 默认值：项目未加载。";
            SetStatus("战场控制台：项目未加载，无法读取 Data.e5 默认值。");
            return;
        }

        var dataDefaults = new BattlefieldUnitDataDefaultService().LoadPersonDefaults(_project, _tables, unit.PersonId);
        _currentBattlefieldConsoleDataDefaults = dataDefaults;
        _battlefieldDataDefaultsBox.Text = BuildBattlefieldDataDefaultsText(dataDefaults);

        if (_currentBattlefieldLegacyScriptDocument == null ||
            !BattlefieldUnitStatusWriteService.IsWritableStatusTarget(unit))
        {
            _battlefieldConsoleStatusPreviewBox.Text = BattlefieldUnitStatusWriteService.IsScene2PlusStatusTarget(unit)
                ? BattlefieldUnitStatusWriteService.Scene2PlusStatusWriteDisabledMessage
                : IsBattlefieldAllyDeploymentTargetKey(unit.TargetKey)
                    ? "脚本覆盖摘要：0x4B 我军出战位只允许写坐标、方向、隐藏，不写装备/兵种/五维。"
                    : "脚本覆盖摘要：本地草稿尚未绑定 Scene1 0x46/0x47，不能写装备/兵种/五维。";
            SetStatus(BattlefieldUnitStatusWriteService.IsScene2PlusStatusTarget(unit)
                ? BattlefieldUnitStatusWriteService.Scene2PlusStatusWriteDisabledMessage
                : IsBattlefieldAllyDeploymentTargetKey(unit.TargetKey)
                ? "战场控制台：0x4B 我军出战位只允许写坐标、方向、隐藏，不写装备/兵种/五维。"
                : "战场控制台：本地草稿尚未绑定 0x46/0x47，不能写装备/兵种/五维。");
            return;
        }

        try
        {
            var draft = _battlefieldUnitStatusWriteService.LoadDraft(
                _project,
                _tables,
                _currentBattlefieldLegacyScriptDocument,
                _currentBattlefieldDocument?.Scenario.FileName ?? string.Empty,
                unit);
            draft.EquipmentBoundarySummary = ItemCategoryBoundaryService.Resolve(_project).DisplayText;
            _currentBattlefieldConsoleStatusDraft = draft;
            _currentBattlefieldConsoleDataDefaults = draft.DataDefaults ?? dataDefaults;
            PopulateBattlefieldConsoleStatusEditors(draft, _currentBattlefieldConsoleDataDefaults);
            SetBattlefieldConsoleStatusControlsEnabled(true);
            _battlefieldConsoleStatusPreviewBox.Text = BuildBattlefieldConsolePreviewText(draft, _currentBattlefieldConsoleDataDefaults);
            if (!_currentBattlefieldConsoleDataDefaults.Found)
            {
                SetStatus("战场控制台：Data.e5 默认值未读到；右侧已按 Scene1 左侧剧本树显示，Data 兜底参考不可用。");
            }
        }
        catch (Exception ex)
        {
            _battlefieldConsoleStatusPreviewBox.Text = "脚本覆盖摘要：读叀Scene1 46/47 状态失败：" + ex.Message;
            SetStatus("战场控制台：读取 46/47 状态失败：" + ex.Message);
            System.Diagnostics.Debug.WriteLine("Load battlefield console status failed: " + ex);
        }
    }

    private void SetBattlefieldConsoleStatusControlsEnabled(bool enabled)
    {
        _battlefieldConsoleWeaponCombo.Enabled = enabled;
        _battlefieldConsoleWeaponLevelInput.Enabled = enabled;
        _battlefieldConsoleArmorCombo.Enabled = enabled;
        _battlefieldConsoleArmorLevelInput.Enabled = enabled;
        _battlefieldConsoleAssistCombo.Enabled = enabled;
        _battlefieldConsoleJobCombo.Enabled = enabled;
        _battlefieldConsoleAbilityGrid.Enabled = enabled;
    }

    private void SetBattlefieldConsolePlacementControlsEnabled(bool enabled)
    {
        _battlefieldFactionAllyRadio.Enabled = enabled;
        _battlefieldFactionFriendRadio.Enabled = enabled;
        _battlefieldFactionEnemyRadio.Enabled = enabled;
        _battlefieldHiddenCheckBox.Enabled = enabled;
        _battlefieldLevelOffsetInput.Enabled = enabled;
        _battlefieldLevelModeCombo.Enabled = enabled;
        _battlefieldAiModeCombo.Enabled = enabled;
        _battlefieldDirectionCombo.Enabled = enabled;
        _battlefieldRemovePlacedUnitButton.Enabled = enabled;
    }

    private string BuildBattlefieldDataDefaultsText(BattlefieldUnitDataDefaults defaults)
    {
        if (!defaults.Found)
        {
            return $"Data.e5 默认值：{defaults.Source}";
        }

        var abilityText = string.Join("  ", defaults.Abilities
            .OrderBy(pair => pair.Key)
            .Select(pair => $"{GetBattlefieldAbilityName(pair.Key)}={pair.Value}"));
        return
            $"Data.e5 默认值：{defaults.Source}\r\n" +
            $"人物：{defaults.PersonId} {defaults.PersonName}  兵种={defaults.FormatJob()}  等级={FormatNullableBattlefieldInt(defaults.Level)}  经验={FormatNullableBattlefieldInt(defaults.Experience)}\r\n" +
            $"五维：{abilityText}\r\n" +
            $"装备：武器={FormatBattlefieldEquipmentBrief(defaults.WeaponId, defaults)} Lv{FormatNullableBattlefieldInt(defaults.WeaponLevel)}；" +
            $"防具={FormatBattlefieldEquipmentBrief(defaults.ArmorId, defaults)} Lv{FormatNullableBattlefieldInt(defaults.ArmorLevel)}；辅助={FormatBattlefieldEquipmentBrief(defaults.AssistId, defaults)}";
    }

    private void PopulateBattlefieldConsoleStatusEditors(BattlefieldUnitStatusDraft draft, BattlefieldUnitDataDefaults defaults)
    {
        PopulateBattlefieldConsoleItemCombo(_battlefieldConsoleWeaponCombo, defaults, BattlefieldEquipmentSlot.Weapon, draft, defaults.WeaponId);
        PopulateBattlefieldConsoleItemCombo(_battlefieldConsoleArmorCombo, defaults, BattlefieldEquipmentSlot.Armor, draft, defaults.ArmorId);
        PopulateBattlefieldConsoleItemCombo(_battlefieldConsoleAssistCombo, defaults, BattlefieldEquipmentSlot.Assist, draft, defaults.AssistId);
        PopulateBattlefieldConsoleJobCombo(defaults);
        var boundary = ItemCategoryBoundaryService.Resolve(_project!);
        SelectBattlefieldComboValue(
            _battlefieldConsoleWeaponCombo,
            ResolveBattlefieldConsoleEquipmentSelection(draft, BattlefieldEquipmentSlot.Weapon, defaults.WeaponId, boundary));
        _battlefieldConsoleWeaponLevelInput.Value = Math.Clamp(draft.HasEquipmentCommand ? draft.WeaponLevel ?? 0 : defaults.WeaponLevel ?? 0, 0, 16);
        SelectBattlefieldComboValue(
            _battlefieldConsoleArmorCombo,
            ResolveBattlefieldConsoleEquipmentSelection(draft, BattlefieldEquipmentSlot.Armor, defaults.ArmorId, boundary));
        _battlefieldConsoleArmorLevelInput.Value = Math.Clamp(draft.HasEquipmentCommand ? draft.ArmorLevel ?? 0 : defaults.ArmorLevel ?? 0, 0, 16);
        SelectBattlefieldComboValue(
            _battlefieldConsoleAssistCombo,
            ResolveBattlefieldConsoleEquipmentSelection(draft, BattlefieldEquipmentSlot.Assist, defaults.AssistId, boundary));
        SelectBattlefieldComboValue(_battlefieldConsoleJobCombo, draft.HasJobCommand ? draft.JobId : defaults.JobId);

        var rows = draft.Abilities.Select(ability =>
        {
            var dataDefault = defaults.GetAbility(ability.AbilityId);
            var operation = ability.HasCommand ? ability.Operation ?? 0 : 0;
            var value = ability.HasCommand && ability.Value.HasValue
                ? ability.Value.Value
                : dataDefault;
            return new BattlefieldConsoleAbilityRow
            {
                AbilityId = ability.AbilityId,
                Name = ability.Name,
                Source = ability.HasCommand ? "S覆盖" : "Data默认",
                Operation = OperationToConsoleText(operation),
                Value = value?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
                DataDefault = dataDefault?.ToString(CultureInfo.InvariantCulture) ?? string.Empty
            };
        }).ToList();
        BindBattlefieldConsoleAbilityGrid(rows);
    }

    private static int? ResolveBattlefieldConsoleEquipmentSelection(
        BattlefieldUnitStatusDraft draft,
        BattlefieldEquipmentSlot slot,
        int? dataDefaultItemId,
        ItemCategoryBoundary boundary)
    {
        if (draft.HasEquipmentCommand)
        {
            var scriptCode = slot switch
            {
                BattlefieldEquipmentSlot.Weapon => draft.Weapon,
                BattlefieldEquipmentSlot.Armor => draft.Armor,
                BattlefieldEquipmentSlot.Assist => draft.Assist,
                _ => null
            };
            if (!scriptCode.HasValue || scriptCode.Value == 0)
            {
                return int.MinValue;
            }

            return BattlefieldUnitDataDefaultService.FromScriptEquipmentCode(scriptCode, boundary, slot, dataDefaultItemId);
        }

        var normalizedData = BattlefieldUnitDataDefaultService.NormalizeDataEquipmentId(dataDefaultItemId);
        return normalizedData ?? int.MinValue;
    }

    private void PopulateBattlefieldConsoleItemCombo(
        ComboBox combo,
        BattlefieldUnitDataDefaults defaults,
        BattlefieldEquipmentSlot slot,
        BattlefieldUnitStatusDraft draft,
        int? dataDefaultItemId)
    {
        combo.DropDownStyle = ComboBoxStyle.DropDownList;
        combo.Items.Clear();
        var normalizedDataDefault = BattlefieldUnitDataDefaultService.NormalizeDataEquipmentId(dataDefaultItemId);
        var defaultText = draft.HasEquipmentCommand
            ? "默认/未指定"
            : FormatBattlefieldEquipmentBrief(normalizedDataDefault ?? dataDefaultItemId, defaults);
        combo.Items.Add(new BattlefieldUnitStatusLookupItem
        {
            Value = int.MinValue,
            Text = defaultText,
            EquipmentDefaultItemId = draft.HasEquipmentCommand ? null : normalizedDataDefault
        });
        combo.Items.Add(new BattlefieldUnitStatusLookupItem { Value = -1, Text = "卸去装备" });
        var boundary = _project != null
            ? ItemCategoryBoundaryService.Resolve(_project)
            : new ItemCategoryBoundary(
                ItemCategoryBoundaryService.MinItemId,
                ItemCategoryBoundaryService.DefaultDefenseStartId,
                ItemCategoryBoundaryService.DefaultAccessoryStartId,
                "控制台物品分类默认边界",
                IsFallback: true);
        var ids = defaults.ItemNames.Keys
            .Where(id => IsBattlefieldEquipmentSlotItem(id, boundary, slot))
            .OrderBy(id => id)
            .ToList();
        foreach (var id in ids)
        {
            combo.Items.Add(new BattlefieldUnitStatusLookupItem
            {
                Value = id,
                Text = FormatBattlefieldEquipmentBrief(id, defaults)
            });
        }
    }

    private static string FormatBattlefieldEquipmentBrief(int? itemId, BattlefieldUnitDataDefaults defaults)
    {
        if (itemId == BattlefieldUnitDataDefaultService.DataEquipmentUnset || !itemId.HasValue)
        {
            return "默认/未指定";
        }

        if (itemId.Value < 0)
        {
            return "卸去装备";
        }

        var name = defaults.ItemNames.TryGetValue(itemId.Value, out var itemName) && !string.IsNullOrWhiteSpace(itemName)
            ? itemName
            : "物品" + itemId.Value.ToString(CultureInfo.InvariantCulture);
        return $"ID{itemId.Value.ToString(CultureInfo.InvariantCulture)} {name}";
    }

    private static bool IsBattlefieldEquipmentSlotItem(int itemId, ItemCategoryBoundary boundary, BattlefieldEquipmentSlot slot)
        => slot switch
        {
            BattlefieldEquipmentSlot.Weapon => itemId >= boundary.WeaponStartId && itemId < boundary.DefenseStartId,
            BattlefieldEquipmentSlot.Armor => itemId >= boundary.DefenseStartId && itemId < boundary.AccessoryStartId,
            BattlefieldEquipmentSlot.Assist => itemId >= boundary.AccessoryStartId,
            _ => false
        };

    private void PopulateBattlefieldConsoleJobCombo(BattlefieldUnitDataDefaults defaults)
    {
        _battlefieldConsoleJobCombo.DropDownStyle = ComboBoxStyle.DropDownList;
        _battlefieldConsoleJobCombo.Items.Clear();
        foreach (var pair in defaults.JobNames.OrderBy(pair => pair.Key))
        {
            _battlefieldConsoleJobCombo.Items.Add(new BattlefieldUnitStatusLookupItem { Value = pair.Key, Text = $"{pair.Key}：{pair.Value}" });
        }
    }

    private void BindBattlefieldConsoleAbilityGrid(IReadOnlyList<BattlefieldConsoleAbilityRow> rows)
    {
        _battlefieldConsoleAbilityGrid.AutoGenerateColumns = false;
        if (_battlefieldConsoleAbilityGrid.Columns.Count == 0)
        {
            _battlefieldConsoleAbilityGrid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = nameof(BattlefieldConsoleAbilityRow.Name), HeaderText = "五维", ReadOnly = true, FillWeight = 58 });
            _battlefieldConsoleAbilityGrid.Columns.Add(new DataGridViewComboBoxColumn { DataPropertyName = nameof(BattlefieldConsoleAbilityRow.Operation), HeaderText = "操作", DataSource = new[] { "=", "+", "-" }, FillWeight = 48 });
            _battlefieldConsoleAbilityGrid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = nameof(BattlefieldConsoleAbilityRow.Value), HeaderText = "目标值", FillWeight = 70 });
            _battlefieldConsoleAbilityGrid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = nameof(BattlefieldConsoleAbilityRow.DataDefault), HeaderText = "Data", ReadOnly = true, FillWeight = 58 });
            _battlefieldConsoleAbilityGrid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = nameof(BattlefieldConsoleAbilityRow.Source), HeaderText = "来源", ReadOnly = true, FillWeight = 72 });
        }

        foreach (DataGridViewColumn column in _battlefieldConsoleAbilityGrid.Columns)
        {
            if (column.DataPropertyName == nameof(BattlefieldConsoleAbilityRow.Source))
            {
                column.Visible = false;
            }
        }

        _battlefieldConsoleAbilityGrid.DataSource = new BindingList<BattlefieldConsoleAbilityRow>(rows.ToList());
    }

    private string BuildBattlefieldConsolePreviewText(BattlefieldUnitStatusDraft draft, BattlefieldUnitDataDefaults defaults)
    {
        var parts = new List<string>
        {
            "脚本覆盖摘要：Scene1 左侧剧本树优先，Data.e5 仅作兜底参考。",
            $"出场字段：左侧剧本树 0x{draft.CommandId:X2} Record={draft.RecordIndex}。",
            draft.HasEquipmentCommand
                ? "装备：左侧剧本树 0x48；槽值 0 表示使用默认配装（左树），不会替换显示为 Data 具体装备。"
                : "装备：Scene1 无 0x48，当前显示 Data.e5。",
            draft.HasJobCommand
                ? "兵种：左侧剧本树 0x52。"
                : "兵种：Scene1 无 0x52，当前显示 Data.e5。"
        };
        foreach (var ability in draft.Abilities)
        {
            var source = ability.HasCommand ? "左侧剧本栀0x38" : "Data.e5";
            var value = ability.HasCommand && ability.Value.HasValue
                ? $"{OperationToConsoleText(ability.Operation ?? 0)} {ability.Value.Value.ToString(CultureInfo.InvariantCulture)}"
                : FormatNullableBattlefieldInt(defaults.GetAbility(ability.AbilityId));
            parts.Add($"{ability.Name}：{source} 当前={value}");
        }

        return string.Join("\r\n", parts);
    }

    private bool TryCommitPendingBattlefieldConsoleChanges(bool finalizeBatchTransaction = true)
        => TryCommitPendingBattlefieldConsoleChangesResult(finalizeBatchTransaction).Success;

    private bool TryCommitPendingBattlefieldConsoleChangesForSave(bool finalizeBatchTransaction = true)
    {
        var result = TryCommitPendingBattlefieldConsoleChangesResult(finalizeBatchTransaction);
        if (result.Status == BattlefieldConsoleCommitStatus.DraftOnly)
        {
            MessageBox.Show(
                this,
                "当前角色只保留为项目侧布阵草稿，尚未绑定可写 46/47/4B S 剧本记录。请先保存布阵草稿或为其绑定出场槽，再执行 S 剧本写回。",
                "存在本地布阵草稿",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return false;
        }
        return BattlefieldConsoleCommitAllowsSave(result);
    }

    private bool BattlefieldConsoleCommitAllowsSave(BattlefieldConsoleCommitResult result)
        => result.Status != BattlefieldConsoleCommitStatus.DraftOnly && result.Success;

    private BattlefieldConsoleCommitResult TryCommitPendingBattlefieldConsoleChangesResult(bool finalizeBatchTransaction = true)
    {
        _battlefieldConsoleCommitAttemptCountForSmoke++;
        FlushPendingBattlefieldDropSynchronizations();
        if (_battlefieldConsoleCommitInProgress)
        {
            return BuildBattlefieldConsoleCommitResult(
                BattlefieldConsoleCommitStatus.CommitInProgress,
                "战场控制台正在提交，请等待当前事务完成。",
                _battlefieldConsoleDirtyTargetKey,
                _battlefieldConsoleDirtyKind,
                allowsNavigation: false,
                retainsDraft: _battlefieldConsoleDirty);
        }

        if (_bindingBattlefieldControlPanel)
        {
            return BuildBattlefieldConsoleCommitResult(
                BattlefieldConsoleCommitStatus.NoChanges,
                "战场控制台正在绑定界面，没有待提交的用户编辑。",
                _battlefieldConsoleDirtyTargetKey,
                _battlefieldConsoleDirtyKind,
                allowsNavigation: true,
                retainsDraft: false);
        }

        if (_battlefieldUnsyncedDraftState != null &&
            !_battlefieldConsoleDirty &&
            _battlefieldBatchTransactionBeforeEdit == null)
        {
            return ShowBattlefieldConsoleCommitResult(BuildBattlefieldConsoleCommitResult(
                _battlefieldUnsyncedDraftState.IsBatch
                    ? BattlefieldConsoleCommitStatus.BatchWriteFailedRolledBack
                    : BattlefieldConsoleCommitStatus.WriteFailedRolledBack,
                "战场控制台：仍有写回失败的未同步草稿。请使用“重试同步”“恢复脚本值”或“保留草稿并解除绑定”处理后再继续修改 S 剧本。",
                string.Join(";", _battlefieldUnsyncedDraftState.TargetKeys),
                _battlefieldUnsyncedDraftState.DirtyKind,
                allowsNavigation: false,
                retainsDraft: true,
                _battlefieldUnsyncedDraftState.LogPath));
        }

        if (!_battlefieldConsoleDirty)
        {
            if (finalizeBatchTransaction && !FinalizeBattlefieldBatchTransaction())
            {
                return BuildBattlefieldConsoleCommitResult(
                    BattlefieldConsoleCommitStatus.BatchWriteFailedRolledBack,
                    _battlefieldUnsyncedDraftState?.ErrorMessage ?? "战场批量编辑写回失败，S 剧本树已回滚。",
                    string.Join(";", _battlefieldUnsyncedDraftState?.TargetKeys ?? []),
                    BattlefieldConsoleDirtyKind.Mixed,
                    allowsNavigation: false,
                    retainsDraft: true,
                    _battlefieldUnsyncedDraftState?.LogPath);
            }

            return BuildBattlefieldConsoleCommitResult(
                BattlefieldConsoleCommitStatus.NoChanges,
                "战场控制台没有待提交编辑。",
                string.Empty,
                BattlefieldConsoleDirtyKind.None,
                allowsNavigation: true,
                retainsDraft: false);
        }

        var targetKey = _battlefieldConsoleDirtyTargetKey;
        var unit = _battlefieldPlacedUnits.FirstOrDefault(candidate =>
            candidate.TargetKey.Equals(targetKey, StringComparison.OrdinalIgnoreCase));
        if (unit == null)
        {
            return ShowBattlefieldConsoleCommitResult(BuildBattlefieldConsoleCommitResult(
                BattlefieldConsoleCommitStatus.ValidationFailed,
                $"战场控制台：待提交目标已不存在，不能静默丢弃编辑。目标={targetKey}",
                targetKey,
                _battlefieldConsoleDirtyKind,
                allowsNavigation: false,
                retainsDraft: true));
        }

        if (_selectedBattlefieldPlacedUnit == null ||
            !ReferenceEquals(_selectedBattlefieldPlacedUnit, unit))
        {
            return ShowBattlefieldConsoleCommitResult(BuildBattlefieldConsoleCommitResult(
                BattlefieldConsoleCommitStatus.ValidationFailed,
                $"战场控制台：当前选择与待提交目标不一致，已保留未同步草稿。请重新选择 {unit.Name} 后重试。",
                targetKey,
                _battlefieldConsoleDirtyKind,
                allowsNavigation: false,
                retainsDraft: true));
        }

        var dirtyKind = _battlefieldConsoleDirtyKind;
        UpdateSelectedBattlefieldPlacedUnitFromConsoleControls();
        if (_currentBattlefieldLegacyScriptDocument == null)
        {
            ClearBattlefieldConsoleDirty();
            return ShowBattlefieldConsoleCommitResult(BuildBattlefieldConsoleCommitResult(
                BattlefieldConsoleCommitStatus.DraftOnly,
                $"战场控制台：当前未加载可写 S 剧本完整树，{unit.Name} 的修改保留为项目侧布阵草稿。",
                targetKey,
                dirtyKind,
                allowsNavigation: true,
                retainsDraft: true));
        }
        var canWritePlacement = BattlefieldDeploymentWriteService.IsScriptPlacementWritable(unit);
        var canWriteStatus = BattlefieldUnitStatusWriteService.IsWritableStatusTarget(unit);
        if (!canWritePlacement && !canWriteStatus)
        {
            ClearBattlefieldConsoleDirty();
            _battlefieldUnsyncedDraftState = null;
            UpdateBattlefieldUnsyncedDraftActions();
            return ShowBattlefieldConsoleCommitResult(BuildBattlefieldConsoleCommitResult(
                BattlefieldConsoleCommitStatus.DraftOnly,
                $"战场控制台：{unit.Name} 没有可写 46/47/4B 绑定，当前值保留为项目侧布阵草稿。",
                targetKey,
                dirtyKind,
                allowsNavigation: true,
                retainsDraft: true));
        }

        BattlefieldUnitStatusDraft? statusDelta = null;
        var stage = "BuildDelta";
        try
        {
            if (canWriteStatus && (dirtyKind & BattlefieldConsoleDirtyKind.Placement) != 0)
            {
                if (_battlefieldConsoleBeforeEditSnapshot == null ||
                    BattlefieldStatusDeploymentFieldsDiffer(_battlefieldConsoleBeforeEditSnapshot, unit))
                {
                    statusDelta = BuildBattlefieldConsoleDeploymentDelta(unit);
                }
            }

            if (canWriteStatus &&
                (dirtyKind & (BattlefieldConsoleDirtyKind.Equipment | BattlefieldConsoleDirtyKind.RuntimeAbility)) != 0)
            {
                var statusBuild = BuildBattlefieldConsolePendingDelta(unit);
                if (statusBuild.Status == BattlefieldConsoleDeltaBuildStatus.ValidationFailed)
                {
                    return ShowBattlefieldConsoleCommitResult(BuildBattlefieldConsoleCommitResult(
                        BattlefieldConsoleCommitStatus.ValidationFailed,
                        statusBuild.Message,
                        targetKey,
                        dirtyKind,
                        allowsNavigation: false,
                        retainsDraft: true,
                        focusTarget: statusBuild.FocusTarget));
                }

                if (statusBuild.Status == BattlefieldConsoleDeltaBuildStatus.Ready && statusBuild.Delta != null)
                {
                    statusDelta = MergeBattlefieldConsoleStatusDeltas(statusDelta, statusBuild.Delta);
                }
            }

            var applyPlacement = canWritePlacement &&
                                 (dirtyKind & BattlefieldConsoleDirtyKind.Placement) != 0 &&
                                 (_battlefieldConsoleBeforeEditSnapshot == null ||
                                  BattlefieldDeploymentOwnedFieldsDiffer(_battlefieldConsoleBeforeEditSnapshot, unit));
            var hasStatusChanges = statusDelta != null && BattlefieldConsoleDeltaHasChanges(statusDelta);
            if (!applyPlacement && !hasStatusChanges)
            {
                ClearBattlefieldConsoleDirty();
                _battlefieldUnsyncedDraftState = null;
                UpdateBattlefieldUnsyncedDraftActions();
                return ShowBattlefieldConsoleCommitResult(BuildBattlefieldConsoleCommitResult(
                    BattlefieldConsoleCommitStatus.NoChanges,
                    "战场控制台：当前值与 S 剧本及 Data 有效值一致，没有需要写入的变化。",
                    targetKey,
                    dirtyKind,
                    allowsNavigation: true,
                    retainsDraft: false));
            }

            LegacyScenarioHistorySnapshot? beforeEdit = null;
            if (_currentBattlefieldLegacyScriptDocument != null)
            {
                beforeEdit = CaptureLegacyScenarioHistorySnapshot(
                    LegacyScriptEditorScope.Battlefield,
                    _currentBattlefieldLegacyScriptDocument);
            }

            _battlefieldConsoleCommitInProgress = true;
            try
            {
                using var operation = PerformanceMetrics.Begin("Battlefield.Console.Transaction");
                stage = applyPlacement ? "ApplyPlacement" : "ApplyStatus";
                var committed = CommitBattlefieldConsoleTransaction(unit, statusDelta, applyPlacement, ref stage);
                if (committed && beforeEdit != null)
                {
                    PushLegacyScenarioUndoSnapshot(LegacyScriptEditorScope.Battlefield, beforeEdit);
                    MarkLegacyScriptStructureDirty(LegacyScriptEditorScope.Battlefield);
                }

                ClearBattlefieldConsoleDirty();
                _battlefieldUnsyncedDraftState = null;
                UpdateBattlefieldUnsyncedDraftActions();

                if (finalizeBatchTransaction && !FinalizeBattlefieldBatchTransaction())
                {
                    return BuildBattlefieldConsoleCommitResult(
                        BattlefieldConsoleCommitStatus.BatchWriteFailedRolledBack,
                        _battlefieldUnsyncedDraftState?.ErrorMessage ?? "战场批量编辑写回失败，S 剧本树已回滚。",
                        targetKey,
                        dirtyKind,
                        allowsNavigation: false,
                        retainsDraft: true,
                        _battlefieldUnsyncedDraftState?.LogPath);
                }

                return ShowBattlefieldConsoleCommitResult(BuildBattlefieldConsoleCommitResult(
                    committed ? BattlefieldConsoleCommitStatus.Committed : BattlefieldConsoleCommitStatus.NoChanges,
                    committed
                        ? "战场控制台：本次焦点会话的变更已合并写入左侧 S 剧本树，尚未完整保存。"
                        : "战场控制台：当前值没有产生实际写入变化。",
                    targetKey,
                    dirtyKind,
                    allowsNavigation: true,
                    retainsDraft: false));
            }
            catch (Exception ex)
            {
                var rollbackSucceeded = false;
                if (beforeEdit != null)
                {
                    try
                    {
                        var restored = CloneLegacyScenarioDocument(beforeEdit.Document);
                        SetCurrentLegacyScriptDocument(LegacyScriptEditorScope.Battlefield, restored);
                        var preferredSelection = FindLegacyScenarioHistorySelection(restored, beforeEdit);
                        using (SuppressBattlefieldScriptSelectionCommit())
                        {
                            RefreshBattlefieldLegacyScriptView(preferredSelection);
                        }
                        rollbackSucceeded = true;
                    }
                    catch (Exception rollbackException)
                    {
                        ex = new AggregateException(ex, rollbackException);
                    }
                }

                var contextual = BuildBattlefieldConsoleCommitException(ex, unit, dirtyKind, stage, rollbackSucceeded, isBatch: false);
                var report = ApplicationErrorService.Report(contextual, "Battlefield console transaction", notify: false);
                _battlefieldUnsyncedDraftState = new BattlefieldUnsyncedDraftState
                {
                    ScenarioFileName = _currentBattlefieldDocument?.Scenario.FileName ?? string.Empty,
                    Stage = stage,
                    ErrorMessage = ex.GetBaseException().Message,
                    LogPath = report.LogPath,
                    DirtyKind = dirtyKind,
                    TargetKeys = [targetKey],
                    BeforeUnits = _battlefieldConsoleBeforeEditSnapshot == null ? [] : [CloneBattlefieldPlacedUnit(_battlefieldConsoleBeforeEditSnapshot)],
                    DraftUnits = [CloneBattlefieldPlacedUnit(unit)],
                    StatusDeltas = statusDelta == null ? [] : [CloneBattlefieldUnitStatusDelta(statusDelta)]
                };
                UpdateBattlefieldUnsyncedDraftActions();
                return ShowBattlefieldConsoleCommitResult(BuildBattlefieldConsoleCommitResult(
                    BattlefieldConsoleCommitStatus.WriteFailedRolledBack,
                    BuildBattlefieldConsoleFailureMessage(unit, stage, ex.GetBaseException().Message, report.LogPath, rollbackSucceeded),
                    targetKey,
                    dirtyKind,
                    allowsNavigation: false,
                    retainsDraft: true,
                    report.LogPath));
            }
            finally
            {
                _battlefieldConsoleCommitInProgress = false;
            }
        }
        catch (Exception ex)
        {
            var contextual = BuildBattlefieldConsoleCommitException(ex, unit, dirtyKind, stage, rollbackSucceeded: true, isBatch: false);
            var report = ApplicationErrorService.Report(contextual, "Battlefield console delta validation", notify: false);
            return ShowBattlefieldConsoleCommitResult(BuildBattlefieldConsoleCommitResult(
                BattlefieldConsoleCommitStatus.ValidationFailed,
                $"战场控制台：无法构建待提交差异：{ex.Message}\r\n日志：{report.LogPath}",
                targetKey,
                dirtyKind,
                allowsNavigation: false,
                retainsDraft: true,
                report.LogPath));
        }
    }

    private BattlefieldConsoleCommitResult BuildBattlefieldConsoleCommitResult(
        BattlefieldConsoleCommitStatus status,
        string message,
        string targetKey,
        BattlefieldConsoleDirtyKind dirtyKind,
        bool allowsNavigation,
        bool retainsDraft,
        string? logPath = null,
        Control? focusTarget = null)
        => new(status, message, targetKey, dirtyKind, allowsNavigation, retainsDraft, logPath, focusTarget);

    private BattlefieldConsoleCommitResult ShowBattlefieldConsoleCommitResult(BattlefieldConsoleCommitResult result)
    {
        SetStatus(result.Message.Replace("\r\n", " "));
        if (result.Status is BattlefieldConsoleCommitStatus.ValidationFailed or
            BattlefieldConsoleCommitStatus.WriteFailedRolledBack or
            BattlefieldConsoleCommitStatus.BatchWriteFailedRolledBack)
        {
            _battlefieldConsoleStatusPreviewBox.Text = result.Message;
        }

        if (result.FocusTarget != null && result.FocusTarget.CanFocus)
        {
            BeginInvoke(new Action(() => result.FocusTarget.Focus()));
        }

        return result;
    }

    private IDisposable SuppressBattlefieldScriptSelectionCommit()
    {
        var previous = _suppressBattlefieldScriptSelectionCommit;
        _suppressBattlefieldScriptSelectionCommit = true;
        return new BattlefieldSelectionCommitScope(() => _suppressBattlefieldScriptSelectionCommit = previous);
    }

    private sealed class BattlefieldSelectionCommitScope(Action dispose) : IDisposable
    {
        private Action? _dispose = dispose;
        public void Dispose() => Interlocked.Exchange(ref _dispose, null)?.Invoke();
    }

    private Exception BuildBattlefieldConsoleCommitException(
        Exception exception,
        BattlefieldPlacedUnit unit,
        BattlefieldConsoleDirtyKind dirtyKind,
        string stage,
        bool rollbackSucceeded,
        bool isBatch)
    {
        var scenario = _currentBattlefieldDocument?.Scenario.FileName ?? "<none>";
        var message =
            $"Battlefield console transaction failed. " +
            $"scenario={scenario}; target={unit.TargetKey}; person={unit.PersonId}; " +
            $"grid=({unit.GridX},{unit.GridY}); mode={(isBatch ? "batch" : "single")}; " +
            $"dirty={dirtyKind}; stage={stage}; rollbackSucceeded={rollbackSucceeded}.";
        return new InvalidOperationException(message, exception);
    }

    private static string BuildBattlefieldConsoleFailureMessage(
        BattlefieldPlacedUnit unit,
        string stage,
        string reason,
        string logPath,
        bool rollbackSucceeded)
    {
        var rollback = rollbackSucceeded
            ? "S 剧本树已恢复到本次编辑前。"
            : "S 剧本树回滚也发生异常，请不要保存并查看日志。";
        var log = string.IsNullOrWhiteSpace(logPath) ? "日志写入失败。" : "日志：" + logPath;
        return
            $"战场控制台：写回失败。\r\n" +
            $"目标：{unit.Name}({unit.PersonId})，{unit.TargetKey}\r\n" +
            $"阶段：{stage}\r\n" +
            $"原因：{reason}\r\n" +
            $"{rollback}\r\n" +
            $"地图和控制台值已保留为未同步草稿，可重试同步或恢复脚本值。\r\n" +
            log;
    }

    private void UpdateBattlefieldUnsyncedDraftActions()
    {
        var visible = _battlefieldUnsyncedDraftState != null;
        _battlefieldRetryUnsyncedDraftButton.Visible = visible;
        _battlefieldRestoreScriptValuesButton.Visible = visible;
        _battlefieldDetachUnsyncedDraftButton.Visible = visible;
        _battlefieldRetryUnsyncedDraftButton.Enabled = visible && !_battlefieldConsoleCommitInProgress;
        _battlefieldRestoreScriptValuesButton.Enabled = visible && !_battlefieldConsoleCommitInProgress;
        _battlefieldDetachUnsyncedDraftButton.Enabled = visible && !_battlefieldConsoleCommitInProgress;
    }

    private void RetryBattlefieldUnsyncedDraft()
    {
        var state = _battlefieldUnsyncedDraftState;
        if (state == null) return;
        if (state.IsBatch)
        {
            RetryBattlefieldBatchUnsyncedDraft(state);
            return;
        }

        var targetKey = state.TargetKeys.FirstOrDefault() ?? string.Empty;
        var unit = _battlefieldPlacedUnits.FirstOrDefault(candidate =>
            candidate.TargetKey.Equals(targetKey, StringComparison.OrdinalIgnoreCase));
        if (unit == null)
        {
            SetStatus("战场控制台：未同步草稿目标已不存在，不能重试。可选择解除脚本绑定后保留为本地草稿。");
            return;
        }

        _selectedBattlefieldPlacedUnit = unit;
        _battlefieldConsoleDirty = true;
        _battlefieldConsoleDirtyTargetKey = unit.TargetKey;
        _battlefieldConsoleDirtyKind = state.DirtyKind;
        _battlefieldConsoleBeforeEditSnapshot = state.BeforeUnits.FirstOrDefault() is { } before
            ? CloneBattlefieldPlacedUnit(before)
            : null;
        var result = TryCommitPendingBattlefieldConsoleChangesResult(finalizeBatchTransaction: true);
        if (result.Success)
        {
            SetStatus("战场控制台：未同步草稿已重试成功，尚未完整保存到文件。");
        }
    }

    private void RetryBattlefieldBatchUnsyncedDraft(BattlefieldUnsyncedDraftState state)
    {
        if (_project == null || _currentBattlefieldLegacyScriptDocument == null)
        {
            SetStatus("战场批量编辑：当前没有可写 S 剧本树，不能重试未同步草稿。");
            return;
        }

        var units = state.TargetKeys
            .Select(targetKey => _battlefieldPlacedUnits.FirstOrDefault(unit =>
                unit.TargetKey.Equals(targetKey, StringComparison.OrdinalIgnoreCase)))
            .Where(unit => unit != null)
            .Cast<BattlefieldPlacedUnit>()
            .ToList();
        if (units.Count != state.TargetKeys.Count)
        {
            SetStatus("战场批量编辑：部分未同步目标已不存在，不能进行整批重试。可恢复脚本值或解除绑定。");
            return;
        }

        var beforeEdit = CaptureLegacyScenarioHistorySnapshot(
            LegacyScriptEditorScope.Battlefield,
            _currentBattlefieldLegacyScriptDocument);
        try
        {
            var writablePlacements = units.Where(BattlefieldDeploymentWriteService.IsScriptPlacementWritable).ToList();
            if (writablePlacements.Count > 0)
            {
                _battlefieldDeploymentWriteService.ApplyScriptPlacements(
                    _currentBattlefieldLegacyScriptDocument,
                    writablePlacements);
            }
            if (state.OldPlacements.Count > 0)
            {
                _battlefieldDeploymentWriteService.ClearFriendEnemyScriptPlacements(
                    _currentBattlefieldLegacyScriptDocument,
                    state.OldPlacements);
            }
            foreach (var delta in state.StatusDeltas)
            {
                if (BattlefieldConsoleDeltaHasChanges(delta))
                {
                    _battlefieldUnitStatusWriteService.Apply(
                        _project,
                        _currentBattlefieldLegacyScriptDocument,
                        CloneBattlefieldUnitStatusDelta(delta));
                }
            }

            PushLegacyScenarioUndoSnapshot(LegacyScriptEditorScope.Battlefield, beforeEdit);
            MarkLegacyScriptStructureDirty(LegacyScriptEditorScope.Battlefield);
            var changed = units.Select(unit => FindBattlefieldDeploymentSourceCommand(unit.TargetKey)).FirstOrDefault(command => command != null);
            using (SuppressBattlefieldScriptSelectionCommit())
            {
                RefreshBattlefieldLegacyScriptView(changed);
            }
            _battlefieldUnsyncedDraftState = null;
            UpdateBattlefieldUnsyncedDraftActions();
            RefreshBattlefieldMapDynamicPreview();
            SetStatus($"战场批量编辑：未同步草稿已整批重试成功，共 {units.Count} 个目标，尚未完整保存到文件。");
        }
        catch (Exception ex)
        {
            var restored = CloneLegacyScenarioDocument(beforeEdit.Document);
            SetCurrentLegacyScriptDocument(LegacyScriptEditorScope.Battlefield, restored);
            using (SuppressBattlefieldScriptSelectionCommit())
            {
                RefreshBattlefieldLegacyScriptView(FindLegacyScenarioHistorySelection(restored, beforeEdit));
            }
            var representative = units.FirstOrDefault() ?? new BattlefieldPlacedUnit { Name = "批量目标" };
            var contextual = BuildBattlefieldConsoleCommitException(
                ex,
                representative,
                state.DirtyKind,
                "RetryBatch",
                rollbackSucceeded: true,
                isBatch: true);
            var report = ApplicationErrorService.Report(contextual, "Battlefield batch retry", notify: false);
            state.Failures.Add(ex.Message);
            _battlefieldConsoleStatusPreviewBox.Text =
                $"战场批量编辑：重试失败，S 剧本树已再次回滚。\r\n原因：{ex.Message}\r\n日志：{report.LogPath}";
            SetStatus("战场批量编辑：重试失败，S 剧本树已再次回滚。" + ex.Message);
        }
    }

    private void RestoreBattlefieldUnsyncedDraftFromScript()
    {
        var state = _battlefieldUnsyncedDraftState;
        if (state == null) return;
        var answer = MessageBox.Show(
            this,
            "将放弃当前未同步地图/控制台值，并从已回滚的 S 剧本树重新读取这些单位。是否继续？",
            "恢复脚本值",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Warning);
        if (answer != DialogResult.Yes) return;

        DiscardBattlefieldUnsyncedDraftFromScript();
        SetStatus("战场控制台：已放弃未同步草稿并恢复脚本事务前的地图/控制台值。");
    }

    private void DiscardBattlefieldUnsyncedDraftFromScript()
    {
        var state = _battlefieldUnsyncedDraftState;
        if (state == null) return;

        foreach (var targetKey in state.TargetKeys)
        {
            var draft = _battlefieldPlacedUnits.FirstOrDefault(unit =>
                unit.TargetKey.Equals(targetKey, StringComparison.OrdinalIgnoreCase));
            var source = state.BeforeUnits.FirstOrDefault(unit =>
                unit.TargetKey.Equals(targetKey, StringComparison.OrdinalIgnoreCase));
            if (draft == null || source == null) continue;
            CopyBattlefieldPlacedUnitValues(source, draft, includeTargetKey: true);
        }

        ClearBattlefieldConsoleDirty();
        _battlefieldUnsyncedDraftState = null;
        UpdateBattlefieldUnsyncedDraftActions();
        if (_selectedBattlefieldPlacedUnit != null)
        {
            SyncBattlefieldControlPanelFromPlacedUnit(_selectedBattlefieldPlacedUnit);
        }
        RefreshBattlefieldMapDynamicPreview();
    }

    private void DetachBattlefieldUnsyncedDraftFromScript()
    {
        var state = _battlefieldUnsyncedDraftState;
        if (state == null) return;
        var answer = MessageBox.Show(
            this,
            "将保留当前地图位置和属性，但解除这些草稿与失效 S 剧本记录的绑定。之后不会自动写入原记录。是否继续？",
            "保留本地草稿并解除绑定",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Warning);
        if (answer != DialogResult.Yes) return;

        DetachBattlefieldUnsyncedDraftFromScriptCore();
        SetStatus("战场控制台：未同步值已保留为项目侧草稿，并解除失效 S 剧本绑定。");
    }

    private void DetachBattlefieldUnsyncedDraftFromScriptCore()
    {
        var state = _battlefieldUnsyncedDraftState;
        if (state == null) return;

        foreach (var targetKey in state.TargetKeys)
        {
            var unit = _battlefieldPlacedUnits.FirstOrDefault(candidate =>
                candidate.TargetKey.Equals(targetKey, StringComparison.OrdinalIgnoreCase));
            if (unit == null) continue;
            unit.TargetKey = $"Placement#{state.ScenarioFileName}#{unit.GridX},{unit.GridY}#{unit.PersonId}#{Guid.NewGuid():N}";
            unit.Source = "未同步本地草稿(已解除脚本绑定)";
            unit.PlacementNote = BattlefieldUnitReviewService.AppendReviewLine(
                unit.PlacementNote,
                "写回失败后由用户确认解除失效 S 剧本绑定，保留为项目侧布阵草稿。");
        }

        ClearBattlefieldConsoleDirty();
        _battlefieldUnsyncedDraftState = null;
        UpdateBattlefieldUnsyncedDraftActions();
        _saveBattlefieldUnitReviewsButton.Enabled = true;
        RefreshBattlefieldMapDynamicPreview();
    }

    private static void CopyBattlefieldPlacedUnitValues(
        BattlefieldPlacedUnit source,
        BattlefieldPlacedUnit target,
        bool includeTargetKey)
    {
        if (includeTargetKey) target.TargetKey = source.TargetKey;
        target.PersonId = source.PersonId;
        target.PersonRawCode = source.PersonRawCode;
        target.Name = source.Name;
        target.JobId = source.JobId;
        target.JobName = source.JobName;
        target.SImageId = source.SImageId;
        target.RImageId = source.RImageId;
        target.Faction = source.Faction;
        target.LevelOffset = source.LevelOffset;
        target.LevelMode = source.LevelMode;
        target.AiMode = source.AiMode;
        target.Hidden = source.Hidden;
        target.Reinforcement = source.Reinforcement;
        target.Direction = source.Direction;
        target.GridX = source.GridX;
        target.GridY = source.GridY;
        target.Source = source.Source;
        target.PlacementNote = source.PlacementNote;
    }

    private static bool BattlefieldDeploymentOwnedFieldsDiffer(
        BattlefieldPlacedUnit before,
        BattlefieldPlacedUnit current)
        => before.PersonId != current.PersonId ||
           before.PersonRawCode != current.PersonRawCode ||
           before.GridX != current.GridX ||
           before.GridY != current.GridY ||
           before.Hidden != current.Hidden ||
           !before.AiMode.Equals(current.AiMode, StringComparison.Ordinal) ||
           !before.Direction.Equals(current.Direction, StringComparison.Ordinal);

    private static bool BattlefieldStatusDeploymentFieldsDiffer(
        BattlefieldPlacedUnit before,
        BattlefieldPlacedUnit current)
        => before.LevelOffset != current.LevelOffset ||
           !before.LevelMode.Equals(current.LevelMode, StringComparison.Ordinal);

    private bool CommitBattlefieldConsolePlacementChanges(BattlefieldPlacedUnit unit)
    {
        var wroteStatus = ApplyBattlefieldDeploymentStatusFieldsToCurrentScript(unit, "\u63a7\u5236\u53f0\u5ef6\u8fdf\u63d0\u4ea4");
        var wrotePlacement = ApplyBattlefieldPlacementToCurrentScript(unit, "\u63a7\u5236\u53f0\u5ef6\u8fdf\u63d0\u4ea4");
        if (_currentBattlefieldDocument != null)
        {
            _battlefieldMapPreviewSelectedUnit = GetSelectedBattlefieldUnitCandidate();
            RefreshBattlefieldMapDynamicPreview();
        }

        _saveBattlefieldUnitReviewsButton.Enabled = true;
        UpdateBattlefieldDeploymentWriteButton();
        return wroteStatus || wrotePlacement;
    }

    private BattlefieldConsoleDeltaBuildResult BuildBattlefieldConsolePendingDelta(BattlefieldPlacedUnit unit)
    {
        if (_project == null ||
            _currentBattlefieldLegacyScriptDocument == null ||
            _currentBattlefieldConsoleStatusDraft == null ||
            _currentBattlefieldConsoleDataDefaults == null)
        {
            return new BattlefieldConsoleDeltaBuildResult(
                BattlefieldConsoleDeltaBuildStatus.ValidationFailed,
                null,
                "战场控制台：当前项目、S 剧本或状态上下文不可用，已保留待提交编辑。");
        }

        if (!BattlefieldUnitStatusWriteService.IsWritableStatusTarget(unit))
        {
            return new BattlefieldConsoleDeltaBuildResult(
                BattlefieldConsoleDeltaBuildStatus.DraftOnly,
                null,
                "战场控制台：当前单位不是可写 Scene1 46/47 状态目标，状态值仅保留为草稿。");
        }

        if (!_currentBattlefieldConsoleStatusDraft.TargetKey.Equals(unit.TargetKey, StringComparison.OrdinalIgnoreCase))
        {
            return new BattlefieldConsoleDeltaBuildResult(
                BattlefieldConsoleDeltaBuildStatus.ValidationFailed,
                null,
                $"战场控制台：状态编辑绑定已过期，草稿目标={_currentBattlefieldConsoleStatusDraft.TargetKey}，当前目标={unit.TargetKey}。请重新选择该单位后重试。");
        }

        if (!_currentBattlefieldConsoleDataDefaults.Found)
        {
            return new BattlefieldConsoleDeltaBuildResult(
                BattlefieldConsoleDeltaBuildStatus.ValidationFailed,
                null,
                "战场控制台：Data.e5 默认值未读到，不能安全计算装备、兵种和能力差异，已保留待提交编辑。");
        }

        if (!_battlefieldConsoleAbilityGrid.EndEdit())
        {
            return new BattlefieldConsoleDeltaBuildResult(
                BattlefieldConsoleDeltaBuildStatus.ValidationFailed,
                null,
                "战场控制台：五维表格仍有未完成编辑，请先完成当前单元格输入。",
                _battlefieldConsoleAbilityGrid);
        }
        var boundary = ItemCategoryBoundaryService.Resolve(_project);
        var abilities = new Dictionary<int, (int Operation, int? Value)>();
        foreach (DataGridViewRow gridRow in _battlefieldConsoleAbilityGrid.Rows)
        {
            if (gridRow.DataBoundItem is not BattlefieldConsoleAbilityRow row) continue;
            int? value = null;
            if (!string.IsNullOrWhiteSpace(row.Value))
            {
                if (!int.TryParse(row.Value.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
                {
                    var rowIndex = gridRow.Index;
                    if (rowIndex >= 0 && _battlefieldConsoleAbilityGrid.Columns.Count > 2)
                    {
                        _battlefieldConsoleAbilityGrid.CurrentCell = _battlefieldConsoleAbilityGrid.Rows[rowIndex].Cells[2];
                        _battlefieldConsoleAbilityGrid.Rows[rowIndex].DefaultCellStyle.BackColor = Color.MistyRose;
                    }
                    return new BattlefieldConsoleDeltaBuildResult(
                        BattlefieldConsoleDeltaBuildStatus.ValidationFailed,
                        null,
                        $"战场控制台：{row.Name} 必须是整数，当前输入“{row.Value}”未提交。",
                        _battlefieldConsoleAbilityGrid);
                }
                value = parsed;
            }
            abilities[row.AbilityId] = (ConsoleTextToOperation(row.Operation), value);
        }

        var delta = _battlefieldUnitStatusWriteService.BuildDeltaDraftFromEffectiveValues(
            _currentBattlefieldConsoleStatusDraft,
            _currentBattlefieldConsoleDataDefaults,
            boundary,
            GetSelectedEquipmentLookupValue(_battlefieldConsoleWeaponCombo, _currentBattlefieldConsoleDataDefaults.WeaponId),
            (int)_battlefieldConsoleWeaponLevelInput.Value,
            GetSelectedEquipmentLookupValue(_battlefieldConsoleArmorCombo, _currentBattlefieldConsoleDataDefaults.ArmorId),
            (int)_battlefieldConsoleArmorLevelInput.Value,
            GetSelectedEquipmentLookupValue(_battlefieldConsoleAssistCombo, _currentBattlefieldConsoleDataDefaults.AssistId),
            GetSelectedLookupValue(_battlefieldConsoleJobCombo),
            abilities);
        return new BattlefieldConsoleDeltaBuildResult(
            BattlefieldConsoleDeltaHasChanges(delta)
                ? BattlefieldConsoleDeltaBuildStatus.Ready
                : BattlefieldConsoleDeltaBuildStatus.NoChanges,
            delta,
            BattlefieldConsoleDeltaHasChanges(delta)
                ? "战场控制台状态差异已生成。"
                : "战场控制台状态值没有变化。");
    }

    private bool CommitBattlefieldConsoleStatusChanges()
    {
        if (_project == null || _currentBattlefieldLegacyScriptDocument == null) return false;
        if (_selectedBattlefieldPlacedUnit == null) return false;
        var build = BuildBattlefieldConsolePendingDelta(_selectedBattlefieldPlacedUnit);
        var delta = build.Delta;
        if (delta == null)
        {
            ShowBattlefieldConsoleCommitResult(BuildBattlefieldConsoleCommitResult(
                BattlefieldConsoleCommitStatus.ValidationFailed,
                build.Message,
                _selectedBattlefieldPlacedUnit.TargetKey,
                BattlefieldConsoleDirtyKind.Equipment | BattlefieldConsoleDirtyKind.RuntimeAbility,
                allowsNavigation: false,
                retainsDraft: true,
                focusTarget: build.FocusTarget));
            return false;
        }
        if (!BattlefieldConsoleDeltaHasChanges(delta))
        {
            _battlefieldConsoleStatusPreviewBox.Text = "\u6218\u573a\u63a7\u5236\u53f0\uff1a\u5f53\u524d\u503c\u5df2\u4e0e Scene1/Data \u6709\u6548\u503c\u4e00\u81f4\u3002";
            return false;
        }

        var beforeEdit = CaptureLegacyScenarioHistorySnapshot(LegacyScriptEditorScope.Battlefield, _currentBattlefieldLegacyScriptDocument);
        var result = _battlefieldUnitStatusWriteService.Apply(_project, _currentBattlefieldLegacyScriptDocument, delta);
        PushLegacyScenarioUndoSnapshot(LegacyScriptEditorScope.Battlefield, beforeEdit);
        RefreshBattlefieldScriptAfterConsoleStatusWrite(delta);
        MarkLegacyScriptStructureDirty(LegacyScriptEditorScope.Battlefield);
        UpdateBattlefieldDeploymentWriteButton();
        ReloadBattlefieldConsoleStatusAfterScriptChange(delta.TargetKey);
        _battlefieldConsoleStatusPreviewBox.Text = BuildBattlefieldUnitStatusWriteDetail(result);
        var wroteEquipment = delta.Weapon.HasValue || delta.WeaponLevel.HasValue || delta.Armor.HasValue || delta.ArmorLevel.HasValue || delta.Assist.HasValue;
        var wroteRuntime = delta.JobId.HasValue || delta.Abilities.Any(ability => ability.Value.HasValue);
        SetStatus(wroteEquipment && wroteRuntime
            ? "\u5df2\u5199\u5165\u5de6\u4fa7 Scene1 S \u5267\u672c\u6811\uff0c\u5c1a\u672a\u5b8c\u6574\u4fdd\u5b58\u5230\u6587\u4ef6\u3002\u6218\u573a\u80fd\u529b\u5757\u4e5f\u5df2\u751f\u6210\u3002"
            : "\u5df2\u5199\u5165\u5de6\u4fa7 Scene1 S \u5267\u672c\u6811\uff0c\u5c1a\u672a\u5b8c\u6574\u4fdd\u5b58\u5230\u6587\u4ef6\u3002");
        return true;
    }

    private void RefreshBattlefieldScriptAfterConsoleStatusWrite(BattlefieldUnitStatusDraft delta)
    {
        var changed = FindBattlefieldUnitStatusWritebackBlock(delta)
                      ?? FindBattlefieldDeploymentSourceCommand(delta.TargetKey);
        if (changed == null)
        {
            return;
        }

        if (!RefreshLegacyEditorCommandInPlace(LegacyScriptEditorScope.Battlefield, changed))
        {
            RefreshBattlefieldLegacyScriptView(changed);
        }
    }
    private static string OperationToConsoleText(int operation)
        => operation switch
        {
            1 => "+",
            2 => "-",
            _ => "="
        };

    private static int ConsoleTextToOperation(string? operation)
        => operation switch
        {
            "+" => 1,
            "-" => 2,
            _ => 0
        };

    private static string GetBattlefieldAbilityName(int abilityId)
        => abilityId switch
        {
            10 => "武力",
            11 => "统帅",
            12 => "智力",
            13 => "敏捷",
            14 => "运气",
            _ => abilityId.ToString(CultureInfo.InvariantCulture)
        };

    private static string FormatNullableBattlefieldInt(int? value)
        => value?.ToString(CultureInfo.InvariantCulture) ?? "?";

    private static void SelectBattlefieldComboValue(ComboBox combo, int? value)
    {
        for (var i = 0; i < combo.Items.Count; i++)
        {
            if (combo.Items[i] is BattlefieldUnitStatusLookupItem item && item.Value == value)
            {
                combo.SelectedIndex = i;
                return;
            }
        }

        if (value.HasValue)
        {
            combo.Items.Add(new BattlefieldUnitStatusLookupItem { Value = value.Value, Text = $"ID{value.Value.ToString(CultureInfo.InvariantCulture)} 自定义" });
            combo.SelectedIndex = combo.Items.Count - 1;
        }
        else
        {
            combo.SelectedIndex = combo.Items.Count > 0 ? 0 : -1;
        }
    }

    private void ApplyBattlefieldConsoleStatusToSelectedUnit()
    {
        if (_bindingBattlefieldControlPanel) return;
        MarkBattlefieldConsoleStatusDirty(BattlefieldConsoleDirtyKind.Equipment | BattlefieldConsoleDirtyKind.RuntimeAbility);
        TryCommitPendingBattlefieldConsoleChanges();
    }
    private static int? GetSelectedLookupValue(ComboBox combo)
        => combo.SelectedItem is BattlefieldUnitStatusLookupItem item
            ? item.Value == int.MinValue ? null : item.Value
            : null;

    private static int? GetSelectedEquipmentLookupValue(ComboBox combo, int? dataDefaultItemId)
    {
        if (combo.SelectedItem is BattlefieldUnitStatusLookupItem item)
        {
            return item.Value == int.MinValue
                ? item.EquipmentDefaultItemId
                : item.Value;
        }

        return BattlefieldUnitDataDefaultService.NormalizeDataEquipmentId(dataDefaultItemId);
    }

    private void ReloadBattlefieldConsoleStatusAfterScriptChange(string preferredTargetKey)
    {
        if (string.IsNullOrWhiteSpace(preferredTargetKey))
        {
            return;
        }

        var unit = _battlefieldPlacedUnits.FirstOrDefault(item =>
            item.TargetKey.Equals(preferredTargetKey, StringComparison.OrdinalIgnoreCase));
        if (unit == null)
        {
            return;
        }

        _selectedBattlefieldPlacedUnit = unit;
        _bindingBattlefieldControlPanel = true;
        try
        {
            SyncBattlefieldControlPanelFromPlacedUnit(unit);
        }
        finally
        {
            _bindingBattlefieldControlPanel = false;
        }
    }

    private static bool BattlefieldConsoleDeltaHasChanges(BattlefieldUnitStatusDraft draft)
        => draft.LevelBonus.HasValue ||
           draft.JobLevel.HasValue ||
           draft.AiPolicy.HasValue ||
           draft.RemoveEquipmentOverride ||
           draft.RemoveJobOverride ||
           draft.RemoveAbilityOverrides.Count > 0 ||
           draft.Weapon.HasValue ||
           draft.WeaponLevel.HasValue ||
           draft.Armor.HasValue ||
           draft.ArmorLevel.HasValue ||
           draft.Assist.HasValue ||
           draft.JobId.HasValue ||
           draft.Abilities.Any(ability => ability.Value.HasValue);

    private static BattlefieldUnitStatusDraft MergeBattlefieldConsoleStatusDeltas(
        BattlefieldUnitStatusDraft? deployment,
        BattlefieldUnitStatusDraft status)
    {
        if (deployment == null) return status;
        deployment.Weapon = status.Weapon;
        deployment.WeaponLevel = status.WeaponLevel;
        deployment.Armor = status.Armor;
        deployment.ArmorLevel = status.ArmorLevel;
        deployment.Assist = status.Assist;
        deployment.JobId = status.JobId;
        deployment.RemoveEquipmentOverride = status.RemoveEquipmentOverride;
        deployment.RemoveJobOverride = status.RemoveJobOverride;
        deployment.RemoveAbilityOverrides.Clear();
        deployment.RemoveAbilityOverrides.AddRange(status.RemoveAbilityOverrides);
        deployment.Abilities.Clear();
        foreach (var ability in status.Abilities)
        {
            deployment.Abilities.Add(new BattlefieldUnitAbilityDraft
            {
                AbilityId = ability.AbilityId,
                Name = ability.Name,
                Operation = ability.Operation,
                Value = ability.Value,
                HasCommand = ability.HasCommand,
                DataDefaultValue = ability.DataDefaultValue,
                RemoveOverride = ability.RemoveOverride
            });
        }
        return deployment;
    }

    private static BattlefieldUnitStatusDraft CloneBattlefieldUnitStatusDelta(BattlefieldUnitStatusDraft source)
    {
        var clone = new BattlefieldUnitStatusDraft
        {
            TargetKey = source.TargetKey,
            ScenarioFileName = source.ScenarioFileName,
            PersonId = source.PersonId,
            PersonName = source.PersonName,
            CommandId = source.CommandId,
            RecordIndex = source.RecordIndex,
            LevelBonus = source.LevelBonus,
            JobLevel = source.JobLevel,
            AiPolicy = source.AiPolicy,
            Weapon = source.Weapon,
            WeaponLevel = source.WeaponLevel,
            Armor = source.Armor,
            ArmorLevel = source.ArmorLevel,
            Assist = source.Assist,
            JobId = source.JobId,
            HasEquipmentCommand = source.HasEquipmentCommand,
            HasJobCommand = source.HasJobCommand,
            DataDefaults = source.DataDefaults,
            RemoveEquipmentOverride = source.RemoveEquipmentOverride,
            RemoveJobOverride = source.RemoveJobOverride,
            EquipmentBoundarySummary = source.EquipmentBoundarySummary,
            SourceSummary = source.SourceSummary,
            CommandPreview = source.CommandPreview
        };
        clone.RemoveAbilityOverrides.AddRange(source.RemoveAbilityOverrides);
        clone.Abilities.Clear();
        foreach (var ability in source.Abilities)
        {
            clone.Abilities.Add(new BattlefieldUnitAbilityDraft
            {
                AbilityId = ability.AbilityId,
                Name = ability.Name,
                Operation = ability.Operation,
                Value = ability.Value,
                HasCommand = ability.HasCommand,
                DataDefaultValue = ability.DataDefaultValue,
                RemoveOverride = ability.RemoveOverride
            });
        }
        return clone;
    }

    private sealed class BattlefieldConsoleAbilityRow
    {
        public int AbilityId { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Operation { get; set; } = "=";
        public string Value { get; set; } = string.Empty;
        public string DataDefault { get; set; } = string.Empty;
        public string Source { get; set; } = string.Empty;
    }

    private static void SelectComboText(ComboBox combo, string text)
    {
        for (var i = 0; i < combo.Items.Count; i++)
        {
            if (!string.Equals(combo.Items[i]?.ToString(), text, StringComparison.Ordinal)) continue;
            combo.SelectedIndex = i;
            return;
        }
    }

    private void RemoveSelectedBattlefieldPlacedUnit()
    {
        if (_selectedBattlefieldPlacedUnit == null) return;
        var selected = _selectedBattlefieldPlacedUnit;
        var targetKey = selected.TargetKey;
        _battlefieldPlacedUnits.Remove(selected);
        _batchEditingBattlefieldTargetKeys.Remove(targetKey);
        ClearBattlefieldPlacedUnitSelection();
        ClearBattlefieldInstructionPreviewForTarget(targetKey);
        var scriptCleared = ClearBattlefieldFriendEnemyPlacementsFromCurrentScript(new[] { selected }, "移除摆放");
        if (_currentBattlefieldDocument != null)
        {
            _battlefieldMapPreviewSelectedUnit = null;
            RefreshBattlefieldMapDynamicPreview();
        }
        _saveBattlefieldUnitReviewsButton.Enabled = true;
        UpdateBattlefieldDeploymentWriteButton();
        SetStatus(scriptCleared
            ? "战场布阵：已移除选中单位，并清空左侧 S 剧本树中的对应 46/47 友/敌军出场槽；尚未完整保存。"
            : "战场布阵：已移除选中单位；未绑定 46/47 友/敌军出场槽，S 剧本树不改。");
    }

    private void ClearBattlefieldPlacedUnits()
    {
        if (_battlefieldPlacedUnits.Count == 0) return;
        var scriptPlacements = _battlefieldPlacedUnits
            .Where(BattlefieldDeploymentWriteService.IsFriendOrEnemyScriptPlacementWritable)
            .ToList();
        if (MessageBox.Show(this,
                scriptPlacements.Count > 0
                    ? $"将清空当前关卡的地图摆放草稿，并清空左侧 S 剧本树中 {scriptPlacements.Count} 个已绑定 46/47 叀敌军出场槽。\r\n\r\n0x4B 我军出战位和本地草稿只从地图预览移除，不攀S 剧本。是否继续？"
                    : "将清空当前关卡的地图摆放草稿；未发现已绑宀46/47 叀敌军出场槽，S 剧本树不改。是否继续？",
                "确认清空摆放",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question) != DialogResult.Yes)
        {
            return;
        }

        _battlefieldPlacedUnits.Clear();
        ClearBattlefieldBatchEditingState(syncControls: false);
        ClearBattlefieldPlacedUnitSelection();
        ClearBattlefieldInstructionPreviewState();
        var scriptCleared = ClearBattlefieldFriendEnemyPlacementsFromCurrentScript(scriptPlacements, "清空摆放");
        if (_currentBattlefieldDocument != null)
        {
            BindBattlefieldUnitCandidates(GetBattlefieldUnitCandidatesForDisplay());
            BindBattlefieldCommandCandidates(GetBattlefieldCommandCandidatesForDisplay());
            RefreshBattlefieldScriptPreviewTree();
            _battlefieldMapPreviewSelectedUnit = null;
            RefreshBattlefieldMapDynamicPreview();
        }
        _saveBattlefieldUnitReviewsButton.Enabled = true;
        UpdateBattlefieldDeploymentWriteButton();
        SetStatus(scriptCleared
            ? $"战场布阵：已清空摆放，并清空左侧 S 剧本树中的 {scriptPlacements.Count} 个 46/47 友/敌军出场槽；尚未完整保存。"
            : "战场布阵：已清空摆放；没有可清空的 46/47 友/敌军出场槽。");
    }

    private bool ClearBattlefieldFriendEnemyPlacementsFromCurrentScript(IReadOnlyList<BattlefieldPlacedUnit> placements, string action)
    {
        if (placements.Count == 0 || _currentBattlefieldDocument == null)
        {
            return false;
        }

        var scriptPlacements = placements
            .Where(BattlefieldDeploymentWriteService.IsFriendOrEnemyScriptPlacementWritable)
            .ToList();
        if (scriptPlacements.Count == 0)
        {
            return false;
        }

        if (_currentBattlefieldLegacyScriptDocument == null)
        {
            SetStatus($"战场布阵：{action}没有可写入的左侧 S 剧本完整树，仅移除地图摆放草稿。");
            return false;
        }

        var beforeEdit = CaptureLegacyScenarioHistorySnapshot(LegacyScriptEditorScope.Battlefield, _currentBattlefieldLegacyScriptDocument);
        BattlefieldDeploymentWriteResult result;
        try
        {
            result = _battlefieldDeploymentWriteService.ClearFriendEnemyScriptPlacements(
                _currentBattlefieldLegacyScriptDocument,
                scriptPlacements);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Clear battlefield placement from current S tree failed: {ex}");
            SetStatus($"战场布阵：{action}未能清空左侧 S 剧本树中的 46/47 出场槽：{ex.Message}；仅移除地图摆放草稿。");
            return false;
        }

        PushLegacyScenarioUndoSnapshot(LegacyScriptEditorScope.Battlefield, beforeEdit);
        foreach (var placement in scriptPlacements)
        {
            ClearBattlefieldInstructionPreviewForTarget(placement.TargetKey);
        }

        var changed = FindBattlefieldDeploymentSourceCommand(scriptPlacements[0].TargetKey);
        RefreshBattlefieldLegacyScriptView(changed);
        RefreshBattlefieldDocumentFromLegacyScript(scriptPlacements[0].TargetKey);
        MarkLegacyScriptStructureDirty(LegacyScriptEditorScope.Battlefield);
        UpdateBattlefieldDeploymentWriteButton();

        var summary = result.Changes.Count == 1
            ? result.Changes[0].Summary
            : $"清空 {result.Changes.Count} 个 46/47 友/敌军出场槽";
        SetStatus($"战场布阵：{action}已同步到左侧 S 剧本树：{summary}，尚未完整保存。");
        return true;
    }

    private void ClearBattlefieldInstructionPreviewForTarget(string targetKey)
    {
        if (string.IsNullOrWhiteSpace(targetKey)) return;
        var commandKey = string.Empty;
        if (TryParseBattlefieldTargetKey(targetKey, out var scene, out var section, out var command, out var offsetHex, out var commandIdHex, out _))
        {
            commandKey = BuildBattlefieldCommandPreviewKey(scene, section, command, offsetHex, commandIdHex);
        }

        _battlefieldUnitCandidatePreviewOverrides.Remove(targetKey);
        if (!string.IsNullOrWhiteSpace(commandKey))
        {
            _battlefieldCommandCandidatePreviewOverrides.Remove(commandKey);
        }
        _battlefieldScriptPreviewPlacementsByTargetKey.Remove(targetKey);

        if (_currentBattlefieldDocument == null) return;
        BindBattlefieldUnitCandidates(GetBattlefieldUnitCandidatesForDisplay());
        BindBattlefieldCommandCandidates(GetBattlefieldCommandCandidatesForDisplay());
        RefreshBattlefieldScriptPreviewTree();
    }

    private void LoadBattlefieldUnitPalette()
    {
        _battlefieldUnitPaletteItems = Array.Empty<BattlefieldUnitPaletteItem>();
        if (_project == null || _tables.Count == 0)
        {
            BindBattlefieldUnitPalette(_battlefieldUnitPaletteItems);
            return;
        }

        try
        {
            var personTable = FindTable(_tables, "6.5-0 人物");
            var rTable = FindTable(_tables, "6.5-0-4 R形象");
            var sTable = FindTable(_tables, "6.5-0-5 S形象");
            var jobTable = FindTable(_tables, "6.5-4 详细兵种");
            var personRead = _tableReader.Read(_project, personTable, _tables);
            var rRead = _tableReader.Read(_project, rTable, _tables);
            var sRead = _tableReader.Read(_project, sTable, _tables);
            var jobRead = _tableReader.Read(_project, jobTable, _tables);
            if (!personRead.Validation.IsUsable || !rRead.Validation.IsUsable || !sRead.Validation.IsUsable)
            {
                BindBattlefieldUnitPalette(_battlefieldUnitPaletteItems);
                return;
            }

            var jobNames = new Dictionary<int, string>();
            if (jobRead.Validation.IsUsable)
            {
                foreach (DataRow jobRow in jobRead.Data.Rows)
                {
                    if (!jobRead.Data.Columns.Contains("ID") || !jobRead.Data.Columns.Contains("名称")) break;
                    var jobId = Convert.ToInt32(jobRow["ID"], CultureInfo.InvariantCulture);
                    var jobName = Convert.ToString(jobRow["名称"], CultureInfo.InvariantCulture) ?? string.Empty;
                    jobNames[jobId] = string.IsNullOrWhiteSpace(jobName) ? $"职业{jobId}" : jobName;
                }
            }

            var count = Math.Min(personRead.Data.Rows.Count, Math.Min(rRead.Data.Rows.Count, sRead.Data.Rows.Count));
            var rows = new List<BattlefieldUnitPaletteItem>();
            for (var i = 0; i < count; i++)
            {
                var personRow = personRead.Data.Rows[i];
                var id = personRead.Data.Columns.Contains("ID")
                    ? Convert.ToInt32(personRow["ID"], CultureInfo.InvariantCulture)
                    : i;
                var name = personRead.Data.Columns.Contains("名称")
                    ? Convert.ToString(personRow["名称"], CultureInfo.InvariantCulture) ?? string.Empty
                    : string.Empty;
                var jobId = personRead.Data.Columns.Contains("职业")
                    ? Convert.ToInt32(personRow["职业"], CultureInfo.InvariantCulture)
                    : (int?)null;
                var rId = Convert.ToInt32(rRead.Data.Rows[i]["R形象编号"], CultureInfo.InvariantCulture);
                var sId = Convert.ToInt32(sRead.Data.Rows[i]["S形象编号"], CultureInfo.InvariantCulture);
                rows.Add(new BattlefieldUnitPaletteItem
                {
                    Index = rows.Count + 1,
                    PersonId = id,
                    Name = string.IsNullOrWhiteSpace(name) ? $"人物{id}" : name,
                    JobId = jobId,
                    JobName = jobId.HasValue ? jobNames.GetValueOrDefault(jobId.Value, $"职业{jobId.Value}") : string.Empty,
                    RImageId = rId,
                    SImageId = sId
                });
            }

            _battlefieldUnitPaletteItems = rows;
            BindBattlefieldUnitPalette(rows);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine("读取战场角色列表失败：" + ex.Message);
            BindBattlefieldUnitPalette(_battlefieldUnitPaletteItems);
        }
    }

    private void MergeBattlefieldScriptPlacements(BattlefieldEditorDocument document)
    {
        RemoveScriptBackedBattlefieldPlacementDuplicates();
        var existingTargets = _battlefieldPlacedUnits
            .Where(x => !string.IsNullOrWhiteSpace(x.TargetKey))
            .Select(x => x.TargetKey)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var occupiedGrids = _battlefieldPlacedUnits
            .Select(x => BuildBattlefieldGridKey(x.GridX, x.GridY))
            .ToHashSet(StringComparer.Ordinal);
        var paletteByPersonId = _battlefieldUnitPaletteItems
            .GroupBy(x => x.PersonId)
            .ToDictionaryFirstByKey(group => group.Key, group => group.First());
        var previewFilter = _battlefieldDeploymentPreviewFilterCombo.SelectedItem?.ToString() ?? "初始部署";
        var selectedTargetKey = GetSelectedBattlefieldUnitCandidate()?.TargetKey ?? string.Empty;

        foreach (var state in document.DeploymentRecords)
        {
            if (state.IsAllySlot) continue;
            if (!ShouldMergeBattlefieldDeploymentRecord(state, previewFilter, selectedTargetKey)) continue;
            if (state.IsBlank) continue;
            if (state.GridX < 0 || state.GridY < 0) continue;
            if (state.PersonId < 0) continue;

            var personId = state.PersonId;
            var targetKey = string.IsNullOrWhiteSpace(state.TargetKey)
                ? $"ScriptPlacement#{document.Scenario.FileName}#{state.CommandIndex}_{state.RecordIndex}"
                : state.TargetKey;
            if (existingTargets.Contains(targetKey)) continue;

            var gridKey = BuildBattlefieldGridKey(state.GridX, state.GridY);
            if (occupiedGrids.Contains(gridKey)) continue;

            paletteByPersonId.TryGetValue(personId, out var palette);
            _battlefieldPlacedUnits.Add(new BattlefieldPlacedUnit
            {
                TargetKey = targetKey,
                PersonId = personId,
                PersonRawCode = state.PersonRawCode,
                Name = palette?.Name ?? $"人物{personId}",
                JobId = palette?.JobId,
                JobName = palette?.JobName ?? string.Empty,
                RImageId = palette?.RImageId ?? 0,
                SImageId = palette?.SImageId ?? 0,
                Faction = GetBattlefieldFactionForCommandId(state.CommandId),
                LevelOffset = state.LevelOffset,
                LevelMode = string.IsNullOrWhiteSpace(state.JobLevel) ? "初级" : state.JobLevel,
                AiMode = string.IsNullOrWhiteSpace(state.AiMode) ? "被动" : state.AiMode,
                Hidden = state.Hidden,
                Reinforcement = state.Reinforcement,
                Direction = state.Direction,
                GridX = state.GridX,
                GridY = state.GridY,
                Source = "S剧本预览",
                PlacementNote = BuildBattlefieldScriptPlacementNote(state)
            });
            existingTargets.Add(targetKey);
            occupiedGrids.Add(gridKey);
        }
    }

    private bool CommitBattlefieldConsoleTransaction(
        BattlefieldPlacedUnit unit,
        BattlefieldUnitStatusDraft? statusDelta,
        bool applyPlacement,
        ref string stage)
    {
        if (_project == null || _currentBattlefieldLegacyScriptDocument == null) return false;

        var wrotePlacement = false;
        BattlefieldUnitStatusWriteResult? statusResult = null;
        if (applyPlacement)
        {
            stage = "ApplyPlacement";
            BattlefieldConsoleCommitStageInterceptForSmoke?.Invoke(stage);
            if (BattlefieldUnitStatusWriteService.IsWritableStatusTarget(unit))
            {
                var bound = _battlefieldUnitStatusWriteService.LoadDraft(
                    _currentBattlefieldLegacyScriptDocument,
                    _currentBattlefieldDocument?.Scenario.FileName ?? string.Empty,
                    unit);
                if (bound.PersonId != 0 && bound.PersonId != unit.PersonId)
                {
                    throw new InvalidOperationException(
                        $"绑定的出场记录人物已变化：地图草稿人物={unit.PersonId}，当前 S 剧本 Record 人物={bound.PersonId}。禁止覆盖其他人物。");
                }
            }
            else if (TryGetBattlefieldDeploymentRecordState(unit.TargetKey) is { } boundState &&
                     boundState.CommandId != 0x4B &&
                     boundState.PersonId >= 0 &&
                     boundState.PersonId != unit.PersonId)
            {
                throw new InvalidOperationException(
                    $"绑定的出场记录人物已变化：地图草稿人物={unit.PersonId}，当前 S 剧本 Record 人物={boundState.PersonId}。禁止覆盖其他人物。");
            }
            var placementResult = _battlefieldDeploymentWriteService.ApplyScriptPlacements(
                _currentBattlefieldLegacyScriptDocument,
                new[] { unit });
            wrotePlacement = placementResult.Changes.Count > 0;
        }

        if (statusDelta != null && BattlefieldConsoleDeltaHasChanges(statusDelta))
        {
            stage = "ApplyStatus";
            BattlefieldConsoleCommitStageInterceptForSmoke?.Invoke(stage);
            statusResult = _battlefieldUnitStatusWriteService.Apply(
                _project,
                _currentBattlefieldLegacyScriptDocument,
                statusDelta);
        }

        var committed = wrotePlacement || statusResult != null;
        if (!committed) return false;

        var changed = statusDelta == null
            ? FindBattlefieldDeploymentSourceCommand(unit.TargetKey)
            : FindBattlefieldUnitStatusWritebackBlock(statusDelta) ?? FindBattlefieldDeploymentSourceCommand(unit.TargetKey);
        stage = "RefreshTree";
        if (changed != null && !RefreshLegacyEditorCommandInPlace(LegacyScriptEditorScope.Battlefield, changed))
        {
            using (SuppressBattlefieldScriptSelectionCommit())
            {
                RefreshBattlefieldLegacyScriptView(changed);
            }
        }

        ClearBattlefieldInstructionPreviewForTarget(unit.TargetKey);
        RefreshBattlefieldInstructionPreviewBindings(unit.TargetKey);
        UpdateBattlefieldDeploymentWriteButton();
        if (statusDelta != null)
        {
            ReloadBattlefieldConsoleStatusAfterScriptChange(statusDelta.TargetKey);
        }
        if (statusResult != null)
        {
            _battlefieldConsoleStatusPreviewBox.Text = BuildBattlefieldUnitStatusWriteDetail(statusResult);
        }
        _battlefieldMapPreviewSelectedUnit = GetSelectedBattlefieldUnitCandidate();
        RefreshBattlefieldMapDynamicPreview();
        _saveBattlefieldUnitReviewsButton.Enabled = true;
        stage = "Complete";
        return true;
    }

    private BattlefieldDeploymentRecordState? TryGetBattlefieldDeploymentRecordState(string targetKey)
    {
        if (_currentBattlefieldDocument == null || _currentBattlefieldLegacyScriptDocument == null) return null;
        var rebuilt = BattlefieldEditorService.RebuildFromLegacyDocument(
            _currentBattlefieldDocument,
            _currentBattlefieldLegacyScriptDocument,
            _project,
            _tables);
        return rebuilt.DeploymentRecords.FirstOrDefault(state =>
            state.TargetKey.Equals(targetKey, StringComparison.OrdinalIgnoreCase));
    }

    private BattlefieldUnitStatusDraft? BuildBattlefieldConsoleDeploymentDelta(BattlefieldPlacedUnit unit)
    {
        if (_project == null || _currentBattlefieldLegacyScriptDocument == null) return null;
        var draft = _battlefieldUnitStatusWriteService.LoadDraft(
            _project,
            _tables,
            _currentBattlefieldLegacyScriptDocument,
            _currentBattlefieldDocument?.Scenario.FileName ?? string.Empty,
            unit);
        if (draft.PersonId != 0 && draft.PersonId != unit.PersonId)
        {
            throw new InvalidOperationException(
                $"绑定的出场记录人物已变化：地图草稿人物={unit.PersonId}，当前 S 剧本 Record 人物={draft.PersonId}。请重新读取或解除失效绑定，禁止覆盖其他人物。");
        }
        var requestedLevelBonus = unit.LevelOffset;
        var requestedJobLevel = MapBattlefieldJobLevel(unit.LevelMode);
        draft.LevelBonus = draft.LevelBonus == requestedLevelBonus ? null : requestedLevelBonus;
        draft.JobLevel = draft.JobLevel == requestedJobLevel ? null : requestedJobLevel;
        // AI/direction/hidden belong to BattlefieldDeploymentWriteService.
        draft.AiPolicy = null;
        draft.Weapon = null;
        draft.WeaponLevel = null;
        draft.Armor = null;
        draft.ArmorLevel = null;
        draft.Assist = null;
        draft.JobId = null;
        draft.RemoveEquipmentOverride = false;
        draft.RemoveJobOverride = false;
        draft.RemoveAbilityOverrides.Clear();
        foreach (var ability in draft.Abilities)
        {
            ability.Value = null;
            ability.Operation = null;
            ability.RemoveOverride = false;
        }
        return draft;
    }

    private void ResetBattlefieldDeploymentPreviewFilter()
    {
        if (_battlefieldDeploymentPreviewFilterCombo.Items.Count > 0)
        {
            _battlefieldDeploymentPreviewFilterCombo.SelectedIndex = 0;
        }
    }

    private void RefreshBattlefieldDeploymentPreviewFilter()
    {
        if (_currentBattlefieldDocument == null) return;

        var removedSelection = false;
        for (var index = _battlefieldPlacedUnits.Count - 1; index >= 0; index--)
        {
            var unit = _battlefieldPlacedUnits[index];
            if (!unit.Source.Equals("S剧本预览", StringComparison.Ordinal))
            {
                continue;
            }

            removedSelection |= ReferenceEquals(_selectedBattlefieldPlacedUnit, unit) ||
                                ReferenceEquals(_editingBattlefieldPlacedUnit, unit) ||
                                ReferenceEquals(_draggingBattlefieldPlacedUnit, unit);
            _battlefieldPlacedUnits.RemoveAt(index);
        }

        if (removedSelection)
        {
            ClearBattlefieldPlacedUnitSelection();
        }

        MergeBattlefieldScriptPlacements(_currentBattlefieldDocument);
        _battlefieldMapPreviewSelectedUnit = GetSelectedBattlefieldUnitCandidate();
        RefreshBattlefieldMapDynamicPreview();
    }

    private static bool ShouldMergeBattlefieldDeploymentRecord(
        BattlefieldDeploymentRecordState state,
        string previewFilter,
        string selectedTargetKey)
        => previewFilter switch
        {
            "全部部署记录" => true,
            "隐藏与援军" => state.Hidden || state.Reinforcement,
            "当前选中命令" => !string.IsNullOrWhiteSpace(selectedTargetKey) &&
                          state.TargetKey.Equals(selectedTargetKey, StringComparison.OrdinalIgnoreCase),
            _ => state.IsInitialDeployment
        };

    private void RemoveScriptBackedBattlefieldPlacementDuplicates()
    {
        if (_battlefieldPlacedUnits.Count <= 1) return;

        var scriptBackedByTarget = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var scriptBackedPersonKeys = new HashSet<string>(StringComparer.Ordinal);
        for (var index = _battlefieldPlacedUnits.Count - 1; index >= 0; index--)
        {
            var unit = _battlefieldPlacedUnits[index];
            if (!BattlefieldDeploymentWriteService.IsScriptPlacementWritable(unit)) continue;

            var targetDuplicate = !string.IsNullOrWhiteSpace(unit.TargetKey) && !scriptBackedByTarget.Add(unit.TargetKey);
            var personKey = BuildBattlefieldPlacementPersonKey(unit);
            var personDuplicate = unit.PersonId >= 0 && !scriptBackedPersonKeys.Add(personKey);
            if (!targetDuplicate && !personDuplicate) continue;

            if (ReferenceEquals(_selectedBattlefieldPlacedUnit, unit) ||
                ReferenceEquals(_editingBattlefieldPlacedUnit, unit) ||
                ReferenceEquals(_draggingBattlefieldPlacedUnit, unit))
            {
                ClearBattlefieldPlacedUnitSelection();
            }
            _battlefieldPlacedUnits.RemoveAt(index);
        }
    }

    private static string BuildBattlefieldPlacementPersonKey(BattlefieldPlacedUnit unit)
        => string.Join(
            "|",
            unit.PersonId.ToString(CultureInfo.InvariantCulture),
            unit.Faction,
            IsBattlefieldAllyDeploymentTargetKey(unit.TargetKey) ? "4B" : "Script");

    private void LoadBattlefieldAllyDeploymentSlots(ScenarioFileInfo scenario, SceneStringDocument? dictionary)
    {
        _battlefieldAllyDeploymentSlots = Array.Empty<BattlefieldAllyDeploymentSlot>();
        if (dictionary == null) return;

        try
        {
            _battlefieldAllyDeploymentSlots = _battlefieldAllyDeploymentSlotService.Load(
                scenario,
                dictionary,
                _battlefieldUnitPaletteItems);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine("读取我军候选出战位失败：" + ex.Message);
        }
    }

    private static bool IsBattlefieldAllyDeploymentPositionCandidate(BattlefieldUnitCandidate candidate)
        => candidate.Category.Contains("我军", StringComparison.Ordinal) &&
           candidate.SourceCommand.Contains("0x4B", StringComparison.OrdinalIgnoreCase);

    private static string BuildBattlefieldGridKey(int x, int y)
        => x.ToString(CultureInfo.InvariantCulture) + "," + y.ToString(CultureInfo.InvariantCulture);

    private static string GetBattlefieldFactionForCommandId(int commandId)
        => commandId switch
        {
            0x46 => "友军",
            0x47 => "敌军",
            _ => "我军"
        };

    private static string InferBattlefieldFaction(BattlefieldUnitCandidate candidate)
    {
        var text = string.Join(' ', candidate.Category, candidate.FactionHint, candidate.SourceCommand, candidate.Annotation);
        if (text.Contains("敌军", StringComparison.Ordinal)) return "敌军";
        if (text.Contains("友军", StringComparison.Ordinal)) return "友军";
        if (text.Contains("我军", StringComparison.Ordinal)) return "我军";
        return "我军";
    }

    private static string InferBattlefieldAiMode(string hint)
    {
        hint ??= string.Empty;
        if (hint.Contains("主动", StringComparison.Ordinal)) return "主动";
        if (hint.Contains("坚守", StringComparison.Ordinal)) return "坚守";
        if (hint.Contains("攻击", StringComparison.Ordinal)) return "攻击";
        if (hint.Contains("指定点", StringComparison.Ordinal) || hint.Contains("到点", StringComparison.Ordinal)) return "到点";
        if (hint.Contains("跟随", StringComparison.Ordinal)) return "跟随";
        if (hint.Contains("逃", StringComparison.Ordinal)) return "逃离";
        return "被动";
    }

    private static string InferBattlefieldLevelMode(string hint)
    {
        hint ??= string.Empty;
        if (hint.Contains("高级", StringComparison.Ordinal) || hint.Contains("高", StringComparison.Ordinal)) return "高级";
        if (hint.Contains("中级", StringComparison.Ordinal) || hint.Contains("中", StringComparison.Ordinal)) return "中级";
        return "初级";
    }

    private static int InferBattlefieldLevelOffset(string hint)
    {
        hint ??= string.Empty;
        if (!hint.Contains("等级", StringComparison.Ordinal) && !hint.Contains("经验", StringComparison.Ordinal))
        {
            return 0;
        }

        var match = Regex.Match(hint, @"[-+]?\d{1,3}");
        if (!match.Success ||
            !int.TryParse(match.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
        {
            return 0;
        }

        return Math.Clamp(value, -99, 99);
    }

    private static bool IsBattlefieldHiddenCandidate(BattlefieldUnitCandidate candidate)
    {
        var text = string.Join(' ', candidate.Category, candidate.SourceCommand, candidate.Annotation);
        return text.Contains("隐藏", StringComparison.Ordinal) ||
               text.Contains("伏兵", StringComparison.Ordinal);
    }

    private static string BuildBattlefieldScriptPlacementNote(BattlefieldDeploymentRecordState state)
        => $"S 剧本初始出场预加载：{HexDisplayFormatter.Format(state.CommandId, 2)} {state.CommandName} / Scene {state.SceneIndex} Section {state.SectionIndex} Cmd {state.CommandIndex} / {state.OffsetHex}\r\n" +
           $"人物：Person2原始码={state.PersonRawCode}，解析={state.PersonId} {state.PersonDisplay}\r\n" +
           $"坐标：({state.GridX},{state.GridY})；阵营：{GetBattlefieldFactionForCommandId(state.CommandId)}；朝向：{state.DirectionCode}({state.Direction})\r\n" +
           $"AI：{state.AiPolicyCode}({state.AiMode})；等级：{state.LevelOffset}；兵种级：{state.JobLevelCode}({state.JobLevel})；隐藏={(state.Hidden ? 1 : 0)}；援军={(state.Reinforcement ? 1 : 0)}\r\n" +
           "当前预览直接来自 46/47 结构化部署槽；写回时保留 Person2 剧本码体系。";

    private void BindBattlefieldUnitPalette(IEnumerable<BattlefieldUnitPaletteItem> rows)
    {
        var list = rows.ToList();
        var selectedPersonId = _selectedBattlefieldPaletteItem?.PersonId;
        _bindingBattlefieldUnitPalette = true;
        try
        {
            _battlefieldUnitListBox.DataSource = null;
            _battlefieldUnitListBox.DataSource = new BindingList<BattlefieldUnitPaletteItem>(list);
            _battlefieldUnitListBox.DisplayMember = nameof(BattlefieldUnitPaletteItem.DisplayText);
            if (list.Count == 0)
            {
                RefreshBattlefieldPaletteUnitPreview(null);
                return;
            }

            var selectedIndex = selectedPersonId.HasValue
                ? list.FindIndex(item => item.PersonId == selectedPersonId.Value)
                : 0;
            _battlefieldUnitListBox.SelectedIndex = Math.Max(0, selectedIndex);
        }
        finally
        {
            _bindingBattlefieldUnitPalette = false;
        }

        RefreshBattlefieldPaletteUnitPreview(_battlefieldUnitListBox.SelectedItem as BattlefieldUnitPaletteItem);
    }

    private void ApplyBattlefieldUnitPaletteFilter()
    {
        var keyword = _battlefieldUnitPaletteFilterBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(keyword))
        {
            BindBattlefieldUnitPalette(_battlefieldUnitPaletteItems);
            return;
        }

        BindBattlefieldUnitPalette(_battlefieldUnitPaletteItems.Where(item =>
            item.PersonId.ToString(CultureInfo.InvariantCulture).Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
            item.Name.Contains(keyword, StringComparison.CurrentCultureIgnoreCase) ||
            item.JobName.Contains(keyword, StringComparison.CurrentCultureIgnoreCase) ||
            item.SImageId.ToString(CultureInfo.InvariantCulture).Contains(keyword, StringComparison.OrdinalIgnoreCase)));
    }

    private void ShowSelectedBattlefieldPaletteUnit()
    {
        var item = _battlefieldUnitListBox.SelectedItem as BattlefieldUnitPaletteItem;
        RefreshBattlefieldPaletteUnitPreview(item);
        if (_bindingBattlefieldUnitPalette || item == null)
        {
            return;
        }

        if (!TryCommitPendingBattlefieldConsoleChanges())
        {
            return;
        }

        ShowBattlefieldPaletteConsolePreview(item);
    }

    private void ShowBattlefieldPaletteConsolePreview(BattlefieldUnitPaletteItem item)
    {
        _selectedBattlefieldPlacedUnit = null;
        _editingBattlefieldPlacedUnit = null;
        _currentBattlefieldConsoleStatusDraft = null;
        _currentBattlefieldConsoleDataDefaults = null;
        ClearBattlefieldConsoleDirty();

        var defaults = BuildBattlefieldPaletteDataDefaults(item);
        _currentBattlefieldConsoleDataDefaults = defaults;

        _bindingBattlefieldControlPanel = true;
        try
        {
            RemoveBattlefieldBatchMixedComboItems();
            _battlefieldHiddenCheckBox.ThreeState = false;
            _battlefieldFactionAllyRadio.Checked = true;
            _battlefieldHiddenCheckBox.Checked = false;
            _battlefieldLevelOffsetInput.Value = 0;
            SelectComboText(_battlefieldLevelModeCombo, "初级");
            SelectComboText(_battlefieldAiModeCombo, "被动");
            SelectComboText(_battlefieldDirectionCombo, "下");

            var draft = new BattlefieldUnitStatusDraft
            {
                PersonId = defaults.PersonId,
                PersonName = defaults.PersonName,
                DataDefaults = defaults
            };
            PopulateBattlefieldConsoleStatusEditors(draft, defaults);
            SetBattlefieldConsolePlacementControlsEnabled(false);
            SetBattlefieldConsoleStatusControlsEnabled(false);
        }
        finally
        {
            _bindingBattlefieldControlPanel = false;
        }

        _battlefieldDataDefaultsBox.Text = BuildBattlefieldDataDefaultsText(defaults);
        _battlefieldConsoleStatusPreviewBox.Text = defaults.Found
            ? "角色列表只读预览：显示 Data.e5 默认装备、兵种与五维；未绑定 S 剧本出场槽。"
            : $"角色列表只读预览：{defaults.Source}";
        var summary =
            $"角色默认预览：{item.PersonId} {item.Name}  职业={item.JobId?.ToString(CultureInfo.InvariantCulture) ?? "?"} {item.JobName}  R={item.RImageId}  S={item.SImageId}";
        SetBattlefieldConsolePreview(summary);
        SetStatus("战场控制台：已切换到角色列表只读预览。");
    }

    private BattlefieldUnitDataDefaults BuildBattlefieldPaletteDataDefaults(BattlefieldUnitPaletteItem item)
    {
        if (_project != null)
        {
            return new BattlefieldUnitDataDefaultService().LoadPersonDefaults(_project, _tables, item.PersonId);
        }

        var jobNames = item.JobId.HasValue
            ? new Dictionary<int, string> { [item.JobId.Value] = string.IsNullOrWhiteSpace(item.JobName) ? $"兵种{item.JobId.Value}" : item.JobName }
            : new Dictionary<int, string>();
        return new BattlefieldUnitDataDefaults
        {
            PersonId = item.PersonId,
            PersonName = string.IsNullOrWhiteSpace(item.Name) ? $"人物{item.PersonId}" : item.Name,
            Found = false,
            Source = "项目未加载，无法读取 Data.e5 默认值。",
            JobId = item.JobId,
            JobNames = jobNames
        };
    }

    private void RefreshBattlefieldPaletteUnitPreview(BattlefieldUnitPaletteItem? item)
    {
        if (item == null)
        {
            _selectedBattlefieldPaletteItem = null;
            SetPictureBoxImage(_battlefieldUnitPreviewBox, null);
            _battlefieldUnitPreviewInfoBox.Text = "选择角色后显示 S 形象。";
            return;
        }

        _selectedBattlefieldPaletteItem = item;
        var faction = GetSelectedBattlefieldFaction();
        var factionSlot = GetBattlefieldFactionSlot(faction);
        var direction = NormalizeBattlefieldDirection(_battlefieldDirectionCombo.SelectedItem?.ToString() ?? "下");
        var levelMode = NormalizeBattlefieldLevelMode(_battlefieldLevelModeCombo.SelectedItem?.ToString() ?? "初级");
        var preview = CloneBattlefieldSImageFrame(item.SImageId, item.JobId, factionSlot, direction, levelMode, _battlefieldUnitAnimationPhase);
        SetPictureBoxImage(_battlefieldUnitPreviewBox, preview);
        var mapping = _project == null
            ? CharacterImageResourceService.ResolveSUnitImageMapping(item.SImageId, item.JobId, factionSlot)
            : CharacterImageResourceService.ResolveSUnitImageMapping(_project, item.SImageId, item.JobId, factionSlot);
        _battlefieldUnitPreviewInfoBox.Text =
            $"{item.DisplayText}\r\n" +
            $"{item.DetailText}\r\n" +
            $"预览阵营：{faction}\r\n" +
            $"等级段：{levelMode}\r\n" +
            $"预览方向：{direction}\r\n" +
            $"{mapping.ShortText}\r\n" +
            (preview == null ? $"未能渲染：{mapping.Detail}" : "三转 S 形象按等级段选择 Unit 图；读取 Unit_mov.e5 的 48x48 待机帧，透明背景，0.8 秒切换。");
    }

    private void LoadBattlefieldScriptView(ScenarioFileInfo scenario, SceneStringDocument dictionary)
    {
        _battlefieldScriptCommandByKey.Clear();
        _battlefieldScriptTextByOffset.Clear();
        _battlefieldScriptTextEntryByOffset.Clear();
        _selectedBattlefieldScriptCommandRow = null;
        _selectedBattlefieldScriptTextEntry = null;
        _battlefieldScriptParameterGrid.DataSource = null;
        ClearBattlefieldScriptParameterEditor();
        _saveBattlefieldScriptStructureButton.Enabled = false;
        _showBattlefieldVariablesButton.Enabled = false;

        try
        {
            _currentBattlefieldLegacyScriptDocument = _legacyScenarioReader.Read(scenario.Path, dictionary);
            ClearLegacyScenarioHistory(LegacyScriptEditorScope.Battlefield);
            _currentBattlefieldScriptStructure = BuildBattlefieldLegacyScriptStructureResult(_currentBattlefieldLegacyScriptDocument);
            _currentBattlefieldScriptTextEntries = BuildBattlefieldLegacyScriptTextEntries(_currentBattlefieldLegacyScriptDocument);
        }
        catch
        {
            _currentBattlefieldLegacyScriptDocument = null;
            ClearLegacyScenarioHistory(LegacyScriptEditorScope.Battlefield);
            _currentBattlefieldScriptStructure = _scenarioStructureProbeReader.Build(scenario.Path, dictionary, maxCommandRows: 600, project: _project, tables: _tables);
            _currentBattlefieldScriptTextEntries = _scenarioTextReader.Read(scenario.Path);
        }

        BuildBattlefieldScriptTree(_currentBattlefieldScriptStructure, _currentBattlefieldScriptTextEntries);
        ClearBattlefieldScriptTextSelection();
        _saveBattlefieldScriptStructureButton.Enabled = _currentBattlefieldLegacyScriptDocument != null;
        _showBattlefieldVariablesButton.Enabled = _currentBattlefieldLegacyScriptDocument != null;
        _battlefieldScriptDetailBox.Text =
            $"S剧本：{scenario.FileName}\r\n" +
            $"Scene：{_currentBattlefieldScriptStructure.SceneCount}  Section：{_currentBattlefieldScriptStructure.SectionCount}  Command：{_currentBattlefieldScriptStructure.CommandCandidateCount}  文本：{_currentBattlefieldScriptTextEntries.Count}";
    }

    private ScenarioStructureProbeResult BuildBattlefieldLegacyScriptStructureResult(LegacyScenarioDocument document)
    {
        var rows = new List<ScenarioStructureRow>();
        var nextIndex = 1;
        foreach (var scene in document.Scenes)
        {
            rows.Add(new ScenarioStructureRow
            {
                Index = nextIndex++,
                Level = 0,
                NodeType = "Scene候选",
                SceneIndex = scene.SceneIndex,
                CommandName = $"Scene {scene.SceneIndex}",
                OffsetHex = HexDisplayFormatter.FormatOffset(scene.FileOffset),
                Confidence = "旧版源码",
                Annotation = "按 CczSceneEditor2 v0.23 Scene 偏移表读取。"
            });

            foreach (var section in scene.Sections)
            {
                rows.Add(new ScenarioStructureRow
                {
                    Index = nextIndex++,
                    Level = 1,
                    NodeType = "Section候选",
                    SceneIndex = section.SceneIndex,
                    SectionIndex = section.SectionIndex,
                    CommandName = $"Section {section.SectionIndex}",
                    OffsetHex = HexDisplayFormatter.FormatOffset(section.FileOffset),
                    Confidence = "旧版源码",
                    Annotation = $"按旧版 Section 长度前缀读取，长度 {section.DeclaredLength} 字节。"
                });

                foreach (var command in section.EnumerateCommands())
                {
                    var row = BuildLegacyScriptCommandRow(command, nextIndex++);
                    rows.Add(row);
                    _battlefieldScriptCommandByKey[BuildLegacyCommandKey(row)] = command;
                }
            }
        }

        return new ScenarioStructureProbeResult
        {
            FilePath = document.FilePath,
            FileName = document.FileName,
            CommandCandidateCount = document.CommandCount,
            SceneCount = document.SceneCount,
            SectionCount = document.SectionCount,
            UsedLegacyParser = true,
            Summary = document.Summary,
            Rows = rows,
            XmlText = BuildLegacyScriptXml(document)
        };
    }

    private IReadOnlyList<ScenarioTextEntry> BuildBattlefieldLegacyScriptTextEntries(LegacyScenarioDocument document)
    {
        var entries = new List<ScenarioTextEntry>();
        var index = 1;
        foreach (var command in document.EnumerateCommands())
        {
            foreach (var parameter in command.TextParameters)
            {
                var capacity = Math.Max(0, parameter.ByteLength - 1);
                var decodeWarning = parameter.TextDecodeWarning;
                var confidence = string.IsNullOrWhiteSpace(parameter.TextDecodeConfidence) ? "高" : parameter.TextDecodeConfidence;
                var entry = new ScenarioTextEntry
                {
                    Index = index++,
                    Offset = parameter.FileOffset,
                    OffsetHex = FormatLegacyScriptOffset(parameter.FileOffset, index),
                    ByteLength = capacity,
                    CharLength = parameter.Text.Length,
                    Kind = $"旧版文本参数 {command.CommandIdHex}",
                    HasNewLines = parameter.Text.Contains('\n') || parameter.Text.Contains('\r'),
                    Preview = parameter.Text.Length > 60 ? parameter.Text[..60] : parameter.Text,
                    Text = parameter.Text,
                    OriginalText = parameter.Text,
                    SourceKind = "旧版完整树文本参数",
                    EncodingName = string.IsNullOrWhiteSpace(parameter.TextEncodingName) ? "GBK" : parameter.TextEncodingName,
                    DecodeConfidence = confidence,
                    DecodeWarning = decodeWarning,
                    IsWritable = confidence != "低",
                    Annotation = $"Scene {command.SceneIndex} / Section {command.SectionIndex} / Command {command.CommandIndex} {command.CommandName} 参数槽 {parameter.Index}。旧版完整树文本参数；解码置信度 {confidence}{(string.IsNullOrWhiteSpace(decodeWarning) ? string.Empty : "；" + decodeWarning)}。"
                };
                entries.Add(entry);
                _battlefieldScriptTextByOffset[entry.Offset] = (command, parameter);
                _battlefieldScriptTextEntryByOffset[entry.Offset] = entry;
            }
        }

        return entries;
    }

    private void BuildBattlefieldScriptTree(ScenarioStructureProbeResult structure, IReadOnlyList<ScenarioTextEntry> texts)
    {
        if (_currentBattlefieldLegacyScriptDocument != null)
        {
            BuildLegacyEditorScriptTree(
                _battlefieldScriptTree,
                _currentBattlefieldLegacyScriptDocument,
                structure,
                _battlefieldScriptItemDataByCommand,
                _battlefieldScriptItemDataByRow,
                commandNodeFactory: (command, row, itemData) =>
                {
                    var node = CreateLegacyEditorCommandTreeNode(command, row, itemData);
                    ApplyBattlefieldScriptPreviewToNode(node, row);
                    return node;
                });
            return;
        }

        _battlefieldScriptTree.BeginUpdate();
        try
        {
            _battlefieldScriptTree.Nodes.Clear();
            var root = new TreeNode(structure.FileName) { ToolTipText = structure.FilePath };
            foreach (var sceneGroup in structure.Rows.Where(row => row.NodeType == "Scene候选").GroupBy(row => row.SceneIndex))
            {
                var sceneRow = sceneGroup.First();
                var sceneNode = new TreeNode(sceneRow.CommandName) { Tag = sceneRow, ToolTipText = sceneRow.Annotation };
                var sections = structure.Rows
                    .Where(row => row.SceneIndex == sceneRow.SceneIndex && row.NodeType == "Section候选")
                    .OrderBy(row => row.SectionIndex)
                    .ToList();
                foreach (var sectionRow in sections)
                {
                    var sectionNode = new TreeNode(sectionRow.CommandName) { Tag = sectionRow, ToolTipText = sectionRow.Annotation };
                    foreach (var commandRow in structure.Rows
                                 .Where(row => row.NodeType == "Command候选" && row.SceneIndex == sectionRow.SceneIndex && row.SectionIndex == sectionRow.SectionIndex)
                                 .OrderBy(row => row.CommandIndex))
                    {
                        var commandNode = new TreeNode(BuildScriptCommandSummary(commandRow, includeIdentity: true, maxVisibleValues: 6))
                        {
                            Tag = commandRow,
                            ToolTipText = BuildBattlefieldScriptCommandTreeToolTip(commandRow),
                            ForeColor = GetScriptCommandColor(commandRow.CommandId)
                        };
                        ApplyBattlefieldScriptPreviewToNode(commandNode, commandRow);
                        sectionNode.Nodes.Add(commandNode);
                    }
                    sceneNode.Nodes.Add(sectionNode);
                }
                root.Nodes.Add(sceneNode);
            }

            _battlefieldScriptTree.Nodes.Add(root);
            root.Expand();
            if (root.Nodes.Count > 0) root.Nodes[0].Expand();
        }
        finally
        {
            _battlefieldScriptTree.EndUpdate();
        }
    }

    private void ShowSelectedBattlefieldScriptNode()
    {
        if (_bindingBattlefieldScriptEditor) return;
        if (_updatingBattlefieldScriptSelection) return;

        _updatingBattlefieldScriptSelection = true;
        try
        {
            ShowSelectedBattlefieldScriptNodeCore();
        }
        finally
        {
            _updatingBattlefieldScriptSelection = false;
        }
    }

    private void ShowSelectedBattlefieldScriptNodeCore()
    {
        if (_battlefieldScriptTree.SelectedNode?.Tag is LegacyScenarioItemData { UiRow: ScenarioStructureRow itemRow } itemData)
        {
            _selectedBattlefieldScriptCommandRow = itemRow.NodeType == "Command候选" ? itemRow : null;
            _selectedBattlefieldScriptTextEntry = null;
            UpdateBattlefieldTextWrapLimitControl(itemData.Command);
            _battlefieldScriptTextBox.Clear();
            BindBattlefieldScriptParameterRows(itemData.Command != null
                ? BuildBattlefieldLegacyScriptParameterRows(itemData.Command)
                : Array.Empty<ScenarioCommandParameterRow>());
            UpdateBattlefieldScriptTextCapacityLabel();
            _battlefieldScriptDetailBox.Text = itemData.Command != null
                ? BuildLegacyScriptRowDetail(itemRow, itemData.Command)
                : BuildBattlefieldScriptRowDetailWithPreview(itemRow);
            SetBattlefieldScriptPreview(itemRow, itemData.Command);
            UpdateBattlefieldScriptImagePreview(itemRow);
            LoadBattlefieldInlineLegacyScriptDialogForSelection();
            return;
        }

        if (_battlefieldScriptTree.SelectedNode?.Tag is ScenarioTextEntry text)
        {
            _selectedBattlefieldScriptTextEntry = text;
            _selectedBattlefieldScriptCommandRow = null;
            UpdateBattlefieldTextWrapLimitControlForTextEntry(text);
            _battlefieldScriptTextBox.Text = text.Text;
            BindBattlefieldScriptParameterRows(Array.Empty<ScenarioCommandParameterRow>());
            _battlefieldScriptDetailBox.Text = BuildBattlefieldScriptTextDetail(text);
            UpdateBattlefieldScriptTextCapacityLabel();
            SetBattlefieldScriptTextPreview(text);
            ClearBattlefieldScriptImagePreview();
            _battlefieldInlineDialogHost.ClearDialog("文本参数请通过所属命令的旧版 Dialog 修改。");
            _applyBattlefieldScriptParameterButton.Enabled = false;
            _resetBattlefieldInlineDialogButton.Enabled = false;
            return;
        }

        if (_battlefieldScriptTree.SelectedNode?.Tag is ScenarioStructureRow row)
        {
            _selectedBattlefieldScriptCommandRow = row.NodeType == "Command候选" ? row : null;
            _selectedBattlefieldScriptTextEntry = null;
            UpdateBattlefieldTextWrapLimitControl(
                _selectedBattlefieldScriptCommandRow != null &&
                _battlefieldScriptCommandByKey.TryGetValue(BuildLegacyCommandKey(row), out var command)
                    ? command
                    : null);
            _battlefieldScriptTextBox.Clear();
            BindBattlefieldScriptParameterRows(row.NodeType == "Command候选"
                ? BuildBattlefieldScriptParameterRows(row)
                : Array.Empty<ScenarioCommandParameterRow>());
            UpdateBattlefieldScriptTextCapacityLabel();
            _battlefieldScriptDetailBox.Text = BuildBattlefieldScriptRowDetailWithPreview(row);
            SetBattlefieldScriptPreview(row);
            UpdateBattlefieldScriptImagePreview(row);
            LoadBattlefieldInlineLegacyScriptDialogForSelection();
            return;
        }

        ClearBattlefieldScriptTextSelection();
        ClearBattlefieldScriptImagePreview();
        _battlefieldInlineDialogHost.ClearDialog("请选择左侧 S 剧本命令。");
        _applyBattlefieldScriptParameterButton.Enabled = false;
        _resetBattlefieldInlineDialogButton.Enabled = false;
    }

    private string BuildBattlefieldScriptTextDetail(ScenarioTextEntry text)
        => $"文本＀{text.Index} {text.Kind} {text.OffsetHex}\r\n" +
           $"容量：GBK {EncodingService.GetGbkByteCount(_battlefieldScriptTextBox.Text)}/{text.ByteLength} 字节\r\n" +
           BuildScenarioTextDecodeLine(text) + "\r\n" +
           $"说明：{text.Annotation}";

    private string BuildBattlefieldScriptRowDetailWithPreview(ScenarioStructureRow row)
    {
        var detail = BuildBattlefieldScriptRowDetail(row);
        var preview = row.NodeType == "Command候选" ? GetBattlefieldScriptPreviewForRow(row) : null;
        return preview == null
            ? detail
            : detail + "\r\n\r\n" + BuildBattlefieldScriptPreviewText(preview);
    }

    private void SetBattlefieldOverviewPreview(string text)
    {
        _battlefieldRightPreviewMode = BattlefieldRightPreviewMode.Script;
        ApplyBattlefieldRightPreviewMode();
        _battlefieldInfoBox.Text = text;
        _battlefieldScriptDetailBox.Text = text;
        ClearBattlefieldScriptImagePreview();
        _battlefieldInlineDialogHost.ClearDialog("请选择左侧 S 剧本命令。");
        _applyBattlefieldScriptParameterButton.Enabled = false;
        _resetBattlefieldInlineDialogButton.Enabled = false;
        UpdateBattlefieldTextWrapLimitControl(null);
    }

    private void SetBattlefieldScriptPreview(ScenarioStructureRow row, LegacyScenarioCommandNode? command = null, string? prefix = null)
    {
        _battlefieldRightPreviewMode = BattlefieldRightPreviewMode.Script;
        ApplyBattlefieldRightPreviewMode();
        _battlefieldScriptDetailBox.Text = BuildBattlefieldRightScriptPreview(row, command, prefix);
        _battlefieldInfoBox.Text = _battlefieldScriptDetailBox.Text;
    }

    private void SetBattlefieldScriptTextPreview(ScenarioTextEntry text, string? prefix = null)
    {
        _battlefieldRightPreviewMode = BattlefieldRightPreviewMode.Script;
        ApplyBattlefieldRightPreviewMode();
        _battlefieldScriptDetailBox.Text = BuildBattlefieldRightScriptTextPreview(text, prefix);
        _battlefieldInfoBox.Text = _battlefieldScriptDetailBox.Text;
    }

    private void SetBattlefieldConsolePreview(string text)
    {
        _battlefieldRightPreviewMode = BattlefieldRightPreviewMode.Console;
        ApplyBattlefieldRightPreviewMode();
        _battlefieldInfoBox.Text = text;
    }

    private void ApplyBattlefieldRightPreviewMode()
    {
        var showConsole = _battlefieldRightPreviewMode == BattlefieldRightPreviewMode.Console;
        _battlefieldConsolePreviewPanel.Visible = showConsole;
        _battlefieldScriptPreviewPanel.Visible = !showConsole;
        if (showConsole)
        {
            _battlefieldConsolePreviewPanel.BringToFront();
        }
        else
        {
            _battlefieldScriptPreviewPanel.BringToFront();
        }
    }

    internal string BattlefieldRightPreviewModeForSmoke => _battlefieldRightPreviewMode.ToString();

    internal void ShowBattlefieldScriptPreviewForSmoke(ScenarioStructureRow row, LegacyScenarioCommandNode? command)
        => SetBattlefieldScriptPreview(row, command);

    internal void ShowBattlefieldScriptTextPreviewForSmoke(ScenarioTextEntry text)
        => SetBattlefieldScriptTextPreview(text);

    internal void ShowBattlefieldConsolePreviewForSmoke(string text)
        => SetBattlefieldConsolePreview(text);

    internal void ShowBattlefieldPaletteConsolePreviewForSmoke(BattlefieldUnitPaletteItem item)
        => ShowBattlefieldPaletteConsolePreview(item);

    private void ClearBattlefieldScriptImagePreview()
    {
        SetPictureBoxImage(_battlefieldScriptImagePreviewBox, null);
        _battlefieldScriptImagePreviewInfoBox.Text = "无图片预览";
    }

    private void UpdateBattlefieldScriptImagePreview(ScenarioStructureRow row)
    {
        if (_battlefieldScriptImagePreviewBox.Parent == null || !_battlefieldScriptImagePreviewBox.Visible)
        {
            ClearBattlefieldScriptImagePreview();
            return;
        }

        if (_project == null || row.NodeType != "Command候选")
        {
            ClearBattlefieldScriptImagePreview();
            return;
        }

        if (!TryResolveScriptImagePreview(row, out var resourceName, out var scriptImageNumber, out var title))
        {
            ClearBattlefieldScriptImagePreview();
            return;
        }

        var resource = _imageResourceCatalogService.FindCatalogItem(_project, resourceName);
        if (resource == null || !resource.Exists || !resource.SupportsPreview)
        {
            SetPictureBoxImage(_battlefieldScriptImagePreviewBox, null);
            _battlefieldScriptImagePreviewInfoBox.Text =
                $"{title} {scriptImageNumber}\r\n" +
                $"资源：{resourceName}\r\n" +
                "未找到可预览资源。";
            return;
        }

        if (!TryGetScriptImagePreviewEntry(resource, scriptImageNumber, out var entry, out var resolveNote))
        {
            SetPictureBoxImage(_battlefieldScriptImagePreviewBox, null);
            _battlefieldScriptImagePreviewInfoBox.Text =
                $"{title} {scriptImageNumber}\r\n" +
                $"资源：{resource.FileName}\r\n" +
                resolveNote;
            return;
        }

        try
        {
            var width = Math.Max(260, _battlefieldScriptImagePreviewBox.ClientSize.Width);
            var height = Math.Max(180, _battlefieldScriptImagePreviewBox.ClientSize.Height);
            var preview = _imageResourceCatalogService.RenderEntryPreview(_project, entry, width, height);
            SetPictureBoxImage(_battlefieldScriptImagePreviewBox, preview);
            _battlefieldScriptImagePreviewInfoBox.Text =
                $"{title} {scriptImageNumber}\r\n" +
                $"资源：{entry.FileName} #{entry.ImageNumber}\r\n" +
                $"{resolveNote}\r\n" +
                $"{entry.Kind}  {entry.DecodedLength:N0}B";
        }
        catch (Exception ex)
        {
            SetPictureBoxImage(_battlefieldScriptImagePreviewBox, null);
            _battlefieldScriptImagePreviewInfoBox.Text =
                $"{title} {scriptImageNumber}\r\n" +
                $"资源：{resource.FileName}\r\n" +
                "预览失败：" + ex.Message;
        }
    }

    private void LoadBattlefieldInlineLegacyScriptDialogForSelection()
    {
        _applyBattlefieldScriptParameterButton.Enabled = false;
        _resetBattlefieldInlineDialogButton.Enabled = false;

        if (!TryGetSelectedBattlefieldLegacyItemData(out var itemData) || itemData.Command == null)
        {
            _battlefieldInlineDialogHost.ClearDialog("请选择左侧 S 剧本命令。");
            UpdateBattlefieldTextWrapLimitControl(null);
            return;
        }

        UpdateBattlefieldTextWrapLimitControl(itemData.Command);
        if (!LegacyCommandEditDispatcher.CanEdit(itemData.Id))
        {
            _battlefieldInlineDialogHost.ClearDialog("该命令在旧版源码中没有修改窗口。");
            return;
        }

        if (!TryValidateBattlefieldLegacyDialogCommand(itemData.Command, out var validationError))
        {
            _battlefieldInlineDialogHost.ClearDialog(validationError);
            return;
        }

        var dialogName = LegacyCommandEditDispatcher.GetDialogName(itemData.Id);
        if (!LegacyMfcDialogCatalog.TryGet(dialogName, out var spec))
        {
            _battlefieldInlineDialogHost.ClearDialog("该旧版 Dialog 尚未迁移为 MFC 控件。");
            return;
        }

        var dialogDataSources = LegacyMfcDialogDataSources.Create(_project, _tables);
        var precedingSameCommandCount = CountPrecedingSameLegacyCommands(_currentBattlefieldLegacyScriptDocument, itemData.Command);
        _battlefieldInlineDialogHost.LoadDialog(
            itemData,
            spec,
            dialogDataSources,
            _currentBattlefieldLegacyScriptDocument?.CommandCount ?? 0,
            precedingSameCommandCount,
            includeDialogButtons: false);
        _battlefieldInlineDialogHost.ConfigureTextWrapping(
            BuildLegacyTextWrapOptions(itemData),
            result => ShowLegacyTextWrapResult(_battlefieldInlineDialogHost, _battlefieldScriptTextWrapLimitInput, result));
        _applyBattlefieldScriptParameterButton.Enabled = true;
        _resetBattlefieldInlineDialogButton.Enabled = true;
    }

    private void ApplyBattlefieldInlineLegacyScriptDialog()
    {
        if (!TryGetSelectedBattlefieldLegacyItemData(out var itemData) || itemData.Command == null)
        {
            MessageBox.Show(this, "请先在左侧 S 剧本树中选择一条命令。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        if (_currentBattlefieldLegacyScriptDocument == null)
        {
            MessageBox.Show(this, "当前 S 剧本尚未读取为旧版完整树，无法应用内嵌修改。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var command = itemData.Command;
        var oldSummary = BuildLegacyScriptParameterPreview(command);
        var beforeCommand = CaptureLegacyItemDataCommandSnapshot(itemData);
        var beforeEdit = CaptureLegacyScenarioHistorySnapshot(LegacyScriptEditorScope.Battlefield, _currentBattlefieldLegacyScriptDocument);
        var preferredParameterIndex = GetSelectedBattlefieldScriptParameterRow()?.Index;
        var preferredTargetKey = ResolveBattlefieldLegacyEditPreferredTargetKey(command, null, preferredParameterIndex);

        _battlefieldInlineDialogHost.ConfigureTextWrapping(BuildLegacyTextWrapOptions(itemData));
        var error = _battlefieldInlineDialogHost.CommitToTarget();
        if (!string.IsNullOrWhiteSpace(error))
        {
            MessageBox.Show(this, error, "参数值无效", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        CopyLegacyItemDataToCommand(itemData);
        var changed = LegacyItemDataCommandChanged(itemData, beforeCommand);
        if (!changed)
        {
            SetStatus($"战场制作内嵌修改：{command.CommandIdHex} {command.CommandName} 未检测到改动");
            LoadBattlefieldInlineLegacyScriptDialogForSelection();
            return;
        }

        PushLegacyScenarioUndoSnapshot(LegacyScriptEditorScope.Battlefield, beforeEdit);
        if (!RefreshLegacyEditorCommandInPlace(LegacyScriptEditorScope.Battlefield, command, preferredParameterIndex))
        {
            RefreshBattlefieldLegacyScriptView(command, preferredParameterIndex);
        }

        RefreshBattlefieldDocumentFromLegacyScript(preferredTargetKey);
        _saveBattlefieldScriptStructureButton.Enabled = true;
        LoadBattlefieldInlineLegacyScriptDialogForSelection();
        SetStatus($"战场制作内嵌修改：{command.CommandIdHex} {command.CommandName}，{oldSummary} -> {BuildLegacyScriptParameterPreview(command)}，需完整保存S剧本");
    }

    private string BuildBattlefieldRightScriptTextPreview(ScenarioTextEntry text, string? prefix)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(prefix))
        {
            parts.Add(prefix.TrimEnd());
        }

        parts.Add("S 剧本文本预览：");
        parts.Add(BuildBattlefieldScriptTextDetail(text));
        return string.Join("\r\n\r\n", parts);
    }

    private string BuildBattlefieldRightScriptPreview(ScenarioStructureRow row, LegacyScenarioCommandNode? command, string? prefix)
    {
        command ??= row.NodeType == "Command候选" &&
                    _battlefieldScriptItemDataByRow.TryGetValue(row, out var rowItemData)
            ? rowItemData.Command
            : null;
        command ??= row.NodeType == "Command候选" &&
                    _battlefieldScriptCommandByKey.TryGetValue(BuildLegacyCommandKey(row), out var legacyCommand)
            ? legacyCommand
            : null;

        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(prefix))
        {
            parts.Add(prefix.TrimEnd());
        }

        parts.Add("S 剧本指令预览：");
        parts.Add(command != null
            ? BuildLegacyScriptRowDetail(row, command)
            : BuildBattlefieldScriptRowDetail(row));

        if (command != null && LegacyCommandEditDispatcher.CanEdit(command.CommandId))
        {
            parts.Add($"旧版编辑窗口：{LegacyCommandEditDispatcher.GetDialogName(command.CommandId)}");
        }

        if (command != null)
        {
            var formatter = new LegacyScenarioCommandDisplayFormatter(LegacyMfcDialogDataSources.Create(_project, _tables));
            var valuesPreview = formatter.FormatValuesPreview(command, 8);
            if (!string.IsNullOrWhiteSpace(valuesPreview))
            {
                parts.Add("参数预览：" + valuesPreview);
            }
        }

        var mapPreview = row.NodeType == "Command候选" ? GetBattlefieldScriptPreviewForRow(row) : null;
        if (mapPreview != null)
        {
            parts.Add(BuildBattlefieldScriptPreviewText(mapPreview));
        }

        return string.Join("\r\n\r\n", parts);
    }

    private string BuildBattlefieldScriptRowDetail(ScenarioStructureRow row)
    {
        if (row.NodeType != "Command候选")
        {
            return $"{row.CommandName}\r\n类型：{row.NodeType}\r\n中文注释：{row.Annotation}";
        }

        if (_currentBattlefieldLegacyScriptDocument != null &&
            _battlefieldScriptCommandByKey.TryGetValue(BuildLegacyCommandKey(row), out var legacyCommand))
        {
            return BuildLegacyScriptRowDetail(row, legacyCommand);
        }

        var valueLines = BuildValueDetailBlock(BuildScenarioStructurePreviewValueLines(row));
        var referenceHint = string.IsNullOrWhiteSpace(row.ReferenceHint)
            ? string.Empty
            : $"\r\n引用候选：{row.ReferenceHint}";
        return
            $"命令：{row.CommandIdHex} {row.CommandName}\r\n" +
            $"位置：Scene {row.SceneIndex} / Section {row.SectionIndex} / Command {row.CommandIndex}\r\n" +
            $"参数：\r\n{valueLines}{referenceHint}\r\n" +
            $"中文注释：{row.Annotation}";
    }

    private string BuildBattlefieldScriptCommandTreeToolTip(ScenarioStructureRow row)
    {
        if (_currentBattlefieldLegacyScriptDocument != null &&
            _battlefieldScriptCommandByKey.TryGetValue(BuildLegacyCommandKey(row), out var legacyCommand))
        {
            return BuildLegacyScriptCommandTreeToolTip(row, legacyCommand);
        }

        return BuildScriptCommandTreeToolTip(row);
    }

    private IReadOnlyList<ScenarioCommandParameterRow> BuildBattlefieldScriptParameterRows(ScenarioStructureRow row)
    {
        if (row.NodeType == "Command候选" &&
            _currentBattlefieldLegacyScriptDocument != null &&
            _battlefieldScriptCommandByKey.TryGetValue(BuildLegacyCommandKey(row), out var legacyCommand))
        {
            return BuildBattlefieldLegacyScriptParameterRows(legacyCommand);
        }

        return Array.Empty<ScenarioCommandParameterRow>();
    }

    private static IReadOnlyList<ScenarioCommandParameterRow> BuildBattlefieldLegacyScriptParameterRows(LegacyScenarioCommandNode command)
    {
        if (command.CommandId is not (0x46 or 0x47))
        {
            return BuildLegacyScriptParameterRows(command);
        }

        return new[]
        {
            new ScenarioCommandParameterRow
            {
                Index = 0,
                SlotName = "出场块摘要",
                Kind = command.CommandId == 0x46 ? "友军出场设定" : "敌军出场设定",
                RawHex = FormatLegacyScriptOffset(command.FileOffset, command.CommandIndex),
                DecimalValue = command.Parameters.Count,
                DecodedValue = $"{command.Parameters.Count} 个旧版参数槽；右侧出场候选已按记录拆分显示。",
                Meaning = "该命令是旧版战场部署大块。为避免点击/双击时同步重建数百行参数表，战场页只显示摘要；双击或点“修改整条指令”会按旧版源码打开 Dialog_70。",
                Risk = "完整结构写回：保存前备份，替换前按旧版规则重读校验。",
                FromTemplate = true,
                Annotation = $"Scene {command.SceneIndex} / Section {command.SectionIndex} / Command {command.CommandIndex} {command.CommandName}"
            }
        };
    }

    private void BindBattlefieldScriptParameterRows(IReadOnlyList<ScenarioCommandParameterRow> rows)
    {
        ClearBattlefieldScriptParameterEditor();
        _bindingBattlefieldScriptEditor = true;
        try
        {
            _battlefieldScriptParameterGrid.DataSource = new BindingList<ScenarioCommandParameterRow>(rows.ToList());
            foreach (DataGridViewColumn column in _battlefieldScriptParameterGrid.Columns)
            {
                column.HeaderText = column.DataPropertyName switch
                {
                    nameof(ScenarioCommandParameterRow.Index) => "序号",
                    nameof(ScenarioCommandParameterRow.SlotName) => "参数名",
                    nameof(ScenarioCommandParameterRow.Kind) => "类型",
                    nameof(ScenarioCommandParameterRow.RawHex) => "十六进制",
                    nameof(ScenarioCommandParameterRow.DecimalValue) => "十进制",
                    nameof(ScenarioCommandParameterRow.DecodedValue) => "当前倀解释",
                    nameof(ScenarioCommandParameterRow.Meaning) => "含义",
                    nameof(ScenarioCommandParameterRow.Risk) => "风险/边界",
                    nameof(ScenarioCommandParameterRow.FromTemplate) => "模板",
                    nameof(ScenarioCommandParameterRow.Annotation) => "中文注释",
                    _ => column.HeaderText
                };
                column.ToolTipText = column.DataPropertyName switch
                {
                    nameof(ScenarioCommandParameterRow.DecodedValue) => "当前参数值及可读解释。",
                    nameof(ScenarioCommandParameterRow.Meaning) => "该槽位在旧版 S 剧本命令里的含义。",
                    nameof(ScenarioCommandParameterRow.Risk) => "完整保存前仍会自动备份并复读校验。",
                    _ => column.ToolTipText
                };
                if (column.DataPropertyName is nameof(ScenarioCommandParameterRow.Index)
                    or nameof(ScenarioCommandParameterRow.Kind)
                    or nameof(ScenarioCommandParameterRow.RawHex)
                    or nameof(ScenarioCommandParameterRow.DecimalValue)
                    or nameof(ScenarioCommandParameterRow.Risk)
                    or nameof(ScenarioCommandParameterRow.FromTemplate)
                    or nameof(ScenarioCommandParameterRow.Annotation))
                {
                    column.Visible = false;
                }

                column.SortMode = DataGridViewColumnSortMode.NotSortable;
                if (column.DataPropertyName is nameof(ScenarioCommandParameterRow.DecodedValue))
                {
                    column.Width = 280;
                }
                else if (column.DataPropertyName is nameof(ScenarioCommandParameterRow.Meaning))
                {
                    column.Width = 220;
                }
                else if (column.DataPropertyName is nameof(ScenarioCommandParameterRow.SlotName))
                {
                    column.Width = 150;
                }
            }

            if (_battlefieldScriptParameterGrid.Rows.Count > 0 && _battlefieldScriptParameterGrid.CurrentCell == null)
            {
                var firstVisibleCell = _battlefieldScriptParameterGrid.Rows[0].Cells
                    .Cast<DataGridViewCell>()
                    .FirstOrDefault(cell => cell.Visible);
                if (firstVisibleCell != null)
                {
                    _battlefieldScriptParameterGrid.CurrentCell = firstVisibleCell;
                    _battlefieldScriptParameterGrid.Rows[0].Selected = true;
                }
            }
        }
        finally
        {
            _bindingBattlefieldScriptEditor = false;
        }

        ShowSelectedBattlefieldScriptParameter();
    }

    private void ClearBattlefieldScriptParameterEditor()
    {
        _battlefieldScriptParameterValueBox.Clear();
        _battlefieldScriptParameterValueBox.Enabled = false;
        _applyBattlefieldScriptParameterButton.Enabled = false;
        _editBattlefieldScriptParametersButton.Enabled = false;
    }

    private void ShowSelectedBattlefieldScriptParameter()
    {
        if (_bindingBattlefieldScriptEditor) return;
        if (!TryGetSelectedBattlefieldScriptParameter(out var command, out var parameter, out _))
        {
            ClearBattlefieldScriptParameterEditor();
            return;
        }

        _battlefieldScriptParameterValueBox.Text = FormatLegacyScriptParameterEditorValue(command, parameter);
        if (command.CommandId is 0x46 or 0x47)
        {
            _editBattlefieldScriptParametersButton.Enabled = true;
            _battlefieldScriptParameterValueBox.Enabled = false;
            _applyBattlefieldScriptParameterButton.Enabled = false;
            SetStatus("战场制作 S 剧本参数：出场大块按旧版源码打开 Dialog_70；侧栏摘要不作为单槽直接编辑。");
            return;
        }

        _editBattlefieldScriptParametersButton.Enabled = CanEditLegacyScriptCommandParameters(command, out _);
        if (CanEditLegacyScriptParameter(command, parameter, out var reason))
        {
            _battlefieldScriptParameterValueBox.Enabled = true;
            _applyBattlefieldScriptParameterButton.Enabled = true;
            SetStatus($"战场制作 S 剧本参数：槽 {parameter.Index} 可编辑，当前倀{FormatLegacyScriptParameterEditorValue(command, parameter)}");
        }
        else
        {
            _battlefieldScriptParameterValueBox.Enabled = false;
            _applyBattlefieldScriptParameterButton.Enabled = false;
            SetStatus("战场制作 S 剧本参数：" + reason);
        }
    }

    private ScenarioCommandParameterRow? GetSelectedBattlefieldScriptParameterRow()
    {
        if (_battlefieldScriptParameterGrid.SelectedRows.Count > 0 &&
            _battlefieldScriptParameterGrid.SelectedRows[0].DataBoundItem is ScenarioCommandParameterRow selected)
        {
            return selected;
        }

        return _battlefieldScriptParameterGrid.CurrentRow?.DataBoundItem as ScenarioCommandParameterRow;
    }

    private bool TryGetSelectedBattlefieldLegacyScriptCommand(out LegacyScenarioCommandNode command)
    {
        if (_battlefieldScriptTree.SelectedNode?.Tag is LegacyScenarioItemData { Command: { } itemCommand })
        {
            command = itemCommand;
            return true;
        }

        if (_selectedBattlefieldScriptCommandRow != null &&
            _battlefieldScriptCommandByKey.TryGetValue(BuildLegacyCommandKey(_selectedBattlefieldScriptCommandRow), out var selected))
        {
            command = selected;
            return true;
        }

        var node = _battlefieldScriptTree.SelectedNode;
        if (node?.Tag is ScenarioStructureRow { NodeType: "Command候选" } treeRow &&
            _battlefieldScriptCommandByKey.TryGetValue(BuildLegacyCommandKey(treeRow), out var treeCommand))
        {
            command = treeCommand;
            return true;
        }

        if (node?.Parent?.Tag is ScenarioStructureRow { NodeType: "Command候选" } parentRow &&
            _battlefieldScriptCommandByKey.TryGetValue(BuildLegacyCommandKey(parentRow), out var parentCommand))
        {
            command = parentCommand;
            return true;
        }

        command = null!;
        return false;
    }

    private bool TryGetBattlefieldLegacyScriptCommand(TreeNode? node, out LegacyScenarioCommandNode command)
    {
        if (node?.Tag is LegacyScenarioItemData { Command: { } itemCommand })
        {
            command = itemCommand;
            return true;
        }

        if (node?.Tag is ScenarioStructureRow treeRow &&
            treeRow.NodeType.Contains("Command", StringComparison.Ordinal) &&
            _battlefieldScriptCommandByKey.TryGetValue(BuildLegacyCommandKey(treeRow), out var treeCommand))
        {
            command = treeCommand;
            return true;
        }

        if (node?.Parent?.Tag is ScenarioStructureRow parentRow &&
            parentRow.NodeType.Contains("Command", StringComparison.Ordinal) &&
            _battlefieldScriptCommandByKey.TryGetValue(BuildLegacyCommandKey(parentRow), out var parentCommand))
        {
            command = parentCommand;
            return true;
        }

        command = null!;
        return false;
    }

    private bool TryGetSelectedBattlefieldScriptParameter(
        out LegacyScenarioCommandNode command,
        out LegacyScenarioCommandParameter parameter,
        out ScenarioCommandParameterRow row)
    {
        command = null!;
        parameter = null!;
        row = null!;
        var selectedRow = GetSelectedBattlefieldScriptParameterRow();
        if (selectedRow == null) return false;
        if (!TryGetSelectedBattlefieldLegacyScriptCommand(out command)) return false;

        row = selectedRow;
        var parameterIndex = selectedRow.Index;
        parameter = command.Parameters.FirstOrDefault(candidate => candidate.Index == parameterIndex)!;
        return parameter != null;
    }

    private void ApplySelectedBattlefieldScriptParameterValue()
    {
        if (!TryGetSelectedBattlefieldScriptParameter(out var command, out var parameter, out var row))
        {
            MessageBox.Show(this, "请先在命令参数页选择一个参数槽。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        if (!CanEditLegacyScriptParameter(command, parameter, out var reason))
        {
            MessageBox.Show(this, reason, "该参数暂不开放编辑", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var beforeEdit = CaptureLegacyScenarioHistorySnapshot(LegacyScriptEditorScope.Battlefield, _currentBattlefieldLegacyScriptDocument!);
        var oldValue = FormatLegacyScriptParameterEditorValue(command, parameter);
        if (!TryApplyLegacyScriptParameterValue(command, parameter, _battlefieldScriptParameterValueBox.Text, out var newValue, out var error))
        {
            MessageBox.Show(this, error, "参数值无效", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            _battlefieldScriptParameterValueBox.Focus();
            _battlefieldScriptParameterValueBox.SelectAll();
            return;
        }

        if (string.Equals(oldValue, newValue, StringComparison.Ordinal))
        {
            SetStatus($"战场制作 S 剧本参数：槽 {parameter.Index} 未检测到改动");
            return;
        }

        PushLegacyScenarioUndoSnapshot(LegacyScriptEditorScope.Battlefield, beforeEdit);
        if (!RefreshLegacyEditorCommandInPlace(LegacyScriptEditorScope.Battlefield, command, row.Index))
        {
            RefreshBattlefieldLegacyScriptView(command, row.Index);
        }
        _saveBattlefieldScriptStructureButton.Enabled = true;
        SetStatus($"战场制作 S 剧本参数：{command.CommandIdHex} {command.CommandName} 槀{parameter.Index} {oldValue} -> {newValue}，需完整保存S剧本");
    }

    private void QueueEditSelectedBattlefieldScriptParameters(TreeNode? requestedNode = null)
    {
        var target = TryResolveBattlefieldScriptCommandTarget(requestedNode, out var resolvedTarget)
            ? resolvedTarget
            : (BattlefieldScriptCommandTargetKey?)null;
        QueueEditSelectedBattlefieldScriptParameters(requestedNode, target);
    }

    private void QueueEditSelectedBattlefieldScriptParameters(
        TreeNode? requestedNode,
        BattlefieldScriptCommandTargetKey? requestedTarget)
    {
        BeginInvoke(new Action(() =>
        {
            if (IsDisposed || _editingBattlefieldLegacyCommandDialog || _battlefieldScriptTree.IsDisposed)
            {
                return;
            }

            if (requestedTarget.HasValue)
            {
                if (!TryFindBattlefieldScriptCommandTarget(requestedTarget.Value, out var queuedItemData, out var currentNode) ||
                    queuedItemData.Command == null)
                {
                    SetStatus("战场制作：该 S 剧本命令已刷新或不存在，无法打开旧版修改窗口。");
                    return;
                }

                if (currentNode != null && !ReferenceEquals(_battlefieldScriptTree.SelectedNode, currentNode))
                {
                    _battlefieldScriptTree.SelectedNode = currentNode;
                }

                EditSelectedBattlefieldScriptParameters(queuedItemData);
                return;
            }

            if (requestedNode != null)
            {
                SetStatus("战场制作：双击节点不是可修改的旧版命令节点。");
                return;
            }

            if (_battlefieldScriptTree.SelectedNode == null ||
                !TryGetSelectedBattlefieldLegacyItemData(out var selectedItemData) ||
                selectedItemData.Command == null)
            {
                SetStatus("战场制作：请先选择 S 剧本树中的旧版命令节点。");
                return;
            }

            EditSelectedBattlefieldScriptParameters(selectedItemData);
        }));
    }

    private void QueueEditBattlefieldScriptParametersFromDoubleClick(TreeNode? requestedNode)
    {
        if (!TryResolveBattlefieldScriptCommandTarget(requestedNode, out var target))
        {
            SetStatus("战场制作：双击节点不是可修改的旧版命令节点。");
            return;
        }

        var result = TryCommitPendingBattlefieldConsoleChangesResult();
        if (!result.AllowsNavigation)
        {
            return;
        }

        QueueEditSelectedBattlefieldScriptParameters(requestedNode, target);
    }

    private void HandleBattlefieldScriptTreeNodeMouseDoubleClick(TreeNodeMouseClickEventArgs e)
    {
        if (e.Button != MouseButtons.Left) return;

        if (ReferenceEquals(_battlefieldScriptSelectionBlockedNode, e.Node))
        {
            _battlefieldScriptSelectionBlockedNode = null;
            return;
        }

        if (!ReferenceEquals(_battlefieldScriptTree.SelectedNode, e.Node))
        {
            // Normally WinForms selects the node before raising NodeMouseDoubleClick.
            // Keep direct/test/keyboard invocations compatible without re-entering the
            // commit gate or committing a second time.
            using (SuppressBattlefieldScriptSelectionCommit())
            {
                _battlefieldScriptTree.SelectedNode = e.Node;
            }
        }

        var selectionGateAlreadyRan =
            ReferenceEquals(_battlefieldScriptSelectionCommitSatisfiedNode, e.Node) &&
            DateTime.UtcNow - _battlefieldScriptSelectionCommitUtc <= TimeSpan.FromSeconds(3);
        _battlefieldScriptSelectionCommitSatisfiedNode = null;
        if (!selectionGateAlreadyRan)
        {
            var result = TryCommitPendingBattlefieldConsoleChangesResult();
            if (!result.AllowsNavigation) return;
        }

        ShowSelectedBattlefieldScriptNode();
        if (TryGetBattlefieldLegacyItemData(e.Node, out var itemData) && itemData.Command != null)
        {
            EditSelectedBattlefieldScriptParameters(itemData);
            return;
        }

        SetStatus("Battlefield script: double-clicked node is not an editable legacy command.");
    }

    private bool TryResolveBattlefieldScriptCommandTarget(
        TreeNode? node,
        out BattlefieldScriptCommandTargetKey target)
    {
        if (TryGetBattlefieldLegacyItemData(node, out var itemData) && itemData.Command != null)
        {
            target = CreateBattlefieldScriptCommandTargetKey(itemData.Command);
            return true;
        }

        if (node != null && TryGetBattlefieldScriptCommandRowFromNode(node, out var row))
        {
            target = CreateBattlefieldScriptCommandTargetKey(row);
            return true;
        }

        target = default;
        return false;
    }

    private static BattlefieldScriptCommandTargetKey CreateBattlefieldScriptCommandTargetKey(LegacyScenarioCommandNode command)
        => new(
            command.SceneIndex,
            command.SectionIndex,
            command.CommandIndex,
            command.CommandId,
            command.FileOffset,
            HexDisplayFormatter.FormatOffset(command.FileOffset),
            command.CommandIdHex);

    private static BattlefieldScriptCommandTargetKey CreateBattlefieldScriptCommandTargetKey(ScenarioStructureRow row)
        => new(
            row.SceneIndex,
            row.SectionIndex,
            row.CommandIndex,
            row.CommandId,
            TryParseHexOffset(row.OffsetHex),
            row.OffsetHex,
            row.CommandIdHex);

    private static int TryParseHexOffset(string offsetHex)
        => int.TryParse(
            offsetHex.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? offsetHex[2..] : offsetHex,
            NumberStyles.HexNumber,
            CultureInfo.InvariantCulture,
            out var value)
            ? value
            : -1;

    private bool TryFindBattlefieldScriptCommandTarget(
        BattlefieldScriptCommandTargetKey target,
        out LegacyScenarioItemData itemData,
        out TreeNode? node)
    {
        node = TryFindBattlefieldScriptCommandNode(target);
        if (node != null && TryGetBattlefieldLegacyItemData(node, out itemData) && itemData.Command != null)
        {
            return true;
        }

        if (_currentBattlefieldLegacyScriptDocument != null)
        {
            var command = _currentBattlefieldLegacyScriptDocument.EnumerateCommands()
                .FirstOrDefault(command => IsSameBattlefieldScriptCommandTarget(target, command));
            if (command != null)
            {
                var row = GetLegacyScriptRowForCommand(LegacyScriptEditorScope.Battlefield, command);
                itemData = GetLegacyEditorItemData(LegacyScriptEditorScope.Battlefield, command, row);
                return true;
            }
        }

        foreach (var pair in _battlefieldScriptItemDataByCommand)
        {
            if (!IsSameBattlefieldScriptCommandTarget(target, pair.Key))
            {
                continue;
            }

            itemData = pair.Value;
            return true;
        }

        itemData = null!;
        node = null;
        return false;
    }

    private TreeNode? TryFindBattlefieldScriptCommandNode(BattlefieldScriptCommandTargetKey target)
    {
        foreach (TreeNode root in _battlefieldScriptTree.Nodes)
        {
            var found = TryFindBattlefieldScriptCommandNode(root, target);
            if (found != null) return found;
        }

        return null;
    }

    private TreeNode? TryFindBattlefieldScriptCommandNode(TreeNode node, BattlefieldScriptCommandTargetKey target)
    {
        if (TryGetBattlefieldScriptCommandRowFromNode(node, out var row) &&
            IsSameBattlefieldScriptCommandTarget(target, row))
        {
            return node;
        }

        foreach (TreeNode child in node.Nodes)
        {
            var found = TryFindBattlefieldScriptCommandNode(child, target);
            if (found != null) return found;
        }

        return null;
    }

    private static bool IsSameBattlefieldScriptCommandTarget(
        BattlefieldScriptCommandTargetKey target,
        LegacyScenarioCommandNode command)
        => target.SceneIndex == command.SceneIndex &&
           target.SectionIndex == command.SectionIndex &&
           target.CommandIndex == command.CommandIndex &&
           target.CommandId == command.CommandId &&
           (target.FileOffset < 0 ||
            command.FileOffset < 0 ||
            target.FileOffset == command.FileOffset ||
            HexDisplayFormatter.EqualsText(target.OffsetHex, HexDisplayFormatter.FormatOffset(command.FileOffset)));

    private static bool IsSameBattlefieldScriptCommandTarget(
        BattlefieldScriptCommandTargetKey target,
        ScenarioStructureRow row)
        => row.NodeType == "Command候选" &&
           target.SceneIndex == row.SceneIndex &&
           target.SectionIndex == row.SectionIndex &&
           target.CommandIndex == row.CommandIndex &&
           target.CommandId == row.CommandId &&
           (string.IsNullOrWhiteSpace(target.OffsetHex) ||
            string.IsNullOrWhiteSpace(row.OffsetHex) ||
            HexDisplayFormatter.EqualsText(target.OffsetHex, row.OffsetHex));

    private void EditSelectedBattlefieldScriptParameters(string? preferredTargetKeyOverride = null, int? preferredParameterIndexOverride = null)
    {
        if (!TryGetSelectedBattlefieldLegacyItemData(out var itemData) || itemData.Command == null)
        {
            MessageBox.Show(this, "Please select a legacy command node in the S script tree.", "Notice", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        EditSelectedBattlefieldScriptParameters(itemData, preferredTargetKeyOverride, preferredParameterIndexOverride);
    }

    private void EditSelectedBattlefieldScriptParameters(
        LegacyScenarioItemData itemData,
        string? preferredTargetKeyOverride = null,
        int? preferredParameterIndexOverride = null)
    {
        if (_editingBattlefieldLegacyCommandDialog)
        {
            MessageBox.Show(this, "请先在 S 剧本树中选择一条旧版命令。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        if (itemData.Command == null)
        {
            MessageBox.Show(this, "当前节点没有绑定旧版命令数据，无法打开旧版 Dialog_70。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        if (!LegacyCommandEditDispatcher.CanEdit(itemData.Id))
        {
            MessageBox.Show(this, "旧版源码的 OnEditModify() 没有为该命令提供修改窗口。", "该命令暂不可修改", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var command = itemData.Command;
        var oldSummary = BuildLegacyScriptParameterPreview(command);
        var beforeCommand = CaptureLegacyItemDataCommandSnapshot(itemData);
        var commandTitle = $"{command.CommandIdHex} {command.CommandName} / ord {itemData.Ord}";
        var dialogDataSources = LegacyMfcDialogDataSources.Create(_project, _tables);
        var precedingSameCommandCount = CountPrecedingSameLegacyCommands(_currentBattlefieldLegacyScriptDocument, command);
        var preferredTargetKey = ResolveBattlefieldLegacyEditPreferredTargetKey(command, preferredTargetKeyOverride, preferredParameterIndexOverride);
        var preferredParameterIndex = preferredParameterIndexOverride ?? GetSelectedBattlefieldScriptParameterRow()?.Index;
        if (BattlefieldScriptCommandEditInterceptForSmoke != null)
        {
            BattlefieldScriptCommandEditInterceptForSmoke(command);
            return;
        }

        var beforeEdit = CaptureLegacyScenarioHistorySnapshot(LegacyScriptEditorScope.Battlefield, _currentBattlefieldLegacyScriptDocument!);
        var edited = false;
        _editingBattlefieldLegacyCommandDialog = true;
        try
        {
            edited = LegacyCommandEditDispatcher.Edit(
                this,
                itemData,
                commandTitle,
                _currentBattlefieldLegacyScriptDocument?.CommandCount ?? 0,
                precedingSameCommandCount,
                dialogDataSources,
                BuildLegacyTextWrapOptions(command));
        }
        catch (Exception ex)
        {
            Debug.WriteLine("战场旧版指令修改窗口打开失败：" + ex);
            MessageBox.Show(this, ex.Message, "旧版指令修改窗口打开失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }
        finally
        {
            _editingBattlefieldLegacyCommandDialog = false;
        }

        if (!edited)
        {
            return;
        }

        CopyLegacyItemDataToCommand(itemData);
        var changed = LegacyItemDataCommandChanged(itemData, beforeCommand);
        if (changed)
        {
            PushLegacyScenarioUndoSnapshot(LegacyScriptEditorScope.Battlefield, beforeEdit);
        }
        if (!RefreshLegacyEditorCommandInPlace(LegacyScriptEditorScope.Battlefield, command, preferredParameterIndex))
        {
            RefreshBattlefieldLegacyScriptView(command, preferredParameterIndex);
        }
        RefreshBattlefieldDocumentFromLegacyScript(preferredTargetKey);
        _saveBattlefieldScriptStructureButton.Enabled = changed || _saveBattlefieldScriptStructureButton.Enabled;
        SetStatus(changed
            ? $"战场制作旧版修改指令：{commandTitle}，{oldSummary} -> {BuildLegacyScriptParameterPreview(command)}，需完整保存S剧本"
            : $"战场制作旧版修改指令：{commandTitle} 未检测到改动，已同步刷新左侧树、地图与控制台");
    }

    private bool ValidateBattlefieldLegacyDialogCommand(LegacyScenarioCommandNode command)
    {
        if (TryValidateBattlefieldLegacyDialogCommand(command, out _))
        {
            return true;
        }

        if (command.CommandId is not (0x46 or 0x47))
        {
            return true;
        }

        var definition = BattlefieldDeploymentRecordDefinition.FromCommandId(command.CommandId);
        if (definition == null)
        {
            return true;
        }

        var expectedCount = definition.RecordCount * definition.GroupSize;
        if (command.Parameters.Count < expectedCount)
        {
            MessageBox.Show(this, $"当前命令只有 {command.Parameters.Count} 个参数槽，预期 {expectedCount} 个槽，无法打开旧版 Dialog_70。", "参数数量异常", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return false;
        }

        for (var i = 0; i < expectedCount; i++)
        {
            if (command.Parameters[i].Kind == LegacyScenarioParameterKind.Word16)
            {
                continue;
            }

            MessageBox.Show(this, $"当前命令参数槽 {i} 不是 16 位数值，无法作为旧版 Dialog_70 出场设定编辑。", "参数类型异常", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return false;
        }

        return true;
    }

    private static bool TryValidateBattlefieldLegacyDialogCommand(LegacyScenarioCommandNode command, out string message)
    {
        message = string.Empty;
        if (command.CommandId is not (0x46 or 0x47))
        {
            return true;
        }

        var definition = BattlefieldDeploymentRecordDefinition.FromCommandId(command.CommandId);
        if (definition == null)
        {
            return true;
        }

        var expectedCount = definition.RecordCount * definition.GroupSize;
        if (command.Parameters.Count < expectedCount)
        {
            message = $"当前命令只有 {command.Parameters.Count} 个参数槽，预期 {expectedCount} 个槽，无法载入旧版 Dialog_70。";
            return false;
        }

        for (var i = 0; i < expectedCount; i++)
        {
            if (command.Parameters[i].Kind == LegacyScenarioParameterKind.Word16)
            {
                continue;
            }

            message = $"当前命令参数槽 {i} 不是 16 位数值，无法作为旧版 Dialog_70 出场设定编辑。";
            return false;
        }

        return true;
    }

    private string? ResolveBattlefieldLegacyEditPreferredTargetKey(
        LegacyScenarioCommandNode command,
        string? preferredTargetKeyOverride,
        int? preferredParameterIndexOverride)
    {
        if (!string.IsNullOrWhiteSpace(preferredTargetKeyOverride))
        {
            return preferredTargetKeyOverride;
        }

        var selectedTargetKey = GetSelectedBattlefieldUnitCandidate()?.TargetKey;
        if (!string.IsNullOrWhiteSpace(selectedTargetKey))
        {
            return selectedTargetKey;
        }

        var definition = BattlefieldDeploymentRecordDefinition.FromCommandId(command.CommandId);
        if (definition == null || command.CommandId is not (0x46 or 0x47))
        {
            return null;
        }

        var parameterIndex = preferredParameterIndexOverride ?? GetSelectedBattlefieldScriptParameterRow()?.Index;
        if (!parameterIndex.HasValue || parameterIndex.Value < 0)
        {
            return null;
        }

        var recordIndex = Math.Clamp(parameterIndex.Value / definition.GroupSize, 0, definition.RecordCount - 1);
        return $"Scene={command.SceneIndex.ToString(CultureInfo.InvariantCulture)};" +
               $"Section={command.SectionIndex.ToString(CultureInfo.InvariantCulture)};" +
               $"Command={command.CommandIndex.ToString(CultureInfo.InvariantCulture)};" +
               $"Offset={HexDisplayFormatter.FormatOffset(command.FileOffset)};" +
               $"Id={command.CommandIdHex};" +
               $"Record={recordIndex.ToString(CultureInfo.InvariantCulture)}";
    }

    private void EditSelectedBattlefieldDeploymentBlock(LegacyScenarioItemData itemData, int? preferredParameterIndexOverride = null)
    {
        var command = itemData.Command;
        if (command == null) return;

        var definition = BattlefieldDeploymentRecordDefinition.FromCommandId(command.CommandId);
        if (definition == null)
        {
            MessageBox.Show(this, "该命令不是 46/47 出场设定。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var expectedCount = definition.RecordCount * definition.GroupSize;
        if (command.Parameters.Count < expectedCount)
        {
            MessageBox.Show(this, $"当前命令只有 {command.Parameters.Count} 个参数槽，预期 {expectedCount} 个槽，无法打开出场块编辑器。", "参数数量异常", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        for (var i = 0; i < expectedCount; i++)
        {
            if (command.Parameters[i].Kind != LegacyScenarioParameterKind.Word16)
            {
                MessageBox.Show(this, $"当前命令参数槽 {i} 不是 16 位数值，无法作为出场块编辑。", "参数类型异常", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
        }

        var oldSummary = BuildLegacyScriptParameterPreview(command);
        var commandTitle = $"{command.CommandIdHex} {command.CommandName} / ord {itemData.Ord}";
        var dialogDataSources = LegacyMfcDialogDataSources.Create(_project, _tables);
        var precedingSameCommandCount = CountPrecedingSameLegacyCommands(_currentBattlefieldLegacyScriptDocument, command);
        var preferredParameterIndex = preferredParameterIndexOverride ?? GetSelectedBattlefieldScriptParameterRow()?.Index;
        var beforeEdit = CaptureLegacyScenarioHistorySnapshot(LegacyScriptEditorScope.Battlefield, _currentBattlefieldLegacyScriptDocument!);
        var edited = false;

        _editingBattlefieldLegacyCommandDialog = true;
        try
        {
            using var dialog = new BattlefieldDeploymentBlockEditDialog(
                commandTitle,
                command,
                dialogDataSources,
                precedingSameCommandCount,
                preferredParameterIndex);

            if (dialog.ShowDialog(this) != DialogResult.OK)
            {
                return;
            }

            if (dialog.CommittedValues.Count != expectedCount)
            {
                MessageBox.Show(this, $"出场块编辑器返回 {dialog.CommittedValues.Count} 个槽，预期 {expectedCount} 个槽。", "参数数量异常", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            for (var i = 0; i < expectedCount; i++)
            {
                command.Parameters[i].IntValue = dialog.CommittedValues[i];
                command.Parameters[i].ByteLength = 2;
            }

            CopyLegacyCommandToItemData(command, itemData);
            edited = dialog.ChangedSlotCount > 0;
            if (!edited)
            {
                SetStatus($"战场制作出场块：{commandTitle} 未检测到改动");
                return;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine("战场出场块编辑器打开失败：" + ex);
            MessageBox.Show(this, ex.Message, "出场块编辑器打开失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }
        finally
        {
            _editingBattlefieldLegacyCommandDialog = false;
        }

        PushLegacyScenarioUndoSnapshot(LegacyScriptEditorScope.Battlefield, beforeEdit);
        if (!RefreshLegacyEditorCommandInPlace(LegacyScriptEditorScope.Battlefield, command, preferredParameterIndex ?? 0))
        {
            RefreshBattlefieldLegacyScriptView(command, preferredParameterIndex ?? 0);
        }
        RefreshBattlefieldDocumentFromLegacyScript();
        _saveBattlefieldScriptStructureButton.Enabled = true;
        SetStatus($"战场制作出场块修改：{commandTitle}，{oldSummary} -> {BuildLegacyScriptParameterPreview(command)}，需完整保存S剧本");
    }

    private void RefreshBattlefieldDocumentFromLegacyScript(string? preferredTargetKey = null)
    {
        if (_project == null || _currentBattlefieldDocument == null || _currentBattlefieldLegacyScriptDocument == null) return;

        preferredTargetKey = string.IsNullOrWhiteSpace(preferredTargetKey)
            ? GetSelectedBattlefieldUnitCandidate()?.TargetKey
            : preferredTargetKey;
        _currentBattlefieldDocument = BattlefieldEditorService.RebuildFromLegacyDocument(
            _currentBattlefieldDocument,
            _currentBattlefieldLegacyScriptDocument,
            _project,
            _tables);
        _battlefieldUnitReviewService.Apply(_project, _currentBattlefieldDocument);
        ClearBattlefieldInstructionPreviewState();
        ClearBattlefieldCommand25Markers();
        MergeBattlefieldScriptPlacements(_currentBattlefieldDocument);
        PopulateBattlefieldUnitCategoryFilter(_currentBattlefieldDocument.UnitCandidates);
        BindBattlefieldUnitCandidates(GetBattlefieldUnitCandidatesForDisplay());
        BindBattlefieldCommandCandidates(GetBattlefieldCommandCandidatesForDisplay());
        var preferredTargetStillPresent = false;
        if (!string.IsNullOrWhiteSpace(preferredTargetKey))
        {
            preferredTargetStillPresent = SelectBattlefieldUnitCandidateGridRow(preferredTargetKey, updatePreview: false);
            if (preferredTargetStillPresent)
            {
                SelectBattlefieldPlacedUnitByTargetKey(preferredTargetKey, enterEdit: false, updatePreview: false);
                ReloadBattlefieldConsoleStatusAfterScriptChange(preferredTargetKey);
            }
            else
            {
                ClearBattlefieldPlacedUnitSelection();
            }
        }
        _battlefieldMapPreviewSelectedUnit = GetSelectedBattlefieldUnitCandidate();
        RefreshBattlefieldMapDynamicPreview();
        UpdateBattlefieldDeploymentWriteButton();
        _battlefieldInfoBox.Text = BuildBattlefieldInfo(_currentBattlefieldDocument) +
                                   "\r\n\r\nS 剧本旧版修改已同步到右侧候选与地图预览；点击“完整保存S剧本”前尚未写入原文件。";
        if (!string.IsNullOrWhiteSpace(preferredTargetKey) && !preferredTargetStillPresent)
        {
            SetStatus("战场制作：编辑后的出场记录已为空位，已清除右侧候选、地图与控制台的旧选择。");
        }
    }

    private bool TryGetSelectedBattlefieldLegacyItemData(out LegacyScenarioItemData itemData)
    {
        if (TryGetBattlefieldLegacyItemData(_battlefieldScriptTree.SelectedNode, out itemData))
        {
            return true;
        }

        itemData = null!;
        return false;
    }

    private bool TryGetBattlefieldLegacyItemData(TreeNode? node, out LegacyScenarioItemData itemData)
    {
        if (node?.Tag is LegacyScenarioItemData selected)
        {
            itemData = selected;
            return true;
        }

        if (node?.Tag is ScenarioStructureRow row)
        {
            if (_battlefieldScriptItemDataByRow.TryGetValue(row, out itemData!) &&
                itemData.Command != null)
            {
                return true;
            }

            if (_battlefieldScriptCommandByKey.TryGetValue(BuildLegacyCommandKey(row), out var rowCommand))
            {
                itemData = GetLegacyEditorItemData(LegacyScriptEditorScope.Battlefield, rowCommand, row);
                return true;
            }
        }

        if (TryGetBattlefieldLegacyScriptCommand(node, out var command) &&
            _battlefieldScriptItemDataByCommand.TryGetValue(command, out itemData!))
        {
            return true;
        }

        if (node != null && TryGetBattlefieldLegacyScriptCommand(node, out command))
        {
            var commandRow = node.Tag as ScenarioStructureRow ?? GetLegacyScriptRowForCommand(LegacyScriptEditorScope.Battlefield, command);
            itemData = GetLegacyEditorItemData(LegacyScriptEditorScope.Battlefield, command, commandRow);
            return true;
        }

        itemData = null!;
        return false;
    }

    private void RefreshBattlefieldLegacyScriptView(
        LegacyScenarioCommandNode? preferredSelection,
        int? preferredParameterIndex = null,
        LegacyScriptViewportSnapshot? viewportSnapshot = null,
        bool preserveViewport = true)
    {
        if (_currentBattlefieldLegacyScriptDocument == null) return;

        var viewport = preserveViewport
            ? viewportSnapshot ?? CaptureLegacyScriptViewport(LegacyScriptEditorScope.Battlefield)
            : null;
        var selectionRestored = false;
        using (SuppressBattlefieldScriptSelectionCommit())
        {
            _bindingBattlefieldScriptEditor = true;
            try
            {
                _battlefieldScriptCommandByKey.Clear();
                _battlefieldScriptTextByOffset.Clear();
                _battlefieldScriptTextEntryByOffset.Clear();
                _currentBattlefieldScriptStructure = BuildBattlefieldLegacyScriptStructureResult(_currentBattlefieldLegacyScriptDocument);
                _currentBattlefieldScriptTextEntries = BuildBattlefieldLegacyScriptTextEntries(_currentBattlefieldLegacyScriptDocument);
                BuildBattlefieldScriptTree(_currentBattlefieldScriptStructure, _currentBattlefieldScriptTextEntries);
            }
            finally
            {
                _bindingBattlefieldScriptEditor = false;
            }
        }

        if (preferredSelection != null && TrySelectBattlefieldLegacyScriptCommand(preferredSelection, ensureVisible: !preserveViewport))
        {
            ShowSelectedBattlefieldScriptNode();
            if (preferredParameterIndex.HasValue)
            {
                TrySelectBattlefieldScriptParameterRow(preferredParameterIndex.Value);
                ShowSelectedBattlefieldScriptParameter();
            }
            selectionRestored = true;
        }
        else if (preserveViewport && TryRestoreLegacyScriptSelectedNode(viewport))
        {
            ShowSelectedBattlefieldScriptNode();
            selectionRestored = true;
        }

        RestoreLegacyScriptViewport(viewport);
        if (!selectionRestored)
        {
            ClearBattlefieldScriptTextSelection();
        }
    }

    private bool TrySelectBattlefieldLegacyScriptCommand(LegacyScenarioCommandNode command, bool ensureVisible = true)
    {
        var target = _currentBattlefieldScriptStructure?.Rows.FirstOrDefault(row =>
            row.NodeType == "Command候选" &&
            row.SceneIndex == command.SceneIndex &&
            row.SectionIndex == command.SectionIndex &&
            row.CommandIndex == command.CommandIndex &&
            row.CommandId == command.CommandId);
        if (target == null)
        {
            return false;
        }

        return SelectBattlefieldScriptTreeNode(target, ensureVisible);
    }

    private bool TrySelectRSceneLegacyScriptCommand(LegacyScenarioCommandNode command, bool ensureVisible = true)
    {
        var target = FindRSceneScriptRowByCommandReference(command) ??
                     _currentRSceneScriptStructure?.Rows.FirstOrDefault(row =>
                         row.NodeType == "Command候选" &&
                         row.SceneIndex == command.SceneIndex &&
                         row.SectionIndex == command.SectionIndex &&
                         row.CommandIndex == command.CommandIndex &&
                         row.CommandId == command.CommandId);
        return target != null && SelectRSceneScriptTreeNode(target, ensureVisible);
    }

    private bool TrySelectBattlefieldScriptParameterRow(int parameterIndex)
    {
        foreach (DataGridViewRow gridRow in _battlefieldScriptParameterGrid.Rows)
        {
            if (gridRow.DataBoundItem is not ScenarioCommandParameterRow candidate || candidate.Index != parameterIndex) continue;
            gridRow.Selected = true;
            var firstVisibleCell = gridRow.Cells.Cast<DataGridViewCell>().FirstOrDefault(cell => cell.Visible);
            if (firstVisibleCell != null)
            {
                _battlefieldScriptParameterGrid.CurrentCell = firstVisibleCell;
            }

            return true;
        }

        return false;
    }

    private void ClearBattlefieldScriptTextSelection()
    {
        _selectedBattlefieldScriptTextEntry = null;
        _selectedBattlefieldScriptCommandRow = null;
        UpdateBattlefieldTextWrapLimitControl(null);
        _battlefieldScriptTextBox.Clear();
        BindBattlefieldScriptParameterRows(Array.Empty<ScenarioCommandParameterRow>());
        UpdateBattlefieldScriptTextCapacityLabel();
    }

    private void UpdateBattlefieldScriptTextCapacityLabel()
    {
        var entry = _selectedBattlefieldScriptTextEntry;
        if (entry == null)
        {
            _battlefieldScriptTextCapacityLabel.Text = "文本容量：未选择";
            _battlefieldScriptTextCapacityLabel.ForeColor = SystemColors.ControlText;
            _battlefieldScriptTextBox.BackColor = SystemColors.Window;
            _saveBattlefieldScriptTextButton.Enabled = false;
            return;
        }

        var bytes = EncodingService.GetGbkByteCount(_battlefieldScriptTextBox.Text);
        if (_battlefieldScriptTextByOffset.TryGetValue(entry.Offset, out var legacyText))
        {
            _battlefieldScriptTextCapacityLabel.Text = $"旧版文本参数：GBK {bytes} 字节；保存会完整重建 S 剧本。";
            _battlefieldScriptTextCapacityLabel.ForeColor = SystemColors.ControlText;
            _battlefieldScriptTextBox.BackColor = SystemColors.Window;
            _saveBattlefieldScriptTextButton.Enabled = !string.Equals(_battlefieldScriptTextBox.Text, legacyText.Parameter.Text, StringComparison.Ordinal);
            return;
        }

        var remaining = entry.ByteLength - bytes;
        _battlefieldScriptTextCapacityLabel.Text = $"文本容量：GBK {bytes}/{entry.ByteLength} 字节，剩佀{remaining} 字节";
        _battlefieldScriptTextCapacityLabel.ForeColor = remaining < 0 ? Color.Firebrick : SystemColors.ControlText;
        _battlefieldScriptTextBox.BackColor = remaining < 0 ? Color.MistyRose : SystemColors.Window;
        _saveBattlefieldScriptTextButton.Enabled = remaining >= 0 && !string.Equals(
            BattlefieldEditorService.NormalizeText(_battlefieldScriptTextBox.Text),
            BattlefieldEditorService.NormalizeText(entry.OriginalText),
            StringComparison.Ordinal);
    }

    private async Task SaveSelectedBattlefieldScriptTextAsync()
    {
        if (!TryCommitPendingBattlefieldConsoleChangesForSave()) return;
        if (_project == null || _currentBattlefieldDocument == null)
        {
            MessageBox.Show(this, "请先读取一个战场关卡。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var entry = _selectedBattlefieldScriptTextEntry;
        if (entry == null)
        {
            MessageBox.Show(this, "请先在左侧 S 剧本树选择一条文本。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        if (!entry.IsWritable)
        {
            MessageBox.Show(this, "该文本候选解码置信度低或来源未确认，当前只读，不能写回。", "文本只读", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        ApplyBattlefieldScriptTextEditorWrapping();
        var newText = BattlefieldEditorService.NormalizeText(_battlefieldScriptTextBox.Text);
        if (newText.Contains('\0'))
        {
            MessageBox.Show(this, "S 剧本文本不能包含 NUL/零字节。", "文本校验失败", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        var scenario = _currentBattlefieldDocument.Scenario;
        if (_currentBattlefieldLegacyScriptDocument != null && _battlefieldScriptTextByOffset.TryGetValue(entry.Offset, out var legacyText))
        {
            if (string.Equals(newText, legacyText.Parameter.Text, StringComparison.Ordinal))
            {
                MessageBox.Show(this, "选中文本没有检测到改动。", "无需保存", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            if (MessageBox.Show(this,
                    $"即将完整保存 {scenario.FileName}。\r\n\r\n文本参数：{entry.OffsetHex}\r\n保存会重廀Scene 偏移、Section/子块长度咀0x76 跳转；保存前自动备份，替换前重读校验。是否继续？",
                    "确认保存 S 剧本文本",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Question) != DialogResult.Yes)
            {
                return;
            }

            try
            {
                Cursor = Cursors.WaitCursor;
                legacyText.Parameter.Text = newText;
                entry.Text = newText;
                var dictionary = _currentSceneStringDocument ?? TryReadSceneDictionaryForProbe()
                    ?? throw new InvalidOperationException("缺少 CczString.ini，无法完成旧版结构写回校验。");
                var result = _legacyScenarioWriter.Save(
                    _project,
                    BuildScenarioRelativePath(scenario),
                    _currentBattlefieldLegacyScriptDocument,
                    dictionary,
                    "战场制作顀S 剧本文本完整保存");

            if (!RefreshLegacyEditorCommandInPlace(LegacyScriptEditorScope.Battlefield, legacyText.Command))
            {
                RefreshBattlefieldLegacyScriptView(legacyText.Command);
            }
            MarkLegacyScriptEditorSavedInPlace(LegacyScriptEditorScope.Battlefield, result);
            System.Diagnostics.Debug.WriteLine($"已从战场制作页完整保孀S 剧本文本：{scenario.FileName} offset={entry.OffsetHex} backup={result.BackupPath}");
            SetStatus($"战场制作：S 剧本文本保存完成 {scenario.FileName}");
            MessageBox.Show(this, $"完整保存完成。\r\n校验：{result.ValidationSummary}\r\n备份：{result.BackupPath}\r\n报告：{result.ReportJsonPath}", "保存完成", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                legacyText.Parameter.Text = entry.OriginalText;
                System.Diagnostics.Debug.WriteLine("战场制作页保存 S 剧本文本失败：" + ex);
                MessageBox.Show(this, ex.Message, "保存 S 剧本文本失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                Cursor = Cursors.Default;
            }

            return;
        }

        var bytes = EncodingService.GetGbkByteCount(newText);
        if (bytes > entry.ByteLength)
        {
            MessageBox.Show(this, $"GBK 字节数 {bytes} 超过原地容量 {entry.ByteLength}，请缩短后再保存。", "文本容量校验失败", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        if (MessageBox.Show(this,
                $"即将写入 RS\\{scenario.FileName} 的文最{entry.OffsetHex}。\r\n\r\n只写该文本线索，未知命令结构保持原样；保存前自动备份，保存后复读校验。是否继续？",
                "确认保存 S 剧本文本",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question) != DialogResult.Yes)
        {
            return;
        }

        try
        {
            Cursor = Cursors.WaitCursor;
            entry.Text = newText;
            var result = _scenarioTextWriter.SaveInPlace(
                _project,
                BuildScenarioRelativePath(scenario),
                new[] { entry },
                "战场制作顀S 剧本文本原地保存");
            await LoadSelectedBattlefieldScenarioAsync();
            _battlefieldScriptDetailBox.Text += $"\r\n\r\n保存完成：变匀{result.ChangedBytes} 字节。\r\n备份：{result.BackupPath}\r\n报告：{result.ReportJsonPath}";
            SetStatus($"战场制作：S 剧本文本保存完成 {scenario.FileName}");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine("战场制作页原地保存 S 剧本文本失败：" + ex);
            MessageBox.Show(this, ex.Message, "保存 S 剧本文本失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            Cursor = Cursors.Default;
        }
    }

    private async Task SaveCurrentBattlefieldLegacyScriptStructureAsync()
    {
        if (!TryCommitPendingBattlefieldConsoleChangesForSave()) return;
        if (_project == null || _currentBattlefieldDocument == null || _currentBattlefieldLegacyScriptDocument == null)
        {
            MessageBox.Show(this, "当前关卡没有进入旧版完整树模式，无法完整保存 S 剧本。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var scenario = _currentBattlefieldDocument.Scenario;
        try
        {
            Cursor = Cursors.WaitCursor;
            var dictionary = _currentSceneStringDocument ?? TryReadSceneDictionaryForProbe()
                ?? throw new InvalidOperationException("缺少 CczString.ini，无法完成旧版结构写回校验。");
            var result = await Task.Run(() => _legacyScenarioWriter.Save(
                _project,
                BuildScenarioRelativePath(scenario),
                _currentBattlefieldLegacyScriptDocument,
                dictionary,
                "战场制作顀S 剧本完整结构保存"));

            MarkLegacyScriptEditorSavedInPlace(LegacyScriptEditorScope.Battlefield, result);
            System.Diagnostics.Debug.WriteLine($"已从战场制作页完整保孀S 剧本：{scenario.FileName} backup={result.BackupPath}");
            SetStatus($"战场制作：S 剧本完整保存完成 {scenario.FileName}");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine("战场制作页完整保存 S 剧本失败：" + ex);
            MessageBox.Show(this, ex.Message, "完整保存 S 剧本失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            Cursor = Cursors.Default;
        }
    }

    private void RefreshRSceneLegacyScriptView(
        LegacyScenarioCommandNode? preferredSelection,
        LegacyScriptViewportSnapshot? viewportSnapshot = null,
        bool preserveViewport = true)
    {
        if (_currentRSceneLegacyScriptDocument == null || _currentRSceneScenario == null) return;

        var viewport = preserveViewport
            ? viewportSnapshot ?? CaptureLegacyScriptViewport(LegacyScriptEditorScope.RScene)
            : null;
        var selectionRestored = false;
        _rSceneScriptCommandByKey.Clear();
        _currentRSceneScriptStructure = BuildRSceneLegacyScriptStructureResult(_currentRSceneLegacyScriptDocument);
        _currentRSceneScriptTextEntries = BuildRSceneLegacyScriptTextEntries(_currentRSceneLegacyScriptDocument);
        BuildRSceneScriptTree(_currentRSceneScriptStructure);
        _currentRSceneCommandCandidates = BuildRSceneCommandCandidates(_currentRSceneLegacyScriptDocument);
        _currentRSceneStateCandidates = _rSceneDraftService.BuildSceneStateCandidates(_currentRSceneLegacyScriptDocument, BuildRSceneVariableSnapshotForCommand);
        BindRSceneStateCandidates(_currentRSceneStateCandidates);

        if (preferredSelection != null)
        {
            var target = FindRSceneScriptRowByCommandReference(preferredSelection) ??
                         _currentRSceneScriptStructure.Rows.FirstOrDefault(row =>
                             row.NodeType == "Command候选" &&
                             row.SceneIndex == preferredSelection.SceneIndex &&
                             row.SectionIndex == preferredSelection.SectionIndex &&
                             row.CommandIndex == preferredSelection.CommandIndex &&
                             row.CommandId == preferredSelection.CommandId);
            if (target != null)
            {
                SelectRSceneScriptTreeNode(target, ensureVisible: !preserveViewport);
                selectionRestored = true;
            }
        }

        if (!selectionRestored && preserveViewport && TryRestoreLegacyScriptSelectedNode(viewport))
        {
            ShowSelectedRSceneScriptNode();
            selectionRestored = true;
        }

        RestoreLegacyScriptViewport(viewport);
        if (!selectionRestored)
        {
            ReapplyRSceneLockedPreview();
            _rSceneScriptDetailBox.Text = BuildRSceneInfoText();
        }
    }

    private ScenarioStructureRow? FindRSceneScriptRowByCommandReference(LegacyScenarioCommandNode command)
        => _rSceneScriptItemDataByCommand.TryGetValue(command, out var itemData) &&
           itemData.UiRow is ScenarioStructureRow row
            ? row
            : null;

    private IReadOnlyList<RSceneCommandCandidate> BuildRSceneCommandCandidates(LegacyScenarioDocument document)
    {
        return BuildRSceneCommandCandidates(document, GetLegacyScenarioCommandDisplayFormatter());
    }

    private IReadOnlyList<RSceneCommandCandidate> BuildRSceneCommandCandidates(
        LegacyScenarioDocument document,
        LegacyScenarioCommandDisplayFormatter formatter)
    {
        return _rSceneDraftService.BuildCommandCandidates(
            document,
            command => formatter.FormatCommand(command, includeIdentity: false),
            command => formatter.FormatValuesPreview(command, maxVisibleValues: 8),
            ResolveRScenePersonReference);
    }

    private async Task SaveCurrentRSceneLegacyScriptStructureAsync()
    {
        if (_project == null || _currentRSceneScenario == null || _currentRSceneLegacyScriptDocument == null)
        {
            MessageBox.Show(this, "当前 R 剧情没有进入旧版完整树模式，无法完整保存 R 剧本。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var scenario = _currentRSceneScenario;
        try
        {
            Cursor = Cursors.WaitCursor;
            var dictionary = _currentSceneStringDocument ?? TryReadSceneDictionaryForProbe()
                ?? throw new InvalidOperationException("缺少 CczString.ini，无法完成旧版结构写回校验。");
            var result = await Task.Run(() => _legacyScenarioWriter.Save(
                _project,
                BuildScenarioRelativePath(scenario),
                _currentRSceneLegacyScriptDocument,
                dictionary,
                "R场景制作顀R 剧本完整结构保存"));

            MarkLegacyScriptEditorSavedInPlace(LegacyScriptEditorScope.RScene, result);
            System.Diagnostics.Debug.WriteLine($"已从 R 场景制作页完整保孀R 剧本：{scenario.FileName} backup={result.BackupPath}");
            SetStatus($"R场景制作：R 剧本完整保存完成 {scenario.FileName}");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine("R 场景制作页完整保存 R 剧本失败：" + ex);
            MessageBox.Show(this, ex.Message, "完整保存 R 剧本失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            Cursor = Cursors.Default;
        }
    }

    private void ApplyBattlefieldScriptSearch()
        => ApplyLegacyScriptSearch(LegacyScriptEditorScope.Battlefield);

    private void ClearBattlefieldScriptSearch()
        => ClearLegacyScriptSearch(LegacyScriptEditorScope.Battlefield);

    private void DrawBattlefieldCoordinateMarker(Graphics graphics, Size imageSize, int gridX, int gridY, int gridWidth, int gridHeight, Color markerColor, string label)
    {
        if (gridWidth <= 0 || gridHeight <= 0) return;
        var cellWidth = imageSize.Width / (float)gridWidth;
        var cellHeight = imageSize.Height / (float)gridHeight;
        var centerX = gridX * cellWidth + cellWidth / 2;
        var centerY = gridY * cellHeight + cellHeight / 2;
        var radius = Math.Max(8f, Math.Min(cellWidth, cellHeight) * 0.42f);
        using var shadowPen = new Pen(Color.Black, 5);
        using var markerPen = new Pen(markerColor, 3);
        using var brush = new SolidBrush(Color.FromArgb(160, Color.Yellow));
        graphics.FillEllipse(brush, centerX - radius, centerY - radius, radius * 2, radius * 2);
        graphics.DrawEllipse(shadowPen, centerX - radius, centerY - radius, radius * 2, radius * 2);
        graphics.DrawEllipse(markerPen, centerX - radius, centerY - radius, radius * 2, radius * 2);
        graphics.DrawString($"{label}:{gridX},{gridY}", Font, Brushes.White, centerX + radius + 2, centerY - radius);
    }

    private void DrawBattlefieldCommand25Markers(Graphics graphics, Size imageSize, int gridWidth, int gridHeight)
    {
        if (_battlefieldCommand25Markers.Count == 0 || gridWidth <= 0 || gridHeight <= 0) return;

        foreach (var marker in _battlefieldCommand25Markers)
        {
            if (marker.GridX < 0 || marker.GridX >= gridWidth || marker.GridY < 0 || marker.GridY >= gridHeight) continue;
            var label = marker.Count > 1 ? "25x" + marker.Count.ToString(CultureInfo.InvariantCulture) : "25";
            DrawBattlefieldCoordinateMarker(graphics, imageSize, marker.GridX, marker.GridY, gridWidth, gridHeight, Color.MediumVioletRed, label);
        }
    }

    private bool TryHitBattlefieldCommand25Marker(Point location, out BattlefieldCommand25Marker marker)
    {
        marker = null!;
        if (_battlefieldCommand25Markers.Count == 0) return false;
        if (!TryMapPreviewPointToGrid(location, out var x, out var y)) return false;

        marker = _battlefieldCommand25Markers
            .Where(item => item.GridX == x && item.GridY == y)
            .OrderBy(item => item.Command.CommandOrdinal)
            .FirstOrDefault()!;
        return marker != null;
    }

    private void JumpToBattlefieldCommand25Marker(BattlefieldCommand25Marker marker)
    {
        var row = FindBattlefieldScriptCommandRow(marker.Command);
        if (row == null)
        {
            SetStatus($"战场制作：未在左侧 S 剧本树中找到指定地点测试 ({marker.GridX},{marker.GridY})。");
            return;
        }

        SelectBattlefieldScriptCommandRow(
            row,
            "从地图指定地点测试预览跳转：\r\n" +
            $"坐标=({marker.GridX},{marker.GridY})，该坐标命令数={marker.Count.ToString(CultureInfo.InvariantCulture)}，跳转到第一条。");
    }

    private ScenarioStructureRow? FindBattlefieldScriptCommandRow(LegacyScenarioCommandNode command)
        => _currentBattlefieldScriptStructure?.Rows.FirstOrDefault(row =>
            row.NodeType == "Command候选" &&
            row.SceneIndex == command.SceneIndex &&
            row.SectionIndex == command.SectionIndex &&
            row.CommandIndex == command.CommandIndex &&
            row.CommandId == command.CommandId &&
            HexDisplayFormatter.EqualsText(row.OffsetHex, HexDisplayFormatter.FormatOffset(command.FileOffset)));

    private void DrawBattlefieldSelectedCoordinateMarker(Graphics graphics, Size imageSize, int gridWidth, int gridHeight)
    {
        var selectedUnit = _battlefieldMapPreviewSelectedUnit;
        if (selectedUnit == null || gridWidth <= 0 || gridHeight <= 0) return;

        if (selectedUnit.TargetKey.Equals(_battlefieldManualMarkerTargetKey, StringComparison.OrdinalIgnoreCase) &&
            _battlefieldManualMarkerX >= 0 &&
            _battlefieldManualMarkerY >= 0)
        {
            if (_battlefieldManualMarkerX < gridWidth && _battlefieldManualMarkerY < gridHeight)
            {
                DrawBattlefieldCoordinateMarker(graphics, imageSize, _battlefieldManualMarkerX, _battlefieldManualMarkerY, gridWidth, gridHeight, Color.DeepSkyBlue, "点选");
            }

            return;
        }

        if (!BattlefieldEditorService.TryExtractFirstCoordinate(selectedUnit, out var gridX, out var gridY)) return;
        if (gridX < 0 || gridX >= gridWidth || gridY < 0 || gridY >= gridHeight) return;

        DrawBattlefieldCoordinateMarker(graphics, imageSize, gridX, gridY, gridWidth, gridHeight, Color.Red, "候选");
    }

    private void UpdateBattlefieldCapacityLabels()
    {
        if (_currentBattlefieldDocument == null)
        {
            _battlefieldTitleBytesLabel.Text = "标题容量：未读取";
            _battlefieldConditionsBytesLabel.Text = "胜败条件容量：未读取";
            _battlefieldTitleBytesLabel.ForeColor = SystemColors.ControlText;
            _battlefieldConditionsBytesLabel.ForeColor = SystemColors.ControlText;
            _battlefieldTitleBox.BackColor = SystemColors.Window;
            _battlefieldConditionsBox.BackColor = SystemColors.Window;
            return;
        }

        _battlefieldTitleBytesLabel.Text = "标题容量：" +
                                           BattlefieldEditorService.FormatCapacity(_currentBattlefieldDocument.TitleEntry, _battlefieldTitleBox.Text);
        _battlefieldConditionsBytesLabel.Text = "胜败条件容量：" +
                                                 BattlefieldEditorService.FormatCapacity(_currentBattlefieldDocument.ConditionEntry, _battlefieldConditionsBox.Text);
        SetBattlefieldCapacityStyle(
            _battlefieldTitleBytesLabel,
            _battlefieldTitleBox,
            BattlefieldEditorService.ValidateTextForEntry(_currentBattlefieldDocument.TitleEntry, _battlefieldTitleBox.Text, "关卡标题"));
        SetBattlefieldCapacityStyle(
            _battlefieldConditionsBytesLabel,
            _battlefieldConditionsBox,
            BattlefieldEditorService.ValidateTextForEntry(_currentBattlefieldDocument.ConditionEntry, _battlefieldConditionsBox.Text, "胜败条件"));
    }

    private static void SetBattlefieldCapacityStyle(Label label, TextBox box, string? validationError)
    {
        var ok = string.IsNullOrWhiteSpace(validationError);
        label.ForeColor = ok ? Color.DarkGreen : Color.DarkRed;
        box.BackColor = ok ? SystemColors.Window : Color.MistyRose;
        if (!ok && !label.Text.Contains("警告", StringComparison.Ordinal))
        {
            label.Text += "；警告：" + validationError;
        }
    }

    private string BuildBattlefieldInfo(BattlefieldEditorDocument document)
    {
        return
            $"{document.Summary}\r\n" +
            $"标题：{BattlefieldEditorService.FormatCapacity(document.TitleEntry, _battlefieldTitleBox.Text)}\r\n" +
            $"胜负条件：{BattlefieldEditorService.FormatCapacity(document.ConditionEntry, _battlefieldConditionsBox.Text)}\r\n" +
            $"地图引用：{document.MapReference.DisplayText}\r\n" +
            $"出场/坐标候选：{document.UnitCandidates.Count} 条；战场命令定位：{document.CommandCandidates.Count} 条。\r\n" +
            $"地图预览单位：{_battlefieldPlacedUnits.Count} 个；我军候选出战位：{_battlefieldAllyDeploymentSlots.Count} 个（强制 {_battlefieldAllyDeploymentSlots.Count(slot => slot.IsForced)} 个）。\r\n" +
            $"说明：{document.Annotation}";
    }

    private void SaveBattlefieldUnitReviews()
    {
        if (!TryCommitPendingBattlefieldConsoleChanges()) return;
        if (_project == null || _currentBattlefieldDocument == null)
        {
            MessageBox.Show(this, "请先读取一个战场关卡。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        try
        {
            _battlefieldUnitGrid.EndEdit();
            var rows = GetBattlefieldUnitCandidatesForDisplay().ToList();
            var path = _battlefieldUnitReviewService.Save(_project, _currentBattlefieldDocument, rows, _battlefieldPlacedUnits);
            _battlefieldInfoBox.Text = BuildBattlefieldInfo(_currentBattlefieldDocument) +
                                       $"\r\n\r\n出场/坐标候选核对和地图摆放已保存：{path}\r\n说明：该文件是项目侧 JSON，不写入 R/S eex，不参与发布封包。";
            SetStatus($"战场制作：已保存出场核对 {rows.Count} 条，摆放 {_battlefieldPlacedUnits.Count} 个");
            System.Diagnostics.Debug.WriteLine($"已保存战场出场核对：{_currentBattlefieldDocument.Scenario.FileName} path={path}");
            MessageBox.Show(this, $"出场/坐标候选核对和地图摆放已保存。\r\n{path}", "保存出场核对完成", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine("保存战场出场核对失败：" + ex);
            MessageBox.Show(this, ex.Message, "保存出场核对失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private async Task WriteBattlefieldDeploymentAsync()
    {
        if (!TryCommitPendingBattlefieldConsoleChangesForSave()) return;
        if (_project == null || _currentBattlefieldDocument == null)
        {
            MessageBox.Show(this, "请先读取一个战场关卡。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        if (_currentBattlefieldLegacyScriptDocument == null)
        {
            MessageBox.Show(this, "Current S script is not in legacy full-tree mode; deployment cannot be saved from the left script tree.", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var writableCount = _battlefieldPlacedUnits.Count(BattlefieldDeploymentWriteService.IsScriptPlacementWritable);
        if (writableCount == 0 && IsLegacyStructureDirty(LegacyScriptEditorScope.Battlefield))
        {
            if (MessageBox.Show(this,
                    $"即将把当前左侀S 剧本树中的出场删陀清空改动保存刀RS\\{_currentBattlefieldDocument.Scenario.FileName}。\r\n\r\n当前地图上没有可再次校准的摆放项，将直接保存内存中的 S 剧本树；保存前自动备份，保存后复读校验。是否继续？",
                    "确认保存当前 S 剧本树",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Question) != DialogResult.Yes)
            {
                return;
            }

            await SaveCurrentBattlefieldLegacyScriptStructureAsync();
            return;
        }

        if (writableCount == 0)
        {
            MessageBox.Show(this,
                "当前地图摆放没有可写回的 S 剧本出场记录。\r\n\r\n可写回对象必须自动或手动绑定到 S 剧本 46/47/4B 出场设置槽，且 TargetKey 含 Scene/Section/Command/Record。纯拖放会优先匹配同阵营同人物已有槽、空 46/47 槽或可用 4B 出战位；找不到槽时才只保存为布阵草稿。",
                "没有可写回记录",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return;
        }

        if (MessageBox.Show(this,
                $"即将写入 RS\\{_currentBattlefieldDocument.Scenario.FileName} 皀46/47/4B 出场设置槽。\r\n\r\n可写回记录：{writableCount} 条。\r\n写回内容＀6/47 写人物编号、坐标和已确讀AI＀B 只写坐标、方向、隐藏标志，不改第一个出战顺序槽。等纀装备/未知状态槽保持原值。保存前自动备份，保存后按旧版树复读校验。是否继续？",
                "确认写回出场刀S 剧本",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question) != DialogResult.Yes)
        {
            return;
        }

        try
        {
            Cursor = Cursors.WaitCursor;
            _battlefieldUnitGrid.EndEdit();
            var scenarioFileName = _currentBattlefieldDocument.Scenario.FileName;
            var rows = GetBattlefieldUnitCandidatesForDisplay().ToList();
            var dictionary = _currentSceneStringDocument ?? TryReadSceneDictionaryForProbe()
                ?? throw new InvalidOperationException("缺少 CczString.ini，无法按旧版完整树写回并校验 S 剧本。");
            var result = _battlefieldDeploymentWriteService.SaveScriptPlacements(
                _project,
                _currentBattlefieldDocument.Scenario,
                dictionary,
                _currentBattlefieldLegacyScriptDocument,
                _battlefieldPlacedUnits);
            var notePath = _battlefieldUnitReviewService.Save(_project, _currentBattlefieldDocument, rows, _battlefieldPlacedUnits);

            ReloadBattlefieldScenarioAfterWrite(scenarioFileName, dictionary);
            _battlefieldInfoBox.Text =
                BuildBattlefieldInfo(_currentBattlefieldDocument!) +
                $"\r\n\r\n出场记录已真实写囀RS\\{scenarioFileName}：{result.WrittenRecordCount} 条，跳过 {result.SkippedRecordCount} 条，变化 {result.ChangedBytes} 字节。\r\n" +
                $"校验：{result.ValidationSummary}\r\n" +
                $"项目侧核寀摆放 JSON：{notePath}\r\n" +
                $"备份：{result.BackupPath}\r\n" +
                $"报告：{result.ReportJsonPath}\r\n" +
                BuildBattlefieldDeploymentWriteDetail(result);
            System.Diagnostics.Debug.WriteLine($"已写回战场出场记录：{scenarioFileName} records={result.WrittenRecordCount} backup={result.BackupPath}");
            SetStatus($"战场制作：出场记录写回完戀{scenarioFileName} records={result.WrittenRecordCount}");
            MessageBox.Show(this,
                $"写回完成：{result.WrittenRecordCount} 条出场记录。\r\n校验：{result.ValidationSummary}\r\n备份：{result.BackupPath}\r\n报告：{result.ReportJsonPath}",
                "写回出场完成",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine("写回战场出场记录失败：" + ex);
            MessageBox.Show(this, ex.Message, "写回出场失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            Cursor = Cursors.Default;
        }

        await Task.CompletedTask;
    }

    private static string BuildBattlefieldDeploymentWriteDetail(BattlefieldDeploymentWriteResult result)
    {
        var parts = new List<string>();
        if (result.Changes.Count > 0)
        {
            parts.Add("具体写回：");
            parts.AddRange(result.Changes.Take(12).Select(change => "- " + change.Summary));
            if (result.Changes.Count > 12) parts.Add($"- ... 其余 {result.Changes.Count - 12} 条略。");
        }

        if (result.SkippedReasons.Count > 0)
        {
            parts.Add("跳过原因：");
            parts.AddRange(result.SkippedReasons.Take(8).Select(reason => "- " + reason));
            if (result.SkippedReasons.Count > 8) parts.Add($"- ... 其余 {result.SkippedReasons.Count - 8} 条略。");
        }

        return parts.Count == 0 ? string.Empty : "\r\n" + string.Join("\r\n", parts);
    }

    private void UpdateBattlefieldDeploymentWriteButton()
        => _writeBattlefieldDeploymentButton.Enabled =
            _currentBattlefieldDocument != null &&
            _currentBattlefieldLegacyScriptDocument != null &&
            (_battlefieldPlacedUnits.Any(BattlefieldDeploymentWriteService.IsScriptPlacementWritable) ||
             IsLegacyStructureDirty(LegacyScriptEditorScope.Battlefield));

    private void SaveBattlefieldTexts()
    {
        if (!TryCommitPendingBattlefieldConsoleChanges()) return;
        if (_project == null || _currentBattlefieldDocument == null)
        {
            MessageBox.Show(this, "请先读取一个战场关卡。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var errors = new List<string>();
        var titleError = BattlefieldEditorService.ValidateTextForEntry(_currentBattlefieldDocument.TitleEntry, _battlefieldTitleBox.Text, "关卡标题");
        if (_currentBattlefieldDocument.TitleEntry != null && titleError != null) errors.Add(titleError);
        var conditionError = BattlefieldEditorService.ValidateTextForEntry(_currentBattlefieldDocument.ConditionEntry, _battlefieldConditionsBox.Text, "胜败条件");
        if (_currentBattlefieldDocument.ConditionEntry != null && conditionError != null) errors.Add(conditionError);
        if (errors.Count > 0)
        {
            MessageBox.Show(this, string.Join("\r\n", errors), "文本容量校验失败", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        var changed = false;
        if (_currentBattlefieldDocument.TitleEntry != null)
        {
            changed |= !string.Equals(
                BattlefieldEditorService.NormalizeText(_battlefieldTitleBox.Text),
                BattlefieldEditorService.NormalizeText(_currentBattlefieldDocument.TitleEntry.OriginalText),
                StringComparison.Ordinal);
        }

        if (_currentBattlefieldDocument.ConditionEntry != null)
        {
            changed |= !string.Equals(
                BattlefieldEditorService.NormalizeText(_battlefieldConditionsBox.Text),
                BattlefieldEditorService.NormalizeText(_currentBattlefieldDocument.ConditionEntry.OriginalText),
                StringComparison.Ordinal);
        }

        if (!changed)
        {
            MessageBox.Show(this, "标题和胜败条件没有检测到改动。", "无需保存", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        if (MessageBox.Show(this,
                $"即将写入 RS\\{_currentBattlefieldDocument.Scenario.FileName}。\r\n\r\n当前仅写回已匹配的标颀胜败条件文本，未知命令结构保持原样；保存前自动备份，保存后复读校验。是否继续？",
                "确认保存战场文本",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question) != DialogResult.Yes)
        {
            return;
        }

        try
        {
            Cursor = Cursors.WaitCursor;
            var scenarioFileName = _currentBattlefieldDocument.Scenario.FileName;
            var dictionary = _currentSceneStringDocument ?? TryReadSceneDictionaryForProbe();
            var result = _battlefieldEditorService.SaveTitleAndConditions(
                _project,
                _currentBattlefieldDocument,
                _battlefieldTitleBox.Text,
                _battlefieldConditionsBox.Text,
                dictionary);
            var reread = _scenarioTextReader.Read(result.FilePath);
            if (_currentBattlefieldDocument.TitleEntry != null)
            {
                VerifyBattlefieldText(reread, _currentBattlefieldDocument.TitleEntry.Offset, _battlefieldTitleBox.Text, "标题");
            }
            if (_currentBattlefieldDocument.ConditionEntry != null)
            {
                VerifyBattlefieldText(reread, _currentBattlefieldDocument.ConditionEntry.Offset, _battlefieldConditionsBox.Text, "胜败条件");
            }

            ReloadBattlefieldScenarioAfterWrite(scenarioFileName, dictionary);
            _battlefieldInfoBox.Text =
                BuildBattlefieldInfo(_currentBattlefieldDocument) +
                $"\r\n\r\n保存完成：写兀{result.EntriesWritten} 条，变化 {result.ChangedBytes} 字节。\r\n备份：{result.BackupPath}\r\n报告：{result.ReportJsonPath}";
            System.Diagnostics.Debug.WriteLine($"已保存战场文本：{scenarioFileName} entries={result.EntriesWritten} backup={result.BackupPath}");
            SetStatus($"战场制作保存完成：{scenarioFileName}");
            MessageBox.Show(this, $"保存完成。\r\n备份：{result.BackupPath}\r\n报告：{result.ReportJsonPath}", "战场制作保存完成", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine("保存战场制作文本失败：" + ex);
            MessageBox.Show(this, ex.Message, "保存战场制作失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            Cursor = Cursors.Default;
        }
    }

    private void ReloadBattlefieldScenarioAfterWrite(string scenarioFileName, SceneStringDocument? dictionary)
    {
        if (_project == null) return;
        EnsureBattlefieldBaseDataLoaded();
        var scenario = _currentScenarioFiles.First(x => x.FileName.Equals(scenarioFileName, StringComparison.OrdinalIgnoreCase));
        dictionary ??= _currentSceneStringDocument ?? TryReadSceneDictionaryForProbe();
        _currentBattlefieldDocument = _battlefieldEditorService.Load(_project, scenario, dictionary, _tables);
        ClearBattlefieldInstructionPreviewState();
        _battlefieldUnitReviewService.Apply(_project, _currentBattlefieldDocument);
        _battlefieldPlacedUnits.Clear();
        _battlefieldPlacedUnits.AddRange(_battlefieldUnitReviewService.LoadPlacements(_project, _currentBattlefieldDocument));
        ClearBattlefieldBatchEditingState(syncControls: false);
        ClearBattlefieldPlacedUnitSelection();
        LoadBattlefieldUnitPalette();
        LoadBattlefieldAllyDeploymentSlots(scenario, dictionary);
        MergeBattlefieldScriptPlacements(_currentBattlefieldDocument);
        if (dictionary != null)
        {
            LoadBattlefieldScriptView(scenario, dictionary);
        }

        _battlefieldTitleBox.Text = _currentBattlefieldDocument.TitleEntry?.Text ?? scenario.TitleHint;
        _battlefieldTitleBox.ReadOnly = _currentBattlefieldDocument.TitleEntry == null;
        _battlefieldConditionsBox.Text = _currentBattlefieldDocument.ConditionEntry?.Text ?? string.Empty;
        _battlefieldConditionsBox.ReadOnly = _currentBattlefieldDocument.ConditionEntry == null;
        PopulateBattlefieldUnitCategoryFilter(_currentBattlefieldDocument.UnitCandidates);
        BindBattlefieldUnitCandidates(GetBattlefieldUnitCandidatesForDisplay());
        BindBattlefieldCommandCandidates(GetBattlefieldCommandCandidatesForDisplay());
        RenderBattlefieldMapPreview(_currentBattlefieldDocument);
        UpdateBattlefieldCapacityLabels();
        _saveBattlefieldTextsButton.Enabled = _currentBattlefieldDocument.TitleEntry != null || _currentBattlefieldDocument.ConditionEntry != null;
        _saveBattlefieldUnitReviewsButton.Enabled = _currentBattlefieldDocument.UnitCandidates.Count > 0;
        UpdateBattlefieldDeploymentWriteButton();
        _jumpBattlefieldMapButton.Enabled = HasBattlefieldMapResource(_currentBattlefieldDocument);
        _jumpBattlefieldScenarioButton.Enabled = true;
    }

    private static void VerifyBattlefieldText(IReadOnlyList<ScenarioTextEntry> reread, int offset, string expectedText, string displayName)
    {
        var actual = reread.FirstOrDefault(x => x.Offset == offset);
        var expected = BattlefieldEditorService.NormalizeText(expectedText);
        if (actual == null || !BattlefieldEditorService.NormalizeText(actual.Text).Equals(expected, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"{displayName}保存后复读校验失败：期望“{expected}”，实际“{actual?.Text ?? "<未找到>"}”。");
        }
    }

    private void JumpBattlefieldMapMaker()
    {
        var document = _currentBattlefieldDocument;
        if (document == null) return;

        var mapId = document.MapReference.MapId;
        var map = string.IsNullOrWhiteSpace(mapId) ? null : FindBattlefieldMapResourceByMapId(mapId);
        if (map == null)
        {
            MessageBox.Show(this, document.MapReference.DisplayText + "：没有可跳转的 Map/Mxxx.jpg 地图图片。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        SelectTabPageByText("地图编辑");
        if (_mapImageList.Items.Count == 0) LoadMapImages();
        if (!SelectMapImageByName(map.Name))
        {
            MessageBox.Show(this, "地图编辑列表中没有找到对应地图图片：" + map.Name, "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
    }

    private async Task JumpBattlefieldScenarioStructureAsync()
    {
        var scenario = _currentBattlefieldDocument?.Scenario;
        if (scenario == null) return;
        if (await SelectScriptScenarioByNameAsync(scenario.FileName))
        {
            SetStatus($"已从战场编辑跳转到剧本编辑：{scenario.FileName}");
        }
        else
        {
            SelectTabPageByText("剧本编辑");
            MessageBox.Show(this, "剧本编辑页没有找到对应关卡：" + scenario.FileName, "无法跳转", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
    }
}
