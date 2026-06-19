using System.Drawing;
using System.Drawing.Drawing2D;
using CCZModStudio.Models;

namespace CCZModStudio.Core;

public sealed class MapCanvasComposeService
{
    private readonly Dictionary<string, CachedImage> _imageCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly TerrainDrivenMapGenerationService _terrainGenerator = new();
    private readonly TerrainMapBeautifyService _beautifyService = new();

    public Bitmap ComposeFinal(MapWorkbenchDraft draft)
        => Compose(draft, Array.Empty<MaterialAsset>(), showTerrain: false, showGrid: false, terrainOpacityPercent: 0, checkerboardBlank: false, beautifyGeneratedMap: true);

    public Bitmap ComposeFinal(MapWorkbenchDraft draft, IReadOnlyList<MaterialAsset> materials)
        => Compose(draft, materials, showTerrain: false, showGrid: false, terrainOpacityPercent: 0, checkerboardBlank: false, beautifyGeneratedMap: true);

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
            throw new InvalidOperationException("地图草稿格数无效。");
        }

        var tileSize = draft.TileSize <= 0 ? MapResourceItem.MapTilePixelSize : draft.TileSize;
        var pixelWidth = checked(draft.GridWidth * tileSize);
        var pixelHeight = checked(draft.GridHeight * tileSize);
        var bitmap = new Bitmap(pixelWidth, pixelHeight, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
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
            throw new InvalidOperationException("地图草稿格数无效。");
        }

        var tileSize = draft.TileSize <= 0 ? MapResourceItem.MapTilePixelSize : draft.TileSize;
        var pixelWidth = checked(draft.GridWidth * tileSize);
        var pixelHeight = checked(draft.GridHeight * tileSize);
        var bitmap = new Bitmap(pixelWidth, pixelHeight, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bitmap);
        g.SmoothingMode = SmoothingMode.None;
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.PixelOffsetMode = PixelOffsetMode.Half;

        var baseImage = GetCachedImage(draft.BaseLayerPath);
        if (baseImage != null)
        {
            g.Clear(Color.Black);
            g.DrawImage(baseImage, new Rectangle(0, 0, pixelWidth, pixelHeight));
        }
        else if (checkerboardBlank)
        {
            DrawCheckerboard(g, pixelWidth, pixelHeight);
        }
        else
        {
            g.Clear(Color.Black);
        }

        if (draft.AutoGenerateMapFromTerrain)
        {
            using var generated = _terrainGenerator.RenderBaseTerrain(draft, materials);
            if (beautifyGeneratedMap)
            {
                using var beautified = _beautifyService.Beautify(draft, generated);
                g.DrawImage(beautified, new Rectangle(0, 0, pixelWidth, pixelHeight));
            }
            else
            {
                g.DrawImage(generated, new Rectangle(0, 0, pixelWidth, pixelHeight));
            }
        }
        else
        {
            DrawCells(g, draft, draft.GeneratedMapCells);
        }

        DrawCells(g, draft, draft.BuildingOverlayCells);
        DrawCells(g, draft, draft.MapCellOverrides);

        if (showTerrain && draft.TerrainCells.Length == draft.GridWidth * draft.GridHeight)
        {
            DrawTerrain(g, draft, pixelWidth, pixelHeight, terrainOpacityPercent);
        }

        if (showGrid)
        {
            DrawGrid(g, draft.GridWidth, draft.GridHeight, pixelWidth, pixelHeight);
        }

        return bitmap;
    }

    private void DrawCells(Graphics g, MapWorkbenchDraft draft, IEnumerable<MapCellOverride> cells)
    {
        var tileSize = draft.TileSize <= 0 ? MapResourceItem.MapTilePixelSize : draft.TileSize;
        foreach (var cell in cells.OrderBy(x => x.Index))
        {
            if (cell.Index < 0 || cell.Index >= draft.GridWidth * draft.GridHeight) continue;
            var materialPath = MapDraftService.ResolveMaterialPath(draft.MaterialRoot, cell.MaterialRelativePath);
            var material = GetCachedImage(materialPath);
            if (material == null) continue;

            var x = cell.Index % draft.GridWidth;
            var y = cell.Index / draft.GridWidth;
            g.DrawImage(material, new Rectangle(x * tileSize, y * tileSize, tileSize, tileSize));
        }
    }

    private Bitmap? GetCachedImage(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return null;

        var fullPath = Path.GetFullPath(path);
        var info = new FileInfo(fullPath);
        if (_imageCache.TryGetValue(fullPath, out var cached) &&
            cached.LastWriteUtc == info.LastWriteTimeUtc &&
            cached.Length == info.Length)
        {
            return cached.Bitmap;
        }

        using var source = Image.FromFile(fullPath);
        var bitmap = new Bitmap(source);
        if (_imageCache.TryGetValue(fullPath, out cached))
        {
            cached.Bitmap.Dispose();
        }

        _imageCache[fullPath] = new CachedImage(info.LastWriteTimeUtc, info.Length, bitmap);
        return bitmap;
    }

    private static void DrawCheckerboard(Graphics g, int pixelWidth, int pixelHeight)
    {
        const int size = 24;
        using var light = new SolidBrush(Color.FromArgb(62, 62, 62));
        using var dark = new SolidBrush(Color.FromArgb(38, 38, 38));
        for (var y = 0; y < pixelHeight; y += size)
        {
            for (var x = 0; x < pixelWidth; x += size)
            {
                var brush = ((x / size) + (y / size)) % 2 == 0 ? light : dark;
                g.FillRectangle(brush, x, y, Math.Min(size, pixelWidth - x), Math.Min(size, pixelHeight - y));
            }
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

    private sealed record CachedImage(DateTime LastWriteUtc, long Length, Bitmap Bitmap);
}
