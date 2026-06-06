using CCZModStudio.Core;
using CCZModStudio.Formats;
using CCZModStudio.Models;
using CCZModStudio;
using System.Data;
using System.Drawing;
using System.Globalization;
using System.Text;
using System.Windows.Forms;

var enableWriteTest = args.Contains("--write", StringComparer.OrdinalIgnoreCase);
var rsSmokeOnly = args.Contains("--rs-smoke", StringComparer.OrdinalIgnoreCase);
var rsWriteSmokeOnly = args.Contains("--rs-write-smoke", StringComparer.OrdinalIgnoreCase);
var migrationSmokeOnly = args.Contains("--migration-smoke", StringComparer.OrdinalIgnoreCase);
var legacyE5sSmokeOnly = args.Contains("--legacy-e5s-smoke", StringComparer.OrdinalIgnoreCase);
var legacyScenarioDepthSmokeOnly = args.Contains("--legacy-scenario-depth-smoke", StringComparer.OrdinalIgnoreCase);
var legacyScriptEditSmokeOnly = args.Contains("--legacy-script-edit-smoke", StringComparer.OrdinalIgnoreCase);
var legacyMfcDialogSmokeOnly = args.Contains("--legacy-mfc-dialog-smoke", StringComparer.OrdinalIgnoreCase);
var e5ImageReplaceSmokeOnly = args.Contains("--e5-image-replace-smoke", StringComparer.OrdinalIgnoreCase);
var shopSmokeOnly = args.Contains("--shop-smoke", StringComparer.OrdinalIgnoreCase);
var jobStrategyWriteSmokeOnly = args.Contains("--job-strategy-write-smoke", StringComparer.OrdinalIgnoreCase);
var legacyAllSmoke = args.Contains("--legacy-all-smoke", StringComparer.OrdinalIgnoreCase);

var detector = new ProjectDetector();
var project = detector.DetectDefaultProject();
Console.WriteLine($"Workspace={project.WorkspaceRoot}");
Console.WriteLine($"GameRoot={project.GameRoot}");
Console.WriteLine($"HexTable={project.HexTableXmlPath}");

var statuses = project.GetFileStatuses();
foreach (var status in statuses)
{
    Console.WriteLine($"FILE {status.Name} exists={status.Exists} size={status.SizeBytes?.ToString() ?? "-"}");
}

var parser = new HexTableParser();
var tables = parser.Load(project.HexTableXmlPath);
Console.WriteLine($"TABLE_COUNT={tables.Count}");

if (rsSmokeOnly)
{
    RunRsSmoke(project, tables);
    return;
}

if (rsWriteSmokeOnly)
{
    RunRsWriteSmoke(project, tables);
    return;
}

if (migrationSmokeOnly)
{
    RunMigrationSmoke(project);
    return;
}

if (legacyE5sSmokeOnly)
{
    RunLegacyE5sSmoke(project);
    return;
}

if (legacyScenarioDepthSmokeOnly)
{
    RunLegacyScenarioDepthSmoke(project);
    return;
}

if (legacyScriptEditSmokeOnly)
{
    RunLegacyScriptEditSmoke(project);
    return;
}

if (legacyMfcDialogSmokeOnly)
{
    RunLegacyMfcDialogSmoke(project, tables);
    return;
}

if (e5ImageReplaceSmokeOnly)
{
    RunE5ImageReplaceSmoke(project);
    return;
}

if (shopSmokeOnly)
{
    RunShopEditorSmoke(project, tables);
    return;
}

if (jobStrategyWriteSmokeOnly)
{
    RunJobStrategyWriteSmoke(project, tables);
    return;
}

if (enableWriteTest && !legacyAllSmoke)
{
    Console.WriteLine("WRITE_MODE=RS_CORE (--write 当前只运行 R/S eex 核心写入烟测；旧 E5S 探针请显式使用 --legacy-e5s-smoke 或 --legacy-all-smoke)");
    RunRsWriteSmoke(project, tables);
    return;
}

if (!legacyAllSmoke)
{
    Console.WriteLine("DEFAULT_MODE=RS_CORE (旧全量探针已拆到 --legacy-all-smoke，E5S 兼容检查为 --legacy-e5s-smoke)");
    RunRsSmoke(project, tables);
    return;
}

var reader = new HexTableReader();
foreach (var tableName in new[] { "6.5-0 人物", "6.5-1 物品（0-103）", "6.5-5 策略", "6.5-7 兵种特效" })
{
    var table = tables.Single(t => t.TableName == tableName);
    var result = reader.Read(project, table, tables);
    Console.WriteLine($"READ {table.TableName} usable={result.Validation.IsUsable} rows={result.Data.Rows.Count} cols={result.Data.Columns.Count} file={Path.GetFileName(result.Validation.FilePath)}");
    if (!result.Validation.IsUsable)
    {
        foreach (var warning in result.Validation.Warnings)
        {
            Console.WriteLine("  WARN " + warning);
        }
    }
}

var fieldAnnotationService = new FieldAnnotationService();
var personAnnotationTable = tables.Single(t => t.TableName == "6.5-0 人物");
var personAnnotationRead = reader.Read(project, personAnnotationTable, tables);
var annotationField = personAnnotationTable.Fields.FirstOrDefault(f => f.ColumnName.Contains("级别", StringComparison.Ordinal))
                      ?? personAnnotationTable.Fields.First(f => f.ConsumesBytes);
var annotationValidation = reader.Validate(project, personAnnotationTable);
var tableSummary = fieldAnnotationService.BuildTableSummary(personAnnotationTable, annotationValidation, project.IsTestCopy);
var fieldAnnotation = fieldAnnotationService.BuildFieldAnnotation(personAnnotationTable, annotationField);
if (!tableSummary.Contains("表说明", StringComparison.Ordinal) || !fieldAnnotation.Contains("中文注释", StringComparison.Ordinal))
{
    throw new InvalidOperationException("字段中文注释服务输出缺少预期标记。");
}
Console.WriteLine($"FIELD_ANNOTATION table={personAnnotationTable.TableName} field={annotationField.ColumnName} short={fieldAnnotationService.BuildShortFieldAnnotation(personAnnotationTable, annotationField)}");
var tableReferenceLookupService = new TableReferenceLookupService();
var jobField = personAnnotationTable.Fields.Single(f => f.ColumnName == "\u804c\u4e1a");
var jobEvidence = tableReferenceLookupService.BuildCellReferenceEvidence(project, tables, personAnnotationTable, jobField, personAnnotationRead.Data.Rows[0]["\u804c\u4e1a"]);
if (!jobEvidence.Contains("\u8de8\u8868\u5f15\u7528\u89e3\u91ca", StringComparison.Ordinal) || !jobEvidence.Contains("6.5-4", StringComparison.Ordinal))
{
    throw new InvalidOperationException("\u804c\u4e1a\u5b57\u6bb5\u8de8\u8868\u5f15\u7528\u89e3\u91ca\u672a\u751f\u6210\u3002");
}
var roleQuoteMappingService = new RoleQuoteMappingService();
var criticalQuoteTable = tables.Single(t => t.TableName == "6.5-0-2 暴击台词");
var retreatQuoteTable = tables.Single(t => t.TableName == "6.5-0-3 撤退台词");
var criticalQuoteRead = reader.Read(project, criticalQuoteTable, tables);
var retreatQuoteRead = reader.Read(project, retreatQuoteTable, tables);
var caocaoRow = personAnnotationRead.Data.Rows.Cast<DataRow>().Single(row => Convert.ToInt32(row["ID"], CultureInfo.InvariantCulture) == 0);
var xiahoudunRow = personAnnotationRead.Data.Rows.Cast<DataRow>().Single(row => Convert.ToInt32(row["ID"], CultureInfo.InvariantCulture) == 1);
var caorenRow = personAnnotationRead.Data.Rows.Cast<DataRow>().Single(row => Convert.ToInt32(row["ID"], CultureInfo.InvariantCulture) == 5);
var liubeiRow = personAnnotationRead.Data.Rows.Cast<DataRow>().Single(row => Convert.ToInt32(row["ID"], CultureInfo.InvariantCulture) == 32);
var caocaoRetreat = roleQuoteMappingService.ResolveRetreatQuote(caocaoRow, retreatQuoteRead.Data);
var caocaoCritical = roleQuoteMappingService.ResolveCriticalQuote(project, caocaoRow, criticalQuoteRead.Data);
var xiahoudunCritical = roleQuoteMappingService.ResolveCriticalQuote(project, xiahoudunRow, criticalQuoteRead.Data);
var caorenCritical = roleQuoteMappingService.ResolveCriticalQuote(project, caorenRow, criticalQuoteRead.Data);
var liubeiCritical = roleQuoteMappingService.ResolveCriticalQuote(project, liubeiRow, criticalQuoteRead.Data);
if (caocaoRetreat.QuoteId != 0 ||
    Convert.ToString(caocaoRetreat.QuoteRow?["介绍"], CultureInfo.InvariantCulture) != "苍天不佑……" ||
    caocaoCritical.QuoteIds.SingleOrDefault() != 0 ||
    xiahoudunCritical.QuoteIds.SingleOrDefault() != 1 ||
    !caorenCritical.QuoteIds.SequenceEqual(new[] { 24, 25, 26 }) ||
    liubeiCritical.QuoteIds.SingleOrDefault() != 20)
{
    throw new InvalidOperationException("角色暴击/撤退台词映射规则与 6.5 基底预期不符。");
}
var criticalField = personAnnotationTable.Fields.Single(f => f.ColumnName == "暴击台词");
var retreatField = personAnnotationTable.Fields.Single(f => f.ColumnName == "撤退台词");
var criticalEvidence = tableReferenceLookupService.BuildCellReferenceEvidence(project, tables, personAnnotationTable, criticalField, caocaoRow["暴击台词"]);
var retreatEvidence = tableReferenceLookupService.BuildCellReferenceEvidence(project, tables, personAnnotationTable, retreatField, caocaoRow["撤退台词"]);
var criticalNavigation = tableReferenceLookupService.ResolveCellReferenceTarget(project, tables, personAnnotationTable, criticalField, caocaoRow["暴击台词"], 0);
var retreatNavigation = tableReferenceLookupService.ResolveCellReferenceTarget(project, tables, personAnnotationTable, retreatField, caocaoRow["撤退台词"], 0);
if (!criticalEvidence.Contains("暴击台词类型号", StringComparison.Ordinal) ||
    !retreatEvidence.Contains("撤退台词兼容字段", StringComparison.Ordinal) ||
    criticalNavigation.CanNavigate ||
    retreatNavigation.CanNavigate)
{
    throw new InvalidOperationException("角色台词字段仍被误解释为直接跨表行引用。");
}
Console.WriteLine($"ROLE_QUOTE_MAPPING retreat0=#{caocaoRetreat.QuoteId} critical0=#{caocaoCritical.QuoteIds[0]} critical1=#{xiahoudunCritical.QuoteIds[0]} caoren={string.Join("/", caorenCritical.QuoteIds)} liubei={string.Join("/", liubeiCritical.QuoteIds)}");
var faceField = personAnnotationTable.Fields.Single(f => f.ColumnName == "\u5934\u50cf");
var faceEvidence = tableReferenceLookupService.BuildCellReferenceEvidence(project, tables, personAnnotationTable, faceField, personAnnotationRead.Data.Rows[0]["\u5934\u50cf"]);
if (!faceEvidence.Contains("\u5934\u50cf\u7f16\u53f7", StringComparison.Ordinal) || !faceEvidence.Contains("Face.e5", StringComparison.OrdinalIgnoreCase))
{
    throw new InvalidOperationException("\u5934\u50cf\u8d44\u6e90\u7f16\u53f7\u89e3\u91ca\u672a\u751f\u6210\u3002");
}
var itemReferenceTable = tables.Single(t => t.TableName == "6.5-1 \u7269\u54c1\uff080-103\uff09");
var itemReferenceRead = reader.Read(project, itemReferenceTable, tables);
var effectField = itemReferenceTable.Fields.Single(f => f.ColumnName == "\u88c5\u5907\u7279\u6548\u53f7");
var effectValueField = itemReferenceTable.Fields.Single(f => f.ColumnName == "\u88c5\u5907\u7279\u6548\u53f7-\u6548\u679c\u503c");
var effectRow = itemReferenceRead.Data.Rows.Cast<DataRow>()
    .FirstOrDefault(row =>
    {
        var effectId = Convert.ToInt32(row["\u88c5\u5907\u7279\u6548\u53f7"], CultureInfo.InvariantCulture);
        return effectId >= 0x1A && effectId <= 0x7F;
    })
    ?? itemReferenceRead.Data.Rows[0];
var effectEvidence = tableReferenceLookupService.BuildCellReferenceEvidence(project, tables, itemReferenceTable, effectField, effectRow["\u88c5\u5907\u7279\u6548\u53f7"]);
if (!effectEvidence.Contains("\u88c5\u5907\u7279\u6548\u53f7", StringComparison.Ordinal))
{
    throw new InvalidOperationException("\u88c5\u5907\u7279\u6548\u53f7\u8de8\u8868\u89e3\u91ca\u672a\u751f\u6210\u3002");
}
var effectParameterEvidence = tableReferenceLookupService.BuildCellReferenceEvidence(project, tables, itemReferenceTable, effectValueField, effectRow["\u88c5\u5907\u7279\u6548\u53f7-\u6548\u679c\u503c"]);
if (!effectParameterEvidence.Contains("\u53c2\u6570\u89e3\u91ca", StringComparison.Ordinal))
{
    throw new InvalidOperationException("\u88c5\u5907\u7279\u6548\u53c2\u6570\u89e3\u91ca\u672a\u751f\u6210\u3002");
}
var strategyAnimationTable = tables.Single(t => t.TableName == "6.5-5-2 \u7b56\u7565\u52a8\u753b1");
var strategyAnimationRead = reader.Read(project, strategyAnimationTable, tables);
var strategyContentField = strategyAnimationTable.Fields.Single(f => f.ColumnName == "\u5185\u5bb9");
var strategyRow = strategyAnimationRead.Data.Rows[0];
var strategyRowId = Convert.ToInt32(strategyRow["ID"], CultureInfo.InvariantCulture);
var strategyCompanionEvidence = tableReferenceLookupService.BuildCellReferenceEvidence(project, tables, strategyAnimationTable, strategyContentField, strategyRow["\u5185\u5bb9"], strategyRowId);
if (!strategyCompanionEvidence.Contains("\u7b56\u7565\u9644\u8868\u89e3\u91ca", StringComparison.Ordinal) ||
    !strategyCompanionEvidence.Contains("6.5-5 \u7b56\u7565", StringComparison.Ordinal))
{
    throw new InvalidOperationException("\u7b56\u7565\u9644\u8868\u884c\u5bf9\u9f50\u89e3\u91ca\u672a\u751f\u6210\u3002");
}
var strategyTable = tables.Single(t => t.TableName == "6.5-5 \u7b56\u7565");
var rangeField = strategyTable.Fields.Single(f => f.ColumnName == "\u65bd\u6cd5\u8303\u56f4");
var strategyRead = reader.Read(project, strategyTable, tables);
var rangeEvidence = tableReferenceLookupService.BuildCellReferenceEvidence(project, tables, strategyTable, rangeField, strategyRead.Data.Rows[0]["\u65bd\u6cd5\u8303\u56f4"], 0);
if (!rangeEvidence.Contains("\u8303\u56f4/\u6a21\u677f\u7f16\u53f7", StringComparison.Ordinal))
{
    throw new InvalidOperationException("\u7b56\u7565\u8303\u56f4\u53c2\u6570\u89e3\u91ca\u672a\u751f\u6210\u3002");
}
var strategyDescriptionTable = tables.Single(t => t.TableName == "6.5-5-1 \u7b56\u7565\u8bf4\u660e");
var strategyDescriptionRead = reader.Read(project, strategyDescriptionTable, tables);
var strategyDescriptionField = strategyDescriptionTable.Fields.Single(f => f.ColumnName == "\u4ecb\u7ecd");
var strategyDescriptionEvidence = tableReferenceLookupService.BuildCellReferenceEvidence(
    project,
    tables,
    strategyDescriptionTable,
    strategyDescriptionField,
    strategyDescriptionRead.Data.Rows[0]["\u4ecb\u7ecd"],
    Convert.ToInt32(strategyDescriptionRead.Data.Rows[0]["ID"], CultureInfo.InvariantCulture));
if (!strategyDescriptionEvidence.Contains("\u884c\u5bf9\u9f50\u6587\u672c\u89e3\u91ca", StringComparison.Ordinal) ||
    !strategyDescriptionEvidence.Contains("6.5-5 \u7b56\u7565", StringComparison.Ordinal))
{
    throw new InvalidOperationException("\u7b56\u7565\u8bf4\u660e\u884c\u5bf9\u9f50\u6587\u672c\u89e3\u91ca\u672a\u751f\u6210\u3002");
}
var jobNavigation = tableReferenceLookupService.ResolveCellReferenceTarget(project, tables, personAnnotationTable, jobField, personAnnotationRead.Data.Rows[0]["\u804c\u4e1a"], 0);
if (!jobNavigation.CanNavigate || jobNavigation.TargetTableName != "6.5-4 \u8be6\u7ec6\u5175\u79cd" || jobNavigation.TargetRowId.Length == 0)
{
    throw new InvalidOperationException("\u4eba\u7269\u804c\u4e1a\u8de8\u8868\u5f15\u7528\u5bfc\u822a\u672a\u751f\u6210\u3002");
}
var strategyDescriptionNavigation = tableReferenceLookupService.ResolveCellReferenceTarget(
    project,
    tables,
    strategyDescriptionTable,
    strategyDescriptionField,
    strategyDescriptionRead.Data.Rows[0]["\u4ecb\u7ecd"],
    Convert.ToInt32(strategyDescriptionRead.Data.Rows[0]["ID"], CultureInfo.InvariantCulture));
if (!strategyDescriptionNavigation.CanNavigate || strategyDescriptionNavigation.TargetTableName != "6.5-5 \u7b56\u7565")
{
    throw new InvalidOperationException("\u7b56\u7565\u8bf4\u660e\u884c\u5bf9\u9f50\u5bfc\u822a\u672a\u751f\u6210\u3002");
}
var effectNavigation = tableReferenceLookupService.ResolveCellReferenceTarget(project, tables, itemReferenceTable, effectField, effectRow["\u88c5\u5907\u7279\u6548\u53f7"], Convert.ToInt32(effectRow["ID"], CultureInfo.InvariantCulture));
if (!effectNavigation.CanNavigate || !effectNavigation.TargetTableName.Contains("\u88c5\u5907\u7279\u6548\u540d\u79f0", StringComparison.Ordinal) || string.IsNullOrWhiteSpace(effectNavigation.TargetFieldName))
{
    throw new InvalidOperationException("\u88c5\u5907\u7279\u6548\u53f7\u5bfc\u822a\u672a\u751f\u6210\u3002");
}
Console.WriteLine($"TABLE_REFERENCE_NAVIGATION jobTarget={jobNavigation.TargetTableName}/{jobNavigation.TargetRowId}/{jobNavigation.TargetFieldName} textTarget={strategyDescriptionNavigation.TargetTableName}/{strategyDescriptionNavigation.TargetRowId}/{strategyDescriptionNavigation.TargetFieldName} effectTarget={effectNavigation.TargetTableName}/{effectNavigation.TargetRowId}/{effectNavigation.TargetFieldName}");
Console.WriteLine($"TABLE_REFERENCE job={jobEvidence[..Math.Min(40, jobEvidence.Length)].Replace("\r", " ").Replace("\n", " ")} face={faceEvidence[..Math.Min(30, faceEvidence.Length)].Replace("\r", " ").Replace("\n", " ")} effect={effectEvidence[..Math.Min(40, effectEvidence.Length)].Replace("\r", " ").Replace("\n", " ")} strategy={strategyCompanionEvidence[..Math.Min(40, strategyCompanionEvidence.Length)].Replace("\r", " ").Replace("\n", " ")}");
var annotationExport = fieldAnnotationService.ExportAnnotations(
    project,
    personAnnotationTable,
    annotationValidation,
    personAnnotationRead.Data,
    field => tableReferenceLookupService.BuildFieldReferenceHint(personAnnotationTable, field));
var annotationExportHeader = File.ReadLines(annotationExport).FirstOrDefault() ?? string.Empty;
if (!File.Exists(annotationExport)
    || !annotationExportHeader.Contains("HighRisk", StringComparison.Ordinal)
    || !annotationExportHeader.Contains("RiskReason", StringComparison.Ordinal)
    || !annotationExportHeader.Contains("ReferenceHint", StringComparison.Ordinal)
    || !annotationExportHeader.Contains("SampleValues", StringComparison.Ordinal)
    || !annotationExportHeader.Contains("TopValues", StringComparison.Ordinal)
    || !annotationExportHeader.Contains("Annotation", StringComparison.Ordinal))
{
    throw new InvalidOperationException("字段注释导出文件缺少预期列。");
}
Console.WriteLine($"FIELD_ANNOTATION_EXPORT file={Path.GetFileName(annotationExport)}");
var visibleCsvPath = Path.Combine(project.WorkspaceRoot, "CCZModStudio_Exports", "Smoke_人物_可见列.csv");
Directory.CreateDirectory(Path.GetDirectoryName(visibleCsvPath)!);
CsvService.ExportColumnsWithAnnotationRow(personAnnotationRead.Data, visibleCsvPath, new[] { "ID", "名称", "级别" }, new Dictionary<string, string>
{
    ["ID"] = "行号/编号；用于回查原始数据。",
    ["名称"] = fieldAnnotationService.BuildShortFieldAnnotation(personAnnotationTable, personAnnotationTable.Fields.Single(f => f.ColumnName == "名称")),
    ["级别"] = fieldAnnotationService.BuildShortFieldAnnotation(personAnnotationTable, annotationField)
});
var visibleCsvLines = File.ReadLines(visibleCsvPath).Take(3).ToList();
var visibleCsvHeader = visibleCsvLines.FirstOrDefault() ?? string.Empty;
if (!visibleCsvHeader.Equals("ID,名称,级别", StringComparison.Ordinal))
{
    throw new InvalidOperationException("可见列 CSV 导出表头不符合预期：" + visibleCsvHeader);
}
if (visibleCsvLines.Count < 3 || !visibleCsvLines[1].Contains("等级", StringComparison.Ordinal) || !visibleCsvLines[2].StartsWith("0,曹操,1", StringComparison.Ordinal))
{
    throw new InvalidOperationException("可见列 CSV 注释行或数据行不符合预期。");
}
Console.WriteLine($"VISIBLE_CSV_EXPORT file={Path.GetFileName(visibleCsvPath)} columns=3");
var originalAnnotationLevel = Convert.ToInt32(personAnnotationRead.Data.Rows[0]["级别"], CultureInfo.InvariantCulture);
personAnnotationRead.Data.Rows[0]["级别"] = originalAnnotationLevel + 1;
var changedRowsForExport = personAnnotationRead.Data.Rows.Cast<DataRow>().Where(row => row.RowState != DataRowState.Unchanged).ToList();
var changedRowsCsvPath = Path.Combine(project.WorkspaceRoot, "CCZModStudio_Exports", "Smoke_人物_改动行.csv");
CsvService.ExportColumnsRowsWithAnnotationRow(personAnnotationRead.Data, changedRowsCsvPath, new[] { "ID", "名称", "级别" }, new Dictionary<string, string>
{
    ["ID"] = "行号/编号；用于回查原始数据。",
    ["名称"] = "名称字段。",
    ["级别"] = "只导出已改动行测试。"
}, changedRowsForExport);
var changedRowsLines = File.ReadLines(changedRowsCsvPath).Take(4).ToList();
if (changedRowsForExport.Count != 1 || changedRowsLines.Count != 3 || !changedRowsLines[2].StartsWith("0,曹操,", StringComparison.Ordinal))
{
    throw new InvalidOperationException("可见/已改动行 CSV 导出不符合预期。");
}
Console.WriteLine($"VISIBLE_ROWS_CSV_EXPORT file={Path.GetFileName(changedRowsCsvPath)} rows={changedRowsForExport.Count}");

var enabled65 = tables.Where(t => t.Enabled && t.Version == "6.5").ToList();
var unusable = new List<string>();
foreach (var table in enabled65)
{
    var validation = reader.Validate(project, table);
    if (!validation.IsUsable)
    {
        unusable.Add($"{table.TableName}: {string.Join("; ", validation.Warnings)}");
    }
}
Console.WriteLine($"VALIDATE_ENABLED_65 total={enabled65.Count} unusable={unusable.Count}");
foreach (var item in unusable)
{
    Console.WriteLine("  UNUSABLE " + item);
}

var patchRoot = ProjectDetector.FindPortableDirectory(project, "普罗-搬运 注入", "普罗-搬运 注入");
var patchPath = Path.Combine(patchRoot ?? string.Empty, "6.4bug修复补丁.txt");
PatchDocument? patchDocument = null;
if (File.Exists(patchPath))
{
    var patchParser = new ProPatchParser();
    var patchService = new PatchApplyService();
    patchDocument = patchParser.Parse(patchPath);
    var preview = patchService.Preview(project, patchDocument, "Ekd5.exe");
    Console.WriteLine($"PATCH version={patchDocument.Version} kind={patchDocument.AddressKind} entries={patchDocument.Entries.Count} canApply={preview.CanApply} warnings={preview.WarningCount} totalBytes={preview.TotalBytes} changedBytes={preview.ChangedBytes}");
    foreach (var row in preview.Rows.Take(3))
    {
        Console.WriteLine($"PATCH_ROW #{row.Index} line={row.SourceLine} addr={row.AddressHex} offset={row.FileOffsetHex} len={row.Length} can={row.CanApply} status={row.Status}");
    }
}
else
{
    Console.WriteLine("PATCH skipped: file not found " + patchPath);
}

var moveParser = new BatchMoveParser();
foreach (var moveFile in new[] { "63搬运64data.txt", "63搬运64exe.txt", "63搬运64imsg.txt" })
{
    var movePath = Path.Combine(patchRoot ?? string.Empty, moveFile);
    if (!File.Exists(movePath))
    {
        Console.WriteLine("MOVE skipped: file not found " + movePath);
        continue;
    }

    var moveDoc = moveParser.Parse(movePath);
    Console.WriteLine($"MOVE {moveFile} entries={moveDoc.Entries.Count} totalLength={moveDoc.Entries.Sum(e => e.Length)}");
    foreach (var entry in moveDoc.Entries.Take(2))
    {
        Console.WriteLine($"MOVE_ROW #{entry.Index} src={entry.SourceOffsetHex} dst={entry.TargetOffsetHex} len={entry.Length} comment={entry.Comment}");
    }
}

var sceneStringPath = ProjectDetector.FindSceneDictionaryPath(project);
SceneStringDocument? sceneDoc = null;
if (File.Exists(sceneStringPath))
{
    sceneDoc = new SceneStringParser().Parse(sceneStringPath);
    Console.WriteLine($"SCENE_DICT commands={sceneDoc.Commands.Count} groups={sceneDoc.Groups.Count}");
    foreach (var command in sceneDoc.Commands.Take(5))
    {
        Console.WriteLine($"SCENE_CMD {command.IdHex} {command.Name}");
    }
}
else
{
    Console.WriteLine("SCENE_DICT skipped: file not found " + sceneStringPath);
}

try
{
    var assets = new MaterialLibraryIndexer().Index(project);
    Console.WriteLine($"MATERIAL assets={assets.Count} categories={assets.Select(a => a.Category).Distinct().Count()}");
    foreach (var asset in assets.Take(5))
    {
        Console.WriteLine($"MATERIAL_ROW {asset.Category}/{asset.FileName} hex={asset.HexTag} desc={asset.Description} size={asset.Width}x{asset.Height}");
    }
}
catch (DirectoryNotFoundException ex)
{
    Console.WriteLine("MATERIAL skipped: " + ex.Message);
}

var imageAssignmentService = new ImageAssignmentService();
var imageAssignments = imageAssignmentService.Load(project, tables);
Console.WriteLine($"IMAGE_ASSIGN rows={imageAssignments.Rows.Count} row0={imageAssignments.Rows[0]["名称"]} R={imageAssignments.Rows[0]["R形象编号"]} S={imageAssignments.Rows[0]["S形象编号"]}");
if (!imageAssignments.Columns.Contains("R资源状态") || !imageAssignments.Columns.Contains("S资源状态"))
{
    throw new InvalidOperationException("人物 R/S 形象联动表缺少资源状态列。");
}
var missingRResources = imageAssignments.AsEnumerable().Count(row => CharacterImageResourceService.IsMissingStatus(Convert.ToString(row["R资源状态"], CultureInfo.InvariantCulture) ?? string.Empty));
var missingSResources = imageAssignments.AsEnumerable().Count(row => CharacterImageResourceService.IsMissingStatus(Convert.ToString(row["S资源状态"], CultureInfo.InvariantCulture) ?? string.Empty));
Console.WriteLine($"IMAGE_ASSIGN_RESOURCE missingR={missingRResources} missingS={missingSResources} row0R={imageAssignments.Rows[0]["R资源状态"]} row0S={imageAssignments.Rows[0]["S资源状态"]}");

var row0RPath = ImageAssignmentService.GetImageResourcePath(project, "R", Convert.ToInt32(imageAssignments.Rows[0]["R\u5f62\u8c61\u7f16\u53f7"], CultureInfo.InvariantCulture));
if (!File.Exists(row0RPath))
{
    throw new InvalidOperationException("R/S \u8d44\u6e90\u8def\u5f84\u5de5\u5177\u672a\u80fd\u5b9a\u4f4d\u5230\u9884\u671f\u7684 R \u8d44\u6e90\uff1a" + row0RPath);
}
var row0RId = Convert.ToInt32(imageAssignments.Rows[0]["R\u5f62\u8c61\u7f16\u53f7"], CultureInfo.InvariantCulture);
var row0SId = Convert.ToInt32(imageAssignments.Rows[0]["S\u5f62\u8c61\u7f16\u53f7"], CultureInfo.InvariantCulture);
var row0FaceId = imageAssignments.Columns.Contains("头像编号")
    ? Convert.ToInt32(imageAssignments.Rows[0]["头像编号"], CultureInfo.InvariantCulture)
    : 0;
var e5ImageReplaceService = new E5ImageReplaceService();
var unitMovPath = CharacterImageResourceService.ResolveGameFile(project, "Unit_mov.e5");
var unitMovEntries = e5ImageReplaceService.ReadIndex(unitMovPath);
if (unitMovEntries.Count < 556)
{
    throw new InvalidOperationException($"Unit_mov.e5 0x110 图片索引表条目不足，预期至少 556，实际 {unitMovEntries.Count}。");
}
var e5ReplacePreview = e5ImageReplaceService.PreviewReplacementFromEntry(project, unitMovPath, 554, unitMovPath);
if (e5ReplacePreview.ImageNumber != 554 ||
    e5ReplacePreview.OldSizeBytes <= 0 ||
    e5ReplacePreview.NewSizeBytes <= 0 ||
    e5ReplacePreview.IndexOffset <= 0)
{
    throw new InvalidOperationException("E5 图片条目替换预览验证失败。");
}
Console.WriteLine($"E5_IMAGE_REPLACE_PREVIEW file={Path.GetFileName(unitMovPath)} entries={unitMovEntries.Count} image=554 kind={e5ReplacePreview.OldKind}->{e5ReplacePreview.NewKind} placement={e5ReplacePreview.Placement}");
var imagePreviewService = new ImageAssignmentPreviewService();
using (var preview = imagePreviewService.RenderResourcePreview(project, "R", row0RId, "\u66f9\u64cd", row0FaceId))
{
    var previewPath = Path.Combine(project.WorkspaceRoot, "CCZModStudio_Exports", "Smoke_RS_R00_Preview.png");
    Directory.CreateDirectory(Path.GetDirectoryName(previewPath)!);
    preview.Save(previewPath, System.Drawing.Imaging.ImageFormat.Png);
    var previewInfo = imagePreviewService.BuildResourceInfo(project, "S", row0SId, "\u66f9\u64cd", row0FaceId);
    if (!File.Exists(previewPath) ||
        !previewInfo.Contains("Unit_", StringComparison.OrdinalIgnoreCase) ||
        !previewInfo.Contains("Ekd5.exe", StringComparison.OrdinalIgnoreCase) ||
        !previewInfo.Contains("Face.e5", StringComparison.OrdinalIgnoreCase))
    {
        throw new InvalidOperationException("R/S image assignment preview service validation failed.");
    }
    Console.WriteLine($"IMAGE_ASSIGN_PREVIEW png={Path.GetFileName(previewPath)} face={row0FaceId} R={row0RId:00} S={row0SId:00}");
}
var imageMissingRows = imageAssignments.Rows.Cast<DataRow>()
    .Where(row => CharacterImageResourceService.IsMissingStatus(Convert.ToString(row["R\u8d44\u6e90\u72b6\u6001"], CultureInfo.InvariantCulture) ?? string.Empty)
               || CharacterImageResourceService.IsMissingStatus(Convert.ToString(row["S\u8d44\u6e90\u72b6\u6001"], CultureInfo.InvariantCulture) ?? string.Empty))
    .ToList();
var firstImageFilterName = imageAssignments.Rows.Cast<DataRow>()
    .Select(row => Convert.ToString(row["\u540d\u79f0"], CultureInfo.InvariantCulture) ?? string.Empty)
    .FirstOrDefault(name => !string.IsNullOrWhiteSpace(name)) ?? string.Empty;
var imageNameFilterRows = string.IsNullOrWhiteSpace(firstImageFilterName)
    ? Array.Empty<DataRow>()
    : ImageAssignmentService.FilterRows(imageAssignments, firstImageFilterName, missingOnly: false);
var imageResourceFilterRows = ImageAssignmentService.FilterRows(imageAssignments, $"R{row0RId}", missingOnly: false);
var imageMissingFilterRows = ImageAssignmentService.FilterRows(imageAssignments, string.Empty, missingOnly: true);
if ((!string.IsNullOrWhiteSpace(firstImageFilterName) && imageNameFilterRows.Count == 0) ||
    imageResourceFilterRows.All(row => !ReferenceEquals(row, imageAssignments.Rows[0])) ||
    imageMissingFilterRows.Count != imageMissingRows.Count ||
    imageMissingFilterRows.Any(row => !imageMissingRows.Contains(row)))
{
    throw new InvalidOperationException("人物 R/S 筛选服务验证失败。");
}
Console.WriteLine($"IMAGE_ASSIGN_FILTER keyword={firstImageFilterName} hits={imageNameFilterRows.Count} resourceHits={imageResourceFilterRows.Count} missing={imageMissingFilterRows.Count}");
var imageMissingReport = Path.Combine(project.WorkspaceRoot, "CCZModStudio_Exports", "Smoke_RS_MissingResources.csv");
Directory.CreateDirectory(Path.GetDirectoryName(imageMissingReport)!);
var imageReportColumns = new[] { "ID", "\u540d\u79f0", "R\u5f62\u8c61\u7f16\u53f7", "S\u5f62\u8c61\u7f16\u53f7", "R\u8d44\u6e90\u72b6\u6001", "S\u8d44\u6e90\u72b6\u6001" };
CsvService.ExportColumnsRowsWithAnnotationRow(imageAssignments, imageMissingReport, imageReportColumns, new Dictionary<string, string>
{
    ["ID"] = "\u4eba\u7269\u884c\u53f7/\u7f16\u53f7",
    ["\u540d\u79f0"] = "\u4eba\u7269\u540d\u79f0",
    ["R\u5f62\u8c61\u7f16\u53f7"] = "R \u8d44\u6e90\u7f16\u53f7",
    ["S\u5f62\u8c61\u7f16\u53f7"] = "S \u8d44\u6e90\u7f16\u53f7",
    ["R\u8d44\u6e90\u72b6\u6001"] = "R \u8d44\u6e90\u5b58\u5728\u6027",
    ["S\u8d44\u6e90\u72b6\u6001"] = "S \u8d44\u6e90\u5b58\u5728\u6027"
}, imageMissingRows);
var imageMissingReportHeader = File.ReadLines(imageMissingReport).FirstOrDefault() ?? string.Empty;
if (!imageMissingReportHeader.Contains("R\u8d44\u6e90\u72b6\u6001", StringComparison.Ordinal))
{
    throw new InvalidOperationException("R/S \u7f3a\u5931\u8d44\u6e90\u62a5\u544a\u5bfc\u51fa\u9a8c\u8bc1\u5931\u8d25\u3002");
}
Console.WriteLine($"IMAGE_ASSIGN_RESOURCE_REPORT file={Path.GetFileName(imageMissingReport)} rows={imageMissingRows.Count} row0RPath={Path.GetFileName(row0RPath)}");

var auditService = new ProjectAuditService();
var auditItems = auditService.Analyze(project, tables);
Console.WriteLine($"AUDIT items={auditItems.Count} errors={auditItems.Count(x => x.Severity == "Error")} warnings={auditItems.Count(x => x.Severity == "Warn")}");
if (!auditItems.Any(x => x.Category == "版本/偏移护栏" && x.Name == "6.5/6.6x 防混用"))
{
    throw new InvalidOperationException("项目体检缺少 6.5/6.6x 偏移防混用护栏。");
}
ProjectVersionGuardService.EnsureCoreFileCompatibleForWrite(project, "Data.e5");
Console.WriteLine("VERSION_GUARD OK 6.5 core sizes matched");
foreach (var item in auditItems.Take(5))
{
    Console.WriteLine($"AUDIT_ROW {item.Severity} {item.Category}/{item.Name} status={item.Status}");
}

var deliveryReportService = new ProjectDeliveryReportService();
var deliveryReportPreview = deliveryReportService.BuildReport(
    project,
    tables,
    auditItems,
    Array.Empty<ProjectDiffItem>(),
    Array.Empty<BackupHistoryItem>());
if (!deliveryReportPreview.Contains("发布前综合报告", StringComparison.Ordinal) ||
    !deliveryReportPreview.Contains("核心文件状态", StringComparison.Ordinal) ||
    !deliveryReportPreview.Contains("MOD创作风险摘要", StringComparison.Ordinal) ||
    !deliveryReportPreview.Contains("最近报告/发布证据摘要", StringComparison.Ordinal) ||
    !deliveryReportPreview.Contains("发布前检查清单", StringComparison.Ordinal) ||
    !deliveryReportPreview.Contains("安全边界", StringComparison.Ordinal))
{
    throw new InvalidOperationException("发布前综合报告预览缺少核心章节或安全边界。");
}
Console.WriteLine($"DELIVERY_REPORT_PREVIEW chars={deliveryReportPreview.Length}");

var workflowGuideService = new ProjectWorkflowGuideService();
var workflowSteps = workflowGuideService.BuildSteps(project, tables.Count, auditItems.Count, 0, 0, 0);
var workflowDashboard = workflowGuideService.BuildDashboard(
    project,
    tables.Count,
    auditItems,
    Array.Empty<ResourceDiagnosticItem>(),
    Array.Empty<ProjectDiffItem>(),
    Array.Empty<BackupHistoryItem>(),
    Array.Empty<CreatorNote>());
var workflowSummary = workflowGuideService.BuildSummary(project, workflowSteps, workflowDashboard);
var workflowActionPlan = workflowGuideService.BuildActionPlan(project, workflowDashboard);
var workflowActionItems = workflowGuideService.BuildActionItems(project, workflowDashboard);
var actionNoteTargetKey = ProjectWorkflowGuideService.BuildWorkflowActionTargetKey("安全模式");
var workflowActionItemsWithNotes = workflowGuideService.BuildActionItems(project, workflowDashboard, new[]
{
    new CreatorNote
    {
        Scope = "工作台行动",
        TargetKey = actionNoteTargetKey,
        Title = "安全模式行动备注",
        Tags = "工作台行动,待办",
        Content = "验证优先行动可以显示相关备注数量。"
    }
});
var testCopyStep = workflowSteps.Single(x => x.Title.Contains("测试副本", StringComparison.Ordinal));
var noteStep = workflowSteps.Single(x => x.Title.Contains("创作者备注", StringComparison.Ordinal));
var safetyDashboard = workflowDashboard.Single(x => x.Area == "安全模式");
var auditDashboard = workflowDashboard.Single(x => x.Area == "项目体检");
var reportDashboard = workflowDashboard.Single(x => x.Area == "最近报告/发布证据");
if (workflowSteps.Count != 8 ||
    workflowDashboard.Count < 10 ||
    !workflowSummary.Contains("制作向导", StringComparison.Ordinal) ||
    !workflowSummary.Contains("工作台提示", StringComparison.Ordinal) ||
    !workflowSummary.Contains("安全边界", StringComparison.Ordinal) ||
    !workflowActionPlan.Contains("优先行动清单", StringComparison.Ordinal) ||
    !workflowActionPlan.Contains("原始目录", StringComparison.Ordinal) ||
    !workflowActionPlan.Contains("创作者备注", StringComparison.Ordinal) ||
    workflowActionItems.Count == 0 ||
    workflowActionItems[0].PriorityNo != 1 ||
    !workflowActionItems[0].SafetyNote.Contains("原始目录", StringComparison.Ordinal) ||
    workflowActionItems.Any(x => string.IsNullOrWhiteSpace(x.TargetArea) || string.IsNullOrWhiteSpace(x.ExpectedResult) || string.IsNullOrWhiteSpace(x.NoteHint)) ||
    !workflowActionItems.Any(x => x.NoteHint.Contains("相关备注", StringComparison.Ordinal)) ||
    !workflowActionItemsWithNotes.Any(x => x.TargetArea == "安全模式" && x.NoteHint.Contains("相关备注 1 条", StringComparison.Ordinal)) ||
    !testCopyStep.SafetyNote.Contains("原始目录", StringComparison.Ordinal) ||
    !noteStep.RecommendedAction.Contains("抓取当前选择", StringComparison.Ordinal) ||
    safetyDashboard.Value != "原始只读" ||
    !auditDashboard.Value.Contains("警告", StringComparison.Ordinal) ||
    !safetyDashboard.RelatedPage.Contains("测试副本", StringComparison.Ordinal) ||
    !reportDashboard.RelatedPage.Contains("测试副本差异", StringComparison.Ordinal) ||
    !workflowDashboard.Any(x => x.Area == "关卡地图联动") ||
    !workflowDashboard.Any(x => x.Area == "SV高风险命令") ||
    !workflowDashboard.Any(x => x.Area == "EEX/Ls/Hexzmap探针"))
{
    throw new InvalidOperationException("制作向导/工作台服务缺少中文步骤、安全边界、风险计数或备注引导。");
}
Console.WriteLine($"WORKFLOW_GUIDE steps={workflowSteps.Count} cards={workflowDashboard.Count} actions={workflowActionItems.Count} testCopyStatus={testCopyStep.Status} safety={safetyDashboard.Value} summaryChars={workflowSummary.Length} actionChars={workflowActionPlan.Length}");

var creatorNoteWorkspace = Path.Combine(project.WorkspaceRoot, "CCZModStudio_TestCopies", "SmokeCreatorNotes_" + DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture));
var creatorNoteGameRoot = Path.Combine(creatorNoteWorkspace, "虚拟备注项目");
Directory.CreateDirectory(creatorNoteGameRoot);
var creatorNoteProject = new CczProject
{
    WorkspaceRoot = creatorNoteWorkspace,
    GameRoot = creatorNoteGameRoot,
    HexTableXmlPath = project.HexTableXmlPath
};
var creatorNoteService = new CreatorNoteService();
var savedCreatorNote = creatorNoteService.Upsert(creatorNoteProject, new CreatorNote
{
    Scope = "数据表单元格",
    TargetKey = "6.5-0 人物#ID=0#字段=级别",
    Title = "曹操等级创作备注",
    Content = "用于验证项目侧创作者备注：记录修改意图、风险和实机验证，不写入游戏文件。",
    Tags = "Smoke,待办,风险",
    SourceHint = "SmokeTests 自动创建"
});
var creatorNoteStore = creatorNoteService.GetStorePath(creatorNoteProject);
var loadedCreatorNotes = creatorNoteService.Load(creatorNoteProject);
if (!File.Exists(creatorNoteStore) ||
    loadedCreatorNotes.Count != 1 ||
    loadedCreatorNotes[0].Id != savedCreatorNote.Id ||
    !loadedCreatorNotes[0].SafetyNote.Contains("不写入游戏文件", StringComparison.Ordinal) ||
    !File.ReadAllText(creatorNoteStore).Contains("曹操等级创作备注", StringComparison.Ordinal))
{
    throw new InvalidOperationException("创作者备注 JSON 保存/读取验证失败。");
}
var updatedCreatorNote = creatorNoteService.Upsert(creatorNoteProject, new CreatorNote
{
    Id = savedCreatorNote.Id,
    Scope = "数据表单元格",
    TargetKey = savedCreatorNote.TargetKey,
    Title = savedCreatorNote.Title,
    Content = savedCreatorNote.Content + "\r\n更新：已补充二次保存验证。",
    Tags = "Smoke,已验证",
    SourceHint = "SmokeTests 二次保存"
});
loadedCreatorNotes = creatorNoteService.Load(creatorNoteProject);
var creatorNoteBackups = Directory.GetFiles(Path.GetDirectoryName(creatorNoteStore)!, "*.bak.json");
if (loadedCreatorNotes.Count != 1 ||
    loadedCreatorNotes[0].Id != updatedCreatorNote.Id ||
    !loadedCreatorNotes[0].Content.Contains("二次保存验证", StringComparison.Ordinal) ||
    creatorNoteBackups.Length == 0)
{
    throw new InvalidOperationException("创作者备注更新或 JSON 备份验证失败。");
}
var filteredCreatorNotes = creatorNoteService.Filter(loadedCreatorNotes, "已验证");
var creatorNoteCsv = creatorNoteService.ExportCsv(creatorNoteProject, loadedCreatorNotes);
if (filteredCreatorNotes.Count != 1 ||
    !File.Exists(creatorNoteCsv) ||
    !File.ReadAllText(creatorNoteCsv).Contains("曹操等级创作备注", StringComparison.Ordinal) ||
    Directory.EnumerateFiles(creatorNoteGameRoot, "*", SearchOption.AllDirectories).Any())
{
    throw new InvalidOperationException("创作者备注筛选/CSV导出/游戏目录零污染验证失败。");
}
var creatorNoteNavigation = new CreatorNoteNavigationService();
var tableNavigation = creatorNoteNavigation.Parse(loadedCreatorNotes[0]);
var svCommandNavigation = creatorNoteNavigation.Parse(new CreatorNote
{
    Scope = "SV命令",
    TargetKey = "SV004.E5S#Scene=1#Section=1#Command=4#Offset=0x000006"
});
var svTextNote = new CreatorNote
{
    Scope = "SV文本",
    TargetKey = "SV004.E5S#TextIndex=0#Offset=0x000120",
    Title = "SV004 开场对白备注",
    Content = "用于验证 SV 文本线索详情区能反显相关创作者备注。",
    Tags = "Smoke,SV文本"
};
var svTextNavigation = creatorNoteNavigation.Parse(svTextNote);
var eexCrossNote = new CreatorNote
{
    Scope = "EEX跨文件对比",
    TargetKey = "EexCross#Base=R_00.eex#PeerKind=同编号R/S#Category=S剧本EEX#File=S_00.eex#Role=文本/说明/动作名候选",
    Title = "EEX跨文件对比备注",
    Content = "验证 EEX 跨文件对比对象详情区能反显相关创作者备注。",
    Tags = "Smoke,EEX跨文件"
};
var eexCrossNavigation = creatorNoteNavigation.Parse(eexCrossNote);
var eexEntryNote = new CreatorNote
{
    Scope = "EEX区段",
    TargetKey = "EexEntry#File=R_00.eex#Category=R剧本EEX#Index=3#Offset=0x002DE8",
    Title = "EEX区段备注",
    Content = "验证 EEX 区段探针详情区能反显相关创作者备注。",
    Tags = "Smoke,EEX区段"
};
var eexEntryNavigation = creatorNoteNavigation.Parse(eexEntryNote);
var hexzmapNavigation = creatorNoteNavigation.Parse(new CreatorNote
{
    Scope = "Hexzmap地形块",
    TargetKey = "M004#Offset=0x000E20"
});
var mapLinkNavigation = creatorNoteNavigation.Parse(new CreatorNote
{
    Scope = "关卡地图联动",
    TargetKey = "SV004.E5S->M004"
});
var lsNavigation = creatorNoteNavigation.Parse(new CreatorNote
{
    Scope = "Ls/E5资源",
    TargetKey = "根目录E5/Data.e5"
});
var resourceDiagnosticNavigation = creatorNoteNavigation.Parse(new CreatorNote
{
    Scope = "资源诊断",
    TargetKey = "资源诊断#分类=地图图片#规则=连续编号缺口#编号=Map#对象=M000.jpg"
});
var workflowActionNavigation = creatorNoteNavigation.Parse(new CreatorNote
{
    Scope = "工作台行动",
    TargetKey = ProjectWorkflowGuideService.BuildWorkflowActionTargetKey("SV高风险命令")
});
if (!tableNavigation.IsRecognized ||
    tableNavigation.Kind != "数据表单元格" ||
    tableNavigation.TableName != "6.5-0 人物" ||
    tableNavigation.RowId != "0" ||
    tableNavigation.FieldName != "级别" ||
    !svCommandNavigation.IsRecognized ||
    svCommandNavigation.FileName != "SV004.E5S" ||
    svCommandNavigation.CommandIndex != 4 ||
    !svTextNavigation.IsRecognized ||
    svTextNavigation.Kind != "SV文本" ||
    svTextNavigation.FileName != "SV004.E5S" ||
    svTextNavigation.TextIndex != 0 ||
    !eexCrossNavigation.IsRecognized ||
    eexCrossNavigation.Kind != "EEX跨文件对比" ||
    eexCrossNavigation.BaseFileName != "R_00.eex" ||
    eexCrossNavigation.FileName != "S_00.eex" ||
    eexCrossNavigation.PeerKind != "同编号R/S" ||
    eexCrossNavigation.RoleHint != "文本/说明/动作名候选" ||
    !eexEntryNavigation.IsRecognized ||
    eexEntryNavigation.Kind != "EEX区段" ||
    eexEntryNavigation.FileName != "R_00.eex" ||
    eexEntryNavigation.Category != "R剧本EEX" ||
    eexEntryNavigation.SectionIndex != 3 ||
    eexEntryNavigation.OffsetHex != "0x002DE8" ||
    !hexzmapNavigation.IsRecognized ||
    hexzmapNavigation.MapId != "M004" ||
    !mapLinkNavigation.IsRecognized ||
    mapLinkNavigation.MapId != "M004" ||
    !lsNavigation.IsRecognized ||
    lsNavigation.Kind != "Ls/E5资源" ||
    lsNavigation.Category != "根目录E5" ||
    lsNavigation.Name != "Data.e5" ||
    !resourceDiagnosticNavigation.IsRecognized ||
    resourceDiagnosticNavigation.Kind != "资源诊断" ||
    resourceDiagnosticNavigation.Category != "地图图片" ||
    resourceDiagnosticNavigation.Rule != "连续编号缺口" ||
    resourceDiagnosticNavigation.Id != "Map" ||
    resourceDiagnosticNavigation.Name != "M000.jpg" ||
    !workflowActionNavigation.IsRecognized ||
    workflowActionNavigation.Kind != "工作台行动" ||
    workflowActionNavigation.Name != "SV高风险命令")
{
    throw new InvalidOperationException("创作者备注目标解析/互跳键验证失败。");
}
Console.WriteLine($"CREATOR_NOTE_NAVIGATION table={tableNavigation.DisplayText} sv={svCommandNavigation.DisplayText} svText={svTextNavigation.DisplayText} eexEntry={eexEntryNavigation.DisplayText} eexCross={eexCrossNavigation.DisplayText} hex={hexzmapNavigation.DisplayText} map={mapLinkNavigation.DisplayText} ls={lsNavigation.DisplayText} diag={resourceDiagnosticNavigation.DisplayText} action={workflowActionNavigation.DisplayText}");
var creatorNoteRelationService = new CreatorNoteRelationService();
var relatedCreatorNotes = creatorNoteRelationService.FindExact(loadedCreatorNotes, "数据表单元格", savedCreatorNote.TargetKey);
var relatedCreatorNoteSummary = creatorNoteRelationService.BuildSummary(loadedCreatorNotes, "数据表单元格", savedCreatorNote.TargetKey);
var mixedCreatorNotes = loadedCreatorNotes.Concat(new[] { svTextNote, eexEntryNote, eexCrossNote }).ToList();
var relatedScenarioTextNoteSummary = creatorNoteRelationService.BuildSummary(mixedCreatorNotes, "SV文本", svTextNote.TargetKey);
var relatedEexEntryNoteSummary = creatorNoteRelationService.BuildSummary(mixedCreatorNotes, "EEX区段", eexEntryNote.TargetKey);
var relatedEexCrossNoteSummary = creatorNoteRelationService.BuildSummary(mixedCreatorNotes, "EEX跨文件对比", eexCrossNote.TargetKey);
var unrelatedCreatorNoteSummary = creatorNoteRelationService.BuildSummary(loadedCreatorNotes, "SV命令", "SV999.E5S#Scene=1#Section=1#Command=1#Offset=0x000000");
if (relatedCreatorNotes.Count != 1 ||
    !relatedCreatorNoteSummary.Contains("相关创作者备注：1 条", StringComparison.Ordinal) ||
    !relatedCreatorNoteSummary.Contains("曹操等级创作备注", StringComparison.Ordinal) ||
    !relatedScenarioTextNoteSummary.Contains("相关创作者备注：1 条", StringComparison.Ordinal) ||
    !relatedScenarioTextNoteSummary.Contains("SV004 开场对白备注", StringComparison.Ordinal) ||
    !relatedEexEntryNoteSummary.Contains("相关创作者备注：1 条", StringComparison.Ordinal) ||
    !relatedEexEntryNoteSummary.Contains("EEX区段备注", StringComparison.Ordinal) ||
    !relatedEexCrossNoteSummary.Contains("相关创作者备注：1 条", StringComparison.Ordinal) ||
    !relatedEexCrossNoteSummary.Contains("EEX跨文件对比备注", StringComparison.Ordinal) ||
    !unrelatedCreatorNoteSummary.Contains("暂无", StringComparison.Ordinal))
{
    throw new InvalidOperationException("创作者备注关联摘要验证失败。");
}
Console.WriteLine($"CREATOR_NOTE_RELATION related={relatedCreatorNotes.Count} svTextSummaryChars={relatedScenarioTextNoteSummary.Length} eexEntrySummaryChars={relatedEexEntryNoteSummary.Length} eexCrossSummaryChars={relatedEexCrossNoteSummary.Length} summaryChars={relatedCreatorNoteSummary.Length}");
if (!creatorNoteService.Delete(creatorNoteProject, updatedCreatorNote.Id) || creatorNoteService.Load(creatorNoteProject).Count != 0)
{
    throw new InvalidOperationException("创作者备注删除验证失败。");
}
Console.WriteLine($"CREATOR_NOTE_SERVICE OK store={creatorNoteStore} csv={Path.GetFileName(creatorNoteCsv)} backups={creatorNoteBackups.Length}");

var gameResources = new GameResourceIndexer().Index(project);
Console.WriteLine($"GAME_RESOURCE items={gameResources.Count} categories={gameResources.Select(x => x.Category).Distinct().Count()}");
if (gameResources.Count == 0 || string.IsNullOrWhiteSpace(gameResources[0].Annotation))
{
    throw new InvalidOperationException("\u6e38\u620f\u8d44\u6e90\u7d22\u5f15\u4e2d\u6587\u6ce8\u91ca\u672a\u751f\u6210\u3002");
}
Console.WriteLine($"GAME_RESOURCE_ANNOTATION first={gameResources[0].Annotation[..Math.Min(24, gameResources[0].Annotation.Length)]}");
var resourceDiagnostics = new ResourceDiagnosticService().Analyze(gameResources);
var resourceDiagnosticErrors = resourceDiagnostics.Count(x => x.Severity == "Error");
var resourceDiagnosticWarnings = resourceDiagnostics.Count(x => x.Severity == "Warn");
var resourceDiagnosticInfos = resourceDiagnostics.Count(x => x.Severity == "Info");
Console.WriteLine($"RESOURCE_DIAGNOSTIC items={resourceDiagnostics.Count} errors={resourceDiagnosticErrors} warnings={resourceDiagnosticWarnings} infos={resourceDiagnosticInfos}");
if (resourceDiagnostics.Count == 0 || !resourceDiagnostics.Any(x => x.Rule == "\u5206\u7c7b\u6982\u89c8"))
{
    throw new InvalidOperationException("\u8d44\u6e90\u8bca\u65ad\u672a\u751f\u6210\u5206\u7c7b\u6982\u89c8\u3002");
}
if (resourceDiagnostics.Any(x => string.IsNullOrWhiteSpace(x.Suggestion)))
{
    throw new InvalidOperationException("\u8d44\u6e90\u8bca\u65ad\u7f3a\u5c11\u4e2d\u6587\u5efa\u8bae\u3002");
}
foreach (var diagnostic in resourceDiagnostics.Take(3))
{
    Console.WriteLine($"RESOURCE_DIAGNOSTIC_ROW {diagnostic.Severity} {diagnostic.Category}/{diagnostic.Rule} status={diagnostic.Status} suggestion={diagnostic.Suggestion[..Math.Min(20, diagnostic.Suggestion.Length)]}");
}
var resourceWorkflowDashboard = workflowGuideService.BuildDashboard(
    project,
    tables.Count,
    auditItems,
    resourceDiagnostics,
    Array.Empty<ProjectDiffItem>(),
    Array.Empty<BackupHistoryItem>(),
    loadedCreatorNotes);
var resourceDashboard = resourceWorkflowDashboard.Single(x => x.Area == "资源诊断");
var noteDashboard = resourceWorkflowDashboard.Single(x => x.Area == "创作者备注");
if (!resourceDashboard.Value.Contains("警告", StringComparison.Ordinal) ||
    !resourceDashboard.Suggestion.Contains("资源诊断", StringComparison.Ordinal) ||
    !noteDashboard.Value.Contains("待办", StringComparison.Ordinal) ||
    !noteDashboard.Value.Contains("风险", StringComparison.Ordinal))
{
    throw new InvalidOperationException("制作向导工作台未正确汇总资源诊断或创作者备注计数。");
}
Console.WriteLine($"WORKFLOW_DASHBOARD resource={resourceDashboard.Value} notes={noteDashboard.Value} level={resourceDashboard.Level}");
var resourceReferenceDiagnostics = new ResourceReferenceDiagnosticService().AnalyzeImageAssignments(project, imageAssignments, gameResources);
var resourceReferenceWarnings = resourceReferenceDiagnostics.Count(x => x.Severity == "Warn");
Console.WriteLine($"RESOURCE_REFERENCE_DIAGNOSTIC items={resourceReferenceDiagnostics.Count} warnings={resourceReferenceWarnings}");
if (resourceReferenceDiagnostics.Count == 0 || !resourceReferenceDiagnostics.Any(x => x.Rule == "\u5f15\u7528\u6982\u89c8"))
{
    throw new InvalidOperationException("\u8d44\u6e90\u5f15\u7528\u8bca\u65ad\u672a\u751f\u6210\u5f15\u7528\u6982\u89c8\u3002");
}
if (resourceReferenceDiagnostics.Any(x => string.IsNullOrWhiteSpace(x.Suggestion)))
{
    throw new InvalidOperationException("\u8d44\u6e90\u5f15\u7528\u8bca\u65ad\u7f3a\u5c11\u4e2d\u6587\u5efa\u8bae\u3002");
}
foreach (var referenceDiagnostic in resourceReferenceDiagnostics.Take(3))
{
    Console.WriteLine($"RESOURCE_REFERENCE_ROW {referenceDiagnostic.Severity} {referenceDiagnostic.Category}/{referenceDiagnostic.Rule} status={referenceDiagnostic.Status}");
}
var tableReferenceDiagnostics = new TableReferenceDiagnosticService().Analyze(project, tables);
var tableReferenceWarnings = tableReferenceDiagnostics.Count(x => x.Severity == "Warn");
var tableReferenceOverview = tableReferenceDiagnostics.FirstOrDefault(x => x.Rule == "跨表引用总览")
                             ?? throw new InvalidOperationException("数据表跨表引用诊断缺少总览。");
var personJobReference = tableReferenceDiagnostics.FirstOrDefault(x =>
                             x.Rule == "跨表引用概览" &&
                             x.Name == "6.5-0 人物/职业")
                         ?? throw new InvalidOperationException("数据表跨表引用诊断缺少人物职业概览。");
if (!tableReferenceOverview.Detail.Contains("源表：6.5-0 人物", StringComparison.Ordinal) ||
    !personJobReference.Detail.Contains("字段：职业", StringComparison.Ordinal) ||
    tableReferenceDiagnostics.Any(x => string.IsNullOrWhiteSpace(x.Suggestion)))
{
    throw new InvalidOperationException("数据表跨表引用诊断缺少中文定位标记或创作建议。");
}
var tableReferenceNav = new ResourceDiagnosticNavigationService().Resolve(personJobReference, Array.Empty<ScenarioMapLinkInfo>(), gameResources);
if (!tableReferenceNav.CanJumpDataTable ||
    tableReferenceNav.TableName != "6.5-0 人物" ||
    tableReferenceNav.TableRowId != "0" ||
    tableReferenceNav.TableFieldName != "职业")
{
    throw new InvalidOperationException("数据表跨表引用诊断未能解析为可跳转的数据表单元格。");
}
Console.WriteLine($"TABLE_REFERENCE_DIAGNOSTIC items={tableReferenceDiagnostics.Count} warnings={tableReferenceWarnings} overview={tableReferenceOverview.Status}");
Console.WriteLine($"TABLE_REFERENCE_DIAGNOSTIC_NAV table={tableReferenceNav.TableName} row={tableReferenceNav.TableRowId} field={tableReferenceNav.TableFieldName}");
foreach (var resource in gameResources.Where(x => x.Category == "地图图片").Take(3))
{
    Console.WriteLine($"GAME_RESOURCE_ROW {resource.Category}/{resource.Name} id={resource.Id} size={resource.Width}x{resource.Height} bytes={resource.SizeBytes}");
}

var eexArchives = new EexArchiveReader().ReadAll(project);
Console.WriteLine($"EEX count={eexArchives.Count} categories={eexArchives.Select(x => x.Category).Distinct().Count()} invalidMagic={eexArchives.Count(x => !x.MagicValid)}");
if (eexArchives.Count == 0 || string.IsNullOrWhiteSpace(eexArchives[0].Annotation) || string.IsNullOrWhiteSpace(eexArchives[0].HeaderAnnotation))
{
    throw new InvalidOperationException("EEX 中文注释列未生成。");
}
Console.WriteLine($"ANNOTATION_PROBE EEX annotation={eexArchives[0].Annotation[..Math.Min(24, eexArchives[0].Annotation.Length)]}");
foreach (var eex in eexArchives.Take(5))
{
    Console.WriteLine($"EEX_ROW {eex.Category}/{eex.FileName} id={eex.Id} len={eex.Length} magic={eex.MagicValid} version={eex.VersionHex} entries={eex.EntryCount} texts={eex.TextHintCount}");
}
var eexProbeTarget = eexArchives.FirstOrDefault(x => x.MagicValid && x.Category == "R剧本EEX")
                     ?? eexArchives.FirstOrDefault(x => x.MagicValid)
                     ?? throw new InvalidOperationException("未找到可用于 EEX 区段探针的有效 EEX 文件。");
var eexEntryProbeRows = new EexEntryProbeReader().Probe(eexProbeTarget.Path, eexProbeTarget.Category);
Console.WriteLine($"EEX_ENTRY_PROBE file={eexProbeTarget.FileName} rows={eexEntryProbeRows.Count} sections={eexEntryProbeRows.Count(x => x.NodeType == "区段候选")} textSections={eexEntryProbeRows.Count(x => x.TextHintCount > 0)}");
if (eexEntryProbeRows.Count < 3 ||
    !eexEntryProbeRows.Any(x => x.NodeType == "文件头" && x.Annotation.Contains("EEX", StringComparison.Ordinal)) ||
    !eexEntryProbeRows.Any(x => x.NodeType == "头字段") ||
    !eexEntryProbeRows.Any(x => x.NodeType == "区段候选") ||
    eexEntryProbeRows.Any(x => string.IsNullOrWhiteSpace(x.Annotation)))
{
    throw new InvalidOperationException("EEX 区段/帧候选探针未生成预期中文行。");
}
foreach (var row in eexEntryProbeRows.Where(x => x.NodeType == "区段候选").Take(3))
{
    Console.WriteLine($"EEX_ENTRY_ROW off={row.OffsetHex} len={row.Length} role={row.RoleHint} text={row.TextHintCount} unique={row.UniqueByteCount} zero={row.ZeroPercent:F1}% smallWord={row.SmallWordPercent:F1}%");
}
var eexEntrySample = eexEntryProbeRows.First(x => x.NodeType == "区段候选");
var eexEntrySampleTargetKey = $"EexEntry#File={eexEntrySample.FileName}#Category={eexEntrySample.Category}#Index={eexEntrySample.Index}#Offset={eexEntrySample.OffsetHex}";
var eexEntrySampleNavigation = creatorNoteNavigation.Parse(new CreatorNote
{
    Scope = "EEX区段",
    TargetKey = eexEntrySampleTargetKey
});
if (!eexEntrySampleNavigation.IsRecognized ||
    eexEntrySampleNavigation.Kind != "EEX区段" ||
    eexEntrySampleNavigation.FileName != eexEntrySample.FileName ||
    eexEntrySampleNavigation.Category != eexEntrySample.Category ||
    eexEntrySampleNavigation.SectionIndex != eexEntrySample.Index ||
    eexEntrySampleNavigation.OffsetHex != eexEntrySample.OffsetHex)
{
    throw new InvalidOperationException("EEX 区段备注目标键无法被导航服务识别。");
}
Console.WriteLine($"EEX_ENTRY_NOTE_NAV target={eexEntrySampleNavigation.DisplayText}");
var eexTreeDetailService = new EexEntryTreeDetailService();
var eexTreeSummary = eexTreeDetailService.BuildTreeSummary(eexEntryProbeRows);
var eexTreeGroups = eexTreeDetailService.BuildGroups(eexEntryProbeRows);
var eexDetailRow = eexEntryProbeRows.First(x => x.NodeType == "区段候选");
var eexNodeDetail = eexTreeDetailService.BuildDetail(eexDetailRow);
if (!eexTreeSummary.Contains("EEX 区段树", StringComparison.Ordinal) ||
    eexTreeGroups.Count == 0 ||
    !eexNodeDetail.Contains("EEX区段节点详情", StringComparison.Ordinal) ||
    !eexNodeDetail.Contains("创作解释", StringComparison.Ordinal) ||
    !eexNodeDetail.Contains("安全边界", StringComparison.Ordinal))
{
    throw new InvalidOperationException("EEX 区段/动作候选树详情缺少中文解释或安全边界。");
}
Console.WriteLine($"EEX_TREE groups={eexTreeGroups.Count} detailChars={eexNodeDetail.Length} firstGroup={eexTreeGroups[0].Name}");

var eexCrossFileComparison = new EexCrossFileComparisonService().Compare(eexProbeTarget, eexArchives);
Console.WriteLine($"EEX_CROSS_FILE target={eexCrossFileComparison.TargetFileName} rows={eexCrossFileComparison.Rows.Count}");
if (!eexCrossFileComparison.Summary.Contains("EEX 跨文件对比", StringComparison.Ordinal) ||
    eexCrossFileComparison.Rows.Count == 0 ||
    eexCrossFileComparison.Rows.Any(x => string.IsNullOrWhiteSpace(x.DifferenceHint) || string.IsNullOrWhiteSpace(x.Annotation)) ||
    !eexCrossFileComparison.Rows.Any(x => x.PeerKind == "选中文件"))
{
    throw new InvalidOperationException("EEX 跨文件对比未生成预期摘要、差异说明或中文注释。");
}
var eexCrossSample = eexCrossFileComparison.Rows.First();
var eexCrossSampleTargetKey = $"EexCross#Base={eexCrossFileComparison.TargetFileName}#PeerKind={eexCrossSample.PeerKind}#Category={eexCrossSample.Category}#File={eexCrossSample.FileName}#Role={eexCrossSample.RoleHint}";
var eexCrossSampleNavigation = creatorNoteNavigation.Parse(new CreatorNote
{
    Scope = "EEX跨文件对比",
    TargetKey = eexCrossSampleTargetKey
});
if (!eexCrossSampleNavigation.IsRecognized ||
    eexCrossSampleNavigation.Kind != "EEX跨文件对比" ||
    eexCrossSampleNavigation.BaseFileName != eexCrossFileComparison.TargetFileName ||
    eexCrossSampleNavigation.FileName != eexCrossSample.FileName ||
    eexCrossSampleNavigation.Category != eexCrossSample.Category)
{
    throw new InvalidOperationException("EEX 跨文件对比备注目标键无法被导航服务识别。");
}
Console.WriteLine($"EEX_CROSS_NOTE_NAV target={eexCrossSampleNavigation.DisplayText}");
foreach (var row in eexCrossFileComparison.Rows.Take(3))
{
    Console.WriteLine($"EEX_CROSS_FILE_ROW {row.PeerKind} {row.Category}/{row.FileName} role={row.RoleHint} sections={row.SectionCount} len={row.TotalLength} note={row.DifferenceHint[..Math.Min(24, row.DifferenceHint.Length)]}");
}

var eexHeatmapSource = eexEntryProbeRows
    .Where(x => x.NodeType == "区段候选" && x.Length > 0)
    .OrderByDescending(x => x.Length)
    .First();
var eexHeatmapOffset = Convert.ToInt32(eexHeatmapSource.OffsetHex.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
    ? eexHeatmapSource.OffsetHex[2..]
    : eexHeatmapSource.OffsetHex, 16);
var eexHeatmapService = new EexByteHeatmapService();
var eexHeatmap = eexHeatmapService.Analyze(
    eexProbeTarget.Path,
    eexProbeTarget.Category,
    eexHeatmapOffset,
    eexHeatmapSource.Length,
    $"{eexHeatmapSource.NodeType}/{eexHeatmapSource.RoleHint}");
if (eexHeatmap.CellValues.Length == 0 ||
    eexHeatmap.Width <= 0 ||
    eexHeatmap.Height <= 0 ||
    eexHeatmap.BytesPerCell <= 0 ||
    string.IsNullOrWhiteSpace(eexHeatmap.Annotation) ||
    !eexHeatmap.Annotation.Contains("只读", StringComparison.Ordinal))
{
    throw new InvalidOperationException("EEX 字节热力图分析结果不完整。");
}
var eexHeatmapExportDir = Path.Combine(project.WorkspaceRoot, "CCZModStudio_Exports");
Directory.CreateDirectory(eexHeatmapExportDir);
var eexHeatmapPng = Path.Combine(eexHeatmapExportDir, "Smoke_EEX_字节热力图.png");
using (var bitmap = eexHeatmapService.Render(eexHeatmap, cellSize: 2))
{
    bitmap.Save(eexHeatmapPng, System.Drawing.Imaging.ImageFormat.Png);
}
if (!File.Exists(eexHeatmapPng) || new FileInfo(eexHeatmapPng).Length == 0)
{
    throw new InvalidOperationException("EEX 字节热力图 PNG 未成功导出。");
}
Console.WriteLine($"EEX_HEATMAP file={eexProbeTarget.FileName} range={eexHeatmap.OffsetHex}-{eexHeatmap.EndOffsetHex} cells={eexHeatmap.CellCount} grid={eexHeatmap.Width}x{eexHeatmap.Height} bpc={eexHeatmap.BytesPerCell} entropy={eexHeatmap.Entropy:F2} png={Path.GetFileName(eexHeatmapPng)}");

var scenarios = new ScenarioFileReader().ReadAll(project, sceneDoc);
Console.WriteLine($"SCENARIO count={scenarios.Count} kinds={scenarios.Select(x => x.Kind).Distinct().Count()} textFiles={scenarios.Count(x => x.TextHintCount > 0)}");
if (scenarios.Count == 0 || string.IsNullOrWhiteSpace(scenarios[0].Annotation) || string.IsNullOrWhiteSpace(scenarios[0].UsageAnnotation))
{
    throw new InvalidOperationException("SV/E5S 中文注释列未生成。");
}
Console.WriteLine($"ANNOTATION_PROBE SCENARIO annotation={scenarios[0].Annotation[..Math.Min(24, scenarios[0].Annotation.Length)]}");
foreach (var scenario in scenarios.Take(5))
{
    Console.WriteLine($"SCENARIO_ROW {scenario.FileName} id={scenario.Id} kind={scenario.Kind} len={scenario.Length} used={scenario.UsedPercent:F1}% words={scenario.WordCount} cmds={scenario.RecognizedCommandCount} title={scenario.TitleHint}");
}
ScenarioStructureProbeResult? structureProbe = null;
if (sceneDoc != null)
{
    var scenarioProbePath = Path.Combine(project.GameRoot, "SV", "SV001.E5S");
    var commandProbe = new ScenarioCommandProbeReader().Probe(scenarioProbePath, sceneDoc, maxRows: 80);
    Console.WriteLine($"SCENARIO_CMD_PROBE file=SV001.E5S rows={commandProbe.Count} unique={commandProbe.Select(x => x.CommandId).Distinct().Count()}");
    foreach (var row in commandProbe.Take(8))
    {
        Console.WriteLine($"SCENARIO_CMD_ROW #{row.Index} off={row.OffsetHex} id={row.CommandIdHex} name={row.CommandName} confidence={row.Confidence} ctx={row.ContextWordsHex}");
    }

    var structureProbePath = Path.Combine(project.GameRoot, "SV", "SV004.E5S");
    structureProbe = new ScenarioStructureProbeReader().Build(structureProbePath, sceneDoc, maxCommandRows: 120, project: project, tables: tables);
    Console.WriteLine($"SCENARIO_STRUCTURE file={structureProbe.FileName} scenes={structureProbe.SceneCount} sections={structureProbe.SectionCount} commands={structureProbe.CommandCandidateCount} rows={structureProbe.Rows.Count}");
    if (structureProbe.Rows.Count == 0
        || !structureProbe.Rows.Any(x => x.NodeType == "Scene候选")
        || !structureProbe.Rows.Any(x => x.NodeType == "Command候选")
        || !structureProbe.XmlText.Contains("<ScenarioStructure", StringComparison.Ordinal)
        || !structureProbe.XmlText.Contains("<Command", StringComparison.Ordinal))
    {
        throw new InvalidOperationException("剧本结构草图/XML 未生成预期 Scene/Command 节点。");
    }
    if (structureProbe.Rows.Any(x => string.IsNullOrWhiteSpace(x.Annotation)))
    {
        throw new InvalidOperationException("剧本结构草图缺少中文注释。");
    }
    if (!structureProbe.Rows.Any(x => !string.IsNullOrWhiteSpace(x.ReferenceHint))
        || !structureProbe.XmlText.Contains("<ReferenceHint>", StringComparison.Ordinal))
    {
        throw new InvalidOperationException("剧本结构草图缺少跨表/资源候选解释。");
    }
    if (!structureProbe.Rows.Any(x => x.NodeType == "Command候选" && x.HasCommandTemplate)
        || !structureProbe.Rows.Any(x => x.CommandTemplateHint.Contains("变量编号", StringComparison.Ordinal)
                                      || x.CommandTemplateHint.Contains("信息编号", StringComparison.Ordinal)
                                      || x.CommandTemplateHint.Contains("目标X坐标", StringComparison.Ordinal))
        || !structureProbe.XmlText.Contains("<CommandTemplateHint", StringComparison.Ordinal)
        || !structureProbe.Summary.Contains("命中常见命令参数模板", StringComparison.Ordinal))
    {
        throw new InvalidOperationException("剧本结构草图缺少命令参数模板短提示列或 XML 输出。");
    }
    foreach (var row in structureProbe.Rows.Where(x => x.NodeType == "Command候选").Take(3))
    {
        Console.WriteLine($"SCENARIO_STRUCTURE_ROW scene={row.SceneIndex} section={row.SectionIndex} off={row.OffsetHex} cmd={row.CommandIdHex}/{row.CommandName} template={row.CommandTemplateHint[..Math.Min(24, row.CommandTemplateHint.Length)]} ref={row.ReferenceHint[..Math.Min(24, row.ReferenceHint.Length)]} note={row.Annotation[..Math.Min(24, row.Annotation.Length)]}");
    }

    var structureFilterService = new ScenarioStructureFilterService();
    var templateRows = structureFilterService.Filter(structureProbe.Rows, new ScenarioStructureFilterOptions { TemplatesOnly = true });
    var textRows = structureFilterService.Filter(structureProbe.Rows, new ScenarioStructureFilterOptions { TextRelatedOnly = true });
    var mapRows = structureFilterService.Filter(structureProbe.Rows, new ScenarioStructureFilterOptions { MapCoordinateOnly = true });
    var highRiskRows = structureFilterService.Filter(structureProbe.Rows, new ScenarioStructureFilterOptions { HighRiskOnly = true });
    var keywordRows = structureFilterService.Filter(structureProbe.Rows, new ScenarioStructureFilterOptions { Keyword = "内部信息", TextRelatedOnly = true });
    var filterSummary = structureFilterService.BuildSummary(structureProbe.Rows, templateRows, new ScenarioStructureFilterOptions { TemplatesOnly = true });
    var allStructureCommands = structureProbe.Rows.Count(x => x.NodeType == "Command候选");
    var highRiskCommandRows = highRiskRows.Where(x => x.NodeType == "Command候选").ToList();
    var highRiskSampleReason = highRiskCommandRows.Count > 0
        ? structureFilterService.BuildHighRiskReason(highRiskCommandRows[0])
        : string.Empty;
    if (!templateRows.Any(x => x.NodeType == "Scene候选") ||
        !templateRows.Any(x => x.NodeType == "Section候选") ||
        !templateRows.Any(x => x.NodeType == "Command候选" && x.HasCommandTemplate) ||
        !textRows.Any(x => x.NodeType == "Command候选" && structureFilterService.IsTextRelated(x)) ||
        !mapRows.Any(x => x.NodeType == "Command候选" && structureFilterService.IsMapCoordinateRelated(x)) ||
        !highRiskRows.Any(x => x.NodeType == "Command候选" && structureFilterService.IsHighRisk(x)) ||
        highRiskCommandRows.Count >= allStructureCommands ||
        string.IsNullOrWhiteSpace(highRiskSampleReason) ||
        !keywordRows.Any(x => x.CommandName.Contains("内部信息", StringComparison.Ordinal)) ||
        !filterSummary.Contains("SV 结构草图筛选", StringComparison.Ordinal))
    {
        throw new InvalidOperationException("SV 结构草图筛选服务未能按模板/文本/地图/高风险/关键字生成预期结果。");
    }
    Console.WriteLine($"SCENARIO_STRUCTURE_FILTER templateCommands={templateRows.Count(x => x.NodeType == "Command候选")} textCommands={textRows.Count(x => x.NodeType == "Command候选")} mapCommands={mapRows.Count(x => x.NodeType == "Command候选")} riskCommands={highRiskCommandRows.Count}/{allStructureCommands} firstRisk={highRiskSampleReason[..Math.Min(22, highRiskSampleReason.Length)]}");
}
var scenarioTextPath = Path.Combine(project.GameRoot, "SV", "SV004.E5S");
var scenarioTexts = new ScenarioTextReader().Read(scenarioTextPath);
Console.WriteLine($"SCENARIO_TEXT file=SV004.E5S rows={scenarioTexts.Count} kinds={scenarioTexts.Select(x => x.Kind).Distinct().Count()} multiline={scenarioTexts.Count(x => x.HasNewLines)}");
foreach (var text in scenarioTexts.Take(6))
{
    Console.WriteLine($"SCENARIO_TEXT_ROW #{text.Index} off={text.OffsetHex} kind={text.Kind} bytes={text.ByteLength} preview={text.Preview}");
}
CreatorNote? scenarioCommandReferenceNoteDraft = null;
if (structureProbe != null)
{
    var commandReferenceService = new ScenarioCommandReferenceNavigationService();
    var textReferenceRow = structureProbe.Rows.FirstOrDefault(row => row.NodeType == "Command候选" && row.CommandName.Contains("内部信息", StringComparison.Ordinal))
                           ?? structureProbe.Rows.First(row => row.NodeType == "Command候选");
    var commandReferenceTargets = commandReferenceService.Analyze(project, tables, textReferenceRow, structureProbe.FileName, null, scenarioTexts);
    var commandReferenceSummary = commandReferenceService.BuildSummary(commandReferenceTargets);
    if (commandReferenceTargets.Count == 0 ||
        !commandReferenceTargets.Any(x => x.CanJumpDataTable && x.Kind is "人物" or "物品" or "策略") ||
        !commandReferenceTargets.Any(x => x.CanJumpScenarioText) ||
        !commandReferenceSummary.Contains("可跳转引用候选", StringComparison.Ordinal) ||
        !commandReferenceSummary.Contains("安全边界", StringComparison.Ordinal))
    {
        throw new InvalidOperationException("SV 命令引用候选导航未生成数据表/文本目标或中文摘要。");
    }
    Console.WriteLine($"SCENARIO_COMMAND_REFERENCE targets={commandReferenceTargets.Count} data={commandReferenceTargets.First(x => x.CanJumpDataTable).DisplayText} text={commandReferenceTargets.First(x => x.CanJumpScenarioText).DisplayText}");

    var commandReferenceNoteTemplateService = new ScenarioCommandReferenceNoteTemplateService();
    var commandReferenceNoteDraft = commandReferenceNoteTemplateService.BuildDraft(structureProbe, textReferenceRow, commandReferenceTargets);
    if (commandReferenceNoteDraft.Scope != "R/S命令" ||
        !commandReferenceNoteDraft.TargetKey.Contains("SV004.E5S#Scene=", StringComparison.Ordinal) ||
        !commandReferenceNoteDraft.Content.Contains("可跳转引用候选", StringComparison.Ordinal) ||
        !commandReferenceNoteDraft.Content.Contains("旧工具对照", StringComparison.Ordinal) ||
        !commandReferenceNoteDraft.Content.Contains("实机验证", StringComparison.Ordinal) ||
        !commandReferenceNoteDraft.Content.Contains("安全边界", StringComparison.Ordinal) ||
        !commandReferenceNoteDraft.Content.Contains("人物", StringComparison.Ordinal) ||
        !commandReferenceNoteDraft.Content.Contains("文本", StringComparison.Ordinal))
    {
        throw new InvalidOperationException("R/S eex 命令引用备注模板缺少预期中文内容。");
    }
    Console.WriteLine($"SCENARIO_COMMAND_REFERENCE_NOTE_TEMPLATE title={commandReferenceNoteDraft.Title} chars={commandReferenceNoteDraft.Content.Length}");
    scenarioCommandReferenceNoteDraft = commandReferenceNoteDraft;
}
var scenarioExportProject = detector.CreateProjectFromGameRoot(project.GameRoot);
var textExportService = new ScenarioTextExportService();
var textCsv = textExportService.ExportCsv(scenarioExportProject, "SV004.E5S", scenarioTexts);
var textTxt = textExportService.ExportTxt(scenarioExportProject, "SV004.E5S", scenarioTexts);
if (!File.Exists(textCsv) || !File.Exists(textTxt))
{
    throw new InvalidOperationException("剧本文本导出文件未生成。");
}
var scenarioTextCsvHeader = File.ReadLines(textCsv).FirstOrDefault() ?? string.Empty;
if (!scenarioTextCsvHeader.Contains("Annotation", StringComparison.Ordinal) ||
    !scenarioTextCsvHeader.Contains("GbkByteCount", StringComparison.Ordinal) ||
    !scenarioTextCsvHeader.Contains("WriteStatus", StringComparison.Ordinal))
{
    throw new InvalidOperationException("剧本文本导出 CSV 缺少注释/字节状态列。");
}
Console.WriteLine($"SCENARIO_TEXT_EXPORT csv={Path.GetFileName(textCsv)} txt={Path.GetFileName(textTxt)}");

var lsResources = new LsResourceReader().ReadAll(project);
Console.WriteLine($"LS_RESOURCE count={lsResources.Count} categories={lsResources.Select(x => x.Category).Distinct().Count()} roles={lsResources.Select(x => x.RoleHint).Distinct().Count()}");
if (lsResources.Count == 0 || string.IsNullOrWhiteSpace(lsResources[0].Annotation) || string.IsNullOrWhiteSpace(lsResources[0].RoleReason))
{
    throw new InvalidOperationException("Ls/E5 中文注释列未生成。");
}
Console.WriteLine($"ANNOTATION_PROBE LS annotation={lsResources[0].Annotation[..Math.Min(24, lsResources[0].Annotation.Length)]}");
foreach (var ls in lsResources.Take(8))
{
    Console.WriteLine($"LS_ROW {ls.Category}/{ls.FileName} role={ls.RoleHint} len={ls.Length} payload={ls.PayloadLength} unique={ls.UniqueByteCount} zero={ls.ZeroPercent:F1}% top={ls.TopBytesHex}");
}
var lsHeatmapTarget = lsResources.FirstOrDefault(x => x.FileName.Equals("Data.e5", StringComparison.OrdinalIgnoreCase))
    ?? lsResources.First(x => x.PayloadLength > 0);
var lsHeatmapLength = (int)Math.Min(lsHeatmapTarget.PayloadLength, 1_048_576);
var lsHeatmap = eexHeatmapService.Analyze(
    lsHeatmapTarget.Path,
    lsHeatmapTarget.Category,
    lsHeatmapTarget.PayloadOffset,
    lsHeatmapLength,
    $"Ls/E5载荷/{lsHeatmapTarget.RoleHint}");
if (lsHeatmap.CellValues.Length == 0 ||
    lsHeatmap.Width <= 0 ||
    lsHeatmap.Height <= 0 ||
    string.IsNullOrWhiteSpace(lsHeatmap.Annotation) ||
    !lsHeatmap.SourceKind.Contains("Ls/E5载荷", StringComparison.Ordinal))
{
    throw new InvalidOperationException("Ls/E5 字节热力图分析结果不完整。");
}
var lsHeatmapPng = Path.Combine(eexHeatmapExportDir, "Smoke_LsE5_字节热力图.png");
using (var bitmap = eexHeatmapService.Render(lsHeatmap, cellSize: 2))
{
    bitmap.Save(lsHeatmapPng, System.Drawing.Imaging.ImageFormat.Png);
}
if (!File.Exists(lsHeatmapPng) || new FileInfo(lsHeatmapPng).Length == 0)
{
    throw new InvalidOperationException("Ls/E5 字节热力图 PNG 未成功导出。");
}
Console.WriteLine($"LS_HEATMAP file={lsHeatmapTarget.FileName} range={lsHeatmap.OffsetHex}-{lsHeatmap.EndOffsetHex} cells={lsHeatmap.CellCount} grid={lsHeatmap.Width}x{lsHeatmap.Height} bpc={lsHeatmap.BytesPerCell} entropy={lsHeatmap.Entropy:F2} png={Path.GetFileName(lsHeatmapPng)}");

var terrainNameLookup = HexzmapProbeReader.BuildTerrainNameLookup(new MaterialLibraryIndexer().Index(project));
var hexzmapProbeReader = new HexzmapProbeReader();
var hexzmapProbe = hexzmapProbeReader.Read(project, terrainNameLookup);
Console.WriteLine($"HEXZMAP blocks={hexzmapProbe.Blocks.Count} magic={hexzmapProbe.Magic} payload={hexzmapProbe.PayloadLength} trailing={hexzmapProbe.TrailingBytes}");
if (!hexzmapProbe.MagicValid || hexzmapProbe.Blocks.Count == 0 || hexzmapProbe.Blocks[0].Width != 20 || hexzmapProbe.Blocks[0].Height != 20)
{
    throw new InvalidOperationException("Hexzmap 地形探针未按 M000 地图分辨率生成 20x20 地形块。");
}
if (string.IsNullOrWhiteSpace(hexzmapProbe.Blocks[0].Annotation) || string.IsNullOrWhiteSpace(hexzmapProbe.Blocks[0].TopTerrainIds))
{
    throw new InvalidOperationException("Hexzmap 地形探针缺少中文注释或地形统计。");
}
if (hexzmapProbe.Blocks[0].KnownTerrainCount <= 0 || !hexzmapProbe.Blocks[0].TopTerrainNames.Contains("平原", StringComparison.Ordinal))
{
    throw new InvalidOperationException("Hexzmap 地形探针未能结合素材库 hex 标记生成中文地形候选。");
}
var firstHexzmapCells = hexzmapProbeReader.GetBlockCells(hexzmapProbe, hexzmapProbe.Blocks[0]);
if (firstHexzmapCells.Length != hexzmapProbe.Blocks[0].BytesRead)
{
    throw new InvalidOperationException("Hexzmap 候选块载荷长度不符合地图分辨率/48 得到的格数。");
}
var hexzmapRenderService = new HexzmapTerrainRenderService();
using (var terrainPreview = hexzmapRenderService.RenderTerrainCells(firstHexzmapCells, hexzmapProbe.Blocks[0].Width, hexzmapProbe.Blocks[0].Height))
{
    if (terrainPreview.Width != hexzmapProbe.Blocks[0].Width * HexzmapTerrainRenderService.DefaultCellSize ||
        terrainPreview.Height != hexzmapProbe.Blocks[0].Height * HexzmapTerrainRenderService.DefaultCellSize)
    {
        throw new InvalidOperationException("Hexzmap 纯地形色块预览尺寸异常。");
    }
}
var firstHexzmapMapPath = Path.Combine(project.GameRoot, "Map", hexzmapProbe.Blocks[0].MapImageName);
if (File.Exists(firstHexzmapMapPath))
{
    using var sourceMapImage = System.Drawing.Image.FromFile(firstHexzmapMapPath);
    using var overlayPreview = hexzmapRenderService.RenderOverlay(
        firstHexzmapCells,
        hexzmapProbe.Blocks[0].Width,
        hexzmapProbe.Blocks[0].Height,
        firstHexzmapMapPath,
        45);
    if (overlayPreview.Width != sourceMapImage.Width || overlayPreview.Height != sourceMapImage.Height)
    {
        throw new InvalidOperationException("Hexzmap 地图底图叠加预览尺寸未与地图图片对齐。");
    }

    var hexzmapOverlayExportDir = Path.Combine(project.WorkspaceRoot, "CCZModStudio_Exports");
    Directory.CreateDirectory(hexzmapOverlayExportDir);
    var hexzmapOverlayPng = Path.Combine(hexzmapOverlayExportDir, "Smoke_Hexzmap_地图叠加预览.png");
    overlayPreview.Save(hexzmapOverlayPng, System.Drawing.Imaging.ImageFormat.Png);
    if (!File.Exists(hexzmapOverlayPng))
    {
        throw new InvalidOperationException("Hexzmap 地图底图叠加预览 PNG 未导出。");
    }

    Console.WriteLine($"HEXZMAP_OVERLAY map={hexzmapProbe.Blocks[0].MapImageName} size={overlayPreview.Width}x{overlayPreview.Height} png={Path.GetFileName(hexzmapOverlayPng)}");
}
Console.WriteLine($"HEXZMAP_ROW {hexzmapProbe.Blocks[0].MapId} top={hexzmapProbe.Blocks[0].TopTerrainNames} map={hexzmapProbe.Blocks[0].MapImageName}");

var scenarioMapLinks = new ScenarioMapLinkService().BuildLinks(scenarios, gameResources, hexzmapProbe);
var completeScenarioMapLinks = scenarioMapLinks.Count(x => x.Status == "完整候选");
Console.WriteLine($"SCENARIO_MAP_LINK rows={scenarioMapLinks.Count} complete={completeScenarioMapLinks} missing={scenarioMapLinks.Count(x => x.Status.Contains("缺", StringComparison.Ordinal))}");
var sv004Link = scenarioMapLinks.FirstOrDefault(x => x.ScenarioFileName.Equals("SV004.E5S", StringComparison.OrdinalIgnoreCase));
if (sv004Link == null || sv004Link.MapId != "M004" || !sv004Link.MapImageExists || !sv004Link.HexzmapBlockExists)
{
    throw new InvalidOperationException("SV004 关卡地图联动未能同时命中 M004 地图图片和 Hexzmap 地形块。");
}
if (string.IsNullOrWhiteSpace(sv004Link.Annotation) || string.IsNullOrWhiteSpace(sv004Link.Suggestion) || string.IsNullOrWhiteSpace(sv004Link.TopTerrainNames))
{
    throw new InvalidOperationException("关卡地图联动缺少中文注释、建议或地形中文候选。");
}
if (structureProbe != null)
{
    var mapReferenceRow = structureProbe.Rows.FirstOrDefault(row =>
                              row.NodeType == "Command候选" &&
                              (row.ReferenceHint.Contains("地图", StringComparison.Ordinal) ||
                               row.ReferenceHint.Contains("坐标", StringComparison.Ordinal) ||
                               row.CommandName.Contains("地点", StringComparison.Ordinal) ||
                               row.CommandName.Contains("进入", StringComparison.Ordinal)))
                          ?? structureProbe.Rows.First(row => row.NodeType == "Command候选");
    var mapReferenceTargets = new ScenarioCommandReferenceNavigationService().Analyze(project, tables, mapReferenceRow, structureProbe.FileName, sv004Link, scenarioTexts);
    if (!mapReferenceTargets.Any(x => x.CanJumpScenarioMap) ||
        !mapReferenceTargets.Any(x => x.Kind is "地图" or "坐标"))
    {
        throw new InvalidOperationException("SV 命令引用候选未生成地图/坐标联动目标。");
    }
    var firstMapReference = mapReferenceTargets.First(x => x.CanJumpScenarioMap);
    Console.WriteLine($"SCENARIO_COMMAND_MAP_REFERENCE targets={mapReferenceTargets.Count} map={firstMapReference.DisplayText}");

    var checklistService = new ScenarioCommandReferenceChecklistService();
    var checklistNotes = loadedCreatorNotes.Concat(new[]
    {
        new CreatorNote
        {
            Scope = "R/S命令",
            TargetKey = $"{structureProbe.FileName}#Scene={mapReferenceRow.SceneIndex}#Section={mapReferenceRow.SectionIndex}#Command={mapReferenceRow.CommandIndex}#Offset={mapReferenceRow.OffsetHex}",
            Title = "Smoke 命令引用核对备注",
            Content = "用于验证 R/S eex 命令引用核对清单能统计命令备注，不写入游戏文件。",
            Tags = "Smoke,R/S命令"
        }
    }).ToList();
    var checklistReport = checklistService.BuildReport(project, tables, structureProbe, structureProbe.Rows, sv004Link, scenarioTexts, checklistNotes);
    if (!checklistReport.Contains("R/S eex 命令引用核对清单", StringComparison.Ordinal) ||
        !checklistReport.Contains("优先核对清单", StringComparison.Ordinal) ||
        !checklistReport.Contains("命令明细", StringComparison.Ordinal) ||
        !checklistReport.Contains("推荐工作流", StringComparison.Ordinal) ||
        !checklistReport.Contains("安全边界", StringComparison.Ordinal) ||
        !checklistReport.Contains("人物", StringComparison.Ordinal) ||
        !checklistReport.Contains("文本", StringComparison.Ordinal) ||
        !checklistReport.Contains("地图", StringComparison.Ordinal) ||
        !checklistReport.Contains("创作者备注", StringComparison.Ordinal))
    {
        throw new InvalidOperationException("R/S eex 命令引用核对清单缺少中文标题、优先项、引用类型、备注或安全边界。");
    }
    var checklistPath = checklistService.WriteReport(project, tables, structureProbe, structureProbe.Rows, sv004Link, scenarioTexts, checklistNotes);
    if (!File.Exists(checklistPath) ||
        new FileInfo(checklistPath).Length == 0 ||
        !File.ReadAllText(checklistPath).Contains("只写入 `CCZModStudio_Reports`", StringComparison.Ordinal))
    {
        throw new InvalidOperationException("R/S eex 命令引用核对清单未成功写入。");
    }
    Console.WriteLine($"SCENARIO_COMMAND_REFERENCE_CHECKLIST file={Path.GetFileName(checklistPath)} chars={checklistReport.Length}");
}
Console.WriteLine($"SCENARIO_MAP_LINK_ROW {sv004Link.ScenarioFileName}->{sv004Link.MapId} title={sv004Link.ScenarioTitle} status={sv004Link.Status} terrain={sv004Link.DominantTerrain}");
var sv004JumpScenarioExists = File.Exists(sv004Link.ScenarioPath);
var sv004JumpMapExists = File.Exists(sv004Link.MapImagePath);
var sv004JumpHex = hexzmapProbe.Blocks.FirstOrDefault(x => x.MapId.Equals(sv004Link.MapId, StringComparison.OrdinalIgnoreCase));
var sv004JumpMapResource = gameResources.FirstOrDefault(x => x.Category == "地图图片" && x.Name.Equals(sv004Link.MapImageName, StringComparison.OrdinalIgnoreCase));
if (!sv004JumpScenarioExists ||
    !sv004JumpMapExists ||
    sv004JumpHex == null ||
    sv004JumpMapResource == null ||
    !sv004JumpHex.OffsetHex.Equals(sv004Link.HexzmapOffsetHex, StringComparison.OrdinalIgnoreCase))
{
    throw new InvalidOperationException("关卡地图联动跳转目标证据不完整：SV、地图浏览或 Hexzmap 块无法互相定位。");
}
Console.WriteLine($"SCENARIO_MAP_JUMP_TARGET sv={Path.GetFileName(sv004Link.ScenarioPath)} map={sv004Link.MapImageName} hex={sv004JumpHex.MapId}@{sv004JumpHex.OffsetHex}");
var sv004JumpCells = hexzmapProbeReader.GetBlockCells(hexzmapProbe, sv004JumpHex);
var scenarioMapPreviewExportDir = Path.Combine(project.WorkspaceRoot, "CCZModStudio_Exports");
Directory.CreateDirectory(scenarioMapPreviewExportDir);
var scenarioMapPreviewPng = Path.Combine(scenarioMapPreviewExportDir, "Smoke_关卡地图联动预览.png");
using (var preview = hexzmapRenderService.RenderOverlay(
    sv004JumpCells,
    sv004JumpHex.Width,
    sv004JumpHex.Height,
    sv004Link.MapImagePath,
    45))
{
    preview.Save(scenarioMapPreviewPng, System.Drawing.Imaging.ImageFormat.Png);
}
if (!File.Exists(scenarioMapPreviewPng) || new FileInfo(scenarioMapPreviewPng).Length == 0)
{
    throw new InvalidOperationException("关卡地图联动预览 PNG 未成功导出。");
}
Console.WriteLine($"SCENARIO_MAP_PREVIEW_PNG file={Path.GetFileName(scenarioMapPreviewPng)} map={sv004Link.MapImageName} hex={sv004JumpHex.MapId}");

var scenarioMapReportService = new ScenarioMapLinkReportService();
var scenarioMapReportNoteCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
{
    [ScenarioMapLinkReportService.BuildCreatorNoteTargetKey(sv004Link)] = 1
};
var scenarioMapVisibleRows = scenarioMapLinks
    .Where(x => x.Status.Contains("缺", StringComparison.Ordinal) || x.ScenarioFileName.Equals("SV004.E5S", StringComparison.OrdinalIgnoreCase))
    .Take(30)
    .ToList();
var scenarioMapReportText = scenarioMapReportService.BuildReport(project, scenarioMapLinks, scenarioMapVisibleRows, scenarioMapReportNoteCounts);
if (!scenarioMapReportText.Contains("# CCZModStudio 关卡地图联动检查报告", StringComparison.Ordinal) ||
    !scenarioMapReportText.Contains("## 3. 优先处理：不完整联动", StringComparison.Ordinal) ||
    !scenarioMapReportText.Contains("## 4. 完整候选示例", StringComparison.Ordinal) ||
    !scenarioMapReportText.Contains("创作者备注", StringComparison.Ordinal) ||
    !scenarioMapReportText.Contains("SV004.E5S", StringComparison.Ordinal) ||
    !scenarioMapReportText.Contains("只写入 `CCZModStudio_Reports`", StringComparison.Ordinal))
{
    throw new InvalidOperationException("关卡地图联动检查报告缺少中文标题、优先项、完整示例、备注或安全边界。");
}
var scenarioMapReportPath = scenarioMapReportService.WriteReport(project, scenarioMapLinks, scenarioMapVisibleRows, scenarioMapReportNoteCounts);
if (!File.Exists(scenarioMapReportPath) ||
    new FileInfo(scenarioMapReportPath).Length == 0 ||
    !File.ReadAllText(scenarioMapReportPath).Contains("当前显示明细", StringComparison.Ordinal))
{
    throw new InvalidOperationException("关卡地图联动检查报告未成功写入。");
}
Console.WriteLine($"SCENARIO_MAP_REPORT file={Path.GetFileName(scenarioMapReportPath)} visible={scenarioMapVisibleRows.Count} incomplete={scenarioMapLinks.Count(x => x.Status.Contains("缺", StringComparison.Ordinal))}");

var commandTemplateService = new ScenarioCommandParameterTemplateService();
var commandTemplateCatalog = commandTemplateService.BuildTemplateCatalog(sceneDoc);
if (commandTemplateService.TemplateCount < 70 ||
    !commandTemplateCatalog.Contains("R/S eex 命令参数模板目录", StringComparison.Ordinal) ||
    !commandTemplateCatalog.Contains("命令字典覆盖表", StringComparison.Ordinal) ||
    !commandTemplateCatalog.Contains("武将能力设定", StringComparison.Ordinal) ||
    !commandTemplateCatalog.Contains("AI 方针", StringComparison.Ordinal) ||
    !commandTemplateCatalog.Contains("安全边界", StringComparison.Ordinal))
{
    throw new InvalidOperationException("R/S eex 命令参数模板目录缺少覆盖表、扩展模板或安全边界。");
}
var commandTemplateCatalogPath = commandTemplateService.WriteTemplateCatalog(project, sceneDoc);
if (!File.Exists(commandTemplateCatalogPath) ||
    !File.ReadAllText(commandTemplateCatalogPath).Contains("只读研究事项", StringComparison.Ordinal))
{
    throw new InvalidOperationException("R/S eex 命令参数模板目录未成功写入或缺少推荐工作流。");
}
Console.WriteLine($"SCENARIO_COMMAND_TEMPLATE_CATALOG count={commandTemplateService.TemplateCount} file={Path.GetFileName(commandTemplateCatalogPath)} chars={commandTemplateCatalog.Length}");

var commandTemplateItems = commandTemplateService.BuildCatalogItems(sceneDoc);
var peopleCommandTemplateItems = commandTemplateItems.Where(item => item.Category == "人物/战场单位").ToList();
var missingCommandTemplateItems = commandTemplateItems.Where(item => item.Status == "待补充").ToList();
var firstCoveredTemplateDetail = commandTemplateService.BuildCatalogItemDetail(commandTemplateItems.First(item => item.Status == "已覆盖"));
if (commandTemplateItems.Count < 100 ||
    peopleCommandTemplateItems.Count == 0 ||
    !commandTemplateItems.Any(item => item.TemplateName.Contains("武将能力设定", StringComparison.Ordinal)) ||
    !commandTemplateItems.Any(item => item.CreatorTip.Contains("实机", StringComparison.Ordinal)) ||
    !firstCoveredTemplateDetail.Contains("SV 命令模板", StringComparison.Ordinal) ||
    !firstCoveredTemplateDetail.Contains("创作者提示", StringComparison.Ordinal) ||
    !firstCoveredTemplateDetail.Contains("安全说明", StringComparison.Ordinal))
{
    throw new InvalidOperationException("SV 命令模板可视化目录行/详情缺少预期中文内容。");
}
Console.WriteLine($"SCENARIO_COMMAND_TEMPLATE_ITEMS count={commandTemplateItems.Count} people={peopleCommandTemplateItems.Count} missing={missingCommandTemplateItems.Count} detailChars={firstCoveredTemplateDetail.Length}");

if (structureProbe != null)
{
    var internalInfoTemplateItem = commandTemplateItems.First(item => item.Id == 0x02);
    var linkedStructureRows = structureProbe.Rows.Where(row =>
    {
        var normalized = row.CommandIdHex.Trim();
        if (normalized.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) normalized = normalized[2..];
        return row.NodeType == "Command候选" &&
               int.TryParse(normalized, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var commandId) &&
               commandId == internalInfoTemplateItem.Id;
    }).ToList();
    if (linkedStructureRows.Count == 0 ||
        !linkedStructureRows.Any(row => row.CommandName.Contains("内部信息", StringComparison.Ordinal)) ||
        !firstCoveredTemplateDetail.Contains("槽位明细", StringComparison.Ordinal))
    {
        throw new InvalidOperationException("SV 命令模板目录未能与结构草图命令 ID 建立可反查关系。");
    }
    Console.WriteLine($"SCENARIO_COMMAND_TEMPLATE_STRUCTURE_LINK id={internalInfoTemplateItem.IdHex} matches={linkedStructureRows.Count} file={structureProbe.FileName}");
}

var projectEvidenceService = new ProjectEvidenceService();
var projectEvidence = projectEvidenceService.Scan(project);
var projectEvidenceSummary = projectEvidenceService.BuildSummary(project, projectEvidence);
if (!projectEvidence.Any(x => x.Kind == "R/S命令引用核对清单") ||
    !projectEvidence.Any(x => x.Kind == "R/S命令参数模板目录") ||
    !projectEvidence.Any(x => x.Kind == "关卡地图联动报告") ||
    !projectEvidence.Any(x => x.Kind == "可视化预览PNG") ||
    !projectEvidenceSummary.Contains("最近报告/发布证据", StringComparison.Ordinal) ||
    !projectEvidenceSummary.Contains("安全边界", StringComparison.Ordinal) ||
    projectEvidence.Any(x => string.IsNullOrWhiteSpace(x.Annotation) || string.IsNullOrWhiteSpace(x.SuggestedUse)))
{
    throw new InvalidOperationException("项目证据服务未能汇总 R/S 清单、地图报告、预览 PNG 或中文说明。");
}
Console.WriteLine($"PROJECT_EVIDENCE count={projectEvidence.Count} checklist={projectEvidence.Count(x => x.Kind == "R/S命令引用核对清单")} templates={projectEvidence.Count(x => x.Kind == "R/S命令参数模板目录")} mapReports={projectEvidence.Count(x => x.Kind == "关卡地图联动报告")} previews={projectEvidence.Count(x => x.Kind == "可视化预览PNG")} summaryChars={projectEvidenceSummary.Length}");

var resourceChangeReviewHintService = new ResourceChangeReviewHintService();
var scenarioMapReviewHint = resourceChangeReviewHintService.BuildScenarioMapReviewHint(sv004JumpMapResource, scenarioMapLinks);
var scenarioMapReviewAffected = resourceChangeReviewHintService.FindAffectedScenarioMapLinks(sv004JumpMapResource, scenarioMapLinks);
if (!resourceChangeReviewHintService.MayAffectScenarioMap(sv004JumpMapResource) ||
    scenarioMapReviewAffected.Count == 0 ||
    !scenarioMapReviewHint.Contains("关卡地图联动复查", StringComparison.Ordinal) ||
    !scenarioMapReviewHint.Contains("检查报告", StringComparison.Ordinal) ||
    !scenarioMapReviewHint.Contains("预览PNG", StringComparison.Ordinal))
{
    throw new InvalidOperationException("资源替换/还原后的关卡地图联动复查提示未生成完整中文建议。");
}
Console.WriteLine($"RESOURCE_CHANGE_REVIEW_HINT affected={scenarioMapReviewAffected.Count} chars={scenarioMapReviewHint.Length} target={sv004JumpMapResource.Name}");

var probeWorkflowDashboard = workflowGuideService.BuildDashboard(
    project,
    tables.Count,
    auditItems,
    resourceDiagnostics,
    Array.Empty<ProjectDiffItem>(),
    Array.Empty<BackupHistoryItem>(),
    loadedCreatorNotes,
    scenarioMapLinks,
    structureProbe,
    eexArchives,
    lsResources,
    hexzmapProbe);
var scenarioMapDashboard = probeWorkflowDashboard.Single(x => x.Area == "关卡地图联动");
var svRiskDashboard = probeWorkflowDashboard.Single(x => x.Area == "SV高风险命令");
var probeDashboard = probeWorkflowDashboard.Single(x => x.Area == "EEX/Ls/Hexzmap探针");
var probeWorkflowActionPlan = workflowGuideService.BuildActionPlan(project, probeWorkflowDashboard, maxItems: 6);
var probeWorkflowActionItems = workflowGuideService.BuildActionItems(project, probeWorkflowDashboard, loadedCreatorNotes, maxItems: 10);
if (!scenarioMapDashboard.Value.Contains("完整", StringComparison.Ordinal) ||
    !scenarioMapDashboard.Evidence.Contains("SV", StringComparison.Ordinal) ||
    structureProbe != null && !svRiskDashboard.Value.Contains("高风险", StringComparison.Ordinal) ||
    !probeDashboard.Value.Contains("EEX", StringComparison.Ordinal) ||
    !probeDashboard.Value.Contains("地形块", StringComparison.Ordinal) ||
    !probeWorkflowActionPlan.Contains("优先行动清单", StringComparison.Ordinal) ||
    !probeWorkflowActionPlan.Contains("创作者备注", StringComparison.Ordinal) ||
    !probeWorkflowActionPlan.Contains("测试副本", StringComparison.Ordinal) ||
    probeWorkflowActionItems.Count == 0 ||
    !probeWorkflowActionItems.Any(x => x.TargetArea == "关卡地图联动") ||
    !probeWorkflowActionItems.Any(x => x.TargetArea == "SV高风险命令") ||
    !probeWorkflowActionItems.Any(x => x.TargetArea == "EEX/Ls/Hexzmap探针") ||
    probeWorkflowActionItems.Any(x => string.IsNullOrWhiteSpace(x.Action) || string.IsNullOrWhiteSpace(x.SafetyNote) || string.IsNullOrWhiteSpace(x.NoteHint)))
{
    throw new InvalidOperationException("制作向导工作台新增探针卡片未正确汇总关卡地图、SV 高风险或 EEX/Ls/Hexzmap 状态。");
}
Console.WriteLine($"WORKFLOW_PROBE_DASHBOARD map={scenarioMapDashboard.Value} sv={svRiskDashboard.Value} probe={probeDashboard.Value} actions={probeWorkflowActionItems.Count} actionChars={probeWorkflowActionPlan.Length}");
if (structureProbe != null)
{
    var nodeDetailRow = structureProbe.Rows.FirstOrDefault(x =>
                            x.NodeType == "Command候选" &&
                            x.ReferenceHint.Contains("文本线索", StringComparison.Ordinal))
                        ?? structureProbe.Rows.First(x => x.NodeType == "Command候选");
    var nodeDetail = new ScenarioStructureNodeDetailService().BuildDetail(nodeDetailRow, structureProbe.FileName, scenarioTexts, sv004Link, project, tables);
    if (!nodeDetail.Contains("剧本节点详情", StringComparison.Ordinal) ||
        !nodeDetail.Contains("同文件", StringComparison.Ordinal) ||
        !nodeDetail.Contains("关卡地图联动", StringComparison.Ordinal) ||
        !nodeDetail.Contains("参数分组解释", StringComparison.Ordinal) ||
        !nodeDetail.Contains("命令参数模板", StringComparison.Ordinal) ||
        !nodeDetail.Contains("坐标候选", StringComparison.Ordinal) ||
        !nodeDetail.Contains("创作提示", StringComparison.Ordinal))
    {
        throw new InvalidOperationException("SV 事件树节点详情缺少中文文本/地图联动解释。");
    }
    Console.WriteLine($"SCENARIO_NODE_DETAIL row={nodeDetailRow.Index} cmd={nodeDetailRow.CommandIdHex}/{nodeDetailRow.CommandName} chars={nodeDetail.Length}");

    var variableTemplateDetail = commandTemplateService.BuildTemplateDetail(new ScenarioStructureRow
    {
        NodeType = "Command候选",
        CommandIdHex = "0x05",
        CommandName = "变量测试",
        ParameterPreview = "0007 0000 0001 0020"
    }, sv004Link, project, tables);
    var coordinateTemplateDetail = commandTemplateService.BuildTemplateDetail(new ScenarioStructureRow
    {
        NodeType = "Command候选",
        CommandIdHex = "0x25",
        CommandName = "武将进入指定地点测试",
        ParameterPreview = "0001 000A 000B 0010"
    }, sv004Link, project, tables);
    if (!variableTemplateDetail.Contains("命令参数模板", StringComparison.Ordinal) ||
        !variableTemplateDetail.Contains("变量编号", StringComparison.Ordinal) ||
        !variableTemplateDetail.Contains("比较方式", StringComparison.Ordinal) ||
        !coordinateTemplateDetail.Contains("目标X坐标", StringComparison.Ordinal) ||
        !coordinateTemplateDetail.Contains("坐标对候选", StringComparison.Ordinal) ||
        !coordinateTemplateDetail.Contains("使用建议", StringComparison.Ordinal))
    {
        throw new InvalidOperationException("SV 常见命令参数模板未生成变量/坐标槽位中文解释。");
    }
    Console.WriteLine($"SCENARIO_COMMAND_TEMPLATE count={commandTemplateService.TemplateCount} variableChars={variableTemplateDetail.Length} coordinateChars={coordinateTemplateDetail.Length} catalog={Path.GetFileName(commandTemplateCatalogPath)} chars={commandTemplateCatalog.Length}");
}
var scenarioMapDiagnostics = new ResourceReferenceDiagnosticService().AnalyzeScenarioMapLinks(project, scenarioMapLinks);
Console.WriteLine($"SCENARIO_MAP_DIAGNOSTIC items={scenarioMapDiagnostics.Count} warnings={scenarioMapDiagnostics.Count(x => x.Severity == "Warn")}");
if (scenarioMapDiagnostics.Count == 0 ||
    !scenarioMapDiagnostics.Any(x => x.Category == "关卡地图联动" && x.Rule == "联动概览"))
{
    throw new InvalidOperationException("关卡地图联动资源诊断未生成概览。");
}
if (!scenarioMapDiagnostics.Any(x => x.Rule == "关卡地图资源不完整") ||
    !scenarioMapDiagnostics.Any(x => x.Rule == "完整候选示例"))
{
    throw new InvalidOperationException("关卡地图联动资源诊断缺少不完整项或完整示例。");
}
if (scenarioMapDiagnostics.Any(x => string.IsNullOrWhiteSpace(x.Suggestion)))
{
    throw new InvalidOperationException("关卡地图联动资源诊断缺少中文建议。");
}
foreach (var diagnostic in scenarioMapDiagnostics.Take(3))
{
    Console.WriteLine($"SCENARIO_MAP_DIAGNOSTIC_ROW {diagnostic.Severity} {diagnostic.Category}/{diagnostic.Rule} status={diagnostic.Status}");
}

var diagnosticNavigationService = new ResourceDiagnosticNavigationService();
var completeMapDiagnostic = scenarioMapDiagnostics.First(x => x.Rule == "完整候选示例");
var completeMapNavigation = diagnosticNavigationService.Resolve(completeMapDiagnostic, scenarioMapLinks, gameResources);
if (!completeMapNavigation.CanOpenScenarioMapLink ||
    !completeMapNavigation.CanJumpScenario ||
    !completeMapNavigation.CanJumpHexzmap ||
    !completeMapNavigation.CanJumpMapViewer ||
    string.IsNullOrWhiteSpace(completeMapNavigation.MapId) ||
    string.IsNullOrWhiteSpace(completeMapNavigation.ScenarioFileName))
{
    throw new InvalidOperationException("完整候选资源诊断未能解析到联动页、SV、Hexzmap 和地图浏览跳转目标。");
}

var incompleteMapDiagnostic = scenarioMapDiagnostics.First(x => x.Rule == "关卡地图资源不完整");
var incompleteMapNavigation = diagnosticNavigationService.Resolve(incompleteMapDiagnostic, scenarioMapLinks, gameResources);
if (!incompleteMapNavigation.CanOpenScenarioMapLink ||
    !incompleteMapNavigation.CanJumpScenario ||
    string.IsNullOrWhiteSpace(incompleteMapNavigation.MapId))
{
    throw new InvalidOperationException("不完整关卡地图诊断未能解析到联动页或 SV 跳转目标。");
}

var mapResourceDiagnostic = resourceDiagnostics.FirstOrDefault(x => x.Category == "地图图片" && !string.IsNullOrWhiteSpace(x.Path));
if (mapResourceDiagnostic != null)
{
    var mapResourceNavigation = diagnosticNavigationService.Resolve(mapResourceDiagnostic, scenarioMapLinks, gameResources);
    if (!mapResourceNavigation.CanJumpMapViewer || string.IsNullOrWhiteSpace(mapResourceNavigation.ResourceName))
    {
        throw new InvalidOperationException("地图图片资源诊断未能解析到地图浏览跳转目标。");
    }
}

var firstNamedImageRow = imageAssignments.Rows.Cast<DataRow>()
    .First(row => !string.IsNullOrWhiteSpace(Convert.ToString(row["名称"], CultureInfo.InvariantCulture)));
var firstNamedImageRowId = Convert.ToInt32(firstNamedImageRow["ID"], CultureInfo.InvariantCulture);
var firstNamedRId = Convert.ToInt32(firstNamedImageRow["R形象编号"], CultureInfo.InvariantCulture);
var syntheticImageDiagnostic = new ResourceDiagnosticItem
{
    Severity = "Warn",
    Category = "表格引用/R形象",
    Rule = "人物形象缺失",
    Id = firstNamedImageRowId.ToString(CultureInfo.InvariantCulture),
    Name = Convert.ToString(firstNamedImageRow["名称"], CultureInfo.InvariantCulture) ?? string.Empty,
    Status = "未定位：Pmapobj.e5",
    Detail = $"6.5-0-4 R形象 第 {firstNamedImageRowId} 行引用 R形象编号={firstNamedRId}，但没有定位到 Pmapobj.e5。",
    Suggestion = "请先确认 Pmapobj.e5 是否在项目根目录或 E5 目录；再对照 6.5 形象指定器与实机确认该编号是否应调整。",
    Path = ImageAssignmentService.GetImageResourcePath(project, "R", firstNamedRId)
};
var imageNavigation = diagnosticNavigationService.Resolve(syntheticImageDiagnostic, scenarioMapLinks, gameResources);
if (!imageNavigation.CanJumpImageAssignment ||
    !imageNavigation.CanJumpDataTable ||
    imageNavigation.ImageAssignmentPrefix != "R" ||
    imageNavigation.ImageAssignmentRowId != firstNamedImageRowId ||
    imageNavigation.ImageResourceId != firstNamedRId ||
    imageNavigation.TableName != "6.5-0-4 R形象" ||
    imageNavigation.TableRowId != firstNamedImageRowId.ToString(CultureInfo.InvariantCulture) ||
    imageNavigation.TableFieldName != "R形象编号" ||
    !imageNavigation.Summary.Contains("人物R形象", StringComparison.Ordinal))
{
    throw new InvalidOperationException("资源诊断未能解析到人物 R/S 形象联动或数据表单元格跳转目标。");
}

Console.WriteLine($"RESOURCE_DIAGNOSTIC_NAV complete={completeMapNavigation.ScenarioFileName}->{completeMapNavigation.MapId} map={completeMapNavigation.MapImageName} incomplete={incompleteMapNavigation.ScenarioFileName}->{incompleteMapNavigation.MapId}");
Console.WriteLine($"RESOURCE_DIAGNOSTIC_RS_NAV prefix={imageNavigation.ImageAssignmentPrefix} row={imageNavigation.ImageAssignmentRowId} resource={imageNavigation.ImageResourceId:00}");
Console.WriteLine($"RESOURCE_DIAGNOSTIC_TABLE_NAV table={imageNavigation.TableName} row={imageNavigation.TableRowId} field={imageNavigation.TableFieldName}");

if (enableWriteTest)
{
    var smokeRoot = Path.Combine(project.WorkspaceRoot, "CCZModStudio_TestCopies", "SmokeTest_" + DateTime.Now.ToString("yyyyMMdd_HHmmss"));
    Directory.CreateDirectory(smokeRoot);
    foreach (var file in new[] { "Ekd5.exe", "Data.e5", "Imsg.e5", "Star.e5", "Hexzmap.e5" })
    {
        var source = Path.Combine(project.GameRoot, file);
        if (File.Exists(source)) File.Copy(source, Path.Combine(smokeRoot, file));
    }
    var smokeSvRoot = Path.Combine(smokeRoot, "SV");
    Directory.CreateDirectory(smokeSvRoot);
    var scenarioSource = Path.Combine(project.GameRoot, "SV", "SV004.E5S");
    if (File.Exists(scenarioSource))
    {
        File.Copy(scenarioSource, Path.Combine(smokeSvRoot, "SV004.E5S"));
    }
    else
    {
        throw new FileNotFoundException("写入烟雾测试需要 SV004.E5S。", scenarioSource);
    }
    var smokeMapRoot = Path.Combine(smokeRoot, "Map");
    Directory.CreateDirectory(smokeMapRoot);
    var mapTargetSource = Path.Combine(project.GameRoot, "Map", "M000.jpg");
    var mapReplacementSource = Directory.GetFiles(Path.Combine(project.GameRoot, "Map"), "M001.*")
        .FirstOrDefault(path => Path.GetExtension(path).Equals(".jpg", StringComparison.OrdinalIgnoreCase)
                             || Path.GetExtension(path).Equals(".jpeg", StringComparison.OrdinalIgnoreCase))
        ?? throw new FileNotFoundException("写入烟雾测试需要 M001 地图图片。", Path.Combine(project.GameRoot, "Map", "M001.*"));
    if (File.Exists(mapTargetSource))
    {
        File.Copy(mapTargetSource, Path.Combine(smokeMapRoot, "M000.jpg"));
    }
    else
    {
        throw new FileNotFoundException("写入烟雾测试需要 M000.jpg 地图图片。", mapTargetSource);
    }

    var unitMovSource = CharacterImageResourceService.ResolveGameFile(project, "Unit_mov.e5");
    if (!File.Exists(unitMovSource))
    {
        throw new FileNotFoundException("写入烟测需要 Unit_mov.e5。", unitMovSource);
    }
    File.Copy(unitMovSource, Path.Combine(smokeRoot, "Unit_mov.e5"), overwrite: false);

    var wavSource = Directory.Exists(Path.Combine(project.GameRoot, "WAV"))
        ? Directory.GetFiles(Path.Combine(project.GameRoot, "WAV"), "*.wav").OrderBy(x => x, StringComparer.CurrentCultureIgnoreCase).FirstOrDefault()
        : null;
    string? smokeWavTarget = null;
    if (wavSource != null)
    {
        var smokeWavRoot = Path.Combine(smokeRoot, "WAV");
        Directory.CreateDirectory(smokeWavRoot);
        smokeWavTarget = Path.Combine(smokeWavRoot, Path.GetFileName(wavSource));
        File.Copy(wavSource, smokeWavTarget, overwrite: true);
    }

    var mp3Source = Directory.Exists(Path.Combine(project.GameRoot, "SoundTrk"))
        ? Directory.GetFiles(Path.Combine(project.GameRoot, "SoundTrk"), "*.mp3").OrderBy(x => x, StringComparer.CurrentCultureIgnoreCase).FirstOrDefault()
        : null;
    string? smokeMp3Target = null;
    if (mp3Source != null)
    {
        var smokeMp3Root = Path.Combine(smokeRoot, "SoundTrk");
        Directory.CreateDirectory(smokeMp3Root);
        smokeMp3Target = Path.Combine(smokeMp3Root, Path.GetFileName(mp3Source));
        File.Copy(mp3Source, smokeMp3Target, overwrite: true);
    }
    File.WriteAllText(Path.Combine(smokeRoot, "_CCZModStudio_TestCopy.txt"),
        $"CreatedAt={DateTime.Now:yyyy-MM-dd HH:mm:ss}\r\nSource={project.GameRoot}\r\n");

    var testProject = detector.CreateProjectFromGameRoot(smokeRoot);
    var table = tables.Single(t => t.TableName == "6.5-0 人物");
    var read = reader.Read(testProject, table, tables);
    var col = read.Data.Columns.Contains("级别") ? "级别" : throw new InvalidOperationException("找不到 级别 列");
    var original = Convert.ToInt32(read.Data.Rows[0][col]);
    var changed = original == 1 ? 2 : 1;
    read.Data.Rows[0][col] = changed;

    var writer = new HexTableWriter();
    var save = writer.SaveToTestCopy(testProject, table, read.Data);
    Console.WriteLine($"WRITE {table.TableName} changedBytes={save.ChangedBytes} backup={save.BackupPath}");
    if (string.IsNullOrWhiteSpace(save.ReportJsonPath) ||
        !File.Exists(save.ReportJsonPath) ||
        !File.ReadAllText(save.ReportJsonPath).Contains("\"OperationKind\": \"数据表保存\"", StringComparison.Ordinal))
    {
        throw new InvalidOperationException("数据表保存未生成结构化 JSON 写入报告。");
    }
    var reportFormatter = new WriteOperationReportFormatter();
    var tableReportText = reportFormatter.FormatForCreator(save.ReportJsonPath);
    if (!tableReportText.Contains("具体改动", StringComparison.Ordinal) ||
        !tableReportText.Contains("级别", StringComparison.Ordinal))
    {
        throw new InvalidOperationException("数据表结构化报告中文详情缺少具体改动或字段名。");
    }
    Console.WriteLine($"STRUCTURED_REPORT table={Path.GetFileName(save.ReportJsonPath)}");

    var reread = reader.Read(testProject, table, tables);
    var actual = Convert.ToInt32(reread.Data.Rows[0][col]);
    if (actual != changed)
    {
        throw new InvalidOperationException($"写入验证失败：expected={changed}, actual={actual}");
    }
    Console.WriteLine($"VERIFY_WRITE OK {col}: {original} -> {actual}");

    var jobSeriesTable = tables.Single(t => t.TableName == "6.5-3 兵种系");
    var jobTerrainPowerTable = tables.Single(t => t.TableName == "6.5-3-1 地形发挥");
    var jobMoveCostTable = tables.Single(t => t.TableName == "6.5-3-2 移动消耗");
    var jobSeriesRead = reader.Read(testProject, jobSeriesTable, tables);
    var jobTerrainPowerRead = reader.Read(testProject, jobTerrainPowerTable, tables);
    var jobMoveCostRead = reader.Read(testProject, jobMoveCostTable, tables);
    var jobSeriesOriginal = Convert.ToString(jobSeriesRead.Data.Rows[0]["名称"], CultureInfo.InvariantCulture) ?? string.Empty;
    var jobSeriesChanged = jobSeriesOriginal == "烟测兵" ? "烟测骑" : "烟测兵";
    jobSeriesRead.Data.Rows[0]["名称"] = jobSeriesChanged;
    var terrainField = jobTerrainPowerRead.Data.Columns.Contains("平原") ? "平原" : jobTerrainPowerRead.Data.Columns.Cast<DataColumn>().First(c => c.ColumnName is not "ID" and not "名称").ColumnName;
    var powerOriginal = Convert.ToInt32(jobTerrainPowerRead.Data.Rows[0][terrainField], CultureInfo.InvariantCulture);
    var powerChanged = powerOriginal == 100 ? 90 : 100;
    jobTerrainPowerRead.Data.Rows[0][terrainField] = powerChanged;
    var moveOriginal = Convert.ToInt32(jobMoveCostRead.Data.Rows[0][terrainField], CultureInfo.InvariantCulture);
    var moveChanged = moveOriginal == 1 ? 2 : 1;
    jobMoveCostRead.Data.Rows[0][terrainField] = moveChanged;
    var jobSeriesSave = writer.SaveToTestCopy(testProject, jobSeriesTable, jobSeriesRead.Data);
    var jobTerrainPowerSave = writer.SaveToTestCopy(testProject, jobTerrainPowerTable, jobTerrainPowerRead.Data);
    var jobMoveCostSave = writer.SaveToTestCopy(testProject, jobMoveCostTable, jobMoveCostRead.Data);
    var jobSeriesVerify = reader.Read(testProject, jobSeriesTable, tables);
    var jobTerrainPowerVerify = reader.Read(testProject, jobTerrainPowerTable, tables);
    var jobMoveCostVerify = reader.Read(testProject, jobMoveCostTable, tables);
    var jobSeriesActual = Convert.ToString(jobSeriesVerify.Data.Rows[0]["名称"], CultureInfo.InvariantCulture);
    var powerActual = Convert.ToInt32(jobTerrainPowerVerify.Data.Rows[0][terrainField], CultureInfo.InvariantCulture);
    var moveActual = Convert.ToInt32(jobMoveCostVerify.Data.Rows[0][terrainField], CultureInfo.InvariantCulture);
    if (jobSeriesActual != jobSeriesChanged || powerActual != powerChanged || moveActual != moveChanged)
    {
        throw new InvalidOperationException($"兵种系/地形写入验证失败：series={jobSeriesActual}, power={powerActual}, move={moveActual}");
    }
    Console.WriteLine($"VERIFY_JOB_TERRAIN OK name={jobSeriesOriginal}->{jobSeriesActual} {terrainField}发挥={powerOriginal}->{powerActual} {terrainField}消耗={moveOriginal}->{moveActual} changedBytes={jobSeriesSave.ChangedBytes + jobTerrainPowerSave.ChangedBytes + jobMoveCostSave.ChangedBytes}");

    var jobEffectDescriptionTable = tables.Single(t => t.TableName == "6.5-7-1 兵种特效说明");
    var jobEffectAssignmentTable = tables.Single(t => t.TableName == "6.5-7-2 兵种特效分配");
    var jobEffectDescriptionRead = reader.Read(testProject, jobEffectDescriptionTable, tables);
    var jobEffectAssignmentRead = reader.Read(testProject, jobEffectAssignmentTable, tables);
    var effectDescriptionChanged = "CCZ兵种特效烟测";
    var effectPersonOriginal = Convert.ToInt32(jobEffectAssignmentRead.Data.Rows[0]["1号武将"], CultureInfo.InvariantCulture);
    var effectPersonChanged = effectPersonOriginal == 0 ? 1 : Math.Max(0, effectPersonOriginal - 1);
    var effectValueOriginal = Convert.ToInt32(jobEffectAssignmentRead.Data.Rows[0]["特效值"], CultureInfo.InvariantCulture);
    var effectValueChanged = effectValueOriginal == 1 ? 2 : 1;
    jobEffectDescriptionRead.Data.Rows[0]["介绍"] = effectDescriptionChanged;
    jobEffectAssignmentRead.Data.Rows[0]["1号武将"] = effectPersonChanged;
    jobEffectAssignmentRead.Data.Rows[0]["特效值"] = effectValueChanged;
    var jobEffectDescriptionSave = writer.SaveToTestCopy(testProject, jobEffectDescriptionTable, jobEffectDescriptionRead.Data);
    var jobEffectAssignmentSave = writer.SaveToTestCopy(testProject, jobEffectAssignmentTable, jobEffectAssignmentRead.Data);
    var jobEffectDescriptionVerify = reader.Read(testProject, jobEffectDescriptionTable, tables);
    var jobEffectAssignmentVerify = reader.Read(testProject, jobEffectAssignmentTable, tables);
    var effectDescriptionActual = Convert.ToString(jobEffectDescriptionVerify.Data.Rows[0]["介绍"], CultureInfo.InvariantCulture);
    var effectPersonActual = Convert.ToInt32(jobEffectAssignmentVerify.Data.Rows[0]["1号武将"], CultureInfo.InvariantCulture);
    var effectValueActual = Convert.ToInt32(jobEffectAssignmentVerify.Data.Rows[0]["特效值"], CultureInfo.InvariantCulture);
    if (effectDescriptionActual != effectDescriptionChanged || effectPersonActual != effectPersonChanged || effectValueActual != effectValueChanged)
    {
        throw new InvalidOperationException($"兵种特效写入验证失败：desc={effectDescriptionActual}, person={effectPersonActual}, value={effectValueActual}");
    }
    Console.WriteLine($"VERIFY_JOB_EFFECT OK desc={effectDescriptionChanged} 1号武将={effectPersonOriginal}->{effectPersonActual} 特效值={effectValueOriginal}->{effectValueActual} changedBytes={jobEffectDescriptionSave.ChangedBytes + jobEffectAssignmentSave.ChangedBytes}");

    var testHexzmapProbe = hexzmapProbeReader.Read(testProject, terrainNameLookup);
    var testHexzmapBlock = testHexzmapProbe.Blocks.First();
    var testHexzmapCells = hexzmapProbeReader.GetBlockCells(testHexzmapProbe, testHexzmapBlock);
    var hexOriginal = testHexzmapCells[0];
    var hexChanged = hexOriginal == 0 ? (byte)1 : (byte)0;
    var hexSecondOriginal = testHexzmapCells[1];
    var hexSecondChanged = hexSecondOriginal == 2 ? (byte)3 : (byte)2;
    testHexzmapCells[0] = hexChanged;
    testHexzmapCells[1] = hexSecondChanged;
    var hexzmapSave = new HexzmapEditorService().SaveBlock(testProject, testHexzmapProbe, testHexzmapBlock, testHexzmapCells, terrainNameLookup);
    var testHexzmapVerify = hexzmapProbeReader.Read(testProject, terrainNameLookup);
    var testHexzmapVerifyCells = hexzmapProbeReader.GetBlockCells(testHexzmapVerify, testHexzmapVerify.Blocks.First(x => x.Index == testHexzmapBlock.Index));
    if (testHexzmapVerifyCells[0] != hexChanged || testHexzmapVerifyCells[1] != hexSecondChanged)
    {
        throw new InvalidOperationException($"Hexzmap 写入验证失败：expected={hexChanged}/{hexSecondChanged}, actual={testHexzmapVerifyCells[0]}/{testHexzmapVerifyCells[1]}");
    }
    if (hexzmapSave.ChangedCells != 2)
    {
        throw new InvalidOperationException($"Hexzmap 多格写入计数异常：expected=2, actual={hexzmapSave.ChangedCells}");
    }
    if (string.IsNullOrWhiteSpace(hexzmapSave.ReportJsonPath) ||
        !File.Exists(hexzmapSave.ReportJsonPath) ||
        !File.ReadAllText(hexzmapSave.ReportJsonPath).Contains("Hexzmap地形格写入", StringComparison.Ordinal))
    {
        throw new InvalidOperationException("Hexzmap 地形格写入未生成结构化报告。");
    }
    Console.WriteLine($"VERIFY_HEXZMAP_WRITE OK {testHexzmapBlock.MapId} cell0=0x{hexOriginal:X2}->0x{hexChanged:X2} cell1=0x{hexSecondOriginal:X2}->0x{hexSecondChanged:X2} changed={hexzmapSave.ChangedCells} backup={Path.GetFileName(hexzmapSave.BackupPath)}");

    var imsgTable = tables.Single(t => t.TableName == "6.5-0-1 人物列传");
    var imsgRead = reader.Read(testProject, imsgTable, tables);
    imsgRead.Data.Rows[32]["介绍"] = "CCZModStudio烟雾测试";
    var imsgSave = writer.SaveToTestCopy(testProject, imsgTable, imsgRead.Data);
    Console.WriteLine($"WRITE_TEXT {imsgTable.TableName} changedBytes={imsgSave.ChangedBytes} backup={imsgSave.BackupPath}");
    if (string.IsNullOrWhiteSpace(imsgSave.ReportJsonPath) ||
        !File.Exists(imsgSave.ReportJsonPath) ||
        !File.ReadAllText(imsgSave.ReportJsonPath).Contains("\"OperationKind\": \"数据表保存\"", StringComparison.Ordinal))
    {
        throw new InvalidOperationException("文本表保存未生成结构化 JSON 写入报告。");
    }
    var imsgReread = reader.Read(testProject, imsgTable, tables);
    var textActual = Convert.ToString(imsgReread.Data.Rows[32]["介绍"]);
    if (textActual != "CCZModStudio烟雾测试")
    {
        throw new InvalidOperationException($"文本写入验证失败：{textActual}");
    }
    Console.WriteLine("VERIFY_TEXT OK");

    var rImageTable = tables.Single(t => t.TableName == "6.5-0-4 R形象");
    var rImageRead = reader.Read(testProject, rImageTable, tables);
    var rColumn = "R形象编号";
    var rOriginal = Convert.ToInt32(rImageRead.Data.Rows[0][rColumn]);
    var rChanged = rOriginal == 0 ? 1 : 0;
    rImageRead.Data.Rows[0][rColumn] = rChanged;
    var rSave = writer.SaveToTestCopy(testProject, rImageTable, rImageRead.Data);
    Console.WriteLine($"WRITE_EXE {rImageTable.TableName} changedBytes={rSave.ChangedBytes} backup={rSave.BackupPath}");
    var rReread = reader.Read(testProject, rImageTable, tables);
    var rActual = Convert.ToInt32(rReread.Data.Rows[0][rColumn]);
    if (rActual != rChanged)
    {
        throw new InvalidOperationException($"EXE R形象写入验证失败：expected={rChanged}, actual={rActual}");
    }
    Console.WriteLine($"VERIFY_EXE OK {rColumn}: {rOriginal} -> {rActual}");

    var imageData = imageAssignmentService.Load(testProject, tables);
    var originalS = Convert.ToInt32(imageData.Rows[1]["S形象编号"]);
    var changedS = originalS == 0 ? 1 : 0;
    imageData.Rows[1]["S形象编号"] = changedS;
    var imageSave = imageAssignmentService.SaveToTestCopy(testProject, tables, imageData);
    Console.WriteLine($"IMAGE_ASSIGN_SAVE tables={imageSave.Saves.Count} changedBytes={imageSave.ChangedBytes} backups={imageSave.BackupSummary}");
    var imageVerify = imageAssignmentService.Load(testProject, tables);
    var actualS = Convert.ToInt32(imageVerify.Rows[1]["S形象编号"]);
    if (actualS != changedS)
    {
        throw new InvalidOperationException($"人物 R/S 联动保存失败：expected={changedS}, actual={actualS}");
    }
    Console.WriteLine($"VERIFY_IMAGE_ASSIGN OK S: {originalS} -> {actualS}");

    var scenarioTextReader = new ScenarioTextReader();
    var testScenarioPath = Path.Combine(testProject.GameRoot, "SV", "SV004.E5S");
    var writableTexts = scenarioTextReader.Read(testScenarioPath).ToList();
    var writableText = writableTexts.FirstOrDefault(x => x.Text.Contains("汜水关之战", StringComparison.Ordinal))
                       ?? writableTexts.FirstOrDefault(x => x.Kind == "标题/场所")
                       ?? throw new InvalidOperationException("未找到可写回的 SV004 剧本文本线索。");
    var originalScenarioText = writableText.Text;
    var replacementScenarioText = originalScenarioText.Contains("汜水关", StringComparison.Ordinal) ? "汜水关战" : "烟测文本";
    if (replacementScenarioText == originalScenarioText) replacementScenarioText = "烟测";
    if (EncodingService.GetGbkByteCount(replacementScenarioText) > writableText.ByteLength)
    {
        replacementScenarioText = "烟测";
    }
    if (EncodingService.GetGbkByteCount(replacementScenarioText) > writableText.ByteLength)
    {
        throw new InvalidOperationException($"SV004 剧本文本容量不足，无法执行短写回测试：capacity={writableText.ByteLength}");
    }

    writableText.Text = replacementScenarioText;
    var scenarioTextSave = new ScenarioTextWriter().SaveInPlaceToTestCopy(testProject, Path.Combine("SV", "SV004.E5S"), new[] { writableText });
    Console.WriteLine($"SCENARIO_TEXT_SAVE entries={scenarioTextSave.EntriesWritten} changedBytes={scenarioTextSave.ChangedBytes} backup={scenarioTextSave.BackupPath}");
    if (string.IsNullOrWhiteSpace(scenarioTextSave.ReportJsonPath) ||
        !File.Exists(scenarioTextSave.ReportJsonPath) ||
        !File.ReadAllText(scenarioTextSave.ReportJsonPath).Contains("\"OperationKind\": \"SV剧本文本写回\"", StringComparison.Ordinal))
    {
        throw new InvalidOperationException("SV 剧本文本写回未生成结构化 JSON 写入报告。");
    }
    var scenarioReportText = reportFormatter.FormatForCreator(scenarioTextSave.ReportJsonPath);
    if (!scenarioReportText.Contains("具体改动", StringComparison.Ordinal) ||
        !scenarioReportText.Contains(writableText.OffsetHex, StringComparison.Ordinal))
    {
        throw new InvalidOperationException("SV 文本结构化报告中文详情缺少具体改动或文本偏移。");
    }
    Console.WriteLine($"STRUCTURED_REPORT scenario={Path.GetFileName(scenarioTextSave.ReportJsonPath)}");
    var textVerify = scenarioTextReader.Read(testScenarioPath).FirstOrDefault(x => x.Offset == writableText.Offset);
    if (textVerify == null || textVerify.Text != replacementScenarioText)
    {
        throw new InvalidOperationException($"SV004 剧本文本写回验证失败：expected={replacementScenarioText}, actual={textVerify?.Text ?? "<missing>"}");
    }
    Console.WriteLine($"VERIFY_SCENARIO_TEXT_WRITE OK {originalScenarioText} -> {textVerify.Text}");

    var testScenarioFiles = new ScenarioFileReader().ReadAll(testProject, sceneDoc);
    var testGameResources = new GameResourceIndexer().Index(testProject);
    var testScenarioMapLinks = new ScenarioMapLinkService().BuildLinks(testScenarioFiles, testGameResources, testHexzmapProbe);
    var testSv004Scenario = testScenarioFiles.First(x => x.FileName.Equals("SV004.E5S", StringComparison.OrdinalIgnoreCase));
    var battlefieldService = new BattlefieldEditorService();
    var battlefieldDoc = battlefieldService.Load(testProject, testSv004Scenario, sceneDoc, tables, testScenarioMapLinks);
    if (battlefieldDoc.TitleEntry == null ||
        !battlefieldDoc.Summary.Contains("出场/坐标候选", StringComparison.Ordinal) ||
        !battlefieldDoc.Annotation.Contains("战场制作文档", StringComparison.Ordinal) ||
        battlefieldDoc.UnitCandidates.Count == 0 ||
        battlefieldDoc.UnitCandidates.All(x => string.IsNullOrWhiteSpace(x.Annotation)) ||
        battlefieldDoc.UnitCandidates.Any(x => string.IsNullOrWhiteSpace(x.TargetKey)))
    {
        throw new InvalidOperationException("战场制作文档未能生成标题、出场/坐标候选、内部键或中文注释。");
    }
    var battlefieldCoordinateCandidate = battlefieldDoc.UnitCandidates.FirstOrDefault(x => BattlefieldEditorService.TryExtractFirstCoordinate(x, out _, out _));
    if (battlefieldCoordinateCandidate == null ||
        !BattlefieldEditorService.TryExtractFirstCoordinate(battlefieldCoordinateCandidate, out var battlefieldX, out var battlefieldY) ||
        battlefieldX < 0 || battlefieldY < 0)
    {
        throw new InvalidOperationException("战场制作未能从出场/坐标候选中解析地图坐标，无法支持地图标记预览。");
    }
    var battlefieldScriptStructure = new ScenarioStructureProbeReader().Build(testScenarioPath, sceneDoc!, maxCommandRows: 600, project: testProject, tables: tables);
    var battlefieldLinkedScriptCommand = BattlefieldEditorService.FindScriptCommandForCandidate(battlefieldScriptStructure.Rows, battlefieldCoordinateCandidate);
    if (battlefieldLinkedScriptCommand == null ||
        battlefieldLinkedScriptCommand.NodeType != "Command候选" ||
        !battlefieldLinkedScriptCommand.OffsetHex.Equals(battlefieldCoordinateCandidate.OffsetHex, StringComparison.OrdinalIgnoreCase))
    {
        throw new InvalidOperationException("战场候选未能定位到剧本制作命令行。");
    }
    var battlefieldReviewService = new BattlefieldUnitReviewService();
    var battlefieldReviewCandidate = battlefieldCoordinateCandidate;
    battlefieldReviewCandidate.ReviewStatus = "已核对";
    battlefieldReviewCandidate.CreatorMemo = BattlefieldUnitReviewService.AppendMemoLine(
        "Smoke：旧工具对照后用于战场出场候选核对，不写入SV。",
        BattlefieldUnitReviewService.BuildCoordinateReviewMemo(battlefieldReviewCandidate, battlefieldX, battlefieldY));
    var quickBattlefieldMemo = BattlefieldUnitReviewService.BuildQuickReviewMemo(battlefieldReviewCandidate, "已核对");
    if (!battlefieldReviewCandidate.CreatorMemo.Contains($"地图点选坐标：({battlefieldX},{battlefieldY})", StringComparison.Ordinal) ||
        !quickBattlefieldMemo.Contains("快速标记：已核对", StringComparison.Ordinal))
    {
        throw new InvalidOperationException("战场制作坐标点选备注/快速标记模板生成失败。");
    }
    var battlefieldReviewPath = battlefieldReviewService.Save(testProject, battlefieldDoc, battlefieldDoc.UnitCandidates);
    if (!File.Exists(battlefieldReviewPath) ||
        !File.ReadAllText(battlefieldReviewPath).Contains("Smoke：旧工具对照", StringComparison.Ordinal))
    {
        throw new InvalidOperationException("战场出场候选核对状态未写入项目侧 JSON。");
    }
    var battlefieldReviewReloadDoc = battlefieldService.Load(testProject, testSv004Scenario, sceneDoc, tables, testScenarioMapLinks);
    battlefieldReviewService.Apply(testProject, battlefieldReviewReloadDoc);
    if (battlefieldReviewReloadDoc.UnitCandidates.FirstOrDefault(x => x.TargetKey == battlefieldReviewCandidate.TargetKey)?.ReviewStatus != "已核对")
    {
        throw new InvalidOperationException("战场出场候选核对状态重新加载失败。");
    }

    var battlefieldTitleReplacement = EncodingService.GetGbkByteCount("烟测关") <= battlefieldDoc.TitleEntry.ByteLength
        ? "烟测关"
        : "烟测";
    if (EncodingService.GetGbkByteCount(battlefieldTitleReplacement) > battlefieldDoc.TitleEntry.ByteLength)
    {
        throw new InvalidOperationException($"战场制作标题容量不足，无法执行短写回测试：capacity={battlefieldDoc.TitleEntry.ByteLength}");
    }
    var battlefieldConditionText = battlefieldDoc.ConditionEntry?.Text ?? string.Empty;
    var battlefieldSave = battlefieldService.SaveTitleAndConditions(testProject, battlefieldDoc, battlefieldTitleReplacement, battlefieldConditionText);
    if (string.IsNullOrWhiteSpace(battlefieldSave.BackupPath) ||
        !File.Exists(battlefieldSave.BackupPath) ||
        string.IsNullOrWhiteSpace(battlefieldSave.ReportJsonPath) ||
        !File.Exists(battlefieldSave.ReportJsonPath) ||
        !File.ReadAllText(battlefieldSave.ReportJsonPath).Contains("\"OperationKind\": \"SV剧本文本写回\"", StringComparison.Ordinal))
    {
        throw new InvalidOperationException("战场制作标题/胜败条件保存未生成备份或结构化写入报告。");
    }

    var battlefieldVerifyDoc = battlefieldService.Load(testProject, testSv004Scenario, sceneDoc, tables, testScenarioMapLinks);
    if (!string.Equals(battlefieldVerifyDoc.TitleEntry?.Text, battlefieldTitleReplacement, StringComparison.Ordinal))
    {
        throw new InvalidOperationException($"战场制作标题复读失败：expected={battlefieldTitleReplacement}, actual={battlefieldVerifyDoc.TitleEntry?.Text ?? "<missing>"}");
    }
    Console.WriteLine($"VERIFY_BATTLEFIELD_TEXT_WRITE OK title={replacementScenarioText}->{battlefieldVerifyDoc.TitleEntry!.Text} units={battlefieldVerifyDoc.UnitCandidates.Count} marker=({battlefieldX},{battlefieldY}) script={battlefieldLinkedScriptCommand.CommandName}@{battlefieldLinkedScriptCommand.OffsetHex} review=已核对 memo=点选 backup={Path.GetFileName(battlefieldSave.BackupPath)}");

    var scriptStructure = new ScenarioStructureProbeReader().Build(testScenarioPath, sceneDoc!, maxCommandRows: 600, project: testProject, tables: tables);
    var scriptTexts = scenarioTextReader.Read(testScenarioPath).ToList();
    if (!scriptStructure.Rows.Any(x => x.NodeType == "Scene候选") ||
        !scriptStructure.Rows.Any(x => x.NodeType == "Section候选") ||
        !scriptStructure.Rows.Any(x => x.NodeType == "Command候选" && !string.IsNullOrWhiteSpace(x.CommandTemplateHint)) ||
        !scriptTexts.Any(x => !string.IsNullOrWhiteSpace(x.Annotation) && x.ByteLength >= x.GbkByteCount))
    {
        throw new InvalidOperationException("剧本制作核心页所需的 Scene/Section/Command、参数模板或文本中文注释数据不完整。");
    }
    var scriptTemplateRow = scriptStructure.Rows.First(x => x.NodeType == "Command候选" && x.HasCommandTemplate);
    var scriptParameterService = new ScenarioCommandParameterTemplateService();
    var scriptMapLink = testScenarioMapLinks.FirstOrDefault(x => x.ScenarioFileName.Equals("SV004.E5S", StringComparison.OrdinalIgnoreCase));
    var scriptParameterRows = scriptParameterService.BuildParameterRows(scriptTemplateRow, scriptMapLink, testProject, tables);
    if (scriptParameterRows.Count == 0 ||
        scriptParameterRows.All(x => string.IsNullOrWhiteSpace(x.DecodedValue)) ||
        scriptParameterRows.All(x => string.IsNullOrWhiteSpace(x.Annotation)))
    {
        throw new InvalidOperationException("剧本制作参数表格行未生成解释或中文注释。");
    }
    var scriptSearchByCommand = new ScenarioScriptSearchService().Search(scriptTemplateRow.CommandName, scriptStructure, scriptTexts);
    var scriptTextForSearch = scriptTexts.FirstOrDefault(x => !string.IsNullOrWhiteSpace(x.Text));
    var scriptSearchByText = scriptTextForSearch == null
        ? Array.Empty<ScenarioSearchResultRow>()
        : new ScenarioScriptSearchService().Search(scriptTextForSearch.Text, scriptStructure, scriptTexts);
    if (!scriptSearchByCommand.Any(x => x.CommandRow != null && x.CommandRow.OffsetHex == scriptTemplateRow.OffsetHex) ||
        (scriptTextForSearch != null && !scriptSearchByText.Any(x => x.TextEntry != null && x.TextEntry.Offset == scriptTextForSearch.Offset)) ||
        scriptSearchByCommand.All(x => string.IsNullOrWhiteSpace(x.Annotation) && string.IsNullOrWhiteSpace(x.ActionHint)))
    {
        throw new InvalidOperationException("剧本制作搜索结果列表未能覆盖命令/文本定位或缺少中文操作提示。");
    }

    var scriptTreeSummary = BuildScriptEditorTreeSummary(scriptStructure, scriptTexts);
    if (scriptTreeSummary.SectionTextGroupCount == 0 || scriptTreeSummary.AttachedTextNodeCount == 0)
    {
        throw new InvalidOperationException($"Script editor tree did not attach text clues at Section level: sectionGroups={scriptTreeSummary.SectionTextGroupCount}, attached={scriptTreeSummary.AttachedTextNodeCount}");
    }
    if (scriptTreeSummary.AttachedTextNodeCount > scriptTexts.Count)
    {
        throw new InvalidOperationException($"Script editor tree attached more text nodes than source texts: attached={scriptTreeSummary.AttachedTextNodeCount}, source={scriptTexts.Count}");
    }
    Console.WriteLine($"SCRIPT_TREE sectionTextGroups={scriptTreeSummary.SectionTextGroupCount} sceneFallbackGroups={scriptTreeSummary.SceneFallbackGroupCount} unassignedGroups={scriptTreeSummary.UnassignedGroupCount} attachedTexts={scriptTreeSummary.AttachedTextNodeCount}");

    var scriptClipboardService = new ScenarioCommandClipboardService();
    var scriptCommandCopy = scriptClipboardService.BuildCommandCopyText("SV004.E5S", scriptTemplateRow, scriptParameterRows);
    if (!scriptCommandCopy.Contains("剧本命令复制候选", StringComparison.Ordinal) ||
        !scriptCommandCopy.Contains("参数表", StringComparison.Ordinal) ||
        !scriptCommandCopy.Contains("安全边界", StringComparison.Ordinal) ||
        !scriptCommandCopy.Contains(scriptTemplateRow.CommandName, StringComparison.Ordinal))
    {
        throw new InvalidOperationException("剧本制作命令复制候选摘要缺少标题、参数表、命令名或安全边界。");
    }
    var scriptClipboardItem = scriptClipboardService.CreateClipboardItem("SV004.E5S", scriptTemplateRow, scriptParameterRows);
    var scriptPasteTarget = scriptStructure.Rows.FirstOrDefault(x => x.NodeType == "Command候选" && x.OffsetHex != scriptTemplateRow.OffsetHex)
                            ?? scriptTemplateRow;
    var scriptPasteTargetParameters = scriptParameterService.BuildParameterRows(scriptPasteTarget, scriptMapLink, testProject, tables);
    var scriptPastePreview = scriptClipboardService.BuildPastePreview(scriptClipboardItem, "SV004.E5S", scriptPasteTarget, scriptPasteTargetParameters);
    if (!scriptPastePreview.Contains("粘贴预览（不写入）", StringComparison.Ordinal) ||
        !scriptPastePreview.Contains("参数差异", StringComparison.Ordinal) ||
        !scriptPastePreview.Contains("预览结论", StringComparison.Ordinal) ||
        !scriptPastePreview.Contains(scriptTemplateRow.CommandName, StringComparison.Ordinal))
    {
        throw new InvalidOperationException("剧本制作粘贴预览缺少不写入声明、参数差异、预览结论或来源命令名。");
    }
    var scriptText = scriptTexts.FirstOrDefault(x => x.Kind == "短文本/标题候选")
                     ?? scriptTexts.FirstOrDefault(x => x.Offset != battlefieldVerifyDoc.TitleEntry.Offset)
                     ?? throw new InvalidOperationException("未找到可用于剧本制作文本写回验证的文本线索。");
    var scriptOriginalText = scriptText.Text;
    var scriptReplacementText = EncodingService.GetGbkByteCount("剧本测") <= scriptText.ByteLength ? "剧本测" : "测";
    if (EncodingService.GetGbkByteCount(scriptReplacementText) > scriptText.ByteLength)
    {
        throw new InvalidOperationException($"剧本制作文本容量不足，无法执行短写回测试：capacity={scriptText.ByteLength}");
    }
    scriptText.Text = scriptReplacementText;
    var scriptTextSave = new ScenarioTextWriter().SaveInPlace(testProject, Path.Combine("SV", "SV004.E5S"), new[] { scriptText }, "剧本制作页保存文本前自动备份");
    var scriptTextVerify = scenarioTextReader.Read(testScenarioPath).FirstOrDefault(x => x.Offset == scriptText.Offset);
    if (scriptTextVerify == null || scriptTextVerify.Text != scriptReplacementText ||
        string.IsNullOrWhiteSpace(scriptTextSave.ReportJsonPath) ||
        !File.Exists(scriptTextSave.ReportJsonPath))
    {
        throw new InvalidOperationException($"剧本制作文本写回验证失败：expected={scriptReplacementText}, actual={scriptTextVerify?.Text ?? "<missing>"}");
    }
    Console.WriteLine($"VERIFY_SCRIPT_EDITOR_TEXT_WRITE OK {scriptOriginalText}->{scriptTextVerify.Text} commands={scriptStructure.CommandCandidateCount} texts={scriptTexts.Count} params={scriptParameterRows.Count} search={scriptSearchByCommand.Count + scriptSearchByText.Count} copyChars={scriptCommandCopy.Length} pastePreviewChars={scriptPastePreview.Length} backup={Path.GetFileName(scriptTextSave.BackupPath)}");

    PatchApplyResult? patchApplyResult = null;
    if (patchDocument != null)
    {
        var patchService = new PatchApplyService();
        var beforePatch = patchService.Preview(testProject, patchDocument, "Ekd5.exe");
        if (!beforePatch.CanApply)
        {
            throw new InvalidOperationException("补丁预览不可应用：" + string.Join("; ", beforePatch.Rows.Where(r => !r.CanApply).Take(3).Select(r => r.Status)));
        }

        var patchApply = patchService.ApplyToTestCopy(testProject, patchDocument, "Ekd5.exe");
        patchApplyResult = patchApply;
        Console.WriteLine($"PATCH_APPLY entries={patchApply.EntriesApplied} bytes={patchApply.BytesWritten} changedBytes={patchApply.ChangedBytes} backup={patchApply.BackupPath} report={patchApply.ReportPath} json={patchApply.ReportJsonPath}");
        if (string.IsNullOrWhiteSpace(patchApply.ReportJsonPath) ||
            !File.Exists(patchApply.ReportJsonPath) ||
            !File.ReadAllText(patchApply.ReportJsonPath).Contains("\"OperationKind\": \"补丁写入\"", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("补丁应用未生成结构化 JSON 写入报告。");
        }
        var patchReportText = reportFormatter.FormatForCreator(patchApply.ReportJsonPath);
        if (!patchReportText.Contains("补丁写入", StringComparison.Ordinal) ||
            !patchReportText.Contains("文件偏移", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("补丁结构化报告中文详情缺少补丁类型或文件偏移。");
        }
        Console.WriteLine($"STRUCTURED_REPORT patch={Path.GetFileName(patchApply.ReportJsonPath)}");
        var afterPatch = patchService.Preview(testProject, patchDocument, "Ekd5.exe");
        if (afterPatch.ChangedBytes != 0)
        {
            throw new InvalidOperationException($"补丁应用后仍有待改变字节：{afterPatch.ChangedBytes}");
        }
        Console.WriteLine("VERIFY_PATCH_APPLY OK");
    }

    var resourceReplaceTarget = Path.Combine(testProject.GameRoot, "Map", "M000.jpg");
    var mapImageReplaceService = new MapImageReplaceService();
    var directMapReplace = mapImageReplaceService.ReplaceMapImage(testProject, resourceReplaceTarget, mapReplacementSource);
    if (!File.ReadAllBytes(resourceReplaceTarget).SequenceEqual(File.ReadAllBytes(mapReplacementSource)) ||
        !File.Exists(directMapReplace.BackupPath) ||
        !File.Exists(directMapReplace.ReportJsonPath) ||
        !File.ReadAllText(directMapReplace.ReportJsonPath).Contains("地图底图替换", StringComparison.Ordinal) ||
        !directMapReplace.FormatCheckSummary.Contains("JPEG 图片格式检查通过", StringComparison.Ordinal))
    {
        throw new InvalidOperationException("地图制作底图替换写入/复读/报告验证失败。");
    }
    Console.WriteLine($"VERIFY_MAP_IMAGE_REPLACE OK target={Path.GetFileName(directMapReplace.TargetPath)} size={directMapReplace.OldWidth}x{directMapReplace.OldHeight}->{directMapReplace.NewWidth}x{directMapReplace.NewHeight} backup={Path.GetFileName(directMapReplace.BackupPath)}");

    var directMapRestore = mapImageReplaceService.ReplaceMapImage(testProject, resourceReplaceTarget, directMapReplace.BackupPath);
    if (!File.ReadAllBytes(resourceReplaceTarget).SequenceEqual(File.ReadAllBytes(directMapReplace.BackupPath)) ||
        !File.Exists(directMapRestore.BackupPath) ||
        !File.Exists(directMapRestore.ReportJsonPath))
    {
        throw new InvalidOperationException("地图制作底图替换备份还原验证失败。");
    }
    Console.WriteLine($"VERIFY_MAP_IMAGE_RESTORE OK backup={Path.GetFileName(directMapRestore.BackupPath)} json={Path.GetFileName(directMapRestore.ReportJsonPath)}");

    var resourceReplaceService = new ResourceReplaceService();
    var resourceReplacePreview = resourceReplaceService.PreviewReplacement(testProject, resourceReplaceTarget, mapReplacementSource);
    Console.WriteLine($"RESOURCE_REPLACE_PREVIEW target={Path.GetFileName(resourceReplacePreview.TargetPath)} old={resourceReplacePreview.OldSizeBytes} new={resourceReplacePreview.NewSizeBytes} delta={resourceReplacePreview.SizeDeltaBytes} changed={resourceReplacePreview.ChangedBytesEstimate} same={resourceReplacePreview.IsContentIdentical} risk={resourceReplacePreview.RiskSummary}");
    if (resourceReplacePreview.OldSizeBytes <= 0 ||
        resourceReplacePreview.NewSizeBytes <= 0 ||
        resourceReplacePreview.ChangedBytesEstimate <= 0 ||
        resourceReplacePreview.IsContentIdentical ||
        string.Equals(resourceReplacePreview.OldSha256, resourceReplacePreview.NewSha256, StringComparison.OrdinalIgnoreCase) ||
        !resourceReplacePreview.FormatCheckSummary.Contains("图片格式检查通过", StringComparison.Ordinal) ||
        string.IsNullOrWhiteSpace(resourceReplacePreview.RiskSummary))
    {
        throw new InvalidOperationException("测试副本资源替换预览验证失败。");
    }
    Console.WriteLine("VERIFY_RESOURCE_REPLACE_PREVIEW OK");

    var resourceReplace = resourceReplaceService.ReplaceInTestCopy(testProject, resourceReplaceTarget, mapReplacementSource);
    Console.WriteLine($"RESOURCE_REPLACE target={Path.GetFileName(resourceReplace.TargetPath)} old={resourceReplace.OldSizeBytes} new={resourceReplace.NewSizeBytes} changed={resourceReplace.ChangedBytesEstimate} format={resourceReplace.FormatCheckSummary} backup={resourceReplace.BackupPath} json={resourceReplace.ReportJsonPath}");
    var replacedBytes = File.ReadAllBytes(resourceReplaceTarget);
    var replacementBytes = File.ReadAllBytes(mapReplacementSource);
    if (!replacedBytes.SequenceEqual(replacementBytes) ||
        !File.Exists(resourceReplace.BackupPath) ||
        !File.Exists(resourceReplace.ReportPath) ||
        !File.Exists(resourceReplace.ReportJsonPath) ||
        !resourceReplace.FormatCheckSummary.Contains("图片格式检查通过", StringComparison.Ordinal) ||
        string.IsNullOrWhiteSpace(resourceReplace.RiskSummary))
    {
        throw new InvalidOperationException("测试副本资源整文件替换验证失败。");
    }
    var resourceReportText = reportFormatter.FormatForCreator(resourceReplace.ReportJsonPath);
    if (!resourceReportText.Contains("资源整文件", StringComparison.Ordinal) ||
        !resourceReportText.Contains("M000.jpg", StringComparison.Ordinal))
    {
        throw new InvalidOperationException("资源整文件替换结构化报告中文详情缺少资源类别或目标文件名。");
    }
    Console.WriteLine($"STRUCTURED_REPORT resource={Path.GetFileName(resourceReplace.ReportJsonPath)}");
    Console.WriteLine("VERIFY_RESOURCE_REPLACE OK");

    var e5WriteService = new E5ImageReplaceService();
    var e5ReplaceTarget = Path.Combine(testProject.GameRoot, "Unit_mov.e5");
    var e5ReplacementPng = Path.Combine(smokeRoot, "Smoke_E5_Replacement.png");
    using (var bitmap = new System.Drawing.Bitmap(12, 12, System.Drawing.Imaging.PixelFormat.Format32bppArgb))
    {
        using var graphics = System.Drawing.Graphics.FromImage(bitmap);
        graphics.Clear(System.Drawing.Color.Transparent);
        using var redBrush = new System.Drawing.SolidBrush(System.Drawing.Color.FromArgb(255, 220, 32, 64));
        using var blueBrush = new System.Drawing.SolidBrush(System.Drawing.Color.FromArgb(255, 32, 96, 220));
        graphics.FillRectangle(redBrush, 1, 1, 5, 10);
        graphics.FillRectangle(blueBrush, 6, 1, 5, 10);
        bitmap.Save(e5ReplacementPng, System.Drawing.Imaging.ImageFormat.Png);
    }
    var e5WritePreview = e5WriteService.PreviewReplacement(testProject, e5ReplaceTarget, 554, e5ReplacementPng);
    if (e5WritePreview.ImageNumber != 554 ||
        e5WritePreview.NewKind != "PNG" ||
        e5WritePreview.NewSizeBytes != new FileInfo(e5ReplacementPng).Length ||
        string.IsNullOrWhiteSpace(e5WritePreview.RiskSummary))
    {
        throw new InvalidOperationException("E5 图片条目替换写入预览验证失败。");
    }
    var e5Write = e5WriteService.Replace(testProject, e5ReplaceTarget, 554, e5ReplacementPng);
    if (!File.Exists(e5Write.BackupPath) ||
        !File.Exists(e5Write.ReportPath) ||
        !File.Exists(e5Write.ReportJsonPath) ||
        !File.ReadAllText(e5Write.ReportJsonPath).Contains("E5图片条目替换", StringComparison.Ordinal) ||
        !e5WriteService.ReadEntryBytes(e5ReplaceTarget, 554).SequenceEqual(File.ReadAllBytes(e5ReplacementPng)))
    {
        throw new InvalidOperationException("E5 图片条目替换写入/复读/报告验证失败。");
    }
    Console.WriteLine($"VERIFY_E5_IMAGE_REPLACE OK target={Path.GetFileName(e5Write.TargetPath)} image={e5Write.ImageNumber} kind={e5Write.OldKind}->{e5Write.NewKind} backup={Path.GetFileName(e5Write.BackupPath)} json={Path.GetFileName(e5Write.ReportJsonPath)}");

    var diffService = new TestCopyDiffService();
    var diffItems = diffService.Analyze(testProject);
    Console.WriteLine($"DIFF items={diffItems.Count} modified={diffItems.Count(x => x.Status == "已修改")} added={diffItems.Count(x => x.Status == "新增")} missing={diffItems.Count(x => x.Status == "缺失")}");
    foreach (var diff in diffItems.Where(x => x.Status == "已修改").Take(8))
    {
        Console.WriteLine($"DIFF_ROW {diff.Status} {diff.RelativePath} source={diff.SourceSize} test={diff.TestSize}");
    }
    foreach (var expectedModified in new[] { "Data.e5", "Imsg.e5", "Ekd5.exe", Path.Combine("SV", "SV004.E5S"), Path.Combine("Map", "M000.jpg") })
    {
        if (!diffItems.Any(x => x.Status == "已修改" && x.RelativePath.Equals(expectedModified, StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException($"测试副本差异未检出预期修改：{expectedModified}");
        }
    }
    var diffReport = diffService.WriteReport(testProject, diffItems);
    if (!File.Exists(diffReport))
    {
        throw new InvalidOperationException("测试副本差异报告未生成。");
    }
    Console.WriteLine($"VERIFY_DIFF OK report={diffReport}");

    var releaseResult = new ReleasePackageService().CreateReleaseCopy(testProject, diffItems);
    if (!Directory.Exists(releaseResult.ReleaseRoot) || !File.Exists(releaseResult.ManifestPath))
    {
        throw new InvalidOperationException("发布副本或发布清单未生成。");
    }
    if (File.Exists(Path.Combine(releaseResult.ReleaseRoot, "_CCZModStudio_TestCopy.txt")))
    {
        throw new InvalidOperationException("发布副本不应包含测试副本标记文件。");
    }
    foreach (var expectedReleaseFile in new[] { "Data.e5", "Imsg.e5", "Ekd5.exe", Path.Combine("SV", "SV004.E5S"), Path.Combine("Map", "M000.jpg") })
    {
        if (!File.Exists(Path.Combine(releaseResult.ReleaseRoot, expectedReleaseFile)))
        {
            throw new InvalidOperationException($"发布副本缺少预期文件：{expectedReleaseFile}");
        }
    }
    Console.WriteLine($"VERIFY_RELEASE_COPY OK root={releaseResult.ReleaseRoot} files={releaseResult.FilesCopied} changed={releaseResult.ChangedItems}");

    var resourceRestorePreview = resourceReplaceService.PreviewReplacement(testProject, resourceReplaceTarget, resourceReplace.BackupPath);
    Console.WriteLine($"RESOURCE_RESTORE_PREVIEW target={Path.GetFileName(resourceRestorePreview.TargetPath)} backup={Path.GetFileName(resourceReplace.BackupPath)} changed={resourceRestorePreview.ChangedBytesEstimate} risk={resourceRestorePreview.RiskSummary}");
    if (resourceRestorePreview.ChangedBytesEstimate <= 0 ||
        resourceRestorePreview.IsContentIdentical ||
        !resourceRestorePreview.FormatCheckSummary.Contains("图片格式检查通过", StringComparison.Ordinal))
    {
        throw new InvalidOperationException("资源备份还原预览验证失败。");
    }
    var resourceRestore = resourceReplaceService.ReplaceInTestCopy(testProject, resourceReplaceTarget, resourceReplace.BackupPath);
    var restoredBytes = File.ReadAllBytes(resourceReplaceTarget);
    var originalBackupBytes = File.ReadAllBytes(resourceReplace.BackupPath);
    if (!restoredBytes.SequenceEqual(originalBackupBytes) ||
        !File.Exists(resourceRestore.BackupPath) ||
        !File.Exists(resourceRestore.ReportPath) ||
        !File.Exists(resourceRestore.ReportJsonPath))
    {
        throw new InvalidOperationException("资源备份还原验证失败。");
    }
    Console.WriteLine($"VERIFY_RESOURCE_RESTORE OK backup={resourceRestore.BackupPath} json={Path.GetFileName(resourceRestore.ReportJsonPath)}");

    if (smokeWavTarget != null && wavSource != null)
    {
        var wavPreview = resourceReplaceService.PreviewReplacement(testProject, smokeWavTarget, wavSource);
        Console.WriteLine($"VERIFY_AUDIO_WAV_PREVIEW summary={wavPreview.FormatCheckSummary}");
        if (!wavPreview.FormatCheckSummary.Contains("WAV 格式检查通过", StringComparison.Ordinal) ||
            !wavPreview.FormatCheckSummary.Contains("Hz", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("WAV 资源替换预览未生成采样率/格式摘要。");
        }
    }

    if (smokeMp3Target != null && mp3Source != null)
    {
        var mp3Preview = resourceReplaceService.PreviewReplacement(testProject, smokeMp3Target, mp3Source);
        Console.WriteLine($"VERIFY_AUDIO_MP3_PREVIEW summary={mp3Preview.FormatCheckSummary}");
        if (!mp3Preview.FormatCheckSummary.Contains("MP3 格式检查通过", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("MP3 资源替换预览未生成格式摘要。");
        }
    }

    var backupHistory = new BackupHistoryService().Scan(testProject);
    var restorableBackups = backupHistory.Count(x => x.Restorable);
    Console.WriteLine($"BACKUP_HISTORY count={backupHistory.Count} restorable={restorableBackups} reports={backupHistory.Count(x => !string.IsNullOrWhiteSpace(x.ReportPath))}");
    if (backupHistory.Count == 0 || restorableBackups == 0)
    {
        throw new InvalidOperationException("备份历史扫描未发现可还原备份。");
    }

    foreach (var expectedTarget in new[] { "Data.e5", "Imsg.e5", "Ekd5.exe", Path.Combine("SV", "SV004.E5S"), Path.Combine("Map", "M000.jpg") })
    {
        if (!backupHistory.Any(x => x.TargetRelativePath.Equals(expectedTarget, StringComparison.OrdinalIgnoreCase) && x.Restorable))
        {
            throw new InvalidOperationException("备份历史缺少预期可还原目标：" + expectedTarget);
        }
    }

    foreach (var structuredTarget in new[] { "Data.e5", "Imsg.e5", "Ekd5.exe", Path.Combine("SV", "SV004.E5S"), Path.Combine("Map", "M000.jpg") })
    {
        var structuredHistory = backupHistory.FirstOrDefault(x =>
            x.TargetRelativePath.Equals(structuredTarget, StringComparison.OrdinalIgnoreCase) &&
            x.ReportPath.EndsWith("WriteOperationReport.json", StringComparison.OrdinalIgnoreCase));
        if (structuredHistory == null || string.IsNullOrWhiteSpace(structuredHistory.Annotation))
        {
            throw new InvalidOperationException("备份历史未能识别结构化写入报告：" + structuredTarget);
        }
    }
    Console.WriteLine("VERIFY_STRUCTURED_WRITE_REPORT OK");

    var resourceReplaceBackupHistory = backupHistory.FirstOrDefault(x => Path.GetFullPath(x.BackupPath).Equals(Path.GetFullPath(resourceReplace.BackupPath), StringComparison.OrdinalIgnoreCase));
    if (resourceReplaceBackupHistory == null ||
        !resourceReplaceBackupHistory.TargetRelativePath.Equals(Path.Combine("Map", "M000.jpg"), StringComparison.OrdinalIgnoreCase) ||
        string.IsNullOrWhiteSpace(resourceReplaceBackupHistory.ReportPath) ||
        !resourceReplaceBackupHistory.ReportPath.EndsWith("WriteOperationReport.json", StringComparison.OrdinalIgnoreCase) ||
        string.IsNullOrWhiteSpace(resourceReplaceBackupHistory.Annotation))
    {
        throw new InvalidOperationException("备份历史未能把资源替换备份关联到报告和目标路径。");
    }
    Console.WriteLine($"VERIFY_BACKUP_HISTORY OK mapTarget={resourceReplaceBackupHistory.TargetRelativePath} report={Path.GetFileName(resourceReplaceBackupHistory.ReportPath)}");

    if (patchApplyResult != null)
    {
        var patchBackupHistory = backupHistory.FirstOrDefault(x => Path.GetFullPath(x.BackupPath).Equals(Path.GetFullPath(patchApplyResult.BackupPath), StringComparison.OrdinalIgnoreCase));
        if (patchBackupHistory == null ||
            !patchBackupHistory.TargetRelativePath.Equals("Ekd5.exe", StringComparison.OrdinalIgnoreCase) ||
            !patchBackupHistory.ReportPath.EndsWith("WriteOperationReport.json", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("备份历史未能把补丁备份关联到结构化报告。");
        }
        Console.WriteLine($"VERIFY_PATCH_BACKUP_HISTORY OK target={patchBackupHistory.TargetRelativePath} report={Path.GetFileName(patchBackupHistory.ReportPath)}");
    }

    var mapDiffItem = diffItems.First(x => x.RelativePath.Equals(Path.Combine("Map", "M000.jpg"), StringComparison.OrdinalIgnoreCase));
    var backupTimelineLinkService = new BackupTimelineLinkService();
    var relatedMapBackups = backupTimelineLinkService.FindRelatedBackups(mapDiffItem, backupHistory);
    var relatedMapSummary = backupTimelineLinkService.BuildSummary(mapDiffItem, relatedMapBackups);
    if (relatedMapBackups.Count == 0 ||
        !relatedMapBackups.Any(x => Path.GetFullPath(x.BackupPath).Equals(Path.GetFullPath(resourceReplace.BackupPath), StringComparison.OrdinalIgnoreCase)) ||
        !relatedMapSummary.Contains("相关备份", StringComparison.Ordinal) ||
        !relatedMapSummary.Contains("Map", StringComparison.Ordinal))
    {
        throw new InvalidOperationException("测试副本差异未能关联到备份历史时间线。");
    }
    Console.WriteLine($"VERIFY_DIFF_BACKUP_TIMELINE OK file={mapDiffItem.RelativePath} backups={relatedMapBackups.Count} latest={relatedMapBackups[0].CreatedAtText}");

    var testAuditItems = auditService.Analyze(testProject, tables);
    var deliveryDiagnostics = resourceDiagnostics
        .Concat(resourceReferenceDiagnostics)
        .Concat(scenarioMapDiagnostics)
        .ToList();
    var deliveryCreatorNotes = loadedCreatorNotes
        .Concat(scenarioCommandReferenceNoteDraft == null ? Array.Empty<CreatorNote>() : new[] { scenarioCommandReferenceNoteDraft })
        .ToList();
    var deliveryReportPath = deliveryReportService.WriteReport(
        testProject,
        tables,
        testAuditItems,
        diffItems,
        backupHistory,
        deliveryDiagnostics,
        scenarioMapLinks,
        deliveryCreatorNotes);
    var deliveryReportText = File.Exists(deliveryReportPath) ? File.ReadAllText(deliveryReportPath) : string.Empty;
    if (!File.Exists(deliveryReportPath) ||
        !deliveryReportText.Contains("发布前综合报告", StringComparison.Ordinal) ||
        !deliveryReportText.Contains("测试副本差异摘要", StringComparison.Ordinal) ||
        !deliveryReportText.Contains("备份历史与结构化写入报告", StringComparison.Ordinal) ||
        !deliveryReportText.Contains("MOD创作风险摘要", StringComparison.Ordinal) ||
        !deliveryReportText.Contains("人物 R/S 形象引用", StringComparison.Ordinal) ||
        !deliveryReportText.Contains("关卡地图联动", StringComparison.Ordinal) ||
        !deliveryReportText.Contains("创作者备注", StringComparison.Ordinal) ||
        !deliveryReportText.Contains("最近报告/发布证据摘要", StringComparison.Ordinal) ||
        !deliveryReportText.Contains("R/S eex 命令备注", StringComparison.Ordinal) ||
        !deliveryReportText.Contains("R/S命令引用核对清单", StringComparison.Ordinal) ||
        !deliveryReportText.Contains("R/S eex 命令参数模板目录", StringComparison.Ordinal) ||
        !deliveryReportText.Contains("可视化预览 PNG", StringComparison.Ordinal) ||
        !deliveryReportText.Contains("发布前检查清单", StringComparison.Ordinal) ||
        !deliveryReportText.Contains("安全边界", StringComparison.Ordinal) ||
        !deliveryReportText.Contains("M000.jpg", StringComparison.Ordinal))
    {
        throw new InvalidOperationException("发布前综合报告文件未生成预期差异、备份或安全清单内容。");
    }
    Console.WriteLine($"VERIFY_DELIVERY_REPORT OK file={Path.GetFileName(deliveryReportPath)} chars={deliveryReportText.Length}");
}

static void RunLegacyE5sSmoke(CczProject project)
{
    var svDir = Path.Combine(project.GameRoot, "SV");
    var files = Directory.Exists(svDir)
        ? Directory.GetFiles(svDir, "*.E5S", SearchOption.TopDirectoryOnly)
            .OrderBy(x => x, StringComparer.CurrentCultureIgnoreCase)
            .ToList()
        : new List<string>();
    Console.WriteLine($"LEGACY_E5S_INDEX count={files.Count} dir={svDir}");
    if (files.Count == 0)
    {
        throw new InvalidOperationException("未找到 SV\\*.E5S；E5S 兼容检查无法运行。");
    }

    var configPath = ProjectDetector.FindPortableFile(
        project,
        "System.ini",
        Path.Combine("老版游戏制作工具", "B形象指定器", "形象指定器6.5", "System.ini"),
        Path.Combine("B形象指定器", "形象指定器6.5", "System.ini"))
        ?? string.Empty;
    var countSvText = ReadIniValue(configPath, "CountSV") ?? "未找到";
    Console.WriteLine($"LEGACY_E5S_CONFIG CountSV={countSvText} source={configPath}");
    if (!string.Equals(countSvText, "900", StringComparison.OrdinalIgnoreCase))
    {
        Console.WriteLine("LEGACY_E5S_CONFIG_WARN CountSV 与当前已知 6.5 默认值 900 不一致，请确认是否为用户扩展存档数。");
    }

    var first = files.First();
    var firstInfo = new FileInfo(first);
    var firstHeader = File.ReadAllBytes(first).Take(16).Select(x => x.ToString("X2", CultureInfo.InvariantCulture));
    Console.WriteLine($"LEGACY_E5S_ROW first={Path.GetFileName(first)} size={firstInfo.Length} head={string.Join(' ', firstHeader)}");

    var sv004 = files.FirstOrDefault(x => Path.GetFileName(x).Equals("SV004.E5S", StringComparison.OrdinalIgnoreCase)) ?? first;
    var textRows = new ScenarioTextReader().Read(sv004, maxItems: 16);
    Console.WriteLine($"LEGACY_E5S_TEXT file={Path.GetFileName(sv004)} rows={textRows.Count} note=compat_only_not_rs_script");

    var sceneStringPath = ProjectDetector.FindSceneDictionaryPath(project);
    if (File.Exists(sceneStringPath))
    {
        var sceneDoc = new SceneStringParser().Parse(sceneStringPath);
        var commandRows = new ScenarioCommandProbeReader().Probe(sv004, sceneDoc, maxRows: 40);
        Console.WriteLine($"LEGACY_E5S_COMMAND_PROBE file={Path.GetFileName(sv004)} rows={commandRows.Count} note=old_probe_only");
    }
    else
    {
        Console.WriteLine("LEGACY_E5S_COMMAND_PROBE skipped: CczString.ini not found");
    }

    Console.WriteLine("LEGACY_E5S_SMOKE OK");
}

static string? ReadIniValue(string path, string key)
{
    if (!File.Exists(path)) return null;
    foreach (var raw in File.ReadLines(path))
    {
        var line = raw.Trim();
        if (line.Length == 0 || line.StartsWith("[", StringComparison.Ordinal) || line.StartsWith(";", StringComparison.Ordinal)) continue;
        var comment = line.IndexOf(';');
        if (comment >= 0) line = line[..comment].Trim();
        var index = line.IndexOf('=', StringComparison.Ordinal);
        if (index <= 0) continue;
        if (line[..index].Trim().Equals(key, StringComparison.OrdinalIgnoreCase))
        {
            return line[(index + 1)..].Trim();
        }
    }

    return null;
}

static void RunMigrationSmoke(CczProject sourceProject)
{
    var migrationRoot = Path.Combine(Path.GetTempPath(), "CCZModStudio_MigrationSmoke_" + Guid.NewGuid().ToString("N"));
    var movedGameRoot = Path.Combine(migrationRoot, "MovedGame");

    try
    {
        Directory.CreateDirectory(movedGameRoot);
        foreach (var coreFile in new[] { "Ekd5.exe", "Data.e5", "Star.e5", "Imsg.e5" })
        {
            var source = Path.Combine(sourceProject.GameRoot, coreFile);
            if (!File.Exists(source))
            {
                throw new FileNotFoundException("Migration smoke missing source core file.", source);
            }

            File.Copy(source, Path.Combine(movedGameRoot, coreFile), overwrite: false);
        }

        var rsRoot = Path.Combine(movedGameRoot, "RS");
        Directory.CreateDirectory(rsRoot);
        var sourceScenarioPath = Path.Combine(sourceProject.GameRoot, "RS", "R_00.eex");
        if (!File.Exists(sourceScenarioPath))
        {
            sourceScenarioPath = Directory.GetFiles(Path.Combine(sourceProject.GameRoot, "RS"), "R_*.eex", SearchOption.TopDirectoryOnly)
                .OrderBy(x => x, StringComparer.CurrentCultureIgnoreCase)
                .FirstOrDefault()
                ?? throw new FileNotFoundException("Migration smoke cannot find R_*.eex.", Path.Combine(sourceProject.GameRoot, "RS"));
        }

        File.Copy(sourceScenarioPath, Path.Combine(rsRoot, Path.GetFileName(sourceScenarioPath)), overwrite: false);

        var migratedProject = new ProjectDetector().CreateProjectFromGameRoot(movedGameRoot);
        if (!migratedProject.GameRoot.Equals(movedGameRoot, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Migration smoke resolved an unexpected game root: " + migratedProject.GameRoot);
        }

        if (!File.Exists(migratedProject.HexTableXmlPath))
        {
            throw new FileNotFoundException(ProjectDetector.BuildMissingHexTableMessage(migratedProject), migratedProject.HexTableXmlPath);
        }

        var migratedTables = new HexTableParser().Load(migratedProject.HexTableXmlPath);
        if (migratedTables.Count == 0)
        {
            throw new InvalidOperationException("Migration smoke loaded zero HexTable definitions.");
        }

        var sceneStringPath = ProjectDetector.FindSceneDictionaryPath(migratedProject);
        if (!File.Exists(sceneStringPath))
        {
            throw new FileNotFoundException("Migration smoke cannot relocate CczString.ini.", sceneStringPath);
        }

        if (!string.IsNullOrWhiteSpace(migratedProject.SceneEditorDirectory) &&
            !Directory.Exists(migratedProject.SceneEditorDirectory))
        {
            throw new DirectoryNotFoundException("Migration smoke resolved a stale scene editor directory: " + migratedProject.SceneEditorDirectory);
        }

        if (!string.IsNullOrWhiteSpace(migratedProject.ImageAssignerDirectory) &&
            !Directory.Exists(migratedProject.ImageAssignerDirectory))
        {
            throw new DirectoryNotFoundException("Migration smoke resolved a stale image assigner directory: " + migratedProject.ImageAssignerDirectory);
        }

        if (!string.IsNullOrWhiteSpace(migratedProject.ImageAssignerSystemIniPath) &&
            !File.Exists(migratedProject.ImageAssignerSystemIniPath))
        {
            throw new FileNotFoundException("Migration smoke resolved a stale image assigner System.ini.", migratedProject.ImageAssignerSystemIniPath);
        }

        if (!string.IsNullOrWhiteSpace(migratedProject.MaterialLibraryRoot) &&
            !Directory.Exists(migratedProject.MaterialLibraryRoot))
        {
            throw new DirectoryNotFoundException("Migration smoke resolved a stale material library root: " + migratedProject.MaterialLibraryRoot);
        }

        if (!string.IsNullOrWhiteSpace(migratedProject.PatchConfigRoot) &&
            !Directory.Exists(migratedProject.PatchConfigRoot))
        {
            throw new DirectoryNotFoundException("Migration smoke resolved a stale patch config root: " + migratedProject.PatchConfigRoot);
        }

        var materialAssets = new MaterialLibraryIndexer().Index(migratedProject);
        var scenarioIndex = new ScenarioFileReader().ReadAllIndex(migratedProject);
        if (scenarioIndex.Count != 1 || !ScenarioFileReader.IsRsScriptFile(scenarioIndex[0].FileName))
        {
            throw new InvalidOperationException("Migration smoke failed to read the moved R/S scenario index.");
        }

        Console.WriteLine($"MIGRATION_SMOKE OK movedRoot={movedGameRoot} workspace={migratedProject.WorkspaceRoot} hex={migratedProject.HexTableXmlPath} dict={sceneStringPath} sceneEditor={migratedProject.SceneEditorDirectory ?? "<missing>"} imageAssigner={migratedProject.ImageAssignerDirectory ?? "<missing>"} materialRoot={migratedProject.MaterialLibraryRoot ?? "<missing>"} patchRoot={migratedProject.PatchConfigRoot ?? "<missing>"} tables={migratedTables.Count} scenarios={scenarioIndex.Count} materials={materialAssets.Count}");
    }
    finally
    {
        try
        {
            if (Directory.Exists(migrationRoot)) Directory.Delete(migrationRoot, recursive: true);
        }
        catch
        {
            // The temp copy is non-authoritative test data; a later cleanup can remove it if Windows still has a handle open.
        }
    }
}

static void RunRsSmoke(CczProject project, IReadOnlyList<HexTableDefinition> tables)
{
    var rTable = tables.Single(t => t.TableName == "6.5-0-4 R形象");
    var sTable = tables.Single(t => t.TableName == "6.5-0-5 S形象");
    Console.WriteLine($"RS_TABLE R={rTable.FileName}:0x{rTable.DataPos:X} S={sTable.FileName}:0x{sTable.DataPos:X}");
    if (!rTable.FileName.Equals("Ekd5.exe", StringComparison.OrdinalIgnoreCase) ||
        !sTable.FileName.Equals("Ekd5.exe", StringComparison.OrdinalIgnoreCase) ||
        rTable.DataPos != 0xE1000 ||
        sTable.DataPos != 0xD2800)
    {
        throw new InvalidOperationException("人物 R/S 表偏移与 B形象指定器 6.5 System.ini 不一致。");
    }

    var stopwatch = System.Diagnostics.Stopwatch.StartNew();
    var scenarioIndex = new ScenarioFileReader().ReadAllIndex(project);
    stopwatch.Stop();
    var e5sCount = scenarioIndex.Count(x => x.FileName.EndsWith(".E5S", StringComparison.OrdinalIgnoreCase));
    var rCount = scenarioIndex.Count(x => x.FileName.StartsWith("R_", StringComparison.OrdinalIgnoreCase));
    var sCount = scenarioIndex.Count(x => x.FileName.StartsWith("S_", StringComparison.OrdinalIgnoreCase));
    Console.WriteLine($"RS_SCENARIO_INDEX count={scenarioIndex.Count} R={rCount} S={sCount} E5S={e5sCount} elapsedMs={stopwatch.ElapsedMilliseconds}");
    if (scenarioIndex.Count == 0 || rCount == 0 || sCount == 0 || e5sCount != 0 ||
        scenarioIndex.Any(x => !ScenarioFileReader.IsRsScriptFile(x.FileName)))
    {
        throw new InvalidOperationException("R/S eex 剧本索引结果不符合预期，不能混入 E5S。");
    }

    var sceneStringPath = ProjectDetector.FindSceneDictionaryPath(project);
    if (File.Exists(sceneStringPath))
    {
        var sceneDoc = new SceneStringParser().Parse(sceneStringPath);
        var firstScenario = scenarioIndex.First();
        var commandRows = new ScenarioCommandProbeReader().Probe(firstScenario.Path, sceneDoc, maxRows: 40);
        var textRows = new ScenarioTextReader().Read(firstScenario.Path, maxItems: 40);
        Console.WriteLine($"RS_SCENARIO_DETAIL file={firstScenario.FileName} commands={commandRows.Count} texts={textRows.Count} kind={firstScenario.Kind}");
        var scriptStructure = new ScenarioStructureProbeReader().Build(firstScenario.Path, sceneDoc, maxCommandRows: 600, project: project, tables: tables);
        var scriptTexts = new ScenarioTextReader().Read(firstScenario.Path).ToList();
        var legacyDocument = new LegacyScenarioReader().Read(firstScenario.Path, sceneDoc);
        if (legacyDocument.SceneCount == 0 ||
            legacyDocument.SectionCount == 0 ||
            legacyDocument.CommandCount == 0 ||
            !legacyDocument.EnumerateCommands().Any(command => command.StartsBodyBlock) ||
            legacyDocument.EnumerateCommands().Any(command => command.CommandId == 0x76 && command.OriginalJumpDisplacement == null))
        {
            throw new InvalidOperationException("Legacy R/S eex scenario tree read result is incomplete.");
        }
        Console.WriteLine($"LEGACY_SCENARIO_READ file={firstScenario.FileName} scenes={legacyDocument.SceneCount} sections={legacyDocument.SectionCount} commands={legacyDocument.CommandCount} jumps={legacyDocument.EnumerateCommands().Count(command => command.CommandId == 0x76)} texts={legacyDocument.EnumerateCommands().SelectMany(command => command.TextParameters).Count()}");
        var scriptTreeSummary = BuildScriptEditorTreeSummary(scriptStructure, scriptTexts);
        if (scriptTreeSummary.SectionTextGroupCount == 0 || scriptTreeSummary.AttachedTextNodeCount == 0)
        {
            throw new InvalidOperationException($"Script editor tree did not attach text clues at Section level: sectionGroups={scriptTreeSummary.SectionTextGroupCount}, attached={scriptTreeSummary.AttachedTextNodeCount}");
        }
        if (scriptTreeSummary.AttachedTextNodeCount > scriptTexts.Count)
        {
            throw new InvalidOperationException($"Script editor tree attached more text nodes than source texts: attached={scriptTreeSummary.AttachedTextNodeCount}, source={scriptTexts.Count}");
        }
        Console.WriteLine($"SCRIPT_TREE sectionTextGroups={scriptTreeSummary.SectionTextGroupCount} sceneFallbackGroups={scriptTreeSummary.SceneFallbackGroupCount} unassignedGroups={scriptTreeSummary.UnassignedGroupCount} attachedTexts={scriptTreeSummary.AttachedTextNodeCount}");

        var battlefieldScenarios = scenarioIndex
            .Where(scenario => ScenarioFileReader.IsBattlefieldScriptFile(scenario.FileName))
            .Take(2)
            .ToList();
        if (battlefieldScenarios.Count == 0)
        {
            throw new InvalidOperationException("R/S eex index did not include any S_XX battlefield scripts.");
        }

        var battlefieldService = new BattlefieldEditorService();
        var battlefieldDocs = battlefieldScenarios
            .Select(scenario => battlefieldService.Load(project, scenario, sceneDoc, tables, Array.Empty<ScenarioMapLinkInfo>()))
            .ToList();
        foreach (var battlefieldDoc in battlefieldDocs)
        {
            if (battlefieldDoc.CommandCandidates.Count == 0 || battlefieldDoc.UnitCandidates.Count == 0)
            {
                throw new InvalidOperationException($"Battlefield editor did not produce command/unit candidates for {battlefieldDoc.Scenario.FileName}.");
            }
        }

        var battlefieldResources = new GameResourceIndexer().Index(project);
        var battlefieldTerrainLookup = HexzmapProbeReader.BuildTerrainNameLookup(new MaterialLibraryIndexer().Index(project));
        var battlefieldHexzmap = File.Exists(project.ResolveGameFile("Hexzmap.e5"))
            ? new HexzmapProbeReader().Read(project, battlefieldTerrainLookup)
            : null;
        var battlefieldMapLinks = new ScenarioMapLinkService().BuildLinks(battlefieldScenarios, battlefieldResources, battlefieldHexzmap);
        var battlefieldDocsWithMaps = battlefieldScenarios
            .Select(scenario => battlefieldService.Load(project, scenario, sceneDoc, tables, battlefieldMapLinks))
            .ToList();

        if (battlefieldDocs.Count > 1 &&
            string.Equals(BuildBattlefieldCommandSignature(battlefieldDocs[0]), BuildBattlefieldCommandSignature(battlefieldDocs[1]), StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Battlefield command candidates did not change between {battlefieldDocs[0].Scenario.FileName} and {battlefieldDocs[1].Scenario.FileName}.");
        }

        if (battlefieldDocs.Count > 1 &&
            string.Equals(BuildBattlefieldUnitSignature(battlefieldDocs[0]), BuildBattlefieldUnitSignature(battlefieldDocs[1]), StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Battlefield unit candidates did not change between {battlefieldDocs[0].Scenario.FileName} and {battlefieldDocs[1].Scenario.FileName}.");
        }

        if (battlefieldDocsWithMaps.Count > 1 &&
            string.Equals(BuildBattlefieldMapSignature(battlefieldDocsWithMaps[0]), BuildBattlefieldMapSignature(battlefieldDocsWithMaps[1]), StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Battlefield map link preview did not change between {battlefieldDocsWithMaps[0].Scenario.FileName} and {battlefieldDocsWithMaps[1].Scenario.FileName}.");
        }

        var battlefieldCoordinateCandidate = battlefieldDocs[0].UnitCandidates.FirstOrDefault(candidate => BattlefieldEditorService.TryExtractFirstCoordinate(candidate, out _, out _))
                                            ?? battlefieldDocs[0].UnitCandidates.First();
        var battlefieldDeploymentCategories = battlefieldDocs
            .SelectMany(document => document.UnitCandidates)
            .Select(candidate => candidate.Category)
            .Where(category => category is "我军出场" or "友军出场" or "敌军出场")
            .Distinct(StringComparer.Ordinal)
            .OrderBy(category => category, StringComparer.Ordinal)
            .ToList();
        if (battlefieldDeploymentCategories.Count == 0)
        {
            throw new InvalidOperationException("Battlefield editor did not split any 0x46/0x47/0x4B deployment records.");
        }

        var allyDeploymentSlotScenario = battlefieldScenarios.Count > 1 ? battlefieldScenarios[1] : battlefieldScenarios[0];
        var allyDeploymentSlots = new BattlefieldAllyDeploymentSlotService().Load(
            allyDeploymentSlotScenario,
            sceneDoc,
            Array.Empty<BattlefieldUnitPaletteItem>());
        if (allyDeploymentSlots.Count == 0)
        {
            throw new InvalidOperationException($"Battlefield ally deployment slot overlay did not parse any 4B/4057 slots for {allyDeploymentSlotScenario.FileName}.");
        }

        var firstAllyDeploymentSlot = allyDeploymentSlots.OrderBy(slot => slot.Order).First();
        if (firstAllyDeploymentSlot.Order < 0 ||
            firstAllyDeploymentSlot.GridX < 0 ||
            firstAllyDeploymentSlot.GridY < 0)
        {
            throw new InvalidOperationException($"Battlefield ally deployment slot overlay parsed an invalid first slot for {allyDeploymentSlotScenario.FileName}: order={firstAllyDeploymentSlot.Order}, coord=({firstAllyDeploymentSlot.GridX},{firstAllyDeploymentSlot.GridY}).");
        }

        var battlefieldLegacyDocument = new LegacyScenarioReader().Read(battlefieldDocs[0].Scenario.Path, sceneDoc);
        var battlefieldLinkedCommand = FindLegacyBattlefieldCommand(battlefieldLegacyDocument, battlefieldCoordinateCandidate);
        if (battlefieldLinkedCommand == null)
        {
            throw new InvalidOperationException($"Battlefield candidate cannot be located in legacy script tree: {battlefieldDocs[0].Scenario.FileName} {battlefieldCoordinateCandidate.TargetKey}");
        }

        Console.WriteLine($"BATTLEFIELD_LEGACY_CANDIDATES first={battlefieldDocs[0].Scenario.FileName} commands={battlefieldDocs[0].CommandCandidates.Count} units={battlefieldDocs[0].UnitCandidates.Count} deployment={string.Join("/", battlefieldDeploymentCategories)} located={battlefieldLinkedCommand.CommandName}@0x{battlefieldLinkedCommand.FileOffset:X6} compare={(battlefieldDocs.Count > 1 ? battlefieldDocs[1].Scenario.FileName : "none")} map={BuildBattlefieldMapSignature(battlefieldDocsWithMaps[0])} allySlots={allyDeploymentSlotScenario.FileName}:{allyDeploymentSlots.Count} first=#{firstAllyDeploymentSlot.DisplayOrder}@({firstAllyDeploymentSlot.GridX},{firstAllyDeploymentSlot.GridY}) forced={allyDeploymentSlots.Count(slot => slot.IsForced)}");
    }
    else
    {
        Console.WriteLine("RS_SCENARIO_DETAIL skipped: CczString.ini not found");
    }

    var imageAssignments = new ImageAssignmentService().Load(project, tables);
    if (imageAssignments.Rows.Count == 0)
    {
        throw new InvalidOperationException("人物 R/S 形象联动表为空。");
    }

    var row0R = Convert.ToInt32(imageAssignments.Rows[0]["R形象编号"], CultureInfo.InvariantCulture);
    var row0S = Convert.ToInt32(imageAssignments.Rows[0]["S形象编号"], CultureInfo.InvariantCulture);
    var row0Job = imageAssignments.Columns.Contains("职业")
        ? Convert.ToInt32(imageAssignments.Rows[0]["职业"], CultureInfo.InvariantCulture)
        : 1;
    var row0Face = imageAssignments.Columns.Contains("头像编号")
        ? Convert.ToInt32(imageAssignments.Rows[0]["头像编号"], CultureInfo.InvariantCulture)
        : 0;
    var row0Name = Convert.ToString(imageAssignments.Rows[0]["名称"], CultureInfo.InvariantCulture) ?? string.Empty;
    var previewService = new ImageAssignmentPreviewService();
    var previewInfo = previewService.BuildResourceInfo(project, "S", row0S, row0Name, row0Face, row0Job, 1);
    AssertSMapping(0, 1, 1, 4);
    AssertSMapping(0, 1, 2, 5);
    AssertSMapping(0, 1, 3, 6);
    AssertSMapping(1, null, 1, 241, 242, 243);
    AssertSMapping(32, null, 1, 334, 335, 336);
    AssertSMapping(33, null, 1, 337);
    AssertSMapping(250, null, 1, 554);
    AssertSMapping(252, null, 1, 556);
    AssertSMapping(253, null, 1, 557);
    var e5ImageReplaceService = new E5ImageReplaceService();
    var unitMovPath = CharacterImageResourceService.ResolveGameFile(project, "Unit_mov.e5");
    var unitMovEntries = e5ImageReplaceService.ReadIndex(unitMovPath);
    if (unitMovEntries.Count < 556)
    {
        throw new InvalidOperationException($"Unit_mov.e5 0x110 图片索引表条目不足，预期至少 556，实际 {unitMovEntries.Count}。");
    }
    var e5ReplacePreview = e5ImageReplaceService.PreviewReplacementFromEntry(project, unitMovPath, 554, unitMovPath);
    if (e5ReplacePreview.ImageNumber != 554 ||
        e5ReplacePreview.OldSizeBytes <= 0 ||
        e5ReplacePreview.NewSizeBytes <= 0 ||
        e5ReplacePreview.IndexOffset <= 0)
    {
        throw new InvalidOperationException("E5 图片条目替换预览验证失败。");
    }
    Console.WriteLine($"E5_IMAGE_REPLACE_PREVIEW file={Path.GetFileName(unitMovPath)} entries={unitMovEntries.Count} image=554 kind={e5ReplacePreview.OldKind}->{e5ReplacePreview.NewKind} placement={e5ReplacePreview.Placement}");
    var indexedSPreviewRow = imageAssignments.Rows.Cast<DataRow>()
        .FirstOrDefault(row => Convert.ToInt32(row["S形象编号"], CultureInfo.InvariantCulture) == 250);
    var indexedSId = indexedSPreviewRow == null
        ? 250
        : Convert.ToInt32(indexedSPreviewRow["S形象编号"], CultureInfo.InvariantCulture);
    using (var rResourcePreview = previewService.TryRenderCharacterResourceImage(project, "R", row0R))
    using (var normalSResourcePreview = previewService.TryRenderCharacterResourceImage(project, "S", row0S, row0Job, 1))
    using (var indexedSResourcePreview = previewService.TryRenderCharacterResourceImage(project, "S", indexedSId, null, 1))
    using (var defaultSAllyPreview = previewService.TryRenderCharacterResourceImage(project, "S", 0, 1, 1))
    using (var defaultSFriendPreview = previewService.TryRenderCharacterResourceImage(project, "S", 0, 1, 2))
    using (var defaultSEnemyPreview = previewService.TryRenderCharacterResourceImage(project, "S", 0, 1, 3))
    using (var outOfRangeSPreview = previewService.TryRenderCharacterResourceImage(project, "S", 253, null, 1))
    {
        if (rResourcePreview == null)
        {
            throw new InvalidOperationException("R 形象应能从 Pmapobj.e5 的 0x110 索引表生成预览。");
        }

        if (row0S > 0 && normalSResourcePreview == null)
        {
            throw new InvalidOperationException($"S={row0S} 形象应能从 Unit_*.e5 的 0x110 索引表生成预览。");
        }

        if (indexedSResourcePreview == null)
        {
            throw new InvalidOperationException($"S={indexedSId} 形象应能从 Unit_*.e5 的 0x110 索引表生成预览。");
        }

        if (defaultSAllyPreview == null || defaultSFriendPreview == null || defaultSEnemyPreview == null)
        {
            throw new InvalidOperationException("S=0 默认兵种形象应能按职业=1 和我/友/敌阵营分别生成预览。");
        }

        if (outOfRangeSPreview != null)
        {
            throw new InvalidOperationException("S=253 按紧凑映射会指向 Unit图557，当前 Unit 索引表应严格越界而不是回退旧直读。");
        }
    }
    using (var battlefieldStageLow = previewService.TryRenderBattlefieldMoveIdleFrame(project, 1, null, 1, "下", 0, "初级", out var battlefieldStageLowDetail))
    using (var battlefieldStageMid = previewService.TryRenderBattlefieldMoveIdleFrame(project, 1, null, 1, "下", 0, "中级", out var battlefieldStageMidDetail))
    using (var battlefieldStageHigh = previewService.TryRenderBattlefieldMoveIdleFrame(project, 1, null, 1, "下", 0, "高级", out var battlefieldStageHighDetail))
    using (var battlefieldDownIdle = previewService.TryRenderBattlefieldMoveIdleFrame(project, indexedSId, null, 1, "下", 0, out var battlefieldDownDetail))
    using (var battlefieldLeftIdle = previewService.TryRenderBattlefieldMoveIdleFrame(project, indexedSId, null, 1, "左", 1, out var battlefieldLeftDetail))
    using (var battlefieldRightIdle = previewService.TryRenderBattlefieldMoveIdleFrame(project, indexedSId, null, 1, "右", 1, out var battlefieldRightDetail))
    {
        if (battlefieldStageLow == null || battlefieldStageMid == null || battlefieldStageHigh == null)
        {
            throw new InvalidOperationException($"三转战场待机帧生成失败：low={battlefieldStageLowDetail} mid={battlefieldStageMidDetail} high={battlefieldStageHighDetail}");
        }

        if (!battlefieldStageLowDetail.Contains("#241", StringComparison.Ordinal) ||
            !battlefieldStageMidDetail.Contains("#242", StringComparison.Ordinal) ||
            !battlefieldStageHighDetail.Contains("#243", StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"三转 S 形象没有按初/中/高级选择 Unit 图：low={battlefieldStageLowDetail} mid={battlefieldStageMidDetail} high={battlefieldStageHighDetail}");
        }

        if (battlefieldDownIdle == null || battlefieldLeftIdle == null || battlefieldRightIdle == null)
        {
            throw new InvalidOperationException($"战场 Unit_mov.e5 待机帧生成失败：down={battlefieldDownDetail} left={battlefieldLeftDetail} right={battlefieldRightDetail}");
        }

        if (battlefieldDownIdle.Size != new Size(48, 48) ||
            battlefieldLeftIdle.Size != new Size(48, 48) ||
            battlefieldRightIdle.Size != new Size(48, 48))
        {
            throw new InvalidOperationException($"战场 Unit_mov.e5 待机帧尺寸错误：down={battlefieldDownIdle.Size} left={battlefieldLeftIdle.Size} right={battlefieldRightIdle.Size}");
        }

        var transparentPixels = CountTransparentPixels(battlefieldDownIdle);
        if (transparentPixels < 64)
        {
            throw new InvalidOperationException($"战场 Unit_mov.e5 待机帧透明背景不足：transparent={transparentPixels}");
        }

        if (!AreHorizontalMirrors(battlefieldLeftIdle, battlefieldRightIdle))
        {
            throw new InvalidOperationException("战场 Unit_mov.e5 右向待机帧没有按左向帧水平翻转。");
        }

        Console.WriteLine($"BATTLEFIELD_IDLE_PREVIEW S={indexedSId} down={battlefieldDownIdle.Width}x{battlefieldDownIdle.Height} transparent={transparentPixels} detail={battlefieldDownDetail} stage={battlefieldStageLowDetail}|{battlefieldStageMidDetail}|{battlefieldStageHighDetail}");
    }
    var legacyCompressedGameRoot = Path.Combine(project.WorkspaceRoot, "基底", "三国之召唤猛将6.4（60关版）基底");
    if (File.Exists(Path.Combine(legacyCompressedGameRoot, "Unit_mov.e5")))
    {
        var legacyCompressedProject = new ProjectDetector().CreateProjectFromGameRoot(legacyCompressedGameRoot);
        var legacyCompressedEntries = e5ImageReplaceService.ReadIndex(CharacterImageResourceService.ResolveGameFile(legacyCompressedProject, "Unit_mov.e5"));
        if (legacyCompressedEntries.Count < 72 || !legacyCompressedEntries[63].IsCompressed)
        {
            throw new InvalidOperationException($"Legacy compressed Unit_mov.e5 index should include compressed entry #64; entries={legacyCompressedEntries.Count} compressed64={legacyCompressedEntries.ElementAtOrDefault(63)?.IsCompressed}");
        }

        using var legacyCompressedPreview = previewService.TryRenderCharacterResourceImage(legacyCompressedProject, "S", 0, 21, 1);
        if (legacyCompressedPreview == null)
        {
            throw new InvalidOperationException("Legacy compressed Unit_mov.e5 entry #64 should render after LS12 decode.");
        }

        var legacyCompressedColorPixels = CountColorfulPixels(legacyCompressedPreview);
        if (legacyCompressedColorPixels < 48)
        {
            throw new InvalidOperationException($"Legacy compressed Unit preview is still blank or grayscale. colorPixels={legacyCompressedColorPixels}");
        }

        Console.WriteLine($"RS_LEGACY_COMPRESSED_PREVIEW game={Path.GetFileName(legacyCompressedGameRoot)} colorPixels={legacyCompressedColorPixels}");
    }
    var previewPng = Path.Combine(project.WorkspaceRoot, "CCZModStudio_Exports", $"Smoke_RS_R00_FacePreview_{Guid.NewGuid():N}.png");
    Directory.CreateDirectory(Path.GetDirectoryName(previewPng)!);
    using (var preview = previewService.RenderResourcePreview(project, "R", row0R, row0Name, row0Face))
    {
        preview.Save(previewPng, System.Drawing.Imaging.ImageFormat.Png);
    }
    var indexedPreviewPng = Path.Combine(project.WorkspaceRoot, "CCZModStudio_Exports", $"Smoke_RS_S{indexedSId}_UnitPreview_{Guid.NewGuid():N}.png");
    var indexedPreviewColorPixels = 0;
    using (var indexedPreview = previewService.TryRenderCharacterResourceImage(project, "S", indexedSId, null, 1))
    {
        if (indexedPreview == null)
        {
            throw new InvalidOperationException($"S={indexedSId} Unit 索引预览二次生成失败。");
        }

        indexedPreviewColorPixels = CountColorfulPixels(indexedPreview);
        if (indexedPreviewColorPixels < 48)
        {
            throw new InvalidOperationException($"S={indexedSId} Unit 索引预览仍接近灰度，可能没有套用 tsb 调色板。colorPixels={indexedPreviewColorPixels}");
        }

        indexedPreview.Save(indexedPreviewPng, System.Drawing.Imaging.ImageFormat.Png);
    }
    Console.WriteLine($"RS_IMAGE_ASSIGN rows={imageAssignments.Rows.Count} row0={row0Name} face={row0Face} job={row0Job} R={row0R} S={row0S}");
    var hexzmapProbe = new HexzmapProbeReader().Read(project);
    if (hexzmapProbe.DirectoryEntries.Count == 0)
    {
        throw new InvalidOperationException("Hexzmap 目录候选探针没有读取到任何目录项。");
    }
    var hexzmapDirectoryHit = hexzmapProbe.DirectoryEntries.FirstOrDefault(entry =>
        entry.CandidateMapIdA.Contains("M000", StringComparison.OrdinalIgnoreCase) ||
        entry.CandidateMapIdB.Contains("M000", StringComparison.OrdinalIgnoreCase) ||
        entry.CandidateMapIdC.Contains("M000", StringComparison.OrdinalIgnoreCase));
    if (hexzmapDirectoryHit == null)
    {
        throw new InvalidOperationException("Hexzmap 目录候选探针没有发现与真实地图格数相匹配的候选项。");
    }
    Console.WriteLine($"HEXZMAP_DIRECTORY entries={hexzmapProbe.DirectoryEntries.Count} firstHitOff=0x{hexzmapDirectoryHit.EntryOffset:X} segment={hexzmapDirectoryHit.SegmentLength} fileOff=0x{hexzmapDirectoryHit.FileOffset:X} next={hexzmapDirectoryHit.NextSegmentLength}");
    if (!previewInfo.Contains("FileHead=D2800", StringComparison.Ordinal) ||
        !previewInfo.Contains("RFileHead=E1000", StringComparison.Ordinal) ||
        !previewInfo.Contains("Ekd5.exe", StringComparison.OrdinalIgnoreCase) ||
        !previewInfo.Contains("Face.e5", StringComparison.OrdinalIgnoreCase) ||
        !File.Exists(previewPng) ||
        new FileInfo(previewPng).Length == 0 ||
        !File.Exists(indexedPreviewPng) ||
        new FileInfo(indexedPreviewPng).Length == 0)
    {
        throw new InvalidOperationException("人物形象预览未读取到 B形象指定器 6.5 的 FileHead/RFileHead 配置或 Face.e5 头像来源。");
    }
    var indexedMapping = CharacterImageResourceService.ResolveSUnitImageMapping(indexedSId);
    Console.WriteLine($"RS_IMAGE_PREVIEW png={Path.GetFileName(previewPng)} indexed={Path.GetFileName(indexedPreviewPng)} face={row0Face} S={indexedSId} mapped={string.Join("/", indexedMapping.ImageNumbers)} colorPixels={indexedPreviewColorPixels}");

    var itemTypeCatalogChecks = new[]
    {
        (TypeId: 8, Expected: "普通弩系", MajorCategory: "武器", Catalog: 0),
        (TypeId: 10, Expected: "普通锤系", MajorCategory: "武器", Catalog: 0),
        (TypeId: 12, Expected: "普通斧系", MajorCategory: "武器", Catalog: 0),
        (TypeId: 58, Expected: "四神宝玉/铜雀", MajorCategory: "辅助/道具", Catalog: 1)
    };
    foreach (var check in itemTypeCatalogChecks)
    {
        var description = ItemTypeCatalogService.BuildDescription(check.TypeId, check.MajorCategory, check.Catalog);
        if (!description.Contains(check.Expected, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"宝物类型目录解释不符合预期：type={check.TypeId}, expected={check.Expected}, actual={description}");
        }
    }
    Console.WriteLine("ITEM_TYPE_CATALOG type8=普通弩系 type10=普通锤系 type12=普通斧系 type58=四神宝玉/铜雀");

    var itemTable = tables.Single(t => t.TableName == "6.5-1 物品（0-103）");
    var itemRead = new HexTableReader().Read(project, itemTable, tables);
    if (!itemRead.Validation.IsUsable || itemRead.Data.Rows.Count == 0)
    {
        throw new InvalidOperationException("物品图标预览烟测无法读取 6.5-1 物品（0-103）。");
    }

    var itemIconIndex = Convert.ToInt32(itemRead.Data.Rows[0]["图标"], CultureInfo.InvariantCulture);
    var itemName = Convert.ToString(itemRead.Data.Rows[0]["名称"], CultureInfo.InvariantCulture) ?? string.Empty;
    var itemIconPreviewService = new ItemIconPreviewService();
    var itemIconPreview = itemIconPreviewService.BuildPreview(project, itemIconIndex);
    var itemIconPng = Path.Combine(project.WorkspaceRoot, "CCZModStudio_Exports", $"Smoke_ItemIcon_{Guid.NewGuid():N}.png");
    Directory.CreateDirectory(Path.GetDirectoryName(itemIconPng)!);
    try
    {
        itemIconPreview.Bitmap?.Save(itemIconPng, System.Drawing.Imaging.ImageFormat.Png);
    }
    finally
    {
        itemIconPreview.Bitmap?.Dispose();
    }

    if (!File.Exists(itemIconPreview.SourcePath) ||
        itemIconPreview.AvailableIconCount <= itemIconIndex ||
        !File.Exists(itemIconPng) ||
        new FileInfo(itemIconPng).Length == 0 ||
        !itemIconPreview.Message.Contains("Itemicon.dll", StringComparison.OrdinalIgnoreCase))
    {
        throw new InvalidOperationException("物品图标预览未能从 Itemicon.dll 提取有效候选图标。");
    }

    Console.WriteLine($"ITEM_ICON_PREVIEW item={itemName} icon={itemIconIndex} count={itemIconPreview.AvailableIconCount} png={Path.GetFileName(itemIconPng)}");

    var accessoryTable = tables.Single(t => t.TableName == "6.5-2 物品（104-255）");
    var accessoryRead = new HexTableReader().Read(project, accessoryTable, tables);
    var accessoryRow = accessoryRead.Data.Rows.Cast<DataRow>()
        .FirstOrDefault(row => Convert.ToInt32(row["ID"], CultureInfo.InvariantCulture) >= 109 &&
                               Convert.ToInt32(row["装备特效号"], CultureInfo.InvariantCulture) == 2)
        ?? throw new InvalidOperationException("未找到可用于辅助装备字段校正烟测的辅助装备行。");
    var accessoryTypeId = Convert.ToInt32(accessoryRow["类型"], CultureInfo.InvariantCulture);
    var accessoryEffectId = Convert.ToInt32(accessoryRow["装备特效号"], CultureInfo.InvariantCulture);
    var effectiveEffectId = ItemEffectInterpretationService.ResolveEffectiveEffectId("辅助/道具", accessoryTypeId, accessoryEffectId);
    var effectiveEffectIdText = ItemEffectInterpretationService.BuildEffectiveEffectIdText("辅助/道具", accessoryTypeId, accessoryEffectId);
    var effectiveEffectDescription = ItemEffectInterpretationService.BuildEffectiveEffectDescription("辅助/道具", accessoryTypeId, accessoryEffectId, effectiveEffectId, _ => string.Empty);
    if (effectiveEffectId != accessoryTypeId ||
        !effectiveEffectIdText.Contains($"类型={accessoryTypeId}", StringComparison.Ordinal) ||
        !effectiveEffectDescription.Contains("类别标记", StringComparison.Ordinal))
    {
        throw new InvalidOperationException($"辅助装备效果字段校正不符合预期：type={accessoryTypeId}, effect={accessoryEffectId}, effective={effectiveEffectId}, text={effectiveEffectIdText}, desc={effectiveEffectDescription}");
    }
    Console.WriteLine($"ITEM_ACCESSORY_EFFECT_MODEL id={Convert.ToInt32(accessoryRow["ID"], CultureInfo.InvariantCulture)} type={accessoryTypeId} rawEffect={accessoryEffectId} effective={effectiveEffectId}");

    Console.WriteLine("RS_SMOKE OK");
}

static int ExtractShopSlotNumber(string columnName)
{
    var text = columnName;
    if (text.StartsWith("\u88c5\u5907", StringComparison.Ordinal)) text = text[2..];
    if (text.StartsWith("\u9053\u5177", StringComparison.Ordinal)) text = text[2..];
    return int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var slot) ? slot : -1;
}

static void RunShopEditorSmoke(CczProject project, IReadOnlyList<HexTableDefinition> tables)
{
    var buildShopEditorData = typeof(MainForm).GetMethod("BuildShopEditorData", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
        ?? throw new MissingMethodException("MainForm.BuildShopEditorData");
    var data = buildShopEditorData.Invoke(smokeForm, new object[] { project, tables }) as DataTable
        ?? throw new InvalidOperationException("商店编辑聚合数据构建失败。");

    var shopTable = tables.Single(t => t.TableName == "6.5-8-1 商店数据");
    var campaignNameTable = tables.Single(t => t.TableName == "6.5-8 战役名称");
    if (data.Rows.Count != shopTable.RowCount)
    {
        throw new InvalidOperationException($"商店编辑行数不正确：actual={data.Rows.Count}, expected={shopTable.RowCount}");
    }

    foreach (var columnName in new[] { "ID", "槽位类型", "关卡名称", "开关仓库人物", "开关仓库人物名", "买卖物品人物", "买卖物品人物名", "装备1", "道具17", "装备摘要", "道具摘要" })
    {
        if (!data.Columns.Contains(columnName))
        {
            throw new InvalidOperationException($"商店编辑缺少列：{columnName}");
        }
    }

    var normalRows = data.Rows.Cast<DataRow>()
        .Count(row => string.Equals(Convert.ToString(row["槽位类型"], CultureInfo.InvariantCulture), "普通关卡", StringComparison.Ordinal));
    if (normalRows != campaignNameTable.RowCount)
    {
        throw new InvalidOperationException($"普通关卡行数不正确：actual={normalRows}, expected={campaignNameTable.RowCount}");
    }

    var first = data.Rows[0];
    var firstName = Convert.ToString(first["关卡名称"], CultureInfo.InvariantCulture) ?? string.Empty;
    var firstWarehousePreview = Convert.ToString(first["开关仓库人物名"], CultureInfo.InvariantCulture) ?? string.Empty;
    var firstBuySellPreview = Convert.ToString(first["买卖物品人物名"], CultureInfo.InvariantCulture) ?? string.Empty;
    if (string.IsNullOrWhiteSpace(firstWarehousePreview) || string.IsNullOrWhiteSpace(firstBuySellPreview))
    {
        throw new InvalidOperationException("商店编辑人物预览列为空。");
    }

    var itemSlotValues = data.Rows.Cast<DataRow>()
        .SelectMany(row => data.Columns.Cast<DataColumn>()
            .Where(column => column.ColumnName.StartsWith("装备", StringComparison.Ordinal) || column.ColumnName.StartsWith("道具", StringComparison.Ordinal))
            .Select(column => Convert.ToString(row[column], CultureInfo.InvariantCulture) ?? string.Empty))
        .Where(value => value.Length > 0)
        .ToList();
    if (itemSlotValues.Count == 0)
    {
        throw new InvalidOperationException("商店编辑没有读取到任何物品槽值。");
    }

    var firstItemId = itemSlotValues
        .Select(value => int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) ? parsed : 255)
        .FirstOrDefault(id => id != 255);
    if (firstItemId == 0)
    {
        firstItemId = 1;
    }

    var buildShopItemLookupTable = typeof(MainForm).GetMethod("BuildShopItemLookupTable", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
        ?? throw new MissingMethodException("MainForm.BuildShopItemLookupTable");
    var itemLookup = buildShopItemLookupTable.Invoke(smokeForm, new object[] { true }) as DataTable
        ?? throw new InvalidOperationException("Shop item lookup table was not built.");
    var mappedItemDisplay = itemLookup.Rows.Cast<DataRow>()
        .Select(row => Convert.ToString(row["\u663e\u793a"], CultureInfo.InvariantCulture) ?? string.Empty)
        .FirstOrDefault(text => text.Contains("\uFF5C", StringComparison.Ordinal) && !text.StartsWith("255 ", StringComparison.Ordinal))
        ?? string.Empty;
    if (string.IsNullOrWhiteSpace(mappedItemDisplay) || int.TryParse(mappedItemDisplay, NumberStyles.Integer, CultureInfo.InvariantCulture, out _))
    {
        throw new InvalidOperationException("Shop item display is still numeric-only; expected Chinese name/category/type text.");
    }

    var buildShopItemDetailText = typeof(MainForm).GetMethod("BuildShopItemDetailText", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
        ?? throw new MissingMethodException("MainForm.BuildShopItemDetailText");
    var itemDetail = Convert.ToString(buildShopItemDetailText.Invoke(smokeForm, new object[] { firstItemId }), CultureInfo.InvariantCulture) ?? string.Empty;
    foreach (var marker in new[] { "\u7269\u54c1\u9884\u89c8", "\u5927\u7c7b", "\u7c7b\u578b", "\u4ef7\u683c\u5b57\u6bb5", "\u7279\u6548", "\u7269\u54c1\u8bf4\u660e" })
    {
        if (!itemDetail.Contains(marker, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Shop item detail is missing creator-facing mapping text: " + marker);
        }
    }

    var currentShopEditorDataField = typeof(MainForm).GetField("_currentShopEditorData", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
        ?? throw new MissingFieldException("MainForm", "_currentShopEditorData");
    var shopEditorGridField = typeof(MainForm).GetField("_shopEditorGrid", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
        ?? throw new MissingFieldException("MainForm", "_shopEditorGrid");
    var shopBatchScopeComboField = typeof(MainForm).GetField("_shopBatchScopeCombo", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
        ?? throw new MissingFieldException("MainForm", "_shopBatchScopeCombo");
    var shopBatchSlotComboField = typeof(MainForm).GetField("_shopBatchSlotCombo", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
        ?? throw new MissingFieldException("MainForm", "_shopBatchSlotCombo");
    var shopBatchSetItemComboField = typeof(MainForm).GetField("_shopBatchSetItemCombo", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
        ?? throw new MissingFieldException("MainForm", "_shopBatchSetItemCombo");
    var shopBatchFindItemComboField = typeof(MainForm).GetField("_shopBatchFindItemCombo", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
        ?? throw new MissingFieldException("MainForm", "_shopBatchFindItemCombo");
    var shopBatchReplaceItemComboField = typeof(MainForm).GetField("_shopBatchReplaceItemCombo", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
        ?? throw new MissingFieldException("MainForm", "_shopBatchReplaceItemCombo");
    var configureShopEditorGrid = typeof(MainForm).GetMethod("ConfigureShopEditorGrid", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
        ?? throw new MissingMethodException("MainForm.ConfigureShopEditorGrid");
    var getShopBatchTargetColumns = typeof(MainForm).GetMethod("GetShopBatchTargetColumns", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
        ?? throw new MissingMethodException("MainForm.GetShopBatchTargetColumns");
    var applyShopBatchSet = typeof(MainForm).GetMethod("ApplyShopBatchSet", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
        ?? throw new MissingMethodException("MainForm.ApplyShopBatchSet");
    var applyShopBatchClear = typeof(MainForm).GetMethod("ApplyShopBatchClear", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
        ?? throw new MissingMethodException("MainForm.ApplyShopBatchClear");
    var applyShopBatchReplace = typeof(MainForm).GetMethod("ApplyShopBatchReplace", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
        ?? throw new MissingMethodException("MainForm.ApplyShopBatchReplace");

    currentShopEditorDataField.SetValue(smokeForm, data);
    var shopEditorGrid = shopEditorGridField.GetValue(smokeForm) as DataGridView
        ?? throw new InvalidOperationException("Unable to read shop editor grid.");
    shopEditorGrid.DataSource = data;
    configureShopEditorGrid.Invoke(smokeForm, Array.Empty<object>());
    if (shopEditorGrid.Columns["\u88c5\u59071"] is not DataGridViewComboBoxColumn equipmentColumn ||
        shopEditorGrid.Columns["\u9053\u517717"] is not DataGridViewComboBoxColumn itemColumn ||
        equipmentColumn.DisplayMember != "\u663e\u793a" ||
        equipmentColumn.ValueMember != "ID" ||
        itemColumn.DisplayMember != "\u663e\u793a" ||
        itemColumn.ValueMember != "ID")
    {
        throw new InvalidOperationException("Shop item slots were not converted to Chinese mapped dropdown columns.");
    }

    var scopeCombo = shopBatchScopeComboField.GetValue(smokeForm) as ComboBox
        ?? throw new InvalidOperationException("Unable to read shop batch scope combo.");
    var slotCombo = shopBatchSlotComboField.GetValue(smokeForm) as ComboBox
        ?? throw new InvalidOperationException("Unable to read shop batch slot combo.");
    var setItemCombo = shopBatchSetItemComboField.GetValue(smokeForm) as ComboBox
        ?? throw new InvalidOperationException("Unable to read shop batch set combo.");
    var findItemCombo = shopBatchFindItemComboField.GetValue(smokeForm) as ComboBox
        ?? throw new InvalidOperationException("Unable to read shop batch find combo.");
    var replaceItemCombo = shopBatchReplaceItemComboField.GetValue(smokeForm) as ComboBox
        ?? throw new InvalidOperationException("Unable to read shop batch replace combo.");

    var batchSlotDisplays = ((DataTable?)slotCombo.DataSource)?.Rows.Cast<DataRow>()
        .Select(row => Convert.ToString(row["\u663e\u793a"], CultureInfo.InvariantCulture) ?? string.Empty)
        .ToList() ?? new List<string>();
    if (!batchSlotDisplays.Contains("\u88c5\u59071", StringComparer.Ordinal) ||
        !batchSlotDisplays.Contains("\u9053\u517717", StringComparer.Ordinal) ||
        batchSlotDisplays.Contains("2", StringComparer.Ordinal))
    {
        throw new InvalidOperationException("Shop batch slot dropdown is missing Chinese equipment/item slot labels.");
    }

    scopeCombo.SelectedItem = "\u5f53\u524d\u7b5b\u9009\u884c";
    data.DefaultView.RowFilter = "ID = 0";
    slotCombo.SelectedValue = "\u88c5\u59071-16";
    var equipmentBatchColumns = ((IEnumerable<string>?)getShopBatchTargetColumns.Invoke(smokeForm, Array.Empty<object>()))?.ToList() ?? new List<string>();
    if (equipmentBatchColumns.Count != 16 || equipmentBatchColumns.Select(ExtractShopSlotNumber).Order().SequenceEqual(Enumerable.Range(1, 16)) == false)
    {
        throw new InvalidOperationException(
            "Shop batch equipment 1-16 range is incorrect. selected=" +
            Convert.ToString(slotCombo.SelectedValue, CultureInfo.InvariantCulture) +
            " columns=" + string.Join(",", equipmentBatchColumns));
    }

    setItemCombo.SelectedValue = firstItemId;
    applyShopBatchSet.Invoke(smokeForm, Array.Empty<object>());
    var batchRow = data.Rows[0];
    if (equipmentBatchColumns.Any(columnName => Convert.ToInt32(batchRow[columnName], CultureInfo.InvariantCulture) != firstItemId))
    {
        throw new InvalidOperationException("Shop batch set did not update equipment 1-16.");
    }

    findItemCombo.SelectedValue = firstItemId;
    replaceItemCombo.SelectedValue = 255;
    applyShopBatchReplace.Invoke(smokeForm, Array.Empty<object>());
    if (equipmentBatchColumns.Any(columnName => Convert.ToInt32(batchRow[columnName], CultureInfo.InvariantCulture) != 255))
    {
        throw new InvalidOperationException("Shop batch replace did not update equipment 1-16.");
    }

    slotCombo.SelectedValue = "\u9053\u517717-32";
    var itemBatchColumns = ((IEnumerable<string>?)getShopBatchTargetColumns.Invoke(smokeForm, Array.Empty<object>()))?.ToList() ?? new List<string>();
    if (itemBatchColumns.Count != 16 || itemBatchColumns.Select(ExtractShopSlotNumber).Order().SequenceEqual(Enumerable.Range(17, 16)) == false)
    {
        throw new InvalidOperationException("Shop batch item 17-32 range is incorrect.");
    }

    setItemCombo.SelectedValue = firstItemId;
    applyShopBatchSet.Invoke(smokeForm, Array.Empty<object>());
    applyShopBatchClear.Invoke(smokeForm, Array.Empty<object>());
    if (itemBatchColumns.Any(columnName => Convert.ToInt32(batchRow[columnName], CultureInfo.InvariantCulture) != 255))
    {
        throw new InvalidOperationException("Shop batch clear did not update item 17-32.");
    }
    data.DefaultView.RowFilter = string.Empty;

    Console.WriteLine($"SHOP_EDITOR_SMOKE rows={data.Rows.Count} normal={normalRows} firstName={firstName} warehouse={firstWarehousePreview} buySell={firstBuySellPreview} itemSlots={itemSlotValues.Count} mapped={mappedItemDisplay} detailId={firstItemId} batchEquip={equipmentBatchColumns.Count} batchItem={itemBatchColumns.Count}");
    Console.WriteLine("SHOP_EDITOR_SMOKE OK");
}

static void RunLegacyScenarioDepthSmoke(CczProject project)
{
    var sceneStringPath = ProjectDetector.FindSceneDictionaryPath(project);
    if (!File.Exists(sceneStringPath))
    {
        throw new FileNotFoundException("Legacy scenario depth smoke requires CczString.ini.", sceneStringPath);
    }

    var sceneDoc = new SceneStringParser().Parse(sceneStringPath);
    var scenarios = new ScenarioFileReader()
        .ReadAllIndex(project)
        .Where(scenario => ScenarioFileReader.IsRsScriptFile(scenario.FileName))
        .ToList();
    if (scenarios.Count == 0)
    {
        throw new InvalidOperationException("Legacy scenario depth smoke found no R/S eex files.");
    }

    var reader = new LegacyScenarioReader();
    var readable = 0;
    var failed = 0;
    long totalCommands = 0;
    var maxDepth = 0;
    var maxDepthFile = string.Empty;
    foreach (var scenario in scenarios)
    {
        try
        {
            var document = reader.Read(scenario.Path, sceneDoc);
            var analysis = AnalyzeLegacyScenarioDepth(document);
            readable++;
            totalCommands += analysis.CommandCount;
            if (analysis.MaxDepth > maxDepth)
            {
                maxDepth = analysis.MaxDepth;
                maxDepthFile = scenario.FileName;
            }

            if (analysis.MaxDepth > 64)
            {
                Console.WriteLine($"LEGACY_SCENARIO_DEPTH_FOLD file={scenario.FileName} commands={analysis.CommandCount} maxDepth={analysis.MaxDepth}");
            }
        }
        catch (InvalidDataException ex)
        {
            failed++;
            Console.WriteLine($"LEGACY_SCENARIO_DEPTH_FALLBACK file={scenario.FileName} reason={ex.Message}");
        }
    }

    if (readable == 0)
    {
        throw new InvalidOperationException("Legacy scenario depth smoke could not read any R/S eex file with the legacy parser.");
    }

    Console.WriteLine($"LEGACY_SCENARIO_DEPTH_OK files={readable}/{scenarios.Count} failed={failed} commands={totalCommands} maxDepth={maxDepth} maxFile={maxDepthFile} uiFoldDepth=64");
}

static (int CommandCount, int MaxDepth) AnalyzeLegacyScenarioDepth(LegacyScenarioDocument document)
{
    var commandCount = 0;
    var maxDepth = 0;
    foreach (var scene in document.Scenes)
    {
        foreach (var section in scene.Sections)
        {
            var activeBlocks = new HashSet<LegacyScenarioCommandBlock>();
            var stack = new Stack<(IReadOnlyList<LegacyScenarioCommandNode> Commands, int Index, int Depth, LegacyScenarioCommandBlock? Owner)>();
            stack.Push((section.Commands, 0, 0, null));

            while (stack.Count > 0)
            {
                var frame = stack.Pop();
                if (frame.Index >= frame.Commands.Count)
                {
                    if (frame.Owner != null)
                    {
                        activeBlocks.Remove(frame.Owner);
                    }
                    continue;
                }

                var command = frame.Commands[frame.Index];
                frame.Index++;
                stack.Push(frame);

                commandCount++;
                maxDepth = Math.Max(maxDepth, frame.Depth);

                var childBlock = command.ChildBlock;
                if (childBlock != null && childBlock.Commands.Count > 0 && activeBlocks.Add(childBlock))
                {
                    stack.Push((childBlock.Commands, 0, frame.Depth + 1, childBlock));
                }
            }
        }
    }

    return (commandCount, maxDepth);
}

static void RunLegacyScriptEditSmoke(CczProject project)
{
    var smokeRoot = Path.Combine(project.WorkspaceRoot, "CCZModStudio_TestCopies", "LegacyScriptEditSmoke_" + DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture));
    Directory.CreateDirectory(smokeRoot);
    foreach (var coreFile in new[] { "Ekd5.exe", "Data.e5", "Star.e5", "Imsg.e5" })
    {
        var source = Path.Combine(project.GameRoot, coreFile);
        if (File.Exists(source))
        {
            File.Copy(source, Path.Combine(smokeRoot, coreFile), overwrite: false);
        }
    }

    var rsRoot = Path.Combine(smokeRoot, "RS");
    Directory.CreateDirectory(rsRoot);
    var sourceScenarioPath = Path.Combine(project.GameRoot, "RS", "R_00.eex");
    if (!File.Exists(sourceScenarioPath))
    {
        sourceScenarioPath = Directory.GetFiles(Path.Combine(project.GameRoot, "RS"), "R_*.eex", SearchOption.TopDirectoryOnly)
            .OrderBy(x => x, StringComparer.CurrentCultureIgnoreCase)
            .FirstOrDefault()
            ?? throw new FileNotFoundException("剧本结构编辑烟测找不到 R_*.eex。", Path.Combine(project.GameRoot, "RS", "R_*.eex"));
    }

    var scenarioFileName = Path.GetFileName(sourceScenarioPath);
    var testScenarioPath = Path.Combine(rsRoot, scenarioFileName);
    File.Copy(sourceScenarioPath, testScenarioPath, overwrite: false);
    File.WriteAllText(Path.Combine(smokeRoot, "_CCZModStudio_TestCopy.txt"),
        $"CreatedAt={DateTime.Now:yyyy-MM-dd HH:mm:ss}\r\nSource={project.GameRoot}\r\nPurpose=Legacy script edit smoke\r\n");

    var sceneStringPath = ProjectDetector.FindSceneDictionaryPath(project);
    if (!File.Exists(sceneStringPath))
    {
        throw new FileNotFoundException("剧本结构编辑烟测需要 CczString.ini。", sceneStringPath);
    }

    var testProject = new ProjectDetector().CreateProjectFromGameRoot(smokeRoot);
    var dictionary = new SceneStringParser().Parse(sceneStringPath);
    var writer = new LegacyScenarioWriter();
    var reader = new LegacyScenarioReader();
    var document = reader.Read(testScenarioPath, dictionary);
    var originalCommandCount = document.CommandCount;

    var targetSection = document.Scenes
        .SelectMany(scene => scene.Sections)
        .Where(section => section.DeclaredLength < 65000)
        .FirstOrDefault(section => section.Commands.Any(command => command.StartsBodyBlock && command.ChildBlock != null))
        ?? throw new InvalidOperationException("剧本结构编辑烟测找不到可追加普通命令的正文 Section。");
    var bodyRoot = targetSection.Commands.First(command => command.StartsBodyBlock && command.ChildBlock != null);
    var bodyCommands = bodyRoot.ChildBlock!.Commands;
    var insertedCommandId = 0x09;
    var insertedCommandName = dictionary.Commands.FirstOrDefault(command => command.Id == insertedCommandId)?.Name ?? $"Command 0x{insertedCommandId:X2}";
    var inserted = new LegacyScenarioCommandNode
    {
        SceneIndex = targetSection.SceneIndex,
        SectionIndex = targetSection.SectionIndex,
        CommandId = insertedCommandId,
        CommandName = insertedCommandName,
        FileOffset = 0,
        ConsumedBytes = 0
    };
    inserted.Parameters.Add(new LegacyScenarioCommandParameter
    {
        Index = 0,
        LayoutCode = 0x04,
        Tag = 0x04,
        FileOffset = 0,
        Kind = LegacyScenarioParameterKind.Dword32,
        ByteLength = 4,
        IntValue = 0
    });

    var insertIndex = GetLegacyScriptEditAppendIndex(bodyCommands);
    var jumpTargets = CaptureLegacyScriptEditJumpTargets(document);
    bodyCommands.Insert(insertIndex, inserted);
    ReindexLegacyScriptEditDocument(document);
    RestoreLegacyScriptEditJumpTargets(document, jumpTargets);

    var addSave = writer.Save(
        testProject,
        Path.Combine("RS", scenarioFileName),
        document,
        dictionary,
        "Legacy script edit smoke add command");
    var addVerify = reader.Read(testScenarioPath, dictionary);
    if (addVerify.CommandCount != originalCommandCount + 1 ||
        string.IsNullOrWhiteSpace(addSave.BackupPath) ||
        !File.Exists(addSave.BackupPath) ||
        string.IsNullOrWhiteSpace(addSave.ReportJsonPath) ||
        !File.Exists(addSave.ReportJsonPath))
    {
        throw new InvalidOperationException("新增剧本命令后的完整保存、复读、备份或报告验证失败。");
    }
    var addedCommandCount = addVerify.CommandCount;

    var verifySection = addVerify.Scenes
        .First(scene => scene.SceneIndex == targetSection.SceneIndex)
        .Sections.First(section => section.SectionIndex == targetSection.SectionIndex);
    var verifyBody = verifySection.Commands.First(command => command.StartsBodyBlock && command.ChildBlock != null).ChildBlock!;
    var deleteIndex = GetLegacyScriptEditAppendIndex(verifyBody.Commands) - 1;
    if (deleteIndex < 0 || verifyBody.Commands[deleteIndex].CommandId != insertedCommandId)
    {
        throw new InvalidOperationException("新增剧本命令复读后未出现在正文追加区。");
    }

    var insertedVerify = verifyBody.Commands[deleteIndex];
    var editableParameter = insertedVerify.Parameters.FirstOrDefault(parameter => parameter.Kind == LegacyScenarioParameterKind.Dword32)
        ?? throw new InvalidOperationException("新增剧本命令复读后缺少可编辑普通参数。");
    const int editedParameterValue = 12345;
    editableParameter.IntValue = editedParameterValue;
    var paramSave = writer.Save(
        testProject,
        Path.Combine("RS", scenarioFileName),
        addVerify,
        dictionary,
        "Legacy script edit smoke edit numeric parameter");
    var paramVerify = reader.Read(testScenarioPath, dictionary);
    var paramVerifySection = paramVerify.Scenes
        .First(scene => scene.SceneIndex == targetSection.SceneIndex)
        .Sections.First(section => section.SectionIndex == targetSection.SectionIndex);
    var paramVerifyBody = paramVerifySection.Commands.First(command => command.StartsBodyBlock && command.ChildBlock != null).ChildBlock!;
    var paramVerifyIndex = GetLegacyScriptEditAppendIndex(paramVerifyBody.Commands) - 1;
    if (paramVerifyIndex < 0 ||
        paramVerifyBody.Commands[paramVerifyIndex].CommandId != insertedCommandId ||
        paramVerifyBody.Commands[paramVerifyIndex].Parameters.FirstOrDefault(parameter => parameter.Kind == LegacyScenarioParameterKind.Dword32)?.IntValue != editedParameterValue ||
        string.IsNullOrWhiteSpace(paramSave.BackupPath) ||
        !File.Exists(paramSave.BackupPath) ||
        string.IsNullOrWhiteSpace(paramSave.ReportJsonPath) ||
        !File.Exists(paramSave.ReportJsonPath))
    {
        throw new InvalidOperationException("修改剧本命令普通参数后的完整保存、复读、备份或报告验证失败。");
    }

    jumpTargets = CaptureLegacyScriptEditJumpTargets(paramVerify);
    paramVerifyBody.Commands.RemoveAt(paramVerifyIndex);
    ReindexLegacyScriptEditDocument(paramVerify);
    RestoreLegacyScriptEditJumpTargets(paramVerify, jumpTargets);

    var deleteSave = writer.Save(
        testProject,
        Path.Combine("RS", scenarioFileName),
        paramVerify,
        dictionary,
        "Legacy script edit smoke delete command");
    var deleteVerify = reader.Read(testScenarioPath, dictionary);
    if (deleteVerify.CommandCount != originalCommandCount ||
        string.IsNullOrWhiteSpace(deleteSave.BackupPath) ||
        !File.Exists(deleteSave.BackupPath) ||
        string.IsNullOrWhiteSpace(deleteSave.ReportJsonPath) ||
        !File.Exists(deleteSave.ReportJsonPath))
    {
        throw new InvalidOperationException("删除剧本命令后的完整保存、复读、备份或报告验证失败。");
    }

    Console.WriteLine($"LEGACY_SCRIPT_EDIT_SMOKE_OK file={scenarioFileName} section={targetSection.SceneIndex}/{targetSection.SectionIndex} command=0x{insertedCommandId:X2}/{insertedCommandName} param={editedParameterValue} count={originalCommandCount}->{addedCommandCount}->{deleteVerify.CommandCount} addBackup={Path.GetFileName(addSave.BackupPath)} paramBackup={Path.GetFileName(paramSave.BackupPath)} deleteBackup={Path.GetFileName(deleteSave.BackupPath)}");
}

static int GetLegacyScriptEditAppendIndex(IReadOnlyList<LegacyScenarioCommandNode> commands)
{
    var index = commands.Count;
    while (index > 0 && IsLegacyScriptEditTrailingBoundary(commands[index - 1]))
    {
        index--;
    }

    return index;
}

static bool IsLegacyScriptEditTrailingBoundary(LegacyScenarioCommandNode command)
    => command.EndsSubEventBlock || command.CommandId is 0x0C or 0x0D;

static Dictionary<LegacyScenarioCommandNode, LegacyScenarioCommandNode> CaptureLegacyScriptEditJumpTargets(LegacyScenarioDocument document)
{
    var byOrdinal = document.EnumerateCommands().ToDictionary(command => command.CommandOrdinal);
    var result = new Dictionary<LegacyScenarioCommandNode, LegacyScenarioCommandNode>();
    foreach (var command in byOrdinal.Values.Where(command => command.CommandId == 0x76))
    {
        if (command.JumpTargetOrdinal.HasValue && byOrdinal.TryGetValue(command.JumpTargetOrdinal.Value, out var target))
        {
            result[command] = target;
        }
    }

    return result;
}

static void RestoreLegacyScriptEditJumpTargets(
    LegacyScenarioDocument document,
    IReadOnlyDictionary<LegacyScenarioCommandNode, LegacyScenarioCommandNode> jumpTargets)
{
    var activeCommands = document.EnumerateCommands().ToHashSet();
    foreach (var pair in jumpTargets)
    {
        if (!activeCommands.Contains(pair.Key)) continue;
        if (activeCommands.Contains(pair.Value))
        {
            pair.Key.JumpTargetOrdinal = pair.Value.CommandOrdinal;
            pair.Key.JumpTargetCommandIndex = pair.Value.CommandIndex;
        }
        else
        {
            pair.Key.JumpTargetOrdinal = null;
            pair.Key.JumpTargetCommandIndex = null;
        }
    }
}

static void ReindexLegacyScriptEditDocument(LegacyScenarioDocument document)
{
    var ordinal = 0;
    foreach (var scene in document.Scenes)
    {
        foreach (var section in scene.Sections)
        {
            var commandIndex = 0;
            ReindexLegacyScriptEditCommands(section.Commands, ref commandIndex, ref ordinal);
        }
    }
}

static void ReindexLegacyScriptEditCommands(
    IReadOnlyList<LegacyScenarioCommandNode> commands,
    ref int commandIndex,
    ref int ordinal)
{
    foreach (var command in commands)
    {
        command.CommandIndex = ++commandIndex;
        command.CommandOrdinal = ordinal++;
        if (command.ChildBlock != null)
        {
            ReindexLegacyScriptEditCommands(command.ChildBlock.Commands, ref commandIndex, ref ordinal);
        }
    }
}

static void RunE5ImageReplaceSmoke(CczProject project)
{
    var smokeRoot = Path.Combine(project.WorkspaceRoot, "CCZModStudio_TestCopies", "E5ImageReplaceSmoke_" + DateTime.Now.ToString("yyyyMMdd_HHmmss"));
    Directory.CreateDirectory(smokeRoot);
    foreach (var coreFile in new[] { "Ekd5.exe", "Data.e5", "Imsg.e5", "Star.e5", "Hexzmap.e5" })
    {
        var source = Path.Combine(project.GameRoot, coreFile);
        if (File.Exists(source))
        {
            File.Copy(source, Path.Combine(smokeRoot, coreFile), overwrite: false);
        }
    }

    var unitMovSource = CharacterImageResourceService.ResolveGameFile(project, "Unit_mov.e5");
    if (!File.Exists(unitMovSource))
    {
        throw new FileNotFoundException("E5 图片条目替换烟测需要 Unit_mov.e5。", unitMovSource);
    }

    var unitMovTarget = Path.Combine(smokeRoot, "Unit_mov.e5");
    File.Copy(unitMovSource, unitMovTarget, overwrite: false);
    var smokeE5Dir = Path.Combine(smokeRoot, "E5");
    Directory.CreateDirectory(smokeE5Dir);
    foreach (var e5File in new[] { "Face.e5", "Effarea.e5", "Hitarea.e5", "Logo.e5", "Mmap.e5", "U_select.e5", "Weather.e5", "Gate.e5", "Mark.e5", "Meff.e5" })
    {
        var source = CharacterImageResourceService.ResolveGameFile(project, e5File);
        if (File.Exists(source))
        {
            File.Copy(source, Path.Combine(smokeE5Dir, e5File), overwrite: false);
        }
    }

    foreach (var rootE5File in new[] { "Pmapobj.e5", "Unit_atk.e5", "Unit_spc.e5" })
    {
        var source = CharacterImageResourceService.ResolveGameFile(project, rootE5File);
        if (File.Exists(source))
        {
            File.Copy(source, Path.Combine(smokeRoot, rootE5File), overwrite: false);
        }
    }

    foreach (var iconDll in new[] { "Itemicon.dll", "Mgcicon.dll", "Cmdicon.dll" })
    {
        var source = Path.Combine(project.GameRoot, iconDll);
        if (File.Exists(source))
        {
            File.Copy(source, Path.Combine(smokeRoot, iconDll), overwrite: false);
        }
    }

    File.WriteAllText(Path.Combine(smokeRoot, "_CCZModStudio_TestCopy.txt"),
        $"CreatedAt={DateTime.Now:yyyy-MM-dd HH:mm:ss}\r\nSource={project.GameRoot}\r\nPurpose=E5 image replace smoke\r\n");

    var testProject = new ProjectDetector().CreateProjectFromGameRoot(smokeRoot);
    var service = new E5ImageReplaceService();
    var catalogService = new ImageResourceCatalogService();
    var catalog = catalogService.BuildCatalog(testProject);
    foreach (var required in new[] { "Face.e5", "Pmapobj.e5", "Unit_mov.e5", "Hitarea.e5", "Effarea.e5", "Logo.e5", "Mmap.e5", "U_select.e5", "Gate.e5", "Weather.e5" })
    {
        var item = catalog.FirstOrDefault(x => x.FileName.Equals(required, StringComparison.OrdinalIgnoreCase));
        if (item == null || !item.Exists || item.EntryCount <= 0)
        {
            throw new InvalidOperationException($"图片资源目录烟测未能读取 {required} 的 0x110 图片索引。");
        }
    }

    var mark = catalog.FirstOrDefault(x => x.FileName.Equals("Mark.e5", StringComparison.OrdinalIgnoreCase));
    if (mark == null || !mark.Exists || mark.SupportsE5Index || mark.CanReplace)
    {
        throw new InvalidOperationException("图片资源目录烟测应将 Mark.e5 标记为非 0x110 索引资源且不可替换。");
    }

    var face = catalog.Single(x => x.FileName.Equals("Face.e5", StringComparison.OrdinalIgnoreCase));
    var faceEntry = catalogService.ReadEntries(face).FirstOrDefault(x => x.ImageNumber == 1);
    if (faceEntry == null)
    {
        throw new InvalidOperationException("图片资源目录烟测未能读取 Face.e5 #1。");
    }

    using (var facePreview = catalogService.RenderEntryPreview(testProject, faceEntry))
    {
        if (facePreview == null || facePreview.Width <= 0 || facePreview.Height <= 0)
        {
            throw new InvalidOperationException("图片资源目录烟测未能渲染 Face.e5 #1 预览。");
        }
    }

    var itemIcon = catalog.Single(x => x.FileName.Equals("Itemicon.dll", StringComparison.OrdinalIgnoreCase));
    var mgcIcon = catalog.Single(x => x.FileName.Equals("Mgcicon.dll", StringComparison.OrdinalIgnoreCase));
    var cmdIcon = catalog.Single(x => x.FileName.Equals("Cmdicon.dll", StringComparison.OrdinalIgnoreCase));
    if (!itemIcon.Exists || itemIcon.EntryCount <= 0 || itemIcon.CanReplace || !itemIcon.SupportsPreview)
    {
        throw new InvalidOperationException("图片资源目录烟测未能把 Itemicon.dll 对齐为只读可预览图标资源。");
    }

    if (!mgcIcon.Exists || mgcIcon.EntryCount <= 0 || mgcIcon.CanReplace || !mgcIcon.SupportsPreview ||
        !cmdIcon.Exists || cmdIcon.EntryCount <= 0 || cmdIcon.CanReplace || !cmdIcon.SupportsPreview)
    {
        throw new InvalidOperationException("图片资源目录烟测未能把策略/命令 DLL 图标对齐为只读可预览资源。");
    }

    var itemIconEntry = catalogService.ReadEntries(itemIcon).FirstOrDefault(x => x.ImageNumber == 0);
    if (itemIconEntry == null || !itemIconEntry.Kind.Equals("DLL图标", StringComparison.OrdinalIgnoreCase))
    {
        throw new InvalidOperationException("图片资源目录烟测未能读取 Itemicon.dll #0 图标条目。");
    }

    using (var iconPreview = catalogService.RenderEntryPreview(testProject, itemIconEntry))
    {
        if (iconPreview == null || iconPreview.Width <= 0 || iconPreview.Height <= 0)
        {
            throw new InvalidOperationException("图片资源目录烟测未能渲染 Itemicon.dll #0 图标预览。");
        }
    }

    Console.WriteLine($"IMAGE_RESOURCE_CATALOG files={catalog.Count} face={face.EntryCount} markIndex={mark.SupportsE5Index} hit={catalog.First(x => x.FileName.Equals("Hitarea.e5", StringComparison.OrdinalIgnoreCase)).EntryCount} eff={catalog.First(x => x.FileName.Equals("Effarea.e5", StringComparison.OrdinalIgnoreCase)).EntryCount} itemIcons={itemIcon.EntryCount} mgcIcons={mgcIcon.EntryCount} cmdIcons={cmdIcon.EntryCount}");

    var entries = service.ReadIndex(unitMovTarget);
    if (entries.Count < 554)
    {
        throw new InvalidOperationException($"Unit_mov.e5 图片索引表条目不足，无法替换 #554：entries={entries.Count}。");
    }

    var replacementPng = Path.Combine(smokeRoot, "Smoke_E5_Replacement.png");
    using (var bitmap = new System.Drawing.Bitmap(12, 12, System.Drawing.Imaging.PixelFormat.Format32bppArgb))
    {
        using var graphics = System.Drawing.Graphics.FromImage(bitmap);
        graphics.Clear(System.Drawing.Color.Transparent);
        using var redBrush = new System.Drawing.SolidBrush(System.Drawing.Color.FromArgb(255, 220, 32, 64));
        using var blueBrush = new System.Drawing.SolidBrush(System.Drawing.Color.FromArgb(255, 32, 96, 220));
        graphics.FillRectangle(redBrush, 1, 1, 5, 10);
        graphics.FillRectangle(blueBrush, 6, 1, 5, 10);
        bitmap.Save(replacementPng, System.Drawing.Imaging.ImageFormat.Png);
    }

    var preview = service.PreviewReplacement(testProject, unitMovTarget, 554, replacementPng);
    if (preview.ImageNumber != 554 ||
        preview.OldSizeBytes <= 0 ||
        preview.NewKind != "PNG" ||
        preview.SourceWidth != 12 ||
        preview.SourceHeight != 12)
    {
        throw new InvalidOperationException("E5 图片条目替换预览断言失败。");
    }

    var result = service.Replace(testProject, unitMovTarget, 554, replacementPng);
    if (!File.Exists(result.BackupPath) ||
        !File.Exists(result.ReportPath) ||
        !File.Exists(result.ReportJsonPath) ||
        !File.ReadAllText(result.ReportJsonPath).Contains("E5图片条目替换", StringComparison.Ordinal) ||
        !service.ReadEntryBytes(unitMovTarget, 554).SequenceEqual(File.ReadAllBytes(replacementPng)))
    {
        throw new InvalidOperationException("E5 图片条目替换写入、复读或报告断言失败。");
    }

    Console.WriteLine($"E5_IMAGE_REPLACE_SMOKE OK target={Path.GetFileName(result.TargetPath)} image={result.ImageNumber} kind={result.OldKind}->{result.NewKind} size={result.OldSizeBytes}->{result.NewSizeBytes} backup={Path.GetFileName(result.BackupPath)} json={Path.GetFileName(result.ReportJsonPath)}");
}

static void RunJobStrategyWriteSmoke(CczProject project, IReadOnlyList<HexTableDefinition> tables)
{
    var smokeRoot = Path.Combine(project.WorkspaceRoot, "CCZModStudio_TestCopies", "JobStrategyWriteSmoke_" + DateTime.Now.ToString("yyyyMMdd_HHmmss"));
    Directory.CreateDirectory(smokeRoot);
    foreach (var coreFile in new[] { "Ekd5.exe", "Data.e5", "Imsg.e5", "Star.e5" })
    {
        var source = Path.Combine(project.GameRoot, coreFile);
        if (!File.Exists(source))
        {
            throw new FileNotFoundException("兵种策略写入烟测缺少核心文件。", source);
        }

        File.Copy(source, Path.Combine(smokeRoot, coreFile), overwrite: false);
    }

    File.WriteAllText(Path.Combine(smokeRoot, "_CCZModStudio_TestCopy.txt"),
        $"CreatedAt={DateTime.Now:yyyy-MM-dd HH:mm:ss}\r\nSource={project.GameRoot}\r\nPurpose=job strategy write smoke\r\n");

    var testProject = new ProjectDetector().CreateProjectFromGameRoot(smokeRoot);
    RunJobStrategyWriteSmokeCore(testProject, tables);
    Console.WriteLine($"JOB_STRATEGY_WRITE_SMOKE_ROOT {smokeRoot}");
}

static void RunRsWriteSmoke(CczProject project, IReadOnlyList<HexTableDefinition> tables)
{
    RunRsSmoke(project, tables);

    var smokeRoot = Path.Combine(project.WorkspaceRoot, "CCZModStudio_TestCopies", "RsWriteSmoke_" + DateTime.Now.ToString("yyyyMMdd_HHmmss"));
    Directory.CreateDirectory(smokeRoot);
    foreach (var coreFile in new[] { "Ekd5.exe", "Data.e5", "Star.e5", "Imsg.e5", "Hexzmap.e5" })
    {
        var source = Path.Combine(project.GameRoot, coreFile);
        if (!File.Exists(source))
        {
            throw new FileNotFoundException("R/S 写入烟测缺少核心文件。", source);
        }

        File.Copy(source, Path.Combine(smokeRoot, coreFile), overwrite: false);
    }

    var rsRoot = Path.Combine(smokeRoot, "RS");
    Directory.CreateDirectory(rsRoot);
    var sourceScenarioPath = Path.Combine(project.GameRoot, "RS", "R_00.eex");
    if (!File.Exists(sourceScenarioPath))
    {
        sourceScenarioPath = Directory.GetFiles(Path.Combine(project.GameRoot, "RS"), "R_*.eex", SearchOption.TopDirectoryOnly)
            .OrderBy(x => x, StringComparer.CurrentCultureIgnoreCase)
            .FirstOrDefault()
            ?? throw new FileNotFoundException("R/S 写入烟测找不到 R_*.eex。", Path.Combine(project.GameRoot, "RS", "R_*.eex"));
    }

    var scenarioFileName = Path.GetFileName(sourceScenarioPath);
    var testScenarioPath = Path.Combine(rsRoot, scenarioFileName);
    File.Copy(sourceScenarioPath, testScenarioPath, overwrite: false);
    var sourceBattlefieldScenarioPath = Path.Combine(project.GameRoot, "RS", "S_00.eex");
    if (!File.Exists(sourceBattlefieldScenarioPath))
    {
        sourceBattlefieldScenarioPath = Directory.GetFiles(Path.Combine(project.GameRoot, "RS"), "S_*.eex", SearchOption.TopDirectoryOnly)
            .OrderBy(x => x, StringComparer.CurrentCultureIgnoreCase)
            .FirstOrDefault()
            ?? throw new FileNotFoundException("R/S write smoke could not find S_*.eex.", Path.Combine(project.GameRoot, "RS", "S_*.eex"));
    }

    var battlefieldScenarioFileName = Path.GetFileName(sourceBattlefieldScenarioPath);
    var testBattlefieldScenarioPath = Path.Combine(rsRoot, battlefieldScenarioFileName);
    File.Copy(sourceBattlefieldScenarioPath, testBattlefieldScenarioPath, overwrite: false);

    var sourceMapRoot = Path.Combine(project.GameRoot, "Map");
    var smokeMapRoot = Path.Combine(smokeRoot, "Map");
    Directory.CreateDirectory(smokeMapRoot);
    var sourceMapPath = FindFirstJpegMap(sourceMapRoot)
        ?? throw new FileNotFoundException("地图底图写入烟测找不到 Map\\*.jpg。", sourceMapRoot);
    File.Copy(sourceMapPath, Path.Combine(smokeMapRoot, Path.GetFileName(sourceMapPath)), overwrite: false);

    File.WriteAllText(Path.Combine(smokeRoot, "_CCZModStudio_TestCopy.txt"),
        $"CreatedAt={DateTime.Now:yyyy-MM-dd HH:mm:ss}\r\nSource={project.GameRoot}\r\nPurpose=R/S eex write smoke\r\n");

    var testProject = new ProjectDetector().CreateProjectFromGameRoot(smokeRoot);
    var scenarioIndex = new ScenarioFileReader().ReadAllIndex(testProject);
    if (scenarioIndex.Count != 2 ||
        !scenarioIndex.Any(x => x.FileName.Equals(scenarioFileName, StringComparison.OrdinalIgnoreCase)) ||
        !scenarioIndex.Any(x => x.FileName.Equals(battlefieldScenarioFileName, StringComparison.OrdinalIgnoreCase)) ||
        scenarioIndex.Any(x => !ScenarioFileReader.IsRsScriptFile(x.FileName)))
    {
        throw new InvalidOperationException("R/S 写入烟测索引未限定在测试副本 RS eex 文件。");
    }

    var sceneStringPath = ProjectDetector.FindSceneDictionaryPath(project);
    if (!File.Exists(sceneStringPath))
    {
        throw new FileNotFoundException("R/S legacy full-structure write smoke requires CczString.ini.", sceneStringPath);
    }

    var sceneDoc = new SceneStringParser().Parse(sceneStringPath);
    var legacyDocument = new LegacyScenarioReader().Read(testScenarioPath, sceneDoc);
    var legacySave = new LegacyScenarioWriter().Save(
        testProject,
        Path.Combine("RS", scenarioFileName),
        legacyDocument,
        sceneDoc,
        "R/S eex legacy full-structure write smoke");
    var legacyVerify = new LegacyScenarioReader().Read(testScenarioPath, sceneDoc);
    if (legacyVerify.SceneCount != legacyDocument.SceneCount ||
        legacyVerify.SectionCount != legacyDocument.SectionCount ||
        legacyVerify.CommandCount != legacyDocument.CommandCount ||
        string.IsNullOrWhiteSpace(legacySave.BackupPath) ||
        !File.Exists(legacySave.BackupPath) ||
        string.IsNullOrWhiteSpace(legacySave.ReportJsonPath) ||
        !File.Exists(legacySave.ReportJsonPath))
    {
        throw new InvalidOperationException("R/S eex legacy full-structure write reread, backup, or report validation failed.");
    }
    Console.WriteLine($"LEGACY_SCENARIO_WRITE_OK file={scenarioFileName} scenes={legacyVerify.SceneCount} sections={legacyVerify.SectionCount} commands={legacyVerify.CommandCount} changedBytes={legacySave.ChangedBytes} backup={Path.GetFileName(legacySave.BackupPath)}");

    RunRScenePositionWriteSmoke(testProject, sceneDoc, scenarioFileName);

    var textReader = new ScenarioTextReader();
    var textRows = textReader.Read(testScenarioPath, maxItems: 80).ToList();
    var writableText = textRows.FirstOrDefault(x => x.ByteLength >= EncodingService.GetGbkByteCount("烟测") &&
                                                    !string.Equals(BattlefieldEditorService.NormalizeText(x.Text), "烟测", StringComparison.Ordinal))
                       ?? textRows.FirstOrDefault(x => x.ByteLength >= EncodingService.GetGbkByteCount("写测"))
                       ?? throw new InvalidOperationException($"{scenarioFileName} 没有可用于 R/S eex 原地短写回烟测的文本线索。");
    var originalText = writableText.Text;
    var replacementText = string.Equals(BattlefieldEditorService.NormalizeText(originalText), "烟测", StringComparison.Ordinal)
        ? "写测"
        : "烟测";
    if (EncodingService.GetGbkByteCount(replacementText) > writableText.ByteLength)
    {
        throw new InvalidOperationException($"R/S eex 文本容量不足：{scenarioFileName} {writableText.OffsetHex} capacity={writableText.ByteLength}");
    }

    writableText.Text = replacementText;
    var textSave = new ScenarioTextWriter().SaveInPlace(
        testProject,
        Path.Combine("RS", scenarioFileName),
        new[] { writableText },
        "R/S eex 写入烟测前自动备份");
    var textVerify = textReader.Read(testScenarioPath, maxItems: 80).FirstOrDefault(x => x.Offset == writableText.Offset);
    if (textVerify == null ||
        !string.Equals(BattlefieldEditorService.NormalizeText(textVerify.Text), replacementText, StringComparison.Ordinal) ||
        string.IsNullOrWhiteSpace(textSave.BackupPath) ||
        !File.Exists(textSave.BackupPath) ||
        string.IsNullOrWhiteSpace(textSave.ReportJsonPath) ||
        !File.Exists(textSave.ReportJsonPath) ||
        !File.ReadAllText(textSave.ReportJsonPath).Contains("\"OperationKind\": \"R/S eex 剧本文本写回\"", StringComparison.Ordinal))
    {
        throw new InvalidOperationException("R/S eex 文本短写回复读、备份或结构化报告验证失败。");
    }

    var imageAssignmentService = new ImageAssignmentService();
    var imageData = imageAssignmentService.Load(testProject, tables);
    var originalR = Convert.ToInt32(imageData.Rows[0]["R形象编号"], CultureInfo.InvariantCulture);
    var changedR = originalR == 0 ? 1 : 0;
    imageData.Rows[0]["R形象编号"] = changedR;
    var imageSave = imageAssignmentService.SaveToTestCopy(testProject, tables, imageData);
    var imageVerify = imageAssignmentService.Load(testProject, tables);
    var actualR = Convert.ToInt32(imageVerify.Rows[0]["R形象编号"], CultureInfo.InvariantCulture);
    if (actualR != changedR ||
        imageSave.Saves.Count == 0 ||
        imageSave.Saves.Any(x => string.IsNullOrWhiteSpace(x.BackupPath) || !File.Exists(x.BackupPath)))
    {
        throw new InvalidOperationException($"人物 R/S 形象指定写回复读失败：expected={changedR}, actual={actualR}");
    }

    Console.WriteLine($"RS_WRITE_TEXT_OK file={scenarioFileName} offset={writableText.OffsetHex} '{originalText}'->'{textVerify.Text}' changedBytes={textSave.ChangedBytes} backup={Path.GetFileName(textSave.BackupPath)}");
    Console.WriteLine($"RS_WRITE_IMAGE_ASSIGN_OK row=0 R={originalR}->{actualR} saves={imageSave.Saves.Count} backups={imageSave.BackupSummary}");

    RunRoleWriteSmoke(testProject, tables);
    RunItemWriteSmoke(testProject, tables);
    RunItemEffectCatalogSmoke(testProject, smokeRoot);
    RunJobWriteSmoke(testProject, tables);
    RunBattlefieldTextWriteSmoke(project, testProject, tables, battlefieldScenarioFileName);
    RunBattlefieldDeploymentWriteSmoke(project, testProject, tables, battlefieldScenarioFileName);
    RunMapImageWriteSmoke(testProject);
    RunHexzmapWriteSmoke(project, testProject);
    RunMapWorkbenchSmoke(project, testProject);
    Console.WriteLine($"RS_WRITE_SMOKE OK root={smokeRoot}");
}

static void RunItemEffectCatalogSmoke(CczProject testProject, string smokeRoot)
{
    var isolatedProject = new CczProject
    {
        WorkspaceRoot = smokeRoot,
        GameRoot = testProject.GameRoot,
        HexTableXmlPath = testProject.HexTableXmlPath,
        SceneDictionaryPath = testProject.SceneDictionaryPath,
        SceneEditorDirectory = testProject.SceneEditorDirectory,
        ImageAssignerDirectory = testProject.ImageAssignerDirectory,
        ImageAssignerSystemIniPath = testProject.ImageAssignerSystemIniPath,
        MaterialLibraryRoot = testProject.MaterialLibraryRoot,
        PatchConfigRoot = testProject.PatchConfigRoot,
        PathDiagnostics = testProject.PathDiagnostics
    };

    var service = new ItemEffectCatalogService();
    var entries = new[]
    {
        new ItemEffectCatalogEntry { EffectId = 42, Name = "神火护体", Description = "变长中文说明：用于烟测 UTF-8 保存与读取。" },
        new ItemEffectCatalogEntry { EffectId = 42, Name = "烈焰护盾·改", Description = "允许同一特效号重复，并保留第二条自定义说明。" },
        new ItemEffectCatalogEntry { EffectId = 99, Name = "超长特效名称-烟测-天地无双护身真诀", Description = "验证特效名不是固定字段长，而是项目侧 UTF-8 变长文本。" }
    };
    var storePath = service.Save(isolatedProject, entries);
    var loaded = service.Load(isolatedProject);
    var lookup = service.BuildDisplayLookup(loaded);
    if (!File.Exists(storePath) ||
        loaded.Count < 3 ||
        !lookup.TryGetValue(42, out var duplicateName) ||
        !duplicateName.Contains("神火护体", StringComparison.Ordinal) ||
        !duplicateName.Contains("烈焰护盾·改", StringComparison.Ordinal) ||
        !lookup.TryGetValue(99, out var longName) ||
        !longName.Contains("天地无双护身真诀", StringComparison.Ordinal) ||
        !File.ReadAllText(storePath, Encoding.UTF8).Contains("烈焰护盾·改", StringComparison.Ordinal))
    {
        throw new InvalidOperationException("宝物特效目录烟测失败：重复特效号、变长中文或 UTF-8 回读不符合预期。");
    }

    Console.WriteLine($"ITEM_EFFECT_CATALOG_OK file={Path.GetFileName(storePath)} dup42={duplicateName} long99={longName}");
}

static void RunRScenePositionWriteSmoke(CczProject testProject, SceneStringDocument sceneDoc, string scenarioFileName)
{
    var relativePath = Path.Combine("RS", scenarioFileName);
    var fullPath = Path.Combine(testProject.GameRoot, relativePath);
    var document = new LegacyScenarioReader().Read(fullPath, sceneDoc);
    var command = document.EnumerateCommands().FirstOrDefault(command =>
        command.CommandId == 0x30 &&
        command.Parameters.Count > 2 &&
        command.Parameters[1].Kind == LegacyScenarioParameterKind.Dword32 &&
        command.Parameters[2].Kind == LegacyScenarioParameterKind.Dword32)
        ?? throw new InvalidOperationException($"{scenarioFileName} 没有可用于 R 场景坐标写回烟测的 0x30 武将出现命令。");

    var originalX = command.Parameters[1].IntValue;
    var originalY = command.Parameters[2].IntValue;
    var targetX = originalX <= 0 ? 1 : originalX - 1;
    var targetY = originalY <= 0 ? 1 : originalY - 1;
    if (targetX == originalX && targetY == originalY)
    {
        targetX = originalX + 1;
    }

    command.Parameters[1].IntValue = targetX;
    command.Parameters[2].IntValue = targetY;
    var save = new LegacyScenarioWriter().Save(
        testProject,
        relativePath,
        document,
        sceneDoc,
        "R场景制作 0x30 武将出现坐标写回烟测");

    var verify = new LegacyScenarioReader().Read(fullPath, sceneDoc);
    var verifiedCommand = verify.EnumerateCommands().FirstOrDefault(candidate =>
        candidate.SceneIndex == command.SceneIndex &&
        candidate.SectionIndex == command.SectionIndex &&
        candidate.CommandIndex == command.CommandIndex &&
        candidate.CommandId == command.CommandId)
        ?? throw new InvalidOperationException("R 场景坐标写回复读失败：找不到原 0x30 命令。");

    var actualX = verifiedCommand.Parameters.Count > 2 ? verifiedCommand.Parameters[1].IntValue : int.MinValue;
    var actualY = verifiedCommand.Parameters.Count > 2 ? verifiedCommand.Parameters[2].IntValue : int.MinValue;
    if (actualX != targetX ||
        actualY != targetY ||
        string.IsNullOrWhiteSpace(save.BackupPath) ||
        !File.Exists(save.BackupPath) ||
        string.IsNullOrWhiteSpace(save.ReportJsonPath) ||
        !File.Exists(save.ReportJsonPath))
    {
        throw new InvalidOperationException($"R 场景坐标写回复读、备份或报告失败：expected=({targetX},{targetY}), actual=({actualX},{actualY})。");
    }

    Console.WriteLine($"RSCENE_POSITION_WRITE_SMOKE_OK file={scenarioFileName} command=Scene={command.SceneIndex};Section={command.SectionIndex};Command={command.CommandIndex};Id={command.CommandIdHex} coord=({originalX},{originalY})->({actualX},{actualY}) backup={Path.GetFileName(save.BackupPath)} changedBytes={save.ChangedBytes}");
}

static void RunRoleWriteSmoke(CczProject testProject, IReadOnlyList<HexTableDefinition> tables)
{
    var reader = new HexTableReader();
    var writer = new HexTableWriter();
    var personTable = tables.Single(t => t.TableName == "6.5-0 人物");
    var biographyTable = tables.Single(t => t.TableName == "6.5-0-1 人物列传");
    var personRead = reader.Read(testProject, personTable, tables);
    var biographyRead = reader.Read(testProject, biographyTable, tables);
    if (!personRead.Validation.IsUsable || !biographyRead.Validation.IsUsable)
    {
        throw new InvalidOperationException("角色写入烟测读取人物表或人物列传失败。");
    }

    var roleId = 0;
    var personRow = FindSmokeRowById(personRead.Data, roleId);
    var biographyRow = FindSmokeRowById(biographyRead.Data, roleId);
    var originalName = Convert.ToString(personRow["名称"], CultureInfo.InvariantCulture) ?? string.Empty;
    var changedName = originalName == "烟测曹操" ? "写测曹操" : "烟测曹操";
    var originalFace = Convert.ToInt32(personRow["头像"], CultureInfo.InvariantCulture);
    var changedFace = originalFace == 0 ? 1 : 0;
    var originalJob = Convert.ToInt32(personRow["职业"], CultureInfo.InvariantCulture);
    var changedJob = originalJob == 0 ? 1 : 0;
    var originalLevel = Convert.ToInt32(personRow["级别"], CultureInfo.InvariantCulture);
    var changedLevel = originalLevel == 1 ? 2 : 1;
    var originalAbility = Convert.ToInt32(personRow["武力"], CultureInfo.InvariantCulture);
    var changedAbility = originalAbility == 99 ? 98 : 99;

    personRow["名称"] = changedName;
    personRow["头像"] = changedFace;
    personRow["职业"] = changedJob;
    personRow["级别"] = changedLevel;
    personRow["武力"] = changedAbility;
    biographyRow["介绍"] = "CCZ人物列传烟测";

    var saves = new[]
    {
        writer.Save(testProject, personTable, personRead.Data),
        writer.Save(testProject, biographyTable, biographyRead.Data)
    };

    var personVerify = reader.Read(testProject, personTable, tables);
    var biographyVerify = reader.Read(testProject, biographyTable, tables);
    var verifyPerson = FindSmokeRowById(personVerify.Data, roleId);
    var actualName = Convert.ToString(verifyPerson["名称"], CultureInfo.InvariantCulture) ?? string.Empty;
    var actualFace = Convert.ToInt32(verifyPerson["头像"], CultureInfo.InvariantCulture);
    var actualJob = Convert.ToInt32(verifyPerson["职业"], CultureInfo.InvariantCulture);
    var actualLevel = Convert.ToInt32(verifyPerson["级别"], CultureInfo.InvariantCulture);
    var actualAbility = Convert.ToInt32(verifyPerson["武力"], CultureInfo.InvariantCulture);
    var actualBiography = Convert.ToString(FindSmokeRowById(biographyVerify.Data, roleId)["介绍"], CultureInfo.InvariantCulture) ?? string.Empty;
    if (actualName != changedName ||
        actualFace != changedFace ||
        actualJob != changedJob ||
        actualLevel != changedLevel ||
        actualAbility != changedAbility ||
        !actualBiography.Contains("CCZ人物列传烟测", StringComparison.Ordinal) ||
        saves.Any(x => string.IsNullOrWhiteSpace(x.BackupPath) || !File.Exists(x.BackupPath)))
    {
        throw new InvalidOperationException($"角色写入烟测复读失败：name={actualName}, face={actualFace}, job={actualJob}, level={actualLevel}, ability={actualAbility}");
    }

    Console.WriteLine($"ROLE_WRITE_SMOKE_OK id={roleId}:{originalName}->{actualName} 头像={originalFace}->{actualFace} 职业={originalJob}->{actualJob} 级别={originalLevel}->{actualLevel} 武力={originalAbility}->{actualAbility} saves={saves.Length}");
}

static void RunItemWriteSmoke(CczProject testProject, IReadOnlyList<HexTableDefinition> tables)
{
    var reader = new HexTableReader();
    var writer = new HexTableWriter();
    var itemLowTable = tables.Single(t => t.TableName == "6.5-1 物品（0-103）");
    var itemHighTable = tables.Single(t => t.TableName == "6.5-2 物品（104-255）");
    var descLowTable = tables.Single(t => t.TableName == "6.5-1-1 物品说明（0-103）");
    var descHighTable = tables.Single(t => t.TableName == "6.5-2-1 物品说明（104-255）");

    var itemLow = reader.Read(testProject, itemLowTable, tables);
    var itemHigh = reader.Read(testProject, itemHighTable, tables);
    var descLow = reader.Read(testProject, descLowTable, tables);
    var descHigh = reader.Read(testProject, descHighTable, tables);
    if (!itemLow.Validation.IsUsable || !itemHigh.Validation.IsUsable || !descLow.Validation.IsUsable || !descHigh.Validation.IsUsable)
    {
        throw new InvalidOperationException("宝物写入烟测读取物品表/说明表失败。");
    }

    var lowItemId = 0;
    var lowItemRow = FindSmokeRowById(itemLow.Data, lowItemId);
    var lowDescRow = FindSmokeRowById(descLow.Data, lowItemId);
    var originalLowName = Convert.ToString(lowItemRow["名称"], CultureInfo.InvariantCulture) ?? string.Empty;
    var originalLowPrice = Convert.ToInt32(lowItemRow["价格（/100）"], CultureInfo.InvariantCulture);
    var newLowName = originalLowName == "烟测宝物" ? "写测宝物" : "烟测宝物";
    var newLowPrice = originalLowPrice == 1 ? 2 : 1;
    lowItemRow["名称"] = newLowName;
    lowItemRow["价格（/100）"] = newLowPrice;
    lowDescRow["介绍"] = "烟测说明";

    var highItemId = Convert.ToInt32(itemHigh.Data.Rows[0]["ID"], CultureInfo.InvariantCulture);
    var highItemRow = FindSmokeRowById(itemHigh.Data, highItemId);
    var highDescRow = FindSmokeRowById(descHigh.Data, highItemId);
    var originalHighName = Convert.ToString(highItemRow["名称"], CultureInfo.InvariantCulture) ?? string.Empty;
    var newHighName = originalHighName == "烟测扩展" ? "写测扩展" : "烟测扩展";
    highItemRow["名称"] = newHighName;
    highDescRow["介绍"] = "扩展烟测说明";

    var saves = new[]
    {
        writer.Save(testProject, itemLowTable, itemLow.Data),
        writer.Save(testProject, descLowTable, descLow.Data),
        writer.Save(testProject, itemHighTable, itemHigh.Data),
        writer.Save(testProject, descHighTable, descHigh.Data)
    };

    var itemLowVerify = reader.Read(testProject, itemLowTable, tables);
    var itemHighVerify = reader.Read(testProject, itemHighTable, tables);
    var descLowVerify = reader.Read(testProject, descLowTable, tables);
    var descHighVerify = reader.Read(testProject, descHighTable, tables);
    if ((Convert.ToString(FindSmokeRowById(itemLowVerify.Data, lowItemId)["名称"], CultureInfo.InvariantCulture) ?? string.Empty) != newLowName ||
        Convert.ToInt32(FindSmokeRowById(itemLowVerify.Data, lowItemId)["价格（/100）"], CultureInfo.InvariantCulture) != newLowPrice ||
        !(Convert.ToString(FindSmokeRowById(descLowVerify.Data, lowItemId)["介绍"], CultureInfo.InvariantCulture) ?? string.Empty).Contains("烟测说明", StringComparison.Ordinal) ||
        (Convert.ToString(FindSmokeRowById(itemHighVerify.Data, highItemId)["名称"], CultureInfo.InvariantCulture) ?? string.Empty) != newHighName ||
        !(Convert.ToString(FindSmokeRowById(descHighVerify.Data, highItemId)["介绍"], CultureInfo.InvariantCulture) ?? string.Empty).Contains("扩展烟测说明", StringComparison.Ordinal) ||
        saves.Any(x => string.IsNullOrWhiteSpace(x.BackupPath) || !File.Exists(x.BackupPath)))
    {
        throw new InvalidOperationException("宝物/物品写入烟测复读、备份或说明写回验证失败。");
    }

    Console.WriteLine($"ITEM_WRITE_SMOKE_OK low={lowItemId}:{originalLowName}->{newLowName} price={originalLowPrice}->{newLowPrice} high={highItemId}:{originalHighName}->{newHighName} saves={saves.Length}");
}

static void RunJobWriteSmoke(CczProject testProject, IReadOnlyList<HexTableDefinition> tables)
{
    var reader = new HexTableReader();
    var writer = new HexTableWriter();

    var detailedJobTable = tables.Single(t => t.TableName == "6.5-4 详细兵种");
    var jobDescriptionTable = tables.Single(t => t.TableName == "6.5-4-1 兵种说明");
    var jobGrowthTable = tables.Single(t => t.TableName == "6.5-4-2 兵种成长");
    var jobPierceTable = tables.Single(t => t.TableName == "6.5-4-3 兵种穿透");

    var detailedJobRead = reader.Read(testProject, detailedJobTable, tables);
    var jobDescriptionRead = reader.Read(testProject, jobDescriptionTable, tables);
    var jobGrowthRead = reader.Read(testProject, jobGrowthTable, tables);
    var jobPierceRead = reader.Read(testProject, jobPierceTable, tables);
    if (!detailedJobRead.Validation.IsUsable || !jobDescriptionRead.Validation.IsUsable ||
        !jobGrowthRead.Validation.IsUsable || !jobPierceRead.Validation.IsUsable)
    {
        throw new InvalidOperationException("兵种写入烟测读取详细兵种/说明/成长/穿透失败。");
    }

    var jobId = 0;
    var detailedJobRow = FindSmokeRowById(detailedJobRead.Data, jobId);
    var jobDescriptionRow = FindSmokeRowById(jobDescriptionRead.Data, jobId);
    var jobGrowthRow = FindSmokeRowById(jobGrowthRead.Data, jobId);
    var jobPierceRow = FindSmokeRowById(jobPierceRead.Data, jobId);
    var originalJobName = Convert.ToString(detailedJobRow["名称"], CultureInfo.InvariantCulture) ?? string.Empty;
    var changedJobName = originalJobName == "烟测兵种" ? "写测兵种" : "烟测兵种";
    var growthField = jobGrowthRead.Data.Columns.Contains("移动力")
        ? "移动力"
        : jobGrowthRead.Data.Columns.Cast<DataColumn>().First(c => c.ColumnName is not "ID" and not "名称").ColumnName;
    var originalGrowth = Convert.ToInt32(jobGrowthRow[growthField], CultureInfo.InvariantCulture);
    var changedGrowth = originalGrowth == 1 ? 2 : 1;
    var originalPierce = Convert.ToInt32(jobPierceRow["穿透"], CultureInfo.InvariantCulture);
    var changedPierce = originalPierce == 0 ? 1 : 0;

    detailedJobRow["名称"] = changedJobName;
    jobDescriptionRow["介绍"] = "CCZ兵种烟测";
    jobGrowthRow[growthField] = changedGrowth;
    jobPierceRow["穿透"] = changedPierce;

    var detailedSaves = new[]
    {
        writer.Save(testProject, detailedJobTable, detailedJobRead.Data),
        writer.Save(testProject, jobDescriptionTable, jobDescriptionRead.Data),
        writer.Save(testProject, jobGrowthTable, jobGrowthRead.Data),
        writer.Save(testProject, jobPierceTable, jobPierceRead.Data)
    };

    var detailedJobVerify = reader.Read(testProject, detailedJobTable, tables);
    var jobDescriptionVerify = reader.Read(testProject, jobDescriptionTable, tables);
    var jobGrowthVerify = reader.Read(testProject, jobGrowthTable, tables);
    var jobPierceVerify = reader.Read(testProject, jobPierceTable, tables);
    var actualJobName = Convert.ToString(FindSmokeRowById(detailedJobVerify.Data, jobId)["名称"], CultureInfo.InvariantCulture) ?? string.Empty;
    var actualJobDescription = Convert.ToString(FindSmokeRowById(jobDescriptionVerify.Data, jobId)["介绍"], CultureInfo.InvariantCulture) ?? string.Empty;
    var actualGrowth = Convert.ToInt32(FindSmokeRowById(jobGrowthVerify.Data, jobId)[growthField], CultureInfo.InvariantCulture);
    var actualPierce = Convert.ToInt32(FindSmokeRowById(jobPierceVerify.Data, jobId)["穿透"], CultureInfo.InvariantCulture);
    if (actualJobName != changedJobName ||
        !actualJobDescription.Contains("CCZ兵种烟测", StringComparison.Ordinal) ||
        actualGrowth != changedGrowth ||
        actualPierce != changedPierce ||
        detailedSaves.Any(x => string.IsNullOrWhiteSpace(x.BackupPath) || !File.Exists(x.BackupPath)))
    {
        throw new InvalidOperationException($"详细兵种写入烟测复读失败：name={actualJobName}, {growthField}={actualGrowth}, pierce={actualPierce}");
    }

    Console.WriteLine($"JOB_WRITE_SMOKE_OK id={jobId}:{originalJobName}->{actualJobName} {growthField}={originalGrowth}->{actualGrowth} 穿透={originalPierce}->{actualPierce} saves={detailedSaves.Length}");

    var jobSeriesTable = tables.Single(t => t.TableName == "6.5-3 兵种系");
    var jobTerrainPowerTable = tables.Single(t => t.TableName == "6.5-3-1 地形发挥");
    var jobMoveCostTable = tables.Single(t => t.TableName == "6.5-3-2 移动消耗");
    var jobRestraintTable = tables.Single(t => t.TableName == "6.5-3-3 兵种相克");
    var jobAttributeTable = tables.Single(t => t.TableName == "6.5-3-4 兵种属性");
    var jobSeriesRead = reader.Read(testProject, jobSeriesTable, tables);
    var jobTerrainPowerRead = reader.Read(testProject, jobTerrainPowerTable, tables);
    var jobMoveCostRead = reader.Read(testProject, jobMoveCostTable, tables);
    var jobRestraintRead = reader.Read(testProject, jobRestraintTable, tables);
    var jobAttributeRead = reader.Read(testProject, jobAttributeTable, tables);
    if (!jobSeriesRead.Validation.IsUsable || !jobTerrainPowerRead.Validation.IsUsable || !jobMoveCostRead.Validation.IsUsable ||
        !jobRestraintRead.Validation.IsUsable || !jobAttributeRead.Validation.IsUsable)
    {
        throw new InvalidOperationException("兵种系/地形/矩阵写入烟测读取失败。");
    }

    var seriesId = 0;
    var jobSeriesRow = FindSmokeRowById(jobSeriesRead.Data, seriesId);
    var terrainPowerRow = FindSmokeRowById(jobTerrainPowerRead.Data, seriesId);
    var moveCostRow = FindSmokeRowById(jobMoveCostRead.Data, seriesId);
    var jobSeriesOriginal = Convert.ToString(jobSeriesRow["名称"], CultureInfo.InvariantCulture) ?? string.Empty;
    var jobSeriesChanged = jobSeriesOriginal == "烟测兵" ? "写测骑" : "烟测兵";
    var terrainField = jobTerrainPowerRead.Data.Columns.Contains("平原")
        ? "平原"
        : jobTerrainPowerRead.Data.Columns.Cast<DataColumn>().First(c => c.ColumnName is not "ID" and not "名称").ColumnName;
    var powerOriginal = Convert.ToInt32(terrainPowerRow[terrainField], CultureInfo.InvariantCulture);
    var powerChanged = powerOriginal == 100 ? 90 : 100;
    var moveOriginal = Convert.ToInt32(moveCostRow[terrainField], CultureInfo.InvariantCulture);
    var moveChanged = moveOriginal == 1 ? 2 : 1;
    jobSeriesRow["名称"] = jobSeriesChanged;
    terrainPowerRow[terrainField] = powerChanged;
    moveCostRow[terrainField] = moveChanged;
    var terrainSaves = new[]
    {
        writer.Save(testProject, jobSeriesTable, jobSeriesRead.Data),
        writer.Save(testProject, jobTerrainPowerTable, jobTerrainPowerRead.Data),
        writer.Save(testProject, jobMoveCostTable, jobMoveCostRead.Data)
    };
    var jobSeriesVerify = reader.Read(testProject, jobSeriesTable, tables);
    var jobTerrainPowerVerify = reader.Read(testProject, jobTerrainPowerTable, tables);
    var jobMoveCostVerify = reader.Read(testProject, jobMoveCostTable, tables);
    var jobSeriesActual = Convert.ToString(FindSmokeRowById(jobSeriesVerify.Data, seriesId)["名称"], CultureInfo.InvariantCulture) ?? string.Empty;
    var powerActual = Convert.ToInt32(FindSmokeRowById(jobTerrainPowerVerify.Data, seriesId)[terrainField], CultureInfo.InvariantCulture);
    var moveActual = Convert.ToInt32(FindSmokeRowById(jobMoveCostVerify.Data, seriesId)[terrainField], CultureInfo.InvariantCulture);
    if (jobSeriesActual != jobSeriesChanged ||
        powerActual != powerChanged ||
        moveActual != moveChanged ||
        terrainSaves.Any(x => string.IsNullOrWhiteSpace(x.BackupPath) || !File.Exists(x.BackupPath)))
    {
        throw new InvalidOperationException($"兵种系/地形写入烟测复读失败：series={jobSeriesActual}, power={powerActual}, move={moveActual}");
    }

    Console.WriteLine($"JOB_TERRAIN_WRITE_SMOKE_OK id={seriesId}:{jobSeriesOriginal}->{jobSeriesActual} {terrainField}发挥={powerOriginal}->{powerActual} {terrainField}消耗={moveOriginal}->{moveActual} saves={terrainSaves.Length}");

    var restraintColumn = jobRestraintRead.Data.Columns.Contains("1") ? "1" : jobRestraintRead.Data.Columns.Cast<DataColumn>().First(c => c.ColumnName is not "ID" and not "名称").ColumnName;
    var restraintRow = FindSmokeRowById(jobRestraintRead.Data, seriesId);
    var restraintOriginal = Convert.ToInt32(restraintRow[restraintColumn], CultureInfo.InvariantCulture);
    var restraintChanged = restraintOriginal == 100 ? 95 : 100;
    restraintRow[restraintColumn] = restraintChanged;
    var attributeColumn = jobAttributeRead.Data.Columns.Contains("0") ? "0" : jobAttributeRead.Data.Columns.Cast<DataColumn>().First(c => c.ColumnName != "ID").ColumnName;
    var attributeRow = FindSmokeRowById(jobAttributeRead.Data, 0);
    var attributeOriginal = Convert.ToInt32(attributeRow[attributeColumn], CultureInfo.InvariantCulture);
    var attributeChanged = attributeOriginal == 1 ? 2 : 1;
    attributeRow[attributeColumn] = attributeChanged;
    var matrixSaves = new[]
    {
        writer.Save(testProject, jobRestraintTable, jobRestraintRead.Data),
        writer.Save(testProject, jobAttributeTable, jobAttributeRead.Data)
    };
    var jobRestraintVerify = reader.Read(testProject, jobRestraintTable, tables);
    var jobAttributeVerify = reader.Read(testProject, jobAttributeTable, tables);
    var restraintActual = Convert.ToInt32(FindSmokeRowById(jobRestraintVerify.Data, seriesId)[restraintColumn], CultureInfo.InvariantCulture);
    var attributeActual = Convert.ToInt32(FindSmokeRowById(jobAttributeVerify.Data, 0)[attributeColumn], CultureInfo.InvariantCulture);
    if (restraintActual != restraintChanged ||
        attributeActual != attributeChanged ||
        matrixSaves.Any(x => string.IsNullOrWhiteSpace(x.BackupPath) || !File.Exists(x.BackupPath)))
    {
        throw new InvalidOperationException($"兵种相克/属性矩阵写入烟测复读失败：restraint={restraintActual}, attribute={attributeActual}");
    }

    Console.WriteLine($"JOB_MATRIX_WRITE_SMOKE_OK 相克[{seriesId},{restraintColumn}]={restraintOriginal}->{restraintActual} 属性[0,{attributeColumn}]={attributeOriginal}->{attributeActual} saves={matrixSaves.Length}");

    var jobEffectDescriptionTable = tables.Single(t => t.TableName == "6.5-7-1 兵种特效说明");
    var jobEffectAssignmentTable = tables.Single(t => t.TableName == "6.5-7-2 兵种特效分配");
    var jobEffectDescriptionRead = reader.Read(testProject, jobEffectDescriptionTable, tables);
    var jobEffectAssignmentRead = reader.Read(testProject, jobEffectAssignmentTable, tables);
    if (!jobEffectDescriptionRead.Validation.IsUsable || !jobEffectAssignmentRead.Validation.IsUsable)
    {
        throw new InvalidOperationException("兵种特效写入烟测读取说明/分配失败。");
    }

    var effectId = 0;
    var effectDescriptionRow = FindSmokeRowById(jobEffectDescriptionRead.Data, effectId);
    var effectAssignmentRow = FindSmokeRowById(jobEffectAssignmentRead.Data, effectId);
    var effectPersonOriginal = Convert.ToInt32(effectAssignmentRow["1号武将"], CultureInfo.InvariantCulture);
    var effectPersonChanged = effectPersonOriginal == 0 ? 1 : 0;
    var effectJobOriginal = Convert.ToInt32(effectAssignmentRow["兵种"], CultureInfo.InvariantCulture);
    var effectJobChanged = effectJobOriginal == 255 ? 0 : 255;
    var effectValueOriginal = Convert.ToInt32(effectAssignmentRow["特效值"], CultureInfo.InvariantCulture);
    var effectValueChanged = effectValueOriginal == 1 ? 2 : 1;
    effectDescriptionRow["介绍"] = "CCZ兵种特效烟测";
    effectAssignmentRow["1号武将"] = effectPersonChanged;
    effectAssignmentRow["兵种"] = effectJobChanged;
    effectAssignmentRow["特效值"] = effectValueChanged;
    var effectSaves = new[]
    {
        writer.Save(testProject, jobEffectDescriptionTable, jobEffectDescriptionRead.Data),
        writer.Save(testProject, jobEffectAssignmentTable, jobEffectAssignmentRead.Data)
    };
    var jobEffectDescriptionVerify = reader.Read(testProject, jobEffectDescriptionTable, tables);
    var jobEffectAssignmentVerify = reader.Read(testProject, jobEffectAssignmentTable, tables);
    var effectDescriptionActual = Convert.ToString(FindSmokeRowById(jobEffectDescriptionVerify.Data, effectId)["介绍"], CultureInfo.InvariantCulture) ?? string.Empty;
    var effectAssignmentVerifyRow = FindSmokeRowById(jobEffectAssignmentVerify.Data, effectId);
    var effectPersonActual = Convert.ToInt32(effectAssignmentVerifyRow["1号武将"], CultureInfo.InvariantCulture);
    var effectJobActual = Convert.ToInt32(effectAssignmentVerifyRow["兵种"], CultureInfo.InvariantCulture);
    var effectValueActual = Convert.ToInt32(effectAssignmentVerifyRow["特效值"], CultureInfo.InvariantCulture);
    if (!effectDescriptionActual.Contains("CCZ兵种特效烟测", StringComparison.Ordinal) ||
        effectPersonActual != effectPersonChanged ||
        effectJobActual != effectJobChanged ||
        effectValueActual != effectValueChanged ||
        effectSaves.Any(x => string.IsNullOrWhiteSpace(x.BackupPath) || !File.Exists(x.BackupPath)))
    {
        throw new InvalidOperationException($"兵种特效写入烟测复读失败：desc={effectDescriptionActual}, person={effectPersonActual}, job={effectJobActual}, value={effectValueActual}");
    }

    Console.WriteLine($"JOB_EFFECT_WRITE_SMOKE_OK id={effectId} 1号武将={effectPersonOriginal}->{effectPersonActual} 兵种={effectJobOriginal}->{effectJobActual} 特效值={effectValueOriginal}->{effectValueActual} saves={effectSaves.Length}");

    RunJobStrategyWriteSmokeCore(testProject, tables);
}

static void RunJobStrategyWriteSmokeCore(CczProject testProject, IReadOnlyList<HexTableDefinition> tables)
{
    var reader = new HexTableReader();
    var writer = new HexTableWriter();
    var strategyTable = tables.Single(t => t.TableName == "6.5-5 策略");
    var strategyLearnTable = tables.Single(t => t.TableName == "6.5-5-7 学会策略");
    var strategyBattleAiTable = tables.Single(t => t.TableName == "6.5-5-8 战场AI策略限制");
    var strategyRead = reader.Read(testProject, strategyTable, tables);
    var strategyLearnRead = reader.Read(testProject, strategyLearnTable, tables);
    var strategyBattleAiRead = reader.Read(testProject, strategyBattleAiTable, tables);
    if (!strategyRead.Validation.IsUsable || !strategyLearnRead.Validation.IsUsable || !strategyBattleAiRead.Validation.IsUsable)
    {
        throw new InvalidOperationException("兵种策略写入烟测读取策略主表或 EKD5 附表失败。");
    }

    var strategyId = 0;
    var strategyRow = FindSmokeRowById(strategyRead.Data, strategyId);
    var strategyLearnRow = FindSmokeRowById(strategyLearnRead.Data, strategyId);
    var strategyBattleAiRow = FindSmokeRowById(strategyBattleAiRead.Data, strategyId);
    var jobLevelColumn = strategyRead.Data.Columns.Contains("0")
        ? "0"
        : strategyRead.Data.Columns.Cast<DataColumn>().First(c => int.TryParse(c.ColumnName, NumberStyles.Integer, CultureInfo.InvariantCulture, out _)).ColumnName;
    var strategyLevelOriginal = Convert.ToInt32(strategyRow[jobLevelColumn], CultureInfo.InvariantCulture);
    var strategyLevelChanged = strategyLevelOriginal == 1 ? 2 : 1;
    var strategyLearnOriginal = Convert.ToInt32(strategyLearnRow["内容"], CultureInfo.InvariantCulture);
    var strategyLearnChanged = strategyLearnOriginal == 1 ? 2 : 1;
    var strategyBattleAiOriginal = Convert.ToInt32(strategyBattleAiRow["内容"], CultureInfo.InvariantCulture);
    var strategyBattleAiChanged = strategyBattleAiOriginal == 1 ? 2 : 1;
    strategyRow[jobLevelColumn] = strategyLevelChanged;
    strategyLearnRow["内容"] = strategyLearnChanged;
    strategyBattleAiRow["内容"] = strategyBattleAiChanged;
    var strategySaves = new[]
    {
        writer.Save(testProject, strategyTable, strategyRead.Data),
        writer.Save(testProject, strategyLearnTable, strategyLearnRead.Data),
        writer.Save(testProject, strategyBattleAiTable, strategyBattleAiRead.Data)
    };
    var strategyVerify = reader.Read(testProject, strategyTable, tables);
    var strategyLearnVerify = reader.Read(testProject, strategyLearnTable, tables);
    var strategyBattleAiVerify = reader.Read(testProject, strategyBattleAiTable, tables);
    var strategyLevelActual = Convert.ToInt32(FindSmokeRowById(strategyVerify.Data, strategyId)[jobLevelColumn], CultureInfo.InvariantCulture);
    var strategyLearnActual = Convert.ToInt32(FindSmokeRowById(strategyLearnVerify.Data, strategyId)["内容"], CultureInfo.InvariantCulture);
    var strategyBattleAiActual = Convert.ToInt32(FindSmokeRowById(strategyBattleAiVerify.Data, strategyId)["内容"], CultureInfo.InvariantCulture);
    if (strategyLevelActual != strategyLevelChanged ||
        strategyLearnActual != strategyLearnChanged ||
        strategyBattleAiActual != strategyBattleAiChanged ||
        strategySaves.Any(x => string.IsNullOrWhiteSpace(x.BackupPath) || !File.Exists(x.BackupPath)))
    {
        throw new InvalidOperationException($"兵种策略写入烟测复读失败：level={strategyLevelActual}, learn={strategyLearnActual}, battleAi={strategyBattleAiActual}");
    }

    Console.WriteLine($"JOB_STRATEGY_WRITE_SMOKE_OK id={strategyId} 学会等级[{jobLevelColumn}]={strategyLevelOriginal}->{strategyLevelActual} 效果索引={strategyLearnOriginal}->{strategyLearnActual} AI战场={strategyBattleAiOriginal}->{strategyBattleAiActual} saves={strategySaves.Length}");
}

static void RunBattlefieldTextWriteSmoke(CczProject sourceProject, CczProject testProject, IReadOnlyList<HexTableDefinition> tables, string scenarioFileName)
{
    var dictionaryPath = ProjectDetector.FindSceneDictionaryPath(sourceProject);
    var dictionary = File.Exists(dictionaryPath) ? new SceneStringParser().Parse(dictionaryPath) : null;
    var scenario = new ScenarioFileReader()
        .ReadAllIndex(testProject)
        .FirstOrDefault(x => x.FileName.Equals(scenarioFileName, StringComparison.OrdinalIgnoreCase))
        ?? throw new InvalidOperationException($"战场制作写入烟测未找到测试副本剧本：{scenarioFileName}");
    var battlefieldService = new BattlefieldEditorService();
    var document = battlefieldService.Load(testProject, scenario, dictionary, tables, Array.Empty<ScenarioMapLinkInfo>());
    if (document.TitleEntry == null)
    {
        throw new InvalidOperationException($"战场制作写入烟测未在 {scenarioFileName} 找到标题文本线索。");
    }

    var originalTitle = BattlefieldEditorService.NormalizeText(document.TitleEntry.Text);
    var titleReplacement = string.Equals(originalTitle, "烟测关", StringComparison.Ordinal) ? "写测关" : "烟测关";
    if (EncodingService.GetGbkByteCount(titleReplacement) > document.TitleEntry.ByteLength)
    {
        titleReplacement = string.Equals(originalTitle, "烟测", StringComparison.Ordinal) ? "写测" : "烟测";
    }
    if (EncodingService.GetGbkByteCount(titleReplacement) > document.TitleEntry.ByteLength)
    {
        throw new InvalidOperationException($"战场制作标题容量不足：file={scenarioFileName}, capacity={document.TitleEntry.ByteLength}");
    }

    var conditions = document.ConditionEntry == null
        ? string.Empty
        : BattlefieldEditorService.NormalizeText(document.ConditionEntry.Text);
    var titleOffset = document.TitleEntry.Offset;
    var save = battlefieldService.SaveTitleAndConditions(testProject, document, titleReplacement, conditions, dictionary);
    var battlefieldReportJson = File.Exists(save.ReportJsonPath) ? File.ReadAllText(save.ReportJsonPath) : string.Empty;
    if (string.IsNullOrWhiteSpace(save.BackupPath) ||
        !File.Exists(save.BackupPath) ||
        string.IsNullOrWhiteSpace(save.ReportJsonPath) ||
        !File.Exists(save.ReportJsonPath) ||
        !battlefieldReportJson.Contains("\"OperationKind\": \"Legacy R/S eex full-structure write\"", StringComparison.Ordinal))
    {
        throw new InvalidOperationException("战场制作标题/胜败条件保存未生成 R/S eex 备份或结构化写入报告。");
    }

    var verifyPath = Path.Combine(testProject.GameRoot, "RS", scenarioFileName);
    var verifyTitle = new ScenarioTextReader().Read(verifyPath)
        .FirstOrDefault(x => x.Offset == titleOffset);
    var actualTitle = BattlefieldEditorService.NormalizeText(verifyTitle?.Text);
    if (!string.Equals(actualTitle, titleReplacement, StringComparison.Ordinal))
    {
        throw new InvalidOperationException($"战场制作标题复读失败：expected={titleReplacement}, actual={actualTitle}");
    }

    Console.WriteLine($"BATTLEFIELD_TEXT_WRITE_SMOKE_OK file={scenarioFileName} title='{originalTitle}'->'{actualTitle}' condition={(document.ConditionEntry == null ? "none" : "present")} backup={Path.GetFileName(save.BackupPath)}");
}

static void RunBattlefieldDeploymentWriteSmoke(CczProject sourceProject, CczProject testProject, IReadOnlyList<HexTableDefinition> tables, string scenarioFileName)
{
    var dictionaryPath = ProjectDetector.FindSceneDictionaryPath(sourceProject);
    if (!File.Exists(dictionaryPath))
    {
        throw new FileNotFoundException("Battlefield deployment write smoke requires CczString.ini.", dictionaryPath);
    }

    var dictionary = new SceneStringParser().Parse(dictionaryPath);
    var scenario = new ScenarioFileReader()
        .ReadAllIndex(testProject)
        .FirstOrDefault(x => x.FileName.Equals(scenarioFileName, StringComparison.OrdinalIgnoreCase))
        ?? throw new InvalidOperationException($"Battlefield deployment write smoke could not find {scenarioFileName}.");
    var service = new BattlefieldEditorService();
    var document = service.Load(testProject, scenario, dictionary, tables, Array.Empty<ScenarioMapLinkInfo>());
    var candidate = document.UnitCandidates.FirstOrDefault(x =>
        x.TargetKey.Contains("Record=", StringComparison.OrdinalIgnoreCase) &&
        BattlefieldEditorService.TryExtractFirstCoordinate(x, out _, out _) &&
        BattlefieldEditorService.TryExtractPersonId(x, out _));
    if (candidate == null)
    {
        var sample = string.Join(" | ", document.UnitCandidates.Take(12).Select(x => $"{x.Category}:{x.PersonHint}:{x.CoordinateHint}:{x.TargetKey}"));
        throw new InvalidOperationException($"Battlefield deployment write smoke found no writable 46/47 direct-coordinate candidate in {scenarioFileName}. sample={sample}");
    }

    if (!BattlefieldEditorService.TryExtractFirstCoordinate(candidate, out var originalX, out var originalY) ||
        !BattlefieldEditorService.TryExtractPersonId(candidate, out var personId))
    {
        throw new InvalidOperationException("Battlefield deployment candidate coordinate/person parse failed.");
    }

    var changedX = originalX == 0 ? 1 : 0;
    var changedY = originalY;
    var placement = new BattlefieldPlacedUnit
    {
        TargetKey = candidate.TargetKey,
        PersonId = personId,
        Name = "SmokeUnit",
        Faction = candidate.Category.Contains("敌军", StringComparison.Ordinal) ? "敌军" :
                  candidate.Category.Contains("友军", StringComparison.Ordinal) ? "友军" : "我军",
        AiMode = candidate.Category == "我军出场" ? "被动" : "主动",
        GridX = changedX,
        GridY = changedY,
        Source = "S剧本预览",
        Memo = "Smoke battlefield deployment write"
    };

    var write = new BattlefieldDeploymentWriteService().SaveScriptPlacements(
        testProject,
        scenario,
        dictionary,
        new[] { placement });
    if (write.WrittenRecordCount != 1 ||
        string.IsNullOrWhiteSpace(write.BackupPath) ||
        !File.Exists(write.BackupPath) ||
        string.IsNullOrWhiteSpace(write.ReportJsonPath) ||
        !File.Exists(write.ReportJsonPath))
    {
        throw new InvalidOperationException("Battlefield deployment write did not produce reread, backup, or report evidence.");
    }

    var verifyDocument = service.Load(testProject, scenario, dictionary, tables, Array.Empty<ScenarioMapLinkInfo>());
    var verifyCandidate = verifyDocument.UnitCandidates.FirstOrDefault(x => x.TargetKey.Equals(candidate.TargetKey, StringComparison.OrdinalIgnoreCase))
        ?? throw new InvalidOperationException("Battlefield deployment write reread lost target candidate.");
    if (!BattlefieldEditorService.TryExtractFirstCoordinate(verifyCandidate, out var actualX, out var actualY) ||
        actualX != changedX ||
        actualY != changedY)
    {
        throw new InvalidOperationException($"Battlefield deployment write reread failed: expected=({changedX},{changedY}), actual=({actualX},{actualY}).");
    }

    Console.WriteLine($"BATTLEFIELD_DEPLOYMENT_WRITE_SMOKE_OK file={scenarioFileName} target={candidate.TargetKey} person={personId} coord=({originalX},{originalY})->({actualX},{actualY}) backup={Path.GetFileName(write.BackupPath)} changedBytes={write.ChangedBytes}");

    var reviewService = new BattlefieldUnitReviewService();
    var localPlacement = new BattlefieldPlacedUnit
    {
        TargetKey = $"Placement#{scenarioFileName}#99,99#{personId}",
        PersonId = personId,
        Name = "SmokeLocalOnly",
        Faction = placement.Faction,
        AiMode = "被动",
        GridX = 2,
        GridY = 2,
        Source = "拖放",
        Memo = "Smoke local-only placement"
    };
    var reviewPath = reviewService.Save(testProject, verifyDocument, verifyDocument.UnitCandidates, new[] { placement, localPlacement });
    var savedPlacementReviews = reviewService.Load(testProject)
        .Where(x => x.IsPlacement && x.ScenarioFileName.Equals(scenarioFileName, StringComparison.OrdinalIgnoreCase))
        .ToList();
    var reloadedPlacements = reviewService.LoadPlacements(testProject, verifyDocument);
    if (savedPlacementReviews.Any(x => x.TargetKey.Equals(placement.TargetKey, StringComparison.OrdinalIgnoreCase)) ||
        reloadedPlacements.Any(x => x.TargetKey.Equals(placement.TargetKey, StringComparison.OrdinalIgnoreCase)) ||
        reloadedPlacements.Count(x => x.TargetKey.Equals(localPlacement.TargetKey, StringComparison.OrdinalIgnoreCase)) != 1)
    {
        throw new InvalidOperationException("Battlefield script-backed placement review cache was reloaded as a duplicate map unit.");
    }

    Console.WriteLine($"BATTLEFIELD_DEPLOYMENT_CACHE_DEDUP_OK file={scenarioFileName} local={localPlacement.TargetKey} scriptSkipped={placement.TargetKey} notes={Path.GetFileName(reviewPath)}");

    var emptySlot = FindOrCreateEmptyBattlefieldDeploymentSlot(testProject, scenario, dictionary, service, tables);
    if (emptySlot != null)
    {
        var emptyPlacement = new BattlefieldPlacedUnit
        {
            TargetKey = emptySlot.TargetKey,
            PersonId = personId,
            Name = "SmokeAutoDrop",
            Faction = emptySlot.Category.Contains("敌军", StringComparison.Ordinal) ? "敌军" : "友军",
            AiMode = "主动",
            GridX = changedX == 0 ? 1 : 0,
            GridY = changedY,
            Source = "纯拖放自动绑定",
            Memo = "Smoke battlefield empty slot auto-bind write"
        };
        var emptyWrite = new BattlefieldDeploymentWriteService().SaveScriptPlacements(
            testProject,
            scenario,
            dictionary,
            new[] { emptyPlacement });
        var emptyVerifyDocument = service.Load(testProject, scenario, dictionary, tables, Array.Empty<ScenarioMapLinkInfo>());
        var emptyVerify = emptyVerifyDocument.UnitCandidates.FirstOrDefault(x => x.TargetKey.Equals(emptySlot.TargetKey, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException("Battlefield empty deployment slot write did not become a visible candidate after reread.");
        var emptyPersonParsed = BattlefieldEditorService.TryExtractPersonId(emptyVerify, out var emptyPersonId);
        var emptyCoordinateParsed = BattlefieldEditorService.TryExtractFirstCoordinate(emptyVerify, out var emptyActualX, out var emptyActualY);
        if (!emptyPersonParsed ||
            !emptyCoordinateParsed ||
            emptyPersonId != personId ||
            emptyActualX != emptyPlacement.GridX ||
            emptyActualY != emptyPlacement.GridY)
        {
            throw new InvalidOperationException($"Battlefield empty slot auto-bind write reread failed: person={emptyPersonId}, coord=({emptyActualX},{emptyActualY}).");
        }

        Console.WriteLine($"BATTLEFIELD_DEPLOYMENT_EMPTY_SLOT_WRITE_OK file={scenarioFileName} target={emptySlot.TargetKey} person={personId} coord=({emptyPlacement.GridX},{emptyPlacement.GridY}) changedBytes={emptyWrite.ChangedBytes}");
    }

    var allyScenario = new ScenarioFileReader()
        .ReadAllIndex(testProject)
        .FirstOrDefault(x => x.FileName.Equals("S_01.eex", StringComparison.OrdinalIgnoreCase))
        ?? scenario;
    var allyDocument = service.Load(testProject, allyScenario, dictionary, tables, Array.Empty<ScenarioMapLinkInfo>());
    var allySlot = BattlefieldEditorService.BuildDeploymentSlotInfos(allyDocument)
        .FirstOrDefault(x => x.IsAllySlot && x.GridX >= 0 && x.GridY >= 0);
    if (allySlot != null)
    {
        var allyPlacement = new BattlefieldPlacedUnit
        {
            TargetKey = allySlot.TargetKey,
            PersonId = personId,
            Name = "SmokeAllySlot",
            Faction = "我军",
            AiMode = "被动",
            Direction = "下",
            Hidden = false,
            GridX = allySlot.GridX == 0 ? 1 : 0,
            GridY = allySlot.GridY,
            Source = "纯拖放自动绑定",
            Memo = "Smoke battlefield 4B slot auto-bind write"
        };
        var allyWrite = new BattlefieldDeploymentWriteService().SaveScriptPlacements(
            testProject,
            allyScenario,
            dictionary,
            new[] { allyPlacement });
        var allyVerifyDocument = service.Load(testProject, allyScenario, dictionary, tables, Array.Empty<ScenarioMapLinkInfo>());
        var allyVerify = allyVerifyDocument.UnitCandidates.FirstOrDefault(x => x.TargetKey.Equals(allySlot.TargetKey, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException("Battlefield 4B slot write reread lost target candidate.");
        if (!BattlefieldEditorService.TryExtractFirstCoordinate(allyVerify, out var allyActualX, out var allyActualY) ||
            allyActualX != allyPlacement.GridX ||
            allyActualY != allyPlacement.GridY)
        {
            throw new InvalidOperationException($"Battlefield 4B slot write reread failed: expected=({allyPlacement.GridX},{allyPlacement.GridY}), actual=({allyActualX},{allyActualY}).");
        }

        Console.WriteLine($"BATTLEFIELD_DEPLOYMENT_ALLY_SLOT_WRITE_OK file={allyScenario.FileName} target={allySlot.TargetKey} coord=({allySlot.GridX},{allySlot.GridY})->({allyActualX},{allyActualY}) changedBytes={allyWrite.ChangedBytes}");
    }
}

static BattlefieldDeploymentSlotInfo? FindOrCreateEmptyBattlefieldDeploymentSlot(
    CczProject testProject,
    ScenarioFileInfo scenario,
    SceneStringDocument dictionary,
    BattlefieldEditorService service,
    IReadOnlyList<HexTableDefinition> tables)
{
    var document = service.Load(testProject, scenario, dictionary, tables, Array.Empty<ScenarioMapLinkInfo>());
    var existing = BattlefieldEditorService.BuildDeploymentSlotInfos(document)
        .FirstOrDefault(x => !x.IsAllySlot && x.IsBlank && x.WritesPerson);
    if (existing != null) return existing;

    var legacyDocument = new LegacyScenarioReader().Read(scenario.Path, dictionary);
    var command = legacyDocument.EnumerateCommands()
        .FirstOrDefault(x => x.CommandId is 0x46 or 0x47 && TryGetDeploymentRecordLayout(x.CommandId, out var layout) && x.Parameters.Count >= layout.GroupSize);
    if (command == null) return null;

    var (groupSize, recordCount) = GetDeploymentRecordLayout(command.CommandId);
    for (var recordIndex = recordCount - 1; recordIndex >= 0; recordIndex--)
    {
        var start = recordIndex * groupSize;
        if (start + groupSize > command.Parameters.Count) continue;

        for (var index = 0; index < groupSize; index++)
        {
            var parameter = command.Parameters[start + index];
            parameter.IntValue = 0;
            parameter.Text = string.Empty;
            parameter.Values.Clear();
        }

        new LegacyScenarioWriter().Save(
            testProject,
            Path.Combine("RS", scenario.FileName),
            legacyDocument,
            dictionary,
            "Smoke synthesize empty battlefield 46/47 deployment slot");

        var reread = service.Load(testProject, scenario, dictionary, tables, Array.Empty<ScenarioMapLinkInfo>());
        return BattlefieldEditorService.BuildDeploymentSlotInfos(reread)
            .FirstOrDefault(x => !x.IsAllySlot && x.IsBlank && x.WritesPerson);
    }

    return null;
}

static bool TryGetDeploymentRecordLayout(int commandId, out (int GroupSize, int RecordCount) layout)
{
    if (commandId == 0x46)
    {
        layout = (11, 20);
        return true;
    }

    if (commandId == 0x47)
    {
        layout = (12, 80);
        return true;
    }

    layout = default;
    return false;
}

static (int GroupSize, int RecordCount) GetDeploymentRecordLayout(int commandId)
    => TryGetDeploymentRecordLayout(commandId, out var layout)
        ? layout
        : throw new ArgumentOutOfRangeException(nameof(commandId), commandId, "Unsupported deployment command.");

static void RunMapImageWriteSmoke(CczProject testProject)
{
    var mapRoot = Path.Combine(testProject.GameRoot, "Map");
    var targetPath = FindFirstJpegMap(mapRoot)
        ?? throw new FileNotFoundException("地图底图写入烟测找不到测试副本 Map\\*.jpg。", mapRoot);
    var replacementRoot = Path.Combine(testProject.GameRoot, "_CCZModStudio_SmokeInputs");
    Directory.CreateDirectory(replacementRoot);
    var replacementPath = Path.Combine(replacementRoot, "Replacement_" + Path.GetFileName(targetPath));

    int width;
    int height;
    using (var sourceImage = System.Drawing.Image.FromFile(targetPath))
    {
        width = sourceImage.Width;
        height = sourceImage.Height;
        using var bitmap = new System.Drawing.Bitmap(width, height);
        using (var graphics = System.Drawing.Graphics.FromImage(bitmap))
        {
            graphics.DrawImage(sourceImage, 0, 0, width, height);
            using var brush = new System.Drawing.SolidBrush(System.Drawing.Color.FromArgb(220, 32, 64));
            graphics.FillRectangle(brush, 0, 0, Math.Min(12, width), Math.Min(12, height));
        }

        bitmap.Save(replacementPath, System.Drawing.Imaging.ImageFormat.Jpeg);
    }

    var result = new MapImageReplaceService().ReplaceMapImage(testProject, targetPath, replacementPath);
    var targetHash = WriteOperationReportService.ComputeSha256(File.ReadAllBytes(targetPath));
    var replacementHash = WriteOperationReportService.ComputeSha256(File.ReadAllBytes(replacementPath));
    if (!targetHash.Equals(replacementHash, StringComparison.OrdinalIgnoreCase) ||
        result.NewWidth != width ||
        result.NewHeight != height ||
        string.IsNullOrWhiteSpace(result.BackupPath) ||
        !File.Exists(result.BackupPath) ||
        string.IsNullOrWhiteSpace(result.ReportJsonPath) ||
        !File.Exists(result.ReportJsonPath) ||
        !File.ReadAllText(result.ReportJsonPath).Contains("\"OperationKind\": \"地图底图替换\"", StringComparison.Ordinal))
    {
        throw new InvalidOperationException("地图底图替换烟测复读、备份或结构化报告验证失败。");
    }

    Console.WriteLine($"MAP_IMAGE_WRITE_SMOKE_OK map={Path.GetFileName(targetPath)} size={result.OldWidth}x{result.OldHeight}->{result.NewWidth}x{result.NewHeight} changed={result.ChangedBytesEstimate} backup={Path.GetFileName(result.BackupPath)}");
}

static void RunHexzmapWriteSmoke(CczProject sourceProject, CczProject testProject)
{
    var terrainNameLookup = HexzmapProbeReader.BuildTerrainNameLookup(new MaterialLibraryIndexer().Index(sourceProject));
    var reader = new HexzmapProbeReader();
    var probe = reader.Read(testProject, terrainNameLookup);
    if (probe.Blocks.Count == 0)
    {
        throw new InvalidOperationException("Hexzmap 写入烟测没有读取到任何候选地形块。");
    }

    var block = probe.Blocks[0];
    var cells = reader.GetBlockCells(probe, block);
    var original0 = cells[0];
    var changed0 = original0 == 0 ? (byte)1 : (byte)0;
    var original1 = cells[1];
    var changed1 = original1 == 2 ? (byte)3 : (byte)2;
    cells[0] = changed0;
    cells[1] = changed1;

    HexzmapSaveResult save;
    try
    {
        save = new HexzmapEditorService().SaveBlock(testProject, probe, block, cells, terrainNameLookup);
    }
    catch (InvalidOperationException ex) when (ex.Message.Contains("6.5/6.6x", StringComparison.Ordinal) &&
                                               ex.Message.Contains("已拒绝写入", StringComparison.Ordinal))
    {
        Console.WriteLine($"HEXZMAP_WRITE_SMOKE_SKIPPED guard={ex.Message}");
        return;
    }
    var verifyProbe = reader.Read(testProject, terrainNameLookup);
    var verifyBlock = verifyProbe.Blocks.First(x => x.Index == block.Index);
    var verifyCells = reader.GetBlockCells(verifyProbe, verifyBlock);
    if (verifyCells[0] != changed0 ||
        verifyCells[1] != changed1 ||
        save.ChangedCells != 2 ||
        string.IsNullOrWhiteSpace(save.BackupPath) ||
        !File.Exists(save.BackupPath) ||
        string.IsNullOrWhiteSpace(save.ReportJsonPath) ||
        !File.Exists(save.ReportJsonPath) ||
        !File.ReadAllText(save.ReportJsonPath).Contains("Hexzmap地形格写入", StringComparison.Ordinal))
    {
        throw new InvalidOperationException($"Hexzmap 地形写入烟测失败：expected={changed0}/{changed1}, actual={verifyCells[0]}/{verifyCells[1]}, changed={save.ChangedCells}");
    }

    Console.WriteLine($"HEXZMAP_WRITE_SMOKE_OK {block.MapId} cell0=0x{original0:X2}->0x{changed0:X2} cell1=0x{original1:X2}->0x{changed1:X2} changed={save.ChangedCells} backup={Path.GetFileName(save.BackupPath)}");
}

static void RunMapWorkbenchSmoke(CczProject sourceProject, CczProject testProject)
{
    var materialRoot = MaterialLibraryIndexer.ResolveMaterialLibraryRoot(sourceProject)
        ?? throw new DirectoryNotFoundException("Map workbench smoke requires a material library.");
    var materials = new MaterialLibraryIndexer().IndexExplicitRoot(materialRoot);
    var material = materials.FirstOrDefault()
        ?? throw new InvalidOperationException("Map workbench smoke could not find any material image.");

    var resources = new GameResourceIndexer().Index(testProject);
    var mapItem = resources
        .Where(x => x.Category == "地图图片" && x.GridWidth > 0 && x.GridHeight > 0)
        .OrderBy(x => x.Id)
        .FirstOrDefault()
        ?? throw new InvalidOperationException("Map workbench smoke could not find a grid-aligned map image.");

    var terrainLookup = HexzmapProbeReader.BuildTerrainNameLookup(materials);
    var hexReader = new HexzmapProbeReader();
    var probe = hexReader.Read(testProject, terrainLookup);
    var mapId = Path.GetFileNameWithoutExtension(mapItem.Name);
    if (mapId.Length > 1 && (mapId[0] == 'M' || mapId[0] == 'm') &&
        int.TryParse(mapId[1..], NumberStyles.Integer, CultureInfo.InvariantCulture, out var mapNumber))
    {
        mapId = $"M{mapNumber:D3}";
    }

    var block = probe.Blocks.FirstOrDefault(x =>
        x.MapId.Equals(mapId, StringComparison.OrdinalIgnoreCase) &&
        x.Width == mapItem.GridWidth &&
        x.Height == mapItem.GridHeight &&
        x.SegmentLength == mapItem.GridCellCount + HexzmapProbeReader.TerrainHeaderSize)
        ?? throw new InvalidOperationException($"Map workbench smoke could not find a matching Hexzmap block for {mapItem.Name}.");

    var draftService = new MapDraftService();
    var composePublishService = new MapCanvasPublishService();
    var draft = draftService.CreateDraftFromMap(testProject, mapItem, materialRoot);
    draft.TerrainCells = hexReader.GetBlockCells(probe, block);
    draft.MapCellOverrides.Add(new MapCellOverride
    {
        Index = 0,
        MaterialRelativePath = MapDraftService.GetMaterialRelativePath(materialRoot, material.FilePath),
        MaterialCategory = material.Category,
        DisplayName = material.FileName
    });
    if (draft.TerrainCells.Length < 2)
    {
        throw new InvalidOperationException("Map workbench smoke requires at least two terrain cells.");
    }

    var terrain0 = draft.TerrainCells[0];
    var terrain1 = draft.TerrainCells[1];
    draft.TerrainCells[0] = terrain0 == 7 ? (byte)8 : (byte)7;
    draft.TerrainCells[1] = terrain1 == 9 ? (byte)10 : (byte)9;

    draftService.SaveDraft(testProject, draft);
    var reloaded = draftService.LoadDraft(testProject, draft.DraftId);
    if (reloaded.GridWidth != draft.GridWidth ||
        reloaded.GridHeight != draft.GridHeight ||
        reloaded.MapCellOverrides.Count != 1 ||
        !reloaded.TerrainCells.SequenceEqual(draft.TerrainCells))
    {
        throw new InvalidOperationException("Map workbench draft save/reload smoke failed.");
    }

    var settings = new MapWorkbenchSettings
    {
        LastDraftId = draft.DraftId,
        LastBoundMapId = draft.BoundMapId,
        LastMaterialRoot = materialRoot
    };
    draftService.SaveSettings(testProject, settings);
    var reloadedSettings = draftService.LoadSettings(testProject);
    if (reloadedSettings.LastDraftId != draft.DraftId ||
        !reloadedSettings.LastMaterialRoot.Equals(materialRoot, StringComparison.OrdinalIgnoreCase))
    {
        throw new InvalidOperationException("Map workbench settings save/reload smoke failed.");
    }

    var exportPath = Path.Combine(testProject.WorkspaceRoot, "CCZModStudio_Exports", "MapWorkbenchSmoke", $"{Path.GetFileNameWithoutExtension(mapItem.Name)}_workbench.jpg");
    composePublishService.ExportJpeg(reloaded, exportPath);
    using (var exported = System.Drawing.Image.FromFile(exportPath))
    {
        var expectedWidth = draft.GridWidth * ResourceIndexItem.MapTilePixelSize;
        var expectedHeight = draft.GridHeight * ResourceIndexItem.MapTilePixelSize;
        if (exported.Width != expectedWidth || exported.Height != expectedHeight)
        {
            throw new InvalidOperationException($"Map workbench export dimensions failed: {exported.Width}x{exported.Height}, expected {expectedWidth}x{expectedHeight}.");
        }
    }

    var mapPublish = composePublishService.PublishToMapImage(testProject, reloaded, mapItem);
    if (string.IsNullOrWhiteSpace(mapPublish.BackupPath) ||
        !File.Exists(mapPublish.BackupPath) ||
        string.IsNullOrWhiteSpace(mapPublish.ReportJsonPath) ||
        !File.Exists(mapPublish.ReportJsonPath) ||
        !File.ReadAllText(mapPublish.ReportJsonPath).Contains("地图工作台底图发布", StringComparison.Ordinal))
    {
        throw new InvalidOperationException("Map workbench map publish backup/report smoke failed.");
    }
    using (var mapVerify = System.Drawing.Image.FromFile(mapItem.Path))
    {
        if (mapVerify.Width != reloaded.PixelWidth || mapVerify.Height != reloaded.PixelHeight)
        {
            throw new InvalidOperationException("Map workbench map publish reread dimension smoke failed.");
        }
    }

    HexzmapSaveResult terrainSave;
    try
    {
        terrainSave = new HexzmapEditorService().SaveBlock(testProject, probe, block, reloaded.TerrainCells, terrainLookup);
    }
    catch (InvalidOperationException ex) when (ex.Message.Contains("6.5/6.6x", StringComparison.Ordinal) &&
                                               ex.Message.Contains("已拒绝写入", StringComparison.Ordinal))
    {
        Console.WriteLine($"MAP_WORKBENCH_TERRAIN_SMOKE_SKIPPED map={mapItem.Name} export={Path.GetFileName(exportPath)} mapBackup={Path.GetFileName(mapPublish.BackupPath)} guard={ex.Message}");
        return;
    }
    var verifyProbe = hexReader.Read(testProject, terrainLookup);
    var verifyBlock = verifyProbe.Blocks.First(x => x.Index == block.Index);
    var verifyCells = hexReader.GetBlockCells(verifyProbe, verifyBlock);
    if (!verifyCells.SequenceEqual(reloaded.TerrainCells) ||
        string.IsNullOrWhiteSpace(terrainSave.BackupPath) ||
        !File.Exists(terrainSave.BackupPath) ||
        string.IsNullOrWhiteSpace(terrainSave.ReportJsonPath) ||
        !File.Exists(terrainSave.ReportJsonPath))
    {
        throw new InvalidOperationException("Map workbench terrain publish reread, backup, or report smoke failed.");
    }

    Console.WriteLine($"MAP_WORKBENCH_SMOKE_OK map={mapItem.Name} grid={draft.GridWidth}x{draft.GridHeight} material={material.Category}/{material.FileName} export={Path.GetFileName(exportPath)} mapBackup={Path.GetFileName(mapPublish.BackupPath)} terrainChanged={terrainSave.ChangedCells}");
}

static string BuildBattlefieldCommandSignature(BattlefieldEditorDocument document)
    => string.Join("|", document.CommandCandidates.Take(20).Select(command =>
        $"{command.SceneIndex}:{command.SectionIndex}:{command.CommandIndex}:{command.OffsetHex}:{command.CommandIdHex}:{command.CommandName}"));

static string BuildBattlefieldUnitSignature(BattlefieldEditorDocument document)
    => string.Join("|", document.UnitCandidates.Take(30).Select(unit =>
        $"{unit.TargetKey}:{unit.Category}:{unit.SourceCommand}:{unit.PersonHint}:{unit.CoordinateHint}:{unit.AiHint}"));

static string BuildBattlefieldMapSignature(BattlefieldEditorDocument document)
{
    var link = document.MapLink;
    return link == null
        ? $"{document.Scenario.FileName}:<no-map>"
        : $"{document.Scenario.FileName}:{link.MapId}:{link.MapImageName}:{link.MapImageExists}:{link.HexzmapBlockExists}:{link.HexzmapOffsetHex}";
}

static int CountColorfulPixels(System.Drawing.Bitmap bitmap)
{
    var count = 0;
    for (var y = 0; y < bitmap.Height; y++)
    {
        for (var x = 0; x < bitmap.Width; x++)
        {
            var pixel = bitmap.GetPixel(x, y);
            if (pixel.A == 0) continue;
            var max = Math.Max(pixel.R, Math.Max(pixel.G, pixel.B));
            var min = Math.Min(pixel.R, Math.Min(pixel.G, pixel.B));
            if (max - min >= 16) count++;
        }
    }

    return count;
}

static int CountTransparentPixels(System.Drawing.Bitmap bitmap)
{
    var count = 0;
    for (var y = 0; y < bitmap.Height; y++)
    {
        for (var x = 0; x < bitmap.Width; x++)
        {
            if (bitmap.GetPixel(x, y).A == 0)
            {
                count++;
            }
        }
    }

    return count;
}

static bool AreHorizontalMirrors(System.Drawing.Bitmap left, System.Drawing.Bitmap right)
{
    if (left.Width != right.Width || left.Height != right.Height)
    {
        return false;
    }

    for (var y = 0; y < left.Height; y++)
    {
        for (var x = 0; x < left.Width; x++)
        {
            if (left.GetPixel(x, y).ToArgb() != right.GetPixel(left.Width - 1 - x, y).ToArgb())
            {
                return false;
            }
        }
    }

    return true;
}

static void AssertSMapping(int sImageId, int? jobId, int factionSlot, params int[] expected)
{
    var mapping = CharacterImageResourceService.ResolveSUnitImageMapping(sImageId, jobId, factionSlot);
    var actual = mapping.ImageNumbers.ToArray();
    if (!actual.SequenceEqual(expected))
    {
        throw new InvalidOperationException($"S 映射不符合预期：S={sImageId}, job={jobId?.ToString(CultureInfo.InvariantCulture) ?? "null"}, faction={factionSlot}, expected={string.Join("/", expected)}, actual={string.Join("/", actual)}");
    }
}

static LegacyScenarioCommandNode? FindLegacyBattlefieldCommand(LegacyScenarioDocument document, BattlefieldUnitCandidate candidate)
{
    if (!BattlefieldEditorService.TryParseScriptCommandLocator(candidate, out var scene, out var section, out var commandIndex, out var offsetHex, out var commandIdHex))
    {
        return null;
    }

    return document.EnumerateCommands().FirstOrDefault(command =>
        command.SceneIndex == scene &&
        command.SectionIndex == section &&
        command.CommandIndex == commandIndex &&
        (string.IsNullOrWhiteSpace(offsetHex) || string.Equals("0x" + command.FileOffset.ToString("X6", CultureInfo.InvariantCulture), offsetHex, StringComparison.OrdinalIgnoreCase)) &&
        (string.IsNullOrWhiteSpace(commandIdHex) || string.Equals(command.CommandIdHex, commandIdHex, StringComparison.OrdinalIgnoreCase)));
}

static (int SectionTextGroupCount, int SceneFallbackGroupCount, int UnassignedGroupCount, int AttachedTextNodeCount) BuildScriptEditorTreeSummary(
    ScenarioStructureProbeResult structure,
    IReadOnlyList<ScenarioTextEntry> texts)
{
    var buildScriptTree = typeof(MainForm).GetMethod("BuildScriptTree", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
        ?? throw new MissingMethodException("MainForm.BuildScriptTree");
    var scriptTreeField = typeof(MainForm).GetField("_scriptTree", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
        ?? throw new MissingFieldException("MainForm", "_scriptTree");

    buildScriptTree.Invoke(smokeForm, new object[] { structure, texts });
    var tree = scriptTreeField.GetValue(smokeForm) as TreeView
        ?? throw new InvalidOperationException("Unable to read script editor tree control.");

    var sectionTextGroups = 0;
    var sceneFallbackGroups = 0;
    var unassignedGroups = 0;
    var attachedTextNodeCount = 0;

    foreach (TreeNode root in tree.Nodes)
    {
        foreach (var node in EnumerateTreeNodes(root))
        {
            if (node.Text.StartsWith("Section文本线索", StringComparison.Ordinal))
            {
                sectionTextGroups++;
                attachedTextNodeCount += node.Nodes.Count;
            }
            else if (node.Text.StartsWith("Scene补充文本线索", StringComparison.Ordinal))
            {
                sceneFallbackGroups++;
                attachedTextNodeCount += node.Nodes.Count;
            }
            else if (node.Text.StartsWith("未归属文本线索", StringComparison.Ordinal))
            {
                unassignedGroups++;
                attachedTextNodeCount += node.Nodes.Count;
            }
        }
    }

    return (sectionTextGroups, sceneFallbackGroups, unassignedGroups, attachedTextNodeCount);
}

static IEnumerable<TreeNode> EnumerateTreeNodes(TreeNode root)
{
    yield return root;
    foreach (TreeNode child in root.Nodes)
    {
        foreach (var nested in EnumerateTreeNodes(child))
        {
            yield return nested;
        }
    }
}

static string? FindFirstJpegMap(string mapRoot)
{
    if (!Directory.Exists(mapRoot)) return null;
    return Directory
        .EnumerateFiles(mapRoot, "M*.jpg", SearchOption.TopDirectoryOnly)
        .Concat(Directory.EnumerateFiles(mapRoot, "M*.jpeg", SearchOption.TopDirectoryOnly))
        .OrderBy(path => path, StringComparer.CurrentCultureIgnoreCase)
        .FirstOrDefault();
}

static DataRow FindSmokeRowById(DataTable table, int id)
{
    foreach (DataRow row in table.Rows)
    {
        if (Convert.ToInt32(row["ID"], CultureInfo.InvariantCulture) == id) return row;
    }
    throw new InvalidOperationException($"烟测表 {table.TableName} 没有找到 ID={id}。");
}
