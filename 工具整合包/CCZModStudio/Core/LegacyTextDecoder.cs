using System.Text;

namespace CCZModStudio.Core;

public sealed record LegacyTextDecodeResult(
    string Text,
    string EncodingName,
    string Confidence,
    IReadOnlyList<string> Warnings,
    byte[] RawBytes,
    string SourcePath = "",
    int? Offset = null)
{
    public string WarningText => Warnings.Count == 0 ? string.Empty : string.Join("；", Warnings);

    public string DiagnosticText
    {
        get
        {
            var location = Offset.HasValue ? $" @{HexDisplayFormatter.FormatOffset(Offset.Value)}" : string.Empty;
            var warning = Warnings.Count == 0 ? string.Empty : "，" + WarningText;
            return $"{EncodingName}，置信度 {Confidence}{location}{warning}";
        }
    }
}

public sealed record LegacyTextFileReadResult(
    string Text,
    string[] Lines,
    string EncodingName,
    string Confidence,
    IReadOnlyList<string> Warnings,
    string SourcePath)
{
    public string DiagnosticText
    {
        get
        {
            var warning = Warnings.Count == 0 ? string.Empty : "，" + string.Join("；", Warnings);
            return $"{EncodingName}，置信度 {Confidence}，行数 {Lines.Length}{warning}";
        }
    }
}

public static class LegacyTextDecoder
{
    private static readonly Lazy<Encoding> StrictGbk = new(() =>
    {
        EncodingService.EnsureCodePages();
        return Encoding.GetEncoding(
            936,
            EncoderFallback.ExceptionFallback,
            DecoderFallback.ExceptionFallback);
    });

    private static readonly UTF8Encoding StrictUtf8 = new(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: true);

    private static readonly UnicodeEncoding StrictUtf16Le = new(
        bigEndian: false,
        byteOrderMark: true,
        throwOnInvalidBytes: true);

    private static readonly UnicodeEncoding StrictUtf16Be = new(
        bigEndian: true,
        byteOrderMark: true,
        throwOnInvalidBytes: true);

    private static readonly string[] MojibakeMarkers =
    [
        "鍦", "璇", "鐨", "鑰", "鏂", "瀛", "淇", "妫", "缂", "绋", "鍓", "鏁",
        "瑙", "瀹", "鎺", "锟", "\uFFFD", "Ã", "Â"
    ];

    public static LegacyTextFileReadResult ReadTextFile(string path)
    {
        var bytes = File.ReadAllBytes(path);
        var decoded = DecodeTextFileBytes(bytes, path);
        var normalized = NormalizeNewLines(decoded.Text);
        var lines = normalized.Split('\n');
        return new LegacyTextFileReadResult(
            normalized,
            lines,
            decoded.EncodingName,
            decoded.Confidence,
            decoded.Warnings,
            path);
    }

    public static string[] ReadAllLines(string path)
        => ReadTextFile(path).Lines;

    public static LegacyTextDecodeResult DecodeGbk(
        byte[] bytes,
        int offset,
        int byteLength,
        string sourcePath = "",
        bool trimAtNull = false)
    {
        if (offset < 0 || byteLength < 0 || offset + byteLength > bytes.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(byteLength), "旧版文本解码范围越界。");
        }

        var span = bytes.AsSpan(offset, byteLength);
        var sourceRaw = span.ToArray();
        if (trimAtNull)
        {
            var nullIndex = span.IndexOf((byte)0);
            if (nullIndex >= 0)
            {
                span = span[..nullIndex];
            }
        }

        var raw = span.ToArray();
        try
        {
            var text = StrictGbk.Value.GetString(raw);
            return BuildResult(text, "GBK", trimAtNull ? sourceRaw : raw, sourcePath, offset);
        }
        catch (DecoderFallbackException ex)
        {
            var text = EncodingService.Gbk.GetString(raw);
            var warnings = AnalyzeText(text).ToList();
            warnings.Insert(0, "严格 GBK 解码失败，已使用宽松 GBK 兜底：" + ex.Message);
            return new LegacyTextDecodeResult(text, "GBK(fallback)", "低", warnings, trimAtNull ? sourceRaw : raw, sourcePath, offset);
        }
    }

    public static LegacyTextDecodeResult DecodeTextFileBytes(byte[] bytes, string sourcePath = "")
    {
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
        {
            return DecodeWith(StrictUtf8, "UTF-8 BOM", bytes.AsSpan(3).ToArray(), sourcePath);
        }

        if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
        {
            return DecodeWith(StrictUtf16Le, "UTF-16 LE BOM", bytes.AsSpan(2).ToArray(), sourcePath);
        }

        if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
        {
            return DecodeWith(StrictUtf16Be, "UTF-16 BE BOM", bytes.AsSpan(2).ToArray(), sourcePath);
        }

        try
        {
            return DecodeWith(StrictUtf8, "UTF-8", bytes, sourcePath);
        }
        catch (DecoderFallbackException)
        {
            return DecodeWith(StrictGbk.Value, "GBK", bytes, sourcePath);
        }
    }

    public static bool ContainsMojibakeMarker(string? text)
    {
        if (string.IsNullOrEmpty(text)) return false;
        return MojibakeMarkers.Any(marker => text.Contains(marker, StringComparison.Ordinal));
    }

    public static IReadOnlyList<string> AnalyzeText(string? text)
    {
        if (string.IsNullOrEmpty(text)) return Array.Empty<string>();

        var warnings = new List<string>();
        var markers = MojibakeMarkers
            .Where(marker => text.Contains(marker, StringComparison.Ordinal))
            .Take(8)
            .ToList();
        if (markers.Count > 0)
        {
            warnings.Add("疑似二次乱码标记：" + string.Join("/", markers));
        }

        var badControls = text.Count(ch => char.IsControl(ch) && ch is not '\r' and not '\n' and not '\t' and not '\0');
        if (badControls > 0)
        {
            warnings.Add($"含异常控制字符 {badControls} 个");
        }

        return warnings;
    }

    public static string NormalizeNewLines(string text)
        => text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');

    private static LegacyTextDecodeResult DecodeWith(Encoding encoding, string encodingName, byte[] bytes, string sourcePath)
    {
        var text = encoding.GetString(bytes);
        return BuildResult(text, encodingName, bytes, sourcePath, offset: null);
    }

    private static LegacyTextDecodeResult BuildResult(string text, string encodingName, byte[] rawBytes, string sourcePath, int? offset)
    {
        var warnings = AnalyzeText(text);
        var confidence = warnings.Count == 0 ? "高" : "低";
        return new LegacyTextDecodeResult(text, encodingName, confidence, warnings, rawBytes, sourcePath, offset);
    }
}
