using CCZModStudio.Core;
using CCZModStudio.Formats;
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
                AutoGenerateMapFromTerrain = true,
                TerrainCells = [0, 1, 2, 3],
                TerrainBaseCells =
                [
                    new MapCellOverride
                    {
                        Index = 0,
                        MaterialRelativePath = Path.GetFileName(grassPath),
                        MaterialCategory = "terrain",
                        DisplayName = "grass.png",
                        Source = MapCellOverrideSources.TerrainBase
                    }
                ]
            };

            var composeService = new MapCanvasComposeService();
            using var renderer = new MapCanvasPreviewRenderer();
            using var fullBefore = composeService.ComposePreview(draft, showTerrain: false, showGrid: true, terrainOpacityPercent: 0);
            var incrementalBefore = renderer.Rebuild(draft, showTerrain: false, showGrid: true, terrainOpacityPercent: 0);
            AssertSameBitmap(fullBefore, incrementalBefore, "initial rebuild");
            AssertPixelNear(incrementalBefore, 72, 24, Color.FromArgb(20, 30, 40), "unpainted cell keeps real base map");

            draft.TerrainBaseCells =
            [
                draft.TerrainBaseCells[0],
                new MapCellOverride
                {
                    Index = 3,
                    MaterialRelativePath = Path.GetFileName(waterPath),
                    MaterialCategory = "terrain",
                    DisplayName = "water.png",
                    Source = MapCellOverrideSources.TerrainBase
                }
            ];
            var dirtyMap = renderer.UpdateMapCell(draft, 3, draft.TerrainBaseCells[1]);
            if (dirtyMap != new Rectangle(48, 48, 48, 48))
            {
                throw new InvalidOperationException($"Unexpected map dirty rect: {dirtyMap}.");
            }

            using var fullAfterMap = composeService.ComposePreview(draft, showTerrain: false, showGrid: true, terrainOpacityPercent: 0);
            AssertSameBitmap(fullAfterMap, incrementalBefore, "map cell update");

            draft.TerrainCells[1] = 9;
            var dirtyTerrain = renderer.UpdateTerrainCell(draft, 1);
            if (dirtyTerrain != new Rectangle(0, 0, 96, 96))
            {
                throw new InvalidOperationException($"Unexpected terrain dirty rect: {dirtyTerrain}.");
            }

            using var fullAfterTerrain = composeService.ComposePreview(draft, showTerrain: false, showGrid: true, terrainOpacityPercent: 0);
            AssertSameBitmap(fullAfterTerrain, incrementalBefore, "terrain cell update");

            AssertAutoTileNeighborRefresh(tempRoot);
            AssertCustomBeautifyFilter(tempRoot);

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

    private static void AssertCustomBeautifyFilter(string tempRoot)
    {
        using var source = new Bitmap(4, 2, PixelFormat.Format32bppArgb);
        source.SetPixel(0, 0, Color.FromArgb(255, 80, 80, 80));
        source.SetPixel(1, 0, Color.FromArgb(255, 180, 180, 180));
        source.SetPixel(2, 0, Color.FromArgb(255, 120, 90, 60));
        source.SetPixel(3, 0, Color.FromArgb(0, 10, 20, 30));
        source.SetPixel(0, 1, Color.FromArgb(255, 40, 40, 40));
        source.SetPixel(1, 1, Color.FromArgb(255, 230, 230, 230));
        source.SetPixel(2, 1, Color.FromArgb(255, 90, 110, 130));
        source.SetPixel(3, 1, Color.FromArgb(255, 150, 100, 80));

        var service = new TerrainMapBeautifyService();
        var cold = BeautifyCustomFilterSettings.CreateDefault();
        cold.PhotoR = 0.1f;
        cold.PhotoG = 0.2f;
        cold.PhotoB = 1f;
        cold.PhotoDensity = 0.65f;
        cold.BalanceB = 0.25f;
        cold.Saturation = 1.2f;
        cold.PreserveLuminosity = false;

        using var filtered = service.ApplyCustomFilterPreview(source, cold, strength: 3);
        if (filtered.Width != source.Width || filtered.Height != source.Height)
        {
            throw new InvalidOperationException("Custom beautify filter changed bitmap dimensions.");
        }

        if (filtered.GetPixel(3, 0).A != 0)
        {
            throw new InvalidOperationException("Custom beautify filter should preserve alpha.");
        }

        if (filtered.GetPixel(2, 1).B <= source.GetPixel(2, 1).B)
        {
            throw new InvalidOperationException("Cold custom filter should increase blue channel.");
        }

        var highCompress = BeautifyCustomFilterSettings.CreateDefault();
        highCompress.PhotoDensity = 0f;
        highCompress.HighlightCompression = 0.6f;
        using var highCompressed = service.ApplyCustomFilterPreview(source, highCompress, strength: 3);
        if (Luminance(highCompressed.GetPixel(1, 1)) >= Luminance(source.GetPixel(1, 1)))
        {
            throw new InvalidOperationException("Highlight compression should lower bright pixels.");
        }

        var shadowLift = BeautifyCustomFilterSettings.CreateDefault();
        shadowLift.PhotoDensity = 0f;
        shadowLift.ShadowLift = 0.6f;
        using var shadowLifted = service.ApplyCustomFilterPreview(source, shadowLift, strength: 3);
        if (Luminance(shadowLifted.GetPixel(0, 1)) <= Luminance(source.GetPixel(0, 1)))
        {
            throw new InvalidOperationException("Shadow lift should brighten dark pixels.");
        }

        var saturated = BeautifyCustomFilterSettings.CreateDefault();
        saturated.PhotoDensity = 0f;
        saturated.Saturation = 2.2f;
        using var saturatedBitmap = service.ApplyCustomFilterPreview(source, saturated, strength: 3);
        if (SaturationSpread(saturatedBitmap.GetPixel(2, 0)) <= SaturationSpread(source.GetPixel(2, 0)))
        {
            throw new InvalidOperationException("Custom saturation should increase channel spread.");
        }

        var draft = new MapWorkbenchDraft
        {
            DraftId = "custom-filter-smoke",
            GridWidth = 1,
            GridHeight = 1,
            TileSize = MapResourceItem.MapTilePixelSize,
            BeautifyGeneratedMap = true,
            BeautifyStrength = 3,
            BeautifyFilterProfile = TerrainBeautifyFilterProfiles.Custom,
            CustomBeautifyFilter = cold.Clone(),
            TerrainCells = [0]
        };
        using var draftFiltered = service.ApplyFilter(draft, source);
        if (draftFiltered.GetPixel(2, 1).B <= source.GetPixel(2, 1).B)
        {
            throw new InvalidOperationException("Draft custom filter parameters were not applied.");
        }

        var projectRoot = Path.Combine(tempRoot, "custom-filter-project");
        Directory.CreateDirectory(projectRoot);
        var project = new CczProject
        {
            WorkspaceRoot = projectRoot,
            GameRoot = projectRoot,
            HexTableXmlPath = Path.Combine(projectRoot, "HexTable.xml")
        };
        var draftService = new MapDraftService();
        var settings = new MapWorkbenchSettings { DefaultCustomBeautifyFilter = cold.Clone() };
        draftService.SaveSettings(project, settings);
        var loaded = draftService.LoadSettings(project);
        if (loaded.DefaultCustomBeautifyFilter == null ||
            Math.Abs(loaded.DefaultCustomBeautifyFilter.PhotoB - cold.PhotoB) > 0.001f ||
            Math.Abs(loaded.DefaultCustomBeautifyFilter.ShadowLift - cold.ShadowLift) > 0.001f)
        {
            throw new InvalidOperationException("Custom beautify global settings did not round-trip.");
        }

        draftService.SaveDraft(project, draft);
        var loadedDraft = draftService.LoadDraft(project, draft.DraftId);
        if (loadedDraft.CustomBeautifyFilter == null ||
            Math.Abs(loadedDraft.CustomBeautifyFilter.MidtoneGamma - cold.MidtoneGamma) > 0.001f)
        {
            throw new InvalidOperationException("Custom beautify draft settings did not round-trip.");
        }
    }

    private static float Luminance(Color color)
        => color.R * 0.299f + color.G * 0.587f + color.B * 0.114f;

    private static int SaturationSpread(Color color)
        => Math.Max(color.R, Math.Max(color.G, color.B)) - Math.Min(color.R, Math.Min(color.G, color.B));

    private static void AssertAutoTileNeighborRefresh(string tempRoot)
    {
        var materialRoot = Path.Combine(tempRoot, "auto-materials");
        Directory.CreateDirectory(Path.Combine(materialRoot, "地形", "0：平原"));
        Directory.CreateDirectory(Path.Combine(materialRoot, "景物"));
        var wallDir = Path.Combine(materialRoot, "建筑", "15：城墙");
        Directory.CreateDirectory(wallDir);
        var basePath = Path.Combine(tempRoot, "auto-base.png");
        SaveSolidBitmap(basePath, 5 * 48, 4 * 48, Color.FromArgb(42, 42, 42));
        SaveSolidBitmap(Path.Combine(materialRoot, "地形", "0：平原", "1.png"), 48, 48, Color.FromArgb(70, 130, 70));
        SaveSolidBitmap(Path.Combine(materialRoot, "景物", "1.png"), 48, 48, Color.FromArgb(180, 180, 40));
        var sheetPath = Path.Combine(wallDir, "1.png");
        SaveDirectionalWallSheetForCanvasSmoke(sheetPath);
        File.WriteAllText(Path.Combine(wallDir, "hex.txt"), "15\r\n城墙\r\n");
        new MaterialAutoTileMetadataService().RepairGroupDirectory(wallDir);

        var materials = new MaterialLibraryIndexer().IndexExplicitRoot(materialRoot);
        var wall = materials.First(asset =>
            asset.AssetType.Equals(MaterialAssetTypes.Building, StringComparison.OrdinalIgnoreCase) &&
            asset.FileName.Equals("1.png", StringComparison.OrdinalIgnoreCase) &&
            (asset.AutoTileMask ?? 0) == MaterialAutoTileMasks.StraightH);

        var draft = new MapWorkbenchDraft
        {
            DraftId = "autotile-neighbor-refresh",
            GridWidth = 5,
            GridHeight = 4,
            TileSize = MapResourceItem.MapTilePixelSize,
            BaseLayerPath = basePath,
            MaterialRoot = materialRoot,
            AutoGenerateMapFromTerrain = true,
            OriginalTerrainCells = Enumerable.Repeat((byte)0, 20).ToArray(),
            TerrainCells = Enumerable.Repeat((byte)0, 20).ToArray()
        };

        static MapCellOverride Cell(string root, MaterialAsset asset, int index) => new()
        {
            Index = index,
            MaterialRelativePath = MapDraftService.GetMaterialRelativePath(root, asset.FilePath),
            MaterialCategory = asset.Category,
            DisplayName = asset.FileName,
            Source = MapCellOverrideSources.BuildingOverlay
        };

        var topRow = new[] { 1, 2, 3 };
        draft.BuildingOverlayCells = topRow.Select(index => Cell(materialRoot, wall, index)).ToList();
        using var renderer = new MapCanvasPreviewRenderer();
        var incremental = renderer.Rebuild(draft, materials, showTerrain: false, showGrid: false, terrainOpacityPercent: 0);

        foreach (var index in new[] { 6, 8, 11, 12, 13, 16, 17, 18 })
        {
            draft.BuildingOverlayCells.Add(Cell(materialRoot, wall, index));
            renderer.UpdateTerrainMaterialCells(draft, new[] { index });
        }

        using var full = new MaterialDrivenTerrainService().ComposeVisualMap(draft, materials, checkerboardBlank: true, beautifyTerrain: false);
        AssertSameBitmap(full, incremental, "auto-tile neighbor refresh");
        AssertAutoTileCornerDirections(incremental, left: 1, top: 0, expectWest: false, expectEast: true, expectSouth: true, expectNorth: false, "top-left corner after neighbor refresh");
        AssertAutoTileCornerDirections(incremental, left: 3, top: 0, expectWest: true, expectEast: false, expectSouth: true, expectNorth: false, "top-right corner after neighbor refresh");
    }

    private static void SaveDirectionalWallSheetForCanvasSmoke(string path)
    {
        using var bitmap = new Bitmap(48 * 15, 48, PixelFormat.Format32bppArgb);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.Clear(Color.Transparent);
        var order = MaterialAutoTileMetadataService.GetCanonicalMaskOrder();
        using var pen = new Pen(Color.FromArgb(250, 250, 250), 5)
        {
            StartCap = System.Drawing.Drawing2D.LineCap.Square,
            EndCap = System.Drawing.Drawing2D.LineCap.Square
        };

        for (var i = 0; i < order.Length; i++)
        {
            var mask = order[i].Mask;
            var cx = i * 48 + 24;
            const int cy = 24;
            if ((mask & MaterialAutoTileMasks.North) != 0) graphics.DrawLine(pen, cx, cy, cx, 0);
            if ((mask & MaterialAutoTileMasks.East) != 0) graphics.DrawLine(pen, cx, cy, i * 48 + 47, cy);
            if ((mask & MaterialAutoTileMasks.South) != 0) graphics.DrawLine(pen, cx, cy, cx, 47);
            if ((mask & MaterialAutoTileMasks.West) != 0) graphics.DrawLine(pen, cx, cy, i * 48, cy);
        }

        bitmap.Save(path, ImageFormat.Png);
    }

    private static void AssertAutoTileCornerDirections(
        Bitmap bitmap,
        int left,
        int top,
        bool expectWest,
        bool expectEast,
        bool expectSouth,
        bool expectNorth,
        string scenario)
    {
        var tileLeft = left * 48;
        var tileTop = top * 48;
        AssertLinePresence(bitmap.GetPixel(tileLeft + 4, tileTop + 24), expectWest, scenario + " west");
        AssertLinePresence(bitmap.GetPixel(tileLeft + 44, tileTop + 24), expectEast, scenario + " east");
        AssertLinePresence(bitmap.GetPixel(tileLeft + 24, tileTop + 44), expectSouth, scenario + " south");
        AssertLinePresence(bitmap.GetPixel(tileLeft + 24, tileTop + 4), expectNorth, scenario + " north");
    }

    private static void AssertLinePresence(Color color, bool expected, string scenario)
    {
        var bright = color.R > 180 && color.G > 180 && color.B > 180;
        if (bright != expected)
        {
            throw new InvalidOperationException($"{scenario}: expectedLine={expected}, actual={color}.");
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
                    throw new InvalidOperationException($"{scenario}: pixel differs at {x},{y}. expected={expected.GetPixel(x, y)} actual={actual.GetPixel(x, y)}");
                }
            }
        }
    }

    private static void AssertPixelNear(Bitmap bitmap, int x, int y, Color expected, string scenario)
    {
        var actual = bitmap.GetPixel(x, y);
        if (Math.Abs(actual.R - expected.R) > 3 ||
            Math.Abs(actual.G - expected.G) > 3 ||
            Math.Abs(actual.B - expected.B) > 3)
        {
            throw new InvalidOperationException($"{scenario}: expected near {expected}, got {actual} at {x},{y}.");
        }
    }
}
