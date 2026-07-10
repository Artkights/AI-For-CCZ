using CCZModStudio.Core;
using CCZModStudio.Models;
using System.Data;
using System.Globalization;

namespace CCZModStudio;

public sealed partial class MainForm
{
    private void LoadDefaultProject()
    {
        try
        {
            var project = _projectDetector.DetectDefaultProject();
            if (!IsProjectGameRootUsable(project))
            {
                ClearProjectLoadState(
                    "项目：未加载",
                    "未自动找到 MOD 项目目录，请点击“打开项目目录”选择包含 Ekd5.exe / Data.e5 / Imsg.e5 / Star.e5 的目录。");
                return;
            }

            LoadProject(project);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine("默认项目加载失败：" + ex.Message);
            ClearProjectLoadState(
                "项目：未加载",
                "默认项目加载失败，请点击“打开项目目录”手动选择 MOD 项目。");
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
            var project = _projectDetector.CreateProjectFromGameRoot(dialog.SelectedPath);
            if (!IsProjectGameRootUsable(project))
            {
                MessageBox.Show(
                    this,
                    BuildInvalidProjectMessage(project),
                    "项目目录不完整",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                return;
            }

            LoadProject(project);
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
        using var perf = TracePerf("LoadProject");
        var currentSessionKey = BuildProjectSessionCacheKey(project);
        var projectChanged = !_loadedProjectSessionKey.Equals(currentSessionKey, StringComparison.OrdinalIgnoreCase);
        _project = project;
        ApplicationErrorService.SetCurrentProjectPath(project.GameRoot);
        _loadedProjectSessionKey = currentSessionKey;
        _legacyMfcDialogDataSources = null;
        _legacyScenarioCommandDisplayFormatter = null;
        if (projectChanged)
        {
            ClearEditorSessionCaches();
        }
        _attackAreaPreviewService.ClearCache();
        _strategyAnimationPreviewService.ClearCache();
        ClearImageAssignmentCaches();
        ClearBattlefieldUnitFrameCache();
        ClearRSceneImageCache();
        ClearRSceneDocumentView();
        InvalidateScriptVariableProjectCache();

        _projectLabel.Text = BuildProjectHeaderText(project);

        ResetProjectBoundState();

        if (!File.Exists(project.HexTableXmlPath))
        {
            _tables = Array.Empty<HexTableDefinition>();
            _tableList.DataSource = null;
            System.Diagnostics.Debug.WriteLine(ProjectDetector.BuildMissingHexTableMessage(project));
            SetStatus("HexTable.xml 缺失");
            return;
        }

        using (TracePerf("LoadProject.HexTable"))
        {
            _tables = new Ccz66HexTableAugmentationService().LoadForProject(project, _tableParser);
        }
        using (TracePerf("LoadProject.SceneDictionary"))
        {
            LoadSceneDictionary(showMessages: false);
        }
        using (TracePerf("LoadProject.MapWorkbenchSettings"))
        {
            LoadMapWorkbenchSettings();
        }
        using (TracePerf("LoadProject.CommandTemplates"))
        {
            LoadScenarioCommandTemplates(silent: true);
        }
        using (TracePerf("LoadProject.RefreshTableList"))
        {
            RefreshTableList();
        }
        System.Diagnostics.Debug.WriteLine($"已加载项目：{project.GameRoot}");
        System.Diagnostics.Debug.WriteLine($"已读取 HexTable 表定义：{_tables.Count} 个。");
        SetStatus($"已加载 {_tables.Count} 个表定义");
    }

    private static string BuildProjectSessionCacheKey(CczProject project)
    {
        return string.Join("|",
            NormalizeProjectCachePath(project.GameRoot),
            NormalizeProjectCachePath(project.HexTableXmlPath),
            NormalizeProjectCachePath(project.SceneDictionaryPath),
            NormalizeProjectCachePath(project.MaterialLibraryRoot));
    }

    private static string NormalizeProjectCachePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return string.Empty;
        try
        {
            return Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
        catch
        {
            return path.Trim();
        }
    }

    private string BuildProjectHeaderText(CczProject? project)
    {
        if (project == null)
        {
            return "项目：未加载    模式：-    版本：-";
        }

        var mode = project.IsTestCopy ? "测试副本" : "当前项目";
        var version = "未知（6.5兜底）";
        try
        {
            var profile = _engineProfileService.Detect(project);
            if (profile.IsKnown &&
                !string.IsNullOrWhiteSpace(profile.VersionHint) &&
                !profile.VersionHint.Equals("unknown", StringComparison.OrdinalIgnoreCase))
            {
                version = profile.VersionHint;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine("读取项目版本失败：" + ex.Message);
        }

        return $"项目：{project.Name}    模式：{mode}    版本：{version}";
    }

    private void RunPackageSelfCheck()
    {
        var result = PackageSelfCheckService.Check();
        if (result.Passed)
        {
            return;
        }

        MessageBox.Show(
            this,
            PackageSelfCheckService.BuildUserMessage(result),
            "GUI 依赖自检失败",
            MessageBoxButtons.OK,
            MessageBoxIcon.Warning);
        SetStatus("GUI 依赖自检失败");
    }

    private void ClearProjectLoadState(string projectText, string statusText)
    {
        _project = null;
        ApplicationErrorService.SetCurrentProjectPath(null);
        _tables = Array.Empty<HexTableDefinition>();
        _projectLabel.Text = BuildProjectHeaderText(null);
        ResetProjectBoundState();
        _tableList.DataSource = null;
        LoadMapWorkbenchSettings();
        SetStatus(statusText);
    }

    private static bool IsProjectGameRootUsable(CczProject project)
    {
        var required = new[] { "Ekd5.exe", "Data.e5", "Imsg.e5", "Star.e5" };
        return Directory.Exists(project.GameRoot) &&
               required.Count(file => File.Exists(Path.Combine(project.GameRoot, file))) >= 3;
    }

    private static string BuildInvalidProjectMessage(CczProject project)
    {
        var required = new[] { "Ekd5.exe", "Data.e5", "Imsg.e5", "Star.e5" };
        var missing = required
            .Where(file => !File.Exists(Path.Combine(project.GameRoot, file)))
            .ToList();

        return "请选择曹操传 MOD 的游戏项目目录，而不是工具目录或空目录。\r\n\r\n" +
               "当前目录：\r\n" + project.GameRoot + "\r\n\r\n" +
               "缺少核心文件：\r\n" +
               (missing.Count == 0 ? "未缺少核心文件，但目录结构仍未通过校验。" : string.Join("\r\n", missing));
    }

    private void ResetProjectBoundState()
    {
        _saveTableButton.Enabled = false;
        _exportCsvButton.Enabled = false;
        _importCsvButton.Enabled = false;
        _dataGrid.ReadOnly = true;
        _dataGrid.DataSource = null;
        _currentTableResult = null;
        _chartColumnCombo.DataSource = null;
        _renderChartButton.Enabled = false;
        SetPictureBoxImage(_tableChartBox, null);
        _tableChartInfoBox.Clear();
        ClearCurrentTableReferenceTarget();

        _currentRoleEditorData = null;
        _roleEditorGrid.DataSource = null;
        _saveRoleEditorButton.Enabled = false;
        _importRoleFaceButton.Enabled = false;
        _batchImportRoleFaceButton.Enabled = false;
        _exportRoleFaceBmpButton.Enabled = false;
        _saveRoleTextDetailButton.Enabled = false;
        ClearRoleEquipmentDetailControls();
        ClearRoleCriticalQuoteAssignmentControls();

        _currentJobEditorData = null;
        _jobEditorGrid.DataSource = null;
        _saveJobEditorButton.Enabled = false;
        _editAccessoryJobGroupsButton.Enabled = false;
        _replaceJobSImageButton.Enabled = false;
        _batchReplaceJobSImageButton.Enabled = false;
        _exportJobSImageBmpButton.Enabled = false;
        _currentAccessoryJobGroupProfile = null;
        _jobSeriesNames = new Dictionary<int, string>();
        ClearJobDescriptionBox("读取兵种后，在此编辑当前兵种介绍。");

        _currentJobStrategyData = null;
        _currentJobAttributeEditorData = null;
        _jobSeriesRead = null;
        _jobRestraintRead = null;
        _jobAttributeRead = null;
        _jobRestraintGrid.DataSource = null;
        _jobAttributeGrid.DataSource = null;
        _saveJobMatrixButton.Enabled = false;
        _saveJobAttributeMatrixButton.Enabled = false;
        _exportJobAttributeCsvButton.Enabled = false;
        _importJobAttributeCsvButton.Enabled = false;
        _pasteJobMatrixSelectionButton.Enabled = false;
        _fillJobMatrixSelectionButton.Enabled = false;
        _batchModifyJobMatrixButton.Enabled = false;
        _jobStrategyDescriptionRead = null;
        _jobStrategyDescriptionEditorBoundRow = null;
        _jobStrategyDescriptionBoxHasValidationError = false;
        _jobStrategyDescriptionBoxValidationError = string.Empty;
        _jobStrategyEditorGrid.DataSource = null;
        _saveJobStrategyEditorButton.Enabled = false;
        _importJobStrategyIconButton.Enabled = false;
        _editJobStrategyIconButton.Enabled = false;
        _exportJobStrategyIconBmpButton.Enabled = false;
        ClearJobStrategyDescriptionBoxes("读取兵种策略后，在此编辑当前策略介绍。");
        ClearJobStrategyPreview("读取兵种策略后显示预览。");

        _currentItemEditorData = null;
        _itemEditorGrid.DataSource = null;
        _saveItemEditorButton.Enabled = false;
        _batchImportItemIconButton.Enabled = false;
        _editItemIconButton.Enabled = false;
        _exportItemIconBmpButton.Enabled = false;
        _currentItemEquipmentTypeDocument = null;
        _currentItemEquipmentTypeData = null;
        _currentItemEquipmentTypeJobData = null;
        _currentItemEquipmentTypeRowIndex = -1;
        _itemEquipmentTypePendingJobStates.Clear();
        _itemEquipmentTypeGrid.DataSource = null;
        _itemEquipmentTypeJobGrid.DataSource = null;
        _saveItemEquipmentTypeSettingsButton.Enabled = false;

        _currentShopEditorData = null;
        _shopEditorGrid.DataSource = null;
        _saveShopEditorButton.Enabled = false;
        _exportShopEditorCsvButton.Enabled = false;
        _importShopEditorCsvButton.Enabled = false;

        _currentImageAssignments = null;
        _imageAssignmentGrid.DataSource = null;
        _saveImageAssignmentsButton.Enabled = false;
        _queryFreeFaceIdsButton.Enabled = false;
        _queryFreeRImageIdsButton.Enabled = false;
        _queryFreeSImageIdsButton.Enabled = false;
        _importImageAssignmentFaceButton.Enabled = false;
        _batchImportImageAssignmentFaceButton.Enabled = false;
        _applyImageAssignmentFaceFrameButton.Enabled = false;
        _batchApplyImageAssignmentFaceFrameButton.Enabled = false;
        _exportRImageBmpButton.Enabled = false;
        _exportSImageBmpButton.Enabled = false;
        _exportImageAssignmentFaceBmpButton.Enabled = false;
        ClearImageAssignmentPreview();
        _currentImageResourceFiles = Array.Empty<ImageResourceFileInfo>();
        _currentImageResourceEntries = Array.Empty<ImageResourceEntryInfo>();
        _imageResourceFileGrid.DataSource = null;
        _imageResourceEntryGrid.DataSource = null;
        SetPictureBoxImage(_imageResourcePreviewBox, null);

        _currentScenarioFiles = Array.Empty<ScenarioFileInfo>();
        _currentScriptStructure = null;
        _currentLegacyScriptDocument = null;
        _currentScriptTextEntries = Array.Empty<ScenarioTextEntry>();
        _scriptScenarioCombo.DataSource = null;
        _scriptTree.Nodes.Clear();
        _scriptCommandGrid.DataSource = null;
        _scriptParameterGrid.DataSource = null;
        _scriptTextGrid.DataSource = null;
        ClearLegacyScriptParameterEditor();

        _currentBattlefieldDocument = null;
        _battlefieldScenarioCombo.DataSource = null;
        _battlefieldScriptTree.Nodes.Clear();
        _battlefieldUnitGrid.DataSource = null;
        _battlefieldCommandGrid.DataSource = null;
        ClearBattlefieldMapPreviewImages();
        ClearBattlefieldInstructionPreviewState();

        _currentRSceneScenario = null;
        _rSceneScenarioCombo.DataSource = null;
        _rSceneScriptTree.Nodes.Clear();
        _rSceneCommandGrid.DataSource = null;

        _currentMapResources = Array.Empty<MapResourceItem>();
        _mapImageList.DataSource = null;
        _currentMapMakerItem = null;
        _currentMaterialRoot = string.Empty;
        _mapWorkbenchMaterialBrowserPopulated = false;
        _mapMakerMaterialTree.Nodes.Clear();
        _mapMakerMaterialListView.Items.Clear();
        _mapMakerMaterialImageList.Images.Clear();
        ClearMapWorkbenchMaterialThumbnailCache();
        ClearMapMakerPreviewImages();
        _mapMakerMapCellOverrideLookup.Clear();
        ResetMapWorkbenchHistory();
    }
}
