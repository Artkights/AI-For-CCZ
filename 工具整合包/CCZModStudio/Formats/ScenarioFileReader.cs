using System.Globalization;
using System.Text.RegularExpressions;
using CCZModStudio.Core;
using CCZModStudio.Models;

namespace CCZModStudio.Formats;

/// <summary>
/// R/S eex script reader.
/// E5S is save/progress data; actual R/S scripts are RS\R_XX.eex and RS\S_XX.eex.
/// </summary>
public sealed partial class ScenarioFileReader
{
    public IReadOnlyList<ScenarioFileInfo> ReadAll(CczProject project, SceneStringDocument? sceneDictionary = null)
    {
        return EnumerateScriptFiles(project)
            .Select(x => Read(x, sceneDictionary))
            .ToList();
    }

    public IReadOnlyList<ScenarioFileInfo> ReadAllIndex(CczProject project)
    {
        return EnumerateScriptFiles(project)
            .Select(ReadIndex)
            .ToList();
    }

    public ScenarioFileInfo ReadIndex(string path)
    {
        var info = new FileInfo(path);
        var kind = Classify(info.Name, info.Length);
        var meta = TryReadEexHeader(File.Exists(path) ? ReadHeaderBytes(path) : Array.Empty<byte>(), info.Length);
        var headerText = meta.MagicValid
            ? $"EEX\\0，HeaderSize={HexDisplayFormatter.Format(meta.HeaderSize)}，区段偏移候选={FormatOffsets(meta.SectionOffsets)}。"
            : "未识别到 EEX\\0 文件头。";

        return new ScenarioFileInfo
        {
            FileName = info.Name,
            Id = ExtractNumber(info.Name),
            Length = info.Length,
            Kind = kind,
            WordCount = checked((int)Math.Min(int.MaxValue, info.Length / 2)),
            UsedBytes = info.Length,
            UsedPercent = info.Length == 0 ? 0 : 100,
            Annotation = $"R/S eex：{info.Name}，类型={kind}，大小={info.Length:N0} 字节。{headerText} 列表阶段只读取文件头，避免读取剧本列表时卡住界面。",
            UsageAnnotation = "请参考 CczSceneEditor2_v0.23 的传统习惯继续校对 eex 命令结构；未确认结构前只做索引、文本短写回和只读定位。",
            Path = path
        };
    }

    public ScenarioFileInfo Read(string path, SceneStringDocument? sceneDictionary = null)
    {
        var bytes = File.ReadAllBytes(path);
        var info = new FileInfo(path);
        var words = new ushort[bytes.Length / 2];
        for (var i = 0; i < words.Length; i++)
        {
            words[i] = BitConverter.ToUInt16(bytes, i * 2);
        }

        var meta = TryReadEexHeader(bytes, bytes.Length);
        var topWords = words
            .GroupBy(x => x)
            .OrderByDescending(g => g.Count())
            .ThenBy(g => g.Key)
            .Take(8)
            .Select(g => $"{HexDisplayFormatter.FormatWord(g.Key)}:{g.Count()}");
        var firstWords = words.Take(24).Select(HexDisplayFormatter.FormatWord);
        var textHits = BinaryTextScanner.ScanGbkNullTerminatedStringHits(bytes, minByteLength: 4, maxItems: 64, requireCjk: true);
        var commandHits = BuildCommandCandidateSummary(words, sceneDictionary, meta.HeaderSize);
        var lastNonZero = Array.FindLastIndex(bytes, x => x != 0);
        var usedBytes = Math.Max(0, lastNonZero + 1);
        var title = GuessTitle(textHits);
        var kind = Classify(info.Name, info.Length);

        return new ScenarioFileInfo
        {
            FileName = info.Name,
            Id = ExtractNumber(info.Name),
            Length = info.Length,
            Kind = kind,
            WordCount = words.Length,
            NonZeroWordCount = words.Count(x => x != 0),
            DistinctWordCount = words.Distinct().Count(),
            UsedBytes = usedBytes,
            UsedPercent = bytes.Length == 0 ? 0 : usedBytes * 100.0 / bytes.Length,
            LastNonZeroOffsetHex = lastNonZero >= 0 ? HexDisplayFormatter.FormatOffset(lastNonZero) : string.Empty,
            FirstWordsHex = string.Join(" ", firstWords),
            TopWordsHex = string.Join(" / ", topWords),
            RecognizedCommandCount = commandHits.Count,
            FirstCommandNames = string.Join(" / ", commandHits.Select(x => $"{x.IdHex}:{x.Name}")),
            TextHintCount = textHits.Count,
            FirstTextOffsetHex = textHits.Count > 0 ? textHits[0].OffsetHex : string.Empty,
            TitleHint = title,
            TextHints = string.Join(" / ", textHits.Take(24).Select(t => $"{t.OffsetHex}:{TrimForGrid(t.Text, 40)}")),
            Annotation = BuildAnnotation(info.Name, kind, title, textHits.Count, commandHits.Count, meta),
            UsageAnnotation = BuildUsageAnnotation(kind, usedBytes, info.Length, textHits.Count, meta),
            Path = path
        };
    }

    private static IReadOnlyList<string> EnumerateScriptFiles(CczProject project)
    {
        var rsDir = project.ResolveGameFile("RS");
        if (!Directory.Exists(rsDir)) return Array.Empty<string>();

        var files = Directory.GetFiles(rsDir, "*.eex", SearchOption.TopDirectoryOnly)
            .Where(path => IsRsScriptFile(Path.GetFileName(path)))
            .OrderBy(path => ScriptSortGroup(Path.GetFileName(path)))
            .ThenBy(path => ExtractNumberAsInt(Path.GetFileName(path)))
            .ThenBy(path => Path.GetFileName(path), StringComparer.CurrentCultureIgnoreCase)
            .ToList();
        return files;
    }

    public static bool IsRsScriptFile(string fileName)
        => RScriptRegex().IsMatch(fileName) || SScriptRegex().IsMatch(fileName);

    public static bool IsBattlefieldScriptFile(string fileName)
        => SScriptRegex().IsMatch(fileName);

    private static int ScriptSortGroup(string fileName)
    {
        if (RScriptRegex().IsMatch(fileName)) return 0;
        if (SScriptRegex().IsMatch(fileName)) return 1;
        return 2;
    }

    private static string BuildAnnotation(string fileName, string kind, string titleHint, int textHintCount, int recognizedCommandCount, EexHeaderInfo meta)
    {
        var kindText = kind switch
        {
            "R剧本" => "R_XX.eex：R 剧本/资源相关 eex，按传统 R/S 剧本入口处理。",
            "S剧本" => "S_XX.eex：S 剧本/资源相关 eex，战场制作优先从这里选择。",
            _ => "eex 文件：尚未归入 R/S 编号规则。"
        };
        var titleText = string.IsNullOrWhiteSpace(titleHint) ? "未识别标题。" : $"标题/文本候选：{titleHint}。";
        var textText = textHintCount > 0 ? $"发现 {textHintCount} 条 GBK 文本线索，可做原容量内短写回。" : "未发现可用文本线索。";
        var commandText = recognizedCommandCount > 0 ? $"命中 {recognizedCommandCount} 个 CczString.ini 命令候选。" : "未命中命令字典候选。";
        var headerText = meta.MagicValid
            ? $"EEX 头：HeaderSize={HexDisplayFormatter.Format(meta.HeaderSize)}，区段偏移候选={FormatOffsets(meta.SectionOffsets)}。"
            : "EEX 头异常或未确认。";
        return $"{kindText}{titleText}{textText}{commandText}{headerText} 文件：{fileName}";
    }

    private static string BuildUsageAnnotation(string kind, long usedBytes, long length, int textHintCount, EexHeaderInfo meta)
    {
        var usage = length == 0 ? 0 : usedBytes * 100.0 / length;
        var writeText = textHintCount > 0
            ? "可尝试 GBK 文本原地短写回；不得扩容或重排未知命令结构。"
            : "当前只建议作为只读索引和结构校对对象。";
        var headerText = meta.MagicValid
            ? $"EEX 区段候选={meta.SectionOffsets.Count}。"
            : "EEX 头未确认。";
        return $"类型={kind}；{headerText}占用 {usedBytes:N0}/{length:N0} 字节 ({usage:F1}%)；{writeText}";
    }

    private static IReadOnlyList<SceneCommandDefinition> BuildCommandCandidateSummary(ushort[] words, SceneStringDocument? dictionary, int scanStartByte)
    {
        if (dictionary == null || dictionary.Commands.Count == 0) return Array.Empty<SceneCommandDefinition>();
        var map = dictionary.Commands.ToDictionary(x => x.Id);
        var result = new List<SceneCommandDefinition>();
        var seen = new HashSet<int>();
        var startWord = Math.Clamp(scanStartByte / 2, 0, words.Length);
        var maxScanWords = Math.Min(words.Length, startWord + 4096);
        for (var i = startWord; i < maxScanWords; i++)
        {
            var word = words[i];
            if (!map.TryGetValue(word, out var command)) continue;
            if (word == 0 && LooksLikeZeroPadding(words, i, maxScanWords)) continue;
            if (!seen.Add(command.Id)) continue;
            result.Add(command);
            if (result.Count >= 24) break;
        }
        return result;
    }

    private static bool LooksLikeZeroPadding(ushort[] words, int index, int scanWordLimit)
    {
        var start = Math.Max(0, index - 2);
        var end = Math.Min(scanWordLimit - 1, index + 2);
        var zeroCount = 0;
        for (var i = start; i <= end; i++)
        {
            if (words[i] == 0) zeroCount++;
        }
        return zeroCount >= 4;
    }

    private static string GuessTitle(IReadOnlyList<BinaryTextHit> textHits)
    {
        return textHits
            .OrderBy(x => x.Offset)
            .Select(x => CleanTitleCandidate(x.Text))
            .Where(x => x.Length is > 0 and <= 40)
            .FirstOrDefault(x => !IsGenericScenarioText(x)) ?? string.Empty;
    }

    private static string CleanTitleCandidate(string text)
    {
        text = text.Replace("\r", "\n", StringComparison.Ordinal);
        var firstLine = text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault() ?? string.Empty;
        var at = firstLine.LastIndexOf('@');
        if (at >= 0 && at < firstLine.Length - 1)
        {
            firstLine = firstLine[(at + 1)..];
        }
        firstLine = firstLine.Trim();
        while (firstLine.Length > 0 && !char.IsLetterOrDigit(firstLine[0]) && !IsCjk(firstLine[0]))
        {
            firstLine = firstLine[1..].TrimStart();
        }
        return firstLine;
    }

    private static bool IsGenericScenarioText(string text)
        => text.Contains("曹操传", StringComparison.Ordinal)
           || text.Contains("回合", StringComparison.Ordinal)
           || text.Contains("存档", StringComparison.Ordinal);

    private static string TrimForGrid(string text, int maxChars)
    {
        text = text.Replace("\r", "\\r", StringComparison.Ordinal).Replace("\n", "\\n", StringComparison.Ordinal);
        return text.Length > maxChars ? text[..maxChars] + "…" : text;
    }

    private static bool IsCjk(char ch) => ch >= 0x4E00 && ch <= 0x9FFF;

    private static string Classify(string fileName, long length)
    {
        if (RScriptRegex().IsMatch(fileName)) return "R剧本";
        if (SScriptRegex().IsMatch(fileName)) return "S剧本";
        if (fileName.EndsWith(".eex", StringComparison.OrdinalIgnoreCase)) return "EEX";
        if (fileName.EndsWith(".E5S", StringComparison.OrdinalIgnoreCase)) return "存档信息";
        return "未知";
    }

    private static string ExtractNumber(string fileName)
    {
        var match = NumberRegex().Match(fileName);
        return match.Success ? match.Value : string.Empty;
    }

    private static int ExtractNumberAsInt(string fileName)
        => int.TryParse(ExtractNumber(fileName), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : int.MaxValue;

    private static byte[] ReadHeaderBytes(string path)
    {
        const int maxHeaderProbeBytes = 256;
        using var stream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        var count = checked((int)Math.Min(maxHeaderProbeBytes, stream.Length));
        var buffer = new byte[count];
        var read = stream.Read(buffer, 0, buffer.Length);
        return read == buffer.Length ? buffer : buffer[..read];
    }

    private static EexHeaderInfo TryReadEexHeader(byte[] bytes, long totalLength)
    {
        var magic = bytes.Length >= 14 && bytes[0] == (byte)'E' && bytes[1] == (byte)'E' && bytes[2] == (byte)'X' && bytes[3] == 0;
        if (!magic) return new EexHeaderInfo(false, 0, Array.Empty<int>());

        var headerSize = checked((int)BitConverter.ToUInt32(bytes, 10));
        if (headerSize < 14 || headerSize > 256 || headerSize > totalLength)
        {
            headerSize = 14;
        }

        var offsets = new List<int>();
        for (var offset = 14; offset + 4 <= headerSize && offset + 4 <= bytes.Length; offset += 4)
        {
            var value = checked((int)BitConverter.ToUInt32(bytes, offset));
            if (value >= headerSize && value < totalLength) offsets.Add(value);
        }

        return new EexHeaderInfo(true, headerSize, offsets.Distinct().OrderBy(x => x).ToList());
    }

    private static string FormatOffsets(IReadOnlyList<int> offsets)
        => offsets.Count == 0 ? "???" : string.Join("/", offsets.Take(8).Select(x => HexDisplayFormatter.FormatOffset(x)));

    private sealed record EexHeaderInfo(bool MagicValid, int HeaderSize, IReadOnlyList<int> SectionOffsets);

    [GeneratedRegex(@"\d+")]
    private static partial Regex NumberRegex();

    [GeneratedRegex(@"^R_\d{2,3}\.eex$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex RScriptRegex();

    [GeneratedRegex(@"^S_\d{2,3}\.eex$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SScriptRegex();
}
