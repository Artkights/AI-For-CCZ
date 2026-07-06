using System.Text;

namespace CCZModStudio.Core;

public static class EncodingService
{
    private static bool _registered;

    public static Encoding Gbk
    {
        get
        {
            EnsureCodePages();
            return Encoding.GetEncoding(936);
        }
    }

    public static void EnsureCodePages()
    {
        if (_registered) return;
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        _registered = true;
    }

    public static string DecodeFixedString(ReadOnlySpan<byte> bytes)
    {
        EnsureCodePages();
        var nul = bytes.IndexOf((byte)0x00);
        var end = nul >= 0 ? nul : bytes.Length;
        while (end > 0 && (bytes[end - 1] == 0x00 || bytes[end - 1] == 0x20 || bytes[end - 1] == 0xFF))
        {
            end--;
        }

        if (end <= 0) return string.Empty;
        return Gbk.GetString(bytes[..end]).TrimEnd('\0', ' ', '\u3000');
    }

    public static byte[] EncodeFixedString(string value, int byteLength)
    {
        EnsureCodePages();
        var output = new byte[byteLength];
        var bytes = Gbk.GetBytes(value ?? string.Empty);
        if (bytes.Length > byteLength)
        {
            throw new InvalidOperationException($"字符串 GBK 字节长度 {bytes.Length} 超过字段容量 {byteLength}。值：{value}");
        }

        Array.Copy(bytes, output, bytes.Length);
        return output;
    }

    public static int GetGbkByteCount(string value)
    {
        EnsureCodePages();
        return Gbk.GetByteCount(value ?? string.Empty);
    }
}
