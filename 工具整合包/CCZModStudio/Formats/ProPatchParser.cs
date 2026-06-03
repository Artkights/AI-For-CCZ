using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using CCZModStudio.Core;
using CCZModStudio.Models;

namespace CCZModStudio.Formats;

public sealed partial class ProPatchParser
{
    public PatchDocument Parse(string path)
    {
        if (!File.Exists(path)) throw new FileNotFoundException("补丁文件不存在。", path);

        var lines = ReadAllLinesSmart(path);
        var comments = new List<string>();
        var entries = new List<PatchEntry>();
        PatchEntryBuilder? current = null;
        string? pendingComment = null;
        string? version = null;
        PatchAddressKind? addressKind = null;
        var headerState = 0;

        foreach (var (rawLine, index) in lines.Select((x, i) => (x, i + 1)))
        {
            var text = rawLine.Trim();
            if (string.IsNullOrWhiteSpace(text)) continue;

            if (text.StartsWith('#'))
            {
                comments.Add(text);
                if (headerState >= 2)
                {
                    pendingComment = AppendComment(pendingComment, text.TrimStart('#').Trim());
                }
                continue;
            }

            if (headerState == 0)
            {
                version = text;
                headerState = 1;
                continue;
            }

            if (headerState == 1)
            {
                addressKind = ParseAddressKind(text);
                headerState = 2;
                continue;
            }

            if (IsAddressLine(text))
            {
                if (current != null) entries.Add(current.Build(entries.Count + 1));
                current = new PatchEntryBuilder(ParseAddress(text), index, pendingComment);
                pendingComment = null;
                continue;
            }

            if (current == null)
            {
                pendingComment = AppendComment(pendingComment, text);
                continue;
            }

            current.AddBytes(ParseHexBytes(text));
        }

        if (current != null) entries.Add(current.Build(entries.Count + 1));

        if (version == null || addressKind == null)
        {
            throw new InvalidOperationException("补丁文件至少需要版本号和地址类型。 ");
        }

        return new PatchDocument
        {
            SourcePath = path,
            Version = version,
            AddressKind = addressKind.Value,
            Entries = entries,
            Comments = comments
        };
    }

    private static PatchAddressKind ParseAddressKind(string text) => text.Trim().ToLowerInvariant() switch
    {
        "o" => PatchAddressKind.OdVirtualAddress,
        "od" => PatchAddressKind.OdVirtualAddress,
        "u" => PatchAddressKind.FileOffset,
        "ue" => PatchAddressKind.FileOffset,
        _ => PatchAddressKind.Unknown
    };

    private static string? AppendComment(string? existing, string next)
    {
        if (string.IsNullOrWhiteSpace(next)) return existing;
        return string.IsNullOrWhiteSpace(existing) ? next : existing + " / " + next;
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

    private static bool IsAddressLine(string text)
    {
        text = text.Trim();
        if (text.Length < 2 || (text[0] != '-' && text[0] != '+')) return false;
        var body = text[1..].Trim();
        return body.Length > 0 && body.All(Uri.IsHexDigit);
    }

    private static uint ParseAddress(string text)
    {
        text = text.Trim().TrimStart('-', '+').Trim();
        return uint.Parse(text, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
    }

    private static byte[] ParseHexBytes(string text)
    {
        var hex = HexCharRegex().Replace(text, string.Empty);
        if (hex.Length == 0) return Array.Empty<byte>();
        if (hex.Length % 2 != 0) throw new InvalidOperationException("十六进制字节数量不是偶数：" + text);

        var bytes = new byte[hex.Length / 2];
        for (var i = 0; i < bytes.Length; i++)
        {
            bytes[i] = byte.Parse(hex.Substring(i * 2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        }
        return bytes;
    }

    [GeneratedRegex("[^0-9A-Fa-f]")]
    private static partial Regex HexCharRegex();

    private sealed class PatchEntryBuilder
    {
        private readonly List<byte> _bytes = new();
        private readonly uint _address;
        private readonly int _line;
        private readonly string? _comment;

        public PatchEntryBuilder(uint address, int line, string? comment)
        {
            _address = address;
            _line = line;
            _comment = comment;
        }

        public void AddBytes(byte[] bytes) => _bytes.AddRange(bytes);

        public PatchEntry Build(int index) => new()
        {
            Index = index,
            Address = _address,
            Bytes = _bytes.ToArray(),
            SourceLine = _line,
            Comment = _comment
        };
    }
}
