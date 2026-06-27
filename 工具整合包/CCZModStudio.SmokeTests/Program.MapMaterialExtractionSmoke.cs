using CCZModStudio.Core;
using CCZModStudio.Models;
using System.Drawing;
using System.Drawing.Imaging;

internal partial class Program
{
    static void RunMapMaterialExtractionSmoke()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "CCZModStudio_MapMaterialExtractionSmoke_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
        try
        {
            var materialRoot = Path.Combine(tempRoot, "materials");
            var terrainDir = Path.Combine(materialRoot, "地形", "12：小河");
            Directory.CreateDirectory(terrainDir);
            Directory.CreateDirectory(Path.Combine(materialRoot, "地形"));
            Directory.CreateDirectory(Path.Combine(materialRoot, "建筑"));
            Directory.CreateDirectory(Path.Combine(materialRoot, "景物"));
            SaveSolidBitmap(Path.Combine(terrainDir, "1.png"), 48, 48, Color.FromArgb(10, 20, 30));
            SaveSolidBitmap(Path.Combine(terrainDir, "2.jpg"), 48, 48, Color.FromArgb(20, 30, 40));
            SaveSolidBitmap(Path.Combine(terrainDir, "39.png"), 48, 48, Color.FromArgb(30, 40, 50));
            SaveSolidBitmap(Path.Combine(terrainDir, "sample.png"), 48, 48, Color.FromArgb(40, 50, 60));
            File.WriteAllText(Path.Combine(terrainDir, "_variants.json"), "[]");

            var basePath = Path.Combine(tempRoot, "base.png");
            SaveQuadrantMap(basePath);
            var draft = new MapWorkbenchDraft
            {
                DraftId = "map-material-extraction-smoke",
                GridWidth = 2,
                GridHeight = 2,
                TileSize = MapResourceItem.MapTilePixelSize,
                BaseLayerPath = basePath,
                MaterialRoot = materialRoot,
                OriginalTerrainCells = [12, 12, 12, 12],
                TerrainCells = [12, 12, 12, 12],
                BeautifyGeneratedMap = false
            };
            var originalTerrain = draft.TerrainCells.ToArray();
            var originalBaseCells = draft.TerrainBaseCells.Count;
            var service = new MapMaterialExtractionService();

            var single = service.Extract(new MapMaterialExtractionRequest
            {
                Draft = draft,
                MaterialRoot = materialRoot,
                CellRange = new Rectangle(1, 0, 1, 1),
                TargetType = MapMaterialExtractionTargetType.Terrain,
                TerrainId = 12,
                Source = MapMaterialExtractionSource.CurrentComposite,
                Materials = Array.Empty<MaterialAsset>()
            });

            AssertExtractionSmokeEqual(1, single.Files.Count, "single count");
            AssertExtractionSmokeEqual(40, single.StartSequence, "single start sequence");
            AssertExtractionSmokeEqual("40.png", Path.GetFileName(single.Files[0].Path), "single file name");
            AssertExtractionPixel(single.Files[0].Path, Color.FromArgb(40, 170, 80), "single source pixel");

            var batch = service.Extract(new MapMaterialExtractionRequest
            {
                Draft = draft,
                MaterialRoot = materialRoot,
                CellRange = new Rectangle(0, 0, 2, 2),
                TargetType = MapMaterialExtractionTargetType.Terrain,
                TerrainId = 12,
                Source = MapMaterialExtractionSource.CurrentComposite,
                Materials = Array.Empty<MaterialAsset>()
            });
            AssertExtractionSmokeEqual(4, batch.Files.Count, "batch count");
            AssertExtractionSmokeEqual(41, batch.StartSequence, "batch start sequence");
            AssertExtractionSmokeEqual(44, batch.EndSequence, "batch end sequence");
            AssertExtractionSmokeEqual("41.png", Path.GetFileName(batch.Files[0].Path), "batch first file");
            AssertExtractionSmokeEqual("42.png", Path.GetFileName(batch.Files[1].Path), "batch second file");
            AssertExtractionPixel(batch.Files[0].Path, Color.FromArgb(170, 40, 40), "batch cell 0");
            AssertExtractionPixel(batch.Files[1].Path, Color.FromArgb(40, 170, 80), "batch cell 1");
            AssertExtractionPixel(batch.Files[2].Path, Color.FromArgb(40, 80, 180), "batch cell 2");
            AssertExtractionPixel(batch.Files[3].Path, Color.FromArgb(210, 190, 70), "batch cell 3");

            var building = service.Extract(new MapMaterialExtractionRequest
            {
                Draft = draft,
                MaterialRoot = materialRoot,
                CellRange = new Rectangle(0, 1, 1, 1),
                TargetType = MapMaterialExtractionTargetType.Building,
                TerrainId = 23,
                Source = MapMaterialExtractionSource.CurrentComposite,
                Materials = Array.Empty<MaterialAsset>()
            });
            var expectedBuildingDir = Path.Combine(materialRoot, "建筑", "23：民居");
            if (!Directory.Exists(expectedBuildingDir))
            {
                throw new InvalidOperationException("Extraction did not create a standard typed building folder.");
            }
            AssertExtractionSmokeEqual(expectedBuildingDir, building.TargetDirectory, "building target directory");
            AssertExtractionSmokeEqual("1.png", Path.GetFileName(building.Files[0].Path), "building first sequence");

            var oldRoot = Path.Combine(tempRoot, "old-layout");
            Directory.CreateDirectory(oldRoot);
            Directory.CreateDirectory(Path.Combine(oldRoot, "地形"));
            var rejected = false;
            try
            {
                service.Extract(new MapMaterialExtractionRequest
                {
                    Draft = draft,
                    MaterialRoot = oldRoot,
                    CellRange = new Rectangle(0, 0, 1, 1),
                    TargetType = MapMaterialExtractionTargetType.Terrain,
                    TerrainId = 12,
                    Source = MapMaterialExtractionSource.CurrentComposite,
                    Materials = Array.Empty<MaterialAsset>()
                });
            }
            catch (InvalidOperationException)
            {
                rejected = true;
            }

            if (!rejected)
            {
                throw new InvalidOperationException("Old-layout material library should be rejected.");
            }

            if (!draft.TerrainCells.SequenceEqual(originalTerrain) ||
                draft.TerrainBaseCells.Count != originalBaseCells ||
                draft.BuildingOverlayCells.Count != 0 ||
                draft.SceneryOverlays.Count != 0)
            {
                throw new InvalidOperationException("Material extraction should not mutate the source draft.");
            }

            Console.WriteLine($"MAP_MATERIAL_EXTRACTION_SMOKE_OK single={Path.GetFileName(single.Files[0].Path)} batch={batch.StartSequence}-{batch.EndSequence} building={Path.GetFileName(building.Files[0].Path)}");
        }
        finally
        {
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }

    private static void SaveQuadrantMap(string path)
    {
        using var bitmap = new Bitmap(96, 96, PixelFormat.Format32bppArgb);
        using var graphics = Graphics.FromImage(bitmap);
        using var red = new SolidBrush(Color.FromArgb(170, 40, 40));
        using var green = new SolidBrush(Color.FromArgb(40, 170, 80));
        using var blue = new SolidBrush(Color.FromArgb(40, 80, 180));
        using var yellow = new SolidBrush(Color.FromArgb(210, 190, 70));
        graphics.FillRectangle(red, 0, 0, 48, 48);
        graphics.FillRectangle(green, 48, 0, 48, 48);
        graphics.FillRectangle(blue, 0, 48, 48, 48);
        graphics.FillRectangle(yellow, 48, 48, 48, 48);
        bitmap.Save(path, ImageFormat.Png);
    }

    private static void AssertExtractionPixel(string path, Color expected, string scenario)
    {
        using var bitmap = new Bitmap(path);
        if (bitmap.Width != 48 || bitmap.Height != 48)
        {
            throw new InvalidOperationException($"{scenario}: extracted bitmap size should be 48x48, actual={bitmap.Width}x{bitmap.Height}.");
        }

        var actual = bitmap.GetPixel(24, 24);
        if (Math.Abs(actual.R - expected.R) > 2 ||
            Math.Abs(actual.G - expected.G) > 2 ||
            Math.Abs(actual.B - expected.B) > 2)
        {
            throw new InvalidOperationException($"{scenario}: expected center {expected}, actual {actual}.");
        }
    }

    private static void AssertExtractionSmokeEqual<T>(T expected, T actual, string scenario)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new InvalidOperationException($"{scenario}: expected {expected}, actual {actual}.");
        }
    }
}
