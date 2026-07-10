using System.Globalization;
using System.Text.RegularExpressions;
using CCZModStudio.Models;

namespace CCZModStudio.Core;

public sealed class SImageMaterialLayoutResolver
{
    private static readonly Regex SFolderIdRegex = new(@"^S_?(\d+)(?:$|[_\-\s].*)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex TurnFolderRegex = new(@"^turn[1-3]$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static bool TryParseSFolderId(string folderNameOrPath, out int id)
    {
        id = 0;
        var name = Path.GetFileName(folderNameOrPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        if (string.IsNullOrWhiteSpace(name)) return false;

        var match = SFolderIdRegex.Match(name);
        return match.Success &&
               int.TryParse(match.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out id);
    }

    public SImageMaterialLayoutResult Resolve(string materialFolder, IReadOnlyList<SImageStageTarget> stageTargets)
    {
        var folder = Path.GetFullPath(materialFolder);
        var rootFiles = ResolveRootFiles(folder);
        var hasFlatFiles = rootFiles.Count > 0;
        var hasTurnFolders = Directory.Exists(folder) &&
                             Directory.EnumerateDirectories(folder)
                                 .Select(Path.GetFileName)
                                 .Any(name => !string.IsNullOrWhiteSpace(name) && TurnFolderRegex.IsMatch(name));

        var stageFiles = stageTargets
            .OrderBy(stage => stage.StageSlot)
            .Select(stage => ResolveStageFiles(folder, stage))
            .ToArray();

        return new SImageMaterialLayoutResult
        {
            MaterialFolder = folder,
            Kind = ResolveKind(hasFlatFiles, hasTurnFolders),
            StageFiles = stageFiles
        };
    }

    public SImageMaterialFolderLayoutSummary InspectFolder(string materialFolder, int sImageId, string displayName)
    {
        var folder = Path.GetFullPath(materialFolder);
        var rootFiles = ResolveRootFiles(folder);
        var presentStages = new List<int>();
        var completeStages = new List<int>();

        for (var stageSlot = 1; stageSlot <= 3; stageSlot++)
        {
            var stageFolder = Path.Combine(folder, $"turn{stageSlot.ToString(CultureInfo.InvariantCulture)}");
            if (!Directory.Exists(stageFolder)) continue;

            presentStages.Add(stageSlot);
            if (HasTriplet(ResolveFiles(stageFolder)))
            {
                completeStages.Add(stageSlot);
            }
        }

        return new SImageMaterialFolderLayoutSummary
        {
            SImageId = sImageId,
            Folder = folder,
            DisplayName = string.IsNullOrWhiteSpace(displayName) ? Path.GetFileName(folder) : displayName,
            Kind = ResolveKind(rootFiles.Count > 0, presentStages.Count > 0),
            HasFlatTriplet = HasTriplet(rootFiles),
            PresentTurnStages = presentStages,
            CompleteTurnStages = completeStages
        };
    }

    private static SImageMaterialStageFiles ResolveStageFiles(string folder, SImageStageTarget stage)
    {
        var staged = ResolveTurnFolderFiles(folder, stage.StageSlot);
        var root = stage.StageSlot == 1 ? ResolveRootFiles(folder) : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var missing = new List<string>();

        var movPath = ResolveStageFilePath(staged, root, "mov.bmp");
        var atkPath = ResolveStageFilePath(staged, root, "atk.bmp");
        var spcPath = ResolveStageFilePath(staged, root, "spc.bmp");

        if (string.IsNullOrWhiteSpace(movPath)) missing.Add(BuildMissingFileText(stage.StageSlot, "mov.bmp"));
        if (string.IsNullOrWhiteSpace(atkPath)) missing.Add(BuildMissingFileText(stage.StageSlot, "atk.bmp"));
        if (string.IsNullOrWhiteSpace(spcPath)) missing.Add(BuildMissingFileText(stage.StageSlot, "spc.bmp"));

        return new SImageMaterialStageFiles
        {
            StageSlot = stage.StageSlot,
            StageName = stage.DisplayName,
            MovPath = movPath,
            AtkPath = atkPath,
            SpcPath = spcPath,
            MissingFiles = missing
        };
    }

    private static string ResolveStageFilePath(
        IReadOnlyDictionary<string, string> staged,
        IReadOnlyDictionary<string, string> root,
        string fileName)
    {
        if (staged.TryGetValue(fileName, out var stagedPath)) return stagedPath;
        return root.TryGetValue(fileName, out var rootPath) ? rootPath : string.Empty;
    }

    private static Dictionary<string, string> ResolveTurnFolderFiles(string folder, int stageSlot)
    {
        var stageFolder = Path.Combine(folder, $"turn{stageSlot.ToString(CultureInfo.InvariantCulture)}");
        return ResolveFiles(stageFolder);
    }

    private static Dictionary<string, string> ResolveRootFiles(string folder)
        => ResolveFiles(folder);

    private static Dictionary<string, string> ResolveFiles(string folder)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (!Directory.Exists(folder)) return result;

        foreach (var file in Directory.EnumerateFiles(folder))
        {
            var fileName = Path.GetFileName(file);
            if (fileName.Equals("mov.bmp", StringComparison.OrdinalIgnoreCase) ||
                fileName.Equals("atk.bmp", StringComparison.OrdinalIgnoreCase) ||
                fileName.Equals("spc.bmp", StringComparison.OrdinalIgnoreCase))
            {
                result[fileName] = file;
            }
        }

        return result;
    }

    private static bool HasTriplet(IReadOnlyDictionary<string, string> files)
        => files.ContainsKey("mov.bmp") &&
           files.ContainsKey("atk.bmp") &&
           files.ContainsKey("spc.bmp");

    private static SImageMaterialLayoutKind ResolveKind(bool hasFlatFiles, bool hasTurnFolders)
    {
        if (hasFlatFiles && hasTurnFolders) return SImageMaterialLayoutKind.Mixed;
        return hasTurnFolders ? SImageMaterialLayoutKind.TurnFolderStages : SImageMaterialLayoutKind.FlatFirstStage;
    }

    private static string BuildMissingFileText(int stageSlot, string fileName)
        => stageSlot == 1
            ? $"turn1/{fileName} or {fileName}"
            : $"turn{stageSlot.ToString(CultureInfo.InvariantCulture)}/{fileName}";
}
