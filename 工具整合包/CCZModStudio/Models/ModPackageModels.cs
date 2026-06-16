using System.Text.Json;
using System.Text.Json.Serialization;

namespace CCZModStudio.Models;

public sealed class ModPackage
{
    public string SchemaVersion { get; set; } = "1.0";
    public ModPackageMetadata Metadata { get; set; } = new();
    public ModSlotPlan SlotPlan { get; set; } = new();
    public List<ModTableUpdate> TableUpdates { get; set; } = [];
    public List<ModScenarioPatch> ScenarioPatches { get; set; } = [];
    public List<EffectPackage> EffectPackages { get; set; } = [];
    public List<ModResourceUpdate> ResourceUpdates { get; set; } = [];
    public ModValidationPlan ValidationPlan { get; set; } = new();
}

public sealed class ModDesign
{
    public string DesignId { get; set; } = string.Empty;
    public string SourcePrompt { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Theme { get; set; } = string.Empty;
    public string TargetVersion { get; set; } = "6.5";
    public string StorySynopsis { get; set; } = string.Empty;
    public List<ModDesignRole> Roles { get; set; } = [];
    public List<string> Factions { get; set; } = [];
    public ModDesignBattlePlan BattlePlan { get; set; } = new();
    public string ResourceStyle { get; set; } = string.Empty;
    public List<string> GameplayGoals { get; set; } = [];
    public List<string> RequestedResources { get; set; } = [];
    public Dictionary<string, string> Assumptions { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class ModDesignRole
{
    public string Name { get; set; } = string.Empty;
    public string Faction { get; set; } = string.Empty;
    public string Job { get; set; } = string.Empty;
    public string Personality { get; set; } = string.Empty;
    public string GameplayRole { get; set; } = string.Empty;
}

public sealed class ModDesignBattlePlan
{
    public string MapTheme { get; set; } = string.Empty;
    public string Objective { get; set; } = string.Empty;
    public string Tempo { get; set; } = string.Empty;
    public int AllyCount { get; set; } = 4;
    public int EnemyCount { get; set; } = 8;
    public List<string> WinConditions { get; set; } = [];
    public List<string> LoseConditions { get; set; } = [];
}

public sealed class StandaloneScenarioAnalysisResult
{
    public string ProjectRoot { get; set; } = string.Empty;
    public string ConstraintMode { get; set; } = "force_open";
    public StandaloneScenarioDesign Design { get; set; } = new();
    public List<string> Decisions { get; set; } = [];
}

public sealed class StandaloneScenarioCompileResult
{
    public string ProjectRoot { get; set; } = string.Empty;
    public string ConstraintMode { get; set; } = "force_open";
    public StandaloneScenarioDesign Design { get; set; } = new();
    public ModAvailableSlotsResult Slots { get; set; } = new();
    public ModPackage Package { get; set; } = new();
    public StandaloneScenarioPlayabilityChecklist Playability { get; set; } = new();
    public List<string> Decisions { get; set; } = [];
    public List<string> Warnings { get; set; } = [];
}

public sealed class StandaloneScenarioDesign
{
    public string DesignId { get; set; } = string.Empty;
    public string SourcePrompt { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Theme { get; set; } = string.Empty;
    public string TargetVersion { get; set; } = "6.5";
    public string Synopsis { get; set; } = string.Empty;
    public string RScenarioId { get; set; } = string.Empty;
    public string SScenarioId { get; set; } = string.Empty;
    public List<StandaloneScenarioRole> Roles { get; set; } = [];
    public BattleDeploymentPlan Battle { get; set; } = new();
    public ScenarioEventGraph EventGraph { get; set; } = new();
    public ScenarioResourcePlan Resources { get; set; } = new();
    public List<string> Rewards { get; set; } = [];
    public Dictionary<string, string> Assumptions { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class StandaloneScenarioRole
{
    public string Key { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Faction { get; set; } = string.Empty;
    public string Job { get; set; } = string.Empty;
    public string Personality { get; set; } = string.Empty;
    public string GameplayRole { get; set; } = string.Empty;
    public int Level { get; set; } = 1;
    public int PersonId { get; set; }
    public int JobId { get; set; }
    public int FaceImageNumber { get; set; }
    public int RImageId { get; set; }
    public int SImageId { get; set; }
}

public sealed class BattleDeploymentPlan
{
    public string MapId { get; set; } = string.Empty;
    public string MapTheme { get; set; } = string.Empty;
    public string Objective { get; set; } = string.Empty;
    public string Tempo { get; set; } = string.Empty;
    public int TurnLimit { get; set; } = 20;
    public List<string> WinConditions { get; set; } = [];
    public List<string> LoseConditions { get; set; } = [];
    public List<BattleDeploymentUnit> Units { get; set; } = [];
}

public sealed class BattleDeploymentUnit
{
    public string RoleKey { get; set; } = string.Empty;
    public string Side { get; set; } = "ally";
    public int X { get; set; }
    public int Y { get; set; }
    public int Direction { get; set; }
    public int AiPolicy { get; set; }
    public int LevelBonus { get; set; }
}

public sealed class ScenarioEventGraph
{
    public List<ScenarioEventNode> Nodes { get; set; } = [];
}

public sealed class ScenarioEventNode
{
    public string Id { get; set; } = string.Empty;
    public string Kind { get; set; } = string.Empty;
    public string Trigger { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public List<ScenarioEventAction> Actions { get; set; } = [];
    public List<string> Next { get; set; } = [];
}

public sealed class ScenarioEventAction
{
    public string Kind { get; set; } = string.Empty;
    public string ActorKey { get; set; } = string.Empty;
    public string TargetKey { get; set; } = string.Empty;
    public string Text { get; set; } = string.Empty;
    public int X { get; set; }
    public int Y { get; set; }
    public int Direction { get; set; }
    public int ActionId { get; set; }
    public int Value { get; set; }
}

public sealed class ScenarioResourcePlan
{
    public string Style { get; set; } = string.Empty;
    public List<ScenarioResourceNeed> Needs { get; set; } = [];
}

public sealed class ScenarioResourceNeed
{
    public string Kind { get; set; } = string.Empty;
    public string Target { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public bool PlaceholderAllowed { get; set; } = true;
}

public sealed class StandaloneScenarioPlayabilityChecklist
{
    public bool HasRIntro { get; set; }
    public bool HasRToSJump { get; set; }
    public bool HasBattleDeployment { get; set; }
    public bool HasVictoryCondition { get; set; }
    public bool HasDefeatCondition { get; set; }
    public bool HasBattleEvents { get; set; }
    public bool HasEpilogue { get; set; }
    public List<string> Risks { get; set; } = [];
}

public sealed class ModDesignAnalysisResult
{
    public string ProjectRoot { get; set; } = string.Empty;
    public string AutomationMode { get; set; } = "aggressive_test_copy";
    public ModDesign Design { get; set; } = new();
    public List<string> Decisions { get; set; } = [];
    public List<string> MissingInputsAutoFilled { get; set; } = [];
}

public sealed class ModPackageCompileResult
{
    public string ProjectRoot { get; set; } = string.Empty;
    public string AutomationMode { get; set; } = "aggressive_test_copy";
    public ModDesign Design { get; set; } = new();
    public ModAvailableSlotsResult Slots { get; set; } = new();
    public ModPackage Package { get; set; } = new();
    public List<string> Decisions { get; set; } = [];
    public List<string> Warnings { get; set; } = [];
}

public sealed class ModPackageRepairResult
{
    public string ProjectRoot { get; set; } = string.Empty;
    public string PackageId { get; set; } = string.Empty;
    public bool Changed { get; set; }
    public ModPackage Package { get; set; } = new();
    public List<string> Repairs { get; set; } = [];
    public List<ModPackageValidationIssue> RemainingIssues { get; set; } = [];
}

public sealed class ModAutomationAttempt
{
    public int Attempt { get; set; }
    public string Stage { get; set; } = string.Empty;
    public bool Passed { get; set; }
    public string Summary { get; set; } = string.Empty;
    public List<ModPackageValidationIssue> Issues { get; set; } = [];
}

public sealed class ModAutoMakeResult
{
    public string ProjectRoot { get; set; } = string.Empty;
    public string TestCopyRoot { get; set; } = string.Empty;
    public string AutomationMode { get; set; } = "aggressive_test_copy";
    public ModDesign Design { get; set; } = new();
    public ModPackage Package { get; set; } = new();
    public ModPackagePreviewResult? Preview { get; set; }
    public ModPackageApplyResult? Apply { get; set; }
    public ModPackageValidationResult? Validation { get; set; }
    public List<ModAutomationAttempt> Attempts { get; set; } = [];
    public List<string> ReportPaths { get; set; } = [];
    public bool Completed { get; set; }
    public string Summary { get; set; } = string.Empty;
}

public sealed class ModAutoValidateResult
{
    public string ProjectRoot { get; set; } = string.Empty;
    public string PackageId { get; set; } = string.Empty;
    public bool Passed { get; set; }
    public ModPackagePreviewResult? Preview { get; set; }
    public List<ModSmokeRunResult> SmokeRuns { get; set; } = [];
    public List<ModPackageValidationIssue> Issues { get; set; } = [];
    public string Summary { get; set; } = string.Empty;
    public string ReportPath { get; set; } = string.Empty;
}

public sealed class ModSmokeRunResult
{
    public string Command { get; set; } = string.Empty;
    public bool Ran { get; set; }
    public bool Passed { get; set; }
    public int? ExitCode { get; set; }
    public string OutputTail { get; set; } = string.Empty;
    public string Error { get; set; } = string.Empty;
}

public sealed class ScenarioPatchCompileRequest
{
    public string RelativePath { get; set; } = string.Empty;
    public string Kind { get; set; } = "auto";
    public string StoryText { get; set; } = string.Empty;
    public string BattleObjective { get; set; } = string.Empty;
    public int SceneIndex { get; set; } = 1;
    public int SectionIndex { get; set; } = 1;
    public string InsertMode { get; set; } = "append";
}

public sealed class ScenarioPatchCompileResult
{
    public string ProjectRoot { get; set; } = string.Empty;
    public ModScenarioPatch Patch { get; set; } = new();
    public ScenarioPatchPreviewResult? Preview { get; set; }
    public List<string> Decisions { get; set; } = [];
}

public sealed class ModPackageMetadata
{
    public string PackageId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Theme { get; set; } = string.Empty;
    public string TargetVersion { get; set; } = "6.5";
    public string AuthorNote { get; set; } = string.Empty;
    public string SourcePrompt { get; set; } = string.Empty;
    public Dictionary<string, string> Tags { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class ModSlotPlan
{
    public List<int> PersonIds { get; set; } = [];
    public List<int> JobIds { get; set; } = [];
    public List<int> ItemIds { get; set; } = [];
    public List<int> StrategyIds { get; set; } = [];
    public List<int> EffectIds { get; set; } = [];
    public List<int> FaceImageNumbers { get; set; } = [];
    public List<int> RImageIds { get; set; } = [];
    public List<int> SImageIds { get; set; } = [];
    public List<string> MapIds { get; set; } = [];
    public List<string> ScenarioIds { get; set; } = [];
    public Dictionary<string, string> Notes { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class ModTableUpdate
{
    public string TableName { get; set; } = string.Empty;
    public int RowId { get; set; }
    public Dictionary<string, JsonElement> Values { get; set; } = new(StringComparer.Ordinal);
    public string Note { get; set; } = string.Empty;
}

public sealed class ModScenarioPatch
{
    public string PatchId { get; set; } = string.Empty;
    public string RelativePath { get; set; } = string.Empty;
    public List<ModScenarioPatchOperation> Operations { get; set; } = [];
    public string Note { get; set; } = string.Empty;
}

public sealed class ModScenarioPatchOperation
{
    public string Operation { get; set; } = "replace_text_parameter";
    public int? SceneIndex { get; set; }
    public int? SectionIndex { get; set; }
    public int? CommandIndex { get; set; }
    public int? CommandOrdinal { get; set; }
    public int? CommandId { get; set; }
    public string? CommandIdHex { get; set; }
    public int? ParameterIndex { get; set; }
    public int? ExpectedLayoutCode { get; set; }
    public string? ExpectedText { get; set; }
    public string? Text { get; set; }
    public int? Value { get; set; }
    public List<int>? Values { get; set; }
    public int? InsertAfterCommandIndex { get; set; }
    public int? InsertBeforeCommandIndex { get; set; }
    public List<ModScenarioCommandDraft> Commands { get; set; } = [];
    public string Note { get; set; } = string.Empty;
}

public sealed class ModScenarioCommandDraft
{
    public int CommandId { get; set; }
    public string? CommandIdHex { get; set; }
    public List<ModScenarioParameterDraft> Parameters { get; set; } = [];
    public string Note { get; set; } = string.Empty;
}

public sealed class ModScenarioParameterDraft
{
    public int? LayoutCode { get; set; }
    public string Kind { get; set; } = "word16";
    public int? IntValue { get; set; }
    public string? Text { get; set; }
    public List<int>? Values { get; set; }
}

public sealed class ModResourceUpdate
{
    public string Kind { get; set; } = "resource";
    public string TargetRelativePath { get; set; } = string.Empty;
    public int? ImageNumber { get; set; }
    public int? IconIndex { get; set; }
    public string ReplacementPath { get; set; } = string.Empty;
    public string Operation { get; set; } = "replace";
    public string Note { get; set; } = string.Empty;
}

public sealed class ModValidationPlan
{
    public bool RequirePreview { get; set; } = true;
    public bool RequireTestCopy { get; set; } = true;
    public bool RequireReread { get; set; } = true;
    public bool RequireRuntimeSmoke { get; set; }
    public List<string> SmokeCommands { get; set; } = [];
    public List<string> ManualChecks { get; set; } = [];
}

public sealed class ModAvailableSlotsResult
{
    public string ProjectRoot { get; set; } = string.Empty;
    public string TargetVersion { get; set; } = string.Empty;
    public List<ModAvailableSlotGroup> Groups { get; set; } = [];
    public List<string> Warnings { get; set; } = [];
    public string SafetyNote { get; set; } = string.Empty;
}

public sealed class ModAvailableSlotGroup
{
    public string Kind { get; set; } = string.Empty;
    public string Source { get; set; } = string.Empty;
    public int TotalCount { get; set; }
    public List<int> AvailableIds { get; set; } = [];
    public List<int> OccupiedIds { get; set; } = [];
    public List<string> AvailableKeys { get; set; } = [];
    public List<string> OccupiedKeys { get; set; } = [];
    public string Note { get; set; } = string.Empty;
}

public sealed class ModPackagePreviewResult
{
    public string ProjectRoot { get; set; } = string.Empty;
    public string PackageId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public bool CanApply { get; set; }
    public string Summary { get; set; } = string.Empty;
    public List<ModPackageValidationIssue> Issues { get; set; } = [];
    public List<ModPackageChangePreview> Changes { get; set; } = [];
    public List<ScenarioPatchPreviewResult> ScenarioPatchPreviews { get; set; } = [];
    public List<EffectPackagePreviewResult> EffectPackagePreviews { get; set; } = [];
    public List<string> RequiredSmokeCommands { get; set; } = [];
    public List<string> ManualChecks { get; set; } = [];
}

public sealed class ModPackageValidationIssue
{
    public string Severity { get; set; } = "warning";
    public string Category { get; set; } = string.Empty;
    public string Target { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
}

public sealed class ModPackageChangePreview
{
    public string Category { get; set; } = string.Empty;
    public string Target { get; set; } = string.Empty;
    public int? RowId { get; set; }
    public string Field { get; set; } = string.Empty;
    public string OldValue { get; set; } = string.Empty;
    public string NewValue { get; set; } = string.Empty;
    public bool Changed { get; set; }
    public string Note { get; set; } = string.Empty;
}

public sealed class ModPackageApplyResult
{
    public string ProjectRoot { get; set; } = string.Empty;
    public string PackageId { get; set; } = string.Empty;
    public bool Applied { get; set; }
    public string Summary { get; set; } = string.Empty;
    public List<ModPackageValidationIssue> Issues { get; set; } = [];
    public List<string> BackupPaths { get; set; } = [];
    public List<string> ReportPaths { get; set; } = [];
    public List<object> ApplyResults { get; set; } = [];
}

public sealed class ModPackageValidationResult
{
    public string ProjectRoot { get; set; } = string.Empty;
    public string PackageId { get; set; } = string.Empty;
    public bool Passed { get; set; }
    public string Summary { get; set; } = string.Empty;
    public ModPackagePreviewResult? Preview { get; set; }
    public List<string> PlannedSmokeCommands { get; set; } = [];
    public List<string> ManualChecks { get; set; } = [];
    public List<ModPackageValidationIssue> Issues { get; set; } = [];
}

public sealed class ModPackageReportResult
{
    public string ProjectRoot { get; set; } = string.Empty;
    public string PackageId { get; set; } = string.Empty;
    public string ReportKind { get; set; } = string.Empty;
    public string JsonPath { get; set; } = string.Empty;
    public string MarkdownPath { get; set; } = string.Empty;
}

public sealed class ScenarioPatchPreviewResult
{
    public string PatchId { get; set; } = string.Empty;
    public string RelativePath { get; set; } = string.Empty;
    public bool CanApply { get; set; }
    public string Summary { get; set; } = string.Empty;
    public int SceneCount { get; set; }
    public int SectionCount { get; set; }
    public int CommandCount { get; set; }
    public int StructuralOperationCount { get; set; }
    public List<ModPackageValidationIssue> Issues { get; set; } = [];
    public List<ModPackageChangePreview> Changes { get; set; } = [];
}

public sealed class ScenarioPatchApplyResult
{
    public string PatchId { get; set; } = string.Empty;
    public string RelativePath { get; set; } = string.Empty;
    public bool Applied { get; set; }
    public string FilePath { get; set; } = string.Empty;
    public string BackupPath { get; set; } = string.Empty;
    public string ReportJsonPath { get; set; } = string.Empty;
    public int ChangedBytes { get; set; }
    public int SceneCount { get; set; }
    public int SectionCount { get; set; }
    public int CommandCount { get; set; }
    public string ValidationSummary { get; set; } = string.Empty;
    public List<ModPackageValidationIssue> Issues { get; set; } = [];
    public List<ModPackageChangePreview> Changes { get; set; } = [];
}
