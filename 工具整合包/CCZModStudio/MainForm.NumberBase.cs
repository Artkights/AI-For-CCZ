using CCZModStudio.Core;
using CCZModStudio.Formats;
using CCZModStudio.Models;
using System.ComponentModel;
using System.Data;
using System.Diagnostics;
using System.Drawing.Imaging;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace CCZModStudio;

public sealed partial class MainForm
{
    private readonly record struct NumberBaseColumnInfo(Type? PreferredType, int MinHexDigits);

    private readonly record struct ParsedIntegerValue(bool IsNegative, ulong Magnitude);

    private void AttachNumberBaseHandlers(Control root)
    {
        if (!_numberBaseTrackedControls.Add(root)) return;

        if (root is DataGridView grid)
        {
            _numberBaseGrids.Add(grid);
            grid.CellFormatting += HandleNumberBaseCellFormatting;
            grid.CellParsing += HandleNumberBaseCellParsing;
        }

        if (root is TabControl tabs)
        {
            tabs.SelectedIndexChanged += (_, _) => RefreshCurrentPageNumberBaseDisplay(updateStatus: false);
        }

        root.ControlAdded += (_, e) =>
        {
            if (e.Control != null) AttachNumberBaseHandlers(e.Control);
        };
        foreach (Control child in root.Controls)
        {
            AttachNumberBaseHandlers(child);
        }
    }

    private void RefreshCurrentPageNumberBaseDisplay(bool updateStatus)
    {
        foreach (var grid in _numberBaseGrids.ToList())
        {
            if (grid.IsDisposed)
            {
                _numberBaseGrids.Remove(grid);
                continue;
            }

            if (IsGridInCurrentPage(grid))
            {
                grid.Invalidate();
            }
        }

        if (!updateStatus) return;
        SetStatus(_currentPageHexButton.Checked
            ? "当前页表格数字显示：16进制。"
            : "当前页表格数字显示：10进制。");
    }

    private void HandleNumberBaseCellFormatting(object? sender, DataGridViewCellFormattingEventArgs e)
    {
        if (sender is not DataGridView grid || e.RowIndex < 0 || e.ColumnIndex < 0) return;
        if (!IsGridInCurrentPage(grid)) return;
        if (!TryGetNumberBaseColumnInfo(grid, e.ColumnIndex, out var info)) return;
        if (!TryGetIntegerValue(e.Value, out var parsed)) return;

        if (_currentPageHexButton.Checked)
        {
            e.Value = FormatIntegerAsHex(parsed, info.MinHexDigits);
            e.FormattingApplied = true;
            return;
        }

        if (IsHexIntegerText(e.Value))
        {
            e.Value = FormatIntegerAsDecimal(parsed);
            e.FormattingApplied = true;
        }
    }

    private void HandleNumberBaseCellParsing(object? sender, DataGridViewCellParsingEventArgs e)
    {
        if (sender is not DataGridView grid || e.RowIndex < 0 || e.ColumnIndex < 0) return;
        if (!TryGetNumberBaseColumnInfo(grid, e.ColumnIndex, out var info)) return;
        var text = Convert.ToString(e.Value, CultureInfo.InvariantCulture);
        if (!TryParseIntegerInput(text, _currentPageHexButton.Checked, out var parsed)) return;

        var targetType = GetNumberBaseParsingType(grid, e.RowIndex, e.ColumnIndex, e.DesiredType, info);
        if (targetType == null) return;
        if (!TryConvertParsedIntegerToType(parsed, targetType, out var converted)) return;

        e.Value = converted;
        e.ParsingApplied = true;
    }

    private bool IsGridInCurrentPage(DataGridView grid)
    {
        if (_mainTabs.SelectedTab == null || !IsDescendantOf(grid, _mainTabs.SelectedTab)) return false;

        for (Control? current = grid; current != null; current = current.Parent)
        {
            if (current is TabPage page && page.Parent is TabControl tabs && tabs.SelectedTab != page)
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsDescendantOf(Control child, Control ancestor)
    {
        for (Control? current = child; current != null; current = current.Parent)
        {
            if (ReferenceEquals(current, ancestor)) return true;
        }

        return false;
    }

    private static bool TryGetNumberBaseColumnInfo(
        DataGridView grid,
        int columnIndex,
        out NumberBaseColumnInfo info)
    {
        info = default;
        if (columnIndex < 0 || columnIndex >= grid.Columns.Count) return false;

        var column = grid.Columns[columnIndex];
        if (column is DataGridViewComboBoxColumn or DataGridViewCheckBoxColumn or DataGridViewImageColumn or
            DataGridViewButtonColumn or DataGridViewLinkColumn)
        {
            return false;
        }

        if (TryGetDataColumn(grid, column, out var dataColumn))
        {
            if (dataColumn.ExtendedProperties["FieldDefinition"] is HexFieldDefinition field)
            {
                return TryGetFieldNumberBaseInfo(field, out info);
            }

            if (TryGetNumericTypeNumberBaseInfo(dataColumn.DataType, out info))
            {
                return true;
            }
        }

        if (TryGetNumericTypeNumberBaseInfo(column.ValueType, out info))
        {
            return true;
        }

        info = new NumberBaseColumnInfo(null, 0);
        return true;
    }

    private static bool TryGetDataColumn(DataGridView grid, DataGridViewColumn column, out DataColumn dataColumn)
    {
        dataColumn = null!;
        var table = grid.DataSource switch
        {
            DataTable directTable => directTable,
            DataView view => view.Table,
            BindingSource { DataSource: DataTable boundTable } => boundTable,
            BindingSource { DataSource: DataView boundView } => boundView.Table,
            _ => null
        };

        if (table == null) return false;
        var propertyName = column.DataPropertyName;
        if (string.IsNullOrWhiteSpace(propertyName) || !table.Columns.Contains(propertyName)) return false;

        dataColumn = table.Columns[propertyName]!;
        return true;
    }

    private static bool TryGetFieldNumberBaseInfo(HexFieldDefinition field, out NumberBaseColumnInfo info)
    {
        switch (field.Kind)
        {
            case HexFieldKind.UInt8:
                info = new NumberBaseColumnInfo(typeof(byte), 2);
                return true;
            case HexFieldKind.UInt16:
                info = new NumberBaseColumnInfo(typeof(ushort), 4);
                return true;
            case HexFieldKind.UInt32:
                info = new NumberBaseColumnInfo(typeof(uint), 8);
                return true;
            default:
                info = default;
                return false;
        }
    }

    private static bool TryGetNumericTypeNumberBaseInfo(Type? type, out NumberBaseColumnInfo info)
    {
        info = default;
        var normalized = NormalizeNullableType(type);
        if (normalized == null) return false;

        if (normalized == typeof(byte) || normalized == typeof(sbyte))
        {
            info = new NumberBaseColumnInfo(normalized, 2);
            return true;
        }

        if (normalized == typeof(ushort) || normalized == typeof(short))
        {
            info = new NumberBaseColumnInfo(normalized, 4);
            return true;
        }

        if (normalized == typeof(uint))
        {
            info = new NumberBaseColumnInfo(normalized, 8);
            return true;
        }

        if (normalized == typeof(int) || normalized == typeof(long) || normalized == typeof(ulong))
        {
            info = new NumberBaseColumnInfo(normalized, normalized == typeof(ulong) ? 16 : 0);
            return true;
        }

        return false;
    }

    private static Type? GetNumberBaseParsingType(
        DataGridView grid,
        int rowIndex,
        int columnIndex,
        Type? desiredType,
        NumberBaseColumnInfo info)
    {
        if (IsSupportedIntegerType(info.PreferredType)) return NormalizeNullableType(info.PreferredType);
        if (IsSupportedIntegerType(desiredType)) return NormalizeNullableType(desiredType);

        var value = rowIndex >= 0 && rowIndex < grid.Rows.Count && columnIndex >= 0 && columnIndex < grid.Columns.Count
            ? grid.Rows[rowIndex].Cells[columnIndex].Value
            : null;
        var valueType = value == null || value == DBNull.Value ? null : value.GetType();
        return IsSupportedIntegerType(valueType) ? NormalizeNullableType(valueType) : null;
    }

    private static bool IsSupportedIntegerType(Type? type)
    {
        var normalized = NormalizeNullableType(type);
        return normalized == typeof(byte) ||
               normalized == typeof(sbyte) ||
               normalized == typeof(ushort) ||
               normalized == typeof(short) ||
               normalized == typeof(uint) ||
               normalized == typeof(int) ||
               normalized == typeof(ulong) ||
               normalized == typeof(long);
    }

    private static Type? NormalizeNullableType(Type? type)
        => type == null ? null : Nullable.GetUnderlyingType(type) ?? type;

    private static bool TryGetIntegerValue(object? value, out ParsedIntegerValue parsed)
    {
        parsed = default;
        if (value == null || value == DBNull.Value) return false;

        switch (value)
        {
            case byte byteValue:
                parsed = new ParsedIntegerValue(false, byteValue);
                return true;
            case sbyte sbyteValue:
                parsed = sbyteValue < 0
                    ? new ParsedIntegerValue(true, (ulong)-sbyteValue)
                    : new ParsedIntegerValue(false, (ulong)sbyteValue);
                return true;
            case ushort ushortValue:
                parsed = new ParsedIntegerValue(false, ushortValue);
                return true;
            case short shortValue:
                parsed = shortValue < 0
                    ? new ParsedIntegerValue(true, (ulong)-shortValue)
                    : new ParsedIntegerValue(false, (ulong)shortValue);
                return true;
            case uint uintValue:
                parsed = new ParsedIntegerValue(false, uintValue);
                return true;
            case int intValue:
                parsed = intValue < 0
                    ? new ParsedIntegerValue(true, ToUnsignedMagnitude(intValue))
                    : new ParsedIntegerValue(false, (ulong)intValue);
                return true;
            case ulong ulongValue:
                parsed = new ParsedIntegerValue(false, ulongValue);
                return true;
            case long longValue:
                parsed = longValue < 0
                    ? new ParsedIntegerValue(true, ToUnsignedMagnitude(longValue))
                    : new ParsedIntegerValue(false, (ulong)longValue);
                return true;
            case string text:
                return TryParseIntegerInput(text, out parsed);
            default:
                return false;
        }
    }

    private static bool TryParseIntegerInput(string? text, out ParsedIntegerValue parsed)
        => TryParseIntegerInput(text, false, out parsed);

    private static bool TryParseIntegerInput(string? text, bool allowBareHex, out ParsedIntegerValue parsed)
    {
        parsed = default;
        text = text?.Trim();
        if (string.IsNullOrWhiteSpace(text)) return false;

        var isNegative = false;
        if (text[0] is '+' or '-')
        {
            isNegative = text[0] == '-';
            text = text[1..].TrimStart();
        }

        if (text.Length == 0) return false;

        ulong magnitude;
        if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            var hex = text[2..].Replace("_", string.Empty, StringComparison.Ordinal);
            if (hex.Length == 0 ||
                !ulong.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out magnitude))
            {
                return false;
            }
        }
        else if (allowBareHex)
        {
            var hex = text.Replace("_", string.Empty, StringComparison.Ordinal);
            if (hex.Length == 0 ||
                !ulong.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out magnitude))
            {
                return false;
            }
        }
        else
        {
            var decimalText = text.Replace("_", string.Empty, StringComparison.Ordinal);
            if (!ulong.TryParse(decimalText, NumberStyles.None, CultureInfo.InvariantCulture, out magnitude))
            {
                return false;
            }
        }

        parsed = new ParsedIntegerValue(isNegative, magnitude);
        return true;
    }

    private static string FormatIntegerAsHex(ParsedIntegerValue parsed, int minHexDigits)
    {
        var format = minHexDigits > 0
            ? "X" + minHexDigits.ToString(CultureInfo.InvariantCulture)
            : "X";
        var hex = parsed.Magnitude.ToString(format, CultureInfo.InvariantCulture);
        return parsed.IsNegative ? "-" + hex : hex;
    }

    private static string FormatIntegerAsDecimal(ParsedIntegerValue parsed)
    {
        if (!parsed.IsNegative) return parsed.Magnitude.ToString(CultureInfo.InvariantCulture);

        var minMagnitude = ToUnsignedMagnitude(long.MinValue);
        return parsed.Magnitude == minMagnitude
            ? long.MinValue.ToString(CultureInfo.InvariantCulture)
            : "-" + parsed.Magnitude.ToString(CultureInfo.InvariantCulture);
    }

    private static bool IsHexIntegerText(object? value)
    {
        var text = Convert.ToString(value, CultureInfo.InvariantCulture)?.TrimStart();
        if (string.IsNullOrWhiteSpace(text)) return false;
        if (text[0] is '+' or '-') text = text[1..].TrimStart();
        return text.StartsWith("0x", StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryConvertParsedIntegerToType(
        ParsedIntegerValue parsed,
        Type? targetType,
        out object converted)
    {
        converted = null!;
        targetType = NormalizeNullableType(targetType);
        if (targetType == null || targetType == typeof(object))
        {
            return TryConvertParsedIntegerToDefaultType(parsed, out converted);
        }

        if (targetType == typeof(byte))
        {
            if (parsed.IsNegative || parsed.Magnitude > byte.MaxValue) return false;
            converted = (byte)parsed.Magnitude;
            return true;
        }

        if (targetType == typeof(sbyte))
        {
            if (!TryConvertParsedIntegerToSigned(parsed, sbyte.MinValue, sbyte.MaxValue, out var value)) return false;
            converted = (sbyte)value;
            return true;
        }

        if (targetType == typeof(ushort))
        {
            if (parsed.IsNegative || parsed.Magnitude > ushort.MaxValue) return false;
            converted = (ushort)parsed.Magnitude;
            return true;
        }

        if (targetType == typeof(short))
        {
            if (!TryConvertParsedIntegerToSigned(parsed, short.MinValue, short.MaxValue, out var value)) return false;
            converted = (short)value;
            return true;
        }

        if (targetType == typeof(uint))
        {
            if (parsed.IsNegative || parsed.Magnitude > uint.MaxValue) return false;
            converted = (uint)parsed.Magnitude;
            return true;
        }

        if (targetType == typeof(int))
        {
            if (!TryConvertParsedIntegerToSigned(parsed, int.MinValue, int.MaxValue, out var value)) return false;
            converted = (int)value;
            return true;
        }

        if (targetType == typeof(ulong))
        {
            if (parsed.IsNegative) return false;
            converted = parsed.Magnitude;
            return true;
        }

        if (targetType == typeof(long))
        {
            if (!TryConvertParsedIntegerToSigned(parsed, long.MinValue, long.MaxValue, out var value)) return false;
            converted = value;
            return true;
        }

        return false;
    }

    private static bool TryConvertParsedIntegerToDefaultType(ParsedIntegerValue parsed, out object converted)
    {
        converted = null!;
        if (parsed.IsNegative)
        {
            if (!TryConvertParsedIntegerToSigned(parsed, long.MinValue, long.MaxValue, out var signed)) return false;
            converted = signed is >= int.MinValue and <= int.MaxValue ? (int)signed : signed;
            return true;
        }

        converted = parsed.Magnitude switch
        {
            <= int.MaxValue => (int)parsed.Magnitude,
            <= uint.MaxValue => (uint)parsed.Magnitude,
            <= long.MaxValue => (long)parsed.Magnitude,
            _ => parsed.Magnitude
        };
        return true;
    }

    private static bool TryConvertParsedIntegerToSigned(
        ParsedIntegerValue parsed,
        long min,
        long max,
        out long value)
    {
        value = 0;
        if (!parsed.IsNegative)
        {
            if (parsed.Magnitude > (ulong)max) return false;
            value = (long)parsed.Magnitude;
            return true;
        }

        var minMagnitude = ToUnsignedMagnitude(min);
        if (parsed.Magnitude > minMagnitude) return false;
        value = parsed.Magnitude == minMagnitude ? min : -(long)parsed.Magnitude;
        return true;
    }

    private static ulong ToUnsignedMagnitude(long value)
        => value >= 0 ? (ulong)value : (ulong)(-(value + 1)) + 1;

    private static bool HexOffsetEquals(string left, string right)
    {
        if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right)) return false;
        return TryParseHex(left, out var leftValue) && TryParseHex(right, out var rightValue)
            ? leftValue == rightValue
            : left.Equals(right, StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryParseHex(string value, out int parsed)
    {
        value = value.Trim();
        if (value.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) value = value[2..];
        return int.TryParse(value, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out parsed);
    }
}
