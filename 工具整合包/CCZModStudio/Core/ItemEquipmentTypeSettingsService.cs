using System.Data;
using System.Globalization;
using CCZModStudio.Formats;
using CCZModStudio.Models;

namespace CCZModStudio.Core;

public sealed class ItemEquipmentTypeSettingsService
{
    public const string TargetFileName = "Ekd5.exe";
    public const int NameTextByteLength = 4;
    public const int NameSlotStride = 5;
    public const int DisplayHiddenValue = 0xFF;
    private const long NameTableBaseOffset = 0x8AC70;
    private const long DisplayTableBaseOffset = 0x81827;

    private static readonly IReadOnlyList<Definition> Definitions =
    [
        new(0, 0x00, "物理武器 1", 0, 0x00),
        new(1, 0x05, "物理武器 2", 1, 0x03),
        new(2, 0x0A, "物理武器 3", 2, 0x06),
        new(3, 0x0F, "物理武器 4", 3, 0x09),
        new(4, 0x14, "物理武器 5", 4, 0x0C),
        new(5, 0x19, "物理武器 6", 5, 0x0F),
        new(6, 0x1E, "物理武器 7", 6, 0x12),
        new(7, 0x23, "策略武器 1", 7, 0x15),
        new(8, 0x28, "策略武器 2", 8, 0x18),
        new(9, 0x2D, "双修武器 1", 9, 0x1B),
        new(10, 0x32, "护具 1", 10, 0x46),
        new(11, 0x37, "护具 2", 11, 0x49),
        new(12, 0x3C, "护具 3", 12, 0x4C),
        new(13, 0x41, "辅助宝物", null, null),
        new(14, 0x46, "消耗品道具", null, null)
    ];

    private readonly HexTableReader _reader = new();
    private readonly HexTableWriter _writer = new();
    private readonly ProjectEquipmentTypeProfileService _profileService = new();
    private readonly WriteOperationReportService _reportService = new();

    public ItemEquipmentTypeSettingsDocument Load(CczProject project, IReadOnlyList<HexTableDefinition> tables)
    {
        var context = BuildContext(project, tables);
        var exeBytes = File.ReadAllBytes(context.ExePath);
        var rows = Definitions.Select(definition => BuildRow(definition, exeBytes, context)).ToArray();
        return new ItemEquipmentTypeSettingsDocument
        {
            TargetFile = TargetFileName,
            Rows = rows,
            Diagnostics = context.Profile.Diagnostics
        };
    }

    public IReadOnlyList<ItemEquipmentTypePreviewChange> Preview(
        CczProject project,
        IReadOnlyList<HexTableDefinition> tables,
        ItemEquipmentTypeSettingsUpdate update)
    {
        var context = BuildContext(project, tables);
        var exeBytes = File.ReadAllBytes(context.ExePath);
        var changes = BuildExeChanges(context, exeBytes, update).ToList();
        changes.AddRange(BuildJobPermissionChanges(context, update));
        return changes;
    }

    public ItemEquipmentTypeSettingsSaveResult Save(
        CczProject project,
        IReadOnlyList<HexTableDefinition> tables,
        ItemEquipmentTypeSettingsUpdate update)
    {
        ProjectVersionGuardService.EnsureCoreFileCompatibleForWrite(project, TargetFileName);

        var context = BuildContext(project, tables);
        var exeOriginal = File.ReadAllBytes(context.ExePath);
        var exeChanges = BuildExeChanges(context, exeOriginal, update).ToArray();
        var permissionChanges = BuildJobPermissionChanges(context, update).ToArray();
        if (exeChanges.Length == 0 && permissionChanges.Length == 0)
        {
            return new ItemEquipmentTypeSettingsSaveResult
            {
                Summary = "装备类型没有检测到需要保存的改动。"
            };
        }

        string exeBackup = string.Empty;
        string exeReport = string.Empty;
        var changedBytes = 0;
        if (exeChanges.Length > 0)
        {
            exeBackup = CreateBeforeSaveBackup(project, context.ExePath);
            var exeOutput = (byte[])exeOriginal.Clone();
            foreach (var change in exeChanges)
            {
                var offset = checked((int)ParseHex(change.OffsetHex));
                var newBytes = Convert.FromHexString(change.NewBytesHex);
                Buffer.BlockCopy(newBytes, 0, exeOutput, offset, newBytes.Length);
            }

            File.WriteAllBytes(context.ExePath, exeOutput);
            VerifyExeWrites(context.ExePath, exeChanges);
            changedBytes += CountChangedBytes(exeOriginal, exeOutput);
            exeReport = WriteExeReport(project, context.ExePath, exeBackup, exeOriginal, exeOutput, exeChanges);
        }

        var tableSaves = new List<TableSaveResult>();
        if (ApplyJobPermissionUpdate(context.JobGrowthRead.Data, context.Profile.JobPermissionSlots, update.JobPermissions))
        {
            var save = _writer.Save(project, context.JobGrowthRead.Table, context.JobGrowthRead.Data);
            tableSaves.Add(save);
            VerifyJobPermissionWrites(project, tables, context.Profile.JobPermissionSlots, update.JobPermissions);
            changedBytes += save.ChangedBytes;
        }

        var allChanges = exeChanges.Concat(permissionChanges).ToArray();
        return new ItemEquipmentTypeSettingsSaveResult
        {
            ChangedFieldCount = allChanges.Length,
            ChangedBytes = changedBytes,
            ExeBackupPath = exeBackup,
            ExeReportJsonPath = exeReport,
            TableSaves = tableSaves,
            Changes = allChanges,
            Summary = $"已保存装备类型 {allChanges.Length} 项，变更 {changedBytes} 字节，备份已生成。"
        };
    }

    public IReadOnlyList<object> GetDefinitions()
        => Definitions.Select(definition => new
        {
            definition.RowIndex,
            definition.NameEntryId,
            definition.Description,
            NameOffsetHex = FormatOffset(GetNameOffset(definition)),
            DisplayOffsetHex = definition.DisplayIndex.HasValue ? FormatOffset(GetDisplayOffset(definition)) : string.Empty,
            definition.NormalDisplayValue
        }).Cast<object>().ToArray();

    private ItemEquipmentTypeRow BuildRow(Definition definition, byte[] exeBytes, Context context)
    {
        EnsureRange(context.ExePath, GetNameOffset(definition), NameTextByteLength);
        var name = EncodingService.DecodeFixedString(exeBytes.AsSpan(checked((int)GetNameOffset(definition)), NameTextByteLength));
        var isVisible = true;
        if (definition.DisplayIndex.HasValue)
        {
            EnsureRange(context.ExePath, GetDisplayOffset(definition), 1);
            isVisible = exeBytes[checked((int)GetDisplayOffset(definition))] != DisplayHiddenValue;
        }

        var permissions = BuildJobPermissions(definition, context).ToArray();
        return new ItemEquipmentTypeRow
        {
            RowIndex = definition.RowIndex,
            NameEntryId = definition.NameEntryId,
            Description = definition.Description,
            Name = name,
            HasDisplayToggle = definition.DisplayIndex.HasValue,
            IsVisible = isVisible,
            DisplayIndex = definition.DisplayIndex,
            NormalDisplayValue = definition.NormalDisplayValue,
            EquipmentSummary = BuildPermissionSummary(permissions),
            JobPermissions = permissions
        };
    }

    private IEnumerable<ItemEquipmentTypeJobPermission> BuildJobPermissions(Definition definition, Context context)
    {
        if (!definition.DisplayIndex.HasValue)
        {
            yield break;
        }

        var firstSlot = definition.RowIndex * 2;
        var secondSlot = firstSlot + 1;
        var slots = context.Profile.JobPermissionSlots;
        if (firstSlot >= slots.Count || secondSlot >= slots.Count)
        {
            yield break;
        }

        var firstColumn = slots[firstSlot].StorageColumnName;
        var secondColumn = slots[secondSlot].StorageColumnName;
        foreach (DataRow row in context.JobGrowthRead.Data.Rows)
        {
            var jobId = Convert.ToInt32(row["ID"], CultureInfo.InvariantCulture);
            var seriesId = MapJobIdToSeriesId(jobId);
            var firstAllowed = row.Table.Columns.Contains(firstColumn) &&
                               Convert.ToInt32(row[firstColumn], CultureInfo.InvariantCulture) != 0;
            var secondAllowed = row.Table.Columns.Contains(secondColumn) &&
                                Convert.ToInt32(row[secondColumn], CultureInfo.InvariantCulture) != 0;
            var state = firstAllowed == secondAllowed
                ? firstAllowed ? ItemEquipmentPermissionState.Checked : ItemEquipmentPermissionState.Unchecked
                : ItemEquipmentPermissionState.Indeterminate;
            yield return new ItemEquipmentTypeJobPermission
            {
                RowIndex = definition.RowIndex,
                JobId = jobId,
                SeriesId = seriesId,
                SeriesName = context.JobSeriesNames.TryGetValue(seriesId, out var seriesName) ? seriesName : string.Empty,
                JobName = Convert.ToString(row["名称"], CultureInfo.InvariantCulture) ?? string.Empty,
                State = state
            };
        }
    }

    private static int MapJobIdToSeriesId(int jobId)
        => jobId < 60 ? jobId / 3 : 20 + (jobId - 60);

    private IEnumerable<ItemEquipmentTypePreviewChange> BuildExeChanges(
        Context context,
        byte[] exeBytes,
        ItemEquipmentTypeSettingsUpdate update)
    {
        foreach (var pair in update.Names)
        {
            var definition = FindDefinition(pair.Key);
            var offset = GetNameOffset(definition);
            EnsureRange(context.ExePath, offset, NameTextByteLength);
            var oldBytes = ReadBytes(exeBytes, offset, NameTextByteLength);
            var newBytes = ParseName(pair.Value, definition);
            if (oldBytes.SequenceEqual(newBytes)) continue;
            yield return BuildChange(
                "名称",
                definition,
                null,
                definition.Description,
                EncodingService.DecodeFixedString(oldBytes),
                pair.Value,
                oldBytes,
                newBytes,
                TargetFileName,
                offset);
        }

        foreach (var pair in update.Visibility)
        {
            var definition = FindDefinition(pair.Key);
            if (!definition.DisplayIndex.HasValue || !definition.NormalDisplayValue.HasValue)
            {
                throw new InvalidOperationException($"{definition.Description} 没有显示开关地址。");
            }

            var offset = GetDisplayOffset(definition);
            EnsureRange(context.ExePath, offset, 1);
            var oldBytes = ReadBytes(exeBytes, offset, 1);
            var newValue = pair.Value ? definition.NormalDisplayValue.Value : DisplayHiddenValue;
            var newBytes = new[] { checked((byte)newValue) };
            if (oldBytes.SequenceEqual(newBytes)) continue;
            yield return BuildChange(
                "显示",
                definition,
                null,
                definition.Description,
                oldBytes[0] == DisplayHiddenValue ? "隐藏" : "显示",
                pair.Value ? "显示" : "隐藏",
                oldBytes,
                newBytes,
                TargetFileName,
                offset);
        }
    }

    private IReadOnlyList<ItemEquipmentTypePreviewChange> BuildJobPermissionChanges(
        Context context,
        ItemEquipmentTypeSettingsUpdate update)
    {
        var changes = new List<ItemEquipmentTypePreviewChange>();
        foreach (var (rowIndex, jobValues) in update.JobPermissions)
        {
            var definition = FindDefinition(rowIndex);
            if (!definition.DisplayIndex.HasValue) continue;
            var firstSlot = definition.RowIndex * 2;
            var secondSlot = firstSlot + 1;
            if (firstSlot >= context.Profile.JobPermissionSlots.Count || secondSlot >= context.Profile.JobPermissionSlots.Count)
            {
                throw new InvalidOperationException($"{definition.Description} 无法映射到兵种可装备槽位。");
            }

            var firstColumn = context.Profile.JobPermissionSlots[firstSlot].StorageColumnName;
            var secondColumn = context.Profile.JobPermissionSlots[secondSlot].StorageColumnName;
            foreach (var (jobId, allowed) in jobValues)
            {
                var row = FindRowById(context.JobGrowthRead.Data, jobId);
                var oldFirst = ReadPermissionValue(row, firstColumn);
                var oldSecond = ReadPermissionValue(row, secondColumn);
                var newValue = allowed ? 1 : 0;
                if (oldFirst == newValue && oldSecond == newValue) continue;
                var jobName = Convert.ToString(row["名称"], CultureInfo.InvariantCulture) ?? string.Empty;
                changes.Add(new ItemEquipmentTypePreviewChange
                {
                    Area = "可装备部队",
                    RowIndex = rowIndex,
                    JobId = jobId,
                    DisplayName = $"{definition.Description} / {jobName}",
                    OldValue = FormatPermissionState(oldFirst, oldSecond),
                    NewValue = allowed ? "允许" : "禁止",
                    TargetFile = context.JobGrowthRead.Table.FileName,
                    OffsetHex = string.Empty,
                    ByteLength = 2
                });
            }
        }

        return changes;
    }

    private static bool ApplyJobPermissionUpdate(
        DataTable data,
        IReadOnlyList<JobEquipmentPermissionSlotDefinition> slots,
        IReadOnlyDictionary<int, Dictionary<int, bool>> updates)
    {
        var changed = false;
        foreach (var (rowIndex, jobValues) in updates)
        {
            var definition = FindDefinition(rowIndex);
            if (!definition.DisplayIndex.HasValue) continue;
            var firstSlot = definition.RowIndex * 2;
            var secondSlot = firstSlot + 1;
            if (firstSlot >= slots.Count || secondSlot >= slots.Count)
            {
                throw new InvalidOperationException($"{definition.Description} 无法映射到兵种可装备槽位。");
            }

            var firstColumn = slots[firstSlot].StorageColumnName;
            var secondColumn = slots[secondSlot].StorageColumnName;
            foreach (var (jobId, allowed) in jobValues)
            {
                var row = FindRowById(data, jobId);
                var newValue = allowed ? 1 : 0;
                if (SetPermissionValue(row, firstColumn, newValue)) changed = true;
                if (SetPermissionValue(row, secondColumn, newValue)) changed = true;
            }
        }

        return changed;
    }

    private void VerifyJobPermissionWrites(
        CczProject project,
        IReadOnlyList<HexTableDefinition> tables,
        IReadOnlyList<JobEquipmentPermissionSlotDefinition> slots,
        IReadOnlyDictionary<int, Dictionary<int, bool>> updates)
    {
        if (updates.Count == 0) return;
        var table = HexTableNameResolver.ResolveForProject(project, tables, "6.5-4-2 兵种成长");
        var reread = _reader.Read(project, table, tables);
        foreach (var (rowIndex, jobValues) in updates)
        {
            var definition = FindDefinition(rowIndex);
            if (!definition.DisplayIndex.HasValue) continue;
            var firstSlot = definition.RowIndex * 2;
            var secondSlot = firstSlot + 1;
            if (firstSlot >= slots.Count || secondSlot >= slots.Count) continue;

            var firstColumn = slots[firstSlot].StorageColumnName;
            var secondColumn = slots[secondSlot].StorageColumnName;
            foreach (var (jobId, allowed) in jobValues)
            {
                var row = FindRowById(reread.Data, jobId);
                var expected = allowed ? 1 : 0;
                if (ReadPermissionValue(row, firstColumn) != expected ||
                    ReadPermissionValue(row, secondColumn) != expected)
                {
                    throw new InvalidOperationException($"装备类型可装备部队复读校验失败：{definition.Description} / ID={jobId}。");
                }
            }
        }
    }

    private Context BuildContext(CczProject project, IReadOnlyList<HexTableDefinition> tables)
    {
        var exePath = project.ResolveGameFile(TargetFileName);
        if (!File.Exists(exePath))
        {
            throw new FileNotFoundException("找不到 Ekd5.exe。", exePath);
        }

        var jobGrowthTable = HexTableNameResolver.ResolveForProject(project, tables, "6.5-4-2 兵种成长");
        var jobGrowthRead = _reader.Read(project, jobGrowthTable, tables);
        if (!jobGrowthRead.Validation.IsUsable)
        {
            throw new InvalidOperationException("兵种成长表不可读取，无法编辑装备类型可装备部队。");
        }

        var storageColumns = ResolveJobEquipmentStorageColumns(jobGrowthRead.Data);
        var profile = _profileService.Build(project, tables, storageColumns);
        return new Context(exePath, jobGrowthRead, profile, BuildJobSeriesNames(project, tables));
    }

    private static IReadOnlyList<string> ResolveJobEquipmentStorageColumns(DataTable jobGrowthData)
    {
        var direct = ProjectEquipmentTypeProfileService.JobPermissionSlotCount == JobEquipmentCategoryColumns.Length
            ? JobEquipmentCategoryColumns.Where(jobGrowthData.Columns.Contains).ToArray()
            : Array.Empty<string>();
        if (direct.Length == ProjectEquipmentTypeProfileService.JobPermissionSlotCount) return direct;

        var growthColumns = new HashSet<string>(StringComparer.Ordinal)
        {
            "移动力",
            "攻击范围",
            "攻击",
            "防御",
            "精神",
            "爆发",
            "士气",
            "HP",
            "MP"
        };
        var candidates = jobGrowthData.Columns
            .Cast<DataColumn>()
            .Select(column => column.ColumnName)
            .Where(column => column is not "ID" and not "名称" && !growthColumns.Contains(column))
            .ToArray();
        return candidates.Length >= ProjectEquipmentTypeProfileService.JobPermissionSlotCount
            ? candidates.TakeLast(ProjectEquipmentTypeProfileService.JobPermissionSlotCount).ToArray()
            : JobEquipmentCategoryColumns;
    }

    private IReadOnlyDictionary<int, string> BuildJobSeriesNames(CczProject project, IReadOnlyList<HexTableDefinition> tables)
    {
        try
        {
            var table = HexTableNameResolver.ResolveForProject(project, tables, "6.5-3 兵种系");
            var read = _reader.Read(project, table, tables);
            if (!read.Validation.IsUsable) return new Dictionary<int, string>();
            return read.Data.Rows
                .Cast<DataRow>()
                .ToDictionary(
                    row => Convert.ToInt32(row["ID"], CultureInfo.InvariantCulture),
                    row => Convert.ToString(row["名称"], CultureInfo.InvariantCulture) ?? string.Empty);
        }
        catch
        {
            return new Dictionary<int, string>();
        }
    }

    private string WriteExeReport(
        CczProject project,
        string targetPath,
        string backupPath,
        byte[] before,
        byte[] after,
        IReadOnlyList<ItemEquipmentTypePreviewChange> changes)
    {
        var changedBytes = CountChangedBytes(before, after);
        var report = new WriteOperationReport
        {
            OperationKind = "宝物装备类型保存",
            SourceAction = "宝物设定/装备类型保存前自动备份",
            ProjectRoot = project.GameRoot,
            TargetRelativePath = WriteOperationReportService.ToProjectRelativePath(project, targetPath),
            TargetPath = targetPath,
            BackupPath = backupPath,
            BeforeSha256 = WriteOperationReportService.ComputeSha256(before),
            AfterSha256 = WriteOperationReportService.ComputeSha256(after),
            ChangedBytes = changedBytes,
            Summary = $"保存宝物装备类型 EXE 字段：{changes.Count} 项，字节变更 {changedBytes:N0}。",
            SafetyNotes = "装备类型写入使用白名单固定偏移、固定长度覆盖、保存前备份和复读校验。",
            Changes = changes.Select(change => new WriteOperationChange
            {
                Category = "宝物装备类型",
                TableName = change.Area,
                RowIndex = change.RowIndex,
                ColumnName = change.DisplayName,
                OffsetHex = change.OffsetHex,
                ByteLength = change.ByteLength,
                OldValue = change.OldValue,
                NewValue = change.NewValue,
                Annotation = $"{change.TargetFile}@{change.OffsetHex}: {change.OldBytesHex}->{change.NewBytesHex}"
            }).ToList(),
            Metadata =
            {
                ["Field"] = "ItemEquipmentTypeSettings",
                ["TargetFileName"] = TargetFileName,
                ["WritePolicy"] = "Whitelist only, fixed-length overwrite, reread validation"
            }
        };
        return _reportService.WriteJsonReport(report, backupPath);
    }

    private static ItemEquipmentTypePreviewChange BuildChange(
        string area,
        Definition definition,
        int? jobId,
        string displayName,
        string oldValue,
        string newValue,
        byte[] oldBytes,
        byte[] newBytes,
        string targetFile,
        long offset)
        => new()
        {
            Area = area,
            RowIndex = definition.RowIndex,
            JobId = jobId,
            DisplayName = displayName,
            OldValue = oldValue,
            NewValue = newValue,
            OldBytesHex = Convert.ToHexString(oldBytes),
            NewBytesHex = Convert.ToHexString(newBytes),
            TargetFile = targetFile,
            OffsetHex = FormatOffset(offset),
            ByteLength = newBytes.Length
        };

    private static byte[] ParseName(string value, Definition definition)
    {
        try
        {
            return EncodingService.EncodeFixedString(value ?? string.Empty, NameTextByteLength);
        }
        catch (InvalidOperationException)
        {
            throw new InvalidOperationException($"{definition.Description} 名称最多 {NameTextByteLength} 字节 GBK。");
        }
    }

    private static string BuildPermissionSummary(IReadOnlyList<ItemEquipmentTypeJobPermission> permissions)
    {
        var enabled = permissions
            .Where(item => item.State is ItemEquipmentPermissionState.Checked or ItemEquipmentPermissionState.Indeterminate)
            .Select(item => item.State == ItemEquipmentPermissionState.Indeterminate ? item.JobName + "(部分)" : item.JobName)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Take(8)
            .ToArray();
        if (enabled.Length == 0) return "无";
        var suffix = permissions.Count(item => item.State is ItemEquipmentPermissionState.Checked or ItemEquipmentPermissionState.Indeterminate) > enabled.Length
            ? "..."
            : string.Empty;
        return string.Join("、", enabled) + suffix;
    }

    private static string FormatPermissionState(int firstValue, int secondValue)
    {
        var first = firstValue != 0;
        var second = secondValue != 0;
        if (first && second) return "允许";
        if (!first && !second) return "禁止";
        return "部分允许";
    }

    private static int ReadPermissionValue(DataRow row, string columnName)
    {
        if (!row.Table.Columns.Contains(columnName))
        {
            throw new InvalidOperationException("兵种成长表缺少可装备槽位列：" + columnName);
        }

        return Convert.ToInt32(row[columnName], CultureInfo.InvariantCulture) == 0 ? 0 : 1;
    }

    private static bool SetPermissionValue(DataRow row, string columnName, int value)
    {
        var oldValue = ReadPermissionValue(row, columnName);
        if (oldValue == value) return false;
        row[columnName] = value;
        return true;
    }

    private static DataRow FindRowById(DataTable data, int id)
    {
        foreach (DataRow row in data.Rows)
        {
            if (Convert.ToInt32(row["ID"], CultureInfo.InvariantCulture) == id) return row;
        }

        throw new InvalidOperationException($"找不到 ID={id} 的数据行。");
    }

    private static Definition FindDefinition(int rowIndex)
        => Definitions.FirstOrDefault(item => item.RowIndex == rowIndex)
           ?? throw new InvalidOperationException("未知装备类型行：" + rowIndex);

    private static long GetNameOffset(Definition definition)
        => NameTableBaseOffset + definition.NameEntryId;

    private static long GetDisplayOffset(Definition definition)
        => DisplayTableBaseOffset + (definition.DisplayIndex ?? throw new InvalidOperationException($"{definition.Description} 没有显示表项。"));

    private static byte[] ReadBytes(byte[] bytes, long offset, int byteLength)
    {
        var result = new byte[byteLength];
        Buffer.BlockCopy(bytes, checked((int)offset), result, 0, byteLength);
        return result;
    }

    private static void VerifyExeWrites(string targetPath, IReadOnlyList<ItemEquipmentTypePreviewChange> changes)
    {
        var bytes = File.ReadAllBytes(targetPath);
        foreach (var change in changes)
        {
            var offset = checked((int)ParseHex(change.OffsetHex));
            var expected = Convert.FromHexString(change.NewBytesHex);
            for (var i = 0; i < expected.Length; i++)
            {
                if (bytes[offset + i] != expected[i])
                {
                    throw new InvalidOperationException($"装备类型复读校验失败：{change.DisplayName}。");
                }
            }
        }
    }

    private static void EnsureRange(string path, long offset, int byteLength)
    {
        var length = new FileInfo(path).Length;
        if (offset < 0 || byteLength <= 0 || offset + byteLength > length)
        {
            throw new InvalidOperationException($"装备类型地址越界：{Path.GetFileName(path)}@{FormatOffset(offset)}，长度 {byteLength}，文件长度 {length}。");
        }
    }

    private static long ParseHex(string value)
    {
        value = value.Trim();
        if (value.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) value = value[2..];
        return long.Parse(value, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
    }

    private static string FormatOffset(long offset)
        => "0x" + offset.ToString("X", CultureInfo.InvariantCulture);

    private static int CountChangedBytes(byte[] before, byte[] after)
    {
        var count = 0;
        for (var i = 0; i < Math.Min(before.Length, after.Length); i++)
        {
            if (before[i] != after[i]) count++;
        }

        return count + Math.Abs(before.Length - after.Length);
    }

    private static string CreateBeforeSaveBackup(CczProject project, string filePath)
    {
        var backupRoot = Path.Combine(project.GameRoot, "_CCZModStudio_Backups");
        Directory.CreateDirectory(backupRoot);
        var stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss_fff", CultureInfo.InvariantCulture);
        var backupPath = Path.Combine(backupRoot, $"{stamp}_{Path.GetFileName(filePath)}");
        var suffix = 1;
        while (File.Exists(backupPath))
        {
            backupPath = Path.Combine(backupRoot, $"{stamp}_{suffix++}_{Path.GetFileName(filePath)}");
        }

        File.Copy(filePath, backupPath, overwrite: false);
        return backupPath;
    }

    private static readonly string[] JobEquipmentCategoryColumns =
    [
        "普通剑",
        "特殊剑",
        "普通枪",
        "特殊枪",
        "普通弓",
        "特殊弓",
        "普通刀",
        "特殊刀",
        "普通炮车",
        "特殊炮车",
        "普通锤",
        "特殊锤",
        "普通斧",
        "特殊斧",
        "普通扇",
        "特殊扇",
        "普通宝剑",
        "特殊宝剑",
        "普通将剑",
        "特殊将剑",
        "普通铠甲",
        "特殊铠甲",
        "普通衣服",
        "特殊衣服",
        "普通袍服",
        "特殊袍服"
    ];

    private sealed record Definition(
        int RowIndex,
        int NameEntryId,
        string Description,
        int? DisplayIndex,
        int? NormalDisplayValue);

    private sealed record Context(
        string ExePath,
        TableReadResult JobGrowthRead,
        ProjectEquipmentTypeProfile Profile,
        IReadOnlyDictionary<int, string> JobSeriesNames);
}
