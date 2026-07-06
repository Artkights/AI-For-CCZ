using System.Data;
using System.Globalization;
using CCZModStudio.Models;

namespace CCZModStudio.Core;

public sealed class RoleQuoteMappingService
{
    public const int RetreatQuoteCount = 49;
    public const int CriticalSpecialQuoteCount = 21;
    public const int CriticalSpecialEmptyRoleId = 1024;
    public const int CriticalGenericStartId = 21;
    public const int CriticalGenericGroupSize = 3;
    public const int CriticalGenericTypeCount = 26;
    public const int CriticalSpecialRoleTableOffset = 0x89C30;
    private readonly WriteOperationReportService _reportService = new();

    public RoleRetreatQuoteMapping ResolveRetreatQuote(DataRow roleRow, DataTable retreatQuotes)
    {
        var roleId = ReadInt(roleRow, "ID");
        var fieldValue = ReadInt(roleRow, "撤退台词");
        var quoteRow = roleId >= 0 && roleId < RetreatQuoteCount
            ? TryFindRowById(retreatQuotes, roleId)
            : null;
        return new RoleRetreatQuoteMapping(
            RoleId: roleId,
            FieldValue: fieldValue,
            QuoteId: quoteRow == null ? null : roleId,
            QuoteRow: quoteRow,
            Explanation: quoteRow == null
                ? $"撤退台词：人物行 ID={roleId} 不在 0..48；引擎通常不会从 49 行撤退台词表为该人物取行，字段值={fieldValue} 仅保留为兼容数据。"
                : $"撤退台词：引擎按人物行 ID={roleId} 读取撤退台词表同 ID 行；人物表字段值={fieldValue} 不参与当前 49 行文本定位。");
    }

    public RoleCriticalQuoteMapping ResolveCriticalQuote(CczProject project, DataRow roleRow, DataTable criticalQuotes)
    {
        var roleId = ReadInt(roleRow, "ID");
        var fieldValue = ReadInt(roleRow, "暴击台词");
        var specialQuoteId = FindSpecialCriticalQuoteId(project, roleId);
        if (specialQuoteId is { } specialId)
        {
            var specialRow = TryFindRowById(criticalQuotes, specialId);
            return new RoleCriticalQuoteMapping(
                RoleId: roleId,
                FieldValue: fieldValue,
                QuoteIds: specialRow == null ? Array.Empty<int>() : new[] { specialId },
                QuoteRows: specialRow == null ? Array.Empty<DataRow>() : new[] { specialRow },
                IsSpecialRoleQuote: true,
                Explanation: specialRow == null
                    ? $"暴击台词：Ekd5.exe 特殊人物表命中人物 ID={roleId}，但暴击台词表缺少特殊行 #{specialId}；字段值={fieldValue} 不作为直接文本行。"
                    : $"暴击台词：Ekd5.exe 特殊人物表命中人物 ID={roleId}，使用特殊台词行 #{specialId}；人物表字段值={fieldValue} 不作为直接文本行。");
        }

        var firstId = CriticalGenericStartId + fieldValue * CriticalGenericGroupSize;
        var rows = new List<DataRow>(CriticalGenericGroupSize);
        var ids = new List<int>(CriticalGenericGroupSize);
        for (var id = firstId; id < firstId + CriticalGenericGroupSize; id++)
        {
            var row = TryFindRowById(criticalQuotes, id);
            if (row == null) continue;
            ids.Add(id);
            rows.Add(row);
        }

        return new RoleCriticalQuoteMapping(
            RoleId: roleId,
            FieldValue: fieldValue,
            QuoteIds: ids,
            QuoteRows: rows,
            IsSpecialRoleQuote: false,
            Explanation: rows.Count == 0
                ? $"暴击台词：未命中特殊人物表，字段值={fieldValue} 解释为普通暴击台词类型；应读取行 #{firstId}..#{firstId + 2}，但表内没有可编辑行。"
                : $"暴击台词：未命中特殊人物表，字段值={fieldValue} 解释为普通暴击台词类型；引擎通常在行 #{firstId}..#{firstId + 2} 中随机显示。");
    }

    public RoleCriticalQuoteMapping ResolveCriticalQuoteSelection(DataRow roleRow, DataTable criticalQuotes, RoleCriticalQuoteSelection selection)
    {
        var roleId = ReadInt(roleRow, "ID");
        var fieldValue = ReadInt(roleRow, "暴击台词");
        return selection.Mode switch
        {
            RoleCriticalQuoteMode.Special => ResolveSpecialCriticalQuoteSelection(roleId, fieldValue, criticalQuotes, selection.Value),
            RoleCriticalQuoteMode.Generic => ResolveGenericCriticalQuoteSelection(roleId, selection.Value, criticalQuotes),
            _ => throw new InvalidOperationException("未知的暴击台词分配模式。")
        };
    }

    public static int FirstGenericCriticalQuoteId(int genericType)
    {
        if (genericType < 0 || genericType >= CriticalGenericTypeCount)
        {
            throw new ArgumentOutOfRangeException(nameof(genericType), $"普通暴击台词类型必须在 0..{CriticalGenericTypeCount - 1}。");
        }

        return CriticalGenericStartId + genericType * CriticalGenericGroupSize;
    }

    public static IReadOnlyList<int> GenericCriticalQuoteIds(int genericType)
    {
        var firstId = FirstGenericCriticalQuoteId(genericType);
        return Enumerable.Range(firstId, CriticalGenericGroupSize).ToArray();
    }

    public RoleCriticalSpecialSlotsSaveResult? SaveSpecialCriticalRoleIds(CczProject project, IReadOnlyList<int> newRoleIds)
    {
        if (newRoleIds.Count != CriticalSpecialQuoteCount)
        {
            throw new InvalidOperationException($"特殊暴击人物表必须正好包含 {CriticalSpecialQuoteCount} 个 Int32 人物 ID。");
        }

        ProjectVersionGuardService.EnsureCoreFileCompatibleForWrite(project, "Ekd5.exe");

        var exePath = project.ResolveGameFile("Ekd5.exe");
        if (!File.Exists(exePath))
        {
            throw new FileNotFoundException("找不到 Ekd5.exe，无法保存特殊暴击人物表。", exePath);
        }

        var original = File.ReadAllBytes(exePath);
        var requiredLength = CriticalSpecialRoleTableOffset + CriticalSpecialQuoteCount * 4;
        if (original.Length < requiredLength)
        {
            throw new InvalidOperationException($"Ekd5.exe 长度不足，无法写入 0x{CriticalSpecialRoleTableOffset:X} 的特殊暴击人物表。");
        }

        var oldRoleIds = ReadSpecialCriticalRoleIdsFromBytes(original);
        if (oldRoleIds.SequenceEqual(newRoleIds))
        {
            return null;
        }

        var output = (byte[])original.Clone();
        using (var stream = new MemoryStream(output, writable: true))
        using (var writer = new BinaryWriter(stream))
        {
            stream.Position = CriticalSpecialRoleTableOffset;
            foreach (var roleId in newRoleIds)
            {
                writer.Write(roleId);
            }
        }

        var backupPath = CreateBeforeSaveBackup(project, exePath);
        File.WriteAllBytes(exePath, output);

        var reread = ReadSpecialCriticalRoleIds(project);
        if (!reread.SequenceEqual(newRoleIds))
        {
            throw new InvalidOperationException("特殊暴击人物表保存后复读失败；请使用写入前备份恢复 Ekd5.exe。");
        }

        var changedBytes = CountDifferences(original, output);
        var reportPath = WriteSpecialCriticalSlotsReport(project, exePath, backupPath, original, output, oldRoleIds, newRoleIds, changedBytes);
        return new RoleCriticalSpecialSlotsSaveResult
        {
            FilePath = exePath,
            BackupPath = backupPath,
            ReportJsonPath = reportPath,
            ChangedBytes = changedBytes,
            OldRoleIds = oldRoleIds,
            NewRoleIds = newRoleIds.ToArray()
        };
    }

    public int? FindSpecialCriticalQuoteId(CczProject project, int roleId)
    {
        if (roleId < 0) return null;
        var exePath = project.ResolveGameFile("Ekd5.exe");
        if (!File.Exists(exePath)) return null;

        try
        {
            using var stream = File.OpenRead(exePath);
            if (stream.Length < CriticalSpecialRoleTableOffset + CriticalSpecialQuoteCount * 4L)
            {
                return null;
            }

            using var reader = new BinaryReader(stream);
            stream.Position = CriticalSpecialRoleTableOffset;
            for (var quoteId = 0; quoteId < CriticalSpecialQuoteCount; quoteId++)
            {
                var specialRoleId = reader.ReadInt32();
                if (specialRoleId == roleId)
                {
                    return quoteId;
                }
            }
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }

        return null;
    }

    public IReadOnlyList<int> ReadSpecialCriticalRoleIds(CczProject project)
    {
        var exePath = project.ResolveGameFile("Ekd5.exe");
        if (!File.Exists(exePath)) return Array.Empty<int>();

        try
        {
            using var stream = File.OpenRead(exePath);
            if (stream.Length < CriticalSpecialRoleTableOffset + CriticalSpecialQuoteCount * 4L)
            {
                return Array.Empty<int>();
            }

            using var memory = new MemoryStream();
            stream.CopyTo(memory);
            return ReadSpecialCriticalRoleIdsFromBytes(memory.ToArray());
        }
        catch (IOException)
        {
            return Array.Empty<int>();
        }
        catch (UnauthorizedAccessException)
        {
            return Array.Empty<int>();
        }
    }

    private RoleCriticalQuoteMapping ResolveSpecialCriticalQuoteSelection(int roleId, int fieldValue, DataTable criticalQuotes, int specialQuoteId)
    {
        if (specialQuoteId < 0 || specialQuoteId >= CriticalSpecialQuoteCount)
        {
            throw new ArgumentOutOfRangeException(nameof(specialQuoteId), $"特殊暴击槽必须在 0..{CriticalSpecialQuoteCount - 1}。");
        }

        var specialRow = TryFindRowById(criticalQuotes, specialQuoteId);
        return new RoleCriticalQuoteMapping(
            RoleId: roleId,
            FieldValue: fieldValue,
            QuoteIds: specialRow == null ? Array.Empty<int>() : new[] { specialQuoteId },
            QuoteRows: specialRow == null ? Array.Empty<DataRow>() : new[] { specialRow },
            IsSpecialRoleQuote: true,
            Explanation: specialRow == null
                ? $"暴击台词：当前选择特殊人物台词槽 #{specialQuoteId}，但暴击台词表缺少该行。"
                : $"暴击台词：当前选择特殊人物台词槽 #{specialQuoteId}；保存后 Ekd5.exe 特殊人物表会把人物 ID={roleId} 分配到该槽。");
    }

    private RoleCriticalQuoteMapping ResolveGenericCriticalQuoteSelection(int roleId, int genericType, DataTable criticalQuotes)
    {
        var firstId = FirstGenericCriticalQuoteId(genericType);
        var rows = new List<DataRow>(CriticalGenericGroupSize);
        var ids = new List<int>(CriticalGenericGroupSize);
        for (var id = firstId; id < firstId + CriticalGenericGroupSize; id++)
        {
            var row = TryFindRowById(criticalQuotes, id);
            if (row == null) continue;
            ids.Add(id);
            rows.Add(row);
        }

        return new RoleCriticalQuoteMapping(
            RoleId: roleId,
            FieldValue: genericType,
            QuoteIds: ids,
            QuoteRows: rows,
            IsSpecialRoleQuote: false,
            Explanation: rows.Count == 0
                ? $"暴击台词：当前选择普通类型 {genericType}，应读取行 #{firstId}..#{firstId + 2}，但表内没有可编辑行。"
                : $"暴击台词：当前选择普通类型 {genericType}；保存后人物表暴击台词字段写为 {genericType}，引擎通常在行 #{firstId}..#{firstId + 2} 中随机显示。");
    }

    private static IReadOnlyList<int> ReadSpecialCriticalRoleIdsFromBytes(byte[] bytes)
    {
        if (bytes.Length < CriticalSpecialRoleTableOffset + CriticalSpecialQuoteCount * 4)
        {
            return Array.Empty<int>();
        }

        using var stream = new MemoryStream(bytes);
        using var reader = new BinaryReader(stream);
        stream.Position = CriticalSpecialRoleTableOffset;
        var ids = new List<int>(CriticalSpecialQuoteCount);
        for (var i = 0; i < CriticalSpecialQuoteCount; i++)
        {
            ids.Add(reader.ReadInt32());
        }

        return ids;
    }

    private string WriteSpecialCriticalSlotsReport(
        CczProject project,
        string exePath,
        string backupPath,
        byte[] original,
        byte[] output,
        IReadOnlyList<int> oldRoleIds,
        IReadOnlyList<int> newRoleIds,
        int changedBytes)
    {
        var targetRelative = WriteOperationReportService.ToProjectRelativePath(project, exePath);
        var report = new WriteOperationReport
        {
            OperationKind = "特殊暴击人物表写入",
            SourceAction = "角色设定页保存特殊暴击人物分配前自动备份",
            ProjectRoot = project.GameRoot,
            TargetRelativePath = targetRelative,
            TargetPath = exePath,
            BackupPath = backupPath,
            BeforeSha256 = WriteOperationReportService.ComputeSha256(original),
            AfterSha256 = WriteOperationReportService.ComputeSha256(output),
            ChangedBytes = changedBytes,
            Summary = $"写入 Ekd5.exe 特殊暴击人物表 21 个 Int32，实际变化 {changedBytes:N0} 字节。",
            SafetyNotes = "该写入只覆盖 0x89C30 起 84 字节，不插入、不删除、不扩展 EXE；保存后已复读 21 个人物 ID。",
            FormatCheckSummary = $"特殊暴击人物表偏移 0x{CriticalSpecialRoleTableOffset:X}，长度 {CriticalSpecialQuoteCount * 4} 字节。",
            RiskSummary = "特殊暴击人物表决定暴击台词 #0..#20 的人物归属；如分配错误，可用写入前备份恢复 Ekd5.exe。",
            Changes = oldRoleIds.Select((oldId, index) => new WriteOperationChange
            {
                Category = "特殊暴击槽",
                TableName = targetRelative,
                RowIndex = index,
                ColumnName = $"特殊槽 #{index}",
                OffsetHex = $"0x{CriticalSpecialRoleTableOffset + index * 4:X}",
                ByteLength = 4,
                OldValue = oldId.ToString(CultureInfo.InvariantCulture),
                NewValue = newRoleIds[index].ToString(CultureInfo.InvariantCulture),
                Annotation = oldId == newRoleIds[index]
                    ? $"特殊槽 #{index} 保持人物 ID={oldId}。"
                    : $"特殊槽 #{index} 从人物 ID={oldId} 改为 ID={newRoleIds[index]}。"
            }).ToList(),
            Metadata =
            {
                ["SpecialCriticalOffset"] = $"0x{CriticalSpecialRoleTableOffset:X}",
                ["SpecialCriticalSlotCount"] = CriticalSpecialQuoteCount.ToString(CultureInfo.InvariantCulture),
                ["EmptyRoleId"] = CriticalSpecialEmptyRoleId.ToString(CultureInfo.InvariantCulture)
            }
        };

        return _reportService.WriteJsonReport(report, backupPath);
    }

    private static int ReadInt(DataRow row, string columnName)
        => Convert.ToInt32(row[columnName], CultureInfo.InvariantCulture);

    private static DataRow? TryFindRowById(DataTable table, int id)
    {
        foreach (DataRow row in table.Rows)
        {
            if (Convert.ToInt32(row["ID"], CultureInfo.InvariantCulture) == id)
            {
                return row;
            }
        }

        return null;
    }

    private static string CreateBeforeSaveBackup(CczProject project, string filePath)
    {
        var backupRoot = Path.Combine(project.GameRoot, "_CCZModStudio_Backups");
        Directory.CreateDirectory(backupRoot);
        var stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss_fff", CultureInfo.InvariantCulture);
        var backupPath = Path.Combine(backupRoot, $"{stamp}_{Path.GetFileName(filePath)}");
        File.Copy(filePath, backupPath, overwrite: false);
        return backupPath;
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
}

public enum RoleCriticalQuoteMode
{
    Special,
    Generic
}

public sealed record RoleCriticalQuoteSelection(RoleCriticalQuoteMode Mode, int Value);

public sealed record RoleRetreatQuoteMapping(
    int RoleId,
    int FieldValue,
    int? QuoteId,
    DataRow? QuoteRow,
    string Explanation);

public sealed record RoleCriticalQuoteMapping(
    int RoleId,
    int FieldValue,
    IReadOnlyList<int> QuoteIds,
    IReadOnlyList<DataRow> QuoteRows,
    bool IsSpecialRoleQuote,
    string Explanation);

public sealed class RoleCriticalSpecialSlotsSaveResult
{
    public required string FilePath { get; init; }
    public int ChangedBytes { get; init; }
    public required string BackupPath { get; init; }
    public string ReportJsonPath { get; init; } = string.Empty;
    public IReadOnlyList<int> OldRoleIds { get; init; } = Array.Empty<int>();
    public IReadOnlyList<int> NewRoleIds { get; init; } = Array.Empty<int>();
}
