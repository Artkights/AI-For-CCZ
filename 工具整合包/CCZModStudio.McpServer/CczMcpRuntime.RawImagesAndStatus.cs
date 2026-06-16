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
                Encode = BuildE5RawEncodePayload(file.Encode)
            }),
            BatchPreview = BuildE5ImageBatchReplacePayload(preview.BatchPreview),
            SafetyNote = "Preview only. R image RAW replacement writes Pmapobj.e5 through replace_r_image_raw with backup and reread verification."
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
                Encode = BuildE5RawEncodePayload(file.Encode)
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
                Encode = BuildE5RawEncodePayload(file.Encode),
                BatchPreview = BuildE5ImageBatchReplacePayload(file.BatchPreview)
            }),
            SafetyNote = "Preview only. S image RAW replacement writes Unit_mov.e5, Unit_atk.e5, and/or Unit_spc.e5 through replace_s_image_raw with backups and reread verification."
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
                Encode = BuildE5RawEncodePayload(file.Encode),
                WriteResult = BuildE5ImageBatchReplacePayload(file.WriteResult),
                file.WriteResult.BackupPath,
                file.WriteResult.ReportPath,
                file.WriteResult.ReportJsonPath
            })
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
}
