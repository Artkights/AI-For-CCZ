using CCZModStudio.Core;
using CCZModStudio.Formats;
using CCZModStudio.Models;
using System.Drawing;
using System.Drawing.Imaging;

internal partial class Program
{
    static void RunAutoTileRegionSmoke()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "CCZModStudio_AutoTileRegionSmoke_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
        try
        {
            var materialRoot = Path.Combine(tempRoot, "materials");
            var wallDir = Path.Combine(materialRoot, "15_wall");
            var regionDir = Path.Combine(materialRoot, "25_region");
            Directory.CreateDirectory(wallDir);
            Directory.CreateDirectory(regionDir);

            SaveAutoTileSmokeSheet(Path.Combine(wallDir, "1.png"));
            SaveAutoTileSmokeSheet(Path.Combine(regionDir, "1.png"));
            SaveSolidAutoTileSmokeBitmap(Path.Combine(regionDir, "60.png"), 48, 48, RegionFillColor);

            var materials = BuildAutoTileSmokeMaterials(wallDir, regionDir);
            var wall = materials.First(asset => asset.TerrainId == 15 && asset.AutoTileMask == MaterialAutoTileMasks.StraightH);
            var region = materials.First(asset => asset.TerrainId == 25 && asset.AutoTileMask == MaterialAutoTileMasks.StraightH);

            AssertAutoTileSmokeEqual(MaterialAutoTileModes.LinePath, wall.AutoTileMode, "wall strip mode");
            AssertAutoTileSmokeEqual(MaterialAutoTileModes.RegionBoundary, region.AutoTileMode, "region strip mode");
            if (materials.Any(asset =>
                    asset.TerrainId == 25 &&
                    asset.AutoTileSetKey.Equals(region.AutoTileSetKey, StringComparison.OrdinalIgnoreCase) &&
                    asset.AutoTileRole.Equals(MaterialAutoTileRoles.Fill, StringComparison.OrdinalIgnoreCase)))
            {
                throw new InvalidOperationException("Region auto-tile set must not contain fill assets.");
            }

            using var service = new MaterialDrivenTerrainService();
            AssertLineCornerDirections(service, materials, materialRoot, wall);
            AssertLineEndpointDirections(service, materials, materialRoot, wall);
            AssertRegionRectangleKeepsCross(service, materials, materialRoot, region);
            AssertIndexerRebuildsCanonicalStripVariants(tempRoot);
            Console.WriteLine("AUTOTILE_REGION_SMOKE_OK line=ok endpoints=ok region=ok cross=ok index=ok");
        }
        finally
        {
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }

    private static readonly Color RegionFillColor = Color.FromArgb(30, 150, 210);

    private static readonly Color[] AutoTileFrameColors =
    [
        Color.FromArgb(90, 90, 90),
        Color.FromArgb(80, 140, 210),
        Color.FromArgb(90, 210, 140),
        Color.FromArgb(170, 120, 210),
        Color.FromArgb(190, 150, 80),
        Color.FromArgb(80, 190, 190),
        Color.FromArgb(190, 80, 160),
        Color.FromArgb(120, 120, 220),
        Color.FromArgb(220, 120, 120),
        Color.FromArgb(120, 220, 120),
        Color.FromArgb(120, 120, 120),
        Color.FromArgb(180, 180, 90),
        Color.FromArgb(90, 180, 180),
        Color.FromArgb(180, 90, 180),
        Color.FromArgb(240, 30, 30)
    ];

    private static IReadOnlyList<MaterialAsset> BuildAutoTileSmokeMaterials(string wallDir, string regionDir)
    {
        var result = new List<MaterialAsset>();
        AddAutoTileSmokeStrip(result, Path.Combine(wallDir, "1.png"), 15, "wall", MaterialAutoTileModes.LinePath);
        AddAutoTileSmokeStrip(result, Path.Combine(regionDir, "1.png"), 25, "region", MaterialAutoTileModes.RegionBoundary);
        result.Add(new MaterialAsset
        {
            AssetType = MaterialAssetTypes.Building,
            Category = "region",
            FileName = "60.png",
            FilePath = Path.Combine(regionDir, "60.png"),
            TerrainId = 25,
            TerrainName = "region",
            GroupKey = "Building:25:region",
            AutoTileSetKey = "Building:25:region:60.png",
            VariantIndex = 15,
            AutoTileRole = MaterialAutoTileRoles.Default,
            AutoTileMask = MaterialAutoTileMasks.None,
            AutoTileMode = MaterialAutoTileModes.Default,
            AutoTilePriority = 0,
            SourceX = 0,
            SourceY = 0,
            SourceWidth = 48,
            SourceHeight = 48,
            Width = 48,
            Height = 48
        });

        return result;
    }

    private static void AddAutoTileSmokeStrip(List<MaterialAsset> result, string path, byte terrainId, string name, string mode)
    {
        var order = MaterialAutoTileMetadataService.GetCanonicalMaskOrder(mode);
        for (var i = 0; i < order.Length; i++)
        {
            var (role, mask) = order[i];
            result.Add(new MaterialAsset
            {
                AssetType = MaterialAssetTypes.Building,
                Category = name,
                FileName = Path.GetFileName(path),
                FilePath = path,
                TerrainId = terrainId,
                TerrainName = name,
                GroupKey = $"Building:{terrainId}:{name}",
                AutoTileSetKey = $"Building:{terrainId}:{name}:1.png",
                VariantIndex = i,
                AutoTileRole = role,
                AutoTileMask = mask,
                AutoTileMode = mode,
                AutoTilePriority = i,
                SourceX = i * 48,
                SourceY = 0,
                SourceWidth = 48,
                SourceHeight = 48,
                Width = 48 * 15,
                Height = 48
            });
        }
    }

    private static void AssertLineCornerDirections(
        MaterialDrivenTerrainService service,
        IReadOnlyList<MaterialAsset> materials,
        string materialRoot,
        MaterialAsset representative)
    {
        var draft = NewAutoTileSmokeDraft(materialRoot, 3, 3);
        draft.BuildingOverlayCells = new[] { 3, 4, 7 }
            .Select(index => AutoTileSmokeCell(materialRoot, representative, index))
            .ToList();
        using var map = service.ComposeVisualMap(draft, materials, checkerboardBlank: false, beautifyTerrain: false);
        AssertFrame(map, 1, 1, 3, "line upper-right corner should use west+south frame");
    }

    private static void AssertLineEndpointDirections(
        MaterialDrivenTerrainService service,
        IReadOnlyList<MaterialAsset> materials,
        string materialRoot,
        MaterialAsset representative)
    {
        var horizontal = NewAutoTileSmokeDraft(materialRoot, 3, 1);
        horizontal.BuildingOverlayCells = new[] { 0, 1, 2 }
            .Select(index => AutoTileSmokeCell(materialRoot, representative, index))
            .ToList();
        using (var map = service.ComposeVisualMap(horizontal, materials, checkerboardBlank: false, beautifyTerrain: false))
        {
            AssertFrame(map, 0, 0, 8, "line left endpoint should use east-facing horizontal end");
            AssertFrame(map, 1, 0, 0, "line horizontal middle should use straight horizontal frame");
            AssertFrame(map, 2, 0, 9, "line right endpoint should use west-facing horizontal end");
        }

        var vertical = NewAutoTileSmokeDraft(materialRoot, 3, 3);
        vertical.BuildingOverlayCells = new[] { 1, 4, 7 }
            .Select(index => AutoTileSmokeCell(materialRoot, representative, index))
            .ToList();
        using (var map = service.ComposeVisualMap(vertical, materials, checkerboardBlank: false, beautifyTerrain: false))
        {
            AssertFrame(map, 1, 0, 7, "line top endpoint should use south-facing vertical end");
            AssertFrame(map, 1, 1, 1, "line vertical middle should use straight vertical frame");
            AssertFrame(map, 1, 2, 6, "line bottom endpoint should use north-facing vertical end");
        }
    }

    private static void AssertRegionRectangleKeepsCross(
        MaterialDrivenTerrainService service,
        IReadOnlyList<MaterialAsset> materials,
        string materialRoot,
        MaterialAsset representative)
    {
        var draft = NewAutoTileSmokeDraft(materialRoot, 5, 5);
        draft.BuildingOverlayCells = Enumerable.Range(0, 25)
            .Select(index => AutoTileSmokeCell(materialRoot, representative, index))
            .ToList();
        using var map = service.ComposeVisualMap(draft, materials, checkerboardBlank: false, beautifyTerrain: false);

        AssertFrame(map, 0, 0, 2, "region top-left corner");
        AssertFrame(map, 4, 0, 3, "region top-right corner");
        AssertFrame(map, 0, 4, 4, "region bottom-left corner");
        AssertFrame(map, 4, 4, 5, "region bottom-right corner");
        AssertFrame(map, 2, 0, 10, "region top edge with south branch");
        AssertFrame(map, 2, 4, 11, "region bottom edge with north branch");
        AssertFrame(map, 0, 2, 12, "region left edge with east branch");
        AssertFrame(map, 4, 2, 13, "region right edge with west branch");
        AssertFrame(map, 2, 2, 14, "region interior should keep cross frame");
    }

    private static void AssertIndexerRebuildsCanonicalStripVariants(string tempRoot)
    {
        var indexRoot = Path.Combine(tempRoot, "indexedMaterials");
        var buildingRoot = Path.Combine(indexRoot, "\u5efa\u7b51");
        var poolDir = Path.Combine(buildingRoot, "25\uff1a\u6c34\u6c60");
        Directory.CreateDirectory(Path.Combine(indexRoot, "\u5730\u5f62"));
        Directory.CreateDirectory(buildingRoot);
        Directory.CreateDirectory(Path.Combine(indexRoot, "\u666f\u7269"));
        Directory.CreateDirectory(poolDir);

        SaveAutoTileSmokeSheet(Path.Combine(poolDir, "1.png"));
        SaveSolidAutoTileSmokeBitmap(Path.Combine(poolDir, "60.png"), 48, 48, RegionFillColor);
        File.WriteAllText(
            Path.Combine(poolDir, "_variants.json"),
            """
            [
              { "FileName": "1.png", "Role": "cornerNE", "Mask": 3, "Mode": "mask", "Priority": 0, "X": 96, "Y": 0, "Width": 48, "Height": 48 },
              { "FileName": "60.png", "Role": "fill", "Mask": 0, "Mode": "regionBoundary", "Priority": 0, "X": 0, "Y": 0, "Width": 48, "Height": 48 }
            ]
            """);

        var indexed = new MaterialLibraryIndexer().IndexExplicitRoot(indexRoot);
        var pool = indexed
            .Where(asset => asset.TerrainId == 25 && asset.FileName == "1.png")
            .OrderBy(asset => asset.AutoTilePriority)
            .ToList();
        AssertAutoTileSmokeEqual(15, pool.Count, "indexed canonical strip variant count");
        AssertAutoTileSmokeEqual(MaterialAutoTileRoles.CornerNW, pool[2].AutoTileRole, "indexed frame 2 role");
        AssertAutoTileSmokeEqual(MaterialAutoTileMasks.CornerNW, pool[2].AutoTileMask, "indexed frame 2 mask");
        AssertAutoTileSmokeEqual(MaterialAutoTileRoles.CornerNE, pool[3].AutoTileRole, "indexed frame 3 role");
        AssertAutoTileSmokeEqual(MaterialAutoTileMasks.CornerNE, pool[3].AutoTileMask, "indexed frame 3 mask");
        AssertAutoTileSmokeEqual(MaterialAutoTileRoles.Cross, pool[14].AutoTileRole, "indexed cross role");
        AssertAutoTileSmokeEqual(0, indexed.Count(asset => asset.TerrainId == 25 && asset.AutoTileRole.Equals(MaterialAutoTileRoles.Fill, StringComparison.OrdinalIgnoreCase)), "indexed fill variant count");

        var wallDir = Path.Combine(buildingRoot, "15\uff1a\u6805\u680f");
        Directory.CreateDirectory(wallDir);
        SaveAutoTileSmokeSheet(Path.Combine(wallDir, "1.png"));
        File.WriteAllText(
            Path.Combine(wallDir, "_variants.json"),
            """
            [
              { "FileName": "1.png", "Role": "endE", "Mask": 2, "Mode": "mask", "Priority": 6, "X": 288, "Y": 0, "Width": 48, "Height": 48 }
            ]
            """);

        indexed = new MaterialLibraryIndexer().IndexExplicitRoot(indexRoot);
        var wallPool = indexed
            .Where(asset => asset.TerrainId == 15 && asset.FileName == "1.png")
            .OrderBy(asset => asset.AutoTilePriority)
            .ToList();
        AssertAutoTileSmokeEqual(15, wallPool.Count, "indexed line canonical strip variant count");
        AssertAutoTileSmokeEqual(MaterialAutoTileRoles.EndN, wallPool[6].AutoTileRole, "indexed line frame 6 role");
        AssertAutoTileSmokeEqual(MaterialAutoTileMasks.North, wallPool[6].AutoTileMask, "indexed line frame 6 mask");
        AssertAutoTileSmokeEqual(MaterialAutoTileRoles.EndS, wallPool[7].AutoTileRole, "indexed line frame 7 role");
        AssertAutoTileSmokeEqual(MaterialAutoTileMasks.South, wallPool[7].AutoTileMask, "indexed line frame 7 mask");
        AssertAutoTileSmokeEqual(MaterialAutoTileRoles.EndE, wallPool[8].AutoTileRole, "indexed line frame 8 role");
        AssertAutoTileSmokeEqual(MaterialAutoTileMasks.East, wallPool[8].AutoTileMask, "indexed line frame 8 mask");
        AssertAutoTileSmokeEqual(MaterialAutoTileRoles.EndW, wallPool[9].AutoTileRole, "indexed line frame 9 role");
        AssertAutoTileSmokeEqual(MaterialAutoTileMasks.West, wallPool[9].AutoTileMask, "indexed line frame 9 mask");
    }

    private static MapWorkbenchDraft NewAutoTileSmokeDraft(string materialRoot, int width, int height)
        => new()
        {
            DraftId = "autotile-region-smoke",
            GridWidth = width,
            GridHeight = height,
            TileSize = MapResourceItem.MapTilePixelSize,
            MaterialRoot = materialRoot,
            OriginalTerrainCells = Enumerable.Repeat((byte)0, width * height).ToArray(),
            TerrainCells = Enumerable.Repeat((byte)0, width * height).ToArray()
        };

    private static MapCellOverride AutoTileSmokeCell(string root, MaterialAsset asset, int index)
        => new()
        {
            Index = index,
            MaterialRelativePath = MapDraftService.GetMaterialRelativePath(root, asset.FilePath),
            MaterialCategory = asset.Category,
            DisplayName = asset.FileName,
            Source = MapCellOverrideSources.BuildingOverlay
        };

    private static void SaveAutoTileSmokeSheet(string path)
    {
        using var bitmap = new Bitmap(48 * 15, 48, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bitmap);
        for (var i = 0; i < AutoTileFrameColors.Length; i++)
        {
            using var brush = new SolidBrush(AutoTileFrameColors[i]);
            g.FillRectangle(brush, i * 48, 0, 48, 48);
        }

        bitmap.Save(path, ImageFormat.Png);
    }

    private static void SaveSolidAutoTileSmokeBitmap(string path, int width, int height, Color color)
    {
        using var bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bitmap);
        using var brush = new SolidBrush(color);
        g.FillRectangle(brush, 0, 0, width, height);
        bitmap.Save(path, ImageFormat.Png);
    }

    private static void AssertFrame(Bitmap map, int cellX, int cellY, int frameIndex, string scenario)
        => AssertColorNear(
            map.GetPixel(cellX * 48 + 24, cellY * 48 + 24),
            AutoTileFrameColors[frameIndex],
            $"{scenario} should use physical frame {frameIndex}");

    private static void AssertColorNear(Color actual, Color expected, string scenario)
    {
        if (Math.Abs(actual.R - expected.R) > 3 ||
            Math.Abs(actual.G - expected.G) > 3 ||
            Math.Abs(actual.B - expected.B) > 3)
        {
            throw new InvalidOperationException($"{scenario}: expected {expected}, actual {actual}.");
        }
    }

    private static void AssertAutoTileSmokeEqual<T>(T expected, T actual, string scenario)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new InvalidOperationException($"{scenario}: expected {expected}, actual {actual}.");
        }
    }
}
