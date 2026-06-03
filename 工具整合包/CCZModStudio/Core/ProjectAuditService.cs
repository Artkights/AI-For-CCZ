using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using CCZModStudio.Formats;
using CCZModStudio.Models;

namespace CCZModStudio.Core;

public sealed class ProjectAuditService
{
    private readonly HexTableReader _tableReader = new();

    public IReadOnlyList<ProjectAuditItem> Analyze(CczProject project, IReadOnlyList<HexTableDefinition> tables)
    {
        var items = new List<ProjectAuditItem>();
        AddMode(items, project);
        items.AddRange(ProjectVersionGuardService.Analyze(project, tables));
        AddCoreFiles(items, project);
        AddDirectories(items, project);
        AddTableChecks(items, project, tables);
        AddResourceCounts(items, project);
        return items;
    }

    public string WriteReport(CczProject project, IReadOnlyList<ProjectAuditItem> items)
    {
        var reportRoot = Path.Combine(project.WorkspaceRoot, "CCZModStudio_Reports");
        Directory.CreateDirectory(reportRoot);
        var stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);
        var reportPath = Path.Combine(reportRoot, $"{stamp}_ProjectAudit.txt");

        var lines = new List<string>
        {
            "CCZModStudio Project Audit",
            "CreatedAt=" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
            "GameRoot=" + project.GameRoot,
            "HexTable=" + project.HexTableXmlPath,
            "IsTestCopy=" + project.IsTestCopy,
            string.Empty,
            "Severity\tCategory\tName\tStatus\tDetail\tPath"
        };

        lines.AddRange(items.Select(x => string.Join('\t',
            Sanitize(x.Severity),
            Sanitize(x.Category),
            Sanitize(x.Name),
            Sanitize(x.Status),
            Sanitize(x.Detail),
            Sanitize(x.Path))));

        File.WriteAllLines(reportPath, lines, Encoding.UTF8);
        return reportPath;
    }

    private static void AddMode(List<ProjectAuditItem> items, CczProject project)
    {
        items.Add(new ProjectAuditItem
        {
            Severity = "Info",
            Category = "安全模式",
            Name = "保存模式",
            Status = project.IsTestCopy ? "测试副本" : "当前项目",
            Detail = project.IsTestCopy
                ? "当前目录带 _CCZModStudio_TestCopy.txt 标记；已确认格式可写入，保存前自动备份。"
                : "当前目录没有测试副本标记；已确认的数据表和 R/S 联动允许直接保存，保存前自动备份。"
        });
    }

    private static void AddCoreFiles(List<ProjectAuditItem> items, CczProject project)
    {
        foreach (var file in ProjectVersionGuardService.Expected65CoreSizes.Keys)
        {
            var path = project.ResolveGameFile(file);
            if (!File.Exists(path))
            {
                items.Add(new ProjectAuditItem
                {
                    Severity = "Error",
                    Category = "核心文件",
                    Name = file,
                    Status = "缺失",
                    Detail = "6.5 核心文件不存在。",
                    Path = path
                });
                continue;
            }

            var info = new FileInfo(path);
            var expected = ProjectVersionGuardService.Expected65CoreSizes[file];
            var severity = info.Length == expected ? "Info" : "Warn";
            var status = info.Length == expected ? "尺寸匹配" : "尺寸不同";
            items.Add(new ProjectAuditItem
            {
                Severity = severity,
                Category = "核心文件",
                Name = file,
                Status = status,
                Detail = $"实际 {info.Length} 字节，6.5 基准 {expected} 字节，SHA256={ComputeSha256(path)}",
                Path = path
            });
        }
    }

    private static void AddDirectories(List<ProjectAuditItem> items, CczProject project)
    {
        foreach (var dir in new[] { "E5", "Map", "RS", "SV", "WAV", "SoundTrk" })
        {
            var path = project.ResolveGameFile(dir);
            var exists = Directory.Exists(path);
            items.Add(new ProjectAuditItem
            {
                Severity = exists ? "Info" : "Warn",
                Category = "资源目录",
                Name = dir,
                Status = exists ? "存在" : "缺失",
                Detail = exists ? $"{Directory.GetFiles(path).Length} 个直接文件" : "资源目录不存在，相关预览或发布可能不完整。",
                Path = path
            });
        }
    }

    private void AddTableChecks(List<ProjectAuditItem> items, CczProject project, IReadOnlyList<HexTableDefinition> tables)
    {
        var enabled65 = tables.Where(t => t.Enabled && t.Version == "6.5").ToList();
        var unusable = new List<string>();
        foreach (var table in enabled65)
        {
            var validation = _tableReader.Validate(project, table);
            if (!validation.IsUsable)
            {
                unusable.Add($"{table.TableName}: {string.Join("; ", validation.Warnings)}");
            }
        }

        items.Add(new ProjectAuditItem
        {
            Severity = unusable.Count == 0 ? "Info" : "Error",
            Category = "表结构",
            Name = "启用的 6.5 表",
            Status = unusable.Count == 0 ? "全部可用" : $"{unusable.Count} 个不可用",
            Detail = unusable.Count == 0
                ? $"共 {enabled65.Count} 个启用表通过文件存在和偏移范围校验。"
                : string.Join(" | ", unusable.Take(8)),
            Path = project.HexTableXmlPath
        });
    }

    private static void AddResourceCounts(List<ProjectAuditItem> items, CczProject project)
    {
        AddCount(items, project, "Map", "*.jpg", "地图 JPG");
        AddCount(items, project, "Map", "*.JPG", "地图 JPG");
        AddCount(items, project, "RS", "R_*.eex", "R 形象");
        AddCount(items, project, "RS", "S_*.eex", "S 形象");
        AddCount(items, project, "SV", "*.E5S", "E5S 存档信息文件");
        AddCount(items, project, "WAV", "*.wav", "WAV 音效");
        AddCount(items, project, "SoundTrk", "*.mp3", "MP3 音轨");
    }

    private static void AddCount(List<ProjectAuditItem> items, CczProject project, string dir, string pattern, string name)
    {
        var path = project.ResolveGameFile(dir);
        if (!Directory.Exists(path)) return;
        var count = Directory.GetFiles(path, pattern).Length;
        items.Add(new ProjectAuditItem
        {
            Severity = count > 0 ? "Info" : "Warn",
            Category = "资源计数",
            Name = name,
            Status = count.ToString(CultureInfo.InvariantCulture),
            Detail = $"{dir}\\{pattern}",
            Path = path
        });
    }

    private static string ComputeSha256(string path)
    {
        using var stream = File.OpenRead(path);
        using var sha = SHA256.Create();
        return Convert.ToHexString(sha.ComputeHash(stream));
    }

    private static string Sanitize(string value) =>
        value.Replace('\t', ' ').Replace("\r", " ", StringComparison.Ordinal).Replace("\n", " ", StringComparison.Ordinal);
}
