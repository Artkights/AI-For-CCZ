using CCZModStudio.Core;
using CCZModStudio.Formats;
using CCZModStudio.Models;
using System.Drawing;
using System.Drawing.Imaging;

internal partial class Program
{
    static void RunTerrainDrivenMapSmoke()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "CCZModStudio_TerrainDrivenMapSmoke_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
        try
        {
            var materialRoot = Path.Combine(tempRoot, "素材库");
            var terrainDir = Path.Combine(materialRoot, "地形");
            var buildingDir = Path.Combine(materialRoot, "建筑");
            Directory.CreateDirectory(terrainDir);
            Directory.CreateDirectory(buildingDir);

            var plainPath = Path.Combine(terrainDir, "0.png");
            var plainVariantPath = Path.Combine(terrainDir, "1.png");
            var waterPath = Path.Combine(terrainDir, "2.png");
            var buildingPath = Path.Combine(buildingDir, "0.png");
            SaveSolidBitmap(plainPath, 48, 48, Color.FromArgb(60, 170, 80));
            SaveSolidBitmap(plainVariantPath, 48, 48, Color.FromArgb(80, 190, 95));
            SaveSolidBitmap(waterPath, 48, 48, Color.FromArgb(35, 85, 190));
            SaveSolidBitmap(buildingPath, 48, 48, Color.FromArgb(180, 80, 60));

            File.WriteAllLines(Path.Combine(terrainDir, "hex.txt"),
            [
                "0", "平原",
                "0", "平原变体",
                "12", "河流"
            ]);
            File.WriteAllLines(Path.Combine(buildingDir, "hex.txt"),
            [
                "0", "城池"
            ]);

            var materials = new MaterialLibraryIndexer().IndexExplicitRoot(materialRoot);
            var plainAssets = materials.Where(asset => asset.Category == "地形" && asset.HexTag == "0").ToList();
            if (plainAssets.Count != 2 || plainAssets[0].Description != "平原")
            {
                throw new InvalidOperationException("hex.txt two-line terrain mapping failed.");
            }

            var draft = new MapWorkbenchDraft
            {
                DraftId = "terrain-driven-smoke",
                GridWidth = 4,
                GridHeight = 4,
                TileSize = MapResourceItem.MapTilePixelSize,
                MaterialRoot = materialRoot,
                TerrainCells =
                [
                    0, 0, 12, 12,
                    0, 0, 12, 12,
                    0, 0, 12, 12,
                    0, 0, 12, 12
                ],
                AutoGenerateMapFromTerrain = true,
                BeautifyGeneratedMap = true,
                BeautifyStrength = 2,
                FeatherRadius = 8
            };

            var generator = new TerrainDrivenMapGenerationService();
            var first = generator.GenerateMapCells(draft, materials).ToList();
            var second = generator.GenerateMapCells(draft, materials).ToList();
            if (first.Count != draft.CellCount || second.Count != draft.CellCount)
            {
                throw new InvalidOperationException("Generated map cell count mismatch.");
            }
            if (!first.Select(cell => cell.MaterialRelativePath).SequenceEqual(second.Select(cell => cell.MaterialRelativePath)))
            {
                throw new InvalidOperationException("Stable terrain variant selection mismatch.");
            }

            draft.GeneratedMapCells = first;
            draft.BuildingOverlayCells =
            [
                new MapCellOverride
                {
                    Index = 5,
                    MaterialRelativePath = MapDraftService.GetMaterialRelativePath(materialRoot, buildingPath),
                    MaterialCategory = "建筑",
                    DisplayName = "0.png",
                    Source = MapCellOverrideSources.BuildingOverlay
                }
            ];
            var beforeTerrain = draft.TerrainCells.ToArray();

            var compose = new MapCanvasComposeService();
            draft.BeautifyGeneratedMap = false;
            using var baseImage = compose.ComposePreview(draft, materials, showTerrain: false, showGrid: false, terrainOpacityPercent: 0, beautifyGeneratedMap: false);
            draft.BeautifyGeneratedMap = true;
            using var beautified = compose.ComposePreview(draft, materials, showTerrain: false, showGrid: false, terrainOpacityPercent: 0, beautifyGeneratedMap: true);
            using var terrainLayer = compose.ComposeTerrainLayerPreview(draft, showGrid: true);
            AssertTerrainDrivenDimensions(beautified, 4 * 48, 4 * 48);
            AssertTerrainDrivenDimensions(terrainLayer, 4 * 48, 4 * 48);
            AssertPixelDifferent(baseImage, beautified, 95, 24, "feathered terrain edge");
            if (!beforeTerrain.SequenceEqual(draft.TerrainCells))
            {
                throw new InvalidOperationException("Building overlay modified TerrainCells.");
            }

            var buildingPixel = beautified.GetPixel(5 % draft.GridWidth * 48 + 24, 5 / draft.GridWidth * 48 + 24);
            if (buildingPixel.R < 140 || buildingPixel.G > 120 || buildingPixel.B > 120)
            {
                throw new InvalidOperationException("Building overlay did not render above terrain.");
            }

            var fallbackDraft = new MapWorkbenchDraft
            {
                DraftId = "terrain-fallback-smoke",
                GridWidth = 2,
                GridHeight = 2,
                TileSize = MapResourceItem.MapTilePixelSize,
                TerrainCells = [27, 27, 27, 27],
                AutoGenerateMapFromTerrain = true,
                BeautifyGeneratedMap = true
            };
            using var fallback = compose.ComposeFinal(fallbackDraft, Array.Empty<MaterialAsset>());
            AssertTerrainDrivenDimensions(fallback, 96, 96);

            using var renderer = new MapCanvasPreviewRenderer();
            draft.BeautifyGeneratedMap = false;
            renderer.Rebuild(draft, materials, showTerrain: false, showGrid: false, terrainOpacityPercent: 0);
            draft.TerrainCells[0] = 12;
            var dirtyRect = renderer.UpdateTerrainCell(draft, 0);
            if (dirtyRect != new Rectangle(0, 0, 48, 48))
            {
                throw new InvalidOperationException("Auto-generated preview did not refresh when terrain overlay is hidden.");
            }

            Console.WriteLine("TERRAIN_DRIVEN_MAP_SMOKE_OK generated=16 fallback=ok feather=ok buildingOverlay=ok");
        }
        finally
        {
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }

    private static void AssertTerrainDrivenDimensions(Bitmap bitmap, int expectedWidth, int expectedHeight)
    {
        if (bitmap.Width != expectedWidth || bitmap.Height != expectedHeight)
        {
            throw new InvalidOperationException($"Terrain-driven bitmap dimensions differ: {bitmap.Width}x{bitmap.Height}, expected {expectedWidth}x{expectedHeight}.");
        }
    }

    private static void AssertPixelDifferent(Bitmap left, Bitmap right, int x, int y, string scenario)
    {
        if (left.GetPixel(x, y).ToArgb() == right.GetPixel(x, y).ToArgb())
        {
            throw new InvalidOperationException($"{scenario}: expected pixel to differ at {x},{y}.");
        }
    }
}
