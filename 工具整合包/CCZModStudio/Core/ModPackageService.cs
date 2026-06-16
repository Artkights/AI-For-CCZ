using System.Data;
using System.Globalization;
using System.Text;
using System.Text.Json;
using CCZModStudio.Formats;
using CCZModStudio.Models;

namespace CCZModStudio.Core;

public sealed partial class ModPackageService
{
    private static readonly HashSet<int> SafeScenarioCommandIds = new()
    {
        0x02, 0x03, 0x04, 0x05,
        0x11, 0x12, 0x13,
        0x14, 0x15, 0x16, 0x17, 0x19, 0x1A,
        0x30, 0x38,
        0x42, 0x44, 0x45, 0x46, 0x47, 0x48,
        0x52, 0x76
    };

    private static readonly HashSet<string> SafeScenarioOperations = new(StringComparer.OrdinalIgnoreCase)
    {
        "replace_text_parameter",
        "update_numeric_parameter",
        "replace_variable_array",
        "append_command",
        "insert_command"
    };

    private static readonly HashSet<string> WritableScenarioOperations = new(StringComparer.OrdinalIgnoreCase)
    {
        "replace_text_parameter",
        "update_numeric_parameter",
        "replace_variable_array"
    };

    private readonly HexTableReader _tableReader = new();
    private readonly HexTableWriter _tableWriter = new();
    private readonly LegacyScenarioReader _scenarioReader = new();
    private readonly LegacyScenarioWriter _scenarioWriter = new();
    private readonly ImageResourceCatalogService _imageCatalog = new();
    private readonly EffectPackageService _effectPackages = new();
    private readonly CczEngineProfileService _engineProfiles = new();

    public ModAvailableSlotsResult ListAvailableSlots(CczProject project, IReadOnlyList<HexTableDefinition> tables, int limit = 32)
    {
        var effectiveLimit = Math.Clamp(limit <= 0 ? 32 : limit, 1, 200);
        var engine = _engineProfiles.Detect(project);
        var result = new ModAvailableSlotsResult
        {
            ProjectRoot = project.GameRoot,
            TargetVersion = engine.TableVersionPrefix,
            SafetyNote = "Slot scan is conservative: it highlights likely empty rows/resources, but a ModPackage preview still owns conflict validation before writes."
        };

        AddTableSlotGroup(project, tables, result, "person", engine.TableHints.PersonTable, effectiveLimit);
        AddTableSlotGroup(project, tables, result, "job_series", engine.TableHints.JobTable, effectiveLimit);
        AddTableSlotGroup(project, tables, result, "job_detail", engine.TableHints.DetailedJobTable, effectiveLimit);
        AddTableSlotGroups(project, tables, result, "item", HexTableNameResolver.ResolveItemTables(project, tables), effectiveLimit);
        AddEffectSlotGroup(project, tables, result, "item_effect", "item", effectiveLimit);
        AddEffectSlotGroup(project, tables, result, "job_effect", "job", effectiveLimit);
        AddEffectSlotGroup(project, tables, result, "personal_effect", "personal", effectiveLimit);
        AddScenarioSlotGroups(project, result, effectiveLimit);
        AddImageSlotGroups(project, result, effectiveLimit);

        return result;
    }

    public ModPackagePreviewResult Preview(
        CczProject project,
        IReadOnlyList<HexTableDefinition> tables,
        ModPackage package,
        SceneStringDocument? scenarioDictionary = null,
        bool allowStructuralScenarioWrites = false)
    {
        ArgumentNullException.ThrowIfNull(package);

        var result = new ModPackagePreviewResult
        {
            ProjectRoot = project.GameRoot,
            PackageId = NormalizePackageId(package),
            Name = package.Metadata.Name
        };

        ValidatePackageHeader(project, package, result);
        ValidateSlotPlan(package, result);
        PreviewTableUpdates(project, tables, package, result);
        var forceOpenScenarioWrites = IsForceOpenPackage(package);
        PreviewScenarioPatches(project, package, scenarioDictionary, result, allowStructuralScenarioWrites || forceOpenScenarioWrites, forceOpenScenarioWrites);
        PreviewEffectPackages(project, tables, package, result);
        PreviewResourceUpdates(project, package, result);
        AddValidationPlan(package, result);

        var errorCount = result.Issues.Count(issue => IsBlocking(issue.Severity));
        result.CanApply = errorCount == 0;
        result.Summary = $"ModPackage preview: tables={package.TableUpdates.Count}, scenarios={package.ScenarioPatches.Count}, effects={package.EffectPackages.Count}, resources={package.ResourceUpdates.Count}, issues={result.Issues.Count}, canApply={result.CanApply}.";
        return result;
    }

    public IReadOnlyList<TableSaveResult> ApplyTableUpdates(
        CczProject project,
        IReadOnlyList<HexTableDefinition> tables,
        IReadOnlyList<ModTableUpdate> updates)
    {
        var saves = new List<TableSaveResult>();
        foreach (var group in updates.GroupBy(update => update.TableName, StringComparer.OrdinalIgnoreCase))
        {
            var table = HexTableNameResolver.ResolveForProject(project, tables, group.Key);
            var read = _tableReader.Read(project, table, tables);
            if (!read.Validation.IsUsable)
            {
                throw new InvalidOperationException($"Table {table.TableName} is not usable for writing.");
            }

            foreach (var update in group)
            {
                var row = FindRow(read.Data, update.RowId, table.TableName);
                foreach (var (columnName, value) in update.Values)
                {
                    if (columnName.Equals("ID", StringComparison.OrdinalIgnoreCase))
                    {
                        throw new InvalidOperationException("ID is synthetic and cannot be written.");
                    }

                    var column = FindColumn(read.Data, columnName);
                    var field = column.ExtendedProperties["FieldDefinition"] as HexFieldDefinition;
                    if (field == null || !field.ConsumesBytes)
                    {
                        throw new InvalidOperationException($"Column {columnName} in table {table.TableName} is derived or non-writable.");
                    }

                    row[column] = ConvertJsonValue(value);
                }
            }

            saves.Add(_tableWriter.Save(project, table, read.Data));
        }

        return saves;
    }

    public ScenarioPatchPreviewResult PreviewScenarioPatch(
        CczProject project,
        ModScenarioPatch patch,
        SceneStringDocument dictionary,
        bool allowStructuralScenarioWrites = false,
        bool forceOpenScenarioWrites = false)
    {
        var document = ReadScenarioDocument(project, patch, dictionary, out var relativePath);
        var preview = new ScenarioPatchPreviewResult
        {
            PatchId = string.IsNullOrWhiteSpace(patch.PatchId) ? Path.GetFileNameWithoutExtension(relativePath) : patch.PatchId,
            RelativePath = relativePath,
            SceneCount = document.SceneCount,
            SectionCount = document.SectionCount,
            CommandCount = document.CommandCount
        };

        if (patch.Operations.Count == 0)
        {
            preview.Issues.Add(Issue("error", "scenario", relativePath, "scenario patch must contain at least one operation."));
        }

        foreach (var operation in patch.Operations)
        {
            PreviewScenarioOperation(document, operation, preview, allowStructuralScenarioWrites, forceOpenScenarioWrites);
        }

        preview.CanApply = preview.Issues.All(issue => !IsBlocking(issue.Severity));
        preview.Summary = $"Scenario patch {preview.PatchId}: operations={patch.Operations.Count}, structural={preview.StructuralOperationCount}, changes={preview.Changes.Count}, issues={preview.Issues.Count}, canApply={preview.CanApply}.";
        return preview;
    }

    public ScenarioPatchApplyResult ApplyScenarioPatch(
        CczProject project,
        ModScenarioPatch patch,
        SceneStringDocument dictionary)
    {
        var preview = PreviewScenarioPatch(project, patch, dictionary);
        if (!preview.CanApply)
        {
            return new ScenarioPatchApplyResult
            {
                PatchId = preview.PatchId,
                RelativePath = preview.RelativePath,
                Applied = false,
                Issues = preview.Issues,
                Changes = preview.Changes
            };
        }

        if (patch.Operations.Any(operation => !WritableScenarioOperations.Contains(operation.Operation)))
        {
            preview.Issues.Add(Issue(
                "error",
                "scenario",
                preview.RelativePath,
                "V1 scenario apply only supports replace_text_parameter, update_numeric_parameter, and replace_variable_array. Structural insert/append is preview-only until command construction has round-trip fixtures."));
            return new ScenarioPatchApplyResult
            {
                PatchId = preview.PatchId,
                RelativePath = preview.RelativePath,
                Applied = false,
                Issues = preview.Issues,
                Changes = preview.Changes
            };
        }

        var document = ReadScenarioDocument(project, patch, dictionary, out var relativePath);
        foreach (var operation in patch.Operations)
        {
            ApplyScenarioOperation(document, operation);
        }

        var save = _scenarioWriter.Save(
            project,
            relativePath,
            document,
            dictionary,
            "ModPackage scenario patch: " + (string.IsNullOrWhiteSpace(patch.PatchId) ? relativePath : patch.PatchId));

        return new ScenarioPatchApplyResult
        {
            PatchId = preview.PatchId,
            RelativePath = relativePath,
            Applied = true,
            FilePath = save.FilePath,
            BackupPath = save.BackupPath,
            ReportJsonPath = save.ReportJsonPath,
            ChangedBytes = save.ChangedBytes,
            SceneCount = save.SceneCount,
            SectionCount = save.SectionCount,
            CommandCount = save.CommandCount,
            ValidationSummary = save.ValidationSummary,
            Issues = preview.Issues,
            Changes = preview.Changes
        };
    }

    public ModPackageReportResult ExportReport(
        CczProject project,
        ModPackage package,
        string reportKind,
        object reportPayload)
    {
        var packageId = NormalizePackageId(package);
        reportKind = string.IsNullOrWhiteSpace(reportKind) ? "preview" : MakeSafeFileStem(reportKind);
        var reportRoot = Path.Combine(project.WorkspaceRoot, "CCZModStudio_Reports", "ModPackages");
        Directory.CreateDirectory(reportRoot);
        var stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss_fff", CultureInfo.InvariantCulture);
        var stem = $"{stamp}_{MakeSafeFileStem(packageId)}_{reportKind}";
        var jsonPath = Path.Combine(reportRoot, stem + ".json");
        var markdownPath = Path.Combine(reportRoot, stem + ".md");

        var jsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
        };
        File.WriteAllText(jsonPath, JsonSerializer.Serialize(reportPayload, jsonOptions), Encoding.UTF8);
        File.WriteAllText(markdownPath, BuildMarkdownReport(package, reportKind, reportPayload), Encoding.UTF8);

        return new ModPackageReportResult
        {
            ProjectRoot = project.GameRoot,
            PackageId = packageId,
            ReportKind = reportKind,
            JsonPath = jsonPath,
            MarkdownPath = markdownPath
        };
    }

    public static string NormalizePackageId(ModPackage package)
    {
        var value = package.Metadata.PackageId;
        if (string.IsNullOrWhiteSpace(value)) value = package.Metadata.Name;
        if (string.IsNullOrWhiteSpace(value)) value = "mod-package";
        return MakeSafeFileStem(value);
    }

    public static bool IsSafeScenarioCommandId(int commandId) => SafeScenarioCommandIds.Contains(commandId);

    public static IReadOnlyList<int> GetSafeScenarioCommandIds()
        => SafeScenarioCommandIds.OrderBy(id => id).ToList();

    public static bool IsForceOpenPackage(ModPackage package)
        => package.Metadata.Tags.TryGetValue("constraint_mode", out var mode) &&
           mode.Equals("force_open", StringComparison.OrdinalIgnoreCase);

    private void AddTableSlotGroup(
        CczProject project,
        IReadOnlyList<HexTableDefinition> tables,
        ModAvailableSlotsResult result,
        string kind,
        string tableName,
        int limit)
    {
        try
        {
            var table = HexTableNameResolver.ResolveForProject(project, tables, tableName);
            AddTableSlotGroups(project, tables, result, kind, new[] { table }, limit);
        }
        catch (Exception ex)
        {
            result.Warnings.Add($"Slot group {kind} unavailable: {ex.Message}");
        }
    }

    private void AddTableSlotGroups(
        CczProject project,
        IReadOnlyList<HexTableDefinition> tables,
        ModAvailableSlotsResult result,
        string kind,
        IReadOnlyList<HexTableDefinition> targetTables,
        int limit)
    {
        foreach (var table in targetTables)
        {
            try
            {
                var read = _tableReader.Read(project, table, tables);
                if (!read.Validation.IsUsable)
                {
                    result.Warnings.Add($"Slot group {kind}/{table.TableName} skipped: table is not readable.");
                    continue;
                }

                var available = new List<int>();
                var occupied = new List<int>();
                foreach (DataRow row in read.Data.Rows)
                {
                    var id = Convert.ToInt32(row["ID"], CultureInfo.InvariantCulture);
                    if (LooksEmptySlot(row))
                    {
                        available.Add(id);
                    }
                    else
                    {
                        occupied.Add(id);
                    }
                }

                result.Groups.Add(new ModAvailableSlotGroup
                {
                    Kind = kind,
                    Source = table.TableName,
                    TotalCount = read.Data.Rows.Count,
                    AvailableIds = available.Take(limit).ToList(),
                    OccupiedIds = occupied.Take(limit).ToList(),
                    Note = "Available rows are inferred from empty/name-placeholder fields; verify with preview before reserving."
                });
            }
            catch (Exception ex)
            {
                result.Warnings.Add($"Slot group {kind}/{table.TableName} unavailable: {ex.Message}");
            }
        }
    }

    private void AddEffectSlotGroup(CczProject project, IReadOnlyList<HexTableDefinition> tables, ModAvailableSlotsResult result, string kind, string domain, int limit)
    {
        try
        {
            var effects = _effectPackages.ListEffects(project, tables, domain, null, 1000);
            var occupied = effects
                .Where(effect => IsVisibleValue(effect.Name) || IsVisibleValue(effect.Description))
                .Select(effect => effect.EffectId)
                .Distinct()
                .OrderBy(id => id)
                .ToList();
            var occupiedSet = occupied.ToHashSet();
            var available = Enumerable.Range(1, 254)
                .Where(id => !occupiedSet.Contains(id))
                .Take(limit)
                .ToList();
            result.Groups.Add(new ModAvailableSlotGroup
            {
                Kind = kind,
                Source = "EffectPackage/" + domain,
                TotalCount = 254,
                AvailableIds = available,
                OccupiedIds = occupied.Take(limit).ToList(),
                Note = "Effect slots are inferred from effect catalogs/tables; preview still checks concrete bindings."
            });
        }
        catch (Exception ex)
        {
            result.Warnings.Add($"Slot group {kind} unavailable: {ex.Message}");
        }
    }

    private static void AddScenarioSlotGroups(CczProject project, ModAvailableSlotsResult result, int limit)
    {
        var rsRoot = Path.Combine(project.GameRoot, "RS");
        foreach (var prefix in new[] { "R", "S" })
        {
            var occupiedKeys = Directory.Exists(rsRoot)
                ? Directory.GetFiles(rsRoot, prefix + "_*.eex", SearchOption.TopDirectoryOnly)
                    .Select(path => Path.GetFileNameWithoutExtension(path))
                    .OrderBy(key => key, StringComparer.OrdinalIgnoreCase)
                    .ToList()
                : [];
            var occupiedSet = occupiedKeys.ToHashSet(StringComparer.OrdinalIgnoreCase);
            var available = Enumerable.Range(0, 100)
                .Select(id => $"{prefix}_{id:00}")
                .Where(key => !occupiedSet.Contains(key))
                .Take(limit)
                .ToList();

            result.Groups.Add(new ModAvailableSlotGroup
            {
                Kind = prefix == "R" ? "r_scenario" : "s_scenario",
                Source = "RS",
                TotalCount = 100,
                AvailableKeys = available,
                OccupiedKeys = occupiedKeys.Take(limit).ToList(),
                Note = "Missing R/S files are available for new standalone scenes only if the campaign index/routes are also updated by the package."
            });
        }
    }

    private void AddImageSlotGroups(CczProject project, ModAvailableSlotsResult result, int limit)
    {
        try
        {
            foreach (var resource in _imageCatalog.BuildCatalog(project).Where(resource => resource.CanReplace && resource.SupportsPreview))
            {
                var entries = _imageCatalog.ReadEntries(resource).Take(limit).Select(entry => entry.ImageNumber).ToList();
                result.Groups.Add(new ModAvailableSlotGroup
                {
                    Kind = "image_resource",
                    Source = string.IsNullOrWhiteSpace(resource.RelativePath) ? resource.FileName : resource.RelativePath,
                    TotalCount = resource.EntryCount,
                    AvailableIds = entries,
                    Note = "Image resources are replaceable slots, not empty slots. Reserve by explicit resource path and image_number/icon_index."
                });
            }
        }
        catch (Exception ex)
        {
            result.Warnings.Add("Image slot scan unavailable: " + ex.Message);
        }
    }

    private static bool LooksEmptySlot(DataRow row)
    {
        var visible = row.Table.Columns
            .Cast<DataColumn>()
            .Where(column => !column.ColumnName.Equals("ID", StringComparison.OrdinalIgnoreCase))
            .Select(column => Convert.ToString(row[column], CultureInfo.InvariantCulture) ?? string.Empty)
            .Where(IsVisibleValue)
            .Take(6)
            .ToList();

        if (visible.Count == 0) return true;
        var primary = visible[0].Trim();
        if (primary.StartsWith("#", StringComparison.Ordinal)) return true;
        if (primary.Equals("0", StringComparison.Ordinal) && visible.All(value => value.Trim() is "" or "0" or "255" or "1024")) return true;
        return false;
    }

    private static bool IsVisibleValue(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return false;
        value = value.Trim();
        return value.Length > 0 &&
               !value.Equals("0", StringComparison.Ordinal) &&
               !value.Equals("255", StringComparison.Ordinal) &&
               !value.Equals("1024", StringComparison.Ordinal);
    }

    private static void ValidatePackageHeader(CczProject project, ModPackage package, ModPackagePreviewResult result)
    {
        if (string.IsNullOrWhiteSpace(package.Metadata.Name))
        {
            result.Issues.Add(Issue("warning", "metadata", "metadata.name", "metadata.name is recommended for author-facing reports."));
        }

        var engine = new CczEngineProfileService().Detect(project);
        var targetVersion = string.IsNullOrWhiteSpace(package.Metadata.TargetVersion) ? "6.5" : package.Metadata.TargetVersion.Trim();
        if (!targetVersion.StartsWith(engine.TableVersionPrefix, StringComparison.OrdinalIgnoreCase) &&
            !engine.TableVersionPrefix.StartsWith(targetVersion, StringComparison.OrdinalIgnoreCase))
        {
            result.Issues.Add(Issue("error", "metadata", "metadata.targetVersion", $"Package targets {targetVersion}, but current project table profile is {engine.TableVersionPrefix}."));
        }
    }

    private static void ValidateSlotPlan(ModPackage package, ModPackagePreviewResult result)
    {
        AddDuplicateSlotIssues(result, "slotPlan.personIds", package.SlotPlan.PersonIds);
        AddDuplicateSlotIssues(result, "slotPlan.jobIds", package.SlotPlan.JobIds);
        AddDuplicateSlotIssues(result, "slotPlan.itemIds", package.SlotPlan.ItemIds);
        AddDuplicateSlotIssues(result, "slotPlan.strategyIds", package.SlotPlan.StrategyIds);
        AddDuplicateSlotIssues(result, "slotPlan.effectIds", package.SlotPlan.EffectIds);
        AddDuplicateSlotIssues(result, "slotPlan.faceImageNumbers", package.SlotPlan.FaceImageNumbers);
        AddDuplicateSlotIssues(result, "slotPlan.rImageIds", package.SlotPlan.RImageIds);
        AddDuplicateSlotIssues(result, "slotPlan.sImageIds", package.SlotPlan.SImageIds);

        var duplicateMaps = package.SlotPlan.MapIds
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .GroupBy(value => value, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToList();
        foreach (var value in duplicateMaps)
        {
            result.Issues.Add(Issue("error", "slotPlan", "slotPlan.mapIds", $"Duplicate map slot: {value}."));
        }
    }

    private static void AddDuplicateSlotIssues(ModPackagePreviewResult result, string target, IReadOnlyList<int> values)
    {
        foreach (var value in values.GroupBy(id => id).Where(group => group.Count() > 1).Select(group => group.Key))
        {
            result.Issues.Add(Issue("error", "slotPlan", target, $"Duplicate slot id: {value}."));
        }
    }

    private void PreviewTableUpdates(CczProject project, IReadOnlyList<HexTableDefinition> tables, ModPackage package, ModPackagePreviewResult result)
    {
        foreach (var update in package.TableUpdates)
        {
            if (string.IsNullOrWhiteSpace(update.TableName))
            {
                result.Issues.Add(Issue("error", "table", "tableUpdates", "tableName is required."));
                continue;
            }

            try
            {
                var table = HexTableNameResolver.ResolveForProject(project, tables, update.TableName);
                var read = _tableReader.Read(project, table, tables);
                if (!read.Validation.IsUsable)
                {
                    result.Issues.Add(Issue("error", "table", table.TableName, "Table is not usable for preview/write."));
                    continue;
                }

                var row = FindRow(read.Data, update.RowId, table.TableName);
                foreach (var (columnName, value) in update.Values)
                {
                    PreviewTableField(update, table, row, columnName, value, result);
                }
            }
            catch (Exception ex)
            {
                result.Issues.Add(Issue("error", "table", update.TableName, ex.Message));
            }
        }
    }

    private static void PreviewTableField(ModTableUpdate update, HexTableDefinition table, DataRow row, string columnName, JsonElement value, ModPackagePreviewResult result)
    {
        if (columnName.Equals("ID", StringComparison.OrdinalIgnoreCase))
        {
            result.Issues.Add(Issue("error", "table", table.TableName, "ID is synthetic and cannot be written."));
            return;
        }

        var column = FindColumn(row.Table, columnName);
        var field = column.ExtendedProperties["FieldDefinition"] as HexFieldDefinition;
        if (field == null || !field.ConsumesBytes)
        {
            result.Issues.Add(Issue("error", "table", table.TableName + "." + columnName, "Column is derived or non-writable."));
            return;
        }

        var converted = ConvertJsonValue(value);
        if (field.Kind == HexFieldKind.FixedString)
        {
            var text = Convert.ToString(converted, CultureInfo.InvariantCulture) ?? string.Empty;
            var gbkBytes = EncodingService.GetGbkByteCount(text);
            if (gbkBytes > field.Size)
            {
                result.Issues.Add(Issue("error", "table", $"{table.TableName}[{update.RowId}].{column.ColumnName}", $"GBK byte length {gbkBytes} exceeds fixed field capacity {field.Size}."));
            }
        }

        var oldValue = Convert.ToString(row[column], CultureInfo.InvariantCulture) ?? string.Empty;
        var newValue = Convert.ToString(converted, CultureInfo.InvariantCulture) ?? string.Empty;
        result.Changes.Add(new ModPackageChangePreview
        {
            Category = "table",
            Target = table.TableName,
            RowId = update.RowId,
            Field = column.ColumnName,
            OldValue = oldValue,
            NewValue = newValue,
            Changed = !string.Equals(oldValue, newValue, StringComparison.Ordinal),
            Note = update.Note
        });
    }

    private void PreviewScenarioPatches(
        CczProject project,
        ModPackage package,
        SceneStringDocument? scenarioDictionary,
        ModPackagePreviewResult result,
        bool allowStructuralScenarioWrites,
        bool forceOpenScenarioWrites)
    {
        if (package.ScenarioPatches.Count == 0) return;
        if (scenarioDictionary == null)
        {
            result.Issues.Add(Issue("error", "scenario", "scenarioPatches", "Scenario dictionary is required to preview structure patches."));
            return;
        }

        foreach (var patch in package.ScenarioPatches)
        {
            try
            {
                var preview = PreviewScenarioPatch(project, patch, scenarioDictionary, allowStructuralScenarioWrites, forceOpenScenarioWrites);
                result.ScenarioPatchPreviews.Add(preview);
                result.Issues.AddRange(preview.Issues);
                result.Changes.AddRange(preview.Changes);
            }
            catch (Exception ex)
            {
                result.Issues.Add(Issue("error", "scenario", patch.RelativePath, ex.Message));
            }
        }
    }

    private void PreviewEffectPackages(CczProject project, IReadOnlyList<HexTableDefinition> tables, ModPackage package, ModPackagePreviewResult result)
    {
        foreach (var effectPackage in package.EffectPackages)
        {
            try
            {
                var preview = _effectPackages.Preview(project, tables, effectPackage, "import");
                result.EffectPackagePreviews.Add(preview);
                foreach (var warning in preview.Warnings)
                {
                    result.Issues.Add(Issue("error", "effect", $"{effectPackage.Domain}:{effectPackage.EffectId}", warning));
                }

                foreach (var change in preview.Changes)
                {
                    result.Changes.Add(new ModPackageChangePreview
                    {
                        Category = "effect/" + change.Category,
                        Target = change.Target,
                        RowId = change.RowId,
                        Field = change.Field,
                        OldValue = change.OldValue,
                        NewValue = change.NewValue,
                        Changed = change.Changed,
                        Note = change.Note
                    });
                }
            }
            catch (Exception ex)
            {
                result.Issues.Add(Issue("error", "effect", $"{effectPackage.Domain}:{effectPackage.EffectId}", ex.Message));
            }
        }
    }

    private static void PreviewResourceUpdates(CczProject project, ModPackage package, ModPackagePreviewResult result)
    {
        foreach (var update in package.ResourceUpdates)
        {
            if (update.Operation.Equals("generate_task", StringComparison.OrdinalIgnoreCase))
            {
                result.Changes.Add(new ModPackageChangePreview
                {
                    Category = "resource_task",
                    Target = FirstNonEmpty(update.TargetRelativePath, update.Kind),
                    Field = update.Kind,
                    OldValue = "missing",
                    NewValue = update.Note,
                    Changed = true,
                    Note = "AI/placeholder resource generation task; no game resource is written during preview."
                });
                continue;
            }

            if (string.IsNullOrWhiteSpace(update.TargetRelativePath))
            {
                result.Issues.Add(Issue("error", "resource", "resourceUpdates", "targetRelativePath is required."));
                continue;
            }

            try
            {
                var targetPath = ResolveProjectPath(project, update.TargetRelativePath, mustExist: true);
                if (!string.IsNullOrWhiteSpace(update.ReplacementPath))
                {
                    _ = ResolveExternalPath(project, update.ReplacementPath, File.Exists);
                }
                else if (!update.Operation.Equals("clear", StringComparison.OrdinalIgnoreCase))
                {
                    result.Issues.Add(Issue("error", "resource", update.TargetRelativePath, "replacementPath is required unless operation=clear."));
                }

                var extension = Path.GetExtension(targetPath);
                if (extension.Equals(".e5", StringComparison.OrdinalIgnoreCase) && !update.ImageNumber.HasValue)
                {
                    result.Issues.Add(Issue("error", "resource", update.TargetRelativePath, "E5 resource updates require imageNumber."));
                }

                if (extension.Equals(".dll", StringComparison.OrdinalIgnoreCase) && !update.IconIndex.HasValue)
                {
                    result.Issues.Add(Issue("error", "resource", update.TargetRelativePath, "DLL icon resource updates require iconIndex."));
                }

                if (Path.GetFileName(targetPath).Equals("Ekd5.exe", StringComparison.OrdinalIgnoreCase))
                {
                    result.Issues.Add(Issue("error", "resource", update.TargetRelativePath, "Direct EXE replacement is forbidden in ModPackage V1."));
                }

                result.Changes.Add(new ModPackageChangePreview
                {
                    Category = "resource",
                    Target = update.TargetRelativePath,
                    Field = update.ImageNumber.HasValue ? "imageNumber" : update.IconIndex.HasValue ? "iconIndex" : update.Operation,
                    OldValue = "existing",
                    NewValue = string.IsNullOrWhiteSpace(update.ReplacementPath) ? update.Operation : update.ReplacementPath,
                    Changed = true,
                    Note = update.Note
                });
            }
            catch (Exception ex)
            {
                result.Issues.Add(Issue("error", "resource", update.TargetRelativePath, ex.Message));
            }
        }
    }

    private static void AddValidationPlan(ModPackage package, ModPackagePreviewResult result)
    {
        var smokes = new[]
        {
            "--effect-package-smoke",
            "--battlefield-unit-status-write-smoke",
            "--e5-image-replace-smoke",
            "--legacy-mfc-dialog-smoke",
            "--rs-write-smoke",
            "--map-preview-smoke"
        };

        foreach (var smoke in smokes)
        {
            if (!result.RequiredSmokeCommands.Contains(smoke, StringComparer.OrdinalIgnoreCase))
            {
                result.RequiredSmokeCommands.Add(smoke);
            }
        }

        foreach (var smoke in package.ValidationPlan.SmokeCommands)
        {
            if (!string.IsNullOrWhiteSpace(smoke) && !result.RequiredSmokeCommands.Contains(smoke, StringComparer.OrdinalIgnoreCase))
            {
                result.RequiredSmokeCommands.Add(smoke);
            }
        }

        result.ManualChecks.AddRange(package.ValidationPlan.ManualChecks.Where(check => !string.IsNullOrWhiteSpace(check)));
        if (package.ValidationPlan.RequireRuntimeSmoke)
        {
            result.ManualChecks.Add("Runtime: launch game, enter target R/S, confirm battle_loaded and inspect unit state.");
        }
    }

    private void PreviewScenarioOperation(
        LegacyScenarioDocument document,
        ModScenarioPatchOperation operation,
        ScenarioPatchPreviewResult preview,
        bool allowStructuralScenarioWrites,
        bool forceOpenScenarioWrites)
    {
        if (!SafeScenarioOperations.Contains(operation.Operation))
        {
            preview.Issues.Add(Issue("error", "scenario", preview.RelativePath, $"Unsupported scenario operation: {operation.Operation}."));
            return;
        }

        if (!WritableScenarioOperations.Contains(operation.Operation))
        {
            preview.StructuralOperationCount++;
            ValidateDraftCommands(operation, preview, forceOpenScenarioWrites);
            if (!allowStructuralScenarioWrites)
            {
                preview.Issues.Add(Issue(
                    "blocked",
                    "scenario",
                    preview.RelativePath,
                    $"Operation {operation.Operation} is preview-only in V1. Use existing battlefield/text tools or wait for command-construction fixtures before apply."));
            }
            else
            {
                PreviewStructuralScenarioOperation(document, operation, preview);
            }
            return;
        }

        var command = ResolveScenarioCommand(document, operation, preview);
        if (command == null) return;
        if (!forceOpenScenarioWrites && !SafeScenarioCommandIds.Contains(command.CommandId))
        {
            preview.Issues.Add(Issue("error", "scenario", command.CommandIdHex, $"Command {command.CommandIdHex} is not in the V1 safe command whitelist."));
            return;
        }

        var parameter = ResolveScenarioParameter(command, operation, preview);
        if (parameter == null) return;
        if (operation.ExpectedLayoutCode.HasValue && parameter.LayoutCode != operation.ExpectedLayoutCode.Value)
        {
            preview.Issues.Add(Issue("error", "scenario", command.DisplayText, $"Parameter layout {CCZModStudio.Core.HexDisplayFormatter.FormatByte((byte)parameter.LayoutCode)} did not match expected {CCZModStudio.Core.HexDisplayFormatter.FormatByte((byte)operation.ExpectedLayoutCode.Value)}."));
            return;
        }

        if (operation.Operation.Equals("replace_text_parameter", StringComparison.OrdinalIgnoreCase))
        {
            if (parameter.Kind != LegacyScenarioParameterKind.Text)
            {
                preview.Issues.Add(Issue("error", "scenario", command.DisplayText, "replace_text_parameter targets only text parameters."));
                return;
            }

            if (operation.ExpectedText != null && !string.Equals(parameter.Text, operation.ExpectedText, StringComparison.Ordinal))
            {
                preview.Issues.Add(Issue("error", "scenario", command.DisplayText, "expectedText does not match current text."));
            }

            var text = operation.Text ?? string.Empty;
            var bytes = EncodingService.GetGbkByteCount(text);
            if (bytes > ushort.MaxValue)
            {
                preview.Issues.Add(Issue("error", "scenario", command.DisplayText, $"Text GBK length {bytes} exceeds V1 safety limit."));
            }

            preview.Changes.Add(new ModPackageChangePreview
            {
                Category = "scenario",
                Target = preview.RelativePath,
                RowId = command.CommandIndex,
                Field = $"command {command.CommandIdHex} param {parameter.Index}",
                OldValue = parameter.Text,
                NewValue = text,
                Changed = !string.Equals(parameter.Text, text, StringComparison.Ordinal),
                Note = operation.Note
            });
        }
        else if (operation.Operation.Equals("update_numeric_parameter", StringComparison.OrdinalIgnoreCase))
        {
            if (parameter.Kind is LegacyScenarioParameterKind.Text or LegacyScenarioParameterKind.VariableArray)
            {
                preview.Issues.Add(Issue("error", "scenario", command.DisplayText, "update_numeric_parameter targets only numeric parameters."));
                return;
            }

            if (!operation.Value.HasValue)
            {
                preview.Issues.Add(Issue("error", "scenario", command.DisplayText, "value is required for update_numeric_parameter."));
                return;
            }

            preview.Changes.Add(new ModPackageChangePreview
            {
                Category = "scenario",
                Target = preview.RelativePath,
                RowId = command.CommandIndex,
                Field = $"command {command.CommandIdHex} param {parameter.Index}",
                OldValue = parameter.IntValue.ToString(CultureInfo.InvariantCulture),
                NewValue = operation.Value.Value.ToString(CultureInfo.InvariantCulture),
                Changed = parameter.IntValue != operation.Value.Value,
                Note = operation.Note
            });
        }
        else if (operation.Operation.Equals("replace_variable_array", StringComparison.OrdinalIgnoreCase))
        {
            if (parameter.Kind != LegacyScenarioParameterKind.VariableArray)
            {
                preview.Issues.Add(Issue("error", "scenario", command.DisplayText, "replace_variable_array targets only variable-array parameters."));
                return;
            }

            var values = operation.Values ?? [];
            if (values.Any(value => value < ushort.MinValue || value > ushort.MaxValue))
            {
                preview.Issues.Add(Issue("error", "scenario", command.DisplayText, "variable-array values must fit UInt16."));
            }

            preview.Changes.Add(new ModPackageChangePreview
            {
                Category = "scenario",
                Target = preview.RelativePath,
                RowId = command.CommandIndex,
                Field = $"command {command.CommandIdHex} param {parameter.Index}",
                OldValue = string.Join("/", parameter.Values),
                NewValue = string.Join("/", values),
                Changed = !parameter.Values.SequenceEqual(values),
                Note = operation.Note
            });
        }
    }

    private static void ValidateDraftCommands(ModScenarioPatchOperation operation, ScenarioPatchPreviewResult preview, bool forceOpenScenarioWrites)
    {
        if (operation.Commands.Count == 0)
        {
            preview.Issues.Add(Issue("error", "scenario", preview.RelativePath, $"{operation.Operation} requires at least one command draft."));
            return;
        }

        if (forceOpenScenarioWrites) return;

        foreach (var draft in operation.Commands)
        {
            var commandId = ResolveCommandId(draft.CommandId, draft.CommandIdHex);
            if (!SafeScenarioCommandIds.Contains(commandId))
            {
                preview.Issues.Add(Issue("error", "scenario", preview.RelativePath, $"Draft command {CCZModStudio.Core.HexDisplayFormatter.FormatByte((byte)commandId)} is not in the V1 safe whitelist."));
            }
        }
    }

    private void ApplyScenarioOperation(LegacyScenarioDocument document, ModScenarioPatchOperation operation)
    {
        var command = ResolveScenarioCommand(document, operation, null)
            ?? throw new InvalidOperationException("Scenario command was not found for operation.");
        var parameter = ResolveScenarioParameter(command, operation, null)
            ?? throw new InvalidOperationException("Scenario parameter was not found for operation.");

        if (operation.Operation.Equals("replace_text_parameter", StringComparison.OrdinalIgnoreCase))
        {
            parameter.Text = operation.Text ?? string.Empty;
        }
        else if (operation.Operation.Equals("update_numeric_parameter", StringComparison.OrdinalIgnoreCase))
        {
            parameter.IntValue = operation.Value ?? throw new InvalidOperationException("value is required for update_numeric_parameter.");
        }
        else if (operation.Operation.Equals("replace_variable_array", StringComparison.OrdinalIgnoreCase))
        {
            parameter.Values.Clear();
            parameter.Values.AddRange(operation.Values ?? []);
        }
        else
        {
            throw new InvalidOperationException($"Scenario operation {operation.Operation} is not writable in V1.");
        }
    }

    private LegacyScenarioDocument ReadScenarioDocument(CczProject project, ModScenarioPatch patch, SceneStringDocument dictionary, out string relativePath)
    {
        if (string.IsNullOrWhiteSpace(patch.RelativePath))
        {
            throw new InvalidOperationException("scenario patch relativePath is required.");
        }

        var fullPath = ResolveProjectPath(project, patch.RelativePath, mustExist: true);
        if (!Path.GetExtension(fullPath).Equals(".eex", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("scenario patch target must be an .eex file.");
        }

        relativePath = Path.GetRelativePath(project.GameRoot, fullPath).Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
        if (!relativePath.StartsWith("RS" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("scenario patch target must be under RS.");
        }

        if (!ScenarioFileReader.IsRsScriptFile(Path.GetFileName(fullPath)))
        {
            throw new InvalidOperationException("scenario patch target must be RS/R_*.eex or RS/S_*.eex.");
        }

        return _scenarioReader.Read(fullPath, dictionary);
    }

    private static LegacyScenarioCommandNode? ResolveScenarioCommand(LegacyScenarioDocument document, ModScenarioPatchOperation operation, ScenarioPatchPreviewResult? preview)
    {
        var commandId = ResolveCommandId(operation.CommandId, operation.CommandIdHex);
        var matches = document.EnumerateCommands()
            .Where(command => !operation.CommandOrdinal.HasValue || command.CommandOrdinal == operation.CommandOrdinal.Value)
            .Where(command => !operation.SceneIndex.HasValue || command.SceneIndex == operation.SceneIndex.Value)
            .Where(command => !operation.SectionIndex.HasValue || command.SectionIndex == operation.SectionIndex.Value)
            .Where(command => !operation.CommandIndex.HasValue || command.CommandIndex == operation.CommandIndex.Value)
            .Where(command => commandId < 0 || command.CommandId == commandId)
            .ToList();

        if (matches.Count == 1) return matches[0];

        var target = operation.CommandOrdinal.HasValue
            ? "ordinal " + operation.CommandOrdinal.Value.ToString(CultureInfo.InvariantCulture)
            : operation.CommandIndex.HasValue
                ? "commandIndex " + operation.CommandIndex.Value.ToString(CultureInfo.InvariantCulture)
                : "command selector";
        var message = matches.Count == 0
            ? $"No command matched {target}."
            : $"Command selector {target} matched {matches.Count} commands; include sceneIndex/sectionIndex/commandIndex or commandOrdinal.";
        if (preview != null)
        {
            preview.Issues.Add(Issue("error", "scenario", target, message));
            return null;
        }

        throw new InvalidOperationException(message);
    }

    private static LegacyScenarioCommandParameter? ResolveScenarioParameter(LegacyScenarioCommandNode command, ModScenarioPatchOperation operation, ScenarioPatchPreviewResult? preview)
    {
        if (!operation.ParameterIndex.HasValue)
        {
            var message = "parameterIndex is required.";
            if (preview != null)
            {
                preview.Issues.Add(Issue("error", "scenario", command.DisplayText, message));
                return null;
            }

            throw new InvalidOperationException(message);
        }

        var parameter = command.Parameters.FirstOrDefault(item => item.Index == operation.ParameterIndex.Value);
        if (parameter != null) return parameter;

        var error = $"Parameter index {operation.ParameterIndex.Value} was not found on {command.DisplayText}.";
        if (preview != null)
        {
            preview.Issues.Add(Issue("error", "scenario", command.DisplayText, error));
            return null;
        }

        throw new InvalidOperationException(error);
    }

    private static int ResolveCommandId(int? commandId, string? commandIdHex)
    {
        if (commandId.HasValue) return commandId.Value;
        if (string.IsNullOrWhiteSpace(commandIdHex)) return -1;
        var text = commandIdHex.Trim();
        if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) text = text[2..];
        return int.TryParse(text, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : -1;
    }

    private static DataRow FindRow(DataTable data, int rowId, string tableName)
    {
        foreach (DataRow row in data.Rows)
        {
            if (Convert.ToInt32(row["ID"], CultureInfo.InvariantCulture) == rowId) return row;
        }

        throw new InvalidOperationException($"Row ID {rowId} was not found in table {tableName}.");
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

    private static string ResolveProjectPath(CczProject project, string relativeOrAbsolutePath, bool mustExist)
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

    private static string ResolveExternalPath(CczProject project, string path, Func<string, bool> exists)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new InvalidOperationException("replacementPath is required.");
        }

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

        return candidates.FirstOrDefault(exists)
               ?? throw new FileNotFoundException("Replacement file was not found.", candidates.First());
    }

    private static ModPackageValidationIssue Issue(string severity, string category, string target, string message)
        => new()
        {
            Severity = severity,
            Category = category,
            Target = target,
            Message = message
        };

    private static bool IsBlocking(string severity)
        => severity.Equals("error", StringComparison.OrdinalIgnoreCase) ||
           severity.Equals("blocked", StringComparison.OrdinalIgnoreCase);

    private static string MakeSafeFileStem(string value)
    {
        var stem = Path.GetFileNameWithoutExtension(value);
        if (string.IsNullOrWhiteSpace(stem)) stem = "mod-package";
        foreach (var invalid in Path.GetInvalidFileNameChars())
        {
            stem = stem.Replace(invalid, '_');
        }

        return stem.Replace(' ', '_');
    }

    private static string BuildMarkdownReport(ModPackage package, string reportKind, object reportPayload)
    {
        var builder = new StringBuilder();
        builder.AppendLine("# ModPackage Report");
        builder.AppendLine();
        builder.AppendLine("- Kind: " + reportKind);
        builder.AppendLine("- Package: " + (string.IsNullOrWhiteSpace(package.Metadata.Name) ? NormalizePackageId(package) : package.Metadata.Name));
        builder.AppendLine("- TargetVersion: " + package.Metadata.TargetVersion);
        builder.AppendLine("- Theme: " + package.Metadata.Theme);
        builder.AppendLine();

        if (reportPayload is ModPackagePreviewResult preview)
        {
            builder.AppendLine("## Preview");
            builder.AppendLine();
            builder.AppendLine("- CanApply: " + preview.CanApply);
            builder.AppendLine("- Summary: " + preview.Summary);
            builder.AppendLine("- Issues: " + preview.Issues.Count);
            foreach (var issue in preview.Issues.Take(80))
            {
                builder.AppendLine($"  - [{issue.Severity}] {issue.Category} {issue.Target}: {issue.Message}");
            }

            builder.AppendLine("- Changes: " + preview.Changes.Count);
            foreach (var change in preview.Changes.Take(120))
            {
                builder.AppendLine($"  - {change.Category} {change.Target} {change.RowId?.ToString(CultureInfo.InvariantCulture) ?? "-"} {change.Field}: {change.OldValue} => {change.NewValue}");
            }
        }
        else if (reportPayload is ModPackageApplyResult apply)
        {
            builder.AppendLine("## Apply");
            builder.AppendLine();
            builder.AppendLine("- Applied: " + apply.Applied);
            builder.AppendLine("- Summary: " + apply.Summary);
            builder.AppendLine("- Backups: " + apply.BackupPaths.Count);
            foreach (var path in apply.BackupPaths.Take(80))
            {
                builder.AppendLine("  - " + path);
            }
            builder.AppendLine("- Reports: " + apply.ReportPaths.Count);
            foreach (var path in apply.ReportPaths.Take(80))
            {
                builder.AppendLine("  - " + path);
            }
        }
        else
        {
            builder.AppendLine("```json");
            builder.AppendLine(JsonSerializer.Serialize(reportPayload, new JsonSerializerOptions { WriteIndented = true }));
            builder.AppendLine("```");
        }

        return builder.ToString();
    }
}
