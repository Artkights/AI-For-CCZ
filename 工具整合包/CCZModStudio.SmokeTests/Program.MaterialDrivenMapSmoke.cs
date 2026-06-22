using CCZModStudio.Core;
using CCZModStudio.Formats;
using CCZModStudio.Models;
using System.Drawing;
using System.Drawing.Imaging;

internal partial class Program
{
    static void RunMaterialDrivenMapSmoke()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "CCZModStudio_MaterialDrivenMapSmoke_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
        try
        {
            var oldRoot = Path.Combine(tempRoot, "old");
            var newRoot = Path.Combine(tempRoot, "素材库");
            BuildLegacyMaterialLibrary(oldRoot);
            var migration = new MaterialLibraryMigrationService().Migrate(oldRoot, newRoot);
            if (migration.TerrainImageCount != 4 || migration.BuildingImageCount != 4 || migration.SceneryImageCount != 1)
            {
                throw new InvalidOperationException("Material library migration counts were not preserved.");
            }

            var materials = new MaterialLibraryIndexer().IndexExplicitRoot(newRoot);
            AssertStandardTypedMaterialFolders(newRoot, "地形");
            AssertStandardTypedMaterialFolders(newRoot, "建筑");
            AssertImageCount(Path.Combine(newRoot, "地形", "2：树林"), 2, "same terrain id should migrate into one standard folder");
            var terrainPlain = materials.Single(asset => asset.AssetType == MaterialAssetTypes.Terrain && asset.TerrainId == 0);
            var terrainWater = materials.Single(asset => asset.AssetType == MaterialAssetTypes.Terrain && asset.TerrainId == 12);
            var wallAssets = materials.Where(asset => asset.AssetType == MaterialAssetTypes.Building && asset.TerrainName == "栅栏").ToList();
            var building = materials.First(asset => asset.AssetType == MaterialAssetTypes.Building && asset.TerrainId == 6);
            var scenery = materials.Single(asset => asset.AssetType == MaterialAssetTypes.Scenery);
            if (scenery.TerrainId.HasValue)
            {
                throw new InvalidOperationException("Scenery material must not carry a terrain id.");
            }

            var draft = new MapWorkbenchDraft
            {
                DraftId = "material-driven-smoke",
                GridWidth = 3,
                GridHeight = 3,
                TileSize = MapResourceItem.MapTilePixelSize,
                MaterialRoot = newRoot,
                OriginalTerrainCells = Enumerable.Repeat((byte)0, 9).ToArray(),
                TerrainCells = Enumerable.Repeat((byte)0, 9).ToArray(),
                TerrainBaseCells =
                [
                    MakeCell(newRoot, terrainWater, 0, MapCellOverrideSources.TerrainBase),
                    MakeCell(newRoot, terrainWater, 1, MapCellOverrideSources.TerrainBase),
                    MakeCell(newRoot, terrainPlain, 3, MapCellOverrideSources.TerrainBase)
                ],
                BuildingOverlayCells =
                [
                    MakeCell(newRoot, building, 4, MapCellOverrideSources.BuildingOverlay)
                ],
                SceneryOverlayCells =
                [
                    MakeCell(newRoot, scenery, 5, MapCellOverrideSources.SceneryOverlay)
                ],
                BeautifyGeneratedMap = true,
                BeautifyStrength = 2,
                FeatherRadius = 8
            };

            var service = new MaterialDrivenTerrainService();
            var derived = service.DeriveTerrainCells(draft, materials);
            AssertMaterialSmokeEqual(12, derived[0], "terrain material changes final terrain");
            AssertMaterialSmokeEqual(6, derived[4], "building material overrides final terrain");
            AssertMaterialSmokeEqual(0, derived[5], "scenery material keeps final terrain");
            draft.TerrainCells = derived;

            using var baseMap = service.ComposeVisualMap(draft, materials, checkerboardBlank: false, beautifyTerrain: false);
            using var beautified = service.ComposeVisualMap(draft, materials, checkerboardBlank: false, beautifyTerrain: true);
            AssertPixelDifferent(baseMap, beautified, 47, 24, "terrain feather edge");
            var sceneryPixel = beautified.GetPixel(5 % 3 * 48 + 24, 5 / 3 * 48 + 24);
            if (sceneryPixel.R < 210 || sceneryPixel.G < 210 || sceneryPixel.B > 80)
            {
                throw new InvalidOperationException("Scenery overlay was not drawn above beautified terrain.");
            }

            var wallSetCount = wallAssets.Select(asset => asset.AutoTileSetKey).Distinct(StringComparer.OrdinalIgnoreCase).Count();
            if (wallSetCount != 2)
            {
                throw new InvalidOperationException($"Wall auto-tile assets should be grouped by physical image. expected=2 actual={wallSetCount}");
            }

            var firstWallRepresentative = wallAssets
                .Where(asset => asset.AutoTileMask == MaterialAutoTileMasks.StraightH)
                .OrderBy(asset => asset.FileName, StringComparer.OrdinalIgnoreCase)
                .First();
            AssertAutoTileCenterColor(service, materials, newRoot, firstWallRepresentative, [3, 4, 5], Color.FromArgb(90, 90, 90), "horizontal wall frame");
            AssertAutoTileCenterColor(service, materials, newRoot, firstWallRepresentative, [1, 4, 7], Color.FromArgb(80, 140, 210), "vertical wall frame");
            AssertAutoTileCenterColor(service, materials, newRoot, firstWallRepresentative, [1, 4, 5], Color.FromArgb(90, 210, 140), "corner NE wall frame");
            AssertAutoTileCenterColor(service, materials, newRoot, firstWallRepresentative, [1, 3, 4], Color.FromArgb(170, 120, 210), "corner NW wall frame");
            AssertAutoTileCenterColor(service, materials, newRoot, firstWallRepresentative, [4, 5, 7], Color.FromArgb(80, 190, 190), "corner SE wall frame");
            AssertAutoTileCenterColor(service, materials, newRoot, firstWallRepresentative, [3, 4, 7], Color.FromArgb(190, 150, 80), "corner SW wall frame");
            AssertAutoTileCenterColor(service, materials, newRoot, firstWallRepresentative, [3, 4, 5, 7], Color.FromArgb(120, 120, 120), "tee north wall frame");
            AssertAutoTileCenterColor(service, materials, newRoot, firstWallRepresentative, [1, 3, 4, 5, 7], Color.FromArgb(240, 30, 30), "cross wall frame");
            AssertAutoTileCenterColor(service, materials, newRoot, firstWallRepresentative, [0, 1, 2, 3, 4, 5, 6, 7], Color.FromArgb(240, 30, 30), "eight-way inner corner fallback frame");

            var secondWallRepresentative = wallAssets
                .Where(asset => asset.AutoTileMask == MaterialAutoTileMasks.StraightH)
                .OrderBy(asset => asset.FileName, StringComparer.OrdinalIgnoreCase)
                .Skip(1)
                .First();
            var secondWallDraft = new MapWorkbenchDraft
            {
                DraftId = "wall-autotile-second-sheet-smoke",
                GridWidth = 3,
                GridHeight = 3,
                TileSize = MapResourceItem.MapTilePixelSize,
                MaterialRoot = newRoot,
                OriginalTerrainCells = Enumerable.Repeat((byte)0, 9).ToArray(),
                TerrainCells = Enumerable.Repeat((byte)0, 9).ToArray(),
                BuildingOverlayCells =
                [
                    MakeCell(newRoot, secondWallRepresentative, 1, MapCellOverrideSources.BuildingOverlay),
                    MakeCell(newRoot, secondWallRepresentative, 3, MapCellOverrideSources.BuildingOverlay),
                    MakeCell(newRoot, secondWallRepresentative, 4, MapCellOverrideSources.BuildingOverlay),
                    MakeCell(newRoot, secondWallRepresentative, 5, MapCellOverrideSources.BuildingOverlay),
                    MakeCell(newRoot, secondWallRepresentative, 7, MapCellOverrideSources.BuildingOverlay)
                ]
            };
            using var secondWallMap = service.ComposeVisualMap(secondWallDraft, materials, checkerboardBlank: false, beautifyTerrain: false);
            var secondCenter = secondWallMap.GetPixel(4 % 3 * 48 + 24, 4 / 3 * 48 + 24);
            if (secondCenter.B < 220 || secondCenter.R > 80 || secondCenter.G > 80)
            {
                throw new InvalidOperationException("Auto-tile cross frame should stay within the selected wall image set.");
            }

            var migrationVariantPath = Directory.GetFiles(Path.Combine(newRoot, "建筑"), "_variants.json", SearchOption.AllDirectories).FirstOrDefault();
            if (migrationVariantPath == null)
            {
                throw new InvalidOperationException("Migration did not create _variants.json for directional material.");
            }

            Console.WriteLine("MATERIAL_DRIVEN_MAP_SMOKE_OK migration=ok index=ok derive=ok beautify=ok autotile=ok");
        }
        finally
        {
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }

    private static void BuildLegacyMaterialLibrary(string root)
    {
        var terrainDir = Path.Combine(root, "地形");
        var buildingDir = Path.Combine(root, "建筑");
        var wallDir = Path.Combine(root, "围墙");
        var randomDir = Path.Combine(root, "随机");
        var sceneryDir = Path.Combine(root, "景物");
        Directory.CreateDirectory(terrainDir);
        Directory.CreateDirectory(buildingDir);
        Directory.CreateDirectory(wallDir);
        Directory.CreateDirectory(randomDir);
        Directory.CreateDirectory(sceneryDir);

        SaveSolidBitmap(Path.Combine(terrainDir, "1.png"), 48, 48, Color.FromArgb(60, 170, 80));
        SaveSolidBitmap(Path.Combine(terrainDir, "2.png"), 48, 48, Color.FromArgb(35, 85, 190));
        SaveSolidBitmap(Path.Combine(terrainDir, "3.png"), 48, 48, Color.FromArgb(20, 130, 45));
        SaveSolidBitmap(Path.Combine(terrainDir, "4.png"), 48, 48, Color.FromArgb(24, 112, 42));
        File.WriteAllLines(Path.Combine(terrainDir, "hex.txt"), ["0", "平原", "12", "河流", "2", "树林1", "2", "树林2"]);

        SaveSolidBitmap(Path.Combine(buildingDir, "1.png"), 48, 48, Color.FromArgb(180, 80, 60));
        File.WriteAllLines(Path.Combine(buildingDir, "hex.txt"), ["6", "城池"]);

        SaveWallSheet(Path.Combine(wallDir, "1.png"), Color.FromArgb(240, 30, 30));
        SaveWallSheet(Path.Combine(wallDir, "2.png"), Color.FromArgb(30, 30, 240));
        File.WriteAllLines(Path.Combine(wallDir, "hex.txt"), ["14", "围墙", "14", "围墙"]);

        SaveSolidBitmap(Path.Combine(randomDir, "1.png"), 48, 48, Color.FromArgb(150, 150, 80));
        File.WriteAllLines(Path.Combine(randomDir, "hex.txt"), ["15", "随机"]);

        SaveSolidBitmap(Path.Combine(sceneryDir, "1.png"), 48, 48, Color.FromArgb(240, 230, 40));
    }

    private static void AssertStandardTypedMaterialFolders(string root, string typeName)
    {
        var typedRoot = Path.Combine(root, typeName);
        var seen = new HashSet<byte>();
        foreach (var dir in Directory.GetDirectories(typedRoot))
        {
            var name = Path.GetFileName(dir);
            var parts = name.Split('：', 2);
            if (parts.Length != 2 || !byte.TryParse(parts[0], out var id))
            {
                throw new InvalidOperationException($"Material folder must use id:name format: {dir}");
            }

            var expected = $"{id}：{MaterialLibraryIndexer.GetBuiltInTerrainName(id)}";
            if (!name.Equals(expected, StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"Material folder should use the standard terrain name. expected={expected} actual={name}");
            }

            if (!seen.Add(id))
            {
                throw new InvalidOperationException($"{typeName} material folder has duplicate terrain id {id}.");
            }
        }
    }

    private static void AssertImageCount(string directory, int expected, string scenario)
    {
        var count = Directory.GetFiles(directory)
            .Count(path => new[] { ".png", ".jpg", ".jpeg", ".bmp" }.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase));
        if (count != expected)
        {
            throw new InvalidOperationException($"{scenario}: expected {expected} images, actual {count}. directory={directory}");
        }
    }

    private static void SaveWallSheet(string path, Color crossColor)
    {
        using var bitmap = new Bitmap(48 * 15, 48, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bitmap);
        var colors = new[]
        {
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
            crossColor
        };
        for (var i = 0; i < colors.Length; i++)
        {
            using var brush = new SolidBrush(colors[i]);
            g.FillRectangle(brush, i * 48, 0, 48, 48);
        }

        bitmap.Save(path, ImageFormat.Png);
    }

    private static void AssertAutoTileCenterColor(
        MaterialDrivenTerrainService service,
        IReadOnlyList<MaterialAsset> materials,
        string materialRoot,
        MaterialAsset representative,
        int[] occupiedCells,
        Color expected,
        string scenario)
    {
        var draft = new MapWorkbenchDraft
        {
            DraftId = "wall-autotile-" + scenario.Replace(' ', '-'),
            GridWidth = 3,
            GridHeight = 3,
            TileSize = MapResourceItem.MapTilePixelSize,
            MaterialRoot = materialRoot,
            OriginalTerrainCells = Enumerable.Repeat((byte)0, 9).ToArray(),
            TerrainCells = Enumerable.Repeat((byte)0, 9).ToArray(),
            BuildingOverlayCells = occupiedCells
                .Select(index => MakeCell(materialRoot, representative, index, MapCellOverrideSources.BuildingOverlay))
                .ToList()
        };

        using var map = service.ComposeVisualMap(draft, materials, checkerboardBlank: false, beautifyTerrain: false);
        var center = map.GetPixel(4 % 3 * 48 + 24, 4 / 3 * 48 + 24);
        if (!ColorsClose(center, expected, tolerance: 3))
        {
            throw new InvalidOperationException($"{scenario}: expected center color {expected}, actual {center}.");
        }
    }

    private static bool ColorsClose(Color actual, Color expected, int tolerance)
        => Math.Abs(actual.R - expected.R) <= tolerance &&
           Math.Abs(actual.G - expected.G) <= tolerance &&
           Math.Abs(actual.B - expected.B) <= tolerance;

    private static MapCellOverride MakeCell(string root, MaterialAsset asset, int index, string source)
        => new()
        {
            Index = index,
            MaterialRelativePath = MapDraftService.GetMaterialRelativePath(root, asset.FilePath),
            MaterialCategory = asset.Category,
            DisplayName = asset.FileName,
            Source = source
        };

    private static void AssertMaterialSmokeEqual<T>(T expected, T actual, string scenario)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new InvalidOperationException($"{scenario}: expected {expected}, actual {actual}.");
        }
    }
}
