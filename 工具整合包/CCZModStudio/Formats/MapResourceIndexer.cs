using System.Drawing;
using System.Globalization;
using CCZModStudio.Core;
using CCZModStudio.Models;

namespace CCZModStudio.Formats;

public sealed class MapResourceIndexer
{
    private readonly LegacyHmMapReader _legacyHmReader = new();

    public IReadOnlyList<MapResourceItem> Index(CczProject project)
    {
        var dir = project.ResolveGameFile("Map");
        if (!Directory.Exists(dir))
        {
            return LegacyHmMapReader.HasLegacyHmLayout(project)
                ? _legacyHmReader.Index(project)
                : Array.Empty<MapResourceItem>();
        }

        var maps = Directory.GetFiles(dir)
            .Where(path => Path.GetExtension(path).Equals(".jpg", StringComparison.OrdinalIgnoreCase) ||
                           Path.GetExtension(path).Equals(".jpeg", StringComparison.OrdinalIgnoreCase))
            .OrderBy(path => path, StringComparer.CurrentCultureIgnoreCase)
            .Select(ReadMap)
            .ToList();
        if (maps.Count > 0) return maps;

        return LegacyHmMapReader.HasLegacyHmLayout(project)
            ? _legacyHmReader.Index(project)
            : maps;
    }

    private static MapResourceItem ReadMap(string path)
    {
        var info = new FileInfo(path);
        var width = 0;
        var height = 0;
        try
        {
            using var image = Image.FromFile(path);
            width = image.Width;
            height = image.Height;
        }
        catch
        {
        }

        return new MapResourceItem
        {
            Id = ExtractNumber(info.Name),
            MapId = string.IsNullOrEmpty(ExtractNumber(info.Name)) ? string.Empty : "M" + ExtractNumber(info.Name),
            Name = info.Name,
            Extension = info.Extension,
            SizeBytes = info.Length,
            SourceKind = "JpegMap",
            Width = width,
            Height = height,
            Path = path
        };
    }

    private static string ExtractNumber(string fileName)
    {
        var digits = new string(Path.GetFileNameWithoutExtension(fileName).Where(char.IsDigit).ToArray());
        return string.IsNullOrEmpty(digits) ? string.Empty : int.Parse(digits, CultureInfo.InvariantCulture).ToString("000", CultureInfo.InvariantCulture);
    }
}
