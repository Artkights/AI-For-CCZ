using System.Data;
using CCZModStudio.Core;
using CCZModStudio.Models;

namespace CCZModStudio.Formats;

public sealed class HexTableReader
{
    private readonly Dictionary<string, DataTable> _indexCache = new(StringComparer.Ordinal);

    public HexTableValidationResult Validate(CczProject project, HexTableDefinition table)
    {
        var warnings = new List<string>();
        var filePath = project.ResolveGameFile(table.FileName);
        var info = new FileInfo(filePath);
        var columnsMatchBytes = table.Columns.Count == table.ByteSizes.Count;
        if (!columnsMatchBytes)
        {
            warnings.Add($"列数量 {table.Columns.Count} 与 Bytes 数量 {table.ByteSizes.Count} 不一致。");
        }

        var padding = table.RowSize - table.PositiveBytesSum;
        if (padding < 0)
        {
            warnings.Add($"字段字节合计 {table.PositiveBytesSum} 大于行长 {table.RowSize}。");
        }
        else if (padding > 0)
        {
            warnings.Add($"每行存在 {padding} 字节未命名/保留区。保存已拆分字段时会保留这些原始字节。 ");
        }

        var fits = info.Exists && info.Length >= table.EndOffsetExclusive;
        if (info.Exists && !fits)
        {
            warnings.Add($"文件长度 {info.Length} 小于表结束偏移 {table.EndOffsetExclusive}。");
        }

        var profile = new CczEngineProfileService().Detect(project);
        var tableStatus = Ccz66RevisedLayout.ExactOrCompatibleTableStatus;
        var writeRisk = string.Empty;
        var isNative66 = false;
        var isCrossVersionFallback = false;
        var isReadOnlyEvidenceOnly = false;
        var semanticValidationStatus = string.Empty;
        var hiddenTailPolicy = string.Empty;
        var effectResolutionSource = string.Empty;
        if (Ccz66RevisedLayout.Is66(profile) && table.Version.Equals("6.6", StringComparison.OrdinalIgnoreCase))
        {
            isNative66 = !table.IsEvidenceReadOnlyTable && !table.ReadOnly;
            isReadOnlyEvidenceOnly = table.IsEvidenceReadOnlyTable || table.ReadOnly;
            tableStatus = isReadOnlyEvidenceOnly
                ? Ccz66RevisedLayout.ReadOnlyEvidenceOnlyTableStatus
                : Ccz66RevisedLayout.Native66TableStatus;
            writeRisk = isReadOnlyEvidenceOnly
                ? "6.6 table is evidence-only/read-only; write is blocked until the layout is verified."
                : "6.6 native table: target file, offset, row size, and capacity checks passed before read/write.";

            if (table.IsGeneratedCompatibilityTable)
            {
                warnings.Add($"6.6NativeGenerated: sourceTable={table.SourceTableName}; evidence={table.EvidenceStatus}; status={tableStatus}.");
            }

            if (isReadOnlyEvidenceOnly)
            {
                warnings.Add("ReadOnlyEvidenceOnly: this 6.6 table is exposed for inspection/preview only and is blocked from save.");
            }

            if (Ccz66ItemNameEncodingService.IsItemBaseTable(table))
            {
                semanticValidationStatus = "Qinger66ItemSemanticAudit";
                hiddenTailPolicy = Ccz66ItemNameEncodingService.HiddenTailPolicy;
                effectResolutionSource = "ItemEffectResolutionService";
                warnings.Add("Qinger66ItemSemanticAudit: fixed names are displayed up to the first NUL; hidden tail bytes are preserved on write.");
            }

            if (ItemEffectNameReader.IsItemEffectNameTable(table))
            {
                semanticValidationStatus = "Obsolete65NameBlockIn66ReadOnly";
                effectResolutionSource = Ccz66ItemEffectNameService.SourceName;
                if (!isReadOnlyEvidenceOnly)
                {
                    warnings.Add("Obsolete65NameBlockIn66: this generated 6.6 equipment-effect name table reuses old 6.5 offsets and is not used for 6.6 item effect display. Use the dedicated 0x9E800 name-slot service instead.");
                }
            }
        }
        else if (Ccz66RevisedLayout.Is66(profile))
        {
            tableStatus = Ccz66RevisedLayout.CrossVersionFallbackTableStatus;
            writeRisk = "CrossVersionFallbackWrite: 6.6 project resolved to a non-6.6 table; offsets and row definitions are not native 6.6 evidence.";
            isCrossVersionFallback = true;
            warnings.Add($"CrossVersionFallback: requestedPrefix=6.6; actualTableVersion={table.Version}; tableFallback=true; write reports will be marked CrossVersionFallbackWrite and should be treated as risky.");
        }

        return new HexTableValidationResult
        {
            Table = table,
            FilePath = filePath,
            FileExists = info.Exists,
            FileLength = info.Exists ? info.Length : 0,
            ColumnsMatchBytes = columnsMatchBytes,
            FitsInFile = fits,
            PaddingBytes = Math.Max(0, padding),
            TableStatus = tableStatus,
            WriteRisk = writeRisk,
            IsNative66 = isNative66,
            IsCrossVersionFallback = isCrossVersionFallback,
            IsReadOnlyEvidenceOnly = isReadOnlyEvidenceOnly,
            SemanticValidationStatus = semanticValidationStatus,
            HiddenTailPolicy = hiddenTailPolicy,
            EffectResolutionSource = effectResolutionSource,
            Warnings = warnings
        };
    }

    public TableReadResult Read(CczProject project, HexTableDefinition table, IReadOnlyList<HexTableDefinition> allTables)
    {
        using var operation = PerformanceMetrics.Begin("HexTable.Read", new Dictionary<string, string>
        {
            ["Table"] = table.TableName,
            ["File"] = table.FileName
        });
        var validation = Validate(project, table);
        var data = CreateDataTable(table);

        if (!validation.IsUsable)
        {
            return new TableReadResult { Table = table, Data = data, Validation = validation };
        }

        if (ItemEffectNameReader.IsItemEffectNameTable(table))
        {
            ReadItemEffectNameTable(project, table, data);
            return new TableReadResult { Table = table, Data = data, Validation = validation };
        }

        if (JobEffectNameReader.IsJobEffectNameTable(table))
        {
            data = new JobEffectNameReader().ReadTable(project, table);
            return new TableReadResult { Table = table, Data = data, Validation = validation };
        }

        var filePath = project.ResolveGameFile(table.FileName);
        DataTable? indexTable = null;
        if (table.Fields.Any(f => f.Kind == HexFieldKind.Derived) && !string.IsNullOrWhiteSpace(table.IndexTable))
        {
            indexTable = TryLoadIndexTable(project, table.IndexTable, allTables);
        }

        var rangeLength = checked(table.RowCount * table.RowSize);
        var tableBytes = ReadTableRange(filePath, table.DataPos, rangeLength);
        for (var rowIndex = 0; rowIndex < table.RowCount; rowIndex++)
        {
            var row = data.NewRow();
            var id = table.BeginId + rowIndex;
            row["ID"] = id;

            var rowStart = checked(rowIndex * table.RowSize);
            var rowBytes = tableBytes.AsSpan(rowStart, table.RowSize);
            var rowByteOffset = 0;
            foreach (var field in table.Fields)
            {
                if (field.Kind == HexFieldKind.Derived)
                {
                    row[field.ColumnName] = ResolveDerivedName(indexTable, rowIndex, id);
                    continue;
                }

                if (field.Size < 0 || rowByteOffset + field.Size > rowBytes.Length)
                    throw new InvalidDataException($"表 {table.TableName} 第 {rowIndex} 行字段 {field.ColumnName} 超出行长。");
                var bytes = rowBytes.Slice(rowByteOffset, field.Size).ToArray();
                rowByteOffset += field.Size;
                row[field.ColumnName] = DecodeField(field, bytes);
            }

            if (Ccz66ItemLayoutService.IsGenerated66ItemBaseTable(table))
            {
                Ccz66ItemLayoutService.ApplyDerivedDisplayValues(project, table, row, rowBytes.ToArray());
            }

            data.Rows.Add(row);
        }

        data.AcceptChanges();

        return new TableReadResult { Table = table, Data = data, Validation = validation };
    }

    public Task<TableReadResult> ReadAsync(
        CczProject project,
        HexTableDefinition table,
        IReadOnlyList<HexTableDefinition> allTables,
        CancellationToken cancellationToken = default)
        => Task.Run(() => Read(project, table, allTables), cancellationToken);

    public void Invalidate(IEnumerable<string> paths)
    {
        var normalized = paths.Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(ProjectResourceFingerprint.Normalize)
            .ToArray();
        if (normalized.Length == 0) return;
        foreach (var key in _indexCache.Keys.Where(key => normalized.Any(path => key.Contains(path, StringComparison.OrdinalIgnoreCase))).ToArray())
            _indexCache.Remove(key);
    }

    private static byte[] ReadTableRange(string path, long offset, int length)
    {
        if (length <= 0) return Array.Empty<byte>();
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete, 4096, FileOptions.RandomAccess);
        stream.Position = offset;
        var bytes = new byte[length];
        stream.ReadExactly(bytes);
        PerformanceMetrics.Increment("HexTable.RangeRead.Count");
        PerformanceMetrics.Increment("HexTable.RangeRead.Bytes", length);
        return bytes;
    }

    private static void ReadItemEffectNameTable(CczProject project, HexTableDefinition table, DataTable data)
    {
        var names = new ItemEffectNameReader().ReadNames(project, table);
        var row = data.NewRow();
        row["ID"] = table.BeginId;
        foreach (DataColumn column in data.Columns)
        {
            if (column.ColumnName == "ID") continue;
            if (TryParseLeadingHexByte(column.ColumnName, out var id) &&
                names.TryGetValue(id, out var name))
            {
                row[column.ColumnName] = name;
            }
            else
            {
                row[column.ColumnName] = string.Empty;
            }
        }

        data.Rows.Add(row);
        data.AcceptChanges();
    }

    private DataTable? TryLoadIndexTable(CczProject project, string indexTableName, IReadOnlyList<HexTableDefinition> allTables)
    {
        if (!HexTableNameResolver.TryResolveForProject(project, allTables, indexTableName, out var indexDefinition)) return null;
        var indexPath = project.ResolveGameFile(indexDefinition.FileName);
        var fingerprint = ProjectResourceFingerprint.CreateRange(
            indexPath,
            indexDefinition.DataPos,
            checked(indexDefinition.RowCount * indexDefinition.RowSize),
            "hex-index-v2");
        var cacheKey = string.Join("|", fingerprint.Path, fingerprint.Length, fingerprint.LastWriteTimeUtcTicks,
            fingerprint.ChangeGeneration, fingerprint.RangeSha256, indexDefinition.Id);
        if (_indexCache.TryGetValue(cacheKey, out var cached)) return cached;

        var validation = Validate(project, indexDefinition);
        if (!validation.IsUsable) return null;

        if (JobEffectNameReader.IsJobEffectNameTable(indexDefinition))
        {
            var jobEffectNames = new JobEffectNameReader().ReadTable(project, indexDefinition);
            _indexCache[cacheKey] = jobEffectNames;
            return jobEffectNames;
        }

        var result = Read(project, indexDefinition, allTables);
        _indexCache[cacheKey] = result.Data;
        return result.Data;
    }

    private static object ResolveDerivedName(DataTable? indexTable, int rowIndex, int id)
    {
        if (indexTable == null || rowIndex < 0 || rowIndex >= indexTable.Rows.Count) return $"#{id}";
        if (indexTable.Columns.Contains("名称")) return indexTable.Rows[rowIndex]["名称"];
        if (indexTable.Columns.Count > 1) return indexTable.Rows[rowIndex][1];
        return $"#{id}";
    }

    private static bool TryParseLeadingHexByte(string columnName, out int id)
    {
        var token = new string(columnName.TakeWhile(Uri.IsHexDigit).ToArray());
        return int.TryParse(token, System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture, out id);
    }

    private static DataTable CreateDataTable(HexTableDefinition table)
    {
        var data = new DataTable(table.TableName);
        data.Columns.Add("ID", typeof(int));
        foreach (var field in table.Fields)
        {
            var columnName = field.ColumnName;
            if (data.Columns.Contains(columnName))
            {
                columnName = $"{field.ColumnName}_{data.Columns.Count}";
                data.Columns.Add(columnName, typeof(string));
            }
            else
            {
                data.Columns.Add(columnName, typeof(object));
            }

            data.Columns[columnName]!.ExtendedProperties["FieldDefinition"] = field;
        }
        return data;
    }

    private static object DecodeField(HexFieldDefinition field, byte[] bytes)
    {
        return field.Kind switch
        {
            HexFieldKind.UInt8 => bytes.Length >= 1 ? bytes[0] : 0,
            HexFieldKind.UInt16 => bytes.Length >= 2 ? BitConverter.ToUInt16(bytes, 0) : 0,
            HexFieldKind.UInt32 => bytes.Length >= 4 ? BitConverter.ToUInt32(bytes, 0) : 0,
            HexFieldKind.FixedString => EncodingService.DecodeFixedString(bytes),
            HexFieldKind.RawBytes => BitConverter.ToString(bytes).Replace("-", " "),
            _ => string.Empty
        };
    }
}
