using System.Drawing;
using System.Text.Encodings.Web;
using System.Text.Json;
using CCZModStudio.Models;

namespace CCZModStudio.Core;

public sealed class MaterialAutoTileMetadataService
{
    private static readonly string[] ImageExtensions = { ".png", ".jpg", ".jpeg", ".bmp" };

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public int RepairGroupDirectory(string groupDir, bool overwrite = true)
    {
        if (string.IsNullOrWhiteSpace(groupDir) || !Directory.Exists(groupDir)) return 0;
        var images = Directory.GetFiles(groupDir)
            .Where(IsImageFile)
            .OrderBy(path => SortKey(Path.GetFileNameWithoutExtension(path)))
            .ThenBy(path => path, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
        if (images.Count == 0) return 0;

        var outputPath = Path.Combine(groupDir, "_variants.json");
        if (!overwrite && File.Exists(outputPath)) return 0;

        var variants = BuildVariantsForGroup(groupDir, images);
        if (variants.Count == 0) return 0;

        File.WriteAllText(outputPath, JsonSerializer.Serialize(variants, JsonOptions));
        return variants.Count;
    }

    public IReadOnlyList<MaterialAutoTileVariant> BuildVariantsForImage(string imagePath)
    {
        if (string.IsNullOrWhiteSpace(imagePath) || !File.Exists(imagePath)) return Array.Empty<MaterialAutoTileVariant>();

        return BuildVariantsForImage(imagePath, InferAutoTileModeFromPath(imagePath));
    }

    private static IReadOnlyList<MaterialAutoTileVariant> BuildVariantsForImage(string imagePath, string autoTileMode)
    {
        var fileName = Path.GetFileName(imagePath);
        using var image = Image.FromFile(imagePath);
        var tile = MapResourceItem.MapTilePixelSize;
        var columns = Math.Max(1, image.Width / tile);

        if (image.Width >= tile * 15 && image.Height <= tile)
        {
            return BuildCanonicalStripVariants(fileName, columns, image.Width, image.Height, NormalizeAutoTileMode(autoTileMode));
        }

        return new[] { BuildVariant(fileName, MaterialAutoTileRoles.Default, MaterialAutoTileMasks.None, MaterialAutoTileModes.Default, 0, 0, 0, image.Width, image.Height) };
    }

    private static List<MaterialAutoTileVariant> BuildVariantsForGroup(string groupDir, IReadOnlyList<string> images)
    {
        var mode = InferAutoTileModeFromPath(groupDir);
        return images
            .SelectMany(path => BuildVariantsForImage(path, mode))
            .ToList();
    }

    private static IReadOnlyList<MaterialAutoTileVariant> BuildCanonicalStripVariants(string fileName, int columns, int imageWidth, int imageHeight, string autoTileMode)
    {
        var variants = new List<MaterialAutoTileVariant>();
        var order = GetCanonicalMaskOrder(autoTileMode);
        var tile = MapResourceItem.MapTilePixelSize;
        for (var i = 0; i < Math.Min(order.Length, columns); i++)
        {
            var x = i * tile;
            var (role, mask) = order[i];
            variants.Add(BuildVariant(fileName, role, mask, autoTileMode, i, x, 0, imageWidth, imageHeight));
        }

        return variants;
    }

    private static MaterialAutoTileVariant BuildVariant(
        string fileName,
        string role,
        int mask,
        string mode,
        int priority,
        int x,
        int y,
        int imageWidth,
        int imageHeight)
    {
        var tile = MapResourceItem.MapTilePixelSize;
        return new MaterialAutoTileVariant
        {
            FileName = fileName,
            Role = role,
            Mask = mask,
            Mode = mode,
            Priority = priority,
            X = x,
            Y = y,
            Width = Math.Min(tile, Math.Max(1, imageWidth - x)),
            Height = Math.Min(tile, Math.Max(1, imageHeight - y))
        };
    }

    public static (string Role, int Mask)[] GetCanonicalMaskOrder()
        =>
        [
            (MaterialAutoTileRoles.StraightH, MaterialAutoTileMasks.StraightH),
            (MaterialAutoTileRoles.StraightV, MaterialAutoTileMasks.StraightV),
            (MaterialAutoTileRoles.CornerNW, MaterialAutoTileMasks.CornerNW),
            (MaterialAutoTileRoles.CornerNE, MaterialAutoTileMasks.CornerNE),
            (MaterialAutoTileRoles.CornerSW, MaterialAutoTileMasks.CornerSW),
            (MaterialAutoTileRoles.CornerSE, MaterialAutoTileMasks.CornerSE),
            (MaterialAutoTileRoles.EndN, MaterialAutoTileMasks.North),
            (MaterialAutoTileRoles.EndE, MaterialAutoTileMasks.East),
            (MaterialAutoTileRoles.EndS, MaterialAutoTileMasks.South),
            (MaterialAutoTileRoles.EndW, MaterialAutoTileMasks.West),
            (MaterialAutoTileRoles.TeeN, MaterialAutoTileMasks.TeeN),
            (MaterialAutoTileRoles.TeeS, MaterialAutoTileMasks.TeeS),
            (MaterialAutoTileRoles.TeeW, MaterialAutoTileMasks.TeeW),
            (MaterialAutoTileRoles.TeeE, MaterialAutoTileMasks.TeeE),
            (MaterialAutoTileRoles.Cross, MaterialAutoTileMasks.Cross)
        ];

    public static (string Role, int Mask)[] GetCanonicalMaskOrder(string autoTileMode)
    {
        var normalized = NormalizeAutoTileMode(autoTileMode);
        if (!normalized.Equals(MaterialAutoTileModes.LinePath, StringComparison.OrdinalIgnoreCase))
        {
            return GetCanonicalMaskOrder();
        }

        return
        [
            (MaterialAutoTileRoles.StraightH, MaterialAutoTileMasks.StraightH),
            (MaterialAutoTileRoles.StraightV, MaterialAutoTileMasks.StraightV),
            (MaterialAutoTileRoles.CornerNW, MaterialAutoTileMasks.CornerNW),
            (MaterialAutoTileRoles.CornerNE, MaterialAutoTileMasks.CornerNE),
            (MaterialAutoTileRoles.CornerSW, MaterialAutoTileMasks.CornerSW),
            (MaterialAutoTileRoles.CornerSE, MaterialAutoTileMasks.CornerSE),
            (MaterialAutoTileRoles.EndN, MaterialAutoTileMasks.North),
            (MaterialAutoTileRoles.EndS, MaterialAutoTileMasks.South),
            (MaterialAutoTileRoles.EndE, MaterialAutoTileMasks.East),
            (MaterialAutoTileRoles.EndW, MaterialAutoTileMasks.West),
            (MaterialAutoTileRoles.TeeN, MaterialAutoTileMasks.TeeN),
            (MaterialAutoTileRoles.TeeS, MaterialAutoTileMasks.TeeS),
            (MaterialAutoTileRoles.TeeW, MaterialAutoTileMasks.TeeW),
            (MaterialAutoTileRoles.TeeE, MaterialAutoTileMasks.TeeE),
            (MaterialAutoTileRoles.Cross, MaterialAutoTileMasks.Cross)
        ];
    }

    public static string RoleFromMask(int mask)
        => mask switch
        {
            MaterialAutoTileMasks.StraightH => MaterialAutoTileRoles.StraightH,
            MaterialAutoTileMasks.StraightV => MaterialAutoTileRoles.StraightV,
            MaterialAutoTileMasks.CornerNE => MaterialAutoTileRoles.CornerNE,
            MaterialAutoTileMasks.CornerNW => MaterialAutoTileRoles.CornerNW,
            MaterialAutoTileMasks.CornerSE => MaterialAutoTileRoles.CornerSE,
            MaterialAutoTileMasks.CornerSW => MaterialAutoTileRoles.CornerSW,
            MaterialAutoTileMasks.North => MaterialAutoTileRoles.EndN,
            MaterialAutoTileMasks.East => MaterialAutoTileRoles.EndE,
            MaterialAutoTileMasks.South => MaterialAutoTileRoles.EndS,
            MaterialAutoTileMasks.West => MaterialAutoTileRoles.EndW,
            MaterialAutoTileMasks.TeeN => MaterialAutoTileRoles.TeeN,
            MaterialAutoTileMasks.TeeE => MaterialAutoTileRoles.TeeE,
            MaterialAutoTileMasks.TeeS => MaterialAutoTileRoles.TeeS,
            MaterialAutoTileMasks.TeeW => MaterialAutoTileRoles.TeeW,
            MaterialAutoTileMasks.Cross => MaterialAutoTileRoles.Cross,
            MaterialAutoTileMasks.InnerCornerNE => MaterialAutoTileRoles.InnerCornerNE,
            MaterialAutoTileMasks.InnerCornerNW => MaterialAutoTileRoles.InnerCornerNW,
            MaterialAutoTileMasks.InnerCornerSE => MaterialAutoTileRoles.InnerCornerSE,
            MaterialAutoTileMasks.InnerCornerSW => MaterialAutoTileRoles.InnerCornerSW,
            _ => MaterialAutoTileRoles.Default
        };

    public static string InferAutoTileModeFromPath(string path)
    {
        var id = TryGetTypedMaterialId(path);
        if (id is 14 or 15)
        {
            return MaterialAutoTileModes.LinePath;
        }

        if (id.HasValue)
        {
            return MaterialAutoTileModes.RegionBoundary;
        }

        var text = path ?? string.Empty;
        if (ContainsAny(text, "fence", "wall"))
        {
            return MaterialAutoTileModes.LinePath;
        }

        return MaterialAutoTileModes.RegionBoundary;
    }

    public static string NormalizeAutoTileMode(string? mode)
    {
        mode = mode?.Trim() ?? string.Empty;
        return mode switch
        {
            MaterialAutoTileModes.LinePath => MaterialAutoTileModes.LinePath,
            MaterialAutoTileModes.RegionBoundary => MaterialAutoTileModes.RegionBoundary,
            MaterialAutoTileModes.Mask => MaterialAutoTileModes.LinePath,
            MaterialAutoTileModes.Default => MaterialAutoTileModes.Default,
            _ => MaterialAutoTileModes.RegionBoundary
        };
    }

    private static bool ContainsAny(string text, params string[] values)
        => values.Any(value => text.Contains(value, StringComparison.CurrentCultureIgnoreCase));

    private static int? TryGetTypedMaterialId(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        foreach (var part in path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
        {
            var text = part.AsSpan().Trim();
            var digitCount = 0;
            while (digitCount < text.Length && char.IsDigit(text[digitCount]))
            {
                digitCount++;
            }

            if (digitCount == 0) continue;
            var separatorIndex = digitCount;
            while (separatorIndex < text.Length && char.IsWhiteSpace(text[separatorIndex]))
            {
                separatorIndex++;
            }

            if (separatorIndex >= text.Length ||
                (text[separatorIndex] != ':' && text[separatorIndex] != '：'))
            {
                continue;
            }

            if (int.TryParse(text[..digitCount], out var id))
            {
                return id;
            }
        }

        return null;
    }

    private static bool IsImageFile(string path)
        => ImageExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);

    private static int SortKey(string name)
        => int.TryParse(name, out var value) ? value : int.MaxValue;
}
