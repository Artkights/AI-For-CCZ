using System.Data;
using System.Globalization;
using System.Text;
using System.Text.Json;
using CCZModStudio.Core;
using CCZModStudio.Formats;
using CCZModStudio.Models;

namespace CCZModStudio.McpServer;

public sealed class CczMcpRuntime
{
    private static readonly HashSet<string> CoreFileDenyList = new(StringComparer.OrdinalIgnoreCase)
    {
        "Ekd5.exe",
        "Data.e5",
        "Imsg.e5",
        "Star.e5",
        "Hexzmap.e5"
    };

    private static readonly HashSet<string> ResourceExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".eex",
        ".e5",
        ".E5S",
        ".jpg",
        ".jpeg",
        ".png",
        ".bmp",
        ".wav",
        ".mp3",
        ".wma",
        ".avi",
        ".wmv",
        ".txt",
        ".ini"
    };

    private readonly ProjectDetector _projectDetector = new();
    private readonly HexTableParser _tableParser = new();
    private readonly HexTableReader _tableReader = new();
    private readonly HexTableWriter _tableWriter = new();
    private readonly ScenarioFileReader _scenarioFileReader = new();
    private readonly ScenarioTextReader _scenarioTextReader = new();
    private readonly ScenarioTextWriter _scenarioTextWriter = new();
    private readonly LegacyScenarioReader _legacyScenarioReader = new();
    private readonly HexzmapProbeReader _hexzmapProbeReader = new();
    private readonly HexzmapEditorService _hexzmapEditor = new();
    private readonly MaterialLibraryIndexer _materialLibraryIndexer = new();
    private readonly MapImageReplaceService _mapImageReplace = new();
    private readonly E5ImageReplaceService _e5ImageReplace = new();
    private readonly ResourceReplaceService _resourceReplace = new();
    private readonly GameResourceIndexer _gameResourceIndexer = new();
    private readonly ResourceDiagnosticService _resourceDiagnosticService = new();
    private readonly ProjectAuditService _projectAudit = new();
    private readonly BackupManager _backupManager = new();
    private readonly TestCopyDiffService _testCopyDiff = new();
    private readonly ReleasePackageService _releasePackage = new();
    private readonly SceneStringParser _sceneStringParser = new();
    private readonly ScenarioCommandParameterTemplateService _scenarioCommandTemplates = new();
    private readonly CreatorNoteService _creatorNoteService = new();
    private readonly CreatorNoteNavigationService _creatorNoteNavigationService = new();

    public object DetectProject(string? gameRoot)
    {
        var project = LoadProject(gameRoot);
        return new
        {
            project.Name,
            project.WorkspaceRoot,
            project.GameRoot,
            project.HexTableXmlPath,
            project.SceneDictionaryPath,
            project.SceneEditorDirectory,
            project.ImageAssignerDirectory,
            project.MaterialLibraryRoot,
            project.PatchConfigRoot,
            project.IsTestCopy,
            Files = project.GetFileStatuses().Select(x => new
            {
                x.Name,
                x.Path,
                x.Exists,
                x.SizeBytes,
                x.Kind
            }),
            project.PathDiagnostics
        };
    }

    public object ListTables(string? gameRoot)
    {
        var project = LoadProject(gameRoot);
        var tables = LoadTables(project);
        return new
        {
            project.GameRoot,
            project.HexTableXmlPath,
            Count = tables.Count,
            Tables = tables.Select(table => new
            {
                table.TableName,
                table.Version,
                table.FileName,
                BeginId = table.BeginId,
                table.RowCount,
                table.RowSize,
                DataPosHex = "0x" + table.DataPos.ToString("X", CultureInfo.InvariantCulture),
                table.ReadOnly,
                Fields = table.Fields.Select(field => new
                {
                    field.ColumnName,
                    Kind = field.Kind.ToString(),
                    field.Size,
                    field.ConsumesBytes
                })
            })
        };
    }

    public object ReadTable(string? gameRoot, string tableName, List<int>? rowIds, List<string>? columns, string? keyword, int limit)
    {
        var project = LoadProject(gameRoot);
        var tables = LoadTables(project);
        var table = FindTable(tables, tableName);
        var result = _tableReader.Read(project, table, tables);
        var selectedColumns = ResolveColumns(result.Data, columns, includeId: true);
        var rowIdSet = rowIds is { Count: > 0 } ? rowIds.ToHashSet() : null;
        var effectiveLimit = NormalizeLimit(limit, 50, 500);

        var rows = result.Data.AsEnumerable()
            .Where(row => rowIdSet == null || rowIdSet.Contains(Convert.ToInt32(row["ID"], CultureInfo.InvariantCulture)))
            .Where(row => MatchesKeyword(row, selectedColumns, keyword))
            .Take(effectiveLimit)
            .Select(row => RowToDictionary(row, selectedColumns))
            .ToList();

        return new
        {
            table.TableName,
            table.FileName,
            Validation = new
            {
                result.Validation.IsUsable,
                result.Validation.FilePath,
                result.Validation.FileExists,
                result.Validation.FileLength,
                result.Validation.Warnings
            },
            TotalRows = result.Data.Rows.Count,
            ReturnedRows = rows.Count,
            Columns = selectedColumns.Select(x => x.ColumnName),
            Rows = rows
        };
    }

    public object WriteTableRows(string? gameRoot, string tableName, List<TableRowUpdate> updates, string? writeMode)
    {
        if (updates.Count == 0) throw new InvalidOperationException("updates must contain at least one row update.");

        var project = LoadProject(gameRoot);
        EnsureWriteMode(project, writeMode);
        var tables = LoadTables(project);
        var table = FindTable(tables, tableName);
        if (table.ReadOnly) throw new InvalidOperationException("The selected table is read-only.");

        var result = _tableReader.Read(project, table, tables);
        if (!result.Validation.IsUsable)
        {
            throw new InvalidOperationException("The selected table is not usable for writing.");
        }

        foreach (var update in updates)
        {
            var row = result.Data.AsEnumerable()
                .FirstOrDefault(x => Convert.ToInt32(x["ID"], CultureInfo.InvariantCulture) == update.RowId)
                ?? throw new InvalidOperationException($"Row ID {update.RowId} was not found in table {table.TableName}.");

            foreach (var (columnName, value) in update.Values)
            {
                if (columnName.Equals("ID", StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException("ID is synthetic and cannot be written.");
                }

                var column = FindColumn(result.Data, columnName);
                var field = column.ExtendedProperties["FieldDefinition"] as HexFieldDefinition;
                if (field == null || !field.ConsumesBytes)
                {
                    throw new InvalidOperationException($"Column {columnName} is derived or non-writable.");
                }

                row[column] = ConvertJsonValue(value);
            }
        }

        var save = _tableWriter.Save(project, table, result.Data);
        return new
        {
            save.FilePath,
            save.BackupPath,
            save.ReportJsonPath,
            save.RowsWritten,
            save.ChangedBytes,
            table.TableName
        };
    }

    public object ReadScenarioTexts(string? gameRoot, string relativePath, string? keyword, int limit)
    {
        var project = LoadProject(gameRoot);
        var filePath = ResolveProjectFile(project, relativePath, mustExist: true);
        EnsureScenarioTargetAllowed(project, filePath);
        var effectiveLimit = NormalizeLimit(limit, 100, 2000);
        var entries = _scenarioTextReader.Read(filePath, maxItems: 4096)
            .Where(entry => string.IsNullOrWhiteSpace(keyword) ||
                            entry.Text.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
                            entry.Preview.Contains(keyword, StringComparison.OrdinalIgnoreCase))
            .Take(effectiveLimit)
            .Select(entry => new
            {
                entry.Index,
                entry.OffsetHex,
                entry.ByteLength,
                entry.CharLength,
                entry.Kind,
                entry.HasNewLines,
                entry.Preview,
                entry.Text,
                entry.GbkByteCount,
                entry.RemainingBytes,
                entry.WriteStatus,
                entry.Annotation
            })
            .ToList();

        return new
        {
            RelativePath = NormalizeProjectRelativePath(project, filePath),
            FilePath = filePath,
            ReturnedRows = entries.Count,
            Entries = entries
        };
    }

    public object ListScenarioFiles(string? gameRoot, string? kind, string? keyword, int limit)
    {
        var project = LoadProject(gameRoot);
        var effectiveLimit = NormalizeLimit(limit, 200, 1000);
        var files = _scenarioFileReader.ReadAllIndex(project)
            .Where(file => MatchesScenarioFileKind(file, kind))
            .Where(file => string.IsNullOrWhiteSpace(keyword) ||
                           file.FileName.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
                           file.Id.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
                           file.Kind.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
                           file.Annotation.Contains(keyword, StringComparison.OrdinalIgnoreCase))
            .ToList();

        return new
        {
            project.GameRoot,
            TotalFiles = files.Count,
            ReturnedFiles = Math.Min(files.Count, effectiveLimit),
            RFiles = files.Count(file => file.Kind.Equals("R剧本", StringComparison.Ordinal)),
            SFiles = files.Count(file => file.Kind.Equals("S剧本", StringComparison.Ordinal)),
            Files = files.Take(effectiveLimit).Select(BuildScenarioFilePayload)
        };
    }

    public object ReadScenarioCommands(
        string? gameRoot,
        string relativePath,
        int? sceneIndex,
        int? sectionIndex,
        string? commandFilter,
        string? keyword,
        int limit)
    {
        var project = LoadProject(gameRoot);
        var filePath = ResolveScenarioFile(project, relativePath);
        var dictionary = LoadRequiredScenarioCommandDictionary(project);
        var document = _legacyScenarioReader.Read(filePath, dictionary);
        var effectiveLimit = NormalizeLimit(limit, 200, 2000);
        var commands = document.EnumerateCommands()
            .Where(command => !sceneIndex.HasValue || command.SceneIndex == sceneIndex.Value)
            .Where(command => !sectionIndex.HasValue || command.SectionIndex == sectionIndex.Value)
            .Where(command => MatchesLegacyCommandFilter(command, commandFilter))
            .Where(command => MatchesLegacyCommandKeyword(command, keyword))
            .ToList();

        return new
        {
            FilePath = filePath,
            RelativePath = NormalizeProjectRelativePath(project, filePath),
            document.Summary,
            document.SceneCount,
            document.SectionCount,
            document.CommandCount,
            TotalMatches = commands.Count,
            ReturnedCommands = Math.Min(commands.Count, effectiveLimit),
            Commands = commands.Take(effectiveLimit).Select(BuildLegacyScenarioCommandPayload),
            SafetyNote = "Read-only legacy scenario command view. Command structure writes must continue to use the verified legacy writer path and are not exposed by this MCP tool."
        };
    }

    public object SearchScenarioScripts(
        string? gameRoot,
        string keyword,
        string? relativePath,
        string? fileKind,
        int limit,
        int maxFiles)
    {
        if (string.IsNullOrWhiteSpace(keyword))
        {
            throw new InvalidOperationException("keyword is required.");
        }

        var project = LoadProject(gameRoot);
        var dictionary = LoadRequiredScenarioCommandDictionary(project);
        var effectiveLimit = NormalizeLimit(limit, 100, 1000);
        var effectiveMaxFiles = NormalizeLimit(maxFiles, 20, 200);
        var candidatePaths = ResolveScenarioSearchFiles(project, relativePath, fileKind)
            .Take(effectiveMaxFiles)
            .ToList();
        var matches = new List<object>();
        var errors = new List<object>();

        foreach (var path in candidatePaths)
        {
            try
            {
                var document = _legacyScenarioReader.Read(path, dictionary);
                foreach (var command in document.EnumerateCommands().Where(command => MatchesLegacyCommandKeyword(command, keyword)))
                {
                    if (matches.Count >= effectiveLimit) break;
                    matches.Add(new
                    {
                        Kind = "Command",
                        FileName = Path.GetFileName(path),
                        RelativePath = NormalizeProjectRelativePath(project, path),
                        Command = BuildLegacyScenarioCommandPayload(command)
                    });
                }

                if (matches.Count < effectiveLimit)
                {
                    foreach (var text in _scenarioTextReader.Read(path, maxItems: 4096).Where(text => MatchesScenarioTextKeyword(text, keyword)))
                    {
                        if (matches.Count >= effectiveLimit) break;
                        matches.Add(new
                        {
                            Kind = "Text",
                            FileName = Path.GetFileName(path),
                            RelativePath = NormalizeProjectRelativePath(project, path),
                            text.Index,
                            text.OffsetHex,
                            text.ByteLength,
                            TextKind = text.Kind,
                            Preview = TrimForMcp(FirstNonEmpty(text.Preview, text.Text), 400),
                            Text = TrimForMcp(text.Text, 800),
                            text.Annotation
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                errors.Add(new
                {
                    FileName = Path.GetFileName(path),
                    RelativePath = NormalizeProjectRelativePath(project, path),
                    Error = ex.Message
                });
            }

            if (matches.Count >= effectiveLimit) break;
        }

        return new
        {
            project.GameRoot,
            Keyword = keyword,
            FilesScanned = candidatePaths.Count,
            ReturnedMatches = matches.Count,
            Errors = errors,
            Matches = matches,
            SafetyNote = "Read-only R/S eex search. Use read_scenario_commands before any manual planning; structure writes are not exposed here."
        };
    }

    public object WriteScenarioTexts(string? gameRoot, string relativePath, List<ScenarioTextUpdate> updates, string? writeMode)
    {
        if (updates.Count == 0) throw new InvalidOperationException("updates must contain at least one scenario text update.");

        var project = LoadProject(gameRoot);
        var mode = EnsureWriteMode(project, writeMode);
        var filePath = ResolveProjectFile(project, relativePath, mustExist: true);
        EnsureScenarioTargetAllowed(project, filePath);
        var normalizedRelative = NormalizeProjectRelativePath(project, filePath);
        var entries = _scenarioTextReader.Read(filePath, maxItems: 4096).ToList();

        foreach (var update in updates)
        {
            var entry = entries.FirstOrDefault(x => x.Index == update.Index)
                ?? throw new InvalidOperationException($"Scenario text index {update.Index} was not found.");
            entry.Text = update.Text ?? string.Empty;
        }

        var save = mode == "test_copy"
            ? _scenarioTextWriter.SaveInPlaceToTestCopy(project, normalizedRelative, entries)
            : _scenarioTextWriter.SaveInPlace(project, normalizedRelative, entries, "MCP scenario text write");

        return new
        {
            save.FilePath,
            save.BackupPath,
            save.ReportJsonPath,
            save.EntriesWritten,
            save.ChangedBytes,
            RelativePath = normalizedRelative
        };
    }

    public object ListHexzmapBlocks(string? gameRoot, string? keyword, bool editableOnly, int limit)
    {
        var project = LoadProject(gameRoot);
        var terrainLookup = BuildTerrainNameLookup(project);
        var probe = _hexzmapProbeReader.Read(project, terrainLookup);
        var effectiveLimit = NormalizeLimit(limit, 200, 1000);
        var filtered = probe.Blocks
            .Where(block => !editableOnly || block.CanEdit)
            .Where(block => MatchesHexzmapBlockKeyword(block, keyword))
            .ToList();

        return new
        {
            project.GameRoot,
            HexzmapPath = probe.Path,
            probe.Magic,
            probe.MagicValid,
            probe.PayloadOffset,
            probe.PayloadLength,
            DirectoryTableOffsetHex = "0x" + probe.DirectoryTableOffset.ToString("X", CultureInfo.InvariantCulture),
            DirectoryEntryCount = probe.DirectoryEntries.Count,
            TotalBlocks = filtered.Count,
            ReturnedBlocks = Math.Min(filtered.Count, effectiveLimit),
            EditableBlocks = filtered.Count(block => block.CanEdit),
            TerrainDictionaryCount = terrainLookup.Count,
            probe.TrailingBytes,
            Blocks = filtered.Take(effectiveLimit).Select(BuildHexzmapBlockPayload),
            SafetyNote = "Read-only Hexzmap block listing. Use read_hexzmap_block to inspect cells before write_hexzmap_block."
        };
    }

    public object ReadHexzmapBlock(string? gameRoot, string mapId, bool includeCells, int maxRows)
    {
        if (string.IsNullOrWhiteSpace(mapId))
        {
            throw new InvalidOperationException("map_id is required, for example M000.");
        }

        var project = LoadProject(gameRoot);
        var terrainLookup = BuildTerrainNameLookup(project);
        var probe = _hexzmapProbeReader.Read(project, terrainLookup);
        var block = probe.Blocks.FirstOrDefault(x => x.MapId.Equals(mapId.Trim(), StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException($"Hexzmap block {mapId} was not found.");
        var cells = _hexzmapProbeReader.GetBlockCells(probe, block);
        var effectiveMaxRows = NormalizeLimit(maxRows, 120, 500);
        var cellRows = includeCells
            ? BuildHexzmapCellRows(cells, block.Width, terrainLookup, effectiveMaxRows)
            : Array.Empty<object>();

        return new
        {
            project.GameRoot,
            HexzmapPath = probe.Path,
            Block = BuildHexzmapBlockPayload(block),
            CellCount = cells.Length,
            ExpectedCellCount = block.Width * block.Height,
            IncludeCells = includeCells,
            MaxRows = effectiveMaxRows,
            ReturnedRows = includeCells ? Math.Min(block.Height, effectiveMaxRows) : 0,
            RowsTruncated = includeCells && block.Height > effectiveMaxRows,
            TopTerrains = BuildHexzmapTerrainCounts(cells, terrainLookup),
            Rows = cellRows,
            SafetyNote = "Read-only Hexzmap cells. Bounds are x=0..width-1 and y=0..height-1; write_hexzmap_block still enforces write_mode, version guard, backup, and reread verification."
        };
    }

    public object WriteHexzmapBlock(string? gameRoot, string mapId, List<HexzmapCellUpdate> changes, string? writeMode)
    {
        if (changes.Count == 0) throw new InvalidOperationException("changes must contain at least one Hexzmap cell update.");

        var project = LoadProject(gameRoot);
        EnsureWriteMode(project, writeMode);
        var probe = _hexzmapProbeReader.Read(project);
        var block = probe.Blocks.FirstOrDefault(x => x.MapId.Equals(mapId, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException($"Hexzmap block {mapId} was not found.");
        var cells = _hexzmapProbeReader.GetBlockCells(probe, block);
        if (cells.Length == 0) throw new InvalidOperationException($"Hexzmap block {mapId} has no editable cells.");

        foreach (var change in changes)
        {
            if (change.X < 0 || change.X >= block.Width || change.Y < 0 || change.Y >= block.Height)
            {
                throw new InvalidOperationException($"Cell ({change.X},{change.Y}) is outside {mapId} bounds {block.Width}x{block.Height}.");
            }

            if (change.TerrainId < byte.MinValue || change.TerrainId > byte.MaxValue)
            {
                throw new InvalidOperationException($"terrain_id must be 0..255. Received {change.TerrainId}.");
            }

            cells[change.Y * block.Width + change.X] = (byte)change.TerrainId;
        }

        var save = _hexzmapEditor.SaveBlock(project, probe, block, cells);
        return new
        {
            save.FilePath,
            save.BackupPath,
            save.ReportJsonPath,
            save.BlockIndex,
            save.MapId,
            save.OffsetHex,
            save.ChangedCells,
            save.ChangedBytes
        };
    }

    public object ReplaceMapImage(string? gameRoot, string targetRelativePath, string replacementPath, string? writeMode)
    {
        var project = LoadProject(gameRoot);
        EnsureWriteMode(project, writeMode);
        var targetPath = ResolveProjectFile(project, targetRelativePath, mustExist: true);
        var sourcePath = ResolveExternalFile(project, replacementPath);
        var result = _mapImageReplace.ReplaceMapImage(project, targetPath, sourcePath);
        return new
        {
            result.TargetPath,
            result.ReplacementPath,
            result.BackupPath,
            result.ReportJsonPath,
            result.OldSizeBytes,
            result.NewSizeBytes,
            result.OldWidth,
            result.OldHeight,
            result.NewWidth,
            result.NewHeight,
            result.ChangedBytesEstimate,
            result.FormatCheckSummary,
            result.Warning
        };
    }

    public object ListE5ImageEntries(string? gameRoot, string targetRelativePath, int limit)
    {
        var project = LoadProject(gameRoot);
        var targetPath = ResolveProjectFile(project, targetRelativePath, mustExist: true);
        EnsureE5ImageTargetAllowed(project, targetPath);
        var effectiveLimit = NormalizeLimit(limit, 2000, 10000);
        var entries = _e5ImageReplace.ReadIndex(targetPath);
        return new
        {
            TargetPath = targetPath,
            TargetRelativePath = NormalizeProjectRelativePath(project, targetPath),
            TotalEntries = entries.Count,
            ReturnedEntries = Math.Min(entries.Count, effectiveLimit),
            Entries = entries.Take(effectiveLimit).Select(entry => new
            {
                entry.ImageNumber,
                entry.Kind,
                entry.Length,
                IndexOffsetHex = "0x" + entry.IndexOffset.ToString("X", CultureInfo.InvariantCulture),
                DataOffsetHex = "0x" + entry.DataOffset.ToString("X", CultureInfo.InvariantCulture),
                entry.IndexOffset,
                entry.DataOffset
            })
        };
    }

    public object PreviewE5ImageReplace(string? gameRoot, string targetRelativePath, int imageNumber, string replacementPath, int? sourceImageNumber)
    {
        var project = LoadProject(gameRoot);
        var targetPath = ResolveProjectFile(project, targetRelativePath, mustExist: true);
        EnsureE5ImageTargetAllowed(project, targetPath);
        var sourcePath = ResolveExternalFile(project, replacementPath);
        var preview = sourceImageNumber.HasValue
            ? _e5ImageReplace.PreviewReplacementFromEntry(project, targetPath, imageNumber, sourcePath, sourceImageNumber.Value)
            : _e5ImageReplace.PreviewReplacement(project, targetPath, imageNumber, sourcePath);
        return BuildE5ImageReplacePayload(preview);
    }

    public object ReplaceE5ImageEntry(string? gameRoot, string targetRelativePath, int imageNumber, string replacementPath, string? writeMode, int? sourceImageNumber)
    {
        var project = LoadProject(gameRoot);
        EnsureWriteMode(project, writeMode);
        var targetPath = ResolveProjectFile(project, targetRelativePath, mustExist: true);
        EnsureE5ImageTargetAllowed(project, targetPath);
        var sourcePath = ResolveExternalFile(project, replacementPath);
        var result = sourceImageNumber.HasValue
            ? _e5ImageReplace.ReplaceFromEntry(project, targetPath, imageNumber, sourcePath, sourceImageNumber.Value)
            : _e5ImageReplace.Replace(project, targetPath, imageNumber, sourcePath);
        return new
        {
            result.BackupPath,
            result.ReportPath,
            result.ReportJsonPath,
            Preview = BuildE5ImageReplacePayload(result)
        };
    }

    public object ReplaceResource(string? gameRoot, string targetRelativePath, string replacementPath, string? writeMode, bool requireSameExtension)
    {
        var project = LoadProject(gameRoot);
        var mode = EnsureWriteMode(project, writeMode);
        var targetPath = ResolveProjectFile(project, targetRelativePath, mustExist: true);
        EnsureGenericResourceAllowed(project, targetPath);
        var sourcePath = ResolveExternalFile(project, replacementPath);

        var result = mode == "test_copy"
            ? _resourceReplace.ReplaceInTestCopy(project, targetPath, sourcePath, requireSameExtension)
            : _resourceReplace.Replace(project, targetPath, sourcePath, requireSameExtension);

        return new
        {
            result.TargetPath,
            result.ReplacementPath,
            result.BackupPath,
            result.ReportPath,
            result.ReportJsonPath,
            result.OldSizeBytes,
            result.NewSizeBytes,
            result.ChangedBytesEstimate,
            result.OldSha256,
            result.NewSha256,
            result.FormatCheckSummary,
            result.FormatWarnings,
            result.RiskSummary
        };
    }

    public object ListProjectResources(string? gameRoot, string? category, string? keyword, int limit)
    {
        var project = LoadProject(gameRoot);
        var resources = _gameResourceIndexer.Index(project);
        var effectiveLimit = NormalizeLimit(limit, 200, 5000);
        var filtered = resources
            .Where(item => MatchesResourceCategory(item, category))
            .Where(item => MatchesResourceKeyword(item, keyword))
            .ToList();

        return new
        {
            project.GameRoot,
            TotalResources = filtered.Count,
            ReturnedResources = Math.Min(filtered.Count, effectiveLimit),
            CategoryCounts = resources
                .GroupBy(item => item.Category)
                .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
                .Select(group => new { Category = group.Key, Count = group.Count() }),
            Resources = filtered.Take(effectiveLimit).Select(item => BuildResourceIndexPayload(project, item)),
            SafetyNote = "Read-only resource index. Use dedicated write tools for tables, scenario text, Hexzmap, Map images, and E5 image entries; use replace_resource only for non-core resources."
        };
    }

    public object RunResourceDiagnostics(string? gameRoot, string? severity, string? category, string? keyword, bool writeReport, int limit)
    {
        var project = LoadProject(gameRoot);
        var resources = _gameResourceIndexer.Index(project);
        var diagnostics = _resourceDiagnosticService.Analyze(resources);
        var effectiveLimit = NormalizeLimit(limit, 200, 2000);
        var filtered = diagnostics
            .Where(item => MatchesResourceDiagnosticSeverity(item, severity))
            .Where(item => string.IsNullOrWhiteSpace(category) || item.Category.Contains(category, StringComparison.OrdinalIgnoreCase))
            .Where(item => MatchesResourceDiagnosticKeyword(item, keyword))
            .ToList();
        var reportPath = writeReport ? WriteResourceDiagnosticReport(project, filtered) : null;

        return new
        {
            project.GameRoot,
            ReportPath = reportPath,
            TotalItems = filtered.Count,
            ReturnedItems = Math.Min(filtered.Count, effectiveLimit),
            ErrorCount = diagnostics.Count(item => item.Severity.Equals("Error", StringComparison.OrdinalIgnoreCase)),
            WarningCount = diagnostics.Count(item => item.Severity.Equals("Warn", StringComparison.OrdinalIgnoreCase)),
            InfoCount = diagnostics.Count(item => item.Severity.Equals("Info", StringComparison.OrdinalIgnoreCase)),
            Categories = diagnostics.Select(item => item.Category).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x).ToList(),
            Items = filtered.Take(effectiveLimit).Select(BuildResourceDiagnosticPayload),
            SafetyNote = "Read-only resource diagnostics. Findings identify missing, duplicate, naming, format, and map-dimension risks before replacement or release."
        };
    }

    public object AuditProject(string? gameRoot, bool writeReport, int limit)
    {
        var project = LoadProject(gameRoot);
        var tables = LoadTables(project);
        var items = _projectAudit.Analyze(project, tables);
        var effectiveLimit = NormalizeLimit(limit, 200, 1000);
        var reportPath = writeReport ? _projectAudit.WriteReport(project, items) : null;
        return new
        {
            project.GameRoot,
            project.IsTestCopy,
            ReportPath = reportPath,
            TotalItems = items.Count,
            ReturnedItems = Math.Min(items.Count, effectiveLimit),
            ErrorCount = items.Count(item => item.Severity.Equals("Error", StringComparison.OrdinalIgnoreCase)),
            WarningCount = items.Count(item => item.Severity.Equals("Warn", StringComparison.OrdinalIgnoreCase)),
            InfoCount = items.Count(item => item.Severity.Equals("Info", StringComparison.OrdinalIgnoreCase)),
            Items = items.Take(effectiveLimit).Select(item => new
            {
                item.Severity,
                item.Category,
                item.Name,
                item.Status,
                item.Detail,
                item.Path
            })
        };
    }

    public object CreateTestCopy(string? gameRoot)
    {
        var project = LoadProject(gameRoot);
        if (project.IsTestCopy)
        {
            throw new InvalidOperationException("create_test_copy must be run from a source project, not an existing test copy.");
        }

        var progressLines = new List<string>();
        var progress = new Progress<string>(line =>
        {
            if (progressLines.Count < 80) progressLines.Add(line);
        });
        var testCopyRoot = _backupManager.CreateTestCopy(project, progress);
        var testProject = _projectDetector.CreateProjectFromGameRoot(testCopyRoot);
        return new
        {
            SourceGameRoot = project.GameRoot,
            TestCopyRoot = testCopyRoot,
            MarkerPath = Path.Combine(testCopyRoot, "_CCZModStudio_TestCopy.txt"),
            testProject.IsTestCopy,
            Files = testProject.GetFileStatuses().Select(x => new
            {
                x.Name,
                x.Path,
                x.Exists,
                x.SizeBytes,
                x.Kind
            }),
            Progress = progressLines
        };
    }

    public object DiffTestCopy(string? gameRoot, bool writeReport, int limit)
    {
        var project = LoadProject(gameRoot);
        var items = _testCopyDiff.Analyze(project);
        var effectiveLimit = NormalizeLimit(limit, 200, 2000);
        var reportPath = writeReport ? _testCopyDiff.WriteReport(project, items) : null;
        return new
        {
            TestCopyRoot = project.GameRoot,
            SourceRoot = _testCopyDiff.ReadSourceRoot(project),
            ReportPath = reportPath,
            TotalItems = items.Count,
            ReturnedItems = Math.Min(items.Count, effectiveLimit),
            ModifiedItems = items.Count(item => item.Status.Equals("已修改", StringComparison.Ordinal)),
            AddedItems = items.Count(item => item.Status.Equals("新增", StringComparison.Ordinal)),
            MissingItems = items.Count(item => item.Status.Equals("缺失", StringComparison.Ordinal)),
            Items = items.Take(effectiveLimit).Select(item => new
            {
                item.Status,
                item.RelativePath,
                item.SourceSize,
                item.TestSize,
                item.SourceSha256,
                item.TestSha256,
                item.Detail,
                item.SourcePath,
                item.TestPath
            })
        };
    }

    public object CreateReleaseCopy(string? gameRoot)
    {
        var project = LoadProject(gameRoot);
        var diffItems = _testCopyDiff.Analyze(project);
        var progressLines = new List<string>();
        var progress = new Progress<string>(line =>
        {
            if (progressLines.Count < 120) progressLines.Add(line);
        });
        var result = _releasePackage.CreateReleaseCopy(project, diffItems, progress);
        return new
        {
            result.ReleaseRoot,
            result.ManifestPath,
            result.FilesCopied,
            result.BytesCopied,
            result.ChangedItems,
            result.ModifiedItems,
            result.AddedItems,
            result.MissingItems,
            Progress = progressLines
        };
    }

    public object ListCreatorNotes(string? gameRoot, string? scope, string? keyword, int limit)
    {
        var project = LoadProject(gameRoot);
        var notes = _creatorNoteService.Load(project);
        var effectiveLimit = NormalizeLimit(limit, 100, 1000);
        var filtered = notes
            .Where(note => string.IsNullOrWhiteSpace(scope) || note.Scope.Contains(scope, StringComparison.OrdinalIgnoreCase))
            .Where(note => string.IsNullOrWhiteSpace(keyword) || MatchesCreatorNoteKeyword(note, keyword))
            .ToList();

        return new
        {
            project.GameRoot,
            StorePath = _creatorNoteService.GetStorePath(project),
            TotalNotes = filtered.Count,
            ReturnedNotes = Math.Min(filtered.Count, effectiveLimit),
            ScopeCounts = notes
                .GroupBy(note => note.Scope)
                .OrderByDescending(group => group.Count())
                .ThenBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
                .Select(group => new { Scope = group.Key, Count = group.Count() }),
            Summary = _creatorNoteService.BuildSummary(project, notes),
            Notes = filtered.Take(effectiveLimit).Select(BuildCreatorNotePayload),
            SafetyNote = "Creator notes are project-side JSON records under CCZModStudio_Notes. They do not modify game files and are excluded from release copies."
        };
    }

    public object UpsertCreatorNote(
        string? gameRoot,
        string? id,
        string? scope,
        string? targetKey,
        string? title,
        string content,
        string? tags,
        string? sourceHint)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            throw new InvalidOperationException("content is required for a creator note.");
        }

        var project = LoadProject(gameRoot);
        var saved = _creatorNoteService.Upsert(project, new CreatorNote
        {
            Id = id ?? string.Empty,
            Scope = scope ?? "全局项目",
            TargetKey = targetKey ?? string.Empty,
            Title = title ?? string.Empty,
            Content = content,
            Tags = tags ?? string.Empty,
            SourceHint = sourceHint ?? "MCP"
        });

        return new
        {
            project.GameRoot,
            StorePath = _creatorNoteService.GetStorePath(project),
            Note = BuildCreatorNotePayload(saved),
            SafetyNote = "Saved under CCZModStudio_Notes with JSON backup on overwrite; no game files were modified."
        };
    }

    public object DeleteCreatorNote(string? gameRoot, string id)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            throw new InvalidOperationException("id is required.");
        }

        var project = LoadProject(gameRoot);
        var removed = _creatorNoteService.Delete(project, id);
        return new
        {
            project.GameRoot,
            StorePath = _creatorNoteService.GetStorePath(project),
            Id = id,
            Removed = removed,
            SafetyNote = "Deletion only updates the project-side creator notes JSON under CCZModStudio_Notes."
        };
    }

    public object ExportCreatorNotesCsv(string? gameRoot, string? scope, string? keyword)
    {
        var project = LoadProject(gameRoot);
        var notes = _creatorNoteService.Load(project)
            .Where(note => string.IsNullOrWhiteSpace(scope) || note.Scope.Contains(scope, StringComparison.OrdinalIgnoreCase))
            .Where(note => string.IsNullOrWhiteSpace(keyword) || MatchesCreatorNoteKeyword(note, keyword))
            .ToList();
        var path = _creatorNoteService.ExportCsv(project, notes);
        return new
        {
            project.GameRoot,
            ExportPath = path,
            ExportedNotes = notes.Count,
            SafetyNote = "CSV export is written under CCZModStudio_Exports/CreatorNotes and does not modify game files."
        };
    }

    public object ListKnowledgeEntries()
    {
        var root = FindKnowledgeRoot();
        var entries = Directory.Exists(root)
            ? Directory.GetFiles(root, "*.md", SearchOption.TopDirectoryOnly)
                .OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase)
                .Select(path => new
                {
                    Name = Path.GetFileName(path),
                    Path = path,
                    SizeBytes = new FileInfo(path).Length
                })
                .ToList()
            : [];

        return new
        {
            KnowledgeRoot = root,
            Count = entries.Count,
            Entries = entries
        };
    }

    public object ReadKnowledgeEntry(string name)
    {
        var root = FindKnowledgeRoot();
        if (!Directory.Exists(root)) throw new DirectoryNotFoundException(root);
        var fullPath = ResolveKnowledgeEntryPath(root, name);

        if (!File.Exists(fullPath)) throw new FileNotFoundException("Knowledge entry was not found.", fullPath);
        return new
        {
            Name = Path.GetFileName(fullPath),
            Path = fullPath,
            Text = File.ReadAllText(fullPath)
        };
    }

    public object SearchKnowledgeEntries(string keyword, int limit, int contextLines)
    {
        if (string.IsNullOrWhiteSpace(keyword))
        {
            throw new InvalidOperationException("keyword is required.");
        }

        var root = FindKnowledgeRoot();
        if (!Directory.Exists(root)) throw new DirectoryNotFoundException(root);
        var effectiveLimit = NormalizeLimit(limit, 50, 500);
        var effectiveContextLines = contextLines < 0 ? 0 : Math.Min(contextLines, 3);
        var matches = new List<object>();
        var totalMatches = 0;

        foreach (var path in Directory.GetFiles(root, "*.md", SearchOption.TopDirectoryOnly)
                     .OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase))
        {
            var lines = File.ReadAllLines(path);
            for (var i = 0; i < lines.Length; i++)
            {
                if (!lines[i].Contains(keyword, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                totalMatches++;
                if (matches.Count >= effectiveLimit)
                {
                    continue;
                }

                var snippetStart = Math.Max(0, i - effectiveContextLines);
                var snippetEnd = Math.Min(lines.Length - 1, i + effectiveContextLines);
                matches.Add(new
                {
                    Name = Path.GetFileName(path),
                    Path = path,
                    LineNumber = i + 1,
                    MatchedLine = lines[i].Trim(),
                    Snippet = string.Join(Environment.NewLine, lines.Skip(snippetStart).Take(snippetEnd - snippetStart + 1))
                });
            }
        }

        return new
        {
            KnowledgeRoot = root,
            Keyword = keyword,
            TotalMatches = totalMatches,
            ReturnedMatches = matches.Count,
            ContextLines = effectiveContextLines,
            Matches = matches
        };
    }

    public object ListScenarioCommandTemplates(string? gameRoot, string? keyword, string? category, string? status, int limit)
    {
        var project = LoadProject(gameRoot);
        var (dictionary, dictionaryPath) = LoadScenarioCommandDictionary(project);
        var effectiveLimit = NormalizeLimit(limit, 100, 1000);
        var filtered = _scenarioCommandTemplates.BuildCatalogItems(dictionary)
            .Where(item => MatchesScenarioCommandKeyword(item, keyword))
            .Where(item => string.IsNullOrWhiteSpace(category) || item.Category.Contains(category, StringComparison.OrdinalIgnoreCase))
            .Where(item => string.IsNullOrWhiteSpace(status) || item.Status.Contains(status, StringComparison.OrdinalIgnoreCase))
            .ToList();

        return new
        {
            project.GameRoot,
            SceneDictionaryPath = dictionaryPath,
            DictionaryLoaded = dictionary != null,
            DictionaryCommandCount = dictionary?.Commands.Count ?? 0,
            TotalItems = filtered.Count,
            ReturnedItems = Math.Min(filtered.Count, effectiveLimit),
            CoveredItems = filtered.Count(item => item.Status.Equals("已覆盖", StringComparison.Ordinal)),
            MissingItems = filtered.Count(item => item.Status.Equals("待补充", StringComparison.Ordinal)),
            Categories = filtered.Select(item => item.Category).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x).ToList(),
            Items = filtered.Take(effectiveLimit).Select(BuildScenarioCommandTemplatePayload)
        };
    }

    public object ReadScenarioCommandTemplate(string command, string? gameRoot)
    {
        if (string.IsNullOrWhiteSpace(command))
        {
            throw new InvalidOperationException("command is required. Use a command id such as 0x78 or a command/template name.");
        }

        var project = LoadProject(gameRoot);
        var (dictionary, dictionaryPath) = LoadScenarioCommandDictionary(project);
        var items = _scenarioCommandTemplates.BuildCatalogItems(dictionary);
        var matches = FindScenarioCommandTemplates(items, command.Trim()).ToList();

        if (matches.Count == 0)
        {
            return new
            {
                project.GameRoot,
                SceneDictionaryPath = dictionaryPath,
                DictionaryLoaded = dictionary != null,
                Query = command,
                MatchCount = 0,
                Message = "No scenario command template matched the query."
            };
        }

        if (matches.Count > 1)
        {
            return new
            {
                project.GameRoot,
                SceneDictionaryPath = dictionaryPath,
                DictionaryLoaded = dictionary != null,
                Query = command,
                MatchCount = matches.Count,
                Message = "Multiple scenario command templates matched the query. Use a precise id such as 0x78.",
                Candidates = matches.Take(20).Select(BuildScenarioCommandTemplatePayload)
            };
        }

        var item = matches[0];
        return new
        {
            project.GameRoot,
            SceneDictionaryPath = dictionaryPath,
            DictionaryLoaded = dictionary != null,
            Query = command,
            MatchCount = 1,
            Template = BuildScenarioCommandTemplatePayload(item),
            Detail = _scenarioCommandTemplates.BuildCatalogItemDetail(item)
        };
    }

    private CczProject LoadProject(string? gameRoot)
    {
        if (!string.IsNullOrWhiteSpace(gameRoot))
        {
            return _projectDetector.CreateProjectFromGameRoot(gameRoot);
        }

        var envGameRoot = Environment.GetEnvironmentVariable("CCZMODSTUDIO_GAME_ROOT");
        if (!string.IsNullOrWhiteSpace(envGameRoot))
        {
            return _projectDetector.CreateProjectFromGameRoot(envGameRoot);
        }

        return _projectDetector.DetectDefaultProject();
    }

    private IReadOnlyList<HexTableDefinition> LoadTables(CczProject project)
    {
        if (!File.Exists(project.HexTableXmlPath))
        {
            throw new FileNotFoundException(ProjectDetector.BuildMissingHexTableMessage(project), project.HexTableXmlPath);
        }

        return _tableParser.Load(project.HexTableXmlPath);
    }

    private static HexTableDefinition FindTable(IReadOnlyList<HexTableDefinition> tables, string tableName)
    {
        return tables.FirstOrDefault(x => x.TableName.Equals(tableName, StringComparison.OrdinalIgnoreCase))
               ?? tables.FirstOrDefault(x => x.TableName.Contains(tableName, StringComparison.OrdinalIgnoreCase))
               ?? throw new InvalidOperationException($"Table {tableName} was not found.");
    }

    private static DataColumn FindColumn(DataTable table, string columnName)
    {
        if (table.Columns.Contains(columnName)) return table.Columns[columnName]!;
        foreach (DataColumn column in table.Columns)
        {
            if (column.ColumnName.Equals(columnName, StringComparison.OrdinalIgnoreCase)) return column;
        }

        throw new InvalidOperationException($"Column {columnName} was not found in table {table.TableName}.");
    }

    private static IReadOnlyList<DataColumn> ResolveColumns(DataTable table, List<string>? columns, bool includeId)
    {
        var result = new List<DataColumn>();
        if (includeId && table.Columns.Contains("ID")) result.Add(table.Columns["ID"]!);

        if (columns is { Count: > 0 })
        {
            foreach (var column in columns)
            {
                var resolved = FindColumn(table, column);
                if (!result.Contains(resolved)) result.Add(resolved);
            }
        }
        else
        {
            foreach (DataColumn column in table.Columns)
            {
                if (!result.Contains(column)) result.Add(column);
            }
        }

        return result;
    }

    private static Dictionary<string, object?> RowToDictionary(DataRow row, IReadOnlyList<DataColumn> columns)
    {
        var values = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var column in columns)
        {
            var value = row[column];
            values[column.ColumnName] = value == DBNull.Value ? null : value;
        }

        return values;
    }

    private static bool MatchesKeyword(DataRow row, IReadOnlyList<DataColumn> columns, string? keyword)
    {
        if (string.IsNullOrWhiteSpace(keyword)) return true;
        return columns.Any(column =>
            Convert.ToString(row[column], CultureInfo.InvariantCulture)?.Contains(keyword, StringComparison.OrdinalIgnoreCase) == true);
    }

    private IReadOnlyDictionary<byte, string> BuildTerrainNameLookup(CczProject project)
    {
        try
        {
            return HexzmapProbeReader.BuildTerrainNameLookup(_materialLibraryIndexer.Index(project));
        }
        catch
        {
            return new Dictionary<byte, string>();
        }
    }

    private static bool MatchesHexzmapBlockKeyword(HexzmapBlockInfo block, string? keyword)
    {
        if (string.IsNullOrWhiteSpace(keyword)) return true;
        return block.MapId.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
               block.MapImageName.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
               block.OffsetHex.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
               block.DominantTerrainName.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
               block.TopTerrainIds.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
               block.TopTerrainNames.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
               block.UnknownTerrainIds.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
               block.Annotation.Contains(keyword, StringComparison.OrdinalIgnoreCase);
    }

    private static object BuildHexzmapBlockPayload(HexzmapBlockInfo block)
        => new
        {
            block.Index,
            block.MapId,
            block.MapImageName,
            block.MapImageExists,
            block.MapPixelWidth,
            block.MapPixelHeight,
            block.Width,
            block.Height,
            block.BytesRead,
            block.CanEdit,
            block.OffsetHex,
            block.DataOffset,
            block.SegmentOffset,
            SegmentOffsetHex = "0x" + block.SegmentOffset.ToString("X", CultureInfo.InvariantCulture),
            block.SegmentLength,
            block.UniqueTerrainCount,
            block.DominantTerrainId,
            DominantTerrainHex = "0x" + block.DominantTerrainId.ToString("X2", CultureInfo.InvariantCulture),
            block.DominantTerrainName,
            block.DominantTerrainCount,
            block.TopTerrainIds,
            block.TopTerrainNames,
            block.KnownTerrainCount,
            block.UnknownTerrainIds,
            block.Annotation
        };

    private static IReadOnlyList<object> BuildHexzmapTerrainCounts(byte[] cells, IReadOnlyDictionary<byte, string> terrainLookup)
    {
        var total = Math.Max(1, cells.Length);
        return cells
            .GroupBy(value => value)
            .OrderByDescending(group => group.Count())
            .ThenBy(group => group.Key)
            .Take(16)
            .Select(group => new
            {
                TerrainId = (int)group.Key,
                TerrainHex = "0x" + group.Key.ToString("X2", CultureInfo.InvariantCulture),
                TerrainName = ResolveTerrainName(terrainLookup, group.Key),
                Count = group.Count(),
                Percent = Math.Round(group.Count() * 100.0 / total, 2)
            })
            .Cast<object>()
            .ToList();
    }

    private static IReadOnlyList<object> BuildHexzmapCellRows(
        byte[] cells,
        int width,
        IReadOnlyDictionary<byte, string> terrainLookup,
        int maxRows)
    {
        if (cells.Length == 0 || width <= 0 || maxRows <= 0) return Array.Empty<object>();

        var height = (int)Math.Ceiling(cells.Length / (double)width);
        var rows = new List<object>();
        for (var y = 0; y < Math.Min(height, maxRows); y++)
        {
            var start = y * width;
            var count = Math.Min(width, cells.Length - start);
            if (count <= 0) break;
            var row = cells.AsSpan(start, count).ToArray();
            rows.Add(new
            {
                Y = y,
                TerrainIds = row.Select(value => (int)value).ToArray(),
                TerrainHex = row.Select(value => "0x" + value.ToString("X2", CultureInfo.InvariantCulture)).ToArray(),
                TerrainSummary = BuildHexzmapRowTerrainSummary(row, terrainLookup)
            });
        }

        return rows;
    }

    private static string BuildHexzmapRowTerrainSummary(byte[] row, IReadOnlyDictionary<byte, string> terrainLookup)
        => string.Join(" / ", row
            .GroupBy(value => value)
            .OrderByDescending(group => group.Count())
            .ThenBy(group => group.Key)
            .Take(8)
            .Select(group =>
            {
                var name = ResolveTerrainName(terrainLookup, group.Key);
                var label = string.IsNullOrWhiteSpace(name)
                    ? "0x" + group.Key.ToString("X2", CultureInfo.InvariantCulture)
                    : "0x" + group.Key.ToString("X2", CultureInfo.InvariantCulture) + "(" + name + ")";
                return label + ":" + group.Count().ToString(CultureInfo.InvariantCulture);
            }));

    private static string ResolveTerrainName(IReadOnlyDictionary<byte, string> terrainLookup, byte terrainId)
        => terrainLookup.TryGetValue(terrainId, out var name) ? name : string.Empty;

    private static object ConvertJsonValue(JsonElement value)
    {
        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString() ?? string.Empty,
            JsonValueKind.Number when value.TryGetInt64(out var longValue) => longValue,
            JsonValueKind.Number => value.GetRawText(),
            JsonValueKind.True => 1,
            JsonValueKind.False => 0,
            JsonValueKind.Null or JsonValueKind.Undefined => string.Empty,
            _ => value.ToString()
        };
    }

    private static int NormalizeLimit(int limit, int defaultValue, int maxValue)
    {
        if (limit <= 0) return defaultValue;
        return Math.Min(limit, maxValue);
    }

    private (SceneStringDocument? Dictionary, string DictionaryPath) LoadScenarioCommandDictionary(CczProject project)
    {
        var dictionaryPath = ProjectDetector.FindSceneDictionaryPath(project);
        if (!File.Exists(dictionaryPath))
        {
            return (null, dictionaryPath);
        }

        return (_sceneStringParser.Parse(dictionaryPath), dictionaryPath);
    }

    private SceneStringDocument LoadRequiredScenarioCommandDictionary(CczProject project)
    {
        var (dictionary, dictionaryPath) = LoadScenarioCommandDictionary(project);
        if (dictionary == null || dictionary.Commands.Count == 0)
        {
            throw new FileNotFoundException("Scenario command dictionary CczString.ini was not found or contains no commands.", dictionaryPath);
        }

        return dictionary;
    }

    private IReadOnlyList<string> ResolveScenarioSearchFiles(CczProject project, string? relativePath, string? fileKind)
    {
        if (!string.IsNullOrWhiteSpace(relativePath))
        {
            return new[] { ResolveScenarioFile(project, relativePath) };
        }

        return _scenarioFileReader.ReadAllIndex(project)
            .Where(file => MatchesScenarioFileKind(file, fileKind))
            .Select(file => file.Path)
            .ToList();
    }

    private static string ResolveScenarioFile(CczProject project, string relativePath)
    {
        var filePath = ResolveProjectFile(project, relativePath, mustExist: true);
        EnsureScenarioTargetAllowed(project, filePath);
        return filePath;
    }

    private static void EnsureScenarioTargetAllowed(CczProject project, string targetPath)
    {
        if (!Path.GetExtension(targetPath).Equals(".eex", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Scenario target must be an .eex file.");
        }

        if (!ScenarioFileReader.IsRsScriptFile(Path.GetFileName(targetPath)))
        {
            throw new InvalidOperationException("Scenario target must be an RS/R_*.eex or RS/S_*.eex script file.");
        }

        var relative = NormalizeProjectRelativePath(project, targetPath);
        var normalized = relative.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
        if (!normalized.StartsWith("RS" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Scenario target must be under the project RS directory.");
        }
    }

    private static bool MatchesScenarioFileKind(ScenarioFileInfo file, string? kind)
    {
        if (string.IsNullOrWhiteSpace(kind)) return true;
        var normalized = kind.Trim();
        if (normalized.Equals("R", StringComparison.OrdinalIgnoreCase)) return file.Kind.Equals("R剧本", StringComparison.Ordinal);
        if (normalized.Equals("S", StringComparison.OrdinalIgnoreCase)) return file.Kind.Equals("S剧本", StringComparison.Ordinal);
        return file.Kind.Contains(normalized, StringComparison.OrdinalIgnoreCase) ||
               file.FileName.StartsWith(normalized, StringComparison.OrdinalIgnoreCase);
    }

    private static object BuildScenarioFilePayload(ScenarioFileInfo file)
        => new
        {
            file.FileName,
            file.Id,
            file.Kind,
            file.Length,
            file.WordCount,
            file.UsedBytes,
            file.UsedPercent,
            file.TitleHint,
            file.TextHintCount,
            file.FirstTextOffsetHex,
            file.Annotation,
            file.UsageAnnotation,
            file.Path
        };

    private static bool MatchesLegacyCommandFilter(LegacyScenarioCommandNode command, string? commandFilter)
    {
        if (string.IsNullOrWhiteSpace(commandFilter)) return true;
        commandFilter = commandFilter.Trim();
        if (TryParseScenarioCommandId(commandFilter, out var id))
        {
            return command.CommandId == id;
        }

        return command.CommandName.Contains(commandFilter, StringComparison.OrdinalIgnoreCase) ||
               command.CommandIdHex.Contains(commandFilter, StringComparison.OrdinalIgnoreCase);
    }

    private static bool MatchesLegacyCommandKeyword(LegacyScenarioCommandNode command, string? keyword)
    {
        if (string.IsNullOrWhiteSpace(keyword)) return true;
        return command.CommandName.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
               command.CommandIdHex.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
               command.FileOffset.ToString(CultureInfo.InvariantCulture).Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
               ("0x" + command.FileOffset.ToString("X6", CultureInfo.InvariantCulture)).Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
               command.Parameters.Any(parameter =>
                   parameter.DisplayValue.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
                   parameter.LayoutCodeHex.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
                   parameter.TagHex.Contains(keyword, StringComparison.OrdinalIgnoreCase));
    }

    private static bool MatchesScenarioTextKeyword(ScenarioTextEntry text, string keyword) =>
        text.Text.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
        text.Preview.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
        text.Kind.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
        text.Annotation.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
        text.OffsetHex.Contains(keyword, StringComparison.OrdinalIgnoreCase);

    private static object BuildLegacyScenarioCommandPayload(LegacyScenarioCommandNode command)
        => new
        {
            command.CommandOrdinal,
            command.SceneIndex,
            command.SectionIndex,
            command.CommandIndex,
            command.CommandId,
            command.CommandIdHex,
            command.CommandName,
            OffsetHex = "0x" + command.FileOffset.ToString("X6", CultureInfo.InvariantCulture),
            command.FileOffset,
            command.ConsumedBytes,
            command.StartsBodyBlock,
            command.IsSubEventMarker,
            command.OpensSubEventBlock,
            command.EndsSubEventBlock,
            command.JumpTargetOrdinal,
            command.JumpTargetCommandIndex,
            command.OriginalJumpDisplacement,
            TextParameterCount = command.TextParameters.Count(),
            Parameters = command.Parameters.Select(BuildLegacyScenarioParameterPayload)
        };

    private static object BuildLegacyScenarioParameterPayload(LegacyScenarioCommandParameter parameter)
        => new
        {
            parameter.Index,
            Kind = parameter.Kind.ToString(),
            parameter.LayoutCode,
            parameter.LayoutCodeHex,
            parameter.Tag,
            parameter.TagHex,
            OffsetHex = "0x" + parameter.FileOffset.ToString("X6", CultureInfo.InvariantCulture),
            parameter.FileOffset,
            parameter.ByteLength,
            parameter.IntValue,
            Values = parameter.Values.Take(32).ToList(),
            ValuePreview = TrimForMcp(parameter.DisplayValue, 600)
        };

    private static bool MatchesScenarioCommandKeyword(ScenarioCommandTemplateCatalogItem item, string? keyword)
    {
        if (string.IsNullOrWhiteSpace(keyword)) return true;
        return item.IdHex.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
               item.DictionaryName.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
               item.TemplateName.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
               item.Status.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
               item.Category.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
               item.SlotSummary.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
               item.Purpose.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
               item.Risk.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
               item.CreatorTip.Contains(keyword, StringComparison.OrdinalIgnoreCase);
    }

    private static IEnumerable<ScenarioCommandTemplateCatalogItem> FindScenarioCommandTemplates(
        IReadOnlyList<ScenarioCommandTemplateCatalogItem> items,
        string query)
    {
        if (TryParseScenarioCommandId(query, out var id))
        {
            return items.Where(item => item.Id == id);
        }

        var exact = items.Where(item =>
            item.IdHex.Equals(query, StringComparison.OrdinalIgnoreCase) ||
            item.DictionaryName.Equals(query, StringComparison.OrdinalIgnoreCase) ||
            item.TemplateName.Equals(query, StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (exact.Count > 0) return exact;

        return items.Where(item => MatchesScenarioCommandKeyword(item, query));
    }

    private static bool TryParseScenarioCommandId(string text, out int id)
    {
        id = 0;
        text = text.Trim();
        if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            return int.TryParse(text[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out id);
        }

        if (text.Length <= 2 && text.All(IsHexDigit))
        {
            return int.TryParse(text, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out id);
        }

        return int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out id);
    }

    private static bool IsHexDigit(char value) =>
        value is >= '0' and <= '9' or >= 'a' and <= 'f' or >= 'A' and <= 'F';

    private static object BuildScenarioCommandTemplatePayload(ScenarioCommandTemplateCatalogItem item)
        => new
        {
            item.Id,
            item.IdHex,
            item.DictionaryName,
            item.TemplateName,
            item.Status,
            item.Category,
            item.SlotCount,
            item.SlotSummary,
            item.Purpose,
            item.Risk,
            item.CreatorTip,
            item.SafetyNote
        };

    private static bool MatchesResourceCategory(ResourceIndexItem item, string? category)
    {
        if (string.IsNullOrWhiteSpace(category)) return true;
        return item.Category.Contains(category.Trim(), StringComparison.OrdinalIgnoreCase);
    }

    private static bool MatchesResourceKeyword(ResourceIndexItem item, string? keyword)
    {
        if (string.IsNullOrWhiteSpace(keyword)) return true;
        keyword = keyword.Trim();
        return item.Category.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
               item.Id.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
               item.Name.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
               item.Extension.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
               item.Magic.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
               item.FormatHint.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
               item.Annotation.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
               item.Path.Contains(keyword, StringComparison.OrdinalIgnoreCase);
    }

    private static object BuildResourceIndexPayload(CczProject project, ResourceIndexItem item)
        => new
        {
            item.Category,
            item.Id,
            item.Name,
            item.Extension,
            item.SizeBytes,
            item.Magic,
            item.FormatHint,
            item.Width,
            item.Height,
            item.GridWidth,
            item.GridHeight,
            item.GridCellCount,
            item.Annotation,
            RelativePath = TryNormalizeProjectRelativePath(project, item.Path),
            item.Path
        };

    private static bool MatchesResourceDiagnosticSeverity(ResourceDiagnosticItem item, string? severity)
    {
        if (string.IsNullOrWhiteSpace(severity)) return true;
        var filters = severity
            .Split(new[] { ',', ';', '/', ' ' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(NormalizeSeverity)
            .Where(x => x.Length > 0)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        return filters.Count == 0 ||
               filters.Contains("All") ||
               filters.Contains(NormalizeSeverity(item.Severity));
    }

    private static string NormalizeSeverity(string value)
    {
        value = value.Trim();
        if (value.Equals("All", StringComparison.OrdinalIgnoreCase) ||
            value.Equals("全部", StringComparison.OrdinalIgnoreCase))
        {
            return "All";
        }

        if (value.Equals("Warning", StringComparison.OrdinalIgnoreCase) ||
            value.Equals("Warn", StringComparison.OrdinalIgnoreCase) ||
            value.Equals("警告", StringComparison.OrdinalIgnoreCase))
        {
            return "Warn";
        }

        if (value.Equals("Error", StringComparison.OrdinalIgnoreCase) ||
            value.Equals("错误", StringComparison.OrdinalIgnoreCase))
        {
            return "Error";
        }

        if (value.Equals("Info", StringComparison.OrdinalIgnoreCase) ||
            value.Equals("Information", StringComparison.OrdinalIgnoreCase) ||
            value.Equals("信息", StringComparison.OrdinalIgnoreCase))
        {
            return "Info";
        }

        return value;
    }

    private static bool MatchesResourceDiagnosticKeyword(ResourceDiagnosticItem item, string? keyword)
    {
        if (string.IsNullOrWhiteSpace(keyword)) return true;
        keyword = keyword.Trim();
        return item.Severity.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
               item.Category.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
               item.Rule.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
               item.Id.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
               item.Name.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
               item.Status.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
               item.Detail.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
               item.Suggestion.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
               item.Path.Contains(keyword, StringComparison.OrdinalIgnoreCase);
    }

    private static object BuildResourceDiagnosticPayload(ResourceDiagnosticItem item)
        => new
        {
            item.Severity,
            item.Category,
            item.Rule,
            item.Id,
            item.Name,
            item.Status,
            item.Detail,
            item.Suggestion,
            item.Path
        };

    private static bool MatchesCreatorNoteKeyword(CreatorNote note, string? keyword)
    {
        if (string.IsNullOrWhiteSpace(keyword)) return true;
        keyword = keyword.Trim();
        return ContainsIgnoreCase(note.Id, keyword) ||
               ContainsIgnoreCase(note.ProjectName, keyword) ||
               ContainsIgnoreCase(note.Scope, keyword) ||
               ContainsIgnoreCase(note.TargetKey, keyword) ||
               ContainsIgnoreCase(note.Title, keyword) ||
               ContainsIgnoreCase(note.Content, keyword) ||
               ContainsIgnoreCase(note.Tags, keyword) ||
               ContainsIgnoreCase(note.SourceHint, keyword);
    }

    private static bool ContainsIgnoreCase(string? value, string keyword)
        => value?.Contains(keyword, StringComparison.OrdinalIgnoreCase) == true;

    private object BuildCreatorNotePayload(CreatorNote note)
    {
        var target = _creatorNoteNavigationService.Parse(note);
        return new
        {
            note.Id,
            note.ProjectName,
            note.Scope,
            note.TargetKey,
            note.Title,
            Content = TrimForMcp(note.Content, 1200),
            note.Tags,
            note.SourceHint,
            CreatedAt = note.CreatedAtText,
            UpdatedAt = note.UpdatedAtText,
            note.SafetyNote,
            Navigation = new
            {
                target.IsRecognized,
                target.Kind,
                target.DisplayText,
                target.FileName,
                target.MapId,
                target.OffsetHex,
                target.Category,
                target.Name,
                target.Rule,
                target.Id,
                target.TableName,
                target.RowId,
                target.FieldName,
                target.RelativePath,
                target.SceneIndex,
                target.SectionIndex,
                target.CommandIndex,
                target.TextIndex
            }
        };
    }

    private static string WriteResourceDiagnosticReport(CczProject project, IReadOnlyList<ResourceDiagnosticItem> items)
    {
        var reportRoot = Path.Combine(project.WorkspaceRoot, "CCZModStudio_Reports");
        Directory.CreateDirectory(reportRoot);
        var stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);
        var reportPath = Path.Combine(reportRoot, $"{stamp}_ResourceDiagnostics.txt");
        var lines = new List<string>
        {
            "CCZModStudio Resource Diagnostics",
            "CreatedAt=" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
            "GameRoot=" + project.GameRoot,
            string.Empty,
            "Severity\tCategory\tRule\tId\tName\tStatus\tDetail\tSuggestion\tPath"
        };
        lines.AddRange(items.Select(item => string.Join('\t',
            SanitizeReportField(item.Severity),
            SanitizeReportField(item.Category),
            SanitizeReportField(item.Rule),
            SanitizeReportField(item.Id),
            SanitizeReportField(item.Name),
            SanitizeReportField(item.Status),
            SanitizeReportField(item.Detail),
            SanitizeReportField(item.Suggestion),
            SanitizeReportField(item.Path))));
        File.WriteAllLines(reportPath, lines, Encoding.UTF8);
        return reportPath;
    }

    private static string SanitizeReportField(string value) =>
        value.Replace('\t', ' ')
            .Replace("\r", " ", StringComparison.Ordinal)
            .Replace("\n", " ", StringComparison.Ordinal);

    private static string FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? string.Empty;

    private static string TrimForMcp(string? value, int maxChars)
    {
        value ??= string.Empty;
        value = value.Replace("\r", "\\r", StringComparison.Ordinal).Replace("\n", "\\n", StringComparison.Ordinal);
        return value.Length <= maxChars ? value : value[..maxChars] + "…";
    }

    private static string EnsureWriteMode(CczProject project, string? writeMode)
    {
        var normalized = string.IsNullOrWhiteSpace(writeMode) ? "direct" : writeMode.Trim().ToLowerInvariant();
        if (normalized is not "direct" and not "test_copy")
        {
            throw new InvalidOperationException("write_mode must be direct or test_copy.");
        }

        if (normalized == "test_copy" && !project.IsTestCopy)
        {
            throw new InvalidOperationException("write_mode=test_copy requires a project with _CCZModStudio_TestCopy.txt.");
        }

        return normalized;
    }

    private static string ResolveProjectFile(CczProject project, string relativeOrAbsolutePath, bool mustExist)
    {
        if (string.IsNullOrWhiteSpace(relativeOrAbsolutePath))
        {
            throw new InvalidOperationException("A project file path is required.");
        }

        var normalizedInput = relativeOrAbsolutePath.Replace('/', Path.DirectorySeparatorChar);
        var fullPath = Path.GetFullPath(Path.IsPathRooted(normalizedInput)
            ? normalizedInput
            : Path.Combine(project.GameRoot, normalizedInput));
        var rootWithSlash = Path.GetFullPath(project.GameRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!fullPath.StartsWith(rootWithSlash, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Target path escapes the current project root: " + fullPath);
        }

        if (mustExist && !File.Exists(fullPath))
        {
            throw new FileNotFoundException("Project file was not found.", fullPath);
        }

        return fullPath;
    }

    private static string NormalizeProjectRelativePath(CczProject project, string fullPath)
        => Path.GetRelativePath(project.GameRoot, fullPath)
            .Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);

    private static string TryNormalizeProjectRelativePath(CczProject project, string fullPath)
    {
        if (string.IsNullOrWhiteSpace(fullPath)) return string.Empty;
        try
        {
            var normalized = Path.GetFullPath(fullPath);
            var root = Path.GetFullPath(project.GameRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var rootWithSlash = root + Path.DirectorySeparatorChar;
            if (!normalized.Equals(root, StringComparison.OrdinalIgnoreCase) &&
                !normalized.StartsWith(rootWithSlash, StringComparison.OrdinalIgnoreCase))
            {
                return string.Empty;
            }

            return NormalizeProjectRelativePath(project, normalized);
        }
        catch
        {
            return string.Empty;
        }
    }

    private static string ResolveExternalFile(CczProject project, string path)
    {
        if (string.IsNullOrWhiteSpace(path)) throw new InvalidOperationException("replacement_path is required.");
        var normalized = path.Replace('/', Path.DirectorySeparatorChar);
        var candidates = new List<string>();
        if (Path.IsPathRooted(normalized))
        {
            candidates.Add(Path.GetFullPath(normalized));
        }
        else
        {
            candidates.Add(Path.GetFullPath(Path.Combine(project.WorkspaceRoot, normalized)));
            candidates.Add(Path.GetFullPath(Path.Combine(project.GameRoot, normalized)));
            candidates.Add(Path.GetFullPath(Path.Combine(Environment.CurrentDirectory, normalized)));
        }

        var found = candidates.FirstOrDefault(File.Exists);
        if (found == null) throw new FileNotFoundException("Replacement file was not found.", candidates.First());
        return found;
    }

    private static void EnsureGenericResourceAllowed(CczProject project, string targetPath)
    {
        var fileName = Path.GetFileName(targetPath);
        if (CoreFileDenyList.Contains(fileName))
        {
            throw new InvalidOperationException($"{fileName} must be modified through a dedicated MCP tool, not replace_resource.");
        }

        var extension = Path.GetExtension(targetPath);
        if (!ResourceExtensions.Contains(extension))
        {
            throw new InvalidOperationException($"Extension {extension} is not allowed for generic resource replacement.");
        }

        _ = NormalizeProjectRelativePath(project, targetPath);
    }

    private static void EnsureE5ImageTargetAllowed(CczProject project, string targetPath)
    {
        var fileName = Path.GetFileName(targetPath);
        if (CoreFileDenyList.Contains(fileName))
        {
            throw new InvalidOperationException($"{fileName} is a core file and cannot be modified through E5 image replacement.");
        }

        if (!Path.GetExtension(targetPath).Equals(".e5", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("E5 image replacement target must be a .e5 file.");
        }

        _ = NormalizeProjectRelativePath(project, targetPath);
    }

    private static object BuildE5ImageReplacePayload(E5ImageReplacePreviewResult preview)
        => new
        {
            preview.TargetPath,
            preview.TargetRelativePath,
            preview.SourcePath,
            preview.ImageNumber,
            IndexOffsetHex = "0x" + preview.IndexOffset.ToString("X", CultureInfo.InvariantCulture),
            OldDataOffsetHex = "0x" + preview.OldDataOffset.ToString("X", CultureInfo.InvariantCulture),
            NewDataOffsetHex = "0x" + preview.NewDataOffset.ToString("X", CultureInfo.InvariantCulture),
            preview.IndexOffset,
            preview.OldDataOffset,
            preview.NewDataOffset,
            preview.OldSizeBytes,
            preview.NewSizeBytes,
            preview.OldFileSizeBytes,
            preview.NewFileSizeBytes,
            preview.FileSizeDeltaBytes,
            preview.ChangedBytesEstimate,
            preview.OldFileSha256,
            preview.NewFileSha256,
            preview.SourceSha256,
            preview.OldKind,
            preview.NewKind,
            preview.SourceWidth,
            preview.SourceHeight,
            preview.Placement,
            preview.FormatWarnings,
            preview.RiskSummary
        };

    private static string FindKnowledgeRoot()
    {
        var starts = new[]
        {
            Environment.CurrentDirectory,
            AppContext.BaseDirectory
        };

        foreach (var start in starts)
        {
            var dir = new DirectoryInfo(start);
            while (dir != null)
            {
                var candidate = Path.Combine(dir.FullName, "本地知识库");
                if (Directory.Exists(candidate)) return candidate;
                candidate = Path.Combine(dir.FullName, "工具整合包", "本地知识库");
                if (Directory.Exists(candidate)) return candidate;
                dir = dir.Parent;
            }
        }

        return Path.Combine(Environment.CurrentDirectory, "本地知识库");
    }

    private static string ResolveKnowledgeEntryPath(string root, string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new InvalidOperationException("Knowledge entry name is required.");
        }

        var fullPath = Path.GetFullPath(Path.Combine(root, name));
        var rootWithSlash = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!fullPath.StartsWith(rootWithSlash, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Knowledge entry path escapes the local knowledge root.");
        }

        return fullPath;
    }
}
