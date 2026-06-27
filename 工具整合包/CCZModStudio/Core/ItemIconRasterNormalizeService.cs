using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace CCZModStudio.Core;

public sealed class ItemIconRasterNormalizeService
{
    public const int SmallIconSize = 16;
    public const int LargeIconSize = 32;
    public static readonly Color GameMagentaKey = Color.FromArgb(255, 247, 0, 255);

    public ItemIconRasterPair NormalizePairFromFile(string sourcePath)
    {
        using var raw = Image.FromFile(sourcePath);
        using var source = CloneArgb(raw);
        return NormalizePair(source, sourcePath);
    }

    public ItemIconRasterPair NormalizePair(Image source, string sourceLabel = "")
    {
        using var sourceBitmap = CloneArgb(source);
        var large = NormalizeToSize(sourceBitmap, LargeIconSize, "large");
        using var largeBitmap = large.CreateTransparentBitmap();
        var small = NormalizeSmallFromLargeCanvas(largeBitmap);
        return new ItemIconRasterPair(
            string.IsNullOrWhiteSpace(sourceLabel) ? "<bitmap>" : sourceLabel,
            source.Width,
            source.Height,
            small,
            large);
    }

    public ItemIconRasterImage NormalizeLarge(Image source, string sourceLabel = "")
    {
        using var sourceBitmap = CloneArgb(source);
        return NormalizeToSize(sourceBitmap, LargeIconSize, string.IsNullOrWhiteSpace(sourceLabel) ? "large" : sourceLabel);
    }

    public Bitmap NormalizeLargeBitmap(Image source)
    {
        var normalized = NormalizeLarge(source);
        return normalized.CreateTransparentBitmap();
    }

    public ItemIconRasterImage NormalizeToSize(Image source, int targetSize, string role = "")
    {
        if (targetSize <= 0) throw new ArgumentOutOfRangeException(nameof(targetSize));

        using var transparentSource = CloneArgb(source);
        var keyPixels = ApplyMagentaTransparency(transparentSource);
        var crop = FindVisibleBounds(transparentSource);
        var exactSize = transparentSource.Width == targetSize && transparentSource.Height == targetSize;
        var output = new Bitmap(targetSize, targetSize, PixelFormat.Format32bppArgb);
        using (var graphics = Graphics.FromImage(output))
        {
            graphics.Clear(Color.Transparent);
            graphics.CompositingMode = CompositingMode.SourceCopy;
            graphics.CompositingQuality = CompositingQuality.HighSpeed;
            graphics.InterpolationMode = InterpolationMode.NearestNeighbor;
            graphics.PixelOffsetMode = PixelOffsetMode.Half;
            graphics.SmoothingMode = SmoothingMode.None;

            if (exactSize)
            {
                graphics.DrawImageUnscaled(transparentSource, 0, 0);
            }
            else if (crop.HasValue)
            {
                var sourceRect = crop.Value;
                var scale = Math.Min(targetSize / (float)sourceRect.Width, targetSize / (float)sourceRect.Height);
                if (float.IsNaN(scale) || float.IsInfinity(scale) || scale <= 0) scale = 1;
                var drawWidth = Math.Max(1, (int)Math.Round(sourceRect.Width * scale));
                var drawHeight = Math.Max(1, (int)Math.Round(sourceRect.Height * scale));
                var x = (targetSize - drawWidth) / 2;
                var y = (targetSize - drawHeight) / 2;
                graphics.DrawImage(transparentSource, new Rectangle(x, y, drawWidth, drawHeight), sourceRect, GraphicsUnit.Pixel);
            }
        }

        try
        {
            var bytes = EncodeTransparentBitmapToGameBmp(output);
            var visible = CountVisiblePixels(output);
            return new ItemIconRasterImage(
                role,
                targetSize,
                targetSize,
                bytes,
                crop,
                exactSize,
                keyPixels,
                visible);
        }
        finally
        {
            output.Dispose();
        }
    }

    private ItemIconRasterImage NormalizeSmallFromLargeCanvas(Image largeSource)
    {
        using var transparentSource = CloneArgb(largeSource);
        var keyPixels = ApplyMagentaTransparency(transparentSource);
        var crop = FindVisibleBounds(transparentSource);
        var exactSize = transparentSource.Width == SmallIconSize && transparentSource.Height == SmallIconSize;
        var output = new Bitmap(SmallIconSize, SmallIconSize, PixelFormat.Format32bppArgb);
        using (var graphics = Graphics.FromImage(output))
        {
            graphics.Clear(Color.Transparent);
            graphics.CompositingMode = CompositingMode.SourceCopy;
            graphics.CompositingQuality = CompositingQuality.HighSpeed;
            graphics.InterpolationMode = InterpolationMode.NearestNeighbor;
            graphics.PixelOffsetMode = PixelOffsetMode.Half;
            graphics.SmoothingMode = SmoothingMode.None;

            if (exactSize)
            {
                graphics.DrawImageUnscaled(transparentSource, 0, 0);
            }
            else
            {
                graphics.DrawImage(
                    transparentSource,
                    new Rectangle(0, 0, SmallIconSize, SmallIconSize),
                    new Rectangle(0, 0, transparentSource.Width, transparentSource.Height),
                    GraphicsUnit.Pixel);
            }
        }

        try
        {
            var bytes = EncodeTransparentBitmapToGameBmp(output);
            var visible = CountVisiblePixels(output);
            return new ItemIconRasterImage(
                "small",
                SmallIconSize,
                SmallIconSize,
                bytes,
                crop,
                exactSize,
                keyPixels,
                visible);
        }
        finally
        {
            output.Dispose();
        }
    }

    public static byte[] EncodeTransparentBitmapToGameBmp(Bitmap source)
    {
        using var flattened = CompositeTransparentToGameMagenta(source);
        using var memory = new MemoryStream();
        flattened.Save(memory, ImageFormat.Bmp);
        return memory.ToArray();
    }

    public static Bitmap CompositeTransparentToGameMagenta(Bitmap source)
    {
        var bitmap = new Bitmap(source.Width, source.Height, PixelFormat.Format24bppRgb);
        for (var y = 0; y < source.Height; y++)
        {
            for (var x = 0; x < source.Width; x++)
            {
                var color = source.GetPixel(x, y);
                bitmap.SetPixel(x, y, color.A == 0 || IsMagentaKey(color)
                    ? GameMagentaKey
                    : Color.FromArgb(255, color.R, color.G, color.B));
            }
        }

        return bitmap;
    }

    public static Bitmap DecodeGameIconBmp(byte[] bytes)
    {
        using var memory = new MemoryStream(bytes, writable: false);
        using var raw = Image.FromStream(memory, useEmbeddedColorManagement: false, validateImageData: true);
        var bitmap = CloneArgb(raw);
        ApplyMagentaTransparency(bitmap);
        return bitmap;
    }

    public static ItemIconBmpInfo ReadBmpInfo(byte[] bytes)
    {
        using var memory = new MemoryStream(bytes, writable: false);
        using var image = Image.FromStream(memory, useEmbeddedColorManagement: false, validateImageData: true);
        var topLeft = Color.Empty;
        if (image.Width > 0 && image.Height > 0)
        {
            using var bitmap = new Bitmap(image);
            topLeft = bitmap.GetPixel(0, 0);
        }

        return new ItemIconBmpInfo(
            image.Width,
            image.Height,
            Image.GetPixelFormatSize(image.PixelFormat),
            topLeft.R,
            topLeft.G,
            topLeft.B);
    }

    private static Bitmap CloneArgb(Image raw)
    {
        var bitmap = new Bitmap(raw.Width, raw.Height, PixelFormat.Format32bppArgb);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.CompositingMode = CompositingMode.SourceCopy;
        graphics.DrawImage(raw, new Rectangle(0, 0, bitmap.Width, bitmap.Height));
        return bitmap;
    }

    private static Rectangle? FindVisibleBounds(Bitmap bitmap)
    {
        var minX = bitmap.Width;
        var minY = bitmap.Height;
        var maxX = -1;
        var maxY = -1;
        for (var y = 0; y < bitmap.Height; y++)
        {
            for (var x = 0; x < bitmap.Width; x++)
            {
                if (bitmap.GetPixel(x, y).A == 0) continue;
                if (x < minX) minX = x;
                if (y < minY) minY = y;
                if (x > maxX) maxX = x;
                if (y > maxY) maxY = y;
            }
        }

        return maxX < 0
            ? null
            : new Rectangle(minX, minY, maxX - minX + 1, maxY - minY + 1);
    }

    private static int ApplyMagentaTransparency(Bitmap bitmap)
    {
        var converted = 0;
        for (var y = 0; y < bitmap.Height; y++)
        {
            for (var x = 0; x < bitmap.Width; x++)
            {
                var pixel = bitmap.GetPixel(x, y);
                if (pixel.A == 0)
                {
                    converted++;
                    continue;
                }

                if (!IsMagentaKey(pixel)) continue;
                bitmap.SetPixel(x, y, Color.Transparent);
                converted++;
            }
        }

        return converted;
    }

    public static bool IsMagentaKey(Color pixel)
    {
        if (pixel.A == 0) return true;
        return pixel.R >= 210 &&
               pixel.B >= 210 &&
               pixel.G <= 90 &&
               Math.Abs(pixel.R - pixel.B) <= 70;
    }

    private static int CountVisiblePixels(Bitmap bitmap)
    {
        var count = 0;
        for (var y = 0; y < bitmap.Height; y++)
        {
            for (var x = 0; x < bitmap.Width; x++)
            {
                if (bitmap.GetPixel(x, y).A != 0) count++;
            }
        }

        return count;
    }
}

public sealed record ItemIconRasterPair(
    string SourceLabel,
    int SourceWidth,
    int SourceHeight,
    ItemIconRasterImage Small,
    ItemIconRasterImage Large)
{
    public string Summary =>
        $"source={SourceWidth}x{SourceHeight}; small={Small.Width}x{Small.Height}; large={Large.Width}x{Large.Height}; " +
        $"crop={Large.CropSummary}; keyPixels={Small.MagentaKeyPixels + Large.MagentaKeyPixels:N0}";
}

public sealed record ItemIconRasterImage(
    string Role,
    int Width,
    int Height,
    byte[] BmpBytes,
    Rectangle? VisibleCrop,
    bool ExactSizePreserved,
    int MagentaKeyPixels,
    int VisiblePixels)
{
    public string CropSummary => VisibleCrop.HasValue
        ? $"{VisibleCrop.Value.X},{VisibleCrop.Value.Y},{VisibleCrop.Value.Width}x{VisibleCrop.Value.Height}"
        : "empty";

    public Bitmap CreateTransparentBitmap()
        => ItemIconRasterNormalizeService.DecodeGameIconBmp(BmpBytes);
}

public sealed record ItemIconBmpInfo(
    int Width,
    int Height,
    int BitCount,
    int TopLeftR,
    int TopLeftG,
    int TopLeftB);
