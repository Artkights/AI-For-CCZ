using System.Data;
using System.Globalization;
using CCZModStudio.Core;
using CCZModStudio.Models;

namespace CCZModStudio.Formats;

public sealed class ImageAssignmentService
{
    private const long ExpectedRImageOffset = 0xE1000;
    private const long ExpectedSImageOffset = 0xD2800;
    private readonly HexTableReader _reader = new();
    private readonly HexTableWriter _writer = new();

    public DataTable Load(CczProject project, IReadOnlyList<HexTableDefinition> tables)
    {
        var personTable = Find(project, tables, "6.5-0 人物");
        var jobTable = Find(project, tables, "6.5-4 详细兵种");
        var rTable = Find(project, tables, "6.5-0-4 R形象");
        var sTable = Find(project, tables, "6.5-0-5 S形象");
        EnsureImageTablesMatchBImageAssigner(rTable, sTable);

        var person = _reader.Read(project, personTable, tables);
        var jobs = _reader.Read(project, jobTable, tables);
        var r = _reader.Read(project, rTable, tables);
        var s = _reader.Read(project, sTable, tables);
        if (!person.Validation.IsUsable || !r.Validation.IsUsable || !s.Validation.IsUsable)
        {
            throw new InvalidOperationException("人物形象设定相关表有不可读取项，请先查看数据表诊断。 ");
        }

        var output = new DataTable("ImageAssignments");
        output.Columns.Add("ID", typeof(int));
        output.Columns.Add("名称", typeof(string));
        output.Columns.Add("头像编号", typeof(int));
        output.Columns.Add("职业", typeof(int));
        output.Columns.Add("职业名称", typeof(string));
        output.Columns.Add("R形象编号", typeof(int));
        output.Columns.Add("S形象编号", typeof(int));
        output.Columns.Add("R资源状态", typeof(string));
        output.Columns.Add("S资源状态", typeof(string));
        output.Columns["ID"]!.ReadOnly = true;
        output.Columns["名称"]!.ReadOnly = true;
        output.Columns["职业"]!.ReadOnly = true;
        output.Columns["职业名称"]!.ReadOnly = true;

        var jobNames = BuildJobNameLookup(jobs.Data);
        var count = Math.Min(person.Data.Rows.Count, Math.Min(r.Data.Rows.Count, s.Data.Rows.Count));
        for (var i = 0; i < count; i++)
        {
            var rId = Convert.ToInt32(r.Data.Rows[i]["R形象编号"], CultureInfo.InvariantCulture);
            var sId = Convert.ToInt32(s.Data.Rows[i]["S形象编号"], CultureInfo.InvariantCulture);
            var jobId = Convert.ToInt32(person.Data.Rows[i]["职业"], CultureInfo.InvariantCulture);
            output.Rows.Add(
                Convert.ToInt32(person.Data.Rows[i]["ID"], CultureInfo.InvariantCulture),
                Convert.ToString(person.Data.Rows[i]["名称"], CultureInfo.InvariantCulture) ?? string.Empty,
                Convert.ToInt32(person.Data.Rows[i]["头像"], CultureInfo.InvariantCulture),
                jobId,
                jobNames.GetValueOrDefault(jobId, $"职业{jobId}"),
                rId,
                sId,
                GetImageResourceStatus(project, "R", rId),
                GetImageResourceStatus(project, "S", sId));
        }

        output.AcceptChanges();
        return output;
    }

    public ImageAssignmentSaveResult SaveToTestCopy(CczProject project, IReadOnlyList<HexTableDefinition> tables, DataTable assignments)
        => Save(project, tables, assignments);

    public ImageAssignmentSaveResult Save(CczProject project, IReadOnlyList<HexTableDefinition> tables, DataTable assignments)
    {
        var personTable = Find(project, tables, "6.5-0 人物");
        var rTable = Find(project, tables, "6.5-0-4 R形象");
        var sTable = Find(project, tables, "6.5-0-5 S形象");
        EnsureImageTablesMatchBImageAssigner(rTable, sTable);
        var personRead = _reader.Read(project, personTable, tables);
        var rRead = _reader.Read(project, rTable, tables);
        var sRead = _reader.Read(project, sTable, tables);
        EnsureWritablePersonFaceField(personTable, personRead.Data);
        var changedFaceRows = new List<DataRow>();
        var changedRRows = new List<DataRow>();
        var changedSRows = new List<DataRow>();

        foreach (DataRow row in assignments.Rows)
        {
            if (row.RowState != DataRowState.Modified) continue;
            var index = assignments.Rows.IndexOf(row);
            var originalFace = Convert.ToInt32(row["头像编号", DataRowVersion.Original], CultureInfo.InvariantCulture);
            var currentFace = Convert.ToInt32(row["头像编号", DataRowVersion.Current], CultureInfo.InvariantCulture);
            var originalR = Convert.ToInt32(row["R形象编号", DataRowVersion.Original], CultureInfo.InvariantCulture);
            var currentR = Convert.ToInt32(row["R形象编号", DataRowVersion.Current], CultureInfo.InvariantCulture);
            var originalS = Convert.ToInt32(row["S形象编号", DataRowVersion.Original], CultureInfo.InvariantCulture);
            var currentS = Convert.ToInt32(row["S形象编号", DataRowVersion.Current], CultureInfo.InvariantCulture);

            if (originalFace != currentFace)
            {
                ValidateFieldValue(personTable, "头像", currentFace);
                personRead.Data.Rows[index]["头像"] = currentFace;
                changedFaceRows.Add(row);
            }

            if (originalR != currentR)
            {
                rRead.Data.Rows[index]["R形象编号"] = currentR;
                changedRRows.Add(row);
            }

            if (originalS != currentS)
            {
                sRead.Data.Rows[index]["S形象编号"] = currentS;
                changedSRows.Add(row);
            }
        }

        var saves = new List<TableSaveResult>();
        if (personRead.Data.GetChanges() != null)
        {
            saves.Add(_writer.Save(project, personTable, personRead.Data));
            VerifyImageAssignmentSave(project, tables, personTable, changedFaceRows, "头像编号", "头像");
        }

        if (rRead.Data.GetChanges() != null)
        {
            saves.Add(_writer.Save(project, rTable, rRead.Data));
            VerifyImageAssignmentSave(project, tables, rTable, changedRRows, "R形象编号", "R形象编号");
        }

        if (sRead.Data.GetChanges() != null)
        {
            saves.Add(_writer.Save(project, sTable, sRead.Data));
            VerifyImageAssignmentSave(project, tables, sTable, changedSRows, "S形象编号", "S形象编号");
        }

        assignments.AcceptChanges();

        return new ImageAssignmentSaveResult { Saves = saves };
    }

    private static HexTableDefinition Find(CczProject project, IReadOnlyList<HexTableDefinition> tables, string tableName) =>
        HexTableNameResolver.ResolveForProject(project, tables, tableName);

    private void VerifyImageAssignmentSave(
        CczProject project,
        IReadOnlyList<HexTableDefinition> tables,
        HexTableDefinition table,
        IReadOnlyList<DataRow> changedRows,
        string assignmentColumnName,
        string rereadColumnName)
    {
        var reread = _reader.Read(project, table, tables);
        if (!reread.Validation.IsUsable)
        {
            throw new InvalidOperationException("人物形象设定保存后重新读取失败，请查看诊断和备份。");
        }

        foreach (var row in changedRows)
        {
            var id = Convert.ToInt32(row["ID"], CultureInfo.InvariantCulture);
            var actualRow = FindRowById(reread.Data, id);
            if (actualRow == null)
            {
                throw new InvalidOperationException($"人物形象设定保存后复读校验失败：找不到 ID={id}。");
            }

            var expectedValue = Convert.ToString(row[assignmentColumnName, DataRowVersion.Current], CultureInfo.InvariantCulture) ?? string.Empty;
            var actualValue = Convert.ToString(actualRow[rereadColumnName], CultureInfo.InvariantCulture) ?? string.Empty;
            if (!string.Equals(expectedValue, actualValue, StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"人物形象设定保存后复读校验失败：ID={id} {assignmentColumnName} 期望 {expectedValue}，实际 {actualValue}。");
            }
        }
    }

    private static void EnsureWritablePersonFaceField(HexTableDefinition personTable, DataTable personData)
    {
        if (!personData.Columns.Contains("头像"))
        {
            throw new InvalidOperationException("人物形象设定保存失败：人物表缺少“头像”字段。");
        }

        var field = FindField(personTable, "头像");
        if (field is not { ConsumesBytes: true })
        {
            throw new InvalidOperationException("人物形象设定保存失败：人物表“头像”字段不可写。");
        }
    }

    private static void ValidateFieldValue(HexTableDefinition table, string columnName, int value)
    {
        var field = FindField(table, columnName);
        if (field == null)
        {
            throw new InvalidOperationException($"人物形象设定保存失败：找不到字段“{columnName}”。");
        }

        if (value < 0)
        {
            throw new InvalidOperationException($"人物形象设定保存失败：{columnName} 不能小于 0。");
        }

        var max = field.Kind switch
        {
            HexFieldKind.UInt8 => byte.MaxValue,
            HexFieldKind.UInt16 => ushort.MaxValue,
            HexFieldKind.UInt32 => uint.MaxValue,
            _ => field.Size > 0 && field.Size < sizeof(long)
                ? (1L << Math.Min(field.Size * 8, 62)) - 1
                : long.MaxValue
        };
        if (value > max)
        {
            throw new InvalidOperationException($"人物形象设定保存失败：{columnName}={value} 超出字段上限 {max}。");
        }
    }

    private static HexFieldDefinition? FindField(HexTableDefinition table, string columnName)
        => table.Fields.FirstOrDefault(field => field.ColumnName.Equals(columnName, StringComparison.Ordinal));

    private static DataRow? FindRowById(DataTable table, int id)
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

    private static IReadOnlyDictionary<int, string> BuildJobNameLookup(DataTable jobs)
    {
        var result = new Dictionary<int, string>();
        if (!jobs.Columns.Contains("ID") || !jobs.Columns.Contains("名称")) return result;
        foreach (DataRow row in jobs.Rows)
        {
            var id = Convert.ToInt32(row["ID"], CultureInfo.InvariantCulture);
            var name = Convert.ToString(row["名称"], CultureInfo.InvariantCulture) ?? string.Empty;
            result[id] = string.IsNullOrWhiteSpace(name) ? $"职业{id}" : name;
        }

        return result;
    }

    private static void EnsureImageTablesMatchBImageAssigner(HexTableDefinition rTable, HexTableDefinition sTable)
    {
        if (!string.Equals(rTable.Version, "6.5", StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(sTable.Version, "6.5", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (!rTable.FileName.Equals("Ekd5.exe", StringComparison.OrdinalIgnoreCase) ||
            !sTable.FileName.Equals("Ekd5.exe", StringComparison.OrdinalIgnoreCase) ||
            rTable.DataPos != ExpectedRImageOffset ||
            sTable.DataPos != ExpectedSImageOffset)
        {
            throw new InvalidOperationException(
                "人物形象设定表与 B形象指定器 6.5 配置不一致，已停止读取/写入。"
                + $" 期望 R=Ekd5.exe:{HexDisplayFormatter.FormatOffset(ExpectedRImageOffset)}，S=Ekd5.exe:{HexDisplayFormatter.FormatOffset(ExpectedSImageOffset)}；"
                + $" 实际 R={rTable.FileName}:{HexDisplayFormatter.FormatOffset(rTable.DataPos)}，S={sTable.FileName}:{HexDisplayFormatter.FormatOffset(sTable.DataPos)}。");
        }
    }

    public static string GetImageResourceStatus(CczProject project, string prefix, int id)
    {
        var resolver = new CharacterImageResourceService();
        var status = prefix.Equals("S", StringComparison.OrdinalIgnoreCase)
            ? resolver.BuildSStatus(project, id)
            : resolver.BuildRStatus(project, id);
        return $"{status.Status}：{status.ResourceName}";
    }

    public static string GetImageResourceFileName(string prefix, int id)
    {
        if (prefix.Equals("Face", StringComparison.OrdinalIgnoreCase))
        {
            var mapping = new CharacterImageResourceService().MapFaceId(id);
            var faceText = mapping.FaceImageNumbers.Count == 1
                ? mapping.FaceImageNumbers[0].ToString(CultureInfo.InvariantCulture)
                : $"{mapping.FaceImageNumbers.First()}-{mapping.FaceImageNumbers.Last()}";
            return $"Face.e5 图{faceText}";
        }

        if (prefix.Equals("S", StringComparison.OrdinalIgnoreCase))
        {
            return CharacterImageResourceService.BuildSMappingShortText(id);
        }

        var front = checked(id * 2 + 1);
        var back = checked(id * 2 + 2);
        return $"Pmapobj.e5 图{front}/{back}";
    }

    public static string GetImageResourcePath(CczProject project, string prefix, int id)
    {
        if (prefix.Equals("Face", StringComparison.OrdinalIgnoreCase))
        {
            return CharacterImageResourceService.ResolveFaceFile(project) ?? Path.Combine(project.GameRoot, "E5", "Face.e5");
        }

        if (prefix.Equals("S", StringComparison.OrdinalIgnoreCase))
        {
            return CharacterImageResourceService.ResolveGameFile(project, "Unit_atk.e5");
        }

        return CharacterImageResourceService.ResolveGameFile(project, "Pmapobj.e5");
    }


    private static string NormalizePrefix(string prefix) =>
        prefix.Equals("S", StringComparison.OrdinalIgnoreCase) ? "S" : "R";

    public static IReadOnlyList<DataRow> FilterRows(DataTable assignments, string keyword, bool missingOnly)
    {
        keyword = keyword.Trim();
        return assignments.Rows.Cast<DataRow>()
            .Where(row => !missingOnly || IsMissing(row))
            .Where(row => string.IsNullOrWhiteSpace(keyword) || MatchesKeyword(row, keyword))
            .ToList();
    }

    private static bool IsMissing(DataRow row) =>
        CharacterImageResourceService.IsMissingStatus(Convert.ToString(row["R资源状态"], CultureInfo.InvariantCulture) ?? string.Empty) ||
        CharacterImageResourceService.IsMissingStatus(Convert.ToString(row["S资源状态"], CultureInfo.InvariantCulture) ?? string.Empty);

    private static bool MatchesKeyword(DataRow row, string keyword)
    {
        var values = new List<string>();
        foreach (var columnName in new[] { "ID", "名称", "头像编号", "职业", "职业名称", "R形象编号", "S形象编号", "R资源状态", "S资源状态" })
        {
            if (!row.Table.Columns.Contains(columnName)) continue;
            values.Add(Convert.ToString(row[columnName], CultureInfo.InvariantCulture) ?? string.Empty);
        }

        AddResourceSearchTokens(row, values, "R");
        AddResourceSearchTokens(row, values, "S");
        return values.Any(value => value.Contains(keyword, StringComparison.CurrentCultureIgnoreCase));
    }

    private static void AddResourceSearchTokens(DataRow row, List<string> values, string prefix)
    {
        var columnName = prefix == "S" ? "S形象编号" : "R形象编号";
        if (!row.Table.Columns.Contains(columnName) ||
            !int.TryParse(Convert.ToString(row[columnName], CultureInfo.InvariantCulture), NumberStyles.Integer, CultureInfo.InvariantCulture, out var id))
        {
            return;
        }

        values.Add(prefix.Equals("S", StringComparison.OrdinalIgnoreCase) ? $"S{id}" : $"R{id}");
        values.Add(GetImageResourceFileName(prefix, id));
        if (prefix.Equals("R", StringComparison.OrdinalIgnoreCase))
        {
            values.Add($"Pmapobj.e5 图{checked(id * 2 + 1)}/{checked(id * 2 + 2)}");
        }
        else
        {
            values.Add("Unit_atk.e5");
            values.Add("Unit_mov.e5");
            values.Add("Unit_spc.e5");
        }
    }
}

public sealed class ImageAssignmentSaveResult
{
    public IReadOnlyList<TableSaveResult> Saves { get; init; } = Array.Empty<TableSaveResult>();
    public int ChangedBytes => Saves.Sum(x => x.ChangedBytes);
    public string BackupSummary => Saves.Count == 0 ? "无" : string.Join("; ", Saves.Select(x => x.BackupPath));
}
