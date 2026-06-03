using CCZModStudio.Core;
using CCZModStudio.Models;

namespace CCZModStudio.Formats;

public sealed class MaterialLibraryIndexer
{
    private static readonly string[] ImageExtensions = { ".png", ".jpg", ".jpeg", ".bmp" };

    public IReadOnlyList<MaterialAsset> Index(string workspaceRoot)
    {
        var root = ResolveMaterialLibraryRoot(workspaceRoot);
        return IndexRoot(root);
    }

    public IReadOnlyList<MaterialAsset> Index(CczProject project)
    {
        var root = ResolveMaterialLibraryRoot(project);
        return IndexRoot(root);
    }

    public static string? ResolveMaterialLibraryRoot(string workspaceRoot)
    {
        return ProjectDetector.FindPortableDirectory(
            workspaceRoot,
            "素材库",
            Path.Combine("普罗-综合工具v0.3", "素材库"),
            Path.Combine("老版游戏制作工具", "普罗-综合工具v0.3", "素材库"),
            Path.Combine("老版游戏制作工具", "素材库"),
            "素材库");
    }

    public static string? ResolveMaterialLibraryRoot(CczProject project)
    {
        if (!string.IsNullOrWhiteSpace(project.MaterialLibraryRoot) &&
            Directory.Exists(project.MaterialLibraryRoot))
        {
            return project.MaterialLibraryRoot;
        }

        return ProjectDetector.FindPortableDirectory(
            project,
            "素材库",
            Path.Combine("普罗-综合工具v0.3", "素材库"),
            Path.Combine("老版游戏制作工具", "普罗-综合工具v0.3", "素材库"),
            Path.Combine("老版游戏制作工具", "素材库"),
            "素材库");
    }

    private static IReadOnlyList<MaterialAsset> IndexRoot(string? root)
    {
        if (root == null) return Array.Empty<MaterialAsset>();

        var result = new List<MaterialAsset>();
        foreach (var categoryDir in Directory.GetDirectories(root).OrderBy(x => x, StringComparer.CurrentCultureIgnoreCase))
        {
            var category = Path.GetFileName(categoryDir);
            var images = Directory.GetFiles(categoryDir)
                .Where(p => ImageExtensions.Contains(Path.GetExtension(p), StringComparer.OrdinalIgnoreCase))
                .OrderBy(p => SortKey(Path.GetFileNameWithoutExtension(p)))
                .ThenBy(p => p, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
            var tags = ReadTags(categoryDir, images.Count);

            for (var i = 0; i < images.Count; i++)
            {
                var path = images[i];
                var info = new FileInfo(path);
                var width = 0;
                var height = 0;
                try
                {
                    using var img = Image.FromFile(path);
                    width = img.Width;
                    height = img.Height;
                }
                catch
                {
                    // Keep dimensions as 0; UI preview will show the real error if the file cannot be opened.
                }

                tags.TryGetValue(i, out var tag);
                result.Add(new MaterialAsset
                {
                    Category = category,
                    FileName = Path.GetFileName(path),
                    FilePath = path,
                    HexTag = tag.HexTag,
                    Description = tag.Description,
                    Width = width,
                    Height = height,
                    SizeBytes = info.Exists ? info.Length : 0
                });
            }
        }

        return result;
    }

    private static Dictionary<int, (string HexTag, string Description)> ReadTags(string categoryDir, int imageCount)
    {
        var path = Path.Combine(categoryDir, "hex.txt");
        var result = new Dictionary<int, (string HexTag, string Description)>();
        if (!File.Exists(path)) return result;

        var lines = File.ReadAllLines(path)
            .Select(x => x.Trim())
            .Where(x => x.Length > 0)
            .ToList();

        if (lines.Count >= imageCount * 2 && IsMostlyNumericEvenLines(lines, imageCount))
        {
            for (var i = 0; i < imageCount; i++)
            {
                result[i] = (lines[i * 2], lines[i * 2 + 1]);
            }
        }
        else
        {
            for (var i = 0; i < Math.Min(imageCount, lines.Count); i++)
            {
                result[i] = (lines[i], string.Empty);
            }
        }

        return result;
    }

    private static bool IsMostlyNumericEvenLines(IReadOnlyList<string> lines, int imageCount)
    {
        var numeric = 0;
        for (var i = 0; i < imageCount; i++)
        {
            if (int.TryParse(lines[i * 2], out _)) numeric++;
        }
        return numeric >= Math.Max(1, imageCount * 2 / 3);
    }

    private static int SortKey(string name) => int.TryParse(name, out var value) ? value : int.MaxValue;
}
