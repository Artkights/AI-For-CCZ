using System.Buffers.Binary;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using CCZModStudio.Models;

namespace CCZModStudio.Core;

internal sealed class StrategyAnimationFrameDecoder
{
    private const int RecordSize = 20;
    private const int MaxPreviewFrames = 96;
    private readonly E5ImageReplaceService _e5ImageService = new();
    private readonly Dictionary<string, RawPalette> _paletteCache = new(StringComparer.OrdinalIgnoreCase);

    public StrategyAnimationFrameDecodeResult Decode(
        CczProject project,
        StrategyAnimationPreviewKind kind,
        byte[] bytes,
        int canvasSize)
    {
        try
        {
            return DecodeCore(project, kind, bytes, canvasSize);
        }
        catch (Exception ex)
        {
            return StrategyAnimationFrameDecodeResult.Fail($"策略动画解析失败：{ex.Message}");
        }
    }

    private StrategyAnimationFrameDecodeResult DecodeCore(
        CczProject project,
        StrategyAnimationPreviewKind kind,
        byte[] bytes,
        int canvasSize)
    {
        if (bytes.Length < RecordSize * 2)
        {
            return StrategyAnimationFrameDecodeResult.Fail($"数据过短：{bytes.Length:N0} 字节，不足以读取策略动画头。");
        }

        var recordCount = bytes[0] + 1;
        var declaredPlaneCount = bytes[1];
        var flag2 = bytes[2];
        var flag3 = bytes[3];
        var delayMs = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(4, 4));
        var width = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(8, 4));
        var height = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(12, 4));
        var soundOrEffectId = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(16, 4));

        if (recordCount <= 1 || recordCount > 512)
        {
            return StrategyAnimationFrameDecodeResult.Fail($"记录数非法：{recordCount}。");
        }

        if (declaredPlaneCount <= 0)
        {
            return StrategyAnimationFrameDecodeResult.Fail($"声明帧平面数非法：{declaredPlaneCount}。");
        }

        if (width <= 0 || height <= 0 || width > 640 || height > 640)
        {
            return StrategyAnimationFrameDecodeResult.Fail($"帧尺寸非法：{width}x{height}。");
        }

        var headerLength = checked(recordCount * RecordSize);
        if (headerLength >= bytes.Length)
        {
            return StrategyAnimationFrameDecodeResult.Fail($"记录区长度 {headerLength:N0} 已超过数据长度 {bytes.Length:N0}。");
        }

        var planeSize = checked(width * height);
        var payloadLength = bytes.Length - headerLength;
        var actualPlaneCount = payloadLength / planeSize;
        var tailBytes = payloadLength % planeSize;
        if (actualPlaneCount <= 0)
        {
            return StrategyAnimationFrameDecodeResult.Fail($"帧载荷不足：payload={payloadLength:N0}，单帧={planeSize:N0}。");
        }

        var recordMaxFrameIndex = GetMaxReferencedFrameIndex(bytes, recordCount, actualPlaneCount);
        var palette = LoadPalette(project);
        var frameCount = Math.Min(actualPlaneCount, MaxPreviewFrames);
        var frames = new List<Bitmap>(frameCount);
        for (var frameIndex = 0; frameIndex < frameCount; frameIndex++)
        {
            using var rawFrame = RenderIndexedFrame(
                bytes,
                headerLength + frameIndex * planeSize,
                width,
                height,
                palette.Colors);
            frames.Add(RenderFrameToCanvas(rawFrame, canvasSize));
        }

        var interval = delayMs is >= 30 and <= 2000
            ? delayMs
            : StrategyAnimationPreviewService.DefaultFrameIntervalMs;
        var kindText = kind == StrategyAnimationPreviewKind.BigMcall ? "Mcall 大动画" : "Meff 小动画";
        var mode = kind == StrategyAnimationPreviewKind.BigMcall ? "Mcall 8bpp 动画" : "Meff 8bpp 动画";
        var extraText = actualPlaneCount > declaredPlaneCount
            ? $"；声明帧平面={declaredPlaneCount}，实际可切出={actualPlaneCount}，按“全部平面”预览"
            : string.Empty;
        var truncateText = actualPlaneCount > frameCount
            ? $"；预览为避免卡顿截取前 {frameCount} 帧"
            : string.Empty;
        var tailText = tailBytes > 0
            ? $"；尾部未切帧字节={tailBytes:N0}"
            : string.Empty;
        var recordReferenceText = recordMaxFrameIndex >= 0
            ? $"；播放记录最大引用帧={recordMaxFrameIndex}"
            : string.Empty;
        var delayText = interval == delayMs
            ? $"{interval}ms"
            : $"{interval}ms（头部原值 {delayMs}ms 已归一）";
        var message =
            $"{kindText}结构解析通过：记录数={recordCount}，播放记录={recordCount - 1}，尺寸={width}x{height}，间隔={delayText}，音效/参数={soundOrEffectId}。\r\n" +
            $"头部标记：byte1={declaredPlaneCount}，byte2={flag2}，byte3={flag3}{recordReferenceText}{extraText}{truncateText}{tailText}。\r\n" +
            $"帧载荷：offset={headerLength:N0}，payload={payloadLength:N0}，单帧={planeSize:N0}，预览帧数={frames.Count}。\r\n" +
            $"调色板：{palette.Mode}{(string.IsNullOrWhiteSpace(palette.Path) ? string.Empty : $"（{palette.Path}）")}。";

        return new StrategyAnimationFrameDecodeResult(
            true,
            frames,
            interval,
            frames.Count > 1,
            mode,
            message,
            width,
            height,
            declaredPlaneCount,
            actualPlaneCount);
    }

    private static int GetMaxReferencedFrameIndex(byte[] bytes, int recordCount, int actualPlaneCount)
    {
        var max = -1;
        for (var recordIndex = 1; recordIndex < recordCount; recordIndex++)
        {
            var offset = recordIndex * RecordSize;
            if (offset + RecordSize > bytes.Length) break;
            var candidate = bytes[offset];
            if (candidate < actualPlaneCount && candidate > max)
            {
                max = candidate;
            }
        }

        return max;
    }

    private RawPalette LoadPalette(CczProject project)
    {
        var spaletPath = ResolveLocalSpaletPath(project);
        if (spaletPath != null)
        {
            var key = "spalet:" + Path.GetFullPath(spaletPath);
            if (_paletteCache.TryGetValue(key, out var cached)) return cached;
            var colors = TryLoadSpaletPalette(spaletPath);
            if (colors.Count >= 256)
            {
                var palette = new RawPalette(colors, "Spalet.e5", spaletPath);
                _paletteCache[key] = palette;
                return palette;
            }
        }

        var cleanPath = ResolveCleanPalettePath(project);
        if (cleanPath != null)
        {
            var key = "clean:" + Path.GetFullPath(cleanPath);
            if (_paletteCache.TryGetValue(key, out var cached)) return cached;
            var colors = LoadCleanPalette(cleanPath);
            if (colors.Count >= 256)
            {
                var palette = new RawPalette(colors, "tsb", cleanPath);
                _paletteCache[key] = palette;
                return palette;
            }
        }

        return new RawPalette(Array.Empty<Color>(), "灰度回退", string.Empty);
    }

    private IReadOnlyList<Color> TryLoadSpaletPalette(string path)
    {
        try
        {
            foreach (var entry in _e5ImageService.ReadIndex(path))
            {
                if (entry.DecodedLength < 256 * 3) continue;
                var bytes = _e5ImageService.ReadEntryBytes(path, entry.ImageNumber);
                if (bytes.Length < 256 * 3) continue;

                var colors = new Color[256];
                for (var i = 0; i < colors.Length; i++)
                {
                    var offset = i * 3;
                    colors[i] = Color.FromArgb(255, bytes[offset + 1], bytes[offset + 2], bytes[offset]);
                }

                return colors;
            }
        }
        catch
        {
            return Array.Empty<Color>();
        }

        return Array.Empty<Color>();
    }

    private static IReadOnlyList<Color> LoadCleanPalette(string path)
    {
        var bytes = File.ReadAllBytes(path);
        if (bytes.Length < 256 * 4) return Array.Empty<Color>();

        var colors = new Color[256];
        for (var i = 0; i < colors.Length; i++)
        {
            var offset = i * 4;
            colors[i] = Color.FromArgb(255, bytes[offset + 2], bytes[offset + 1], bytes[offset]);
        }

        return colors;
    }

    private static string? ResolveLocalSpaletPath(CczProject project)
    {
        var parentRoot = Directory.GetParent(project.GameRoot)?.FullName ?? project.WorkspaceRoot;
        var candidates = new[]
        {
            Path.Combine(project.GameRoot, "Spalet.e5"),
            Path.Combine(project.GameRoot, "E5", "Spalet.e5"),
            Path.Combine(parentRoot, "E5", "Spalet.e5")
        };

        return candidates.FirstOrDefault(path => File.Exists(path));
    }

    private static string? ResolveCleanPalettePath(CczProject project)
    {
        var candidates = new[]
        {
            PortableInstallPaths.PaletteTsbPath,
            Path.Combine(project.GameRoot, "tsb")
        };

        return candidates.FirstOrDefault(path => File.Exists(path) && new FileInfo(path).Length >= 256 * 4);
    }

    private static Bitmap RenderIndexedFrame(
        byte[] bytes,
        int pixelOffset,
        int width,
        int height,
        IReadOnlyList<Color> palette)
    {
        var frame = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var value = bytes[pixelOffset + y * width + x];
                if (value == 0)
                {
                    frame.SetPixel(x, y, Color.Transparent);
                    continue;
                }

                var color = value < palette.Count
                    ? palette[value]
                    : Color.FromArgb(255, Math.Min(255, 32 + value), Math.Min(255, 32 + value), Math.Min(255, 32 + value));
                frame.SetPixel(x, y, IsMagentaKey(color) ? Color.Transparent : color);
            }
        }

        return frame;
    }

    private static Bitmap RenderFrameToCanvas(Image source, int canvasSize)
    {
        var width = Math.Max(96, canvasSize);
        var height = Math.Max(96, canvasSize);
        var canvas = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(canvas);
        g.Clear(Color.FromArgb(28, 30, 32));
        g.InterpolationMode = InterpolationMode.NearestNeighbor;
        g.PixelOffsetMode = PixelOffsetMode.Half;
        g.CompositingQuality = CompositingQuality.HighSpeed;

        using var borderPen = new Pen(Color.FromArgb(90, 100, 108));
        var rect = new Rectangle(8, 8, width - 16, height - 16);
        g.DrawRectangle(borderPen, rect);

        var scale = Math.Min((rect.Width - 8) / (float)source.Width, (rect.Height - 8) / (float)source.Height);
        if (float.IsNaN(scale) || float.IsInfinity(scale) || scale <= 0) scale = 1;
        var drawWidth = Math.Max(1, (int)Math.Round(source.Width * scale));
        var drawHeight = Math.Max(1, (int)Math.Round(source.Height * scale));
        var drawX = rect.Left + (rect.Width - drawWidth) / 2;
        var drawY = rect.Top + (rect.Height - drawHeight) / 2;
        g.DrawImage(source, new Rectangle(drawX, drawY, drawWidth, drawHeight));
        return canvas;
    }

    private static bool IsMagentaKey(Color pixel)
    {
        if (pixel.A == 0) return true;
        return pixel.R >= 210 &&
               pixel.B >= 210 &&
               pixel.G <= 90 &&
               Math.Abs(pixel.R - pixel.B) <= 70;
    }

    private sealed record RawPalette(IReadOnlyList<Color> Colors, string Mode, string Path);
}

internal sealed record StrategyAnimationFrameDecodeResult(
    bool Success,
    IReadOnlyList<Bitmap> Frames,
    int FrameIntervalMs,
    bool Loop,
    string RenderMode,
    string Message,
    int Width,
    int Height,
    int DeclaredPlaneCount,
    int ActualPlaneCount)
{
    public static StrategyAnimationFrameDecodeResult Fail(string message)
        => new(
            false,
            Array.Empty<Bitmap>(),
            StrategyAnimationPreviewService.DefaultFrameIntervalMs,
            false,
            "不可解析",
            message,
            0,
            0,
            0,
            0);
}
