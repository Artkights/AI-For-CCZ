using System.Globalization;
using CCZModStudio.Core;
using CCZModStudio.Models;

namespace CCZModStudio.Formats;

public sealed class SceneStringParser
{
    public SceneStringDocument Parse(string path)
    {
        if (!File.Exists(path)) throw new FileNotFoundException("剧本字典文件不存在。", path);
        var read = LegacyTextDecoder.ReadTextFile(path);
        var lines = read.Lines
            .Select(x => x.Trim())
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .ToList();

        if (lines.Count == 0) throw new InvalidOperationException("剧本字典文件为空。 ");

        var commandWarnings = new List<string>();
        var commands = ParseCommands(lines[0], commandWarnings);
        var groups = new List<SceneStringGroup>();
        for (var i = 1; i < lines.Count; i++)
        {
            var items = lines[i].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            groups.Add(new SceneStringGroup
            {
                Index = i,
                ItemCount = items.Length,
                ItemsText = string.Join("，", items)
            });
        }

        return new SceneStringDocument
        {
            SourcePath = path,
            EncodingName = read.EncodingName,
            DecodeConfidence = read.Confidence,
            DecodeWarnings = read.Warnings.Concat(commandWarnings).ToList(),
            SourceLineCount = read.Lines.Length,
            Commands = commands,
            Groups = groups
        };
    }

    private static IReadOnlyList<SceneCommandDefinition> ParseCommands(string line, ICollection<string> warnings)
    {
        var result = new List<SceneCommandDefinition>();
        var seen = new Dictionary<int, string>();
        foreach (var part in line.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var colon = part.IndexOf(':');
            if (colon <= 0 || colon == part.Length - 1) continue;
            var idText = part[..colon].Trim();
            var name = part[(colon + 1)..].Trim();
            if (!int.TryParse(idText, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var id)) continue;
            if (seen.TryGetValue(id, out var firstText))
            {
                warnings.Add($"CczString.ini command id duplicated: {id:X2}; kept first \"{firstText}\", ignored \"{part}\".");
                continue;
            }

            seen[id] = part;
            result.Add(new SceneCommandDefinition { Id = id, Name = name });
        }
        return result.OrderBy(x => x.Id).ToList();
    }

}
