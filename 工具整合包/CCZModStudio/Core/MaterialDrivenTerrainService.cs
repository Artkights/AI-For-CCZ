using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Globalization;
using CCZModStudio.Formats;
using CCZModStudio.Models;

namespace CCZModStudio.Core;

public sealed class MaterialDrivenTerrainService : IDisposable
{
    private readonly Dictionary<string, CachedImage> _imageCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly TerrainVisualSynthesisService _terrainVisualSynthesisService = new();
    private readonly ObjectContactBlendService _objectContactBlendService = new();
    private readonly ObjectAlphaRepairService _objectAlphaRepairService = new();

    public byte[] DeriveTerrainCells(MapWorkbenchDraft draft, IReadOnlyList<MaterialAsset> materials)
    {
        var cellCount = Math.Max(0, draft.CellCount);
        var result = new byte[cellCount];
        if (IsTerrainDrivenVisual(draft))
        {
            if (draft.TerrainCells.Length == cellCount)
            {
                Array.Copy(draft.TerrainCells, result, cellCount);
            }
            else if (draft.OriginalTerrainCells.Length == cellCount)
            {
                Array.Copy(draft.OriginalTerrainCells, result, cellCount);
            }

            return result;
        }

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
        if (IsTerrainDrivenVisual(draft))
        {
            return _terrainVisualSynthesisService.RenderBaseTerrain(draft, materials);
        }

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
        if (IsTerrainDrivenVisual(draft))
        {
            using var visualTerrain = _terrainVisualSynthesisService.RenderBaseTerrain(draft, materials);
            var visualBitmap = new Bitmap(visualTerrain.Width, visualTerrain.Height, PixelFormat.Format32bppArgb);
            using var visualGraphics = CreateGraphics(visualBitmap);
            visualGraphics.DrawImage(visualTerrain, new Rectangle(0, 0, visualBitmap.Width, visualBitmap.Height));
            DrawOverlays(visualGraphics, draft, materials, drawScenery: true);
            ApplyObjectContactBlend(visualBitmap, draft, materials);
            if (beautifyTerrain)
            {
                using var filtered = new TerrainMapBeautifyService().ApplyFilter(draft, visualBitmap);
                visualGraphics.DrawImage(filtered, new Rectangle(0, 0, visualBitmap.Width, visualBitmap.Height));
            }

            return visualBitmap;
        }

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
        if (beautifyTerrain)
        {
            using var filtered = new TerrainMapBeautifyService().ApplyFilter(draft, bitmap);
            g.DrawImage(filtered, new Rectangle(0, 0, bitmap.Width, bitmap.Height));
        }

        return bitmap;
    }

    private void ApplyObjectContactBlend(Bitmap bitmap, MapWorkbenchDraft draft, IReadOnlyList<MaterialAsset> materials)
    {
        if (!draft.TerrainVisualProfile.UseObjectContactBlend || draft.BuildingOverlayCells.Count == 0)
        {
            return;
        }

        var materialLookup = BuildAssetLookup(draft, materials);
        var buildingPlan = BuildUnifiedBuildingOverlayPlan(draft, materialLookup, materials);
        var items = new List<ObjectContactBlendItem>();
        foreach (var cell in draft.BuildingOverlayCells.OrderBy(cell => cell.Index))
        {
            if (cell.Index < 0 || cell.Index >= draft.CellCount) continue;
            var asset = buildingPlan.GetValueOrDefault(cell.Index);
            if (asset == null)
            {
                if (!TryGetAsset(materialLookup, cell, out asset)) continue;
                if (asset.AssetType.Equals(MaterialAssetTypes.Building, StringComparison.OrdinalIgnoreCase))
                {
                    asset = SelectAutoTileAsset(draft, asset, cell.Index, materialLookup, materials, draft.BuildingOverlayCells);
                }
            }

            var source = GetCachedImage(asset.FilePath);
            if (source == null) continue;
            items.Add(new ObjectContactBlendItem(cell.Index, asset, source));
        }

        _objectContactBlendService.Apply(bitmap, draft, draft.TerrainVisualProfile, items);
    }

    public void DrawCell(Graphics g, MapWorkbenchDraft draft, IReadOnlyList<MaterialAsset> materials, int index, bool includeTerrain, bool includeBuilding, bool includeScenery)
    {
        if (index < 0 || index >= draft.CellCount) return;
        var materialLookup = BuildAssetLookup(draft, materials);
        if (includeTerrain)
        {
            if (IsTerrainDrivenVisual(draft))
            {
                using var terrain = _terrainVisualSynthesisService.RenderBaseTerrain(draft, materials);
                var rect = GetTileRectangle(draft, index);
                g.DrawImage(terrain, rect, rect, GraphicsUnit.Pixel);
            }
            else
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
        }

        if (includeBuilding)
        {
            var building = draft.BuildingOverlayCells.LastOrDefault(cell => cell.Index == index);
            if (building != null)
            {
                var buildingPlan = BuildUnifiedBuildingOverlayPlan(draft, materialLookup, materials);
                DrawOverlayCell(g, draft, building, materialLookup, materials, draft.BuildingOverlayCells, buildingPlan.GetValueOrDefault(building.Index));
            }
        }

        if (includeScenery)
        {
            if (draft.SceneryOverlays.Count > 0)
            {
                DrawSceneryOverlaysIntersecting(g, draft, index);
            }
            else
            {
                var scenery = draft.SceneryOverlayCells.LastOrDefault(cell => cell.Index == index);
                if (scenery != null)
                {
                    DrawOverlayCell(g, draft, scenery, materialLookup, materials, draft.SceneryOverlayCells);
                }
            }
        }
    }

    public void DrawOverlays(Graphics g, MapWorkbenchDraft draft, IReadOnlyList<MaterialAsset> materials, bool drawScenery)
    {
        var materialLookup = BuildAssetLookup(draft, materials);
        var buildingPlan = BuildUnifiedBuildingOverlayPlan(draft, materialLookup, materials);
        foreach (var cell in draft.BuildingOverlayCells.OrderBy(cell => cell.Index))
        {
            DrawOverlayCell(g, draft, cell, materialLookup, materials, draft.BuildingOverlayCells, buildingPlan.GetValueOrDefault(cell.Index));
        }

        if (drawScenery)
        {
            if (draft.SceneryOverlays.Count > 0)
            {
                DrawSceneryOverlays(g, draft);
            }
            else
            {
                foreach (var cell in draft.SceneryOverlayCells.OrderBy(cell => cell.Index))
                {
                    DrawOverlayCell(g, draft, cell, materialLookup, materials, draft.SceneryOverlayCells, overrideAsset: null);
                }
            }
        }
    }

    public void Dispose()
    {
        _terrainVisualSynthesisService.Dispose();
        foreach (var cached in _imageCache.Values)
        {
            cached.Bitmap.Dispose();
        }

        _imageCache.Clear();
    }

    private static bool IsTerrainDrivenVisual(MapWorkbenchDraft draft)
        => draft.GenerationMode.Equals(MapWorkbenchGenerationModes.TerrainDrivenVisual, StringComparison.OrdinalIgnoreCase);

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
        IReadOnlyList<MapCellOverride> layerCells,
        MaterialAsset? overrideAsset = null)
    {
        if (cell.Index < 0 || cell.Index >= draft.CellCount) return;
        var hasPlannedAsset = overrideAsset != null;
        if (!hasPlannedAsset && !TryGetAsset(materialLookup, cell, out overrideAsset)) return;
        var asset = overrideAsset!;
        if (!hasPlannedAsset && asset.AssetType.Equals(MaterialAssetTypes.Building, StringComparison.OrdinalIgnoreCase))
        {
            asset = SelectAutoTileAsset(draft, asset, cell.Index, materialLookup, materials, layerCells);
        }

        DrawMaterialAsset(g, asset, GetTileRectangle(draft, cell.Index), draft.TerrainVisualProfile);
    }

    private Dictionary<int, MaterialAsset> BuildUnifiedBuildingOverlayPlan(
        MapWorkbenchDraft draft,
        IReadOnlyDictionary<string, MaterialAsset> materialLookup,
        IReadOnlyList<MaterialAsset> materials)
    {
        if (!draft.TerrainVisualProfile.UseGlobalBuildingStyle || draft.BuildingOverlayCells.Count == 0)
        {
            return new Dictionary<int, MaterialAsset>();
        }

        var entries = draft.BuildingOverlayCells
            .Select((cell, order) => (Cell: cell, Order: order, Asset: TryGetAsset(materialLookup, cell, out var asset) ? asset : null))
            .Where(item => item.Asset != null && item.Asset.AssetType.Equals(MaterialAssetTypes.Building, StringComparison.OrdinalIgnoreCase))
            .Select(item => new BuildingOverlayEntry(
                item.Cell,
                item.Order,
                item.Asset!,
                BuildBuildingFamilyKey(item.Cell, item.Asset!),
                BuildBuildingStyleKey(item.Asset!)))
            .Where(item => !string.IsNullOrWhiteSpace(item.FamilyKey))
            .ToList();
        if (entries.Count == 0)
        {
            return new Dictionary<int, MaterialAsset>();
        }

        var result = new Dictionary<int, MaterialAsset>();
        foreach (var family in entries.GroupBy(item => item.FamilyKey, StringComparer.OrdinalIgnoreCase))
        {
            var targetStyle = family
                .OrderBy(item => item.Order)
                .Last()
                .StyleKey;
            var styleAssets = materials
                .Where(asset => asset.AssetType.Equals(MaterialAssetTypes.Building, StringComparison.OrdinalIgnoreCase))
                .Where(asset => BuildBuildingFamilyKey(null, asset).Equals(family.Key, StringComparison.OrdinalIgnoreCase))
                .Where(asset => BuildBuildingStyleKey(asset).Equals(targetStyle, StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (styleAssets.Count == 0)
            {
                styleAssets = family.Select(item => item.Asset).ToList();
            }

            foreach (var entry in family)
            {
                var mask = BuildLinePathMaskForFamily(draft, entry.Cell.Index, family.Key, entries);
                var selected = TerrainAutoTileResolver.SelectAutoTileAssetForMask(styleAssets, mask) ??
                               styleAssets.OrderBy(asset => asset.AutoTilePriority).ThenBy(asset => asset.VariantIndex).FirstOrDefault() ??
                               entry.Asset;
                result[entry.Cell.Index] = selected;
            }
        }

        return result;
    }

    private static string BuildBuildingFamilyKey(MapCellOverride? cell, MaterialAsset asset)
    {
        if (asset.TerrainId.HasValue)
        {
            return "terrain:" + asset.TerrainId.Value.ToString(CultureInfo.InvariantCulture);
        }

        if (cell != null && MaterialHexTagParser.TryParseTerrainId(cell.MaterialCategory, out var id))
        {
            return "terrain:" + id.ToString(CultureInfo.InvariantCulture);
        }

        if (!string.IsNullOrWhiteSpace(asset.TerrainName))
        {
            return "name:" + asset.TerrainName.Trim();
        }

        if (!string.IsNullOrWhiteSpace(asset.GroupKey))
        {
            return "group:" + asset.GroupKey.Trim();
        }

        return "file:" + Path.GetFileNameWithoutExtension(asset.FilePath);
    }

    private static string BuildBuildingStyleKey(MaterialAsset asset)
        => string.IsNullOrWhiteSpace(asset.AutoTileSetKey)
            ? string.IsNullOrWhiteSpace(asset.GroupKey) ? asset.FilePath : asset.GroupKey
            : asset.AutoTileSetKey;

    private static int BuildLinePathMaskForFamily(
        MapWorkbenchDraft draft,
        int index,
        string familyKey,
        IReadOnlyList<BuildingOverlayEntry> entries)
    {
        var x = index % draft.GridWidth;
        var y = index / draft.GridWidth;
        var mask = 0;
        if (IsSameBuildingFamilyAt(draft, x, y - 1, familyKey, entries)) mask |= MaterialAutoTileMasks.North;
        if (IsSameBuildingFamilyAt(draft, x + 1, y, familyKey, entries)) mask |= MaterialAutoTileMasks.East;
        if (IsSameBuildingFamilyAt(draft, x, y + 1, familyKey, entries)) mask |= MaterialAutoTileMasks.South;
        if (IsSameBuildingFamilyAt(draft, x - 1, y, familyKey, entries)) mask |= MaterialAutoTileMasks.West;
        if (HasAll(mask, MaterialAutoTileMasks.North | MaterialAutoTileMasks.East) &&
            IsSameBuildingFamilyAt(draft, x + 1, y - 1, familyKey, entries))
        {
            mask |= MaterialAutoTileMasks.NorthEast;
        }

        if (HasAll(mask, MaterialAutoTileMasks.South | MaterialAutoTileMasks.East) &&
            IsSameBuildingFamilyAt(draft, x + 1, y + 1, familyKey, entries))
        {
            mask |= MaterialAutoTileMasks.SouthEast;
        }

        if (HasAll(mask, MaterialAutoTileMasks.South | MaterialAutoTileMasks.West) &&
            IsSameBuildingFamilyAt(draft, x - 1, y + 1, familyKey, entries))
        {
            mask |= MaterialAutoTileMasks.SouthWest;
        }

        if (HasAll(mask, MaterialAutoTileMasks.North | MaterialAutoTileMasks.West) &&
            IsSameBuildingFamilyAt(draft, x - 1, y - 1, familyKey, entries))
        {
            mask |= MaterialAutoTileMasks.NorthWest;
        }

        return mask;
    }

    private static bool IsSameBuildingFamilyAt(
        MapWorkbenchDraft draft,
        int x,
        int y,
        string familyKey,
        IReadOnlyList<BuildingOverlayEntry> entries)
    {
        if (x < 0 || y < 0 || x >= draft.GridWidth || y >= draft.GridHeight) return false;
        var index = y * draft.GridWidth + x;
        return entries.Any(entry => entry.Cell.Index == index && entry.FamilyKey.Equals(familyKey, StringComparison.OrdinalIgnoreCase));
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

        var mask = BuildLinePathMask(draft, current, index, materialLookup, layerCells);
        return TerrainAutoTileResolver.SelectAutoTileAssetForMask(groupAssets, mask) ?? current;
    }

    private static int BuildLinePathMask(
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

    private void DrawMaterialAsset(Graphics g, MaterialAsset asset, Rectangle target, TerrainVisualProfile? profile = null)
    {
        var source = GetCachedImage(asset.FilePath);
        if (source == null) return;
        var sourceRect = BuildSourceRect(asset, source);
        if (asset.AssetType.Equals(MaterialAssetTypes.Building, StringComparison.OrdinalIgnoreCase))
        {
            using var tile = new Bitmap(target.Width, target.Height, PixelFormat.Format32bppArgb);
            using (var tileGraphics = CreateGraphics(tile))
            {
                tileGraphics.DrawImage(source, new Rectangle(0, 0, target.Width, target.Height), sourceRect, GraphicsUnit.Pixel);
            }

            using var overlaySelection = SelectObjectOverlay(tile, profile ?? new TerrainVisualProfile());
            if (overlaySelection.Bitmap != null)
            {
                g.DrawImage(overlaySelection.Bitmap, target);
            }

            return;
        }

        g.DrawImage(source, target, sourceRect, GraphicsUnit.Pixel);
    }

    private void DrawSceneryOverlays(Graphics g, MapWorkbenchDraft draft)
    {
        foreach (var overlay in draft.SceneryOverlays
                     .OrderBy(overlay => overlay.ZOrder)
                     .ThenBy(overlay => overlay.Y)
                     .ThenBy(overlay => overlay.X))
        {
            DrawSceneryOverlay(g, draft, overlay);
        }
    }

    private void DrawSceneryOverlaysIntersecting(Graphics g, MapWorkbenchDraft draft, int index)
    {
        var rect = GetTileRectangle(draft, index);
        foreach (var overlay in draft.SceneryOverlays
                     .OrderBy(overlay => overlay.ZOrder)
                     .ThenBy(overlay => overlay.Y)
                     .ThenBy(overlay => overlay.X))
        {
            if (!GetSceneryBounds(overlay).IntersectsWith(rect)) continue;
            DrawSceneryOverlay(g, draft, overlay);
        }
    }

    private void DrawSceneryOverlay(Graphics g, MapWorkbenchDraft draft, MapSceneryOverlay overlay)
    {
        var image = GetCachedImage(MapDraftService.ResolveMaterialPath(draft.MaterialRoot, overlay.MaterialRelativePath));
        if (image == null) return;
        var target = GetSceneryTargetRectangle(overlay);
        var canvas = new Rectangle(0, 0, draft.PixelWidth, draft.PixelHeight);
        var bounds = Rectangle.Intersect(GetSceneryBounds(overlay), canvas);
        if (bounds.IsEmpty) return;

        var state = g.Save();
        try
        {
            g.SetClip(canvas);
            var centerX = target.Left + target.Width / 2f;
            var centerY = target.Top + target.Height / 2f;
            g.TranslateTransform(centerX, centerY);
            if (Math.Abs(overlay.RotationDegrees) > 0.001f)
            {
                g.RotateTransform(overlay.RotationDegrees);
            }

            g.DrawImage(
                image,
                new RectangleF(-target.Width / 2f, -target.Height / 2f, target.Width, target.Height),
                new RectangleF(0, 0, image.Width, image.Height),
                GraphicsUnit.Pixel);
        }
        finally
        {
            g.Restore(state);
        }
    }

    private static Rectangle GetSceneryTargetRectangle(MapSceneryOverlay overlay)
        => new(
            overlay.X,
            overlay.Y,
            overlay.Width <= 0 ? MapResourceItem.MapTilePixelSize : overlay.Width,
            overlay.Height <= 0 ? MapResourceItem.MapTilePixelSize : overlay.Height);

    private static Rectangle GetSceneryBounds(MapSceneryOverlay overlay)
    {
        var rect = GetSceneryTargetRectangle(overlay);
        if (Math.Abs(overlay.RotationDegrees % 360f) < 0.001f) return rect;
        var radians = overlay.RotationDegrees * MathF.PI / 180f;
        var cos = MathF.Abs(MathF.Cos(radians));
        var sin = MathF.Abs(MathF.Sin(radians));
        var width = rect.Width * cos + rect.Height * sin;
        var height = rect.Width * sin + rect.Height * cos;
        var centerX = rect.Left + rect.Width / 2f;
        var centerY = rect.Top + rect.Height / 2f;
        return Rectangle.FromLTRB(
            (int)MathF.Floor(centerX - width / 2f),
            (int)MathF.Floor(centerY - height / 2f),
            (int)MathF.Ceiling(centerX + width / 2f),
            (int)MathF.Ceiling(centerY + height / 2f));
    }

    private void DrawBaseLayer(Graphics g, MapWorkbenchDraft draft, int width, int height, bool checkerboardBlank)
    {
        var baseImage = GetCachedImage(draft.BaseLayerPath);
        if (baseImage != null)
        {
            var copyWidth = Math.Min(width, baseImage.Width);
            var copyHeight = Math.Min(height, baseImage.Height);
            g.DrawImage(baseImage, new Rectangle(0, 0, copyWidth, copyHeight), new Rectangle(0, 0, copyWidth, copyHeight), GraphicsUnit.Pixel);
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
            var clipped = Rectangle.Intersect(rect, new Rectangle(0, 0, baseImage.Width, baseImage.Height));
            if (clipped.Width <= 0 || clipped.Height <= 0)
            {
                using var blankBrush = new SolidBrush(Color.FromArgb(42, 42, 42));
                g.FillRectangle(blankBrush, rect);
                return;
            }

            var sourceRect = new Rectangle(
                clipped.X,
                clipped.Y,
                clipped.Width,
                clipped.Height);
            g.DrawImage(baseImage, clipped, sourceRect, GraphicsUnit.Pixel);
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

    private sealed record BuildingOverlayEntry(
        MapCellOverride Cell,
        int Order,
        MaterialAsset Asset,
        string FamilyKey,
        string StyleKey);

    private ObjectOverlaySelection SelectObjectOverlay(Bitmap tile, TerrainVisualProfile profile)
    {
        using var tileBuffer = FastBitmapBuffer.FromBitmap(tile);
        if (tileBuffer.HasAlphaBelow(250))
        {
            return new ObjectOverlaySelection(new Bitmap(tile));
        }

        var repaired = _objectAlphaRepairService.Repair(tile, profile);
        if (repaired.Repaired || repaired.RejectedPixelCount == 0)
        {
            return new ObjectOverlaySelection(repaired.Bitmap, repaired);
        }

        repaired.Dispose();
        return new ObjectOverlaySelection(null);
    }

    private sealed class ObjectOverlaySelection : IDisposable
    {
        private readonly IDisposable? _owner;

        public ObjectOverlaySelection(Bitmap? bitmap, IDisposable? owner = null)
        {
            Bitmap = bitmap;
            _owner = owner ?? bitmap;
        }

        public Bitmap? Bitmap { get; }

        public void Dispose()
        {
            _owner?.Dispose();
        }
    }

    private sealed record CachedImage(DateTime LastWriteUtc, long Length, Bitmap Bitmap);
}
