using CCZModStudio.Core;
using CCZModStudio.Models;

namespace CCZModStudio.Formats;

public sealed class ScenarioTextWriter
{
    private readonly WriteOperationReportService _reportService = new();

    public ScenarioTextSaveResult SaveInPlaceToTestCopy(CczProject project, string relativeScenarioPath, IReadOnlyList<ScenarioTextEntry> entries)
        => SaveInPlaceCore(project, relativeScenarioPath, entries, requireTestCopy: true, sourceAction: "R/S eex 文本写回前自动备份");

    public ScenarioTextSaveResult SaveInPlace(CczProject project, string relativeScenarioPath, IReadOnlyList<ScenarioTextEntry> entries, string sourceAction = "R/S eex 文本写回前自动备份")
        => SaveInPlaceCore(project, relativeScenarioPath, entries, requireTestCopy: false, sourceAction: sourceAction);

    private ScenarioTextSaveResult SaveInPlaceCore(CczProject project, string relativeScenarioPath, IReadOnlyList<ScenarioTextEntry> entries, bool requireTestCopy, string sourceAction)
    {
        _ = requireTestCopy;

        if (entries.Count == 0)
        {
            throw new InvalidOperationException("没有可保存的剧本文本线索。");
        }

        var entriesToWrite = entries
            .Where(entry => string.IsNullOrEmpty(entry.OriginalText)
                            || !string.Equals(NormalizeLineBreaks(entry.Text), NormalizeLineBreaks(entry.OriginalText), StringComparison.Ordinal))
            .ToList();
        if (entriesToWrite.Count == 0)
        {
            return new ScenarioTextSaveResult
            {
                FilePath = ResolveScenarioPath(project, relativeScenarioPath),
                BackupPath = string.Empty,
                EntriesWritten = 0,
                ChangedBytes = 0
            };
        }

        var filePath = ResolveScenarioPath(project, relativeScenarioPath);
        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException("目标剧本文本文件不存在。", filePath);
        }

        var original = File.ReadAllBytes(filePath);
        var output = (byte[])original.Clone();
        var changes = new List<WriteOperationChange>();
        foreach (var entry in entriesToWrite)
        {
            ValidateEntry(entry, original.Length);
            var normalizedText = NormalizeLineBreaks(entry.Text);
            var encoded = EncodingService.Gbk.GetBytes(normalizedText);
            if (encoded.Length > entry.ByteLength)
            {
                throw new InvalidOperationException($"第 {entry.Index} 条文本超长：GBK {encoded.Length} 字节，原地容量 {entry.ByteLength} 字节。只能等长或缩短。");
            }
            if (encoded.Length < 4)
            {
                throw new InvalidOperationException($"第 {entry.Index} 条文本过短：GBK {encoded.Length} 字节。为避免破坏剧本文本索引，当前工具要求至少 4 字节。");
            }

            Array.Clear(output, entry.Offset, entry.ByteLength);
            Buffer.BlockCopy(encoded, 0, output, entry.Offset, encoded.Length);
            changes.Add(new WriteOperationChange
            {
                Category = "剧本文本",
                TableName = Path.GetFileName(relativeScenarioPath),
                RowIndex = entry.Index,
                ColumnName = entry.Kind,
                OffsetHex = entry.OffsetHex,
                ByteLength = entry.ByteLength,
                OldValue = NormalizeLineBreaks(entry.OriginalText),
                NewValue = normalizedText,
                Annotation = $"R/S eex 文本 #{entry.Index}（{entry.Kind}，{entry.OffsetHex}）从“{NormalizeLineBreaks(entry.OriginalText)}”改为“{normalizedText}”；原地容量 {entry.ByteLength} 字节。"
            });
        }

        var changedBytes = 0;
        for (var i = 0; i < original.Length; i++)
        {
            if (original[i] != output[i]) changedBytes++;
        }

        var backupPath = CreateBeforeSaveBackup(project, filePath);
        File.WriteAllBytes(filePath, output);
        var reportJsonPath = WriteStructuredReport(project, relativeScenarioPath, filePath, backupPath, original, output, changedBytes, entriesToWrite.Count, changes, sourceAction);

        return new ScenarioTextSaveResult
        {
            FilePath = filePath,
            BackupPath = backupPath,
            ReportJsonPath = reportJsonPath,
            EntriesWritten = entriesToWrite.Count,
            ChangedBytes = changedBytes
        };
    }

    private string WriteStructuredReport(
        CczProject project,
        string relativeScenarioPath,
        string filePath,
        string backupPath,
        byte[] original,
        byte[] output,
        int changedBytes,
        int entriesWritten,
        List<WriteOperationChange> changes,
        string sourceAction)
    {
        var normalizedRelativePath = relativeScenarioPath
            .Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar)
            .TrimStart(Path.DirectorySeparatorChar);
        var report = new WriteOperationReport
        {
            OperationKind = "R/S eex 剧本文本写回",
            SourceAction = sourceAction,
            ProjectRoot = project.GameRoot,
            TargetRelativePath = normalizedRelativePath,
            TargetPath = filePath,
            BackupPath = backupPath,
            BeforeSha256 = WriteOperationReportService.ComputeSha256(original),
            AfterSha256 = WriteOperationReportService.ComputeSha256(output),
            ChangedBytes = changedBytes,
            Summary = $"写回 {entriesWritten} 条 R/S eex 文本，目标 {normalizedRelativePath}，字节改动 {changedBytes:N0}。",
            SafetyNotes = project.IsTestCopy
                ? "该报告由测试副本 R/S eex 文本原地短写回流程自动生成。当前只支持等长或缩短写回，不移动文本区、不扩容、不重建完整命令树。"
                : "该报告由当前 MOD 项目 R/S eex 文本原地短写回流程自动生成。保存前已备份目标文件；当前只支持等长或缩短写回，不移动文本区、不扩容、不重建完整命令树。",
            Changes = changes,
            Metadata =
            {
                ["RelativeScenarioPath"] = normalizedRelativePath,
                ["EntriesWritten"] = entriesWritten.ToString(System.Globalization.CultureInfo.InvariantCulture)
            }
        };
        return _reportService.WriteJsonReport(report, backupPath);
    }

    private static string ResolveScenarioPath(CczProject project, string relativeScenarioPath)
    {
        var filePath = Path.GetFullPath(Path.Combine(project.GameRoot, relativeScenarioPath));
        var gameRoot = Path.GetFullPath(project.GameRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!filePath.StartsWith(gameRoot, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("目标剧本文本文件不在当前项目目录内，拒绝写入：" + filePath);
        }

        return filePath;
    }

    private static void ValidateEntry(ScenarioTextEntry entry, int fileLength)
    {
        if (entry.Offset < 0 || entry.ByteLength <= 0 || checked(entry.Offset + entry.ByteLength) > fileLength)
        {
            throw new InvalidOperationException($"第 {entry.Index} 条文本偏移/长度超出文件范围。Offset={entry.OffsetHex}, ByteLength={entry.ByteLength}");
        }
    }

    private static string NormalizeLineBreaks(string? text)
        => (text ?? string.Empty).Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Trim();

    private static string CreateBeforeSaveBackup(CczProject project, string filePath)
    {
        var backupRoot = Path.Combine(project.GameRoot, "_CCZModStudio_Backups");
        Directory.CreateDirectory(backupRoot);
        var stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss_fff");
        var backupPath = Path.Combine(backupRoot, $"{stamp}_{Path.GetFileName(filePath)}");
        var suffix = 1;
        while (File.Exists(backupPath))
        {
            backupPath = Path.Combine(backupRoot, $"{stamp}_{suffix++}_{Path.GetFileName(filePath)}");
        }
        File.Copy(filePath, backupPath, overwrite: false);
        return backupPath;
    }
}
