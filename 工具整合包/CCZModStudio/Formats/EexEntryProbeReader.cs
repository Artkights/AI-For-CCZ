using System.Globalization;
using CCZModStudio.Core;
using CCZModStudio.Models;

namespace CCZModStudio.Formats;

/// <summary>
/// EEX 资源包只读条目/区段探针。
/// 当前不解压、不重封包、不声称已确认图像帧格式；只把头字段、候选区段、文本线索和字节统计展示给创作者。
/// </summary>
public sealed class EexEntryProbeReader
{
    private const int HeaderSize = 30;

    public IReadOnlyList<EexEntryProbeRow> Probe(string path, string category)
    {
        var bytes = File.ReadAllBytes(path);
        var fileName = Path.GetFileName(path);
        var rows = new List<EexEntryProbeRow>();
        var magicValid = bytes.Length >= 4 && bytes[0] == (byte)'E' && bytes[1] == (byte)'E' && bytes[2] == (byte)'X' && bytes[3] == 0;
        rows.Add(new EexEntryProbeRow
        {
            FileName = fileName,
            Category = category,
            Index = 0,
            NodeType = "文件头",
            OffsetHex = HexDisplayFormatter.FormatOffset(0),
            Length = Math.Min(HeaderSize, bytes.Length),
            ValueHex = bytes.Length >= 6 ? "Version=" + HexDisplayFormatter.FormatWord(BitConverter.ToUInt16(bytes, 4)) : string.Empty,
            RoleHint = magicValid ? "EEX头" : "魔数异常",
            UniqueByteCount = CountUnique(bytes.AsSpan(0, Math.Min(HeaderSize, bytes.Length))),
            ZeroPercent = ComputeZeroPercent(bytes.AsSpan(0, Math.Min(HeaderSize, bytes.Length))),
            FirstBytesHex = ToHex(bytes.AsSpan(0, Math.Min(HeaderSize, bytes.Length))),
            Annotation = magicValid
                ? "EEX\\0 魔数有效。0x04-0x05 疑似版本；0x0A-0x0D 当前按疑似条目数读取；0x0E 以后若是文件内偏移，会作为候选区段边界显示。"
                : "文件头不是 EEX\\0；后续区段推断不可靠，仅建议备份和只读查看。",
            Path = path
        });

        if (bytes.Length >= 14)
        {
            var entryCount = BitConverter.ToUInt32(bytes, 10);
            rows.Add(new EexEntryProbeRow
            {
                FileName = fileName,
                Category = category,
                Index = 1,
                NodeType = "头字段",
                OffsetHex = HexDisplayFormatter.FormatOffset(10),
                Length = 4,
                ValueHex = HexDisplayFormatter.FormatDword(entryCount),
                RoleHint = "疑似条目数",
                Annotation = $"当前按疑似条目数解释为 {entryCount}。R/S 资源常见值约 20-40，但条目表边界尚未完全确认。",
                Path = path
            });
        }

        var boundaries = new SortedSet<int> { Math.Min(HeaderSize, bytes.Length), bytes.Length };
        for (var offset = 14; offset <= 26; offset += 4)
        {
            if (bytes.Length < offset + 4) continue;
            var value = BitConverter.ToUInt32(bytes, offset);
            var valueHex = HexDisplayFormatter.FormatDword(value);
            var plausible = value >= HeaderSize && value < bytes.Length;
            if (plausible) boundaries.Add((int)value);
            rows.Add(new EexEntryProbeRow
            {
                FileName = fileName,
                Category = category,
                Index = rows.Count,
                NodeType = "头字段",
                OffsetHex = HexDisplayFormatter.FormatOffset(offset),
                Length = 4,
                ValueHex = valueHex,
                RoleHint = plausible ? "候选区段偏移" : "头字段/非偏移",
                Annotation = plausible
                    ? $"该 32 位值落在文件范围内，作为候选区段边界 {HexDisplayFormatter.FormatOffset(value)} 参与切分。"
                    : "该 32 位值不在有效文件偏移范围内，暂不作为区段边界；可能是计数、标志或其他头字段。",
                Path = path
            });
        }

        var boundaryList = boundaries.ToList();
        for (var i = 0; i < boundaryList.Count - 1; i++)
        {
            var start = boundaryList[i];
            var end = boundaryList[i + 1];
            if (end <= start) continue;
            rows.Add(BuildSectionRow(bytes, start, end - start, rows.Count, fileName, category, path));
        }

        return rows;
    }

    private static EexEntryProbeRow BuildSectionRow(byte[] bytes, int offset, int length, int index, string fileName, string category, string path)
    {
        var span = bytes.AsSpan(offset, length);
        var slice = span.ToArray();
        var textHits = BinaryTextScanner.ScanGbkNullTerminatedStringHits(slice, minByteLength: 5, maxItems: 5, requireCjk: false);
        var textHints = textHits.Count == 0
            ? string.Empty
            : string.Join(" / ", textHits.Select(x => $"{HexDisplayFormatter.FormatOffset(offset + x.Offset)}:{(x.Text.Length > 24 ? x.Text[..24] + "…" : x.Text)}"));
        var unique = CountUnique(span);
        var zero = ComputeZeroPercent(span);
        var smallWordPercent = ComputeSmallWordPercent(span);
        var role = InferSectionRole(length, textHits.Count, unique, zero, smallWordPercent);

        return new EexEntryProbeRow
        {
            FileName = fileName,
            Category = category,
            Index = index,
            NodeType = "区段候选",
            OffsetHex = HexDisplayFormatter.FormatOffset(offset),
            Length = length,
            ValueHex = HexDisplayFormatter.FormatRange(offset, offset + length - 1),
            RoleHint = role,
            TextHintCount = textHits.Count,
            TextHints = textHints,
            UniqueByteCount = unique,
            ZeroPercent = zero,
            SmallWordPercent = smallWordPercent,
            FirstBytesHex = ToHex(span[..Math.Min(48, span.Length)]),
            Annotation = BuildSectionAnnotation(role, length, textHits.Count, unique, zero, smallWordPercent),
            Path = path
        };
    }

    private static string InferSectionRole(int length, int textCount, int unique, double zeroPercent, double smallWordPercent)
    {
        if (textCount >= 2) return "文本/说明/动作名候选";
        if (smallWordPercent >= 85 && length <= 20000) return "动作参数/帧表候选";
        if (unique >= 96 && length >= 1024 && zeroPercent < 70) return "图像或压缩载荷候选";
        if (zeroPercent >= 70 && length >= 1024) return "稀疏数据/透明像素候选";
        return "未知二进制区段";
    }

    private static string BuildSectionAnnotation(string role, int length, int textCount, int unique, double zeroPercent, double smallWordPercent)
    {
        return $"{role}：长度 {length:N0} 字节，不同字节 {unique}，00占比 {zeroPercent:F1}%，小整数16位词占比 {smallWordPercent:F1}%。" +
               (textCount > 0
                   ? $"发现 {textCount} 条文本线索，可辅助判断动作名、说明或来源。"
                   : "未发现明显文本线索。") +
               "当前仅为只读候选分析，不执行 EEX 解压、帧图像还原或写回。";
    }

    private static int CountUnique(ReadOnlySpan<byte> span)
    {
        Span<bool> seen = stackalloc bool[256];
        var count = 0;
        foreach (var value in span)
        {
            if (seen[value]) continue;
            seen[value] = true;
            count++;
        }
        return count;
    }

    private static double ComputeZeroPercent(ReadOnlySpan<byte> span)
    {
        if (span.Length == 0) return 0;
        var zero = 0;
        foreach (var value in span)
        {
            if (value == 0) zero++;
        }
        return zero * 100.0 / span.Length;
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

    private static string ToHex(ReadOnlySpan<byte> span)
        => HexDisplayFormatter.FormatByteList(span.ToArray());
}
