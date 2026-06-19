using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Globalization;
using CCZModStudio.Models;

namespace CCZModStudio.Core;

public sealed class TerrainDrivenMapGenerationService
{
    private readonly Dictionary<string, CachedImage> _imageCache = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyList<MapCellOverride> GenerateMapCells(MapWorkbenchDraft draft, IReadOnlyList<MaterialAsset> materials)
    {
        if (!CanGenerate(draft)) return Array.Empty<MapCellOverride>();

        var terrainAssets = BuildTerrainAssets(draft, materials);
        var result = new List<MapCellOverride>(draft.CellCount);
        for (var index = 0; index < draft.CellCount; index++)
        {
            var terrainId = draft.TerrainCells[index];
            if (!terrainAssets.TryGetValue(terrainId, out var candidates) || candidates.Count == 0) continue;

            var kind = GetAdjacencyKind(draft, index, terrainId);
            var candidate = SelectStableCandidate(candidates, draft, index, terrainId, kind);
            result.Add(new MapCellOverride
            {
                Index = index,
                MaterialRelativePath = MapDraftService.GetMaterialRelativePath(draft.MaterialRoot, candidate.FilePath),
                MaterialCategory = candidate.Category,
                DisplayName = candidate.FileName,
                Source = MapCellOverrideSources.Generated
            });
        }

        return result;
    }

    public Bitmap RenderBaseTerrain(MapWorkbenchDraft draft, IReadOnlyList<MaterialAsset> materials)
    {
        var tileSize = draft.TileSize <= 0 ? MapResourceItem.MapTilePixelSize : draft.TileSize;
        var bitmap = new Bitmap(draft.GridWidth * tileSize, draft.GridHeight * tileSize, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bitmap);
        g.SmoothingMode = SmoothingMode.None;
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.PixelOffsetMode = PixelOffsetMode.Half;

        if (!CanGenerate(draft))
        {
            g.Clear(Color.Transparent);
            return bitmap;
        }

        var terrainAssets = BuildTerrainAssets(draft, materials);
        for (var y = 0; y < draft.GridHeight; y++)
        {
            for (var x = 0; x < draft.GridWidth; x++)
            {
                var index = y * draft.GridWidth + x;
                var terrainId = draft.TerrainCells[index];
                var rect = new Rectangle(x * tileSize, y * tileSize, tileSize, tileSize);
                if (terrainAssets.TryGetValue(terrainId, out var candidates) && candidates.Count > 0)
                {
                    var kind = GetAdjacencyKind(draft, index, terrainId);
                    var candidate = SelectStableCandidate(candidates, draft, index, terrainId, kind);
                    var image = GetCachedImage(candidate.FilePath);
                    if (image != null)
                    {
                        DrawVariant(g, image, rect, draft, index, terrainId);
                        continue;
                    }
                }

                using var brush = new SolidBrush(HexzmapTerrainRenderService.GetTerrainColor(terrainId));
                g.FillRectangle(brush, rect);
            }
        }

        return bitmap;
    }

    public void DisposeCachedImages()
    {
        foreach (var cached in _imageCache.Values)
        {
            cached.Bitmap.Dispose();
        }

        _imageCache.Clear();
    }

    private static bool CanGenerate(MapWorkbenchDraft draft)
        => draft.AutoGenerateMapFromTerrain &&
           draft.GridWidth > 0 &&
           draft.GridHeight > 0 &&
           draft.TerrainCells.Length == draft.CellCount;

    private static Dictionary<byte, List<MaterialAsset>> BuildTerrainAssets(MapWorkbenchDraft draft, IReadOnlyList<MaterialAsset> materials)
    {
        var result = new Dictionary<byte, List<MaterialAsset>>();
        foreach (var material in materials)
        {
            if (!material.Category.Equals("地形", StringComparison.CurrentCultureIgnoreCase) &&
                !material.Category.Contains("地形", StringComparison.CurrentCultureIgnoreCase))
            {
                continue;
            }

            if (!byte.TryParse(material.HexTag.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var id))
            {
                continue;
            }

            if (!result.TryGetValue(id, out var list))
            {
                list = new List<MaterialAsset>();
                result[id] = list;
            }

            list.Add(material);
        }

        foreach (var pair in result)
        {
            pair.Value.Sort((left, right) => CompareMaterialAssets(left, right));
        }

        return result;
    }

    private static int CompareMaterialAssets(MaterialAsset left, MaterialAsset right)
    {
        var leftNumber = ParseLeadingNumber(left.FileName);
        var rightNumber = ParseLeadingNumber(right.FileName);
        var numberCompare = leftNumber.CompareTo(rightNumber);
        return numberCompare != 0
            ? numberCompare
            : string.Compare(left.FileName, right.FileName, StringComparison.CurrentCultureIgnoreCase);
    }

    private static int ParseLeadingNumber(string fileName)
        => int.TryParse(Path.GetFileNameWithoutExtension(fileName), NumberStyles.Integer, CultureInfo.InvariantCulture, out var number)
            ? number
            : int.MaxValue;

    private static MaterialAsset SelectStableCandidate(IReadOnlyList<MaterialAsset> candidates, MapWorkbenchDraft draft, int index, byte terrainId, TerrainAdjacencyKind kind)
    {
        if (candidates.Count == 1) return candidates[0];
        var preferred = FilterByAdjacency(candidates, kind);
        var hash = StableHash(draft.DraftId, index, terrainId, 0x9E3779B9u ^ (uint)kind);
        var selected = (int)(hash % (uint)preferred.Count);
        return preferred[selected];
    }

    private static IReadOnlyList<MaterialAsset> FilterByAdjacency(IReadOnlyList<MaterialAsset> candidates, TerrainAdjacencyKind kind)
    {
        var exact = candidates.Where(asset => MatchesAdjacency(asset, kind)).ToList();
        if (exact.Count > 0) return exact;

        if (kind != TerrainAdjacencyKind.Normal)
        {
            var transition = candidates
                .Where(asset => ContainsAny(asset, "transition", "blend", "mix", "过渡", "融合", "混合", "边", "岸", "沿"))
                .ToList();
            if (transition.Count > 0) return transition;
        }

        var ordinary = candidates
            .Where(asset => !ContainsAny(asset, "edge", "border", "corner", "transition", "blend", "mix", "边", "岸", "沿", "角", "过渡", "融合", "混合"))
            .ToList();
        return ordinary.Count > 0 ? ordinary : candidates;
    }

    private static bool MatchesAdjacency(MaterialAsset asset, TerrainAdjacencyKind kind)
        => kind switch
        {
            TerrainAdjacencyKind.Edge => ContainsAny(asset, "edge", "border", "side", "边", "岸", "沿"),
            TerrainAdjacencyKind.Corner => ContainsAny(asset, "corner", "inner", "outer", "角", "转角", "内角", "外角"),
            TerrainAdjacencyKind.Transition => ContainsAny(asset, "transition", "blend", "mix", "过渡", "融合", "混合"),
            _ => !ContainsAny(asset, "edge", "border", "corner", "transition", "blend", "mix", "边", "岸", "沿", "角", "过渡", "融合", "混合")
        };

    private static bool ContainsAny(MaterialAsset asset, params string[] tokens)
    {
        var text = asset.FileName + " " + asset.Description;
        return tokens.Any(token => text.Contains(token, StringComparison.CurrentCultureIgnoreCase));
    }

    private static TerrainAdjacencyKind GetAdjacencyKind(MapWorkbenchDraft draft, int index, byte terrainId)
    {
        var x = index % draft.GridWidth;
        var y = index / draft.GridWidth;
        var left = IsDifferentTerrain(draft, x - 1, y, terrainId);
        var right = IsDifferentTerrain(draft, x + 1, y, terrainId);
        var top = IsDifferentTerrain(draft, x, y - 1, terrainId);
        var bottom = IsDifferentTerrain(draft, x, y + 1, terrainId);
        var cardinal = (left ? 1 : 0) + (right ? 1 : 0) + (top ? 1 : 0) + (bottom ? 1 : 0);
        if (cardinal == 0)
        {
            return HasDifferentDiagonal(draft, x, y, terrainId)
                ? TerrainAdjacencyKind.Corner
                : TerrainAdjacencyKind.Normal;
        }

        if (cardinal == 1) return TerrainAdjacencyKind.Edge;
        var adjacentPair = (left || right) && (top || bottom);
        return adjacentPair ? TerrainAdjacencyKind.Corner : TerrainAdjacencyKind.Transition;
    }

    private static bool HasDifferentDiagonal(MapWorkbenchDraft draft, int x, int y, byte terrainId)
        => IsDifferentTerrain(draft, x - 1, y - 1, terrainId) ||
           IsDifferentTerrain(draft, x + 1, y - 1, terrainId) ||
           IsDifferentTerrain(draft, x - 1, y + 1, terrainId) ||
           IsDifferentTerrain(draft, x + 1, y + 1, terrainId);

    private static bool IsDifferentTerrain(MapWorkbenchDraft draft, int x, int y, byte terrainId)
    {
        if (x < 0 || y < 0 || x >= draft.GridWidth || y >= draft.GridHeight) return false;
        return draft.TerrainCells[y * draft.GridWidth + x] != terrainId;
    }

    private Bitmap? GetCachedImage(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return null;

        var fullPath = Path.GetFullPath(path);
        var info = new FileInfo(fullPath);
        if (_imageCache.TryGetValue(fullPath, out var cached) &&
            cached.LastWriteUtc == info.LastWriteTimeUtc &&
            cached.Length == info.Length)
        {
            return cached.Bitmap;
        }

        using var source = Image.FromFile(fullPath);
        var bitmap = new Bitmap(source);
        if (_imageCache.TryGetValue(fullPath, out cached))
        {
            cached.Bitmap.Dispose();
        }

        _imageCache[fullPath] = new CachedImage(info.LastWriteTimeUtc, info.Length, bitmap);
        return bitmap;
    }

    private static void DrawVariant(Graphics g, Image source, Rectangle target, MapWorkbenchDraft draft, int index, byte terrainId)
    {
        var hash = StableHash(draft.DraftId, index, terrainId, 0x85EBCA6Bu);
        var flip = (hash & 1) != 0;
        using var variant = new Bitmap(source);
        if (flip)
        {
            variant.RotateFlip(RotateFlipType.RotateNoneFlipX);
        }

        g.DrawImage(variant, target);
        var tint = (int)((hash >> 3) & 0x0F) - 7;
        if (tint == 0) return;

        var alpha = Math.Min(18, Math.Abs(tint) * 2);
        using var brush = new SolidBrush(tint > 0
            ? Color.FromArgb(alpha, Color.White)
            : Color.FromArgb(alpha, Color.Black));
        g.FillRectangle(brush, target);
    }

    private static uint StableHash(string? seed, int index, byte terrainId, uint salt)
    {
        unchecked
        {
            var hash = 2166136261u ^ salt;
            foreach (var ch in seed ?? string.Empty)
            {
                hash ^= ch;
                hash *= 16777619u;
            }

            hash ^= (uint)index;
            hash *= 16777619u;
            hash ^= terrainId;
            hash *= 16777619u;
            return hash;
        }
    }

    private enum TerrainAdjacencyKind
    {
        Normal,
        Edge,
        Corner,
        Transition
    }

    private sealed record CachedImage(DateTime LastWriteUtc, long Length, Bitmap Bitmap);
}
