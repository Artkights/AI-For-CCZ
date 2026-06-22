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

        var variants = images
            .SelectMany(BuildVariantsForImage)
            .ToList();
        if (variants.Count == 0) return 0;

        File.WriteAllText(outputPath, JsonSerializer.Serialize(variants, JsonOptions));
        return variants.Count;
    }

    public IReadOnlyList<MaterialAutoTileVariant> BuildVariantsForImage(string imagePath)
    {
        if (string.IsNullOrWhiteSpace(imagePath) || !File.Exists(imagePath)) return Array.Empty<MaterialAutoTileVariant>();

        var fileName = Path.GetFileName(imagePath);
        using var image = Image.FromFile(imagePath);
        var tile = MapResourceItem.MapTilePixelSize;
        var columns = Math.Max(1, image.Width / tile);
        var rows = Math.Max(1, image.Height / tile);

        if (image.Width >= tile * 15 && image.Height <= tile)
        {
            return BuildCanonicalStripVariants(fileName, columns, image.Width, image.Height);
        }

        return new[] { BuildVariant(fileName, MaterialAutoTileRoles.Default, MaterialAutoTileMasks.None, MaterialAutoTileModes.Default, 0, 0, 0, image.Width, image.Height) };
    }

    private static IReadOnlyList<MaterialAutoTileVariant> BuildCanonicalStripVariants(string fileName, int columns, int imageWidth, int imageHeight)
    {
        var variants = new List<MaterialAutoTileVariant>();
        var order = GetCanonicalMaskOrder();
        var tile = MapResourceItem.MapTilePixelSize;
        for (var i = 0; i < Math.Min(order.Length, columns); i++)
        {
            var x = i * tile;
            var (role, mask) = order[i];
            variants.Add(BuildVariant(fileName, role, mask, MaterialAutoTileModes.Mask, i, x, 0, imageWidth, imageHeight));
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
            (MaterialAutoTileRoles.CornerNE, MaterialAutoTileMasks.CornerNE),
            (MaterialAutoTileRoles.CornerNW, MaterialAutoTileMasks.CornerNW),
            (MaterialAutoTileRoles.CornerSW, MaterialAutoTileMasks.CornerSW),
            (MaterialAutoTileRoles.CornerSE, MaterialAutoTileMasks.CornerSE),
            (MaterialAutoTileRoles.EndN, MaterialAutoTileMasks.North),
            (MaterialAutoTileRoles.EndE, MaterialAutoTileMasks.East),
            (MaterialAutoTileRoles.EndS, MaterialAutoTileMasks.South),
            (MaterialAutoTileRoles.EndW, MaterialAutoTileMasks.West),
            (MaterialAutoTileRoles.TeeN, MaterialAutoTileMasks.TeeN),
            (MaterialAutoTileRoles.TeeE, MaterialAutoTileMasks.TeeE),
            (MaterialAutoTileRoles.TeeS, MaterialAutoTileMasks.TeeS),
            (MaterialAutoTileRoles.TeeW, MaterialAutoTileMasks.TeeW),
            (MaterialAutoTileRoles.Cross, MaterialAutoTileMasks.Cross)
        ];

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

    private static bool IsImageFile(string path)
        => ImageExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);

    private static int SortKey(string name)
        => int.TryParse(name, out var value) ? value : int.MaxValue;

}
