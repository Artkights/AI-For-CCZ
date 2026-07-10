using System.Globalization;
using CCZModStudio.Core;
using CCZModStudio.Formats;
using CCZModStudio.Models;

internal partial class Program
{
    static void RunCmfDesignerUiProbeSmoke()
    {
        var project = new ProjectDetector().DetectDefaultProject();
        var oldToolsRoot = CheatMakerCmfProbe.FindDefaultOldToolsRoot(project.WorkspaceRoot)
            ?? throw new DirectoryNotFoundException("Old tools root was not found under workspace.");
        var corpus = new CheatMakerCmfProbe().ScanCorpus(oldToolsRoot);
        RunCmfDesignerUiProbeSmokeFor(project, oldToolsRoot, FindCmfByLength(corpus, 768_391), mustContainText: "策略修改his");
        RunCmfDesignerUiProbeSmokeFor(project, oldToolsRoot, FindCmfByLength(corpus, 1_145_916), mustContainText: string.Empty);
    }

    private static void RunCmfDesignerUiProbeSmokeFor(
        CczProject project,
        string oldToolsRoot,
        string cmfPath,
        string mustContainText)
    {
        var relativePath = Path.GetRelativePath(oldToolsRoot, cmfPath);
        var beforeSha = ComputeSmokeSha256(cmfPath);
        var result = new CmfDesignerExtractionService().ExtractDesignerSnapshot(
            project,
            relativePath,
            new CmfDesignerExtractionOptions
            {
                Mode = "UiProbe",
                TimeoutMs = 25000
            });
        var afterSha = ComputeSmokeSha256(cmfPath);
        if (!beforeSha.Equals(afterSha, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Original CMF SHA changed during real UiProbe smoke: " + relativePath);
        }

        if (result.Snapshot.Pages.Count <= 0)
        {
            throw new InvalidOperationException("Real UiProbe did not extract any pages for " + relativePath);
        }

        if (result.Snapshot.Controls.Count <= 0)
        {
            throw new InvalidOperationException("Real UiProbe did not extract any controls for " + relativePath);
        }

        if (!string.IsNullOrWhiteSpace(mustContainText) &&
            !result.Snapshot.Pages.Any(page => page.Name.Contains(mustContainText, StringComparison.OrdinalIgnoreCase)) &&
            !result.Snapshot.Controls.Any(control => control.Text.Contains(mustContainText, StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException("Real UiProbe did not capture expected designer text '" + mustContainText + "' for " + relativePath);
        }

        foreach (var binding in result.Snapshot.Bindings.Where(binding => !string.IsNullOrWhiteSpace(binding.UeOffsetHex)))
        {
            var normalized = binding.UeOffsetHex.Trim().Replace("0x", string.Empty, StringComparison.OrdinalIgnoreCase);
            if (!long.TryParse(normalized, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out _))
            {
                throw new InvalidOperationException("Real UiProbe produced a non-hex UE offset: " + binding.UeOffsetHex);
            }
        }

        Console.WriteLine(
            "CMF_DESIGNER_UI_PROBE_SMOKE_OK " +
            $"cmf={relativePath} pages={result.Snapshot.Pages.Count} modules={result.Snapshot.Modules.Count} " +
            $"controls={result.Snapshot.Controls.Count} bindings={result.Snapshot.Bindings.Count} report={result.ReportDirectory}");
    }
}
