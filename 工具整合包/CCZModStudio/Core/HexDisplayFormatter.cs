using System.Globalization;
using System.Text.RegularExpressions;

namespace CCZModStudio.Core;

public static class HexDisplayFormatter
{
    private static readonly Regex PrefixedHexRegex = new(
        @"(?<![A-Za-z0-9_])0x(?<hex>[0-9A-Fa-f]+)\b",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    public static string Format(byte value, int minDigits = 2)
        => FormatUnsigned(value, minDigits);

    public static string Format(ushort value, int minDigits = 4)
        => FormatUnsigned(value, minDigits);

    public static string Format(uint value, int minDigits = 0)
        => FormatUnsigned(value, minDigits);

    public static string Format(int value, int minDigits = 0)
        => value < 0
            ? "-" + FormatUnsigned((ulong)-(long)value, minDigits)
            : FormatUnsigned((ulong)value, minDigits);

    public static string Format(long value, int minDigits = 0)
        => value < 0
            ? "-" + FormatUnsigned((ulong)(-(value + 1)) + 1, minDigits)
            : FormatUnsigned((ulong)value, minDigits);

    public static string Format(ulong value, int minDigits = 0)
        => FormatUnsigned(value, minDigits);

    public static string FormatOffset(long value, int minDigits = 6)
        => Format(value, minDigits);

    public static string FormatOffset(ulong value, int minDigits = 6)
        => Format(value, minDigits);

    public static string FormatByte(byte value)
        => Format(value, 2);

    public static string FormatWord(ushort value)
        => Format(value, 4);

    public static string FormatWord(int value)
        => Format(value, 4);

    public static string FormatDword(uint value)
        => Format(value, 8);

    public static string FormatDword(int value)
        => Format(value, 8);

    public static string FormatRange(long start, long end, int minDigits = 6)
        => $"{FormatOffset(start, minDigits)}-{FormatOffset(end, minDigits)}";

    public static string FormatByteList(IEnumerable<byte> values, string separator = " ")
        => string.Join(separator, values.Select(FormatByte));

    public static string NormalizeText(string? text)
        => string.IsNullOrEmpty(text)
            ? string.Empty
            : PrefixedHexRegex.Replace(text, match => match.Groups["hex"].Value.ToUpperInvariant());

    public static bool EqualsText(string? left, string? right)
        => string.Equals(NormalizeText(left), NormalizeText(right), StringComparison.OrdinalIgnoreCase);

    private static string FormatUnsigned(ulong value, int minDigits)
    {
        var format = BuildFormat(minDigits);
        return value.ToString(format, CultureInfo.InvariantCulture);
    }

    private static string BuildFormat(int minDigits)
        => minDigits > 0 ? "X" + minDigits.ToString(CultureInfo.InvariantCulture) : "X";
}
