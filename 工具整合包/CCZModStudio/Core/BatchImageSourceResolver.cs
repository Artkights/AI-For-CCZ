using System.Globalization;
using System.Text.RegularExpressions;

namespace CCZModStudio.Core;

public enum BatchImageSourceKind
{
    ItemIcon,
    StrategyIcon,
    Face
}

public sealed record BatchImageSourceCandidate(string SourcePath, int? FieldValue, string SourceKey);
public sealed record BatchItemIconSourcePair(int IconIndex, string LargeSourcePath, string SmallSourcePath);

public static class BatchImageSourceResolver
{
    private static readonly Regex ItemIconFileRegex = new(@"^item_icon_(\d+)$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex ItemIconPairFileRegex = new(@"^item_icon_(\d+)_(small|large)$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex StrategyIconFileRegex = new(@"^strategy_icon_(\d+)$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex FaceFileRegex = new(@"^face_(\d+)$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex ItemIconFolderRegex = new(@"^item_icon_(\d+)(?:_|$)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex StrategyIconFolderRegex = new(@"^strategy_icon_(\d+)(?:_|$)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex FaceFolderRegex = new(@"^face_(\d+)(?:_|$)", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static IReadOnlyList<BatchImageSourceCandidate> Resolve(
        BatchImageSourceKind kind,
        IReadOnlyList<string> sourceFiles,
        string sourceRoot)
    {
        var candidates = new List<BatchImageSourceCandidate>();
        foreach (var sourceFile in sourceFiles.Where(path => !string.IsNullOrWhiteSpace(path)))
        {
            var fullPath = Path.GetFullPath(sourceFile);
            candidates.Add(new BatchImageSourceCandidate(
                fullPath,
                TryParseCanonicalFileId(kind, fullPath),
                Path.GetFileName(fullPath)));
        }

        if (string.IsNullOrWhiteSpace(sourceRoot))
        {
            return OrderCandidates(candidates);
        }

        var root = Path.GetFullPath(sourceRoot);
        if (!Directory.Exists(root))
        {
            throw new DirectoryNotFoundException($"Batch image source root was not found: {root}");
        }

        foreach (var sourceFile in Directory.EnumerateFiles(root)
                     .Where(IsImageFile)
                     .Select(Path.GetFullPath))
        {
            var id = TryParseCanonicalFileId(kind, sourceFile);
            if (id.HasValue)
            {
                candidates.Add(new BatchImageSourceCandidate(sourceFile, id, Path.GetFileName(sourceFile)));
            }
        }

        foreach (var folder in Directory.EnumerateDirectories(root).OrderBy(path => path, StringComparer.CurrentCultureIgnoreCase))
        {
            var id = TryParseLegacyFolderId(kind, folder);
            if (!id.HasValue) continue;

            var source = ResolveLegacyFolderSource(kind, folder, id.Value);
            if (!string.IsNullOrWhiteSpace(source))
            {
                candidates.Add(new BatchImageSourceCandidate(
                    Path.GetFullPath(source),
                    id.Value,
                    Path.GetFileName(folder)));
            }
        }

        return OrderCandidates(candidates);
    }

    public static IReadOnlyDictionary<int, BatchItemIconSourcePair> ResolveItemIconPairs(
        IReadOnlyList<string> sourceFiles,
        string sourceRoot)
    {
        var files = new List<string>();
        files.AddRange(sourceFiles.Where(path => !string.IsNullOrWhiteSpace(path)).Select(Path.GetFullPath));
        if (!string.IsNullOrWhiteSpace(sourceRoot) && Directory.Exists(sourceRoot))
        {
            files.AddRange(Directory.EnumerateFiles(Path.GetFullPath(sourceRoot))
                .Where(IsImageFile)
                .Select(Path.GetFullPath));
        }

        var grouped = new Dictionary<int, (string Small, string Large)>();
        foreach (var file in files.Where(File.Exists))
        {
            var match = ItemIconPairFileRegex.Match(Path.GetFileNameWithoutExtension(file));
            if (!match.Success) continue;
            if (!int.TryParse(match.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id)) continue;
            grouped.TryGetValue(id, out var current);
            if (match.Groups[2].Value.Equals("small", StringComparison.OrdinalIgnoreCase))
            {
                current.Small = file;
            }
            else
            {
                current.Large = file;
            }

            grouped[id] = current;
        }

        return grouped
            .Where(item => !string.IsNullOrWhiteSpace(item.Value.Large))
            .ToDictionary(
                item => item.Key,
                item => new BatchItemIconSourcePair(item.Key, item.Value.Large, item.Value.Small),
                EqualityComparer<int>.Default);
    }

    private static IReadOnlyList<BatchImageSourceCandidate> OrderCandidates(IEnumerable<BatchImageSourceCandidate> candidates)
        => candidates
            .GroupBy(candidate => candidate.SourcePath, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .OrderBy(candidate => Path.GetFileName(candidate.SourcePath), StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(candidate => candidate.SourcePath, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();

    private static int? TryParseCanonicalFileId(BatchImageSourceKind kind, string sourcePath)
    {
        var name = Path.GetFileNameWithoutExtension(sourcePath);
        var regex = kind switch
        {
            BatchImageSourceKind.ItemIcon => ItemIconFileRegex,
            BatchImageSourceKind.StrategyIcon => StrategyIconFileRegex,
            BatchImageSourceKind.Face => FaceFileRegex,
            _ => null
        };
        if (regex == null) return null;
        var match = regex.Match(name);
        return match.Success && int.TryParse(match.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id)
            ? id
            : null;
    }

    private static int? TryParseLegacyFolderId(BatchImageSourceKind kind, string folder)
    {
        var name = Path.GetFileName(folder);
        var regex = kind switch
        {
            BatchImageSourceKind.ItemIcon => ItemIconFolderRegex,
            BatchImageSourceKind.StrategyIcon => StrategyIconFolderRegex,
            BatchImageSourceKind.Face => FaceFolderRegex,
            _ => null
        };
        if (regex == null) return null;
        var match = regex.Match(name);
        return match.Success && int.TryParse(match.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id)
            ? id
            : null;
    }

    private static string? ResolveLegacyFolderSource(BatchImageSourceKind kind, string folder, int id)
    {
        var names = kind switch
        {
            BatchImageSourceKind.ItemIcon => new[] { "large.bmp", "icon.bmp" },
            BatchImageSourceKind.StrategyIcon => new[] { "icon.bmp" },
            BatchImageSourceKind.Face when id == 0 => new[] { "face.bmp", "face_01.bmp" },
            BatchImageSourceKind.Face => new[] { "face.bmp" },
            _ => Array.Empty<string>()
        };

        var files = Directory.EnumerateFiles(folder).ToArray();
        foreach (var name in names)
        {
            var match = files.FirstOrDefault(file => Path.GetFileName(file).Equals(name, StringComparison.OrdinalIgnoreCase));
            if (!string.IsNullOrWhiteSpace(match)) return match;
        }

        return null;
    }

    private static bool IsImageFile(string path)
    {
        var extension = Path.GetExtension(path);
        return extension.Equals(".bmp", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".jpg", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".jpeg", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".png", StringComparison.OrdinalIgnoreCase);
    }
}
