using System.Globalization;
using CCZModStudio.Core;
using CCZModStudio.Models;

namespace CCZModStudio.Formats;

public sealed class HexTableWriter
{
    private readonly HexTableReader _reader = new();
    private readonly WriteOperationReportService _reportService = new();

    public TableSaveResult SaveToTestCopy(CczProject project, HexTableDefinition table, System.Data.DataTable data)
        => Save(project, table, data);

    public TableSaveResult Save(CczProject project, HexTableDefinition table, System.Data.DataTable data)
    {
        ProjectVersionGuardService.EnsureTableCompatibleForWrite(project, table);

        var validation = _reader.Validate(project, table);
        if (!validation.IsUsable)
        {
            throw new InvalidOperationException("表结构或目标文件不可写，请先查看诊断信息。 ");
        }

        if (table.ReadOnly)
        {
            throw new InvalidOperationException("该表被标记为 ReadOnly，当前版本不允许写入。 ");
        }

        if (data.Rows.Count != table.RowCount)
        {
            throw new InvalidOperationException($"行数不匹配：表定义 {table.RowCount} 行，当前数据 {data.Rows.Count} 行。 ");
        }

        var filePath = validation.FilePath;
        var backupPath = CreateBeforeSaveBackup(project, filePath);
        var original = File.ReadAllBytes(filePath);
        var output = (byte[])original.Clone();
        var changes = new List<WriteOperationChange>();

        var changedBytes = 0;
        for (var rowIndex = 0; rowIndex < table.RowCount; rowIndex++)
        {
            var rowOffset = checked((int)(table.DataPos + ((long)rowIndex * table.RowSize)));
            var rowBuffer = new byte[table.RowSize];
            Buffer.BlockCopy(original, rowOffset, rowBuffer, 0, table.RowSize);

            var fieldOffset = 0;
            var dataColumnIndex = 1; // column 0 is synthetic ID
            foreach (var field in table.Fields)
            {
                if (!field.ConsumesBytes)
                {
                    dataColumnIndex++;
                    continue;
                }

                var value = data.Rows[rowIndex][dataColumnIndex];
                var shouldEncode = IsCellChanged(data.Rows[rowIndex], dataColumnIndex);
                var currentFieldOffset = fieldOffset;
                if (shouldEncode)
                {
                    var encoded = EncodeField(field, value);
                    if (encoded.Length != field.Size)
                    {
                        throw new InvalidOperationException($"字段 {field.ColumnName} 编码长度 {encoded.Length} 与定义长度 {field.Size} 不一致。 ");
                    }

                    Buffer.BlockCopy(encoded, 0, rowBuffer, fieldOffset, field.Size);
                    changes.Add(BuildFieldChange(table, data.Rows[rowIndex], rowIndex, dataColumnIndex, field, rowOffset + currentFieldOffset));
                }
                fieldOffset += field.Size;
                dataColumnIndex++;
            }

            for (var i = 0; i < table.RowSize; i++)
            {
                if (output[rowOffset + i] != rowBuffer[i])
                {
                    changedBytes++;
                    output[rowOffset + i] = rowBuffer[i];
                }
            }
        }

        File.WriteAllBytes(filePath, output);
        var reportJsonPath = WriteStructuredReport(project, table, filePath, backupPath, original, output, changedBytes, changes);

        return new TableSaveResult
        {
            Table = table,
            FilePath = filePath,
            RowsWritten = table.RowCount,
            ChangedBytes = changedBytes,
            BackupPath = backupPath,
            ReportJsonPath = reportJsonPath
        };
    }

    private string WriteStructuredReport(
        CczProject project,
        HexTableDefinition table,
        string filePath,
        string backupPath,
        byte[] original,
        byte[] output,
        int changedBytes,
        List<WriteOperationChange> changes)
    {
        var targetRelative = WriteOperationReportService.ToProjectRelativePath(project, filePath);
        var report = new WriteOperationReport
        {
            OperationKind = "数据表保存",
            SourceAction = "数据表保存前自动备份",
            ProjectRoot = project.GameRoot,
            TargetRelativePath = targetRelative,
            TargetPath = filePath,
            BackupPath = backupPath,
            BeforeSha256 = WriteOperationReportService.ComputeSha256(original),
            AfterSha256 = WriteOperationReportService.ComputeSha256(output),
            ChangedBytes = changedBytes,
            Summary = $"保存数据表“{table.TableName}”，目标 {targetRelative}，字段改动 {changes.Count} 项，字节改动 {changedBytes:N0}。",
            SafetyNotes = project.IsTestCopy
                ? "该报告由测试副本写入流程自动生成。还原时请使用备份历史/回滚页的预览和再备份流程。"
                : "该报告由当前 MOD 项目直接保存流程自动生成。保存前已备份目标文件；如需回退，请使用备份文件或备份历史功能。",
            Changes = changes,
            Metadata =
            {
                ["TableName"] = table.TableName,
                ["Version"] = table.Version,
                ["RowCount"] = table.RowCount.ToString(CultureInfo.InvariantCulture),
                ["RowSize"] = table.RowSize.ToString(CultureInfo.InvariantCulture),
                ["DataPos"] = "0x" + table.DataPos.ToString("X", CultureInfo.InvariantCulture)
            }
        };
        return _reportService.WriteJsonReport(report, backupPath);
    }

    private static WriteOperationChange BuildFieldChange(
        HexTableDefinition table,
        System.Data.DataRow row,
        int rowIndex,
        int columnIndex,
        HexFieldDefinition field,
        long absoluteOffset)
    {
        var oldValue = row.HasVersion(System.Data.DataRowVersion.Original)
            ? Convert.ToString(row[columnIndex, System.Data.DataRowVersion.Original], CultureInfo.InvariantCulture) ?? string.Empty
            : string.Empty;
        var newValue = Convert.ToString(row[columnIndex, System.Data.DataRowVersion.Current], CultureInfo.InvariantCulture) ?? string.Empty;
        return new WriteOperationChange
        {
            Category = "表格字段",
            TableName = table.TableName,
            RowIndex = rowIndex,
            ColumnName = field.ColumnName,
            OffsetHex = "0x" + absoluteOffset.ToString("X", CultureInfo.InvariantCulture),
            ByteLength = field.Size,
            OldValue = oldValue,
            NewValue = newValue,
            Annotation = $"表“{table.TableName}”第 {rowIndex} 行字段“{field.ColumnName}”从“{oldValue}”改为“{newValue}”。"
        };
    }

    private static bool IsCellChanged(System.Data.DataRow row, int columnIndex)
    {
        if (row.RowState == System.Data.DataRowState.Added) return true;
        if (row.RowState == System.Data.DataRowState.Unchanged) return false;
        if (!row.HasVersion(System.Data.DataRowVersion.Original)) return true;

        var original = row[columnIndex, System.Data.DataRowVersion.Original];
        var current = row[columnIndex, System.Data.DataRowVersion.Current];
        return !Equals(Convert.ToString(original, CultureInfo.InvariantCulture), Convert.ToString(current, CultureInfo.InvariantCulture));
    }

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

    private static byte[] EncodeField(HexFieldDefinition field, object? value)
    {
        var text = Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty;
        return field.Kind switch
        {
            HexFieldKind.UInt8 => new[] { checked((byte)ParseInteger(text, byte.MinValue, byte.MaxValue, field.ColumnName)) },
            HexFieldKind.UInt16 => BitConverter.GetBytes(checked((ushort)ParseInteger(text, ushort.MinValue, ushort.MaxValue, field.ColumnName))),
            HexFieldKind.UInt32 => BitConverter.GetBytes(checked((uint)ParseUnsignedInteger(text, uint.MinValue, uint.MaxValue, field.ColumnName))),
            HexFieldKind.FixedString => EncodingService.EncodeFixedString(text, field.Size),
            HexFieldKind.RawBytes => ParseRawBytes(text, field.Size, field.ColumnName),
            _ => Array.Empty<byte>()
        };
    }

    private static long ParseInteger(string text, long min, long max, string fieldName)
    {
        text = text.Trim();
        if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            var parsedHex = Convert.ToInt64(text[2..], 16);
            if (parsedHex < min || parsedHex > max) throw OutOfRange(fieldName, text, min, max);
            return parsedHex;
        }

        if (!long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
        {
            throw new InvalidOperationException($"字段 {fieldName} 的值不是有效整数：{text}");
        }
        if (value < min || value > max) throw OutOfRange(fieldName, text, min, max);
        return value;
    }

    private static ulong ParseUnsignedInteger(string text, ulong min, ulong max, string fieldName)
    {
        text = text.Trim();
        ulong value;
        if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            value = Convert.ToUInt64(text[2..], 16);
        }
        else if (!ulong.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value))
        {
            throw new InvalidOperationException($"字段 {fieldName} 的值不是有效无符号整数：{text}");
        }

        if (value < min || value > max)
        {
            throw new InvalidOperationException($"字段 {fieldName} 的值超出范围：{text}，允许 {min}..{max}");
        }
        return value;
    }

    private static Exception OutOfRange(string fieldName, string text, long min, long max) =>
        new InvalidOperationException($"字段 {fieldName} 的值超出范围：{text}，允许 {min}..{max}");

    private static byte[] ParseRawBytes(string text, int expectedLength, string fieldName)
    {
        var clean = text.Replace("-", " ").Replace(",", " ").Replace("0x", "", StringComparison.OrdinalIgnoreCase);
        var parts = clean.Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != expectedLength)
        {
            throw new InvalidOperationException($"字段 {fieldName} 需要 {expectedLength} 个十六进制字节，当前 {parts.Length} 个。 ");
        }

        var bytes = new byte[expectedLength];
        for (var i = 0; i < parts.Length; i++)
        {
            bytes[i] = byte.Parse(parts[i], NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        }
        return bytes;
    }
}
