using CCZModStudio.Core;
using CCZModStudio.Formats;

internal partial class Program
{
    static void RunCmfProbeSmoke()
    {
        var project = new ProjectDetector().DetectDefaultProject();
        var workspaceRoot = project.WorkspaceRoot;
        var target = CheatMakerCmfProbe.FindDefaultStar66XSample(workspaceRoot)
            ?? throw new FileNotFoundException("6.6X CMF sample was not found under workspace.", workspaceRoot);
        var baseline = CheatMakerCmfProbe.FindDefaultStar66KBaseline(workspaceRoot)
            ?? throw new FileNotFoundException("6.6K CMF baseline was not found under workspace.", workspaceRoot);

        var probe = new CheatMakerCmfProbe();
        var targetResult = probe.Probe(target, baseline, "6.6X CMF smoke target");
        if (!targetResult.IsCheatMakerCmf ||
            !targetResult.Signature.Equals(CheatMakerCmfProbe.CmfSignature, StringComparison.OrdinalIgnoreCase) ||
            targetResult.Length != Ccz66RevisedLayout.Star66XCheatMakerCmfLength ||
            !targetResult.Sha256.Equals(Ccz66RevisedLayout.Star66XCheatMakerCmfSha256, StringComparison.OrdinalIgnoreCase) ||
            targetResult.Utf16CrlfCount != Ccz66RevisedLayout.Star66XCheatMakerCmfUtf16CrlfCount ||
            !targetResult.LooksProtectedOrObfuscated)
        {
            throw new InvalidOperationException(
                "6.6X CMF probe mismatch: " +
                $"signature={targetResult.Signature}, length={targetResult.Length}, sha={targetResult.Sha256}, " +
                $"crlf={targetResult.Utf16CrlfCount}, protected={targetResult.LooksProtectedOrObfuscated}");
        }

        var baselineResult = probe.Probe(baseline);
        if (!baselineResult.IsCheatMakerCmf ||
            baselineResult.Length != Ccz66RevisedLayout.Star66KCheatMakerCmfLength ||
            baselineResult.Utf16CrlfCount != Ccz66RevisedLayout.Star66KCheatMakerCmfUtf16CrlfCount)
        {
            throw new InvalidOperationException(
                "6.6K CMF baseline probe mismatch: " +
                $"signature={baselineResult.Signature}, length={baselineResult.Length}, crlf={baselineResult.Utf16CrlfCount}");
        }

        if (targetResult.Comparison == null ||
            targetResult.Comparison.BaselineLength != Ccz66RevisedLayout.Star66KCheatMakerCmfLength ||
            targetResult.Comparison.ExtraUtf16CrlfSegments != 4)
        {
            throw new InvalidOperationException("6.6 CMF comparison summary did not report the expected K/X segment delta.");
        }

        var temp = Path.Combine(Path.GetTempPath(), "cczmodstudio-not-cmf-" + Guid.NewGuid().ToString("N") + ".bin");
        try
        {
            File.WriteAllBytes(temp, new byte[] { 0x50, 0x4B, 0x03, 0x04, 0x4D, 0x5A });
            var rejected = probe.Probe(temp);
            if (rejected.IsCheatMakerCmf)
            {
                throw new InvalidOperationException("Ordinary ZIP/PE-like bytes were incorrectly accepted as CheatMaker CMF.");
            }
        }
        finally
        {
            if (File.Exists(temp)) File.Delete(temp);
        }

        Console.WriteLine(
            "CMF_PROBE_SMOKE OK " +
            $"targetLength={targetResult.Length} targetCrlf={targetResult.Utf16CrlfCount} " +
            $"baselineLength={baselineResult.Length} baselineCrlf={baselineResult.Utf16CrlfCount} " +
            $"delta={targetResult.Comparison.ExtraUtf16CrlfSegments}");
    }
}
