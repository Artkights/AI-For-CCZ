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
            LoadProject(_projectDetector.DetectDefaultProject());
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine("默认项目加载失败：" + ex.Message);
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
        ClearRSceneImageCache();
        ClearRSceneDocumentView();
        InvalidateScriptVariableProjectCache();

        var mode = project.IsTestCopy ? "测试副本" : "当前项目";
        _projectLabel.Text = $"项目：{project.Name}    模式：{mode}    GameRoot：{project.GameRoot}    HexTable：{project.HexTableXmlPath}";

        ResetProjectBoundState();

        if (!File.Exists(project.HexTableXmlPath))
        {
            _tables = Array.Empty<HexTableDefinition>();
            _tableList.DataSource = null;
            System.Diagnostics.Debug.WriteLine(ProjectDetector.BuildMissingHexTableMessage(project));
            SetStatus("HexTable.xml 缺失");
            return;
        }

        _tables = _tableParser.Load(project.HexTableXmlPath);
        LoadSceneDictionary(showMessages: false);
        IndexMaterialLibrary(showMessages: false);
        LoadMapWorkbenchSettings();
        LoadScenarioCommandTemplates(silent: true);
        RefreshTableList();
        System.Diagnostics.Debug.WriteLine($"已加载项目：{project.GameRoot}");
        System.Diagnostics.Debug.WriteLine($"已读取 HexTable 表定义：{_tables.Count} 个。");
        SetStatus($"已加载 {_tables.Count} 个表定义");
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
        _tableChartBox.Image?.Dispose();
        _tableChartBox.Image = null;
        _tableChartInfoBox.Clear();
        ClearCurrentTableReferenceTarget();

        _currentRoleEditorData = null;
        _roleEditorGrid.DataSource = null;
        _saveRoleEditorButton.Enabled = false;
        _saveRoleTextDetailButton.Enabled = false;

        _currentJobEditorData = null;
        _jobEditorGrid.DataSource = null;
        _saveJobEditorButton.Enabled = false;

        _currentItemEditorData = null;
        _itemEditorGrid.DataSource = null;
        _saveItemEditorButton.Enabled = false;

        _currentShopEditorData = null;
        _shopEditorGrid.DataSource = null;
        _saveShopEditorButton.Enabled = false;

        _currentImageAssignments = null;
        _imageAssignmentGrid.DataSource = null;
        _saveImageAssignmentsButton.Enabled = false;
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
        _mapViewerBox.Image = null;
        _mapViewerRenderedImage?.Dispose();
        _mapViewerRenderedImage = null;
        _mapViewerImage?.Dispose();
        _mapViewerImage = null;
        ResetMapWorkbenchHistory();
    }
}
