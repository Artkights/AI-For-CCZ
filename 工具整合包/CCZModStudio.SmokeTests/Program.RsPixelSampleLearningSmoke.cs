using CCZModStudio.Core;
using CCZModStudio.Models;
using System.Globalization;

internal static class ProgramRsPixelSampleLearningSmoke
{
    public static void Run(string[] args)
    {
        var detector = new ProjectDetector();
        var envGameRoot = Environment.GetEnvironmentVariable("CCZMODSTUDIO_GAME_ROOT");
        var project = string.IsNullOrWhiteSpace(envGameRoot)
            ? detector.DetectDefaultProject()
            : detector.CreateProjectFromGameRoot(envGameRoot);

        var outputId = ReadArg(args, "--output-id") ?? "spear_cavalry_mvp";
        var topReviewCount = int.TryParse(ReadArg(args, "--top-review-count"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : 12;
        var overwrite = args.Contains("--overwrite", StringComparer.OrdinalIgnoreCase);

        var result = new RsPixelSampleLearningService().Build(project, new RsPixelSampleLearningRequest
        {
            UnitType = "spear_cavalry",
            OutputId = outputId,
            OverwriteExisting = overwrite,
            TopReviewCount = topReviewCount
        });

        if (result.CandidateCount == 0)
        {
            throw new InvalidOperationException("R/S sample learning found no candidates.");
        }

        if (result.Reports.Count < 5)
        {
            throw new InvalidOperationException("R/S sample learning did not write the expected report set.");
        }

        Console.WriteLine(
            $"RS_PIXEL_SAMPLE_LEARNING_SMOKE OK root={result.OutputRoot} candidates={result.CandidateCount} complete={result.CompleteCandidateCount} strong={result.StrongMachineCandidateCount} partial={result.PartialCandidateCount} negative={result.NegativeCandidateCount}");
        foreach (var report in result.Reports)
        {
            Console.WriteLine("REPORT " + report);
        }
    }

    private static string? ReadArg(string[] args, string name)
    {
        for (var i = 0; i < args.Length - 1; i++)
        {
            if (args[i].Equals(name, StringComparison.OrdinalIgnoreCase)) return args[i + 1];
        }
        return null;
    }
}
