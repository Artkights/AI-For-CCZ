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
    private readonly ScenarioTextReader _scenarioTextReader = new();
    private readonly ScenarioTextWriter _scenarioTextWriter = new();
    private readonly HexzmapProbeReader _hexzmapProbeReader = new();
    private readonly HexzmapEditorService _hexzmapEditor = new();
    private readonly MapImageReplaceService _mapImageReplace = new();
    private readonly ResourceReplaceService _resourceReplace = new();

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

    public object WriteScenarioTexts(string? gameRoot, string relativePath, List<ScenarioTextUpdate> updates, string? writeMode)
    {
        if (updates.Count == 0) throw new InvalidOperationException("updates must contain at least one scenario text update.");

        var project = LoadProject(gameRoot);
        var mode = EnsureWriteMode(project, writeMode);
        var filePath = ResolveProjectFile(project, relativePath, mustExist: true);
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
        var fullPath = Path.GetFullPath(Path.Combine(root, name));
        var rootWithSlash = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!fullPath.StartsWith(rootWithSlash, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Knowledge entry path escapes the local knowledge root.");
        }

        if (!File.Exists(fullPath)) throw new FileNotFoundException("Knowledge entry was not found.", fullPath);
        return new
        {
            Name = Path.GetFileName(fullPath),
            Path = fullPath,
            Text = File.ReadAllText(fullPath)
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
}
