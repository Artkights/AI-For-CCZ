using CCZModStudio.Core;
using CCZModStudio.Models;
using System.Drawing;
using System.Drawing.Imaging;

internal partial class Program
{
    static void RunTerrainInteriorNaturalizationSmoke()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "CCZModStudio_TerrainInteriorNaturalizationSmoke_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
        try
        {
            var materialRoot = Path.Combine(tempRoot, "materials");
            Directory.CreateDirectory(materialRoot);
            var grassA = Path.Combine(materialRoot, "grass-a.png");
            var grassB = Path.Combine(materialRoot, "grass-b.png");
            var wall = Path.Combine(materialRoot, "wall.png");
            SaveInteriorNaturalPatch(grassA, Color.FromArgb(66, 132, 48), Color.FromArgb(110, 156, 58), diagonal: false);
            SaveInteriorNaturalPatch(grassB, Color.FromArgb(82, 122, 42), Color.FromArgb(128, 146, 70), diagonal: true);
            SaveInteriorWallPatch(wall);

            var grassDraft = new MapWorkbenchDraft
            {
                DraftId = "terrain-interior-natural-smoke",
                GridWidth = 4,
                GridHeight = 3,
                TileSize = MapResourceItem.MapTilePixelSize,
                MaterialRoot = materialRoot,
                TerrainCells = Enumerable.Repeat((byte)1, 12).ToArray(),
                GenerationMode = MapWorkbenchGenerationModes.TerrainDrivenVisual,
                TerrainVisualProfile = new TerrainVisualProfile
                {
                    Seed = "interior-natural",
                    RedrawChangedCellsOnly = false,
                    UseCurrentMapSamples = false,
                    UseDirectionalBoundaryBlend = false,
                    LocalColorTransferStrength = 0f,
                    ColorAlignmentStrength = 0f,
                    TextureNoiseStrength = 0f,
                    UseInteriorTextureSynthesis = true,
                    EnableNaturalTileTransforms = true,
                    UseInteriorSeamBlend = true,
                    InteriorSecondaryBlendStrength = 0.22f,
                    RegionTextureUnifyStrength = 0.18f,
                    RegionNoiseScalePixels = 96
                }
            };
            var grassMaterials = new List<MaterialAsset>
            {
                MakeInteriorTerrainAsset(grassA, 1, "grass-a", "Terrain:1:grass", 0, MaterialAutoTileModes.Default),
                MakeInteriorTerrainAsset(grassB, 1, "grass-b", "Terrain:1:grass", 1, MaterialAutoTileModes.Default)
            };

            using var synthesis = new TerrainVisualSynthesisService();
            using var grassResult = synthesis.Synthesize(new TerrainVisualSynthesisRequest
            {
                Draft = grassDraft,
                Materials = grassMaterials
            });
            if (grassResult.Diagnostics.NaturalizedRegionCount != 1 ||
                grassResult.Diagnostics.SecondaryPatchBlendPixelCount == 0 ||
                grassResult.Diagnostics.InteriorSeamBlendPixelCount == 0 ||
                grassResult.Diagnostics.TileTransformCount == 0)
            {
                throw new InvalidOperationException(
                    "Natural terrain interior synthesis did not report naturalization, secondary blending, seams, and transforms. " +
                    $"regions={grassResult.Diagnostics.NaturalizedRegionCount}, secondary={grassResult.Diagnostics.SecondaryPatchBlendPixelCount}, seam={grassResult.Diagnostics.InteriorSeamBlendPixelCount}, transforms={grassResult.Diagnostics.TileTransformCount}");
            }

            var distinctCenters = CountDistinctTileCenterColors(grassResult.Bitmap, grassDraft.GridWidth, grassDraft.GridHeight);
            if (distinctCenters < 3)
            {
                throw new InvalidOperationException($"Natural terrain should vary inside the same region; distinct center colors={distinctCenters}.");
            }

            using var repeat = synthesis.Synthesize(new TerrainVisualSynthesisRequest
            {
                Draft = grassDraft,
                Materials = grassMaterials
            });
            AssertSameBitmap(grassResult.Bitmap, repeat.Bitmap, "terrain interior synthesis same seed");

            var wallDraft = new MapWorkbenchDraft
            {
                DraftId = "terrain-interior-wall-smoke",
                GridWidth = 3,
                GridHeight = 1,
                TileSize = MapResourceItem.MapTilePixelSize,
                MaterialRoot = materialRoot,
                TerrainCells = [15, 15, 15],
                GenerationMode = MapWorkbenchGenerationModes.TerrainDrivenVisual,
                TerrainVisualProfile = grassDraft.TerrainVisualProfile.Clone()
            };
            wallDraft.TerrainVisualProfile.Seed = "interior-wall";
            var wallMaterials = new List<MaterialAsset>
            {
                MakeInteriorTerrainAsset(wall, 15, "wall", "Terrain:15:wall", 0, MaterialAutoTileModes.LinePath)
            };
            using var wallResult = synthesis.Synthesize(new TerrainVisualSynthesisRequest
            {
                Draft = wallDraft,
                Materials = wallMaterials
            });
            if (wallResult.Diagnostics.NaturalizedRegionCount != 0 ||
                wallResult.Diagnostics.TileTransformCount != 0 ||
                wallResult.Diagnostics.SecondaryPatchBlendPixelCount != 0 ||
                wallResult.Diagnostics.StructureTransformSkippedCount != wallDraft.CellCount)
            {
                throw new InvalidOperationException(
                    "Structure terrain should keep auto-tile connection rules and skip random interior transforms. " +
                    $"natural={wallResult.Diagnostics.NaturalizedRegionCount}, transforms={wallResult.Diagnostics.TileTransformCount}, secondary={wallResult.Diagnostics.SecondaryPatchBlendPixelCount}, skipped={wallResult.Diagnostics.StructureTransformSkippedCount}");
            }

            Console.WriteLine("TERRAIN_INTERIOR_NATURALIZATION_SMOKE_OK naturalization=ok seams=ok deterministic=ok structureSkip=ok");
        }
        finally
        {
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }

    private static MaterialAsset MakeInteriorTerrainAsset(string path, byte terrainId, string name, string groupKey, int variantIndex, string autoTileMode)
        => new()
        {
            Category = "library",
            FileName = Path.GetFileName(path),
            HexTag = terrainId.ToString(System.Globalization.CultureInfo.InvariantCulture),
            Description = name,
            AssetType = MaterialAssetTypes.Terrain,
            TerrainId = terrainId,
            TerrainName = name,
            GroupKey = groupKey,
            AutoTileSetKey = groupKey,
            VariantIndex = variantIndex,
            AutoTileRole = MaterialAutoTileRoles.Default,
            AutoTileMask = MaterialAutoTileMasks.None,
            AutoTileMode = autoTileMode,
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

    private static void SaveInteriorNaturalPatch(string path, Color low, Color high, bool diagonal)
    {
        using var bitmap = new Bitmap(48, 48, PixelFormat.Format32bppArgb);
        for (var y = 0; y < bitmap.Height; y++)
        {
            for (var x = 0; x < bitmap.Width; x++)
            {
                var wave = diagonal
                    ? (Math.Sin((x + y) / 5.0) + 1.0) * 0.5
                    : (Math.Sin(x / 4.0) * 0.35 + Math.Cos(y / 6.0) * 0.35 + 0.5);
                var amount = Math.Clamp(wave, 0.0, 1.0);
                bitmap.SetPixel(
                    x,
                    y,
                    Color.FromArgb(
                        255,
                        Lerp(low.R, high.R, amount),
                        Lerp(low.G, high.G, amount),
                        Lerp(low.B, high.B, amount)));
            }
        }

        bitmap.Save(path, ImageFormat.Png);
    }

    private static void SaveInteriorWallPatch(string path)
    {
        using var bitmap = new Bitmap(48, 48, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bitmap);
        g.Clear(Color.FromArgb(100, 72, 45));
        using var mortar = new Pen(Color.FromArgb(50, 38, 28), 3);
        g.DrawLine(mortar, 0, 12, 47, 12);
        g.DrawLine(mortar, 0, 28, 47, 28);
        g.DrawLine(mortar, 12, 0, 12, 47);
        g.DrawLine(mortar, 32, 0, 32, 47);
        bitmap.Save(path, ImageFormat.Png);
    }

    private static int CountDistinctTileCenterColors(Bitmap bitmap, int gridWidth, int gridHeight)
    {
        var colors = new HashSet<int>();
        for (var y = 0; y < gridHeight; y++)
        {
            for (var x = 0; x < gridWidth; x++)
            {
                colors.Add(bitmap.GetPixel(x * 48 + 24, y * 48 + 24).ToArgb());
            }
        }

        return colors.Count;
    }

    private static int Lerp(int left, int right, double amount)
        => Math.Clamp((int)Math.Round(left + (right - left) * amount), 0, 255);
}
