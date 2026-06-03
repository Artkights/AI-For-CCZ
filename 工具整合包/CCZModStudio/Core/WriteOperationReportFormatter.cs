using System.Text;
using System.Text.Json;
using CCZModStudio.Models;

namespace CCZModStudio.Core;

/// <summary>
/// 将结构化写入 JSON 报告转成创作者可直接阅读的中文摘要。
/// </summary>
public sealed class WriteOperationReportFormatter
{
    public string FormatForCreator(string reportPath, int maxChanges = 12)
    {
        if (string.IsNullOrWhiteSpace(reportPath) || !File.Exists(reportPath))
        {
            return "结构化报告：无可读取的 JSON 报告。";
        }

        WriteOperationReport? report;
        try
        {
            report = JsonSerializer.Deserialize<WriteOperationReport>(File.ReadAllText(reportPath));
        }
        catch (Exception ex)
        {
            return "结构化报告读取失败：" + ex.Message;
        }

        if (report == null)
        {
            return "结构化报告读取失败：JSON 内容为空或格式不匹配。";
        }

        var builder = new StringBuilder();
        builder.AppendLine($"结构化报告：{report.OperationKind}");
        builder.AppendLine($"时间：{report.CreatedAt:yyyy-MM-dd HH:mm:ss}    操作ID：{report.OperationId}");
        builder.AppendLine($"目标：{report.TargetRelativePath}");
        builder.AppendLine($"备份：{report.BackupPath}");
        builder.AppendLine($"摘要：{report.Summary}");
        builder.AppendLine($"变化字节：{report.ChangedBytes:N0}");
        if (!string.IsNullOrWhiteSpace(report.BeforeSha256) || !string.IsNullOrWhiteSpace(report.AfterSha256))
        {
            builder.AppendLine($"SHA256：{ShortHash(report.BeforeSha256)} -> {ShortHash(report.AfterSha256)}");
        }
        if (!string.IsNullOrWhiteSpace(report.SafetyNotes))
        {
            builder.AppendLine($"安全说明：{report.SafetyNotes}");
        }
        if (!string.IsNullOrWhiteSpace(report.RiskSummary))
        {
            builder.AppendLine($"风险提示：{report.RiskSummary}");
        }

        builder.AppendLine("具体改动：");
        if (report.Changes.Count == 0)
        {
            builder.AppendLine("- 未记录具体改动项。");
        }
        else
        {
            foreach (var change in report.Changes.Take(Math.Max(1, maxChanges)))
            {
                var rowText = change.RowIndex.HasValue ? $"#{change.RowIndex.Value}" : "#-";
                var byteText = change.ByteLength.HasValue ? $"{change.ByteLength.Value}字节" : "未知长度";
                builder.AppendLine($"- [{change.Category}] {change.TableName} {rowText} {change.ColumnName} {change.OffsetHex} {byteText}");
                builder.AppendLine($"  原值：{TrimForDisplay(change.OldValue)}");
                builder.AppendLine($"  新值：{TrimForDisplay(change.NewValue)}");
                if (!string.IsNullOrWhiteSpace(change.Annotation))
                {
                    builder.AppendLine($"  说明：{change.Annotation}");
                }
            }
            if (report.Changes.Count > maxChanges)
            {
                builder.AppendLine($"（还有 {report.Changes.Count - maxChanges} 项改动未显示，可打开 JSON 查看完整内容。）");
            }
        }

        return builder.ToString();
    }

    private static string ShortHash(string hash)
        => string.IsNullOrWhiteSpace(hash)
            ? "-"
            : hash.Length <= 16
                ? hash
                : hash[..16] + "…";

    private static string TrimForDisplay(string value)
    {
        value = (value ?? string.Empty)
            .Replace("\r\n", "\\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Replace("\n", "\\n", StringComparison.Ordinal);
        return value.Length <= 120 ? value : value[..120] + "…";
    }
}
