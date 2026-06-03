using System.Globalization;
using System.Text;
using CCZModStudio.Core;
using CCZModStudio.Models;

namespace CCZModStudio.Formats;

public sealed class BatchMoveParser
{
    public BatchMoveDocument Parse(string path)
    {
        if (!File.Exists(path)) throw new FileNotFoundException("搬运配置文件不存在。", path);

        var entries = new List<BatchMoveEntry>();
        string? pendingComment = null;
        foreach (var (rawLine, index) in ReadAllLinesSmart(path).Select((x, i) => (x, i + 1)))
        {
            var line = rawLine.Trim();
            if (string.IsNullOrWhiteSpace(line)) continue;
            if (line.StartsWith('#'))
            {
                pendingComment = line.TrimStart('#').Trim();
                continue;
            }

            var parts = line.Split(new[] { ' ', '\t', ',' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 3)
            {
                throw new InvalidOperationException($"搬运配置第 {index} 行至少需要：源偏移 目标偏移 长度。实际：{line}");
            }

            entries.Add(new BatchMoveEntry
            {
                Index = entries.Count + 1,
                SourceLine = index,
                Comment = pendingComment ?? string.Empty,
                SourceOffset = ParseHexOffset(parts[0]),
                TargetOffset = ParseHexOffset(parts[1]),
                Length = ParseLength(parts[2])
            });
            pendingComment = null;
        }

        return new BatchMoveDocument { SourcePath = path, Entries = entries };
    }

    private static long ParseHexOffset(string text)
    {
        text = text.Trim();
        if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) text = text[2..];
        return long.Parse(text, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
    }

    private static int ParseLength(string text)
    {
        text = text.Trim();
        if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            return checked((int)long.Parse(text[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture));
        }

        // 普罗搬运配置中偏移使用十六进制；长度样例同时出现 2600/8000/32768 等，按旧工具习惯优先当作十进制长度。
        return int.Parse(text, NumberStyles.Integer, CultureInfo.InvariantCulture);
    }

    private static string[] ReadAllLinesSmart(string path)
    {
        var bytes = File.ReadAllBytes(path);
        try
        {
            return new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true)
                .GetString(bytes)
                .Replace("\r\n", "\n", StringComparison.Ordinal)
                .Replace('\r', '\n')
                .Split('\n');
        }
        catch (DecoderFallbackException)
        {
            EncodingService.EnsureCodePages();
            return EncodingService.Gbk
                .GetString(bytes)
                .Replace("\r\n", "\n", StringComparison.Ordinal)
                .Replace('\r', '\n')
                .Split('\n');
        }
    }
}
