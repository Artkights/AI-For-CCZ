using CCZModStudio.Core;
using CCZModStudio.Formats;
using CCZModStudio.Models;
using CCZModStudio;
using System.Data;
using System.Drawing;
using System.Globalization;
using System.Text;
using System.Windows.Forms;

var enableWriteTest = args.Contains("--write", StringComparer.OrdinalIgnoreCase);
var rsSmokeOnly = args.Contains("--rs-smoke", StringComparer.OrdinalIgnoreCase);
var rsMojibakeSmokeOnly = args.Contains("--rs-mojibake-smoke", StringComparer.OrdinalIgnoreCase);
var rsWriteSmokeOnly = args.Contains("--rs-write-smoke", StringComparer.OrdinalIgnoreCase);
var rsTextWriteSmokeOnly = args.Contains("--rs-text-write-smoke", StringComparer.OrdinalIgnoreCase);
var rsDeploymentWriteSmokeOnly = args.Contains("--rs-deployment-write-smoke", StringComparer.OrdinalIgnoreCase);
var migrationSmokeOnly = args.Contains("--migration-smoke", StringComparer.OrdinalIgnoreCase);
var legacyE5sSmokeOnly = args.Contains("--legacy-e5s-smoke", StringComparer.OrdinalIgnoreCase);
var legacyScenarioDepthSmokeOnly = args.Contains("--legacy-scenario-depth-smoke", StringComparer.OrdinalIgnoreCase);
var legacyScriptEditSmokeOnly = args.Contains("--legacy-script-edit-smoke", StringComparer.OrdinalIgnoreCase);
var legacyVariableTestDisplaySmokeOnly = args.Contains("--legacy-variable-test-display-smoke", StringComparer.OrdinalIgnoreCase);
var scriptVariableUsageSmokeOnly = args.Contains("--script-variable-usage-smoke", StringComparer.OrdinalIgnoreCase);
var legacyMfcDialogSmokeOnly = args.Contains("--legacy-mfc-dialog-smoke", StringComparer.OrdinalIgnoreCase);
var rSceneDialogPreviewSmokeOnly = args.Contains("--rscene-dialog-preview-smoke", StringComparer.OrdinalIgnoreCase);
var rSceneFramePreviewSmokeOnly = args.Contains("--rscene-frame-preview-smoke", StringComparer.OrdinalIgnoreCase);
var e5ImageReplaceSmokeOnly = args.Contains("--e5-image-replace-smoke", StringComparer.OrdinalIgnoreCase);
var pixelEditorCodecSmokeOnly = args.Contains("--pixel-editor-codec-smoke", StringComparer.OrdinalIgnoreCase);
var bmpExportSmokeOnly = args.Contains("--bmp-export-smoke", StringComparer.OrdinalIgnoreCase);
var imageAssignmentFreeIdSmokeOnly = args.Contains("--image-assignment-free-id-smoke", StringComparer.OrdinalIgnoreCase);
var imageAssignmentWriteSmokeOnly = args.Contains("--image-assignment-write-smoke", StringComparer.OrdinalIgnoreCase);
var rImageRawReplaceSmokeOnly = args.Contains("--r-image-raw-replace-smoke", StringComparer.OrdinalIgnoreCase);
var sImageRawReplaceSmokeOnly = args.Contains("--s-image-raw-replace-smoke", StringComparer.OrdinalIgnoreCase);
var batchImageImportSmokeOnly = args.Contains("--batch-image-import-smoke", StringComparer.OrdinalIgnoreCase);
var rImageTrueColorImport = args.Contains("--r-image-truecolor-import", StringComparer.OrdinalIgnoreCase);
var sImageTrueColorImport = args.Contains("--s-image-truecolor-import", StringComparer.OrdinalIgnoreCase);
var trueColorEntryInspect = args.Contains("--truecolor-entry-inspect", StringComparer.OrdinalIgnoreCase);
var aiImageAssetSmokeOnly = args.Contains("--ai-image-asset-smoke", StringComparer.OrdinalIgnoreCase);
var shopSmokeOnly = args.Contains("--shop-smoke", StringComparer.OrdinalIgnoreCase);
var jobAreaDropdownSmokeOnly = args.Contains("--job-area-dropdown-smoke", StringComparer.OrdinalIgnoreCase);
var jobWriteSmokeOnly = args.Contains("--job-write-smoke", StringComparer.OrdinalIgnoreCase);
var jobStrategyWriteSmokeOnly = args.Contains("--job-strategy-write-smoke", StringComparer.OrdinalIgnoreCase);
var accessoryJobGroupSmokeOnly = args.Contains("--accessory-job-group-smoke", StringComparer.OrdinalIgnoreCase);
var globalSettingsWriteSmokeOnly = args.Contains("--global-settings-write-smoke", StringComparer.OrdinalIgnoreCase);
var globalSettingsDialogSmokeOnly = args.Contains("--global-settings-dialog-smoke", StringComparer.OrdinalIgnoreCase);
var uiLayoutSettingsSmokeOnly = args.Contains("--ui-layout-settings-smoke", StringComparer.OrdinalIgnoreCase);
var uiLayoutApplySmokeOnly = args.Contains("--ui-layout-apply-smoke", StringComparer.OrdinalIgnoreCase);
var mainTabSwitchLayoutSmokeOnly = args.Contains("--main-tab-switch-layout-smoke", StringComparer.OrdinalIgnoreCase);
var csvEncodingSmokeOnly = args.Contains("--csv-encoding-smoke", StringComparer.OrdinalIgnoreCase);
var gridEditingSmokeOnly = args.Contains("--grid-editing-smoke", StringComparer.OrdinalIgnoreCase);
var csvImportCellChangeSmokeOnly = args.Contains("--csv-cell-change-smoke", StringComparer.OrdinalIgnoreCase);
var mapPreviewSmokeOnly = args.Contains("--map-preview-smoke", StringComparer.OrdinalIgnoreCase);
var mapTerrainConsistencySmokeOnly = args.Contains("--map-terrain-consistency-smoke", StringComparer.OrdinalIgnoreCase);
var hexzmapSyncSmokeOnly = args.Contains("--hexzmap-sync-smoke", StringComparer.OrdinalIgnoreCase);
var hexzmapWriteSmokeOnly = args.Contains("--hexzmap-write-smoke", StringComparer.OrdinalIgnoreCase);
var mapCanvasPreviewSmokeOnly = args.Contains("--map-canvas-preview-smoke", StringComparer.OrdinalIgnoreCase);
var mapWorkbenchUiSmokeOnly = args.Contains("--map-workbench-ui-smoke", StringComparer.OrdinalIgnoreCase);
var terrainDrivenMapSmokeOnly = args.Contains("--terrain-driven-map-smoke", StringComparer.OrdinalIgnoreCase);
var terrainStyleAlignedMapSmokeOnly = args.Contains("--terrain-style-aligned-map-smoke", StringComparer.OrdinalIgnoreCase);
var terrainInteriorNaturalizationSmokeOnly = args.Contains("--terrain-interior-naturalization-smoke", StringComparer.OrdinalIgnoreCase);
var terrainBuildingStyleSmokeOnly = args.Contains("--terrain-building-style-smoke", StringComparer.OrdinalIgnoreCase);
var terrainGlobalTransitionFieldSmokeOnly = args.Contains("--terrain-global-transition-field-smoke", StringComparer.OrdinalIgnoreCase);
var terrainRegionTextureCanvasSmokeOnly = args.Contains("--terrain-region-texture-canvas-smoke", StringComparer.OrdinalIgnoreCase);
var buildingContactBlendSmokeOnly = args.Contains("--building-contact-blend-smoke", StringComparer.OrdinalIgnoreCase);
var buildingGroundInpaintSmokeOnly = args.Contains("--building-ground-inpaint-smoke", StringComparer.OrdinalIgnoreCase);
var objectAlphaRepairSmokeOnly = args.Contains("--object-alpha-repair-smoke", StringComparer.OrdinalIgnoreCase);
var currentMapPureSamplePrioritySmokeOnly = args.Contains("--current-map-pure-sample-priority-smoke", StringComparer.OrdinalIgnoreCase);
var terrainObjectGroundInpaintSmokeOnly = args.Contains("--terrain-object-ground-inpaint-smoke", StringComparer.OrdinalIgnoreCase);
var bridgeGroundInferenceSmokeOnly = args.Contains("--bridge-ground-inference-smoke", StringComparer.OrdinalIgnoreCase);
var objectFootprintColorContinuitySmokeOnly = args.Contains("--object-footprint-color-continuity-smoke", StringComparer.OrdinalIgnoreCase);
var materialDrivenMapSmokeOnly = args.Contains("--material-driven-map-smoke", StringComparer.OrdinalIgnoreCase);
var mapMaterialExtractionSmokeOnly = args.Contains("--map-material-extraction-smoke", StringComparer.OrdinalIgnoreCase);
var autoTileRegionSmokeOnly = args.Contains("--autotile-region-smoke", StringComparer.OrdinalIgnoreCase);
var battlefieldPreviewSmokeOnly = args.Contains("--battlefield-preview-smoke", StringComparer.OrdinalIgnoreCase);
var battlefieldUnitStatusWriteSmokeOnly = args.Contains("--battlefield-unit-status-write-smoke", StringComparer.OrdinalIgnoreCase);
var effectPackageSmokeOnly = args.Contains("--effect-package-smoke", StringComparer.OrdinalIgnoreCase);
var modPackageSmokeOnly = args.Contains("--mod-package-smoke", StringComparer.OrdinalIgnoreCase);
var standaloneScenarioSmokeOnly = args.Contains("--standalone-scenario-smoke", StringComparer.OrdinalIgnoreCase);
var standaloneSemanticSmokeOnly = args.Contains("--standalone-semantic-smoke", StringComparer.OrdinalIgnoreCase);
var standaloneGoldenSamplesSmokeOnly = args.Contains("--standalone-golden-samples-smoke", StringComparer.OrdinalIgnoreCase);
var strictPlayablePreviewSmokeOnly = args.Contains("--strict-playable-preview-smoke", StringComparer.OrdinalIgnoreCase);
var exclusiveSetScenarioSmokeOnly = args.Contains("--exclusive-set-smoke", StringComparer.OrdinalIgnoreCase);
var scenarioTextImportSmokeOnly = args.Contains("--scenario-text-import-smoke", StringComparer.OrdinalIgnoreCase);
var scenarioTextWriterSmokeOnly = args.Contains("--scenario-text-writer-smoke", StringComparer.OrdinalIgnoreCase);
var roleCriticalQuoteUiSmokeOnly = args.Contains("--role-critical-quote-ui-smoke", StringComparer.OrdinalIgnoreCase);
var roleDefaultEquipmentSmokeOnly = args.Contains("--role-default-equipment-smoke", StringComparer.OrdinalIgnoreCase);
var revised66SmokeOnly = args.Contains("--66-revised-smoke", StringComparer.OrdinalIgnoreCase);
var revised66RegressionSmokeOnly = args.Contains("--66-regression-smoke", StringComparer.OrdinalIgnoreCase);
var dongwuLegacyLayoutSmokeOnly = args.Contains("--dongwu-legacy-layout-smoke", StringComparer.OrdinalIgnoreCase);
var duplicateKeySmokeOnly = args.Contains("--duplicate-key-smoke", StringComparer.OrdinalIgnoreCase);
var scriptTreeUiSmokeOnly = args.Contains("--script-tree-ui-smoke", StringComparer.OrdinalIgnoreCase);
var battlefieldScriptTreeUiSmokeOnly = args.Contains("--battlefield-script-tree-ui-smoke", StringComparer.OrdinalIgnoreCase);
var guiPackageLayoutSmokeOnly = args.Contains("--gui-package-layout-smoke", StringComparer.OrdinalIgnoreCase);

if (duplicateKeySmokeOnly)
{
    RunDuplicateKeySmoke();
    return;
}

if (scriptTreeUiSmokeOnly)
{
    RunScriptTreeUiSmoke();
    return;
}

if (battlefieldScriptTreeUiSmokeOnly)
{
    RunBattlefieldScriptTreeUiSmoke();
    return;
}

if (guiPackageLayoutSmokeOnly)
{
    RunGuiPackageLayoutSmoke();
    return;
}

var detector = new ProjectDetector();
var envGameRoot = Environment.GetEnvironmentVariable("CCZMODSTUDIO_GAME_ROOT");
var project = string.IsNullOrWhiteSpace(envGameRoot)
    ? detector.DetectDefaultProject()
    : detector.CreateProjectFromGameRoot(envGameRoot);
Console.WriteLine($"Workspace={project.WorkspaceRoot}");
Console.WriteLine($"GameRoot={project.GameRoot}");
Console.WriteLine($"HexTable={project.HexTableXmlPath}");

var statuses = project.GetFileStatuses();
foreach (var status in statuses)
{
    Console.WriteLine($"FILE {status.Name} exists={status.Exists} size={status.SizeBytes?.ToString() ?? "-"}");
}

var parser = new HexTableParser();
var tables = parser.Load(project.HexTableXmlPath);
Console.WriteLine($"TABLE_COUNT={tables.Count}");

if (rsSmokeOnly)
{
    RunRsSmoke(project, tables);
    return;
}

if (rsMojibakeSmokeOnly)
{
    RunRsMojibakeSmoke(project, tables);
    return;
}

if (rsWriteSmokeOnly)
{
    RunRsWriteSmoke(project, tables);
    return;
}

if (rsTextWriteSmokeOnly)
{
    RunBattlefieldTextWriteSmoke(project, tables);
    return;
}

if (rsDeploymentWriteSmokeOnly)
{
    RunBattlefieldDeploymentWriteSmoke(project, tables);
    return;
}

if (migrationSmokeOnly)
{
    RunMigrationSmoke(project);
    return;
}

if (legacyE5sSmokeOnly)
{
    RunLegacyE5sSmoke(project);
    return;
}

if (legacyScenarioDepthSmokeOnly)
{
    RunLegacyScenarioDepthSmoke(project);
    return;
}

if (legacyScriptEditSmokeOnly)
{
    RunLegacyScriptEditSmoke(project);
    return;
}

if (legacyVariableTestDisplaySmokeOnly)
{
    RunLegacyVariableTestDisplaySmoke(project, tables);
    return;
}

if (scriptVariableUsageSmokeOnly)
{
    RunScriptVariableUsageSmoke();
    return;
}

if (legacyMfcDialogSmokeOnly)
{
    RunLegacyMfcDialogSmoke(project, tables);
    return;
}

if (rSceneDialogPreviewSmokeOnly)
{
    RunRSceneDialogPreviewSmoke(project, tables);
    return;
}

if (rSceneFramePreviewSmokeOnly)
{
    RunRSceneFramePreviewSmoke(project, tables);
    return;
}

if (e5ImageReplaceSmokeOnly)
{
    RunE5ImageReplaceSmoke(project);
    return;
}

if (pixelEditorCodecSmokeOnly)
{
    RunPixelEditorCodecSmoke(project);
    return;
}

if (bmpExportSmokeOnly)
{
    RunBmpExportSmoke(project);
    return;
}

if (imageAssignmentFreeIdSmokeOnly)
{
    RunImageAssignmentFreeIdSmoke(project, tables);
    return;
}

if (imageAssignmentWriteSmokeOnly)
{
    RunImageAssignmentWriteSmoke(project, tables);
    return;
}

if (rImageRawReplaceSmokeOnly)
{
    RunRImageRawReplaceSmoke(project);
    return;
}

if (sImageRawReplaceSmokeOnly)
{
    RunSImageRawReplaceSmoke(project);
    return;
}

if (batchImageImportSmokeOnly)
{
    RunBatchImageImportSmoke(project);
    return;
}

if (rImageTrueColorImport)
{
    RunRImageTrueColorImport(project, args);
    return;
}

if (sImageTrueColorImport)
{
    RunSImageTrueColorImport(project, args);
    return;
}

if (trueColorEntryInspect)
{
    RunTrueColorEntryInspect(project, args);
    return;
}

if (aiImageAssetSmokeOnly)
{
    RunAiImageAssetSmoke(project);
    return;
}

if (shopSmokeOnly)
{
    RunShopEditorSmoke(project, tables);
    return;
}

if (jobAreaDropdownSmokeOnly)
{
    RunJobAreaDropdownSmoke(project, tables);
    return;
}

if (jobWriteSmokeOnly)
{
    RunJobWriteOnlySmoke(project, tables);
    return;
}

if (jobStrategyWriteSmokeOnly)
{
    RunJobStrategyWriteSmoke(project, tables);
    return;
}

if (accessoryJobGroupSmokeOnly)
{
    RunAccessoryJobGroupSmoke(project, tables);
    return;
}

if (globalSettingsWriteSmokeOnly)
{
    RunGlobalSettingsWriteSmoke(project, tables);
    return;
}

if (globalSettingsDialogSmokeOnly)
{
    RunGlobalSettingsDialogSmoke(project, tables);
    return;
}

if (uiLayoutSettingsSmokeOnly)
{
    RunUiLayoutSettingsSmoke();
    return;
}

if (uiLayoutApplySmokeOnly)
{
    RunUiLayoutApplySmoke();
    return;
}

if (mainTabSwitchLayoutSmokeOnly)
{
    RunMainTabSwitchLayoutSmoke();
    return;
}

if (csvEncodingSmokeOnly)
{
    RunCsvEncodingSmoke();
    return;
}

if (gridEditingSmokeOnly)
{
    RunGridEditingSmoke();
    return;
}

if (csvImportCellChangeSmokeOnly)
{
    RunCsvImportCellChangeSmoke();
    return;
}

if (mapPreviewSmokeOnly)
{
    RunMapImagePreviewSmoke(project);
    return;
}

if (mapTerrainConsistencySmokeOnly)
{
    RunMapTerrainConsistencySmoke(project);
    return;
}

if (hexzmapSyncSmokeOnly)
{
    RunHexzmapSyncSmoke(project);
    return;
}

if (hexzmapWriteSmokeOnly)
{
    RunHexzmapWriteSmoke(project);
    return;
}

if (mapCanvasPreviewSmokeOnly)
{
    RunMapCanvasPreviewSmoke();
    return;
}

if (mapWorkbenchUiSmokeOnly)
{
    RunMapWorkbenchUiSmoke(project);
    return;
}

if (terrainDrivenMapSmokeOnly)
{
    RunTerrainDrivenMapSmoke();
    return;
}

if (terrainStyleAlignedMapSmokeOnly)
{
    RunTerrainStyleAlignedMapSmoke();
    return;
}

if (terrainInteriorNaturalizationSmokeOnly)
{
    RunTerrainInteriorNaturalizationSmoke();
    return;
}

if (terrainBuildingStyleSmokeOnly)
{
    RunTerrainBuildingStyleSmoke();
    return;
}

if (terrainGlobalTransitionFieldSmokeOnly)
{
    RunTerrainGlobalTransitionFieldSmoke();
    return;
}

if (terrainRegionTextureCanvasSmokeOnly)
{
    RunTerrainRegionTextureCanvasSmoke();
    return;
}

if (buildingContactBlendSmokeOnly)
{
    RunBuildingContactBlendSmoke();
    return;
}

if (buildingGroundInpaintSmokeOnly)
{
    RunBuildingGroundInpaintSmoke();
    return;
}

if (objectAlphaRepairSmokeOnly)
{
    RunObjectAlphaRepairSmoke();
    return;
}

if (currentMapPureSamplePrioritySmokeOnly)
{
    RunCurrentMapPureSamplePrioritySmoke();
    return;
}

if (terrainObjectGroundInpaintSmokeOnly)
{
    RunTerrainObjectGroundInpaintSmoke();
    return;
}

if (bridgeGroundInferenceSmokeOnly)
{
    RunBridgeGroundInferenceSmoke();
    return;
}

if (objectFootprintColorContinuitySmokeOnly)
{
    RunObjectFootprintColorContinuitySmoke();
    return;
}

if (materialDrivenMapSmokeOnly)
{
    RunMaterialDrivenMapSmoke();
    return;
}

if (mapMaterialExtractionSmokeOnly)
{
    RunMapMaterialExtractionSmoke();
    return;
}

if (autoTileRegionSmokeOnly)
{
    RunAutoTileRegionSmoke();
    return;
}

if (battlefieldPreviewSmokeOnly)
{
    RunBattlefieldPreviewSmoke(project, tables);
    return;
}

if (dongwuLegacyLayoutSmokeOnly)
{
    RunDongwuLegacyLayoutSmoke(project, tables);
    return;
}

if (battlefieldUnitStatusWriteSmokeOnly)
{
    RunBattlefieldUnitStatusWriteSmoke(project, tables);
    return;
}

if (effectPackageSmokeOnly)
{
    RunEffectPackageSmoke(project, tables);
    return;
}

if (modPackageSmokeOnly)
{
    RunModPackageSmoke(project, tables);
    return;
}

if (standaloneScenarioSmokeOnly)
{
    RunStandaloneScenarioSmoke(project, tables);
    return;
}

if (standaloneSemanticSmokeOnly)
{
    RunStandaloneSemanticSmoke(project, tables);
    return;
}

if (standaloneGoldenSamplesSmokeOnly)
{
    RunStandaloneGoldenSamplesSmoke(project, tables);
    return;
}

if (strictPlayablePreviewSmokeOnly)
{
    RunStrictPlayablePreviewSmoke(project, tables);
    return;
}

if (exclusiveSetScenarioSmokeOnly)
{
    RunExclusiveSetScenarioSmoke(project, tables);
    return;
}

if (scenarioTextImportSmokeOnly)
{
    RunScenarioTextImportSmoke(project, tables);
    return;
}

if (scenarioTextWriterSmokeOnly)
{
    RunScenarioTextWriterSmoke(project);
    return;
}

if (roleCriticalQuoteUiSmokeOnly)
{
    RunRoleCriticalQuoteUiSmoke();
    return;
}

if (roleDefaultEquipmentSmokeOnly)
{
    RunRoleDefaultEquipmentSmoke(project, tables);
    return;
}

if (revised66SmokeOnly)
{
    Run66RevisedSmoke(project);
    return;
}

if (revised66RegressionSmokeOnly)
{
    Run66RegressionSmoke(project);
    return;
}

if (enableWriteTest)
{
    Console.WriteLine("WRITE_MODE=RS_CORE (--write 当前运行 R/S eex 核心写入烟测；E5S 兼容检查请显式使用 --legacy-e5s-smoke)");
    RunRsWriteSmoke(project, tables);
    return;
}

Console.WriteLine("DEFAULT_MODE=RS_CORE (E5S 兼容检查为 --legacy-e5s-smoke)");
RunRsSmoke(project, tables);
