using CCZModStudio.Core;
using CCZModStudio.Models;
using System.Drawing;
using System.Drawing.Imaging;

internal partial class Program
{
    static void RunMapCanvasPreviewSmoke()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "CCZModStudio_MapCanvasPreviewSmoke_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
        try
        {
            var basePath = Path.Combine(tempRoot, "base.png");
            var materialRoot = Path.Combine(tempRoot, "materials");
            Directory.CreateDirectory(materialRoot);
            var grassPath = Path.Combine(materialRoot, "grass.png");
            var waterPath = Path.Combine(materialRoot, "water.png");

            SaveSolidBitmap(basePath, 96, 96, Color.FromArgb(20, 30, 40));
            SaveSolidBitmap(grassPath, 48, 48, Color.FromArgb(40, 160, 80));
            SaveSolidBitmap(waterPath, 48, 48, Color.FromArgb(40, 90, 180));

            var draft = new MapWorkbenchDraft
            {
                DraftId = "preview-smoke",
                GridWidth = 2,
                GridHeight = 2,
                TileSize = MapResourceItem.MapTilePixelSize,
                BaseLayerPath = basePath,
                MaterialRoot = materialRoot,
                TerrainCells = [0, 1, 2, 3],
                MapCellOverrides =
                [
                    new MapCellOverride
                    {
                        Index = 0,
                        MaterialRelativePath = Path.GetFileName(grassPath),
                        MaterialCategory = "terrain",
                        DisplayName = "grass.png"
                    }
                ]
            };

            var composeService = new MapCanvasComposeService();
            using var renderer = new MapCanvasPreviewRenderer();
            using var fullBefore = composeService.ComposePreview(draft, showTerrain: true, showGrid: true, terrainOpacityPercent: 45);
            var incrementalBefore = renderer.Rebuild(draft, showTerrain: true, showGrid: true, terrainOpacityPercent: 45);
            AssertSameBitmap(fullBefore, incrementalBefore, "initial rebuild");

            draft.MapCellOverrides =
            [
                draft.MapCellOverrides[0],
                new MapCellOverride
                {
                    Index = 3,
                    MaterialRelativePath = Path.GetFileName(waterPath),
                    MaterialCategory = "terrain",
                    DisplayName = "water.png"
                }
            ];
            var dirtyMap = renderer.UpdateMapCell(draft, 3, draft.MapCellOverrides[1]);
            if (dirtyMap != new Rectangle(48, 48, 48, 48))
            {
                throw new InvalidOperationException($"Unexpected map dirty rect: {dirtyMap}.");
            }

            using var fullAfterMap = composeService.ComposePreview(draft, showTerrain: true, showGrid: true, terrainOpacityPercent: 45);
            AssertSameBitmap(fullAfterMap, incrementalBefore, "map cell update");

            draft.TerrainCells[1] = 9;
            var dirtyTerrain = renderer.UpdateTerrainCell(draft, 1);
            if (dirtyTerrain != new Rectangle(48, 0, 48, 48))
            {
                throw new InvalidOperationException($"Unexpected terrain dirty rect: {dirtyTerrain}.");
            }

            using var fullAfterTerrain = composeService.ComposePreview(draft, showTerrain: true, showGrid: true, terrainOpacityPercent: 45);
            AssertSameBitmap(fullAfterTerrain, incrementalBefore, "terrain cell update");

            Console.WriteLine("MAP_CANVAS_PREVIEW_SMOKE_OK");
        }
        finally
        {
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }

    private static void SaveSolidBitmap(string path, int width, int height, Color color)
    {
        using var bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        using (var graphics = Graphics.FromImage(bitmap))
        using (var brush = new SolidBrush(color))
        {
            graphics.FillRectangle(brush, 0, 0, width, height);
        }

        bitmap.Save(path, ImageFormat.Png);
    }

    private static void AssertSameBitmap(Bitmap expected, Bitmap actual, string scenario)
    {
        if (expected.Width != actual.Width || expected.Height != actual.Height)
        {
            throw new InvalidOperationException($"{scenario}: bitmap dimensions differ.");
        }

        for (var y = 0; y < expected.Height; y++)
        {
            for (var x = 0; x < expected.Width; x++)
            {
                if (expected.GetPixel(x, y).ToArgb() != actual.GetPixel(x, y).ToArgb())
                {
                    throw new InvalidOperationException($"{scenario}: pixel differs at {x},{y}.");
                }
            }
        }
    }
}
