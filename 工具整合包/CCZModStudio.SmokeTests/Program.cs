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
var rsWriteSmokeOnly = args.Contains("--rs-write-smoke", StringComparer.OrdinalIgnoreCase);
var migrationSmokeOnly = args.Contains("--migration-smoke", StringComparer.OrdinalIgnoreCase);
var legacyE5sSmokeOnly = args.Contains("--legacy-e5s-smoke", StringComparer.OrdinalIgnoreCase);
var legacyScenarioDepthSmokeOnly = args.Contains("--legacy-scenario-depth-smoke", StringComparer.OrdinalIgnoreCase);
var legacyScriptEditSmokeOnly = args.Contains("--legacy-script-edit-smoke", StringComparer.OrdinalIgnoreCase);
var scriptVariableUsageSmokeOnly = args.Contains("--script-variable-usage-smoke", StringComparer.OrdinalIgnoreCase);
var legacyMfcDialogSmokeOnly = args.Contains("--legacy-mfc-dialog-smoke", StringComparer.OrdinalIgnoreCase);
var rSceneDialogPreviewSmokeOnly = args.Contains("--rscene-dialog-preview-smoke", StringComparer.OrdinalIgnoreCase);
var rSceneFramePreviewSmokeOnly = args.Contains("--rscene-frame-preview-smoke", StringComparer.OrdinalIgnoreCase);
var e5ImageReplaceSmokeOnly = args.Contains("--e5-image-replace-smoke", StringComparer.OrdinalIgnoreCase);
var aiImageAssetSmokeOnly = args.Contains("--ai-image-asset-smoke", StringComparer.OrdinalIgnoreCase);
var shopSmokeOnly = args.Contains("--shop-smoke", StringComparer.OrdinalIgnoreCase);
var jobStrategyWriteSmokeOnly = args.Contains("--job-strategy-write-smoke", StringComparer.OrdinalIgnoreCase);
var globalSettingsWriteSmokeOnly = args.Contains("--global-settings-write-smoke", StringComparer.OrdinalIgnoreCase);
var globalSettingsDialogSmokeOnly = args.Contains("--global-settings-dialog-smoke", StringComparer.OrdinalIgnoreCase);
var uiLayoutSettingsSmokeOnly = args.Contains("--ui-layout-settings-smoke", StringComparer.OrdinalIgnoreCase);
var uiLayoutApplySmokeOnly = args.Contains("--ui-layout-apply-smoke", StringComparer.OrdinalIgnoreCase);
var csvEncodingSmokeOnly = args.Contains("--csv-encoding-smoke", StringComparer.OrdinalIgnoreCase);
var battlefieldPreviewSmokeOnly = args.Contains("--battlefield-preview-smoke", StringComparer.OrdinalIgnoreCase);
var effectPackageSmokeOnly = args.Contains("--effect-package-smoke", StringComparer.OrdinalIgnoreCase);
var exclusiveSetScenarioSmokeOnly = args.Contains("--exclusive-set-smoke", StringComparer.OrdinalIgnoreCase);

var detector = new ProjectDetector();
var project = detector.DetectDefaultProject();
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

if (rsWriteSmokeOnly)
{
    RunRsWriteSmoke(project, tables);
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

if (jobStrategyWriteSmokeOnly)
{
    RunJobStrategyWriteSmoke(project, tables);
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

if (csvEncodingSmokeOnly)
{
    RunCsvEncodingSmoke();
    return;
}

if (battlefieldPreviewSmokeOnly)
{
    RunBattlefieldPreviewSmoke(project, tables);
    return;
}

if (effectPackageSmokeOnly)
{
    RunEffectPackageSmoke(project, tables);
    return;
}

if (exclusiveSetScenarioSmokeOnly)
{
    RunExclusiveSetScenarioSmoke(project, tables);
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
