using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using CCZModStudio.Models;

namespace CCZModStudio.Core;

public sealed class AttackAreaPreviewService
{
    private readonly E5ImageReplaceService _e5ImageService = new();
    private readonly Dictionary<string, IReadOnlyList<E5ImageEntryInfo>> _indexCache = new(StringComparer.OrdinalIgnoreCase);

    public void ClearCache()
    {
        _indexCache.Clear();
    }

    public AttackAreaPreviewResult BuildPreview(CczProject project, string columnName, int fieldValue, int canvasSize = 192)
    {
        var resource = ResolveResource(columnName);
        if (resource == null)
        {
            return new AttackAreaPreviewResult(
                string.Empty,
                columnName,
                fieldValue,
                0,
                0,
                null,
                "当前字段不是攻击范围或穿透，暂无范围预览。");
        }

        var sourcePath = ResolveAreaImagePath(project, resource.Value.FileName);
        if (!File.Exists(sourcePath))
        {
            return new AttackAreaPreviewResult(
                sourcePath,
                resource.Value.DisplayName,
                fieldValue,
                fieldValue + 1,
                0,
                null,
                $"未找到 {resource.Value.FileName}，无法显示{resource.Value.DisplayName}预览。");
        }

        var entries = GetIndex(sourcePath);
        var imageNumber = fieldValue + 1;
        if (fieldValue < 0 || imageNumber <= 0 || imageNumber > entries.Count)
        {
            var rangeText = entries.Count > 0 ? $"0-{entries.Count - 1}" : "无可用条目";
            return new AttackAreaPreviewResult(
                sourcePath,
                resource.Value.DisplayName,
                fieldValue,
                imageNumber,
                entries.Count,
                null,
                $"{resource.Value.DisplayName}字段值 {fieldValue} 没有匹配图片；{resource.Value.FileName} 当前可预览字段值范围：{rangeText}。");
        }

        try
        {
            var bytes = _e5ImageService.ReadEntryBytes(sourcePath, imageNumber);
            using var decoded = TryDecodeImage(bytes);
            if (decoded == null)
            {
                return new AttackAreaPreviewResult(
                    sourcePath,
                    resource.Value.DisplayName,
                    fieldValue,
                    imageNumber,
                    entries.Count,
                    null,
                    $"{resource.Value.DisplayName}字段值 {fieldValue} -> {resource.Value.FileName} 图号 #{imageNumber}，但图片条目不是可解码的 BMP/JPG/PNG。");
            }

            var bitmap = RenderCanvas(decoded, canvasSize);
            var entry = entries[imageNumber - 1];
            var info = new FileInfo(sourcePath);
            var message =
                $"{resource.Value.DisplayName}字段值={fieldValue} -> {resource.Value.FileName} 图号 #{imageNumber}；条目数={entries.Count}。\r\n" +
                $"{BuildFieldHint(resource.Value.Kind, fieldValue)}\r\n" +
                $"索引项：offset=0x{entry.DataOffset:X}，size={entry.StoredLength:N0}/{entry.DecodedLength:N0}，类型={entry.Kind}。\r\n" +
                $"文件：{info.Length:N0} 字节，修改时间 {info.LastWriteTime:yyyy-MM-dd HH:mm:ss}。";
            return new AttackAreaPreviewResult(
                sourcePath,
                resource.Value.DisplayName,
                fieldValue,
                imageNumber,
                entries.Count,
                bitmap,
                message);
        }
        catch (Exception ex)
        {
            return new AttackAreaPreviewResult(
                sourcePath,
                resource.Value.DisplayName,
                fieldValue,
                imageNumber,
                entries.Count,
                null,
                $"{resource.Value.DisplayName}字段值 {fieldValue} 读取失败：{ex.Message}");
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

    private static AreaResource? ResolveResource(string columnName)
    {
        return columnName switch
        {
            "攻击范围" or "攻击距离" => new AreaResource("Hitarea.e5", "攻击范围", AreaPreviewKind.AttackRange),
            "穿透" or "穿透范围" => new AreaResource("Effarea.e5", "穿透", AreaPreviewKind.PierceRange),
            _ => null
        };
    }

    private static string ResolveAreaImagePath(CczProject project, string fileName)
    {
        var parentRoot = Directory.GetParent(project.GameRoot)?.FullName ?? project.WorkspaceRoot;
        var candidates = new[]
        {
            Path.Combine(project.GameRoot, "E5", fileName),
            Path.Combine(project.GameRoot, "e5", fileName),
            Path.Combine(project.GameRoot, fileName),
            Path.Combine(parentRoot, "E5", fileName),
            Path.Combine(project.WorkspaceRoot, "E5", fileName)
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

        using var gridPen = new Pen(Color.FromArgb(230, 230, 230));
        var cell = Math.Max(12, size / 8);
        for (var x = 0; x <= size; x += cell) g.DrawLine(gridPen, x, 0, x, size);
        for (var y = 0; y <= size; y += cell) g.DrawLine(gridPen, 0, y, size, y);

        var scale = Math.Min((size - 18) / (float)source.Width, (size - 18) / (float)source.Height);
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

    private static string BuildFieldHint(AreaPreviewKind kind, int fieldValue)
    {
        return kind == AreaPreviewKind.AttackRange
            ? BuildAttackRangeHint(fieldValue)
            : BuildPierceRangeHint(fieldValue);
    }

    private static string BuildAttackRangeHint(int value)
    {
        return value switch
        {
            0 => "旧资料口径：十字相邻，常见于一二转骑兵、一转君主。",
            1 => "旧资料口径：周围一圈，常见于步兵系、三转骑兵、二三转君主。",
            2 => "旧资料口径：近中程弓形，常见于一转弓兵、二转弓骑兵。",
            3 => "旧资料口径：中程弓形，常见于二转弓兵、三转弓骑兵。",
            4 => "旧资料口径：远程弓形，常见于三转弓兵。",
            5 => "旧资料口径：原版未见常规兵种使用，需实机验证。",
            6 => "旧资料口径：没羽箭形状。",
            7 => "旧资料口径：一转炮车攻击形状。",
            8 => "旧资料口径：二三转炮车攻击形状。",
            9 => "旧资料口径：一转弓骑兵攻击形状。",
            10 => "旧资料口径：全屏攻击；10 以后旧资料称不可攻击，但 6.5 资源内仍有扩展图，需实机验证。",
            _ => "6.5 Hitarea.e5 扩展图候选；字段语义需结合实机目标选择验证。"
        };
    }

    private static string BuildPierceRangeHint(int value)
    {
        return value switch
        {
            0 => "旧资料口径：正常攻击，不使用穿透。",
            1 => "旧资料口径：十字穿透。",
            2 => "旧资料口径：九宫穿透。",
            3 => "旧资料口径：大没羽箭穿透。",
            4 => "旧资料口径：蛇矛穿透。",
            5 => "旧资料口径：长蛇矛，穿六个。",
            6 => "旧资料口径：大大没羽箭穿透。",
            _ => "Effarea.e5 扩展图候选；穿透效果需结合实机验证。"
        };
    }

    private readonly record struct AreaResource(string FileName, string DisplayName, AreaPreviewKind Kind);

    private enum AreaPreviewKind
    {
        AttackRange,
        PierceRange
    }
}

public sealed record AttackAreaPreviewResult(
    string SourcePath,
    string DisplayName,
    int FieldValue,
    int ImageNumber,
    int EntryCount,
    Bitmap? Bitmap,
    string Message);
