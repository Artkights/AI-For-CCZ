using CCZModStudio.Core;
using CCZModStudio.Models;
using System.Drawing;
using System.Drawing.Imaging;

internal partial class Program
{
    static void RunTerrainBuildingStyleSmoke()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "CCZModStudio_TerrainBuildingStyleSmoke_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
        try
        {
            var materialRoot = Path.Combine(tempRoot, "materials");
            Directory.CreateDirectory(materialRoot);
            var basePath = Path.Combine(tempRoot, "M002.png");
            var plainPath = Path.Combine(materialRoot, "plain.png");
            var oldWallPath = Path.Combine(materialRoot, "wall-old.png");
            var newWallPath = Path.Combine(materialRoot, "wall-new.png");
            SaveSolidBitmap(plainPath, 48, 48, Color.FromArgb(60, 150, 70));
            SaveSolidBitmap(basePath, 48 * 3, 48, Color.FromArgb(210, 30, 30));
            SaveBuildingStylePatch(oldWallPath, Color.FromArgb(150, 90, 45));
            SaveBuildingStylePatch(newWallPath, NewWallColor);

            var draft = new MapWorkbenchDraft
            {
                DraftId = "terrain-building-style-smoke",
                BoundMapId = "M002",
                GridWidth = 3,
                GridHeight = 1,
                TileSize = MapResourceItem.MapTilePixelSize,
                MaterialRoot = materialRoot,
                BaseLayerPath = basePath,
                OriginalTerrainCells = [0, 0, 0],
                TerrainCells = [0, 0, 0],
                GenerationMode = MapWorkbenchGenerationModes.TerrainDrivenVisual,
                AutoGenerateMapFromTerrain = true,
                TerrainVisualProfile = new TerrainVisualProfile
                {
                    Seed = "building-style",
                    RedrawChangedCellsOnly = true,
                    UseCurrentMapSamples = false,
                    RegenerateGroundUnderBuildingOverlays = true,
                    BuildingGroundContextRadiusCells = 0,
                    UseGlobalBuildingStyle = true,
                    UseDirectionalBoundaryBlend = false,
                    UseInteriorTextureSynthesis = false,
                    LocalColorTransferStrength = 0f,
                    ColorAlignmentStrength = 0f,
                    TextureNoiseStrength = 0f
                },
                BuildingOverlayCells =
                [
                    MakeBuildingCell(0, oldWallPath),
                    MakeBuildingCell(1, oldWallPath),
                    MakeBuildingCell(2, newWallPath)
                ]
            };
            var materials = new List<MaterialAsset>
            {
                MakeBuildingStyleAsset(plainPath, 0, "plain", "Terrain:0:plain", MaterialAssetTypes.Terrain, MaterialAutoTileModes.Default),
                MakeBuildingStyleAsset(oldWallPath, 15, "old-wall", "Wall:old", MaterialAssetTypes.Building, MaterialAutoTileModes.LinePath),
                MakeBuildingStyleAsset(newWallPath, 15, "new-wall", "Wall:new", MaterialAssetTypes.Building, MaterialAutoTileModes.LinePath)
            };

            using var synthesis = new TerrainVisualSynthesisService();
            using var baseResult = synthesis.Synthesize(new TerrainVisualSynthesisRequest
            {
                Draft = draft,
                Materials = materials
            });
            if (baseResult.Diagnostics.BuildingGroundRedrawCellCount != 3 ||
                baseResult.Diagnostics.BuildingOverlayCellCount != 3)
            {
                throw new InvalidOperationException(
                    $"Building ground redraw diagnostics failed. redraw={baseResult.Diagnostics.BuildingGroundRedrawCellCount}, overlays={baseResult.Diagnostics.BuildingOverlayCellCount}");
            }

            var clearedBase = baseResult.Bitmap.GetPixel(4, 4);
            if (ColorDistance(clearedBase, Color.FromArgb(60, 150, 70)) >= ColorDistance(clearedBase, Color.FromArgb(210, 30, 30)))
            {
                throw new InvalidOperationException($"Building ground was not regenerated before overlay; transparent corner remained too close to old base color {clearedBase}.");
            }

            using var materialDriven = new MaterialDrivenTerrainService();
            using var composed = materialDriven.ComposeVisualMap(draft, materials, checkerboardBlank: true, beautifyTerrain: false);
            for (var cell = 0; cell < 3; cell++)
            {
                var center = composed.GetPixel(cell * 48 + 24, 24);
                if (ColorDistance(center, NewWallColor) > 4)
                {
                    throw new InvalidOperationException($"Building cell {cell} was not unified to the latest wall style. actual={center}");
                }

                var transparentCorner = composed.GetPixel(cell * 48 + 4, 4);
                if (ColorDistance(transparentCorner, Color.FromArgb(60, 150, 70)) >= ColorDistance(transparentCorner, Color.FromArgb(210, 30, 30)))
                {
                    throw new InvalidOperationException($"Building cell {cell} transparent area exposed old base instead of regenerated ground. actual={transparentCorner}");
                }
            }

            Console.WriteLine("TERRAIN_BUILDING_STYLE_SMOKE_OK groundRedraw=ok globalBuildingStyle=ok noOldBaseOverlap=ok");
        }
        finally
        {
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }

    private static readonly Color NewWallColor = Color.FromArgb(45, 120, 190);

    private static MapCellOverride MakeBuildingCell(int index, string path)
        => new()
        {
            Index = index,
            MaterialRelativePath = Path.GetFileName(path),
            MaterialCategory = "15",
            DisplayName = Path.GetFileName(path),
            Source = MapCellOverrideSources.BuildingOverlay
        };

    private static MaterialAsset MakeBuildingStyleAsset(string path, byte terrainId, string name, string styleKey, string assetType, string autoTileMode)
        => new()
        {
            Category = assetType.Equals(MaterialAssetTypes.Building, StringComparison.OrdinalIgnoreCase) ? "building" : "terrain",
            FileName = Path.GetFileName(path),
            HexTag = terrainId.ToString(System.Globalization.CultureInfo.InvariantCulture),
            Description = name,
            AssetType = assetType,
            TerrainId = terrainId,
            TerrainName = name,
            GroupKey = styleKey,
            AutoTileSetKey = styleKey,
            VariantIndex = 0,
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

    private static void SaveBuildingStylePatch(string path, Color color)
    {
        using var bitmap = new Bitmap(48, 48, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bitmap);
        g.Clear(Color.Transparent);
        using var brush = new SolidBrush(color);
        g.FillRectangle(brush, 10, 10, 28, 28);
        bitmap.Save(path, ImageFormat.Png);
    }
}
