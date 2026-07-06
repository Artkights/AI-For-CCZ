using System.Globalization;
using CCZModStudio.Formats;
using CCZModStudio.Models;

namespace CCZModStudio.Core;

public static class Ccz66ItemNameEncodingService
{
    public const string HiddenTailPolicy = "Qinger66VisibleName15BytesPreserveFollowingLayoutBytes";

    public static bool Is66ItemNameField(CczProject project, HexTableDefinition table, HexFieldDefinition field)
        => Ccz66RevisedLayout.Is66(project) &&
           table.Version.Equals(Ccz66RevisedLayout.Version, StringComparison.OrdinalIgnoreCase) &&
           IsItemBaseTable(table) &&
           field.Kind == HexFieldKind.FixedString &&
           field.Size is 15 or 17 &&
           IsNameColumn(field.ColumnName);

    public static string DecodeVisibleName(ReadOnlySpan<byte> bytes)
        => EncodingService.DecodeFixedString(bytes);

    public static string GetHiddenTailHex(ReadOnlySpan<byte> bytes)
    {
        var nul = bytes.IndexOf((byte)0x00);
        if (nul < 0 || nul + 1 >= bytes.Length) return string.Empty;
        var tail = bytes[(nul + 1)..];
        return tail.Length == 0 ? string.Empty : BitConverter.ToString(tail.ToArray()).Replace("-", " ");
    }

    public static byte[] EncodePreservingHiddenTail(ReadOnlySpan<byte> originalBytes, string value)
    {
        var output = originalBytes.ToArray();
        var firstNul = output.AsSpan().IndexOf((byte)0x00);
        var hasInlineHiddenTail = originalBytes.Length != Ccz66ItemLayoutService.NameSize;
        var visibleCapacity = hasInlineHiddenTail && firstNul >= 0 ? firstNul : output.Length;
        if (visibleCapacity <= 0)
        {
            visibleCapacity = output.Length;
        }

        EncodingService.EnsureCodePages();
        var encoded = EncodingService.Gbk.GetBytes(value ?? string.Empty);
        if (encoded.Length > visibleCapacity)
        {
            throw new InvalidOperationException(
                $"6.6 item name is {encoded.Length} GBK bytes, but the visible name capacity before hidden tail is {visibleCapacity} bytes. Hidden tail bytes are preserved by default.");
        }

        if (hasInlineHiddenTail && firstNul >= 0)
        {
            output.AsSpan(0, visibleCapacity).Fill(0x20);
        }
        else
        {
            Array.Clear(output, 0, visibleCapacity);
        }

        Buffer.BlockCopy(encoded, 0, output, 0, encoded.Length);
        return output;
    }

    public static bool IsItemBaseTable(HexTableDefinition table)
    {
        if (table.RowSize != 25) return false;
        if (table.Fields.Count < 9) return false;
        if (!table.FileName.Equals("Data.e5", StringComparison.OrdinalIgnoreCase) &&
            !table.FileName.Equals("Star.e5", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var key = HexTableNameResolver.BuildRangeAgnosticSemanticKey(table.TableName);
        if (key.Contains("璇存槑", StringComparison.Ordinal) ||
            key.Contains("说明", StringComparison.Ordinal) ||
            key.Contains("鐗规晥", StringComparison.Ordinal) ||
            key.Contains("特效", StringComparison.Ordinal))
        {
            return false;
        }

        if (table.Fields[0].Kind != HexFieldKind.FixedString ||
            table.Fields[0].Size is not (15 or 17) ||
            !IsNameColumn(table.Fields[0].ColumnName) ||
            table.PositiveBytesSum != table.RowSize)
        {
            return false;
        }

        if (table.Fields[0].Size == 17)
        {
            return table.Fields.Skip(1).Take(8).All(field => field.Kind == HexFieldKind.UInt8 && field.Size == 1);
        }

        return table.Fields.Any(field => field.ColumnName.Equals(Ccz66ItemLayoutService.RawEffectMarkerColumnName, StringComparison.Ordinal)) &&
               table.Fields.Count(field => field.ConsumesBytes) == 11 &&
               table.Fields.Where(field => !ReferenceEquals(field, table.Fields[0]) && field.ConsumesBytes)
                   .All(field => field.Kind == HexFieldKind.UInt8 && field.Size == 1);
    }

    public static bool IsNameColumn(string columnName)
        => columnName.Equals("鍚嶇О", StringComparison.OrdinalIgnoreCase) ||
           columnName.Equals("名称", StringComparison.OrdinalIgnoreCase);

    public static string FormatRawBytes(ReadOnlySpan<byte> bytes)
        => BitConverter.ToString(bytes.ToArray()).Replace("-", " ");

    public static bool ContainsVisibleControlChars(string value)
        => value.Any(ch => char.IsControl(ch) && ch != '\r' && ch != '\n' && ch != '\t');

    public static int ReadIntField(byte[] rowBuffer, HexTableDefinition table, string columnToken)
    {
        var offset = 0;
        foreach (var field in table.Fields)
        {
            if (field.ConsumesBytes)
            {
                if (field.ColumnName.Contains(columnToken, StringComparison.Ordinal))
                {
                    return field.Kind switch
                    {
                        HexFieldKind.UInt8 when offset < rowBuffer.Length => rowBuffer[offset],
                        HexFieldKind.UInt16 when offset + 1 < rowBuffer.Length => BitConverter.ToUInt16(rowBuffer, offset),
                        HexFieldKind.UInt32 when offset + 3 < rowBuffer.Length => checked((int)BitConverter.ToUInt32(rowBuffer, offset)),
                        _ => 0
                    };
                }

                offset += field.Size;
            }
        }

        return 0;
    }
}
