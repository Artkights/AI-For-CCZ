using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using CCZModStudio.Models;

namespace CCZModStudio.Formats;

/// <summary>
/// EEX/Ls/E5 字节热力图只读可视化。
/// 该服务不尝试解压、解密或重封包，只把文件/候选区段/载荷范围的原始字节压缩成可观察的颜色网格，
/// 用于辅助判断“文本/参数表/压缩载荷/透明或稀疏数据”等候选性质。
/// </summary>
public sealed class EexByteHeatmapService
{
    private const int DefaultPreferredWidth = 128;
    private const int DefaultMaxCells = 32768;

    public EexByteHeatmapResult Analyze(
        string path,
        string category,
        int? offset = null,
        int? length = null,
        string sourceKind = "整文件",
        int preferredWidth = DefaultPreferredWidth,
        int maxCells = DefaultMaxCells)
    {
        var bytes = File.ReadAllBytes(path);
        if (bytes.Length == 0)
        {
            throw new InvalidOperationException("文件为空，无法生成字节热力图。");
        }

        var start = Math.Clamp(offset ?? 0, 0, bytes.Length - 1);
        var requestedLength = length.GetValueOrDefault(bytes.Length - start);
        if (requestedLength <= 0)
        {
            requestedLength = bytes.Length - start;
        }

        var actualLength = Math.Min(requestedLength, bytes.Length - start);
        if (actualLength <= 0)
        {
            throw new InvalidOperationException("选中的字节范围为空，无法生成字节热力图。");
        }

        preferredWidth = Math.Clamp(preferredWidth, 32, 256);
        maxCells = Math.Clamp(maxCells, preferredWidth, 131072);
        var bytesPerCell = Math.Max(1, (int)Math.Ceiling(actualLength / (double)maxCells));
        var cellCount = (actualLength + bytesPerCell - 1) / bytesPerCell;
        var width = Math.Min(preferredWidth, Math.Max(1, cellCount));
        var height = Math.Max(1, (cellCount + width - 1) / width);
        var cells = BuildCells(bytes, start, actualLength, bytesPerCell, cellCount);
        var span = bytes.AsSpan(start, actualLength);
        var histogram = BuildHistogram(span);
        var textHits = BinaryTextScanner.ScanGbkNullTerminatedStringHits(span.ToArray(), minByteLength: 5, maxItems: 6, requireCjk: false);
        var textHints = textHits.Count == 0
            ? string.Empty
            : string.Join(" / ", textHits.Select(x => $"0x{start + x.Offset:X6}:{(x.Text.Length > 24 ? x.Text[..24] + "…" : x.Text)}"));
        var unique = histogram.Count(x => x > 0);
        var zeroPercent = histogram[0] * 100.0 / actualLength;
        var ffPercent = histogram[0xFF] * 100.0 / actualLength;
        var smallWordPercent = ComputeSmallWordPercent(span);
        var entropy = ComputeEntropy(histogram, actualLength);
        var role = InferRole(actualLength, unique, zeroPercent, ffPercent, smallWordPercent, entropy, textHits.Count);

        return new EexByteHeatmapResult
        {
            FileName = Path.GetFileName(path),
            Category = category,
            SourceKind = string.IsNullOrWhiteSpace(sourceKind) ? "整文件" : sourceKind,
            Offset = start,
            Length = actualLength,
            Width = width,
            Height = height,
            BytesPerCell = bytesPerCell,
            CellValues = cells,
            UniqueByteCount = unique,
            ZeroPercent = zeroPercent,
            FFPercent = ffPercent,
            SmallWordPercent = smallWordPercent,
            Entropy = entropy,
            TopBytes = FormatTopBytes(histogram),
            TopWords = FormatTopWords(span),
            TextHintCount = textHits.Count,
            TextHints = textHints,
            RoleHint = role,
            Annotation = BuildAnnotation(role, actualLength, bytesPerCell, unique, zeroPercent, ffPercent, smallWordPercent, entropy, textHits.Count),
            Path = path
        };
    }

    public Bitmap Render(EexByteHeatmapResult result, int cellSize = 4)
    {
        if (result.CellValues.Length == 0)
        {
            throw new InvalidOperationException("热力图单元为空，无法渲染。");
        }

        cellSize = Math.Clamp(cellSize, 2, 12);
        var titleHeight = 66;
        var legendHeight = 46;
        var plotWidth = result.Width * cellSize;
        var plotHeight = result.Height * cellSize;
        var bitmapWidth = Math.Max(720, plotWidth + 32);
        var bitmapHeight = Math.Max(280, titleHeight + plotHeight + legendHeight + 24);
        var bitmap = new Bitmap(bitmapWidth, bitmapHeight);
        using var g = Graphics.FromImage(bitmap);
        g.Clear(Color.FromArgb(248, 248, 248));
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using var titleFont = new Font("Microsoft YaHei UI", 10.5F, FontStyle.Bold, GraphicsUnit.Point);
        using var smallFont = new Font("Microsoft YaHei UI", 8.5F, FontStyle.Regular, GraphicsUnit.Point);
        using var textBrush = new SolidBrush(Color.FromArgb(28, 28, 28));
        using var mutedBrush = new SolidBrush(Color.FromArgb(92, 92, 92));
        using var borderPen = new Pen(Color.FromArgb(70, 70, 70));

        var title = $"{result.Category}/{result.FileName}  {result.SourceKind}  {result.OffsetHex}-{result.EndOffsetHex}";
        g.DrawString(title, titleFont, textBrush, 12, 8);
        g.DrawString(
            $"长度 {result.Length:N0} 字节；每格约 {result.BytesPerCell:N0} 字节；网格 {result.Width}x{result.Height}；{result.RoleHint}",
            smallFont,
            mutedBrush,
            12,
            34);

        var originX = 12;
        var originY = titleHeight;
        for (var i = 0; i < result.CellValues.Length; i++)
        {
            var x = i % result.Width;
            var y = i / result.Width;
            using var brush = new SolidBrush(ToHeatColor(result.CellValues[i]));
            g.FillRectangle(brush, originX + x * cellSize, originY + y * cellSize, cellSize, cellSize);
        }

        g.DrawRectangle(borderPen, originX, originY, plotWidth, plotHeight);

        var legendX = 12;
        var legendY = originY + plotHeight + 14;
        for (var i = 0; i <= 255; i++)
        {
            using var brush = new SolidBrush(ToHeatColor((byte)i));
            g.FillRectangle(brush, legendX + i, legendY, 1, 12);
        }

        g.DrawRectangle(borderPen, legendX, legendY, 256, 12);
        g.DrawString("00", smallFont, mutedBrush, legendX, legendY + 15);
        g.DrawString("原始字节值颜色刻度（低值→高值）", smallFont, mutedBrush, legendX + 72, legendY + 15);
        g.DrawString("FF", smallFont, mutedBrush, legendX + 236, legendY + 15);
        g.DrawString(
            $"00占比 {result.ZeroPercent:F1}%；FF占比 {result.FFPercent:F1}%；熵 {result.Entropy:F2}；高频字节 {result.TopBytes}",
            smallFont,
            mutedBrush,
            legendX + 286,
            legendY + 4);

        return bitmap;
    }

    private static byte[] BuildCells(byte[] bytes, int offset, int length, int bytesPerCell, int cellCount)
    {
        var cells = new byte[cellCount];
        for (var cell = 0; cell < cellCount; cell++)
        {
            var start = offset + cell * bytesPerCell;
            var end = Math.Min(offset + length, start + bytesPerCell);
            var sum = 0L;
            for (var i = start; i < end; i++)
            {
                sum += bytes[i];
            }
            cells[cell] = (byte)Math.Round(sum / (double)Math.Max(1, end - start), MidpointRounding.AwayFromZero);
        }

        return cells;
    }

    private static int[] BuildHistogram(ReadOnlySpan<byte> span)
    {
        var histogram = new int[256];
        foreach (var value in span)
        {
            histogram[value]++;
        }
        return histogram;
    }

    private static double ComputeEntropy(int[] histogram, int length)
    {
        if (length <= 0) return 0;
        var entropy = 0.0;
        foreach (var count in histogram)
        {
            if (count == 0) continue;
            var p = count / (double)length;
            entropy -= p * Math.Log2(p);
        }
        return entropy;
    }

    private static double ComputeSmallWordPercent(ReadOnlySpan<byte> span)
    {
        var words = span.Length / 2;
        if (words == 0) return 0;
        var small = 0;
        for (var i = 0; i + 1 < span.Length; i += 2)
        {
            var value = span[i] | (span[i + 1] << 8);
            if (value <= 0x03FF || value == 0xFFFF) small++;
        }
        return small * 100.0 / words;
    }

    private static string FormatTopBytes(int[] histogram)
    {
        return string.Join(", ", histogram
            .Select((count, value) => new { Value = value, Count = count })
            .Where(x => x.Count > 0)
            .OrderByDescending(x => x.Count)
            .ThenBy(x => x.Value)
            .Take(8)
            .Select(x => $"0x{x.Value:X2}:{x.Count}"));
    }

    private static string FormatTopWords(ReadOnlySpan<byte> span)
    {
        var counts = new Dictionary<int, int>();
        for (var i = 0; i + 1 < span.Length; i += 2)
        {
            var value = span[i] | (span[i + 1] << 8);
            counts[value] = counts.GetValueOrDefault(value) + 1;
        }

        return string.Join(", ", counts
            .OrderByDescending(x => x.Value)
            .ThenBy(x => x.Key)
            .Take(8)
            .Select(x => $"0x{x.Key:X4}:{x.Value}"));
    }

    private static string InferRole(
        int length,
        int unique,
        double zeroPercent,
        double ffPercent,
        double smallWordPercent,
        double entropy,
        int textCount)
    {
        if (textCount >= 2) return "文本/动作说明混合区候选";
        if (smallWordPercent >= 85 && length <= 65536) return "小整数参数表/帧表候选";
        if (zeroPercent >= 70) return "稀疏/透明数据候选";
        if (ffPercent >= 35) return "FF填充或遮罩数据候选";
        if (entropy >= 7.2 && unique >= 180) return "高熵压缩载荷候选";
        if (unique >= 96 && length >= 1024) return "图像/压缩混合载荷候选";
        return "未知二进制区段候选";
    }

    private static string BuildAnnotation(
        string role,
        int length,
        int bytesPerCell,
        int unique,
        double zeroPercent,
        double ffPercent,
        double smallWordPercent,
        double entropy,
        int textCount)
    {
        return $"只读字节热力图：{role}。本图每个色块汇总约 {bytesPerCell:N0} 字节，用颜色显示原始字节均值；" +
               $"范围长度 {length:N0}，不同字节 {unique}，00占比 {zeroPercent:F1}%，FF占比 {ffPercent:F1}%，" +
               $"小整数16位词占比 {smallWordPercent:F1}%，熵 {entropy:F2}。" +
               (textCount > 0 ? $"发现 {textCount} 条 GBK 文本线索，可配合区段探针判断动作名或说明。" : "未发现明显 GBK 文本线索。") +
               "该功能不解压、不复原帧图、不写入 EEX；只能作为格式研究和异常排查证据。";
    }

    private static Color ToHeatColor(byte value)
    {
        var t = value / 255.0;
        var hue = (1.0 - t) * 240.0;
        return FromHsv(hue, 0.88, 0.18 + 0.78 * Math.Sqrt(t));
    }

    private static Color FromHsv(double hue, double saturation, double value)
    {
        var c = value * saturation;
        var x = c * (1 - Math.Abs(hue / 60.0 % 2 - 1));
        var m = value - c;
        var (r1, g1, b1) = hue switch
        {
            < 60 => (c, x, 0.0),
            < 120 => (x, c, 0.0),
            < 180 => (0.0, c, x),
            < 240 => (0.0, x, c),
            < 300 => (x, 0.0, c),
            _ => (c, 0.0, x)
        };

        return Color.FromArgb(
            ClampColor((r1 + m) * 255),
            ClampColor((g1 + m) * 255),
            ClampColor((b1 + m) * 255));
    }

    private static int ClampColor(double value)
        => Math.Clamp((int)Math.Round(value, MidpointRounding.AwayFromZero), 0, 255);
}
