using System.Text;
using CCZModStudio.Core;

namespace CCZModStudio.Formats;

public static class BinaryTextScanner
{
    private static readonly Lazy<Encoding> StrictGbk = new(() =>
    {
        EncodingService.EnsureCodePages();
        return Encoding.GetEncoding(
            936,
            EncoderFallback.ExceptionFallback,
            DecoderFallback.ExceptionFallback);
    });

    public static IReadOnlyList<string> ScanGbkNullTerminatedStrings(byte[] bytes, int minByteLength = 4, int maxItems = 20, bool requireCjk = false)
        => ScanGbkNullTerminatedStringHits(bytes, minByteLength, maxItems, requireCjk).Select(x => x.Text).ToList();

    public static IReadOnlyList<BinaryTextHit> ScanGbkNullTerminatedStringHits(byte[] bytes, int minByteLength = 4, int maxItems = 20, bool requireCjk = false)
    {
        EncodingService.EnsureCodePages();
        var result = new List<BinaryTextHit>();
        var start = 0;
        for (var i = 0; i <= bytes.Length; i++)
        {
            if (i < bytes.Length && bytes[i] != 0x00) continue;
            var length = i - start;
            if (length >= minByteLength)
            {
                var text = Decode(bytes.AsSpan(start, length));
                if (LooksUseful(text) && (!requireCjk || text.Any(IsCjk)))
                {
                    result.Add(new BinaryTextHit(start, length, text));
                    if (result.Count >= maxItems) break;
                }
            }
            start = i + 1;
        }
        return result;
    }

    private static string Decode(ReadOnlySpan<byte> span)
    {
        try
        {
            return TrimBoundaryControls(StrictGbk.Value.GetString(span).Trim());
        }
        catch (DecoderFallbackException)
        {
            return string.Empty;
        }
    }

    private static string TrimBoundaryControls(string text)
    {
        var start = 0;
        var end = text.Length;
        while (start < end && char.IsControl(text[start]) && text[start] is not '\r' and not '\n' and not '\t') start++;
        while (end > start && char.IsControl(text[end - 1]) && text[end - 1] is not '\r' and not '\n' and not '\t') end--;
        return start == 0 && end == text.Length ? text : text[start..end].Trim();
    }

    private static bool LooksUseful(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;
        if (text.Contains('\uFFFD')) return false;
        var useful = 0;
        var total = 0;
        foreach (var ch in text)
        {
            if (char.IsControl(ch) && ch is not '\r' and not '\n' and not '\t') return false;
            total++;
            if (char.IsLetterOrDigit(ch) || IsCjk(ch) || char.IsPunctuation(ch) || char.IsWhiteSpace(ch)) useful++;
        }
        return total > 0 && useful * 100 / total >= 80;
    }

    private static bool IsCjk(char ch) => ch >= 0x4E00 && ch <= 0x9FFF;
}

public sealed record BinaryTextHit(int Offset, int ByteLength, string Text)
{
    public string OffsetHex => HexDisplayFormatter.FormatOffset(Offset);
}
