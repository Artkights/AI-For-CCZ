using System.Globalization;
using System.Text;
using CCZModStudio.Core;
using CCZModStudio.Models;

namespace CCZModStudio.Formats;

public sealed class SceneStringParser
{
    public SceneStringDocument Parse(string path)
    {
        if (!File.Exists(path)) throw new FileNotFoundException("剧本字典文件不存在。", path);
        var lines = ReadAllLinesSmart(path)
            .Select(x => x.Trim())
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .ToList();

        if (lines.Count == 0) throw new InvalidOperationException("剧本字典文件为空。 ");

        var commands = ParseCommands(lines[0]);
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
            Commands = commands,
            Groups = groups
        };
    }

    private static IReadOnlyList<SceneCommandDefinition> ParseCommands(string line)
    {
        var result = new List<SceneCommandDefinition>();
        foreach (var part in line.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var colon = part.IndexOf(':');
            if (colon <= 0 || colon == part.Length - 1) continue;
            var idText = part[..colon].Trim();
            var name = part[(colon + 1)..].Trim();
            if (!int.TryParse(idText, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var id)) continue;
            result.Add(new SceneCommandDefinition { Id = id, Name = name });
        }
        return result.OrderBy(x => x.Id).ToList();
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
}
