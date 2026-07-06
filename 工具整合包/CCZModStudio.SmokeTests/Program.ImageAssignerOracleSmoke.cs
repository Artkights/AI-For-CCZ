using CCZModStudio.Core;
using CCZModStudio.Models;

partial class Program
{
    static void RunImageAssignerOracleSmoke(CczProject project, IReadOnlyList<HexTableDefinition> tables)
    {
        var service = new OfficialImageAssignerOracleService();
        var profile = service.Detect(project);
        if (!profile.Found)
        {
            throw new InvalidOperationException("Official image assigner oracle was not found.");
        }

        AssertStringEqual("6.5 oracle version", "6.5", profile.VersionKind);
        AssertStringEqual("6.5 oracle compatibility", "Compatible", profile.CompatibilityStatus);
        AssertLongEqual("6.5 executable length", OfficialImageAssignerOracleService.Expected65ExeLength, profile.ExecutableLength ?? -1);
        AssertLongEqual("6.5 FileHead", OfficialImageAssignerOracleService.ExpectedSImageOffset, GetRequiredNumeric(profile, "FileHead"));
        AssertLongEqual("6.5 RFileHead", OfficialImageAssignerOracleService.ExpectedRImageOffset, GetRequiredNumeric(profile, "RFileHead"));
        AssertLongEqual("6.5 UserXK", OfficialImageAssignerOracleService.ExpectedUserXkOffset, GetRequiredNumeric(profile, "UserXK"));
        AssertLongEqual("6.5 SCount", OfficialImageAssignerOracleService.Expected65StarItemCount, GetRequiredNumeric(profile, "SCount"));
        AssertLongEqual("6.5 DefID", OfficialImageAssignerOracleService.ExpectedDefId, GetRequiredNumeric(profile, "DefID"));
        AssertLongEqual("6.5 AssID", OfficialImageAssignerOracleService.ExpectedAssId, GetRequiredNumeric(profile, "AssID"));
        AssertLongEqual("6.5 SMagic", OfficialImageAssignerOracleService.ExpectedStrategyCount, GetRequiredNumeric(profile, "SMagic"));
        if (profile.Dependencies.Any(item => !item.Exists))
        {
            throw new InvalidOperationException("6.5 oracle dependency missing: " + string.Join(", ", profile.Dependencies.Where(item => !item.Exists).Select(item => item.Name)));
        }

        var comparison = service.Compare(project, tables, includeGlobalCandidates: true);
        AssertStringEqual("6.5 oracle comparison", "MatchedOfficialImageAssigner", comparison.OracleStatus);
        AssertContainsCheck(comparison, "R形象", "MatchedOfficialImageAssigner");
        AssertContainsCheck(comparison, "S形象", "MatchedOfficialImageAssigner");
        AssertContainsCheck(comparison, "兵种相克", "MatchedOfficialImageAssigner");
        AssertContainsCheck(comparison, "GlobalNumericSettings", "NeedsUiOrDiffExtraction");

        var plan = service.BuildValidationPlan(project, "r_image_assignment", 0);
        if (!plan.ExpectedByteRanges.Any(value => value.Contains("E1000", StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException("R image validation plan did not include Ekd5.exe@0xE1000.");
        }

        Run66xOracleSmoke(project);
        Console.WriteLine("IMAGE_ASSIGNER_ORACLE_SMOKE_OK");
    }

    static void RunImageAssignerOracleExperimentSmoke(CczProject project, IReadOnlyList<HexTableDefinition> tables)
    {
        var service = new OfficialImageAssignerOracleService();
        var r = service.RunAssignmentWriteExperiment(project, tables, "r_image_assignment", 0, null);
        AssertOracleExperimentPassed("R", r);
        Console.WriteLine($"IMAGE_ASSIGNER_ORACLE_EXPERIMENT_R passed=True offset={r.OfficialOffsetHex}/{r.CczOffsetHex} original={r.OriginalValue} new={r.NewValue}");
        Console.WriteLine($"IMAGE_ASSIGNER_ORACLE_EXPERIMENT_R_ROOT before={r.BeforeRoot}");
        Console.WriteLine($"IMAGE_ASSIGNER_ORACLE_EXPERIMENT_R_ROOT official={r.OfficialCaseRoot}");
        Console.WriteLine($"IMAGE_ASSIGNER_ORACLE_EXPERIMENT_R_ROOT ccz={r.CczCaseRoot}");

        var s = service.RunAssignmentWriteExperiment(project, tables, "s_image_assignment", 0, null);
        AssertOracleExperimentPassed("S", s);
        Console.WriteLine($"IMAGE_ASSIGNER_ORACLE_EXPERIMENT_S passed=True offset={s.OfficialOffsetHex}/{s.CczOffsetHex} original={s.OriginalValue} new={s.NewValue}");
        Console.WriteLine($"IMAGE_ASSIGNER_ORACLE_EXPERIMENT_S_ROOT before={s.BeforeRoot}");
        Console.WriteLine($"IMAGE_ASSIGNER_ORACLE_EXPERIMENT_S_ROOT official={s.OfficialCaseRoot}");
        Console.WriteLine($"IMAGE_ASSIGNER_ORACLE_EXPERIMENT_S_ROOT ccz={s.CczCaseRoot}");
        Console.WriteLine("IMAGE_ASSIGNER_ORACLE_EXPERIMENT_SMOKE_OK");
    }

    private static void Run66xOracleSmoke(CczProject defaultProject)
    {
        var workspace = defaultProject.WorkspaceRoot;
        var gameRoot = Path.Combine(workspace, "基底", "新改曹操傳6.6修正版");
        if (!Directory.Exists(gameRoot))
        {
            Console.WriteLine("IMAGE_ASSIGNER_ORACLE_66X_SKIPPED missing 6.6 revised baseline");
            return;
        }

        var project66 = new ProjectDetector().CreateProjectFromGameRoot(gameRoot);
        var profile = new OfficialImageAssignerOracleService().Detect(project66);
        if (!profile.Found)
        {
            throw new InvalidOperationException("6.6x official image assigner oracle was not found.");
        }

        AssertStringEqual("6.6x oracle version", "6.6x", profile.VersionKind);
        AssertStringEqual("6.6x oracle compatibility", "Compatible", profile.CompatibilityStatus);
        AssertLongEqual("6.6x executable length", OfficialImageAssignerOracleService.Expected66xExeLength, profile.ExecutableLength ?? -1);
        AssertLongEqual("6.6x FileHead", OfficialImageAssignerOracleService.ExpectedSImageOffset, GetRequiredNumeric(profile, "FileHead"));
        AssertLongEqual("6.6x RFileHead", OfficialImageAssignerOracleService.ExpectedRImageOffset, GetRequiredNumeric(profile, "RFileHead"));
        AssertLongEqual("6.6x UserXK", OfficialImageAssignerOracleService.ExpectedUserXkOffset, GetRequiredNumeric(profile, "UserXK"));
        AssertLongEqual("6.6x SCount", OfficialImageAssignerOracleService.Expected66xStarItemCount, GetRequiredNumeric(profile, "SCount"));
        AssertLongEqual("6.6x SMagic", OfficialImageAssignerOracleService.ExpectedStrategyCount, GetRequiredNumeric(profile, "SMagic"));
        foreach (var key in new[] { "MgID", "MgAIYN1", "MgAIYN2", "MgHit", "MgHurt", "MgHurtYN", "MgMeff", "MgMcall", "Mg8", "MgMF" })
        {
            if (!profile.Config.StrategyExtensionAddresses.ContainsKey(key))
            {
                throw new InvalidOperationException("6.6x oracle missing strategy extension key: " + key);
            }
        }
    }

    private static long GetRequiredNumeric(ImageAssignerOracleProfile profile, string key)
        => profile.Config.NumericValues.TryGetValue(key, out var value)
            ? value
            : throw new InvalidOperationException($"Oracle config missing numeric key {key}.");

    private static void AssertLongEqual(string label, long expected, long actual)
    {
        if (expected != actual)
        {
            throw new InvalidOperationException($"{label} mismatch: expected={expected}, actual={actual}.");
        }
    }

    private static void AssertStringEqual(string label, string expected, string actual)
    {
        if (!string.Equals(expected, actual, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"{label} mismatch: expected={expected}, actual={actual}.");
        }
    }

    private static void AssertContainsCheck(ImageAssignerOracleComparison comparison, string key, string status)
    {
        var check = comparison.Checks.FirstOrDefault(item => item.Key.Equals(key, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException("Oracle comparison missing check " + key);
        AssertStringEqual("oracle check " + key, status, check.Status);
    }

    private static void AssertOracleExperimentPassed(string label, ImageAssignerOracleExperimentResult result)
    {
        if (!result.SameOffset || !result.SameBytes || !result.RereadMatches)
        {
            throw new InvalidOperationException($"{label} oracle experiment failed: sameOffset={result.SameOffset}, sameBytes={result.SameBytes}, reread={result.RereadMatches}, official={result.OfficialOffsetHex}, ccz={result.CczOffsetHex}.");
        }
    }
}
