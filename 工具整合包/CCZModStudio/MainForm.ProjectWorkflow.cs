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
    private void LoadDefaultProject()
    {
        try
        {
            var project = _projectDetector.DetectDefaultProject();
            LoadProject(project);
        }
        catch (Exception ex)
        {
            Log("默认项目加载失败：" + ex.Message);
        }
    }

    private void OpenProjectDialog()
    {
        using var dialog = new FolderBrowserDialog
        {
            Description = "请选择包含 Ekd5.exe / Data.e5 / Imsg.e5 / Star.e5 的游戏项目目录",
            UseDescriptionForTitle = true
        };

        if (_project != null && Directory.Exists(_project.GameRoot))
        {
            dialog.SelectedPath = _project.GameRoot;
        }

        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            LoadProject(_projectDetector.CreateProjectFromGameRoot(dialog.SelectedPath));
        }
    }

    private void ReloadCurrentProject()
    {
        if (_project == null)
        {
            LoadDefaultProject();
            return;
        }
        LoadProject(_project);
    }

    private void LoadProject(CczProject project)
    {
        _project = project;
        _legacyMfcDialogDataSources = null;
        _legacyScenarioCommandDisplayFormatter = null;
        _attackAreaPreviewService.ClearCache();
        _strategyAnimationPreviewService.ClearCache();
        _imageAssignmentPreviewService.ClearCache();
        ClearBattlefieldUnitFrameCache();
        ClearRSceneDocumentView();
        InvalidateScriptVariableProjectCache();
        var mode = project.IsTestCopy ? "测试副本：允许保存" : "当前项目：允许保存";
        _projectLabel.Text = $"项目：{project.Name}    模式：{mode}    GameRoot：{project.GameRoot}    HexTable：{project.HexTableXmlPath}";
        RefreshProjectFileStatusView(updateStatus: false);
        _saveTableButton.Enabled = false;
        _exportCsvButton.Enabled = false;
        _importCsvButton.Enabled = false;
        _dataGrid.ReadOnly = true;
        _dataGrid.DataSource = null;
        _currentTableResult = null;
        _currentRoleEditorData = null;
        _currentBattlefieldDocument = null;
        _currentBattlefieldScriptStructure = null;
        _currentBattlefieldLegacyScriptDocument = null;
        _currentBattlefieldScriptTextEntries = Array.Empty<ScenarioTextEntry>();
        _selectedBattlefieldScriptTextEntry = null;
        _selectedBattlefieldScriptCommandRow = null;
        _battlefieldScriptCommandByKey.Clear();
        _battlefieldScriptTextByOffset.Clear();
        _battlefieldScriptTextEntryByOffset.Clear();
        _battlefieldScriptParameterGrid.DataSource = null;
        ClearBattlefieldScriptParameterEditor();
        _saveBattlefieldScriptStructureButton.Enabled = false;
        _showBattlefieldVariablesButton.Enabled = false;
        ClearBattlefieldInstructionPreviewState();
        _battlefieldPlacedUnits.Clear();
        _battlefieldAllyDeploymentSlots = Array.Empty<BattlefieldAllyDeploymentSlot>();
        ClearBattlefieldPlacedUnitSelection();
        _battlefieldUnitPaletteItems = Array.Empty<BattlefieldUnitPaletteItem>();
        _battlefieldScriptTree.Nodes.Clear();
        _battlefieldUnitListBox.DataSource = null;
        _battlefieldUnitPaletteFilterBox.Clear();
        SetPictureBoxImage(_battlefieldUnitPreviewBox, null);
        _writeBattlefieldDeploymentButton.Enabled = false;
        _roleEditorJobLookup = null;
        _roleEditorJobNames = new Dictionary<int, string>();
        _roleBiographyRead = null;
        _roleCriticalQuoteRead = null;
        _roleRetreatQuoteRead = null;
        _roleEditorGrid.DataSource = null;
        _roleBiographyBox.Clear();
        _roleCriticalQuoteBox.Clear();
        _roleRetreatQuoteBox.Clear();
        _roleTextDetailInfoBox.Clear();
        _saveRoleEditorButton.Enabled = false;
        _saveRoleTextDetailButton.Enabled = false;
        _currentJobEditorData = null;
        _jobNameRead = null;
        _jobDescriptionRead = null;
        _jobGrowthRead = null;
        _jobPierceRead = null;
        _jobEditorGrid.DataSource = null;
        _saveJobEditorButton.Enabled = false;
        _jobEditorInfoBox.Text = "兵种设定：读取后可编辑详细兵种名称、说明、成长参数和穿透。";
        ClearJobAreaPreview("兵种设定：读取后选择“攻击范围”或“穿透”单元格可显示右侧范围图。");
        _currentJobTerrainData = null;
        _jobSeriesRead = null;
        _jobTerrainPowerRead = null;
        _jobMoveCostRead = null;
        _jobTerrainGrid.DataSource = null;
        _saveJobTerrainButton.Enabled = false;
        _jobTerrainInfoBox.Text = "兵种系/地形：读取后可编辑兵种系名称、各地形发挥和移动消耗。";
        _jobRestraintRead = null;
        _jobAttributeRead = null;
        _jobRestraintGrid.DataSource = null;
        _jobAttributeGrid.DataSource = null;
        _saveJobMatrixButton.Enabled = false;
        _jobMatrixInfoBox.Text = "相克/属性矩阵：读取后可编辑 40x40 兵种相克矩阵与 8x40 原始兵种属性矩阵。";
        _currentJobStrategyData = null;
        _jobStrategyRead = null;
        _jobStrategyCompanionReads.Clear();
        _jobStrategyJobNames = new Dictionary<int, string>();
        _jobStrategyConfiguredMagicCount = 0;
        _jobStrategyConfiguredMagicSource = string.Empty;
        _jobStrategyEditorGrid.DataSource = null;
        _saveJobStrategyEditorButton.Enabled = false;
        _jobStrategyEditorInfoBox.Text = "兵种策略：读取后可编辑策略基础属性、EKD5 策略附表属性，以及各详细兵种的学会等级。";
        ClearJobStrategyPreview("兵种策略：读取后选择“施法范围”“穿透范围”“策略图标”“小动画”“大动画”可显示右侧预览。");
        _currentJobEffectData = null;
        _jobEffectNameTable = null;
        _jobEffectDescriptionRead = null;
        _jobEffectAssignmentRead = null;
        _jobEffectNames = new Dictionary<int, string>();
        _jobEffectPersonNames = new Dictionary<int, string>();
        _jobEffectJobNames = new Dictionary<int, string>();
        _jobEffectEditorGrid.DataSource = null;
        _saveJobEffectEditorButton.Enabled = false;
        _jobEffectEditorInfoBox.Text = "兵种特效：读取后可编辑特效说明、武将/兵种分配和特效值；特效名称只读显示。";
        _currentItemEditorData = null;
        _itemBaseLowRead = null;
        _itemBaseHighRead = null;
        _itemDescriptionLowRead = null;
        _itemDescriptionHighRead = null;
        _itemEffectNames = new Dictionary<int, string>();
        _itemEditorGrid.DataSource = null;
        _saveItemEditorButton.Enabled = false;
        _itemEditorInfoBox.Text = "宝物设定：读取后按旧版表头顺序显示；特效名只读并随特效号动态映射，支持通过“宝物特效”维护项目侧 UTF-8 特效目录，隐藏图鉴、分段、重复价格和长解释列。";
        ClearItemIconPreview("宝物设定：读取后会按所选行的“图标”字段从 Itemicon.dll 显示候选图标。");
        _currentShopEditorData = null;
        _shopCampaignNameRead = null;
        _shopDataRead = null;
        _shopEditorPersonNames = new Dictionary<int, string>();
        _shopEditorItemNames = new Dictionary<int, string>();
        _shopEditorItemInfos = new Dictionary<int, ShopItemInfo>();
        _shopEditorGrid.DataSource = null;
        _shopEditorGrid.ReadOnly = true;
        _shopEditorSearchBox.Clear();
        _saveShopEditorButton.Enabled = false;
        _shopBatchSetButton.Enabled = false;
        _shopBatchClearButton.Enabled = false;
        _shopBatchReplaceButton.Enabled = false;
        _shopBatchSlotCombo.DataSource = null;
        _shopBatchSetItemCombo.DataSource = null;
        _shopBatchFindItemCombo.DataSource = null;
        _shopBatchReplaceItemCombo.DataSource = null;
        _shopEditorInfoBox.Text = "商店编辑：读取后可按关卡/商店槽位编辑战役名称、开关仓库人物、买卖物品人物、装备 1-16 和道具 17-32。";
        _roleEditorInfoBox.Text = "角色设定：读取后可编辑人物基础字段、头像、职业、等级、能力和 R/S 形象编号；保存前自动备份，保存后复读校验。";
        _chartColumnCombo.DataSource = null;
        _renderChartButton.Enabled = false;
        _tableChartBox.Image?.Dispose();
        _tableChartBox.Image = null;
        _tableChartInfoBox.Clear();
        _fieldAnnotationBox.Text = "字段说明：请选择数据表和单元格。";
        ClearCurrentTableReferenceTarget();
        _tableColumnFilterBox.Clear();
        _filterTableColumnsButton.Enabled = false;
        _clearTableColumnFilterButton.Enabled = false;
        _dangerTableColumnsOnly.Checked = false;
        _dangerTableColumnsOnly.Enabled = false;
        _exportFieldAnnotationsButton.Enabled = false;
        _exportVisibleColumnsCsvButton.Enabled = false;
        _visibleColumnsCsvWithNotes.Checked = true;
        _visibleColumnsCsvWithNotes.Enabled = false;
        _tableRowFilterBox.Clear();
        _filterTableRowsButton.Enabled = false;
        _clearTableRowFilterButton.Enabled = false;
        _changedTableRowsOnly.Checked = false;
        _changedTableRowsOnly.Enabled = false;
        _tableRowSearchVisibleColumnsOnly.Checked = true;
        _tableRowSearchVisibleColumnsOnly.Enabled = false;
        _currentAuditItems = Array.Empty<ProjectAuditItem>();
        _auditGrid.DataSource = null;
        _auditInfoBox.Clear();
        _writeAuditReportButton.Enabled = false;
        _currentProjectDiffItems = Array.Empty<ProjectDiffItem>();
        _projectDiffGrid.DataSource = null;
        _projectDiffInfoBox.Clear();
        _writeProjectDiffReportButton.Enabled = false;
        _createReleaseCopyButton.Enabled = false;
        _writeDeliveryReportButton.Enabled = false;
        _showDiffBackupTimelineButton.Enabled = false;
        _projectDiffStatusFilterCombo.Items.Clear();
        _projectDiffSearchBox.Clear();
        _currentBackupHistoryItems = Array.Empty<BackupHistoryItem>();
        _backupHistoryGrid.DataSource = null;
        _backupHistoryInfoBox.Clear();
        _currentProjectEvidenceItems = Array.Empty<ProjectEvidenceItem>();
        _workflowEvidenceGrid.DataSource = null;
        _currentCreatorNotes = Array.Empty<CreatorNote>();
        _creatorNoteGrid.DataSource = null;
        ClearCreatorNoteEditor();
        _creatorNoteSearchBox.Clear();
        _currentImageAssignments = null;
        _imageAssignmentSummaryText = string.Empty;
        _imageAssignmentGrid.DataSource = null;
        _imageAssignmentGrid.ReadOnly = true;
        _imageAssignmentSearchBox.Clear();
        _imageAssignmentMissingOnlyCheckBox.Checked = false;
        _saveImageAssignmentsButton.Enabled = false;
        _imageAssignmentInfoBox.Clear();
        ClearImageAssignmentPreview();
        _currentPatchPreview = null;
        _applyPatchButton.Enabled = false;
        _patchGrid.DataSource = null;
        _patchInfoBox.Clear();
        _currentBatchMoveDocument = null;
        _moveGrid.DataSource = null;
        _moveInfoBox.Clear();
        _currentSceneStringDocument = null;
        _sceneCommandGrid.DataSource = null;
        _sceneGroupGrid.DataSource = null;
        _sceneDictionaryInfoBox.Clear();
        _currentMaterialAssets = Array.Empty<MaterialAsset>();
        _materialGrid.DataSource = null;
        _materialPreview.Image?.Dispose();
        _materialPreview.Image = null;
        _currentImageResourceFiles = Array.Empty<ImageResourceFileInfo>();
        _currentImageResourceEntries = Array.Empty<ImageResourceEntryInfo>();
        _imageResourceFileGrid.DataSource = null;
        _imageResourceEntryGrid.DataSource = null;
        _imageResourceCategoryFilterCombo.Items.Clear();
        _imageResourceSearchBox.Clear();
        _imageResourceCatalogService.ClearCache();
        SetPictureBoxImage(_imageResourcePreviewBox, null);
        _imageResourceInfoBox.Text = "图片处理：读取后可管理 Face/R/S、范围图、策略动画、背景图、R 插图、界面图等 E5 图片资源。";
        _imageResourceEntryInfoBox.Clear();
        _materialInfoBox.Clear();
        _mapMakerSelectedMaterial = null;
        _mapWorkbenchSettings = new MapWorkbenchSettings();
        _currentMapWorkbenchDraft = null;
        _mapMakerMaterialGrid.DataSource = null;
        _mapMakerMaterialCategoryCombo.Items.Clear();
        _mapMakerMaterialSearchBox.Clear();
        _mapMakerMaterialPreview.Image?.Dispose();
        _mapMakerMaterialPreview.Image = null;
        _mapMakerMaterialInfoBox.Text = "素材库：点击“选择素材库”后从本地目录读取图片。地图画笔会把选中素材整张缩放到 48x48 并覆盖当前格。";
        _currentGameResources = Array.Empty<ResourceIndexItem>();
        _gameResourceGrid.DataSource = null;
        _gameResourcePreview.Image?.Dispose();
        _gameResourcePreview.Image = null;
        _gameResourceInfoBox.Clear();
        _currentResourceDiagnostics = Array.Empty<ResourceDiagnosticItem>();
        _resourceDiagnosticGrid.DataSource = null;
        _resourceDiagnosticInfoBox.Clear();
        _resourceDiagnosticSeverityFilterCombo.Items.Clear();
        _resourceDiagnosticCategoryFilterCombo.Items.Clear();
        _resourceDiagnosticSearchBox.Clear();
        _jumpDiagnosticScenarioMapButton.Enabled = false;
        _jumpDiagnosticScenarioButton.Enabled = false;
        _jumpDiagnosticHexzmapButton.Enabled = false;
        _jumpDiagnosticMapViewerButton.Enabled = false;
        _currentEexArchives = Array.Empty<EexArchiveInfo>();
        _eexArchiveGrid.DataSource = null;
        _currentEexEntryProbeRows = Array.Empty<EexEntryProbeRow>();
        _eexEntryProbeGrid.DataSource = null;
        _eexEntryTree.Nodes.Clear();
        _eexEntryTreeInfoBox.Clear();
        _currentEexCrossFileComparison = null;
        _eexCrossFileGrid.DataSource = null;
        _eexCrossFileInfoBox.Clear();
        _eexArchiveInfoBox.Clear();
        _currentScenarioFiles = Array.Empty<ScenarioFileInfo>();
        _scenarioFileGrid.DataSource = null;
        _currentScenarioCommandProbeRows = Array.Empty<ScenarioCommandProbeRow>();
        _scenarioCommandProbeGrid.DataSource = null;
        _currentScenarioStructureResult = null;
        _scenarioStructureGrid.DataSource = null;
        _scenarioStructureTree.Nodes.Clear();
        _scenarioStructureNodeInfoBox.Clear();
        _scenarioStructureXmlBox.Clear();
        _currentScenarioCommandTemplateItems = Array.Empty<ScenarioCommandTemplateCatalogItem>();
        _scenarioCommandTemplateGrid.DataSource = null;
        _scenarioCommandTemplateInfoBox.Clear();
        _scenarioCommandTemplateSearchBox.Clear();
        _scenarioCommandTemplateCategoryCombo.Items.Clear();
        _scenarioCommandTemplateStatusCombo.Items.Clear();
        _filterScenarioCommandTemplatesButton.Enabled = false;
        _clearScenarioCommandTemplateFilterButton.Enabled = false;
        _showScenarioCommandTemplateInStructureButton.Enabled = false;
        ClearScenarioCommandReferenceTargets();
        ResetScenarioStructureFilterControls();
        _currentScenarioTextEntries = Array.Empty<ScenarioTextEntry>();
        _scenarioTextGrid.DataSource = null;
        _currentBattlefieldDocument = null;
        ClearBattlefieldInstructionPreviewState();
        _battlefieldAllyDeploymentSlots = Array.Empty<BattlefieldAllyDeploymentSlot>();
        _battlefieldScenarioCombo.DataSource = null;
        _battlefieldTitleBox.Clear();
        _battlefieldConditionsBox.Clear();
        _battlefieldTitleBytesLabel.Text = "标题容量：未读取";
        _battlefieldConditionsBytesLabel.Text = "胜败条件容量：未读取";
        _battlefieldUnitGrid.DataSource = null;
        _battlefieldUnitCategoryFilterCombo.Items.Clear();
        _battlefieldUnitFilterBox.Clear();
        _battlefieldMapClickMemoCheckBox.Checked = false;
        _reloadBattlefieldScenarioAfterCurrentLoad = false;
        ClearBattlefieldManualMarker();
        _battlefieldCommandGrid.DataSource = null;
        ClearBattlefieldMapPreviewImages();
        _battlefieldMapHintLabel.Text = "地图预览：选择出场/坐标候选后，如果能解析到地图格坐标，会在地图上用红圈标记。";
        _battlefieldInfoBox.Text = "战场制作：读取关卡后可编辑标题/胜败条件文本，并联动显示地图底图、地形层和战场命令候选。未知 R/S eex 命令结构不强写。";
        _saveBattlefieldTextsButton.Enabled = false;
        _saveBattlefieldUnitReviewsButton.Enabled = false;
        _writeBattlefieldDeploymentButton.Enabled = false;
        _createBattlefieldNoteButton.Enabled = false;
        _jumpBattlefieldMapButton.Enabled = false;
        _jumpBattlefieldScenarioButton.Enabled = false;
        _currentScriptStructure = null;
        _currentLegacyScriptDocument = null;
        _currentScriptTextEntries = Array.Empty<ScenarioTextEntry>();
        _currentScriptScenario = null;
        _selectedScriptCommandRow = null;
        _selectedScriptTextEntry = null;
        _legacyScriptCommandByKey.Clear();
        _legacyScriptRowByKey.Clear();
        _legacyScriptTextByOffset.Clear();
        _legacyScriptTextEntryByOffset.Clear();
        _scriptScenarioCombo.DataSource = null;
        _scriptNewCommandCombo.DataSource = null;
        _scriptNewCommandCombo.Enabled = false;
        _scriptTree.Nodes.Clear();
        _scriptCommandGrid.DataSource = null;
        _scriptParameterGrid.DataSource = null;
        ClearLegacyScriptParameterEditor();
        _scriptTextGrid.DataSource = null;
        _scriptTextEditorBox.Clear();
        _scriptTextCapacityLabel.Text = "文本容量：未选择";
        _scriptDetailBox.Text = "剧本制作：读取 RS/R_*.eex / RS/S_*.eex 后按 CczSceneEditor2 v0.23 习惯显示 Scene / Section / Command 树；文本参数挂在命令节点下，选中具体文本参数后编辑。";
        _showScriptVariablesButton.Enabled = false;
        _locateScriptCommandButton.Enabled = false;
        _copyScriptCommandButton.Enabled = false;
        _saveScriptTextButton.Enabled = false;
        _saveScriptStructureButton.Enabled = false;
        _createScriptNoteButton.Enabled = false;
        _jumpScriptBattlefieldButton.Enabled = false;
        UpdateScriptStructureEditButtons();
        _probeScenarioCommandsButton.Enabled = false;
        _buildScenarioStructureButton.Enabled = false;
        _exportScenarioStructureXmlButton.Enabled = false;
        _exportScenarioCommandTemplateCatalogButton.Enabled = false;
        _probeScenarioTextsButton.Enabled = false;
        _exportScenarioTextsButton.Enabled = false;
        _saveScenarioTextsButton.Enabled = false;
        _scenarioTextFilterBox.Clear();
        _scenarioTextFilterButton.Enabled = false;
        _scenarioTextFilterClearButton.Enabled = false;
        _scenarioTextChangedOnly.Checked = false;
        _scenarioTextChangedOnly.Enabled = false;
        _scenarioFileInfoBox.Clear();
        _currentLsResources = Array.Empty<LsResourceInfo>();
        _lsResourceGrid.DataSource = null;
        _lsResourceInfoBox.Clear();
        ClearLsResourceHeatmapPreview();
        _currentHexzmapProbe = null;
        _hexzmapGrid.DataSource = null;
        _hexzmapInfoBox.Clear();
        _hexzmapPreviewBox.Image?.Dispose();
        _hexzmapPreviewBox.Image = null;
        _exportHexzmapProbeCsvButton.Enabled = false;
        _exportHexzmapOverlayPngButton.Enabled = false;
        _terrainEditorBlock = null;
        _terrainEditorCells = Array.Empty<byte>();
        _terrainEditorOriginalCells = Array.Empty<byte>();
        _terrainEditorTerrainLookup = new Dictionary<byte, string>();
        _currentScenarioMapLinks = Array.Empty<ScenarioMapLinkInfo>();
        _scenarioMapLinkGrid.DataSource = null;
        _scenarioMapLinkInfoBox.Clear();
        _scenarioMapImagePreviewBox.Image?.Dispose();
        _scenarioMapImagePreviewBox.Image = null;
        _scenarioMapTerrainPreviewBox.Image?.Dispose();
        _scenarioMapTerrainPreviewBox.Image = null;
        _exportScenarioMapLinksCsvButton.Enabled = false;
        _locateScenarioMapScenarioButton.Enabled = false;
        _locateScenarioMapImageButton.Enabled = false;
        _jumpScenarioMapScenarioButton.Enabled = false;
        _jumpScenarioMapHexzmapButton.Enabled = false;
        _jumpScenarioMapViewerButton.Enabled = false;
        _exportScenarioMapPreviewPngButton.Enabled = false;
        _scenarioMapLinkStatusFilterCombo.Items.Clear();
        _scenarioMapLinkSearchBox.Clear();
        _filterScenarioMapLinksButton.Enabled = false;
        _clearScenarioMapLinkFilterButton.Enabled = false;
        _scenarioMapLinksIncompleteOnly.Checked = false;
        _scenarioMapLinksIncompleteOnly.Enabled = false;
        _mapImageList.DataSource = null;
        _currentMapMakerItem = null;
        _mapMakerTerrainPresetCombo.DataSource = null;
        _mapMakerGridWidthInput.Value = 30;
        _mapMakerGridHeightInput.Value = 30;
        _mapMakerGridWidthInput.Enabled = true;
        _mapMakerGridHeightInput.Enabled = true;
        _mapMakerBrushModeCombo.SelectedIndex = 0;
        _mapWorkbenchBrushMode = MapWorkbenchBrushMode.Browse;
        _mapMakerEditTerrainCheckBox.Checked = false;
        _mapMakerEditTerrainCheckBox.Enabled = false;
        _mapMakerReplaceMapImageButton.Enabled = false;
        _mapMakerExportPreviewButton.Enabled = false;
        _mapMakerExportJpgButton.Enabled = false;
        _mapMakerPublishMapButton.Enabled = false;
        _mapMakerPublishTerrainButton.Enabled = false;
        _mapMakerSaveDraftButton.Enabled = false;
        _mapMakerSaveTerrainButton.Enabled = false;
        _mapMakerUndoTerrainButton.Enabled = false;
        _mapMakerRedoTerrainButton.Enabled = false;
        ResetMapWorkbenchHistory();
        _mapViewerBox.Image = null;
        _mapViewerRenderedImage?.Dispose();
        _mapViewerRenderedImage = null;
        _mapViewerImage?.Dispose();
        _mapViewerImage = null;
        _mapViewerBox.Size = Size.Empty;
        _mapViewerInfoBox.Text = "地图制作：统一地图绘制工作台。新建草稿默认 30x30，每格 48x48；地图画笔覆盖格图片，地形画笔编辑草稿地形层；绑定现有 Mxxx 槽位且尺寸一致时才允许发布到游戏。";

        if (!File.Exists(project.HexTableXmlPath))
        {
            _tables = Array.Empty<HexTableDefinition>();
            _legacyMfcDialogDataSources = null;
            _legacyScenarioCommandDisplayFormatter = null;
            _tableList.DataSource = null;
            Log(ProjectDetector.BuildMissingHexTableMessage(project));
            SetStatus("HexTable.xml 缺失");
            RememberLocatedObjectContextForNotes();
            RefreshWorkflowGuide(updateStatus: false);
            return;
        }

        _tables = _tableParser.Load(project.HexTableXmlPath);
        _legacyMfcDialogDataSources = null;
        _legacyScenarioCommandDisplayFormatter = null;
        _writeDeliveryReportButton.Enabled = true;
        _exportScenarioCommandTemplateCatalogButton.Enabled = true;
        LoadSceneDictionary(showMessages: false);
        IndexMaterialLibrary(showMessages: false);
        LoadMapWorkbenchSettings();
        LoadScenarioCommandTemplates(silent: true);
        LoadCreatorNotes(silent: true);
        RefreshTableList();
        Log($"已加载项目：{project.GameRoot}");
        Log($"已读取 HexTable 表定义：{_tables.Count} 个。默认显示启用的 6.X 表。");
        SetStatus($"已加载 {_tables.Count} 个表定义（当前项目可编辑，保存前自动备份）");
        RefreshWorkflowGuide(updateStatus: false);
    }

    private void RefreshWorkflowGuide(bool updateStatus = true)
    {
        _currentWorkflowDashboardItems = _workflowGuideService.BuildDashboard(
            _project,
            _tables.Count,
            _currentAuditItems,
            _currentResourceDiagnostics,
            _currentProjectDiffItems,
            _currentBackupHistoryItems,
            _currentCreatorNotes,
            _currentScenarioMapLinks,
            _currentScenarioStructureResult,
            _currentEexArchives,
            _currentLsResources,
            _currentHexzmapProbe);
        _currentWorkflowActionItems = _workflowGuideService.BuildActionItems(_project, _currentWorkflowDashboardItems, _currentCreatorNotes);
        _currentWorkflowSteps = _workflowGuideService.BuildSteps(
            _project,
            _tables.Count,
            _currentAuditItems.Count,
            _currentProjectDiffItems.Count,
            _currentBackupHistoryItems.Count,
            _currentCreatorNotes.Count);
        _workflowActionGrid.DataSource = new BindingList<WorkflowActionItem>(_currentWorkflowActionItems.ToList());
        ConfigureWorkflowActionGrid();
        _workflowDashboardGrid.DataSource = new BindingList<WorkflowDashboardItem>(_currentWorkflowDashboardItems.ToList());
        ConfigureWorkflowDashboardGrid();
        RefreshProjectEvidence(updateStatus: false);
        _workflowGuideGrid.DataSource = new BindingList<WorkflowGuideStep>(_currentWorkflowSteps.ToList());
        ConfigureWorkflowGuideGrid();
        _workflowGuideInfoBox.Text = BuildWorkflowGuideOverviewText();
        if (updateStatus) SetStatus("制作向导流程状态已刷新");
    }

    private string BuildWorkflowGuideOverviewText()
        => _workflowGuideService.BuildSummary(_project, _currentWorkflowSteps, _currentWorkflowDashboardItems) +
           "\r\n\r\n" +
           _workflowGuideService.BuildActionPlan(_project, _currentWorkflowDashboardItems) +
           "\r\n\r\n" +
           _projectEvidenceService.BuildSummary(_project, _currentProjectEvidenceItems);

    private void RefreshProjectEvidence(bool updateStatus = true)
    {
        _currentProjectEvidenceItems = _projectEvidenceService.Scan(_project);
        _workflowEvidenceGrid.DataSource = new BindingList<ProjectEvidenceItem>(_currentProjectEvidenceItems.ToList());
        ConfigureProjectEvidenceGrid();
        if (updateStatus)
        {
            _workflowGuideInfoBox.Text = BuildWorkflowGuideOverviewText();
            SetStatus($"最近报告/发布证据已刷新：{_currentProjectEvidenceItems.Count} 项");
        }
    }

    private void ConfigureWorkflowActionGrid()
    {
        if (_workflowActionGrid.Columns.Count == 0) return;
        SetWorkflowActionColumn(nameof(WorkflowActionItem.PriorityNo), "优先级", 65);
        SetWorkflowActionColumn(nameof(WorkflowActionItem.Level), "级别", 70);
        SetWorkflowActionColumn(nameof(WorkflowActionItem.TargetArea), "关联工作台", 140);
        SetWorkflowActionColumn(nameof(WorkflowActionItem.Action), "建议行动", 420);
        SetWorkflowActionColumn(nameof(WorkflowActionItem.ExpectedResult), "预期结果", 360);
        SetWorkflowActionColumn(nameof(WorkflowActionItem.SafetyNote), "安全说明", 360);
        SetWorkflowActionColumn(nameof(WorkflowActionItem.NoteHint), "备注提示", 260);
        HideNonAuthoringColumns(
            _workflowActionGrid,
            nameof(WorkflowActionItem.SafetyNote),
            nameof(WorkflowActionItem.NoteHint));

        foreach (DataGridViewRow row in _workflowActionGrid.Rows)
        {
            if (row.DataBoundItem is not WorkflowActionItem item) continue;
            row.DefaultCellStyle.BackColor = item.Level switch
            {
                "风险" => Color.MistyRose,
                "提醒" => Color.LemonChiffon,
                _ => Color.Honeydew
            };
            row.HeaderCell.Value = item.PriorityNo.ToString(CultureInfo.InvariantCulture);
        }
    }

    private void SetWorkflowActionColumn(string propertyName, string headerText, int width)
    {
        if (!_workflowActionGrid.Columns.Contains(propertyName)) return;
        var column = _workflowActionGrid.Columns[propertyName];
        column.HeaderText = headerText;
        column.Width = width;
        column.AutoSizeMode = DataGridViewAutoSizeColumnMode.None;
    }

    private void ConfigureWorkflowDashboardGrid()
    {
        if (_workflowDashboardGrid.Columns.Count == 0) return;
        SetWorkflowDashboardColumn(nameof(WorkflowDashboardItem.Area), "工作台项目", 140);
        SetWorkflowDashboardColumn(nameof(WorkflowDashboardItem.Level), "级别", 70);
        SetWorkflowDashboardColumn(nameof(WorkflowDashboardItem.Value), "当前值", 150);
        SetWorkflowDashboardColumn(nameof(WorkflowDashboardItem.Summary), "摘要", 380);
        SetWorkflowDashboardColumn(nameof(WorkflowDashboardItem.Suggestion), "建议", 380);
        SetWorkflowDashboardColumn(nameof(WorkflowDashboardItem.RelatedPage), "相关页面", 220);
        SetWorkflowDashboardColumn(nameof(WorkflowDashboardItem.Evidence), "证据/路径", 420);
        HideNonAuthoringColumns(
            _workflowDashboardGrid,
            nameof(WorkflowDashboardItem.Suggestion),
            nameof(WorkflowDashboardItem.RelatedPage),
            nameof(WorkflowDashboardItem.Evidence));

        foreach (DataGridViewRow row in _workflowDashboardGrid.Rows)
        {
            if (row.DataBoundItem is not WorkflowDashboardItem item) continue;
            row.DefaultCellStyle.BackColor = item.Level switch
            {
                "风险" => Color.MistyRose,
                "提醒" => Color.LemonChiffon,
                _ => Color.Honeydew
            };
            row.HeaderCell.Value = item.Level;
        }
    }

    private void SetWorkflowDashboardColumn(string propertyName, string headerText, int width)
    {
        if (!_workflowDashboardGrid.Columns.Contains(propertyName)) return;
        var column = _workflowDashboardGrid.Columns[propertyName];
        column.HeaderText = headerText;
        column.Width = width;
        column.AutoSizeMode = DataGridViewAutoSizeColumnMode.None;
    }

    private void ConfigureProjectEvidenceGrid()
    {
        if (_workflowEvidenceGrid.Columns.Count == 0) return;
        SetProjectEvidenceColumn(nameof(ProjectEvidenceItem.Category), "证据分类", 130);
        SetProjectEvidenceColumn(nameof(ProjectEvidenceItem.Kind), "证据类型", 150);
        SetProjectEvidenceColumn(nameof(ProjectEvidenceItem.FileName), "文件名", 260);
        SetProjectEvidenceColumn(nameof(ProjectEvidenceItem.LastWriteTimeText), "生成/修改时间", 150);
        SetProjectEvidenceColumn(nameof(ProjectEvidenceItem.SizeText), "大小", 80);
        SetProjectEvidenceColumn(nameof(ProjectEvidenceItem.SourceRoot), "来源目录", 130);
        SetProjectEvidenceColumn(nameof(ProjectEvidenceItem.Annotation), "中文说明", 420);
        SetProjectEvidenceColumn(nameof(ProjectEvidenceItem.SuggestedUse), "创作者用途", 360);
        SetProjectEvidenceColumn(nameof(ProjectEvidenceItem.SafetyNote), "安全说明", 320);
        SetProjectEvidenceColumn(nameof(ProjectEvidenceItem.FullPath), "完整路径", 420);
        HideNonAuthoringColumns(
            _workflowEvidenceGrid,
            nameof(ProjectEvidenceItem.LastWriteTime),
            nameof(ProjectEvidenceItem.SizeBytes),
            nameof(ProjectEvidenceItem.SourceRoot),
            nameof(ProjectEvidenceItem.FullPath),
            nameof(ProjectEvidenceItem.Annotation),
            nameof(ProjectEvidenceItem.SuggestedUse),
            nameof(ProjectEvidenceItem.SafetyNote));

        foreach (DataGridViewRow row in _workflowEvidenceGrid.Rows)
        {
            if (row.DataBoundItem is not ProjectEvidenceItem item) continue;
            row.DefaultCellStyle.BackColor = item.Category switch
            {
                "发布证据" => Color.Honeydew,
                "安全检查证据" => Color.AliceBlue,
                "写入/回滚证据" => Color.LemonChiffon,
                "R/S剧本证据" => Color.Lavender,
                "地图/可视化证据" => Color.MintCream,
                _ => Color.White
            };
            row.HeaderCell.Value = item.Kind;
        }
    }

    private void SetProjectEvidenceColumn(string propertyName, string headerText, int width)
    {
        if (!_workflowEvidenceGrid.Columns.Contains(propertyName)) return;
        var column = _workflowEvidenceGrid.Columns[propertyName];
        column.HeaderText = headerText;
        column.Width = width;
        column.AutoSizeMode = DataGridViewAutoSizeColumnMode.None;
    }

    private void ConfigureWorkflowGuideGrid()
    {
        if (_workflowGuideGrid.Columns.Count == 0) return;
        SetWorkflowGuideColumn(nameof(WorkflowGuideStep.StepNo), "步骤", 55);
        SetWorkflowGuideColumn(nameof(WorkflowGuideStep.Stage), "阶段", 70);
        SetWorkflowGuideColumn(nameof(WorkflowGuideStep.Title), "目标", 210);
        SetWorkflowGuideColumn(nameof(WorkflowGuideStep.Status), "当前状态", 130);
        SetWorkflowGuideColumn(nameof(WorkflowGuideStep.RecommendedAction), "建议操作", 420);
        SetWorkflowGuideColumn(nameof(WorkflowGuideStep.RelatedPage), "相关页面", 220);
        SetWorkflowGuideColumn(nameof(WorkflowGuideStep.WhyItMatters), "为什么重要", 340);
        SetWorkflowGuideColumn(nameof(WorkflowGuideStep.SafetyNote), "安全说明", 320);
        HideNonAuthoringColumns(
            _workflowGuideGrid,
            nameof(WorkflowGuideStep.RelatedPage),
            nameof(WorkflowGuideStep.WhyItMatters),
            nameof(WorkflowGuideStep.SafetyNote));

        foreach (DataGridViewRow row in _workflowGuideGrid.Rows)
        {
            if (row.DataBoundItem is not WorkflowGuideStep step) continue;
            row.DefaultCellStyle.BackColor =
                step.Status.Contains("需要", StringComparison.Ordinal) || step.Status.Contains("强烈", StringComparison.Ordinal)
                    ? Color.MistyRose
                    : step.Status.Contains("建议", StringComparison.Ordinal) || step.Status.Contains("等待", StringComparison.Ordinal)
                        ? Color.LemonChiffon
                        : Color.Honeydew;
            row.HeaderCell.Value = step.StepNo.ToString(CultureInfo.InvariantCulture);
        }
    }

    private void SetWorkflowGuideColumn(string propertyName, string headerText, int width)
    {
        if (!_workflowGuideGrid.Columns.Contains(propertyName)) return;
        var column = _workflowGuideGrid.Columns[propertyName];
        column.HeaderText = headerText;
        column.Width = width;
        column.AutoSizeMode = DataGridViewAutoSizeColumnMode.None;
    }

    private WorkflowGuideStep? GetSelectedWorkflowStep()
    {
        if (_workflowGuideGrid.SelectedRows.Count > 0 && _workflowGuideGrid.SelectedRows[0].DataBoundItem is WorkflowGuideStep selected) return selected;
        if (_workflowGuideGrid.CurrentRow?.DataBoundItem is WorkflowGuideStep current) return current;
        return null;
    }

    private WorkflowActionItem? GetSelectedWorkflowActionItem()
    {
        if (_workflowActionGrid.SelectedRows.Count > 0 && _workflowActionGrid.SelectedRows[0].DataBoundItem is WorkflowActionItem selected) return selected;
        if (_workflowActionGrid.CurrentRow?.DataBoundItem is WorkflowActionItem current) return current;
        return null;
    }

    private WorkflowDashboardItem? GetSelectedWorkflowDashboardItem()
    {
        if (_workflowDashboardGrid.SelectedRows.Count > 0 && _workflowDashboardGrid.SelectedRows[0].DataBoundItem is WorkflowDashboardItem selected) return selected;
        if (_workflowDashboardGrid.CurrentRow?.DataBoundItem is WorkflowDashboardItem current) return current;
        return null;
    }

    private ProjectEvidenceItem? GetSelectedProjectEvidenceItem()
    {
        if (_workflowEvidenceGrid.SelectedRows.Count > 0 && _workflowEvidenceGrid.SelectedRows[0].DataBoundItem is ProjectEvidenceItem selected) return selected;
        if (_workflowEvidenceGrid.CurrentRow?.DataBoundItem is ProjectEvidenceItem current) return current;
        return null;
    }

    private void ShowSelectedWorkflowActionItem()
    {
        var item = GetSelectedWorkflowActionItem();
        if (item == null)
        {
            _workflowGuideInfoBox.Text = BuildWorkflowGuideOverviewText();
            return;
        }

        _workflowGuideInfoBox.Text =
            $"优先行动 #{item.PriorityNo}    级别：{item.Level}    关联工作台：{item.TargetArea}\r\n" +
            $"建议行动：{item.Action}\r\n" +
            $"预期结果：{item.ExpectedResult}\r\n" +
            $"安全说明：{item.SafetyNote}\r\n\r\n" +
            $"备注提示：{item.NoteHint}\r\n\r\n" +
            "提示：双击本行动，或点击“处理选中行动”，会跳转到对应页面并尽量自动筛选/定位第一条风险对象；点击“处理并建对象备注”会处理后为定位到的具体对象创建备注；点击“为行动建备注”会创建行动级备注。两类备注都不写入游戏文件。\r\n\r\n" +
            BuildWorkflowGuideOverviewText();
    }

    private void ShowSelectedWorkflowDashboardItem()
    {
        var item = GetSelectedWorkflowDashboardItem();
        if (item == null)
        {
            _workflowGuideInfoBox.Text = BuildWorkflowGuideOverviewText();
            return;
        }

        _workflowGuideInfoBox.Text =
            $"工作台：{item.Area}    级别：{item.Level}    当前值：{item.Value}\r\n" +
            $"摘要：{item.Summary}\r\n" +
            $"建议：{item.Suggestion}\r\n" +
            $"相关页面：{item.RelatedPage}\r\n" +
            $"证据/路径：{item.Evidence}\r\n\r\n" +
            BuildWorkflowGuideOverviewText();
    }

    private void ShowSelectedProjectEvidenceItem()
    {
        var item = GetSelectedProjectEvidenceItem();
        if (item == null)
        {
            _workflowGuideInfoBox.Text = BuildWorkflowGuideOverviewText();
            return;
        }

        SetLastCreatorNoteContext(
            "报告/发布证据",
            BuildProjectEvidenceCreatorNoteTargetKey(item),
            $"{item.Kind}：{item.FileName}",
            "从制作向导最近报告/发布证据表抓取；该证据文件不参与游戏运行。",
            $"证据类型：{item.Kind}\r\n文件：{item.FullPath}\r\n中文说明：{item.Annotation}\r\n用途：{item.SuggestedUse}\r\n复查结论：\r\n实机验证：");
        _workflowGuideInfoBox.Text =
            $"报告/发布证据：{item.Kind}    分类：{item.Category}    时间：{item.LastWriteTimeText}    大小：{item.SizeText}\r\n" +
            $"文件：{item.FileName}\r\n" +
            $"路径：{item.FullPath}\r\n" +
            $"中文说明：{item.Annotation}\r\n" +
            $"创作者用途：{item.SuggestedUse}\r\n" +
            $"安全说明：{item.SafetyNote}\r\n\r\n" +
            "提示：双击本行或点击“打开选中证据”可直接打开；点击“定位证据文件”会在资源管理器中定位；也可以点击“创作者备注 -> 抓取当前选择”为该证据写复查备注。\r\n\r\n" +
            BuildWorkflowGuideOverviewText();
    }

    private void OpenSelectedProjectEvidence()
    {
        var item = GetSelectedProjectEvidenceItem();
        if (item == null)
        {
            MessageBox.Show(this, "请先在“最近报告/发布证据”表中选择一项。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        if (!File.Exists(item.FullPath))
        {
            MessageBox.Show(this, "证据文件不存在：" + item.FullPath, "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            RefreshProjectEvidence(updateStatus: false);
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = item.FullPath,
                UseShellExecute = true
            });
            SetStatus("已打开证据文件：" + item.FileName);
        }
        catch
        {
            OpenFileLocation(item.FullPath);
        }
    }

    private void LocateSelectedProjectEvidence()
    {
        var item = GetSelectedProjectEvidenceItem();
        if (item == null)
        {
            MessageBox.Show(this, "请先在“最近报告/发布证据”表中选择一项。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        if (!File.Exists(item.FullPath))
        {
            MessageBox.Show(this, "证据文件不存在：" + item.FullPath, "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            RefreshProjectEvidence(updateStatus: false);
            return;
        }

        OpenFileLocation(item.FullPath);
    }

    private static string BuildProjectEvidenceCreatorNoteTargetKey(ProjectEvidenceItem item)
        => $"Evidence#{item.Kind}#{item.FileName}#{item.LastWriteTime:yyyyMMddHHmmss}";

    private void HandleSelectedWorkflowActionItem()
    {
        var action = GetSelectedWorkflowActionItem();
        if (action == null)
        {
            MessageBox.Show(this, "请先选择一个优先行动。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        if (action.TargetArea == "项目状态")
        {
            OpenProjectDialog();
            return;
        }

        var dashboardItem = _currentWorkflowDashboardItems.FirstOrDefault(x => x.Area == action.TargetArea);
        if (dashboardItem == null)
        {
            MessageBox.Show(this, $"没有找到关联工作台项：{action.TargetArea}", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var selected = SelectGridRow<WorkflowDashboardItem>(_workflowDashboardGrid, x => x.Area == dashboardItem.Area);
        if (!selected)
        {
            _workflowDashboardGrid.ClearSelection();
        }

        HandleSelectedWorkflowDashboardItem();
    }

    private void HandleSelectedWorkflowActionAndCreateLocatedNote()
    {
        var action = GetSelectedWorkflowActionItem();
        if (action == null)
        {
            MessageBox.Show(this, "请先选择一个优先行动。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        HandleSelectedWorkflowActionItem();
        if (_project == null) return;

        var context = BuildCreatorNoteContextFromSelection();
        if (context.Scope == "全局项目")
        {
            MessageBox.Show(this, "已处理行动，但暂未定位到可生成对象备注的具体数据行。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        CreateCreatorNoteFromContext(
            context,
            $"从优先行动“{action.TargetArea}”处理后定位对象创建。",
            $"{action.Level},对象证据,待办");
    }

    private void CreateCreatorNoteFromSelectedWorkflowAction()
    {
        if (_project == null)
        {
            MessageBox.Show(this, "请先加载项目。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        var action = GetSelectedWorkflowActionItem();
        if (action == null)
        {
            MessageBox.Show(this, "请先选择一个优先行动。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        try
        {
            var targetKey = ProjectWorkflowGuideService.BuildWorkflowActionTargetKey(action.TargetArea);
            var saved = _creatorNoteService.Upsert(_project, new CreatorNote
            {
                Scope = "工作台行动",
                TargetKey = targetKey,
                Title = $"行动#{action.PriorityNo}：{action.TargetArea}",
                Tags = $"{action.Level},工作台行动,待办",
                SourceHint = "从制作向导优先行动表创建。",
                Content =
                    $"关联工作台：{action.TargetArea}\r\n" +
                    $"建议行动：{action.Action}\r\n" +
                    $"预期结果：{action.ExpectedResult}\r\n" +
                    $"安全说明：{action.SafetyNote}\r\n\r\n" +
                    "处理记录：\r\n" +
                    "实机验证：\r\n" +
                    "回滚点/备份："
            });

            _currentCreatorNotes = _creatorNoteService.Load(_project);
            BindCreatorNoteRows(_currentCreatorNotes);
            SelectCreatorNoteById(saved.Id);
            SelectTabPageByText("创作者备注");
            _creatorNoteInfoBox.Text =
                "已为优先行动创建项目侧备注。\r\n" +
                "该备注只保存在 CCZModStudio_Notes，不写入游戏文件。\r\n\r\n" +
                _creatorNoteService.BuildSummary(_project, _currentCreatorNotes);
            SetStatus("已为优先行动创建创作者备注");
            RememberLocatedObjectContextForNotes();
            RefreshWorkflowGuide(updateStatus: false);
        }
        catch (Exception ex)
        {
            Log("为优先行动创建备注失败：" + ex);
            MessageBox.Show(this, ex.Message, "创建行动备注失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void RememberLocatedObjectContextForNotes()
    {
        var context = BuildCreatorNoteContextFromSelection();
        if (context.Scope == "全局项目" || string.IsNullOrWhiteSpace(context.TargetKey)) return;
        SetLastCreatorNoteContext(context.Scope, context.TargetKey, context.Title, context.SourceHint, context.ContentSeed);
    }

    private void CreateCreatorNoteFromContext(
        (string Scope, string TargetKey, string Title, string SourceHint, string ContentSeed) context,
        string sourceHint,
        string tags)
    {
        if (_project == null)
        {
            MessageBox.Show(this, "请先加载项目。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        var saved = _creatorNoteService.Upsert(_project, new CreatorNote
        {
            Scope = context.Scope,
            TargetKey = context.TargetKey,
            Title = context.Title,
            Tags = tags,
            SourceHint = sourceHint + " " + context.SourceHint,
            Content = context.ContentSeed
        });

        _currentCreatorNotes = _creatorNoteService.Load(_project);
        BindCreatorNoteRows(_currentCreatorNotes);
        SelectCreatorNoteById(saved.Id);
        SelectTabPageByText("创作者备注");
        _creatorNoteInfoBox.Text =
            "已为当前定位对象创建项目侧备注。\r\n" +
            "该备注只保存在 CCZModStudio_Notes，不写入游戏文件。\r\n\r\n" +
            _creatorNoteService.BuildSummary(_project, _currentCreatorNotes);
        SetStatus("已为当前定位对象创建创作者备注");
        RefreshWorkflowGuide(updateStatus: false);
    }

    private void HandleSelectedWorkflowDashboardItem()
    {
        var item = GetSelectedWorkflowDashboardItem();
        if (item == null)
        {
            MessageBox.Show(this, "请先选择一个工作台项目。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        try
        {
            switch (item.Area)
            {
                case "写入模式":
                    if (_project?.IsTestCopy == true)
                    {
                        SelectTabPageByText("测试副本差异/发布");
                        if (_currentProjectDiffItems.Count == 0) AnalyzeProjectDiff();
                    }
                    else
                    {
                        CreateTestCopy();
                    }
                    break;

                case "数据表与中文注释":
                    SelectTabPageByText("角色设定");
                    SetStatus("通用数据表入口已移除；请使用角色、兵种、宝物、商店等专用编辑页。");
                    break;

                case "项目体检":
                    SelectTabPageByText("项目体检/发布检查");
                    if (_currentAuditItems.Count == 0) RunProjectAudit();
                    SelectFirstAuditRiskRow();
                    break;

                case "资源诊断":
                    SelectTabPageByText("资源诊断");
                    if (_currentResourceDiagnostics.Count == 0)
                    {
                        RunResourceDiagnostics();
                    }
                    else
                    {
                        ApplyResourceDiagnosticDashboardFilter();
                    }
                    break;

                case "创作者备注":
                    SelectTabPageByText("创作者备注");
                    if (_currentCreatorNotes.Count == 0) LoadCreatorNotes(silent: true);
                    ApplyCreatorNoteDashboardFilter(item);
                    break;

                case "差异与备份":
                    if (_project?.IsTestCopy == true)
                    {
                        SelectTabPageByText("测试副本差异/发布");
                        if (_currentProjectDiffItems.Count == 0) AnalyzeProjectDiff();
                        else ApplyProjectDiffDashboardFilter();
                    }
                    else
                    {
                        SelectTabPageByText("测试副本差异/发布");
                        _projectDiffInfoBox.Text = "当前项目可直接编辑；测试副本差异需要先创建测试副本作为对照。备份历史仍可直接查看和还原。";
                        MessageBox.Show(this, "测试副本差异需要带 _CCZModStudio_TestCopy.txt 标记的目录；当前项目的备份历史可直接查看。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    }
                    break;

                case "关卡地图联动":
                    SelectTabPageByText("关卡地图联动");
                    if (_currentScenarioMapLinks.Count == 0) LoadScenarioMapLinks();
                    ApplyScenarioMapDashboardFilter();
                    break;

                case "SV高风险命令":
                    SelectTabPageByText("剧本制作");
                    SetStatus("已跳转到剧本制作；请选择剧本并核对高风险命令。");
                    break;

                case "最近报告/发布证据":
                    HandleLatestReportDashboardItem(item);
                    break;

                default:
                    if (!string.IsNullOrWhiteSpace(item.RelatedPage))
                    {
                        SelectTabPageByText(item.RelatedPage.Split('/')[0].Trim());
                    }
                    break;
            }

            RememberLocatedObjectContextForNotes();
            RefreshWorkflowGuide(updateStatus: false);
        }
        catch (Exception ex)
        {
            Log("处理工作台项目失败：" + ex);
            MessageBox.Show(this, ex.Message, "处理工作台项目失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void ApplyResourceDiagnosticDashboardFilter()
    {
        if (_currentResourceDiagnostics.Count == 0) return;
        var severity = _currentResourceDiagnostics.Any(x => x.Severity == "Error")
            ? "Error"
            : _currentResourceDiagnostics.Any(x => x.Severity == "Warn")
                ? "Warn"
                : "全部";
        SelectComboValueOrFirst(_resourceDiagnosticSeverityFilterCombo, severity);
        SelectComboValueOrFirst(_resourceDiagnosticCategoryFilterCombo, "全部");
        _resourceDiagnosticSearchBox.Clear();
        ApplyResourceDiagnosticFilter();
        var found = SelectGridRow<ResourceDiagnosticItem>(_resourceDiagnosticGrid, row =>
            severity == "全部" || row.Severity.Equals(severity, StringComparison.OrdinalIgnoreCase));
        if (found) ShowSelectedResourceDiagnostic();
        SetStatus(severity == "全部"
            ? "已跳转到资源诊断页并显示全部诊断项。"
            : $"已跳转到资源诊断页，已筛选并定位第一条 {severity} 项。");
    }

    private void ApplyCreatorNoteDashboardFilter(WorkflowDashboardItem item)
    {
        var keyword = item.Value.Contains("待办 0", StringComparison.Ordinal)
            ? item.Value.Contains("风险 0", StringComparison.Ordinal) ? string.Empty : "风险"
            : "待办";
        _creatorNoteSearchBox.Text = keyword;
        if (string.IsNullOrWhiteSpace(keyword))
        {
            ClearCreatorNoteFilter();
            SetStatus("已跳转到创作者备注页并显示全部备注。");
        }
        else
        {
            ApplyCreatorNoteFilter();
            var found = SelectGridRow<CreatorNote>(_creatorNoteGrid, note =>
                ContainsKeyword(note.Tags, keyword) ||
                ContainsKeyword(note.Title, keyword) ||
                ContainsKeyword(note.Content, keyword) ||
                ContainsKeyword(note.SourceHint, keyword));
            if (found) ShowSelectedCreatorNote();
            SetStatus($"已跳转到创作者备注页并筛选：{keyword}");
        }
    }

    private void ApplyProjectDiffDashboardFilter()
    {
        if (_currentProjectDiffItems.Count == 0) return;
        var status = _currentProjectDiffItems.Any(x => x.Status == "已修改")
            ? "已修改"
            : _currentProjectDiffItems.Any(x => x.Status == "新增")
                ? "新增"
                : _currentProjectDiffItems.Any(x => x.Status == "缺失")
                    ? "缺失"
                    : "全部";
        SelectComboValueOrFirst(_projectDiffStatusFilterCombo, status);
        _projectDiffSearchBox.Clear();
        ApplyProjectDiffFilter();
        var found = SelectGridRow<ProjectDiffItem>(_projectDiffGrid, row =>
            status == "全部" || row.Status.Equals(status, StringComparison.OrdinalIgnoreCase));
        if (found)
        {
            ShowSelectedProjectDiffItem();
            ShowBackupTimelineForSelectedProjectDiff();
            SelectFirstBackupHistoryRow();
            SelectTabPageByText("备份历史/回滚");
        }
        SetStatus(status == "全部"
            ? "已跳转到测试副本差异页并显示全部差异。"
            : $"已筛选并定位第一条“{status}”差异，同时联动备份时间线。");
    }

    private void ApplyScenarioMapDashboardFilter()
    {
        if (_currentScenarioMapLinks.Count == 0) return;
        _scenarioMapLinksIncompleteOnly.Checked = _currentScenarioMapLinks.Any(ScenarioMapLinkIsIncomplete);
        SelectComboValueOrFirst(_scenarioMapLinkStatusFilterCombo, "全部");
        _scenarioMapLinkSearchBox.Clear();
        ApplyScenarioMapLinkFilter();
        var found = SelectGridRow<ScenarioMapLinkInfo>(_scenarioMapLinkGrid, ScenarioMapLinkIsIncomplete)
                    || SelectGridRow<ScenarioMapLinkInfo>(_scenarioMapLinkGrid, row => row.Status == "完整候选")
                    || SelectGridRow<ScenarioMapLinkInfo>(_scenarioMapLinkGrid, _ => true);
        if (found) ShowSelectedScenarioMapLink();
        SetStatus(found
            ? "已跳转到关卡地图联动页，并定位第一条不完整/候选联动。"
            : "已跳转到关卡地图联动页。");
    }

    private void HandleLatestReportDashboardItem(WorkflowDashboardItem item)
    {
        if (_currentProjectEvidenceItems.Count == 0)
        {
            RefreshProjectEvidence(updateStatus: false);
        }

        var evidence = _currentProjectEvidenceItems.FirstOrDefault(x => x.FullPath.Equals(item.Evidence, StringComparison.OrdinalIgnoreCase))
                       ?? _currentProjectEvidenceItems.OrderByDescending(x => x.LastWriteTime).FirstOrDefault();
        if (evidence != null && File.Exists(evidence.FullPath))
        {
            SelectTabPageByText("制作向导");
            var selected = SelectGridRow<ProjectEvidenceItem>(_workflowEvidenceGrid, x => x.FullPath.Equals(evidence.FullPath, StringComparison.OrdinalIgnoreCase));
            if (selected)
            {
                ShowSelectedProjectEvidenceItem();
            }
            OpenSelectedProjectEvidence();
            return;
        }

        SelectTabPageByText("测试副本差异/发布");
        if (_project != null)
        {
            WriteProjectDeliveryReport();
        }
        else
        {
            MessageBox.Show(this, "请先加载项目，再生成综合报告。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
    }

    private void SelectFirstAuditRiskRow()
    {
        if (_currentAuditItems.Count == 0) return;
        var found = SelectGridRow<ProjectAuditItem>(_auditGrid, row => row.Severity == "Error")
                    || SelectGridRow<ProjectAuditItem>(_auditGrid, row => row.Severity == "Warn")
                    || SelectGridRow<ProjectAuditItem>(_auditGrid, _ => true);
        SetStatus(found ? "已跳转到项目体检页，并定位第一条错误/警告项。" : "已跳转到项目体检页。");
    }

    private void SelectFirstBackupHistoryRow()
    {
        var found = SelectGridRow<BackupHistoryItem>(_backupHistoryGrid, row => row.Restorable)
                    || SelectGridRow<BackupHistoryItem>(_backupHistoryGrid, _ => true);
        if (found) ShowSelectedBackupHistoryItem();
    }

    private void ShowSelectedWorkflowStep()
    {
        var step = GetSelectedWorkflowStep();
        if (step == null)
        {
            _workflowGuideInfoBox.Text = _workflowGuideService.BuildSummary(_project, _currentWorkflowSteps, _currentWorkflowDashboardItems);
            return;
        }

        _workflowGuideInfoBox.Text =
            $"第 {step.StepNo} 步：{step.Title}\r\n" +
            $"阶段：{step.Stage}    当前状态：{step.Status}    相关页面：{step.RelatedPage}\r\n" +
            $"建议操作：{step.RecommendedAction}\r\n" +
            $"为什么重要：{step.WhyItMatters}\r\n" +
            $"安全说明：{step.SafetyNote}\r\n\r\n" +
            _workflowGuideService.BuildSummary(_project, _currentWorkflowSteps, _currentWorkflowDashboardItems);
    }

    private void OpenProjectFileStatusPage()
    {
        RefreshProjectFileStatusView(updateStatus: false);
        SelectTabPageByText("项目文件检查");
        SetStatus("已打开项目文件检查。");
    }

    private void RefreshProjectFileStatusView(bool updateStatus)
    {
        if (_project == null)
        {
            _fileGrid.DataSource = null;
            _projectFileSummaryBox.Text = "尚未加载项目。请先打开包含 Ekd5.exe / Data.e5 / Imsg.e5 / Star.e5 的 MOD 项目目录。";
            if (updateStatus) SetStatus("尚未加载项目。");
            return;
        }

        var statuses = _project.GetFileStatuses().ToList();
        _fileGrid.DataSource = new BindingList<ProjectFileStatus>(statuses);
        ConfigureProjectFileStatusGrid();

        var coreMissing = statuses.Where(x => x.Kind == "核心" && !x.Exists).ToList();
        var optionalMissing = statuses.Where(x => x.Kind != "核心" && !x.Exists).ToList();
        var coreSummary = coreMissing.Count == 0
            ? "核心文件齐全"
            : "缺少核心文件：" + string.Join("、", coreMissing.Select(x => x.Name));
        var optionalSummary = optionalMissing.Count == 0
            ? "可选资源齐全"
            : "缺少可选资源：" + string.Join("、", optionalMissing.Select(x => x.Name));
        _projectFileSummaryBox.Text =
            $"项目：{_project.Name}\r\n" +
            $"目录：{_project.GameRoot}\r\n" +
            $"{coreSummary}；{optionalSummary}。";

        if (updateStatus)
        {
            SetStatus($"项目文件检查已刷新：{coreSummary}。");
        }
    }

    private void ConfigureProjectFileStatusGrid()
    {
        if (_fileGrid.Columns.Count == 0) return;

        if (_fileGrid.Columns[nameof(ProjectFileStatus.Name)] is { } nameColumn)
        {
            nameColumn.HeaderText = "文件";
            nameColumn.FillWeight = 18;
        }
        if (_fileGrid.Columns[nameof(ProjectFileStatus.Path)] is { } pathColumn)
        {
            pathColumn.HeaderText = "路径";
            pathColumn.FillWeight = 52;
        }
        if (_fileGrid.Columns[nameof(ProjectFileStatus.Exists)] is { } existsColumn)
        {
            existsColumn.HeaderText = "存在";
            existsColumn.FillWeight = 10;
        }
        if (_fileGrid.Columns[nameof(ProjectFileStatus.SizeBytes)] is { } sizeColumn)
        {
            sizeColumn.HeaderText = "大小";
            sizeColumn.FillWeight = 10;
        }
        if (_fileGrid.Columns[nameof(ProjectFileStatus.Kind)] is { } kindColumn)
        {
            kindColumn.HeaderText = "类型";
            kindColumn.FillWeight = 10;
        }
        HideNonAuthoringColumns(_fileGrid, nameof(ProjectFileStatus.Path));
    }
}
