using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using CCZModStudio.Models;

namespace CCZModStudio.Core;

public sealed class MapCanvasPreviewRenderer : IDisposable
{
    private readonly Dictionary<string, CachedImage> _imageCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<int, SolidBrush> _terrainBrushCache = new();
    private readonly MapCanvasComposeService _composeService = new();
    private IReadOnlyList<MaterialAsset> _materials = Array.Empty<MaterialAsset>();

    private Bitmap? _preview;
    private int _gridWidth;
    private int _gridHeight;
    private int _tileSize;
    private bool _showTerrain;
    private bool _showGrid;
    private int _terrainOpacityPercent;
    private bool _terrainLayerOnly;

    public Bitmap Rebuild(MapWorkbenchDraft draft, bool showTerrain, bool showGrid, int terrainOpacityPercent)
        => Rebuild(draft, Array.Empty<MaterialAsset>(), showTerrain, showGrid, terrainOpacityPercent);

    public Bitmap Rebuild(MapWorkbenchDraft draft, IReadOnlyList<MaterialAsset> materials, bool showTerrain, bool showGrid, int terrainOpacityPercent)
        => Rebuild(draft, materials, showTerrain, showGrid, terrainOpacityPercent, beautifyGeneratedMap: draft.BeautifyGeneratedMap);

    public Bitmap Rebuild(
        MapWorkbenchDraft draft,
        IReadOnlyList<MaterialAsset> materials,
        bool showTerrain,
        bool showGrid,
        int terrainOpacityPercent,
        bool beautifyGeneratedMap)
    {
        ValidateDraft(draft);
        Clear();

        _materials = materials;
        _gridWidth = draft.GridWidth;
        _gridHeight = draft.GridHeight;
        _tileSize = draft.TileSize <= 0 ? MapResourceItem.MapTilePixelSize : draft.TileSize;
        _showTerrain = showTerrain;
        _showGrid = showGrid;
        _terrainOpacityPercent = Math.Clamp(terrainOpacityPercent, 0, 100);
        _terrainLayerOnly = false;

        _preview = _composeService.ComposePreview(draft, materials, showTerrain, showGrid, _terrainOpacityPercent, beautifyGeneratedMap);
        return _preview;
    }

    public Bitmap RebuildTerrainLayer(MapWorkbenchDraft draft, bool showGrid)
    {
        ValidateDraft(draft);
        Clear();

        _gridWidth = draft.GridWidth;
        _gridHeight = draft.GridHeight;
        _tileSize = draft.TileSize <= 0 ? MapResourceItem.MapTilePixelSize : draft.TileSize;
        _showTerrain = true;
        _showGrid = showGrid;
        _terrainOpacityPercent = 100;
        _terrainLayerOnly = true;
        _materials = Array.Empty<MaterialAsset>();

        _preview = _composeService.ComposeTerrainLayerPreview(draft, showGrid);
        return _preview;
    }

    public Rectangle UpdateMapCell(MapWorkbenchDraft draft, int index, MapCellOverride? cell)
    {
        if (!CanUpdateCell(draft, index) || _preview == null) return Rectangle.Empty;
        if (_terrainLayerOnly) return Rectangle.Empty;
        if (draft.AutoGenerateMapFromTerrain)
        {
            Rebuild(draft, _materials, _showTerrain, _showGrid, _terrainOpacityPercent);
            return GetTileRectangle(index);
        }

        var rect = GetTileRectangle(index);
        RedrawTile(draft, index, cell);
        return rect;
    }

    public Rectangle UpdateTerrainCell(MapWorkbenchDraft draft, int index)
    {
        if (!CanUpdateCell(draft, index) || _preview == null) return Rectangle.Empty;
        if (draft.TerrainCells.Length != draft.CellCount) return Rectangle.Empty;
        if (_terrainLayerOnly)
        {
            RedrawTerrainLayerTile(draft, index);
            return GetTileRectangle(index);
        }

        if (draft.AutoGenerateMapFromTerrain)
        {
            Rebuild(draft, _materials, _showTerrain, _showGrid, _terrainOpacityPercent);
            return GetTileRectangle(index);
        }

        if (!_showTerrain) return Rectangle.Empty;
        RedrawTile(draft, index, null);
        return GetTileRectangle(index);
    }

    public void Clear()
    {
        _preview?.Dispose();
        _preview = null;
        _gridWidth = 0;
        _gridHeight = 0;
        _tileSize = 0;
        _showTerrain = false;
        _showGrid = false;
        _terrainOpacityPercent = 0;
        _terrainLayerOnly = false;
        _materials = Array.Empty<MaterialAsset>();
    }

    public void Dispose()
    {
        Clear();
        foreach (var cached in _imageCache.Values)
        {
            cached.Bitmap.Dispose();
        }

        _imageCache.Clear();
        foreach (var brush in _terrainBrushCache.Values)
        {
            brush.Dispose();
        }

        _terrainBrushCache.Clear();
    }

    private void RedrawTile(MapWorkbenchDraft draft, int index, MapCellOverride? updatedCell)
    {
        if (_preview == null) return;
        var rect = GetTileRectangle(index);
        using var g = CreateGraphics(_preview);
        g.SetClip(rect);
        DrawBase(g, draft, rect, checkerboardBlank: true);

        var cell = updatedCell ?? draft.MapCellOverrides.FirstOrDefault(x => x.Index == index);
        if (cell != null)
        {
            DrawMapCell(g, draft, index, cell);
        }

        if (_showTerrain && draft.TerrainCells.Length == draft.CellCount)
        {
            DrawTerrainCell(g, draft, index);
        }

        if (_showGrid)
        {
            DrawGrid(g, _gridWidth, _gridHeight, _preview.Width, _preview.Height);
        }

        g.ResetClip();
    }

    private void RedrawTerrainLayerTile(MapWorkbenchDraft draft, int index)
    {
        if (_preview == null) return;
        var rect = GetTileRectangle(index);
        using var g = CreateGraphics(_preview);
        g.SetClip(rect);
        using var black = new SolidBrush(Color.Black);
        g.FillRectangle(black, rect);

        if (draft.TerrainCells.Length == draft.CellCount)
        {
            DrawTerrainCell(g, draft, index);
        }

        if (_showGrid)
        {
            DrawGrid(g, _gridWidth, _gridHeight, _preview.Width, _preview.Height);
        }

        g.ResetClip();
    }

    private static void ValidateDraft(MapWorkbenchDraft draft)
    {
        if (draft.GridWidth <= 0 || draft.GridHeight <= 0)
        {
            throw new InvalidOperationException("Map draft grid size is invalid.");
        }
    }

    private bool CanUpdateCell(MapWorkbenchDraft draft, int index)
    {
        if (_preview == null || _gridWidth <= 0 || _gridHeight <= 0 || _tileSize <= 0) return false;
        if (draft.GridWidth != _gridWidth || draft.GridHeight != _gridHeight) return false;
        var tileSize = draft.TileSize <= 0 ? MapResourceItem.MapTilePixelSize : draft.TileSize;
        if (tileSize != _tileSize) return false;
        return index >= 0 && index < _gridWidth * _gridHeight;
    }

    private void DrawBase(Graphics g, MapWorkbenchDraft draft, Rectangle rect, bool checkerboardBlank)
    {
        var baseImage = GetCachedImage(draft.BaseLayerPath);
        if (baseImage != null)
        {
            using var black = new SolidBrush(Color.Black);
            g.FillRectangle(black, rect);
            g.DrawImage(baseImage, rect, rect, GraphicsUnit.Pixel);
        }
        else if (checkerboardBlank)
        {
            DrawCheckerboard(g, rect);
        }
        else
        {
            using var black = new SolidBrush(Color.Black);
            g.FillRectangle(black, rect);
        }
    }

    private void DrawMapCell(Graphics g, MapWorkbenchDraft draft, int index, MapCellOverride cell)
    {
        if (index < 0 || index >= draft.CellCount) return;
        var materialPath = MapDraftService.ResolveMaterialPath(draft.MaterialRoot, cell.MaterialRelativePath);
        var material = GetCachedImage(materialPath);
        if (material == null) return;
        g.DrawImage(material, GetTileRectangle(index));
    }

    private void DrawTerrain(Graphics g, MapWorkbenchDraft draft)
    {
        for (var index = 0; index < draft.TerrainCells.Length; index++)
        {
            DrawTerrainCell(g, draft, index);
        }
    }

    private void DrawTerrainCell(Graphics g, MapWorkbenchDraft draft, int index)
    {
        if (index < 0 || index >= draft.TerrainCells.Length) return;
        var alpha = _terrainOpacityPercent * 255 / 100;
        if (alpha <= 0) return;
        var color = HexzmapTerrainRenderService.GetTerrainColor(draft.TerrainCells[index]);
        var key = (alpha << 24) | color.ToArgb() & 0x00FFFFFF;
        if (!_terrainBrushCache.TryGetValue(key, out var brush))
        {
            brush = new SolidBrush(Color.FromArgb(alpha, color));
            _terrainBrushCache[key] = brush;
        }

        g.FillRectangle(brush, GetTileRectangle(index));
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

    private Rectangle GetTileRectangle(int index)
    {
        var x = index % _gridWidth;
        var y = index / _gridWidth;
        return new Rectangle(x * _tileSize, y * _tileSize, _tileSize, _tileSize);
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

    private static Bitmap CreateBitmap(int width, int height)
        => new(width, height, PixelFormat.Format32bppArgb);

    private static Graphics CreateGraphics(Image image)
    {
        var g = Graphics.FromImage(image);
        g.SmoothingMode = SmoothingMode.None;
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.PixelOffsetMode = PixelOffsetMode.Half;
        return g;
    }

    private static void DrawCheckerboard(Graphics g, Rectangle rect)
    {
        const int size = 24;
        using var light = new SolidBrush(Color.FromArgb(62, 62, 62));
        using var dark = new SolidBrush(Color.FromArgb(38, 38, 38));
        var startX = rect.Left - PositiveMod(rect.Left, size);
        var startY = rect.Top - PositiveMod(rect.Top, size);
        for (var y = startY; y < rect.Bottom; y += size)
        {
            for (var x = startX; x < rect.Right; x += size)
            {
                var brush = ((x / size) + (y / size)) % 2 == 0 ? light : dark;
                g.FillRectangle(brush, x, y, Math.Min(size, rect.Right - x), Math.Min(size, rect.Bottom - y));
            }
        }
    }

    private static int PositiveMod(int value, int divisor)
    {
        var result = value % divisor;
        return result < 0 ? result + divisor : result;
    }

    private sealed record CachedImage(DateTime LastWriteUtc, long Length, Bitmap Bitmap);
}
