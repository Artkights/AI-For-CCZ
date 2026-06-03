using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using CCZModStudio.Models;

namespace CCZModStudio.Core;

/// <summary>
/// 项目侧宝物特效目录。使用 UTF-8 JSON 保存，不修改游戏内固定宽度特效名称表。
/// </summary>
public sealed class ItemEffectCatalogService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public string GetStorePath(CczProject project)
    {
        var root = Path.Combine(project.WorkspaceRoot, "CCZModStudio_Notes");
        return Path.Combine(root, $"{MakeSafeFileName(project.Name)}_ItemEffectCatalog.json");
    }

    public IReadOnlyList<ItemEffectCatalogEntry> Load(CczProject project, IReadOnlyList<ItemEffectCatalogEntry>? seedEntries = null)
    {
        var path = GetStorePath(project);
        if (!File.Exists(path))
        {
            return Normalize(seedEntries ?? Array.Empty<ItemEffectCatalogEntry>());
        }

        try
        {
            var entries = JsonSerializer.Deserialize<List<ItemEffectCatalogEntry>>(File.ReadAllText(path, Encoding.UTF8), JsonOptions)
                          ?? new List<ItemEffectCatalogEntry>();
            return Normalize(entries);
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException("宝物特效目录 JSON 解析失败，请先备份并检查文件：" + path, ex);
        }
    }

    public string Save(CczProject project, IReadOnlyList<ItemEffectCatalogEntry> entries)
    {
        var path = GetStorePath(project);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        if (File.Exists(path))
        {
            File.Copy(path, BuildUniqueBackupPath(path), overwrite: false);
        }

        var normalized = Normalize(entries);
        File.WriteAllText(path, JsonSerializer.Serialize(normalized, JsonOptions), Encoding.UTF8);
        return path;
    }

    public IReadOnlyDictionary<int, string> BuildDisplayLookup(IReadOnlyList<ItemEffectCatalogEntry> entries)
    {
        var result = new Dictionary<int, string>();
        foreach (var group in entries.GroupBy(entry => entry.EffectId).OrderBy(group => group.Key))
        {
            var joined = string.Join(" / ", group
                .Select(entry => entry.Name?.Trim() ?? string.Empty)
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Distinct(StringComparer.CurrentCulture));
            if (string.IsNullOrWhiteSpace(joined)) continue;
            result[group.Key] = joined;
        }

        return result;
    }

    private static List<ItemEffectCatalogEntry> Normalize(IEnumerable<ItemEffectCatalogEntry> entries)
    {
        return entries
            .Select(entry => new ItemEffectCatalogEntry
            {
                EffectId = entry.EffectId,
                Name = entry.Name?.Trim() ?? string.Empty,
                Description = entry.Description?.Trim() ?? string.Empty
            })
            .Where(entry => !(entry.EffectId == 0 &&
                              string.IsNullOrWhiteSpace(entry.Name) &&
                              string.IsNullOrWhiteSpace(entry.Description)))
            .ToList();
    }

    private static string BuildUniqueBackupPath(string path)
    {
        var dir = Path.GetDirectoryName(path)!;
        var name = Path.GetFileNameWithoutExtension(path);
        var ext = Path.GetExtension(path);
        var stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss_fff", CultureInfo.InvariantCulture);
        return Path.Combine(dir, $"{name}.{stamp}.bak{ext}");
    }

    private static string MakeSafeFileName(string name)
    {
        foreach (var ch in Path.GetInvalidFileNameChars())
        {
            name = name.Replace(ch, '_');
        }

        return name;
    }
}
