using System.Data;
using System.Globalization;
using System.Text;
using System.Text.Json;
using CCZModStudio.Formats;
using CCZModStudio.Models;

namespace CCZModStudio.Core;

public sealed partial class ModPackageService
{
    private const int EmptyPerson1Code = -1;

    private static readonly HashSet<int> SafeScenarioCommandIds = new()
    {
        0x02, 0x03, 0x04, 0x05,
        0x11, 0x12, 0x13,
        0x14, 0x15, 0x16, 0x17, 0x19, 0x1A,
        0x30, 0x38,
        0x42, 0x44, 0x45, 0x46, 0x47, 0x48,
        0x52, 0x5A, 0x72, 0x76, 0x77
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

    private static readonly HashSet<string> StructuralScenarioOperations = new(StringComparer.OrdinalIgnoreCase)
    {
        "append_command",
        "insert_command"
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
        => Preview(project, tables, package, scenarioDictionary, allowStructuralScenarioWrites, strictPlayablePreview: false);

    public ModPackagePreviewResult Preview(
        CczProject project,
        IReadOnlyList<HexTableDefinition> tables,
        ModPackage package,
        SceneStringDocument? scenarioDictionary,
        bool allowStructuralScenarioWrites,
        bool strictPlayablePreview)
    {
        ArgumentNullException.ThrowIfNull(package);

        var result = new ModPackagePreviewResult
        {
            ProjectRoot = project.GameRoot,
            PackageId = NormalizePackageId(package),
            Name = package.Metadata.Name,
            PlayableTier = string.IsNullOrWhiteSpace(package.Metadata.PlayableTier) ? "draft" : package.Metadata.PlayableTier
        };

        ValidatePackageHeader(project, package, result);
        ValidateSlotPlan(package, result);
        PreviewTableUpdates(project, tables, package, result);
        var forceOpenScenarioWrites = IsForceOpenPackage(package);
        PreviewScenarioPatches(project, package, scenarioDictionary, result, allowStructuralScenarioWrites || forceOpenScenarioWrites, forceOpenScenarioWrites);
        PreviewEffectPackages(project, tables, package, result);
        PreviewResourceUpdates(project, package, result);
        AddValidationPlan(package, result);
        AddPlayabilityEvidence(project, package, result);
        if (strictPlayablePreview)
        {
            AddStrictPlayableIssues(package, result);
        }

        result.PlayableTier = DeterminePreviewPlayableTier(package, result);

        result.CanApply = true;
        result.Summary = $"ModPackage preview: tables={package.TableUpdates.Count}, scenarios={package.ScenarioPatches.Count}, effects={package.EffectPackages.Count}, resources={package.ResourceUpdates.Count}, tier={result.PlayableTier}, issues={result.Issues.Count}, canApply={result.CanApply}.";
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
        var structuralOperation = patch.Operations.FirstOrDefault(operation => StructuralScenarioOperations.Contains(operation.Operation));
        if (structuralOperation != null)
        {
            preview.Issues.Add(Issue(
                "error",
                "scenario",
                preview.RelativePath,
                $"Conservative apply_scenario_patch refuses structural operation {structuralOperation.Operation}; use an explicit structural writer only after preview/reread validation."));
            preview.CanApply = false;
        }

        if (patch.Operations.Any(operation => !WritableScenarioOperations.Contains(operation.Operation)))
        {
            preview.Issues.Add(Issue(
                "error",
                "scenario",
                preview.RelativePath,
                "Conservative apply_scenario_patch supports only replace_text_parameter, update_numeric_parameter, and replace_variable_array."));
            preview.CanApply = false;
        }

        if (!preview.CanApply)
        {
            return new ScenarioPatchApplyResult
            {
                PatchId = preview.PatchId,
                RelativePath = preview.RelativePath,
                Applied = false,
                SceneCount = preview.SceneCount,
                SectionCount = preview.SectionCount,
                CommandCount = preview.CommandCount,
                ValidationSummary = "Scenario patch preview failed; no file was written.",
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

    public static bool IsSafeScenarioCommandId(int commandId) => commandId >= 0 && commandId <= 0xFF;

    public static IReadOnlyList<int> GetSafeScenarioCommandIds()
        => Enumerable.Range(0, 0x100).ToList();

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
                var finalAssetRequired = update.Note.Contains("final asset required", StringComparison.OrdinalIgnoreCase);
                result.Issues.Add(Issue(
                    finalAssetRequired ? "error" : "warning",
                    "resource",
                    FirstNonEmpty(update.TargetRelativePath, update.Kind),
                    finalAssetRequired
                        ? "Required final asset is missing; playable-test-copy validation is blocked."
                        : "Resource is a generation task only; package remains static-only until a real asset is prepared and imported."));
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

    private static string DeterminePreviewPlayableTier(ModPackage package, ModPackagePreviewResult result)
    {
        if (result.Issues.Any(issue => IsBlocking(issue.Severity))) return "draft";
        if (package.ResourceUpdates.Any(update => update.Operation.Equals("generate_task", StringComparison.OrdinalIgnoreCase))) return "static-only";
        if (package.ScenarioPatches.Any(patch => patch.Operations.Any(operation => operation.Operation is "append_command" or "insert_command"))) return "static-only";
        if (package.ValidationPlan.RequireRuntimeSmoke) return "static-only";
        return string.IsNullOrWhiteSpace(package.Metadata.PlayableTier) ? "static-preview" : package.Metadata.PlayableTier;
    }

    private static void AddValidationPlan(ModPackage package, ModPackagePreviewResult result)
    {
        var smokes = new[]
        {
            "--effect-package-smoke",
            "--battlefield-unit-status-write-smoke",
            "--e5-image-replace-smoke",
            "--legacy-mfc-dialog-smoke",
            "--rs-text-write-smoke",
            "--rs-deployment-write-smoke",
            "--rs-write-smoke",
            "--map-preview-smoke",
            "--hexzmap-sync-smoke",
            "--map-terrain-consistency-smoke"
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

    private static void AddPlayabilityEvidence(CczProject project, ModPackage package, ModPackagePreviewResult result)
    {
        AddEvidence(result, "preview.static", result.CanApply ? "pass" : "blocked", result.CanApply ? "none" : "blocking", result.CanApply ? "Static preview has no blocking issues." : "Static preview has blocking issues.", "preview");
        AddEvidence(result, "scenario.r_to_s", HasScenarioCommand(package, 0x11) ? "present" : "missing", "blocking", HasScenarioCommand(package, 0x11) ? "R/S jump command draft is present." : "No R/S jump command draft was found.", "scenario");
        AddEvidence(result, "scenario.deployment", HasScenarioCommand(package, 0x44, 0x46, 0x47, 0x4B) ? "present" : "missing", "blocking", HasScenarioCommand(package, 0x44, 0x46, 0x47, 0x4B) ? "Deployment command draft is present." : "No deployment command draft was found.", "scenario");
        AddEvidence(result, "scenario.ai", HasDeploymentWithAi(package) ? "present" : "missing", "blocking", HasDeploymentWithAi(package) ? "Deployment drafts include AI policy parameter slots." : "Deployment drafts do not expose AI policy slots.", "scenario");
        AddEvidence(result, "scenario.victory", HasTypedEvent(package, "victory") ? "present" : "missing", "blocking", HasTypedEvent(package, "victory") ? "Victory typed event is present." : "Victory typed event is missing.", "scenario");
        AddEvidence(result, "scenario.defeat", HasTypedEvent(package, "defeat") ? "present" : "missing", "blocking", HasTypedEvent(package, "defeat") ? "Defeat typed event is present." : "Defeat typed event is missing.", "scenario");
        AddEvidence(result, "scenario.reward", HasTypedEvent(package, "reward") || HasScenarioCommand(package, 0x72) ? "present" : "missing", "warning", HasTypedEvent(package, "reward") || HasScenarioCommand(package, 0x72) ? "Reward command/evidence is present." : "Reward evidence is missing.", "scenario");
        AddEvidence(result, "scenario.structural_draft", package.ScenarioPatches.Any(patch => patch.Operations.Any(operation => operation.Operation is "append_command" or "insert_command")) ? "draft" : "none", "blocking", package.ScenarioPatches.Any(patch => patch.Operations.Any(operation => operation.Operation is "append_command" or "insert_command")) ? "Structural append/insert commands remain drafts until strict command fixtures and runtime checks pass." : "No structural draft operations are present.", "scenario");
        AddEvidence(result, "resource.final_assets", package.ResourceUpdates.Any(update => update.Operation.Equals("generate_task", StringComparison.OrdinalIgnoreCase)) ? "missing" : "present", "blocking", package.ResourceUpdates.Any(update => update.Operation.Equals("generate_task", StringComparison.OrdinalIgnoreCase)) ? "One or more resources are generation tasks only." : "No generation-only resource tasks remain.", "resource");
        AddEvidence(result, "smoke.required", result.RequiredSmokeCommands.Count > 0 ? "planned" : "missing", "blocking", result.RequiredSmokeCommands.Count > 0 ? "Required smoke commands are listed in the validation plan." : "No required smoke commands were planned.", "smoke");
        AddEvidence(result, "runtime.battle_loaded", package.ValidationPlan.RequireRuntimeSmoke ? "required" : "not_required", package.ValidationPlan.RequireRuntimeSmoke ? "blocking" : "none", package.ValidationPlan.RequireRuntimeSmoke ? "Runtime battle_loaded evidence is required before runtime-playable." : "Package does not request runtime smoke.", "runtime");

        foreach (var mapId in package.SlotPlan.MapIds.Where(id => !string.IsNullOrWhiteSpace(id)).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var mapPath = Path.Combine(project.GameRoot, "Map", mapId + ".jpg");
            AddEvidence(result, "map.jpg." + mapId, File.Exists(mapPath) ? "present" : "missing", "blocking", File.Exists(mapPath) ? $"Map image {mapId}.jpg exists." : $"Map image {mapId}.jpg is missing.", "map");
        }
    }

    private static void AddStrictPlayableIssues(ModPackage package, ModPackagePreviewResult result)
    {
        foreach (var evidence in result.PlayabilityEvidence.Where(item => item.TierImpact.Equals("blocking", StringComparison.OrdinalIgnoreCase) && !item.Status.Equals("pass", StringComparison.OrdinalIgnoreCase) && !item.Status.Equals("present", StringComparison.OrdinalIgnoreCase) && !item.Status.Equals("none", StringComparison.OrdinalIgnoreCase)))
        {
            result.Issues.Add(Issue("error", "playability", evidence.Key, evidence.Message));
        }

        foreach (var draft in package.ScenarioPatches.SelectMany(patch => patch.Operations).SelectMany(operation => operation.Commands))
        {
            var commandId = ResolveCommandId(draft.CommandId, draft.CommandIdHex);
            if (commandId == 0x76 && string.IsNullOrWhiteSpace(draft.Note))
            {
                result.Issues.Add(Issue("error", "playability", "scenario.fallback", "Bare 0x76 fallback command cannot be used for strict playable preview."));
            }

            if (draft.Parameters.Any(parameter => string.IsNullOrWhiteSpace(parameter.Kind) || !parameter.LayoutCode.HasValue))
            {
                result.Issues.Add(Issue("error", "playability", "scenario.parameters", $"Draft command {HexDisplayFormatter.FormatByte((byte)Math.Clamp(commandId, 0, 255))} has an unexplained parameter."));
            }
        }

        if (package.ValidationPlan.RequireRuntimeSmoke)
        {
            result.Issues.Add(Issue("error", "runtime", "battle_loaded", "Runtime smoke is required but no runtime evidence is attached to this preview."));
        }
    }

    private static void AddEvidence(ModPackagePreviewResult result, string key, string status, string tierImpact, string message, string source)
    {
        result.PlayabilityEvidence.Add(new ModPlayabilityEvidence
        {
            Key = key,
            Status = status,
            TierImpact = tierImpact,
            Message = message,
            Source = source
        });
    }

    private static bool HasScenarioCommand(ModPackage package, params int[] commandIds)
    {
        var set = commandIds.ToHashSet();
        return package.ScenarioPatches
            .SelectMany(patch => patch.Operations)
            .SelectMany(operation => operation.Commands)
            .Any(command => set.Contains(ResolveCommandId(command.CommandId, command.CommandIdHex)));
    }

    private static bool HasTypedEvent(ModPackage package, string eventKind)
        => package.ScenarioPatches
            .SelectMany(patch => patch.Operations)
            .SelectMany(operation => operation.Commands)
            .Any(command => command.Note.Contains("typed_event:" + eventKind, StringComparison.OrdinalIgnoreCase));

    private static bool HasDeploymentWithAi(ModPackage package)
        => package.ScenarioPatches
            .SelectMany(patch => patch.Operations)
            .SelectMany(operation => operation.Commands)
            .Any(command =>
            {
                var commandId = ResolveCommandId(command.CommandId, command.CommandIdHex);
                return commandId is 0x44 or 0x46 or 0x47 or 0x4B && command.Parameters.Count >= 8;
            });

    private void PreviewScenarioOperation(
        LegacyScenarioDocument document,
        ModScenarioPatchOperation operation,
        ScenarioPatchPreviewResult preview,
        bool allowStructuralScenarioWrites,
        bool forceOpenScenarioWrites)
    {
        var knownOperation = SafeScenarioOperations.Contains(operation.Operation);

        if (operation.Operation.Equals("append_command", StringComparison.OrdinalIgnoreCase) ||
            operation.Operation.Equals("insert_command", StringComparison.OrdinalIgnoreCase))
        {
            preview.StructuralOperationCount++;
            PreviewStructuralScenarioOperation(document, operation, preview);
            return;
        }

        if (!knownOperation)
        {
            preview.Changes.Add(new ModPackageChangePreview
            {
                Category = "scenario",
                Target = preview.RelativePath,
                Field = operation.Operation,
                OldValue = "unknown",
                NewValue = "accepted",
                Changed = false,
                Note = "Unknown scenario operation accepted by force-open MCP mode."
            });
            return;
        }

        var command = ResolveScenarioCommand(document, operation, preview);
        if (command == null) return;

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
        if (operation.Commands.Count == 0) return;

        if (forceOpenScenarioWrites) return;

        foreach (var draft in operation.Commands)
        {
            var commandId = ResolveCommandId(draft.CommandId, draft.CommandIdHex);
            if (!SafeScenarioCommandIds.Contains(commandId))
            {
                preview.Issues.Add(Issue("error", "scenario", preview.RelativePath, $"Draft command {CCZModStudio.Core.HexDisplayFormatter.FormatByte((byte)commandId)} is not in the V1 safe whitelist."));
            }

            if (draft.Parameters.Any(parameter => string.IsNullOrWhiteSpace(parameter.Kind) || !parameter.LayoutCode.HasValue))
            {
                preview.Issues.Add(Issue("error", "scenario", preview.RelativePath, $"Draft command {CCZModStudio.Core.HexDisplayFormatter.FormatByte((byte)Math.Clamp(commandId, 0, 255))} has a parameter without kind/layout evidence."));
            }
        }
    }

    private void ApplyScenarioOperation(LegacyScenarioDocument document, ModScenarioPatchOperation operation)
    {
        if (operation.Operation.Equals("append_command", StringComparison.OrdinalIgnoreCase) ||
            operation.Operation.Equals("insert_command", StringComparison.OrdinalIgnoreCase))
        {
            ApplyStructuralScenarioOperation(document, operation);
            return;
        }

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
            return;
        }
    }

    private static void ApplyStructuralScenarioOperation(LegacyScenarioDocument document, ModScenarioPatchOperation operation)
    {
        var section = ResolveScenarioSection(document, operation)
            ?? throw new InvalidOperationException("Target scene/section was not found.");
        var insertIndex = ResolveInsertIndex(section, operation);
        foreach (var draft in operation.Commands)
        {
            section.Commands.Insert(insertIndex, CreateScenarioCommandFromDraft(draft, section.SceneIndex, section.SectionIndex));
            insertIndex++;
        }
    }

    private static LegacyScenarioCommandNode CreateScenarioCommandFromDraft(ModScenarioCommandDraft draft, int sceneIndex, int sectionIndex)
    {
        var commandId = ResolveCommandId(draft.CommandId, draft.CommandIdHex);
        if (commandId < 0) commandId = draft.CommandId;
        var command = new LegacyScenarioCommandNode
        {
            SceneIndex = sceneIndex,
            SectionIndex = sectionIndex,
            CommandId = commandId,
            CommandName = $"Command 0x{commandId:X2}",
            FileOffset = 0,
            ConsumedBytes = 0
        };

        var defaultParameters = CreateDefaultScenarioParameters(commandId);
        foreach (var parameter in defaultParameters)
        {
            command.Parameters.Add(parameter);
        }

        for (var i = 0; i < draft.Parameters.Count; i++)
        {
            var source = draft.Parameters[i];
            var target = i < command.Parameters.Count
                ? command.Parameters[i]
                : CreateParameterFromDraft(source, i);
            ApplyParameterDraft(target, source);
            if (i >= command.Parameters.Count)
            {
                command.Parameters.Add(target);
            }
        }

        return command;
    }

    private static List<LegacyScenarioCommandParameter> CreateDefaultScenarioParameters(int commandId)
    {
        if (commandId < 0 || commandId >= ScenarioStructureProbeReader.LegacyCommandInstructionTable.Count)
        {
            return [];
        }

        var instructions = ScenarioStructureProbeReader.LegacyCommandInstructionTable[commandId];
        var result = new List<LegacyScenarioCommandParameter>();
        var parameterCount = commandId switch
        {
            0x46 => 11 * 20,
            0x47 => 12 * 80,
            _ => 13
        };
        for (var index = 0; index < parameterCount; index++)
        {
            var layoutCode = commandId switch
            {
                0x46 => instructions[index % 11],
                0x47 => instructions[index % 12],
                _ => instructions[index]
            };
            if (layoutCode == -1) break;

            result.Add(new LegacyScenarioCommandParameter
            {
                Index = result.Count,
                LayoutCode = layoutCode,
                Tag = layoutCode,
                FileOffset = 0,
                Kind = KindFromLayoutCode(layoutCode),
                ByteLength = ByteLengthFromLayoutCode(layoutCode),
                IntValue = GetDefaultScenarioParameterValue(commandId, index)
            });
        }

        return result;
    }

    private static int GetDefaultScenarioParameterValue(int commandId, int parameterIndex)
    {
        if (IsForceAllyDeploymentPersonParameter(commandId, parameterIndex))
        {
            return EmptyPerson1Code;
        }

        var definition = BattlefieldDeploymentRecordDefinition.FromCommandId(commandId);
        if (definition is { WritesPerson: true } &&
            definition.PersonIndex >= 0 &&
            definition.GroupSize > 0 &&
            parameterIndex % definition.GroupSize == definition.PersonIndex)
        {
            return BattlefieldDeploymentRecordFormatter.EmptyPerson2Code;
        }

        return 0;
    }

    private static bool IsForceAllyDeploymentPersonParameter(int commandId, int parameterIndex)
        => commandId == 0x4A && parameterIndex is >= 1 and <= 10;

    private static LegacyScenarioCommandParameter CreateParameterFromDraft(ModScenarioParameterDraft draft, int index)
    {
        var layoutCode = draft.LayoutCode ?? LayoutCodeFromKind(draft.Kind);
        return new LegacyScenarioCommandParameter
        {
            Index = index,
            LayoutCode = layoutCode,
            Tag = layoutCode,
            FileOffset = 0,
            Kind = KindFromDraft(draft, layoutCode),
            ByteLength = ByteLengthFromLayoutCode(layoutCode)
        };
    }

    private static void ApplyParameterDraft(LegacyScenarioCommandParameter target, ModScenarioParameterDraft source)
    {
        target.Kind = KindFromDraft(source, target.LayoutCode);
        target.ByteLength = ByteLengthFromLayoutCode(target.LayoutCode);
        target.IntValue = source.IntValue ?? 0;
        target.Text = source.Text ?? string.Empty;
        target.Values.Clear();
        target.Values.AddRange(source.Values ?? []);
    }

    private static LegacyScenarioParameterKind KindFromDraft(ModScenarioParameterDraft draft, int layoutCode)
    {
        if (draft.Kind.Equals("text", StringComparison.OrdinalIgnoreCase)) return LegacyScenarioParameterKind.Text;
        if (draft.Kind.Equals("variable_array", StringComparison.OrdinalIgnoreCase) ||
            draft.Kind.Equals("variablearray", StringComparison.OrdinalIgnoreCase) ||
            draft.Kind.Equals("vararray", StringComparison.OrdinalIgnoreCase)) return LegacyScenarioParameterKind.VariableArray;
        if (draft.Kind.Equals("dword32", StringComparison.OrdinalIgnoreCase) ||
            draft.Kind.Equals("int32", StringComparison.OrdinalIgnoreCase)) return LegacyScenarioParameterKind.Dword32;
        if (draft.Kind.Equals("word16", StringComparison.OrdinalIgnoreCase) ||
            draft.Kind.Equals("int16", StringComparison.OrdinalIgnoreCase) ||
            draft.Kind.Equals("uint16", StringComparison.OrdinalIgnoreCase)) return LegacyScenarioParameterKind.Word16;
        return KindFromLayoutCode(layoutCode);
    }

    private static int LayoutCodeFromKind(string kind)
    {
        if (kind.Equals("text", StringComparison.OrdinalIgnoreCase)) return 0x05;
        if (kind.Equals("variable_array", StringComparison.OrdinalIgnoreCase) ||
            kind.Equals("variablearray", StringComparison.OrdinalIgnoreCase) ||
            kind.Equals("vararray", StringComparison.OrdinalIgnoreCase)) return 0x35;
        if (kind.Equals("dword32", StringComparison.OrdinalIgnoreCase) ||
            kind.Equals("int32", StringComparison.OrdinalIgnoreCase)) return 0x04;
        return 0x01;
    }

    private static LegacyScenarioParameterKind KindFromLayoutCode(int layoutCode)
        => layoutCode switch
        {
            0x05 => LegacyScenarioParameterKind.Text,
            0x35 => LegacyScenarioParameterKind.VariableArray,
            0x04 => LegacyScenarioParameterKind.Dword32,
            _ => LegacyScenarioParameterKind.Word16
        };

    private static int ByteLengthFromLayoutCode(int layoutCode)
        => layoutCode switch
        {
            0x05 => 1,
            0x35 => 2,
            0x04 => 4,
            _ => 2
        };

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
            builder.AppendLine("- PlayabilityEvidence: " + preview.PlayabilityEvidence.Count);
            foreach (var evidence in preview.PlayabilityEvidence.Take(80))
            {
                builder.AppendLine($"  - [{evidence.Status}/{evidence.TierImpact}] {evidence.Key}: {evidence.Message}");
            }

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
