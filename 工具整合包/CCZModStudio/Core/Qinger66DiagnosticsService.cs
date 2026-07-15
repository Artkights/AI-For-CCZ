using CCZModStudio.Formats;
using CCZModStudio.Models;

namespace CCZModStudio.Core;

public sealed class Qinger66DiagnosticsService
{
    private static readonly string[] OtherCoreTableNames =
    [
        "6.5-0-4 R形象",
        "6.5-0-5 S形象",
        "6.5-1 物品（0-103）",
        "6.5-2 物品（104-255）",
        "6.5-1-1 物品说明（0-103）",
        "6.5-2-1 物品说明（104-255）",
        "6.5-3 兵种系",
        "6.5-4 详细兵种",
        "6.5-4-2 兵种成长",
        "6.5-7 兵种特效",
        "6.5-7-3 人物专属、套装专属",
        "6.5-8 战役名称",
        "6.5-8-1 商店数据"
    ];

    private static readonly IReadOnlyDictionary<string, string> RequiredResourceUsage = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["E5\\Item.e5"] = "6.6 item icons; field N maps to small #2N+1 and large #2N+2.",
        ["E5\\Mtem.e5"] = "6.6 strategy icons; field N maps to E5 image #N+1.",
        ["E5\\DT.e5"] = "6.6 dynamic images for 72-12/72-32.",
        ["E5\\Fb.e5"] = "6.6 half-body dialogue images.",
        ["E5\\U_select.e5"] = "6.6 command icons, terrain image, buff arrow, and custom R numeric images.",
        ["E5\\Pmap.e5"] = "6.6 scene terrain image resource."
    };

    public Qinger66Diagnostics Build(CczProject project, CczEngineProfile engine, IReadOnlyList<HexTableDefinition> tables, HexTableReader reader)
    {
        var applies = IsQinger66Candidate(project, engine);
        var coreTableNames = new[]
        {
            engine.TableHints.PersonTable,
            engine.TableHints.BiographyTable,
            engine.TableHints.CriticalQuoteTable,
            engine.TableHints.RetreatQuoteTable
        }.Concat(OtherCoreTableNames);
        var tableDiagnostics = applies
            ? coreTableNames.Select(name => BuildTableDiagnostic(project, tables, reader, name)).ToArray()
            : Array.Empty<Qinger66TableDiagnostic>();
        var resources = applies
            ? Ccz66RevisedLayout.RequiredE5Resources.Select(path => BuildResourceDiagnostic(project, path, "6.6-required-resource", RequiredResourceUsage.GetValueOrDefault(path, string.Empty))).ToArray()
            : Array.Empty<Qinger66ResourceDiagnostic>();
        var obsolete = applies
            ? Ccz66RevisedLayout.ObsoleteRuntimeFiles.Select(path => BuildResourceDiagnostic(project, path, "6.6-obsolete-runtime-file", "Legacy 6.1-6.5 runtime resource; 6.6 revised reads the E5 replacement when available.")).ToArray()
            : Array.Empty<Qinger66ResourceDiagnostic>();
        var itemAudit = applies
            ? new Qinger66ItemAuditService().Build(project, engine, tables).Summary
            : new Qinger66ItemAuditSummary();
        var quoteLayout = applies ? new RoleQuoteLayoutService().Resolve(project) : null;
        var obsoleteWarnings = applies
            ? obsolete.Select(item => $"{item.RelativePath}: ObsoleteRuntimeResource in 6.6; use E5/Item.e5 or E5/Mtem.e5 where applicable.").ToArray()
            : Array.Empty<string>();

        var warnings = new List<string>();
        if (applies)
        {
            if (engine.PathHint.StartsWith("6.", StringComparison.OrdinalIgnoreCase) &&
                !engine.PathHint.Equals(engine.VersionHint, StringComparison.OrdinalIgnoreCase))
            {
                warnings.Add($"PathHint={engine.PathHint} conflicts with detected engine {engine.VersionHint}; low-word/version evidence takes precedence.");
            }

            warnings.AddRange(resources.Where(item => !item.Exists).Select(item => $"Missing 6.6 resource: {item.RelativePath}."));
            warnings.AddRange(itemAudit.Warnings);
            if (quoteLayout != null)
            {
                warnings.Add("Role quote layout: " + RoleQuoteLayoutService.BuildSummary(quoteLayout));
                if (!string.IsNullOrWhiteSpace(quoteLayout.Warning)) warnings.Add(quoteLayout.Warning);
                if (quoteLayout.Legacy66RetreatRegionMayContainText)
                {
                    warnings.Add("Legacy Imsg.e5 @ 0x6B788 contains text-like data, but it is diagnostic evidence only; active 6.6 retreat quotes remain at 0x78050.");
                }
            }

            if (tableDiagnostics.Any(item => item.IsCrossVersionFallback))
            {
                warnings.Add("At least one requested table resolved through CrossVersionFallback; writes require explicit CrossVersionFallbackWrite.");
            }
        }

        return new Qinger66Diagnostics
        {
            Applies = applies,
            ProjectName = project.Name,
            GameRoot = project.GameRoot,
            EngineVersionHint = engine.VersionHint,
            TableVersionPrefix = engine.TableVersionPrefix,
            VersionResourceLowWord = engine.VersionResourceLowWord,
            ExeSize = engine.ExeSize,
            ExeSha256 = engine.ExeSha256,
            PathHint = engine.PathHint,
            PathHintConflictsWithEngine = !string.IsNullOrWhiteSpace(engine.PathHint) &&
                                          !engine.PathHint.Equals(engine.VersionHint, StringComparison.OrdinalIgnoreCase),
            IsCrossVersionFallback = tableDiagnostics.Any(item => item.IsCrossVersionFallback),
            TableStatusSummary = BuildSummary(tableDiagnostics),
            ItemAuditSummary = itemAudit,
            QuoteLayout = quoteLayout?.ToDiagnosticPayload(),
            Tables = tableDiagnostics,
            RequiredResources = resources,
            ObsoleteRuntimeFiles = obsolete,
            ObsoleteResourceWarnings = obsoleteWarnings,
            Warnings = warnings
        };
    }

    public static bool IsQinger66Candidate(CczProject project, CczEngineProfile engine)
        => Ccz66RevisedLayout.Is66(engine) &&
           (project.Name.Contains("清儿吕布传", StringComparison.OrdinalIgnoreCase) ||
            project.GameRoot.Contains("清儿吕布传", StringComparison.OrdinalIgnoreCase));

    private static Qinger66TableDiagnostic BuildTableDiagnostic(CczProject project, IReadOnlyList<HexTableDefinition> tables, HexTableReader reader, string tableName)
    {
        if (!HexTableNameResolver.TryResolveForProject(project, tables, tableName, out var table))
        {
            return new Qinger66TableDiagnostic
            {
                RequestedName = tableName,
                TableStatus = "Missing",
                Warnings = [$"Table was not resolved for project: {tableName}."]
            };
        }

        var validation = reader.Validate(project, table);
        return new Qinger66TableDiagnostic
        {
            RequestedName = tableName,
            ResolvedName = table.TableName,
            Version = table.Version,
            FileName = table.FileName,
            FilePath = validation.FilePath,
            FileExists = validation.FileExists,
            FileLength = validation.FileLength,
            DataPos = table.DataPos,
            EndOffsetExclusive = table.EndOffsetExclusive,
            RowCount = table.RowCount,
            RowSize = table.RowSize,
            TableStatus = validation.TableStatus,
            WriteRisk = validation.WriteRisk,
            IsUsable = validation.IsUsable,
            CanWrite = validation.CanWrite,
            IsNative66 = validation.IsNative66,
            IsCrossVersionFallback = validation.IsCrossVersionFallback,
            IsReadOnlyEvidenceOnly = validation.IsReadOnlyEvidenceOnly,
            SemanticValidationStatus = validation.SemanticValidationStatus,
            HiddenTailPolicy = validation.HiddenTailPolicy,
            EffectResolutionSource = validation.EffectResolutionSource,
            Warnings = validation.Warnings
        };
    }

    private static Qinger66TableStatusSummary BuildSummary(IReadOnlyList<Qinger66TableDiagnostic> tables)
        => new()
        {
            Total = tables.Count,
            Native66 = tables.Count(item => item.IsNative66),
            CrossVersionFallback = tables.Count(item => item.IsCrossVersionFallback),
            ReadOnlyEvidenceOnly = tables.Count(item => item.IsReadOnlyEvidenceOnly),
            Unusable = tables.Count(item => !item.IsUsable),
            Writable = tables.Count(item => item.CanWrite)
        };

    private static Qinger66ResourceDiagnostic BuildResourceDiagnostic(CczProject project, string relativePath, string status, string usage)
    {
        var fullPath = Ccz66RevisedLayout.ResolveResourcePath(project, relativePath);
        var info = new FileInfo(fullPath);
        return new Qinger66ResourceDiagnostic
        {
            RelativePath = relativePath,
            Path = fullPath,
            Exists = info.Exists,
            SizeBytes = info.Exists ? info.Length : 0,
            Status = status,
            Usage = usage
        };
    }
}
