using System.Data;
using CCZModStudio.Models;

namespace CCZModStudio.Core;

public sealed class RoleQuoteMappingService
{
    public const int RetreatQuoteCount = 49;
    public const int CriticalSpecialQuoteCount = 21;
    public const int CriticalGenericStartId = 21;
    public const int CriticalGenericGroupSize = 3;
    public const int CriticalSpecialRoleTableOffset = 0x89C30;

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

            using var reader = new BinaryReader(stream);
            stream.Position = CriticalSpecialRoleTableOffset;
            var ids = new List<int>(CriticalSpecialQuoteCount);
            for (var i = 0; i < CriticalSpecialQuoteCount; i++)
            {
                ids.Add(reader.ReadInt32());
            }

            return ids;
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

    private static int ReadInt(DataRow row, string columnName)
        => Convert.ToInt32(row[columnName], System.Globalization.CultureInfo.InvariantCulture);

    private static DataRow? TryFindRowById(DataTable table, int id)
    {
        foreach (DataRow row in table.Rows)
        {
            if (Convert.ToInt32(row["ID"], System.Globalization.CultureInfo.InvariantCulture) == id)
            {
                return row;
            }
        }

        return null;
    }
}

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
