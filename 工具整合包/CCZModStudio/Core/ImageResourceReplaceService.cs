using System.Globalization;
using System.Text;
using CCZModStudio.Formats;
using CCZModStudio.Models;

namespace CCZModStudio.Core;

/// <summary>
/// 人物 R/S 形象资源整文件导入/替换服务。
/// 只处理 RS\R_XX.eex / RS\S_XX.eex 这种已确认的独立资源文件，不解析或重封包 EEX 内部帧数据。
/// </summary>
public sealed class ImageResourceReplaceService
{
    private readonly WriteOperationReportService _reportService = new();

    public ResourceReplacePreviewResult PreviewReplacement(CczProject project, string targetPath, string replacementPath, string expectedPrefix)
    {
        var data = BuildPreview(project, targetPath, replacementPath, expectedPrefix);
        return new ResourceReplacePreviewResult
        {
            TargetPath = data.TargetPath,
            TargetRelativePath = data.TargetRelativePath,
            ReplacementPath = data.ReplacementPath,
            Extension = data.Extension,
            OldSizeBytes = data.OldBytes.LongLength,
            NewSizeBytes = data.NewBytes.LongLength,
            ChangedBytesEstimate = data.ChangedBytes,
            OldSha256 = data.OldHash,
            NewSha256 = data.NewHash,
            FormatCheckSummary = data.FormatCheckSummary,
            FormatWarnings = data.FormatWarnings,
            RiskSummary = data.RiskSummary
        };
    }

    public ResourceReplaceResult Replace(CczProject project, string targetPath, string replacementPath, string expectedPrefix)
    {
        var data = BuildPreview(project, targetPath, replacementPath, expectedPrefix);
        if (data.TargetExists && data.OldHash.Equals(data.NewHash, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("替换来源与当前 R/S 资源内容完全相同，无需保存。");
        }

        var backupPath = CreateBeforeSaveBackup(project, data.TargetPath, data.TargetExists);
        Directory.CreateDirectory(Path.GetDirectoryName(data.TargetPath)!);
        var tempPath = data.TargetPath + ".CCZModStudio.tmp";
        File.WriteAllBytes(tempPath, data.NewBytes);
        File.Move(tempPath, data.TargetPath, overwrite: true);

        var writtenBytes = File.ReadAllBytes(data.TargetPath);
        var writtenHash = WriteOperationReportService.ComputeSha256(writtenBytes);
        if (!writtenHash.Equals(data.NewHash, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("R/S 资源写入后复读校验失败：目标文件哈希与来源不一致。请使用备份恢复。");
        }

        EnsureEexMagic(data.TargetPath, "写入后的目标资源");
        var reportJsonPath = WriteStructuredReport(project, data, backupPath);
        return new ResourceReplaceResult
        {
            TargetPath = data.TargetPath,
            ReplacementPath = data.ReplacementPath,
            BackupPath = backupPath,
            ReportPath = reportJsonPath,
            ReportJsonPath = reportJsonPath,
            OldSizeBytes = data.OldBytes.LongLength,
            NewSizeBytes = data.NewBytes.LongLength,
            ChangedBytesEstimate = data.ChangedBytes,
            OldSha256 = data.OldHash,
            NewSha256 = data.NewHash,
            FormatCheckSummary = data.FormatCheckSummary,
            FormatWarnings = data.FormatWarnings,
            RiskSummary = data.RiskSummary
        };
    }

    private ReplacementPreviewData BuildPreview(CczProject project, string targetPath, string replacementPath, string expectedPrefix)
    {
        targetPath = Path.GetFullPath(targetPath);
        replacementPath = Path.GetFullPath(replacementPath);
        expectedPrefix = expectedPrefix.Equals("S", StringComparison.OrdinalIgnoreCase) ? "S" : "R";

        EnsureTargetIsRsImage(project, targetPath, expectedPrefix);
        if (!File.Exists(replacementPath)) throw new FileNotFoundException("替换来源 R/S 资源不存在。", replacementPath);
        EnsureEexExtension(targetPath, "目标 R/S 资源");
        EnsureEexExtension(replacementPath, "替换来源 R/S 资源");
        if (File.Exists(targetPath))
        {
            EnsureEexMagic(targetPath, "目标 R/S 资源");
        }

        EnsureEexMagic(replacementPath, "替换来源 R/S 资源");
        var oldBytes = File.Exists(targetPath) ? File.ReadAllBytes(targetPath) : Array.Empty<byte>();
        var newBytes = File.ReadAllBytes(replacementPath);
        var oldHash = oldBytes.Length == 0 ? "目标原本不存在" : WriteOperationReportService.ComputeSha256(oldBytes);
        var newHash = WriteOperationReportService.ComputeSha256(newBytes);
        var changedBytes = EstimateChangedBytes(oldBytes, newBytes);
        var relative = WriteOperationReportService.ToProjectRelativePath(project, targetPath);
        var sizeWarning = oldBytes.Length == 0
            ? "目标资源原本不存在；本次会新建该 EEX 文件。"
            : oldBytes.LongLength == newBytes.LongLength
                ? string.Empty
                : $"大小变化：原 {oldBytes.LongLength:N0} 字节 -> 新 {newBytes.LongLength:N0} 字节。";
        var formatWarnings = string.IsNullOrWhiteSpace(sizeWarning) ? Array.Empty<string>() : new[] { sizeWarning };
        var formatCheck = $"EEX 格式检查通过：识别到 EEX\\0 魔数；目标 {Path.GetFileName(targetPath)}，来源 {Path.GetFileName(replacementPath)}。";
        var risk = string.IsNullOrWhiteSpace(sizeWarning)
            ? "R/S 资源仍为 EEX 文件；本操作只做整文件替换，不修改人物表编号，替换后建议进战场查看动作/方向/特效是否正常。"
            : sizeWarning + " R/S EEX 可以整文件替换，但不会解析内部帧结构；替换后必须实机确认。";

        return new ReplacementPreviewData(
            targetPath,
            replacementPath,
            relative,
            ".eex",
            oldBytes,
            newBytes,
            oldHash,
            newHash,
            changedBytes,
            formatCheck,
            formatWarnings,
            risk,
            File.Exists(targetPath));
    }

    private string WriteStructuredReport(CczProject project, ReplacementPreviewData data, string backupPath)
    {
        var report = new WriteOperationReport
        {
            OperationKind = "R/S形象资源整文件导入/替换",
            SourceAction = "形象设定页导入/替换 R/S EEX 前自动备份",
            ProjectRoot = project.GameRoot,
            TargetRelativePath = data.TargetRelativePath,
            TargetPath = data.TargetPath,
            BackupPath = backupPath,
            BeforeSha256 = data.OldHash,
            AfterSha256 = data.NewHash,
            ChangedBytes = data.ChangedBytes,
            Summary = $"导入/替换 R/S 形象资源 {data.TargetRelativePath}，来源 {data.ReplacementPath}，估算改动 {data.ChangedBytes:N0} 字节。",
            SafetyNotes = "该报告由形象设定页直接保存生成。保存前已备份原文件；如果目标原本不存在，则备份为缺失标记。当前不会解析或重封包 EEX 内部帧/动作结构。",
            FormatCheckSummary = data.FormatCheckSummary,
            RiskSummary = data.RiskSummary,
            Changes =
            [
                new WriteOperationChange
                {
                    Category = "R/S形象资源",
                    TableName = data.TargetRelativePath,
                    ColumnName = Path.GetFileName(data.TargetPath),
                    OffsetHex = "整文件",
                    ByteLength = data.NewBytes.Length,
                    OldValue = $"旧大小={data.OldBytes.LongLength:N0} 字节；SHA256={data.OldHash}",
                    NewValue = $"新大小={data.NewBytes.LongLength:N0} 字节；SHA256={data.NewHash}；来源={data.ReplacementPath}",
                    Annotation = "人物 R/S 编号表不会被本操作修改；若要让人物使用该资源，请在形象设定页确认对应 R形象编号/S形象编号。"
                }
            ],
            Metadata =
            {
                ["ReplacementPath"] = data.ReplacementPath,
                ["OldSizeBytes"] = data.OldBytes.LongLength.ToString(CultureInfo.InvariantCulture),
                ["NewSizeBytes"] = data.NewBytes.LongLength.ToString(CultureInfo.InvariantCulture),
                ["ChangedBytesEstimate"] = data.ChangedBytes.ToString(CultureInfo.InvariantCulture),
                ["TargetExistedBefore"] = data.TargetExists.ToString(CultureInfo.InvariantCulture)
            }
        };

        return _reportService.WriteJsonReport(report, backupPath);
    }

    private static void EnsureTargetIsRsImage(CczProject project, string targetPath, string expectedPrefix)
    {
        var rsRoot = Path.GetFullPath(project.ResolveGameFile("RS")).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!targetPath.StartsWith(rsRoot, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("目标文件不在当前项目 RS 目录内，拒绝替换：" + targetPath);
        }

        var fileName = Path.GetFileName(targetPath);
        if (!fileName.StartsWith(expectedPrefix + "_", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"目标资源前缀与当前选择不一致：期望 {expectedPrefix}_XX.eex，实际 {fileName}。");
        }
    }

    private static void EnsureEexExtension(string path, string displayName)
    {
        if (!Path.GetExtension(path).Equals(".eex", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"{displayName} 必须是 .eex 文件：{path}");
        }
    }

    private static void EnsureEexMagic(string path, string displayName)
    {
        using var stream = File.OpenRead(path);
        Span<byte> header = stackalloc byte[4];
        if (stream.Read(header) < 4 || header[0] != (byte)'E' || header[1] != (byte)'E' || header[2] != (byte)'X' || header[3] != 0)
        {
            throw new InvalidOperationException($"{displayName} 格式检查失败：缺少 EEX\\0 魔数。");
        }
    }

    private static string CreateBeforeSaveBackup(CczProject project, string filePath, bool targetExists)
    {
        var backupRoot = Path.Combine(project.GameRoot, "_CCZModStudio_Backups");
        Directory.CreateDirectory(backupRoot);
        var stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss_fff", CultureInfo.InvariantCulture);
        var safeRelative = MakeSafeRelativeName(project, filePath);
        var backupPath = Path.Combine(backupRoot, targetExists ? $"{stamp}_{safeRelative}" : $"{stamp}_{safeRelative}.missing.txt");
        var suffix = 1;
        while (File.Exists(backupPath))
        {
            backupPath = Path.Combine(backupRoot, targetExists ? $"{stamp}_{suffix++}_{safeRelative}" : $"{stamp}_{suffix++}_{safeRelative}.missing.txt");
        }

        if (targetExists)
        {
            File.Copy(filePath, backupPath, overwrite: false);
        }
        else
        {
            File.WriteAllText(backupPath, "目标 R/S 资源在本次导入前不存在；该文件是缺失标记，不是可直接还原的 EEX 备份。\r\nTarget=" + filePath, Encoding.UTF8);
        }

        return backupPath;
    }

    private static string MakeSafeRelativeName(CczProject project, string filePath)
    {
        var relative = Path.GetRelativePath(project.GameRoot, filePath);
        foreach (var invalid in Path.GetInvalidFileNameChars())
        {
            relative = relative.Replace(invalid, '_');
        }

        return relative.Replace(Path.DirectorySeparatorChar, '_').Replace(Path.AltDirectorySeparatorChar, '_');
    }

    private static int EstimateChangedBytes(byte[] oldBytes, byte[] newBytes)
    {
        var length = Math.Min(oldBytes.Length, newBytes.Length);
        var changed = 0L;
        for (var i = 0; i < length; i++)
        {
            if (oldBytes[i] != newBytes[i]) changed++;
        }

        changed += Math.Abs((long)oldBytes.Length - newBytes.Length);
        return changed > int.MaxValue ? int.MaxValue : (int)changed;
    }

    private sealed record ReplacementPreviewData(
        string TargetPath,
        string ReplacementPath,
        string TargetRelativePath,
        string Extension,
        byte[] OldBytes,
        byte[] NewBytes,
        string OldHash,
        string NewHash,
        int ChangedBytes,
        string FormatCheckSummary,
        IReadOnlyList<string> FormatWarnings,
        string RiskSummary,
        bool TargetExists);
}
