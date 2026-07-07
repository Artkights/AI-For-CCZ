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
    private const long JobRestraintOffset = 0xA3280;
    private const long JobAttributeOffset = 0xA38C0;
    private const long JobUserXkTailOffset = 0xA3A00;
    private const long RuntimeVaDelta = 0x42E800;

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

        var attributeRuntimeVa = attributeRead.Table.DataPos == JobAttributeOffset
            ? FormatRuntimeVa(attributeRead.Table.DataPos)
            : string.Empty;
        var restraintOffsetHex = string.IsNullOrWhiteSpace(userXk)
            ? HexDisplayFormatter.FormatOffset(JobRestraintOffset)
            : NormalizeHexWithoutPrefix(userXk);
        var restraintOffset = TryParseHexOffset(userXk, out var parsedUserXk)
            ? parsedUserXk
            : JobRestraintOffset;

        return new
        {
            Source = "B形象指定器 System.ini UserXK=A3280 plus HexTable 6.5-3-3/6.5-3-4",
            ImageAssignerSystemIniPath = systemIni,
            UserXK = userXk,
            UserXKFileOffsetHex = "0x" + restraintOffsetHex,
            RestraintBlock = new
            {
                FileOffsetHex = "0x" + restraintOffsetHex,
                RuntimeVirtualAddressHex = FormatRuntimeVa(restraintOffset),
                Rows = 40,
                Columns = 40,
                Bytes = 1600,
                Formula = "offset = 0xA3280 + attacker_job_series_id*40 + defender_job_series_id"
            },
            AttributeBlock = new
            {
                TableName = attributeRead.Table.TableName,
                attributeRead.Table.FileName,
                FileOffsetHex = "0x" + HexDisplayFormatter.FormatOffset(attributeRead.Table.DataPos),
                Rows = attributeRead.Table.RowCount,
                Columns = 40,
                Bytes = attributeRead.Table.RowCount * attributeRead.Table.RowSize,
                RuntimeVirtualAddressHex = attributeRuntimeVa,
                Formula = "offset = 0xA38C0 + attribute_row_id*40 + job_series_id",
                Notes = "row0 official field is 移动声音. Old cavalry/infantry patches used that byte as a proxy and must not rename the field."
            },
            UserXkTail40 = new
            {
                FileOffsetHex = "0x" + HexDisplayFormatter.FormatOffset(JobUserXkTailOffset),
                RuntimeVirtualAddressHex = FormatRuntimeVa(JobUserXkTailOffset),
                Bytes = 40,
                RangeHex = "0xA3A00..0xA3A27",
                WriteEnabled = false,
                Notes = "Falls inside the old figure-1 1960-byte UserXK block tail, but outside the 8 official B形象指定器 Option1 attributes. Read/report only until a single-field official diff proves ownership."
            },
            AttributeRows = BuildJobAttributeRowMetadata(),
            Corrections = new[]
            {
                "6.5-3-4 row0 is 移动声音, not 兵种大类.",
                "6.5-3-3 remains the 40x40 restraint matrix at 0xA3280.",
                "6.5-3-4 remains the 8x40 official attribute matrix at 0xA38C0."
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
            EnsureMatrixCoordinate(read, update, fieldName);
            var row = FindRowById(read.Data, update.RowId);
            var column = FindColumn(read.Data, update.ColumnId.ToString(CultureInfo.InvariantCulture));
            EnsureWritableMatrixValue(read, column, update.Value, fieldName);
            var oldValue = row[column] is DBNull ? null : row[column];
            if (mutate) row[column] = update.Value;
            var fileOffset = read.Table.DataPos + ((long)update.RowId * read.Table.RowSize) + update.ColumnId;
            changes.Add(new
            {
                Table = read.Table.TableName,
                Field = fieldName,
                RowId = update.RowId,
                RowLabel = fieldName.Equals("attribute_matrix", StringComparison.OrdinalIgnoreCase)
                    ? BuildAttributeRowLabel(update.RowId).Label
                    : ReadName(row),
                ColumnId = update.ColumnId,
                Column = column.ColumnName,
                OldValue = oldValue,
                NewValue = update.Value,
                OldDisplayValue = fieldName.Equals("attribute_matrix", StringComparison.OrdinalIgnoreCase) && oldValue != null
                    ? FormatJobAttributeDisplayValue(update.RowId, Convert.ToInt32(oldValue, CultureInfo.InvariantCulture))
                    : oldValue,
                NewDisplayValue = fieldName.Equals("attribute_matrix", StringComparison.OrdinalIgnoreCase)
                    ? FormatJobAttributeDisplayValue(update.RowId, update.Value)
                    : (object)update.Value,
                FileOffsetHex = "0x" + HexDisplayFormatter.FormatOffset(fileOffset),
                RuntimeVirtualAddressHex = FormatRuntimeVa(fileOffset)
            });
        }
    }

    private static void EnsureMatrixCoordinate(TableReadResult read, JobMatrixCellUpdate update, string fieldName)
    {
        if (update.RowId < read.Table.BeginId || update.RowId >= read.Table.BeginId + read.Table.RowCount)
        {
            throw new InvalidOperationException($"{fieldName} row_id must be {read.Table.BeginId}..{read.Table.BeginId + read.Table.RowCount - 1}.");
        }

        if (update.ColumnId < 0 || update.ColumnId >= read.Table.RowSize)
        {
            throw new InvalidOperationException($"{fieldName} column_id must be 0..{read.Table.RowSize - 1}.");
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
    {
        var definition = GetJobAttributeDefinition(rowId);
        var offset = rowId is >= 0 and <= 7 ? JobAttributeOffset + (rowId * 40L) : 0;
        return new JobAttributeRowMeaning(
            rowId,
            definition.Label,
            definition.Confidence,
            definition.ValueDisplayRule,
            rowId is >= 0 and <= 7 ? "0x" + HexDisplayFormatter.FormatOffset(offset) : string.Empty,
            rowId is >= 0 and <= 7 ? FormatRuntimeVa(offset) : string.Empty,
            definition.EditorKind,
            definition.EditorKind == "numeric" ? byte.MinValue : null,
            definition.EditorKind == "numeric" ? byte.MaxValue : null,
            definition.EditorKind == "combo" ? definition.ValueChoices : Array.Empty<JobAttributeValueChoice>());
    }

    private static string FormatJobAttributeDisplayValue(int rowId, int value)
    {
        var definition = GetJobAttributeDefinition(rowId);
        if (definition.EditorKind == "numeric")
        {
            return value.ToString(CultureInfo.InvariantCulture);
        }

        var choice = definition.ValueChoices.FirstOrDefault(candidate => candidate.Value == value);
        return choice?.Display ?? $"自定义：{value.ToString(CultureInfo.InvariantCulture)}";
    }

    private static JobAttributeDefinition GetJobAttributeDefinition(int rowId)
        => JobAttributeDefinitions.TryGetValue(rowId, out var definition)
            ? definition
            : new JobAttributeDefinition(rowId, $"属性{rowId}", "out_of_range", "超出当前 8 行属性矩阵定义。", "numeric", []);

    private static readonly IReadOnlyDictionary<int, JobAttributeDefinition> JobAttributeDefinitions =
        new Dictionary<int, JobAttributeDefinition>
        {
            [0] = new(0, "移动声音", "official", "马蹄声：0，车轮声：1，脚步声：2，无声：3", "combo", [new(0, "马蹄声"), new(1, "车轮声"), new(2, "脚步声"), new(3, "无声")]),
            [1] = new(1, "移动速度", "neutral_label", "速度档位0：0，速度档位1：1；中性档位名，具体语义待进一步验证", "combo", [new(0, "速度档位0"), new(1, "速度档位1")]),
            [2] = new(2, "攻击声音", "neutral_label", "攻击音效0：0，攻击音效1：1；中性档位名，具体语义待进一步验证", "combo", [new(0, "攻击音效0"), new(1, "攻击音效1")]),
            [3] = new(3, "远程兵种", "official", "否：0，是：1", "combo", [new(0, "否"), new(1, "是")]),
            [4] = new(4, "攻击延迟", "official", "无延迟：0，有延迟：1", "combo", [new(0, "无延迟"), new(1, "有延迟")]),
            [5] = new(5, "兵种类型", "neutral_label", "类型0：0，类型1：1，类型2：2；中性类型名，具体语义待进一步验证", "combo", [new(0, "类型0"), new(1, "类型1"), new(2, "类型2")]),
            [6] = new(6, "策略伤害", "official", "百分比式单字节，常见值 90/100/110/120/125/130；GUI 使用数值输入", "numeric", []),
            [7] = new(7, "参与围攻", "official", "不参与：0，参与：1；当前 6.5 基底默认全 1", "combo", [new(0, "不参与"), new(1, "参与")])
        };

    private static string FormatRuntimeVa(long fileOffset)
        => "0x" + HexDisplayFormatter.Format(fileOffset + RuntimeVaDelta, 8);

    private static string NormalizeHexWithoutPrefix(string value)
    {
        var text = value.Trim();
        if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) text = text[2..];
        return text.ToUpperInvariant();
    }

    private static bool TryParseHexOffset(string? value, out long offset)
    {
        offset = 0;
        if (string.IsNullOrWhiteSpace(value)) return false;
        var text = NormalizeHexWithoutPrefix(value);
        return long.TryParse(text, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out offset);
    }

    private sealed record JobAttributeRowMeaning(
        int RowId,
        string Label,
        string Confidence,
        string ValueDisplayRule,
        string FileOffsetBaseHex,
        string RuntimeVaBaseHex,
        string EditorKind,
        int? MinValue,
        int? MaxValue,
        IReadOnlyList<JobAttributeValueChoice> ValueChoices);

    private sealed record JobAttributeDefinition(int RowId, string Label, string Confidence, string ValueDisplayRule, string EditorKind, IReadOnlyList<JobAttributeValueChoice> ValueChoices);

    private sealed record JobAttributeValueChoice(int Value, string Label)
    {
        public string Display => $"{Label}：{Value}";
    }

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
