using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using CCZModStudio.Models;

namespace CCZModStudio.Core;

/// <summary>
/// 项目侧创作者备注服务。只读写 CCZModStudio_Notes 下的 JSON/CSV，不修改任何游戏文件。
/// </summary>
public sealed class CreatorNoteService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public string GetStorePath(CczProject project)
    {
        var root = Path.Combine(project.WorkspaceRoot, "CCZModStudio_Notes");
        return Path.Combine(root, $"{MakeSafeFileName(project.Name)}_CreatorNotes.json");
    }

    public IReadOnlyList<CreatorNote> Load(CczProject project)
    {
        var path = GetStorePath(project);
        if (!File.Exists(path)) return Array.Empty<CreatorNote>();

        try
        {
            var notes = JsonSerializer.Deserialize<List<CreatorNote>>(File.ReadAllText(path, Encoding.UTF8), JsonOptions)
                        ?? new List<CreatorNote>();
            return notes
                .Where(note => !string.IsNullOrWhiteSpace(note.Id))
                .Select(Normalize)
                .OrderByDescending(note => note.UpdatedAtText, StringComparer.Ordinal)
                .ThenBy(note => note.Scope, StringComparer.CurrentCultureIgnoreCase)
                .ThenBy(note => note.TargetKey, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException("创作者备注 JSON 解析失败，请先备份并检查文件：" + path, ex);
        }
    }

    public CreatorNote Upsert(CczProject project, CreatorNote draft)
    {
        var notes = Load(project).Select(Clone).ToList();
        var now = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
        var id = string.IsNullOrWhiteSpace(draft.Id) ? Guid.NewGuid().ToString("N") : draft.Id.Trim();
        var existing = notes.FirstOrDefault(note => note.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
        if (existing == null)
        {
            existing = new CreatorNote
            {
                Id = id,
                CreatedAtText = now
            };
            notes.Add(existing);
        }

        existing.ProjectName = project.Name;
        existing.Scope = string.IsNullOrWhiteSpace(draft.Scope) ? "全局项目" : draft.Scope.Trim();
        existing.TargetKey = draft.TargetKey.Trim();
        existing.Title = string.IsNullOrWhiteSpace(draft.Title) ? BuildDefaultTitle(existing.Scope, existing.TargetKey) : draft.Title.Trim();
        existing.Content = draft.Content.Trim();
        existing.Tags = draft.Tags.Trim();
        existing.SourceHint = draft.SourceHint.Trim();
        existing.UpdatedAtText = now;
        if (string.IsNullOrWhiteSpace(existing.CreatedAtText)) existing.CreatedAtText = now;
        existing.SafetyNote = "项目侧备注：保存在 CCZModStudio_Notes，不写入游戏文件，不参与发布封包。";

        SaveAll(project, notes);
        return existing;
    }

    public bool Delete(CczProject project, string id)
    {
        if (string.IsNullOrWhiteSpace(id)) return false;
        var notes = Load(project).Select(Clone).ToList();
        var removed = notes.RemoveAll(note => note.Id.Equals(id, StringComparison.OrdinalIgnoreCase)) > 0;
        if (removed) SaveAll(project, notes);
        return removed;
    }

    public string ExportCsv(CczProject project, IReadOnlyList<CreatorNote>? notes = null)
    {
        notes ??= Load(project);
        var exportRoot = Path.Combine(project.WorkspaceRoot, "CCZModStudio_Exports", "CreatorNotes");
        Directory.CreateDirectory(exportRoot);
        var path = Path.Combine(exportRoot, $"{MakeSafeFileName(project.Name)}_创作者备注.csv");
        var lines = new List<string>
        {
            "Id,ProjectName,Scope,TargetKey,Title,Tags,SourceHint,CreatedAt,UpdatedAt,SafetyNote,Content"
        };
        lines.AddRange(notes.Select(note => string.Join(',',
            Csv(note.Id),
            Csv(note.ProjectName),
            Csv(note.Scope),
            Csv(note.TargetKey),
            Csv(note.Title),
            Csv(note.Tags),
            Csv(note.SourceHint),
            Csv(note.CreatedAtText),
            Csv(note.UpdatedAtText),
            Csv(note.SafetyNote),
            Csv(note.Content))));
        File.WriteAllLines(path, lines, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
        return path;
    }

    public IReadOnlyList<CreatorNote> Filter(IEnumerable<CreatorNote> notes, string keyword)
    {
        if (string.IsNullOrWhiteSpace(keyword)) return notes.ToList();
        keyword = keyword.Trim();
        return notes.Where(note =>
                Contains(note.Scope, keyword) ||
                Contains(note.TargetKey, keyword) ||
                Contains(note.Title, keyword) ||
                Contains(note.Content, keyword) ||
                Contains(note.Tags, keyword) ||
                Contains(note.SourceHint, keyword))
            .ToList();
    }

    public string BuildSummary(CczProject project, IReadOnlyList<CreatorNote> notes)
    {
        var store = GetStorePath(project);
        var byScope = notes
            .GroupBy(note => note.Scope)
            .OrderByDescending(group => group.Count())
            .ThenBy(group => group.Key, StringComparer.CurrentCultureIgnoreCase)
            .Select(group => $"{group.Key}:{group.Count()}")
            .ToList();
        return
            $"创作者备注：{notes.Count} 条。存储位置：{store}\r\n" +
            $"分类：{(byScope.Count == 0 ? "无" : string.Join("，", byScope))}\r\n" +
            "说明：这些备注只保存在项目侧 JSON 中，用于记录剧情设计、资源用途、待办、风险和实机验证结果；不会写入 Data/Imsg/SV/EEX/EXE 等游戏文件，也不会被发布副本打包。";
    }

    private static void SaveAll(CczProject project, IReadOnlyList<CreatorNote> notes)
    {
        var path = new CreatorNoteService().GetStorePath(project);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        if (File.Exists(path))
        {
            var backup = BuildUniqueBackupPath(path);
            File.Copy(path, backup, overwrite: false);
        }

        var ordered = notes
            .Select(Normalize)
            .OrderByDescending(note => note.UpdatedAtText, StringComparer.Ordinal)
            .ThenBy(note => note.Scope, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(note => note.TargetKey, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
        File.WriteAllText(path, JsonSerializer.Serialize(ordered, JsonOptions), Encoding.UTF8);
    }

    private static string BuildUniqueBackupPath(string path)
    {
        var directory = Path.GetDirectoryName(path)!;
        var baseName = Path.GetFileNameWithoutExtension(path);
        var stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss_fff", CultureInfo.InvariantCulture);
        var candidate = Path.Combine(directory, $"{baseName}_{stamp}.bak.json");
        for (var index = 1; File.Exists(candidate); index++)
        {
            candidate = Path.Combine(directory, $"{baseName}_{stamp}_{index}.bak.json");
        }
        return candidate;
    }

    private static CreatorNote Normalize(CreatorNote note)
    {
        note.Scope = string.IsNullOrWhiteSpace(note.Scope) ? "全局项目" : note.Scope.Trim();
        note.TargetKey = note.TargetKey?.Trim() ?? string.Empty;
        note.Title = string.IsNullOrWhiteSpace(note.Title) ? BuildDefaultTitle(note.Scope, note.TargetKey) : note.Title.Trim();
        note.Content = note.Content?.Trim() ?? string.Empty;
        note.Tags = note.Tags?.Trim() ?? string.Empty;
        note.SourceHint = note.SourceHint?.Trim() ?? string.Empty;
        note.SafetyNote = string.IsNullOrWhiteSpace(note.SafetyNote)
            ? "项目侧备注：不写入游戏文件，不参与发布封包。"
            : note.SafetyNote.Trim();
        return note;
    }

    private static CreatorNote Clone(CreatorNote note) => new()
    {
        Id = note.Id,
        ProjectName = note.ProjectName,
        Scope = note.Scope,
        TargetKey = note.TargetKey,
        Title = note.Title,
        Content = note.Content,
        Tags = note.Tags,
        SourceHint = note.SourceHint,
        CreatedAtText = note.CreatedAtText,
        UpdatedAtText = note.UpdatedAtText,
        SafetyNote = note.SafetyNote
    };

    private static string BuildDefaultTitle(string scope, string target)
        => string.IsNullOrWhiteSpace(target) ? $"{scope}备注" : $"{scope}：{target}";

    private static bool Contains(string value, string keyword)
        => value?.IndexOf(keyword, StringComparison.CurrentCultureIgnoreCase) >= 0;

    private static string Csv(string value)
        => "\"" + (value ?? string.Empty).Replace("\"", "\"\"", StringComparison.Ordinal).Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n') + "\"";

    private static string MakeSafeFileName(string name)
    {
        foreach (var ch in Path.GetInvalidFileNameChars())
        {
            name = name.Replace(ch, '_');
        }
        return string.IsNullOrWhiteSpace(name) ? "Project" : name;
    }
}
