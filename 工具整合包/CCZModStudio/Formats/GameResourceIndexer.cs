using System.Text.RegularExpressions;
using CCZModStudio.Models;

namespace CCZModStudio.Formats;

public sealed partial class GameResourceIndexer
{
    public IReadOnlyList<ResourceIndexItem> Index(CczProject project)
    {
        var items = new List<ResourceIndexItem>();
        AddRootFiles(project, items);
        AddE5Files(project, items);
        AddMapImages(project, items);
        AddEexFiles(project, items, "RS", "R_*.eex", "R剧本EEX");
        AddEexFiles(project, items, "RS", "S_*.eex", "S剧本EEX");
        AddFiles(project, items, "SV", "*.E5S", "E5S存档信息");
        AddFiles(project, items, "WAV", "*.wav", "WAV音效");
        AddFiles(project, items, "SoundTrk", "*.mp3", "MP3音轨");
        return items;
    }

    private static void AddRootFiles(CczProject project, List<ResourceIndexItem> items)
    {
        foreach (var path in Directory.GetFiles(project.GameRoot).OrderBy(x => x, StringComparer.CurrentCultureIgnoreCase))
        {
            var info = new FileInfo(path);
            var formatHint = DetectFormat(path);
            items.Add(new ResourceIndexItem
            {
                Category = "根目录",
                Id = string.Empty,
                Name = info.Name,
                Extension = info.Extension,
                SizeBytes = info.Length,
                Magic = ReadMagicHex(path),
                FormatHint = formatHint,
                Annotation = BuildAnnotation("根目录", info.Name, formatHint),
                Path = path
            });
        }
    }

    private static void AddE5Files(CczProject project, List<ResourceIndexItem> items)
    {
        var dir = project.ResolveGameFile("E5");
        if (!Directory.Exists(dir)) return;
        foreach (var path in Directory.GetFiles(dir, "*.e5").OrderBy(x => x, StringComparer.CurrentCultureIgnoreCase))
        {
            var info = new FileInfo(path);
            var formatHint = DetectFormat(path);
            items.Add(new ResourceIndexItem
            {
                Category = "E5资源",
                Id = string.Empty,
                Name = info.Name,
                Extension = info.Extension,
                SizeBytes = info.Length,
                Magic = ReadMagicHex(path),
                FormatHint = formatHint,
                Annotation = BuildAnnotation("E5资源", info.Name, formatHint),
                Path = path
            });
        }
    }

    private static void AddMapImages(CczProject project, List<ResourceIndexItem> items)
    {
        var dir = project.ResolveGameFile("Map");
        if (!Directory.Exists(dir)) return;
        var files = Directory.GetFiles(dir)
            .Where(x => Path.GetExtension(x).Equals(".jpg", StringComparison.OrdinalIgnoreCase) ||
                        Path.GetExtension(x).Equals(".jpeg", StringComparison.OrdinalIgnoreCase))
            .OrderBy(x => x, StringComparer.CurrentCultureIgnoreCase);

        foreach (var path in files)
        {
            var info = new FileInfo(path);
            var formatHint = DetectFormat(path);
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
                // Leave dimensions empty; the preview surface reports image load errors.
            }

            items.Add(new ResourceIndexItem
            {
                Category = "地图图片",
                Id = ExtractNumber(info.Name),
                Name = info.Name,
                Extension = info.Extension,
                SizeBytes = info.Length,
                Magic = ReadMagicHex(path),
                FormatHint = formatHint,
                Annotation = BuildAnnotation("地图图片", info.Name, formatHint),
                Width = width,
                Height = height,
                Path = path
            });
        }
    }

    private static void AddEexFiles(CczProject project, List<ResourceIndexItem> items, string dirName, string pattern, string category)
    {
        var dir = project.ResolveGameFile(dirName);
        if (!Directory.Exists(dir)) return;
        foreach (var path in Directory.GetFiles(dir, pattern).OrderBy(x => x, StringComparer.CurrentCultureIgnoreCase))
        {
            var info = new FileInfo(path);
            var formatHint = DetectFormat(path);
            items.Add(new ResourceIndexItem
            {
                Category = category,
                Id = ExtractNumber(info.Name),
                Name = info.Name,
                Extension = info.Extension,
                SizeBytes = info.Length,
                Magic = ReadMagicHex(path),
                FormatHint = formatHint,
                Annotation = BuildAnnotation(category, info.Name, formatHint),
                Path = path
            });
        }
    }

    private static void AddFiles(CczProject project, List<ResourceIndexItem> items, string dirName, string pattern, string category)
    {
        var dir = project.ResolveGameFile(dirName);
        if (!Directory.Exists(dir)) return;
        foreach (var path in Directory.GetFiles(dir, pattern).OrderBy(x => x, StringComparer.CurrentCultureIgnoreCase))
        {
            var info = new FileInfo(path);
            var formatHint = DetectFormat(path);
            items.Add(new ResourceIndexItem
            {
                Category = category,
                Id = ExtractNumber(info.Name),
                Name = info.Name,
                Extension = info.Extension,
                SizeBytes = info.Length,
                Magic = ReadMagicHex(path),
                FormatHint = formatHint,
                Annotation = BuildAnnotation(category, info.Name, formatHint),
                Path = path
            });
        }
    }


    private static string BuildAnnotation(string category, string fileName, string formatHint)
    {
        var name = Path.GetFileNameWithoutExtension(fileName);
        var categoryText = category switch
        {
            "\u6839\u76ee\u5f55" => "\u6839\u76ee\u5f55\u6587\u4ef6\uff1a\u901a\u5e38\u662f\u4e3b\u7a0b\u5e8f\u3001\u6838\u5fc3\u8868\u6216\u5168\u5c40\u914d\u7f6e\u3002",
            "E5\u8d44\u6e90" => "E5 \u5c01\u88c5\u8d44\u6e90\uff1a\u53ef\u80fd\u662f\u6570\u503c\u8868\u3001\u56fe\u50cf\u3001\u52a8\u4f5c\u6216\u5176\u4ed6\u5c01\u88c5\u8f7d\u8377\u3002",
            "\u5730\u56fe\u56fe\u7247" => "\u5730\u56fe\u56fe\u7247\uff1a\u53ef\u5728\u5730\u56fe\u6d4f\u89c8\u9875\u9884\u89c8\uff0c\u5e38\u7528\u4e8e\u5173\u5361\u6218\u573a\u80cc\u666f/\u5730\u56fe\u7d20\u6750\u3002",
            "R剧本EEX" => "R 剧本/场景 eex：用于 R 场景脚本/资源相关读取；人物 R 形象本体按教程定位到 Pmapobj.e5。",
            "S剧本EEX" => "S 剧本/战场 eex：用于 S 战场脚本/资源相关读取；人物 S 形象本体按教程定位到 Unit_atk/mov/spc.e5。",
            "\u5267\u672c/\u5173\u5361" => "SV/E5S \u5267\u672c\u5173\u5361\u6587\u4ef6\uff1a\u53ef\u5728 SV \u63a2\u9488\u9875\u67e5\u770b\u547d\u4ee4\u5019\u9009\u548c\u6587\u672c\u7ebf\u7d22\u3002",
            "WAV\u97f3\u6548" => "WAV \u97f3\u6548\uff1a\u901a\u5e38\u7528\u4e8e\u6218\u6597\u3001\u754c\u9762\u6216\u5267\u60c5\u97f3\u6548\u3002",
            "MP3\u97f3\u8f68" => "MP3 \u97f3\u8f68\uff1a\u901a\u5e38\u7528\u4e8e\u80cc\u666f\u97f3\u4e50\u6216\u5267\u60c5\u97f3\u4e50\u3002",
            _ => "\u8d44\u6e90\u6587\u4ef6\uff1a\u5df2\u7d22\u5f15\uff0c\u9700\u7ed3\u5408\u5206\u7c7b\u548c\u683c\u5f0f\u63d0\u793a\u5224\u65ad\u7528\u9014\u3002"
        };

        var formatText = string.IsNullOrWhiteSpace(formatHint)
            ? "\u672a\u8bc6\u522b\u5230\u7a33\u5b9a\u9b54\u6570\uff0c\u9700\u7ed3\u5408\u6269\u5c55\u540d\u548c\u4e13\u7528\u63a2\u9488\u5224\u65ad\u3002"
            : $"\u683c\u5f0f\u7ebf\u7d22\uff1a{formatHint}\u3002";
        var nameText = name.Contains("Face", StringComparison.OrdinalIgnoreCase) ? "\u6587\u4ef6\u540d\u6697\u793a\u5934\u50cf\u8d44\u6e90\u3002" :
            name.Contains("Unit", StringComparison.OrdinalIgnoreCase) ? "\u6587\u4ef6\u540d\u6697\u793a\u5355\u4f4d\u52a8\u4f5c\u8d44\u6e90\u3002" :
            name.Contains("Map", StringComparison.OrdinalIgnoreCase) ? "\u6587\u4ef6\u540d\u6697\u793a\u5730\u56fe\u76f8\u5173\u8d44\u6e90\u3002" : string.Empty;
        return categoryText + formatText + nameText;
    }

    private static string ExtractNumber(string fileName)
    {
        var match = NumberRegex().Match(fileName);
        return match.Success ? match.Value : string.Empty;
    }

    private static string ReadMagicHex(string path)
    {
        Span<byte> buffer = stackalloc byte[8];
        try
        {
            using var stream = File.OpenRead(path);
            var read = stream.Read(buffer);
            return BitConverter.ToString(buffer[..read].ToArray()).Replace("-", " ");
        }
        catch
        {
            return string.Empty;
        }
    }

    private static string DetectFormat(string path)
    {
        Span<byte> buffer = stackalloc byte[16];
        try
        {
            using var stream = File.OpenRead(path);
            var read = stream.Read(buffer);
            if (read >= 4 && buffer[0] == (byte)'E' && buffer[1] == (byte)'E' && buffer[2] == (byte)'X' && buffer[3] == 0) return "EEX资源包";
            if (read >= 4 && buffer[0] == (byte)'L' && buffer[1] == (byte)'s' && buffer[2] == (byte)'1') return "Ls压缩资源";
            if (read >= 2 && buffer[0] == (byte)'M' && buffer[1] == (byte)'Z') return "Windows PE";
            if (read >= 2 && buffer[0] == 0xFF && buffer[1] == 0xD8) return "JPEG地图/图片";
            if (read >= 4 && buffer[0] == (byte)'R' && buffer[1] == (byte)'I' && buffer[2] == (byte)'F' && buffer[3] == (byte)'F') return "WAV音效";
            if (read >= 3 && buffer[0] == (byte)'I' && buffer[1] == (byte)'D' && buffer[2] == (byte)'3') return "MP3音轨";
            if (read >= 2 && buffer[0] == 0xFF && (buffer[1] & 0xE0) == 0xE0) return "MP3音轨";
        }
        catch
        {
            return string.Empty;
        }

        return Path.GetExtension(path).Equals(".E5S", StringComparison.OrdinalIgnoreCase)
            ? "E5S存档信息/旧兼容"
            : string.Empty;
    }

    [GeneratedRegex(@"\d+")]
    private static partial Regex NumberRegex();
}
