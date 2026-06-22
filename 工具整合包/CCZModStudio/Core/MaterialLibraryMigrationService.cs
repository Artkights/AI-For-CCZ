using System.Drawing;
using System.Text.Encodings.Web;
using System.Text.Json;
using CCZModStudio.Formats;
using CCZModStudio.Models;

namespace CCZModStudio.Core;

public sealed class MaterialLibraryMigrationService
{
    private static readonly string[] ImageExtensions = { ".png", ".jpg", ".jpeg", ".bmp" };

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public MaterialLibraryMigrationResult Migrate(string oldRoot, string newRoot, string? hexTexPath = null, bool overwrite = false)
    {
        if (string.IsNullOrWhiteSpace(oldRoot)) throw new ArgumentException("Old material library root is required.", nameof(oldRoot));
        if (string.IsNullOrWhiteSpace(newRoot)) throw new ArgumentException("New material library root is required.", nameof(newRoot));
        oldRoot = Path.GetFullPath(oldRoot);
        newRoot = Path.GetFullPath(newRoot);
        if (!Directory.Exists(oldRoot)) throw new DirectoryNotFoundException(oldRoot);

        if (Directory.Exists(newRoot) && !overwrite && Directory.EnumerateFileSystemEntries(newRoot).Any())
        {
            throw new InvalidOperationException("Output material library already exists and is not empty: " + newRoot);
        }

        Directory.CreateDirectory(Path.Combine(newRoot, "地形"));
        Directory.CreateDirectory(Path.Combine(newRoot, "建筑"));
        Directory.CreateDirectory(Path.Combine(newRoot, "景物"));

        _ = ReadTerrainNames(hexTexPath) ?? ReadTerrainNames(Path.Combine(oldRoot, "HexTex.txt"));
        var result = new MaterialLibraryMigrationResult
        {
            OldRoot = oldRoot,
            NewRoot = newRoot
        };
        var outputCounters = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        foreach (var categoryDir in Directory.GetDirectories(oldRoot).OrderBy(path => path, StringComparer.CurrentCultureIgnoreCase))
        {
            var category = Path.GetFileName(categoryDir);
            var images = Directory.GetFiles(categoryDir)
                .Where(IsImageFile)
                .OrderBy(path => SortKey(Path.GetFileNameWithoutExtension(path)))
                .ThenBy(path => path, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
            if (images.Count == 0) continue;

            var tags = ReadTags(categoryDir, images.Count);
            var targetKind = InferTargetKind(category);
            for (var i = 0; i < images.Count; i++)
            {
                var image = images[i];
                tags.TryGetValue(i, out var tag);
                if (targetKind == MaterialAssetTypes.Scenery)
                {
                    CopyScenery(newRoot, image, outputCounters);
                    result.SceneryImageCount++;
                    continue;
                }

                var id = MaterialHexTagParser.TryParseTerrainId(tag.HexTag, out var parsedId) ? parsedId : (byte)0;
                var name = BuildMigratedTerrainName(id);
                var groupDir = Path.Combine(newRoot, targetKind == MaterialAssetTypes.Terrain ? "地形" : "建筑", MakeTypedFolderName(id, name));
                Directory.CreateDirectory(groupDir);
                var copiedPath = CopyNumbered(groupDir, image, outputCounters);
                if (targetKind == MaterialAssetTypes.Terrain)
                {
                    result.TerrainImageCount++;
                }
                else
                {
                    result.BuildingImageCount++;
                    if (IsDirectionalLegacyCategory(category))
                    {
                        result.GeneratedVariantMetadataCount += EnsureVariantMetadata(groupDir, copiedPath);
                    }
                }
            }
        }

        return result;
    }

    private static void CopyScenery(string newRoot, string sourcePath, Dictionary<string, int> counters)
    {
        var sceneryRoot = Path.Combine(newRoot, "景物");
        Directory.CreateDirectory(sceneryRoot);
        CopyNumbered(sceneryRoot, sourcePath, counters);
    }

    private static string CopyNumbered(string targetDir, string sourcePath, Dictionary<string, int> counters)
    {
        Directory.CreateDirectory(targetDir);
        if (!counters.TryGetValue(targetDir, out var next))
        {
            next = Directory.GetFiles(targetDir)
                .Where(IsImageFile)
                .Select(path => SortKey(Path.GetFileNameWithoutExtension(path)))
                .Where(value => value != int.MaxValue)
                .DefaultIfEmpty(0)
                .Max() + 1;
        }

        var extension = Path.GetExtension(sourcePath);
        string destination;
        do
        {
            destination = Path.Combine(targetDir, $"{next++}{extension}");
        }
        while (File.Exists(destination));

        counters[targetDir] = next;
        File.Copy(sourcePath, destination, overwrite: true);
        return destination;
    }

    private static int EnsureVariantMetadata(string groupDir, string imagePath)
    {
        var path = Path.Combine(groupDir, "_variants.json");
        var existing = new List<MaterialAutoTileVariant>();
        if (File.Exists(path))
        {
            try
            {
                existing = JsonSerializer.Deserialize<List<MaterialAutoTileVariant>>(File.ReadAllText(path), JsonOptions) ?? new List<MaterialAutoTileVariant>();
            }
            catch
            {
                existing = new List<MaterialAutoTileVariant>();
            }
        }

        var fileName = Path.GetFileName(imagePath);
        if (existing.Any(v => v.FileName.Equals(fileName, StringComparison.OrdinalIgnoreCase)))
        {
            return 0;
        }

        var variants = new MaterialAutoTileMetadataService().BuildVariantsForImage(imagePath);
        foreach (var variant in variants)
        {
            existing.Add(variant);
        }

        File.WriteAllText(path, JsonSerializer.Serialize(existing, JsonOptions));
        return 1;
    }

    private static Dictionary<byte, string>? ReadTerrainNames(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return null;
        var result = new Dictionary<byte, string>();
        var lines = File.ReadAllLines(path)
            .Select(line => line.Trim())
            .Where(line => line.Length > 0 && !line.StartsWith("#", StringComparison.Ordinal))
            .ToList();
        for (var i = 0; i < lines.Count; i++)
        {
            var line = lines[i];
            if (TryParseTerrainNameLine(line, out var id, out var name))
            {
                if (!string.IsNullOrWhiteSpace(name)) result[id] = name;
                continue;
            }

            if (i <= byte.MaxValue)
            {
                result[(byte)i] = line;
            }
        }

        return result;
    }

    private static bool TryParseTerrainNameLine(string line, out byte id, out string name)
    {
        id = 0;
        name = string.Empty;
        var separators = new[] { '\t', ',', '，', ':', '：', '=', ' ' };
        var parts = line.Split(separators, 2, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length < 2 || !MaterialHexTagParser.TryParseTerrainId(parts[0], out id)) return false;
        name = parts[1].Trim();
        return name.Length > 0;
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
            if (MaterialHexTagParser.TryParseTerrainId(lines[i * 2], out _)) numeric++;
        }
        return numeric >= Math.Max(1, imageCount * 2 / 3);
    }

    private static string BuildMigratedTerrainName(byte id)
        => MaterialLibraryIndexer.GetBuiltInTerrainName(id);

    private static string InferTargetKind(string category)
    {
        if (category.Contains("地形", StringComparison.CurrentCultureIgnoreCase)) return MaterialAssetTypes.Terrain;
        if (category.Contains("建筑", StringComparison.CurrentCultureIgnoreCase) ||
            category.Contains("围墙", StringComparison.CurrentCultureIgnoreCase) ||
            category.Contains("随机", StringComparison.CurrentCultureIgnoreCase))
        {
            return MaterialAssetTypes.Building;
        }

        return MaterialAssetTypes.Scenery;
    }

    private static bool IsDirectionalLegacyCategory(string category)
        => category.Contains("围墙", StringComparison.CurrentCultureIgnoreCase) ||
           category.Contains("随机", StringComparison.CurrentCultureIgnoreCase);

    private static string MakeTypedFolderName(byte id, string name)
        => $"{id}：{MakeSafeName(name)}";

    private static string MakeSafeName(string value)
    {
        value = string.IsNullOrWhiteSpace(value) ? "未标记" : value.Trim();
        foreach (var ch in Path.GetInvalidFileNameChars())
        {
            value = value.Replace(ch, '_');
        }

        return value;
    }

    private static bool IsImageFile(string path)
        => ImageExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);

    private static int SortKey(string name)
        => int.TryParse(name, out var value) ? value : int.MaxValue;

    private static (int Width, int Height) GetImageDimensions(string path)
    {
        try
        {
            using var img = Image.FromFile(path);
            return (img.Width, img.Height);
        }
        catch
        {
            return (MapResourceItem.MapTilePixelSize, MapResourceItem.MapTilePixelSize);
        }
    }
}

public sealed class MaterialLibraryMigrationResult
{
    public string OldRoot { get; init; } = string.Empty;
    public string NewRoot { get; init; } = string.Empty;
    public int TerrainImageCount { get; set; }
    public int BuildingImageCount { get; set; }
    public int SceneryImageCount { get; set; }
    public int GeneratedVariantMetadataCount { get; set; }
}
