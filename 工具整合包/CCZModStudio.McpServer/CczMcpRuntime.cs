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
    private readonly CczEngineProfileService _engineProfileService = new();
    private readonly OfficialImageAssignerOracleService _imageAssignerOracleService = new();
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
    private readonly RsPixelCharacterDesignService _rsPixelCharacterDesignService = new();
    private readonly RsPixelEditWorkspaceService _rsPixelEditWorkspaceService = new();
    private readonly RsPixelSampleLearningService _rsPixelSampleLearningService = new();
    private readonly RImageReplaceService _rImageReplaceService = new();
    private readonly SImageReplaceService _sImageReplaceService = new();
    private readonly RsPixelMaterialValidationService _rsPixelMaterialValidationService = new();
    private readonly JobSImageReplaceService _jobSImageReplaceService = new();
    private readonly BatchRImageReplaceService _batchRImageReplaceService = new();
    private readonly BatchSImageReplaceService _batchSImageReplaceService = new();
    private readonly BatchItemIconImportService _batchItemIconImportService = new();
    private readonly BatchStrategyIconImportService _batchStrategyIconImportService = new();
    private readonly BatchRoleFaceImportService _batchRoleFaceImportService = new();
    private readonly BatchJobSImageReplaceService _batchJobSImageReplaceService = new();
    private readonly BmpImageExportService _bmpImageExportService = new();
    private readonly E5RoleRawNormalizeService _e5RoleRawNormalizeService = new();
    private readonly ResourceReplaceService _resourceReplace = new();
    private readonly BackupManager _backupManager = new();
    private readonly SceneStringParser _sceneStringParser = new();
    private readonly ScenarioCommandParameterTemplateService _scenarioCommandTemplates = new();
    private readonly EffectPackageService _effectPackageService = new();
    private readonly CmfDerivedCapabilityService _cmfDerivedCapabilityService = new();
    private readonly BattlefieldEditorService _battlefieldEditorService = new();
    private readonly BattlefieldUnitStatusWriteService _battlefieldUnitStatusWriteService = new();

    private CczProject LoadProject(string? gameRoot)
    {
        var explicitGameRoot = !string.IsNullOrWhiteSpace(gameRoot);
        CczProject project;
        if (explicitGameRoot)
        {
            project = _projectDetector.CreateProjectFromGameRoot(gameRoot!);
        }
        else
        {
            var envGameRoot = Environment.GetEnvironmentVariable("CCZMODSTUDIO_GAME_ROOT");
            if (!string.IsNullOrWhiteSpace(envGameRoot))
            {
                project = _projectDetector.CreateProjectFromGameRoot(envGameRoot);
                explicitGameRoot = true;
            }
            else
            {
                project = _projectDetector.DetectDefaultProject();
            }

        }

        if (explicitGameRoot) EnsureUsableGameRoot(project);
        return project;
    }

    public object DetectProject(string? gameRoot)
    {
        var project = LoadProject(gameRoot);
        var engine = _engineProfileService.Detect(project);
        var tables = File.Exists(project.HexTableXmlPath)
            ? LoadTables(project)
            : Array.Empty<HexTableDefinition>();
        var qinger66Diagnostics = _qinger66DiagnosticsService.Build(project, engine, tables, _tableReader);
        engine.TableStatusSummary = qinger66Diagnostics.TableStatusSummary;
        engine.Qinger66Diagnostics = qinger66Diagnostics;
        var cmfEvidence = BuildCmfEvidence(project);
        var cmfKnowledge = _cmfDerivedCapabilityService.BuildSummary(project);
        var imageAssignerOracle = _imageAssignerOracleService.Detect(project);
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
            Engine = engine,
            TableStatusSummary = qinger66Diagnostics.TableStatusSummary,
            Qinger66Diagnostics = qinger66Diagnostics,
            ImageAssignerOracle = imageAssignerOracle,
            CmfEvidence = cmfEvidence,
            CmfEvidenceSummary = cmfEvidence,
            CmfKnowledge = cmfKnowledge,
            CmfDerivedCapabilitySummary = cmfKnowledge,
            Files = project.GetFileStatuses().Select(x => new
            {
                x.Name,
                x.Path,
                x.Exists,
                x.SizeBytes,
                x.Kind,
                x.CountsAsMissing,
                x.Note
            }),
            project.PathDiagnostics
        };
    }

    private static object? BuildCmfEvidence(CczProject project)
    {
        var report = BuildCmfCorpusReport(project);
        if (report == null || report.TotalFiles == 0) return null;

        var coreSamples = report.Entries
            .Where(entry => entry.EvidenceCategory.Equals("CczRelevantRootSample", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(entry => entry.Length)
            .Select(entry => new
            {
                entry.RelativePath,
                entry.Length,
                entry.Sha256,
                entry.FormatSignature,
                entry.FormatVersion,
                entry.Utf16CrlfCount,
                entry.LooksProtectedOrObfuscated,
                entry.EvidenceCategory,
                entry.Summary
            })
            .ToArray();

        return new
        {
            report.EvidenceOnly,
            report.SafetyNote,
            report.RootPath,
            report.TotalFiles,
            report.CheatMakerCmfCount,
            report.SignatureCounts,
            report.CategoryCounts,
            CoreSamples = coreSamples
        };
    }

    private static CheatMakerCmfCorpusReport? BuildCmfCorpusReport(CczProject project)
    {
        var root = CheatMakerCmfProbe.FindDefaultOldToolsRoot(project.WorkspaceRoot);
        return string.IsNullOrWhiteSpace(root)
            ? null
            : new CheatMakerCmfProbe().ScanCorpus(root);
    }

    private static string ResolveCmfEvidencePath(CheatMakerCmfCorpusReport report, string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
        {
            throw new InvalidOperationException("CMF relative path is required.");
        }

        var normalized = relativePath.Replace('/', Path.DirectorySeparatorChar);
        var fullPath = Path.GetFullPath(Path.IsPathRooted(normalized)
            ? normalized
            : Path.Combine(report.RootPath, normalized));
        var rootWithSlash = Path.GetFullPath(report.RootPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!fullPath.StartsWith(rootWithSlash, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("CMF evidence path escapes the old tools root.");
        }

        return fullPath;
    }

    public object ListCmfEvidence(string? gameRoot)
    {
        var project = LoadProject(gameRoot);
        var report = BuildCmfCorpusReport(project)
            ?? new CheatMakerCmfCorpusReport();
        return new
        {
            report.EvidenceOnly,
            report.SafetyNote,
            report.RootPath,
            report.TotalFiles,
            report.CheatMakerCmfCount,
            report.SignatureCounts,
            report.CategoryCounts,
            Entries = report.Entries.Select(entry => new
            {
                entry.RelativePath,
                entry.FileName,
                entry.Length,
                entry.Sha256,
                entry.FormatSignature,
                entry.FormatVersion,
                entry.Utf16CrlfCount,
                entry.IsCheatMakerCmf,
                entry.LooksProtectedOrObfuscated,
                entry.VisibleKeywordHits,
                SegmentCount = entry.Segments.Count,
                Segments = entry.Segments.Take(8),
                entry.EvidenceCategory,
                entry.EvidenceOnly,
                entry.AuthoritativeToolSource,
                entry.Summary,
                entry.Warnings
            })
        };
    }

    public object ReadCmfEvidence(string? gameRoot, string relativePath)
    {
        var project = LoadProject(gameRoot);
        var report = BuildCmfCorpusReport(project)
            ?? throw new DirectoryNotFoundException("Old tools CMF root was not found.");
        var fullPath = ResolveCmfEvidencePath(report, relativePath);
        var probe = new CheatMakerCmfProbe().Probe(fullPath, null, "MCP CMF evidence read", report.RootPath);
        return new
        {
            probe.EvidenceOnly,
            probe.SafetyNote,
            probe.RelativePath,
            probe.Path,
            probe.Exists,
            probe.Length,
            probe.Sha256,
            probe.Signature,
            probe.FormatSignature,
            probe.FormatVersion,
            probe.IsCheatMakerCmf,
            probe.Utf16CrlfCount,
            probe.FirstUtf16CrlfOffsets,
            probe.LooksProtectedOrObfuscated,
            probe.VisibleKeywordHits,
            SegmentCount = probe.Segments.Count,
            Segments = probe.Segments,
            probe.EvidenceCategory,
            probe.Summary,
            probe.Warnings
        };
    }

    public object ExtractCmfKnowledge(string? gameRoot, string relativePath)
    {
        var project = LoadProject(gameRoot);
        var cmf = _cmfDerivedCapabilityService.ExtractProject(project, relativePath);
        return new
        {
            project.GameRoot,
            cmf.AuthoritativeToolSource,
            cmf.ConversionPolicy,
            Project = cmf,
            SafetyNote = "CMF knowledge extraction is read-only. Static candidates require CheatMaker export/UI metadata, version match, address classification, and reread validation before writes."
        };
    }

    public object ImportCmfExportKnowledge(string? gameRoot, string relativePath, string exportPath)
    {
        var project = LoadProject(gameRoot);
        var cmf = _cmfDerivedCapabilityService.ImportCheatMakerExport(project, relativePath, exportPath);
        return new
        {
            project.GameRoot,
            cmf.AuthoritativeToolSource,
            cmf.ConversionPolicy,
            cmf.ExtractionMode,
            FieldCount = cmf.ExportFields.Count,
            BindingCount = cmf.DataBindings.Count,
            FeatureCount = cmf.FeatureCandidates.Count,
            ExportFields = cmf.ExportFields.Take(200),
            Features = cmf.FeatureCandidates.Take(100),
            Warnings = cmf.Warnings,
            SafetyNote = "CheatMaker export data is treated as stronger field metadata, but this import is read-only. Writes still require version match, address classification, PE/file mapping, bounds check, test-copy write, and reread validation."
        };
    }

    public object ExtractCheatMakerDesignerSnapshot(
        string? gameRoot,
        string relativePath,
        string? mode,
        int timeoutMs,
        bool keepProcessOpen,
        string? fixtureSnapshotPath)
    {
        var project = LoadProject(gameRoot);
        var result = _cmfDerivedCapabilityService.ExtractDesignerSnapshot(
            project,
            relativePath,
            new CmfDesignerExtractionOptions
            {
                Mode = string.IsNullOrWhiteSpace(mode) ? "UiProbe" : mode!,
                TimeoutMs = timeoutMs <= 0 ? 15000 : timeoutMs,
                KeepProcessOpen = keepProcessOpen,
                FixtureSnapshotPath = fixtureSnapshotPath ?? string.Empty
            });

        return new
        {
            project.GameRoot,
            result.ReportDirectory,
            result.SnapshotJsonPath,
            result.FieldsCsvPath,
            result.ModulesMarkdownPath,
            result.AddressesMarkdownPath,
            result.RawUiTreeJsonPath,
            result.Snapshot.SourcePath,
            result.Snapshot.RelativePath,
            result.Snapshot.SourceSha256,
            result.Snapshot.ExtractionMode,
            PageCount = result.Snapshot.Pages.Count,
            ModuleCount = result.Snapshot.Modules.Count,
            ControlCount = result.Snapshot.Controls.Count,
            BindingCount = result.Snapshot.Bindings.Count,
            Bindings = result.Snapshot.Bindings.Take(200),
            result.Warnings,
            SafetyNote = "Designer extraction is read-only: the source CMF is copied to a temp directory before CheatMaker is launched, and writes are not enabled until address classification, bounds check, test-copy write, and reread validation pass."
        };
    }

    public object ImportCheatMakerDesignerSnapshot(string? gameRoot, string relativePath, string snapshotPath)
    {
        var project = LoadProject(gameRoot);
        var cmf = _cmfDerivedCapabilityService.ImportDesignerSnapshot(project, relativePath, snapshotPath);
        return new
        {
            project.GameRoot,
            cmf.AuthoritativeToolSource,
            cmf.ConversionPolicy,
            cmf.ExtractionMode,
            DesignerFieldCount = cmf.DesignerSnapshot?.Bindings.Count ?? 0,
            BindingCount = cmf.DataBindings.Count,
            FeatureCount = cmf.FeatureCandidates.Count,
            DesignerFields = cmf.DesignerSnapshot?.Bindings.Take(200) ?? Array.Empty<CmfDesignerBinding>(),
            Features = cmf.FeatureCandidates.Take(100),
            Warnings = cmf.Warnings,
            SafetyNote = "Designer snapshot import is read-only and treats 地址(HEX) as UE file offset first. Writes still require version match, address classification, bounds check, test-copy write, and reread validation."
        };
    }

    public object ListCheatMakerDesignerFields(string? gameRoot, string? relativePath, int limit)
    {
        var project = LoadProject(gameRoot);
        var effectiveLimit = NormalizeLimit(limit, 200, 2000);
        var fields = _cmfDerivedCapabilityService.ListDesignerFields(project, relativePath);
        return new
        {
            project.GameRoot,
            TotalFields = fields.Count,
            Fields = fields.Take(effectiveLimit),
            SafetyNote = "Designer fields are address evidence only. UE offsets are not writable rules until each field is classified, bounds checked, written on a test copy, and reread."
        };
    }

    public object ListCheatMakerManualSeedFields(string? gameRoot, string? keyword, int limit)
    {
        var project = LoadProject(gameRoot);
        var effectiveLimit = NormalizeLimit(limit, 200, 2000);
        var fields = _cmfDerivedCapabilityService.ListManualSeedFields(project, keyword);
        return new
        {
            project.GameRoot,
            TotalFields = fields.Count,
            Fields = fields.Take(effectiveLimit),
            SafetyNote = "Manual seed fields are user-confirmed CMF address evidence only. They remain read-only until each field passes bounds check, test-copy write, and reread validation."
        };
    }

    public object ValidateCheatMakerManualSeed(string? gameRoot)
    {
        var project = LoadProject(gameRoot);
        var report = _cmfDerivedCapabilityService.ValidateManualSeeds(project);
        return new
        {
            project.GameRoot,
            report.ReportKind,
            report.CreatedAt,
            report.SeedCount,
            report.FieldCount,
            report.TableCount,
            report.ExpandedTableEntryCount,
            report.IsValid,
            report.Issues,
            SafetyNote = "Manual seed validation checks the built-in CMF manual address seeds only. It does not write CMF or game files."
        };
    }

    public object CompareCheatMakerDesignerSnapshots(
        string? gameRoot,
        string leftRelativePath,
        string rightRelativePath,
        string? leftSnapshotPath,
        string? rightSnapshotPath)
    {
        var project = LoadProject(gameRoot);
        var report = _cmfDerivedCapabilityService.CompareDesignerSnapshots(
            project,
            leftRelativePath,
            rightRelativePath,
            leftSnapshotPath,
            rightSnapshotPath);
        return new
        {
            project.GameRoot,
            report.ReportDirectory,
            report.JsonReportPath,
            report.MarkdownReportPath,
            PageDiffCount = report.PageDiffs.Count,
            ModuleDiffCount = report.ModuleDiffs.Count,
            BindingDiffCount = report.BindingDiffs.Count,
            report.PageDiffs,
            report.ModuleDiffs,
            BindingDiffs = report.BindingDiffs.Take(200),
            report.Warnings,
            SafetyNote = "Designer snapshot diff compares extracted design-time metadata only; it does not modify CMF or game files."
        };
    }

    public object VerifyCheatMakerDesignerWrites(
        string? gameRoot,
        string relativePath,
        string? snapshotPath,
        List<string>? bindingIds,
        int maxFields,
        bool includeNeedsManualReview)
    {
        var project = LoadProject(gameRoot);
        var report = _cmfDerivedCapabilityService.VerifyDesignerWrites(
            project,
            relativePath,
            new CmfDesignerWriteVerificationOptions
            {
                SnapshotPath = snapshotPath ?? string.Empty,
                BindingIds = bindingIds?.ToArray() ?? Array.Empty<string>(),
                MaxFields = maxFields <= 0 ? 500 : maxFields,
                IncludeNeedsManualReview = includeNeedsManualReview
            });
        return new
        {
            project.GameRoot,
            report.TestCopyRoot,
            report.ReportDirectory,
            report.JsonReportPath,
            report.TotalFields,
            report.WriteVerifiedCount,
            Fields = report.Fields.Take(200),
            report.Warnings,
            SafetyNote = "Write verification writes only to a CCZModStudio test copy. Only fields with FinalStatus=WriteVerified and CanPromoteToWrite=true may be considered for a formal save path."
        };
    }

    public object ListCmfFeatures(string? gameRoot, string? category, string? keyword, int limit)
    {
        var project = LoadProject(gameRoot);
        var effectiveLimit = NormalizeLimit(limit, 100, 1000);
        var features = _cmfDerivedCapabilityService.ListFeatures(project, category, keyword);
        return new
        {
            project.GameRoot,
            AuthoritativeToolSource = true,
            TotalFeatures = features.Count,
            Features = features.Take(effectiveLimit),
            SafetyNote = "CMF features are high-trust old-tool candidates. promote_cmf_feature_candidate reports validation gaps before any write rule can be created."
        };
    }

    public object ReadCmfFeature(string? gameRoot, string featureId)
    {
        var project = LoadProject(gameRoot);
        var feature = _cmfDerivedCapabilityService.ReadFeature(project, featureId);
        return new
        {
            project.GameRoot,
            AuthoritativeToolSource = true,
            Feature = feature,
            SafetyNote = feature.WritePolicy
        };
    }

    public object PromoteCmfFeatureCandidate(string? gameRoot, string featureId)
    {
        var project = LoadProject(gameRoot);
        var draft = _cmfDerivedCapabilityService.PromoteFeature(project, featureId);
        return new
        {
            project.GameRoot,
            Draft = draft,
            SafetyNote = "This creates a rule draft only. No game files are modified and CanWriteNow remains false until all validation blockers are cleared."
        };
    }

    public object CompareCmfEvidence(string? gameRoot, string leftRelativePath, string rightRelativePath)
    {
        var project = LoadProject(gameRoot);
        var report = BuildCmfCorpusReport(project)
            ?? throw new DirectoryNotFoundException("Old tools CMF root was not found.");
        var leftPath = ResolveCmfEvidencePath(report, leftRelativePath);
        var rightPath = ResolveCmfEvidencePath(report, rightRelativePath);
        var left = new CheatMakerCmfProbe().Probe(leftPath, rightPath, "MCP CMF evidence comparison target", report.RootPath);
        return new
        {
            left.EvidenceOnly,
            left.SafetyNote,
            Left = new
            {
                left.RelativePath,
                left.Length,
                left.Sha256,
                left.FormatSignature,
                left.Utf16CrlfCount,
                left.EvidenceCategory
            },
            Right = left.Comparison == null ? null : new
            {
                RelativePath = Path.GetRelativePath(report.RootPath, rightPath),
                Length = left.Comparison.BaselineLength,
                Sha256 = left.Comparison.BaselineSha256,
                FormatSignature = left.Comparison.BaselineSignature,
                Utf16CrlfCount = left.Comparison.BaselineUtf16CrlfCount
            },
            left.Comparison
        };
    }

    private IReadOnlyList<HexTableDefinition> LoadTables(CczProject project)
    {
        if (!File.Exists(project.HexTableXmlPath))
        {
            throw new FileNotFoundException(ProjectDetector.BuildMissingHexTableMessage(project), project.HexTableXmlPath);
        }

        return new Ccz66HexTableAugmentationService().LoadForProject(project, _tableParser);
    }

    private HexTableDefinition FindTable(CczProject project, IReadOnlyList<HexTableDefinition> tables, string tableName)
        => HexTableNameResolver.ResolveForProject(project, tables, tableName);

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
                if (!includeId || !column.ColumnName.Equals("ID", StringComparison.Ordinal))
                {
                    if (column.ExtendedProperties["FieldDefinition"] is HexFieldDefinition field &&
                        !field.VisibleByDefault)
                    {
                        continue;
                    }
                }

                if (!result.Contains(column)) result.Add(column);
            }
        }

        return result;
    }

    private static Dictionary<string, object?> RowToDictionary(DataRow row, IReadOnlyList<DataColumn> columns)
    {
        var result = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        foreach (var column in columns) result[column.ColumnName] = row[column] is DBNull ? null : row[column];
        return result;
    }

    private static string FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? string.Empty;

    private static string TrimForMcp(string? value, int maxChars)
    {
        value ??= string.Empty;
        value = value.Replace("\r", "\\r", StringComparison.Ordinal).Replace("\n", "\\n", StringComparison.Ordinal);
        return value.Length <= maxChars ? value : value[..maxChars] + "...";
    }

    private static string EnsureWriteMode(CczProject project, string? writeMode)
    {
        return "direct";
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

    private static void EnsureUsableGameRoot(CczProject project)
    {
        var missing = project.GetFileStatuses()
            .Where(status => (status.Kind is "core" or "resource-directory") && status.CountsAsMissing && !status.Exists)
            .Select(status => status.Name)
            .ToList();
        if (missing.Count == 0) return;

        throw new InvalidOperationException(
            "The selected game_root is incomplete and cannot be used for automation. Missing: " +
            string.Join(", ", missing) +
            ". Pass the real base directory that contains Ekd5.exe, Data.e5, Imsg.e5, Star.e5, and RS.");
    }

    private static IReadOnlyList<string> ResolveExternalFiles(CczProject project, IReadOnlyList<string> paths)
    {
        if (paths.Count == 0) throw new InvalidOperationException("source_files must contain at least one file.");
        return paths.Select(path => ResolveExternalFile(project, path)).ToArray();
    }

    private static IReadOnlyList<string> ResolveOptionalExternalFiles(CczProject project, IReadOnlyList<string>? paths)
        => paths == null || paths.Count == 0
            ? Array.Empty<string>()
            : paths.Select(path => ResolveExternalFile(project, path)).ToArray();

    private static string ResolveExternalDirectory(CczProject project, string path)
    {
        if (string.IsNullOrWhiteSpace(path)) throw new InvalidOperationException("material_folder is required.");
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

        var found = candidates.FirstOrDefault(Directory.Exists);
        if (found == null) throw new DirectoryNotFoundException("Material folder was not found: " + candidates.First());
        return found;
    }

    private static string ResolveOptionalExternalDirectory(CczProject project, string? path)
        => string.IsNullOrWhiteSpace(path) ? string.Empty : ResolveExternalDirectory(project, path);

    private static string ResolveExternalOutputDirectory(CczProject project, string path)
    {
        if (string.IsNullOrWhiteSpace(path)) throw new InvalidOperationException("output_root is required.");
        var normalized = path.Replace('/', Path.DirectorySeparatorChar);
        return Path.GetFullPath(Path.IsPathRooted(normalized)
            ? normalized
            : Path.Combine(project.WorkspaceRoot, normalized));
    }

    private static string ResolveDllIconTarget(CczProject project, string targetRelativePath)
    {
        var targetPath = ResolveProjectFile(project, targetRelativePath, mustExist: true);
        if (!Path.GetExtension(targetPath).Equals(".dll", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("DLL icon target must be a .dll file.");
        }

        var fileName = Path.GetFileName(targetPath);

        return targetPath;
    }

    private static void EnsureNotObsolete66DllIconTarget(CczProject project, string targetRelativePath, int iconIndex)
    {
        if (!ItemIconMappingService.IsObsolete66DllIconResource(project, targetRelativePath))
        {
            return;
        }

        throw new InvalidOperationException(ItemIconMappingService.BuildObsolete66DllIconMessage(project, targetRelativePath, iconIndex));
    }

    private static bool TryBuildObsolete66DllIconPayload(CczProject project, string targetRelativePath, int iconIndex, out object payload)
    {
        payload = new { };
        if (!ItemIconMappingService.IsObsolete66DllIconResource(project, targetRelativePath))
        {
            return false;
        }

        var fileName = Path.GetFileName(targetRelativePath.Replace('/', Path.DirectorySeparatorChar));
        var kind = fileName.Equals("Mgcicon.dll", StringComparison.OrdinalIgnoreCase) ? "strategy" : "item";
        var mapping = new ItemIconMappingService().Resolve(project, iconIndex, kind);
        payload = new
        {
            Error = "ObsoleteRuntimeResource",
            Message = ItemIconMappingService.BuildObsolete66DllIconMessage(project, targetRelativePath, iconIndex),
            project.GameRoot,
            TargetRelativePath = targetRelativePath,
            IconIndex = iconIndex,
            ReplacementKind = kind,
            SuggestedResource = mapping.ResourceRelativePath,
            SuggestedSmallImageNumber = mapping.SmallImageNumber,
            SuggestedLargeImageNumber = mapping.LargeImageNumber,
            mapping.MappingRule,
            SafetyNote = "6.6 projects read/write item and strategy icons through E5 resources. DLL icon tools are intentionally blocked for Itemicon.dll and Mgcicon.dll; Cmdicon.dll remains a DLL resource."
        };
        return true;
    }

    private static void EnsureGenericResourceAllowed(CczProject project, string targetPath)
    {
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
            IndexOffsetHex = HexDisplayFormatter.FormatOffset(preview.IndexOffset),
            OldDataOffsetHex = HexDisplayFormatter.FormatOffset(preview.OldDataOffset),
            NewDataOffsetHex = HexDisplayFormatter.FormatOffset(preview.NewDataOffset),
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
        if (string.IsNullOrWhiteSpace(name)) throw new InvalidOperationException("Knowledge entry name is required.");
        var fullPath = Path.GetFullPath(Path.Combine(root, name));
        var rootWithSlash = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!fullPath.StartsWith(rootWithSlash, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Knowledge entry path escapes the local knowledge root.");
        }
        return fullPath;
    }

    public object ListKnowledgeEntries()
    {
        var root = FindKnowledgeRoot();
        var entries = Directory.Exists(root)
            ? Directory.GetFiles(root, "*.md", SearchOption.AllDirectories)
                .OrderBy(path => Path.GetRelativePath(root, path), StringComparer.OrdinalIgnoreCase)
                .Select(path => new { Name = Path.GetFileName(path), RelativePath = Path.GetRelativePath(root, path), Path = path, SizeBytes = new FileInfo(path).Length })
                .ToList()
            : [];
        return new { KnowledgeRoot = root, Count = entries.Count, Entries = entries };
    }

    public object ReadKnowledgeEntry(string name)
    {
        var root = FindKnowledgeRoot();
        if (!Directory.Exists(root)) throw new DirectoryNotFoundException(root);
        var fullPath = ResolveKnowledgeEntryPath(root, name);
        if (!File.Exists(fullPath) && !name.Contains('/') && !name.Contains('\\'))
        {
            var matches = Directory.GetFiles(root, "*.md", SearchOption.AllDirectories)
                .Where(path => Path.GetFileName(path).Equals(name, StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (matches.Count == 1) fullPath = matches[0];
            else if (matches.Count > 1)
            {
                var candidates = string.Join(", ", matches.Take(12).Select(path => Path.GetRelativePath(root, path)));
                throw new InvalidOperationException($"Knowledge entry name matched multiple files. Use a relative path. Matches: {candidates}");
            }
        }
        if (!File.Exists(fullPath)) throw new FileNotFoundException("Knowledge entry was not found.", fullPath);
        return new { Name = Path.GetFileName(fullPath), RelativePath = Path.GetRelativePath(root, fullPath), Path = fullPath, Text = File.ReadAllText(fullPath) };
    }

    public object SearchKnowledgeEntries(string keyword, int limit, int contextLines)
    {
        if (string.IsNullOrWhiteSpace(keyword)) throw new InvalidOperationException("keyword is required.");
        var root = FindKnowledgeRoot();
        if (!Directory.Exists(root)) throw new DirectoryNotFoundException(root);
        var effectiveLimit = NormalizeLimit(limit, 50, 500);
        var effectiveContextLines = contextLines < 0 ? 0 : Math.Min(contextLines, 3);
        var matches = new List<object>();
        var totalMatches = 0;
        foreach (var path in Directory.GetFiles(root, "*.md", SearchOption.AllDirectories).OrderBy(path => Path.GetRelativePath(root, path), StringComparer.OrdinalIgnoreCase))
        {
            var lines = File.ReadAllLines(path);
            for (var i = 0; i < lines.Length; i++)
            {
                if (!lines[i].Contains(keyword, StringComparison.OrdinalIgnoreCase)) continue;
                totalMatches++;
                if (matches.Count >= effectiveLimit) continue;
                var snippetStart = Math.Max(0, i - effectiveContextLines);
                var snippetEnd = Math.Min(lines.Length - 1, i + effectiveContextLines);
                matches.Add(new { Name = Path.GetFileName(path), RelativePath = Path.GetRelativePath(root, path), Path = path, LineNumber = i + 1, MatchedLine = lines[i].Trim(), Snippet = string.Join(Environment.NewLine, lines.Skip(snippetStart).Take(snippetEnd - snippetStart + 1)) });
            }
        }
        return new { KnowledgeRoot = root, Keyword = keyword, TotalMatches = totalMatches, ReturnedMatches = matches.Count, ContextLines = effectiveContextLines, Matches = matches };
    }

    private static bool MatchesKeyword(DataRow row, IReadOnlyList<DataColumn> columns, string? keyword)
    {
        if (string.IsNullOrWhiteSpace(keyword)) return true;
        return columns.Any(column => Convert.ToString(row[column], CultureInfo.InvariantCulture)?.Contains(keyword, StringComparison.OrdinalIgnoreCase) == true);
    }

    private IReadOnlyDictionary<byte, string> BuildTerrainNameLookup(CczProject project)
    {
        try { return HexzmapProbeReader.BuildTerrainNameLookup(_materialLibraryIndexer.Index(project)); }
        catch { return new Dictionary<byte, string>(); }
    }

    private static bool MatchesHexzmapBlockKeyword(HexzmapBlockInfo block, string? keyword)
    {
        if (string.IsNullOrWhiteSpace(keyword)) return true;
        return block.MapId.Contains(keyword, StringComparison.OrdinalIgnoreCase) || block.MapImageName.Contains(keyword, StringComparison.OrdinalIgnoreCase) || block.OffsetHex.Contains(keyword, StringComparison.OrdinalIgnoreCase) || block.DominantTerrainName.Contains(keyword, StringComparison.OrdinalIgnoreCase) || block.TopTerrainIds.Contains(keyword, StringComparison.OrdinalIgnoreCase) || block.TopTerrainNames.Contains(keyword, StringComparison.OrdinalIgnoreCase) || block.UnknownTerrainIds.Contains(keyword, StringComparison.OrdinalIgnoreCase) || block.Annotation.Contains(keyword, StringComparison.OrdinalIgnoreCase);
    }

    private static object BuildHexzmapBlockPayload(HexzmapBlockInfo block)
        => new { block.Index, block.MapId, block.MapImageName, block.MapImageExists, block.MapPixelWidth, block.MapPixelHeight, block.Width, block.Height, block.BytesRead, block.CanEdit, OffsetHex = HexDisplayFormatter.NormalizeText(block.OffsetHex), block.DataOffset, block.SegmentOffset, SegmentOffsetHex = HexDisplayFormatter.FormatOffset(block.SegmentOffset), block.SegmentLength, block.DecodedLength, block.DataPrefixLength, block.UniqueTerrainCount, block.DominantTerrainId, DominantTerrainHex = HexDisplayFormatter.Format(block.DominantTerrainId, 2), block.DominantTerrainName, block.DominantTerrainCount, TopTerrainIds = HexDisplayFormatter.NormalizeText(block.TopTerrainIds), block.TopTerrainNames, block.KnownTerrainCount, UnknownTerrainIds = HexDisplayFormatter.NormalizeText(block.UnknownTerrainIds), Annotation = HexDisplayFormatter.NormalizeText(block.Annotation) };

    private static IReadOnlyList<object> BuildHexzmapTerrainCounts(byte[] cells, IReadOnlyDictionary<byte, string> terrainLookup)
    {
        var total = Math.Max(1, cells.Length);
        return cells.GroupBy(value => value).OrderByDescending(group => group.Count()).ThenBy(group => group.Key).Take(16).Select(group => new { TerrainId = (int)group.Key, TerrainHex = HexDisplayFormatter.FormatByte(group.Key), TerrainName = ResolveTerrainName(terrainLookup, group.Key), Count = group.Count(), Percent = Math.Round(group.Count() * 100.0 / total, 2) }).Cast<object>().ToList();
    }

    private static IReadOnlyList<object> BuildHexzmapCellRows(byte[] cells, int width, IReadOnlyDictionary<byte, string> terrainLookup, int maxRows)
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
            rows.Add(new { Y = y, TerrainIds = row.Select(value => (int)value).ToArray(), TerrainHex = row.Select(HexDisplayFormatter.FormatByte).ToArray(), TerrainSummary = BuildHexzmapRowTerrainSummary(row, terrainLookup) });
        }
        return rows;
    }

    private static string BuildHexzmapRowTerrainSummary(byte[] row, IReadOnlyDictionary<byte, string> terrainLookup)
        => string.Join(" / ", row.GroupBy(value => value).OrderByDescending(group => group.Count()).ThenBy(group => group.Key).Take(8).Select(group => { var name = ResolveTerrainName(terrainLookup, group.Key); var terrainHex = HexDisplayFormatter.FormatByte(group.Key); var label = string.IsNullOrWhiteSpace(name) ? terrainHex : terrainHex + "(" + name + ")"; return label + ":" + group.Count().ToString(CultureInfo.InvariantCulture); }));

    private static string ResolveTerrainName(IReadOnlyDictionary<byte, string> terrainLookup, byte terrainId)
        => terrainLookup.TryGetValue(terrainId, out var name) ? name : string.Empty;

    private static object ConvertJsonValue(JsonElement value)
    {
        return value.ValueKind switch { JsonValueKind.String => value.GetString() ?? string.Empty, JsonValueKind.Number when value.TryGetInt64(out var longValue) => longValue, JsonValueKind.Number => value.GetRawText(), JsonValueKind.True => 1, JsonValueKind.False => 0, JsonValueKind.Null or JsonValueKind.Undefined => string.Empty, _ => value.ToString() };
    }

    private static int NormalizeLimit(int limit, int defaultValue, int maxValue)
    {
        if (limit <= 0) return defaultValue;
        return Math.Min(limit, maxValue);
    }

    private (SceneStringDocument? Dictionary, string DictionaryPath) LoadScenarioCommandDictionary(CczProject project)
    {
        var dictionaryPath = ProjectDetector.FindSceneDictionaryPath(project);
        if (!File.Exists(dictionaryPath)) return (null, dictionaryPath);
        return (_sceneStringParser.Parse(dictionaryPath), dictionaryPath);
    }

    private SceneStringDocument LoadRequiredScenarioCommandDictionary(CczProject project)
    {
        var (dictionary, dictionaryPath) = LoadScenarioCommandDictionary(project);
        if (dictionary == null || dictionary.Commands.Count == 0) throw new FileNotFoundException("Scenario command dictionary CczString.ini was not found or contains no commands.", dictionaryPath);
        return dictionary;
    }

    private IReadOnlyList<string> ResolveScenarioSearchFiles(CczProject project, string? relativePath, string? fileKind)
    {
        if (!string.IsNullOrWhiteSpace(relativePath)) return new[] { ResolveScenarioFile(project, relativePath) };
        return _scenarioFileReader.ReadAllIndex(project).Where(file => MatchesScenarioFileKind(file, fileKind)).Select(file => file.Path).ToList();
    }

    private static string ResolveScenarioFile(CczProject project, string relativePath)
    {
        var filePath = ResolveProjectFile(project, relativePath, mustExist: true);
        EnsureScenarioTargetAllowed(project, filePath);
        return filePath;
    }

    private static void EnsureScenarioTargetAllowed(CczProject project, string targetPath)
    {
        if (!Path.GetExtension(targetPath).Equals(".eex", StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Scenario target must be an .eex file.");
        if (!ScenarioFileReader.IsRsScriptFile(Path.GetFileName(targetPath))) throw new InvalidOperationException("Scenario target must be an RS/R_*.eex or RS/S_*.eex script file.");
        var normalized = NormalizeProjectRelativePath(project, targetPath).Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
        if (!normalized.StartsWith("RS" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Scenario target must be under the project RS directory.");
    }

    private static bool MatchesScenarioFileKind(ScenarioFileInfo file, string? kind)
    {
        if (string.IsNullOrWhiteSpace(kind)) return true;
        var normalized = kind.Trim();
        if (normalized.Equals("R", StringComparison.OrdinalIgnoreCase)) return file.Kind.Equals("R剧本", StringComparison.Ordinal);
        if (normalized.Equals("S", StringComparison.OrdinalIgnoreCase)) return file.Kind.Equals("S剧本", StringComparison.Ordinal);
        return file.Kind.Contains(normalized, StringComparison.OrdinalIgnoreCase) || file.FileName.StartsWith(normalized, StringComparison.OrdinalIgnoreCase);
    }

    private static object BuildScenarioFilePayload(ScenarioFileInfo file)
        => new { file.FileName, file.Id, file.Kind, file.Length, file.WordCount, file.UsedBytes, file.UsedPercent, file.TitleHint, file.TextHintCount, file.FirstTextOffsetHex, file.Annotation, file.UsageAnnotation, file.Path };

    private static bool MatchesLegacyCommandFilter(LegacyScenarioCommandNode command, string? commandFilter)
    {
        if (string.IsNullOrWhiteSpace(commandFilter)) return true;
        commandFilter = commandFilter.Trim();
        if (TryParseScenarioCommandId(commandFilter, out var id)) return command.CommandId == id;
        return command.CommandName.Contains(commandFilter, StringComparison.OrdinalIgnoreCase) || command.CommandIdHex.Contains(commandFilter, StringComparison.OrdinalIgnoreCase);
    }

    private static bool MatchesLegacyCommandKeyword(LegacyScenarioCommandNode command, string? keyword)
    {
        if (string.IsNullOrWhiteSpace(keyword)) return true;
        return command.CommandName.Contains(keyword, StringComparison.OrdinalIgnoreCase) || command.CommandIdHex.Contains(keyword, StringComparison.OrdinalIgnoreCase) || command.FileOffset.ToString(CultureInfo.InvariantCulture).Contains(keyword, StringComparison.OrdinalIgnoreCase) || HexDisplayFormatter.FormatOffset(command.FileOffset).Contains(HexDisplayFormatter.NormalizeText(keyword), StringComparison.OrdinalIgnoreCase) || command.Parameters.Any(parameter => parameter.DisplayValue.Contains(keyword, StringComparison.OrdinalIgnoreCase) || parameter.LayoutCodeHex.Contains(keyword, StringComparison.OrdinalIgnoreCase) || parameter.TagHex.Contains(keyword, StringComparison.OrdinalIgnoreCase));
    }

    private static bool MatchesScenarioTextKeyword(ScenarioTextEntry text, string keyword)
        => text.Text.Contains(keyword, StringComparison.OrdinalIgnoreCase) || text.Preview.Contains(keyword, StringComparison.OrdinalIgnoreCase) || text.Kind.Contains(keyword, StringComparison.OrdinalIgnoreCase) || text.Annotation.Contains(keyword, StringComparison.OrdinalIgnoreCase) || text.OffsetHex.Contains(keyword, StringComparison.OrdinalIgnoreCase);

    private static object BuildLegacyScenarioCommandPayload(LegacyScenarioCommandNode command)
        => new { command.CommandOrdinal, command.SceneIndex, command.SectionIndex, command.CommandIndex, command.CommandId, CommandIdHex = HexDisplayFormatter.NormalizeText(command.CommandIdHex), command.CommandName, OffsetHex = HexDisplayFormatter.FormatOffset(command.FileOffset), command.FileOffset, command.ConsumedBytes, command.StartsBodyBlock, command.IsSubEventMarker, command.OpensSubEventBlock, command.EndsSubEventBlock, command.JumpTargetOrdinal, command.JumpTargetCommandIndex, command.OriginalJumpDisplacement, TextParameterCount = command.TextParameters.Count(), Parameters = command.Parameters.Select(BuildLegacyScenarioParameterPayload) };

    private static object BuildLegacyScenarioParameterPayload(LegacyScenarioCommandParameter parameter)
        => new { parameter.Index, Kind = parameter.Kind.ToString(), parameter.LayoutCode, LayoutCodeHex = HexDisplayFormatter.NormalizeText(parameter.LayoutCodeHex), parameter.Tag, TagHex = HexDisplayFormatter.NormalizeText(parameter.TagHex), OffsetHex = HexDisplayFormatter.FormatOffset(parameter.FileOffset), parameter.FileOffset, parameter.ByteLength, parameter.IntValue, Values = parameter.Values.Take(32).ToList(), ValuePreview = HexDisplayFormatter.NormalizeText(TrimForMcp(parameter.DisplayValue, 600)) };

    private static bool MatchesScenarioCommandKeyword(ScenarioCommandTemplateCatalogItem item, string? keyword)
    {
        if (string.IsNullOrWhiteSpace(keyword)) return true;
        return item.IdHex.Contains(keyword, StringComparison.OrdinalIgnoreCase) || item.DictionaryName.Contains(keyword, StringComparison.OrdinalIgnoreCase) || item.TemplateName.Contains(keyword, StringComparison.OrdinalIgnoreCase) || item.Status.Contains(keyword, StringComparison.OrdinalIgnoreCase) || item.Category.Contains(keyword, StringComparison.OrdinalIgnoreCase) || item.SlotSummary.Contains(keyword, StringComparison.OrdinalIgnoreCase) || item.Purpose.Contains(keyword, StringComparison.OrdinalIgnoreCase) || item.Risk.Contains(keyword, StringComparison.OrdinalIgnoreCase) || item.CreatorTip.Contains(keyword, StringComparison.OrdinalIgnoreCase);
    }

    private static IEnumerable<ScenarioCommandTemplateCatalogItem> FindScenarioCommandTemplates(IReadOnlyList<ScenarioCommandTemplateCatalogItem> items, string query)
    {
        if (TryParseScenarioCommandId(query, out var id)) return items.Where(item => item.Id == id);
        var exact = items.Where(item => item.IdHex.Equals(query, StringComparison.OrdinalIgnoreCase) || item.DictionaryName.Equals(query, StringComparison.OrdinalIgnoreCase) || item.TemplateName.Equals(query, StringComparison.OrdinalIgnoreCase)).ToList();
        return exact.Count > 0 ? exact : items.Where(item => MatchesScenarioCommandKeyword(item, query));
    }

    private static bool TryParseScenarioCommandId(string text, out int id)
    {
        id = 0;
        text = text.Trim();
        var dash = text.IndexOf('-', StringComparison.Ordinal);
        if (dash > 0 &&
            int.TryParse(text[..dash], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var commandId) &&
            int.TryParse(text[(dash + 1)..], NumberStyles.Integer, CultureInfo.InvariantCulture, out var subId))
        {
            id = checked(commandId * 0x100 + subId);
            return true;
        }

        if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) return int.TryParse(text[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out id);
        if (text.Length <= 2 && text.All(IsHexDigit)) return int.TryParse(text, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out id);
        return int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out id);
    }

    private static bool IsHexDigit(char value) => value is >= '0' and <= '9' or >= 'a' and <= 'f' or >= 'A' and <= 'F';

    private static object BuildScenarioCommandTemplatePayload(ScenarioCommandTemplateCatalogItem item)
        => new { item.Id, item.IdHex, item.DictionaryName, item.TemplateName, item.Status, item.Category, item.SlotCount, item.SlotSummary, item.Purpose, item.Risk, item.CreatorTip, item.SafetyNote };

    private static bool MatchesImageResourceKeyword(ImageResourceFileInfo resource, string? keyword)
    {
        if (string.IsNullOrWhiteSpace(keyword)) return true;
        keyword = keyword.Trim();
        return resource.Key.Contains(keyword, StringComparison.OrdinalIgnoreCase) || resource.Category.Contains(keyword, StringComparison.OrdinalIgnoreCase) || resource.DisplayName.Contains(keyword, StringComparison.OrdinalIgnoreCase) || resource.FileName.Contains(keyword, StringComparison.OrdinalIgnoreCase) || resource.Aliases.Contains(keyword, StringComparison.OrdinalIgnoreCase) || resource.Usage.Contains(keyword, StringComparison.OrdinalIgnoreCase) || resource.RelativePath.Contains(keyword, StringComparison.OrdinalIgnoreCase) || resource.Path.Contains(keyword, StringComparison.OrdinalIgnoreCase) || resource.ResourceFormat.Contains(keyword, StringComparison.OrdinalIgnoreCase) || resource.KindSummary.Contains(keyword, StringComparison.OrdinalIgnoreCase) || resource.Status.Contains(keyword, StringComparison.OrdinalIgnoreCase);
    }

    private static bool MatchesImageResourceEntryKeyword(ImageResourceEntryInfo entry, string? keyword)
    {
        if (string.IsNullOrWhiteSpace(keyword)) return true;
        keyword = keyword.Trim();
        return entry.ResourceKey.Contains(keyword, StringComparison.OrdinalIgnoreCase) || entry.Category.Contains(keyword, StringComparison.OrdinalIgnoreCase) || entry.ResourceName.Contains(keyword, StringComparison.OrdinalIgnoreCase) || entry.FileName.Contains(keyword, StringComparison.OrdinalIgnoreCase) || entry.Kind.Contains(keyword, StringComparison.OrdinalIgnoreCase) || entry.Usage.Contains(keyword, StringComparison.OrdinalIgnoreCase) || entry.Path.Contains(keyword, StringComparison.OrdinalIgnoreCase);
    }

    private ImageResourceFileInfo ResolveImageResource(CczProject project, string resource)
    {
        if (string.IsNullOrWhiteSpace(resource)) throw new InvalidOperationException("resource is required.");
        var query = resource.Trim();
        var catalog = _imageResourceCatalog.BuildCatalog(project);
        var normalizedQuery = query.Replace('/', Path.DirectorySeparatorChar);
        var exact = catalog.FirstOrDefault(item => item.Key.Equals(query, StringComparison.OrdinalIgnoreCase) || item.FileName.Equals(query, StringComparison.OrdinalIgnoreCase) || item.DisplayName.Equals(query, StringComparison.OrdinalIgnoreCase) || item.RelativePath.Equals(normalizedQuery, StringComparison.OrdinalIgnoreCase) || item.Path.Equals(query, StringComparison.OrdinalIgnoreCase));
        if (exact != null) return exact;
        var targetPath = ResolveOptionalProjectFile(project, query);
        if (targetPath != null)
        {
            exact = catalog.FirstOrDefault(item => item.Path.Equals(targetPath, StringComparison.OrdinalIgnoreCase));
            if (exact != null) return exact;
        }
        var matches = catalog.Where(item => MatchesImageResourceKeyword(item, query)).ToList();
        if (matches.Count == 1) return matches[0];
        if (matches.Count > 1) throw new InvalidOperationException($"Image resource query matched multiple resources. Use a more precise key or file name. Matches: {string.Join(", ", matches.Take(12).Select(item => item.Key + "/" + item.FileName))}");
        throw new InvalidOperationException("Image resource was not found: " + resource);
    }

    private static object BuildImageResourcePayload(CczProject project, ImageResourceFileInfo resource)
        => new { resource.Key, resource.Category, resource.DisplayName, resource.FileName, resource.Aliases, resource.Usage, resource.RelativePath, ProjectRelativePath = TryNormalizeProjectRelativePath(project, resource.Path), resource.Path, resource.Exists, resource.SizeBytes, resource.EntryCount, resource.SupportsE5Index, resource.SupportsPreview, resource.CanReplace, resource.ResourceFormat, resource.KindSummary, resource.Status, resource.SafetyNote };

    private static object BuildImageResourceEntryPayload(CczProject project, ImageResourceEntryInfo entry)
        => new { entry.ResourceKey, entry.Category, entry.ResourceName, entry.FileName, ProjectRelativePath = TryNormalizeProjectRelativePath(project, entry.Path), entry.Path, entry.ImageNumber, IndexOffsetHex = HexDisplayFormatter.FormatOffset(entry.IndexOffset), DataOffsetHex = HexDisplayFormatter.FormatOffset(entry.DataOffset), entry.IndexOffset, entry.DataOffset, entry.StoredLength, entry.DecodedLength, entry.IsCompressed, entry.Kind, entry.Usage, entry.CanReplace };

    private object BuildAiImageReplacementPreview(CczProject project, AiImagePromptPlan plan, string outputPath)
    {
        if (plan.Preset.TargetKind == "dll_icon" && !Path.GetExtension(plan.TargetRelativePath).Equals(".e5", StringComparison.OrdinalIgnoreCase))
        {
            return BuildDllIconReplacePayload(_iconResourceReplace.PreviewReplaceBitmapIcon(project, ResolveDllIconTarget(project, plan.TargetRelativePath), plan.TargetImageNumbers.FirstOrDefault(), outputPath));
        }

        if (plan.TargetImageNumbers.Count > 1 || plan.Preset.TargetKind == "e5_batch")
        {
            var targetPath = ResolveProjectFile(project, plan.TargetRelativePath, mustExist: true);
            EnsureE5ImageTargetAllowed(project, targetPath);
            var requests = plan.TargetImageNumbers.Select(imageNumber => new E5ImageBatchReplaceRequest { ImageNumber = imageNumber, SourcePath = outputPath, SourceLabel = outputPath, OperationKind = "AI生成预览替换" }).ToArray();
            return BuildE5ImageBatchReplacePayload(_e5ImageReplace.PreviewBatchReplacement(project, targetPath, requests));
        }
        var e5TargetPath = ResolveProjectFile(project, plan.TargetRelativePath, mustExist: true);
        EnsureE5ImageTargetAllowed(project, e5TargetPath);
        return BuildE5ImageReplacePayload(_e5ImageReplace.PreviewReplacement(project, e5TargetPath, plan.TargetImageNumbers.First(), outputPath));
    }

    private static object BuildAiImagePromptPlanPayload(AiImagePromptPlan plan)
        => new { Preset = plan.Preset, plan.Description, plan.Prompt, plan.NegativePrompt, plan.TargetRelativePath, plan.TargetImageNumbers, plan.TargetWidth, plan.TargetHeight, plan.OutputFormat, plan.GenerationSize, plan.Quality, plan.MappingSummary, plan.Warnings, plan.ReferenceImages, SafetyNote = "AI drawing plan only describes generation, post-processing, and replacement preview; it does not write game resources." };

    private static object BuildAiImagePreparePayload(AiImagePrepareResult result)
        => new { Plan = BuildAiImagePromptPlanPayload(result.Plan), result.SourcePath, result.OutputPath, result.ManifestPath, result.SourceWidth, result.SourceHeight, result.OutputWidth, result.OutputHeight, result.OutputFormat, result.SourceSha256, result.OutputSha256, result.PostProcessSummary, result.ReplacementPreview, PreparedFiles = result.PreparedFiles.Select(BuildAiImagePreparedFilePayload) };

    private static object BuildAiImagePreparedFilePayload(AiImagePreparedFile file)
        => new { file.Role, file.TargetRelativePath, file.TargetImageNumbers, file.OutputPath, file.OutputWidth, file.OutputHeight, file.OutputSha256, file.ReplacementPreview };

    private static object BuildAiImageDrawPayload(AiImageDrawResult result)
        => new { result.DryRun, Plan = BuildAiImagePromptPlanPayload(result.Plan), result.Provider, result.ApiMode, result.BaseUrl, result.TextModel, result.ImageModel, result.RawResponsePath, result.GeneratedSourcePath, Prepared = result.Prepared == null ? null : BuildAiImagePreparePayload(result.Prepared), result.Logs };

    private static object BuildRsPixelCharacterDesignPayload(RsPixelCharacterDesignResult result)
        => new
        {
            result.PackageId,
            result.PackageRoot,
            result.GenerationStatus,
            result.DesignImagePath,
            result.FormatActionImagePath,
            result.Warnings,
            result.Errors,
            result.Reports,
            SUnitPlan = BuildAiImagePromptPlanPayload(result.SUnitPlan),
            RActorPlan = BuildAiImagePromptPlanPayload(result.RActorPlan),
            SUnitDraw = result.SUnitDraw == null ? null : BuildAiImageDrawPayload(result.SUnitDraw),
            RActorDraw = result.RActorDraw == null ? null : BuildAiImageDrawPayload(result.RActorDraw),
            result.SafetyNote
        };

    private static object BuildRsPixelEditWorkspacePayload(RsPixelEditWorkspaceResult result)
        => new
        {
            result.PackageId,
            result.PackageRoot,
            result.WorkspaceRoot,
            result.MaterialsRoot,
            MaterialFiles = result.MaterialFiles.Select(file => new
            {
                file.Role,
                file.SourcePath,
                file.WorkspacePath,
                file.MaterialPath,
                file.Width,
                file.Height,
                file.FrameWidth,
                file.FrameHeight,
                file.FrameCount,
                file.Sha256
            }),
            result.Reports,
            result.Warnings,
            result.SafetyNote
        };

    private static object BuildRsPixelEditPlanPayload(RsPixelEditPlanResult result)
        => new
        {
            result.PackageRoot,
            result.PlanPath,
            RecommendedOperations = result.RecommendedOperations.Select(BuildRsPixelFrameEditOperationPayload),
            result.ReviewGates,
            result.SafetyNote
        };

    private static object BuildRsPixelFrameEditBatchPayload(RsPixelFrameEditBatchResult result)
        => new
        {
            result.PackageRoot,
            result.OperationCount,
            result.ChangedPixelCount,
            result.EditLogPath,
            result.WrittenFiles,
            result.Warnings,
            EditLogEntries = result.EditLogEntries.Select(entry => new
            {
                entry.Timestamp,
                entry.Operation,
                entry.Target,
                entry.Frames,
                entry.X,
                entry.Y,
                entry.Width,
                entry.Height,
                entry.ChangedPixelCount,
                entry.Note
            })
        };

    private static object BuildRsPixelContactSheetPayload(RsPixelContactSheetResult result)
        => new
        {
            result.PackageRoot,
            result.Scale,
            result.ContactSheets,
            result.ReportPath,
            SafetyNote = "Contact sheets are preview exports only and do not write game resources."
        };

    private static object BuildRsPixelEditValidationPayload(RsPixelEditValidationResult result)
        => new
        {
            result.PackageRoot,
            result.LocalPixelEditPassed,
            result.ReportPath,
            result.SingleSpearRiskFrames,
            result.FaceRiskFrames,
            result.Warnings,
            FrameCheckCount = result.FrameChecks.Count,
            EmptyFrames = result.FrameChecks.Where(check => !check.NonEmpty).Select(check => $"{check.Target}:{check.FrameIndex.ToString(CultureInfo.InvariantCulture)}").ToArray(),
            MaterialValidation = BuildRsPixelMaterialValidationPayload(result.MaterialValidation)
        };

    private static object BuildRsPixelFrameEditOperationPayload(RsPixelFrameEditOperation operation)
        => new
        {
            operation.Operation,
            operation.Target,
            operation.Frames,
            operation.X,
            operation.Y,
            operation.Width,
            operation.Height,
            operation.X2,
            operation.Y2,
            operation.Color,
            operation.SecondaryColor,
            operation.Note
        };

    private List<E5ImageBatchReplaceRequest> BuildE5ImageBatchRequests(CczProject project, IReadOnlyList<E5ImageBatchUpdate> updates)
    {
        if (updates.Count == 0) throw new InvalidOperationException("updates must contain at least one E5 image batch update.");
        var requests = new List<E5ImageBatchReplaceRequest>();
        foreach (var update in updates)
        {
            if (update.ImageNumber <= 0) throw new InvalidOperationException("image_number must be positive.");
            var sourcePath = ResolveExternalFile(project, update.ReplacementPath);
            var sourceLabel = sourcePath;
            byte[]? sourceBytes = null;
            if (update.SourceImageNumber.HasValue)
            {
                if (update.SourceImageNumber.Value <= 0) throw new InvalidOperationException("source_image_number must be positive when supplied.");
                sourceBytes = _e5ImageReplace.ReadEntryBytes(sourcePath, update.SourceImageNumber.Value);
                sourceLabel = $"{sourcePath}#{update.SourceImageNumber.Value}";
            }
            requests.Add(new E5ImageBatchReplaceRequest { ImageNumber = update.ImageNumber, SourcePath = sourcePath, SourceBytes = sourceBytes, SourceLabel = sourceLabel, OperationKind = string.IsNullOrWhiteSpace(update.OperationKind) ? "replace" : update.OperationKind });
        }
        return requests;
    }

    private static object BuildE5ImageBatchReplacePayload(E5ImageBatchReplacePreviewResult preview)
        => new { preview.TargetPath, preview.TargetRelativePath, preview.OperationCount, preview.OldFileSizeBytes, preview.NewFileSizeBytes, preview.FileSizeDeltaBytes, preview.ChangedBytesEstimate, preview.OldFileSha256, preview.NewFileSha256, preview.FormatWarnings, preview.RiskSummary, Operations = preview.Operations.Select(operation => new { operation.ImageNumber, IndexOffsetHex = HexDisplayFormatter.FormatOffset(operation.IndexOffset), OldDataOffsetHex = HexDisplayFormatter.FormatOffset(operation.OldDataOffset), NewDataOffsetHex = HexDisplayFormatter.FormatOffset(operation.NewDataOffset), operation.IndexOffset, operation.OldDataOffset, operation.NewDataOffset, operation.OldSizeBytes, operation.NewSizeBytes, operation.OldKind, operation.NewKind, operation.SourcePath, operation.OperationKind, operation.SourceSha256, operation.SourceWidth, operation.SourceHeight, operation.Placement, operation.FormatWarnings }) };

    private static object BuildDllIconReplacePayload(IconResourceReplacePreviewResult preview)
        => new { preview.TargetPath, preview.TargetRelativePath, preview.IconIndex, preview.ResourceIds, preview.SourcePath, preview.OperationKind, preview.OldFileSizeBytes, preview.SourceSizeBytes, preview.OldFileSha256, preview.SourceSha256, preview.SourceWidth, preview.SourceHeight, preview.ResourceFormat, preview.FormatWarnings, preview.RiskSummary };

    private static object BuildResourceReplacePreviewPayload(ResourceReplacePreviewResult preview)
        => new { preview.TargetPath, preview.TargetRelativePath, preview.ReplacementPath, preview.Extension, preview.OldSizeBytes, preview.NewSizeBytes, preview.SizeDeltaBytes, preview.ChangedBytesEstimate, preview.ChangedPercent, preview.OldSha256, preview.NewSha256, preview.IsContentIdentical, preview.FormatCheckSummary, preview.FormatWarnings, preview.RiskSummary };

    private static object BuildMapImageReplacePreviewPayload(MapImageReplacePreviewResult preview)
        => new { preview.TargetPath, preview.TargetRelativePath, preview.ReplacementPath, preview.OldSizeBytes, preview.NewSizeBytes, preview.SizeDeltaBytes, preview.OldWidth, preview.OldHeight, preview.NewWidth, preview.NewHeight, preview.ChangedBytesEstimate, preview.ChangedPercent, preview.OldSha256, preview.NewSha256, preview.IsContentIdentical, preview.FormatCheckSummary, preview.FormatWarnings, preview.RiskSummary, SafetyNote = "Read-only preview. No game file, backup, or report is written by preview_map_image." };
}
