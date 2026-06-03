using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using CCZModStudio.Core;
using CCZModStudio.Models;

namespace CCZModStudio.Formats;

public sealed class PatchApplyService
{
    private readonly WriteOperationReportService _reportService = new();

    public PatchPreviewResult Preview(CczProject project, PatchDocument document, string targetFileName = "Ekd5.exe")
    {
        var targetFilePath = project.ResolveGameFile(targetFileName);
        if (!File.Exists(targetFilePath))
        {
            var missingRows = document.Entries.Select(e => new PatchPreviewRow
            {
                Index = e.Index,
                SourceLine = e.SourceLine,
                Comment = e.Comment ?? string.Empty,
                AddressKind = document.AddressKind.ToString(),
                AddressHex = "0x" + e.AddressHex,
                FileOffsetHex = "-",
                FileOffset = -1,
                Length = e.Bytes.Length,
                NewBytesHex = ToHex(e.Bytes),
                CanApply = false,
                Status = "目标文件不存在：" + targetFilePath
            }).ToList();

            return new PatchPreviewResult { Document = document, TargetFilePath = targetFilePath, Rows = missingRows, ChangedBytes = 0 };
        }

        var targetBytes = File.ReadAllBytes(targetFilePath);
        PeAddressMapper? mapper = null;
        if (document.AddressKind == PatchAddressKind.OdVirtualAddress)
        {
            try
            {
                mapper = PeAddressMapper.Load(targetFilePath);
            }
            catch (Exception ex)
            {
                var mapErrorRows = document.Entries.Select(e => new PatchPreviewRow
                {
                    Index = e.Index,
                    SourceLine = e.SourceLine,
                    Comment = e.Comment ?? string.Empty,
                    AddressKind = document.AddressKind.ToString(),
                    AddressHex = "0x" + e.AddressHex,
                    FileOffsetHex = "-",
                    FileOffset = -1,
                    Length = e.Bytes.Length,
                    NewBytesHex = ToHex(e.Bytes),
                    CanApply = false,
                    Status = "PE 映射初始化失败：" + ex.Message
                }).ToList();
                return new PatchPreviewResult { Document = document, TargetFilePath = targetFilePath, Rows = mapErrorRows, ChangedBytes = 0 };
            }
        }

        var rows = new List<PatchPreviewRow>();
        foreach (var entry in document.Entries)
        {
            rows.Add(BuildPreviewRow(document, entry, targetBytes, mapper));
        }

        return new PatchPreviewResult
        {
            Document = document,
            TargetFilePath = targetFilePath,
            Rows = rows,
            ChangedBytes = ComputeFinalChangedBytes(targetBytes, document, rows)
        };
    }

    public PatchApplyResult ApplyToTestCopy(CczProject project, PatchDocument document, string targetFileName = "Ekd5.exe")
    {
        if (!project.IsTestCopy)
        {
            throw new InvalidOperationException("安全限制：当前项目不是测试副本，禁止应用补丁。请先创建并打开 CCZModStudio 测试副本。 ");
        }

        ProjectVersionGuardService.EnsureCoreFileCompatibleForWrite(project, targetFileName);

        var preview = Preview(project, document, targetFileName);
        if (!preview.CanApply)
        {
            var errors = preview.Rows.Where(r => !r.CanApply).Take(10).Select(r => $"#{r.Index} {r.AddressHex}: {r.Status}");
            throw new InvalidOperationException("补丁预览存在不可应用项，已取消写入：\r\n" + string.Join("\r\n", errors));
        }

        var targetBytes = File.ReadAllBytes(preview.TargetFilePath);
        var beforeHash = ComputeSha256(targetBytes);
        var output = (byte[])targetBytes.Clone();
        var entryByIndex = document.Entries.ToDictionary(e => e.Index);

        var bytesWritten = 0;
        foreach (var row in preview.Rows)
        {
            var entry = entryByIndex[row.Index];
            for (var i = 0; i < entry.Bytes.Length; i++)
            {
                var offset = checked((int)row.FileOffset + i);
                output[offset] = entry.Bytes[i];
            }
            bytesWritten += entry.Bytes.Length;
        }

        var changedBytes = CountDifferences(targetBytes, output);

        var backupPath = CreateBeforeSaveBackup(project, preview.TargetFilePath);
        File.WriteAllBytes(preview.TargetFilePath, output);
        var afterHash = ComputeSha256(output);
        var reportPath = WriteReport(project, preview, backupPath, beforeHash, afterHash, changedBytes, bytesWritten);
        var reportJsonPath = WriteStructuredReport(project, preview, backupPath, reportPath, beforeHash, afterHash, changedBytes, bytesWritten);

        return new PatchApplyResult
        {
            TargetFilePath = preview.TargetFilePath,
            BackupPath = backupPath,
            ReportPath = reportPath,
            ReportJsonPath = reportJsonPath,
            EntriesApplied = preview.Rows.Count,
            BytesWritten = bytesWritten,
            ChangedBytes = changedBytes
        };
    }

    private string WriteStructuredReport(
        CczProject project,
        PatchPreviewResult preview,
        string backupPath,
        string textReportPath,
        string beforeHash,
        string afterHash,
        int changedBytes,
        int bytesWritten)
    {
        var targetRelative = WriteOperationReportService.ToProjectRelativePath(project, preview.TargetFilePath);
        var report = new WriteOperationReport
        {
            OperationKind = "补丁写入",
            SourceAction = "普罗补丁应用前自动备份",
            ProjectRoot = project.GameRoot,
            TargetRelativePath = targetRelative,
            TargetPath = preview.TargetFilePath,
            BackupPath = backupPath,
            TextReportPath = textReportPath,
            BeforeSha256 = beforeHash,
            AfterSha256 = afterHash,
            ChangedBytes = changedBytes,
            Summary = $"应用补丁到“{targetRelative}”，补丁项 {preview.Rows.Count} 条，写入 {bytesWritten:N0} 字节，实际变化 {changedBytes:N0} 字节。",
            SafetyNotes = "该报告由测试副本补丁写入流程自动生成。所有补丁项已先完成地址换算、越界检查和预览；写入方式为定长字节覆盖，不插入、不删除、不扩展 EXE。",
            FormatCheckSummary = $"补丁预览通过：{preview.Rows.Count} 项，地址类型 {preview.Document.AddressKind}。",
            RiskSummary = "补丁会直接改变目标二进制文件字节；如补丁来源版本不匹配，可能导致游戏异常。必要时可在备份历史/回滚页恢复写入前备份。",
            Changes = preview.Rows.Select(row => new WriteOperationChange
            {
                Category = "补丁字节",
                TableName = targetRelative,
                RowIndex = row.Index,
                ColumnName = string.IsNullOrWhiteSpace(row.Comment) ? $"源行 {row.SourceLine}" : row.Comment,
                OffsetHex = row.FileOffsetHex,
                ByteLength = row.Length,
                OldValue = row.OldBytesHex,
                NewValue = row.NewBytesHex,
                Annotation = $"源行 {row.SourceLine}，地址 {row.AddressHex}，文件偏移 {row.FileOffsetHex}，长度 {row.Length} 字节，{(row.Changed ? "会改变原有字节" : "写入内容与原字节一致")}。{row.Comment}"
            }).ToList(),
            Metadata =
            {
                ["PatchSource"] = preview.Document.SourcePath,
                ["PatchVersion"] = preview.Document.Version,
                ["AddressKind"] = preview.Document.AddressKind.ToString(),
                ["Entries"] = preview.Rows.Count.ToString(CultureInfo.InvariantCulture),
                ["BytesWritten"] = bytesWritten.ToString(CultureInfo.InvariantCulture),
                ["ChangedBytes"] = changedBytes.ToString(CultureInfo.InvariantCulture)
            }
        };

        return _reportService.WriteJsonReport(report, backupPath);
    }

    private static PatchPreviewRow BuildPreviewRow(PatchDocument document, PatchEntry entry, byte[] targetBytes, PeAddressMapper? mapper)
    {
        try
        {
            if (entry.Bytes.Length == 0)
            {
                return ErrorRow(document, entry, "补丁项没有字节内容。 ");
            }

            var fileOffset = document.AddressKind switch
            {
                PatchAddressKind.FileOffset => entry.Address,
                PatchAddressKind.OdVirtualAddress => mapper?.VirtualAddressToFileOffset(entry.Address)
                    ?? throw new InvalidOperationException("缺少 PE 映射器。"),
                _ => throw new InvalidOperationException("未知地址类型，不能换算文件偏移。")
            };

            if (fileOffset < 0 || fileOffset > targetBytes.LongLength)
            {
                return ErrorRow(document, entry, $"文件偏移越界：0x{fileOffset:X}", fileOffset);
            }

            if (fileOffset + entry.Bytes.Length > targetBytes.LongLength)
            {
                return ErrorRow(document, entry, $"写入范围越界：0x{fileOffset:X} + {entry.Bytes.Length} > 文件长度 0x{targetBytes.LongLength:X}", fileOffset);
            }

            var oldBytes = new byte[entry.Bytes.Length];
            Buffer.BlockCopy(targetBytes, checked((int)fileOffset), oldBytes, 0, entry.Bytes.Length);
            var changed = !oldBytes.SequenceEqual(entry.Bytes);

            return new PatchPreviewRow
            {
                Index = entry.Index,
                SourceLine = entry.SourceLine,
                Comment = entry.Comment ?? string.Empty,
                AddressKind = document.AddressKind.ToString(),
                AddressHex = "0x" + entry.AddressHex,
                FileOffsetHex = "0x" + fileOffset.ToString("X", CultureInfo.InvariantCulture),
                FileOffset = fileOffset,
                Length = entry.Bytes.Length,
                OldBytesHex = ToHex(oldBytes),
                NewBytesHex = ToHex(entry.Bytes),
                Changed = changed,
                CanApply = true,
                Status = "OK"
            };
        }
        catch (Exception ex)
        {
            return ErrorRow(document, entry, ex.Message);
        }
    }

    private static PatchPreviewRow ErrorRow(PatchDocument document, PatchEntry entry, string status, long fileOffset = -1) => new()
    {
        Index = entry.Index,
        SourceLine = entry.SourceLine,
        Comment = entry.Comment ?? string.Empty,
        AddressKind = document.AddressKind.ToString(),
        AddressHex = "0x" + entry.AddressHex,
        FileOffsetHex = fileOffset >= 0 ? "0x" + fileOffset.ToString("X", CultureInfo.InvariantCulture) : "-",
        FileOffset = fileOffset,
        Length = entry.Bytes.Length,
        NewBytesHex = ToHex(entry.Bytes),
        CanApply = false,
        Status = status
    };

    private static int ComputeFinalChangedBytes(byte[] targetBytes, PatchDocument document, IReadOnlyList<PatchPreviewRow> rows)
    {
        if (rows.Count == 0 || rows.Any(r => !r.CanApply)) return 0;

        var output = (byte[])targetBytes.Clone();
        var entryByIndex = document.Entries.ToDictionary(e => e.Index);
        foreach (var row in rows)
        {
            var entry = entryByIndex[row.Index];
            Buffer.BlockCopy(entry.Bytes, 0, output, checked((int)row.FileOffset), entry.Bytes.Length);
        }

        return CountDifferences(targetBytes, output);
    }

    private static int CountDifferences(byte[] before, byte[] after)
    {
        if (before.Length != after.Length) throw new InvalidOperationException("内部错误：比较的字节数组长度不同。");
        var count = 0;
        for (var i = 0; i < before.Length; i++)
        {
            if (before[i] != after[i]) count++;
        }
        return count;
    }

    private static string CreateBeforeSaveBackup(CczProject project, string filePath)
    {
        var backupRoot = Path.Combine(project.GameRoot, "_CCZModStudio_Backups");
        Directory.CreateDirectory(backupRoot);
        var stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss_fff");
        var backupPath = Path.Combine(backupRoot, $"{stamp}_{Path.GetFileName(filePath)}");
        File.Copy(filePath, backupPath, overwrite: false);
        return backupPath;
    }

    private static string WriteReport(CczProject project, PatchPreviewResult preview, string backupPath, string beforeHash, string afterHash, int changedBytes, int bytesWritten)
    {
        var backupRoot = Path.Combine(project.GameRoot, "_CCZModStudio_Backups");
        Directory.CreateDirectory(backupRoot);
        var stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss_fff");
        var reportPath = Path.Combine(backupRoot, $"{stamp}_PatchReport.txt");

        var lines = new List<string>
        {
            "CCZModStudio Patch Apply Report",
            "CreatedAt=" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
            "PatchSource=" + preview.Document.SourcePath,
            "PatchVersion=" + preview.Document.Version,
            "AddressKind=" + preview.Document.AddressKind,
            "TargetFile=" + preview.TargetFilePath,
            "BackupFile=" + backupPath,
            "BeforeSHA256=" + beforeHash,
            "AfterSHA256=" + afterHash,
            "Entries=" + preview.Rows.Count.ToString(CultureInfo.InvariantCulture),
            "BytesWritten=" + bytesWritten.ToString(CultureInfo.InvariantCulture),
            "ChangedBytes=" + changedBytes.ToString(CultureInfo.InvariantCulture),
            string.Empty,
            "Index\tLine\tAddress\tFileOffset\tLength\tChanged\tComment\tNewBytes"
        };

        lines.AddRange(preview.Rows.Select(r => string.Join('\t',
            r.Index.ToString(CultureInfo.InvariantCulture),
            r.SourceLine.ToString(CultureInfo.InvariantCulture),
            r.AddressHex,
            r.FileOffsetHex,
            r.Length.ToString(CultureInfo.InvariantCulture),
            r.Changed ? "Y" : "N",
            r.Comment.Replace('\t', ' '),
            r.NewBytesHex)));

        File.WriteAllLines(reportPath, lines, Encoding.UTF8);
        return reportPath;
    }

    private static string ComputeSha256(byte[] bytes)
    {
        using var sha = SHA256.Create();
        return Convert.ToHexString(sha.ComputeHash(bytes));
    }

    private static string ToHex(byte[] bytes)
    {
        const int max = 96;
        var hex = BitConverter.ToString(bytes.Take(max).ToArray()).Replace("-", " ");
        return bytes.Length > max ? hex + $" ... (+{bytes.Length - max} bytes)" : hex;
    }
}
