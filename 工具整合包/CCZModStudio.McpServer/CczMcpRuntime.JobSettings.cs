using System.Data;
using System.Globalization;
using System.Text.Json;
using CCZModStudio.Core;
using CCZModStudio.Formats;
using CCZModStudio.Models;

namespace CCZModStudio.McpServer;

public sealed partial class CczMcpRuntime
{
    private const string JobSeriesTableName = "6.5-3 兵种系";
    private const string JobRestraintTableName = "6.5-3-3 兵种相克";
    private const string JobAttributeTableName = "6.5-3-4 兵种属性";
    private const string DetailedJobTableName = "6.5-4 详细兵种";
    private const string JobDescriptionTableName = "6.5-4-1 兵种说明";
    private const string JobGrowthTableName = "6.5-4-2 兵种成长";
    private const string JobPierceTableName = "6.5-4-3 兵种穿透";

    public object ReadJobSettings(string? gameRoot, List<int>? jobIds, List<int>? jobSeriesIds, bool includeMatrices, int limit)
    {
        var project = LoadProject(gameRoot);
        var tables = LoadTables(project);
        var build = BuildJobSettings(project, tables);
        var effectiveLimit = NormalizeLimit(limit, 80, 500);
        var jobIdSet = jobIds is { Count: > 0 } ? jobIds.ToHashSet() : null;
        var seriesIdSet = jobSeriesIds is { Count: > 0 } ? jobSeriesIds.ToHashSet() : null;
        var jobs = BuildDetailedJobRows(build, jobIdSet, effectiveLimit);
        var series = BuildJobSeriesRows(build.JobSeriesRead, seriesIdSet, effectiveLimit);

        return new
        {
            project.GameRoot,
            Engine = build.Engine,
            Tables = BuildJobSettingsTableEvidence(build),
            Evidence = BuildJobAttributeEvidence(project, build.JobAttributeRead),
            EquipmentPermissionSlots = build.EquipmentProfile.JobPermissionSlots.Select(BuildJobEquipmentSlotPayload),
            AttributeRows = BuildJobAttributeRowMetadata(),
            TotalJobSeries = build.JobSeriesRead.Data.Rows.Count,
            ReturnedJobSeries = series.Count,
            JobSeries = series,
            TotalDetailedJobs = build.DetailedJobRead.Data.Rows.Count,
            ReturnedDetailedJobs = jobs.Count,
            DetailedJobs = jobs,
            Matrices = includeMatrices
                ? new
                {
                    Restraint = BuildMatrixPayload(build.JobRestraintRead, build.JobSeriesNames, maxRows: 40),
                    Attributes = BuildMatrixPayload(build.JobAttributeRead, build.JobSeriesNames, maxRows: 8)
                }
                : null,
            AccessoryJobGroups = build.AccessoryJobGroups,
            SafetyNote = "Read-only job settings. Dedicated write_job_settings uses HexTableWriter backups/reports; accessory job groups are exposed through read/preview/write_accessory_job_groups."
        };
    }

    public object PreviewJobSettings(string? gameRoot, JobSettingsUpdate update)
    {
        var project = LoadProject(gameRoot);
        var tables = LoadTables(project);
        var build = BuildJobSettings(project, tables);
        var changes = ApplyJobSettingsUpdate(build, update, mutate: false);
        return new
        {
            project.GameRoot,
            Changes = changes,
            Evidence = BuildJobAttributeEvidence(project, build.JobAttributeRead),
            AttributeRows = BuildJobAttributeRowMetadata(),
            SafetyNote = "Preview only; no game files were modified."
        };
    }

    public object WriteJobSettings(string? gameRoot, JobSettingsUpdate update, string? writeMode)
    {
        var project = LoadProject(gameRoot);
        EnsureWriteMode(project, writeMode);
        var tables = LoadTables(project);
        var build = BuildJobSettings(project, tables);
        var changes = ApplyJobSettingsUpdate(build, update, mutate: true);
        var saves = SaveChangedJobSettingsTables(project, build);

        return new
        {
            project.GameRoot,
            Changes = changes,
            SaveCount = saves.Count,
            Saves = saves.Select(BuildTableSavePayload),
            Evidence = BuildJobAttributeEvidence(project, build.JobAttributeRead),
            SafetyNote = "Job settings writes use HexTableWriter backups and structured reports for Ekd5.exe/Data.e5/Imsg.e5 backed tables."
        };
    }

    private JobSettingsBuild BuildJobSettings(CczProject project, IReadOnlyList<HexTableDefinition> tables)
    {
        var engine = _engineProfileService.Detect(project);
        var jobSeriesRead = ReadRequiredJobTable(project, tables, engine.TableHints.JobSeriesTable);
        var detailedJobRead = ReadRequiredJobTable(project, tables, engine.TableHints.DetailedJobTable);
        var descriptionRead = ReadRequiredJobTable(project, tables, JobDescriptionTableName);
        var growthRead = ReadRequiredJobTable(project, tables, JobGrowthTableName);
        var pierceRead = ReadRequiredJobTable(project, tables, JobPierceTableName);
        var restraintRead = ReadRequiredJobTable(project, tables, JobRestraintTableName);
        var attributeRead = ReadRequiredJobTable(project, tables, JobAttributeTableName);
        var storageColumns = ResolveJobEquipmentStorageColumns(growthRead.Data);
        var equipmentProfile = _equipmentTypeProfileService.Build(project, tables, storageColumns);
        var accessoryProfile = _accessoryJobGroupService.Read(project, tables);

        return new JobSettingsBuild(
            engine,
            jobSeriesRead,
            detailedJobRead,
            descriptionRead,
            growthRead,
            pierceRead,
            restraintRead,
            attributeRead,
            equipmentProfile,
            accessoryProfile,
            BuildIdNameLookup(jobSeriesRead.Data),
            BuildIdNameLookup(detailedJobRead.Data));
    }

    private TableReadResult ReadRequiredJobTable(CczProject project, IReadOnlyList<HexTableDefinition> tables, string tableName)
    {
        var table = FindTable(project, tables, tableName);
        var read = _tableReader.Read(project, table, tables);
        if (!read.Validation.IsUsable)
        {
            throw new InvalidOperationException($"Job settings table is not usable: {read.Table.TableName}. Warnings: {string.Join("; ", read.Validation.Warnings)}");
        }

        return read;
    }

    private static IReadOnlyDictionary<int, string> BuildIdNameLookup(DataTable data)
    {
        var result = new Dictionary<int, string>();
        var nameColumn = data.Columns.Contains("名称")
            ? data.Columns["名称"]!
            : data.Columns.Cast<DataColumn>().FirstOrDefault(column => !column.ColumnName.Equals("ID", StringComparison.OrdinalIgnoreCase));
        if (nameColumn == null) return result;

        foreach (DataRow row in data.Rows)
        {
            var id = Convert.ToInt32(row["ID"], CultureInfo.InvariantCulture);
            result[id] = Convert.ToString(row[nameColumn], CultureInfo.InvariantCulture) ?? string.Empty;
        }

        return result;
    }

    private IReadOnlyList<Dictionary<string, object?>> BuildDetailedJobRows(JobSettingsBuild build, HashSet<int>? jobIdSet, int limit)
    {
        var result = new List<Dictionary<string, object?>>();
        foreach (DataRow detailRow in build.DetailedJobRead.Data.Rows)
        {
            var id = Convert.ToInt32(detailRow["ID"], CultureInfo.InvariantCulture);
            if (jobIdSet != null && !jobIdSet.Contains(id)) continue;
            var descriptionRow = TryFindRowById(build.DescriptionRead.Data, id);
            var growthRow = TryFindRowById(build.GrowthRead.Data, id);
            var pierceRow = TryFindRowById(build.PierceRead.Data, id);
            var growthColumns = growthRow == null
                ? Array.Empty<DataColumn>()
                : growthRow.Table.Columns.Cast<DataColumn>().Where(column => column.ColumnName is not "ID" and not "名称").ToArray();

            var item = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
            {
                ["job_id"] = id,
                ["name"] = ReadName(detailRow),
                ["description"] = descriptionRow != null && descriptionRow.Table.Columns.Contains("介绍")
                    ? Convert.ToString(descriptionRow["介绍"], CultureInfo.InvariantCulture) ?? string.Empty
                    : string.Empty,
                ["growth_values"] = growthRow == null
                    ? new Dictionary<string, object?>()
                    : RowToDictionary(growthRow, growthColumns),
                ["pierce"] = pierceRow != null && pierceRow.Table.Columns.Contains("穿透")
                    ? Convert.ToInt32(pierceRow["穿透"], CultureInfo.InvariantCulture)
                    : null,
                ["equipment_permissions"] = growthRow == null
                    ? Array.Empty<object>()
                    : build.EquipmentProfile.JobPermissionSlots.Select(slot => BuildJobEquipmentPermissionPayload(growthRow, slot)).ToArray()
            };
            result.Add(item);
            if (result.Count >= limit) break;
        }

        return result;
    }

    private static IReadOnlyList<Dictionary<string, object?>> BuildJobSeriesRows(TableReadResult jobSeriesRead, HashSet<int>? seriesIdSet, int limit)
    {
        var result = new List<Dictionary<string, object?>>();
        foreach (DataRow row in jobSeriesRead.Data.Rows)
        {
            var id = Convert.ToInt32(row["ID"], CultureInfo.InvariantCulture);
            if (seriesIdSet != null && !seriesIdSet.Contains(id)) continue;
            result.Add(new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
            {
                ["job_series_id"] = id,
                ["name"] = ReadName(row)
            });
            if (result.Count >= limit) break;
        }

        return result;
    }

    private static object BuildJobEquipmentSlotPayload(JobEquipmentPermissionSlotDefinition slot)
        => new
        {
            slot.SlotIndex,
            slot.StorageColumnName,
            slot.TypeId,
            slot.DisplayName,
            slot.SummaryName,
            slot.SampleText,
            slot.SourceNote,
            Source = slot.Source.ToString(),
            slot.SourceDisplayName
        };

    private static object BuildJobEquipmentPermissionPayload(DataRow growthRow, JobEquipmentPermissionSlotDefinition slot)
    {
        var enabled = growthRow.Table.Columns.Contains(slot.StorageColumnName) &&
                      Convert.ToInt32(growthRow[slot.StorageColumnName], CultureInfo.InvariantCulture) != 0;
        return new
        {
            slot.SlotIndex,
            slot.StorageColumnName,
            slot.TypeId,
            slot.SummaryName,
            Enabled = enabled,
            Value = growthRow.Table.Columns.Contains(slot.StorageColumnName)
                ? Convert.ToInt32(growthRow[slot.StorageColumnName], CultureInfo.InvariantCulture)
                : 0
        };
    }

    private static IReadOnlyList<object> BuildMatrixPayload(TableReadResult read, IReadOnlyDictionary<int, string> jobSeriesNames, int maxRows)
    {
        var numericColumns = read.Data.Columns.Cast<DataColumn>()
            .Where(column => int.TryParse(column.ColumnName, NumberStyles.Integer, CultureInfo.InvariantCulture, out _))
            .ToArray();

        return read.Data.Rows.Cast<DataRow>()
            .Take(maxRows)
            .Select(row =>
            {
                var rowId = Convert.ToInt32(row["ID"], CultureInfo.InvariantCulture);
                return new
                {
                    RowId = rowId,
                    RowLabel = read.Data.Columns.Contains("名称")
                        ? Convert.ToString(row["名称"], CultureInfo.InvariantCulture) ?? string.Empty
                        : BuildAttributeRowLabel(rowId).Label,
                    RowMeaning = !read.Data.Columns.Contains("名称") ? BuildAttributeRowLabel(rowId) : null,
                    Values = numericColumns.ToDictionary(
                        column => column.ColumnName,
                        column => row[column] is DBNull ? null : row[column]),
                    ColumnLabels = numericColumns.ToDictionary(
                        column => column.ColumnName,
                        column => int.TryParse(column.ColumnName, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id) &&
                                  jobSeriesNames.TryGetValue(id, out var name)
                            ? name
                            : string.Empty)
                };
            })
            .Cast<object>()
            .ToArray();
    }

    private static IReadOnlyList<object> BuildJobAttributeRowMetadata()
        => Enumerable.Range(0, 8)
            .Select(row => (object)BuildAttributeRowLabel(row))
            .ToArray();

    private static object BuildJobAttributeEvidence(CczProject project, TableReadResult attributeRead)
    {
        var systemIni = string.Empty;
        var userXk = string.Empty;
        if (!string.IsNullOrWhiteSpace(project.ImageAssignerSystemIniPath) &&
            File.Exists(project.ImageAssignerSystemIniPath))
        {
            systemIni = project.ImageAssignerSystemIniPath;
            userXk = ReadIniValue(systemIni, "UserXK");
        }

        var runtimeVa = attributeRead.Table.DataPos == 0xA38C0
            ? "0x004D20C0"
            : string.Empty;

        return new
        {
            Source = "B形象指定器 System.ini UserXK plus HexTable 6.5-3-4",
            ImageAssignerSystemIniPath = systemIni,
            UserXK = userXk,
            UserXKFileOffsetHex = string.IsNullOrWhiteSpace(userXk) ? string.Empty : "0x" + userXk,
            RestraintBlock = string.IsNullOrWhiteSpace(userXk)
                ? null
                : new { FileOffsetHex = "0x" + userXk, Rows = 40, Columns = 40, Bytes = 1600 },
            AttributeBlock = new
            {
                TableName = attributeRead.Table.TableName,
                attributeRead.Table.FileName,
                FileOffsetHex = HexDisplayFormatter.FormatOffset(attributeRead.Table.DataPos),
                Rows = attributeRead.Table.RowCount,
                Columns = attributeRead.Table.RowSize,
                RuntimeVirtualAddressHex = runtimeVa,
                Notes = "row0 confirmed as broad job-series category: 0=cavalry, 2=infantry; row3/row4 are ranged/shooting candidates and still require dynamic battle verification."
            }
        };
    }

    private static string ReadIniValue(string path, string key)
    {
        try
        {
            foreach (var rawLine in File.ReadLines(path, EncodingService.Gbk))
            {
                var line = rawLine.Trim();
                if (line.Length == 0 || line.StartsWith(";", StringComparison.Ordinal)) continue;
                var separator = line.IndexOf('=');
                if (separator <= 0) continue;
                var name = line[..separator].Trim();
                if (!name.Equals(key, StringComparison.OrdinalIgnoreCase)) continue;
                return line[(separator + 1)..].Trim();
            }
        }
        catch
        {
            return string.Empty;
        }

        return string.Empty;
    }

    private static object BuildJobSettingsTableEvidence(JobSettingsBuild build)
        => new
        {
            JobSeries = BuildJobSettingsTableEvidenceItem(build.JobSeriesRead),
            DetailedJob = BuildJobSettingsTableEvidenceItem(build.DetailedJobRead),
            Description = BuildJobSettingsTableEvidenceItem(build.DescriptionRead),
            Growth = BuildJobSettingsTableEvidenceItem(build.GrowthRead),
            Pierce = BuildJobSettingsTableEvidenceItem(build.PierceRead),
            Restraint = BuildJobSettingsTableEvidenceItem(build.JobRestraintRead),
            Attribute = BuildJobSettingsTableEvidenceItem(build.JobAttributeRead)
        };

    private static object BuildJobSettingsTableEvidenceItem(TableReadResult read)
        => new
        {
            read.Table.TableName,
            read.Table.FileName,
            DataPosHex = HexDisplayFormatter.FormatOffset(read.Table.DataPos),
            read.Table.RowCount,
            read.Table.RowSize,
            read.Validation.Warnings
        };

    private object ApplyJobSettingsUpdate(JobSettingsBuild build, JobSettingsUpdate? update, bool mutate)
    {
        if (update == null) throw new InvalidOperationException("update is required.");
        var changes = new List<object>();

        foreach (var pair in update.JobSeriesNames)
        {
            var row = FindRowById(build.JobSeriesRead.Data, pair.Key);
            var nameColumn = FindNameColumnForMcp(build.JobSeriesRead.Data);
            var oldValue = Convert.ToString(row[nameColumn], CultureInfo.InvariantCulture) ?? string.Empty;
            if (mutate) row[nameColumn] = pair.Value;
            changes.Add(new { Table = build.JobSeriesRead.Table.TableName, RowId = pair.Key, Column = nameColumn.ColumnName, OldValue = oldValue, NewValue = pair.Value });
        }

        foreach (var jobUpdate in update.DetailedJobs)
        {
            if (jobUpdate.JobId < 0) throw new InvalidOperationException("job_id must be non-negative.");
            if (jobUpdate.Name != null)
            {
                var row = FindRowById(build.DetailedJobRead.Data, jobUpdate.JobId);
                var nameColumn = FindNameColumnForMcp(build.DetailedJobRead.Data);
                AddCellChange(changes, build.DetailedJobRead, row, jobUpdate.JobId, nameColumn, jobUpdate.Name, mutate);
            }

            if (jobUpdate.Description != null)
            {
                var row = FindRowById(build.DescriptionRead.Data, jobUpdate.JobId);
                var column = FindColumn(build.DescriptionRead.Data, "介绍");
                AddCellChange(changes, build.DescriptionRead, row, jobUpdate.JobId, column, jobUpdate.Description, mutate);
            }

            foreach (var (columnName, value) in jobUpdate.GrowthValues)
            {
                var row = FindRowById(build.GrowthRead.Data, jobUpdate.JobId);
                var column = FindColumn(build.GrowthRead.Data, columnName);
                EnsureWritableJobColumn(column);
                AddCellChange(changes, build.GrowthRead, row, jobUpdate.JobId, column, ConvertJsonValue(value), mutate);
            }

            if (jobUpdate.Pierce.HasValue)
            {
                var row = FindRowById(build.PierceRead.Data, jobUpdate.JobId);
                var column = FindColumn(build.PierceRead.Data, "穿透");
                AddCellChange(changes, build.PierceRead, row, jobUpdate.JobId, column, jobUpdate.Pierce.Value, mutate);
            }
        }

        ApplyMatrixCellUpdates(changes, build.JobRestraintRead, update.RestraintMatrix, mutate, "restraint_matrix");
        ApplyMatrixCellUpdates(changes, build.JobAttributeRead, update.AttributeMatrix, mutate, "attribute_matrix");

        return new { ChangeCount = changes.Count, Changes = changes };
    }

    private static void AddCellChange(
        List<object> changes,
        TableReadResult read,
        DataRow row,
        int rowId,
        DataColumn column,
        object? newValue,
        bool mutate)
    {
        if (column.ColumnName.Equals("ID", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("ID is synthetic and cannot be written.");
        }

        var oldValue = row[column] is DBNull ? null : row[column];
        if (mutate) row[column] = newValue ?? string.Empty;
        changes.Add(new { Table = read.Table.TableName, RowId = rowId, Column = column.ColumnName, OldValue = oldValue, NewValue = newValue });
    }

    private static void EnsureWritableJobColumn(DataColumn column)
    {
        if (column.ColumnName.Equals("ID", StringComparison.OrdinalIgnoreCase) ||
            column.ColumnName.Equals("名称", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Column {column.ColumnName} cannot be written through growth_values.");
        }

        var field = column.ExtendedProperties["FieldDefinition"] as HexFieldDefinition;
        if (field == null || !field.ConsumesBytes)
        {
            throw new InvalidOperationException($"Column {column.ColumnName} is derived or non-writable.");
        }
    }

    private static void ApplyMatrixCellUpdates(
        List<object> changes,
        TableReadResult read,
        List<JobMatrixCellUpdate>? updates,
        bool mutate,
        string fieldName)
    {
        if (updates == null) return;
        foreach (var update in updates)
        {
            var row = FindRowById(read.Data, update.RowId);
            var column = FindColumn(read.Data, update.ColumnId.ToString(CultureInfo.InvariantCulture));
            EnsureWritableMatrixValue(read, column, update.Value, fieldName);
            AddCellChange(changes, read, row, update.RowId, column, update.Value, mutate);
        }
    }

    private static void EnsureWritableMatrixValue(TableReadResult read, DataColumn column, int value, string fieldName)
    {
        if (value < byte.MinValue || value > byte.MaxValue)
        {
            throw new InvalidOperationException($"{fieldName} value must be in 0..255.");
        }

        var field = column.ExtendedProperties["FieldDefinition"] as HexFieldDefinition;
        if (field == null || !field.ConsumesBytes)
        {
            throw new InvalidOperationException($"Column {column.ColumnName} in {read.Table.TableName} is derived or non-writable.");
        }
    }

    private IReadOnlyList<TableSaveResult> SaveChangedJobSettingsTables(CczProject project, JobSettingsBuild build)
    {
        var saves = new List<TableSaveResult>();
        AddSaveIfChanged(project, build.JobSeriesRead, saves);
        AddSaveIfChanged(project, build.DetailedJobRead, saves);
        AddSaveIfChanged(project, build.DescriptionRead, saves);
        AddSaveIfChanged(project, build.GrowthRead, saves);
        AddSaveIfChanged(project, build.PierceRead, saves);
        AddSaveIfChanged(project, build.JobRestraintRead, saves);
        AddSaveIfChanged(project, build.JobAttributeRead, saves);
        return saves;
    }

    private void AddSaveIfChanged(CczProject project, TableReadResult read, List<TableSaveResult> saves)
    {
        if (read.Data.GetChanges() != null)
        {
            saves.Add(_tableWriter.Save(project, read.Table, read.Data));
        }
    }

    private static DataRow? TryFindRowById(DataTable data, int rowId)
        => data.Rows.Cast<DataRow>().FirstOrDefault(row => Convert.ToInt32(row["ID"], CultureInfo.InvariantCulture) == rowId);

    private static string ReadName(DataRow row)
    {
        if (row.Table.Columns.Contains("名称"))
        {
            return Convert.ToString(row["名称"], CultureInfo.InvariantCulture) ?? string.Empty;
        }

        var nameColumn = row.Table.Columns.Cast<DataColumn>()
            .FirstOrDefault(column => !column.ColumnName.Equals("ID", StringComparison.OrdinalIgnoreCase));
        return nameColumn == null ? string.Empty : Convert.ToString(row[nameColumn], CultureInfo.InvariantCulture) ?? string.Empty;
    }

    private static IReadOnlyList<string> ResolveJobEquipmentStorageColumns(DataTable growthData)
    {
        var preferred = new[]
        {
            "普通剑", "特殊剑", "普通枪", "特殊枪", "普通弓", "特殊弓", "普通刀", "特殊刀",
            "普通炮车", "特殊炮车", "普通锤", "特殊锤", "普通斧", "特殊斧", "普通扇", "特殊扇",
            "普通宝剑", "特殊宝剑", "普通将剑", "特殊将剑", "普通铠甲", "特殊铠甲", "普通衣服", "特殊衣服",
            "普通袍服", "特殊袍服"
        };
        var direct = preferred.Where(growthData.Columns.Contains).ToArray();
        if (direct.Length == preferred.Length) return direct;

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
        var candidates = growthData.Columns
            .Cast<DataColumn>()
            .Select(column => column.ColumnName)
            .Where(column => column is not "ID" and not "名称" && !growthColumns.Contains(column))
            .ToArray();
        return candidates.Length >= ProjectEquipmentTypeProfileService.JobPermissionSlotCount
            ? candidates.TakeLast(ProjectEquipmentTypeProfileService.JobPermissionSlotCount).ToArray()
            : preferred;
    }

    private static JobAttributeRowMeaning BuildAttributeRowLabel(int rowId)
        => rowId switch
        {
            0 => new JobAttributeRowMeaning(rowId, "大类", "confirmed", "0=骑兵类，2=步兵类；1/3 暂按器械/特殊候选处理，需结合项目内容确认。"),
            1 => new JobAttributeRowMeaning(rowId, "属性1", "unknown", "形象指定器 UserXK 属性矩阵行；语义待验证。"),
            2 => new JobAttributeRowMeaning(rowId, "属性2", "unknown", "形象指定器 UserXK 属性矩阵行；语义待验证。"),
            3 => new JobAttributeRowMeaning(rowId, "远程候选A", "candidate", "远程/射击相关候选行，仍需动态战斗验证。"),
            4 => new JobAttributeRowMeaning(rowId, "远程候选B", "candidate", "远程/射击相关候选行，仍需动态战斗验证。"),
            5 => new JobAttributeRowMeaning(rowId, "属性5", "unknown", "形象指定器 UserXK 属性矩阵行；语义待验证。"),
            6 => new JobAttributeRowMeaning(rowId, "属性6", "unknown", "形象指定器 UserXK 属性矩阵行；语义待验证。"),
            7 => new JobAttributeRowMeaning(rowId, "属性7", "unknown", "形象指定器 UserXK 属性矩阵行；语义待验证。"),
            _ => new JobAttributeRowMeaning(rowId, $"属性{rowId}", "unknown", "超出当前 8 行属性矩阵定义。")
        };

    private sealed record JobAttributeRowMeaning(int RowId, string Label, string Confidence, string Meaning);

    private sealed record JobSettingsBuild(
        CczEngineProfile Engine,
        TableReadResult JobSeriesRead,
        TableReadResult DetailedJobRead,
        TableReadResult DescriptionRead,
        TableReadResult GrowthRead,
        TableReadResult PierceRead,
        TableReadResult JobRestraintRead,
        TableReadResult JobAttributeRead,
        ProjectEquipmentTypeProfile EquipmentProfile,
        AccessoryJobGroupProfile AccessoryJobGroups,
        IReadOnlyDictionary<int, string> JobSeriesNames,
        IReadOnlyDictionary<int, string> DetailedJobNames);
}
