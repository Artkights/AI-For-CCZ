using CCZModStudio.Core;
using CCZModStudio.Models;
using System.Drawing;
using System.Drawing.Imaging;

internal partial class Program
{
    static void RunTerrainStyleAlignedMapSmoke()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "CCZModStudio_TerrainStyleAlignedMapSmoke_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
        try
        {
            var materialRoot = Path.Combine(tempRoot, "materials");
            var sampleRoot = Path.Combine(tempRoot, "draft-assets", "StyleSamples", "M001");
            Directory.CreateDirectory(materialRoot);

            var basePath = Path.Combine(tempRoot, "M001.png");
            var libraryWaterPath = Path.Combine(materialRoot, "library-water.png");
            var libraryPlainPath = Path.Combine(materialRoot, "library-plain.png");
            SaveTerrainStyleBaseMap(basePath);
            SaveSolidBitmap(libraryWaterPath, 48, 48, Color.FromArgb(225, 40, 190));
            SaveSolidBitmap(libraryPlainPath, 48, 48, Color.FromArgb(80, 170, 70));

            var originalTerrain = BuildStyleSmokeOriginalTerrain();
            var terrainCells = originalTerrain.ToArray();
            terrainCells[24] = 12;

            var materials = new List<MaterialAsset>
            {
                MakeTerrainStyleAsset(libraryWaterPath, 12, "library-water", variantIndex: 0),
                MakeTerrainStyleAsset(libraryPlainPath, 0, "library-plain", variantIndex: 1)
            };

            var draft = new MapWorkbenchDraft
            {
                DraftId = "terrain-style-aligned-smoke",
                BoundMapId = "M001",
                GridWidth = 5,
                GridHeight = 5,
                TileSize = MapResourceItem.MapTilePixelSize,
                BaseLayerPath = basePath,
                MaterialRoot = materialRoot,
                OriginalTerrainCells = originalTerrain,
                TerrainCells = terrainCells,
                GenerationMode = MapWorkbenchGenerationModes.TerrainDrivenVisual,
                TerrainVisualProfile = new TerrainVisualProfile
                {
                    Seed = "style-smoke",
                    UseCurrentMapSamples = true,
                    AutoExtractCurrentMapSamples = true,
                    RedrawChangedCellsOnly = true,
                    EdgeFeatherRadius = 6,
                    BlendStrength = 2,
                    ColorAlignmentStrength = 0.65f,
                    TextureNoiseStrength = 0f,
                    StyleSampleRoot = sampleRoot
                },
                TerrainBaseCells =
                [
                    new MapCellOverride
                    {
                        Index = 0,
                        MaterialRelativePath = Path.GetFileName(libraryWaterPath),
                        MaterialCategory = "terrain",
                        DisplayName = "library-water.png",
                        Source = MapCellOverrideSources.TerrainBase
                    }
                ]
            };

            var styleService = new CurrentMapStyleProfileService();
            var styleProfile = styleService.BuildProfile(draft, sampleRoot, writeSamples: true);
            var waterProfile = styleProfile.FindTerrain(12) ?? throw new InvalidOperationException("Current map style profile did not include terrain 12.");
            if (styleProfile.SampleCount == 0 || waterProfile.Samples.Count == 0 || !waterProfile.Samples.Any(sample => File.Exists(sample.FilePath)))
            {
                throw new InvalidOperationException("Current map style samples were not written to the draft-private sample root.");
            }

            using var synthesis = new TerrainVisualSynthesisService();
            using var result = synthesis.Synthesize(new TerrainVisualSynthesisRequest
            {
                Draft = draft,
                Materials = materials,
                StyleProfile = styleProfile
            });

            if (!result.Diagnostics.UsedCurrentMapStyle || result.Diagnostics.StyleSampleCount == 0)
            {
                throw new InvalidOperationException("Terrain visual synthesis did not report current-map style usage.");
            }

            AssertTerrainStyleDimensions(result.Bitmap, 5 * 48, 5 * 48);
            AssertColorCloser(
                result.Bitmap.GetPixel(4 * 48 + 24, 4 * 48 + 24),
                StyleWaterColor,
                Color.FromArgb(225, 40, 190),
                "changed water cell should remain closer to current-map sample than material library after local color transfer");
            AssertColorNear(result.Bitmap.GetPixel(4 * 48 + 24, 0 * 48 + 24), StylePlainColor, "untouched distant cell should preserve original base pixels");
            if (result.Diagnostics.RegionCount == 0 ||
                result.Diagnostics.RegionLockedMaterialCount == 0 ||
                result.Diagnostics.MixedTerrainCellCount == 0 ||
                result.Diagnostics.BoundaryMaskPixelCount == 0 ||
                result.Diagnostics.LocalColorTransferPixelCount == 0)
            {
                throw new InvalidOperationException(
                    "Terrain style synthesis should report region locks, directional boundary mixing, and local color transfer. " +
                    $"regions={result.Diagnostics.RegionCount}, locked={result.Diagnostics.RegionLockedMaterialCount}, mixed={result.Diagnostics.MixedTerrainCellCount}, maskPixels={result.Diagnostics.BoundaryMaskPixelCount}, colorPixels={result.Diagnostics.LocalColorTransferPixelCount}");
            }

            using var repeat = synthesis.Synthesize(new TerrainVisualSynthesisRequest
            {
                Draft = draft,
                Materials = materials,
                StyleProfile = styleProfile
            });
            AssertSameBitmap(result.Bitmap, repeat.Bitmap, "same seed terrain style synthesis");

            var materialDriven = new MaterialDrivenTerrainService();
            var derived = materialDriven.DeriveTerrainCells(draft, materials);
            if (derived[0] != draft.TerrainCells[0])
            {
                throw new InvalidOperationException("TerrainDrivenVisual mode allowed material layers to override TerrainCells.");
            }

            var fallbackDraft = new MapWorkbenchDraft
            {
                DraftId = "terrain-style-fallback-smoke",
                GridWidth = 2,
                GridHeight = 1,
                TileSize = MapResourceItem.MapTilePixelSize,
                TerrainCells = [27, 27],
                GenerationMode = MapWorkbenchGenerationModes.TerrainDrivenVisual,
                TerrainVisualProfile = new TerrainVisualProfile
                {
                    RedrawChangedCellsOnly = false,
                    UseCurrentMapSamples = false,
                    EdgeFeatherRadius = 4,
                    BlendStrength = 1
                }
            };
            using var fallback = synthesis.Synthesize(new TerrainVisualSynthesisRequest
            {
                Draft = fallbackDraft,
                Materials = Array.Empty<MaterialAsset>()
            });
            AssertTerrainStyleDimensions(fallback.Bitmap, 96, 48);
            if (fallback.Diagnostics.FallbackCellCount != fallbackDraft.CellCount || fallback.Diagnostics.MissingTerrainIds.Count != 1)
            {
                throw new InvalidOperationException("Missing terrain fallback diagnostics were not reported.");
            }

            AssertTerrainStyleRegionLocksOneMaterialGroup(tempRoot);

            Console.WriteLine("TERRAIN_STYLE_ALIGNED_MAP_SMOKE_OK samples=ok priority=ok changedOnly=ok modeIsolation=ok fallback=ok regionLock=ok boundaryBlend=ok localColor=ok");
        }
        finally
        {
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }

    private static readonly Color StyleWaterColor = Color.FromArgb(25, 90, 185);
    private static readonly Color StylePlainColor = Color.FromArgb(75, 160, 70);

    private static byte[] BuildStyleSmokeOriginalTerrain()
    {
        var result = new byte[25];
        for (var y = 0; y < 5; y++)
        {
            for (var x = 0; x < 5; x++)
            {
                result[y * 5 + x] = x < 3 ? (byte)12 : (byte)0;
            }
        }

        return result;
    }

    private static void SaveTerrainStyleBaseMap(string path)
    {
        using var bitmap = new Bitmap(5 * 48, 5 * 48, PixelFormat.Format32bppArgb);
        using var graphics = Graphics.FromImage(bitmap);
        using var waterBrush = new SolidBrush(StyleWaterColor);
        using var plainBrush = new SolidBrush(StylePlainColor);
        for (var y = 0; y < 5; y++)
        {
            for (var x = 0; x < 5; x++)
            {
                graphics.FillRectangle(x < 3 ? waterBrush : plainBrush, x * 48, y * 48, 48, 48);
            }
        }

        bitmap.Save(path, ImageFormat.Png);
    }

    private static MaterialAsset MakeTerrainStyleAsset(string path, byte terrainId, string name, int variantIndex)
        => new()
        {
            Category = "library",
            FileName = Path.GetFileName(path),
            HexTag = terrainId.ToString(System.Globalization.CultureInfo.InvariantCulture),
            Description = name,
            AssetType = MaterialAssetTypes.Terrain,
            TerrainId = terrainId,
            TerrainName = name,
            GroupKey = $"Terrain:{terrainId}:{name}",
            AutoTileSetKey = $"Terrain:{terrainId}:{name}",
            VariantIndex = variantIndex,
            AutoTileRole = MaterialAutoTileRoles.Default,
            AutoTileMask = MaterialAutoTileMasks.None,
            AutoTileMode = MaterialAutoTileModes.Default,
            AutoTilePriority = 0,
            SourceX = 0,
            SourceY = 0,
            SourceWidth = 48,
            SourceHeight = 48,
            Width = 48,
            Height = 48,
            SizeBytes = new FileInfo(path).Length,
            FilePath = path
        };

    private static void AssertTerrainStyleDimensions(Bitmap bitmap, int expectedWidth, int expectedHeight)
    {
        if (bitmap.Width != expectedWidth || bitmap.Height != expectedHeight)
        {
            throw new InvalidOperationException($"Terrain style bitmap dimensions differ: {bitmap.Width}x{bitmap.Height}, expected {expectedWidth}x{expectedHeight}.");
        }
    }

    private static void AssertColorCloser(Color actual, Color expectedCloser, Color expectedFarther, string scenario)
    {
        if (ColorDistance(actual, expectedCloser) >= ColorDistance(actual, expectedFarther))
        {
            throw new InvalidOperationException($"{scenario}: actual {actual} was not closer to {expectedCloser} than {expectedFarther}.");
        }
    }

    private static void AssertTerrainStyleRegionLocksOneMaterialGroup(string tempRoot)
    {
        var materialRoot = Path.Combine(tempRoot, "region-lock-materials");
        Directory.CreateDirectory(materialRoot);
        var firstPath = Path.Combine(materialRoot, "fence-a.png");
        var secondPath = Path.Combine(materialRoot, "fence-b.png");
        SaveSolidBitmap(firstPath, 48, 48, Color.FromArgb(120, 80, 45));
        SaveSolidBitmap(secondPath, 48, 48, Color.FromArgb(40, 120, 180));
        var draft = new MapWorkbenchDraft
        {
            DraftId = "terrain-style-region-lock-smoke",
            GridWidth = 3,
            GridHeight = 1,
            TileSize = MapResourceItem.MapTilePixelSize,
            TerrainCells = [14, 14, 14],
            GenerationMode = MapWorkbenchGenerationModes.TerrainDrivenVisual,
            TerrainVisualProfile = new TerrainVisualProfile
            {
                RedrawChangedCellsOnly = false,
                UseCurrentMapSamples = false,
                UseRegionConsistentMaterial = true,
                UseDirectionalBoundaryBlend = false,
                LocalColorTransferStrength = 0f,
                ColorAlignmentStrength = 0f,
                TextureNoiseStrength = 0f,
                Seed = "region-lock"
            }
        };
        var materials = new List<MaterialAsset>
        {
            MakeTerrainStyleAsset(firstPath, 14, "fence-a", variantIndex: 0),
            MakeTerrainStyleAsset(secondPath, 14, "fence-b", variantIndex: 1)
        };
        using var synthesis = new TerrainVisualSynthesisService();
        using var result = synthesis.Synthesize(new TerrainVisualSynthesisRequest
        {
            Draft = draft,
            Materials = materials
        });
        var first = result.Bitmap.GetPixel(24, 24);
        var second = result.Bitmap.GetPixel(48 + 24, 24);
        var third = result.Bitmap.GetPixel(96 + 24, 24);
        if (ColorDistance(first, second) > 3 || ColorDistance(second, third) > 3)
        {
            throw new InvalidOperationException("Region material locking should keep a connected structure region on one material group.");
        }

        if (result.Diagnostics.RegionCount != 1 || result.Diagnostics.RegionLockedMaterialCount != 1)
        {
            throw new InvalidOperationException($"Region lock diagnostics were not reported. regions={result.Diagnostics.RegionCount}, locked={result.Diagnostics.RegionLockedMaterialCount}");
        }
    }
}
