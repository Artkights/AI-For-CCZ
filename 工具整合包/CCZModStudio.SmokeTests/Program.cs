using CCZModStudio.Core;
using CCZModStudio.Formats;
using CCZModStudio.Models;
using CCZModStudio;
using System.Data;
using System.Drawing;
using System.Globalization;
using System.Text;
using System.Windows.Forms;

using var backupStorageIsolation = new TemporarySmokeDirectory("BackupStorageIsolation");
ProjectBackupPathService.DefaultParentOverrideForTests = Path.Combine(backupStorageIsolation.Path, "Backups");
ProjectBackupPathService.ConfigPathOverrideForTests = Path.Combine(backupStorageIsolation.Path, "config", "backup-settings.json");

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
var legacyTextWrapSmokeOnly = args.Contains("--legacy-text-wrap-smoke", StringComparer.OrdinalIgnoreCase);
var rSceneDialogPreviewSmokeOnly = args.Contains("--rscene-dialog-preview-smoke", StringComparer.OrdinalIgnoreCase);
var rSceneFramePreviewSmokeOnly = args.Contains("--rscene-frame-preview-smoke", StringComparer.OrdinalIgnoreCase);
var e5ImageReplaceSmokeOnly = args.Contains("--e5-image-replace-smoke", StringComparer.OrdinalIgnoreCase);
var e5IndexIntegritySmokeOnly = args.Contains("--e5-index-integrity-smoke", StringComparer.OrdinalIgnoreCase);
var pixelEditorCodecSmokeOnly = args.Contains("--pixel-editor-codec-smoke", StringComparer.OrdinalIgnoreCase);
var pixelEditorIdentitySmokeOnly = args.Contains("--pixel-editor-identity-smoke", StringComparer.OrdinalIgnoreCase);
var rsSingleFrameSmokeOnly = args.Contains("--rs-single-frame-smoke", StringComparer.OrdinalIgnoreCase);
var bmpExportSmokeOnly = args.Contains("--bmp-export-smoke", StringComparer.OrdinalIgnoreCase);
var imageAssignmentFreeIdSmokeOnly = args.Contains("--image-assignment-free-id-smoke", StringComparer.OrdinalIgnoreCase);
var imagePreviewPerformanceSmokeOnly = args.Contains("--image-preview-performance-smoke", StringComparer.OrdinalIgnoreCase);
var imageAssignmentWriteSmokeOnly = args.Contains("--image-assignment-write-smoke", StringComparer.OrdinalIgnoreCase);
var rImageRawReplaceSmokeOnly = args.Contains("--r-image-raw-replace-smoke", StringComparer.OrdinalIgnoreCase);
var sImageRawReplaceSmokeOnly = args.Contains("--s-image-raw-replace-smoke", StringComparer.OrdinalIgnoreCase);
var rsPixelMaterialValidationSmokeOnly = args.Contains("--rs-pixel-material-validation-smoke", StringComparer.OrdinalIgnoreCase);
var batchImageImportSmokeOnly = args.Contains("--batch-image-import-smoke", StringComparer.OrdinalIgnoreCase);
var imageAssignmentImportPreviewDialogSmokeOnly = args.Contains("--image-assignment-import-preview-dialog-smoke", StringComparer.OrdinalIgnoreCase);
var adaptiveImagePreviewSmokeOnly = args.Contains("--adaptive-image-preview-smoke", StringComparer.OrdinalIgnoreCase);
var portraitFrameApplySmokeOnly = args.Contains("--portrait-frame-apply-smoke", StringComparer.OrdinalIgnoreCase);
var portraitFrameDirectorySmokeOnly = args.Contains("--portrait-frame-directory-smoke", StringComparer.OrdinalIgnoreCase);
var rImageTrueColorImport = args.Contains("--r-image-truecolor-import", StringComparer.OrdinalIgnoreCase);
var sImageTrueColorImport = args.Contains("--s-image-truecolor-import", StringComparer.OrdinalIgnoreCase);
var trueColorEntryInspect = args.Contains("--truecolor-entry-inspect", StringComparer.OrdinalIgnoreCase);
var aiImageAssetSmokeOnly = args.Contains("--ai-image-asset-smoke", StringComparer.OrdinalIgnoreCase);
var rsPixelCharacterDesignWorkflowSmokeOnly = args.Contains("--rs-pixel-character-design-workflow-smoke", StringComparer.OrdinalIgnoreCase);
var rsPixelCharacterDesignPostprocessSmokeOnly = args.Contains("--rs-pixel-character-design-postprocess-smoke", StringComparer.OrdinalIgnoreCase);
var rsPixelCharacterDesignFakeRetroSmokeOnly = args.Contains("--rs-pixel-character-design-fake-retro-smoke", StringComparer.OrdinalIgnoreCase);
var rsPixelEditWorkspaceSmokeOnly = args.Contains("--rs-pixel-edit-workspace-smoke", StringComparer.OrdinalIgnoreCase);
var rsPixelSampleLearningSmokeOnly = args.Contains("--rs-pixel-sample-learning-smoke", StringComparer.OrdinalIgnoreCase);
var shopSmokeOnly = args.Contains("--shop-smoke", StringComparer.OrdinalIgnoreCase);
var jobAreaDropdownSmokeOnly = args.Contains("--job-area-dropdown-smoke", StringComparer.OrdinalIgnoreCase);
var jobWriteSmokeOnly = args.Contains("--job-write-smoke", StringComparer.OrdinalIgnoreCase);
var jobStrategyWriteSmokeOnly = args.Contains("--job-strategy-write-smoke", StringComparer.OrdinalIgnoreCase);
var accessoryJobGroupSmokeOnly = args.Contains("--accessory-job-group-smoke", StringComparer.OrdinalIgnoreCase);
var globalSettingsWriteSmokeOnly = args.Contains("--global-settings-write-smoke", StringComparer.OrdinalIgnoreCase);
var gameTitleVersionedSmokeOnly = args.Contains("--game-title-versioned-smoke", StringComparer.OrdinalIgnoreCase);
var globalSettingsDialogSmokeOnly = args.Contains("--global-settings-dialog-smoke", StringComparer.OrdinalIgnoreCase);
var globalNumericEvidenceSmokeOnly = args.Contains("--global-numeric-evidence-smoke", StringComparer.OrdinalIgnoreCase);
var globalNumericDiscoverySmokeOnly = args.Contains("--global-numeric-discovery-smoke", StringComparer.OrdinalIgnoreCase);
var globalNumericQuerySmokeOnly = args.Contains("--global-numeric-query-smoke", StringComparer.OrdinalIgnoreCase);
var globalNumericWriteSmokeOnly = args.Contains("--global-numeric-write-smoke", StringComparer.OrdinalIgnoreCase);
var abilityTierPatchSmokeOnly = args.Contains("--ability-tier-patch-smoke", StringComparer.OrdinalIgnoreCase);
var uiLayoutSettingsSmokeOnly = args.Contains("--ui-layout-settings-smoke", StringComparer.OrdinalIgnoreCase);
var uiLayoutApplySmokeOnly = args.Contains("--ui-layout-apply-smoke", StringComparer.OrdinalIgnoreCase);
var unsavedCloseSmokeOnly = args.Contains("--unsaved-close-smoke", StringComparer.OrdinalIgnoreCase);
var mainTabSwitchLayoutSmokeOnly = args.Contains("--main-tab-switch-layout-smoke", StringComparer.OrdinalIgnoreCase);
var gridViewportSmokeOnly = args.Contains("--grid-viewport-smoke", StringComparer.OrdinalIgnoreCase);
var applicationPerformanceSmokeOnly = args.Contains("--application-performance-smoke", StringComparer.OrdinalIgnoreCase);
var simplifiedUiSmokeOnly = args.Contains("--simplified-ui-smoke", StringComparer.OrdinalIgnoreCase);
var usageGuideDocumentSmokeOnly = args.Contains("--usage-guide-doc-smoke", StringComparer.OrdinalIgnoreCase);
var sImageExportDialogLayoutSmokeOnly = args.Contains("--s-image-export-dialog-layout-smoke", StringComparer.OrdinalIgnoreCase);
var csvEncodingSmokeOnly = args.Contains("--csv-encoding-smoke", StringComparer.OrdinalIgnoreCase);
var gridEditingSmokeOnly = args.Contains("--grid-editing-smoke", StringComparer.OrdinalIgnoreCase);
var csvImportCellChangeSmokeOnly = args.Contains("--csv-cell-change-smoke", StringComparer.OrdinalIgnoreCase);
var itemEditorReadOnlySmokeOnly = args.Contains("--item-readonly-smoke", StringComparer.OrdinalIgnoreCase);
var itemEditorWriteSmokeOnly = args.Contains("--item-editor-write-smoke", StringComparer.OrdinalIgnoreCase);
var itemEquipmentTypeSettingsSmokeOnly = args.Contains("--item-equipment-type-smoke", StringComparer.OrdinalIgnoreCase);
var tableDerivedDisplaySmokeOnly = args.Contains("--table-derived-smoke", StringComparer.OrdinalIgnoreCase);
var mapPreviewSmokeOnly = args.Contains("--map-preview-smoke", StringComparer.OrdinalIgnoreCase);
var mapTerrainConsistencySmokeOnly = args.Contains("--map-terrain-consistency-smoke", StringComparer.OrdinalIgnoreCase);
var hexzmapSyncSmokeOnly = args.Contains("--hexzmap-sync-smoke", StringComparer.OrdinalIgnoreCase);
var hexzmapWriteSmokeOnly = args.Contains("--hexzmap-write-smoke", StringComparer.OrdinalIgnoreCase);
var mapPairImportExportSmokeOnly = args.Contains("--map-pair-import-export-smoke", StringComparer.OrdinalIgnoreCase);
var mapCanvasPreviewSmokeOnly = args.Contains("--map-canvas-preview-smoke", StringComparer.OrdinalIgnoreCase);
var mapWorkbenchUiSmokeOnly = args.Contains("--map-workbench-ui-smoke", StringComparer.OrdinalIgnoreCase);
var mapSlotPublishSmokeOnly = args.Contains("--map-slot-publish-smoke", StringComparer.OrdinalIgnoreCase);
var terrainDrivenMapSmokeOnly = args.Contains("--terrain-driven-map-smoke", StringComparer.OrdinalIgnoreCase);
var terrainStyleAlignedMapSmokeOnly = args.Contains("--terrain-style-aligned-map-smoke", StringComparer.OrdinalIgnoreCase);
var terrainInteriorNaturalizationSmokeOnly = args.Contains("--terrain-interior-naturalization-smoke", StringComparer.OrdinalIgnoreCase);
var terrainEditToolsSmokeOnly = args.Contains("--terrain-edit-tools-smoke", StringComparer.OrdinalIgnoreCase);
var terrainRenderV2SmokeOnly = args.Contains("--terrain-render-v2-smoke", StringComparer.OrdinalIgnoreCase);
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
var assemblyPatchSmokeOnly = args.Contains("--assembly-patch-smoke", StringComparer.OrdinalIgnoreCase);
var effectInjectionDiscoverySmokeOnly = args.Contains("--effect-injection-discovery-smoke", StringComparer.OrdinalIgnoreCase);
var effectSemanticSmokeOnly = args.Contains("--effect-semantic-smoke", StringComparer.OrdinalIgnoreCase);
var effectInventorySmokeOnly = args.Contains("--effect-inventory-smoke", StringComparer.OrdinalIgnoreCase);
var effectAuthoringSmokeOnly = args.Contains("--effect-authoring-smoke", StringComparer.OrdinalIgnoreCase);
var effectAuthoringWriteSmokeOnly = args.Contains("--effect-authoring-write-smoke", StringComparer.OrdinalIgnoreCase);
var effectNativeAdapterWriteSmokeOnly = args.Contains("--effect-native-adapter-write-smoke", StringComparer.OrdinalIgnoreCase);
var effectCompositeWriteSmokeOnly = args.Contains("--effect-composite-write-smoke", StringComparer.OrdinalIgnoreCase);
var effectLocationSmokeOnly = args.Contains("--effect-location-smoke", StringComparer.OrdinalIgnoreCase);
var effectModuleSmokeOnly = args.Contains("--effect-module-smoke", StringComparer.OrdinalIgnoreCase);
var effectAnalysisV2SmokeOnly = args.Contains("--effect-analysis-v2-smoke", StringComparer.OrdinalIgnoreCase);
var effectOpenAuthoringV7SmokeOnly = args.Contains("--effect-open-authoring-v7-smoke", StringComparer.OrdinalIgnoreCase);
var effectWorkbenchUiSmokeOnly = args.Contains("--effect-workbench-ui-smoke", StringComparer.OrdinalIgnoreCase);
var modPackageSmokeOnly = args.Contains("--mod-package-smoke", StringComparer.OrdinalIgnoreCase);
var standaloneScenarioSmokeOnly = args.Contains("--standalone-scenario-smoke", StringComparer.OrdinalIgnoreCase);
var standaloneSemanticSmokeOnly = args.Contains("--standalone-semantic-smoke", StringComparer.OrdinalIgnoreCase);
var standaloneGoldenSamplesSmokeOnly = args.Contains("--standalone-golden-samples-smoke", StringComparer.OrdinalIgnoreCase);
var strictPlayablePreviewSmokeOnly = args.Contains("--strict-playable-preview-smoke", StringComparer.OrdinalIgnoreCase);
var exclusiveSetScenarioSmokeOnly = args.Contains("--exclusive-set-smoke", StringComparer.OrdinalIgnoreCase);
var scenarioTextImportSmokeOnly = args.Contains("--scenario-text-import-smoke", StringComparer.OrdinalIgnoreCase);
var scenarioTextWriterSmokeOnly = args.Contains("--scenario-text-writer-smoke", StringComparer.OrdinalIgnoreCase);
var roleCriticalQuoteUiSmokeOnly = args.Contains("--role-critical-quote-ui-smoke", StringComparer.OrdinalIgnoreCase);
var roleQuoteLayoutSmokeOnly = args.Contains("--role-quote-layout-smoke", StringComparer.OrdinalIgnoreCase);
var roleDefaultEquipmentSmokeOnly = args.Contains("--role-default-equipment-smoke", StringComparer.OrdinalIgnoreCase);
var roleTextUnsavedSyncSmokeOnly = args.Contains("--role-text-unsaved-sync-smoke", StringComparer.OrdinalIgnoreCase);
var revised66SmokeOnly = args.Contains("--66-revised-smoke", StringComparer.OrdinalIgnoreCase);
var revised66RegressionSmokeOnly = args.Contains("--66-regression-smoke", StringComparer.OrdinalIgnoreCase);
var dongwuLegacyLayoutSmokeOnly = args.Contains("--dongwu-legacy-layout-smoke", StringComparer.OrdinalIgnoreCase);
var duplicateKeySmokeOnly = args.Contains("--duplicate-key-smoke", StringComparer.OrdinalIgnoreCase);
var scriptTreeUiSmokeOnly = args.Contains("--script-tree-ui-smoke", StringComparer.OrdinalIgnoreCase);
var battlefieldScriptTreeUiSmokeOnly = args.Contains("--battlefield-script-tree-ui-smoke", StringComparer.OrdinalIgnoreCase);
var battlefieldConsoleCommitSmokeOnly = args.Contains("--battlefield-console-commit-smoke", StringComparer.OrdinalIgnoreCase);
var guiPackageLayoutSmokeOnly = args.Contains("--gui-package-layout-smoke", StringComparer.OrdinalIgnoreCase);
var portableStorageSmokeOnly = args.Contains("--portable-storage-smoke", StringComparer.OrdinalIgnoreCase);
var backupPathTransactionSmokeOnly = args.Contains("--backup-path-transaction-smoke", StringComparer.OrdinalIgnoreCase);
var engineProfileSmokeOnly = args.Contains("--engine-profile-smoke", StringComparer.OrdinalIgnoreCase);
var qinger66ReadSmokeOnly = args.Contains("--qinger-66-read-smoke", StringComparer.OrdinalIgnoreCase);
var qinger66WriteSmokeOnly = args.Contains("--qinger-66-write-smoke", StringComparer.OrdinalIgnoreCase);
var cmfProbeSmokeOnly = args.Contains("--cmf-probe-smoke", StringComparer.OrdinalIgnoreCase);
var cmfCorpusSmokeOnly = args.Contains("--cmf-corpus-smoke", StringComparer.OrdinalIgnoreCase);
var cmfKnowledgeSmokeOnly = args.Contains("--cmf-knowledge-smoke", StringComparer.OrdinalIgnoreCase);
var cmfDesignerExtractionSmokeOnly = args.Contains("--cmf-designer-extraction-smoke", StringComparer.OrdinalIgnoreCase);
var cmfDesignerUiProbeSmokeOnly = args.Contains("--cmf-designer-ui-probe-smoke", StringComparer.OrdinalIgnoreCase);
var cmfDesignerWriteSmokeOnly = args.Contains("--cmf-designer-write-smoke", StringComparer.OrdinalIgnoreCase);
var cmfManualSeedSmokeOnly = args.Contains("--cmf-manual-seed-smoke", StringComparer.OrdinalIgnoreCase);
var cmSettingsSmokeOnly = args.Contains("--cm-settings-smoke", StringComparer.OrdinalIgnoreCase);
var cmSettings66SmokeOnly = args.Contains("--cm-settings-66-smoke", StringComparer.OrdinalIgnoreCase);
var imageAssignerOracleSmokeOnly = args.Contains("--image-assigner-oracle-smoke", StringComparer.OrdinalIgnoreCase);
var imageAssignerOracleExperimentSmokeOnly = args.Contains("--image-assigner-oracle-experiment-smoke", StringComparer.OrdinalIgnoreCase);
var lubuColorfulExportOnly = args.Contains("--lubu-colorful-export", StringComparer.OrdinalIgnoreCase);

if (portableStorageSmokeOnly)
{
    RunPortableStorageSmoke();
    return;
}

if (duplicateKeySmokeOnly)
{
    RunDuplicateKeySmoke();
    return;
}

if (applicationPerformanceSmokeOnly)
{
    var jsonIndex = Array.FindIndex(args, value => value.Equals("--json", StringComparison.OrdinalIgnoreCase));
    RunApplicationPerformanceSmoke(jsonIndex >= 0 && jsonIndex + 1 < args.Length ? args[jsonIndex + 1] : null);
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

if (battlefieldConsoleCommitSmokeOnly)
{
    RunBattlefieldConsoleCommitSmoke();
    return;
}

if (guiPackageLayoutSmokeOnly)
{
    RunGuiPackageLayoutSmoke();
    return;
}

if (simplifiedUiSmokeOnly)
{
    RunSimplifiedUiSmoke();
    return;
}

if (usageGuideDocumentSmokeOnly)
{
    RunUsageGuideDocumentSmoke();
    return;
}

if (engineProfileSmokeOnly)
{
    RunEngineProfileSmoke();
    return;
}

if (qinger66ReadSmokeOnly)
{
    RunQinger66ReadSmoke();
    return;
}

if (qinger66WriteSmokeOnly)
{
    RunQinger66WriteSmoke();
    return;
}

if (cmfProbeSmokeOnly)
{
    RunCmfProbeSmoke();
    return;
}

if (cmfCorpusSmokeOnly)
{
    RunCmfCorpusSmoke();
    return;
}

if (cmfKnowledgeSmokeOnly)
{
    RunCmfKnowledgeSmoke();
    return;
}

if (cmfDesignerExtractionSmokeOnly)
{
    RunCmfDesignerExtractionSmoke();
    return;
}

if (cmfDesignerUiProbeSmokeOnly)
{
    RunCmfDesignerUiProbeSmoke();
    return;
}

if (cmfDesignerWriteSmokeOnly)
{
    RunCmfDesignerWriteSmoke();
    return;
}

if (cmfManualSeedSmokeOnly)
{
    RunCmfManualSeedSmoke();
    return;
}

if (cmSettingsSmokeOnly)
{
    RunCmSettingsSmoke();
    return;
}

if (cmSettings66SmokeOnly)
{
    RunCmSettings66Smoke();
    return;
}

if (globalNumericEvidenceSmokeOnly)
{
    RunGlobalNumericEvidenceSmoke();
    return;
}

if (globalNumericDiscoverySmokeOnly)
{
    RunGlobalNumericDiscoverySmoke();
    return;
}

if (globalNumericQuerySmokeOnly)
{
    RunGlobalNumericQuerySmoke();
    return;
}

if (globalNumericWriteSmokeOnly)
{
    RunGlobalNumericWriteSmoke();
    return;
}

if (abilityTierPatchSmokeOnly)
{
    RunAbilityTierPatchSmoke();
    return;
}

if (rsPixelCharacterDesignWorkflowSmokeOnly)
{
    ProgramRsPixelCharacterDesignWorkflowSmoke.Run(args);
    return;
}

if (rsPixelCharacterDesignPostprocessSmokeOnly)
{
    ProgramRsPixelCharacterDesignPostprocessSmoke.Run(args);
    return;
}

if (rsPixelCharacterDesignFakeRetroSmokeOnly)
{
    ProgramRsPixelCharacterDesignFakeRetroSmoke.Run(args);
    return;
}

if (rsPixelEditWorkspaceSmokeOnly)
{
    ProgramRsPixelEditWorkspaceSmoke.Run(args);
    return;
}

if (rsPixelSampleLearningSmokeOnly)
{
    ProgramRsPixelSampleLearningSmoke.Run(args);
    return;
}

if (lubuColorfulExportOnly)
{
    ProgramLuBuColorfulEnhanceUtility.Run(args);
    return;
}

if (portraitFrameDirectorySmokeOnly)
{
    RunPortraitFrameDirectorySmoke();
    return;
}

if (sImageExportDialogLayoutSmokeOnly)
{
    RunSImageExportDialogLayoutSmoke();
    return;
}

if (imageAssignmentImportPreviewDialogSmokeOnly)
{
    RunImageAssignmentImportPreviewDialogSmoke();
    return;
}

if (adaptiveImagePreviewSmokeOnly)
{
    RunAdaptiveImagePreviewSmoke();
    return;
}

if (legacyTextWrapSmokeOnly)
{
    RunLegacyTextWrapSmoke();
    return;
}

if (rsSingleFrameSmokeOnly)
{
    RunRsSingleFrameSmoke();
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

if (backupPathTransactionSmokeOnly)
{
    RunBackupPathTransactionSmoke(project);
    return;
}

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

if (e5IndexIntegritySmokeOnly)
{
    RunE5IndexIntegritySmoke(project);
    return;
}

if (pixelEditorCodecSmokeOnly)
{
    RunPixelEditorCodecSmoke(project);
    return;
}

if (pixelEditorIdentitySmokeOnly)
{
    RunPixelEditorRawIdentitySmoke(project);
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

if (imagePreviewPerformanceSmokeOnly)
{
    RunImagePreviewPerformanceSmoke(project);
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

if (rsPixelMaterialValidationSmokeOnly)
{
    RunRsPixelMaterialValidationSmoke(project);
    return;
}

if (batchImageImportSmokeOnly)
{
    RunBatchImageImportSmoke(project);
    return;
}

if (portraitFrameApplySmokeOnly)
{
    RunPortraitFrameApplySmoke(project);
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

if (gameTitleVersionedSmokeOnly)
{
    RunGameTitleVersionedSmoke(project.WorkspaceRoot, project.HexTableXmlPath);
    return;
}

if (imageAssignerOracleSmokeOnly)
{
    RunImageAssignerOracleSmoke(project, tables);
    return;
}

if (imageAssignerOracleExperimentSmokeOnly)
{
    RunImageAssignerOracleExperimentSmoke(project, tables);
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

if (unsavedCloseSmokeOnly)
{
    RunUnsavedCloseSmoke();
    return;
}

if (mainTabSwitchLayoutSmokeOnly)
{
    RunMainTabSwitchLayoutSmoke();
    return;
}

if (gridViewportSmokeOnly)
{
    RunGridViewportSmoke();
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

if (itemEditorReadOnlySmokeOnly)
{
    RunItemEditorReadOnlySmoke(project, tables);
    return;
}

if (itemEditorWriteSmokeOnly)
{
    RunItemEditorWriteSmoke(project, tables);
    return;
}

if (itemEquipmentTypeSettingsSmokeOnly)
{
    RunItemEquipmentTypeSettingsSmoke(project, tables);
    return;
}

if (tableDerivedDisplaySmokeOnly)
{
    RunTableDerivedDisplaySmoke(project, tables);
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

if (mapPairImportExportSmokeOnly)
{
    RunMapPairImportExportSmoke(project);
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

if (mapSlotPublishSmokeOnly)
{
    RunMapSlotPublishSmoke(project);
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

if (terrainEditToolsSmokeOnly)
{
    RunTerrainEditToolsSmoke();
    return;
}

if (terrainRenderV2SmokeOnly)
{
    RunTerrainRenderV2Smoke();
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

if (assemblyPatchSmokeOnly)
{
    RunAssemblyPatchSmoke(project);
    return;
}

if (effectInjectionDiscoverySmokeOnly)
{
    RunEffectInjectionDiscoverySmoke(project);
    return;
}

if (effectSemanticSmokeOnly)
{
    RunEffectSemanticSmoke(project);
    return;
}

if (effectInventorySmokeOnly)
{
    RunEffectInventorySmoke(project);
    return;
}

if (effectLocationSmokeOnly)
{
    RunEffectLocationSmoke(project);
    return;
}

if (effectModuleSmokeOnly)
{
    RunEffectModuleSmoke(project);
    return;
}

if (effectAnalysisV2SmokeOnly)
{
    RunEffectAnalysisV2Smoke(project);
    return;
}

if (effectAuthoringSmokeOnly || effectAuthoringWriteSmokeOnly)
{
    RunEffectAuthoringSmoke(project, effectAuthoringWriteSmokeOnly);
    return;
}

if (effectOpenAuthoringV7SmokeOnly)
{
    RunEffectOpenAuthoringV7Smoke(project);
    return;
}

if (effectWorkbenchUiSmokeOnly)
{
    RunEffectWorkbenchUiSmoke(project);
    return;
}

if (effectNativeAdapterWriteSmokeOnly)
{
    RunManagedNativeAdapterRoundTrip(ResolveEffectInjectionDiscoverySmokeSourceProject(project));
    Console.WriteLine("EFFECT_NATIVE_ADAPTER_WRITE_SMOKE_OK");
    return;
}

if (effectCompositeWriteSmokeOnly)
{
    RunCompositeWriteRoundTrip(ResolveEffectInjectionDiscoverySmokeSourceProject(project));
    Console.WriteLine("EFFECT_COMPOSITE_WRITE_SMOKE_OK");
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
    RunRoleCriticalQuoteUiSmoke(project);
    return;
}

if (roleQuoteLayoutSmokeOnly)
{
    RunRoleQuoteLayoutSmoke();
    return;
}

if (roleDefaultEquipmentSmokeOnly)
{
    RunRoleDefaultEquipmentSmoke(project, tables);
    return;
}

if (roleTextUnsavedSyncSmokeOnly)
{
    RunRoleTextUnsavedSyncSmoke(project, tables);
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
