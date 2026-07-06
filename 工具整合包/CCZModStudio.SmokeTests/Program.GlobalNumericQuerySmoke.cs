using CCZModStudio.Core;
using CCZModStudio.Formats;

internal partial class Program
{
    static void RunGlobalNumericQuerySmoke()
    {
        var project = new ProjectDetector().DetectDefaultProject();
        var tables = new HexTableParser().Load(project.HexTableXmlPath);
        var service = new GlobalSettingsService();
        var document = service.Load(project, tables);
        var report = new GlobalNumericQueryService().Query(project, service.GetNumericDefinitions());

        if (!File.Exists(report.ReportPath) || !Directory.Exists(report.EvidenceRoot))
        {
            throw new InvalidOperationException("Global numeric query did not write the expected JSON report.");
        }

        if (report.Fields.Count != document.NumericSettings.Count || report.Fields.Count < 10)
        {
            throw new InvalidOperationException("Global numeric query field count does not match the numeric catalog.");
        }

        var editableKeys = report.Fields
            .Where(field => field.CanEdit)
            .Select(field => field.Key)
            .OrderBy(key => key, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (!editableKeys.SequenceEqual([
                "EquipmentLevelLimitNormal",
                "EquipmentLevelLimitSpecial",
                "EquipmentLevelRaiseNormal",
                "EquipmentLevelRaiseSpecial",
                "LevelLimit",
                "PromotionLevelFirst",
                "UpgradeExperience"
            ], StringComparer.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Global numeric query reported unexpected editable fields: " + string.Join(", ", editableKeys));
        }

        foreach (var field in report.Fields)
        {
            if (field.CanEdit)
            {
                if (field.WriteTargets.Count == 0 ||
                    field.Candidates.All(candidate => !candidate.IsVerifiedWriteTarget))
                {
                    throw new InvalidOperationException("Verified numeric field did not expose verified targets in query report: " + field.Key);
                }

                continue;
            }

            if (field.OracleCoverage == "ParentKeyUseLeaf")
            {
                if (field.EvidenceStatus != "组合项" ||
                    field.WriteTargets.Count != 0 ||
                    field.Candidates.Any(candidate => candidate.IsVerifiedWriteTarget))
                {
                    throw new InvalidOperationException("Parent numeric field query unexpectedly looks writable: " + field.Key);
                }

                continue;
            }

            if (field.EvidenceStatus != "待验证" ||
                field.WriteTargets.Count != 0 ||
                field.Candidates.Any(candidate => candidate.IsVerifiedWriteTarget) ||
                !field.QueryConclusion.Contains("保持只读", StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Pending numeric field query unexpectedly looks promotable: " + field.Key);
            }
        }

        var level = report.Fields.Single(field => field.Key == "LevelLimit");
        var exp = report.Fields.Single(field => field.Key == "UpgradeExperience");
        Console.WriteLine(
            "GLOBAL_NUMERIC_QUERY_SMOKE_OK " +
            $"fields={report.Fields.Count} editable={editableKeys.Length} " +
            $"levelCandidates={level.TotalCandidateCount} expCandidates={exp.TotalCandidateCount} " +
            "report=" + report.ReportPath);
    }
}
