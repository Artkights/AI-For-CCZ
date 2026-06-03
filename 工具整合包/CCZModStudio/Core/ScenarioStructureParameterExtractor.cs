using System.Globalization;
using System.Text.RegularExpressions;
using CCZModStudio.Models;

namespace CCZModStudio.Core;

internal static class ScenarioStructureParameterExtractor
{
    public static IReadOnlyList<int> ExtractLogicalWords(ScenarioStructureRow row)
    {
        var logicalWords = ParseWords(row.ParameterPreview);
        if (logicalWords.Count > 0)
        {
            return logicalWords;
        }

        return ParseWords(row.RawContextWordsHex);
    }

    public static IReadOnlyList<int> ExtractRawContextWords(ScenarioStructureRow row)
        => ParseWords(row.RawContextWordsHex);

    private static IReadOnlyList<int> ParseWords(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return Array.Empty<int>();
        }

        return Regex.Matches(text, @"(?<![0-9A-Fa-f])[0-9A-Fa-f]{4}(?![0-9A-Fa-f])")
            .Select(match => int.Parse(match.Value, NumberStyles.HexNumber, CultureInfo.InvariantCulture))
            .ToList();
    }
}
