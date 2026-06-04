using CCZModStudio.Models;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace CCZModStudio.Core;

public sealed class StrategyAnimationPreviewService
{
    private readonly E5ImageReplaceService _e5ImageService = new();
    private readonly Dictionary<string, IReadOnlyList<E5ImageEntryInfo>> _indexCache = new(StringComparer.OrdinalIgnoreCase);

    public void ClearCache()
    {
        _indexCache.Clear();
    }

    public StrategyAnimationPreviewResult BuildPreview(CczProject project, int animationValue, int canvasSize = 160)
    {
        var sourcePath = ResolveAnimationPath(project);
        if (!File.Exists(sourcePath))
        {
            return new StrategyAnimationPreviewResult(
                sourcePath,
                animationValue,
                animationValue,
                0,
                null,
                "未找到 E5\\Meff.e5，暂无法显示策略动画预览。");
        }

        var entries = GetIndex(sourcePath);
        if (animationValue == 255)
        {
            return new StrategyAnimationPreviewResult(
                sourcePath,
                animationValue,
                0,
                entries.Count,
                null,
                $"策略动画字段值 255 通常表示无动画或保留值；Meff.e5 当前条目数={entries.Count}。");
        }

        var imageNumber = animationValue + 1;
        if (imageNumber <= 0 || imageNumber > entries.Count)
        {
            var rangeText = entries.Count > 0 ? $"0-{entries.Count - 1}" : "无可用条目";
            return new StrategyAnimationPreviewResult(
                sourcePath,
                animationValue,
                imageNumber,
                entries.Count,
                null,
                $"策略动画字段值 {animationValue} 没有匹配 Meff.e5 条目；当前可预览字段值范围：{rangeText}。");
        }

        try
        {
            var bytes = _e5ImageService.ReadEntryBytes(sourcePath, imageNumber);
            using var decoded = TryDecodeImage(bytes);
            var entry = entries[imageNumber - 1];
            if (decoded == null)
            {
                return new StrategyAnimationPreviewResult(
                    sourcePath,
                    animationValue,
                    imageNumber,
                    entries.Count,
                    null,
                    $"策略动画字段值 {animationValue} -> Meff.e5 图号 #{imageNumber}（字段值+1）；条目存在，但不是可直接解码的 BMP/JPG/PNG。索引：offset=0x{entry.DataOffset:X}，size={entry.StoredLength:N0}/{entry.DecodedLength:N0}，类型={entry.Kind}。");
            }

            var bitmap = RenderCanvas(decoded, canvasSize);
            var info = new FileInfo(sourcePath);
            var message =
                $"策略动画字段值 {animationValue} -> Meff.e5 图号 #{imageNumber}（字段值+1）；条目数={entries.Count}。\r\n" +
                $"索引：offset=0x{entry.DataOffset:X}，size={entry.StoredLength:N0}/{entry.DecodedLength:N0}，类型={entry.Kind}。\r\n" +
                $"文件：{info.Length:N0} 字节，修改时间 {info.LastWriteTime:yyyy-MM-dd HH:mm:ss}。";
            return new StrategyAnimationPreviewResult(
                sourcePath,
                animationValue,
                imageNumber,
                entries.Count,
                bitmap,
                message);
        }
        catch (Exception ex)
        {
            return new StrategyAnimationPreviewResult(
                sourcePath,
                animationValue,
                imageNumber,
                entries.Count,
                null,
                $"策略动画字段值 {animationValue} 读取失败：{ex.Message}");
        }
    }

    private IReadOnlyList<E5ImageEntryInfo> GetIndex(string path)
    {
        path = Path.GetFullPath(path);
        if (_indexCache.TryGetValue(path, out var cached)) return cached;
        var entries = _e5ImageService.ReadIndex(path);
        _indexCache[path] = entries;
        return entries;
    }

    private static string ResolveAnimationPath(CczProject project)
    {
        var parentRoot = Directory.GetParent(project.GameRoot)?.FullName ?? project.WorkspaceRoot;
        var candidates = new[]
        {
            Path.Combine(project.GameRoot, "E5", "Meff.e5"),
            Path.Combine(project.GameRoot, "e5", "Meff.e5"),
            Path.Combine(project.GameRoot, "Meff.e5"),
            Path.Combine(parentRoot, "E5", "Meff.e5"),
            Path.Combine(project.WorkspaceRoot, "E5", "Meff.e5")
        };

        return candidates.FirstOrDefault(File.Exists) ?? candidates[0];
    }

    private static Bitmap? TryDecodeImage(byte[] bytes)
    {
        if (bytes.Length < 2) return null;
        var isBmp = bytes[0] == (byte)'B' && bytes[1] == (byte)'M';
        var isJpeg = bytes[0] == 0xFF && bytes[1] == 0xD8;
        var isPng = bytes.Length >= 8 &&
                    bytes[0] == 0x89 &&
                    bytes[1] == (byte)'P' &&
                    bytes[2] == (byte)'N' &&
                    bytes[3] == (byte)'G';
        if (!isBmp && !isJpeg && !isPng) return null;

        try
        {
            using var memory = new MemoryStream(bytes, writable: false);
            using var image = Image.FromStream(memory, useEmbeddedColorManagement: false, validateImageData: false);
            return new Bitmap(image);
        }
        catch
        {
            return null;
        }
    }

    private static Bitmap RenderCanvas(Image source, int canvasSize)
    {
        var size = Math.Max(96, canvasSize);
        var canvas = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(canvas);
        g.Clear(Color.White);
        g.InterpolationMode = InterpolationMode.NearestNeighbor;
        g.PixelOffsetMode = PixelOffsetMode.Half;
        g.CompositingQuality = CompositingQuality.HighSpeed;

        var scale = Math.Min((size - 16) / (float)source.Width, (size - 16) / (float)source.Height);
        if (float.IsNaN(scale) || float.IsInfinity(scale) || scale <= 0) scale = 1;
        var width = Math.Max(1, (int)Math.Round(source.Width * scale));
        var height = Math.Max(1, (int)Math.Round(source.Height * scale));
        var left = (size - width) / 2;
        var top = (size - height) / 2;
        g.DrawImage(source, new Rectangle(left, top, width, height));

        using var borderPen = new Pen(Color.FromArgb(120, 128, 136));
        g.DrawRectangle(borderPen, 0, 0, size - 1, size - 1);
        return canvas;
    }
}

public sealed record StrategyAnimationPreviewResult(
    string SourcePath,
    int FieldValue,
    int ImageNumber,
    int EntryCount,
    Bitmap? Bitmap,
    string Message);
