using System.Drawing;
using CCZModStudio.Models;

namespace CCZModStudio.Core;

public sealed class StrategyAnimationPreviewService
{
    private readonly E5ImageReplaceService _e5ImageService = new();
    private readonly E5ImageRenderService _renderService = new();
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
            var entry = entries[imageNumber - 1];
            var bitmap = _renderService.RenderEntry(project, Path.GetFileName(sourcePath), bytes, canvasSize, canvasSize, out var renderMode);
            if (bitmap == null)
            {
                return new StrategyAnimationPreviewResult(
                    sourcePath,
                    animationValue,
                    imageNumber,
                    entries.Count,
                    null,
                    $"策略动画字段值 {animationValue} -> Meff.e5 图号 #{imageNumber}（字段值+1）；条目存在，但不是当前可渲染的图片/字节图。索引：offset=0x{entry.DataOffset:X}，size={entry.StoredLength:N0}/{entry.DecodedLength:N0}，类型={entry.Kind}。");
            }

            var info = new FileInfo(sourcePath);
            var message =
                $"策略动画字段值 {animationValue} -> Meff.e5 图号 #{imageNumber}（字段值+1）；条目数={entries.Count}。\r\n" +
                $"索引：offset=0x{entry.DataOffset:X}，size={entry.StoredLength:N0}/{entry.DecodedLength:N0}，类型={entry.Kind}。\r\n" +
                $"预览模式：{renderMode}；若显示为未知字节图候选，需用旧工具或实机确认真实帧宽高。\r\n" +
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

}

public sealed record StrategyAnimationPreviewResult(
    string SourcePath,
    int FieldValue,
    int ImageNumber,
    int EntryCount,
    Bitmap? Bitmap,
    string Message);
