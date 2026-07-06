using System.Data;
using System.Globalization;
using CCZModStudio.Formats;
using CCZModStudio.Models;

namespace CCZModStudio.Core;

public static class Ccz66ItemLayoutService
{
    public const int RowSize = 0x19;
    public const int NameOffset = 0x00;
    public const int NameSize = 0x0F;
    public const int IconOffset = 0x0F;
    public const int Reserved10Offset = 0x10;
    public const int TypeOffset = 0x11;
    public const int RawEffectMarkerOffset = 0x12;
    public const int PriceOffset = 0x13;
    public const int LegacyIconOrReserved14Offset = 0x14;
    public const int InitialAbilityOffset = 0x15;
    public const int EffectValueOffset = 0x16;
    public const int GrowthOffset = 0x17;
    public const int CatalogOffset = 0x18;

    public const string Reserved10ColumnName = "6.6保留10";
    public const string RawEffectMarkerColumnName = "6.6原始类别标记";
    public const string LegacyIconOrReserved14ColumnName = "6.6保留14";
    public const string DisplayEffectColumnName = "装备特效号";

    public static bool IsGenerated66ItemBaseTable(HexTableDefinition table)
        => table.Version.Equals(Ccz66RevisedLayout.Version, StringComparison.OrdinalIgnoreCase) &&
           table.RowSize == RowSize &&
           table.IsGeneratedCompatibilityTable &&
           Ccz66ItemNameEncodingService.IsItemBaseTable(table) &&
           table.Fields.Any(field => field.ColumnName.Equals(RawEffectMarkerColumnName, StringComparison.Ordinal));

    public static bool IsSourceItemBaseTable(HexTableDefinition table)
    {
        if (table.RowSize != RowSize) return false;
        if (!table.Version.Equals("6.5", StringComparison.OrdinalIgnoreCase)) return false;
        if (!table.FileName.Equals("Data.e5", StringComparison.OrdinalIgnoreCase) &&
            !table.FileName.Equals("Star.e5", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (table.Fields.Count != 9) return false;
        if (table.Fields[0].Kind != HexFieldKind.FixedString || table.Fields[0].Size != 17) return false;
        if (!Ccz66ItemNameEncodingService.IsNameColumn(table.Fields[0].ColumnName)) return false;
        if (!table.Fields.Skip(1).All(field => field.Kind == HexFieldKind.UInt8 && field.Size == 1)) return false;

        var key = HexTableNameResolver.BuildRangeAgnosticSemanticKey(table.TableName);
        return (key.Contains("物品", StringComparison.Ordinal) || key.Contains("鐗╁搧", StringComparison.Ordinal)) &&
               !key.Contains("说明", StringComparison.Ordinal) &&
               !key.Contains("璇存槑", StringComparison.Ordinal) &&
               !key.Contains("特效", StringComparison.Ordinal) &&
               !key.Contains("鐗规晥", StringComparison.Ordinal);
    }

    public static HexTableDefinition CloneSourceItemTableFor66(
        HexTableDefinition source,
        string tableName,
        int id,
        bool writable,
        string reason)
    {
        var sourceFields = source.Fields.ToArray();
        var name = sourceFields[0];
        var type = sourceFields[1];
        var rawEffect = sourceFields[2];
        var price = sourceFields[3];
        var icon = sourceFields[4];
        var initial = sourceFields[5];
        var effectValue = sourceFields[6];
        var growth = sourceFields[7];
        var catalog = sourceFields[8];

        var fields = new[]
        {
            CloneField(name, size: NameSize, kind: HexFieldKind.FixedString),
            CloneField(icon, size: 1, kind: HexFieldKind.UInt8),
            HiddenField(Reserved10ColumnName),
            CloneField(type, size: 1, kind: HexFieldKind.UInt8),
            HiddenField(RawEffectMarkerColumnName),
            CloneField(rawEffect, columnName: DisplayEffectColumnName, size: 0, kind: HexFieldKind.Derived),
            CloneField(price, size: 1, kind: HexFieldKind.UInt8),
            HiddenField(LegacyIconOrReserved14ColumnName),
            CloneField(initial, size: 1, kind: HexFieldKind.UInt8),
            CloneField(effectValue, size: 1, kind: HexFieldKind.UInt8),
            CloneField(growth, size: 1, kind: HexFieldKind.UInt8),
            CloneField(catalog, size: 1, kind: HexFieldKind.UInt8)
        };

        return new HexTableDefinition
        {
            Id = id,
            Enabled = source.Enabled,
            TableName = tableName,
            FileName = source.FileName,
            DataPos = source.DataPos,
            RowCount = source.RowCount,
            RowSize = source.RowSize,
            Columns = fields.Select(field => field.ColumnName).ToArray(),
            ByteSizes = fields.Select(field => field.Size).ToArray(),
            IndexTable = RewriteIndexTable(source.IndexTable),
            BeginId = source.BeginId,
            OnMem = source.OnMem,
            ReadOnly = !writable || source.ReadOnly,
            Version = Ccz66RevisedLayout.Version,
            Fields = fields,
            EvidenceStatus = writable
                ? "Native66QingerItemLayoutVerified"
                : "ReadOnly66EvidenceOnly",
            SourceTableName = source.TableName,
            IsGeneratedCompatibilityTable = true,
            IsEvidenceReadOnlyTable = !writable
        };
    }

    public static void ApplyDerivedDisplayValues(
        CczProject project,
        HexTableDefinition table,
        DataRow row,
        byte[] rowBytes)
    {
        if (!IsGenerated66ItemBaseTable(table)) return;

        SetDisplayEffect(row, rowBytes);
    }

    public static int ResolveDisplayedEffectId(byte[] rowBytes)
    {
        var marker = ReadByte(rowBytes, RawEffectMarkerOffset);
        var typeId = ReadByte(rowBytes, TypeOffset);
        return IsAccessoryOrConsumableMarker(marker) ? typeId : marker;
    }

    public static int ResolveRawMarker(byte[] rowBytes)
        => ReadByte(rowBytes, RawEffectMarkerOffset);

    public static bool IsAccessoryOrConsumableMarker(int marker)
        => marker is 2 or 3;

    public static bool IsDisplayEffectColumn(HexFieldDefinition field)
        => field.ColumnName.Equals(DisplayEffectColumnName, StringComparison.Ordinal) &&
           field.Kind == HexFieldKind.Derived &&
           field.Size == 0;

    public static void EncodeDerivedWrites(
        HexTableDefinition table,
        DataRow row,
        byte[] rowBuffer,
        List<WriteOperationChange> changes,
        int rowIndex,
        long rowOffset)
    {
        if (!IsGenerated66ItemBaseTable(table) || !row.Table.Columns.Contains(DisplayEffectColumnName))
        {
            return;
        }

        var displayColumn = row.Table.Columns[DisplayEffectColumnName]!;
        if (displayColumn.ExtendedProperties["FieldDefinition"] is not HexFieldDefinition field ||
            !IsDisplayEffectColumn(field) ||
            !IsCellChanged(row, displayColumn.Ordinal))
        {
            return;
        }

        var newEffectId = ParseByteCell(row[displayColumn], DisplayEffectColumnName);
        var marker = ReadByte(rowBuffer, RawEffectMarkerOffset);
        if (IsAccessoryOrConsumableMarker(marker))
        {
            if (row.Table.Columns.Contains("类型") && IsCellChanged(row, row.Table.Columns["类型"]!.Ordinal))
            {
                var newTypeId = ParseByteCell(row["类型"], "类型");
                if (newTypeId != newEffectId)
                {
                    throw new InvalidOperationException(
                        $"6.6 辅助/道具行 ID={row["ID"]} 的“类型”和“装备特效号”映射到同一物理字节 row+0x11；两者不能同时改成不同值。");
                }
            }

            if (row.Table.Columns.Contains("类型"))
            {
                row["类型"] = newEffectId;
            }

            if (rowBuffer[TypeOffset] != newEffectId)
            {
                rowBuffer[TypeOffset] = newEffectId;
            }

            changes.Add(BuildChange(table, row, rowIndex, displayColumn.Ordinal, field, rowOffset + TypeOffset, 1,
                $"6.6 辅助/道具行：可见“装备特效号”写入 row+0x11，保留 row+0x12 类别标记 {marker}。"));
            return;
        }

        if (rowBuffer[RawEffectMarkerOffset] != newEffectId)
        {
            rowBuffer[RawEffectMarkerOffset] = newEffectId;
        }

        changes.Add(BuildChange(table, row, rowIndex, displayColumn.Ordinal, field, rowOffset + RawEffectMarkerOffset, 1,
            "6.6 普通装备行：可见“装备特效号”写入 row+0x12。"));
    }

    public static bool ShouldSkipPhysicalFieldWrite(
        HexTableDefinition table,
        HexFieldDefinition field,
        byte[] rowBuffer)
        => IsGenerated66ItemBaseTable(table) &&
           field.ColumnName.Equals("类型", StringComparison.Ordinal) &&
           IsAccessoryOrConsumableMarker(ReadByte(rowBuffer, RawEffectMarkerOffset));

    private static void SetDisplayEffect(DataRow row, byte[] rowBytes)
    {
        if (!row.Table.Columns.Contains(DisplayEffectColumnName)) return;
        row[DisplayEffectColumnName] = ResolveDisplayedEffectId(rowBytes);
    }

    private static int ReadByte(byte[] rowBytes, int offset)
        => offset >= 0 && offset < rowBytes.Length ? rowBytes[offset] : 0;

    private static HexFieldDefinition CloneField(
        HexFieldDefinition source,
        string? columnName = null,
        int? size = null,
        HexFieldKind? kind = null,
        bool? visibleByDefault = null)
        => new()
        {
            ColumnName = columnName ?? source.ColumnName,
            Size = size ?? source.Size,
            Kind = kind ?? source.Kind,
            VisibleByDefault = visibleByDefault ?? source.VisibleByDefault
        };

    private static HexFieldDefinition HiddenField(string columnName)
        => new()
        {
            ColumnName = columnName,
            Size = 1,
            Kind = HexFieldKind.UInt8,
            VisibleByDefault = false
        };

    private static string RewriteIndexTable(string indexTable)
    {
        if (string.IsNullOrWhiteSpace(indexTable)) return indexTable;
        if (indexTable.Equals("6.5", StringComparison.OrdinalIgnoreCase)) return Ccz66RevisedLayout.Version;
        return HexTableNameResolver.BuildVersionedTableName(Ccz66RevisedLayout.Version, indexTable);
    }

    private static bool IsCellChanged(DataRow row, int columnIndex)
    {
        if (row.RowState == DataRowState.Added) return true;
        if (row.RowState == DataRowState.Unchanged) return false;
        if (!row.HasVersion(DataRowVersion.Original)) return true;

        var original = row[columnIndex, DataRowVersion.Original];
        var current = row[columnIndex, DataRowVersion.Current];
        return !Equals(Convert.ToString(original, CultureInfo.InvariantCulture), Convert.ToString(current, CultureInfo.InvariantCulture));
    }

    private static byte ParseByteCell(object? value, string fieldName)
    {
        var text = Convert.ToString(value, CultureInfo.InvariantCulture)?.Trim() ?? string.Empty;
        if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            var parsedHex = Convert.ToInt32(text[2..], 16);
            if (parsedHex < byte.MinValue || parsedHex > byte.MaxValue) throw OutOfRange(fieldName, text);
            return (byte)parsedHex;
        }

        if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) ||
            parsed < byte.MinValue ||
            parsed > byte.MaxValue)
        {
            throw OutOfRange(fieldName, text);
        }

        return (byte)parsed;
    }

    private static Exception OutOfRange(string fieldName, string text)
        => new InvalidOperationException($"字段 {fieldName} 的值超出范围：{text}，允许 0..255");

    private static WriteOperationChange BuildChange(
        HexTableDefinition table,
        DataRow row,
        int rowIndex,
        int columnIndex,
        HexFieldDefinition field,
        long absoluteOffset,
        int byteLength,
        string annotation)
    {
        var oldValue = row.HasVersion(DataRowVersion.Original)
            ? Convert.ToString(row[columnIndex, DataRowVersion.Original], CultureInfo.InvariantCulture) ?? string.Empty
            : string.Empty;
        var newValue = Convert.ToString(row[columnIndex, DataRowVersion.Current], CultureInfo.InvariantCulture) ?? string.Empty;
        return new WriteOperationChange
        {
            Category = "表格字段",
            TableName = table.TableName,
            RowIndex = rowIndex,
            ColumnName = field.ColumnName,
            OffsetHex = HexDisplayFormatter.FormatOffset(absoluteOffset),
            ByteLength = byteLength,
            OldValue = oldValue,
            NewValue = newValue,
            Annotation = annotation
        };
    }
}
