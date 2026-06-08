using System.Data;
using System.Drawing.Imaging;
using System.Globalization;
using System.Text;
using System.Text.Json;
using CCZModStudio.Core;
using CCZModStudio.Formats;
using CCZModStudio.Models;

namespace CCZModStudio.McpServer;

public sealed partial class CczMcpRuntime
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
    private readonly ImageResourceCatalogService _imageResourceCatalog = new();
    private readonly IconResourceReplaceService _iconResourceReplace = new();
    private readonly AiImageAssetService _aiImageAssetService = new();
    private readonly ResourceReplaceService _resourceReplace = new();
    private readonly GameResourceIndexer _gameResourceIndexer = new();
    private readonly ResourceDiagnosticService _resourceDiagnosticService = new();
    private readonly ProjectAuditService _projectAudit = new();
    private readonly BackupManager _backupManager = new();
    private readonly BackupHistoryService _backupHistoryService = new();
    private readonly TestCopyDiffService _testCopyDiff = new();
    private readonly ReleasePackageService _releasePackage = new();
    private readonly ProjectEvidenceService _projectEvidenceService = new();
    private readonly ProjectDeliveryReportService _projectDeliveryReportService = new();
    private readonly ProjectWorkflowGuideService _projectWorkflowGuideService = new();
    private readonly SceneStringParser _sceneStringParser = new();
    private readonly ScenarioMapLinkService _scenarioMapLinkService = new();
    private readonly ScenarioCommandParameterTemplateService _scenarioCommandTemplates = new();
    private readonly CreatorNoteService _creatorNoteService = new();
    private readonly CreatorNoteNavigationService _creatorNoteNavigationService = new();
    private readonly EffectPackageService _effectPackageService = new();

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
        => HexTableNameResolver.Resolve(tables, tableName);

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

    private static bool MatchesImageResourceKeyword(ImageResourceFileInfo resource, string? keyword)
    {
        if (string.IsNullOrWhiteSpace(keyword)) return true;
        keyword = keyword.Trim();
        return resource.Key.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
               resource.Category.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
               resource.DisplayName.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
               resource.FileName.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
               resource.Aliases.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
               resource.Usage.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
               resource.RelativePath.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
               resource.Path.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
               resource.ResourceFormat.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
               resource.KindSummary.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
               resource.Status.Contains(keyword, StringComparison.OrdinalIgnoreCase);
    }

    private static bool MatchesImageResourceEntryKeyword(ImageResourceEntryInfo entry, string? keyword)
    {
        if (string.IsNullOrWhiteSpace(keyword)) return true;
        keyword = keyword.Trim();
        return entry.ResourceKey.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
               entry.Category.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
               entry.ResourceName.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
               entry.FileName.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
               entry.Kind.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
               entry.Usage.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
               entry.Path.Contains(keyword, StringComparison.OrdinalIgnoreCase);
    }

    private ImageResourceFileInfo ResolveImageResource(CczProject project, string resource)
    {
        if (string.IsNullOrWhiteSpace(resource))
        {
            throw new InvalidOperationException("resource is required.");
        }

        var query = resource.Trim();
        var catalog = _imageResourceCatalog.BuildCatalog(project);
        var normalizedQuery = query.Replace('/', Path.DirectorySeparatorChar);
        var exact = catalog.FirstOrDefault(item =>
            item.Key.Equals(query, StringComparison.OrdinalIgnoreCase) ||
            item.FileName.Equals(query, StringComparison.OrdinalIgnoreCase) ||
            item.DisplayName.Equals(query, StringComparison.OrdinalIgnoreCase) ||
            item.RelativePath.Equals(normalizedQuery, StringComparison.OrdinalIgnoreCase) ||
            item.Path.Equals(query, StringComparison.OrdinalIgnoreCase));
        if (exact != null) return exact;

        var targetPath = ResolveOptionalProjectFile(project, query);
        if (targetPath != null)
        {
            exact = catalog.FirstOrDefault(item => item.Path.Equals(targetPath, StringComparison.OrdinalIgnoreCase));
            if (exact != null) return exact;
        }

        var matches = catalog.Where(item => MatchesImageResourceKeyword(item, query)).ToList();
        if (matches.Count == 1) return matches[0];
        if (matches.Count > 1)
        {
            var names = string.Join(", ", matches.Take(12).Select(item => item.Key + "/" + item.FileName));
            throw new InvalidOperationException($"Image resource query matched multiple resources. Use a more precise key or file name. Matches: {names}");
        }

        throw new InvalidOperationException("Image resource was not found: " + resource);
    }

    private static object BuildImageResourcePayload(CczProject project, ImageResourceFileInfo resource)
        => new
        {
            resource.Key,
            resource.Category,
            resource.DisplayName,
            resource.FileName,
            resource.Aliases,
            resource.Usage,
            resource.RelativePath,
            ProjectRelativePath = TryNormalizeProjectRelativePath(project, resource.Path),
            resource.Path,
            resource.Exists,
            resource.SizeBytes,
            resource.EntryCount,
            resource.SupportsE5Index,
            resource.SupportsPreview,
            resource.CanReplace,
            resource.ResourceFormat,
            resource.KindSummary,
            resource.Status,
            resource.SafetyNote
        };

    private static object BuildImageResourceEntryPayload(CczProject project, ImageResourceEntryInfo entry)
        => new
        {
            entry.ResourceKey,
            entry.Category,
            entry.ResourceName,
            entry.FileName,
            ProjectRelativePath = TryNormalizeProjectRelativePath(project, entry.Path),
            entry.Path,
            entry.ImageNumber,
            IndexOffsetHex = "0x" + entry.IndexOffset.ToString("X", CultureInfo.InvariantCulture),
            DataOffsetHex = "0x" + entry.DataOffset.ToString("X", CultureInfo.InvariantCulture),
            entry.IndexOffset,
            entry.DataOffset,
            entry.StoredLength,
            entry.DecodedLength,
            entry.IsCompressed,
            entry.Kind,
            entry.Usage,
            entry.CanReplace
        };

    private object BuildAiImageReplacementPreview(CczProject project, AiImagePromptPlan plan, string outputPath)
    {
        if (plan.Preset.TargetKind == "dll_icon")
        {
            var targetPath = ResolveDllIconTarget(project, plan.TargetRelativePath);
            var iconIndex = plan.TargetImageNumbers.FirstOrDefault();
            return BuildDllIconReplacePayload(_iconResourceReplace.PreviewReplaceBitmapIcon(project, targetPath, iconIndex, outputPath));
        }

        if (plan.TargetImageNumbers.Count > 1 || plan.Preset.TargetKind == "e5_batch")
        {
            var targetPath = ResolveProjectFile(project, plan.TargetRelativePath, mustExist: true);
            EnsureE5ImageTargetAllowed(project, targetPath);
            var requests = plan.TargetImageNumbers.Select(imageNumber => new E5ImageBatchReplaceRequest
            {
                ImageNumber = imageNumber,
                SourcePath = outputPath,
                SourceLabel = outputPath,
                OperationKind = "AI生成预览替换"
            }).ToArray();
            return BuildE5ImageBatchReplacePayload(_e5ImageReplace.PreviewBatchReplacement(project, targetPath, requests));
        }

        var e5TargetPath = ResolveProjectFile(project, plan.TargetRelativePath, mustExist: true);
        EnsureE5ImageTargetAllowed(project, e5TargetPath);
        return BuildE5ImageReplacePayload(_e5ImageReplace.PreviewReplacement(project, e5TargetPath, plan.TargetImageNumbers.First(), outputPath));
    }

    private static object BuildAiImagePromptPlanPayload(AiImagePromptPlan plan)
        => new
        {
            Preset = plan.Preset,
            plan.Description,
            plan.Prompt,
            plan.NegativePrompt,
            plan.TargetRelativePath,
            plan.TargetImageNumbers,
            plan.TargetWidth,
            plan.TargetHeight,
            plan.OutputFormat,
            plan.GenerationSize,
            plan.Quality,
            plan.MappingSummary,
            plan.Warnings,
            SafetyNote = "AI 绘图计划只描述生成、后处理和替换预览，不直接写入游戏资源。"
        };

    private static object BuildAiImagePreparePayload(AiImagePrepareResult result)
        => new
        {
            Plan = BuildAiImagePromptPlanPayload(result.Plan),
            result.SourcePath,
            result.OutputPath,
            result.ManifestPath,
            result.SourceWidth,
            result.SourceHeight,
            result.OutputWidth,
            result.OutputHeight,
            result.OutputFormat,
            result.SourceSha256,
            result.OutputSha256,
            result.PostProcessSummary,
            result.ReplacementPreview,
            PreparedFiles = result.PreparedFiles.Select(BuildAiImagePreparedFilePayload)
        };

    private static object BuildAiImagePreparedFilePayload(AiImagePreparedFile file)
        => new
        {
            file.Role,
            file.TargetRelativePath,
            file.TargetImageNumbers,
            file.OutputPath,
            file.OutputWidth,
            file.OutputHeight,
            file.OutputSha256,
            file.ReplacementPreview
        };

    private static object BuildAiImageDrawPayload(AiImageDrawResult result)
        => new
        {
            result.DryRun,
            Plan = BuildAiImagePromptPlanPayload(result.Plan),
            result.Provider,
            result.ApiMode,
            result.BaseUrl,
            result.TextModel,
            result.ImageModel,
            result.RawResponsePath,
            result.GeneratedSourcePath,
            Prepared = result.Prepared == null ? null : BuildAiImagePreparePayload(result.Prepared),
            result.Logs
        };

    private List<E5ImageBatchReplaceRequest> BuildE5ImageBatchRequests(CczProject project, IReadOnlyList<E5ImageBatchUpdate> updates)
    {
        if (updates.Count == 0)
        {
            throw new InvalidOperationException("updates must contain at least one E5 image batch update.");
        }

        var requests = new List<E5ImageBatchReplaceRequest>();
        foreach (var update in updates)
        {
            if (update.ImageNumber <= 0)
            {
                throw new InvalidOperationException("image_number must be positive.");
            }

            var sourcePath = ResolveExternalFile(project, update.ReplacementPath);
            var sourceLabel = sourcePath;
            byte[]? sourceBytes = null;
            if (update.SourceImageNumber.HasValue)
            {
                if (update.SourceImageNumber.Value <= 0)
                {
                    throw new InvalidOperationException("source_image_number must be positive when supplied.");
                }

                sourceBytes = _e5ImageReplace.ReadEntryBytes(sourcePath, update.SourceImageNumber.Value);
                sourceLabel = $"{sourcePath}#{update.SourceImageNumber.Value}";
            }

            requests.Add(new E5ImageBatchReplaceRequest
            {
                ImageNumber = update.ImageNumber,
                SourcePath = sourcePath,
                SourceBytes = sourceBytes,
                SourceLabel = sourceLabel,
                OperationKind = string.IsNullOrWhiteSpace(update.OperationKind) ? "replace" : update.OperationKind
            });
        }

        return requests;
    }

    private static object BuildE5ImageBatchReplacePayload(E5ImageBatchReplacePreviewResult preview)
        => new
        {
            preview.TargetPath,
            preview.TargetRelativePath,
            preview.OperationCount,
            preview.OldFileSizeBytes,
            preview.NewFileSizeBytes,
            preview.FileSizeDeltaBytes,
            preview.ChangedBytesEstimate,
            preview.OldFileSha256,
            preview.NewFileSha256,
            preview.FormatWarnings,
            preview.RiskSummary,
            Operations = preview.Operations.Select(operation => new
            {
                operation.ImageNumber,
                IndexOffsetHex = "0x" + operation.IndexOffset.ToString("X", CultureInfo.InvariantCulture),
                OldDataOffsetHex = "0x" + operation.OldDataOffset.ToString("X", CultureInfo.InvariantCulture),
                NewDataOffsetHex = "0x" + operation.NewDataOffset.ToString("X", CultureInfo.InvariantCulture),
                operation.IndexOffset,
                operation.OldDataOffset,
                operation.NewDataOffset,
                operation.OldSizeBytes,
                operation.NewSizeBytes,
                operation.OldKind,
                operation.NewKind,
                operation.SourcePath,
                operation.OperationKind,
                operation.SourceSha256,
                operation.SourceWidth,
                operation.SourceHeight,
                operation.Placement,
                operation.FormatWarnings
            })
        };

    private static object BuildDllIconReplacePayload(IconResourceReplacePreviewResult preview)
        => new
        {
            preview.TargetPath,
            preview.TargetRelativePath,
            preview.IconIndex,
            preview.ResourceIds,
            preview.SourcePath,
            preview.OperationKind,
            preview.OldFileSizeBytes,
            preview.SourceSizeBytes,
            preview.OldFileSha256,
            preview.SourceSha256,
            preview.SourceWidth,
            preview.SourceHeight,
            preview.ResourceFormat,
            preview.FormatWarnings,
            preview.RiskSummary
        };

    private static object BuildResourceReplacePreviewPayload(ResourceReplacePreviewResult preview)
        => new
        {
            preview.TargetPath,
            preview.TargetRelativePath,
            preview.ReplacementPath,
            preview.Extension,
            preview.OldSizeBytes,
            preview.NewSizeBytes,
            preview.SizeDeltaBytes,
            preview.ChangedBytesEstimate,
            preview.ChangedPercent,
            preview.OldSha256,
            preview.NewSha256,
            preview.IsContentIdentical,
            preview.FormatCheckSummary,
            preview.FormatWarnings,
            preview.RiskSummary
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

    private IReadOnlyList<ProjectDiffItem> SafeAnalyzeDiff(CczProject project)
    {
        if (!project.IsTestCopy)
        {
            return Array.Empty<ProjectDiffItem>();
        }

        try
        {
            return _testCopyDiff.Analyze(project);
        }
        catch
        {
            return Array.Empty<ProjectDiffItem>();
        }
    }

    private IReadOnlyList<BackupHistoryItem> SafeScanBackups(CczProject project)
    {
        try
        {
            return _backupHistoryService.Scan(project);
        }
        catch
        {
            return Array.Empty<BackupHistoryItem>();
        }
    }

    private IReadOnlyList<ScenarioMapLinkInfo> BuildScenarioMapLinks(CczProject project, IReadOnlyList<ResourceIndexItem> resources)
    {
        try
        {
            var scenarios = _scenarioFileReader.ReadAllIndex(project)
                .Where(file => MatchesScenarioFileKind(file, "S"))
                .ToList();
            var terrainLookup = BuildTerrainNameLookup(project);
            var hexzmap = _hexzmapProbeReader.Read(project, terrainLookup);
            return _scenarioMapLinkService.BuildLinks(scenarios, resources, hexzmap);
        }
        catch
        {
            return Array.Empty<ScenarioMapLinkInfo>();
        }
    }

    private static bool MatchesProjectEvidenceKeyword(ProjectEvidenceItem item, string? keyword)
    {
        if (string.IsNullOrWhiteSpace(keyword)) return true;
        keyword = keyword.Trim();
        return ContainsIgnoreCase(item.Category, keyword) ||
               ContainsIgnoreCase(item.Kind, keyword) ||
               ContainsIgnoreCase(item.FileName, keyword) ||
               ContainsIgnoreCase(item.SourceRoot, keyword) ||
               ContainsIgnoreCase(item.FullPath, keyword) ||
               ContainsIgnoreCase(item.Annotation, keyword) ||
               ContainsIgnoreCase(item.SuggestedUse, keyword) ||
               ContainsIgnoreCase(item.SafetyNote, keyword);
    }

    private static object BuildProjectEvidencePayload(ProjectEvidenceItem item)
        => new
        {
            item.Category,
            item.Kind,
            item.FileName,
            item.SourceRoot,
            item.FullPath,
            item.LastWriteTimeText,
            item.SizeBytes,
            item.SizeText,
            item.Annotation,
            item.SuggestedUse,
            item.SafetyNote
        };

    private ProjectEvidenceItem ResolveProjectEvidenceItem(CczProject project, string pathOrFile)
    {
        var evidence = _projectEvidenceService.Scan(project, maxItems: 1000);
        var query = pathOrFile.Trim();
        var aliasMatch = ResolveProjectEvidenceAlias(evidence, query);
        if (aliasMatch != null)
        {
            return aliasMatch;
        }

        var normalizedQuery = query.Replace('/', Path.DirectorySeparatorChar);

        var exactFullPath = evidence
            .FirstOrDefault(item => item.FullPath.Equals(Path.GetFullPath(normalizedQuery), StringComparison.OrdinalIgnoreCase));
        if (exactFullPath != null)
        {
            return exactFullPath;
        }

        var relativeMatches = evidence
            .Where(item =>
                TryRelativeEvidencePath(project, item.FullPath).Equals(normalizedQuery, StringComparison.OrdinalIgnoreCase) ||
                Path.GetFileName(item.FullPath).Equals(query, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (relativeMatches.Count == 1)
        {
            return relativeMatches[0];
        }

        if (relativeMatches.Count > 1)
        {
            var candidates = string.Join(", ", relativeMatches.Take(12).Select(item => TryRelativeEvidencePath(project, item.FullPath)));
            throw new InvalidOperationException("Evidence file name matched multiple files. Use a more specific relative path. Matches: " + candidates);
        }

        throw new FileNotFoundException("Project evidence was not found by list_project_evidence.", query);
    }

    private static ProjectEvidenceItem? ResolveProjectEvidenceAlias(IReadOnlyList<ProjectEvidenceItem> evidence, string query)
    {
        if (evidence.Count == 0 || string.IsNullOrWhiteSpace(query))
        {
            return null;
        }

        var normalized = query.Trim().TrimStart('@').Replace("-", "_", StringComparison.Ordinal).ToLowerInvariant();
        if (normalized is "latest" or "latest_evidence")
        {
            return evidence
                .OrderByDescending(item => item.LastWriteTime)
                .FirstOrDefault();
        }

        if (normalized is "latest_important" or "important")
        {
            return evidence.FirstOrDefault();
        }

        if (normalized is "latest_delivery_report" or "delivery_report" or "project_delivery_report")
        {
            return evidence
                .Where(item => item.Kind.Equals("发布前综合报告", StringComparison.Ordinal))
                .OrderByDescending(item => item.LastWriteTime)
                .FirstOrDefault();
        }

        if (normalized is "latest_write_report" or "write_report")
        {
            return evidence
                .Where(item => item.Kind.Equals("结构化写入报告", StringComparison.Ordinal))
                .OrderByDescending(item => item.LastWriteTime)
                .FirstOrDefault();
        }

        return null;
    }

    private static string TryRelativeEvidencePath(CczProject project, string fullPath)
    {
        try
        {
            var workspaceRoot = Path.GetFullPath(project.WorkspaceRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var gameRoot = Path.GetFullPath(project.GameRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var normalized = Path.GetFullPath(fullPath);
            if (normalized.StartsWith(workspaceRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            {
                return Path.GetRelativePath(workspaceRoot, normalized);
            }

            if (normalized.StartsWith(gameRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            {
                return Path.GetRelativePath(gameRoot, normalized);
            }
        }
        catch
        {
            // Fall through to file name for malformed paths.
        }

        return Path.GetFileName(fullPath);
    }

    private static bool IsTextEvidenceExtension(string extension)
        => extension.Equals(".md", StringComparison.OrdinalIgnoreCase) ||
           extension.Equals(".txt", StringComparison.OrdinalIgnoreCase) ||
           extension.Equals(".json", StringComparison.OrdinalIgnoreCase) ||
           extension.Equals(".csv", StringComparison.OrdinalIgnoreCase);

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

    private static string? ResolveOptionalProjectFile(CczProject project, string relativeOrAbsolutePath)
    {
        try
        {
            return ResolveProjectFile(project, relativeOrAbsolutePath, mustExist: false);
        }
        catch
        {
            return null;
        }
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

    private static string ResolveDllIconTarget(CczProject project, string targetRelativePath)
    {
        var targetPath = ResolveProjectFile(project, targetRelativePath, mustExist: true);
        if (!Path.GetExtension(targetPath).Equals(".dll", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("DLL icon target must be a .dll file.");
        }

        var fileName = Path.GetFileName(targetPath);
        if (!fileName.Equals("Itemicon.dll", StringComparison.OrdinalIgnoreCase) &&
            !fileName.Equals("Mgcicon.dll", StringComparison.OrdinalIgnoreCase) &&
            !fileName.Equals("Cmdicon.dll", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("DLL icon replacement is currently limited to Itemicon.dll, Mgcicon.dll, and Cmdicon.dll.");
        }

        return targetPath;
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

    private static string MakeSafeFileStem(string value)
    {
        var stem = Path.GetFileNameWithoutExtension(value);
        if (string.IsNullOrWhiteSpace(stem)) stem = "preview";
        foreach (var invalid in Path.GetInvalidFileNameChars())
        {
            stem = stem.Replace(invalid, '_');
        }

        return stem.Replace(' ', '_');
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
