using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using CCZModStudio.Models;

namespace CCZModStudio.Core;

public sealed class E5ImageRenderService
{
    private static readonly byte[] PngMagic = [0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A];

    public Bitmap? RenderEntry(
        CczProject project,
        string fileName,
        byte[] bytes,
        int canvasWidth,
        int canvasHeight,
        out string renderMode)
    {
        var rsLayout = new RsFrameLayoutResolver().TryResolve(fileName);
        if (rsLayout != null)
        {
            var probe = RsStripLayoutService.Probe(fileName, DetectStorageKind(bytes), bytes);
            if (!probe.IsSupportedLayout)
            {
                renderMode = probe.Detail;
                return null;
            }

            using var decodedStrip = TryDecodeStandardImage(bytes);
            if (decodedStrip != null)
            {
                using var frame = CropRsRepresentativeFrame(decodedStrip, rsLayout);
                renderMode = probe.Detail;
                return RenderToCanvas(frame, canvasWidth, canvasHeight);
            }

            using var rawFrame = TryRenderKnownRawEntry(project, fileName, bytes);
            if (rawFrame != null)
            {
                renderMode = probe.Detail;
                return RenderToCanvas(rawFrame, canvasWidth, canvasHeight);
            }

            renderMode = "R/S 帧条解码失败";
            return null;
        }

        using var decoded = TryDecodeStandardImage(bytes);
        if (decoded != null)
        {
            renderMode = "标准图片";
            return RenderToCanvas(decoded, canvasWidth, canvasHeight);
        }

        using var raw = TryRenderKnownRawEntry(project, fileName, bytes);
        if (raw != null)
        {
            renderMode = "已知 RAW 帧条";
            return RenderToCanvas(raw, canvasWidth, canvasHeight);
        }

        using var byteMap = TryRenderByteMap(project, bytes);
        if (byteMap != null)
        {
            renderMode = "未知字节图候选";
            return RenderToCanvas(byteMap, canvasWidth, canvasHeight);
        }

        renderMode = "不可渲染";
        return null;
    }

    public Bitmap? TryDecodeStandardImage(byte[] bytes)
    {
        if (!IsStandardImage(bytes)) return null;

        try
        {
            using var memory = new MemoryStream(bytes, writable: false);
            using var raw = Image.FromStream(memory, useEmbeddedColorManagement: false, validateImageData: false);
            var bitmap = CloneArgb(raw);
            if (!IsPng(bytes)) ApplyMagentaTransparency(bitmap);
            return bitmap;
        }
        catch
        {
            return null;
        }
    }

    public Bitmap RenderToCanvas(
        Image source,
        int canvasWidth,
        int canvasHeight,
        Color? background = null,
        bool drawGrid = false)
    {
        var width = Math.Max(96, canvasWidth);
        var height = Math.Max(96, canvasHeight);
        var canvas = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(canvas);
        g.Clear(background ?? Color.FromArgb(28, 30, 32));
        g.InterpolationMode = InterpolationMode.NearestNeighbor;
        g.PixelOffsetMode = PixelOffsetMode.Half;
        g.CompositingQuality = CompositingQuality.HighSpeed;

        if (drawGrid)
        {
            using var gridPen = new Pen(Color.FromArgb(230, 230, 230));
            var cell = Math.Max(12, Math.Min(width, height) / 8);
            for (var x = 0; x <= width; x += cell) g.DrawLine(gridPen, x, 0, x, height);
            for (var y = 0; y <= height; y += cell) g.DrawLine(gridPen, 0, y, width, y);
        }

        using var borderPen = new Pen(Color.FromArgb(90, 100, 108));
        var rect = new Rectangle(8, 8, width - 16, height - 16);
        g.DrawRectangle(borderPen, rect);

        var scale = Math.Min((rect.Width - 8) / (float)source.Width, (rect.Height - 8) / (float)source.Height);
        if (float.IsNaN(scale) || float.IsInfinity(scale) || scale <= 0) scale = 1;
        var drawWidth = Math.Max(1, (int)Math.Round(source.Width * scale));
        var drawHeight = Math.Max(1, (int)Math.Round(source.Height * scale));
        var drawX = rect.Left + (rect.Width - drawWidth) / 2;
        var drawY = rect.Top + (rect.Height - drawHeight) / 2;
        var destination = new Rectangle(drawX, drawY, drawWidth, drawHeight);
        g.DrawImage(source, destination);
        return canvas;
    }

    private static bool IsStandardImage(byte[] bytes)
        => bytes.Length >= 2 &&
           ((bytes[0] == (byte)'B' && bytes[1] == (byte)'M') ||
            (bytes[0] == 0xFF && bytes[1] == 0xD8) ||
            (bytes.Length >= PngMagic.Length && bytes.AsSpan(0, PngMagic.Length).SequenceEqual(PngMagic)));

    private static bool IsPng(byte[] bytes)
        => bytes.Length >= PngMagic.Length && bytes.AsSpan(0, PngMagic.Length).SequenceEqual(PngMagic);

    private static Bitmap? TryRenderKnownRawEntry(
        CczProject project,
        string fileName,
        byte[] bytes)
    {
        var spec = ResolveRawSpec(fileName);
        if (spec == null) return null;
        var rawLength = bytes.Length - (bytes.Length % spec.Value.Width);
        if (rawLength < spec.Value.Width * spec.Value.FrameHeight) return null;

        var colors = LoadRawPalette(project);
        var rawHeight = rawLength / spec.Value.Width;
        using var strip = new Bitmap(spec.Value.Width, rawHeight, PixelFormat.Format32bppArgb);
        for (var y = 0; y < rawHeight; y++)
        {
            for (var x = 0; x < spec.Value.Width; x++)
            {
                var value = bytes[y * spec.Value.Width + x];
                if (value == 0)
                {
                    strip.SetPixel(x, y, Color.Transparent);
                    continue;
                }

                var gray = Math.Min(255, 48 + value);
                var color = value < colors.Count ? colors[value] : Color.FromArgb(255, gray, gray, gray);
                strip.SetPixel(x, y, IsMagentaKey(color) ? Color.Transparent : color);
            }
        }

        var layout = new RsFrameLayoutResolver().Resolve(fileName);
        return CropRsRepresentativeFrame(strip, layout);
    }

    private static Bitmap? TryRenderByteMap(CczProject project, byte[] bytes)
    {
        if (bytes.Length < 64) return null;
        var width = GuessByteMapWidth(bytes.Length);
        var rawLength = bytes.Length - (bytes.Length % width);
        if (rawLength <= 0) return null;

        var colors = LoadRawPalette(project);
        var height = Math.Clamp(rawLength / width, 1, 2048);
        using var map = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var value = bytes[y * width + x];
                if (value == 0)
                {
                    map.SetPixel(x, y, Color.Transparent);
                    continue;
                }

                var gray = Math.Min(255, 36 + value);
                var color = value < colors.Count ? colors[value] : Color.FromArgb(255, gray, gray, gray);
                map.SetPixel(x, y, IsMagentaKey(color) ? Color.Transparent : color);
            }
        }

        return CropGenericRepresentativeFrame(map, Math.Min(height, GuessByteMapFrameHeight(width, height)));
    }

    private static int GuessByteMapWidth(int length)
    {
        var preferred = new[] { 64, 80, 96, 120, 128, 144, 160, 192, 200, 240, 256, 320 };
        foreach (var width in preferred)
        {
            if (length % width == 0) return width;
        }

        var sqrt = Math.Sqrt(length);
        return preferred.OrderBy(x => Math.Abs(x - sqrt)).First();
    }

    private static int GuessByteMapFrameHeight(int width, int height)
    {
        if (height >= width) return width;
        if (height >= 96) return 96;
        if (height >= 64) return 64;
        return height;
    }

    private static (int Width, int FrameHeight)? ResolveRawSpec(string fileName)
    {
        if (fileName.Equals("Pmapobj.e5", StringComparison.OrdinalIgnoreCase)) return (48, 64);
        if (fileName.Equals("Unit_atk.e5", StringComparison.OrdinalIgnoreCase)) return (64, 64);
        if (fileName.Equals("Unit_mov.e5", StringComparison.OrdinalIgnoreCase)) return (48, 48);
        if (fileName.Equals("Unit_spc.e5", StringComparison.OrdinalIgnoreCase)) return (48, 48);
        return null;
    }

    private static IReadOnlyList<Color> LoadRawPalette(CczProject project)
    {
        var candidates = new[]
        {
            PortableInstallPaths.PaletteTsbPath,
            Path.Combine(project.GameRoot, "tsb")
        };

        var path = candidates.FirstOrDefault(path => File.Exists(path) && new FileInfo(path).Length >= 256 * 4);
        if (path == null) return Array.Empty<Color>();

        var bytes = File.ReadAllBytes(path);
        var colors = new Color[256];
        for (var i = 0; i < colors.Length; i++)
        {
            var offset = i * 4;
            colors[i] = Color.FromArgb(255, bytes[offset + 2], bytes[offset + 1], bytes[offset]);
        }

        return colors;
    }

    private static Bitmap CropRsRepresentativeFrame(Bitmap strip, RsFrameLayout layout)
    {
        Bitmap? fallback = null;
        for (var frameIndex = 0; frameIndex < layout.ExpectedFrameCount; frameIndex++)
        {
            var frame = RsStripLayoutService.CropFrame(strip, layout, frameIndex);
            if (CountVisiblePixels(frame) > Math.Max(12, frame.Width * frame.Height / 80))
            {
                fallback?.Dispose();
                return frame;
            }

            if (fallback == null) fallback = frame;
            else frame.Dispose();
        }

        return fallback ?? RsStripLayoutService.CropFrame(strip, layout, 0);
    }

    private static Bitmap CropGenericRepresentativeFrame(Bitmap strip, int frameHeight)
    {
        var frameCount = Math.Max(1, strip.Height / Math.Max(1, frameHeight));
        Bitmap? fallback = null;
        for (var frameIndex = 0; frameIndex < frameCount; frameIndex++)
        {
            var frame = CropFrame(strip, frameIndex * frameHeight, frameHeight);
            if (CountVisiblePixels(frame) > Math.Max(12, frame.Width * frame.Height / 80))
            {
                fallback?.Dispose();
                return frame;
            }

            if (fallback == null) fallback = frame;
            else frame.Dispose();
        }

        return fallback ?? CropFrame(strip, 0, frameHeight);
    }

    private static Bitmap CropFrame(Bitmap strip, int y, int frameHeight)
    {
        var height = Math.Min(frameHeight, strip.Height - y);
        if (height <= 0) height = Math.Min(frameHeight, strip.Height);
        var frame = new Bitmap(strip.Width, height, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(frame);
        g.Clear(Color.Transparent);
        g.CompositingMode = CompositingMode.SourceCopy;
        g.InterpolationMode = InterpolationMode.NearestNeighbor;
        g.PixelOffsetMode = PixelOffsetMode.Half;
        g.DrawImage(strip, new Rectangle(0, 0, frame.Width, frame.Height), new Rectangle(0, y, frame.Width, frame.Height), GraphicsUnit.Pixel);
        ApplyMagentaTransparency(frame);
        return frame;
    }

    private static Bitmap CloneArgb(Image image)
    {
        var bitmap = new Bitmap(image.Width, image.Height, PixelFormat.Format32bppArgb);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.Clear(Color.Transparent);
        graphics.CompositingMode = CompositingMode.SourceCopy;
        graphics.DrawImage(image, new Rectangle(Point.Empty, bitmap.Size));
        return bitmap;
    }

    private static string DetectStorageKind(byte[] bytes)
    {
        if (bytes.Length >= 2 && bytes[0] == (byte)'B' && bytes[1] == (byte)'M') return "BMP";
        if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xD8) return "JPG";
        if (IsPng(bytes)) return "PNG";
        return "RAW";
    }

    private static void ApplyMagentaTransparency(Bitmap bitmap)
    {
        for (var y = 0; y < bitmap.Height; y++)
        {
            for (var x = 0; x < bitmap.Width; x++)
            {
                if (IsMagentaKey(bitmap.GetPixel(x, y)))
                {
                    bitmap.SetPixel(x, y, Color.Transparent);
                }
            }
        }
    }

    private static bool IsMagentaKey(Color pixel)
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
