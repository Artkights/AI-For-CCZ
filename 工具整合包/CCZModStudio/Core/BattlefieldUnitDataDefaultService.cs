using CCZModStudio.Formats;
using CCZModStudio.Models;
using System.Data;
using System.Globalization;

namespace CCZModStudio.Core;

public sealed class BattlefieldUnitDataDefaultService
{
    private readonly CczEngineProfileService _engineProfileService = new();
    private readonly HexTableReader _tableReader = new();

    public BattlefieldUnitDataDefaults LoadPersonDefaults(
        CczProject project,
        IReadOnlyList<HexTableDefinition> tables,
        int personId)
    {
        var profile = _engineProfileService.Detect(project);
        var itemNames = BuildNameMap(project, tables, profile.TableHints.ItemLowTable)
            .Concat(BuildNameMap(project, tables, profile.TableHints.ItemHighTable))
            .GroupBy(pair => pair.Key)
            .ToDictionary(group => group.Key, group => group.Last().Value);
        var jobNames = BuildNameMap(project, tables, profile.TableHints.DetailedJobTable);

        try
        {
            if (!HexTableNameResolver.TryResolveForProject(project, tables, profile.TableHints.PersonTable, out var personTable))
            {
                return Missing(personId, itemNames, jobNames, $"未找到人物表：{profile.TableHints.PersonTable}");
            }

            var read = _tableReader.Read(project, personTable, tables);
            if (!read.Validation.IsUsable || !read.Data.Columns.Contains("ID"))
            {
                return Missing(personId, itemNames, jobNames, $"人物表不可读：{personTable.TableName}");
            }

            var row = read.Data.Rows.Cast<DataRow>()
                .FirstOrDefault(candidate => ReadInt(candidate, "ID") == personId);
            if (row == null)
            {
                return Missing(personId, itemNames, jobNames, $"人物表缺少 ID={personId}");
            }

            var name = ReadString(row, "名称", "姓名", "名字");
            var abilities = new Dictionary<int, int>();
            AddAbility(abilities, 10, row, "武力");
            AddAbility(abilities, 11, row, "统帅", "统率");
            AddAbility(abilities, 12, row, "智力");
            AddAbility(abilities, 13, row, "敏捷");
            AddAbility(abilities, 14, row, "运气", "士气");

            return new BattlefieldUnitDataDefaults
            {
                PersonId = personId,
                PersonName = string.IsNullOrWhiteSpace(name) ? $"人物{personId}" : name,
                Found = true,
                Source = personTable.TableName,
                JobId = ReadNullableInt(row, "职业", "兵种"),
                Level = ReadNullableInt(row, "人物等级", "等级"),
                Experience = ReadNullableInt(row, "人物经验值", "经验"),
                AllyFlag = ReadNullableInt(row, "我军标识"),
                WeaponId = ReadNullableInt(row, "武器"),
                WeaponLevel = ReadNullableInt(row, "武器等级"),
                WeaponExperience = ReadNullableInt(row, "武器经验值"),
                ArmorId = ReadNullableInt(row, "防具"),
                ArmorLevel = ReadNullableInt(row, "防具等级"),
                ArmorExperience = ReadNullableInt(row, "防具经验值"),
                AssistId = ReadNullableInt(row, "辅助"),
                Abilities = abilities,
                ItemNames = itemNames,
                JobNames = jobNames
            };
        }
        catch (Exception ex)
        {
            return Missing(personId, itemNames, jobNames, "读取 Data.e5 人物默认值失败：" + ex.Message);
        }
    }

    private BattlefieldUnitDataDefaults Missing(
        int personId,
        IReadOnlyDictionary<int, string> itemNames,
        IReadOnlyDictionary<int, string> jobNames,
        string source)
        => new()
        {
            PersonId = personId,
            PersonName = $"人物{personId}",
            Found = false,
            Source = source,
            ItemNames = itemNames,
            JobNames = jobNames
        };

    public static int ToScriptEquipmentCode(int? itemId, ItemCategoryBoundary boundary, BattlefieldEquipmentSlot slot)
    {
        if (!itemId.HasValue)
        {
            return 0;
        }

        if (itemId.Value < 0)
        {
            return 1;
        }

        var start = slot switch
        {
            BattlefieldEquipmentSlot.Weapon => boundary.WeaponStartId,
            BattlefieldEquipmentSlot.Armor => boundary.DefenseStartId,
            BattlefieldEquipmentSlot.Assist => boundary.AccessoryStartId,
            _ => 0
        };
        return Math.Max(0, itemId.Value - start) + 2;
    }

    public static int? FromScriptEquipmentCode(int? scriptCode, ItemCategoryBoundary boundary, BattlefieldEquipmentSlot slot, int? dataDefaultItemId)
    {
        if (!scriptCode.HasValue || scriptCode.Value == 0)
        {
            return dataDefaultItemId;
        }

        if (scriptCode.Value == 1)
        {
            return -1;
        }

        var start = slot switch
        {
            BattlefieldEquipmentSlot.Weapon => boundary.WeaponStartId,
            BattlefieldEquipmentSlot.Armor => boundary.DefenseStartId,
            BattlefieldEquipmentSlot.Assist => boundary.AccessoryStartId,
            _ => 0
        };
        return start + scriptCode.Value - 2;
    }

    private IReadOnlyDictionary<int, string> BuildNameMap(
        CczProject project,
        IReadOnlyList<HexTableDefinition> tables,
        string tableName)
    {
        var result = new Dictionary<int, string>();
        try
        {
            if (!HexTableNameResolver.TryResolveForProject(project, tables, tableName, out var table)) return result;
            var read = _tableReader.Read(project, table, tables);
            if (!read.Validation.IsUsable || !read.Data.Columns.Contains("ID")) return result;
            var nameColumn = FindColumn(read.Data, "名称", "姓名", "名字");
            foreach (DataRow row in read.Data.Rows)
            {
                var id = ReadInt(row, "ID");
                if (!id.HasValue) continue;
                var name = !string.IsNullOrWhiteSpace(nameColumn)
                    ? Convert.ToString(row[nameColumn], CultureInfo.InvariantCulture)?.Trim()
                    : string.Empty;
                if (!string.IsNullOrWhiteSpace(name)) result[id.Value] = name!;
            }
        }
        catch
        {
            result.Clear();
        }

        return result;
    }

    private static void AddAbility(Dictionary<int, int> abilities, int abilityId, DataRow row, params string[] names)
    {
        var value = ReadNullableInt(row, names);
        if (value.HasValue) abilities[abilityId] = value.Value;
    }

    private static int? ReadInt(DataRow row, string columnName)
        => ReadNullableInt(row, columnName);

    private static int? ReadNullableInt(DataRow row, params string[] names)
    {
        var column = FindColumn(row.Table, names);
        if (string.IsNullOrWhiteSpace(column)) return null;
        var value = row[column];
        if (value == null || value == DBNull.Value) return null;
        if (value is int intValue) return intValue;
        return int.TryParse(Convert.ToString(value, CultureInfo.InvariantCulture), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : null;
    }

    private static string ReadString(DataRow row, params string[] names)
    {
        var column = FindColumn(row.Table, names);
        return string.IsNullOrWhiteSpace(column)
            ? string.Empty
            : Convert.ToString(row[column], CultureInfo.InvariantCulture)?.Trim() ?? string.Empty;
    }

    private static string FindColumn(DataTable data, params string[] names)
    {
        foreach (var name in names)
        {
            if (data.Columns.Contains(name)) return name;
        }

        foreach (var name in names)
        {
            var column = data.Columns.Cast<DataColumn>()
                .FirstOrDefault(candidate => candidate.ColumnName.Contains(name, StringComparison.Ordinal));
            if (column != null) return column.ColumnName;
        }

        return string.Empty;
    }
}

public enum BattlefieldEquipmentSlot
{
    Weapon,
    Armor,
    Assist
}
