using System.Drawing;
using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using CCZModStudio.Core;
using CCZModStudio.Models;

namespace CCZModStudio.Formats;

public sealed class MaterialLibraryIndexer
{
    private static readonly string[] ImageExtensions = { ".png", ".jpg", ".jpeg", ".bmp" };
    private static readonly Regex TypedFolderPattern = new(@"^\s*(?<id>\d+)\s*[：:]\s*(?<name>.+?)\s*$", RegexOptions.Compiled);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

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

    public IReadOnlyList<MaterialAsset> IndexExplicitRoot(string root)
    {
        if (string.IsNullOrWhiteSpace(root)) return Array.Empty<MaterialAsset>();
        return IndexRoot(Path.GetFullPath(root));
    }

    public static string? ResolveMaterialLibraryRoot(string workspaceRoot)
    {
        var candidates = new List<string?>();
        AddMaterialRootCandidates(candidates, workspaceRoot);
        candidates.Add(ProjectDetector.FindPortableDirectory(
            workspaceRoot,
            "素材库",
            Path.Combine("普罗-综合工具v0.3", "素材库"),
            Path.Combine("老版游戏制作工具", "普罗-综合工具v0.3", "素材库"),
            Path.Combine("老版游戏制作工具", "素材库"),
            "素材库"));
        AddBuiltInMaterialRootCandidate(candidates);
        return SelectBestMaterialLibraryRoot(candidates);
    }

    public static string? ResolveMaterialLibraryRoot(CczProject project)
    {
        var candidates = new List<string?> { project.MaterialLibraryRoot };
        AddMaterialRootCandidates(candidates, project.GameRoot);
        AddMaterialRootCandidates(candidates, project.WorkspaceRoot);

        candidates.Add(ProjectDetector.FindPortableDirectory(
            project,
            "素材库",
            Path.Combine("普罗-综合工具v0.3", "素材库"),
            Path.Combine("老版游戏制作工具", "普罗-综合工具v0.3", "素材库"),
            Path.Combine("老版游戏制作工具", "素材库"),
            "素材库"));
        AddBuiltInMaterialRootCandidate(candidates);
        return SelectBestMaterialLibraryRoot(candidates);
    }

    public static string? SelectBestMaterialLibraryRoot(IEnumerable<string?> candidates)
    {
        return candidates
            .Select((path, index) => (Path: NormalizeCandidate(path), Index: index))
            .Where(item => item.Path != null && Directory.Exists(item.Path))
            .DistinctBy(item => item.Path!, StringComparer.OrdinalIgnoreCase)
            .Select(item => (item.Path, item.Index, IsNewLayout: LooksLikeNewLayout(item.Path!), Score: ScoreMaterialLibraryRoot(item.Path!)))
            .Where(item => item.Score > 0)
            .OrderByDescending(item => item.IsNewLayout)
            .ThenBy(item => item.Index)
            .ThenByDescending(item => item.Score)
            .Select(item => item.Path)
            .FirstOrDefault();
    }

    private static IReadOnlyList<MaterialAsset> IndexRoot(string? root)
    {
        if (root == null || !Directory.Exists(root)) return Array.Empty<MaterialAsset>();
        return LooksLikeNewLayout(root) ? IndexNewLayout(root) : IndexLegacyLayout(root);
    }

    private static void AddMaterialRootCandidates(List<string?> candidates, string? root)
    {
        if (string.IsNullOrWhiteSpace(root)) return;
        candidates.Add(Path.Combine(root, "素材库"));
        candidates.Add(Path.Combine(root, "普罗-综合工具v0.3", "素材库"));
        candidates.Add(Path.Combine(root, "老版游戏制作工具", "普罗-综合工具v0.3", "素材库"));
        candidates.Add(Path.Combine(root, "老版游戏制作工具", "素材库"));
    }

    private static void AddBuiltInMaterialRootCandidate(List<string?> candidates)
        => candidates.Add(PortableInstallPaths.LegacyResource("普罗-综合工具v0.3", "素材库"));

    private static string? NormalizeCandidate(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        try
        {
            return Path.GetFullPath(path);
        }
        catch
        {
            return null;
        }
    }

    private static int ScoreMaterialLibraryRoot(string root)
    {
        if (!Directory.Exists(root)) return 0;
        var score = LooksLikeNewLayout(root) ? 10_000 : 1_000;
        var terrainRoot = Path.Combine(root, "地形");
        var buildingRoot = Path.Combine(root, "建筑");
        var sceneryRoot = Path.Combine(root, "景物");
        score += CountTypedImageAssets(terrainRoot) * 10;
        score += CountTypedImageAssets(buildingRoot) * 10;
        score += CountImages(sceneryRoot);

        if (Directory.Exists(terrainRoot)) score += 100;
        if (Directory.Exists(buildingRoot)) score += 100;
        if (Directory.Exists(sceneryRoot)) score += 100;
        return score;
    }

    private static int CountTypedImageAssets(string typedRoot)
    {
        if (!Directory.Exists(typedRoot)) return 0;
        return Directory.GetDirectories(typedRoot)
            .Where(TryParseTypedFolder)
            .Sum(path => CountImages(path));
    }

    private static int CountImages(string root)
        => Directory.Exists(root)
            ? Directory.GetFiles(root, "*", SearchOption.AllDirectories).Count(IsImageFile)
            : 0;

    private static bool LooksLikeNewLayout(string root)
    {
        var terrainRoot = Path.Combine(root, "地形");
        var buildingRoot = Path.Combine(root, "建筑");
        var sceneryRoot = Path.Combine(root, "景物");
        if (!Directory.Exists(terrainRoot) || !Directory.Exists(buildingRoot) || !Directory.Exists(sceneryRoot))
        {
            return false;
        }

        return Directory.GetDirectories(terrainRoot).Any(TryParseTypedFolder) ||
               Directory.GetDirectories(buildingRoot).Any(TryParseTypedFolder);
    }

    private static IReadOnlyList<MaterialAsset> IndexNewLayout(string root)
    {
        var result = new List<MaterialAsset>();
        IndexTypedRoot(result, root, "地形", MaterialAssetTypes.Terrain);
        IndexTypedRoot(result, root, "建筑", MaterialAssetTypes.Building);
        IndexSceneryRoot(result, root);
        return result
            .OrderBy(asset => GetTypePriority(asset.AssetType))
            .ThenBy(asset => asset.TerrainId ?? byte.MaxValue)
            .ThenBy(asset => asset.GroupKey, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(asset => asset.VariantIndex)
            .ThenBy(asset => asset.FileName, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    private static void IndexTypedRoot(List<MaterialAsset> result, string root, string folderName, string assetType)
    {
        var typedRoot = Path.Combine(root, folderName);
        if (!Directory.Exists(typedRoot)) return;

        foreach (var groupDir in Directory.GetDirectories(typedRoot).OrderBy(path => ParseTypedSortKey(path).Id).ThenBy(path => path, StringComparer.CurrentCultureIgnoreCase))
        {
            if (!TryParseTypedFolder(groupDir, out var id, out _)) continue;
            var name = GetBuiltInTerrainName(id);
            var variants = ReadVariants(groupDir);
            var images = Directory.GetFiles(groupDir)
                .Where(IsImageFile)
                .OrderBy(path => SortKey(Path.GetFileNameWithoutExtension(path)))
                .ThenBy(path => path, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
            var groupKey = $"{assetType}:{id}:{name}";
            if (variants.Count > 0)
            {
                foreach (var variant in variants
                             .OrderBy(v => SortKey(Path.GetFileNameWithoutExtension(v.FileName)))
                             .ThenBy(v => v.Priority)
                             .ThenBy(v => v.Role, StringComparer.OrdinalIgnoreCase))
                {
                    var imagePath = Path.Combine(groupDir, variant.FileName);
                    if (!File.Exists(imagePath) || !IsImageFile(imagePath)) continue;
                    var dimensions = GetImageDimensions(imagePath);
                    var rect = NormalizeSourceRect(variant, dimensions.Width, dimensions.Height);
                    result.Add(CreateAsset(
                        assetType,
                        folderName,
                        imagePath,
                        id,
                        name,
                        groupKey,
                        BuildAutoTileSetKey(assetType, id, name, variant.FileName),
                        result.Count(asset => asset.GroupKey.Equals(groupKey, StringComparison.OrdinalIgnoreCase)),
                        variant.Role,
                        variant.Mask,
                        variant.Mode,
                        variant.Priority,
                        rect));
                }

                var variantFiles = variants.Select(v => v.FileName).ToHashSet(StringComparer.OrdinalIgnoreCase);
                foreach (var imagePath in images.Where(path => !variantFiles.Contains(Path.GetFileName(path))))
                {
                    var dimensions = GetImageDimensions(imagePath);
                    var rect = new Rectangle(0, 0, Math.Min(MapResourceItem.MapTilePixelSize, Math.Max(1, dimensions.Width)), Math.Min(MapResourceItem.MapTilePixelSize, Math.Max(1, dimensions.Height)));
                    result.Add(CreateAsset(
                        assetType,
                        folderName,
                        imagePath,
                        id,
                        name,
                        groupKey,
                        BuildAutoTileSetKey(assetType, id, name, Path.GetFileName(imagePath)),
                        result.Count(asset => asset.GroupKey.Equals(groupKey, StringComparison.OrdinalIgnoreCase)),
                        MaterialAutoTileRoles.Default,
                        MaterialAutoTileMasks.None,
                        MaterialAutoTileModes.Default,
                        0,
                        rect));
                }

                continue;
            }

            for (var i = 0; i < images.Count; i++)
            {
                var imagePath = images[i];
                var dimensions = GetImageDimensions(imagePath);
                var rect = new Rectangle(0, 0, Math.Min(MapResourceItem.MapTilePixelSize, Math.Max(1, dimensions.Width)), Math.Min(MapResourceItem.MapTilePixelSize, Math.Max(1, dimensions.Height)));
                result.Add(CreateAsset(
                    assetType,
                    folderName,
                    imagePath,
                    id,
                    name,
                    groupKey,
                    BuildAutoTileSetKey(assetType, id, name, Path.GetFileName(imagePath)),
                    i,
                    MaterialAutoTileRoles.Default,
                    MaterialAutoTileMasks.None,
                    MaterialAutoTileModes.Default,
                    0,
                    rect));
            }
        }
    }

    private static void IndexSceneryRoot(List<MaterialAsset> result, string root)
    {
        var sceneryRoot = Path.Combine(root, "景物");
        if (!Directory.Exists(sceneryRoot)) return;

        var images = Directory.GetFiles(sceneryRoot, "*", SearchOption.AllDirectories)
            .Where(IsImageFile)
            .OrderBy(path => SortKey(Path.GetFileNameWithoutExtension(path)))
            .ThenBy(path => path, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
        for (var i = 0; i < images.Count; i++)
        {
            var imagePath = images[i];
            var dimensions = GetImageDimensions(imagePath);
            var rect = new Rectangle(0, 0, Math.Min(MapResourceItem.MapTilePixelSize, Math.Max(1, dimensions.Width)), Math.Min(MapResourceItem.MapTilePixelSize, Math.Max(1, dimensions.Height)));
            result.Add(CreateAsset(
                MaterialAssetTypes.Scenery,
                "景物",
                imagePath,
                null,
                string.Empty,
                "Scenery",
                BuildAutoTileSetKey(MaterialAssetTypes.Scenery, null, string.Empty, Path.GetFileName(imagePath)),
                i,
                MaterialAutoTileRoles.Default,
                MaterialAutoTileMasks.None,
                MaterialAutoTileModes.Default,
                0,
                rect));
        }
    }

    private static IReadOnlyList<MaterialAsset> IndexLegacyLayout(string root)
    {
        var result = new List<MaterialAsset>();
        foreach (var categoryDir in Directory.GetDirectories(root).OrderBy(x => x, StringComparer.CurrentCultureIgnoreCase))
        {
            var category = Path.GetFileName(categoryDir);
            var images = Directory.GetFiles(categoryDir)
                .Where(IsImageFile)
                .OrderBy(p => SortKey(Path.GetFileNameWithoutExtension(p)))
                .ThenBy(p => p, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
            var tags = ReadTags(categoryDir, images.Count);
            var assetType = InferLegacyAssetType(category);

            for (var i = 0; i < images.Count; i++)
            {
                var path = images[i];
                tags.TryGetValue(i, out var tag);
                byte? terrainId = null;
                if (!assetType.Equals(MaterialAssetTypes.Scenery, StringComparison.OrdinalIgnoreCase) &&
                    MaterialHexTagParser.TryParseTerrainId(tag.HexTag, out var id))
                {
                    terrainId = id;
                }

                var name = terrainId.HasValue
                    ? GetBuiltInTerrainName(terrainId.Value)
                    : string.Empty;
                var dimensions = GetImageDimensions(path);
                var rect = new Rectangle(0, 0, Math.Min(MapResourceItem.MapTilePixelSize, Math.Max(1, dimensions.Width)), Math.Min(MapResourceItem.MapTilePixelSize, Math.Max(1, dimensions.Height)));
                result.Add(CreateAsset(
                    assetType,
                    category,
                    path,
                    terrainId,
                    name,
                    terrainId.HasValue ? $"{assetType}:{terrainId.Value}:{category}" : $"{assetType}:{category}",
                    BuildAutoTileSetKey(assetType, terrainId, name, Path.GetFileName(path)),
                    i,
                    MaterialAutoTileRoles.Default,
                    MaterialAutoTileMasks.None,
                    MaterialAutoTileModes.Default,
                    0,
                    rect,
                    tag.HexTag,
                    tag.Description));
            }
        }

        return result;
    }

    private static MaterialAsset CreateAsset(
        string assetType,
        string category,
        string path,
        byte? terrainId,
        string terrainName,
        string groupKey,
        string autoTileSetKey,
        int variantIndex,
        string autoTileRole,
        int? autoTileMask,
        string autoTileMode,
        int autoTilePriority,
        Rectangle sourceRect,
        string? hexTag = null,
        string? description = null)
    {
        var info = new FileInfo(path);
        var dimensions = GetImageDimensions(path);
        return new MaterialAsset
        {
            Category = category,
            FileName = Path.GetFileName(path),
            FilePath = path,
            HexTag = hexTag ?? (terrainId.HasValue ? terrainId.Value.ToString(CultureInfo.InvariantCulture) : string.Empty),
            Description = description ?? terrainName,
            AssetType = assetType,
            TerrainId = terrainId,
            TerrainName = terrainName,
            GroupKey = groupKey,
            AutoTileSetKey = string.IsNullOrWhiteSpace(autoTileSetKey) ? groupKey : autoTileSetKey,
            VariantIndex = variantIndex,
            AutoTileRole = string.IsNullOrWhiteSpace(autoTileRole) ? MaterialAutoTileRoles.Default : autoTileRole,
            AutoTileMask = autoTileMask,
            AutoTileMode = string.IsNullOrWhiteSpace(autoTileMode) ? MaterialAutoTileModes.Mask : autoTileMode,
            AutoTilePriority = autoTilePriority,
            SourceX = sourceRect.X,
            SourceY = sourceRect.Y,
            SourceWidth = sourceRect.Width,
            SourceHeight = sourceRect.Height,
            Width = dimensions.Width,
            Height = dimensions.Height,
            SizeBytes = info.Exists ? info.Length : 0
        };
    }

    private static string BuildAutoTileSetKey(string assetType, byte? terrainId, string terrainName, string fileName)
        => terrainId.HasValue
            ? $"{assetType}:{terrainId.Value}:{terrainName}:{Path.GetFileName(fileName)}"
            : $"{assetType}:{terrainName}:{Path.GetFileName(fileName)}";

    private static List<MaterialAutoTileVariant> ReadVariants(string groupDir)
    {
        var path = Path.Combine(groupDir, "_variants.json");
        if (!File.Exists(path)) return new List<MaterialAutoTileVariant>();

        try
        {
            return JsonSerializer.Deserialize<List<MaterialAutoTileVariant>>(File.ReadAllText(path), JsonOptions)?
                .Where(v => !string.IsNullOrWhiteSpace(v.FileName))
                .Select(v =>
                {
                    v.FileName = Path.GetFileName(v.FileName.Trim());
                    v.Role = string.IsNullOrWhiteSpace(v.Role) ? MaterialAutoTileRoles.Default : v.Role.Trim();
                    v.Mask ??= RoleToMask(v.Role);
                    v.Mode = string.IsNullOrWhiteSpace(v.Mode) ? MaterialAutoTileModes.Mask : v.Mode.Trim();
                    NormalizeLegacyCanonicalCornerVariant(v);
                    return v;
                })
                .ToList() ?? new List<MaterialAutoTileVariant>();
        }
        catch
        {
            return new List<MaterialAutoTileVariant>();
        }
    }

    public static int RoleToMask(string role)
        => role switch
        {
            MaterialAutoTileRoles.StraightH => MaterialAutoTileMasks.StraightH,
            MaterialAutoTileRoles.StraightV => MaterialAutoTileMasks.StraightV,
            MaterialAutoTileRoles.CornerNE => MaterialAutoTileMasks.CornerNE,
            MaterialAutoTileRoles.CornerNW => MaterialAutoTileMasks.CornerNW,
            MaterialAutoTileRoles.CornerSE => MaterialAutoTileMasks.CornerSE,
            MaterialAutoTileRoles.CornerSW => MaterialAutoTileMasks.CornerSW,
            MaterialAutoTileRoles.EndN => MaterialAutoTileMasks.North,
            MaterialAutoTileRoles.EndE => MaterialAutoTileMasks.East,
            MaterialAutoTileRoles.EndS => MaterialAutoTileMasks.South,
            MaterialAutoTileRoles.EndW => MaterialAutoTileMasks.West,
            MaterialAutoTileRoles.TeeN => MaterialAutoTileMasks.TeeN,
            MaterialAutoTileRoles.TeeE => MaterialAutoTileMasks.TeeE,
            MaterialAutoTileRoles.TeeS => MaterialAutoTileMasks.TeeS,
            MaterialAutoTileRoles.TeeW => MaterialAutoTileMasks.TeeW,
            MaterialAutoTileRoles.Cross => MaterialAutoTileMasks.Cross,
            MaterialAutoTileRoles.InnerCornerNE => MaterialAutoTileMasks.InnerCornerNE,
            MaterialAutoTileRoles.InnerCornerNW => MaterialAutoTileMasks.InnerCornerNW,
            MaterialAutoTileRoles.InnerCornerSE => MaterialAutoTileMasks.InnerCornerSE,
            MaterialAutoTileRoles.InnerCornerSW => MaterialAutoTileMasks.InnerCornerSW,
            _ => MaterialAutoTileMasks.None
        };

    private static void NormalizeLegacyCanonicalCornerVariant(MaterialAutoTileVariant variant)
    {
        if (variant.Y != 0 ||
            variant.Width != MapResourceItem.MapTilePixelSize ||
            variant.Height != MapResourceItem.MapTilePixelSize)
        {
            return;
        }

        if (variant.X == MapResourceItem.MapTilePixelSize * 2 &&
            variant.Mask == MaterialAutoTileMasks.CornerNW)
        {
            variant.Role = MaterialAutoTileRoles.CornerNE;
            variant.Mask = MaterialAutoTileMasks.CornerNE;
        }
        else if (variant.X == MapResourceItem.MapTilePixelSize * 3 &&
                 variant.Mask == MaterialAutoTileMasks.CornerNE)
        {
            variant.Role = MaterialAutoTileRoles.CornerNW;
            variant.Mask = MaterialAutoTileMasks.CornerNW;
        }
    }

    private static Rectangle NormalizeSourceRect(MaterialAutoTileVariant variant, int imageWidth, int imageHeight)
    {
        var x = Math.Clamp(variant.X, 0, Math.Max(0, imageWidth - 1));
        var y = Math.Clamp(variant.Y, 0, Math.Max(0, imageHeight - 1));
        var width = variant.Width <= 0 ? MapResourceItem.MapTilePixelSize : variant.Width;
        var height = variant.Height <= 0 ? MapResourceItem.MapTilePixelSize : variant.Height;
        width = Math.Clamp(width, 1, Math.Max(1, imageWidth - x));
        height = Math.Clamp(height, 1, Math.Max(1, imageHeight - y));
        return new Rectangle(x, y, width, height);
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

    private static bool TryParseTypedFolder(string path)
        => TryParseTypedFolder(path, out _, out _);

    private static bool TryParseTypedFolder(string path, out byte id, out string name)
    {
        id = 0;
        name = string.Empty;
        var match = TypedFolderPattern.Match(Path.GetFileName(path));
        if (!match.Success) return false;
        if (!byte.TryParse(match.Groups["id"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out id)) return false;
        name = match.Groups["name"].Value.Trim();
        return true;
    }

    private static (int Id, string Name) ParseTypedSortKey(string path)
        => TryParseTypedFolder(path, out var id, out var name) ? (id, name) : (int.MaxValue, Path.GetFileName(path));

    private static bool IsImageFile(string path)
        => ImageExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);

    private static int SortKey(string name)
        => int.TryParse(name, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : int.MaxValue;

    private static int GetTypePriority(string type)
        => type switch
        {
            MaterialAssetTypes.Terrain => 0,
            MaterialAssetTypes.Building => 1,
            MaterialAssetTypes.Scenery => 2,
            _ => 3
        };

    private static string InferLegacyAssetType(string category)
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

    public static string GetBuiltInTerrainName(byte terrainId)
        => terrainId switch
        {
            0 => "平原",
            1 => "草原",
            2 => "树林",
            3 => "荒地",
            4 => "山地",
            5 => "岩山",
            6 => "山崖",
            7 => "雪原",
            8 => "桥梁",
            9 => "浅滩",
            10 => "沼泽",
            11 => "池塘",
            12 => "小河",
            13 => "大河",
            14 => "栅栏",
            15 => "城墙",
            16 => "城内",
            17 => "城门",
            18 => "城池",
            19 => "关隘",
            20 => "鹿砦",
            21 => "村庄",
            22 => "兵营",
            23 => "民居",
            24 => "宝物库",
            25 => "水池",
            26 => "火",
            27 => "船",
            28 => "祭坛",
            29 => "地下",
            _ => $"地形{terrainId}"
        };

    private static (int Width, int Height) GetImageDimensions(string path)
    {
        try
        {
            using var img = Image.FromFile(path);
            return (img.Width, img.Height);
        }
        catch
        {
            return (0, 0);
        }
    }
}
