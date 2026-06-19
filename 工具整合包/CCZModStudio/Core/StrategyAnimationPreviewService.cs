using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Text;
using CCZModStudio.Formats;
using CCZModStudio.Models;

namespace CCZModStudio.Core;

public sealed class StrategyAnimationPreviewService
{
    internal const int DefaultFrameIntervalMs = 120;
    private const int MaxCachedAnimatedPreviews = 32;
    private readonly E5ImageReplaceService _e5ImageService = new();
    private readonly E5ImageRenderService _renderService = new();
    private readonly StrategyAnimationFrameDecoder _frameDecoder = new();
    private readonly Dictionary<string, IReadOnlyList<E5ImageEntryInfo>> _indexCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, CachedAnimatedPreview> _animatedPreviewCache = new(StringComparer.OrdinalIgnoreCase);

    public void ClearCache()
    {
        _indexCache.Clear();
        foreach (var cached in _animatedPreviewCache.Values)
        {
            cached.Dispose();
        }

        _animatedPreviewCache.Clear();
    }

    public StrategyAnimationPreviewResult BuildPreview(CczProject project, int animationValue, int canvasSize = 160)
        => BuildPreview(project, StrategyAnimationPreviewKind.SmallMeff, animationValue, canvasSize);

    public StrategyAnimationPreviewResult BuildPreview(
        CczProject project,
        StrategyAnimationPreviewKind kind,
        int animationValue,
        int canvasSize = 160)
    {
        var animated = BuildAnimatedPreview(project, kind, animationValue, canvasSize);
        try
        {
            return new StrategyAnimationPreviewResult(
                animated.SourcePath,
                animated.FieldValue,
                animated.ImageNumber,
                animated.EntryCount,
                animated.FirstFrame == null ? null : new Bitmap(animated.FirstFrame),
                animated.Message);
        }
        finally
        {
            DisposeFrames(animated.Frames);
        }
    }

    private StrategyAnimationAnimatedPreviewResult BuildSmallMeffPreview(CczProject project, int animationValue, int canvasSize)
    {
        var sourcePath = ResolveMeffPath(project);
        if (!File.Exists(sourcePath))
        {
            return new StrategyAnimationAnimatedPreviewResult(
                sourcePath,
                animationValue,
                animationValue,
                0,
                Array.Empty<Bitmap>(),
                DefaultFrameIntervalMs,
                false,
                "不可预览",
                "未找到 E5\\Meff.e5，暂无法显示策略动画预览。");
        }

        var entries = GetIndex(sourcePath);
        if (animationValue == 255)
        {
            return new StrategyAnimationAnimatedPreviewResult(
                sourcePath,
                animationValue,
                0,
                entries.Count,
                Array.Empty<Bitmap>(),
                DefaultFrameIntervalMs,
                false,
                "无动画",
                $"小动画 / Meff 字段值 255 通常表示无动画或保留值；Meff.e5 当前条目数={entries.Count}。");
        }

        var imageNumber = animationValue + 1;
        if (imageNumber <= 0 || imageNumber > entries.Count)
        {
            var rangeText = entries.Count > 0 ? $"0-{entries.Count - 1}" : "无可用条目";
            return new StrategyAnimationAnimatedPreviewResult(
                sourcePath,
                animationValue,
                imageNumber,
                entries.Count,
                Array.Empty<Bitmap>(),
                DefaultFrameIntervalMs,
                false,
                "越界",
                $"小动画 / Meff 字段值 {animationValue} 没有匹配 Meff.e5 条目；当前可预览字段值范围：{rangeText}。");
        }

        try
        {
            var bytes = _e5ImageService.ReadEntryBytes(sourcePath, imageNumber);
            var entry = entries[imageNumber - 1];
            var animation = _frameDecoder.Decode(project, StrategyAnimationPreviewKind.SmallMeff, bytes, canvasSize);
            if (animation.Success)
            {
                var meffInfo = new FileInfo(sourcePath);
                var animationMessage =
                    $"小动画 / Meff 字段值 {animationValue} -> Meff.e5 图号 #{imageNumber}（字段值+1）；条目数={entries.Count}。\r\n" +
                    $"索引：offset={HexDisplayFormatter.FormatOffset(entry.DataOffset)}，size={entry.StoredLength:N0}/{entry.DecodedLength:N0}，类型={entry.Kind}。\r\n" +
                    animation.Message + "\r\n" +
                    $"文件：{meffInfo.Length:N0} 字节，修改时间 {meffInfo.LastWriteTime:yyyy-MM-dd HH:mm:ss}。";
                return new StrategyAnimationAnimatedPreviewResult(
                    sourcePath,
                    animationValue,
                    imageNumber,
                    entries.Count,
                    animation.Frames,
                    animation.FrameIntervalMs,
                    animation.Loop,
                    animation.RenderMode,
                    animationMessage);
            }

            using var decoded = _renderService.TryDecodeStandardImage(bytes);
            var bitmap = decoded == null ? null : RenderFrameToCanvas(decoded, canvasSize);
            if (bitmap == null)
            {
                return new StrategyAnimationAnimatedPreviewResult(
                    sourcePath,
                    animationValue,
                    imageNumber,
                    entries.Count,
                    Array.Empty<Bitmap>(),
                    DefaultFrameIntervalMs,
                    false,
                    "Meff 条目状态",
                    $"小动画 / Meff 字段值 {animationValue} -> Meff.e5 图号 #{imageNumber}（字段值+1）；条目存在，但结构化动画解析未通过，也不是标准图片。\r\n" +
                    $"索引：offset={HexDisplayFormatter.FormatOffset(entry.DataOffset)}，size={entry.StoredLength:N0}/{entry.DecodedLength:N0}，类型={entry.Kind}。\r\n" +
                    animation.Message);
            }

            var info = new FileInfo(sourcePath);
            var message =
                $"小动画 / Meff 字段值 {animationValue} -> Meff.e5 图号 #{imageNumber}（字段值+1）；条目数={entries.Count}。\r\n" +
                $"索引：offset={HexDisplayFormatter.FormatOffset(entry.DataOffset)}，size={entry.StoredLength:N0}/{entry.DecodedLength:N0}，类型={entry.Kind}。\r\n" +
                $"预览模式：标准图片单帧；非标准 Meff 帧格式需用旧工具或实机确认。\r\n" +
                $"文件：{info.Length:N0} 字节，修改时间 {info.LastWriteTime:yyyy-MM-dd HH:mm:ss}。";
            return new StrategyAnimationAnimatedPreviewResult(
                sourcePath,
                animationValue,
                imageNumber,
                entries.Count,
                [bitmap],
                DefaultFrameIntervalMs,
                false,
                "标准图片",
                message);
        }
        catch (Exception ex)
        {
            return new StrategyAnimationAnimatedPreviewResult(
                sourcePath,
                animationValue,
                imageNumber,
                entries.Count,
                Array.Empty<Bitmap>(),
                DefaultFrameIntervalMs,
                false,
                "读取失败",
                $"小动画 / Meff 字段值 {animationValue} 读取失败：{ex.Message}");
        }
    }

    public StrategyAnimationAnimatedPreviewResult BuildAnimatedPreview(CczProject project, int animationValue, int canvasSize = 160)
        => BuildAnimatedPreview(project, StrategyAnimationPreviewKind.SmallMeff, animationValue, canvasSize);

    public StrategyAnimationAnimatedPreviewResult BuildAnimatedPreview(
        CczProject project,
        StrategyAnimationPreviewKind kind,
        int animationValue,
        int canvasSize = 160)
    {
        if (kind == StrategyAnimationPreviewKind.BigMcall)
        {
            return BuildBigMcallPreview(project, animationValue, canvasSize);
        }

        var sourcePath = ResolveMeffPath(project);
        var info = new FileInfo(sourcePath);
        var cacheKey = $"{kind}|{Path.GetFullPath(sourcePath)}|{(info.Exists ? info.LastWriteTimeUtc.Ticks : 0)}|{(info.Exists ? info.Length : 0)}|{animationValue}|{canvasSize}";
        if (_animatedPreviewCache.TryGetValue(cacheKey, out var cached))
        {
            return cached.ToResult();
        }

        var fresh = BuildSmallMeffPreview(project, animationValue, canvasSize);
        try
        {
            var result = Cache(fresh);
            AddAnimatedPreviewCache(cacheKey, result);
            return result.ToResult();
        }
        finally
        {
            DisposeFrames(fresh.Frames);
        }
    }

    private StrategyAnimationAnimatedPreviewResult BuildBigMcallPreview(CczProject project, int animationValue, int canvasSize)
    {
        if (animationValue == 255 || animationValue < 100)
        {
            var placeholderPath = ResolveMcallPath(project, 0);
            return new StrategyAnimationAnimatedPreviewResult(
                placeholderPath,
                animationValue,
                0,
                0,
                Array.Empty<Bitmap>(),
                DefaultFrameIntervalMs,
                false,
                "无 Mcall 大动画",
                $"大动画 / Mcall 字段值 {animationValue} 不调用 Mcall；小于 100 或 255 按无大动画处理。若需查看后续小动画，请选择“小动画 / Meff”字段。");
        }

        var mcallNumber = animationValue - 100;
        var sourcePath = ResolveMcallPath(project, mcallNumber);
        if (!File.Exists(sourcePath))
        {
            return new StrategyAnimationAnimatedPreviewResult(
                sourcePath,
                animationValue,
                mcallNumber,
                0,
                Array.Empty<Bitmap>(),
                DefaultFrameIntervalMs,
                false,
                "Mcall 缺失",
                $"大动画 / Mcall 字段值 {animationValue} -> Mcall 编号 {mcallNumber}，但未找到 {Path.GetFileName(sourcePath)}。已搜索 E5、M 与根目录，支持 D2/D3/无补零命名。");
        }

        var info = new FileInfo(sourcePath);
        var bytes = File.ReadAllBytes(sourcePath);
        var animation = _frameDecoder.Decode(project, StrategyAnimationPreviewKind.BigMcall, bytes, canvasSize);
        if (animation.Success)
        {
            var animationMessage =
                $"大动画 / Mcall 字段值 {animationValue} -> Mcall 编号 {mcallNumber}；文件 {Path.GetFileName(sourcePath)}。\r\n" +
                animation.Message + "\r\n" +
                $"文件：{info.Length:N0} 字节，修改时间 {info.LastWriteTime:yyyy-MM-dd HH:mm:ss}。";

            return new StrategyAnimationAnimatedPreviewResult(
                sourcePath,
                animationValue,
                mcallNumber,
                0,
                animation.Frames,
                animation.FrameIntervalMs,
                animation.Loop,
                animation.RenderMode,
                animationMessage);
        }

        LsResourceInfo? lsInfo = null;
        try
        {
            lsInfo = new LsResourceReader().Read(sourcePath, "策略大动画");
        }
        catch
        {
            lsInfo = null;
        }

        var magic = ReadMagic(sourcePath);
        var message =
            $"大动画 / Mcall 字段值 {animationValue} -> Mcall 编号 {mcallNumber}；文件 {Path.GetFileName(sourcePath)}。\r\n" +
            $"LS 状态：magic={magic}，大小={info.Length:N0} 字节，修改时间 {info.LastWriteTime:yyyy-MM-dd HH:mm:ss}。\r\n" +
            (lsInfo == null
                ? "LS 探针读取失败；当前仅显示文件定位，不猜测动画帧。\r\n"
                : $"载荷 offset={HexDisplayFormatter.FormatOffset(lsInfo.PayloadOffset)}，length={lsInfo.PayloadLength:N0}，unique={lsInfo.UniqueByteCount}，00占比={lsInfo.ZeroPercent:N1}%。\r\n") +
            $"结构化动画解析未通过：{animation.Message}\r\n" +
            "当前不生成猜测帧；若该策略还有小动画，请切到“小动画 / Meff”字段查看。";

        return new StrategyAnimationAnimatedPreviewResult(
            sourcePath,
            animationValue,
            mcallNumber,
            0,
            Array.Empty<Bitmap>(),
            DefaultFrameIntervalMs,
            false,
            "Mcall 文件状态",
            message);
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

    private void AddAnimatedPreviewCache(string key, CachedAnimatedPreview preview)
    {
        if (_animatedPreviewCache.Count >= MaxCachedAnimatedPreviews)
        {
            var firstKey = _animatedPreviewCache.Keys.FirstOrDefault();
            if (firstKey != null && _animatedPreviewCache.Remove(firstKey, out var stale))
            {
                stale.Dispose();
            }
        }

        _animatedPreviewCache[key] = preview;
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
        => ResolveMeffPath(project);

    private static string ResolveMeffPath(CczProject project)
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

    private static string ResolveMcallPath(CczProject project, int mcallNumber)
    {
        var parentRoot = Directory.GetParent(project.GameRoot)?.FullName ?? project.WorkspaceRoot;
        var names = new[]
        {
            $"Mcall{mcallNumber:D2}.e5",
            $"Mcall{mcallNumber:D3}.e5",
            $"Mcall{mcallNumber}.e5"
        }.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var directories = new[]
        {
            Path.Combine(project.GameRoot, "E5"),
            Path.Combine(project.GameRoot, "e5"),
            Path.Combine(project.GameRoot, "M"),
            Path.Combine(project.GameRoot, "m"),
            project.GameRoot,
            Path.Combine(parentRoot, "E5"),
            Path.Combine(parentRoot, "M"),
            Path.Combine(project.WorkspaceRoot, "E5"),
            Path.Combine(project.WorkspaceRoot, "M")
        };

        foreach (var directory in directories)
        {
            foreach (var name in names)
            {
                var path = Path.Combine(directory, name);
                if (File.Exists(path)) return path;
            }
        }

        return Path.Combine(project.GameRoot, "E5", names[0]);
    }

    private static string ReadMagic(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            var buffer = new byte[Math.Min(16, (int)Math.Min(stream.Length, 16))];
            var read = stream.Read(buffer, 0, buffer.Length);
            return read == 0 ? "<empty>" : Encoding.ASCII.GetString(buffer, 0, read).TrimEnd('\0', ' ');
        }
        catch
        {
            return "<unreadable>";
        }
    }

    private static CachedAnimatedPreview Cache(StrategyAnimationAnimatedPreviewResult result)
        => new(
            result.SourcePath,
            result.FieldValue,
            result.ImageNumber,
            result.EntryCount,
            result.Frames.Select(frame => new Bitmap(frame)).ToArray(),
            result.FrameIntervalMs,
            result.Loop,
            result.RenderMode,
            result.Message);

    private static void DisposeFrames(IEnumerable<Bitmap> frames)
    {
        foreach (var frame in frames)
        {
            frame.Dispose();
        }
    }

}

public enum StrategyAnimationPreviewKind
{
    SmallMeff,
    BigMcall
}

public sealed record StrategyAnimationPreviewResult(
    string SourcePath,
    int FieldValue,
    int ImageNumber,
    int EntryCount,
    Bitmap? Bitmap,
    string Message);

public sealed record StrategyAnimationAnimatedPreviewResult(
    string SourcePath,
    int FieldValue,
    int ImageNumber,
    int EntryCount,
    IReadOnlyList<Bitmap> Frames,
    int FrameIntervalMs,
    bool Loop,
    string RenderMode,
    string Message)
{
    public Bitmap? FirstFrame => Frames.Count == 0 ? null : Frames[0];
}

internal sealed class CachedAnimatedPreview : IDisposable
{
    public string SourcePath { get; }
    public int FieldValue { get; }
    public int ImageNumber { get; }
    public int EntryCount { get; }
    public IReadOnlyList<Bitmap> Frames { get; }
    public int FrameIntervalMs { get; }
    public bool Loop { get; }
    public string RenderMode { get; }
    public string Message { get; }

    public CachedAnimatedPreview(
        string sourcePath,
        int fieldValue,
        int imageNumber,
        int entryCount,
        IReadOnlyList<Bitmap> frames,
        int frameIntervalMs,
        bool loop,
        string renderMode,
        string message)
    {
        SourcePath = sourcePath;
        FieldValue = fieldValue;
        ImageNumber = imageNumber;
        EntryCount = entryCount;
        Frames = frames;
        FrameIntervalMs = frameIntervalMs;
        Loop = loop;
        RenderMode = renderMode;
        Message = message;
    }

    public static CachedAnimatedPreview Empty(
        string sourcePath,
        int fieldValue,
        int imageNumber,
        int entryCount,
        string renderMode,
        string message)
        => new(sourcePath, fieldValue, imageNumber, entryCount, Array.Empty<Bitmap>(), StrategyAnimationPreviewService.DefaultFrameIntervalMs, false, renderMode, message);

    public StrategyAnimationAnimatedPreviewResult ToResult()
        => new(
            SourcePath,
            FieldValue,
            ImageNumber,
            EntryCount,
            Frames.Select(frame => new Bitmap(frame)).ToArray(),
            FrameIntervalMs,
            Loop,
            RenderMode,
            Message);

    public void Dispose()
    {
        foreach (var frame in Frames)
        {
            frame.Dispose();
        }
    }
}
