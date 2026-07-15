using System.Globalization;
using System.Text.RegularExpressions;

namespace CCZModStudio.Core;

public static class JobSImageMaterialLayout
{
    private static readonly Regex JobFolderRegex = new(@"^Job_?(\d+)$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex FactionFolderRegex = new(@"^Faction_?([1-3])$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static IReadOnlyList<string> RequiredFileNames { get; } = ["mov.bmp", "atk.bmp", "spc.bmp"];

    public static string BuildJobFolderName(int jobId)
    {
        if (jobId < 0) throw new ArgumentOutOfRangeException(nameof(jobId));
        return "Job" + jobId.ToString(CultureInfo.InvariantCulture);
    }

    public static string BuildFactionFolderName(int factionSlot)
    {
        ValidateFactionSlot(factionSlot);
        return "Faction" + factionSlot.ToString(CultureInfo.InvariantCulture);
    }

    public static string BuildJobFolder(string root, int jobId)
        => Path.Combine(root, BuildJobFolderName(jobId));

    public static string BuildFactionFolder(string jobFolder, int factionSlot)
        => Path.Combine(jobFolder, BuildFactionFolderName(factionSlot));

    public static bool TryParseJobFolder(string folder, out int jobId)
    {
        jobId = 0;
        var match = JobFolderRegex.Match(Path.GetFileName(Path.TrimEndingDirectorySeparator(folder)));
        return match.Success &&
               int.TryParse(match.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out jobId);
    }

    public static bool TryParseFactionFolder(string folder, out int factionSlot)
    {
        factionSlot = 0;
        var match = FactionFolderRegex.Match(Path.GetFileName(Path.TrimEndingDirectorySeparator(folder)));
        return match.Success &&
               int.TryParse(match.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out factionSlot);
    }

    public static IReadOnlyList<string> GetMissingRequiredFiles(string folder)
        => RequiredFileNames
            .Where(fileName => !File.Exists(Path.Combine(folder, fileName)))
            .ToArray();

    public static bool HasCompleteTriplet(string folder)
        => Directory.Exists(folder) && GetMissingRequiredFiles(folder).Count == 0;

    public static bool HasAnyTripletFile(string folder)
        => Directory.Exists(folder) && RequiredFileNames.Any(fileName => File.Exists(Path.Combine(folder, fileName)));

    public static IReadOnlyList<int> NormalizeFactionSlots(IReadOnlyList<int> factionSlots, int? fallbackSlot = null)
    {
        if (factionSlots.Count == 0 && !fallbackSlot.HasValue)
        {
            throw new InvalidOperationException("Select at least one faction slot.");
        }

        var slots = factionSlots.Count == 0
            ? [fallbackSlot!.Value]
            : factionSlots.Distinct().OrderBy(slot => slot).ToArray();
        foreach (var slot in slots) ValidateFactionSlot(slot);
        return slots;
    }

    public static void ValidateFactionSlot(int factionSlot)
    {
        if (factionSlot is < 1 or > 3)
        {
            throw new InvalidOperationException($"Faction slots must be 1..3: {factionSlot}");
        }
    }
}
