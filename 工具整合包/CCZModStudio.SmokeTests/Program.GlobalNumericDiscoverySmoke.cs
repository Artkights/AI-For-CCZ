using CCZModStudio.Core;
using CCZModStudio.Formats;

internal partial class Program
{
    static void RunGlobalNumericDiscoverySmoke()
    {
        var project = new ProjectDetector().DetectDefaultProject();
        var tables = new HexTableParser().Load(project.HexTableXmlPath);
        var service = new GlobalSettingsService();
        var document = service.Load(project, tables);
        var report = new GlobalNumericDiscoveryService().PrepareManualDiffExperiment(project, service.GetNumericDefinitions());

        if (report.Status != "NeedsManualOfficialDiff")
        {
            throw new InvalidOperationException("Discovery smoke must not promote numeric fields automatically.");
        }

        if (!File.Exists(report.ReportPath) ||
            !Directory.Exists(report.BeforeRoot) ||
            !Directory.Exists(report.OfficialCaseRoot) ||
            !Directory.Exists(report.OfficialToolRoot))
        {
            throw new InvalidOperationException("Discovery smoke did not create the expected report/test-copy directories.");
        }

        if (report.Fields.Count != document.NumericSettings.Count || report.Fields.Count < 10)
        {
            throw new InvalidOperationException("Discovery report field count does not match the global numeric catalog.");
        }

        foreach (var field in report.Fields)
        {
            var setting = document.NumericSettings.Single(item => item.Key.Equals(field.Key, StringComparison.OrdinalIgnoreCase));
            if (setting.CanEdit)
            {
                if (field.ByteLength <= 0 ||
                    string.IsNullOrWhiteSpace(field.TargetFileName) ||
                    field.UniqueDiff)
                {
                    throw new InvalidOperationException("Discovery smoke produced unexpected verified field metadata: " + field.Key);
                }

                continue;
            }

            if (setting.OracleCoverage == "ParentKeyUseLeaf")
            {
                if (field.ByteLength != 0 ||
                    field.FileOffset != 0 ||
                    !string.IsNullOrWhiteSpace(field.TargetFileName) ||
                    field.UniqueDiff)
                {
                    throw new InvalidOperationException("Discovery smoke unexpectedly produced parent field metadata: " + field.Key);
                }

                continue;
            }

            if (field.ByteLength != 0 ||
                field.FileOffset != 0 ||
                !string.IsNullOrWhiteSpace(field.TargetFileName) ||
                field.UniqueDiff)
            {
                throw new InvalidOperationException("Discovery smoke unexpectedly produced a writable/unique field: " + field.Key);
            }
        }

        foreach (var diff in report.FileDiffs.Where(diff => diff.BeforeExists && diff.AfterExists))
        {
            if (diff.ChangedByteCount != 0 || diff.Changes.Count != 0)
            {
                throw new InvalidOperationException("Initial discovery copies must be byte-identical before manual official edits: " + diff.RelativePath);
            }
        }

        var lowRisk = new GlobalNumericDiscoveryService().PrepareLowRiskManualDiffExperiment(project);
        if (!File.Exists(lowRisk.ReportPath) ||
            !Directory.Exists(lowRisk.BeforeRoot) ||
            !Directory.Exists(lowRisk.NoopCaseRoot) ||
            !Directory.Exists(lowRisk.NoopOfficialToolRoot) ||
            lowRisk.Cases.Count != 7)
        {
            throw new InvalidOperationException("Low-risk discovery smoke did not create noop/case/tool directories.");
        }

        foreach (var lowRiskCase in lowRisk.Cases)
        {
            if (!Directory.Exists(lowRiskCase.CaseRoot) ||
                !Directory.Exists(lowRiskCase.OfficialToolRoot) ||
                string.IsNullOrWhiteSpace(lowRiskCase.Instruction))
            {
                throw new InvalidOperationException("Low-risk discovery case is incomplete: " + lowRiskCase.Key);
            }
        }

        var compare = new GlobalNumericDiscoveryService().CompareLowRiskCaseDiffs(project, lowRisk.EvidenceRoot);
        if (compare.Status != "NeedsManualOfficialDiff" ||
            !File.Exists(compare.ReportPath) ||
            compare.Cases.Count != 7 ||
            compare.Cases.Any(item => item.HasChanges || item.MinimalPromotableCandidate))
        {
            throw new InvalidOperationException("Low-risk compare should remain pending before manual official edits.");
        }

        Console.WriteLine(
            "GLOBAL_NUMERIC_DISCOVERY_SMOKE_OK " +
            "status=" + report.Status +
            " fields=" + report.Fields.Count +
            " report=" + report.ReportPath +
            " lowRiskCases=" + lowRisk.Cases.Count +
            " lowRiskReport=" + lowRisk.ReportPath);
    }
}
