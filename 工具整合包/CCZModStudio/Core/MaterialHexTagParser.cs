using System.Globalization;

namespace CCZModStudio.Core;

public static class MaterialHexTagParser
{
    public static bool TryParseTerrainId(string? text, out byte id)
    {
        id = 0;
        text = text?.Trim();
        if (string.IsNullOrWhiteSpace(text)) return false;

        if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            return byte.TryParse(text[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out id);
        }

        if (byte.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out id))
        {
            return true;
        }

        return byte.TryParse(text, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out id);
    }
}
