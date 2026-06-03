using CCZModStudio.Models;

namespace CCZModStudio.Core;

/// <summary>
/// 创作者备注关联服务：按 Scope + TargetKey 查找与当前界面对象直接相关的项目侧备注。
/// 该服务只处理内存中的备注列表，不读写游戏文件。
/// </summary>
public sealed class CreatorNoteRelationService
{
    public IReadOnlyList<CreatorNote> FindExact(IEnumerable<CreatorNote> notes, string scope, string targetKey)
    {
        if (string.IsNullOrWhiteSpace(scope) || string.IsNullOrWhiteSpace(targetKey))
        {
            return Array.Empty<CreatorNote>();
        }

        var scopeAliases = BuildScopeAliases(scope);
        return notes
            .Where(note =>
                scopeAliases.Contains(note.Scope, StringComparer.OrdinalIgnoreCase) &&
                note.TargetKey.Equals(targetKey, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(note => note.UpdatedAtText, StringComparer.Ordinal)
            .ToList();
    }

    public int CountExact(IEnumerable<CreatorNote> notes, string scope, string targetKey)
        => FindExact(notes, scope, targetKey).Count;

    public string BuildSummary(IEnumerable<CreatorNote> notes, string scope, string targetKey, int maxItems = 3)
    {
        var related = FindExact(notes, scope, targetKey);
        if (related.Count == 0)
        {
            return "\r\n相关创作者备注：暂无。可在“创作者备注”页点击“抓取当前选择”记录用途、风险、待办和实机验证结果。";
        }

        var lines = new List<string>
        {
            $"\r\n相关创作者备注：{related.Count} 条（项目侧记录，不写入游戏文件）"
        };

        foreach (var note in related.Take(Math.Max(1, maxItems)))
        {
            var tags = string.IsNullOrWhiteSpace(note.Tags) ? "无标签" : note.Tags;
            lines.Add($"- {note.Title}｜标签：{tags}｜更新：{note.UpdatedAtText}");
            if (!string.IsNullOrWhiteSpace(note.Content))
            {
                lines.Add("  " + Trim(note.Content, 90));
            }
        }

        if (related.Count > maxItems)
        {
            lines.Add($"  ……另有 {related.Count - maxItems} 条，请到“创作者备注”页筛选查看。");
        }

        return string.Join("\r\n", lines);
    }

    private static string Trim(string text, int maxChars)
    {
        text = text.Replace("\r\n", " ", StringComparison.Ordinal)
            .Replace('\r', ' ')
            .Replace('\n', ' ')
            .Trim();
        return text.Length <= maxChars ? text : text[..maxChars] + "…";
    }

    private static IReadOnlyList<string> BuildScopeAliases(string scope)
    {
        if (scope.Equals("R/S命令", StringComparison.OrdinalIgnoreCase) ||
            scope.Equals("SV命令", StringComparison.OrdinalIgnoreCase))
        {
            return new[] { "R/S命令", "SV命令" };
        }

        if (scope.Equals("R/S文本", StringComparison.OrdinalIgnoreCase) ||
            scope.Equals("SV文本", StringComparison.OrdinalIgnoreCase))
        {
            return new[] { "R/S文本", "SV文本" };
        }

        if (scope.Equals("R/S命令模板", StringComparison.OrdinalIgnoreCase) ||
            scope.Equals("SV命令模板", StringComparison.OrdinalIgnoreCase))
        {
            return new[] { "R/S命令模板", "SV命令模板" };
        }

        return new[] { scope };
    }
}
