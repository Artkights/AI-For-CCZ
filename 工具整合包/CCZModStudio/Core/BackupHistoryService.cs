using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using CCZModStudio.Models;

namespace CCZModStudio.Core;

/// <summary>
/// 项目备份历史扫描服务。
/// 只读取 _CCZModStudio_Backups，不写入；用于把表格保存、补丁应用、剧本文本写回、资源替换/还原产生的备份整理成时间线。
/// </summary>
public sealed partial class BackupHistoryService
{
    private static readonly string[] KnownResourceDirectories = { "Map", "RS", "SV", "E5", "WAV", "SoundTrk" };
    private static readonly string[] RootCoreFiles = { "Ekd5.exe", "Data.e5", "Imsg.e5", "Star.e5", "Hexzmap.e5" };

    public IReadOnlyList<BackupHistoryItem> Scan(CczProject project)
    {
        var backupRoot = Path.Combine(project.GameRoot, "_CCZModStudio_Backups");
        if (!Directory.Exists(backupRoot))
        {
            return Array.Empty<BackupHistoryItem>();
        }

        var reportLookup = LoadReportLookup(backupRoot, project);
        var items = new List<BackupHistoryItem>();
        foreach (var backupPath in Directory.GetFiles(backupRoot).OrderByDescending(x => x, StringComparer.CurrentCultureIgnoreCase))
        {
            var fileName = Path.GetFileName(backupPath);
            if (IsReportFile(fileName))
            {
                continue;
            }

            var createdAt = TryParseStamp(fileName);
            var reportInfo = reportLookup.TryGetValue(Path.GetFullPath(backupPath), out var foundReport) ? foundReport : null;
            var targetRelative = reportInfo?.TargetRelativePath;
            if (string.IsNullOrWhiteSpace(targetRelative))
            {
                targetRelative = InferTargetRelativePathFromBackupName(fileName);
            }

            var targetPath = string.IsNullOrWhiteSpace(targetRelative)
                ? string.Empty
                : Path.Combine(project.GameRoot, targetRelative);
            var exists = !string.IsNullOrWhiteSpace(targetPath) && File.Exists(targetPath);
            var sameExtension = exists && Path.GetExtension(targetPath).Equals(Path.GetExtension(backupPath), StringComparison.OrdinalIgnoreCase);
            var restorable = exists && sameExtension;
            var kind = reportInfo?.Kind ?? InferKind(targetRelative, backupPath);
            var status = restorable
                ? "可还原"
                : string.IsNullOrWhiteSpace(targetRelative)
                    ? "不可还原：无法识别目标路径"
                    : !exists
                        ? "不可还原：目标文件不存在"
                        : !sameExtension
                            ? "不可还原：扩展名不一致"
                            : "不可还原";

            var info = new FileInfo(backupPath);
            items.Add(new BackupHistoryItem
            {
                CreatedAt = createdAt,
                Kind = kind,
                TargetRelativePath = targetRelative ?? string.Empty,
                TargetPath = targetPath,
                BackupFileName = fileName,
                BackupPath = backupPath,
                BackupSizeBytes = info.Length,
                ReportPath = reportInfo?.ReportPath ?? string.Empty,
                SourceAction = reportInfo?.SourceAction ?? InferSourceAction(kind),
                Restorable = restorable,
                Status = status,
                Annotation = BuildAnnotation(kind, targetRelative ?? string.Empty, reportInfo != null)
            });
        }

        return items
            .OrderByDescending(x => x.CreatedAt)
            .ThenBy(x => x.BackupFileName, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    private static Dictionary<string, BackupReportInfo> LoadReportLookup(string backupRoot, CczProject project)
    {
        var result = new Dictionary<string, BackupReportInfo>(StringComparer.OrdinalIgnoreCase);
        foreach (var reportPath in Directory.GetFiles(backupRoot, "*WriteOperationReport.json").OrderBy(x => x, StringComparer.CurrentCultureIgnoreCase))
        {
            try
            {
                var report = JsonSerializer.Deserialize<WriteOperationReport>(File.ReadAllText(reportPath));
                if (report == null || string.IsNullOrWhiteSpace(report.BackupPath)) continue;
                var targetRelative = !string.IsNullOrWhiteSpace(report.TargetRelativePath)
                    ? report.TargetRelativePath
                    : !string.IsNullOrWhiteSpace(report.TargetPath)
                        ? Path.GetRelativePath(project.GameRoot, report.TargetPath)
                        : string.Empty;
                result[Path.GetFullPath(report.BackupPath)] = new BackupReportInfo(
                    string.IsNullOrWhiteSpace(report.OperationKind) ? "结构化写入" : report.OperationKind,
                    targetRelative,
                    reportPath,
                    string.IsNullOrWhiteSpace(report.SourceAction) ? "结构化写入报告" : report.SourceAction);
            }
            catch
            {
                // 结构化报告损坏不应阻断备份历史；该备份仍可由文件名推断。
            }
        }

        foreach (var reportPath in Directory.GetFiles(backupRoot, "*Report.txt").OrderBy(x => x, StringComparer.CurrentCultureIgnoreCase))
        {
            try
            {
                var values = ReadReportKeyValues(reportPath);
                if (values.TryGetValue("Backup", out var resourceBackup))
                {
                    var targetRelative = values.TryGetValue("TargetRelative", out var relative)
                        ? relative
                        : values.TryGetValue("Target", out var target)
                            ? Path.GetRelativePath(project.GameRoot, target)
                            : string.Empty;
                    var backupFullPath = Path.GetFullPath(resourceBackup);
                    if (!HasStructuredReport(result, backupFullPath))
                    {
                        result[backupFullPath] = new BackupReportInfo("资源整文件替换/还原", targetRelative, reportPath, "资源替换/还原报告");
                    }
                }
                else if (values.TryGetValue("BackupFile", out var patchBackup))
                {
                    var targetRelative = values.TryGetValue("TargetFile", out var targetFile)
                        ? Path.GetRelativePath(project.GameRoot, targetFile)
                        : string.Empty;
                    var backupFullPath = Path.GetFullPath(patchBackup);
                    if (!HasStructuredReport(result, backupFullPath))
                    {
                        result[backupFullPath] = new BackupReportInfo("补丁写入", targetRelative, reportPath, "普罗补丁应用报告");
                    }
                }
            }
            catch
            {
                // 报告损坏不应阻断备份历史；该备份仍可用文件名推断。
            }
        }

        return result;
    }

    private static bool HasStructuredReport(Dictionary<string, BackupReportInfo> lookup, string backupFullPath)
        => lookup.TryGetValue(backupFullPath, out var existing) &&
           existing.ReportPath.EndsWith("WriteOperationReport.json", StringComparison.OrdinalIgnoreCase);

    private static bool IsReportFile(string fileName)
    {
        return fileName.EndsWith("Report.txt", StringComparison.OrdinalIgnoreCase) ||
               fileName.EndsWith("Report.json", StringComparison.OrdinalIgnoreCase) ||
               fileName.EndsWith("WriteOperationReport.json", StringComparison.OrdinalIgnoreCase);
    }

    private static Dictionary<string, string> ReadReportKeyValues(string reportPath)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in File.ReadLines(reportPath))
        {
            var index = line.IndexOf('=');
            if (index <= 0) continue;
            var key = line[..index].Trim();
            var value = line[(index + 1)..].Trim();
            if (!string.IsNullOrWhiteSpace(key))
            {
                values[key] = value;
            }
        }

        return values;
    }

    private static DateTime TryParseStamp(string fileName)
    {
        var match = BackupNameRegex().Match(fileName);
        if (!match.Success)
        {
            return DateTime.MinValue;
        }

        return DateTime.TryParseExact(match.Groups["stamp"].Value, "yyyyMMdd_HHmmss_fff", CultureInfo.InvariantCulture, DateTimeStyles.None, out var value)
            ? value
            : DateTime.MinValue;
    }

    private static string InferTargetRelativePathFromBackupName(string fileName)
    {
        var match = BackupNameRegex().Match(fileName);
        var safeName = match.Success ? match.Groups["name"].Value : fileName;

        foreach (var directory in KnownResourceDirectories)
        {
            var prefix = directory + "_";
            if (safeName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return Path.Combine(directory, safeName[prefix.Length..]);
            }
        }

        if (ScenarioFileRegex().IsMatch(safeName))
        {
            return Path.Combine("SV", safeName);
        }

        return safeName;
    }

    private static string InferKind(string targetRelativePath, string backupPath)
    {
        var fileName = Path.GetFileName(targetRelativePath);
        if (RootCoreFiles.Contains(fileName, StringComparer.OrdinalIgnoreCase))
        {
            return fileName.Equals("Ekd5.exe", StringComparison.OrdinalIgnoreCase) ? "EXE/形象/补丁备份" : "核心表备份";
        }

        var extension = Path.GetExtension(targetRelativePath);
        if (extension.Equals(".E5S", StringComparison.OrdinalIgnoreCase)) return "E5S存档信息/旧兼容备份";
        if (extension.Equals(".eex", StringComparison.OrdinalIgnoreCase)) return "R/S 或 EEX 资源备份";
        if (extension.Equals(".jpg", StringComparison.OrdinalIgnoreCase) || extension.Equals(".jpeg", StringComparison.OrdinalIgnoreCase) || extension.Equals(".png", StringComparison.OrdinalIgnoreCase) || extension.Equals(".bmp", StringComparison.OrdinalIgnoreCase)) return "图片资源备份";
        if (extension.Equals(".wav", StringComparison.OrdinalIgnoreCase) || extension.Equals(".mp3", StringComparison.OrdinalIgnoreCase)) return "音频资源备份";
        if (extension.Equals(".e5", StringComparison.OrdinalIgnoreCase)) return "E5资源/表备份";
        return "文件备份";
    }

    private static string InferSourceAction(string kind)
    {
        return kind switch
        {
            "补丁写入" => "应用补丁前自动备份",
            "资源整文件替换/还原" => "资源整文件写入前自动备份",
            "核心表备份" => "数据表保存前自动备份",
            "EXE/形象/补丁备份" => "EXE/R-S/补丁写入前自动备份",
            "E5S存档信息/旧兼容备份" => "E5S 存档信息或旧兼容探针写入前自动备份",
            _ => "写入前自动备份"
        };
    }

    private static string BuildAnnotation(string kind, string targetRelativePath, bool hasReport)
    {
        var reportText = hasReport ? "已关联写入报告，可查看当时的目标、来源、哈希和改动摘要。" : "未找到配套报告，目标路径由备份文件名推断。";
        var caution = kind switch
        {
            "核心表备份" => "核心表恢复会整文件覆盖 Data/Imsg/Star/Hexzmap，请确认要回到该时间点。",
            "EXE/形象/补丁备份" => "EXE 恢复会整文件覆盖 Ekd5.exe，可能同时回退 R/S 形象编号和补丁改动。",
            "E5S存档信息/旧兼容备份" => "E5S 恢复会整文件覆盖存档/旧兼容信息；它不是当前 R/S eex 剧本主格式。",
            "资源整文件替换/还原" => "该备份来自整文件替换/还原流程，可用于把资源恢复到写入前状态。",
            _ => "还原会整文件覆盖目标；建议还原后重新运行资源诊断和差异报告。"
        };
        return $"{kind}：目标 {targetRelativePath}。{reportText}{caution}";
    }

    private sealed record BackupReportInfo(string Kind, string TargetRelativePath, string ReportPath, string SourceAction);

    [GeneratedRegex(@"^(?<stamp>\d{8}_\d{6}_\d{3})_(?:(?<dup>\d+)_)?(?<name>.+)$")]
    private static partial Regex BackupNameRegex();

    [GeneratedRegex(@"^SV\d+\.E5S$", RegexOptions.IgnoreCase)]
    private static partial Regex ScenarioFileRegex();
}
