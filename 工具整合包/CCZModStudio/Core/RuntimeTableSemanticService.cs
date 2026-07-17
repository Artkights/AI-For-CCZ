using CCZModStudio.Formats;
using CCZModStudio.Models;

namespace CCZModStudio.Core;

public sealed class RuntimeTableSemanticService
{
    public RuntimeTableFieldResolution Resolve(string tableId, int recordId, string? fieldId = null)
    {
        if (!EngineRuntimeSemanticRegistry.Tables.TryGetValue(tableId, out var table))
            throw new ArgumentException("未知运行时表：" + tableId, nameof(tableId));
        var field = string.IsNullOrWhiteSpace(fieldId)
            ? null
            : table.Fields.FirstOrDefault(item => item.FieldId.Equals(fieldId, StringComparison.OrdinalIgnoreCase))
              ?? throw new ArgumentException($"{tableId} 不包含字段 {fieldId}。", nameof(fieldId));
        return new RuntimeTableFieldResolution
        {
            TableId = table.TableId,
            TableNameZh = table.DisplayNameZh,
            RecordId = recordId,
            FieldId = field?.FieldId ?? string.Empty,
            FieldNameZh = field?.DisplayNameZh ?? "记录起点",
            Address = table.AddressOf(recordId, fieldId),
            Width = field?.Width ?? table.RecordStride,
            EvidenceLevel = field?.EvidenceLevel ?? table.EvidenceLevel,
            Writable = field?.Writable == true
        };
    }

    public RuntimeTableAddressResolution Explain(uint address)
        => EngineRuntimeSemanticRegistry.TryResolveTableAddress(address, out var resolution)
            ? resolution
            : throw new ArgumentOutOfRangeException(nameof(address), $"0x{address:X8} 不属于已登记运行时表。");
}

public sealed class RuntimeTableFieldResolution
{
    public string TableId { get; set; } = string.Empty;
    public string TableNameZh { get; set; } = string.Empty;
    public int RecordId { get; set; }
    public string FieldId { get; set; } = string.Empty;
    public string FieldNameZh { get; set; } = string.Empty;
    public uint Address { get; set; }
    public string AddressHex => $"0x{Address:X8}";
    public int Width { get; set; }
    public string EvidenceLevel { get; set; } = string.Empty;
    public bool Writable { get; set; }
}

public sealed class RuntimeTableSchemaAuditService
{
    public RuntimeTableSchemaAuditResult Audit(CczProject project)
    {
        var result = new RuntimeTableSchemaAuditResult();
        var tables = new HexTableParser().Load(project.HexTableXmlPath);
        var prefix = new CczEngineProfileService().Detect(project).TableVersionPrefix;
        var versionTables = tables.Where(item => item.TableName.StartsWith(prefix + "-", StringComparison.OrdinalIgnoreCase)).ToList();

        var itemTables = versionTables.Where(item => item.RowSize == EngineRuntimeSemanticRegistry.ItemRecordStride &&
                                                     item.ByteSizes.SequenceEqual([17, 1, 1, 1, 1, 1, 1, 1, 1]))
            .OrderBy(item => item.BeginId).ToList();
        Check(itemTables.Sum(item => item.RowCount) == 255 && itemTables.FirstOrDefault()?.BeginId == 0 &&
              itemTables.SelectMany(item => Enumerable.Range(item.BeginId, item.RowCount)).Distinct().Count() == 255,
            "items", "道具表必须由连续的 0..254 共 255 行、每行 25 字节组成。", result);

        var detailed = versionTables.FirstOrDefault(item => item.RowSize == EngineRuntimeSemanticRegistry.DetailedJobRecordStride &&
                                                            item.RowCount == 80 &&
                                                            item.TableName.Contains("兵种成长", StringComparison.Ordinal) &&
                                                            item.IndexTable.Contains("详细兵种", StringComparison.Ordinal));
        Check(detailed != null && detailed.ByteSizes.Where(item => item > 0).Sum() == EngineRuntimeSemanticRegistry.DetailedJobRecordStride,
            "detailed-jobs", "详细兵种表必须为 80 行、每行 35 字节。", result);

        var terrain = versionTables.FirstOrDefault(item => item.RowSize == EngineRuntimeSemanticRegistry.JobFamilyTerrainRecordStride &&
                                                           item.RowCount == 40 && item.TableName.Contains("地形发挥", StringComparison.Ordinal));
        var movement = versionTables.FirstOrDefault(item => item.RowSize == EngineRuntimeSemanticRegistry.JobFamilyTerrainRecordStride &&
                                                            item.RowCount == 40 && item.TableName.Contains("移动消耗", StringComparison.Ordinal));
        Check(terrain != null && movement != null && movement.DataPos - terrain.DataPos == 30 &&
              terrain.ByteSizes.Where(item => item > 0).Sum() == 30 && movement.ByteSizes.Where(item => item > 0).Sum() == 30,
            "job-family-terrain", "地形发挥和移动消耗必须是同一 60 字节记录的前后两个 30 字节视图。", result);

        result.Accepted = result.WarningsZh.Count == 0;
        result.SummaryZh = result.Accepted
            ? "HexTable 与 6.4/6.5 运行时道具、兵种和地形表语义一致。"
            : "HexTable 与运行时表语义不一致：" + string.Join("；", result.WarningsZh);
        return result;
    }

    private static void Check(bool condition, string tableId, string warning, RuntimeTableSchemaAuditResult result)
    {
        result.TableChecks[tableId] = condition;
        if (!condition) result.WarningsZh.Add(warning);
    }
}

public sealed class RuntimeTableSchemaAuditResult
{
    public bool Accepted { get; set; }
    public Dictionary<string, bool> TableChecks { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public List<string> WarningsZh { get; set; } = [];
    public string SummaryZh { get; set; } = string.Empty;
}
