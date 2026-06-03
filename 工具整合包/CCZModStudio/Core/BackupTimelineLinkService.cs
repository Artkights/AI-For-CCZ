using System.Text;
using CCZModStudio.Models;

namespace CCZModStudio.Core;

/// <summary>
/// 将“测试副本差异”与“备份历史”按相对路径关联起来。
/// 只做内存筛选和中文摘要，不读写游戏文件。
/// </summary>
public sealed class BackupTimelineLinkService
{
    public IReadOnlyList<BackupHistoryItem> FindRelatedBackups(ProjectDiffItem diffItem, IEnumerable<BackupHistoryItem> history)
    {
        var diffPath = NormalizeRelativePath(diffItem.RelativePath);
        if (string.IsNullOrWhiteSpace(diffPath)) return Array.Empty<BackupHistoryItem>();

        return history
            .Where(item => NormalizeRelativePath(item.TargetRelativePath).Equals(diffPath, StringComparison.OrdinalIgnoreCase)
                           || (!string.IsNullOrWhiteSpace(item.TargetPath)
                               && !string.IsNullOrWhiteSpace(diffItem.TestPath)
                               && Path.GetFullPath(item.TargetPath).Equals(Path.GetFullPath(diffItem.TestPath), StringComparison.OrdinalIgnoreCase)))
            .OrderByDescending(item => item.CreatedAt)
            .ThenBy(item => item.BackupFileName, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    public string BuildSummary(ProjectDiffItem diffItem, IReadOnlyList<BackupHistoryItem> relatedBackups, int maxItems = 5)
    {
        var builder = new StringBuilder();
        builder.AppendLine($"差异文件：{diffItem.RelativePath}");
        builder.AppendLine($"差异状态：{diffItem.Status}    原始大小：{FormatSize(diffItem.SourceSize)}    测试副本大小：{FormatSize(diffItem.TestSize)}");
        builder.AppendLine($"差异说明：{diffItem.Detail}");
        builder.AppendLine($"相关备份：{relatedBackups.Count} 条");
        if (relatedBackups.Count == 0)
        {
            builder.AppendLine("未在备份历史中找到同路径备份。可能是手动修改、复制文件、删除文件，或旧版本写入时没有生成可识别报告。");
            builder.AppendLine("建议：若要回滚，请优先查看原始项目文件或手动备份；后续写入尽量通过 CCZModStudio 的测试副本流程完成。");
            return builder.ToString();
        }

        builder.AppendLine("最近备份时间线：");
        foreach (var item in relatedBackups.Take(Math.Max(1, maxItems)))
        {
            builder.AppendLine($"- {item.CreatedAtText} [{item.Kind}] {item.BackupFileName}；{item.Status}；来源：{item.SourceAction}");
        }
        if (relatedBackups.Count > maxItems)
        {
            builder.AppendLine($"（还有 {relatedBackups.Count - maxItems} 条更早备份，可在“备份历史/回滚”页筛出查看。）");
        }
        builder.AppendLine("建议：点击“筛出相关备份”后，可在“备份历史/回滚”页定位报告或执行带预览的整文件回滚。");
        return builder.ToString();
    }

    private static string NormalizeRelativePath(string path)
        => (path ?? string.Empty)
            .Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar)
            .TrimStart(Path.DirectorySeparatorChar)
            .Trim();

    private static string FormatSize(long? size)
        => size.HasValue ? $"{size.Value:N0} 字节" : "-";
}
