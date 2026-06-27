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

    private static IReadOnlyDictionary<byte, string> BuildTerrainNameLookupForBackground(CczProject project)
    {
        try
        {
            var materials = new MaterialLibraryIndexer().Index(project);
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
            _battlefieldUnitReviewService.Apply(_project, _currentBattlefieldDocument);
            _battlefieldPlacedUnits.Clear();
            _battlefieldPlacedUnits.AddRange(_battlefieldUnitReviewService.LoadPlacements(_project, _currentBattlefieldDocument));
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
            _battlefieldInfoBox.Text = BuildBattlefieldInfo(_currentBattlefieldDocument);
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
            _battlefieldInfoBox.Text = BuildBattlefieldInfo(_currentBattlefieldDocument);
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
            "从右侧出场/坐标候选双击定位：\r\n" +
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
            var definition = DeploymentBlockDefinition.FromCommandId(commandId);
            var preferredParameterIndex = definition == null || recordIndex < 0
                ? (int?)null
                : recordIndex * definition.Stride;
            EditSelectedBattlefieldDeploymentBlock(itemData, preferredParameterIndex);
            return;
        }

        if (commandId == 0x4B)
        {
            EditSelectedBattlefieldScriptParameters();
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

        _battlefieldScriptTree.SelectedNode = found;
        if (ensureVisible)
        {
            found.EnsureVisible();
        }
        return true;
    }

    private bool SelectBattlefieldUnitCandidateGridRow(string targetKey)
    {
        if (string.IsNullOrWhiteSpace(targetKey)) return false;
        if (TrySelectBattlefieldUnitCandidateGridRow(targetKey)) return true;

        if (_currentBattlefieldDocument == null ||
            !GetBattlefieldUnitCandidatesForDisplay().Any(candidate => candidate.TargetKey.Equals(targetKey, StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        BindBattlefieldUnitCandidates(GetBattlefieldUnitCandidatesForDisplay());
        return TrySelectBattlefieldUnitCandidateGridRow(targetKey);
    }

    private bool TrySelectBattlefieldUnitCandidateGridRow(string targetKey)
    {
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
                RenderBattlefieldMapPreview(_currentBattlefieldDocument, GetSelectedBattlefieldUnitCandidate());
                _battlefieldInfoBox.Text =
                    BuildBattlefieldInfo(_currentBattlefieldDocument) +
                    $"\r\n\r\n我军候选出战位：#{slot.DisplayOrder}  坐标=({slot.GridX},{slot.GridY})  {(slot.IsForced ? "强制出战" : "候选")}\r\n" +
                    (slot.IsForced
                        ? $"强制角色：{slot.PersonId} {slot.Name}  职业={slot.JobId?.ToString(CultureInfo.InvariantCulture) ?? "?"} {slot.JobName}  S={slot.SImageId}\r\n"
                        : "未绑定强制角色，保留为战前候选位。\r\n") +
                    $"来源：{slot.Source}\r\n" +
                    $"命令：{slot.SourceFileName} / {slot.SourceLocator}\r\n" +
                    $"原始 4B 槽值：{slot.SourceValues}";
                SetStatus($"战场布阵：已选中我军候选出战位 #{slot.DisplayOrder} ({x},{y})");
                return;
            }

            if (e.Button == MouseButtons.Left)
            {
                ClearBattlefieldPlacedUnitSelection();
                RenderBattlefieldMapPreview(_currentBattlefieldDocument, GetSelectedBattlefieldUnitCandidate());
            }
            SetStatus($"战场布阵：({x},{y}) 没有已摆放单位。");
            return;
        }

        var enterEdit = e.Button == MouseButtons.Right;
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
                "该单位没有绑定到 0x46 友军出场设定或 0x47 敌军出场设定，不能直接写回状态。\r\n\r\n请双击由 S 剧本 46/47 自动加载或拖放时已绑定到 46/47 记录的友军/敌军单位。",
                "不能写回状态",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return;
        }

        try
        {
            Cursor = Cursors.WaitCursor;
            var scenarioFileName = _currentBattlefieldDocument.Scenario.FileName;
            var dictionary = _currentSceneStringDocument ?? TryReadSceneDictionaryForProbe()
                ?? throw new InvalidOperationException("缺少 CczString.ini，无法按旧版完整树写回并校验 S 剧本。");
            var draft = _battlefieldUnitStatusWriteService.LoadDraft(
                _currentBattlefieldDocument.Scenario,
                dictionary,
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
                    $"即将写入 RS\\{scenarioFileName}。\r\n\r\n46/47 等级加成、兵种级、AI 方针是出场记录字段；48 装备按部署段写入；52 兵种和 38 五维是脚本运行指令，不是出场记录或 Data.e5 永久人物表字段。52 会按旧资料自动包裹 4081 能力重算开关，但战场初始显示是否变化仍取决于该 Section/子事件是否在单位生成后执行。\r\n保存前会自动备份，保存后复读校验脚本结构。是否继续？",
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
            SetStatus($"战场单位状态：已写回 {unit.Name}({unit.PersonId}) -> {scenarioFileName}");
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

        var titles = new[]
        {
            BattlefieldUnitStatusWriteService.CombinedStatusBlockTitle,
            BattlefieldUnitStatusWriteService.EquipmentStatusBlockTitle,
            BattlefieldUnitStatusWriteService.RuntimeStatusBlockTitle
        };
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
    {
        if (_draggingBattlefieldPlacedUnit == null || _battlefieldPlacedUnitDragStart == null) return;
        if ((e.Button & (MouseButtons.Left | MouseButtons.Right)) == 0) return;
        if (!TryMapPreviewPointToGrid(e.Location, out var x, out var y)) return;
        if (_draggingBattlefieldPlacedUnit.GridX == x && _draggingBattlefieldPlacedUnit.GridY == y) return;

        _draggingBattlefieldPlacedUnit.GridX = x;
        _draggingBattlefieldPlacedUnit.GridY = y;
        _battlefieldPlacedUnitDragMoved = true;
        if (_currentBattlefieldDocument != null)
        {
            RenderBattlefieldMapPreview(_currentBattlefieldDocument, GetSelectedBattlefieldUnitCandidate());
        }
        SetStatus($"战场布阵：拖动 {_draggingBattlefieldPlacedUnit.Name} -> ({x},{y})");
    }

    private void EndBattlefieldPlacedUnitInteraction()
    {
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
                RenderBattlefieldMapPreview(_currentBattlefieldDocument, GetSelectedBattlefieldUnitCandidate());
            }
            SetStatus($"战场布阵：目标格 ({occupied.GridX},{occupied.GridY}) 已有 {occupied.Name}，已取消移动。");
            return;
        }

        unit.PlacementNote = BattlefieldUnitReviewService.AppendReviewLine(
            unit.PlacementNote,
            $"地图拖拽：({oldGrid.X},{oldGrid.Y}) -> ({unit.GridX},{unit.GridY})。");
        var synced = SyncBattlefieldInstructionPreviewAfterPlacement(unit, "地图拖动");
        if (_currentBattlefieldDocument != null)
        {
            RenderBattlefieldMapPreview(_currentBattlefieldDocument, GetSelectedBattlefieldUnitCandidate());
            _battlefieldInfoBox.Text =
                BuildBattlefieldInfo(_currentBattlefieldDocument) +
                $"\r\n\r\n已移动地图单位：\r\n" +
                $"{unit.PersonId} {unit.Name}  坐标=({oldGrid.X},{oldGrid.Y}) -> ({unit.GridX},{unit.GridY})  阵营={unit.Faction}\r\n" +
                $"职业={unit.JobId?.ToString(CultureInfo.InvariantCulture) ?? "?"} {unit.JobName}  R={unit.RImageId}  S={unit.SImageId}\r\n" +
                $"等级={unit.LevelMode}+{unit.LevelOffset}  AI={unit.AiMode}  隐藏={unit.Hidden}  转向={unit.Direction}\r\n" +
                (synced
                    ? "右侧候选和左侧 S 剧本预览已同步；点击“写回出场到S剧本”后写入指令。"
                    : "未绑定到可写 46/47/4B 出场设置；只能保存为布阵草稿记录。");
        }

        _saveBattlefieldUnitReviewsButton.Enabled = true;
        UpdateBattlefieldDeploymentWriteButton();
        SetStatus(synced
            ? $"战场布阵：已移动 {unit.Name} -> ({unit.GridX},{unit.GridY})，候选和 S 剧本预览已同步，尚未写回。"
            : $"战场布阵：已移动 {unit.Name} -> ({unit.GridX},{unit.GridY})；未绑定可写出场设置。");
    }

    private void SelectBattlefieldPlacedUnit(BattlefieldPlacedUnit unit, bool enterEdit)
    {
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
        SelectBattlefieldUnitCandidateGridRow(unit.TargetKey);
        RenderBattlefieldMapPreview(document, GetSelectedBattlefieldUnitCandidate());
        _battlefieldInfoBox.Text =
            BuildBattlefieldInfo(document) +
            $"\r\n\r\n当前地图单位：\r\n" +
            $"{unit.PersonId} {unit.Name}  坐标=({unit.GridX},{unit.GridY})  阵营={unit.Faction}\r\n" +
            $"职业={unit.JobId?.ToString(CultureInfo.InvariantCulture) ?? "?"} {unit.JobName}  R={unit.RImageId}  S={unit.SImageId}\r\n" +
            $"等级={unit.LevelMode}+{unit.LevelOffset}  AI={unit.AiMode}  隐藏={unit.Hidden}  转向={unit.Direction}\r\n" +
            $"状态：{(ReferenceEquals(unit, _editingBattlefieldPlacedUnit) ? "可编辑，拖拽后可同步 46/47/4B 出场设置预览" : "已选中，右键进入可编辑状态")}\r\n" +
            $"来源：{unit.Source}\r\n" +
            $"布阵记录：{unit.PlacementNote}";
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
        _selectedBattlefieldPlacedUnit = null;
        _editingBattlefieldPlacedUnit = null;
        _draggingBattlefieldPlacedUnit = null;
        _battlefieldPlacedUnitDragStart = null;
        _battlefieldPlacedUnitDragMoved = false;
        _battlefieldMapPreviewBox.Capture = false;
        _battlefieldMapPreviewBox.Cursor = Cursors.Default;
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
            _battlefieldMapHintLabel.Text = "指定地点测试预览：已取消。";
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
        _battlefieldMapHintLabel.Text = markers.Count == 0
            ? "指定地点测试预览：当前剧本没有可标记的坐标。"
            : $"指定地点测试预览：已标记 {markers.Count} 个坐标；点击标记可跳到第一条对应指令。";
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
        RenderBattlefieldMapPreview(_currentBattlefieldDocument, candidate);
        if (candidate == null) return;

        var numberText = candidate.BattlefieldNumber.HasValue
            ? candidate.BattlefieldNumber.Value.ToString(CultureInfo.InvariantCulture)
            : "-";
        _battlefieldInfoBox.Text =
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
            $"中文注释：{candidate.Annotation}";
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
        ClearBattlefieldMapPreviewImages();
        var mapId = BuildBattlefieldMapId(document.Scenario);
        if (string.IsNullOrWhiteSpace(mapId))
        {
            _battlefieldMapHintLabel.Text = "地图预览：当前关卡编号无法匹配 Map/Mxxx.jpg。";
            return;
        }

        try
        {
            var map = FindBattlefieldMapResourceByMapId(mapId);
            var block = _currentHexzmapProbe?.Blocks.FirstOrDefault(x => x.MapId.Equals(mapId, StringComparison.OrdinalIgnoreCase));
            if (_currentHexzmapProbe != null && block != null)
            {
                var cells = _hexzmapProbeReader.GetBlockCells(_currentHexzmapProbe, block);
                if (cells.Length == block.BytesRead)
                {
                    var preview = map != null && File.Exists(map.Path) && !map.SourceKind.Equals("LegacyHmRaw", StringComparison.OrdinalIgnoreCase)
                        ? _hexzmapTerrainRenderService.RenderOverlay(cells, block.Width, block.Height, map.Path, 45)
                        : RenderHexzmapCells(cells, block.Width, block.Height);
                    SetBattlefieldMapPreviewImage(preview, selectedUnit, map);
                    return;
                }
            }

            if (map != null && File.Exists(map.Path))
            {
                using var image = RenderBattlefieldBaseMap(map);
                SetBattlefieldMapPreviewImage(new Bitmap(image), selectedUnit, map);
                return;
            }

            _battlefieldMapHintLabel.Text = $"地图预览：未找到 {mapId} 对应的 Map/Mxxx.jpg 或 Hexzmap 地形块。";
        }
        catch (Exception ex)
        {
            ClearBattlefieldMapPreviewImages();
            _battlefieldMapHintLabel.Text = "地图预览生成失败：" + ex.Message;
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

    private static string BuildBattlefieldMapId(ScenarioFileInfo scenario)
    {
        if (!string.IsNullOrWhiteSpace(scenario.Id) &&
            int.TryParse(scenario.Id, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id))
        {
            return "M" + id.ToString("000", CultureInfo.InvariantCulture);
        }

        var digits = new string((scenario.FileName ?? string.Empty).Where(char.IsDigit).ToArray());
        if (digits.Length == 0 ||
            !int.TryParse(digits, NumberStyles.Integer, CultureInfo.InvariantCulture, out id))
        {
            return string.Empty;
        }

        return "M" + id.ToString("000", CultureInfo.InvariantCulture);
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
        var mapId = BuildBattlefieldMapId(document.Scenario);
        return !string.IsNullOrWhiteSpace(mapId) && FindBattlefieldMapResourceByMapId(mapId) != null;
    }

    private void SetBattlefieldMapPreviewImage(Image image, BattlefieldUnitCandidate? selectedUnit)
        => SetBattlefieldMapPreviewImage(image, selectedUnit, null);

    private void SetBattlefieldMapPreviewImage(Image image, BattlefieldUnitCandidate? selectedUnit, MapResourceItem? map)
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
                _battlefieldMapHintLabel.Text = $"地图点选记录：{selectedUnit.Category} {selectedUnit.SourceCommand} -> 坐标 ({_battlefieldManualMarkerX},{_battlefieldManualMarkerY})。";
            }
        }
        else if (selectedUnit != null && BattlefieldEditorService.TryExtractFirstCoordinate(selectedUnit, out var gridX, out var gridY))
        {
            if (gridX >= 0 && gridX < gridWidth && gridY >= 0 && gridY < gridHeight)
            {
                _battlefieldMapHintLabel.Text = $"地图标记：{selectedUnit.Category} {selectedUnit.SourceCommand} -> 坐标 ({gridX},{gridY})。";
            }
            else
            {
                _battlefieldMapHintLabel.Text = $"地图标记：解析到坐标 ({gridX},{gridY})，但超出当前地图格数范围，未绘制。";
            }
        }
        else
        {
            var allySlotText = _battlefieldAllyDeploymentSlots.Count == 0
                ? string.Empty
                : $"；我军候选出战位 {_battlefieldAllyDeploymentSlots.Count} 个（强制 {_battlefieldAllyDeploymentSlots.Count(slot => slot.IsForced)} 个）";
            _battlefieldMapHintLabel.Text = $"地图：{gridWidth}x{gridHeight} 格，已摆放 {_battlefieldPlacedUnits.Count} 个单位{allySlotText}。";
        }

        _battlefieldMapStaticPreviewImage = image as Bitmap ?? new Bitmap(image);
        if (!ReferenceEquals(_battlefieldMapStaticPreviewImage, image))
        {
            image.Dispose();
        }

        _battlefieldMapStaticGridSize = (gridWidth, gridHeight);
        RefreshBattlefieldMapDynamicPreview();
    }

    private void RefreshBattlefieldMapDynamicPreview()
    {
        if (_battlefieldMapStaticPreviewImage == null)
        {
            var oldEmpty = _battlefieldMapPreviewBox.Image;
            _battlefieldMapPreviewBox.Image = null;
            oldEmpty?.Dispose();
            ApplyBattlefieldMapZoom();
            return;
        }

        var image = new Bitmap(_battlefieldMapStaticPreviewImage.Width, _battlefieldMapStaticPreviewImage.Height, PixelFormat.Format32bppArgb);
        using (var graphics = Graphics.FromImage(image))
        {
            graphics.DrawImageUnscaled(_battlefieldMapStaticPreviewImage, 0, 0);
        }

        var (gridWidth, gridHeight) = _battlefieldMapStaticGridSize;
        if (gridWidth > 0 && gridHeight > 0)
        {
            DrawBattlefieldForcedAllyDeploymentUnits(image, gridWidth, gridHeight);
            DrawBattlefieldPlacedUnits(image, gridWidth, gridHeight);
            DrawBattlefieldAllyDeploymentOrderBadges(image, gridWidth, gridHeight);
            DrawBattlefieldSelectedCoordinateMarker(image, gridWidth, gridHeight);
            DrawBattlefieldCommand25Markers(image, gridWidth, gridHeight);
            DrawBattlefieldHoverCell(image, gridWidth, gridHeight);
        }

        var old = _battlefieldMapPreviewBox.Image;
        _battlefieldMapPreviewBox.Image = image;
        old?.Dispose();
        ApplyBattlefieldMapZoom();
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

    private void ClearBattlefieldMapHover()
    {
        if (_battlefieldHoverGridX < 0 && _battlefieldHoverGridY < 0)
        {
            _battlefieldMapHintLabel.Text = "地形：-    坐标：-";
            return;
        }

        _battlefieldHoverGridX = -1;
        _battlefieldHoverGridY = -1;
        _battlefieldMapHintLabel.Text = "地形：-    坐标：-";
        RefreshBattlefieldMapDynamicPreview();
    }

    private void UpdateBattlefieldMapHoverLabel()
    {
        if (_battlefieldHoverGridX < 0 || _battlefieldHoverGridY < 0)
        {
            _battlefieldMapHintLabel.Text = "地形：-    坐标：-";
            return;
        }

        var terrain = TryGetBattlefieldHoverTerrain(_battlefieldHoverGridX, _battlefieldHoverGridY, out var text)
            ? text
            : "未知";
        _battlefieldMapHintLabel.Text = $"地形：{terrain}    坐标：({_battlefieldHoverGridX}, {_battlefieldHoverGridY})";
    }

    private bool TryGetBattlefieldHoverTerrain(int x, int y, out string terrain)
    {
        terrain = string.Empty;
        if (_currentBattlefieldDocument == null || _currentHexzmapProbe == null) return false;
        var mapId = BuildBattlefieldMapId(_currentBattlefieldDocument.Scenario);
        if (string.IsNullOrWhiteSpace(mapId)) return false;
        var block = _currentHexzmapProbe.Blocks.FirstOrDefault(item => item.MapId.Equals(mapId, StringComparison.OrdinalIgnoreCase));
        if (block == null || x < 0 || y < 0 || x >= block.Width || y >= block.Height) return false;

        var cells = _hexzmapProbeReader.GetBlockCells(_currentHexzmapProbe, block);
        var index = y * block.Width + x;
        if (index < 0 || index >= cells.Length) return false;
        terrain = FormatTerrainValue(cells[index]);
        return true;
    }

    private void DrawBattlefieldHoverCell(Image image, int gridWidth, int gridHeight)
    {
        if (_battlefieldHoverGridX < 0 || _battlefieldHoverGridY < 0) return;
        if (_battlefieldHoverGridX >= gridWidth || _battlefieldHoverGridY >= gridHeight) return;

        using var graphics = Graphics.FromImage(image);
        var cellWidth = image.Width / (float)gridWidth;
        var cellHeight = image.Height / (float)gridHeight;
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
        var old = _battlefieldMapPreviewBox.Image;
        _battlefieldMapPreviewBox.Image = null;
        old?.Dispose();
        _battlefieldMapStaticPreviewImage?.Dispose();
        _battlefieldMapStaticPreviewImage = null;
        _battlefieldMapStaticGridSize = default;
        _battlefieldMapPreviewSelectedUnit = null;
        _battlefieldHoverGridX = -1;
        _battlefieldHoverGridY = -1;
        ApplyBattlefieldMapZoom();
    }

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
        var image = _battlefieldMapPreviewBox.Image;
        if (image == null)
        {
            _battlefieldMapZoomPercent = Math.Clamp(_battlefieldMapZoomPercent, 25, 800);
            _battlefieldMapPreviewBox.Size = Size.Empty;
            _battlefieldMapZoomLabel.Text = $"缩放 {_battlefieldMapZoomPercent}%";
            return;
        }

        var zoom = Math.Clamp(_battlefieldMapZoomPercent, 25, 800) / 100.0;
        _battlefieldMapZoomPercent = (int)Math.Round(zoom * 100);
        _battlefieldMapPreviewBox.Size = new Size(
            Math.Max(1, (int)Math.Round(image.Width * zoom)),
            Math.Max(1, (int)Math.Round(image.Height * zoom)));
        _battlefieldMapZoomLabel.Text = $"缩放 {_battlefieldMapZoomPercent}%";
    }

    private (int Width, int Height) GetCurrentBattlefieldMapGridSize(Image? image)
    {
        if (image != null &&
            image.Width % MapResourceItem.MapTilePixelSize == 0 &&
            image.Height % MapResourceItem.MapTilePixelSize == 0)
        {
            return (image.Width / MapResourceItem.MapTilePixelSize, image.Height / MapResourceItem.MapTilePixelSize);
        }

        return (0, 0);
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

    private void DrawBattlefieldPlacedUnits(Image image, int gridWidth, int gridHeight)
    {
        if (_project == null || gridWidth <= 0 || gridHeight <= 0) return;
        using var graphics = Graphics.FromImage(image);
        var cellWidth = image.Width / (float)gridWidth;
        var cellHeight = image.Height / (float)gridHeight;

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
            using var borderPen = new Pen(isEditing ? Color.Orange : isSelected ? Color.Yellow : factionColor, isEditing ? 5 : isSelected ? 4 : 2);
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

    private void DrawBattlefieldForcedAllyDeploymentUnits(Image image, int gridWidth, int gridHeight)
    {
        if (_project == null || _battlefieldAllyDeploymentSlots.Count == 0 || gridWidth <= 0 || gridHeight <= 0) return;

        using var graphics = Graphics.FromImage(image);
        var cellWidth = image.Width / (float)gridWidth;
        var cellHeight = image.Height / (float)gridHeight;
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
                graphics.DrawImage(preview, FitImageIntoRect(preview.Size, Rectangle.Round(rect)));
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

    private void DrawBattlefieldAllyDeploymentOrderBadges(Image image, int gridWidth, int gridHeight)
    {
        if (_battlefieldAllyDeploymentSlots.Count == 0 || gridWidth <= 0 || gridHeight <= 0) return;

        using var graphics = Graphics.FromImage(image);
        var cellWidth = image.Width / (float)gridWidth;
        var cellHeight = image.Height / (float)gridHeight;
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
                    RefreshBattlefieldMapDynamicPreview();
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
            "左" => "左",
            "右" => "右",
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
            Direction = _battlefieldDirectionCombo.SelectedItem?.ToString() ?? "下",
            GridX = x,
            GridY = y,
            Source = scriptBacked ? "S剧本出场设置(拖放调整)" : "拖放",
            PlacementNote = scriptBacked && scriptCandidate != null
                ? BuildBattlefieldScriptBoundPlacementNote(item, x, y, scriptCandidate)
                : BuildBattlefieldPlacementNote(item, x, y)
        };
        _battlefieldPlacedUnits.Add(placed);
        _selectedBattlefieldPlacedUnit = placed;
        _editingBattlefieldPlacedUnit = null;
        _draggingBattlefieldPlacedUnit = null;
        _battlefieldPlacedUnitDragStart = null;
        _battlefieldPlacedUnitDragMoved = false;
        SyncBattlefieldControlPanelFromPlacedUnit(placed);
        var synced = SyncBattlefieldInstructionPreviewAfterPlacement(placed, "拖放");
        RenderBattlefieldMapPreview(_currentBattlefieldDocument);
        _saveBattlefieldUnitReviewsButton.Enabled = true;
        UpdateBattlefieldDeploymentWriteButton();
        SetStatus(synced
            ? $"战场布阵：{item.DisplayText} -> ({x},{y})，已同步右侧候选和左侧 S 剧本预览，尚未写回。"
            : $"战场布阵：{item.DisplayText} -> ({x},{y})；未找到可用的 46/47/4B 出场设置槽，左右指令不改，需保存为布阵草稿。");
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
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
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
            SourceCommandDisplay = $"{BuildBattlefieldDeploymentSourceDisplay(command?.CommandName, slot.Category)} 第 {slot.RecordIndex + 1} 条",
            PersonDisplay = slot.IsAllySlot
                ? slot.PersonOrOrder.ToString(CultureInfo.InvariantCulture)
                : slot.IsBlank ? string.Empty : slot.PersonOrOrder.ToString(CultureInfo.InvariantCulture),
            CoordinateDisplay = $"({slot.GridX},{slot.GridY})",
            FactionDisplay = slot.Category.Replace("出场", string.Empty, StringComparison.Ordinal),
            AiDisplay = string.Empty,
            LevelJobDisplay = string.Empty,
            Category = slot.Category,
            SourceCommand = $"{command?.CommandIdHex ?? HexDisplayFormatter.Format(slot.CommandId, 2)} {command?.CommandName ?? slot.Category} 第 {slot.RecordIndex + 1} 条",
            SceneSection = command == null
                ? $"Record {slot.RecordIndex}"
                : $"Scene {command.SceneIndex} / Section {command.SectionIndex} / Cmd {command.CommandIndex} / 第 {slot.RecordIndex + 1} 条",
            OffsetHex = command?.OffsetHex ?? string.Empty,
            PersonHint = slot.IsAllySlot
                ? $"我军出战顺序：{slot.PersonOrOrder}（地图标注显示为第 {Math.Max(0, slot.PersonOrOrder + 1)} 位）"
                : slot.IsBlank ? "空出场槽：可由地图拖放写入人物" : $"人物/部队：{slot.PersonOrOrder}",
            CoordinateHint = $"坐标候选：({slot.GridX},{slot.GridY})",
            FactionHint = $"阵营候选：{slot.Category.Replace("出场", string.Empty, StringComparison.Ordinal)}",
            AiHint = slot.WritesAi ? "AI/方针槽可随拖放控制面板写回。" : "无直接 AI 方针槽。",
            LevelOrStateHint = slot.IsAllySlot ? "4B 我军出战位：写回坐标/方向/隐藏标志，不改出战顺序。" : "空槽自动绑定：写回人物、坐标和已确认 AI。",
            Annotation = "由地图拖放自动绑定到 S 剧本出场设置槽；点击写回前仅作为预览覆盖。",
            TargetKey = slot.TargetKey
        };
    }

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

    private BattlefieldUnitCandidate BuildBattlefieldUnitCandidatePreview(BattlefieldUnitCandidate original, BattlefieldPlacedUnit placed, string action)
    {
        var reviewStatus = string.IsNullOrWhiteSpace(original.ReviewStatus) ? "已调整待写回" : original.ReviewStatus + " / 已调整待写回";
        var isAllySlot = IsBattlefieldAllyDeploymentTargetKey(original.TargetKey);
        var personPreviewText = isAllySlot
            ? $"预览角色：{placed.PersonId} {placed.Name}（仅用于地图标注；4B 写回不改出战顺序/人物槽）"
            : $"预览人物/部队：{placed.PersonId} {placed.Name}";
        var aiPreviewText = isAllySlot
            ? $"4B 无 AI 写回；原候选：{original.AiHint}"
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
            Category = original.Category,
            SourceCommand = original.SourceCommand + " [地图预览已调整]",
            SceneSection = original.SceneSection,
            OffsetHex = original.OffsetHex,
            PersonHint = $"{personPreviewText}；原候选：{original.PersonHint}",
            CoordinateHint = $"预览坐标：({placed.GridX},{placed.GridY})；原候选：{original.CoordinateHint}",
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
        if (!SelectBattlefieldUnitCandidateGridRow(targetKey) && !string.IsNullOrWhiteSpace(selectedTargetKey))
        {
            SelectBattlefieldUnitCandidateGridRow(selectedTargetKey);
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
        if (preview == null)
        {
            node.Text = baseText;
            node.ToolTipText = baseToolTip;
            node.ForeColor = GetScriptCommandColor(row.CommandId);
            return;
        }

        var previewLabel = IsBattlefieldAllyDeploymentTargetKey(preview.TargetKey)
            ? $"4B坐标@{preview.GridX},{preview.GridY}"
            : $"{preview.PersonId}@{preview.GridX},{preview.GridY}";
        node.Text = $"{baseText} [地图预览已调整: {previewLabel}]";
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
            : $"地图预览：人物={preview.PersonId} {preview.Name}，坐标=({preview.GridX},{preview.GridY})，阵营={preview.Faction}，AI={preview.AiMode}；尚未写回 S 剧本。";

    private void ApplyBattlefieldControlPanelToSelectedUnit()
    {
        if (_bindingBattlefieldControlPanel) return;
        if (_selectedBattlefieldPlacedUnit == null) return;
        _selectedBattlefieldPlacedUnit.Faction = GetSelectedBattlefieldFaction();
        _selectedBattlefieldPlacedUnit.LevelOffset = (int)_battlefieldLevelOffsetInput.Value;
        _selectedBattlefieldPlacedUnit.LevelMode = _battlefieldLevelModeCombo.SelectedItem?.ToString() ?? "初级";
        _selectedBattlefieldPlacedUnit.AiMode = _battlefieldAiModeCombo.SelectedItem?.ToString() ?? "被动";
        _selectedBattlefieldPlacedUnit.Hidden = _battlefieldHiddenCheckBox.Checked;
        _selectedBattlefieldPlacedUnit.Direction = _battlefieldDirectionCombo.SelectedItem?.ToString() ?? "下";
        if (_currentBattlefieldDocument != null)
        {
            SyncBattlefieldInstructionPreviewAfterPlacement(_selectedBattlefieldPlacedUnit, "控制面板调整");
            RenderBattlefieldMapPreview(_currentBattlefieldDocument);
        }
        _saveBattlefieldUnitReviewsButton.Enabled = true;
        UpdateBattlefieldDeploymentWriteButton();
    }

    private void HandleBattlefieldFactionChanged()
    {
        if (_bindingBattlefieldControlPanel) return;
        RefreshBattlefieldPaletteUnitPreview(_battlefieldUnitListBox.SelectedItem as BattlefieldUnitPaletteItem);
        ApplyBattlefieldControlPanelToSelectedUnit();
    }

    private void SyncBattlefieldControlPanelFromPlacedUnit(BattlefieldPlacedUnit unit)
    {
        _bindingBattlefieldControlPanel = true;
        try
        {
            _battlefieldFactionAllyRadio.Checked = unit.Faction == "我军";
            _battlefieldFactionFriendRadio.Checked = unit.Faction == "友军";
            _battlefieldFactionEnemyRadio.Checked = unit.Faction == "敌军";
            _battlefieldHiddenCheckBox.Checked = unit.Hidden;
            _battlefieldLevelOffsetInput.Value = Math.Clamp(unit.LevelOffset, (int)_battlefieldLevelOffsetInput.Minimum, (int)_battlefieldLevelOffsetInput.Maximum);
            SelectComboText(_battlefieldLevelModeCombo, unit.LevelMode);
            SelectComboText(_battlefieldAiModeCombo, unit.AiMode);
            SelectComboText(_battlefieldDirectionCombo, unit.Direction);
        }
        finally
        {
            _bindingBattlefieldControlPanel = false;
        }

        RefreshBattlefieldPaletteUnitPreview(_battlefieldUnitListBox.SelectedItem as BattlefieldUnitPaletteItem);
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
        var targetKey = _selectedBattlefieldPlacedUnit.TargetKey;
        _battlefieldPlacedUnits.Remove(_selectedBattlefieldPlacedUnit);
        ClearBattlefieldPlacedUnitSelection();
        ClearBattlefieldInstructionPreviewForTarget(targetKey);
        if (_currentBattlefieldDocument != null)
        {
            RenderBattlefieldMapPreview(_currentBattlefieldDocument);
        }
        _saveBattlefieldUnitReviewsButton.Enabled = true;
        UpdateBattlefieldDeploymentWriteButton();
        SetStatus("战场布阵：已移除选中单位。");
    }

    private void ClearBattlefieldPlacedUnits()
    {
        if (_battlefieldPlacedUnits.Count == 0) return;
        if (MessageBox.Show(this, "将清空当前关卡的地图摆放草稿，不修改 S 剧本。是否继续？", "确认清空摆放", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
        {
            return;
        }

        _battlefieldPlacedUnits.Clear();
        ClearBattlefieldPlacedUnitSelection();
        ClearBattlefieldInstructionPreviewState();
        if (_currentBattlefieldDocument != null)
        {
            BindBattlefieldUnitCandidates(GetBattlefieldUnitCandidatesForDisplay());
            BindBattlefieldCommandCandidates(GetBattlefieldCommandCandidatesForDisplay());
            RefreshBattlefieldScriptPreviewTree();
        }
        if (_currentBattlefieldDocument != null)
        {
            RenderBattlefieldMapPreview(_currentBattlefieldDocument);
        }
        _saveBattlefieldUnitReviewsButton.Enabled = true;
        UpdateBattlefieldDeploymentWriteButton();
        SetStatus("战场布阵：已清空摆放。");
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
            .ToDictionary(group => group.Key, group => group.First());

        foreach (var candidate in document.UnitCandidates)
        {
            if (IsBattlefieldAllyDeploymentPositionCandidate(candidate)) continue;
            if (!BattlefieldEditorService.TryExtractFirstCoordinate(candidate, out var x, out var y)) continue;
            if (!BattlefieldEditorService.TryExtractPersonId(candidate, out var personId)) continue;

            var targetKey = string.IsNullOrWhiteSpace(candidate.TargetKey)
                ? $"ScriptPlacement#{document.Scenario.FileName}#{candidate.Index}"
                : candidate.TargetKey;
            if (existingTargets.Contains(targetKey)) continue;

            var gridKey = BuildBattlefieldGridKey(x, y);
            if (occupiedGrids.Contains(gridKey)) continue;

            paletteByPersonId.TryGetValue(personId, out var palette);
            _battlefieldPlacedUnits.Add(new BattlefieldPlacedUnit
            {
                TargetKey = targetKey,
                PersonId = personId,
                Name = palette?.Name ?? $"人物{personId}",
                JobId = palette?.JobId,
                JobName = palette?.JobName ?? string.Empty,
                RImageId = palette?.RImageId ?? 0,
                SImageId = palette?.SImageId ?? 0,
                Faction = InferBattlefieldFaction(candidate),
                LevelOffset = InferBattlefieldLevelOffset(candidate.LevelOrStateHint),
                LevelMode = InferBattlefieldLevelMode(candidate.LevelOrStateHint),
                AiMode = InferBattlefieldAiMode(candidate.AiHint),
                Hidden = IsBattlefieldHiddenCandidate(candidate),
                Direction = "下",
                GridX = x,
                GridY = y,
                Source = "S剧本预览",
                PlacementNote = BuildBattlefieldScriptPlacementNote(candidate)
            });
            existingTargets.Add(targetKey);
            occupiedGrids.Add(gridKey);
        }
    }

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

    private static string BuildBattlefieldScriptPlacementNote(BattlefieldUnitCandidate candidate)
        => $"从 S 剧本出场/坐标候选预加载：{candidate.SourceCommand} / {candidate.SceneSection} / {candidate.OffsetHex}\r\n" +
           $"人物：{candidate.PersonHint}\r\n" +
           $"坐标：{candidate.CoordinateHint}\r\n" +
           $"阵营：{candidate.FactionHint}\r\n" +
           $"AI：{candidate.AiHint}\r\n" +
           $"等级/状态：{candidate.LevelOrStateHint}\r\n" +
           "当前只保存项目侧预览/调整，不直接写回未知 S 剧本命令参数。";

    private void BindBattlefieldUnitPalette(IEnumerable<BattlefieldUnitPaletteItem> rows)
    {
        var list = rows.ToList();
        var selectedPersonId = _selectedBattlefieldPaletteItem?.PersonId;
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
        RefreshBattlefieldPaletteUnitPreview(_battlefieldUnitListBox.SelectedItem as BattlefieldUnitPaletteItem);
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
        var mapping = CharacterImageResourceService.ResolveSUnitImageMapping(item.SImageId, item.JobId, factionSlot);
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
            _battlefieldScriptTextBox.Clear();
            BindBattlefieldScriptParameterRows(itemData.Command != null
                ? BuildBattlefieldLegacyScriptParameterRows(itemData.Command)
                : Array.Empty<ScenarioCommandParameterRow>());
            UpdateBattlefieldScriptTextCapacityLabel();
            _battlefieldScriptDetailBox.Text = itemData.Command != null
                ? BuildLegacyScriptRowDetail(itemRow, itemData.Command)
                : BuildBattlefieldScriptRowDetailWithPreview(itemRow);
            return;
        }

        if (_battlefieldScriptTree.SelectedNode?.Tag is ScenarioTextEntry text)
        {
            _selectedBattlefieldScriptTextEntry = text;
            _selectedBattlefieldScriptCommandRow = null;
            _battlefieldScriptTextBox.Text = text.Text;
            BindBattlefieldScriptParameterRows(Array.Empty<ScenarioCommandParameterRow>());
            _battlefieldScriptDetailBox.Text = BuildBattlefieldScriptTextDetail(text);
            UpdateBattlefieldScriptTextCapacityLabel();
            return;
        }

        if (_battlefieldScriptTree.SelectedNode?.Tag is ScenarioStructureRow row)
        {
            _selectedBattlefieldScriptCommandRow = row.NodeType == "Command候选" ? row : null;
            _selectedBattlefieldScriptTextEntry = null;
            _battlefieldScriptTextBox.Clear();
            BindBattlefieldScriptParameterRows(row.NodeType == "Command候选"
                ? BuildBattlefieldScriptParameterRows(row)
                : Array.Empty<ScenarioCommandParameterRow>());
            UpdateBattlefieldScriptTextCapacityLabel();
            _battlefieldScriptDetailBox.Text = BuildBattlefieldScriptRowDetailWithPreview(row);
            return;
        }

        ClearBattlefieldScriptTextSelection();
    }

    private string BuildBattlefieldScriptTextDetail(ScenarioTextEntry text)
        => $"文本：#{text.Index} {text.Kind} {text.OffsetHex}\r\n" +
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
                Kind = command.CommandId == 0x46 ? "友军出场块" : "敌军出场块",
                RawHex = FormatLegacyScriptOffset(command.FileOffset, command.CommandIndex),
                DecimalValue = command.Parameters.Count,
                DecodedValue = $"{command.Parameters.Count} 个旧版参数槽；右侧出场候选已按记录拆分显示。",
                Meaning = "该命令是旧版战场部署大块。为避免点击/双击时同步重建数百行参数表，战场页只显示摘要；双击或点“修改整条指令”可打开出场块编辑器。",
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
                    nameof(ScenarioCommandParameterRow.DecodedValue) => "当前值/解释",
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
            SetStatus("战场制作 S 剧本参数：出场大块可用专用编辑器修改；侧栏摘要不作为单槽直接编辑。");
            return;
        }

        _editBattlefieldScriptParametersButton.Enabled = CanEditLegacyScriptCommandParameters(command, out _);
        if (CanEditLegacyScriptParameter(command, parameter, out var reason))
        {
            _battlefieldScriptParameterValueBox.Enabled = true;
            _applyBattlefieldScriptParameterButton.Enabled = true;
            SetStatus($"战场制作 S 剧本参数：槽 {parameter.Index} 可编辑，当前值 {FormatLegacyScriptParameterEditorValue(command, parameter)}");
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
        SetStatus($"战场制作 S 剧本参数：{command.CommandIdHex} {command.CommandName} 槽 {parameter.Index} {oldValue} -> {newValue}，需完整保存S剧本");
    }

    private void QueueEditSelectedBattlefieldScriptParameters()
    {
        BeginInvoke(new Action(() =>
        {
            if (IsDisposed || _editingBattlefieldLegacyCommandDialog)
            {
                return;
            }

            EditSelectedBattlefieldScriptParameters();
        }));
    }

    private void EditSelectedBattlefieldScriptParameters()
    {
        if (_editingBattlefieldLegacyCommandDialog)
        {
            SetStatus("战场制作：旧版指令修改窗口已打开。");
            return;
        }

        if (!TryGetSelectedBattlefieldLegacyItemData(out var itemData) || itemData.Command == null)
        {
            MessageBox.Show(this, "请先在 S 剧本树中选择一条旧版命令。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        if (itemData.Id is 0x46 or 0x47)
        {
            EditSelectedBattlefieldDeploymentBlock(itemData);
            return;
        }

        if (!LegacyCommandEditDispatcher.CanEdit(itemData.Id))
        {
            MessageBox.Show(this, "旧版源码的 OnEditModify() 没有为该命令提供修改窗口。", "该命令暂不可修改", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var command = itemData.Command;
        var oldSummary = BuildLegacyScriptParameterPreview(command);
        var commandTitle = $"{command.CommandIdHex} {command.CommandName} / ord {itemData.Ord}";
        var dialogDataSources = LegacyMfcDialogDataSources.Create(_project, _tables);
        var precedingSameCommandCount = CountPrecedingSameLegacyCommands(_currentBattlefieldLegacyScriptDocument, command);
        var beforeEdit = CaptureLegacyScenarioHistorySnapshot(LegacyScriptEditorScope.Battlefield, _currentBattlefieldLegacyScriptDocument!);
        var edited = false;
        _editingBattlefieldLegacyCommandDialog = true;
        try
        {
            edited = LegacyCommandEditDispatcher.Edit(this, itemData, commandTitle, _currentBattlefieldLegacyScriptDocument?.CommandCount ?? 0, precedingSameCommandCount, dialogDataSources);
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
        if (oldSummary != BuildLegacyScriptParameterPreview(command))
        {
            PushLegacyScenarioUndoSnapshot(LegacyScriptEditorScope.Battlefield, beforeEdit);
        }
        if (!RefreshLegacyEditorCommandInPlace(LegacyScriptEditorScope.Battlefield, command))
        {
            RefreshBattlefieldLegacyScriptView(command);
        }
        RefreshBattlefieldDocumentFromLegacyScript();
        _saveBattlefieldScriptStructureButton.Enabled = true;
        SetStatus($"战场制作旧版修改指令：{commandTitle}，{oldSummary} -> {BuildLegacyScriptParameterPreview(command)}，需完整保存S剧本");
    }

    private void EditSelectedBattlefieldDeploymentBlock(LegacyScenarioItemData itemData, int? preferredParameterIndexOverride = null)
    {
        var command = itemData.Command;
        if (command == null) return;

        var definition = DeploymentBlockDefinition.FromCommandId(command.CommandId);
        if (definition == null)
        {
            MessageBox.Show(this, "该命令不是 46/47 出场设定。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
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

            var expectedCount = definition.RecordCount * definition.Stride;
            if (dialog.CommittedValues.Count != expectedCount)
            {
                MessageBox.Show(this, $"出场块编辑器返回 {dialog.CommittedValues.Count} 个槽，预期 {expectedCount} 个槽。", "参数数量异常", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            if (command.Parameters.Count < expectedCount)
            {
                MessageBox.Show(this, $"当前命令只有 {command.Parameters.Count} 个参数槽，预期 {expectedCount} 个槽，无法提交出场块修改。", "参数数量异常", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            for (var i = 0; i < expectedCount; i++)
            {
                if (command.Parameters[i].Kind != LegacyScenarioParameterKind.Word16)
                {
                    MessageBox.Show(this, $"当前命令参数槽 {i} 不是 16 位数值，无法作为出场块提交。", "参数类型异常", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
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

    private void RefreshBattlefieldDocumentFromLegacyScript()
    {
        if (_project == null || _currentBattlefieldDocument == null || _currentBattlefieldLegacyScriptDocument == null) return;

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
        RenderBattlefieldMapPreview(_currentBattlefieldDocument, GetSelectedBattlefieldUnitCandidate());
        UpdateBattlefieldDeploymentWriteButton();
        _battlefieldInfoBox.Text = BuildBattlefieldInfo(_currentBattlefieldDocument) +
                                   "\r\n\r\nS 剧本旧版修改已同步到右侧候选与地图预览；点击“完整保存S剧本”前尚未写入原文件。";
    }

    private bool TryGetSelectedBattlefieldLegacyItemData(out LegacyScenarioItemData itemData)
    {
        if (_battlefieldScriptTree.SelectedNode?.Tag is LegacyScenarioItemData selected)
        {
            itemData = selected;
            return true;
        }

        if (TryGetSelectedBattlefieldLegacyScriptCommand(out var command) &&
            _battlefieldScriptItemDataByCommand.TryGetValue(command, out itemData!))
        {
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
        _battlefieldScriptTextCapacityLabel.Text = $"文本容量：GBK {bytes}/{entry.ByteLength} 字节，剩余 {remaining} 字节";
        _battlefieldScriptTextCapacityLabel.ForeColor = remaining < 0 ? Color.Firebrick : SystemColors.ControlText;
        _battlefieldScriptTextBox.BackColor = remaining < 0 ? Color.MistyRose : SystemColors.Window;
        _saveBattlefieldScriptTextButton.Enabled = remaining >= 0 && !string.Equals(
            BattlefieldEditorService.NormalizeText(_battlefieldScriptTextBox.Text),
            BattlefieldEditorService.NormalizeText(entry.OriginalText),
            StringComparison.Ordinal);
    }

    private async Task SaveSelectedBattlefieldScriptTextAsync()
    {
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
                    $"即将完整保存 {scenario.FileName}。\r\n\r\n文本参数：{entry.OffsetHex}\r\n保存会重建 Scene 偏移、Section/子块长度和 0x76 跳转；保存前自动备份，替换前重读校验。是否继续？",
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
                    "战场制作页 S 剧本文本完整保存");

            if (!RefreshLegacyEditorCommandInPlace(LegacyScriptEditorScope.Battlefield, legacyText.Command))
            {
                RefreshBattlefieldLegacyScriptView(legacyText.Command);
            }
            MarkLegacyScriptEditorSavedInPlace(LegacyScriptEditorScope.Battlefield, result);
            System.Diagnostics.Debug.WriteLine($"已从战场制作页完整保存 S 剧本文本：{scenario.FileName} offset={entry.OffsetHex} backup={result.BackupPath}");
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
                $"即将写入 RS\\{scenario.FileName} 的文本 {entry.OffsetHex}。\r\n\r\n只写该文本线索，未知命令结构保持原样；保存前自动备份，保存后复读校验。是否继续？",
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
                "战场制作页 S 剧本文本原地保存");
            await LoadSelectedBattlefieldScenarioAsync();
            _battlefieldScriptDetailBox.Text += $"\r\n\r\n保存完成：变化 {result.ChangedBytes} 字节。\r\n备份：{result.BackupPath}\r\n报告：{result.ReportJsonPath}";
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
                "战场制作页 S 剧本完整结构保存"));

            MarkLegacyScriptEditorSavedInPlace(LegacyScriptEditorScope.Battlefield, result);
            System.Diagnostics.Debug.WriteLine($"已从战场制作页完整保存 S 剧本：{scenario.FileName} backup={result.BackupPath}");
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
                "R场景制作页 R 剧本完整结构保存"));

            MarkLegacyScriptEditorSavedInPlace(LegacyScriptEditorScope.RScene, result);
            System.Diagnostics.Debug.WriteLine($"已从 R 场景制作页完整保存 R 剧本：{scenario.FileName} backup={result.BackupPath}");
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
    {
        if (_currentBattlefieldScriptStructure == null) return;
        var keyword = _battlefieldScriptSearchBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(keyword))
        {
            ClearBattlefieldScriptSearch();
            return;
        }

        var match = _battlefieldScriptTree.Nodes
            .Cast<TreeNode>()
            .SelectMany(EnumerateScriptTreeNodes)
            .FirstOrDefault(node => node.Text.Contains(keyword, StringComparison.CurrentCultureIgnoreCase) ||
                                    (node.Tag is ScenarioStructureRow row && (row.CommandName.Contains(keyword, StringComparison.CurrentCultureIgnoreCase) ||
                                                                               row.CommandIdHex.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
                                                                               row.ParameterPreview.Contains(keyword, StringComparison.CurrentCultureIgnoreCase))) ||
                                    (node.Tag is ScenarioTextEntry text && text.Text.Contains(keyword, StringComparison.CurrentCultureIgnoreCase)));
        if (match == null)
        {
            _battlefieldScriptDetailBox.Text = $"S 剧本搜索：未命中“{keyword}”。";
            return;
        }

        _battlefieldScriptTree.SelectedNode = match;
        match.EnsureVisible();
        SetStatus($"战场制作 S 剧本搜索命中：{keyword}");
    }

    private void ClearBattlefieldScriptSearch()
    {
        _battlefieldScriptSearchBox.Clear();
        if (_currentBattlefieldScriptStructure != null)
        {
            _battlefieldScriptDetailBox.Text =
                $"S剧本：{_currentBattlefieldScriptStructure.FileName}\r\n" +
                $"Scene：{_currentBattlefieldScriptStructure.SceneCount}  Section：{_currentBattlefieldScriptStructure.SectionCount}  Command：{_currentBattlefieldScriptStructure.CommandCandidateCount}  文本：{_currentBattlefieldScriptTextEntries.Count}";
        }
    }

    private void DrawBattlefieldCoordinateMarker(Image image, int gridX, int gridY, int gridWidth, int gridHeight, Color markerColor, string label)
    {
        if (gridWidth <= 0 || gridHeight <= 0) return;
        using var graphics = Graphics.FromImage(image);
        var cellWidth = image.Width / (float)gridWidth;
        var cellHeight = image.Height / (float)gridHeight;
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

    private void DrawBattlefieldCommand25Markers(Image image, int gridWidth, int gridHeight)
    {
        if (_battlefieldCommand25Markers.Count == 0 || gridWidth <= 0 || gridHeight <= 0) return;

        foreach (var marker in _battlefieldCommand25Markers)
        {
            if (marker.GridX < 0 || marker.GridX >= gridWidth || marker.GridY < 0 || marker.GridY >= gridHeight) continue;
            var label = marker.Count > 1 ? "25x" + marker.Count.ToString(CultureInfo.InvariantCulture) : "25";
            DrawBattlefieldCoordinateMarker(image, marker.GridX, marker.GridY, gridWidth, gridHeight, Color.MediumVioletRed, label);
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

    private void DrawBattlefieldSelectedCoordinateMarker(Image image, int gridWidth, int gridHeight)
    {
        var selectedUnit = _battlefieldMapPreviewSelectedUnit;
        if (selectedUnit == null || gridWidth <= 0 || gridHeight <= 0) return;

        if (selectedUnit.TargetKey.Equals(_battlefieldManualMarkerTargetKey, StringComparison.OrdinalIgnoreCase) &&
            _battlefieldManualMarkerX >= 0 &&
            _battlefieldManualMarkerY >= 0)
        {
            if (_battlefieldManualMarkerX < gridWidth && _battlefieldManualMarkerY < gridHeight)
            {
                DrawBattlefieldCoordinateMarker(image, _battlefieldManualMarkerX, _battlefieldManualMarkerY, gridWidth, gridHeight, Color.DeepSkyBlue, "点选");
            }

            return;
        }

        if (!BattlefieldEditorService.TryExtractFirstCoordinate(selectedUnit, out var gridX, out var gridY)) return;
        if (gridX < 0 || gridX >= gridWidth || gridY < 0 || gridY >= gridHeight) return;

        DrawBattlefieldCoordinateMarker(image, gridX, gridY, gridWidth, gridHeight, Color.Red, "候选");
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
            $"出场/坐标候选：{document.UnitCandidates.Count} 条；战场命令定位：{document.CommandCandidates.Count} 条。\r\n" +
            $"地图预览单位：{_battlefieldPlacedUnits.Count} 个；我军候选出战位：{_battlefieldAllyDeploymentSlots.Count} 个（强制 {_battlefieldAllyDeploymentSlots.Count(slot => slot.IsForced)} 个）。\r\n" +
            $"说明：{document.Annotation}";
    }

    private void SaveBattlefieldUnitReviews()
    {
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
        if (_project == null || _currentBattlefieldDocument == null)
        {
            MessageBox.Show(this, "请先读取一个战场关卡。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var writableCount = _battlefieldPlacedUnits.Count(BattlefieldDeploymentWriteService.IsScriptPlacementWritable);
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
                $"即将写入 RS\\{_currentBattlefieldDocument.Scenario.FileName} 的 46/47/4B 出场设置槽。\r\n\r\n可写回记录：{writableCount} 条。\r\n写回内容：46/47 写人物编号、坐标和已确认 AI；4B 只写坐标、方向、隐藏标志，不改第一个出战顺序槽。等级/装备/未知状态槽保持原值。保存前自动备份，保存后按旧版树复读校验。是否继续？",
                "确认写回出场到 S 剧本",
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
                _battlefieldPlacedUnits);
            var notePath = _battlefieldUnitReviewService.Save(_project, _currentBattlefieldDocument, rows, _battlefieldPlacedUnits);

            ReloadBattlefieldScenarioAfterWrite(scenarioFileName, dictionary);
            _battlefieldInfoBox.Text =
                BuildBattlefieldInfo(_currentBattlefieldDocument!) +
                $"\r\n\r\n出场记录已真实写回 RS\\{scenarioFileName}：{result.WrittenRecordCount} 条，跳过 {result.SkippedRecordCount} 条，变化 {result.ChangedBytes} 字节。\r\n" +
                $"校验：{result.ValidationSummary}\r\n" +
                $"项目侧核对/摆放 JSON：{notePath}\r\n" +
                $"备份：{result.BackupPath}\r\n" +
                $"报告：{result.ReportJsonPath}\r\n" +
                BuildBattlefieldDeploymentWriteDetail(result);
            System.Diagnostics.Debug.WriteLine($"已写回战场出场记录：{scenarioFileName} records={result.WrittenRecordCount} backup={result.BackupPath}");
            SetStatus($"战场制作：出场记录写回完成 {scenarioFileName} records={result.WrittenRecordCount}");
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
            _battlefieldPlacedUnits.Any(BattlefieldDeploymentWriteService.IsScriptPlacementWritable);

    private void SaveBattlefieldTexts()
    {
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
                $"即将写入 RS\\{_currentBattlefieldDocument.Scenario.FileName}。\r\n\r\n当前仅写回已匹配的标题/胜败条件文本，未知命令结构保持原样；保存前自动备份，保存后复读校验。是否继续？",
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
                $"\r\n\r\n保存完成：写入 {result.EntriesWritten} 条，变化 {result.ChangedBytes} 字节。\r\n备份：{result.BackupPath}\r\n报告：{result.ReportJsonPath}";
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

        var mapId = BuildBattlefieldMapId(document.Scenario);
        var map = string.IsNullOrWhiteSpace(mapId) ? null : FindBattlefieldMapResourceByMapId(mapId);
        if (map == null)
        {
            MessageBox.Show(this, "当前关卡没有可跳转的 Map/Mxxx.jpg 地图图片。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
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
