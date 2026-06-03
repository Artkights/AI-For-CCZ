using System.Globalization;
using System.Security.Cryptography;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Unicode;
using CCZModStudio.Models;

namespace CCZModStudio.Core;

/// <summary>
/// 统一写入操作结构化报告。
/// 该服务只把已经发生的安全写入整理成 JSON 证据，便于“差异文件 -> 备份 -> 具体改动”追溯。
/// </summary>
public sealed class WriteOperationReportService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.Create(UnicodeRanges.All)
    };

    public string WriteJsonReport(WriteOperationReport report, string backupPath)
    {
        var backupRoot = Path.GetDirectoryName(backupPath);
        if (string.IsNullOrWhiteSpace(backupRoot))
        {
            throw new InvalidOperationException("无法确定备份目录，不能写入结构化报告。");
        }

        Directory.CreateDirectory(backupRoot);
        var stamp = ExtractStamp(Path.GetFileName(backupPath)) ?? DateTime.Now.ToString("yyyyMMdd_HHmmss_fff", CultureInfo.InvariantCulture);
        var safeKind = MakeSafeFileName(string.IsNullOrWhiteSpace(report.OperationKind) ? "WriteOperation" : report.OperationKind);
        var path = Path.Combine(backupRoot, $"{stamp}_{safeKind}_WriteOperationReport.json");
        var suffix = 1;
        while (File.Exists(path))
        {
            path = Path.Combine(backupRoot, $"{stamp}_{suffix++}_{safeKind}_WriteOperationReport.json");
        }

        report.JsonReportPath = path;
        File.WriteAllText(path, JsonSerializer.Serialize(report, JsonOptions));
        return path;
    }

    public static string ComputeSha256(byte[] bytes)
        => Convert.ToHexString(SHA256.HashData(bytes));

    public static string ComputeSha256(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    public static string ToProjectRelativePath(CczProject project, string path)
        => Path.GetRelativePath(project.GameRoot, path)
            .Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);

    private static string? ExtractStamp(string fileName)
    {
        if (fileName.Length < 19) return null;
        var candidate = fileName[..19];
        return DateTime.TryParseExact(candidate, "yyyyMMdd_HHmmss_fff", CultureInfo.InvariantCulture, DateTimeStyles.None, out _)
            ? candidate
            : null;
    }

    private static string MakeSafeFileName(string fileName)
    {
        foreach (var invalid in Path.GetInvalidFileNameChars())
        {
            fileName = fileName.Replace(invalid, '_');
        }
        return fileName;
    }
}
