using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using CCZModStudio.Models;

namespace CCZModStudio.Core;

public sealed class MapCanvasComposeService
{
    private readonly MaterialDrivenTerrainService _materialDrivenService = new();

    public Bitmap ComposeFinal(MapWorkbenchDraft draft)
        => Compose(draft, Array.Empty<MaterialAsset>(), showTerrain: false, showGrid: false, terrainOpacityPercent: 0, checkerboardBlank: false, beautifyGeneratedMap: draft.BeautifyGeneratedMap);

    public Bitmap ComposeFinal(MapWorkbenchDraft draft, IReadOnlyList<MaterialAsset> materials)
        => Compose(draft, materials, showTerrain: false, showGrid: false, terrainOpacityPercent: 0, checkerboardBlank: false, beautifyGeneratedMap: draft.BeautifyGeneratedMap);

    public Bitmap ComposePreview(MapWorkbenchDraft draft, bool showTerrain, bool showGrid, int terrainOpacityPercent)
        => Compose(draft, Array.Empty<MaterialAsset>(), showTerrain, showGrid, terrainOpacityPercent, checkerboardBlank: true, beautifyGeneratedMap: draft.BeautifyGeneratedMap);

    public Bitmap ComposePreview(MapWorkbenchDraft draft, IReadOnlyList<MaterialAsset> materials, bool showTerrain, bool showGrid, int terrainOpacityPercent)
        => Compose(draft, materials, showTerrain, showGrid, terrainOpacityPercent, checkerboardBlank: true, beautifyGeneratedMap: draft.BeautifyGeneratedMap);

    public Bitmap ComposePreview(
        MapWorkbenchDraft draft,
        IReadOnlyList<MaterialAsset> materials,
        bool showTerrain,
        bool showGrid,
        int terrainOpacityPercent,
        bool beautifyGeneratedMap)
        => Compose(draft, materials, showTerrain, showGrid, terrainOpacityPercent, checkerboardBlank: true, beautifyGeneratedMap);

    public Bitmap ComposeTerrainLayerPreview(MapWorkbenchDraft draft, bool showGrid)
    {
        if (draft.GridWidth <= 0 || draft.GridHeight <= 0)
        {
            throw new InvalidOperationException("Map draft grid size is invalid.");
        }

        var tileSize = draft.TileSize <= 0 ? MapResourceItem.MapTilePixelSize : draft.TileSize;
        var pixelWidth = checked(draft.GridWidth * tileSize);
        var pixelHeight = checked(draft.GridHeight * tileSize);
        var bitmap = new Bitmap(pixelWidth, pixelHeight, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bitmap);
        g.SmoothingMode = SmoothingMode.None;
        g.InterpolationMode = InterpolationMode.NearestNeighbor;
        g.PixelOffsetMode = PixelOffsetMode.Half;
        g.Clear(Color.Black);

        if (draft.TerrainCells.Length == draft.GridWidth * draft.GridHeight)
        {
            DrawTerrain(g, draft, pixelWidth, pixelHeight, opacityPercent: 100);
        }

        if (showGrid)
        {
            DrawGrid(g, draft.GridWidth, draft.GridHeight, pixelWidth, pixelHeight);
        }

        return bitmap;
    }

    private Bitmap Compose(
        MapWorkbenchDraft draft,
        IReadOnlyList<MaterialAsset> materials,
        bool showTerrain,
        bool showGrid,
        int terrainOpacityPercent,
        bool checkerboardBlank,
        bool beautifyGeneratedMap)
    {
        if (draft.GridWidth <= 0 || draft.GridHeight <= 0)
        {
            throw new InvalidOperationException("Map draft grid size is invalid.");
        }

        var originalTerrainCells = draft.TerrainCells;
        try
        {
            draft.TerrainCells = _materialDrivenService.DeriveTerrainCells(draft, materials);
            using var visual = _materialDrivenService.ComposeVisualMap(draft, materials, checkerboardBlank, beautifyGeneratedMap);
            var bitmap = new Bitmap(visual.Width, visual.Height, PixelFormat.Format32bppArgb);
            using var g = Graphics.FromImage(bitmap);
            g.SmoothingMode = SmoothingMode.None;
            g.InterpolationMode = InterpolationMode.NearestNeighbor;
            g.PixelOffsetMode = PixelOffsetMode.Half;
            g.DrawImage(visual, new Rectangle(0, 0, bitmap.Width, bitmap.Height));

            if (showTerrain && draft.TerrainCells.Length == draft.GridWidth * draft.GridHeight)
            {
                DrawTerrain(g, draft, bitmap.Width, bitmap.Height, terrainOpacityPercent);
            }

            if (showGrid)
            {
                DrawGrid(g, draft.GridWidth, draft.GridHeight, bitmap.Width, bitmap.Height);
            }

            return bitmap;
        }
        finally
        {
            draft.TerrainCells = originalTerrainCells;
        }
    }

    private static void DrawTerrain(Graphics g, MapWorkbenchDraft draft, int pixelWidth, int pixelHeight, int opacityPercent)
    {
        var alpha = Math.Clamp(opacityPercent, 0, 100) * 255 / 100;
        if (alpha <= 0) return;

        var cellWidth = pixelWidth / (float)draft.GridWidth;
        var cellHeight = pixelHeight / (float)draft.GridHeight;
        for (var y = 0; y < draft.GridHeight; y++)
        {
            for (var x = 0; x < draft.GridWidth; x++)
            {
                var value = draft.TerrainCells[y * draft.GridWidth + x];
                var color = HexzmapTerrainRenderService.GetTerrainColor(value);
                using var brush = new SolidBrush(Color.FromArgb(alpha, color));
                g.FillRectangle(brush, x * cellWidth, y * cellHeight, cellWidth + 0.5f, cellHeight + 0.5f);
            }
        }
    }

    private static void DrawGrid(Graphics g, int gridWidth, int gridHeight, int pixelWidth, int pixelHeight)
    {
        using var darkPen = new Pen(Color.FromArgb(150, Color.Black));
        using var lightPen = new Pen(Color.FromArgb(70, Color.White));
        for (var x = 0; x <= gridWidth; x++)
        {
            var px = x * pixelWidth / (float)gridWidth;
            g.DrawLine(darkPen, px, 0, px, pixelHeight);
            g.DrawLine(lightPen, px + 1, 0, px + 1, pixelHeight);
        }

        for (var y = 0; y <= gridHeight; y++)
        {
            var py = y * pixelHeight / (float)gridHeight;
            g.DrawLine(darkPen, 0, py, pixelWidth, py);
            g.DrawLine(lightPen, 0, py + 1, pixelWidth, py + 1);
        }
    }
}
