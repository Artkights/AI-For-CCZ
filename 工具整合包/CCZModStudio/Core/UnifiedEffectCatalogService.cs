using System.Data;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using CCZModStudio.Formats;
using CCZModStudio.Models;

namespace CCZModStudio.Core;

public sealed class UnifiedEffectCatalog
{
    public List<EffectChannelReference> PersonalJobEffects { get; set; } = [];
    public List<EffectChannelReference> ItemEffects { get; set; } = [];

    public EffectChannelReference ResolvePersonalJob(int effectId)
        => PersonalJobEffects.FirstOrDefault(item => item.EffectId == effectId)
           ?? UnifiedEffectCatalogService.CreateUnknown(EffectChannelKind.PersonalJob, effectId);

    public EffectChannelReference ResolveItem(int effectId)
        => ItemEffects.FirstOrDefault(item => item.EffectId == effectId)
           ?? UnifiedEffectCatalogService.CreateUnknown(EffectChannelKind.Item, effectId);
}

/// <summary>
/// Shared effect identity and binding catalog used by editors, inventory and MCP.
/// Personal and job effects share the 6.X-7 name space; item effects remain a separate channel.
/// </summary>
public sealed class UnifiedEffectCatalogService
{
    private readonly HexTableReader _reader = new();
    private readonly JobEffectNameReader _jobNames = new();
    private readonly ItemEffectNameReader _itemNames = new();
    private readonly ItemEffectCatalogService _itemCatalog = new();
    private readonly CczEngineProfileService _profiles = new();

    public UnifiedEffectCatalog Build(CczProject project, IReadOnlyList<HexTableDefinition> tables)
    {
        var result = new UnifiedEffectCatalog();
        var profile = _profiles.Detect(project);
        var personalNames = ReadPersonalJobNames(project, tables, profile.TableHints.JobEffectNameTable);
        var itemNames = ReadItemNames(project, tables);

        for (var id = 0; id <= byte.MaxValue; id++)
        {
            personalNames.TryGetValue(id, out var personalName);
            itemNames.TryGetValue(id, out var itemName);
            result.PersonalJobEffects.Add(CreateReference(EffectChannelKind.PersonalJob, id, personalName));
            result.ItemEffects.Add(id == 0
                ? new EffectChannelReference
                {
                    Channel = EffectChannelKind.Item,
                    EffectId = 0,
                    Name = "未启用宝物渠道",
                    DisplayName = "未启用宝物渠道（00）",
                    Description = "装备/宝物特效号 00 表示该判定不使用宝物渠道。",
                    NameSource = "渠道规则",
                    IsEnabled = false
                }
                : CreateReference(EffectChannelKind.Item, id, itemName));
        }

        AddJobBindings(project, tables, profile.TableHints.JobEffectAssignmentTable, result.PersonalJobEffects);
        AddPersonalBindings(project, tables, profile.TableHints.PersonalEffectTable, result.PersonalJobEffects);
        AddItemBindings(project, tables, profile.TableHints.ItemLowTable, result.ItemEffects);
        AddItemBindings(project, tables, profile.TableHints.ItemHighTable, result.ItemEffects);
        return result;
    }

    public string BuildFingerprint(CczProject project, string targetFile = "Ekd5.exe")
    {
        var builder = new StringBuilder();
        // Cache lookup must stay cheap. The shared executable snapshot computes the
        // full SHA only after this fast resource identity misses.
        AddFileIdentity(builder, project.ResolveGameFile(targetFile));
        AddFileIdentity(builder, project.ResolveGameFile("Data.e5"));
        AddFileIdentity(builder, project.ResolveGameFile("Star.e5"));
        AddFileIdentity(builder, project.ResolveGameFile("Imsg.e5"));
        AddFileIdentity(builder, project.ResolveGameFile("Item.e5"));
        AddFileIdentity(builder, project.HexTableXmlPath);
        AddFileIdentity(builder, _itemCatalog.GetStorePath(project));
        foreach (var root in new[]
                 {
                     ProjectPatchIdentityService.EffectManifestRoot(project),
                     ProjectPatchIdentityService.CompositeManifestRoot(project)
                 })
        {
            if (!Directory.Exists(root)) continue;
            foreach (var path in Directory.GetFiles(root, "*.json").OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
            {
                AddFileIdentity(builder, path);
            }
        }

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString())));
    }

    internal static EffectChannelReference CreateUnknown(string channel, int effectId)
        => CreateReference(channel, effectId, null);

    private IReadOnlyDictionary<int, string> ReadPersonalJobNames(
        CczProject project,
        IReadOnlyList<HexTableDefinition> tables,
        string tableName)
    {
        return HexTableNameResolver.TryResolveForProject(project, tables, tableName, out var table)
            ? _jobNames.ReadNames(project, table)
            : new Dictionary<int, string>();
    }

    private IReadOnlyDictionary<int, string> ReadItemNames(CczProject project, IReadOnlyList<HexTableDefinition> tables)
    {
        var result = new Dictionary<int, string>(_itemNames.ReadBaseNames(project, tables));
        var seed = _itemNames.ReadBaseCatalogEntries(project, tables);
        foreach (var pair in _itemCatalog.BuildDisplayLookup(_itemCatalog.Load(project, seed)))
        {
            result[pair.Key] = pair.Value;
        }

        return result;
    }

    private void AddJobBindings(
        CczProject project,
        IReadOnlyList<HexTableDefinition> tables,
        string tableName,
        IReadOnlyList<EffectChannelReference> effects)
    {
        var data = TryRead(project, tables, tableName);
        if (data == null) return;
        foreach (DataRow row in data.Rows)
        {
            var id = ReadInt(row, "ID", -1);
            if (id is < 0 or > byte.MaxValue) continue;
            var personIds = new[] { ReadInt(row, "1号武将", 1024), ReadInt(row, "2号武将", 1024), ReadInt(row, "3号武将", 1024) }
                .Where(value => value is >= 0 and < 1024).ToArray();
            var jobId = ReadInt(row, "兵种", 255);
            var effectValue = ReadInt(row, "特效值", 0);
            if (personIds.Length == 0 && jobId == 255 && effectValue == 0) continue;
            AddBinding(effects[id], "兵种/武将分配",
                $"武将 {FormatIds(personIds)}；兵种 {(jobId == 255 ? "不限" : jobId.ToString(CultureInfo.InvariantCulture))}", effectValue,
                new EffectPackageBinding
                {
                    Kind = "job_assignment",
                    RowId = id,
                    PersonId = ReadInt(row, "1号武将", 1024),
                    PersonId2 = ReadInt(row, "2号武将", 1024),
                    PersonId3 = ReadInt(row, "3号武将", 1024),
                    JobId = jobId,
                    EffectValue = effectValue
                });
        }
    }

    private void AddPersonalBindings(
        CczProject project,
        IReadOnlyList<HexTableDefinition> tables,
        string tableName,
        IReadOnlyList<EffectChannelReference> effects)
    {
        var data = TryRead(project, tables, tableName);
        if (data == null) return;
        foreach (DataRow row in data.Rows)
        {
            var id = ReadInt(row, "ID", -1);
            if (id is < 0 or > byte.MaxValue) continue;
            AddPersonalBinding(effects[id], row, "人物专属一", "武将1", ["装备1"], "特效值1");
            AddPersonalBinding(effects[id], row, "人物专属二", "武将2", ["装备2"], "特效值2");
            AddPersonalBinding(effects[id], row, "三件套", null, ["装备3-1", "装备3-2", "装备3-3"], "特效值3");
            AddPersonalBinding(effects[id], row, "四件套", null, ["装备4-1", "装备4-2", "装备4-3"], "特效值4");
        }
    }

    private void AddItemBindings(
        CczProject project,
        IReadOnlyList<HexTableDefinition> tables,
        string tableName,
        IReadOnlyList<EffectChannelReference> effects)
    {
        var data = TryRead(project, tables, tableName);
        if (data == null || !data.Columns.Contains("装备特效号")) return;
        foreach (DataRow row in data.Rows)
        {
            var effectId = ReadInt(row, "装备特效号", 0);
            if (effectId is <= 0 or > byte.MaxValue) continue;
            var itemId = ReadInt(row, "ID", -1);
            var itemName = ReadText(row, "名称");
            var effectValue = ReadInt(row, "装备特效号-效果值", 0);
            AddBinding(effects[effectId], "宝物/物品",
                string.IsNullOrWhiteSpace(itemName) ? $"物品 {itemId}" : $"{itemName}（{itemId}）", effectValue,
                new EffectPackageBinding
                {
                    Kind = "item",
                    RowId = itemId,
                    ItemId = itemId,
                    EffectValue = effectValue
                });
        }
    }

    private static void AddPersonalBinding(
        EffectChannelReference effect,
        DataRow row,
        string kind,
        string? personColumn,
        IReadOnlyList<string> itemColumns,
        string valueColumn)
    {
        var personId = personColumn == null ? 1024 : ReadInt(row, personColumn, 1024);
        var rawItemIds = itemColumns.Select(column => ReadInt(row, column, 0)).ToArray();
        var itemIds = rawItemIds.Where(value => value is > 0 and < 255).ToArray();
        var effectValue = ReadInt(row, valueColumn, 0);
        var hasPerson = personColumn != null && personId is > 0 and < 1024;
        if (!hasPerson && itemIds.Length == 0 && effectValue == 0) return;
        var parts = new List<string>();
        if (hasPerson) parts.Add("武将 " + personId);
        if (itemIds.Length > 0) parts.Add("装备 " + FormatIds(itemIds));
        AddBinding(effect, kind, string.Join("；", parts), effectValue, new EffectPackageBinding
        {
            Kind = kind switch
            {
                "人物专属一" => "person_item_1",
                "人物专属二" => "person_item_2",
                "三件套" => "set_3",
                _ => "set_4"
            },
            RowId = ReadInt(row, "ID", -1),
            PersonId = hasPerson ? personId : null,
            ItemId = itemIds.Length > 0 ? itemIds[0] : null,
            ItemId2 = itemIds.Length > 1 ? itemIds[1] : null,
            ItemId3 = itemIds.Length > 2 ? itemIds[2] : null,
            EffectValue = effectValue
        });
    }

    private DataTable? TryRead(CczProject project, IReadOnlyList<HexTableDefinition> tables, string tableName)
    {
        if (!HexTableNameResolver.TryResolveForProject(project, tables, tableName, out var table)) return null;
        var read = _reader.Read(project, table, tables);
        return read.Validation.IsUsable ? read.Data : null;
    }

    private static EffectChannelReference CreateReference(string channel, int effectId, string? name)
    {
        var hasName = !string.IsNullOrWhiteSpace(name) && !name.StartsWith('#');
        var fallback = channel == EffectChannelKind.Item ? "未命名宝物特效" : "未命名个人特技";
        var resolvedName = hasName ? name!.Trim() : fallback;
        return new EffectChannelReference
        {
            Channel = channel,
            EffectId = effectId,
            Name = resolvedName,
            DisplayName = $"{resolvedName}（{effectId:X2}）",
            NameSource = hasName ? (channel == EffectChannelKind.Item ? "宝物特效目录" : "兵种特效名称区") : "未命名编号"
        };
    }

    private static void AddBinding(
        EffectChannelReference effect,
        string kind,
        string summary,
        int? value,
        EffectPackageBinding? packageBinding = null)
    {
        effect.IsConfigured = true;
        effect.Bindings.Add(new EffectBindingReference { Kind = kind, Summary = summary, EffectValue = value, PackageBinding = packageBinding });
    }

    private static int ReadInt(DataRow row, string column, int fallback)
        => row.Table.Columns.Contains(column) && int.TryParse(Convert.ToString(row[column], CultureInfo.InvariantCulture), out var value)
            ? value
            : fallback;

    private static string ReadText(DataRow row, string column)
        => row.Table.Columns.Contains(column) ? Convert.ToString(row[column], CultureInfo.InvariantCulture)?.Trim() ?? string.Empty : string.Empty;

    private static string FormatIds(IEnumerable<int> values) => string.Join("/", values.Select(value => value.ToString(CultureInfo.InvariantCulture)));

    private static void AddFileIdentity(StringBuilder builder, string path)
    {
        if (!File.Exists(path))
        {
            builder.Append(path).Append("|missing\n");
            return;
        }

        var info = new FileInfo(path);
        builder.Append(info.FullName).Append('|').Append(info.Length).Append('|').Append(info.LastWriteTimeUtc.Ticks).Append('\n');
    }

}
