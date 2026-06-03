using System.Globalization;
using CCZModStudio.Models;

namespace CCZModStudio.Formats;

public sealed class ScenarioTextReader
{
    public IReadOnlyList<ScenarioTextEntry> Read(string path, int maxItems = 1024)
    {
        if (!File.Exists(path)) throw new FileNotFoundException("R/S eex 或旧 E5S 文件不存在。", path);
        var bytes = File.ReadAllBytes(path);
        var hits = BinaryTextScanner.ScanGbkNullTerminatedStringHits(bytes, minByteLength: 4, maxItems: maxItems, requireCjk: true);
        var result = new List<ScenarioTextEntry>();
        foreach (var hit in hits)
        {
            var cleaned = CleanText(hit.Text);
            if (string.IsNullOrWhiteSpace(cleaned)) continue;
            if (!LooksLikeScenarioText(cleaned)) continue;
            var kind = Classify(cleaned);
            result.Add(new ScenarioTextEntry
            {
                Index = result.Count + 1,
                Offset = hit.Offset,
                OffsetHex = "0x" + hit.Offset.ToString("X6", CultureInfo.InvariantCulture),
                ByteLength = hit.ByteLength,
                CharLength = cleaned.Length,
                Kind = kind,
                HasNewLines = cleaned.Contains('\n') || cleaned.Contains('\r'),
                Preview = BuildPreview(cleaned, 80),
                Text = cleaned,
                OriginalText = cleaned,
                Annotation = BuildAnnotation(cleaned, kind, hit.Offset, hit.ByteLength)
            });
        }
        return result;
    }

    private static string CleanText(string text)
    {
        text = text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Trim();
        while (text.Length > 0 && !char.IsLetterOrDigit(text[0]) && !IsCjk(text[0]))
        {
            text = text[1..].TrimStart();
        }
        return text;
    }

    private static string Classify(string text)
    {
        if (text.Contains("胜利条件", StringComparison.Ordinal) || text.Contains("失败条件", StringComparison.Ordinal)) return "胜败条件";
        if (text.Contains("提示", StringComparison.Ordinal) || text.Contains("可", StringComparison.Ordinal) && text.Contains("获得", StringComparison.Ordinal)) return "提示/奖励";
        if (text.Contains('：') || text.Contains(':')) return "对白/说明";
        if (text.Length <= 24 && !text.Contains('\n') && (text.Contains("之战", StringComparison.Ordinal) || text.Contains("军", StringComparison.Ordinal) || text.Contains("主营", StringComparison.Ordinal))) return "标题/场所";
        if (text.Length <= 24 && !text.Contains('\n')) return "短文本/标题候选";
        return text.Contains('\n') ? "多行文本" : "文本";
    }

    private static string BuildAnnotation(string text, string kind, int offset, int byteLength)
    {
        var capacity = $"原地容量 {byteLength} 个 GBK 字节；偏移 0x{offset:X6}。";
        return kind switch
        {
            "胜败条件" => "关卡目标说明文本，通常显示在战前/情报界面；建议只改文字含义，不扩容。" + capacity,
            "标题/场所" => "疑似关卡标题、地点名或阵营名；会影响创作者定位剧本章节。" + capacity,
            "短文本/标题候选" => "短文本候选，可能是标题、地点、人物名或提示片段；修改前建议结合上下文确认。" + capacity,
            "提示/奖励" => "提示或奖励说明文本，常与战利品、获得物、教程提示相关。" + capacity,
            "对白/说明" => "疑似对白或说明句；当前仅支持等长/缩短的原地写回。" + capacity,
            "多行文本" => "多行说明文本；保存时会按工具规范统一使用 LF 换行，且只能写入测试副本。" + capacity,
            _ => "普通 GBK 文本线索；当前作为格式探针结果处理，支持测试副本内原地短写回。" + capacity
        };
    }

    private static bool LooksLikeScenarioText(string text)
    {
        var cjk = text.Count(IsCjk);
        if (cjk == 0) return false;
        if (text.Length <= 3 && cjk < 2) return false;
        return cjk * 100 / text.Length >= 50
               || text.Contains("胜利条件", StringComparison.Ordinal)
               || text.Contains("失败条件", StringComparison.Ordinal);
    }

    private static string BuildPreview(string text, int maxChars)
    {
        var preview = text.Replace("\n", "\\n", StringComparison.Ordinal);
        return preview.Length > maxChars ? preview[..maxChars] + "…" : preview;
    }

    private static bool IsCjk(char ch) => ch >= 0x4E00 && ch <= 0x9FFF;
}
