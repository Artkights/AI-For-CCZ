using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using CCZModStudio.Formats;
using CCZModStudio.Models;

namespace CCZModStudio.Core;

public sealed class MaterialDrivenTerrainService : IDisposable
{
    private readonly Dictionary<string, CachedImage> _imageCache = new(StringComparer.OrdinalIgnoreCase);

    public byte[] DeriveTerrainCells(MapWorkbenchDraft draft, IReadOnlyList<MaterialAsset> materials)
    {
        var cellCount = Math.Max(0, draft.CellCount);
        var result = new byte[cellCount];
        if (draft.OriginalTerrainCells.Length == cellCount)
        {
            Array.Copy(draft.OriginalTerrainCells, result, cellCount);
        }
        else if (draft.TerrainCells.Length == cellCount)
        {
            Array.Copy(draft.TerrainCells, result, cellCount);
        }

        var materialLookup = BuildAssetLookup(draft, materials);
        ApplyTerrainLayer(result, draft.TerrainBaseCells, materialLookup);
        ApplyTerrainLayer(result, draft.BuildingOverlayCells, materialLookup);
        return result;
    }

    public TerrainGenerationDiagnostics Analyze(MapWorkbenchDraft draft, IReadOnlyList<MaterialAsset> materials)
    {
        if (draft.GridWidth <= 0 || draft.GridHeight <= 0)
        {
            return new TerrainGenerationDiagnostics(false, materials.Count, 0, 0, 0, Array.Empty<byte>(), Array.Empty<byte>());
        }

        var materialLookup = BuildAssetLookup(draft, materials);
        var terrainIds = new HashSet<byte>();
        var missingIds = new HashSet<byte>();
        var matched = 0;
        var fallback = 0;
        foreach (var cell in draft.TerrainBaseCells.Concat(draft.BuildingOverlayCells))
        {
            if (materialLookup.TryGetValue(cell.MaterialRelativePath, out var asset) && asset.TerrainId.HasValue)
            {
                matched++;
                terrainIds.Add(asset.TerrainId.Value);
            }
            else
            {
                fallback++;
                if (MaterialHexTagParser.TryParseTerrainId(cell.MaterialCategory, out var id))
                {
                    missingIds.Add(id);
                }
            }
        }

        return new TerrainGenerationDiagnostics(
            true,
            materials.Count,
            materials.Count(asset => asset.TerrainId.HasValue),
            matched,
            fallback,
            missingIds.OrderBy(id => id).ToArray(),
            terrainIds.OrderBy(id => id).ToArray());
    }

    public Bitmap ComposeBaseTerrain(MapWorkbenchDraft draft, IReadOnlyList<MaterialAsset> materials, bool checkerboardBlank)
    {
        ValidateDraft(draft);
        var tileSize = draft.TileSize <= 0 ? MapResourceItem.MapTilePixelSize : draft.TileSize;
        var bitmap = new Bitmap(draft.GridWidth * tileSize, draft.GridHeight * tileSize, PixelFormat.Format32bppArgb);
        using var g = CreateGraphics(bitmap);
        DrawBlank(g, bitmap.Width, bitmap.Height, checkerboardBlank);
        DrawBaseLayer(g, draft, bitmap.Width, bitmap.Height, checkerboardBlank);
        var materialLookup = BuildAssetLookup(draft, materials);

        var terrainByIndex = draft.TerrainBaseCells
            .Where(cell => cell.Index >= 0 && cell.Index < draft.CellCount)
            .GroupBy(cell => cell.Index)
            .ToDictionary(group => group.Key, group => group.Last());
        for (var index = 0; index < draft.CellCount; index++)
        {
            if (terrainByIndex.TryGetValue(index, out var cell) &&
                TryGetAsset(materialLookup, cell, out var asset))
            {
                DrawMaterialAsset(g, asset, GetTileRectangle(draft, index));
            }
        }

        return bitmap;
    }

    public Bitmap ComposeVisualMap(MapWorkbenchDraft draft, IReadOnlyList<MaterialAsset> materials, bool checkerboardBlank, bool beautifyTerrain)
    {
        ValidateDraft(draft);
        var tileSize = draft.TileSize <= 0 ? MapResourceItem.MapTilePixelSize : draft.TileSize;
        var bitmap = new Bitmap(draft.GridWidth * tileSize, draft.GridHeight * tileSize, PixelFormat.Format32bppArgb);
        using var g = CreateGraphics(bitmap);
        DrawBlank(g, bitmap.Width, bitmap.Height, checkerboardBlank);

        using var terrain = ComposeBaseTerrain(draft, materials, checkerboardBlank: false);
        if (beautifyTerrain)
        {
            using var beautified = new TerrainMapBeautifyService().Beautify(draft, terrain);
            g.DrawImage(beautified, new Rectangle(0, 0, bitmap.Width, bitmap.Height));
        }
        else
        {
            g.DrawImage(terrain, new Rectangle(0, 0, bitmap.Width, bitmap.Height));
        }

        DrawOverlays(g, draft, materials, drawScenery: true);
        return bitmap;
    }

    public void DrawCell(Graphics g, MapWorkbenchDraft draft, IReadOnlyList<MaterialAsset> materials, int index, bool includeTerrain, bool includeBuilding, bool includeScenery)
    {
        if (index < 0 || index >= draft.CellCount) return;
        var materialLookup = BuildAssetLookup(draft, materials);
        if (includeTerrain)
        {
            var baseCell = draft.TerrainBaseCells.LastOrDefault(cell => cell.Index == index);
            if (baseCell != null && TryGetAsset(materialLookup, baseCell, out var asset))
            {
                DrawMaterialAsset(g, asset, GetTileRectangle(draft, index));
            }
            else
            {
                DrawBaseCell(g, draft, index);
            }
        }

        if (includeBuilding)
        {
            var building = draft.BuildingOverlayCells.LastOrDefault(cell => cell.Index == index);
            if (building != null)
            {
                DrawOverlayCell(g, draft, building, materialLookup, materials, draft.BuildingOverlayCells);
            }
        }

        if (includeScenery)
        {
            var scenery = draft.SceneryOverlayCells.LastOrDefault(cell => cell.Index == index);
            if (scenery != null)
            {
                DrawOverlayCell(g, draft, scenery, materialLookup, materials, draft.SceneryOverlayCells);
            }
        }
    }

    public void DrawOverlays(Graphics g, MapWorkbenchDraft draft, IReadOnlyList<MaterialAsset> materials, bool drawScenery)
    {
        var materialLookup = BuildAssetLookup(draft, materials);
        foreach (var cell in draft.BuildingOverlayCells.OrderBy(cell => cell.Index))
        {
            DrawOverlayCell(g, draft, cell, materialLookup, materials, draft.BuildingOverlayCells);
        }

        if (!drawScenery) return;
        foreach (var cell in draft.SceneryOverlayCells.OrderBy(cell => cell.Index))
        {
            DrawOverlayCell(g, draft, cell, materialLookup, materials, draft.SceneryOverlayCells);
        }
    }

    public void Dispose()
    {
        foreach (var cached in _imageCache.Values)
        {
            cached.Bitmap.Dispose();
        }

        _imageCache.Clear();
    }

    private static void ApplyTerrainLayer(byte[] target, IEnumerable<MapCellOverride> cells, IReadOnlyDictionary<string, MaterialAsset> materialLookup)
    {
        foreach (var cell in cells.OrderBy(cell => cell.Index))
        {
            if ((uint)cell.Index >= (uint)target.Length) continue;
            if (materialLookup.TryGetValue(cell.MaterialRelativePath, out var asset) && asset.TerrainId.HasValue)
            {
                target[cell.Index] = asset.TerrainId.Value;
            }
        }
    }

    private void DrawOverlayCell(
        Graphics g,
        MapWorkbenchDraft draft,
        MapCellOverride cell,
        IReadOnlyDictionary<string, MaterialAsset> materialLookup,
        IReadOnlyList<MaterialAsset> materials,
        IReadOnlyList<MapCellOverride> layerCells)
    {
        if (cell.Index < 0 || cell.Index >= draft.CellCount) return;
        if (!TryGetAsset(materialLookup, cell, out var asset)) return;
        if (asset.AssetType.Equals(MaterialAssetTypes.Building, StringComparison.OrdinalIgnoreCase))
        {
            asset = SelectAutoTileAsset(draft, asset, cell.Index, materialLookup, materials, layerCells);
        }

        DrawMaterialAsset(g, asset, GetTileRectangle(draft, cell.Index));
    }

    private MaterialAsset SelectAutoTileAsset(
        MapWorkbenchDraft draft,
        MaterialAsset current,
        int index,
        IReadOnlyDictionary<string, MaterialAsset> materialLookup,
        IReadOnlyList<MaterialAsset> materials,
        IReadOnlyList<MapCellOverride> layerCells)
    {
        if (string.IsNullOrWhiteSpace(current.GroupKey)) return current;
        var autoTileSetKey = string.IsNullOrWhiteSpace(current.AutoTileSetKey)
            ? $"{current.GroupKey}:{current.FileName}"
            : current.AutoTileSetKey;
        var groupAssets = materials
            .Where(asset => asset.GroupKey.Equals(current.GroupKey, StringComparison.OrdinalIgnoreCase))
            .Where(asset => (string.IsNullOrWhiteSpace(asset.AutoTileSetKey)
                    ? $"{asset.GroupKey}:{asset.FileName}"
                    : asset.AutoTileSetKey)
                .Equals(autoTileSetKey, StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (groupAssets.Count <= 1) return current;

        var mask = BuildConnectionMask(draft, current, index, materialLookup, layerCells);
        return SelectAutoTileAssetForMask(groupAssets, mask) ?? current;
    }

    private static int GetAssetMask(MaterialAsset asset)
        => asset.AutoTileMask ?? MaterialLibraryIndexer.RoleToMask(asset.AutoTileRole);

    private static MaterialAsset? SelectAutoTileAssetForMask(IReadOnlyList<MaterialAsset> assets, int mask)
    {
        var normalized = NormalizeEightWayMask(mask);
        foreach (var candidateMask in BuildAutoTileMaskFallbacks(normalized))
        {
            var exact = SelectAutoTileAssetByMask(assets, candidateMask);
            if (exact != null) return exact;
        }

        return SelectAutoTileDefaultAsset(assets);
    }

    private static MaterialAsset? SelectAutoTileAssetByMask(IEnumerable<MaterialAsset> assets, int mask)
        => assets
            .Where(asset => NormalizeEightWayMask(GetAssetMask(asset)) == mask)
            .OrderBy(asset => asset.AutoTilePriority)
            .ThenBy(asset => asset.VariantIndex)
            .FirstOrDefault();

    private static MaterialAsset? SelectAutoTileDefaultAsset(IEnumerable<MaterialAsset> assets)
        => assets
               .Where(asset => GetAssetMask(asset) == MaterialAutoTileMasks.None)
               .OrderBy(asset => asset.AutoTilePriority)
               .ThenBy(asset => asset.VariantIndex)
               .FirstOrDefault() ??
           assets
               .OrderBy(asset => asset.AutoTilePriority)
               .ThenBy(asset => asset.VariantIndex)
               .FirstOrDefault();

    private static IEnumerable<int> BuildAutoTileMaskFallbacks(int mask)
    {
        yield return mask;

        var cardinal = mask & MaterialAutoTileMasks.CardinalMask;
        if (cardinal != mask)
        {
            yield return cardinal;
        }

        if (cardinal == MaterialAutoTileMasks.Cross)
        {
            foreach (var innerCornerMask in BuildMissingDiagonalInnerCornerMasks(mask))
            {
                yield return innerCornerMask;
            }

            yield return MaterialAutoTileMasks.Cross;
        }

        if (BitCount(cardinal) == 3)
        {
            yield return cardinal switch
            {
                MaterialAutoTileMasks.TeeN => MaterialAutoTileMasks.StraightH,
                MaterialAutoTileMasks.TeeS => MaterialAutoTileMasks.StraightH,
                MaterialAutoTileMasks.TeeE => MaterialAutoTileMasks.StraightV,
                MaterialAutoTileMasks.TeeW => MaterialAutoTileMasks.StraightV,
                _ => MaterialAutoTileMasks.None
            };
        }
        else if (BitCount(cardinal) == 2)
        {
            if (cardinal is MaterialAutoTileMasks.CornerNE or MaterialAutoTileMasks.CornerNW)
            {
                yield return MaterialAutoTileMasks.North;
            }
            else if (cardinal is MaterialAutoTileMasks.CornerSE or MaterialAutoTileMasks.CornerSW)
            {
                yield return MaterialAutoTileMasks.South;
            }

            if (cardinal is MaterialAutoTileMasks.CornerNE or MaterialAutoTileMasks.CornerSE)
            {
                yield return MaterialAutoTileMasks.East;
            }
            else if (cardinal is MaterialAutoTileMasks.CornerNW or MaterialAutoTileMasks.CornerSW)
            {
                yield return MaterialAutoTileMasks.West;
            }
        }
        else if (BitCount(cardinal) == 1)
        {
            yield return cardinal switch
            {
                MaterialAutoTileMasks.North or MaterialAutoTileMasks.South => MaterialAutoTileMasks.StraightV,
                MaterialAutoTileMasks.East or MaterialAutoTileMasks.West => MaterialAutoTileMasks.StraightH,
                _ => MaterialAutoTileMasks.None
            };
        }

        yield return MaterialAutoTileMasks.None;
    }

    private static int NormalizeEightWayMask(int mask)
    {
        var cardinal = mask & MaterialAutoTileMasks.CardinalMask;
        var diagonal = mask & MaterialAutoTileMasks.DiagonalMask;
        var normalizedDiagonal = 0;
        if (HasAll(cardinal, MaterialAutoTileMasks.North | MaterialAutoTileMasks.East) &&
            HasDiagonal(diagonal, MaterialAutoTileMasks.NorthEast))
        {
            normalizedDiagonal |= MaterialAutoTileMasks.NorthEast;
        }

        if (HasAll(cardinal, MaterialAutoTileMasks.South | MaterialAutoTileMasks.East) &&
            HasDiagonal(diagonal, MaterialAutoTileMasks.SouthEast))
        {
            normalizedDiagonal |= MaterialAutoTileMasks.SouthEast;
        }

        if (HasAll(cardinal, MaterialAutoTileMasks.South | MaterialAutoTileMasks.West) &&
            HasDiagonal(diagonal, MaterialAutoTileMasks.SouthWest))
        {
            normalizedDiagonal |= MaterialAutoTileMasks.SouthWest;
        }

        if (HasAll(cardinal, MaterialAutoTileMasks.North | MaterialAutoTileMasks.West) &&
            HasDiagonal(diagonal, MaterialAutoTileMasks.NorthWest))
        {
            normalizedDiagonal |= MaterialAutoTileMasks.NorthWest;
        }

        return cardinal | normalizedDiagonal;
    }

    private static IEnumerable<int> BuildMissingDiagonalInnerCornerMasks(int mask)
    {
        var filled = MaterialAutoTileMasks.Filled;
        if (!HasDiagonal(mask, MaterialAutoTileMasks.NorthEast)) yield return filled & ~MaterialAutoTileMasks.NorthEast;
        if (!HasDiagonal(mask, MaterialAutoTileMasks.SouthEast)) yield return filled & ~MaterialAutoTileMasks.SouthEast;
        if (!HasDiagonal(mask, MaterialAutoTileMasks.SouthWest)) yield return filled & ~MaterialAutoTileMasks.SouthWest;
        if (!HasDiagonal(mask, MaterialAutoTileMasks.NorthWest)) yield return filled & ~MaterialAutoTileMasks.NorthWest;
    }

    private static int BuildConnectionMask(
        MapWorkbenchDraft draft,
        MaterialAsset current,
        int index,
        IReadOnlyDictionary<string, MaterialAsset> materialLookup,
        IReadOnlyList<MapCellOverride> layerCells)
    {
        var x = index % draft.GridWidth;
        var y = index / draft.GridWidth;
        var mask = 0;
        if (IsSameGroupAt(draft, current, x, y - 1, materialLookup, layerCells)) mask |= 1;
        if (IsSameGroupAt(draft, current, x + 1, y, materialLookup, layerCells)) mask |= 2;
        if (IsSameGroupAt(draft, current, x, y + 1, materialLookup, layerCells)) mask |= 4;
        if (IsSameGroupAt(draft, current, x - 1, y, materialLookup, layerCells)) mask |= 8;
        if (HasAll(mask, MaterialAutoTileMasks.North | MaterialAutoTileMasks.East) &&
            IsSameGroupAt(draft, current, x + 1, y - 1, materialLookup, layerCells))
        {
            mask |= MaterialAutoTileMasks.NorthEast;
        }

        if (HasAll(mask, MaterialAutoTileMasks.South | MaterialAutoTileMasks.East) &&
            IsSameGroupAt(draft, current, x + 1, y + 1, materialLookup, layerCells))
        {
            mask |= MaterialAutoTileMasks.SouthEast;
        }

        if (HasAll(mask, MaterialAutoTileMasks.South | MaterialAutoTileMasks.West) &&
            IsSameGroupAt(draft, current, x - 1, y + 1, materialLookup, layerCells))
        {
            mask |= MaterialAutoTileMasks.SouthWest;
        }

        if (HasAll(mask, MaterialAutoTileMasks.North | MaterialAutoTileMasks.West) &&
            IsSameGroupAt(draft, current, x - 1, y - 1, materialLookup, layerCells))
        {
            mask |= MaterialAutoTileMasks.NorthWest;
        }

        return mask;
    }

    private static bool HasAll(int mask, int bits)
        => (mask & bits) == bits;

    private static bool HasDiagonal(int mask, int diagonal)
        => (mask & diagonal) == diagonal;

    private static int BitCount(int value)
    {
        value &= MaterialAutoTileMasks.CardinalMask;
        var count = 0;
        while (value != 0)
        {
            count += value & 1;
            value >>= 1;
        }

        return count;
    }

    private static bool IsSameGroupAt(
        MapWorkbenchDraft draft,
        MaterialAsset current,
        int x,
        int y,
        IReadOnlyDictionary<string, MaterialAsset> materialLookup,
        IReadOnlyList<MapCellOverride> layerCells)
    {
        if (x < 0 || y < 0 || x >= draft.GridWidth || y >= draft.GridHeight) return false;
        var index = y * draft.GridWidth + x;
        var cell = layerCells.LastOrDefault(c => c.Index == index);
        return cell != null &&
               materialLookup.TryGetValue(cell.MaterialRelativePath, out var asset) &&
               asset.GroupKey.Equals(current.GroupKey, StringComparison.OrdinalIgnoreCase);
    }

    private void DrawMaterialAsset(Graphics g, MaterialAsset asset, Rectangle target)
    {
        var source = GetCachedImage(asset.FilePath);
        if (source == null) return;
        var sourceRect = BuildSourceRect(asset, source);
        g.DrawImage(source, target, sourceRect, GraphicsUnit.Pixel);
    }

    private void DrawBaseLayer(Graphics g, MapWorkbenchDraft draft, int width, int height, bool checkerboardBlank)
    {
        var baseImage = GetCachedImage(draft.BaseLayerPath);
        if (baseImage != null)
        {
            g.DrawImage(baseImage, new Rectangle(0, 0, width, height), new Rectangle(0, 0, Math.Min(width, baseImage.Width), Math.Min(height, baseImage.Height)), GraphicsUnit.Pixel);
            return;
        }

        if (!checkerboardBlank) return;
        using var brush = new SolidBrush(Color.FromArgb(42, 42, 42));
        g.FillRectangle(brush, new Rectangle(0, 0, width, height));
    }

    private void DrawBaseCell(Graphics g, MapWorkbenchDraft draft, int index)
    {
        var rect = GetTileRectangle(draft, index);
        var baseImage = GetCachedImage(draft.BaseLayerPath);
        if (baseImage != null)
        {
            var sourceRect = new Rectangle(
                Math.Clamp(rect.X, 0, Math.Max(0, baseImage.Width - 1)),
                Math.Clamp(rect.Y, 0, Math.Max(0, baseImage.Height - 1)),
                Math.Min(rect.Width, Math.Max(1, baseImage.Width - rect.X)),
                Math.Min(rect.Height, Math.Max(1, baseImage.Height - rect.Y)));
            g.DrawImage(baseImage, rect, sourceRect, GraphicsUnit.Pixel);
            return;
        }

        using var brush = new SolidBrush(Color.FromArgb(42, 42, 42));
        g.FillRectangle(brush, rect);
    }

    private static Rectangle BuildSourceRect(MaterialAsset asset, Image image)
    {
        var x = Math.Clamp(asset.SourceX, 0, Math.Max(0, image.Width - 1));
        var y = Math.Clamp(asset.SourceY, 0, Math.Max(0, image.Height - 1));
        var width = asset.SourceWidth <= 0 ? Math.Min(MapResourceItem.MapTilePixelSize, image.Width - x) : asset.SourceWidth;
        var height = asset.SourceHeight <= 0 ? Math.Min(MapResourceItem.MapTilePixelSize, image.Height - y) : asset.SourceHeight;
        width = Math.Clamp(width, 1, Math.Max(1, image.Width - x));
        height = Math.Clamp(height, 1, Math.Max(1, image.Height - y));
        return new Rectangle(x, y, width, height);
    }

    private bool TryGetAsset(IReadOnlyDictionary<string, MaterialAsset> materialLookup, MapCellOverride cell, out MaterialAsset asset)
        => materialLookup.TryGetValue(cell.MaterialRelativePath, out asset!);

    private static Dictionary<string, MaterialAsset> BuildAssetLookup(MapWorkbenchDraft draft, IReadOnlyList<MaterialAsset> materials)
    {
        var result = new Dictionary<string, MaterialAsset>(StringComparer.OrdinalIgnoreCase);
        foreach (var asset in materials
                     .OrderBy(asset => asset.AutoTileRole.Equals(MaterialAutoTileRoles.Default, StringComparison.OrdinalIgnoreCase) ? 0 : 1)
                     .ThenBy(asset => asset.AutoTilePriority)
                     .ThenBy(asset => asset.VariantIndex))
        {
            var relative = MapDraftService.GetMaterialRelativePath(draft.MaterialRoot, asset.FilePath);
            result.TryAdd(relative, asset);
            result.TryAdd(asset.FilePath, asset);
        }

        return result;
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

    private static Graphics CreateGraphics(Bitmap bitmap)
    {
        var g = Graphics.FromImage(bitmap);
        g.SmoothingMode = SmoothingMode.None;
        g.InterpolationMode = InterpolationMode.NearestNeighbor;
        g.PixelOffsetMode = PixelOffsetMode.Half;
        return g;
    }

    private static void DrawBlank(Graphics g, int width, int height, bool checkerboardBlank)
    {
        if (!checkerboardBlank)
        {
            g.Clear(Color.Transparent);
            return;
        }

        const int size = 24;
        using var light = new SolidBrush(Color.FromArgb(62, 62, 62));
        using var dark = new SolidBrush(Color.FromArgb(38, 38, 38));
        for (var y = 0; y < height; y += size)
        {
            for (var x = 0; x < width; x += size)
            {
                var brush = ((x / size) + (y / size)) % 2 == 0 ? light : dark;
                g.FillRectangle(brush, x, y, Math.Min(size, width - x), Math.Min(size, height - y));
            }
        }
    }

    private static Rectangle GetTileRectangle(MapWorkbenchDraft draft, int index)
    {
        var tileSize = draft.TileSize <= 0 ? MapResourceItem.MapTilePixelSize : draft.TileSize;
        var x = index % draft.GridWidth;
        var y = index / draft.GridWidth;
        return new Rectangle(x * tileSize, y * tileSize, tileSize, tileSize);
    }

    private static void ValidateDraft(MapWorkbenchDraft draft)
    {
        if (draft.GridWidth <= 0 || draft.GridHeight <= 0)
        {
            throw new InvalidOperationException("Map draft grid size is invalid.");
        }
    }

    private sealed record CachedImage(DateTime LastWriteUtc, long Length, Bitmap Bitmap);
}
