using System.Data;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.Reflection;
using System.Text;
using CCZModStudio.Core;
using CCZModStudio.Formats;
using CCZModStudio.Models;

namespace CCZModStudio.McpServer;

public sealed partial class CczMcpRuntime
{
    private static readonly string[] AuthoringToolNames =
    [
        "read_mcp_capability_manifest",
        "replace_job_s_image_raw_batch",
        "write_item_effect_name66_slot",
        "export_table_csv",
        "preview_import_table_csv",
        "apply_import_table_csv",
        "read_table_schema",
        "read_table_derived_display",
        "read_role_editor",
        "preview_write_roles",
        "write_roles",
        "read_role_texts",
        "preview_write_role_texts",
        "write_role_texts",
        "find_free_image_assignment_ids",
        "preview_image_assignment_update",
        "write_image_assignment_update",
        "read_editable_image_target",
        "preview_editable_image_write",
        "write_editable_image",
        "list_portrait_frames",
        "preview_apply_portrait_frame",
        "apply_portrait_frame",
        "preview_extract_map_materials",
        "extract_map_materials",
        "preview_terrain_beautify_filter",
        "apply_terrain_beautify_to_draft",
        "diagnose_qinger66_project",
        "audit_qinger66_items",
        "list_legacy_mfc_dialogs",
        "read_legacy_mfc_dialog",
        "read_scenario_reference_checklist"
    ];

    private static readonly IReadOnlyDictionary<string, string[]> AuthoringManifestGroups =
        new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["project"] = ["detect_project", "list_tables", "read_mcp_capability_manifest"],
            ["tables"] =
            [
                "read_table", "write_table_rows", "read_table_schema", "read_table_derived_display",
                "export_table_csv", "preview_import_table_csv", "apply_import_table_csv"
            ],
            ["roles"] =
            [
                "read_role_editor", "preview_write_roles", "write_roles",
                "read_role_texts", "preview_write_role_texts", "write_role_texts"
            ],
            ["image_assignments"] =
            [
                "find_free_image_assignment_ids", "preview_image_assignment_update", "write_image_assignment_update"
            ],
            ["editable_images"] =
            [
                "read_editable_image_target", "preview_editable_image_write", "write_editable_image"
            ],
            ["portrait_frames"] =
            [
                "list_portrait_frames", "preview_apply_portrait_frame", "apply_portrait_frame"
            ],
            ["map_authoring"] =
            [
                "preview_extract_map_materials", "extract_map_materials",
                "preview_terrain_beautify_filter", "apply_terrain_beautify_to_draft"
            ],
            ["diagnostics"] =
            [
                "diagnose_qinger66_project", "audit_qinger66_items",
                "list_legacy_mfc_dialogs", "read_legacy_mfc_dialog", "read_scenario_reference_checklist"
            ]
        };

    private readonly EditableImageCodecService _editableImageCodecService = new();
    private readonly PortraitFrameApplyService _portraitFrameApplyService = new();
    private readonly MapMaterialExtractionService _mapMaterialExtractionService = new();
    private readonly TableDerivedDisplayService _tableDerivedDisplayService = new();
    private readonly ImageAssignmentService _imageAssignmentService = new();
    private readonly ImageAssignmentPreviewService _imageAssignmentPreviewService = new();
    private readonly RoleQuoteMappingService _roleQuoteMappingService = new();

    public object ReadMcpCapabilityManifest(string? gameRoot)
    {
        CczProject? project = null;
        string? projectDetectionError = null;
        try
        {
            project = LoadProject(gameRoot);
        }
        catch (Exception ex)
        {
            projectDetectionError = ex.Message;
        }

        var toolNames = typeof(CczMcpTools)
            .GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly)
            .Where(method => method.GetCustomAttributes().Any(attribute => attribute.GetType().Name == "McpServerToolAttribute"))
            .Select(method => method.Name)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();
        var assembly = typeof(CczMcpTools).Assembly.GetName();
        return new
        {
            Service = "CCZModStudio.McpServer",
            DocumentVersion = "2026-07-07",
            SchemaVersion = 1,
            Build = new
            {
                AssemblyName = assembly.Name,
                Version = assembly.Version?.ToString() ?? string.Empty,
                TargetFramework = "net8.0-windows"
            },
            GameRoot = project?.GameRoot ?? string.Empty,
            ProjectDetection = new
            {
                Ok = project != null,
                Error = projectDetectionError ?? string.Empty
            },
            ToolCount = toolNames.Length,
            ToolDiscoverySource = "runtime reflection over CczMcpTools public MCP methods",
            Groups = AuthoringManifestGroups,
            AddedAuthoringTools = AuthoringToolNames,
            Aliases = new[]
            {
                new { Alias = "replace_job_s_image_raw_batch", Target = "replace_job_s_image_raw_batch_replace", DeprecatedNameKept = true },
                new { Alias = "write_item_effect_name66_slot", Target = "write_item_effect_name_66_slot", DeprecatedNameKept = true }
            },
            RemovedOrForbidden = new[] { "promote_test_copy_mod" },
            Safety = new
            {
                WriteMode = "write_mode is accepted for compatibility and normalized by existing runtime guards; default is direct.",
                PreviewTools = "preview_* methods do not modify game files. Export/report tools write only CCZModStudio_Exports or CCZModStudio_Reports unless an explicit output path is supplied.",
                GameDebugBoundary = "This manifest covers the authoring MCP server only; GameDebug tools keep their existing process/debug safety boundary."
            },
            Tools = toolNames
        };
    }

    public object ExportTableCsv(
        string? gameRoot,
        string tableName,
        string? outputPath,
        List<string>? columns,
        List<int>? rowIds,
        string? keyword,
        bool includeAnnotationRow,
        int limit)
    {
        var project = LoadProject(gameRoot);
        var tables = LoadTables(project);
        var table = FindTable(project, tables, tableName);
        var read = _tableReader.Read(project, table, tables);
        EnsureReadableTable(read);
        var selectedColumns = ResolveColumns(read.Data, columns, includeId: true)
            .Select(column => column.ColumnName)
            .ToArray();
        var rowIdSet = rowIds is { Count: > 0 } ? rowIds.ToHashSet() : null;
        var effectiveLimit = NormalizeLimit(limit, 10000, 50000);
        var selectedRows = read.Data.Rows.Cast<DataRow>()
            .Where(row => rowIdSet == null || rowIdSet.Contains(Convert.ToInt32(row["ID"], CultureInfo.InvariantCulture)))
            .Where(row => MatchesKeyword(row, selectedColumns.Select(name => read.Data.Columns[name]!).ToArray(), keyword))
            .Take(effectiveLimit)
            .ToArray();
        var targetPath = string.IsNullOrWhiteSpace(outputPath)
            ? BuildExportPath(project, "TableCsv", $"{table.TableName}.csv")
            : ResolveExportOrExternalOutput(project, outputPath!);
        Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
        if (includeAnnotationRow)
        {
            var annotations = selectedColumns.ToDictionary(
                name => name,
                name => BuildColumnAnnotation(table, read.Data.Columns[name]!),
                StringComparer.Ordinal);
            CsvService.ExportColumnsRowsWithAnnotationRow(read.Data, targetPath, selectedColumns, annotations, selectedRows);
        }
        else
        {
            CsvService.ExportColumnsRows(read.Data, targetPath, selectedColumns, selectedRows);
        }

        return new
        {
            project.GameRoot,
            table.TableName,
            read.Validation.TableStatus,
            OutputPath = targetPath,
            Encoding = "UTF-8 with BOM",
            TotalRows = read.Data.Rows.Count,
            ExportedRows = selectedRows.Length,
            Columns = selectedColumns,
            IncludeAnnotationRow = includeAnnotationRow,
            SizeBytes = new FileInfo(targetPath).Length,
            SafetyNote = "CSV export writes only the requested/export path; no game files were modified."
        };
    }

    public object PreviewImportTableCsv(
        string? gameRoot,
        string tableName,
        string csvPath,
        bool allowPartialColumns,
        bool matchByIdWhenPresent)
    {
        var project = LoadProject(gameRoot);
        var tables = LoadTables(project);
        var table = FindTable(project, tables, tableName);
        var read = _tableReader.Read(project, table, tables);
        EnsureReadableTable(read);
        var sourcePath = ResolveExternalFile(project, csvPath);
        var previewTable = read.Data.Copy();
        var import = CsvService.ImportIntoWithChanges(
            previewTable,
            sourcePath,
            allowPartialColumns,
            matchByIdWhenPresent,
            columnName => IsWritableDataColumn(read.Data.Columns[columnName]!));
        var changed = BuildChangedCellsPreview(read.Data, previewTable, import.ChangedCells);
        return new
        {
            project.GameRoot,
            table.TableName,
            CsvPath = sourcePath,
            Encoding = "strict UTF-8 input; annotation row is skipped when detected",
            ImportedRows = import.ImportedRows,
            ChangedCellCount = import.ChangedCells.Count,
            ChangedRows = import.ChangedCells.Select(cell => cell.RowKey ?? cell.RowIndex.ToString(CultureInfo.InvariantCulture)).Distinct().Count(),
            SkippedReadOnlyCells = import.SkippedReadOnlyCells,
            ChangedCells = changed.Take(200),
            Truncated = changed.Count > 200,
            RejectedColumns = BuildRejectedCsvColumns(previewTable),
            SafetyNote = "Preview only copied the in-memory table and did not write game files."
        };
    }

    public object ApplyImportTableCsv(
        string? gameRoot,
        string tableName,
        string csvPath,
        string? writeMode,
        bool allowPartialColumns,
        bool matchByIdWhenPresent)
    {
        var project = LoadProject(gameRoot);
        EnsureWriteMode(project, writeMode);
        var tables = LoadTables(project);
        var table = FindTable(project, tables, tableName);
        var read = _tableReader.Read(project, table, tables);
        EnsureWritableTable(read, writeMode);
        var sourcePath = ResolveExternalFile(project, csvPath);
        var import = CsvService.ImportIntoWithChanges(
            read.Data,
            sourcePath,
            allowPartialColumns,
            matchByIdWhenPresent,
            columnName => IsWritableDataColumn(read.Data.Columns[columnName]!));
        var save = _tableWriter.Save(project, table, read.Data);
        var reread = _tableReader.Read(project, table, tables);
        return new
        {
            project.GameRoot,
            table.TableName,
            CsvPath = sourcePath,
            ImportedRows = import.ImportedRows,
            ChangedCellCount = import.ChangedCells.Count,
            SkippedReadOnlyCells = import.SkippedReadOnlyCells,
            Result = BuildTableSavePayload(save),
            BackupPaths = new[] { save.BackupPath },
            ChangedBytes = save.ChangedBytes,
            RereadOk = reread.Validation.IsUsable,
            ReportPath = save.ReportJsonPath,
            Warnings = reread.Validation.Warnings
        };
    }

    public object ReadTableSchema(string? gameRoot, string tableName)
    {
        var project = LoadProject(gameRoot);
        var tables = LoadTables(project);
        var table = FindTable(project, tables, tableName);
        var read = _tableReader.Read(project, table, tables);
        var columns = read.Data.Columns.Cast<DataColumn>()
            .Select(column =>
            {
                var field = column.ExtendedProperties["FieldDefinition"] as HexFieldDefinition;
                return new
                {
                    column.ColumnName,
                    DataType = column.DataType.Name,
                    IsSyntheticId = column.ColumnName.Equals("ID", StringComparison.OrdinalIgnoreCase),
                    Writable = IsWritableDataColumn(column),
                    ReadOnlyReason = BuildReadOnlyReason(column),
                    Field = field == null ? null : new
                    {
                        Kind = field.Kind.ToString(),
                        field.Size,
                        field.ConsumesBytes,
                        field.VisibleByDefault,
                        Annotation = _fieldAnnotationService.BuildFieldAnnotation(table, field),
                        ShortAnnotation = _fieldAnnotationService.BuildShortFieldAnnotation(table, field),
                        RiskReason = _fieldAnnotationService.GetRiskReason(table, field)
                    }
                };
            })
            .ToArray();
        return new
        {
            project.GameRoot,
            table.TableName,
            table.FileName,
            table.Version,
            table.BeginId,
            table.RowCount,
            table.RowSize,
            DataPosHex = HexDisplayFormatter.FormatOffset(table.DataPos),
            table.IndexTable,
            Validation = BuildValidationPayload(read.Validation),
            Columns = columns,
            WriteRules = new
            {
                IdColumn = "ID is synthetic and cannot be written.",
                DerivedColumns = "Columns without FieldDefinition or with FieldDefinition.ConsumesBytes=false are display-only unless HexTableWriter has an explicit derived encoder.",
                CsvImport = "CSV import only applies columns that exist and consume bytes; unsupported/display columns are skipped."
            }
        };
    }

    public object ReadTableDerivedDisplay(string? gameRoot, string tableName, List<int>? rowIds, int limit)
    {
        var project = LoadProject(gameRoot);
        var tables = LoadTables(project);
        var table = FindTable(project, tables, tableName);
        var read = _tableReader.Read(project, table, tables);
        EnsureReadableTable(read);
        var effectiveLimit = NormalizeLimit(limit, 50, 500);
        var rowIdSet = rowIds is { Count: > 0 } ? rowIds.ToHashSet() : null;
        var rows = new List<object>();
        foreach (var row in read.Data.Rows.Cast<DataRow>()
                     .Where(row => rowIdSet == null || rowIdSet.Contains(Convert.ToInt32(row["ID"], CultureInfo.InvariantCulture)))
                     .Take(effectiveLimit))
        {
            var changes = _tableDerivedDisplayService.RefreshRow(project, tables, table, row);
            var derivedValues = read.Data.Columns.Cast<DataColumn>()
                .Where(column => !IsWritableDataColumn(column) && !column.ColumnName.Equals("ID", StringComparison.OrdinalIgnoreCase))
                .ToDictionary(column => column.ColumnName, column => row[column] is DBNull ? null : row[column], StringComparer.OrdinalIgnoreCase);
            rows.Add(new
            {
                RowId = Convert.ToInt32(row["ID"], CultureInfo.InvariantCulture),
                DerivedValues = derivedValues,
                RefreshChanges = changes
            });
        }

        return new
        {
            project.GameRoot,
            table.TableName,
            ReturnedRows = rows.Count,
            Rows = rows,
            Note = "TableDerivedDisplayService currently returns an empty refresh change set; derived columns from the table reader are still exposed for AI self-check."
        };
    }

    public object ReadRoleEditor(string? gameRoot, string? keyword, int limit)
    {
        var project = LoadProject(gameRoot);
        var tables = LoadTables(project);
        var build = BuildRoleEditorTables(project, tables);
        var rows = BuildRoleEditorRows(project, build)
            .Where(row => MatchesObjectKeyword(row, keyword))
            .Take(NormalizeLimit(limit, 100, 1000))
            .ToArray();
        return new
        {
            project.GameRoot,
            Tables = new
            {
                Person = build.Person.Table.TableName,
                R = build.R.Table.TableName,
                S = build.S.Table.TableName,
                Job = build.Job?.Table.TableName
            },
            TotalRows = build.Person.Data.Rows.Count,
            ReturnedRows = rows.Length,
            Columns = BuildRoleEditorColumnSchema(build),
            Choices = new
            {
                Jobs = BuildIdNameChoices(build.Job?.Data),
                Equipment = BuildEquipmentChoices(project, tables)
            },
            Rows = rows,
            SafetyNote = "Read-only integrated role editor view. Use preview_write_roles before write_roles."
        };
    }

    public object PreviewWriteRoles(string? gameRoot, List<RoleUpdate> updates)
    {
        var project = LoadProject(gameRoot);
        var tables = LoadTables(project);
        var build = BuildRoleEditorTables(project, tables);
        var changes = ApplyRoleUpdates(build, updates, mutate: false);
        return new
        {
            project.GameRoot,
            Preview = changes,
            BackupPaths = Array.Empty<string>(),
            ChangedBytes = 0,
            RereadOk = true,
            ReportPath = string.Empty,
            Warnings = Array.Empty<string>(),
            SafetyNote = "Preview only; no files were modified."
        };
    }

    public object WriteRoles(string? gameRoot, List<RoleUpdate> updates, string? writeMode)
    {
        var project = LoadProject(gameRoot);
        EnsureWriteMode(project, writeMode);
        var tables = LoadTables(project);
        var build = BuildRoleEditorTables(project, tables);
        var changes = ApplyRoleUpdates(build, updates, mutate: true);
        var saves = new List<TableSaveResult>();
        if (build.Person.Data.GetChanges() != null) saves.Add(_tableWriter.Save(project, build.Person.Table, build.Person.Data));
        if (build.R.Data.GetChanges() != null) saves.Add(_tableWriter.Save(project, build.R.Table, build.R.Data));
        if (build.S.Data.GetChanges() != null) saves.Add(_tableWriter.Save(project, build.S.Table, build.S.Data));
        var reread = BuildRoleEditorTables(project, tables);
        return new
        {
            project.GameRoot,
            Result = changes,
            Saves = saves.Select(BuildTableSavePayload),
            BackupPaths = saves.Select(save => save.BackupPath).ToArray(),
            ChangedBytes = saves.Sum(save => save.ChangedBytes),
            RereadOk = reread.Person.Validation.IsUsable && reread.R.Validation.IsUsable && reread.S.Validation.IsUsable,
            ReportPath = string.Join(";", saves.Select(save => save.ReportJsonPath).Where(path => !string.IsNullOrWhiteSpace(path))),
            Warnings = reread.Person.Validation.Warnings.Concat(reread.R.Validation.Warnings).Concat(reread.S.Validation.Warnings).ToArray()
        };
    }

    public object ReadRoleTexts(string? gameRoot, List<int>? roleIds, int limit)
    {
        var project = LoadProject(gameRoot);
        var tables = LoadTables(project);
        var build = BuildRoleTextTables(project, tables);
        var roleBuild = BuildRoleEditorTables(project, tables);
        var roleIdSet = roleIds is { Count: > 0 } ? roleIds.ToHashSet() : null;
        var rows = roleBuild.Person.Data.Rows.Cast<DataRow>()
            .Where(row => roleIdSet == null || roleIdSet.Contains(Convert.ToInt32(row["ID"], CultureInfo.InvariantCulture)))
            .Take(NormalizeLimit(limit, 100, 1000))
            .Select(row => BuildRoleTextPayload(project, row, build))
            .ToArray();
        return new
        {
            project.GameRoot,
            Tables = new
            {
                Biography = build.Biography.Table.TableName,
                Critical = build.Critical.Table.TableName,
                Retreat = build.Retreat.Table.TableName
            },
            ReturnedRows = rows.Length,
            SpecialCriticalRoleIds = _roleQuoteMappingService.ReadSpecialCriticalRoleIds(project),
            Rows = rows,
            SafetyNote = "Read-only role biography/critical/retreat text mapping."
        };
    }

    public object PreviewWriteRoleTexts(string? gameRoot, List<RoleTextUpdate> updates)
    {
        var project = LoadProject(gameRoot);
        var tables = LoadTables(project);
        var roleBuild = BuildRoleEditorTables(project, tables);
        var textBuild = BuildRoleTextTables(project, tables);
        var changes = ApplyRoleTextUpdates(project, roleBuild, textBuild, updates, mutate: false);
        return new
        {
            project.GameRoot,
            Preview = changes,
            BackupPaths = Array.Empty<string>(),
            ChangedBytes = 0,
            RereadOk = true,
            ReportPath = string.Empty,
            Warnings = Array.Empty<string>(),
            SafetyNote = "Preview only; no files were modified."
        };
    }

    public object WriteRoleTexts(string? gameRoot, List<RoleTextUpdate> updates, string? writeMode)
    {
        var project = LoadProject(gameRoot);
        EnsureWriteMode(project, writeMode);
        var tables = LoadTables(project);
        var roleBuild = BuildRoleEditorTables(project, tables);
        var textBuild = BuildRoleTextTables(project, tables);
        var changes = ApplyRoleTextUpdates(project, roleBuild, textBuild, updates, mutate: true);
        var saves = new List<TableSaveResult>();
        var specialSaves = new List<RoleCriticalSpecialSlotsSaveResult>();
        foreach (var specialIds in changes.SpecialCriticalRoleIdWrites)
        {
            var save = _roleQuoteMappingService.SaveSpecialCriticalRoleIds(project, specialIds);
            if (save != null) specialSaves.Add(save);
        }

        if (roleBuild.Person.Data.GetChanges() != null) saves.Add(_tableWriter.Save(project, roleBuild.Person.Table, roleBuild.Person.Data));
        if (textBuild.Biography.Data.GetChanges() != null) saves.Add(_tableWriter.Save(project, textBuild.Biography.Table, textBuild.Biography.Data));
        if (textBuild.Critical.Data.GetChanges() != null) saves.Add(_tableWriter.Save(project, textBuild.Critical.Table, textBuild.Critical.Data));
        if (textBuild.Retreat.Data.GetChanges() != null) saves.Add(_tableWriter.Save(project, textBuild.Retreat.Table, textBuild.Retreat.Data));

        return new
        {
            project.GameRoot,
            Result = changes,
            Saves = saves.Select(BuildTableSavePayload),
            SpecialSaves = specialSaves.Select(save => new { save.FilePath, save.BackupPath, save.ReportJsonPath, save.ChangedBytes }),
            BackupPaths = saves.Select(save => save.BackupPath).Concat(specialSaves.Select(save => save.BackupPath)).ToArray(),
            ChangedBytes = saves.Sum(save => save.ChangedBytes) + specialSaves.Sum(save => save.ChangedBytes),
            RereadOk = true,
            ReportPath = string.Join(";", saves.Select(save => save.ReportJsonPath).Concat(specialSaves.Select(save => save.ReportJsonPath)).Where(path => !string.IsNullOrWhiteSpace(path))),
            Warnings = Array.Empty<string>()
        };
    }

    public object FindFreeImageAssignmentIds(string? gameRoot, string kind, int startId, int limit)
    {
        var project = LoadProject(gameRoot);
        var tables = LoadTables(project);
        var assignments = _imageAssignmentService.Load(project, tables);
        var normalizedKind = NormalizeImageAssignmentKind(kind);
        var assigned = CollectAssignedImageIds(assignments, normalizedKind);
        var available = _imageAssignmentPreviewService.GetAvailableCharacterImageIds(project, normalizedKind, includeZero: false);
        var candidates = available
            .Where(id => id >= Math.Max(0, startId) && !assigned.Contains(id))
            .Distinct()
            .OrderBy(id => id)
            .Take(NormalizeLimit(limit, 50, 500))
            .Select(id => new
            {
                Id = id,
                Detail = BuildImageAssignmentCandidateDetail(normalizedKind, id),
                ResourceStatus = BuildImageAssignmentResourceStatus(project, normalizedKind, id)
            })
            .ToArray();
        return new
        {
            project.GameRoot,
            Kind = normalizedKind,
            AvailableCount = available.Count,
            AssignedCount = assigned.Count,
            ReturnedCount = candidates.Length,
            Candidates = candidates,
            Oracle = DetectImageAssignerOracle(gameRoot),
            Warnings = BuildImageAssignmentResourceWarnings(project, normalizedKind)
        };
    }

    public object PreviewImageAssignmentUpdate(string? gameRoot, List<ImageAssignmentUpdate> updates)
    {
        var project = LoadProject(gameRoot);
        var tables = LoadTables(project);
        var assignments = _imageAssignmentService.Load(project, tables);
        var changes = ApplyImageAssignmentUpdates(assignments, updates, mutate: false);
        return new
        {
            project.GameRoot,
            Preview = changes,
            BackupPaths = Array.Empty<string>(),
            ChangedBytes = 0,
            RereadOk = true,
            ReportPath = string.Empty,
            Warnings = Array.Empty<string>(),
            SafetyNote = "Preview only; no files were modified."
        };
    }

    public object WriteImageAssignmentUpdate(string? gameRoot, List<ImageAssignmentUpdate> updates, string? writeMode)
    {
        var project = LoadProject(gameRoot);
        EnsureWriteMode(project, writeMode);
        var tables = LoadTables(project);
        var assignments = _imageAssignmentService.Load(project, tables);
        var changes = ApplyImageAssignmentUpdates(assignments, updates, mutate: true);
        var save = _imageAssignmentService.Save(project, tables, assignments);
        var reread = _imageAssignmentService.Load(project, tables);
        return new
        {
            project.GameRoot,
            Result = changes,
            Saves = save.Saves.Select(BuildTableSavePayload),
            BackupPaths = save.Saves.Select(item => item.BackupPath).ToArray(),
            ChangedBytes = save.ChangedBytes,
            RereadOk = reread.Rows.Count == assignments.Rows.Count,
            ReportPath = string.Join(";", save.Saves.Select(item => item.ReportJsonPath).Where(path => !string.IsNullOrWhiteSpace(path))),
            Warnings = Array.Empty<string>()
        };
    }

    public object ReadEditableImageTarget(string? gameRoot, EditableImageTargetRequest request)
    {
        var project = LoadProject(gameRoot);
        var target = ResolveEditableImageTarget(project, request);
        using var document = _editableImageCodecService.Load(project, target);
        var outputPath = BuildExportPath(project, "EditableImages", BuildEditableImageExportName(target, "read.png"));
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
        document.Bitmap.Save(outputPath, ImageFormat.Png);
        return new
        {
            project.GameRoot,
            Target = BuildEditableImageTargetPayload(project, document.Target),
            document.Bitmap.Width,
            document.Bitmap.Height,
            document.FrameWidth,
            document.FrameHeight,
            document.RestrictToPalette,
            document.PaletteRole,
            PaletteColorCount = document.Palette.Count,
            document.PalettePath,
            document.LoadDetail,
            OutputPath = outputPath,
            SafetyNote = "Read/export only; no game files were modified."
        };
    }

    public object PreviewEditableImageWrite(
        string? gameRoot,
        EditableImageTargetRequest request,
        string? replacementPath,
        List<PixelEditUpdate>? pixelEdits)
    {
        var project = LoadProject(gameRoot);
        var target = ResolveEditableImageTarget(project, request);
        using var bitmap = BuildEditableImageWriteBitmap(project, target, replacementPath, pixelEdits);
        var preview = _editableImageCodecService.PreviewWrite(project, target, bitmap);
        return new
        {
            project.GameRoot,
            Preview = BuildEditableImageWritePreviewPayload(preview),
            BackupPaths = Array.Empty<string>(),
            ChangedBytes = preview.E5Preview?.ChangedBytesEstimate ?? 0,
            RereadOk = true,
            ReportPath = string.Empty,
            Warnings = preview.Warnings,
            SafetyNote = "Preview only; no game files were modified."
        };
    }

    public object WriteEditableImage(
        string? gameRoot,
        EditableImageTargetRequest request,
        string? replacementPath,
        List<PixelEditUpdate>? pixelEdits,
        string? writeMode)
    {
        var project = LoadProject(gameRoot);
        EnsureWriteMode(project, writeMode);
        var target = ResolveEditableImageTarget(project, request);
        using var bitmap = BuildEditableImageWriteBitmap(project, target, replacementPath, pixelEdits);
        var result = _editableImageCodecService.Write(project, target, bitmap);
        using var reread = _editableImageCodecService.Load(project, target);
        var changedBytes = result.E5Result?.ChangedBytesEstimate ?? result.DllResult?.ChangedBytesEstimate ?? 0;
        return new
        {
            project.GameRoot,
            Result = BuildEditableImageWriteResultPayload(result),
            BackupPaths = new[] { result.BackupPath }.Where(path => !string.IsNullOrWhiteSpace(path)).ToArray(),
            ChangedBytes = changedBytes,
            RereadOk = reread.Bitmap.Width > 0 && reread.Bitmap.Height > 0,
            ReportPath = result.ReportPath,
            Warnings = Array.Empty<string>()
        };
    }

    public object ListPortraitFrames(string? gameRoot, string? root, int limit)
    {
        var project = LoadProject(gameRoot);
        var roots = string.IsNullOrWhiteSpace(root)
            ? PortraitFrameAssetDirectoryService.GetKnownFrameDirectories(project)
            : new[] { ResolveExternalDirectory(project, root!) };
        var extensions = new HashSet<string>([".png", ".bmp", ".jpg", ".jpeg"], StringComparer.OrdinalIgnoreCase);
        var files = roots
            .Where(Directory.Exists)
            .SelectMany(directory => Directory.EnumerateFiles(directory, "*", SearchOption.TopDirectoryOnly))
            .Where(path => extensions.Contains(Path.GetExtension(path)))
            .OrderBy(path => Path.GetFileName(path), StringComparer.OrdinalIgnoreCase)
            .Take(NormalizeLimit(limit, 200, 2000))
            .Select(path => new
            {
                Path = path,
                FileName = Path.GetFileName(path),
                SizeBytes = new FileInfo(path).Length,
                Directory = Path.GetDirectoryName(path)
            })
            .ToArray();
        return new
        {
            project.GameRoot,
            Roots = roots,
            Count = files.Length,
            Frames = files
        };
    }

    public object PreviewApplyPortraitFrame(string? gameRoot, string framePath, List<PortraitFrameTargetUpdate> targets)
    {
        var project = LoadProject(gameRoot);
        var request = BuildPortraitFrameRequest(project, framePath, targets, "direct");
        var preview = _portraitFrameApplyService.Preview(project, request);
        return new
        {
            project.GameRoot,
            Preview = BuildPortraitFramePreviewPayload(preview),
            BackupPaths = Array.Empty<string>(),
            ChangedBytes = preview.E5Preview?.ChangedBytesEstimate ?? 0,
            RereadOk = true,
            ReportPath = string.Empty,
            Warnings = preview.Warnings,
            SafetyNote = "Preview only; no game files were modified."
        };
    }

    public object ApplyPortraitFrame(string? gameRoot, string framePath, List<PortraitFrameTargetUpdate> targets, string? writeMode)
    {
        var project = LoadProject(gameRoot);
        EnsureWriteMode(project, writeMode);
        var request = BuildPortraitFrameRequest(project, framePath, targets, writeMode ?? "direct");
        var result = _portraitFrameApplyService.Replace(project, request);
        return new
        {
            project.GameRoot,
            Result = BuildPortraitFramePreviewPayload(result),
            BackupPaths = new[] { result.E5Result?.BackupPath }.Where(path => !string.IsNullOrWhiteSpace(path)).ToArray(),
            ChangedBytes = result.E5Result?.ChangedBytesEstimate ?? 0,
            RereadOk = true,
            ReportPath = FirstNonEmpty(result.AggregateReportPath, result.E5Result?.ReportJsonPath),
            Warnings = result.Warnings
        };
    }

    public object PreviewExtractMapMaterials(string? gameRoot, MapMaterialExtractionMcpRequest request)
    {
        var project = LoadProject(gameRoot);
        var coreRequest = BuildMapMaterialExtractionRequest(project, request);
        var preview = _mapMaterialExtractionService.Preview(coreRequest);
        return new
        {
            project.GameRoot,
            Preview = preview,
            BackupPaths = Array.Empty<string>(),
            ChangedBytes = 0,
            RereadOk = true,
            ReportPath = string.Empty,
            Warnings = Array.Empty<string>(),
            SafetyNote = "Preview only; no files were created."
        };
    }

    public object ExtractMapMaterials(string? gameRoot, MapMaterialExtractionMcpRequest request, string? writeMode)
    {
        var project = LoadProject(gameRoot);
        EnsureWriteMode(project, writeMode);
        var coreRequest = BuildMapMaterialExtractionRequest(project, request);
        var result = _mapMaterialExtractionService.Extract(coreRequest);
        return new
        {
            project.GameRoot,
            Result = result,
            BackupPaths = Array.Empty<string>(),
            ChangedBytes = 0,
            RereadOk = result.Files.All(file => File.Exists(file.Path)),
            ReportPath = string.Empty,
            Warnings = Array.Empty<string>(),
            SafetyNote = "Extraction writes only material library/export image files; no game resources were modified."
        };
    }

    public object PreviewTerrainBeautifyFilter(string? gameRoot, string draftId, string? filter, int strength)
    {
        var project = LoadProject(gameRoot);
        var draft = _mapDraftService.LoadDraft(project, draftId);
        var materials = LoadMaterialsForDraft(project, draft);
        using var baseTerrain = _terrainDrivenMapGenerationService.RenderBaseTerrain(draft, materials);
        var previewDraft = CloneMapDraftForMcp(draft);
        previewDraft.BeautifyGeneratedMap = true;
        previewDraft.BeautifyStrength = NormalizeBeautifyStrength(strength, draft.BeautifyStrength);
        using var beautified = _terrainMapBeautifyService.Beautify(previewDraft, baseTerrain);
        var outputPath = BuildExportPath(project, "MapWorkbench", $"{draft.DraftId}_terrain_beautify_filter.png");
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
        beautified.Save(outputPath, ImageFormat.Png);
        return new
        {
            project.GameRoot,
            Draft = BuildMapDraftSummary(previewDraft),
            Filter = string.IsNullOrWhiteSpace(filter) ? "default" : filter,
            Strength = previewDraft.BeautifyStrength,
            OutputPath = outputPath,
            Width = beautified.Width,
            Height = beautified.Height,
            SafetyNote = "Preview export writes only CCZModStudio_Exports/MapWorkbench; draft and game files were not modified."
        };
    }

    public object ApplyTerrainBeautifyToDraft(string? gameRoot, string draftId, string? filter, int strength, string? writeMode)
    {
        var project = LoadProject(gameRoot);
        EnsureWriteMode(project, writeMode);
        var draft = _mapDraftService.LoadDraft(project, draftId);
        draft.BeautifyGeneratedMap = true;
        draft.BeautifyStrength = NormalizeBeautifyStrength(strength, draft.BeautifyStrength);
        _mapDraftService.SaveDraft(project, draft);
        var reread = _mapDraftService.LoadDraft(project, draftId);
        return new
        {
            project.GameRoot,
            Result = BuildMapDraftSummary(reread),
            Filter = string.IsNullOrWhiteSpace(filter) ? "default" : filter,
            BackupPaths = Array.Empty<string>(),
            ChangedBytes = 0,
            RereadOk = reread.BeautifyGeneratedMap && reread.BeautifyStrength == draft.BeautifyStrength,
            ReportPath = string.Empty,
            Warnings = Array.Empty<string>(),
            SafetyNote = "Saved only the map workbench draft JSON; game resources were not modified."
        };
    }

    public object DiagnoseQinger66Project(string? gameRoot)
    {
        var project = LoadProject(gameRoot);
        var engine = _engineProfileService.Detect(project);
        var tables = LoadTables(project);
        return _qinger66DiagnosticsService.Build(project, engine, tables, _tableReader);
    }

    public object AuditQinger66Items(string? gameRoot, int limit)
    {
        var project = LoadProject(gameRoot);
        var engine = _engineProfileService.Detect(project);
        var tables = LoadTables(project);
        var audit = new Qinger66ItemAuditService().Build(project, engine, tables);
        return new
        {
            project.GameRoot,
            audit.Summary,
            Rows = audit.Rows.Take(NormalizeLimit(limit, 200, 2000))
        };
    }

    public object ListLegacyMfcDialogs(string? gameRoot, int limit)
    {
        var project = LoadProject(gameRoot);
        var roots = new[]
            {
                Path.Combine(project.WorkspaceRoot, "旧版游戏制作工具"),
                Path.Combine(project.WorkspaceRoot, "老版游戏制作工具"),
                Path.Combine(project.WorkspaceRoot, "工具整合包", "CCZModStudio", "Assets", "LegacyResources")
            }
            .Where(Directory.Exists)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var files = roots.SelectMany(root => Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
            .Where(path => Path.GetExtension(path) is ".rc" or ".dlg" or ".txt" or ".ini" or ".cmf")
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .Take(NormalizeLimit(limit, 100, 1000))
            .Select(path => new
            {
                RelativePath = roots.Select(root => TryRelativePath(root, path)).FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? path,
                Path = path,
                FileName = Path.GetFileName(path),
                SizeBytes = new FileInfo(path).Length
            })
            .ToArray();
        return new
        {
            project.GameRoot,
            Roots = roots,
            Count = files.Length,
            Dialogs = files,
            SafetyNote = "Read-only legacy resource catalog; GUI clipboard/window state is intentionally not exposed as MCP state."
        };
    }

    public object ReadLegacyMfcDialog(string? gameRoot, string relativePath, int maxChars)
    {
        var project = LoadProject(gameRoot);
        if (string.IsNullOrWhiteSpace(relativePath)) throw new InvalidOperationException("relative_path is required.");
        var roots = new[]
            {
                Path.Combine(project.WorkspaceRoot, "旧版游戏制作工具"),
                Path.Combine(project.WorkspaceRoot, "老版游戏制作工具"),
                Path.Combine(project.WorkspaceRoot, "工具整合包", "CCZModStudio", "Assets", "LegacyResources")
            }
            .Where(Directory.Exists)
            .ToArray();
        var path = ResolveUnderAnyRoot(roots, relativePath);
        var bytes = File.ReadAllBytes(path);
        var text = TryDecodeText(bytes);
        var limit = NormalizeLimit(maxChars, 12000, 100000);
        return new
        {
            project.GameRoot,
            Path = path,
            RelativePath = roots.Select(root => TryRelativePath(root, path)).FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? path,
            SizeBytes = bytes.Length,
            Text = text.Length <= limit ? text : text[..limit],
            Truncated = text.Length > limit,
            SafetyNote = "Read-only legacy resource text."
        };
    }

    public object ReadScenarioReferenceChecklist(string? gameRoot, string? relativePath, int limit)
    {
        var project = LoadProject(gameRoot);
        var files = string.IsNullOrWhiteSpace(relativePath)
            ? _scenarioFileReader.ReadAllIndex(project).Take(NormalizeLimit(limit, 50, 500)).Select(file => file.Path).ToArray()
            : new[] { ResolveScenarioFile(project, relativePath!) };
        var rows = files.Select(path => new
            {
                RelativePath = NormalizeProjectRelativePath(project, path),
                FileName = Path.GetFileName(path),
                Kind = Path.GetFileName(path).StartsWith("S_", StringComparison.OrdinalIgnoreCase) ? "S" : "R",
                SuggestedReadTools = new[] { "read_scenario_commands", "inspect_eex_entries", "compare_eex_archives" },
                Checklist = new[]
                {
                    "Confirm target file with list_scenario_files.",
                    "Read command structure with read_scenario_commands before writing.",
                    "Use preview_scenario_patch or dedicated write tools for modifications.",
                    "Inspect EEX sections when command layout evidence is unclear."
                }
            })
            .ToArray();
        return new
        {
            project.GameRoot,
            Count = rows.Length,
            Rows = rows,
            SafetyNote = "Read-only navigation checklist; no scenario files were modified."
        };
    }

    private static void EnsureReadableTable(TableReadResult read)
    {
        if (!read.Validation.IsUsable)
        {
            throw new InvalidOperationException("The selected table is not usable: " + read.Validation.TableStatus);
        }
    }

    private static void EnsureWritableTable(TableReadResult read, string? writeMode)
    {
        EnsureReadableTable(read);
        if (read.Validation.IsReadOnlyEvidenceOnly)
        {
            throw new InvalidOperationException("The selected table is read-only/evidence-only and cannot be written: " + read.Validation.TableStatus);
        }

        if (read.Validation.IsCrossVersionFallback && !IsCrossVersionFallbackWriteMode(writeMode))
        {
            throw new InvalidOperationException("The selected 6.6 table resolved through CrossVersionFallback. Re-run with write_mode=CrossVersionFallbackWrite to explicitly accept the non-native layout risk.");
        }

        if (!read.Validation.CanWrite && !read.Validation.IsCrossVersionFallback)
        {
            throw new InvalidOperationException("The selected table cannot be written: " + read.Validation.TableStatus);
        }
    }

    private static bool IsWritableDataColumn(DataColumn column)
    {
        if (column.ColumnName.Equals("ID", StringComparison.OrdinalIgnoreCase)) return false;
        return column.ExtendedProperties["FieldDefinition"] is HexFieldDefinition { ConsumesBytes: true };
    }

    private static string BuildReadOnlyReason(DataColumn column)
    {
        if (column.ColumnName.Equals("ID", StringComparison.OrdinalIgnoreCase)) return "synthetic ID";
        if (column.ExtendedProperties["FieldDefinition"] is not HexFieldDefinition field) return "derived or aggregate column";
        return field.ConsumesBytes ? string.Empty : "derived/display column";
    }

    private string BuildColumnAnnotation(HexTableDefinition table, DataColumn column)
    {
        if (column.ColumnName.Equals("ID", StringComparison.OrdinalIgnoreCase)) return "Synthetic row id; not writable.";
        return column.ExtendedProperties["FieldDefinition"] is HexFieldDefinition field
            ? _fieldAnnotationService.BuildShortFieldAnnotation(table, field)
            : "Derived or aggregate display column; not writable.";
    }

    private static object BuildValidationPayload(HexTableValidationResult validation)
        => new
        {
            validation.IsUsable,
            validation.CanWrite,
            validation.FilePath,
            validation.FileExists,
            validation.FileLength,
            validation.TableStatus,
            validation.WriteRisk,
            validation.IsNative66,
            validation.IsCrossVersionFallback,
            validation.IsReadOnlyEvidenceOnly,
            validation.SemanticValidationStatus,
            validation.HiddenTailPolicy,
            validation.EffectResolutionSource,
            validation.Warnings
        };

    private static IReadOnlyList<object> BuildChangedCellsPreview(DataTable before, DataTable after, IReadOnlyList<CsvChangedCell> cells)
    {
        var result = new List<object>();
        foreach (var cell in cells)
        {
            var rowIndex = cell.RowIndex;
            if (rowIndex < 0 || rowIndex >= before.Rows.Count || rowIndex >= after.Rows.Count) continue;
            var column = before.Columns[cell.ColumnName]!;
            result.Add(new
            {
                cell.RowKey,
                cell.RowIndex,
                cell.ColumnName,
                OldValue = before.Rows[rowIndex][column] is DBNull ? null : before.Rows[rowIndex][column],
                NewValue = after.Rows[rowIndex][cell.ColumnName] is DBNull ? null : after.Rows[rowIndex][cell.ColumnName]
            });
        }

        return result;
    }

    private static IReadOnlyList<object> BuildRejectedCsvColumns(DataTable table)
        => table.Columns.Cast<DataColumn>()
            .Where(column => !IsWritableDataColumn(column))
            .Select(column => new { column.ColumnName, Reason = BuildReadOnlyReason(column) })
            .Cast<object>()
            .ToArray();

    private sealed record RoleEditorBuild(TableReadResult Person, TableReadResult R, TableReadResult S, TableReadResult? Job);

    private RoleEditorBuild BuildRoleEditorTables(CczProject project, IReadOnlyList<HexTableDefinition> tables)
    {
        var person = _tableReader.Read(project, FindTable(project, tables, "6.5-0 人物"), tables);
        var r = _tableReader.Read(project, FindTable(project, tables, "6.5-0-4 R形象"), tables);
        var s = _tableReader.Read(project, FindTable(project, tables, "6.5-0-5 S形象"), tables);
        EnsureReadableTable(person);
        EnsureReadableTable(r);
        EnsureReadableTable(s);
        TableReadResult? job = null;
        if (HexTableNameResolver.TryResolveForProject(project, tables, "6.5-4 详细兵种", out var jobTable))
        {
            var jobRead = _tableReader.Read(project, jobTable, tables);
            if (jobRead.Validation.IsUsable) job = jobRead;
        }

        return new RoleEditorBuild(person, r, s, job);
    }

    private IReadOnlyList<object> BuildRoleEditorRows(CczProject project, RoleEditorBuild build)
    {
        var rows = new List<object>();
        var jobNames = BuildAuthoringIdNameLookup(build.Job?.Data);
        var count = Math.Min(build.Person.Data.Rows.Count, Math.Min(build.R.Data.Rows.Count, build.S.Data.Rows.Count));
        for (var i = 0; i < count; i++)
        {
            var personRow = build.Person.Data.Rows[i];
            var rRow = build.R.Data.Rows[i];
            var sRow = build.S.Data.Rows[i];
            var id = Convert.ToInt32(personRow["ID"], CultureInfo.InvariantCulture);
            var faceId = ReadIntIfColumn(personRow, "头像");
            var jobId = ReadIntIfColumn(personRow, "职业");
            var rId = ReadIntIfColumn(rRow, "R形象编号");
            var sId = ReadIntIfColumn(sRow, "S形象编号");
            var columns = build.Person.Data.Columns.Cast<DataColumn>().ToArray();
            rows.Add(new
            {
                RowId = id,
                Name = ReadStringIfColumn(personRow, "名称"),
                Person = RowToDictionary(personRow, columns),
                Job = new { JobId = jobId, Name = jobNames.GetValueOrDefault(jobId, string.Empty) },
                Face = new
                {
                    FaceId = faceId,
                    ResourcePath = ImageAssignmentService.GetImageResourcePath(project, "Face", faceId),
                    ResourceHint = ImageAssignmentService.GetImageResourceFileName("Face", faceId)
                },
                R = new
                {
                    ImageId = rId,
                    ResourceStatus = ImageAssignmentService.GetImageResourceStatus(project, "R", rId),
                    ResourcePath = ImageAssignmentService.GetImageResourcePath(project, "R", rId),
                    ResourceHint = ImageAssignmentService.GetImageResourceFileName("R", rId)
                },
                S = new
                {
                    ImageId = sId,
                    ResourceStatus = ImageAssignmentService.GetImageResourceStatus(project, "S", sId),
                    ResourcePath = ImageAssignmentService.GetImageResourcePath(project, "S", sId),
                    ResourceHint = ImageAssignmentService.GetImageResourceFileName("S", sId)
                }
            });
        }

        return rows;
    }

    private object BuildRoleEditorColumnSchema(RoleEditorBuild build)
        => new
        {
            Person = build.Person.Data.Columns.Cast<DataColumn>().Select(column => new { column.ColumnName, Writable = IsWritableDataColumn(column), Reason = BuildReadOnlyReason(column) }),
            R = build.R.Data.Columns.Cast<DataColumn>().Select(column => new { column.ColumnName, Writable = IsWritableDataColumn(column), Reason = BuildReadOnlyReason(column) }),
            S = build.S.Data.Columns.Cast<DataColumn>().Select(column => new { column.ColumnName, Writable = IsWritableDataColumn(column), Reason = BuildReadOnlyReason(column) })
        };

    private object ApplyRoleUpdates(RoleEditorBuild build, List<RoleUpdate>? updates, bool mutate)
    {
        if (updates == null || updates.Count == 0) throw new InvalidOperationException("updates must contain at least one role update.");
        var changes = new List<object>();
        foreach (var update in updates)
        {
            var personRow = FindRowById(build.Person.Data, update.RowId);
            var rRow = FindRowById(build.R.Data, update.RowId);
            var sRow = FindRowById(build.S.Data, update.RowId);
            foreach (var (columnName, value) in update.Values)
            {
                var column = FindColumn(build.Person.Data, columnName);
                if (!IsWritableDataColumn(column)) throw new InvalidOperationException($"Column {columnName} is not writable in the person table.");
                var newValue = ConvertJsonValue(value);
                changes.Add(BuildCellChange(build.Person.Table.TableName, update.RowId, column.ColumnName, personRow[column], newValue));
                if (mutate) personRow[column] = newValue;
            }

            if (update.FaceId.HasValue)
            {
                var column = FindColumn(build.Person.Data, "头像");
                var newValue = update.FaceId.Value;
                changes.Add(BuildCellChange(build.Person.Table.TableName, update.RowId, column.ColumnName, personRow[column], newValue));
                if (mutate) personRow[column] = newValue;
            }

            if (update.RImageId.HasValue)
            {
                var column = FindColumn(build.R.Data, "R形象编号");
                var newValue = update.RImageId.Value;
                changes.Add(BuildCellChange(build.R.Table.TableName, update.RowId, column.ColumnName, rRow[column], newValue));
                if (mutate) rRow[column] = newValue;
            }

            if (update.SImageId.HasValue)
            {
                var column = FindColumn(build.S.Data, "S形象编号");
                var newValue = update.SImageId.Value;
                changes.Add(BuildCellChange(build.S.Table.TableName, update.RowId, column.ColumnName, sRow[column], newValue));
                if (mutate) sRow[column] = newValue;
            }
        }

        return new { ChangeCount = changes.Count, Changes = changes };
    }

    private sealed record RoleTextBuild(TableReadResult Biography, TableReadResult Critical, TableReadResult Retreat);

    private RoleTextBuild BuildRoleTextTables(CczProject project, IReadOnlyList<HexTableDefinition> tables)
    {
        var biography = _tableReader.Read(project, FindTable(project, tables, "6.5-0-1 人物列传"), tables);
        var critical = _tableReader.Read(project, FindTable(project, tables, "6.5-0-2 暴击台词"), tables);
        var retreat = _tableReader.Read(project, FindTable(project, tables, "6.5-0-3 撤退台词"), tables);
        EnsureReadableTable(biography);
        EnsureReadableTable(critical);
        EnsureReadableTable(retreat);
        return new RoleTextBuild(biography, critical, retreat);
    }

    private object BuildRoleTextPayload(CczProject project, DataRow roleRow, RoleTextBuild build)
    {
        var roleId = Convert.ToInt32(roleRow["ID"], CultureInfo.InvariantCulture);
        var biography = TryFindRowById(build.Biography.Data, roleId);
        var critical = _roleQuoteMappingService.ResolveCriticalQuote(project, roleRow, build.Critical.Data);
        var retreat = _roleQuoteMappingService.ResolveRetreatQuote(roleRow, build.Retreat.Data);
        return new
        {
            RoleId = roleId,
            Name = ReadStringIfColumn(roleRow, "名称"),
            Biography = biography == null ? null : RowToDictionary(biography, build.Biography.Data.Columns.Cast<DataColumn>().ToArray()),
            Critical = new
            {
                critical.FieldValue,
                critical.QuoteIds,
                critical.IsSpecialRoleQuote,
                critical.Explanation,
                Quotes = critical.QuoteRows.Select(row => RowToDictionary(row, build.Critical.Data.Columns.Cast<DataColumn>().ToArray()))
            },
            Retreat = new
            {
                retreat.FieldValue,
                retreat.QuoteId,
                retreat.Explanation,
                Quote = retreat.QuoteRow == null ? null : RowToDictionary(retreat.QuoteRow, build.Retreat.Data.Columns.Cast<DataColumn>().ToArray())
            }
        };
    }

    private sealed class RoleTextChangeSet
    {
        public List<object> Changes { get; } = [];
        public List<IReadOnlyList<int>> SpecialCriticalRoleIdWrites { get; } = [];
        public int ChangeCount => Changes.Count;
    }

    private RoleTextChangeSet ApplyRoleTextUpdates(
        CczProject project,
        RoleEditorBuild roleBuild,
        RoleTextBuild textBuild,
        List<RoleTextUpdate>? updates,
        bool mutate)
    {
        if (updates == null || updates.Count == 0) throw new InvalidOperationException("updates must contain at least one role text update.");
        var result = new RoleTextChangeSet();
        var specialRoleIds = _roleQuoteMappingService.ReadSpecialCriticalRoleIds(project).ToList();
        foreach (var update in updates)
        {
            var roleRow = FindRowById(roleBuild.Person.Data, update.RoleId);
            if (update.Biography != null)
            {
                EncodingService.EncodeFixedString(update.Biography, 200);
                var row = FindRowById(textBuild.Biography.Data, update.RoleId);
                var column = FindColumn(textBuild.Biography.Data, "介绍");
                result.Changes.Add(BuildCellChange(textBuild.Biography.Table.TableName, update.RoleId, column.ColumnName, row[column], update.Biography));
                if (mutate) row[column] = update.Biography;
            }

            RoleQuoteMappingService? service = _roleQuoteMappingService;
            var selection = ResolveRoleTextCriticalSelection(update, roleRow);
            if (selection != null)
            {
                ApplyCriticalQuoteAssignment(project, roleBuild, roleRow, selection, specialRoleIds, result, mutate);
            }

            if (update.CriticalQuotes is { Count: > 0 })
            {
                var effectiveSelection = selection ?? ResolveCurrentRoleCriticalSelection(project, roleRow, textBuild.Critical.Data);
                var mapping = service.ResolveCriticalQuoteSelection(roleRow, textBuild.Critical.Data, effectiveSelection);
                for (var i = 0; i < update.CriticalQuotes.Count && i < mapping.QuoteRows.Count; i++)
                {
                    var text = update.CriticalQuotes[i];
                    EncodingService.EncodeFixedString(text, 200);
                    var row = mapping.QuoteRows[i];
                    var quoteId = mapping.QuoteIds[i];
                    var column = FindColumn(textBuild.Critical.Data, "介绍");
                    result.Changes.Add(BuildCellChange(textBuild.Critical.Table.TableName, quoteId, column.ColumnName, row[column], text));
                    if (mutate) row[column] = text;
                }
            }

            if (update.RetreatQuote != null)
            {
                EncodingService.EncodeFixedString(update.RetreatQuote, 200);
                var mapping = service.ResolveRetreatQuote(roleRow, textBuild.Retreat.Data);
                if (mapping.QuoteRow == null)
                {
                    throw new InvalidOperationException($"Role {update.RoleId} has no editable retreat quote row.");
                }

                var column = FindColumn(textBuild.Retreat.Data, "介绍");
                result.Changes.Add(BuildCellChange(textBuild.Retreat.Table.TableName, mapping.QuoteId ?? update.RoleId, column.ColumnName, mapping.QuoteRow[column], update.RetreatQuote));
                if (mutate) mapping.QuoteRow[column] = update.RetreatQuote;
            }
        }

        if (mutate && result.SpecialCriticalRoleIdWrites.Count == 0 && updates.Any(update => ResolveRoleTextCriticalSelection(update, FindRowById(roleBuild.Person.Data, update.RoleId)) != null))
        {
            result.SpecialCriticalRoleIdWrites.Add(specialRoleIds);
        }

        return result;
    }

    private RoleCriticalQuoteSelection? ResolveRoleTextCriticalSelection(RoleTextUpdate update, DataRow roleRow)
    {
        if (string.IsNullOrWhiteSpace(update.CriticalQuoteMode) && !update.CriticalQuoteValue.HasValue)
        {
            return null;
        }

        var modeText = (update.CriticalQuoteMode ?? "generic").Trim().ToLowerInvariant();
        var value = update.CriticalQuoteValue ?? 0;
        return modeText is "special" or "special_role" or "slot"
            ? new RoleCriticalQuoteSelection(RoleCriticalQuoteMode.Special, value)
            : new RoleCriticalQuoteSelection(RoleCriticalQuoteMode.Generic, value);
    }

    private RoleCriticalQuoteSelection ResolveCurrentRoleCriticalSelection(CczProject project, DataRow roleRow, DataTable criticalTable)
    {
        var mapping = _roleQuoteMappingService.ResolveCriticalQuote(project, roleRow, criticalTable);
        if (mapping.IsSpecialRoleQuote)
        {
            return new RoleCriticalQuoteSelection(RoleCriticalQuoteMode.Special, mapping.QuoteIds.FirstOrDefault());
        }

        var genericType = ReadIntIfColumn(roleRow, "暴击台词");
        return new RoleCriticalQuoteSelection(RoleCriticalQuoteMode.Generic, Math.Clamp(genericType, 0, RoleQuoteMappingService.CriticalGenericTypeCount - 1));
    }

    private void ApplyCriticalQuoteAssignment(
        CczProject project,
        RoleEditorBuild roleBuild,
        DataRow roleRow,
        RoleCriticalQuoteSelection selection,
        List<int> specialRoleIds,
        RoleTextChangeSet changes,
        bool mutate)
    {
        var roleId = Convert.ToInt32(roleRow["ID"], CultureInfo.InvariantCulture);
        if (specialRoleIds.Count != RoleQuoteMappingService.CriticalSpecialQuoteCount)
        {
            throw new InvalidOperationException("Cannot read the complete 21-slot special critical role table.");
        }

        for (var i = 0; i < specialRoleIds.Count; i++)
        {
            if (specialRoleIds[i] == roleId)
            {
                changes.Changes.Add(new { Table = "Ekd5.exe special critical slots", RowId = i, Column = "RoleId", OldValue = specialRoleIds[i], NewValue = RoleQuoteMappingService.CriticalSpecialEmptyRoleId });
                if (mutate) specialRoleIds[i] = RoleQuoteMappingService.CriticalSpecialEmptyRoleId;
            }
        }

        if (selection.Mode == RoleCriticalQuoteMode.Special)
        {
            if (selection.Value < 0 || selection.Value >= RoleQuoteMappingService.CriticalSpecialQuoteCount)
            {
                throw new InvalidOperationException("special critical quote slot must be 0..20.");
            }

            changes.Changes.Add(new { Table = "Ekd5.exe special critical slots", RowId = selection.Value, Column = "RoleId", OldValue = specialRoleIds[selection.Value], NewValue = roleId });
            if (mutate) specialRoleIds[selection.Value] = roleId;
        }
        else
        {
            if (selection.Value < 0 || selection.Value >= RoleQuoteMappingService.CriticalGenericTypeCount)
            {
                throw new InvalidOperationException("generic critical quote type must be 0..25.");
            }

            var column = FindColumn(roleBuild.Person.Data, "暴击台词");
            changes.Changes.Add(BuildCellChange(roleBuild.Person.Table.TableName, roleId, column.ColumnName, roleRow[column], selection.Value));
            if (mutate) roleRow[column] = selection.Value;
        }

        if (mutate)
        {
            changes.SpecialCriticalRoleIdWrites.Clear();
            changes.SpecialCriticalRoleIdWrites.Add(specialRoleIds.ToArray());
        }
    }

    private object ApplyImageAssignmentUpdates(DataTable assignments, List<ImageAssignmentUpdate>? updates, bool mutate)
    {
        if (updates == null || updates.Count == 0) throw new InvalidOperationException("updates must contain at least one image assignment update.");
        var changes = new List<object>();
        foreach (var update in updates)
        {
            var row = FindRowById(assignments, update.RowId);
            ApplyAssignmentColumn(row, "头像编号", update.FaceId, changes, mutate);
            ApplyAssignmentColumn(row, "R形象编号", update.RImageId, changes, mutate);
            ApplyAssignmentColumn(row, "S形象编号", update.SImageId, changes, mutate);
        }

        return new { ChangeCount = changes.Count, Changes = changes };
    }

    private static void ApplyAssignmentColumn(DataRow row, string columnName, int? value, List<object> changes, bool mutate)
    {
        if (!value.HasValue) return;
        var column = FindColumn(row.Table, columnName);
        var rowId = Convert.ToInt32(row["ID"], CultureInfo.InvariantCulture);
        changes.Add(BuildCellChange(row.Table.TableName, rowId, column.ColumnName, row[column], value.Value));
        if (mutate) row[column] = value.Value;
    }

    private static object BuildCellChange(string tableName, int rowId, string columnName, object? oldValue, object? newValue)
        => new
        {
            Table = tableName,
            RowId = rowId,
            Column = columnName,
            OldValue = oldValue is DBNull ? null : oldValue,
            NewValue = newValue is DBNull ? null : newValue
        };

    private static int ReadIntIfColumn(DataRow row, string columnName)
    {
        if (!row.Table.Columns.Contains(columnName)) return 0;
        var value = row[columnName];
        return value == null || value is DBNull ? 0 : Convert.ToInt32(value, CultureInfo.InvariantCulture);
    }

    private static string ReadStringIfColumn(DataRow row, string columnName)
    {
        if (!row.Table.Columns.Contains(columnName)) return string.Empty;
        return Convert.ToString(row[columnName], CultureInfo.InvariantCulture) ?? string.Empty;
    }

    private static IReadOnlyDictionary<int, string> BuildAuthoringIdNameLookup(DataTable? data)
    {
        var result = new Dictionary<int, string>();
        if (data == null || !data.Columns.Contains("ID")) return result;
        var nameColumn = data.Columns.Contains("名称") ? data.Columns["名称"] : data.Columns.Cast<DataColumn>().FirstOrDefault(column => !column.ColumnName.Equals("ID", StringComparison.OrdinalIgnoreCase));
        if (nameColumn == null) return result;
        foreach (DataRow row in data.Rows)
        {
            result[Convert.ToInt32(row["ID"], CultureInfo.InvariantCulture)] = Convert.ToString(row[nameColumn], CultureInfo.InvariantCulture) ?? string.Empty;
        }

        return result;
    }

    private static IReadOnlyList<object> BuildIdNameChoices(DataTable? data)
        => BuildAuthoringIdNameLookup(data).Select(pair => new { Id = pair.Key, Name = pair.Value }).Cast<object>().ToArray();

    private IReadOnlyList<object> BuildEquipmentChoices(CczProject project, IReadOnlyList<HexTableDefinition> tables)
    {
        var items = new List<object> { new { Id = 255, Name = "empty/unassigned", Kind = "empty" } };
        var lookup = BuildItemNameLookupForMcp(project, tables);
        items.AddRange(lookup.Select(pair => new { Id = pair.Key, Name = pair.Value, Kind = "item" }));
        return items;
    }

    private IReadOnlyDictionary<int, string> BuildItemNameLookupForMcp(CczProject project, IReadOnlyList<HexTableDefinition> tables)
    {
        var result = new Dictionary<int, string>();
        foreach (var table in HexTableNameResolver.ResolveItemTables(project, tables))
        {
            var read = _tableReader.Read(project, table, tables);
            if (!read.Validation.IsUsable || !read.Data.Columns.Contains("名称")) continue;
            foreach (DataRow row in read.Data.Rows)
            {
                result[Convert.ToInt32(row["ID"], CultureInfo.InvariantCulture)] = Convert.ToString(row["名称"], CultureInfo.InvariantCulture) ?? string.Empty;
            }
        }

        return result;
    }

    private static bool MatchesObjectKeyword(object value, string? keyword)
    {
        if (string.IsNullOrWhiteSpace(keyword)) return true;
        return value.ToString()?.Contains(keyword, StringComparison.OrdinalIgnoreCase) == true;
    }

    private static string NormalizeImageAssignmentKind(string kind)
    {
        var text = (kind ?? string.Empty).Trim().ToLowerInvariant();
        return text switch
        {
            "face" or "portrait" or "head" => "Face",
            "s" or "s_unit" or "s_image" => "S",
            _ => "R"
        };
    }

    private static HashSet<int> CollectAssignedImageIds(DataTable assignments, string kind)
    {
        var columnName = kind switch
        {
            "Face" => "头像编号",
            "S" => "S形象编号",
            _ => "R形象编号"
        };
        var result = new HashSet<int>();
        if (!assignments.Columns.Contains(columnName)) return result;
        foreach (DataRow row in assignments.Rows)
        {
            if (int.TryParse(Convert.ToString(row[columnName], CultureInfo.InvariantCulture), NumberStyles.Integer, CultureInfo.InvariantCulture, out var id) && id > 0)
            {
                result.Add(id);
            }
        }

        return result;
    }

    private static string BuildImageAssignmentCandidateDetail(string kind, int id)
    {
        if (kind == "Face")
        {
            return ImageAssignmentService.GetImageResourceFileName("Face", id);
        }

        return kind == "S"
            ? CharacterImageResourceService.BuildSMappingShortText(id)
            : ImageAssignmentService.GetImageResourceFileName("R", id);
    }

    private static string BuildImageAssignmentResourceStatus(CczProject project, string kind, int id)
    {
        if (kind == "Face")
        {
            var path = ImageAssignmentService.GetImageResourcePath(project, "Face", id);
            return File.Exists(path) ? "present: " + path : "missing: " + path;
        }

        return ImageAssignmentService.GetImageResourceStatus(project, kind, id);
    }

    private static IReadOnlyList<string> BuildImageAssignmentResourceWarnings(CczProject project, string kind)
    {
        if (kind == "Face")
        {
            var path = ImageAssignmentService.GetImageResourcePath(project, "Face", 0);
            return File.Exists(path) ? Array.Empty<string>() : new[] { "Face resource missing: " + path };
        }

        if (kind == "S")
        {
            var warnings = new List<string>();
            foreach (var fileName in new[] { "Unit_atk.e5", "Unit_mov.e5", "Unit_spc.e5" })
            {
                var path = CharacterImageResourceService.ResolveGameFile(project, fileName);
                if (!File.Exists(path)) warnings.Add(fileName + " missing: " + path);
            }

            return warnings;
        }

        var rPath = CharacterImageResourceService.ResolveGameFile(project, "Pmapobj.e5");
        return File.Exists(rPath) ? Array.Empty<string>() : new[] { "Pmapobj.e5 missing: " + rPath };
    }

    private EditableImageTarget ResolveEditableImageTarget(CczProject project, EditableImageTargetRequest request)
    {
        if (request == null) throw new InvalidOperationException("request is required.");
        var semantic = (request.Semantic ?? string.Empty).Trim().ToLowerInvariant();
        if (!string.IsNullOrWhiteSpace(semantic))
        {
            return ResolveSemanticEditableImageTarget(project, request, semantic);
        }

        if (string.IsNullOrWhiteSpace(request.TargetRelativePath))
        {
            throw new InvalidOperationException("target_relative_path is required when semantic is not supplied.");
        }

        var targetPath = ResolveEditableImageProjectFile(project, request.TargetRelativePath!);
        return new EditableImageTarget
        {
            Kind = ParseEditableImageKind(request.Kind, targetPath),
            DisplayName = string.IsNullOrWhiteSpace(request.DisplayName) ? Path.GetFileName(targetPath) : request.DisplayName!,
            TargetPath = targetPath,
            ImageNumber = request.ImageNumber ?? request.LargeImageNumber ?? 1,
            IconIndex = request.IconIndex ?? request.ImageNumber ?? 0,
            ResourceFormat = request.ResourceFormat ?? string.Empty,
            FrameWidth = request.FrameWidth,
            FrameHeight = request.FrameHeight,
            IsItemIconPair = request.IsItemIconPair == true,
            SmallImageNumber = request.SmallImageNumber ?? Math.Max(1, (request.ImageNumber ?? 2) - 1),
            LargeImageNumber = request.LargeImageNumber ?? request.ImageNumber ?? 2,
            OperationKind = "MCP editable image"
        };
    }

    private EditableImageTarget ResolveSemanticEditableImageTarget(CczProject project, EditableImageTargetRequest request, string semantic)
    {
        var rowId = request.RowId ?? throw new InvalidOperationException("row_id is required for semantic editable image targets.");
        var tables = LoadTables(project);
        if (semantic is "item_icon")
        {
            var itemTable = HexTableNameResolver.ResolveItemTables(project, tables).FirstOrDefault(table => rowId >= table.BeginId && rowId < table.BeginId + table.RowCount)
                            ?? throw new InvalidOperationException("Item row was not found for row_id " + rowId);
            var read = _tableReader.Read(project, itemTable, tables);
            var row = FindRowById(read.Data, rowId);
            var icon = ReadIntIfColumn(row, "图标");
            var mapping = new ItemIconMappingService().Resolve(project, icon, "item");
            return new EditableImageTarget
            {
                Kind = EditableImageTargetKind.E5Standard,
                DisplayName = "item_icon_" + rowId.ToString(CultureInfo.InvariantCulture),
                TargetPath = Ccz66RevisedLayout.ResolveResourcePath(project, mapping.ResourceRelativePath),
                ImageNumber = mapping.LargeImageNumber,
                IsItemIconPair = mapping.SmallImageNumber.HasValue,
                SmallImageNumber = mapping.SmallImageNumber ?? Math.Max(1, mapping.LargeImageNumber - 1),
                LargeImageNumber = mapping.LargeImageNumber,
                OperationKind = "MCP item icon edit"
            };
        }

        if (semantic is "strategy_icon")
        {
            var table = FindTable(project, tables, "6.5-5 策略");
            var read = _tableReader.Read(project, table, tables);
            var row = FindRowById(read.Data, rowId);
            var icon = ReadIntIfColumn(row, "策略图标");
            var mapping = new ItemIconMappingService().Resolve(project, icon, "strategy");
            return new EditableImageTarget
            {
                Kind = EditableImageTargetKind.E5Standard,
                DisplayName = "strategy_icon_" + rowId.ToString(CultureInfo.InvariantCulture),
                TargetPath = Ccz66RevisedLayout.ResolveResourcePath(project, mapping.ResourceRelativePath),
                ImageNumber = mapping.LargeImageNumber,
                OperationKind = "MCP strategy icon edit"
            };
        }

        if (semantic is "face_assignment" or "face" or "portrait")
        {
            var assignments = _imageAssignmentService.Load(project, tables);
            var row = FindRowById(assignments, rowId);
            var faceId = ReadIntIfColumn(row, "头像编号");
            var mapping = new CharacterImageResourceService().MapFaceId(faceId);
            var imageNumber = mapping.FaceImageNumbers.FirstOrDefault();
            var targetPath = CharacterImageResourceService.ResolveFaceFile(project)
                             ?? Path.Combine(project.GameRoot, "E5", "Face.e5");
            if (imageNumber <= 0) throw new InvalidOperationException("Face assignment does not resolve to a Face.e5 image number.");
            return new EditableImageTarget
            {
                Kind = EditableImageTargetKind.E5Standard,
                DisplayName = "face_assignment_" + rowId.ToString(CultureInfo.InvariantCulture),
                TargetPath = targetPath,
                ImageNumber = imageNumber,
                OperationKind = "MCP face assignment image edit"
            };
        }

        if (semantic is "r_actor" or "r_image" or "r_assignment")
        {
            var assignments = _imageAssignmentService.Load(project, tables);
            var row = FindRowById(assignments, rowId);
            var rImageId = ReadIntIfColumn(row, "R形象编号");
            var imageNumber = rImageId <= 0 ? 1 : checked(rImageId * 2 + 1);
            return new EditableImageTarget
            {
                Kind = EditableImageTargetKind.E5RawStrip,
                DisplayName = "r_actor_assignment_" + rowId.ToString(CultureInfo.InvariantCulture),
                TargetPath = CharacterImageResourceService.ResolveGameFile(project, "Pmapobj.e5"),
                ImageNumber = imageNumber,
                FrameWidth = 48,
                FrameHeight = 64,
                OperationKind = "MCP R actor assignment image edit"
            };
        }

        if (semantic is "s_unit" or "s_image" or "s_assignment")
        {
            var assignments = _imageAssignmentService.Load(project, tables);
            var row = FindRowById(assignments, rowId);
            var sImageId = ReadIntIfColumn(row, "S形象编号");
            var jobId = ReadIntIfColumn(row, "职业");
            var mapping = CharacterImageResourceService.ResolveSUnitImageMapping(sImageId, jobId, request.IconIndex ?? CharacterImageResourceService.DefaultSPreviewFactionSlot);
            var imageNumber = mapping.ImageNumbers.FirstOrDefault();
            if (imageNumber <= 0) throw new InvalidOperationException("S assignment does not resolve to a Unit image number.");
            return new EditableImageTarget
            {
                Kind = EditableImageTargetKind.E5RawStrip,
                DisplayName = "s_unit_assignment_" + rowId.ToString(CultureInfo.InvariantCulture),
                TargetPath = CharacterImageResourceService.ResolveGameFile(project, "Unit_mov.e5"),
                ImageNumber = imageNumber,
                FrameWidth = 48,
                FrameHeight = 48,
                OperationKind = "MCP S unit assignment image edit"
            };
        }

        throw new InvalidOperationException("Unsupported semantic editable image target: " + semantic);
    }

    private static string ResolveEditableImageProjectFile(CczProject project, string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath)) throw new InvalidOperationException("target_relative_path is required.");
        if (Path.IsPathRooted(relativePath))
        {
            var rooted = Path.GetFullPath(relativePath);
            if (File.Exists(rooted)) return rooted;
        }

        var direct = Path.GetFullPath(Path.Combine(project.GameRoot, relativePath));
        if (File.Exists(direct)) return direct;
        var e5 = Path.GetFullPath(Path.Combine(project.GameRoot, "E5", relativePath));
        if (File.Exists(e5)) return e5;

        return ResolveProjectFile(project, relativePath, mustExist: true);
    }

    private static EditableImageTargetKind ParseEditableImageKind(string kind, string targetPath)
    {
        var text = (kind ?? string.Empty).Trim().ToLowerInvariant();
        if (text is "dll" or "dll_bitmap_icon" || Path.GetExtension(targetPath).Equals(".dll", StringComparison.OrdinalIgnoreCase))
        {
            return EditableImageTargetKind.DllBitmapIcon;
        }

        if (text is "raw" or "e5_raw_strip") return EditableImageTargetKind.E5RawStrip;
        return EditableImageTargetKind.E5Standard;
    }

    private Bitmap BuildEditableImageWriteBitmap(CczProject project, EditableImageTarget target, string? replacementPath, List<PixelEditUpdate>? pixelEdits)
    {
        if (!string.IsNullOrWhiteSpace(replacementPath))
        {
            var sourcePath = ResolveExternalFile(project, replacementPath!);
            using var image = Image.FromFile(sourcePath);
            return new Bitmap(image);
        }

        if (pixelEdits is not { Count: > 0 })
        {
            throw new InvalidOperationException("replacement_path or pixel_edits is required.");
        }

        using var document = _editableImageCodecService.Load(project, target);
        var bitmap = new Bitmap(document.Bitmap);
        foreach (var edit in pixelEdits)
        {
            if (edit.X < 0 || edit.Y < 0 || edit.X >= bitmap.Width || edit.Y >= bitmap.Height)
            {
                throw new InvalidOperationException($"pixel edit is outside the bitmap: {edit.X},{edit.Y}");
            }

            bitmap.SetPixel(edit.X, edit.Y, ParseArgb(edit.Argb));
        }

        return bitmap;
    }

    private static Color ParseArgb(string value)
    {
        var text = (value ?? string.Empty).Trim().TrimStart('#');
        if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) text = text[2..];
        if (text.Length == 6) text = "FF" + text;
        if (!int.TryParse(text, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var argb))
        {
            throw new InvalidOperationException("argb must be RRGGBB, AARRGGBB, #RRGGBB, or #AARRGGBB.");
        }

        return Color.FromArgb(argb);
    }

    private static string BuildEditableImageExportName(EditableImageTarget target, string suffix)
        => $"{Path.GetFileNameWithoutExtension(target.TargetPath)}_{target.ImageNumber}_{target.IconIndex}_{suffix}";

    private static object BuildEditableImageTargetPayload(CczProject project, EditableImageTarget target)
        => new
        {
            Kind = target.Kind.ToString(),
            target.DisplayName,
            target.TargetPath,
            TargetRelativePath = TryNormalizeStatic(project, target.TargetPath),
            target.ImageNumber,
            target.IconIndex,
            target.ResourceFormat,
            target.FrameWidth,
            target.FrameHeight,
            target.IsItemIconPair,
            target.SmallImageNumber,
            target.LargeImageNumber
        };

    private static object BuildEditableImageWritePreviewPayload(EditableImageWritePreview preview)
        => new
        {
            preview.TargetRelativePath,
            preview.Summary,
            preview.Warnings,
            E5 = preview.E5Preview == null ? null : new { preview.E5Preview.OperationCount, preview.E5Preview.ChangedBytesEstimate, preview.E5Preview.FileSizeDeltaBytes, preview.E5Preview.RiskSummary },
            Dll = preview.DllPreview == null ? null : new { OperationCount = preview.DllPreview.Items.Count, preview.DllPreview.RiskSummary }
        };

    private static object BuildEditableImageWriteResultPayload(EditableImageWriteResult result)
        => new
        {
            result.TargetRelativePath,
            result.Summary,
            result.BackupPath,
            result.ReportPath,
            E5 = result.E5Result == null ? null : new { result.E5Result.OperationCount, result.E5Result.ChangedBytesEstimate, result.E5Result.ReportJsonPath },
            Dll = result.DllResult == null ? null : new { OperationCount = result.DllResult.Items.Count, result.DllResult.ChangedBytesEstimate, result.DllResult.ReportJsonPath }
        };

    private PortraitFrameApplyRequest BuildPortraitFrameRequest(CczProject project, string framePath, List<PortraitFrameTargetUpdate>? targets, string writeMode)
    {
        if (targets == null || targets.Count == 0) throw new InvalidOperationException("targets must contain at least one portrait frame target.");
        return new PortraitFrameApplyRequest
        {
            FramePath = ResolveExternalFile(project, framePath),
            WriteMode = string.IsNullOrWhiteSpace(writeMode) ? "direct" : writeMode,
            TargetRows = targets.Select(target => new PortraitFrameTargetRow(target.RowId, target.DisplayName, target.FaceId)).ToArray()
        };
    }

    private static object BuildPortraitFramePreviewPayload(PortraitFrameApplyPreviewResult preview)
        => new
        {
            preview.TargetPath,
            preview.TargetRelativePath,
            preview.CanWrite,
            preview.TotalOperationCount,
            Items = preview.Items.Select(item => new { item.RowId, item.DisplayName, item.FaceId, item.FramePath, item.TargetImageNumbers, item.OutputWidth, item.OutputHeight }),
            SkippedItems = preview.SkippedItems,
            preview.Warnings,
            E5 = preview.E5Preview == null ? null : new { preview.E5Preview.OperationCount, preview.E5Preview.ChangedBytesEstimate, preview.E5Preview.FileSizeDeltaBytes, preview.E5Preview.RiskSummary }
        };

    private MapMaterialExtractionRequest BuildMapMaterialExtractionRequest(CczProject project, MapMaterialExtractionMcpRequest request)
    {
        if (request == null) throw new InvalidOperationException("request is required.");
        var draft = _mapDraftService.LoadDraft(project, request.DraftId);
        var materialRoot = string.IsNullOrWhiteSpace(request.MaterialRoot)
            ? (string.IsNullOrWhiteSpace(draft.MaterialRoot) ? MaterialLibraryIndexer.ResolveMaterialLibraryRoot(project) : draft.MaterialRoot)
            : ResolveExportOrExternalDirectory(project, request.MaterialRoot!);
        if (string.IsNullOrWhiteSpace(materialRoot))
        {
            throw new InvalidOperationException("material_root is required when the draft has no material root and no default material library can be resolved.");
        }

        return new MapMaterialExtractionRequest
        {
            Draft = draft,
            MaterialRoot = materialRoot,
            CellRange = new Rectangle(request.X, request.Y, request.Width, request.Height),
            TargetType = ParseMapMaterialTargetType(request.TargetType),
            TerrainId = request.TerrainId.HasValue ? checked((byte)request.TerrainId.Value) : null,
            Source = ParseMapMaterialSource(request.Source),
            Materials = LoadMaterialsForDraft(project, draft)
        };
    }

    private static MapMaterialExtractionTargetType ParseMapMaterialTargetType(string value)
        => (value ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            "building" or "buildings" => MapMaterialExtractionTargetType.Building,
            "scenery" or "scene" => MapMaterialExtractionTargetType.Scenery,
            _ => MapMaterialExtractionTargetType.Terrain
        };

    private static MapMaterialExtractionSource ParseMapMaterialSource(string value)
        => (value ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            "original" or "original_base_map" => MapMaterialExtractionSource.OriginalBaseMap,
            "generated" or "generated_terrain_base" => MapMaterialExtractionSource.GeneratedTerrainBase,
            _ => MapMaterialExtractionSource.CurrentComposite
        };

    private static int NormalizeBeautifyStrength(int value, int fallback)
        => value <= 0 ? Math.Max(1, fallback) : Math.Clamp(value, 1, 10);

    private static MapWorkbenchDraft CloneMapDraftForMcp(MapWorkbenchDraft draft)
        => new()
        {
            DraftId = draft.DraftId,
            BoundMapId = draft.BoundMapId,
            GridWidth = draft.GridWidth,
            GridHeight = draft.GridHeight,
            TileSize = draft.TileSize,
            BaseLayerPath = draft.BaseLayerPath,
            MaterialRoot = draft.MaterialRoot,
            AutoGenerateMapFromTerrain = draft.AutoGenerateMapFromTerrain,
            BeautifyGeneratedMap = draft.BeautifyGeneratedMap,
            BeautifyStrength = draft.BeautifyStrength,
            FeatherRadius = draft.FeatherRadius,
            CreatedAtText = draft.CreatedAtText,
            UpdatedAtText = draft.UpdatedAtText,
            OriginalTerrainCells = draft.OriginalTerrainCells.ToArray(),
            TerrainCells = draft.TerrainCells.ToArray(),
            TerrainBaseCells = draft.TerrainBaseCells.ToList(),
            GeneratedMapCells = draft.GeneratedMapCells.ToList(),
            BuildingOverlayCells = draft.BuildingOverlayCells.ToList(),
            SceneryOverlayCells = draft.SceneryOverlayCells.ToList(),
            MapCellOverrides = draft.MapCellOverrides.ToList(),
            TerrainMaterialPlan = draft.TerrainMaterialPlan.ToList()
        };

    private static string TryRelativePath(string root, string path)
    {
        try
        {
            var fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            var fullPath = Path.GetFullPath(path);
            return fullPath.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase)
                ? Path.GetRelativePath(root, fullPath)
                : string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    private static string ResolveUnderAnyRoot(IReadOnlyList<string> roots, string relativePath)
    {
        foreach (var root in roots)
        {
            var candidate = Path.GetFullPath(Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar)));
            var rootWithSlash = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (candidate.StartsWith(rootWithSlash, StringComparison.OrdinalIgnoreCase) && File.Exists(candidate))
            {
                return candidate;
            }
        }

        if (Path.IsPathRooted(relativePath) && File.Exists(relativePath))
        {
            return Path.GetFullPath(relativePath);
        }

        throw new FileNotFoundException("Legacy dialog/resource file was not found.", relativePath);
    }

    private static string TryDecodeText(byte[] bytes)
    {
        try
        {
            return new UTF8Encoding(false, true).GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            return Encoding.GetEncoding(936).GetString(bytes);
        }
    }
}
