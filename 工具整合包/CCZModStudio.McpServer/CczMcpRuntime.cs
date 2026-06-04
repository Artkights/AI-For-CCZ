using System.Data;
using System.Globalization;
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
    private readonly MapImageReplaceService _mapImageReplace = new();
    private readonly E5ImageReplaceService _e5ImageReplace = new();
    private readonly ResourceReplaceService _resourceReplace = new();
    private readonly ProjectAuditService _projectAudit = new();
    private readonly BackupManager _backupManager = new();
    private readonly TestCopyDiffService _testCopyDiff = new();
    private readonly ReleasePackageService _releasePackage = new();
    private readonly SceneStringParser _sceneStringParser = new();
    private readonly ScenarioCommandParameterTemplateService _scenarioCommandTemplates = new();

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
