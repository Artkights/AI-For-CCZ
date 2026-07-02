using System.Globalization;
using CCZModStudio.Core;
using CCZModStudio.Models;

namespace CCZModStudio.Formats;

public sealed class ScenarioCommandProbeReader
{
    public IReadOnlyList<ScenarioCommandProbeRow> Probe(string path, SceneStringDocument sceneDictionary, int maxRows = 600)
    {
        if (!File.Exists(path)) throw new FileNotFoundException("R/S eex 文件不存在。", path);
        if (sceneDictionary.Commands.Count == 0) return Array.Empty<ScenarioCommandProbeRow>();

        var bytes = File.ReadAllBytes(path);
        var words = new ushort[bytes.Length / 2];
        for (var i = 0; i < words.Length; i++)
        {
            words[i] = BitConverter.ToUInt16(bytes, i * 2);
        }

        var dictionary = DictionaryBuild.ToDictionaryFirstByKey(sceneDictionary.Commands, x => x.Id, x => x);
        var eexHeaderSize = TryGetEexHeaderSize(bytes);
        var startWordIndex = eexHeaderSize > 0 ? Math.Clamp(eexHeaderSize / 2, 0, words.Length) : 0;
        var scanWordLimit = DetermineScanWordLimit(bytes, words, startWordIndex, eexHeaderSize > 0);

        var rows = new List<ScenarioCommandProbeRow>();
        for (var wordIndex = startWordIndex; wordIndex < scanWordLimit; wordIndex++)
        {
            var id = words[wordIndex];
            if (!dictionary.TryGetValue(id, out var command)) continue;
            if (id == 0 && LooksLikeZeroPadding(words, wordIndex, scanWordLimit)) continue;

            var context = words
                .Skip(wordIndex)
                .Take(10)
                .Select(x => HexDisplayFormatter.FormatWord(x));
            var note = BuildNote(id, words, wordIndex, scanWordLimit, eexHeaderSize > 0);
            var confidence = note.Contains("低可信度", StringComparison.Ordinal) ? "低" : id == 0 ? "低" : "中";

            rows.Add(new ScenarioCommandProbeRow
            {
                Index = rows.Count + 1,
                WordIndex = wordIndex,
                OffsetHex = HexDisplayFormatter.FormatOffset(wordIndex * 2),
                CommandId = id,
                CommandIdHex = HexDisplayFormatter.Format(id),
                CommandName = command.Name,
                ContextWordsHex = string.Join(" ", context),
                Confidence = confidence,
                Note = note,
                Annotation = BuildAnnotation(id, command.Name, confidence, note, eexHeaderSize > 0)
            });

            if (rows.Count >= maxRows) break;
        }

        return rows;
    }

    private static int DetermineScanWordLimit(byte[] bytes, ushort[] words, int startWordIndex, bool isEex)
    {
        if (isEex)
        {
            return words.Length;
        }

        var firstTextOffset = BinaryTextScanner
            .ScanGbkNullTerminatedStringHits(bytes, minByteLength: 5, maxItems: 1, requireCjk: true)
            .FirstOrDefault()?.Offset;
        var scanWordLimit = Math.Min(words.Length, (firstTextOffset ?? Math.Min(bytes.Length, 0x30000)) / 2);
        if (scanWordLimit <= startWordIndex) scanWordLimit = Math.Min(words.Length, startWordIndex + 0x30000 / 2);
        return scanWordLimit;
    }

    private static int TryGetEexHeaderSize(byte[] bytes)
    {
        var magic = bytes.Length >= 14 && bytes[0] == (byte)'E' && bytes[1] == (byte)'E' && bytes[2] == (byte)'X' && bytes[3] == 0;
        if (!magic) return 0;
        var headerSize = checked((int)BitConverter.ToUInt32(bytes, 10));
        return headerSize is >= 14 and <= 256 && headerSize <= bytes.Length ? headerSize : 14;
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

    private static string BuildNote(ushort id, ushort[] words, int index, int scanWordLimit, bool isEex)
    {
        var source = isEex ? "R/S eex 数据区" : "E5S 旧兼容探针数据区";
        if (id == 0) return $"命令 ID 为 0，常见于填充、分隔或事件结束标记；来源={source}。";
        if (index == 0) return $"位于扫描起点，缺少前置上下文；来源={source}。";
        if (index + 1 >= scanWordLimit) return $"接近扫描窗口末尾，缺少后续参数上下文；来源={source}。";

        var previous = words[index - 1];
        var next = words[index + 1];
        if (previous == 0 && next == 0) return $"前后都被 0 包围，低可信度；来源={source}。";

        return isEex
            ? "在 eex 数据区命中 16 位命令字典；需继续对照 a新剧本编辑器/CczString.ini 和实机结果，不代表已确认完整命令长度。"
            : "在 E5S 旧探针 16 位窗口中命中命令字典；仅作历史兼容核对，不作为 R/S eex 主线依据。";
    }

    private static string BuildAnnotation(ushort id, string commandName, string confidence, string note, bool isEex)
    {
        if (id == 0)
        {
            return "填充/分隔候选：命令 ID 为 0，通常不能直接视为有效剧情命令。";
        }

        var prefix = confidence == "低"
            ? "低可信度命令候选：需要结合上下文、旧工具和实机继续核对"
            : isEex
                ? "eex 命令候选：来自 CczString.ini 字典命中，请按 R/S eex 结构继续核对"
                : "E5S 旧探针命令候选：仅用于历史兼容核对";
        return $"{prefix}，ID={id}，名称={commandName}。{note}";
    }
}
