using CCZModStudio.Models;

namespace CCZModStudio.McpServer;

public sealed partial class CczMcpRuntime
{
    public object DetectImageAssignerOracle(string? gameRoot)
    {
        var project = LoadProject(gameRoot);
        var profile = _imageAssignerOracleService.Detect(project);
        return new
        {
            project.GameRoot,
            Oracle = profile,
            SafetyNote = "Read-only detection. The official image assigner is used as an oracle/referee, not as an automatic write path."
        };
    }

    public object ReadImageAssignerOracleConfig(string? gameRoot)
    {
        var project = LoadProject(gameRoot);
        var profile = _imageAssignerOracleService.Detect(project);
        return new
        {
            project.GameRoot,
            profile.VersionKind,
            profile.CompatibilityStatus,
            profile.SystemIniPath,
            Config = profile.Config,
            StrategyExtensionReadOnlyCandidates = profile.Config.StrategyExtensionAddresses,
            SafetyNote = "System.ini keys are official-tool evidence. Mg* strategy-extension addresses remain read-only candidates until a dedicated writer is validated."
        };
    }

    public object CompareImageAssignerOracle(string? gameRoot, bool includeGlobalCandidates)
    {
        var project = LoadProject(gameRoot);
        var tables = LoadTables(project);
        var comparison = _imageAssignerOracleService.Compare(project, tables, includeGlobalCandidates);
        return new
        {
            project.GameRoot,
            Comparison = comparison,
            SafetyNote = "Comparison validates CCZModStudio assumptions against the official image assigner configuration; it does not write files."
        };
    }

    public object PlanImageAssignerValidation(string? gameRoot, string changeKind, int? rowId)
    {
        var project = LoadProject(gameRoot);
        var plan = _imageAssignerOracleService.BuildValidationPlan(project, changeKind, rowId);
        return new
        {
            project.GameRoot,
            Plan = plan,
            SafetyNote = "Run this plan only against test copies. Official-tool writes into the original project are outside the supported automation path."
        };
    }

    public object RunImageAssignerOracleSmoke(string? gameRoot, string? mode)
    {
        var project = LoadProject(gameRoot);
        var tables = LoadTables(project);
        return new
        {
            project.GameRoot,
            Result = _imageAssignerOracleService.RunSmoke(project, tables, mode)
        };
    }

    public object CompareImageAssignerOutput(string beforeRoot, string officialAfterRoot, string cczAfterRoot)
    {
        var report = _imageAssignerOracleService.CompareOutputs(beforeRoot, officialAfterRoot, cczAfterRoot);
        return new
        {
            Report = report,
            SafetyNote = "Compares three existing directories only. No official tool, game file, or project file is modified."
        };
    }

    public object RunImageAssignerAssignmentExperiment(string? gameRoot, string changeKind, int rowId, int? newValue)
    {
        var project = LoadProject(gameRoot);
        var tables = LoadTables(project);
        var result = _imageAssignerOracleService.RunAssignmentWriteExperiment(project, tables, changeKind, rowId, newValue);
        return new
        {
            project.GameRoot,
            Result = result,
            Passed = result.SameOffset && result.SameBytes && result.RereadMatches,
            SafetyNote = "This experiment writes only generated test copies under CCZModStudio_TestCopies. The original project root is not modified."
        };
    }

    private object BuildImageAssignerOracleStatusPayload(CczProject project, HexTableDefinition table)
    {
        var status = _imageAssignerOracleService.ResolveTableOracleStatus(project, table);
        if (string.IsNullOrWhiteSpace(status))
        {
            return new { Applies = false, OracleStatus = string.Empty };
        }

        var profile = _imageAssignerOracleService.Detect(project);
        return new
        {
            Applies = true,
            OracleStatus = status,
            profile.VersionKind,
            profile.CompatibilityStatus,
            profile.SystemIniPath,
            SafetyNote = "R/S image assignment tables are checked against the official B image assigner System.ini."
        };
    }
}
