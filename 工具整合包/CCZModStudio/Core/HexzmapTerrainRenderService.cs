using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace CCZModStudio.Core;

public sealed class HexzmapTerrainRenderService
{
    public const int DefaultCellSize = 16;

    public Bitmap RenderTerrainCells(byte[] cells, int width, int height, int cellSize = DefaultCellSize)
    {
        ValidateCells(cells, width, height);
        if (cellSize <= 0) throw new ArgumentOutOfRangeException(nameof(cellSize), "单格像素必须大于 0。");

        var bitmap = new Bitmap(width * cellSize, height * cellSize, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bitmap);
        g.Clear(Color.Black);
        g.SmoothingMode = SmoothingMode.None;
        g.InterpolationMode = InterpolationMode.NearestNeighbor;
        g.PixelOffsetMode = PixelOffsetMode.Half;

        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var value = cells[y * width + x];
                using var brush = new SolidBrush(GetTerrainColor(value));
                g.FillRectangle(brush, x * cellSize, y * cellSize, cellSize, cellSize);
            }
        }

        DrawGrid(g, width, height, bitmap.Width, bitmap.Height, Color.FromArgb(80, Color.Black));
        return bitmap;
    }

    public Bitmap RenderOverlay(byte[] cells, int width, int height, string mapImagePath, int overlayOpacityPercent = 45)
    {
        if (string.IsNullOrWhiteSpace(mapImagePath))
        {
            return RenderTerrainCells(cells, width, height);
        }

        using var image = Image.FromFile(mapImagePath);
        return RenderOverlay(cells, width, height, image, overlayOpacityPercent);
    }

    public Bitmap RenderOverlay(byte[] cells, int width, int height, Image mapImage, int overlayOpacityPercent = 45)
    {
        ValidateCells(cells, width, height);
        if (mapImage.Width <= 0 || mapImage.Height <= 0)
        {
            throw new ArgumentException("地图底图尺寸无效，无法生成叠加预览。", nameof(mapImage));
        }

        var alpha = Math.Clamp(overlayOpacityPercent, 0, 100) * 255 / 100;
        var bitmap = new Bitmap(mapImage.Width, mapImage.Height, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bitmap);
        g.Clear(Color.Black);
        g.SmoothingMode = SmoothingMode.None;
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.PixelOffsetMode = PixelOffsetMode.Half;
        g.DrawImage(mapImage, new Rectangle(0, 0, bitmap.Width, bitmap.Height));

        var cellWidth = bitmap.Width / (float)width;
        var cellHeight = bitmap.Height / (float)height;
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var color = GetTerrainColor(cells[y * width + x]);
                using var brush = new SolidBrush(Color.FromArgb(alpha, color));
                g.FillRectangle(brush, x * cellWidth, y * cellHeight, cellWidth + 0.5f, cellHeight + 0.5f);
            }
        }

        DrawGrid(g, width, height, bitmap.Width, bitmap.Height, Color.FromArgb(130, Color.Black));
        DrawGrid(g, width, height, bitmap.Width, bitmap.Height, Color.FromArgb(55, Color.White));
        return bitmap;
    }

    public static Color GetTerrainColor(byte value)
    {
        Color[] palette =
        {
            Color.FromArgb(64, 128, 64),
            Color.FromArgb(110, 170, 80),
            Color.FromArgb(40, 110, 50),
            Color.FromArgb(160, 140, 90),
            Color.FromArgb(90, 120, 160),
            Color.FromArgb(70, 150, 190),
            Color.FromArgb(130, 130, 130),
            Color.FromArgb(180, 180, 120),
            Color.FromArgb(120, 90, 60),
            Color.FromArgb(200, 200, 200),
            Color.FromArgb(150, 80, 80),
            Color.FromArgb(90, 80, 150),
            Color.FromArgb(200, 160, 80),
            Color.FromArgb(80, 160, 120),
            Color.FromArgb(160, 100, 160),
            Color.FromArgb(210, 210, 150)
        };

        var baseColor = palette[value % palette.Length];
        var adjust = (value / palette.Length) * 18;
        return Color.FromArgb(
            Math.Clamp(baseColor.R + adjust, 0, 255),
            Math.Clamp(baseColor.G + adjust, 0, 255),
            Math.Clamp(baseColor.B + adjust, 0, 255));
    }

    private static void ValidateCells(byte[] cells, int width, int height)
    {
        if (width <= 0) throw new ArgumentOutOfRangeException(nameof(width), "地形宽度必须大于 0。");
        if (height <= 0) throw new ArgumentOutOfRangeException(nameof(height), "地形高度必须大于 0。");
        if (cells.Length < width * height)
        {
            throw new ArgumentException($"地形数据不足：需要 {width * height} 字节，实际 {cells.Length} 字节。", nameof(cells));
        }
    }

    private static void DrawGrid(Graphics g, int width, int height, int pixelWidth, int pixelHeight, Color color)
    {
        using var pen = new Pen(color);
        for (var x = 0; x <= width; x++)
        {
            var px = x * pixelWidth / (float)width;
            g.DrawLine(pen, px, 0, px, pixelHeight);
        }

        for (var y = 0; y <= height; y++)
        {
            var py = y * pixelHeight / (float)height;
            g.DrawLine(pen, 0, py, pixelWidth, py);
        }
    }
}
