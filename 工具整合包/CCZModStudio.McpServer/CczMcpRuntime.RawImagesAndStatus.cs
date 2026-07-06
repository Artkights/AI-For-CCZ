using CCZModStudio.Core;
using CCZModStudio.Formats;
using CCZModStudio.Models;

namespace CCZModStudio.McpServer;

public sealed partial class CczMcpRuntime
{
    public object ListBattlefieldUnitStatusTargets(string? gameRoot, string? relativePath, string? keyword, int limit)
    {
        var project = LoadProject(gameRoot);
        var tables = LoadTables(project);
        var dictionary = LoadRequiredScenarioCommandDictionary(project);
        var scenarios = ResolveBattlefieldStatusScenarioFiles(project, relativePath);
        var effectiveLimit = NormalizeLimit(limit, 200, 1000);
        var targets = new List<object>();
        var errors = new List<object>();

        foreach (var scenario in scenarios)
        {
            try
            {
                var document = _battlefieldEditorService.Load(project, scenario, dictionary, tables);
                foreach (var candidate in document.UnitCandidates
                             .Where(IsWritableBattlefieldStatusCandidate)
                             .Where(candidate => MatchesBattlefieldUnitStatusKeyword(candidate, keyword)))
                {
                    targets.Add(BuildBattlefieldUnitStatusTargetPayload(project, scenario, candidate));
                }
            }
            catch (Exception ex)
            {
                errors.Add(new
                {
                    scenario.FileName,
                    RelativePath = TryNormalizeProjectRelativePath(project, scenario.Path),
                    Error = ex.Message
                });
            }
        }

        return new
        {
            project.GameRoot,
            TotalTargets = targets.Count,
            ReturnedTargets = Math.Min(targets.Count, effectiveLimit),
            Errors = errors,
            Targets = targets.Take(effectiveLimit),
            SafetyNote = "Read-only list. Only TargetKey values returned here are accepted by read/write battlefield unit status tools."
        };
    }

    public object ReadBattlefieldUnitStatus(string? gameRoot, string relativePath, string targetKey)
    {
        var project = LoadProject(gameRoot);
        var scenario = ResolveBattlefieldStatusScenario(project, relativePath);
        var dictionary = LoadRequiredScenarioCommandDictionary(project);
        var draft = _battlefieldUnitStatusWriteService.LoadDraft(
            scenario,
            dictionary,
            BuildBattlefieldStatusPlacement(targetKey));

        return new
        {
            project.GameRoot,
            Scenario = BuildScenarioFilePayload(scenario),
            Draft = BuildBattlefieldUnitStatusDraftPayload(draft),
            SafetyNote = "Read-only draft. Use write_battlefield_unit_status with the same target_key to write selected fields."
        };
    }

    public object WriteBattlefieldUnitStatus(
        string? gameRoot,
        string relativePath,
        BattlefieldUnitStatusUpdate update,
        string? writeMode)
    {
        if (update == null) throw new InvalidOperationException("update is required.");
        if (string.IsNullOrWhiteSpace(update.TargetKey)) throw new InvalidOperationException("update.target_key is required.");

        var project = LoadProject(gameRoot);
        EnsureWriteMode(project, writeMode);
        var scenario = ResolveBattlefieldStatusScenario(project, relativePath);
        var dictionary = LoadRequiredScenarioCommandDictionary(project);
        var draft = _battlefieldUnitStatusWriteService.LoadDraft(
            scenario,
            dictionary,
            BuildBattlefieldStatusPlacement(update.TargetKey));
        ApplyBattlefieldUnitStatusUpdate(draft, update);
        var result = _battlefieldUnitStatusWriteService.Save(project, scenario, dictionary, draft);

        return new
        {
            project.GameRoot,
            Scenario = BuildScenarioFilePayload(scenario),
            Draft = BuildBattlefieldUnitStatusDraftPayload(draft),
            Result = result,
                SafetyNote = "Write uses LegacyScenarioWriter with backup, report, and reread validation; only 46/47 deployment records are supported."
        };
    }

    public object ExportBmpAssets(
        string? gameRoot,
        string kind,
        string outputRoot,
        List<BmpExportTargetUpdate>? targets,
        bool? singleMode,
        bool overwriteExisting,
        int factionSlot)
    {
        var project = LoadProject(gameRoot);
        var exportTargets = BuildBmpExportTargets(targets);
        var request = new BmpExportRequest
        {
            Kind = ParseBmpExportKind(kind),
            OutputRoot = ResolveExternalOutputDirectory(project, outputRoot),
            SingleMode = singleMode ?? exportTargets.Count == 1,
            OverwriteExisting = overwriteExisting,
            FactionSlot = factionSlot,
            Targets = exportTargets
        };
        return BuildBmpExportPayload(_bmpImageExportService.Export(project, request));
    }

    public object PreviewRImageRawReplace(string? gameRoot, int rImageId, string materialFolder)
    {
        var project = LoadProject(gameRoot);
        var request = new RImageReplaceRequest
        {
            RImageId = rImageId,
            MaterialFolder = ResolveExternalDirectory(project, materialFolder)
        };
        return BuildRImageRawReplacePreviewPayload(_rImageReplaceService.Preview(project, request));
    }

    public object ValidateRsPixelMaterialPackage(
        string? gameRoot,
        string? materialRoot,
        string? rMaterialFolder,
        string? sMaterialFolder,
        int? rImageId,
        int? sImageId,
        int? jobId,
        int factionSlot)
    {
        var project = LoadProject(gameRoot);
        var request = new RsPixelMaterialValidationRequest
        {
            MaterialRoot = ResolveOptionalExternalDirectory(project, materialRoot),
            RMaterialFolder = ResolveOptionalExternalDirectory(project, rMaterialFolder),
            SMaterialFolder = ResolveOptionalExternalDirectory(project, sMaterialFolder),
            RImageId = rImageId,
            SImageId = sImageId,
            JobId = jobId,
            FactionSlot = factionSlot
        };
        return BuildRsPixelMaterialValidationPayload(_rsPixelMaterialValidationService.Validate(project, request));
    }

    public object ReplaceRImageRaw(string? gameRoot, int rImageId, string materialFolder, string? writeMode)
    {
        var project = LoadProject(gameRoot);
        EnsureWriteMode(project, writeMode);
        var request = new RImageReplaceRequest
        {
            RImageId = rImageId,
            MaterialFolder = ResolveExternalDirectory(project, materialFolder),
            WriteMode = writeMode ?? "direct"
        };
        return BuildRImageRawReplaceResultPayload(_rImageReplaceService.Replace(project, request));
    }

    public object PreviewRImageRawBatchReplace(string? gameRoot, string materialRoot, List<int>? allowedRImageIds)
    {
        var project = LoadProject(gameRoot);
        var request = new BatchRImageReplaceRequest
        {
            MaterialRoot = ResolveExternalDirectory(project, materialRoot),
            AllowedRImageIds = (allowedRImageIds ?? []).Where(id => id >= 0).ToHashSet(),
            IncludeOnlySelectedOrFiltered = allowedRImageIds is { Count: > 0 }
        };
        return BuildBatchRImageRawReplacePreviewPayload(_batchRImageReplaceService.Preview(project, request));
    }

    public object ReplaceRImageRawBatch(string? gameRoot, string materialRoot, List<int>? allowedRImageIds, string? writeMode)
    {
        var project = LoadProject(gameRoot);
        EnsureWriteMode(project, writeMode);
        var request = new BatchRImageReplaceRequest
        {
            MaterialRoot = ResolveExternalDirectory(project, materialRoot),
            AllowedRImageIds = (allowedRImageIds ?? []).Where(id => id >= 0).ToHashSet(),
            IncludeOnlySelectedOrFiltered = allowedRImageIds is { Count: > 0 },
            WriteMode = writeMode ?? "direct"
        };
        return BuildBatchRImageRawReplaceResultPayload(_batchRImageReplaceService.Replace(project, request));
    }

    public object PreviewSImageRawReplace(
        string? gameRoot,
        int sImageId,
        string materialFolder,
        int? jobId,
        int factionSlot)
    {
        var project = LoadProject(gameRoot);
        var request = new SImageReplaceRequest
        {
            SImageId = sImageId,
            MaterialFolder = ResolveExternalDirectory(project, materialFolder),
            JobId = jobId,
            FactionSlot = factionSlot
        };
        return BuildSImageRawReplacePreviewPayload(_sImageReplaceService.Preview(project, request));
    }

    public object ReplaceSImageRaw(
        string? gameRoot,
        int sImageId,
        string materialFolder,
        int? jobId,
        int factionSlot,
        string? writeMode)
    {
        var project = LoadProject(gameRoot);
        EnsureWriteMode(project, writeMode);
        var request = new SImageReplaceRequest
        {
            SImageId = sImageId,
            MaterialFolder = ResolveExternalDirectory(project, materialFolder),
            JobId = jobId,
            FactionSlot = factionSlot,
            WriteMode = writeMode ?? "direct"
        };
        return BuildSImageRawReplaceResultPayload(_sImageReplaceService.Replace(project, request));
    }

    public object PreviewJobSImageRawReplace(
        string? gameRoot,
        int jobId,
        string materialFolder,
        List<int>? factionSlots)
    {
        var project = LoadProject(gameRoot);
        var request = new JobSImageReplaceRequest
        {
            JobId = jobId,
            MaterialFolder = ResolveExternalDirectory(project, materialFolder),
            FactionSlots = factionSlots ?? []
        };
        return BuildJobSImageRawReplacePreviewPayload(_jobSImageReplaceService.Preview(project, request));
    }

    public object ReplaceJobSImageRaw(
        string? gameRoot,
        int jobId,
        string materialFolder,
        List<int>? factionSlots,
        string? writeMode)
    {
        var project = LoadProject(gameRoot);
        EnsureWriteMode(project, writeMode);
        var request = new JobSImageReplaceRequest
        {
            JobId = jobId,
            MaterialFolder = ResolveExternalDirectory(project, materialFolder),
            FactionSlots = factionSlots ?? [],
            WriteMode = writeMode ?? "direct"
        };
        return BuildJobSImageRawReplaceResultPayload(_jobSImageReplaceService.Replace(project, request));
    }

    public object PreviewSImageRawBatchReplace(
        string? gameRoot,
        string materialRoot,
        List<BatchSImageUsageUpdate>? allowedUsages,
        int factionSlot)
    {
        var project = LoadProject(gameRoot);
        var request = new BatchSImageReplaceRequest
        {
            MaterialRoot = ResolveExternalDirectory(project, materialRoot),
            AllowedSImageUsages = BuildBatchSImageUsages(allowedUsages),
            IncludeOnlySelectedOrFiltered = allowedUsages is { Count: > 0 },
            FactionSlot = factionSlot
        };
        return BuildBatchSImageRawReplacePreviewPayload(_batchSImageReplaceService.Preview(project, request));
    }

    public object ReplaceSImageRawBatch(
        string? gameRoot,
        string materialRoot,
        List<BatchSImageUsageUpdate>? allowedUsages,
        int factionSlot,
        string? writeMode)
    {
        var project = LoadProject(gameRoot);
        EnsureWriteMode(project, writeMode);
        var request = new BatchSImageReplaceRequest
        {
            MaterialRoot = ResolveExternalDirectory(project, materialRoot),
            AllowedSImageUsages = BuildBatchSImageUsages(allowedUsages),
            IncludeOnlySelectedOrFiltered = allowedUsages is { Count: > 0 },
            FactionSlot = factionSlot,
            WriteMode = writeMode ?? "direct"
        };
        return BuildBatchSImageRawReplaceResultPayload(_batchSImageReplaceService.Replace(project, request));
    }

    public object PreviewItemIconBatchImport(
        string? gameRoot,
        List<string>? sourceFiles,
        string? sourceRoot,
        List<BatchItemIconTargetRowUpdate>? targetRows,
        string? matchMode)
    {
        var project = LoadProject(gameRoot);
        var request = new BatchItemIconImportRequest
        {
            SourceFiles = ResolveOptionalExternalFiles(project, sourceFiles),
            SourceRoot = ResolveOptionalExternalDirectory(project, sourceRoot),
            TargetRows = BuildBatchItemIconTargetRows(targetRows),
            MatchMode = string.IsNullOrWhiteSpace(matchMode) ? "auto" : matchMode
        };
        return BuildBatchItemIconImportPreviewPayload(_batchItemIconImportService.Preview(project, request));
    }

    public object ReplaceItemIconBatchImport(
        string? gameRoot,
        List<string>? sourceFiles,
        string? sourceRoot,
        List<BatchItemIconTargetRowUpdate>? targetRows,
        string? matchMode,
        string? writeMode)
    {
        var project = LoadProject(gameRoot);
        EnsureWriteMode(project, writeMode);
        var request = new BatchItemIconImportRequest
        {
            SourceFiles = ResolveOptionalExternalFiles(project, sourceFiles),
            SourceRoot = ResolveOptionalExternalDirectory(project, sourceRoot),
            TargetRows = BuildBatchItemIconTargetRows(targetRows),
            MatchMode = string.IsNullOrWhiteSpace(matchMode) ? "auto" : matchMode,
            WriteMode = writeMode ?? "direct"
        };
        return BuildBatchItemIconImportResultPayload(_batchItemIconImportService.Replace(project, request));
    }

    public object PreviewStrategyIconBatchImport(
        string? gameRoot,
        List<string>? sourceFiles,
        string? sourceRoot,
        List<BatchStrategyIconTargetRowUpdate>? targetRows,
        string? matchMode)
    {
        var project = LoadProject(gameRoot);
        var request = new BatchStrategyIconImportRequest
        {
            SourceFiles = ResolveOptionalExternalFiles(project, sourceFiles),
            SourceRoot = ResolveOptionalExternalDirectory(project, sourceRoot),
            TargetRows = BuildBatchStrategyIconTargetRows(targetRows),
            MatchMode = string.IsNullOrWhiteSpace(matchMode) ? "auto" : matchMode
        };
        return BuildBatchStrategyIconImportPreviewPayload(_batchStrategyIconImportService.Preview(project, request));
    }

    public object ReplaceStrategyIconBatchImport(
        string? gameRoot,
        List<string>? sourceFiles,
        string? sourceRoot,
        List<BatchStrategyIconTargetRowUpdate>? targetRows,
        string? matchMode,
        string? writeMode)
    {
        var project = LoadProject(gameRoot);
        EnsureWriteMode(project, writeMode);
        var request = new BatchStrategyIconImportRequest
        {
            SourceFiles = ResolveOptionalExternalFiles(project, sourceFiles),
            SourceRoot = ResolveOptionalExternalDirectory(project, sourceRoot),
            TargetRows = BuildBatchStrategyIconTargetRows(targetRows),
            MatchMode = string.IsNullOrWhiteSpace(matchMode) ? "auto" : matchMode,
            WriteMode = writeMode ?? "direct"
        };
        return BuildBatchStrategyIconImportResultPayload(_batchStrategyIconImportService.Replace(project, request));
    }

    public object PreviewRoleFaceBatchImport(
        string? gameRoot,
        List<string>? sourceFiles,
        string? sourceRoot,
        List<BatchRoleFaceTargetRowUpdate>? targetRows,
        string? matchMode)
    {
        var project = LoadProject(gameRoot);
        var request = new BatchRoleFaceImportRequest
        {
            SourceFiles = ResolveOptionalExternalFiles(project, sourceFiles),
            SourceRoot = ResolveOptionalExternalDirectory(project, sourceRoot),
            TargetRows = BuildBatchRoleFaceTargetRows(targetRows),
            MatchMode = string.IsNullOrWhiteSpace(matchMode) ? "auto" : matchMode
        };
        return BuildBatchRoleFaceImportPreviewPayload(_batchRoleFaceImportService.Preview(project, request));
    }

    public object ReplaceRoleFaceBatchImport(
        string? gameRoot,
        List<string>? sourceFiles,
        string? sourceRoot,
        List<BatchRoleFaceTargetRowUpdate>? targetRows,
        string? matchMode,
        string? writeMode)
    {
        var project = LoadProject(gameRoot);
        EnsureWriteMode(project, writeMode);
        var request = new BatchRoleFaceImportRequest
        {
            SourceFiles = ResolveOptionalExternalFiles(project, sourceFiles),
            SourceRoot = ResolveOptionalExternalDirectory(project, sourceRoot),
            TargetRows = BuildBatchRoleFaceTargetRows(targetRows),
            MatchMode = string.IsNullOrWhiteSpace(matchMode) ? "auto" : matchMode,
            WriteMode = writeMode ?? "direct"
        };
        return BuildBatchRoleFaceImportResultPayload(_batchRoleFaceImportService.Replace(project, request));
    }

    public object PreviewJobSImageRawBatchReplace(
        string? gameRoot,
        string materialRoot,
        List<int>? allowedJobIds,
        List<int>? factionSlots)
    {
        var project = LoadProject(gameRoot);
        var request = new BatchJobSImageReplaceRequest
        {
            MaterialRoot = ResolveExternalDirectory(project, materialRoot),
            AllowedJobIds = (allowedJobIds ?? []).Where(id => id >= 0).ToHashSet(),
            IncludeOnlySelectedOrFiltered = allowedJobIds is { Count: > 0 },
            FactionSlots = factionSlots ?? []
        };
        return BuildBatchJobSImageRawReplacePreviewPayload(_batchJobSImageReplaceService.Preview(project, request));
    }

    public object ReplaceJobSImageRawBatch(
        string? gameRoot,
        string materialRoot,
        List<int>? allowedJobIds,
        List<int>? factionSlots,
        string? writeMode)
    {
        var project = LoadProject(gameRoot);
        EnsureWriteMode(project, writeMode);
        var request = new BatchJobSImageReplaceRequest
        {
            MaterialRoot = ResolveExternalDirectory(project, materialRoot),
            AllowedJobIds = (allowedJobIds ?? []).Where(id => id >= 0).ToHashSet(),
            IncludeOnlySelectedOrFiltered = allowedJobIds is { Count: > 0 },
            FactionSlots = factionSlots ?? [],
            WriteMode = writeMode ?? "direct"
        };
        return BuildBatchJobSImageRawReplaceResultPayload(_batchJobSImageReplaceService.Replace(project, request));
    }

    public object PreviewE5RoleRawNormalize(string? gameRoot)
    {
        var project = LoadProject(gameRoot);
        return BuildE5RoleRawNormalizePreviewPayload(_e5RoleRawNormalizeService.Preview(project));
    }

    public object NormalizeE5RoleRaw(string? gameRoot, string? writeMode)
    {
        var project = LoadProject(gameRoot);
        EnsureWriteMode(project, writeMode);
        return BuildE5RoleRawNormalizeResultPayload(_e5RoleRawNormalizeService.Normalize(project));
    }

    private IReadOnlyList<ScenarioFileInfo> ResolveBattlefieldStatusScenarioFiles(CczProject project, string? relativePath)
    {
        if (!string.IsNullOrWhiteSpace(relativePath))
        {
            return new[] { ResolveBattlefieldStatusScenario(project, relativePath) };
        }

        return _scenarioFileReader.ReadAllIndex(project)
            .Where(file => ScenarioFileReader.IsBattlefieldScriptFile(file.FileName))
            .OrderBy(file => file.FileName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static ScenarioFileInfo ResolveBattlefieldStatusScenario(CczProject project, string relativePath)
    {
        var filePath = ResolveScenarioFile(project, relativePath);
        var fileName = Path.GetFileName(filePath);
        if (!ScenarioFileReader.IsBattlefieldScriptFile(fileName))
        {
            throw new InvalidOperationException("Battlefield unit status tools only support RS/S_*.eex files.");
        }

        return new ScenarioFileReader().Read(filePath, null);
    }

    private static BattlefieldPlacedUnit BuildBattlefieldStatusPlacement(string targetKey)
        => new()
        {
            TargetKey = targetKey,
            Name = "MCPUnitStatus",
            Source = "MCP"
        };

    private static bool IsWritableBattlefieldStatusCandidate(BattlefieldUnitCandidate candidate)
    {
        if (!BattlefieldEditorService.TryExtractPersonId(candidate, out var personId))
        {
            return false;
        }

        var placement = new BattlefieldPlacedUnit
        {
            TargetKey = candidate.TargetKey,
            PersonId = personId,
            Name = candidate.PersonDisplay,
            Source = "MCP"
        };
        return BattlefieldUnitStatusWriteService.IsWritableStatusTarget(placement);
    }

    private static bool MatchesBattlefieldUnitStatusKeyword(BattlefieldUnitCandidate candidate, string? keyword)
    {
        if (string.IsNullOrWhiteSpace(keyword)) return true;
        keyword = keyword.Trim();
        return candidate.TargetKey.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
               candidate.SourceCommand.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
               candidate.SourceCommandDisplay.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
               candidate.PersonDisplay.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
               candidate.PersonHint.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
               candidate.Category.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
               candidate.SceneSection.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
               candidate.OffsetHex.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
               candidate.CoordinateDisplay.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
               candidate.Annotation.Contains(keyword, StringComparison.OrdinalIgnoreCase);
    }

    private static object BuildBattlefieldUnitStatusTargetPayload(CczProject project, ScenarioFileInfo scenario, BattlefieldUnitCandidate candidate)
    {
        BattlefieldEditorService.TryExtractPersonId(candidate, out var personId);
        var hasCoordinate = BattlefieldEditorService.TryExtractFirstCoordinate(candidate, out var x, out var y);
        return new
        {
            scenario.FileName,
            RelativePath = NormalizeProjectRelativePath(project, scenario.Path),
            candidate.Index,
            candidate.TargetKey,
            candidate.BattlefieldNumber,
            PersonId = personId,
            candidate.PersonDisplay,
            candidate.PersonHint,
            candidate.Category,
            candidate.FactionDisplay,
            candidate.AiDisplay,
            candidate.LevelJobDisplay,
            candidate.SourceCommand,
            candidate.SourceCommandDisplay,
            candidate.SceneSection,
            OffsetHex = HexDisplayFormatter.NormalizeText(candidate.OffsetHex),
            X = hasCoordinate ? x : (int?)null,
            Y = hasCoordinate ? y : (int?)null,
            candidate.CoordinateDisplay,
            candidate.Annotation
        };
    }

    private static object BuildBattlefieldUnitStatusDraftPayload(BattlefieldUnitStatusDraft draft)
        => new
        {
            draft.TargetKey,
            draft.ScenarioFileName,
            draft.PersonId,
            draft.PersonName,
            draft.CommandId,
            CommandIdHex = HexDisplayFormatter.Format(draft.CommandId, 2),
            draft.RecordIndex,
            draft.LevelBonus,
            draft.JobLevel,
            draft.AiPolicy,
            draft.Weapon,
            draft.WeaponLevel,
            draft.Armor,
            draft.ArmorLevel,
            draft.Assist,
            draft.JobId,
            draft.HasEquipmentCommand,
            draft.HasJobCommand,
            draft.EquipmentBoundarySummary,
            Abilities = draft.Abilities.Select(ability => new
            {
                ability.AbilityId,
                ability.Name,
                ability.Operation,
                ability.Value,
                ability.HasCommand
            }),
            draft.SourceSummary,
            CommandPreview = HexDisplayFormatter.NormalizeText(draft.CommandPreview)
        };

    private static void ApplyBattlefieldUnitStatusUpdate(BattlefieldUnitStatusDraft draft, BattlefieldUnitStatusUpdate update)
    {
        if (update.LevelBonus.HasValue) draft.LevelBonus = update.LevelBonus;
        if (update.JobLevel.HasValue) draft.JobLevel = update.JobLevel;
        if (update.AiPolicy.HasValue) draft.AiPolicy = update.AiPolicy;
        if (update.Weapon.HasValue) draft.Weapon = update.Weapon;
        if (update.WeaponLevel.HasValue) draft.WeaponLevel = update.WeaponLevel;
        if (update.Armor.HasValue) draft.Armor = update.Armor;
        if (update.ArmorLevel.HasValue) draft.ArmorLevel = update.ArmorLevel;
        if (update.Assist.HasValue) draft.Assist = update.Assist;
        if (update.JobId.HasValue) draft.JobId = update.JobId;

        if (update.Abilities == null) return;
        foreach (var abilityUpdate in update.Abilities)
        {
            var ability = draft.Abilities.FirstOrDefault(item => item.AbilityId == abilityUpdate.AbilityId)
                ?? throw new InvalidOperationException($"Unsupported ability_id for battlefield unit status: {abilityUpdate.AbilityId}.");
            if (abilityUpdate.Operation.HasValue) ability.Operation = abilityUpdate.Operation;
            if (abilityUpdate.Value.HasValue) ability.Value = abilityUpdate.Value;
        }
    }

    private static object BuildRImageRawReplacePreviewPayload(RImageReplacePreviewResult preview)
        => new
        {
            preview.Request.RImageId,
            preview.Request.MaterialFolder,
            Mapping = preview.Mapping,
            preview.TotalOperationCount,
            preview.Warnings,
            Files = preview.Files.Select(file => new
            {
                file.Role,
                file.TargetFileName,
                file.TargetPath,
                file.SourcePath,
                file.ImageNumber,
                Encode = BuildE5TrueColorEncodePayload(file.Encode)
            }),
            BatchPreview = BuildE5ImageBatchReplacePayload(preview.BatchPreview),
            SafetyNote = "Preview only. R image true-color replacement writes Pmapobj.e5 through replace_r_image_raw with backup and reread verification."
        };

    private static object BuildRsPixelMaterialValidationPayload(RsPixelMaterialValidationResult result)
        => new
        {
            result.MaterialRoot,
            result.RMaterialFolder,
            result.SMaterialFolder,
            result.Request.RImageId,
            result.Request.SImageId,
            result.Request.JobId,
            result.Request.FactionSlot,
            result.FormatPassed,
            result.PreviewPassed,
            result.ReadyForTestCopyWrite,
            result.RequiresTestCopyWrite,
            result.TotalOperationCount,
            result.Warnings,
            result.Errors,
            Files = result.Files.Select(file => new
            {
                file.Group,
                file.Role,
                file.ExpectedFileName,
                file.Path,
                file.Exists,
                ExpectedSize = $"{file.ExpectedWidth}x{file.ExpectedHeight}",
                ActualSize = file.Width.HasValue && file.Height.HasValue ? $"{file.Width}x{file.Height}" : string.Empty,
                file.Width,
                file.Height,
                file.DimensionPassed,
                file.PixelCount,
                file.TransparentPixelCount,
                file.StrictMagentaPixelCount,
                file.NearMagentaPixelCount,
                file.StrictMagentaPercent,
                file.NearMagentaPercent,
                file.InteriorStrictMagentaPixelCount,
                file.InteriorNearMagentaPixelCount,
                file.MagentaKeyLikely,
                file.Warnings,
                file.Errors
            }),
            RPreview = result.RPreview == null ? null : BuildRImageRawReplacePreviewPayload(result.RPreview),
            SPreview = result.SPreview == null ? null : BuildSImageRawReplacePreviewPayload(result.SPreview),
            PreviewOperations = result.PreviewOperations.Select(operation => new
            {
                operation.Group,
                operation.TargetFileName,
                operation.TargetPath,
                operation.SourcePath,
                operation.ImageNumber,
                operation.OldKind,
                operation.NewKind,
                operation.OldSizeBytes,
                operation.NewSizeBytes,
                RawToPng = operation.OldKind.Equals("RAW", StringComparison.OrdinalIgnoreCase) &&
                           operation.NewKind.Equals("PNG", StringComparison.OrdinalIgnoreCase),
                operation.FormatWarnings
            }),
            result.SafetyNote
        };

    private static object BuildRImageRawReplaceResultPayload(RImageReplaceResult result)
        => new
        {
            result.Request.RImageId,
            result.Request.MaterialFolder,
            Mapping = result.Mapping,
            result.TotalOperationCount,
            result.Warnings,
            result.AggregateReportPath,
            Files = result.Files.Select(file => new
            {
                file.Role,
                file.TargetFileName,
                file.TargetPath,
                file.SourcePath,
                file.ImageNumber,
                Encode = BuildE5TrueColorEncodePayload(file.Encode)
            }),
            WriteResult = BuildE5ImageBatchReplacePayload(result.WriteResult),
            result.WriteResult.BackupPath,
            result.WriteResult.ReportPath,
            result.WriteResult.ReportJsonPath
        };

    private static object BuildSImageRawReplacePreviewPayload(SImageReplacePreviewResult preview)
        => new
        {
            preview.Request.SImageId,
            preview.Request.MaterialFolder,
            preview.Request.JobId,
            preview.Request.FactionSlot,
            Mapping = preview.Mapping,
            preview.TotalOperationCount,
            preview.Warnings,
            Files = preview.Files.Select(file => new
            {
                file.ActionName,
                file.TargetFileName,
                file.TargetPath,
                file.SourcePath,
                Encode = BuildE5TrueColorEncodePayload(file.Encode),
                BatchPreview = BuildE5ImageBatchReplacePayload(file.BatchPreview)
            }),
            SafetyNote = "Preview only. S image true-color replacement writes Unit_mov.e5, Unit_atk.e5, and/or Unit_spc.e5 through replace_s_image_raw with backups and reread verification."
        };

    private static object BuildSImageRawReplaceResultPayload(SImageReplaceResult result)
        => new
        {
            result.Request.SImageId,
            result.Request.MaterialFolder,
            result.Request.JobId,
            result.Request.FactionSlot,
            Mapping = result.Mapping,
            result.TotalOperationCount,
            result.Warnings,
            result.AggregateReportPath,
            Files = result.Files.Select(file => new
            {
                file.ActionName,
                file.TargetFileName,
                file.TargetPath,
                file.SourcePath,
                Encode = BuildE5TrueColorEncodePayload(file.Encode),
                WriteResult = BuildE5ImageBatchReplacePayload(file.WriteResult),
                file.WriteResult.BackupPath,
                file.WriteResult.ReportPath,
                file.WriteResult.ReportJsonPath
            })
        };

    private static object BuildJobSImageRawReplacePreviewPayload(JobSImageReplacePreviewResult preview)
        => new
        {
            preview.Request.JobId,
            preview.Request.MaterialFolder,
            FactionSlots = preview.Request.FactionSlots,
            preview.TotalOperationCount,
            preview.Warnings,
            Factions = preview.Factions.Select(faction => new
            {
                faction.FactionSlot,
                faction.FactionName,
                Mapping = faction.Preview.Mapping,
                faction.Preview.TotalOperationCount,
                Files = faction.Preview.Files.Select(file => new
                {
                    file.ActionName,
                    file.TargetFileName,
                    file.TargetPath,
                    file.SourcePath,
                    Encode = BuildE5TrueColorEncodePayload(file.Encode),
                    BatchPreview = BuildE5ImageBatchReplacePayload(file.BatchPreview)
                })
            }),
            SafetyNote = "Preview only. Job S image true-color replacement fixes S=0 and maps job_id + selected faction_slots to default Unit image numbers."
        };

    private static object BuildJobSImageRawReplaceResultPayload(JobSImageReplaceResult result)
        => new
        {
            result.Request.JobId,
            result.Request.MaterialFolder,
            FactionSlots = result.Request.FactionSlots,
            result.TotalOperationCount,
            result.Warnings,
            Factions = result.Factions.Select(faction => new
            {
                faction.FactionSlot,
                faction.FactionName,
                Mapping = faction.Result.Mapping,
                faction.Result.TotalOperationCount,
                faction.Result.AggregateReportPath,
                Files = faction.Result.Files.Select(file => new
                {
                    file.ActionName,
                    file.TargetFileName,
                    file.TargetPath,
                    file.SourcePath,
                    Encode = BuildE5TrueColorEncodePayload(file.Encode),
                    WriteResult = BuildE5ImageBatchReplacePayload(file.WriteResult),
                    file.WriteResult.BackupPath,
                    file.WriteResult.ReportPath,
                    file.WriteResult.ReportJsonPath
                })
            })
        };

    private static BmpExportKind ParseBmpExportKind(string kind)
    {
        var normalized = (kind ?? string.Empty)
            .Trim()
            .Replace("-", "_", StringComparison.Ordinal)
            .ToLowerInvariant();
        return normalized switch
        {
            "job_s_image" or "jobsimage" or "job_s" => BmpExportKind.JobSImage,
            "r_image" or "rimage" or "r" => BmpExportKind.RImage,
            "s_image" or "simage" or "s" => BmpExportKind.SImage,
            "face" or "role_face" or "roleface" => BmpExportKind.Face,
            "item_icon" or "itemicon" or "item" => BmpExportKind.ItemIcon,
            "strategy_icon" or "strategyicon" or "strategy" => BmpExportKind.StrategyIcon,
            _ => throw new InvalidOperationException(
                "Unsupported BMP export kind. Use job_s_image, r_image, s_image, face, item_icon, or strategy_icon.")
        };
    }

    private static IReadOnlyList<BmpExportTarget> BuildBmpExportTargets(IReadOnlyList<BmpExportTargetUpdate>? updates)
        => updates == null
            ? Array.Empty<BmpExportTarget>()
            : updates.Select(update => new BmpExportTarget
            {
                RowId = update.RowId,
                DisplayName = update.DisplayName,
                FieldValue = update.FieldValue,
                JobId = update.JobId
            }).ToArray();

    private static IReadOnlyList<BatchSImageUsage> BuildBatchSImageUsages(IReadOnlyList<BatchSImageUsageUpdate>? updates)
        => updates == null
            ? Array.Empty<BatchSImageUsage>()
            : updates.Select(update => new BatchSImageUsage(update.SImageId, update.JobId, update.FactionSlot)).ToArray();

    private static IReadOnlyList<BatchItemIconTargetRow> BuildBatchItemIconTargetRows(IReadOnlyList<BatchItemIconTargetRowUpdate>? updates)
        => updates == null
            ? Array.Empty<BatchItemIconTargetRow>()
            : updates.Select(update => new BatchItemIconTargetRow(update.RowId, update.DisplayName, update.IconIndex)).ToArray();

    private static IReadOnlyList<BatchStrategyIconTargetRow> BuildBatchStrategyIconTargetRows(IReadOnlyList<BatchStrategyIconTargetRowUpdate>? updates)
        => updates == null
            ? Array.Empty<BatchStrategyIconTargetRow>()
            : updates.Select(update => new BatchStrategyIconTargetRow(update.RowId, update.DisplayName, update.IconIndex)).ToArray();

    private static IReadOnlyList<BatchRoleFaceTargetRow> BuildBatchRoleFaceTargetRows(IReadOnlyList<BatchRoleFaceTargetRowUpdate>? updates)
        => updates == null
            ? Array.Empty<BatchRoleFaceTargetRow>()
            : updates.Select(update => new BatchRoleFaceTargetRow(update.RowId, update.DisplayName, update.FaceId)).ToArray();

    private static object BuildBmpExportPayload(BmpExportResult result)
        => new
        {
            result.Request.Kind,
            result.Request.OutputRoot,
            result.Request.SingleMode,
            result.Request.OverwriteExisting,
            result.Request.FactionSlot,
            TargetCount = result.Request.Targets.Count,
            FileCount = result.Files.Count,
            SkippedCount = result.SkippedItems.Count,
            result.Warnings,
            Files = result.Files.Select(file => new
            {
                file.Kind,
                file.RowId,
                file.FieldValue,
                file.DisplayName,
                file.Role,
                file.SourcePath,
                file.SourceRelativePath,
                file.ImageNumber,
                file.ResourceId,
                file.Width,
                file.Height,
                file.OutputPath
            }),
            SkippedItems = result.SkippedItems.Select(item => new
            {
                item.Kind,
                item.RowId,
                item.FieldValue,
                item.DisplayName,
                item.OutputPath,
                item.Reason
            }),
            SafetyNote = "Exports importable BMP material files only. No game resources are modified and no JSON report is written."
        };

    private static object BuildBatchRImageRawReplacePreviewPayload(BatchRImageReplacePreviewResult preview)
        => new
        {
            preview.Request.MaterialRoot,
            AllowedRImageIds = preview.Request.AllowedRImageIds,
            preview.Request.IncludeOnlySelectedOrFiltered,
            preview.TotalOperationCount,
            preview.CanWrite,
            preview.Warnings,
            preview.SkippedItems,
            Items = preview.Items.Select(item => new
            {
                item.RImageId,
                item.MaterialFolder,
                item.FrontImageNumber,
                item.BackImageNumber,
                item.FrontSourcePath,
                item.BackSourcePath,
                FrontEncode = BuildE5TrueColorEncodePayload(item.FrontEncode),
                BackEncode = BuildE5TrueColorEncodePayload(item.BackEncode)
            }),
            BatchPreview = preview.BatchPreview == null ? null : BuildE5ImageBatchReplacePayload(preview.BatchPreview),
            SafetyNote = "Preview only. Batch R image true-color replacement writes Pmapobj.e5 once with backup and reread verification."
        };

    private static object BuildBatchRImageRawReplaceResultPayload(BatchRImageReplaceResult result)
        => new
        {
            Preview = BuildBatchRImageRawReplacePreviewPayload(result),
            result.AggregateReportPath,
            WriteResult = result.WriteResult == null ? null : BuildE5ImageBatchReplacePayload(result.WriteResult),
            result.WriteResult?.BackupPath,
            result.WriteResult?.ReportPath,
            result.WriteResult?.ReportJsonPath
        };

    private static object BuildBatchSImageRawReplacePreviewPayload(BatchSImageReplacePreviewResult preview)
        => new
        {
            preview.Request.MaterialRoot,
            preview.Request.IncludeOnlySelectedOrFiltered,
            preview.Request.FactionSlot,
            preview.TotalOperationCount,
            preview.CanWrite,
            preview.Warnings,
            preview.SkippedItems,
            Items = preview.Items.Select(item => new
            {
                item.SImageId,
                item.JobId,
                item.FactionSlot,
                item.MaterialFolder,
                item.ImageNumbers,
                item.MappingDetail,
                item.MovSourcePath,
                item.AtkSourcePath,
                item.SpcSourcePath,
                MovEncode = BuildE5TrueColorEncodePayload(item.MovEncode),
                AtkEncode = BuildE5TrueColorEncodePayload(item.AtkEncode),
                SpcEncode = BuildE5TrueColorEncodePayload(item.SpcEncode)
            }),
            FilePreviews = preview.FilePreviews.ToDictionary(pair => pair.Key, pair => BuildE5ImageBatchReplacePayload(pair.Value)),
            SafetyNote = "Preview only. Batch S image true-color replacement writes Unit_mov.e5, Unit_atk.e5, and Unit_spc.e5 in grouped batches."
        };

    private static object BuildBatchSImageRawReplaceResultPayload(BatchSImageReplaceResult result)
        => new
        {
            Preview = BuildBatchSImageRawReplacePreviewPayload(result),
            result.AggregateReportPath,
            WriteResults = result.WriteResults.ToDictionary(pair => pair.Key, pair => new
            {
                Preview = BuildE5ImageBatchReplacePayload(pair.Value),
                pair.Value.BackupPath,
                pair.Value.ReportPath,
                pair.Value.ReportJsonPath
            })
        };

    private static object BuildBatchJobSImageRawReplacePreviewPayload(BatchJobSImageReplacePreviewResult preview)
        => new
        {
            preview.Request.MaterialRoot,
            AllowedJobIds = preview.Request.AllowedJobIds,
            preview.Request.IncludeOnlySelectedOrFiltered,
            preview.Request.FactionSlots,
            preview.TotalOperationCount,
            preview.CanWrite,
            preview.Warnings,
            preview.SkippedItems,
            Items = preview.Items.Select(item => new
            {
                item.JobId,
                item.MaterialFolder,
                Preview = BuildJobSImageRawReplacePreviewPayload(item.Preview)
            }),
            SafetyNote = "Preview only. Batch job S image true-color replacement consumes Job{jobId}/mov.bmp, atk.bmp, and spc.bmp folders."
        };

    private static object BuildBatchJobSImageRawReplaceResultPayload(BatchJobSImageReplaceResult result)
        => new
        {
            Preview = BuildBatchJobSImageRawReplacePreviewPayload(result),
            Results = result.Results.Select(item => new
            {
                item.JobId,
                item.MaterialFolder,
                Result = BuildJobSImageRawReplaceResultPayload(item.Result)
            })
        };

    private static object BuildBatchItemIconImportPreviewPayload(BatchItemIconImportPreviewResult preview)
        => new
        {
            preview.TargetPath,
            preview.TargetRelativePath,
            preview.ResourceKind,
            preview.TotalOperationCount,
            preview.CanWrite,
            preview.Warnings,
            preview.SkippedItems,
            Items = preview.Items.Select(item => new
            {
                item.RowId,
                item.DisplayName,
                item.IconIndex,
                item.SourcePath,
                item.TargetImageNumbers,
                item.ResourceIds
            }),
            DllPreview = preview.DllPreview == null ? null : new
            {
                preview.DllPreview.TargetPath,
                preview.DllPreview.TargetRelativePath,
                preview.DllPreview.OperationKind,
                preview.DllPreview.OldFileSizeBytes,
                preview.DllPreview.OldFileSha256,
                preview.DllPreview.ResourceFormat,
                preview.DllPreview.FormatWarnings,
                preview.DllPreview.RiskSummary,
                Items = preview.DllPreview.Items
            },
            E5Preview = preview.E5Preview == null ? null : BuildE5ImageBatchReplacePayload(preview.E5Preview),
            SafetyNote = "Preview only. Batch item icon import writes Itemicon.dll on 6.5 or E5/Item.e5 on 6.6; item table icon fields are not changed."
        };

    private static object BuildBatchItemIconImportResultPayload(BatchItemIconImportResult result)
        => new
        {
            Preview = BuildBatchItemIconImportPreviewPayload(result),
            result.AggregateReportPath,
            DllResult = result.DllResult == null ? null : new
            {
                result.DllResult.BackupPath,
                result.DllResult.ReportPath,
                result.DllResult.ReportJsonPath,
                result.DllResult.NewFileSizeBytes,
                result.DllResult.ChangedBytesEstimate,
                result.DllResult.NewFileSha256
            },
            E5Result = result.E5Result == null ? null : new
            {
                Preview = BuildE5ImageBatchReplacePayload(result.E5Result),
                result.E5Result.BackupPath,
                result.E5Result.ReportPath,
                result.E5Result.ReportJsonPath
            }
        };

    private static object BuildBatchStrategyIconImportPreviewPayload(BatchStrategyIconImportPreviewResult preview)
        => new
        {
            preview.TargetPath,
            preview.TargetRelativePath,
            preview.ResourceKind,
            preview.TotalOperationCount,
            preview.CanWrite,
            preview.Warnings,
            preview.SkippedItems,
            Items = preview.Items.Select(item => new
            {
                item.RowId,
                item.DisplayName,
                item.IconIndex,
                item.SourcePath,
                item.TargetImageNumbers,
                item.ResourceIds
            }),
            DllPreview = preview.DllPreview == null ? null : new
            {
                preview.DllPreview.TargetPath,
                preview.DllPreview.TargetRelativePath,
                preview.DllPreview.OperationKind,
                preview.DllPreview.OldFileSizeBytes,
                preview.DllPreview.OldFileSha256,
                preview.DllPreview.ResourceFormat,
                preview.DllPreview.FormatWarnings,
                preview.DllPreview.RiskSummary,
                Items = preview.DllPreview.Items
            },
            E5Preview = preview.E5Preview == null ? null : BuildE5ImageBatchReplacePayload(preview.E5Preview),
            SafetyNote = "Preview only. Batch strategy icon import writes Mgcicon.dll on 6.5 or E5/Mtem.e5 on 6.6."
        };

    private static object BuildBatchStrategyIconImportResultPayload(BatchStrategyIconImportResult result)
        => new
        {
            Preview = BuildBatchStrategyIconImportPreviewPayload(result),
            DllResult = result.DllResult == null ? null : new
            {
                result.DllResult.BackupPath,
                result.DllResult.ReportPath,
                result.DllResult.ReportJsonPath,
                result.DllResult.NewFileSizeBytes,
                result.DllResult.ChangedBytesEstimate,
                result.DllResult.NewFileSha256
            },
            E5Result = result.E5Result == null ? null : new
            {
                Preview = BuildE5ImageBatchReplacePayload(result.E5Result),
                result.E5Result.BackupPath,
                result.E5Result.ReportPath,
                result.E5Result.ReportJsonPath
            }
        };

    private static object BuildBatchRoleFaceImportPreviewPayload(BatchRoleFaceImportPreviewResult preview)
        => new
        {
            preview.TargetPath,
            preview.TargetRelativePath,
            preview.TotalOperationCount,
            preview.CanWrite,
            preview.Warnings,
            preview.SkippedItems,
            Items = preview.Items.Select(item => new
            {
                item.RowId,
                item.DisplayName,
                item.FaceId,
                item.SourcePath,
                item.SourceKind,
                item.SourceWidth,
                item.SourceHeight,
                item.OutputKind,
                item.OutputWidth,
                item.OutputHeight,
                item.FormatRequirement,
                item.TargetImageNumbers,
                item.TrueColorResourceIds
            }),
            E5Preview = preview.E5Preview == null ? null : BuildE5ImageBatchReplacePayload(preview.E5Preview),
            SafetyNote = "Preview only. Batch role face import writes Face.e5 entries that match the current face import pipeline."
        };

    private static object BuildBatchRoleFaceImportResultPayload(BatchRoleFaceImportResult result)
        => new
        {
            Preview = BuildBatchRoleFaceImportPreviewPayload(result),
            result.AggregateReportPath,
            E5Result = result.E5Result == null ? null : new
            {
                Preview = BuildE5ImageBatchReplacePayload(result.E5Result),
                result.E5Result.BackupPath,
                result.E5Result.ReportPath,
                result.E5Result.ReportJsonPath
            }
        };

    private static object BuildE5RoleRawNormalizePreviewPayload(E5RoleRawNormalizePreviewResult preview)
        => new
        {
            preview.ConvertCount,
            preview.SkipCount,
            preview.Warnings,
            Files = preview.Files.Select(file => new
            {
                file.TargetFileName,
                file.TargetPath,
                file.ConvertCount,
                file.SkipCount,
                Entries = file.Entries.Select(BuildE5RoleRawNormalizeEntryPayload),
                BatchPreview = file.BatchPreview == null ? null : BuildE5ImageBatchReplacePayload(file.BatchPreview)
            }),
            SafetyNote = "Preview only. normalize_e5_role_raw converts only standard image entries that match role-image RAW dimensions; compressed and unknown entries are skipped."
        };

    private static object BuildE5RoleRawNormalizeResultPayload(E5RoleRawNormalizeResult result)
        => new
        {
            result.ConvertCount,
            result.SkipCount,
            result.Warnings,
            result.AggregateReportPath,
            Files = result.Files.Select(file => new
            {
                file.TargetFileName,
                file.TargetPath,
                file.ConvertCount,
                file.SkipCount,
                Entries = file.Entries.Select(BuildE5RoleRawNormalizeEntryPayload),
                WriteResult = file.WriteResult == null
                    ? null
                    : new
                    {
                        Preview = BuildE5ImageBatchReplacePayload(file.WriteResult),
                        file.WriteResult.BackupPath,
                        file.WriteResult.ReportPath,
                        file.WriteResult.ReportJsonPath
                    }
            })
        };

    private static object BuildE5RoleRawNormalizeEntryPayload(E5RoleRawNormalizeEntryPreview entry)
        => new
        {
            entry.TargetFileName,
            entry.TargetPath,
            entry.ImageNumber,
            entry.OldKind,
            entry.OldSizeBytes,
            entry.Status,
            entry.Reason,
            Encode = BuildE5RawEncodePayload(entry.Encode)
        };

    private static object? BuildE5RawEncodePayload(E5RawEncodeResult? encode)
        => encode == null
            ? null
            : new
            {
                encode.SourcePath,
                encode.TargetFileName,
                encode.SourceWidth,
                encode.SourceHeight,
                encode.RawLength,
                encode.TransparentPixels,
                encode.ExactPalettePixels,
                encode.NearestPalettePixels,
                encode.PalettePath,
                encode.Warnings
            };

    private static object? BuildE5TrueColorEncodePayload(E5TrueColorEncodeResult? encode)
        => encode == null
            ? null
            : new
            {
                encode.SourcePath,
                encode.TargetFileName,
                encode.SourceWidth,
                encode.SourceHeight,
                encode.NormalizedWidth,
                encode.NormalizedHeight,
                encode.StorageFormat,
                encode.ColorDepth,
                encode.ImageLength,
                encode.TransparentPixels,
                encode.MagentaKeyPixels,
                encode.Quantization,
                encode.Warnings
            };
}
