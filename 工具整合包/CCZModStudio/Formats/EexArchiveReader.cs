using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using CCZModStudio.Models;

namespace CCZModStudio.Formats;

public sealed partial class EexArchiveReader
{
    public IReadOnlyList<EexArchiveInfo> ReadAll(CczProject project)
    {
        var result = new List<EexArchiveInfo>();
        AddDirectory(project.ResolveGameFile("RS"), "R形象", "R_*.eex", result);
        AddDirectory(project.ResolveGameFile("RS"), "S形象", "S_*.eex", result);
        AddDirectory(project.ResolveGameFile("Map"), "地图EEX", "*.eex", result);
        return result;
    }

    public EexArchiveInfo Read(string path, string category)
    {
        var bytes = File.ReadAllBytes(path);
        var info = new FileInfo(path);
        var magicValid = bytes.Length >= 4 && bytes[0] == (byte)'E' && bytes[1] == (byte)'E' && bytes[2] == (byte)'X' && bytes[3] == 0;
        var texts = BinaryTextScanner.ScanGbkNullTerminatedStrings(bytes, minByteLength: 5, maxItems: 8);

        return new EexArchiveInfo
        {
            Category = category,
            FileName = info.Name,
            Id = ExtractNumber(info.Name),
            Length = info.Length,
            MagicValid = magicValid,
            VersionHex = bytes.Length >= 6 ? "0x" + BitConverter.ToUInt16(bytes, 4).ToString("X4", CultureInfo.InvariantCulture) : string.Empty,
            EntryCount = bytes.Length >= 14 ? checked((int)BitConverter.ToUInt32(bytes, 10)) : 0,
            Header14Hex = ReadUInt32Hex(bytes, 14),
            Header18Hex = ReadUInt32Hex(bytes, 18),
            Header22Hex = ReadUInt32Hex(bytes, 22),
            Header26Hex = ReadUInt32Hex(bytes, 26),
            TextHintCount = texts.Count,
            TextHints = string.Join(" / ", texts.Select(t => t.Length > 40 ? t[..40] + "…" : t)),
            Annotation = BuildAnnotation(category, magicValid, bytes.Length >= 14 ? checked((int)BitConverter.ToUInt32(bytes, 10)) : 0, texts.Count, info.Length),
            HeaderAnnotation = BuildHeaderAnnotation(magicValid, bytes.Length),
            Path = path
        };
    }



    private static string BuildAnnotation(string category, bool magicValid, int entryCount, int textHintCount, long length)
    {
        if (!magicValid)
        {
            return "\u9b54\u6570\u4e0d\u662f EEX\\0\uff0c\u53ef\u80fd\u4e0d\u662f\u6807\u51c6 EEX \u8d44\u6e90\u5305\uff1b\u53ea\u5efa\u8bae\u67e5\u770b\u548c\u5907\u4efd\uff0c\u4e0d\u5efa\u8bae\u5199\u5165\u3002";
        }

        var role = category switch
        {
            "R\u5f62\u8c61" => "R \u5f62\u8c61\u8d44\u6e90\u5305\uff1a\u901a\u5e38\u5bf9\u5e94\u4eba\u7269 R\u5f62\u8c61\u7f16\u53f7\uff0c\u7528\u4e8e\u6218\u573a\u89d2\u8272\u5f62\u8c61/\u52a8\u4f5c\u76f8\u5173\u8d44\u6e90\u3002",
            "S\u5f62\u8c61" => "S \u5f62\u8c61\u8d44\u6e90\u5305\uff1a\u901a\u5e38\u5bf9\u5e94\u4eba\u7269 S\u5f62\u8c61\u7f16\u53f7\uff0c\u7528\u4e8e\u6218\u573a\u5c0f\u4eba/\u52a8\u4f5c\u76f8\u5173\u8d44\u6e90\u3002",
            "\u5730\u56feEEX" => "\u5730\u56fe EEX \u8d44\u6e90\u5305\uff1a\u901a\u5e38\u4e0e\u5730\u56fe\u663e\u793a\u3001\u5730\u56fe\u9644\u52a0\u8d44\u6e90\u6216\u573a\u666f\u7d20\u6750\u76f8\u5173\u3002",
            _ => "EEX \u8d44\u6e90\u5305\uff1a\u66f9\u64cd\u4f20 MOD \u5e38\u89c1\u5c01\u88c5\u8d44\u6e90\uff0c\u5f53\u524d\u4ec5\u505a\u53ea\u8bfb\u8bc6\u522b\u3002"
        };

        var entryText = entryCount > 0
            ? $"\u7591\u4f3c\u6761\u76ee\u6570 {entryCount}\uff1b\u6761\u76ee\u8fb9\u754c\u5c1a\u672a\u5b8c\u5168\u9a8c\u8bc1\u3002"
            : "\u672a\u8bfb\u5230\u660e\u786e\u6761\u76ee\u6570\uff0c\u53ef\u80fd\u662f\u5c0f\u578b\u6216\u975e\u5178\u578b EEX\u3002";
        var textText = textHintCount > 0
            ? $"\u53d1\u73b0 {textHintCount} \u6761 GBK \u6587\u672c\u7ebf\u7d22\uff0c\u53ef\u8f85\u52a9\u5224\u65ad\u8d44\u6e90\u6765\u6e90/\u8bf4\u660e\u3002"
            : "\u672a\u53d1\u73b0\u660e\u663e GBK \u6587\u672c\u7ebf\u7d22\u3002";
        var sizeText = length < 1024 ? "\u6587\u4ef6\u8f83\u5c0f\uff0c\u9700\u786e\u8ba4\u662f\u5426\u4e3a\u7a7a\u58f3/\u5360\u4f4d\u8d44\u6e90\u3002" : "\u6587\u4ef6\u5927\u5c0f\u6b63\u5e38\uff0c\u4ecd\u9700\u7ed3\u5408\u6e38\u620f\u5185\u5f15\u7528\u9a8c\u8bc1\u3002";
        return $"{role}{entryText}{textText}{sizeText}";
    }

    private static string BuildHeaderAnnotation(bool magicValid, int length)
    {
        if (!magicValid) return "\u5934\u90e8\u89e3\u91ca\u4e0d\u53ef\u7528\uff1a\u6587\u4ef6\u5f00\u5934\u4e0d\u662f EEX\\0\u3002";
        if (length < 30) return "\u5934\u90e8\u8fc7\u77ed\uff1a\u4e0d\u8db3\u4ee5\u8bfb\u53d6\u5e38\u89c1 EEX \u5934\u90e8\u5b57\u6bb5\u3002";
        return "\u5934\u90e8\u8bf4\u660e\uff1a0x00-0x03 \u4e3a EEX\\0 \u9b54\u6570\uff1b0x04-0x05 \u7591\u4f3c\u7248\u672c\uff1b0x0A-0x0D \u5f53\u524d\u6309\u7591\u4f3c\u6761\u76ee\u6570\u8bfb\u53d6\uff1b0x0E/0x12/0x16/0x1A \u4e3a\u5f85\u9a8c\u8bc1\u5934\u90e8\u5b57\u6bb5\uff0c\u4ec5\u4f5c\u683c\u5f0f\u7814\u7a76\u8bc1\u636e\u3002";
    }

    private static void AddDirectory(string dir, string category, string pattern, List<EexArchiveInfo> result)
    {
        if (!Directory.Exists(dir)) return;
        foreach (var path in Directory.GetFiles(dir, pattern).OrderBy(x => x, StringComparer.CurrentCultureIgnoreCase))
        {
            result.Add(new EexArchiveReader().Read(path, category));
        }
    }

    private static string ReadUInt32Hex(byte[] bytes, int offset)
    {
        return bytes.Length >= offset + 4
            ? "0x" + BitConverter.ToUInt32(bytes, offset).ToString("X8", CultureInfo.InvariantCulture)
            : string.Empty;
    }

    private static string ExtractNumber(string fileName)
    {
        var match = NumberRegex().Match(fileName);
        return match.Success ? match.Value : string.Empty;
    }

    [GeneratedRegex(@"\d+")]
    private static partial Regex NumberRegex();
}

