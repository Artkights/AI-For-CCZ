using CCZModStudio.Core;
using CCZModStudio.Formats;
using CCZModStudio.Models;

internal partial class Program
{
    static void RunCmfCorpusSmoke()
    {
        var project = new ProjectDetector().DetectDefaultProject();
        var oldToolsRoot = CheatMakerCmfProbe.FindDefaultOldToolsRoot(project.WorkspaceRoot)
            ?? throw new DirectoryNotFoundException("Old tools root was not found under workspace.");

        var probe = new CheatMakerCmfProbe();
        var report = probe.ScanCorpus(oldToolsRoot);
        AssertEqual("total CMF count", 29, report.TotalFiles);
        AssertEqual("CheatMaker CMF count", 29, report.CheatMakerCmfCount);
        AssertEqual("cmf04 count", 24, GetCount(report.SignatureCounts, "cmf04"));
        AssertEqual("cmf05 count", 1, GetCount(report.SignatureCounts, "cmf05"));
        AssertEqual("cmf0a count", 4, GetCount(report.SignatureCounts, "cmf0a"));
        AssertEqual("CCZ root CMF count", 7, GetCount(report.CategoryCounts, "CczRelevantRootSample"));
        AssertEqual("CheatMaker format sample count", 22, GetCount(report.CategoryCounts, "CheatMakerFormatSample"));

        AssertEntry(report, "CheatMaker配套文件_star175EXE额外修改器[6.4版].cmf", "cmf0a", 394_376, 17, "43A5102EAA09C47247E9F98EBA4954077AB26B52A6C168BB99CE3A385684885D");
        AssertEntry(report, "Star6.5引擎exe修改器.cmf", "cmf0a", 768_391, 8, "30D6141B45794527660A925E345B490B8AE42E99E3EE4DF2E6F4E8F70F7CABAA");
        AssertEntry(report, "Star6.6X 引擎.cmf", "cmf0a", 1_145_916, 15, "EBE2DD8A336EB83654114A913581684C6AA62908F03E63C1E38C58020C13F297");
        AssertEntry(report, "特效CM.cmf", "cmf0a", 12_370, 1, "CCE11C630E3EDDC9B2476DD6A323D7F32DD408F8623CB6C151695F93EA06EC03");
        AssertEntry(report, "修改特效名.cmf", "cmf04", 15_346, 1, "2B3AB502B15F0B72FF3334E1CEA8E70938A1F81764AE4AB7EB66C7A6868E22F0");
        AssertEntry(report, "剧本特效介绍（读取imsg）.cmf", "cmf04", 12_930, 1, "895D226D764F00287C37805E37DB98700AC5B88ECFE426340BEBBA089B9FC003");
        AssertEntry(report, "剧本特效名字（读取引擎）.cmf", "cmf04", 12_762, 1, "23C4B854EEBEC6777A44FB4E79B99604DED306D17060892C8F4B56F32A43D661");

        var comparisonTarget = Path.Combine(oldToolsRoot, "Star6.6X 引擎.cmf");
        var comparisonBaseline = Path.Combine(oldToolsRoot, "Star6.5引擎exe修改器.cmf");
        var comparison = probe.Probe(comparisonTarget, comparisonBaseline, "CMF corpus comparison smoke", oldToolsRoot).Comparison
            ?? throw new InvalidOperationException("CMF corpus comparison did not return a comparison result.");
        if (comparison.LengthDelta <= 0 || comparison.ExtraUtf16CrlfSegments != 7)
        {
            throw new InvalidOperationException($"CMF comparison mismatch: lengthDelta={comparison.LengthDelta}, segmentDelta={comparison.ExtraUtf16CrlfSegments}.");
        }

        var temp = Path.Combine(Path.GetTempPath(), "cczmodstudio-cmf-corpus-not-cmf-" + Guid.NewGuid().ToString("N") + ".bin");
        try
        {
            File.WriteAllBytes(temp, new byte[] { 0x50, 0x4B, 0x03, 0x04, 0x4D, 0x5A });
            var rejected = probe.Probe(temp);
            if (rejected.IsCheatMakerCmf)
            {
                throw new InvalidOperationException("ZIP/PE-like bytes were incorrectly accepted as CheatMaker CMF.");
            }
        }
        finally
        {
            if (File.Exists(temp)) File.Delete(temp);
        }

        Console.WriteLine(
            "CMF_CORPUS_SMOKE OK " +
            $"root={oldToolsRoot} total={report.TotalFiles} " +
            $"signatures=cmf04:{GetCount(report.SignatureCounts, "cmf04")},cmf05:{GetCount(report.SignatureCounts, "cmf05")},cmf0a:{GetCount(report.SignatureCounts, "cmf0a")} " +
            $"categories=ccz:{GetCount(report.CategoryCounts, "CczRelevantRootSample")},format:{GetCount(report.CategoryCounts, "CheatMakerFormatSample")}");
    }

    private static void AssertEntry(CheatMakerCmfCorpusReport report, string relativePath, string signature, long length, int crlf, string sha256)
    {
        var entry = report.Entries.FirstOrDefault(item => item.RelativePath.Equals(relativePath, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException("CMF corpus entry was not found: " + relativePath);
        if (!entry.IsCheatMakerCmf ||
            !entry.FormatSignature.Equals(signature, StringComparison.OrdinalIgnoreCase) ||
            entry.Length != length ||
            entry.Utf16CrlfCount != crlf ||
            !entry.Sha256.Equals(sha256, StringComparison.OrdinalIgnoreCase) ||
            !entry.EvidenceCategory.Equals("CczRelevantRootSample", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"CMF corpus entry mismatch for {relativePath}: " +
                $"signature={entry.FormatSignature}, length={entry.Length}, crlf={entry.Utf16CrlfCount}, sha={entry.Sha256}, category={entry.EvidenceCategory}.");
        }
    }

    private static int GetCount(IReadOnlyDictionary<string, int> counts, string key)
        => counts.TryGetValue(key, out var count) ? count : 0;

    private static void AssertEqual(string label, int expected, int actual)
    {
        if (expected != actual)
        {
            throw new InvalidOperationException($"{label} mismatch: expected={expected}, actual={actual}.");
        }
    }
}
