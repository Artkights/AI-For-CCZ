using CCZModStudio.Core;
using CCZModStudio.Models;
using System.Globalization;

internal partial class Program
{
    private const string AbilityTierLocal65Root = @"F:\从0开始的AI制作曹操传MOD\曹操传加强版6.5（未加密）\基底\加强版6.5未加密版\加强版6.5未加密版";

    static void RunAbilityTierPatchSmoke()
    {
        var sourceRoot = Directory.Exists(AbilityTierLocal65Root)
            ? AbilityTierLocal65Root
            : new ProjectDetector().DetectDefaultProject().GameRoot;
        var sourceProject = new ProjectDetector().CreateProjectFromGameRoot(sourceRoot);
        var service = new AbilityTierPatchService();

        var scan = service.Scan(sourceProject);
        AssertAbilityTierScan(scan);

        var sixTierProfile = service.BuildDefaultProfile(6, "Letter");
        var sixPreview = service.Preview(sourceProject, sixTierProfile);
        AssertSixTierPreview(sixPreview);

        var tenTierPreview = service.Preview(sourceProject, service.BuildDefaultProfile(10, "Number"));
        if (tenTierPreview.CanWrite ||
            !tenTierPreview.Status.Equals("RequiresRelocation", StringComparison.OrdinalIgnoreCase) ||
            tenTierPreview.Changes.Count != 0)
        {
            throw new InvalidOperationException("10-tier ability profile should remain relocation-only and non-writable.");
        }

        var multiCharPreview = service.Preview(
            sourceProject,
            new AbilityTierProfile
            {
                ProfileName = "InvalidMultiCharLabel",
                TierCount = 6,
                DisplayMode = "Custom",
                Labels = ["C", "B", "A", "S", "X", "10"],
                PatchMode = "MergeOriginalBranches"
            });
        if (multiCharPreview.CanWrite ||
            !multiCharPreview.Warnings.Any(warning => warning.Contains("relocated display table", StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException("Multi-character ability labels should require a relocated display table.");
        }

        var smokeRoot = CreateAbilityTierSmokeCopy(sourceProject);
        var testProject = new ProjectDetector().CreateProjectFromGameRoot(smokeRoot);
        var writeSix = service.Write(testProject, sixTierProfile);
        AssertAbilityTierWriteResult(writeSix, 6);

        var rereadSix = service.Scan(testProject, writeReport: false);
        AssertMaxReturnAndClamp(rereadSix, 6);

        var writeSeven = service.Write(testProject, service.BuildDefaultProfile(7, "Letter"));
        AssertAbilityTierWriteResult(writeSeven, 7);

        var rereadSeven = service.Scan(testProject, writeReport: false);
        AssertMaxReturnAndClamp(rereadSeven, 7);

        Console.WriteLine(
            "ABILITY_TIER_PATCH_SMOKE_OK " +
            $"source={sourceProject.GameRoot} testRoot={smokeRoot} " +
            $"sha={scan.ExeSha256} labels={string.Join("/", scan.DisplayLabels.Take(7))} " +
            $"sixChanges={sixPreview.Changes.Count} report={scan.ReportPath}");
    }

    private static void AssertAbilityTierScan(AbilityTierScanReport scan)
    {
        if (!File.Exists(scan.ReportPath))
        {
            throw new InvalidOperationException("Ability tier scan did not write a query report.");
        }

        var expectedThresholds = new Dictionary<string, byte>(StringComparer.Ordinal)
        {
            ["Z"] = 0x7F,
            ["V"] = 0x6F,
            ["X"] = 0x64,
            ["S"] = 0x5A,
            ["A"] = 0x46,
            ["B"] = 0x32
        };
        foreach (var expected in expectedThresholds)
        {
            var rule = scan.ThresholdRules.SingleOrDefault(item => item.Label == expected.Key);
            if (rule == null || rule.Threshold != expected.Value)
            {
                throw new InvalidOperationException($"Ability tier threshold mismatch for {expected.Key}.");
            }
        }

        if (scan.ReturnRules.Count != 6 ||
            scan.CallSites.Count != 5 ||
            scan.CallSites.Any(site => site.TargetVa != 0x0041A534) ||
            scan.ClampSite == null ||
            scan.DisplayPointer == null ||
            scan.DisplayPointer.LabelPointers.Count < 7 ||
            !scan.DisplayLabels.Take(7).SequenceEqual(["C", "B", "A", "S", "X", "V", "Z"], StringComparer.Ordinal))
        {
            throw new InvalidOperationException("Ability tier scan did not collect the expected 6.5 patch metadata.");
        }

        if (!scan.ClampSite.BytesHex.StartsWith("3C07", StringComparison.OrdinalIgnoreCase) ||
            scan.ClampSite.BytesHex[4..].Length != 8)
        {
            throw new InvalidOperationException("Ability tier clamp signature is unexpected: " + scan.ClampSite.BytesHex);
        }

        if (!scan.CanPatchMergeProfiles)
        {
            throw new InvalidOperationException("Ability tier scan should be eligible for 4-7 tier merge patching: " + string.Join("; ", scan.Warnings));
        }
    }

    private static void AssertSixTierPreview(AbilityTierPatchPreview preview)
    {
        if (!preview.CanWrite)
        {
            throw new InvalidOperationException("6-tier ability profile should be writable: " + string.Join("; ", preview.Warnings));
        }

        var zMerge = preview.Changes.SingleOrDefault(change =>
            change.Category == "ReturnMerge" &&
            change.Va == 0x0041A547 &&
            change.OldBytesHex.Equals("07", StringComparison.OrdinalIgnoreCase) &&
            change.NewBytesHex.Equals("06", StringComparison.OrdinalIgnoreCase));
        if (zMerge == null)
        {
            throw new InvalidOperationException("6-tier preview did not include the expected Z return merge 07->06.");
        }

        var clamp = preview.Changes.SingleOrDefault(change => change.Category == "Clamp");
        if (clamp == null ||
            !clamp.NewBytesHex.Equals("3C077202B006", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("6-tier preview did not include expected clamp bytes.");
        }
    }

    private static string CreateAbilityTierSmokeCopy(CczProject project)
    {
        var smokeRoot = Path.Combine(
            project.WorkspaceRoot,
            "CCZModStudio_TestCopies",
            "AbilityTierPatchSmoke_" + DateTime.Now.ToString("yyyyMMdd_HHmmss_fff", CultureInfo.InvariantCulture));
        Directory.CreateDirectory(smokeRoot);

        foreach (var fileName in new[] { "Ekd5.exe", "Data.e5", "Imsg.e5", "Star.e5" })
        {
            var source = Path.Combine(project.GameRoot, fileName);
            if (File.Exists(source))
            {
                File.Copy(source, Path.Combine(smokeRoot, fileName), overwrite: false);
            }
        }

        File.WriteAllText(
            Path.Combine(smokeRoot, "_CCZModStudio_TestCopy.txt"),
            $"CreatedAt={DateTime.Now:yyyy-MM-dd HH:mm:ss}\r\nSource={project.GameRoot}\r\nPurpose=ability tier patch smoke\r\n");
        return smokeRoot;
    }

    private static void AssertAbilityTierWriteResult(AbilityTierPatchWriteResult result, int tierCount)
    {
        if (result.ChangedBytes <= 0 ||
            string.IsNullOrWhiteSpace(result.BackupPath) ||
            !File.Exists(result.BackupPath) ||
            string.IsNullOrWhiteSpace(result.ReportJsonPath) ||
            !File.Exists(result.ReportJsonPath))
        {
            throw new InvalidOperationException("Ability tier write did not create expected backup/report or changed no bytes.");
        }

        AssertMaxReturnAndClamp(result.ReadBack, tierCount);
    }

    private static void AssertMaxReturnAndClamp(AbilityTierScanReport scan, int tierCount)
    {
        var maxReturn = scan.ReturnRules.Max(rule => rule.ReturnValue);
        if (maxReturn > tierCount)
        {
            throw new InvalidOperationException($"Ability tier max return mismatch: {maxReturn} > {tierCount}.");
        }

        var expectedClamp = Convert.ToHexString(new byte[] { 0x3C, checked((byte)(tierCount + 1)), 0x72, 0x02, 0xB0, checked((byte)tierCount) });
        if (!string.Equals(scan.ClampSite?.BytesHex, expectedClamp, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Ability tier clamp mismatch: {scan.ClampSite?.BytesHex} != {expectedClamp}.");
        }
    }
}
