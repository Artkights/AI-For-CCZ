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
            var visualFallbackPath = Path.Combine(buildingDir, "1.png");
            var hexFallbackPath = Path.Combine(buildingDir, "2.png");
            SaveSolidBitmap(plainPath, 48, 48, Color.FromArgb(60, 170, 80));
            SaveSolidBitmap(plainVariantPath, 48, 48, Color.FromArgb(80, 190, 95));
            SaveSolidBitmap(waterPath, 48, 48, Color.FromArgb(35, 85, 190));
            SaveSolidBitmap(buildingPath, 48, 48, Color.FromArgb(180, 80, 60));
            SaveSolidBitmap(visualFallbackPath, 48, 48, Color.FromArgb(210, 50, 170));
            SaveSolidBitmap(hexFallbackPath, 48, 48, Color.FromArgb(230, 220, 40));

            File.WriteAllLines(Path.Combine(terrainDir, "hex.txt"),
            [
                "0", "平原",
                "0", "平原变体",
                "12", "河流"
            ]);
            File.WriteAllLines(Path.Combine(buildingDir, "hex.txt"),
            [
                "0", "城池",
                "22", "城门",
                "0E", "围墙"
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
            var initialPlan = draft.TerrainMaterialPlan
                .Select(item => item.MaterialRelativePath)
                .ToList();
            if (first.Count != draft.CellCount || second.Count != draft.CellCount)
            {
                throw new InvalidOperationException("Generated map cell count mismatch.");
            }
            if (!first.Select(cell => cell.MaterialRelativePath).SequenceEqual(second.Select(cell => cell.MaterialRelativePath)))
            {
                throw new InvalidOperationException("Stable terrain variant selection mismatch.");
            }
            if (draft.TerrainMaterialPlan.Count != 2)
            {
                throw new InvalidOperationException("Terrain material plan was not created for each used terrain family.");
            }
            if (!initialPlan.SequenceEqual(draft.TerrainMaterialPlan.Select(item => item.MaterialRelativePath)))
            {
                throw new InvalidOperationException("Terrain material plan changed during repeated generation.");
            }
            if (first.Where(cell => draft.TerrainCells[cell.Index] == 0)
                .Select(cell => cell.MaterialRelativePath)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count() != 1)
            {
                throw new InvalidOperationException("Same terrain id did not use one stable main material.");
            }

            generator.SetManualPlanItem(draft, 0, plainAssets[1]);
            var manualCells = generator.GenerateMapCells(draft, materials).ToList();
            var manualPath = MapDraftService.GetMaterialRelativePath(materialRoot, plainAssets[1].FilePath);
            if (draft.TerrainMaterialPlan.First(item => item.TerrainId == 0).SelectionMode != TerrainMaterialSelectionModes.Manual ||
                manualCells.Where(cell => draft.TerrainCells[cell.Index] == 0).Any(cell => !cell.MaterialRelativePath.Equals(manualPath, StringComparison.OrdinalIgnoreCase)))
            {
                throw new InvalidOperationException("Manual terrain material plan was not respected.");
            }

            var missingManual = draft.TerrainMaterialPlan.First(item => item.TerrainId == 0);
            missingManual.MaterialRelativePath = "missing-manual.png";
            generator.EnsureMaterialPlan(draft, materials);
            if (!missingManual.SelectionMode.Equals(TerrainMaterialSelectionModes.MissingManual, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Missing manual material was not reported as MissingManual.");
            }
            var missingManualCells = generator.GenerateMapCells(draft, materials).ToList();
            if (missingManualCells.Any(cell => draft.TerrainCells[cell.Index] == 0))
            {
                throw new InvalidOperationException("Missing manual material was silently replaced by an auto material.");
            }
            generator.ResetPlanItemToAuto(draft, 0, materials);

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

            if (!MaterialHexTagParser.TryParseTerrainId("0x16", out var prefixedHex) || prefixedHex != 0x16 ||
                !MaterialHexTagParser.TryParseTerrainId("0E", out var bareHex) || bareHex != 0x0E ||
                !MaterialHexTagParser.TryParseTerrainId("13", out var decimalValue) || decimalValue != 13)
            {
                throw new InvalidOperationException("Material HexTag parser failed decimal/hex coverage.");
            }

            var visualFallbackDraft = new MapWorkbenchDraft
            {
                DraftId = "terrain-visual-fallback-smoke",
                GridWidth = 2,
                GridHeight = 1,
                TileSize = MapResourceItem.MapTilePixelSize,
                MaterialRoot = materialRoot,
                TerrainCells = [22, 14],
                AutoGenerateMapFromTerrain = true,
                BeautifyGeneratedMap = false
            };
            var diagnostics = generator.Analyze(visualFallbackDraft, materials);
            if (diagnostics.MatchedCellCount != visualFallbackDraft.CellCount || diagnostics.FallbackCellCount != 0)
            {
                throw new InvalidOperationException("Non-terrain material HexTag fallback did not match all cells.");
            }

            using var visualFallback = generator.RenderBaseTerrain(visualFallbackDraft, materials);
            var fallbackPixel = visualFallback.GetPixel(24, 24);
            if (fallbackPixel.R < 180 || fallbackPixel.G > 90 || fallbackPixel.B < 130)
            {
                throw new InvalidOperationException("Building-category visual fallback did not render for terrain id 22.");
            }
            var hexPixel = visualFallback.GetPixel(72, 24);
            if (hexPixel.R < 180 || hexPixel.G < 170 || hexPixel.B > 90)
            {
                throw new InvalidOperationException("Bare hexadecimal HexTag fallback did not render for terrain id 14.");
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
            using var fallback = generator.RenderBaseTerrain(fallbackDraft, Array.Empty<MaterialAsset>());
            AssertTerrainDrivenDimensions(fallback, 96, 96);

            using var renderer = new MapCanvasPreviewRenderer();
            draft.BeautifyGeneratedMap = false;
            renderer.Rebuild(draft, materials, showTerrain: false, showGrid: false, terrainOpacityPercent: 0);
            draft.TerrainCells[0] = 12;
            var dirtyRect = renderer.UpdateTerrainCell(draft, 0);
            if (dirtyRect.IsEmpty || !dirtyRect.Contains(new Rectangle(0, 0, 48, 48)) || dirtyRect.Width >= draft.PixelWidth || dirtyRect.Height >= draft.PixelHeight)
            {
                throw new InvalidOperationException("Auto-generated terrain edit did not stay in a localized dirty region.");
            }
            var flushedRect = renderer.RefreshDirtyBaseMap(draft, materials);
            if (flushedRect.IsEmpty || flushedRect.Width >= draft.PixelWidth || flushedRect.Height >= draft.PixelHeight)
            {
                throw new InvalidOperationException("Dirty base map refresh did not stay localized.");
            }

            draft.TerrainCells[0] = 0;
            draft.GeneratedMapCells = generator.GenerateMapCells(draft, materials).ToList();
            var materialDirtyRect = renderer.UpdateTerrainMaterialCells(draft, new[] { 0, 1 });
            if (materialDirtyRect.IsEmpty || materialDirtyRect.Width >= draft.PixelWidth || materialDirtyRect.Height >= draft.PixelHeight)
            {
                throw new InvalidOperationException("Terrain material plan preview update did not stay localized.");
            }

            Console.WriteLine("TERRAIN_DRIVEN_MAP_SMOKE_OK generated=16 fallback=ok feather=ok buildingOverlay=ok visualFallback=ok hexTag=ok");
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
