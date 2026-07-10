using System.Data;
using System.Drawing.Imaging;
using System.Globalization;
using System.Text.Json;
using CCZModStudio.Core;
using CCZModStudio.Formats;
using CCZModStudio.Models;

namespace CCZModStudio.McpServer;

public sealed partial class CczMcpRuntime
{
    private readonly MapDraftService _mapDraftService = new();
    private readonly MapResourceIndexer _mapResourceIndexer = new();
    private readonly MapCanvasComposeService _mapCanvasComposeService = new();
    private readonly MapCanvasPublishService _mapCanvasPublishService = new();
    private readonly MaterialLibraryMigrationService _materialLibraryMigrationService = new();
    private readonly MaterialDrivenTerrainService _materialDrivenTerrainService = new();
    private readonly TerrainDrivenMapGenerationService _terrainDrivenMapGenerationService = new();
    private readonly TerrainMapBeautifyService _terrainMapBeautifyService = new();
    private readonly ShopEditorService _shopEditorService = new();
    private readonly ShopRuntimeDiagnosticService _shopRuntimeDiagnosticService = new();
    private readonly GlobalSettingsService _globalSettingsService = new();
    private readonly CmSettingsService _cmSettingsService = new();
    private readonly AbilityTierPatchService _abilityTierPatchService = new();
    private readonly ScenarioTextImportService _scenarioTextImportService = new();
    private readonly ScenarioTextExportService _scenarioTextExportService = new();
    private readonly ScriptVariableUsageService _scriptVariableUsageService = new();
    private readonly ScriptVariableValueResolver _scriptVariableValueResolver = new();
    private readonly RSceneDraftService _rSceneDraftService = new();
    private readonly BattlefieldDeploymentWriteService _battlefieldDeploymentWriteService = new();
    private readonly EexArchiveReader _eexArchiveReader = new();
    private readonly EexEntryProbeReader _eexEntryProbeReader = new();
    private readonly EexEntryTreeDetailService _eexEntryTreeDetailService = new();
    private readonly EexCrossFileComparisonService _eexCrossFileComparisonService = new();
    private readonly FieldAnnotationService _fieldAnnotationService = new();
    private readonly ItemEffectCatalogService _itemEffectCatalogService = new();
    private readonly ItemEffectNameReader _itemEffectNameReader = new();
    private readonly Ccz66ItemEffectNameService _ccz66ItemEffectNameService = new();
    private readonly ProjectEquipmentTypeProfileService _equipmentTypeProfileService = new();
    private readonly AccessoryJobGroupService _accessoryJobGroupService = new();
    private readonly AttackAreaPreviewService _attackAreaPreviewService = new();
    private readonly StrategyAnimationPreviewService _strategyAnimationPreviewService = new();
    private readonly Qinger66DiagnosticsService _qinger66DiagnosticsService = new();

    public object ListMapDrafts(string? gameRoot, int limit)
    {
        var project = LoadProject(gameRoot);
        var drafts = _mapDraftService.ListDrafts(project)
            .Take(NormalizeLimit(limit, 100, 1000))
            .Select(draft => new
            {
                draft.DraftId,
                draft.BoundMapId,
                draft.GridWidth,
                draft.GridHeight,
                draft.TileSize,
                draft.PixelWidth,
                draft.PixelHeight,
                draft.BaseLayerPath,
                draft.MaterialRoot,
                TerrainBaseCellCount = draft.TerrainBaseCells.Count,
                GeneratedMapCellCount = draft.GeneratedMapCells.Count,
                BuildingOverlayCellCount = draft.BuildingOverlayCells.Count,
                SceneryOverlayCellCount = draft.SceneryOverlayCells.Count,
                ManualOverrideCellCount = draft.MapCellOverrides.Count,
                draft.AutoGenerateMapFromTerrain,
                draft.BeautifyGeneratedMap,
                draft.CreatedAtText,
                draft.UpdatedAtText
            })
            .ToList();

        return new
        {
            project.GameRoot,
            DraftRoot = _mapDraftService.GetDraftStoreRoot(project),
            Count = drafts.Count,
            Drafts = drafts
        };
    }

    public object ReadMapDraft(string? gameRoot, string draftId)
    {
        var project = LoadProject(gameRoot);
        var draft = _mapDraftService.LoadDraft(project, draftId);
        return new
        {
            project.GameRoot,
            DraftRoot = _mapDraftService.GetDraftStoreRoot(project),
            Draft = draft,
            MissingAssets = _mapDraftService.FindMissingAssets(draft)
        };
    }

    public object SaveMapDraft(string? gameRoot, MapDraftSaveRequest request)
    {
        if (request == null) throw new InvalidOperationException("request is required.");

        var project = LoadProject(gameRoot);
        var draft = BuildMapDraft(project, request);
        _mapDraftService.SaveDraft(project, draft);
        var reread = _mapDraftService.LoadDraft(project, draft.DraftId);
        return new
        {
            project.GameRoot,
            DraftPath = Path.Combine(_mapDraftService.GetDraftStoreRoot(project), MakeSafeFileNameForMcp(draft.DraftId) + ".json"),
            Draft = BuildMapDraftSummary(reread),
            MissingAssets = _mapDraftService.FindMissingAssets(reread),
            SafetyNote = "Saved only a map workbench draft JSON under CCZModStudio_Notes; no game resource was modified."
        };
    }

    public object PreviewMapCanvas(string? gameRoot, string draftId, bool showTerrain, bool showGrid, int terrainOpacityPercent)
    {
        var project = LoadProject(gameRoot);
        var draft = _mapDraftService.LoadDraft(project, draftId);
        var materials = LoadMaterialsForDraft(project, draft);
        using var bitmap = _mapCanvasComposeService.ComposePreview(draft, materials, showTerrain, showGrid, terrainOpacityPercent);
        var outputPath = BuildExportPath(project, "MapWorkbench", $"{draft.DraftId}_preview.png");
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
        bitmap.Save(outputPath, ImageFormat.Png);
        return BuildImageExportPayload(project, draft, outputPath, bitmap.Width, bitmap.Height, "Preview export writes only CCZModStudio_Exports/MapWorkbench.");
    }

    public object ExportMapCanvasJpeg(string? gameRoot, string draftId, string? outputPath)
    {
        var project = LoadProject(gameRoot);
        var draft = _mapDraftService.LoadDraft(project, draftId);
        var materials = LoadMaterialsForDraft(project, draft);
        var targetPath = string.IsNullOrWhiteSpace(outputPath)
            ? BuildExportPath(project, "MapWorkbench", $"{draft.DraftId}_canvas.jpg")
            : ResolveExportOrExternalOutput(project, outputPath!);
        _mapCanvasPublishService.ExportJpeg(draft, materials, targetPath);
        using var image = System.Drawing.Image.FromFile(targetPath);
        return BuildImageExportPayload(project, draft, targetPath, image.Width, image.Height, "JPEG export writes only the requested/export path; no project Map file was modified.");
    }

    public object PublishMapCanvasToMapImage(string? gameRoot, string draftId, string mapId, string? writeMode)
    {
        var project = LoadProject(gameRoot);
        EnsureWriteMode(project, writeMode);
        var draft = _mapDraftService.LoadDraft(project, draftId);
        var target = ResolveMapResource(project, string.IsNullOrWhiteSpace(mapId) ? draft.BoundMapId : mapId);
        var materials = LoadMaterialsForDraft(project, draft);
        var result = _mapCanvasPublishService.PublishToMapImage(project, draft, target, materials);
        return new
        {
            project.GameRoot,
            Target = BuildMapResourcePayload(project, target),
            Result = result,
            SafetyNote = "Publish writes a Map/*.jpg only through MapCanvasPublishService with backup, report, and reread validation."
        };
    }

    public object PublishMapWorkbenchBundle(string? gameRoot, string draftId, string? mapId)
    {
        var project = LoadProject(gameRoot);
        EnsureWriteMode(project, "direct");
        var draft = _mapDraftService.LoadDraft(project, draftId);
        var target = ResolveMapResource(project, string.IsNullOrWhiteSpace(mapId) ? draft.BoundMapId : mapId);
        var materials = LoadMaterialsForDraft(project, draft);
        var mapResult = _mapCanvasPublishService.PublishToMapImage(project, draft, target, materials);

        var terrainLookup = BuildTerrainNameLookup(project);
        var probe = _hexzmapProbeReader.Read(project, terrainLookup);
        var block = probe.Blocks.FirstOrDefault(x => x.MapId.Equals(target.Id, StringComparison.OrdinalIgnoreCase) ||
                                                     x.MapId.Equals("M" + target.Id, StringComparison.OrdinalIgnoreCase))
                    ?? throw new InvalidOperationException($"Hexzmap block for map {target.Id} was not found.");
        if (draft.TerrainCells.Length != block.Width * block.Height)
        {
            throw new InvalidOperationException($"Draft terrain cell count {draft.TerrainCells.Length} does not match Hexzmap block {block.MapId} size {block.Width}x{block.Height}.");
        }

        var currentCells = _hexzmapProbeReader.GetBlockCells(probe, block);
        object? terrainResult;
        if (currentCells.SequenceEqual(draft.TerrainCells))
        {
            terrainResult = new
            {
                Skipped = true,
                block.MapId,
                Reason = "Hexzmap terrain cells already match the draft."
            };
        }
        else
        {
            terrainResult = _hexzmapEditor.SaveBlock(project, probe, block, draft.TerrainCells, terrainLookup);
        }

        return new
        {
            project.GameRoot,
            Draft = BuildMapDraftSummary(draft),
            Target = BuildMapResourcePayload(project, target),
            MapImageResult = mapResult,
            TerrainResult = terrainResult,
            SafetyNote = "Published Map/Mxxx.jpg and the matching Hexzmap.e5 terrain block as one direct-write bundle with independent backups, reports, and reread validation."
        };
    }

    public object ListMaterialAssets(string? gameRoot, string? materialRoot, string? assetType, string? keyword, int limit)
    {
        var project = LoadProject(gameRoot);
        var root = ResolveMaterialRootForRead(project, materialRoot);
        var materials = string.IsNullOrWhiteSpace(root)
            ? Array.Empty<MaterialAsset>()
            : _materialLibraryIndexer.IndexExplicitRoot(root);
        var effectiveLimit = NormalizeLimit(limit, 200, 5000);
        var filtered = materials
            .Where(asset => string.IsNullOrWhiteSpace(assetType) || asset.AssetType.Equals(assetType, StringComparison.OrdinalIgnoreCase) || asset.Category.Contains(assetType, StringComparison.OrdinalIgnoreCase))
            .Where(asset => MatchesMaterialKeyword(asset, keyword))
            .ToList();

        return new
        {
            project.GameRoot,
            MaterialRoot = root,
            TotalAssets = filtered.Count,
            ReturnedAssets = Math.Min(filtered.Count, effectiveLimit),
            ByType = filtered.GroupBy(asset => asset.AssetType).Select(group => new { Type = group.Key, Count = group.Count() }),
            Assets = filtered.Take(effectiveLimit).Select(asset => BuildMaterialAssetPayload(project, root, asset))
        };
    }

    public object MigrateMaterialLibraryPreview(string? gameRoot, string oldRoot, string? newRoot, string? hexTexPath)
    {
        var project = LoadProject(gameRoot);
        var sourceRoot = ResolveExternalDirectory(project, oldRoot);
        var targetRoot = string.IsNullOrWhiteSpace(newRoot)
            ? Path.Combine(project.WorkspaceRoot, "CCZModStudio_Exports", "MaterialLibraryMigration", MakeSafeFileNameForMcp(Path.GetFileName(sourceRoot)))
            : ResolveExportOrExternalDirectory(project, newRoot!);
        var sourceAssets = _materialLibraryIndexer.IndexExplicitRoot(sourceRoot);
        return new
        {
            project.GameRoot,
            OldRoot = sourceRoot,
            NewRoot = targetRoot,
            HexTexPath = string.IsNullOrWhiteSpace(hexTexPath) ? Path.Combine(sourceRoot, "HexTex.txt") : ResolveOptionalExternalFile(project, hexTexPath!),
            ExistingOutput = Directory.Exists(targetRoot),
            ExistingOutputFileCount = Directory.Exists(targetRoot) ? Directory.GetFiles(targetRoot, "*", SearchOption.AllDirectories).Length : 0,
            SourceAssetCount = sourceAssets.Count,
            ByType = sourceAssets.GroupBy(asset => asset.AssetType).Select(group => new { Type = group.Key, Count = group.Count() }),
            SafetyNote = "Preview only inspects the source and planned output. It does not copy files or create directories."
        };
    }

    public object MigrateMaterialLibrary(string? gameRoot, string oldRoot, string newRoot, string? hexTexPath, bool overwrite, string? writeMode)
    {
        var project = LoadProject(gameRoot);
        EnsureWriteMode(project, writeMode);
        var result = _materialLibraryMigrationService.Migrate(
            ResolveExternalDirectory(project, oldRoot),
            ResolveExportOrExternalDirectory(project, newRoot),
            string.IsNullOrWhiteSpace(hexTexPath) ? null : ResolveOptionalExternalFile(project, hexTexPath!),
            overwrite);

        return new
        {
            project.GameRoot,
            Result = result,
            SafetyNote = "Material migration writes only the requested material-library output directory; it does not modify game resources."
        };
    }

    public object AnalyzeMaterialDrivenTerrain(string? gameRoot, string draftId)
    {
        var project = LoadProject(gameRoot);
        var draft = _mapDraftService.LoadDraft(project, draftId);
        var materials = LoadMaterialsForDraft(project, draft);
        return new
        {
            project.GameRoot,
            Draft = BuildMapDraftSummary(draft),
            Diagnostics = _materialDrivenTerrainService.Analyze(draft, materials)
        };
    }

    public object DeriveMaterialTerrainCells(string? gameRoot, string draftId, bool saveDraft, string? writeMode)
    {
        var project = LoadProject(gameRoot);
        if (saveDraft) EnsureWriteMode(project, writeMode);
        var draft = _mapDraftService.LoadDraft(project, draftId);
        var materials = LoadMaterialsForDraft(project, draft);
        var cells = _materialDrivenTerrainService.DeriveTerrainCells(draft, materials);
        if (saveDraft)
        {
            draft.TerrainCells = cells;
            _mapDraftService.SaveDraft(project, draft);
        }

        return new
        {
            project.GameRoot,
            Draft = BuildMapDraftSummary(draft),
            Saved = saveDraft,
            TerrainCounts = BuildTerrainCounts(cells),
            Cells = cells.Select(value => (int)value).ToArray(),
            SafetyNote = saveDraft ? "Saved only the draft JSON; Hexzmap.e5 was not modified." : "Preview only; no files were modified."
        };
    }

    public object GenerateTerrainDrivenMap(string? gameRoot, string draftId, bool saveDraft, string? writeMode)
    {
        var project = LoadProject(gameRoot);
        if (saveDraft) EnsureWriteMode(project, writeMode);
        var draft = _mapDraftService.LoadDraft(project, draftId);
        var materials = LoadMaterialsForDraft(project, draft);
        var diagnostics = _terrainDrivenMapGenerationService.Analyze(draft, materials);
        _terrainDrivenMapGenerationService.EnsureMaterialPlan(draft, materials);
        var generated = _terrainDrivenMapGenerationService.GenerateMapCells(draft, materials).ToList();
        if (saveDraft)
        {
            draft.GeneratedMapCells = generated;
            _mapDraftService.SaveDraft(project, draft);
        }

        using var bitmap = _terrainDrivenMapGenerationService.RenderBaseTerrain(draft, materials);
        var outputPath = BuildExportPath(project, "MapWorkbench", $"{draft.DraftId}_terrain_generated.png");
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
        bitmap.Save(outputPath, ImageFormat.Png);

        return new
        {
            project.GameRoot,
            Draft = BuildMapDraftSummary(draft),
            Diagnostics = diagnostics,
            GeneratedCellCount = generated.Count,
            Saved = saveDraft,
            PreviewPath = outputPath,
            PreviewWidth = bitmap.Width,
            PreviewHeight = bitmap.Height,
            MaterialPlan = draft.TerrainMaterialPlan,
            SafetyNote = saveDraft ? "Saved only generated cells in the draft JSON; game resources were not modified." : "Preview only; no draft or game resources were modified."
        };
    }

    public object BeautifyTerrainMapPreview(string? gameRoot, string draftId)
    {
        var project = LoadProject(gameRoot);
        var draft = _mapDraftService.LoadDraft(project, draftId);
        var materials = LoadMaterialsForDraft(project, draft);
        using var baseTerrain = _terrainDrivenMapGenerationService.RenderBaseTerrain(draft, materials);
        using var beautified = _terrainMapBeautifyService.Beautify(draft, baseTerrain);
        var outputPath = BuildExportPath(project, "MapWorkbench", $"{draft.DraftId}_terrain_beautified.png");
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
        beautified.Save(outputPath, ImageFormat.Png);
        return BuildImageExportPayload(project, draft, outputPath, beautified.Width, beautified.Height, "Preview export writes only CCZModStudio_Exports/MapWorkbench.");
    }

    public object ReadShopEditor(string? gameRoot, string? keyword, int limit)
    {
        var project = LoadProject(gameRoot);
        var tables = LoadTables(project);
        var build = _shopEditorService.Build(project, tables);
        var columns = build.Data.Columns.Cast<DataColumn>().ToArray();
        var effectiveLimit = NormalizeLimit(limit, 80, 500);
        var rows = build.Data.Rows.Cast<DataRow>()
            .Where(row => MatchesKeyword(row, columns, keyword))
            .Take(effectiveLimit)
            .Select(row => RowToDictionary(row, columns))
            .ToList();
        return new
        {
            project.GameRoot,
            Summary = _shopEditorService.BuildSummary(build.Data),
            TotalRows = build.Data.Rows.Count,
            ReturnedRows = rows.Count,
            Columns = columns.Select(column => new { column.ColumnName, Type = column.DataType.Name, column.ReadOnly }),
            Rows = rows,
            ItemCount = build.ItemInfos.Count,
            SafetyNote = "Read-only shop editor view. Use preview_write_shop_rows before write_shop_rows."
        };
    }

    public object DiagnoseShopRuntime(string? gameRoot, int limit)
    {
        var project = LoadProject(gameRoot);
        var tables = LoadTables(project);
        return _shopRuntimeDiagnosticService.Diagnose(project, tables, NormalizeLimit(limit, 120, 1000));
    }

    public object PreviewWriteShopRows(string? gameRoot, List<ShopRowUpdate> updates)
    {
        var project = LoadProject(gameRoot);
        var tables = LoadTables(project);
        var build = _shopEditorService.Build(project, tables);
        var preview = ApplyShopUpdates(build, updates, mutate: true);
        _shopEditorService.EnsureShopDataTableValidForSave(
            project,
            tables,
            build.ShopDataRead.Table,
            build.ShopDataRead.Data,
            changedItemSlotsOnly: true);
        return new
        {
            project.GameRoot,
            Preview = preview,
            SafetyNote = "Preview only; no game files were modified."
        };
    }

    public object WriteShopRows(string? gameRoot, List<ShopRowUpdate> updates, string? writeMode)
    {
        var project = LoadProject(gameRoot);
        EnsureWriteMode(project, writeMode);
        var tables = LoadTables(project);
        var build = _shopEditorService.Build(project, tables);
        var preview = ApplyShopUpdates(build, updates, mutate: true);
        _shopEditorService.EnsureShopDataTableValidForSave(
            project,
            tables,
            build.ShopDataRead.Table,
            build.ShopDataRead.Data,
            changedItemSlotsOnly: true);
        var saves = new List<TableSaveResult>();
        if (build.CampaignNameRead.Data.GetChanges() != null)
        {
            saves.Add(_tableWriter.Save(project, build.CampaignNameRead.Table, build.CampaignNameRead.Data));
        }

        if (build.ShopDataRead.Data.GetChanges() != null)
        {
            saves.Add(_tableWriter.Save(project, build.ShopDataRead.Table, build.ShopDataRead.Data));
        }

        return new
        {
            project.GameRoot,
            Preview = preview,
            SaveCount = saves.Count,
            Saves = saves.Select(BuildTableSavePayload),
            SafetyNote = "Shop writes use HexTableWriter backups/reports for campaign-name and shop-data tables."
        };
    }

    public object ReadGlobalSettings(string? gameRoot, int limit)
    {
        var project = LoadProject(gameRoot);
        var tables = LoadTables(project);
        var document = _globalSettingsService.Load(project, tables);
        var oracle = _imageAssignerOracleService.Compare(project, tables, includeGlobalCandidates: true);
        return new
        {
            project.GameRoot,
            ImageAssignerOracle = oracle,
            document.GameTitle,
            NumericSettings = document.NumericSettings,
            NumericDefinitions = document.NumericDefinitions,
            CmfCandidates = document.CmfCandidates,
            Evidence = document.Evidence,
            JobSeriesNames = DataTableRows(document.JobSeriesNames, NormalizeLimit(limit, 80, 500)),
            DetailedJobNames = DataTableRows(document.DetailedJobNames, NormalizeLimit(limit, 80, 500)),
            SafetyNote = "Numeric settings remain read-only unless verified offsets are added in Core."
        };
    }

    public object PreviewWriteGlobalSettings(string? gameRoot, GlobalSettingsUpdate update)
    {
        var project = LoadProject(gameRoot);
        var tables = LoadTables(project);
        var document = _globalSettingsService.Load(project, tables);
        var changes = ApplyGlobalSettingsUpdate(project, document, update, mutate: false);
        return new
        {
            project.GameRoot,
            Changes = changes,
            SafetyNote = "Preview only; no game files were modified."
        };
    }

    public object WriteGlobalSettings(string? gameRoot, GlobalSettingsUpdate update, string? writeMode)
    {
        var project = LoadProject(gameRoot);
        EnsureWriteMode(project, writeMode);
        var tables = LoadTables(project);
        var document = _globalSettingsService.Load(project, tables);
        var changes = ApplyGlobalSettingsUpdate(project, document, update, mutate: true);
        GlobalSettingsSaveResult? numericResult = null;
        if (update.NumericSettings.Count > 0)
        {
            numericResult = _globalSettingsService.SaveNumericSettings(project, document, update.NumericSettings);
        }

        var result = _globalSettingsService.Save(
            project,
            tables,
            document,
            saveJobSeries: update.JobSeriesNames.Count > 0,
            saveDetailedJobs: update.DetailedJobNames.Count > 0,
            saveGameTitle: update.GameTitle != null);

        return new
        {
            project.GameRoot,
            Changes = changes,
            Result = result,
            NumericResult = numericResult,
            SafetyNote = "Global-setting writes use GlobalSettingsService backups, reports, and reread validation."
        };
    }

    public object ReadCmSettings(string? gameRoot)
    {
        var project = LoadProject(gameRoot);
        var document = _cmSettingsService.Load(project);
        return new
        {
            project.GameRoot,
            Document = document,
            SafetyNote = "Read-only CM settings view. Use preview_write_cm_settings before write_cm_settings."
        };
    }

    public object PreviewWriteCmSettings(string? gameRoot, CmSettingsMcpUpdate update)
    {
        var project = LoadProject(gameRoot);
        var changes = _cmSettingsService.Preview(project, ToCmSettingsUpdate(update));
        return new
        {
            project.GameRoot,
            Changes = changes,
            SafetyNote = "Preview only; no game files were modified."
        };
    }

    public object WriteCmSettings(string? gameRoot, CmSettingsMcpUpdate update, string? writeMode)
    {
        var project = LoadProject(gameRoot);
        EnsureWriteMode(project, writeMode);
        var result = _cmSettingsService.Save(project, ToCmSettingsUpdate(update));
        return new
        {
            project.GameRoot,
            Result = result,
            SafetyNote = "CM setting writes use whitelisted offsets, backup, fixed-length overwrite, and reread validation."
        };
    }

    public object RunGlobalNumericDiscovery(string? gameRoot)
    {
        var project = LoadProject(gameRoot);
        var report = new GlobalNumericDiscoveryService().PrepareManualDiffExperiment(
            project,
            _globalSettingsService.GetNumericDefinitions());
        return new
        {
            project.GameRoot,
            Report = report,
            SafetyNote = "Prepared test copies and a JSON report only; source project files were not modified."
        };
    }

    private static CmSettingsUpdate ToCmSettingsUpdate(CmSettingsMcpUpdate? update)
    {
        var result = new CmSettingsUpdate();
        if (update == null) return result;

        foreach (var pair in update.Values)
        {
            result.Values[pair.Key] = pair.Value;
        }

        foreach (var pair in update.TerrainStrategy)
        {
            result.TerrainStrategy[pair.Key] = new CmTerrainStrategyUpdate
            {
                Fire = pair.Value.Fire,
                Water = pair.Value.Water,
                Wind = pair.Value.Wind,
                Earth = pair.Value.Earth
            };
        }

        return result;
    }

    public object RunGlobalNumericLowRiskDiscovery(string? gameRoot)
    {
        var project = LoadProject(gameRoot);
        var report = new GlobalNumericDiscoveryService().PrepareLowRiskManualDiffExperiment(project);
        return new
        {
            project.GameRoot,
            Report = report,
            SafetyNote = "Prepared noop/case test copies and official-tool copies only; source project files were not modified."
        };
    }

    public object CompareGlobalNumericLowRiskDiffs(string? gameRoot, string evidenceRoot)
    {
        var project = LoadProject(gameRoot);
        var report = new GlobalNumericDiscoveryService().CompareLowRiskCaseDiffs(project, evidenceRoot);
        return new
        {
            project.GameRoot,
            Report = report,
            SafetyNote = "Compare only. Candidate offsets are not promoted until metadata is added and write round-trip passes."
        };
    }

    public object QueryGlobalNumericDefinitions(string? gameRoot)
    {
        var project = LoadProject(gameRoot);
        var report = new GlobalNumericQueryService().Query(
            project,
            _globalSettingsService.GetNumericDefinitions());
        return new
        {
            project.GameRoot,
            Report = report,
            SafetyNote = "Read-only static query. Candidate hits do not change CanEdit; pending fields still require official single-field diff."
        };
    }

    public object QueryAbilityTierPatchPoints(string? gameRoot)
    {
        var project = LoadProject(gameRoot);
        var report = _abilityTierPatchService.Scan(project);
        return new
        {
            project.GameRoot,
            Report = report,
            SafetyNote = "Read-only scan. Fixed forum addresses are not trusted; write eligibility depends on PE mapping and local machine-code signatures."
        };
    }

    public object PreviewWriteAbilityTierProfile(string? gameRoot, AbilityTierProfileUpdate update)
    {
        var project = LoadProject(gameRoot);
        var profile = BuildAbilityTierProfile(update);
        var preview = _abilityTierPatchService.Preview(project, profile);
        return new
        {
            project.GameRoot,
            Preview = preview,
            SafetyNote = preview.CanWrite
                ? "Preview only; no game files were modified. This profile can be written with backup and reread validation."
                : "Preview only; no game files were modified. Non-writable profiles require relocation or additional validation."
        };
    }

    public object WriteAbilityTierProfile(string? gameRoot, AbilityTierProfileUpdate update, string? writeMode)
    {
        var project = LoadProject(gameRoot);
        EnsureWriteMode(project, writeMode);
        var profile = BuildAbilityTierProfile(update);
        var result = _abilityTierPatchService.Write(project, profile);
        return new
        {
            project.GameRoot,
            Result = result,
            SafetyNote = "Ability-tier profile was written through signature-checked Ekd5.exe offsets with backup, SHA256 report, and read-back validation."
        };
    }

    private AbilityTierProfile BuildAbilityTierProfile(AbilityTierProfileUpdate update)
    {
        if (update == null)
        {
            throw new InvalidOperationException("ability tier profile update is required.");
        }

        var displayMode = string.IsNullOrWhiteSpace(update.DisplayMode)
            ? "Letter"
            : update.DisplayMode.Trim();
        var profile = _abilityTierPatchService.BuildDefaultProfile(update.TierCount, displayMode);
        if (update.Labels.Count == 0)
        {
            return profile;
        }

        return new AbilityTierProfile
        {
            ProfileName = $"Custom{update.TierCount}Tier{displayMode}",
            TierCount = update.TierCount,
            DisplayMode = displayMode,
            Labels = update.Labels.Select(label => label ?? string.Empty).ToList(),
            PatchMode = profile.PatchMode
        };
    }

    public object ParseScenarioTextImport(string? gameRoot, string input, string? scenarioKind)
    {
        var project = LoadProject(gameRoot);
        var tables = LoadTables(project);
        var dictionary = LoadRequiredScenarioCommandDictionary(project);
        var service = new ScenarioTextImportService(ScenarioTextImportService.LoadPersonNames(project, tables));
        var result = service.Parse(
            input ?? string.Empty,
            sceneIndex: 1,
            sectionIndex: 1,
            (commandId, sceneIndex, sectionIndex) => CreateScenarioImportPreviewCommand(dictionary, commandId, sceneIndex, sectionIndex));
        return new
        {
            project.GameRoot,
            ScenarioKind = scenarioKind ?? "R",
            result.Success,
            CommandCount = result.Commands.Count,
            PreviewRows = result.PreviewRows,
            Errors = result.Errors,
            SafetyNote = "Parse only; use scenario patch tools for controlled writes."
        };
    }

    public object ApplyScenarioTextImport(string? gameRoot, string input, string relativePath, string? scenarioKind)
    {
        var project = LoadProject(gameRoot);
        EnsureWriteMode(project, "direct");
        var tables = LoadTables(project);
        var dictionary = LoadRequiredScenarioCommandDictionary(project);
        var targetPath = ResolveScenarioFile(project, relativePath);
        var service = new ScenarioTextImportService(ScenarioTextImportService.LoadPersonNames(project, tables));
        var parsed = service.Parse(
            input ?? string.Empty,
            sceneIndex: 1,
            sectionIndex: 1,
            (commandId, sceneIndex, sectionIndex) => CreateScenarioImportPreviewCommand(dictionary, commandId, sceneIndex, sectionIndex));
        if (!parsed.Success)
        {
            return new
            {
                project.GameRoot,
                ScenarioKind = scenarioKind ?? InferScenarioKindFromPath(targetPath),
                RelativePath = NormalizeProjectRelativePath(project, targetPath),
                Applied = false,
                CommandCount = parsed.Commands.Count,
                parsed.PreviewRows,
                parsed.Errors,
                SafetyNote = "Scenario text import was parsed but not written because it contains errors or no commands."
            };
        }

        var patch = new ModScenarioPatch
        {
            PatchId = Path.GetFileNameWithoutExtension(targetPath) + "-text-import",
            RelativePath = NormalizeProjectRelativePath(project, targetPath),
            Note = "Compiled from AI scenario text import markup.",
            Operations =
            {
                new ModScenarioPatchOperation
                {
                    Operation = "append_command",
                    SceneIndex = 1,
                    SectionIndex = 1,
                    Commands = parsed.Commands.Select(ConvertLegacyCommandToDraft).ToList(),
                    Note = "AI scenario text import append."
                }
            }
        };
        var result = _modPackageService.ApplyScenarioPatchAggressive(project, patch, dictionary, forceOpenScenarioWrites: true);
        return new
        {
            project.GameRoot,
            ScenarioKind = scenarioKind ?? InferScenarioKindFromPath(targetPath),
            RelativePath = NormalizeProjectRelativePath(project, targetPath),
            CommandCount = parsed.Commands.Count,
            parsed.PreviewRows,
            parsed.Errors,
            Patch = patch,
            Result = result,
            SafetyNote = "AI scenario text import was compiled to structural R/S commands and written directly through LegacyScenarioWriter backups, reports, and reread validation."
        };
    }

    public object ReadScenarioTextImportTemplate(string? gameRoot)
    {
        var project = string.IsNullOrWhiteSpace(gameRoot) ? null : LoadProject(gameRoot);
        return new
        {
            GameRoot = project?.GameRoot ?? string.Empty,
            Template = ScenarioTextImportService.LoadTemplateText(project),
            SafetyNote = "Template is read-only guidance for AI-authored scenario text import drafts."
        };
    }

    public object ExportScenarioTexts(string? gameRoot, string relativePath, string format)
    {
        var project = LoadProject(gameRoot);
        var filePath = ResolveScenarioFile(project, relativePath);
        var entries = _scenarioTextReader.Read(filePath, maxItems: 4096).ToList();
        var normalizedFormat = (format ?? "csv").Trim().ToLowerInvariant();
        var outputPath = normalizedFormat switch
        {
            "txt" or "text" => _scenarioTextExportService.ExportTxt(project, Path.GetFileName(filePath), entries),
            _ => _scenarioTextExportService.ExportCsv(project, Path.GetFileName(filePath), entries)
        };

        return new
        {
            project.GameRoot,
            RelativePath = NormalizeProjectRelativePath(project, filePath),
            EntryCount = entries.Count,
            Format = normalizedFormat,
            OutputPath = outputPath,
            SafetyNote = "Export only writes CCZModStudio_Exports/ScenarioTexts."
        };
    }

    public object ScanScriptVariables(string? gameRoot, string? relativePath, int limit, int maxFiles)
    {
        var project = LoadProject(gameRoot);
        var dictionary = LoadRequiredScenarioCommandDictionary(project);
        var effectiveLimit = NormalizeLimit(limit, 200, 5000);
        if (!string.IsNullOrWhiteSpace(relativePath))
        {
            var path = ResolveScenarioFile(project, relativePath);
            var document = _legacyScenarioReader.Read(path, dictionary);
            var snapshot = _scriptVariableUsageService.BuildCurrentScenarioSnapshot(document);
            return new
            {
                project.GameRoot,
                RelativePath = NormalizeProjectRelativePath(project, path),
                snapshot.SourceLabel,
                snapshot.VersionKey,
                snapshot.BuiltAt,
                TotalSummaries = snapshot.Summaries.Count,
                TotalOccurrences = snapshot.Occurrences.Count,
                Summaries = snapshot.Summaries.Take(effectiveLimit),
                Occurrences = snapshot.Occurrences.Take(effectiveLimit),
                SafetyNote = "Read-only variable usage scan for one R/S script."
            };
        }

        var result = _scriptVariableUsageService.ScanProject(project, dictionary);
        return new
        {
            project.GameRoot,
            result.SourceLabel,
            result.VersionKey,
            result.BuiltAt,
            result.ScannedScenarioCount,
            result.FailedScenarios,
            TotalSummaries = result.Summaries.Count,
            TotalOccurrences = result.Occurrences.Count,
            Summaries = result.Summaries.Take(effectiveLimit),
            Occurrences = result.Occurrences.Take(effectiveLimit),
            SafetyNote = "Read-only variable usage scan."
        };
    }

    public object ReadScriptVariableSnapshot(string? gameRoot, string relativePath, int? sceneIndex, int? sectionIndex, int? commandIndex, List<int>? addresses)
    {
        var project = LoadProject(gameRoot);
        var filePath = ResolveScenarioFile(project, relativePath);
        var dictionary = LoadRequiredScenarioCommandDictionary(project);
        var document = _legacyScenarioReader.Read(filePath, dictionary);
        var currentCommand = document.EnumerateCommands()
            .FirstOrDefault(command => (!sceneIndex.HasValue || command.SceneIndex == sceneIndex.Value) &&
                                       (!sectionIndex.HasValue || command.SectionIndex == sectionIndex.Value) &&
                                       (!commandIndex.HasValue || command.CommandIndex == commandIndex.Value));
        var preceding = _scriptVariableValueResolver.ReadPrecedingProjectDocuments(project, dictionary, Path.GetFileName(filePath), document);
        var snapshot = _scriptVariableValueResolver.BuildSnapshotToCommand(document, currentCommand, preceding);
        var requested = addresses is { Count: > 0 } ? addresses.Distinct().OrderBy(x => x).ToArray() : Array.Empty<int>();
        return new
        {
            project.GameRoot,
            RelativePath = NormalizeProjectRelativePath(project, filePath),
            StopCommand = currentCommand == null ? null : BuildLegacyScenarioCommandPayload(currentCommand),
            PrecedingScenarioCount = preceding.Count,
            Addresses = requested.Select(address =>
            {
                var hasInteger = snapshot.TryGetInteger(address, out var integerValue);
                var hasBoolean = snapshot.TryGetBoolean(address, out var booleanValue);
                return new { Address = address, HasInteger = hasInteger, IntegerValue = hasInteger ? integerValue : (int?)null, HasBoolean = hasBoolean, BooleanValue = hasBoolean ? booleanValue : (bool?)null };
            }),
            SafetyNote = requested.Length == 0 ? "Pass addresses to read concrete snapshot values. The Core snapshot intentionally does not expose private full dictionaries." : "Read-only variable value snapshot."
        };
    }

    public object ReadRSceneDraft(string? gameRoot, string scenarioFileName)
    {
        var project = LoadProject(gameRoot);
        return new
        {
            project.GameRoot,
            StorePath = _rSceneDraftService.GetStorePath(project),
            Draft = _rSceneDraftService.LoadDraft(project, scenarioFileName)
        };
    }

    public object SaveRSceneDraft(string? gameRoot, RSceneDraftSaveRequest request)
    {
        if (request == null) throw new InvalidOperationException("request is required.");
        var project = LoadProject(gameRoot);
        var actors = request.Actors.Select(actor => new RScenePlacedActor
        {
            TargetKey = actor.TargetKey,
            PersonId = actor.PersonId,
            Name = actor.Name,
            JobId = actor.JobId,
            JobName = actor.JobName,
            RImageId = actor.RImageId,
            SImageId = actor.SImageId,
            Facing = actor.Facing,
            FrameIndex = actor.FrameIndex,
            GridX = actor.GridX,
            GridY = actor.GridY,
            PixelX = actor.PixelX,
            PixelY = actor.PixelY,
            Source = actor.Source,
            ActorNote = actor.ActorNote,
            LastActionTargetKey = actor.LastActionTargetKey
        }).ToList();
        var path = _rSceneDraftService.SaveDraft(project, request.ScenarioFileName, request.BackgroundImageNumber, request.GridSize, actors);
        return new
        {
            project.GameRoot,
            StorePath = path,
            Draft = _rSceneDraftService.LoadDraft(project, request.ScenarioFileName),
            SafetyNote = "Saved only an R-scene draft JSON under CCZModStudio_Notes; no scenario file was modified."
        };
    }

    public object PublishRSceneDraftToScenario(string? gameRoot, string scenarioFileName)
    {
        var project = LoadProject(gameRoot);
        EnsureWriteMode(project, "direct");
        var dictionary = LoadRequiredScenarioCommandDictionary(project);
        var draft = _rSceneDraftService.LoadDraft(project, scenarioFileName);
        if (string.IsNullOrWhiteSpace(draft.ScenarioFileName))
        {
            throw new InvalidOperationException("scenario_file_name is required.");
        }

        var relativePath = NormalizeRSceneScenarioPath(draft.ScenarioFileName);
        _ = ResolveScenarioFile(project, relativePath);
        var commands = new List<ModScenarioCommandDraft>();
        if (draft.BackgroundImageNumber > 0)
        {
            commands.Add(BuildRSceneBackgroundDraft(draft.BackgroundImageNumber));
        }

        commands.Add(BuildWordCommandDraft(0x1C, 0, "R scene redraw before draft actors."));
        foreach (var actor in draft.Actors.Where(actor => actor.PersonId >= 0 && actor.GridX >= 0 && actor.GridY >= 0))
        {
            commands.Add(BuildRSceneActorAppearDraft(actor));
        }

        if (commands.Count == 0)
        {
            throw new InvalidOperationException("R-scene draft has no background or valid actors to publish.");
        }

        var patch = new ModScenarioPatch
        {
            PatchId = Path.GetFileNameWithoutExtension(relativePath) + "-rscene-draft",
            RelativePath = relativePath,
            Note = "Published from R-scene visual draft.",
            Operations =
            {
                new ModScenarioPatchOperation
                {
                    Operation = "append_command",
                    SceneIndex = 1,
                    SectionIndex = 1,
                    Commands = commands,
                    Note = "Append R-scene draft background/redraw/actor placement commands."
                }
            }
        };
        var result = _modPackageService.ApplyScenarioPatchAggressive(project, patch, dictionary, forceOpenScenarioWrites: true);
        return new
        {
            project.GameRoot,
            Draft = draft,
            Patch = patch,
            Result = result,
            SafetyNote = "R-scene draft published as 0x27 background, 0x1C redraw, and 0x30 actor appear commands through LegacyScenarioWriter direct-write validation."
        };
    }

    public object ListRSceneCommandCandidates(string? gameRoot, string relativePath, int limit)
    {
        var project = LoadProject(gameRoot);
        var filePath = ResolveScenarioFile(project, relativePath);
        var dictionary = LoadRequiredScenarioCommandDictionary(project);
        var document = _legacyScenarioReader.Read(filePath, dictionary);
        var preceding = _scriptVariableValueResolver.ReadPrecedingProjectDocuments(project, dictionary, Path.GetFileName(filePath), document);
        var candidates = _rSceneDraftService.BuildCommandCandidates(
            document,
            command => command.CommandName,
            null,
            (command, parameterIndex) => ResolveRScenePersonReference(document, preceding, command, parameterIndex)).Take(NormalizeLimit(limit, 200, 2000)).ToList();
        var states = _rSceneDraftService.BuildSceneStateCandidates(
            document,
            (command, _) => _scriptVariableValueResolver.BuildSnapshotToCommand(document, command, preceding));
        return new
        {
            project.GameRoot,
            RelativePath = NormalizeProjectRelativePath(project, filePath),
            CandidateCount = candidates.Count,
            Candidates = candidates,
            SceneStates = states.Take(NormalizeLimit(limit, 200, 2000)),
            SafetyNote = "Read-only R-scene command candidates."
        };
    }

    public object ListBattlefieldDeploymentSlots(string? gameRoot, string relativePath, int limit)
    {
        var project = LoadProject(gameRoot);
        var scenario = ResolveBattlefieldStatusScenario(project, relativePath);
        var tables = LoadTables(project);
        var dictionary = LoadRequiredScenarioCommandDictionary(project);
        var document = _battlefieldEditorService.Load(project, scenario, dictionary, tables);
        var slots = BattlefieldEditorService.BuildDeploymentSlotInfos(document).Take(NormalizeLimit(limit, 300, 3000)).ToList();
        return new
        {
            project.GameRoot,
            Scenario = BuildScenarioFilePayload(scenario),
            Summary = document.Summary,
            SlotCount = slots.Count,
            Slots = slots,
            SafetyNote = "Read-only deployment slots. Use preview_battlefield_deployment_write before write_battlefield_deployment."
        };
    }

    public object PreviewBattlefieldDeploymentWrite(string? gameRoot, string relativePath, List<BattlefieldDeploymentUpdate> updates)
    {
        var project = LoadProject(gameRoot);
        var scenario = ResolveBattlefieldStatusScenario(project, relativePath);
        var placements = BuildBattlefieldPlacements(updates);
        return new
        {
            project.GameRoot,
            Scenario = BuildScenarioFilePayload(scenario),
            RequestedPlacementCount = placements.Count,
            Placements = placements.Select(placement => new { placement.TargetKey, placement.PersonId, placement.Name, placement.GridX, placement.GridY, placement.AiMode, placement.Direction, placement.Hidden, Writable = BattlefieldDeploymentWriteService.IsScriptPlacementWritable(placement) }),
            SafetyNote = "Preview only. Writable=true means the TargetKey is a verified 46/47/4B S-script deployment record."
        };
    }

    public object WriteBattlefieldDeployment(string? gameRoot, string relativePath, List<BattlefieldDeploymentUpdate> updates, string? writeMode)
    {
        var project = LoadProject(gameRoot);
        EnsureWriteMode(project, writeMode);
        var scenario = ResolveBattlefieldStatusScenario(project, relativePath);
        var dictionary = LoadRequiredScenarioCommandDictionary(project);
        var placements = BuildBattlefieldPlacements(updates);
        var result = _battlefieldDeploymentWriteService.SaveScriptPlacements(project, scenario, dictionary, placements);
        return new
        {
            project.GameRoot,
            Scenario = BuildScenarioFilePayload(scenario),
            Result = result,
            SafetyNote = "Deployment writes use LegacyScenarioWriter with backup, report, and reread validation."
        };
    }

    public object InspectEexEntries(string? gameRoot, string relativePath, string? category, int limit)
    {
        var project = LoadProject(gameRoot);
        var path = ResolveProjectFile(project, relativePath, mustExist: true);
        var archiveCategory = string.IsNullOrWhiteSpace(category) ? InferEexCategory(project, path) : category!;
        var rows = _eexEntryProbeReader.Probe(path, archiveCategory);
        return new
        {
            project.GameRoot,
            RelativePath = NormalizeProjectRelativePath(project, path),
            Summary = _eexEntryTreeDetailService.BuildTreeSummary(rows),
            Groups = _eexEntryTreeDetailService.BuildGroups(rows),
            Rows = rows.Take(NormalizeLimit(limit, 200, 2000)),
            SafetyNote = "Read-only EEX probe. No decompression, repacking, or writes are performed."
        };
    }

    public object CompareEexArchives(string? gameRoot, string relativePath, int maxPeers)
    {
        var project = LoadProject(gameRoot);
        var path = ResolveProjectFile(project, relativePath, mustExist: true);
        var all = _eexArchiveReader.ReadAll(project);
        var target = all.FirstOrDefault(item => Path.GetFullPath(item.Path).Equals(Path.GetFullPath(path), StringComparison.OrdinalIgnoreCase))
                     ?? _eexArchiveReader.Read(path, InferEexCategory(project, path));
        var result = _eexCrossFileComparisonService.Compare(target, all, NormalizeLimit(maxPeers, 8, 32));
        return new
        {
            project.GameRoot,
            Target = target,
            Result = result,
            SafetyNote = "Read-only cross-file EEX comparison."
        };
    }

    public object ExportTableAnnotations(string? gameRoot, string tableName)
    {
        var project = LoadProject(gameRoot);
        var tables = LoadTables(project);
        var table = FindTable(project, tables, tableName);
        var read = _tableReader.Read(project, table, tables);
        var path = _fieldAnnotationService.ExportAnnotations(project, table, read.Validation, read.Data);
        return new
        {
            project.GameRoot,
            table.TableName,
            OutputPath = path,
            SafetyNote = "Export only writes CCZModStudio_Exports/TableAnnotations."
        };
    }

    public object ListItemEffectCatalog(string? gameRoot)
    {
        var project = LoadProject(gameRoot);
        var tables = LoadTables(project);
        var seed = BuildDefaultItemEffectCatalogEntries(project, tables);
        var entries = _itemEffectCatalogService.Load(project, seed);
        return new
        {
            project.GameRoot,
            StorePath = _itemEffectCatalogService.GetStorePath(project),
            Count = entries.Count,
            Entries = entries,
            DisplayLookup = _itemEffectCatalogService.BuildDisplayLookup(entries),
            Native66NamePool = Ccz66RevisedLayout.Is66(project)
                ? new
                {
                    Source = Ccz66ItemEffectNameService.SourceName,
                    BaseOffset = HexDisplayFormatter.FormatOffset(Ccz66ItemEffectNameService.NamePoolOffset),
                    SlotSize = Ccz66ItemEffectNameService.SlotSize,
                    SentinelOk = _ccz66ItemEffectNameService.ValidateSentinels(project, out var warnings),
                    Warnings = warnings
                }
                : null
        };
    }

    public object WriteItemEffectName66Slot(string? gameRoot, int slotIndex, string name, string? writeMode)
    {
        var project = LoadProject(gameRoot);
        EnsureWriteMode(project, writeMode);
        var result = _ccz66ItemEffectNameService.WriteSlot(project, slotIndex, name ?? string.Empty);
        return new
        {
            project.GameRoot,
            Result = result,
            SafetyNote = "Wrote only the 6.6 Ekd5.exe item-effect name slot at 0x9E800 + slot_index * 16. Effect-id bindings are unchanged."
        };
    }

    public object SaveItemEffectCatalog(string? gameRoot, List<ItemEffectCatalogEntry> entries, string? writeMode)
    {
        var project = LoadProject(gameRoot);
        EnsureWriteMode(project, writeMode);
        var path = _itemEffectCatalogService.Save(project, entries ?? []);
        return new
        {
            project.GameRoot,
            StorePath = path,
            Count = entries?.Count ?? 0,
            SafetyNote = "Saved only the project-side UTF-8 item-effect catalog JSON; fixed-width game tables were not modified."
        };
    }

    public object ReadEquipmentTypeProfile(string? gameRoot)
    {
        var project = LoadProject(gameRoot);
        var tables = LoadTables(project);
        var profile = _equipmentTypeProfileService.Build(project, tables);
        return new
        {
            project.GameRoot,
            Profile = profile,
            SafetyNote = "Read-only project equipment type profile."
        };
    }

    public object ReadAccessoryJobGroups(string? gameRoot)
    {
        var project = LoadProject(gameRoot);
        var tables = LoadTables(project);
        var profile = _accessoryJobGroupService.Read(project, tables);
        return new
        {
            project.GameRoot,
            Profile = profile,
            SafetyNote = "Read-only accessory equipment multi-job-series grouping from Ekd5.exe:0044C341."
        };
    }

    public object PreviewAccessoryJobGroups(string? gameRoot, List<List<int>> groups)
    {
        var project = LoadProject(gameRoot);
        var tables = LoadTables(project);
        var preview = _accessoryJobGroupService.Preview(project, tables, NormalizeAccessoryJobGroups(groups));
        return new
        {
            project.GameRoot,
            Preview = preview,
            SafetyNote = "Preview only; no files were modified."
        };
    }

    public object WriteAccessoryJobGroups(string? gameRoot, List<List<int>> groups, string? writeMode)
    {
        var project = LoadProject(gameRoot);
        EnsureWriteMode(project, writeMode);

        var tables = LoadTables(project);
        var save = _accessoryJobGroupService.Save(project, tables, NormalizeAccessoryJobGroups(groups));
        return new
        {
            project.GameRoot,
            save.TargetFilePath,
            save.BackupPath,
            save.ReportJsonPath,
            save.BytesWritten,
            save.ChangedBytes,
            save.Preview,
            SafetyNote = "Wrote only Ekd5.exe accessory job grouping bytes at OD address 0044C341 using fixed-length overwrite."
        };
    }

    public object PreviewAttackArea(string? gameRoot, string columnName, int fieldValue, int canvasSize)
    {
        var project = LoadProject(gameRoot);
        var preview = _attackAreaPreviewService.BuildPreview(project, columnName, fieldValue, NormalizeLimit(canvasSize, 192, 1024));
        string? outputPath = null;
        if (preview.Bitmap != null)
        {
            outputPath = BuildExportPath(project, "EffectPreviews", $"attack_area_{MakeSafeFileNameForMcp(columnName)}_{fieldValue}.png");
            Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
            preview.Bitmap.Save(outputPath, ImageFormat.Png);
            preview.Bitmap.Dispose();
        }

        return new
        {
            project.GameRoot,
            preview.SourcePath,
            preview.DisplayName,
            preview.FieldValue,
            preview.ImageNumber,
            preview.EntryCount,
            PreviewPath = outputPath,
            preview.Message,
            SafetyNote = "Preview export writes only CCZModStudio_Exports/EffectPreviews."
        };
    }

    public object PreviewStrategyAnimation(string? gameRoot, int animationValue, string? kind, int canvasSize)
    {
        var project = LoadProject(gameRoot);
        var previewKind = string.Equals(kind, "big_mcall", StringComparison.OrdinalIgnoreCase) ||
                          string.Equals(kind, "big", StringComparison.OrdinalIgnoreCase) ||
                          string.Equals(kind, "mcall", StringComparison.OrdinalIgnoreCase)
            ? StrategyAnimationPreviewKind.BigMcall
            : StrategyAnimationPreviewKind.SmallMeff;
        var animated = _strategyAnimationPreviewService.BuildAnimatedPreview(project, previewKind, animationValue, NormalizeLimit(canvasSize, 160, 1024));
        var framePaths = new List<string>();
        try
        {
            var root = BuildExportDirectory(project, "EffectPreviews");
            Directory.CreateDirectory(root);
            for (var i = 0; i < Math.Min(animated.Frames.Count, 24); i++)
            {
                var framePath = Path.Combine(root, $"strategy_{previewKind}_{animationValue}_frame{i:D2}.png");
                animated.Frames[i].Save(framePath, ImageFormat.Png);
                framePaths.Add(framePath);
            }
        }
        finally
        {
            foreach (var frame in animated.Frames) frame.Dispose();
        }

        return new
        {
            project.GameRoot,
            Kind = previewKind.ToString(),
            animated.SourcePath,
            animated.FieldValue,
            animated.ImageNumber,
            animated.EntryCount,
            FrameCount = animated.Frames.Count,
            animated.FrameIntervalMs,
            animated.Loop,
            animated.RenderMode,
            FramePaths = framePaths,
            animated.Message,
            SafetyNote = "Preview export writes only CCZModStudio_Exports/EffectPreviews."
        };
    }

    private MapWorkbenchDraft BuildMapDraft(CczProject project, MapDraftSaveRequest request)
    {
        MapWorkbenchDraft draft;
        if (request.Draft.HasValue && request.Draft.Value.ValueKind == JsonValueKind.Object)
        {
            draft = request.Draft.Value.Deserialize<MapWorkbenchDraft>(new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                    ?? throw new InvalidOperationException("request.draft could not be deserialized.");
        }
        else if (!string.IsNullOrWhiteSpace(request.DraftId))
        {
            try
            {
                draft = _mapDraftService.LoadDraft(project, request.DraftId);
            }
            catch (FileNotFoundException)
            {
                draft = _mapDraftService.CreateBlankDraft(request.GridWidth ?? 30, request.GridHeight ?? 30, request.MaterialRoot ?? string.Empty);
                draft.DraftId = request.DraftId;
            }
        }
        else
        {
            draft = _mapDraftService.CreateBlankDraft(request.GridWidth ?? 30, request.GridHeight ?? 30, request.MaterialRoot ?? string.Empty);
        }

        if (!string.IsNullOrWhiteSpace(request.BoundMapId)) draft.BoundMapId = request.BoundMapId.Trim();
        if (request.GridWidth.HasValue) draft.GridWidth = request.GridWidth.Value;
        if (request.GridHeight.HasValue) draft.GridHeight = request.GridHeight.Value;
        if (request.BaseLayerPath != null) draft.BaseLayerPath = ResolveOptionalProjectOrExternalFile(project, request.BaseLayerPath) ?? request.BaseLayerPath;
        if (request.MaterialRoot != null) draft.MaterialRoot = string.IsNullOrWhiteSpace(request.MaterialRoot) ? string.Empty : ResolveExportOrExternalDirectory(project, request.MaterialRoot);
        if (request.AutoGenerateMapFromTerrain.HasValue) draft.AutoGenerateMapFromTerrain = request.AutoGenerateMapFromTerrain.Value;
        if (request.BeautifyGeneratedMap.HasValue) draft.BeautifyGeneratedMap = request.BeautifyGeneratedMap.Value;
        if (request.BeautifyStrength.HasValue) draft.BeautifyStrength = request.BeautifyStrength.Value;
        if (request.FeatherRadius.HasValue) draft.FeatherRadius = request.FeatherRadius.Value;
        return draft;
    }

    private IReadOnlyList<MaterialAsset> LoadMaterialsForDraft(CczProject project, MapWorkbenchDraft draft)
    {
        var root = string.IsNullOrWhiteSpace(draft.MaterialRoot) ? MaterialLibraryIndexer.ResolveMaterialLibraryRoot(project) : draft.MaterialRoot;
        return string.IsNullOrWhiteSpace(root) ? Array.Empty<MaterialAsset>() : _materialLibraryIndexer.IndexExplicitRoot(root);
    }

    private static object BuildMapDraftSummary(MapWorkbenchDraft draft)
        => new
        {
            draft.DraftId,
            draft.BoundMapId,
            draft.GridWidth,
            draft.GridHeight,
            draft.TileSize,
            draft.PixelWidth,
            draft.PixelHeight,
            draft.BaseLayerPath,
            draft.MaterialRoot,
            TerrainBaseCellCount = draft.TerrainBaseCells.Count,
            GeneratedMapCellCount = draft.GeneratedMapCells.Count,
            BuildingOverlayCellCount = draft.BuildingOverlayCells.Count,
            SceneryOverlayCellCount = draft.SceneryOverlayCells.Count,
            ManualOverrideCellCount = draft.MapCellOverrides.Count,
            draft.AutoGenerateMapFromTerrain,
            draft.BeautifyGeneratedMap,
            draft.BeautifyStrength,
            draft.FeatherRadius,
            draft.CreatedAtText,
            draft.UpdatedAtText
        };

    private MapResourceItem ResolveMapResource(CczProject project, string? mapId)
    {
        var maps = _mapResourceIndexer.Index(project);
        if (string.IsNullOrWhiteSpace(mapId))
        {
            throw new InvalidOperationException("map_id is required when the draft is not bound to a map.");
        }

        var query = mapId.Trim();
        var normalized = query.StartsWith("M", StringComparison.OrdinalIgnoreCase) ? query[1..] : query;
        if (int.TryParse(normalized, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id))
        {
            var padded = id.ToString("000", CultureInfo.InvariantCulture);
            var byId = maps.FirstOrDefault(map => map.Id.Equals(padded, StringComparison.OrdinalIgnoreCase) ||
                                                  Path.GetFileNameWithoutExtension(map.Name).Equals("M" + padded, StringComparison.OrdinalIgnoreCase));
            if (byId != null) return byId;
        }

        var byName = maps.FirstOrDefault(map => map.Name.Equals(query, StringComparison.OrdinalIgnoreCase) ||
                                                Path.GetFileNameWithoutExtension(map.Name).Equals(query, StringComparison.OrdinalIgnoreCase));
        return byName ?? throw new InvalidOperationException("Map resource was not found: " + mapId);
    }

    private static object BuildMapResourcePayload(CczProject project, MapResourceItem item)
        => new { item.Id, item.Name, item.Extension, item.SizeBytes, item.Width, item.Height, item.GridWidth, item.GridHeight, ProjectRelativePath = TryNormalizeStatic(project, item.Path), item.Path };

    private static string TryNormalizeStatic(CczProject project, string path)
    {
        try
        {
            var root = Path.GetFullPath(project.GameRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            var full = Path.GetFullPath(path);
            return full.StartsWith(root, StringComparison.OrdinalIgnoreCase)
                ? Path.GetRelativePath(project.GameRoot, full)
                : string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    private string? ResolveMaterialRootForRead(CczProject project, string? materialRoot)
        => string.IsNullOrWhiteSpace(materialRoot)
            ? MaterialLibraryIndexer.ResolveMaterialLibraryRoot(project)
            : ResolveExportOrExternalDirectory(project, materialRoot);

    private static object BuildMaterialAssetPayload(CczProject project, string? root, MaterialAsset asset)
        => new
        {
            asset.AssetType,
            asset.Category,
            asset.FileName,
            RelativePath = string.IsNullOrWhiteSpace(root) ? asset.FilePath : MapDraftService.GetMaterialRelativePath(root, asset.FilePath),
            asset.FilePath,
            asset.HexTag,
            asset.Description,
            asset.TerrainId,
            asset.TerrainName,
            asset.GroupKey,
            asset.AutoTileSetKey,
            asset.VariantIndex,
            asset.AutoTileRole,
            asset.AutoTileMask,
            asset.AutoTileMode,
            asset.AutoTilePriority,
            asset.SourceX,
            asset.SourceY,
            asset.SourceWidth,
            asset.SourceHeight,
            asset.Width,
            asset.Height,
            asset.SizeBytes
        };

    private static bool MatchesMaterialKeyword(MaterialAsset asset, string? keyword)
    {
        if (string.IsNullOrWhiteSpace(keyword)) return true;
        return asset.FileName.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
               asset.FilePath.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
               asset.Category.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
               asset.AssetType.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
               asset.HexTag.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
               asset.Description.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
               asset.TerrainName.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
               asset.GroupKey.Contains(keyword, StringComparison.OrdinalIgnoreCase);
    }

    private string ResolveExportOrExternalOutput(CczProject project, string path)
    {
        var normalized = path.Replace('/', Path.DirectorySeparatorChar);
        if (Path.IsPathRooted(normalized)) return Path.GetFullPath(normalized);
        return Path.GetFullPath(Path.Combine(project.WorkspaceRoot, normalized));
    }

    private string ResolveExportOrExternalDirectory(CczProject project, string path)
    {
        var normalized = path.Replace('/', Path.DirectorySeparatorChar);
        return Path.IsPathRooted(normalized)
            ? Path.GetFullPath(normalized)
            : Path.GetFullPath(Path.Combine(project.WorkspaceRoot, normalized));
    }

    private string? ResolveOptionalExternalFile(CczProject project, string path)
    {
        try
        {
            return ResolveExternalFile(project, path);
        }
        catch
        {
            return null;
        }
    }

    private string? ResolveOptionalProjectOrExternalFile(CczProject project, string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return string.Empty;
        try
        {
            var projectFile = ResolveProjectFile(project, path, mustExist: false);
            if (File.Exists(projectFile)) return projectFile;
        }
        catch
        {
        }

        try
        {
            return ResolveExternalFile(project, path);
        }
        catch
        {
            return path;
        }
    }

    private static IReadOnlyList<object> BuildTerrainCounts(byte[] cells)
        => cells.GroupBy(value => value)
            .OrderByDescending(group => group.Count())
            .ThenBy(group => group.Key)
            .Select(group => new { TerrainId = (int)group.Key, Count = group.Count() })
            .Cast<object>()
            .ToList();

    private static string InferScenarioKindFromPath(string path)
    {
        var fileName = Path.GetFileName(path);
        if (fileName.StartsWith("S_", StringComparison.OrdinalIgnoreCase)) return "S";
        return "R";
    }

    private static string NormalizeRSceneScenarioPath(string scenarioFileName)
    {
        var fileName = Path.GetFileName(scenarioFileName.Trim());
        if (string.IsNullOrWhiteSpace(fileName)) throw new InvalidOperationException("scenario_file_name is required.");
        if (!fileName.EndsWith(".eex", StringComparison.OrdinalIgnoreCase)) fileName += ".eex";
        if (!fileName.StartsWith("R_", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("R-scene draft publishing targets only RS/R_*.eex files.");
        }

        return Path.Combine("RS", fileName);
    }

    private static ModScenarioCommandDraft ConvertLegacyCommandToDraft(LegacyScenarioCommandNode command)
        => new()
        {
            CommandId = command.CommandId,
            CommandIdHex = command.CommandIdHex,
            Note = "Compiled from AI scenario text import.",
            Parameters = command.Parameters.Select(ConvertLegacyParameterToDraft).ToList()
        };

    private static ModScenarioParameterDraft ConvertLegacyParameterToDraft(LegacyScenarioCommandParameter parameter)
    {
        var layoutCode = parameter.Kind == LegacyScenarioParameterKind.Text ? 0x05 : parameter.LayoutCode;
        return new ModScenarioParameterDraft
        {
            LayoutCode = layoutCode,
            Kind = parameter.Kind switch
            {
                LegacyScenarioParameterKind.Text => "text",
                LegacyScenarioParameterKind.VariableArray => "variable_array",
                LegacyScenarioParameterKind.Dword32 => "dword32",
                _ => "word16"
            },
            IntValue = parameter.IntValue,
            Text = parameter.Text,
            Values = parameter.Values.Count == 0 ? null : parameter.Values.ToList()
        };
    }

    private static ModScenarioCommandDraft BuildRSceneBackgroundDraft(int backgroundImageNumber)
    {
        var backgroundSlot = Math.Clamp(backgroundImageNumber, 1, 999) - 1;
        return new ModScenarioCommandDraft
        {
            CommandId = 0x27,
            CommandIdHex = "0x27",
            Note = "R-scene draft background display.",
            Parameters =
            {
                WordParameter(0),
                WordParameter(backgroundSlot)
            }
        };
    }

    private static ModScenarioCommandDraft BuildRSceneActorAppearDraft(RScenePlacedActor actor)
        => new()
        {
            CommandId = 0x30,
            CommandIdHex = "0x30",
            Note = string.IsNullOrWhiteSpace(actor.ActorNote) ? "R-scene draft actor appear." : actor.ActorNote,
            Parameters =
            {
                WordParameter(actor.PersonId),
                WordParameter(actor.GridX),
                WordParameter(actor.GridY),
                WordParameter(FacingToDirectionValue(actor.Facing)),
                WordParameter(Math.Clamp(actor.FrameIndex, 0, 999))
            }
        };

    private static ModScenarioCommandDraft BuildWordCommandDraft(int commandId, int value, string note)
        => new()
        {
            CommandId = commandId,
            CommandIdHex = "0x" + commandId.ToString("X2", CultureInfo.InvariantCulture),
            Note = note,
            Parameters = { WordParameter(value) }
        };

    private static ModScenarioParameterDraft WordParameter(int value)
        => new()
        {
            LayoutCode = 0x01,
            Kind = "word16",
            IntValue = value
        };

    private static int FacingToDirectionValue(string? facing)
        => (facing ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            "0" or "up" or "u" or "north" or "n" or "上" or "北" => 0,
            "1" or "right" or "r" or "east" or "e" or "右" or "东" => 1,
            "2" or "down" or "d" or "south" or "s" or "下" or "南" => 2,
            "3" or "left" or "l" or "west" or "w" or "左" or "西" => 3,
            _ => 2
        };

    private object ApplyShopUpdates(ShopEditorBuildResult build, List<ShopRowUpdate>? updates, bool mutate)
    {
        if (updates == null || updates.Count == 0) throw new InvalidOperationException("updates must contain at least one shop row update.");
        var changes = new List<object>();
        foreach (var update in updates)
        {
            if (update.RowId < 0) throw new InvalidOperationException("row_id must be non-negative.");
            if (update.CampaignName != null)
            {
                var row = FindRowById(build.CampaignNameRead.Data, update.RowId);
                var nameColumn = FindNameColumnForMcp(build.CampaignNameRead.Data);
                var oldValue = Convert.ToString(row[nameColumn], CultureInfo.InvariantCulture) ?? string.Empty;
                if (mutate) row[nameColumn] = update.CampaignName;
                changes.Add(new { Table = build.CampaignNameRead.Table.TableName, update.RowId, Column = nameColumn.ColumnName, OldValue = oldValue, NewValue = update.CampaignName });
            }

            if (update.ShopValues.Count > 0)
            {
                var row = FindRowById(build.ShopDataRead.Data, update.RowId);
                foreach (var (columnName, value) in update.ShopValues)
                {
                    var column = FindColumn(build.ShopDataRead.Data, columnName);
                    if (column.ColumnName.Equals("ID", StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("ID is synthetic and cannot be written.");
                    var oldValue = row[column] is DBNull ? null : row[column];
                    var newValue = ConvertJsonValue(value);
                    if (mutate) row[column] = newValue;
                    changes.Add(new { Table = build.ShopDataRead.Table.TableName, update.RowId, Column = column.ColumnName, OldValue = oldValue, NewValue = newValue });
                }
            }
        }

        return new { ChangeCount = changes.Count, Changes = changes };
    }

    private static DataRow FindRowById(DataTable data, int rowId)
        => data.Rows.Cast<DataRow>().FirstOrDefault(row => Convert.ToInt32(row["ID"], CultureInfo.InvariantCulture) == rowId)
           ?? throw new InvalidOperationException($"Row ID {rowId} was not found in table {data.TableName}.");

    private static DataColumn FindNameColumnForMcp(DataTable data)
    {
        foreach (DataColumn column in data.Columns)
        {
            if (column.ColumnName.Equals("\u540D\u79F0", StringComparison.OrdinalIgnoreCase) ||
                column.ColumnName.Equals("Name", StringComparison.OrdinalIgnoreCase))
            {
                return column;
            }
        }

        foreach (DataColumn column in data.Columns)
        {
            if (!column.ColumnName.Equals("ID", StringComparison.OrdinalIgnoreCase) &&
                (column.ColumnName.Contains('\u540D', StringComparison.Ordinal) ||
                 column.ColumnName.Contains('\u79F0', StringComparison.Ordinal)))
            {
                return column;
            }
        }

        var writableCandidates = data.Columns.Cast<DataColumn>()
            .Where(column => !column.ColumnName.Equals("ID", StringComparison.OrdinalIgnoreCase) && !column.ReadOnly)
            .ToList();
        if (writableCandidates.Count == 1)
        {
            return writableCandidates[0];
        }

        throw new InvalidOperationException("Name column was not found in table " + data.TableName + ". Columns: " + string.Join(", ", data.Columns.Cast<DataColumn>().Select(column => column.ColumnName)));
    }

    private static object BuildTableSavePayload(TableSaveResult save)
        => new { save.Table.TableName, save.FilePath, save.BackupPath, save.ReportJsonPath, save.RowsWritten, save.ChangedBytes };

    private object ApplyGlobalSettingsUpdate(CczProject project, GlobalSettingsDocument document, GlobalSettingsUpdate? update, bool mutate)
    {
        if (update == null) throw new InvalidOperationException("update is required.");
        var changes = new List<object>();
        changes.AddRange(_globalSettingsService.PreviewNumericSettings(project, document, update.NumericSettings));

        if (update.GameTitle != null)
        {
            changes.Add(new { Field = "game_title", OldValue = document.GameTitle.Title, NewValue = update.GameTitle, document.GameTitle.CapacityBytes });
            if (mutate) document.GameTitle.Title = update.GameTitle;
        }

        foreach (var pair in update.JobSeriesNames)
        {
            var row = FindRowById(document.JobSeriesNames, pair.Key);
            var nameColumn = FindNameColumnForMcp(document.JobSeriesNames);
            var oldValue = Convert.ToString(row[nameColumn], CultureInfo.InvariantCulture) ?? string.Empty;
            changes.Add(new { Field = "job_series_names", RowId = pair.Key, OldValue = oldValue, NewValue = pair.Value });
            if (mutate) row[nameColumn] = pair.Value;
        }

        foreach (var pair in update.DetailedJobNames)
        {
            var row = FindRowById(document.DetailedJobNames, pair.Key);
            var nameColumn = FindNameColumnForMcp(document.DetailedJobNames);
            var oldValue = Convert.ToString(row[nameColumn], CultureInfo.InvariantCulture) ?? string.Empty;
            changes.Add(new { Field = "detailed_job_names", RowId = pair.Key, OldValue = oldValue, NewValue = pair.Value });
            if (mutate) row[nameColumn] = pair.Value;
        }

        return new { ChangeCount = changes.Count, Changes = changes };
    }

    private static IReadOnlyList<Dictionary<string, object?>> DataTableRows(DataTable data, int limit)
    {
        var columns = data.Columns.Cast<DataColumn>().ToArray();
        return data.Rows.Cast<DataRow>()
            .Take(limit)
            .Select(row => RowToDictionary(row, columns))
            .ToList();
    }

    private static IReadOnlyList<BattlefieldPlacedUnit> BuildBattlefieldPlacements(List<BattlefieldDeploymentUpdate>? updates)
    {
        if (updates == null || updates.Count == 0) throw new InvalidOperationException("updates must contain at least one battlefield deployment update.");
        return updates.Select(update => new BattlefieldPlacedUnit
        {
            TargetKey = update.TargetKey,
            PersonId = update.PersonId,
            Name = update.Name,
            GridX = update.GridX,
            GridY = update.GridY,
            AiMode = update.AiMode,
            Direction = update.Direction,
            Hidden = update.Hidden,
            Faction = update.Faction,
            Source = update.Source,
            PlacementNote = update.PlacementNote
        }).ToList();
    }

    private static LegacyScenarioCommandNode CreateScenarioImportPreviewCommand(SceneStringDocument dictionary, int commandId, int sceneIndex, int sectionIndex)
    {
        var name = dictionary.Commands.FirstOrDefault(command => command.Id == commandId)?.Name;
        var command = new LegacyScenarioCommandNode
        {
            SceneIndex = sceneIndex,
            SectionIndex = sectionIndex,
            CommandIndex = 0,
            CommandOrdinal = 0,
            CommandId = commandId,
            CommandName = string.IsNullOrWhiteSpace(name) ? "0x" + commandId.ToString("X2", CultureInfo.InvariantCulture) : name,
            FileOffset = 0,
            ConsumedBytes = 0
        };
        AddScenarioImportPreviewParameters(command);
        return command;
    }

    private static void AddScenarioImportPreviewParameters(LegacyScenarioCommandNode command)
    {
        switch (command.CommandId)
        {
            case 0x14:
                AddScenarioImportPreviewTextParameter(command, 0);
                break;
            case 0x2C:
                AddScenarioImportPreviewTextParameter(command, 0);
                AddScenarioImportPreviewWordParameter(command, 1);
                AddScenarioImportPreviewWordParameter(command, 2);
                AddScenarioImportPreviewWordParameter(command, 3);
                break;
            case 0x30:
                for (var i = 0; i < 5; i++) AddScenarioImportPreviewWordParameter(command, i);
                break;
            case 0x32:
                for (var i = 0; i < 6; i++) AddScenarioImportPreviewWordParameter(command, i);
                break;
            case 0x33:
                for (var i = 0; i < 3; i++) AddScenarioImportPreviewWordParameter(command, i);
                break;
            case 0x34:
                for (var i = 0; i < 2; i++) AddScenarioImportPreviewWordParameter(command, i);
                break;
            case 0x4F:
                for (var i = 0; i < 6; i++) AddScenarioImportPreviewWordParameter(command, i);
                break;
            case 0x50:
                for (var i = 0; i < 4; i++) AddScenarioImportPreviewWordParameter(command, i);
                break;
        }
    }

    private static void AddScenarioImportPreviewTextParameter(LegacyScenarioCommandNode command, int index)
        => command.Parameters.Add(new LegacyScenarioCommandParameter
        {
            Index = index,
            LayoutCode = 0x0E,
            Tag = 0x0E,
            FileOffset = 0,
            Kind = LegacyScenarioParameterKind.Text,
            Text = string.Empty,
            ByteLength = 0
        });

    private static void AddScenarioImportPreviewWordParameter(LegacyScenarioCommandNode command, int index)
        => command.Parameters.Add(new LegacyScenarioCommandParameter
        {
            Index = index,
            LayoutCode = 0x01,
            Tag = 0x01,
            FileOffset = 0,
            Kind = LegacyScenarioParameterKind.Word16,
            IntValue = 0,
            ByteLength = 2
        });

    private int? ResolveRScenePersonReference(
        LegacyScenarioDocument document,
        IReadOnlyList<LegacyScenarioDocument> preceding,
        LegacyScenarioCommandNode command,
        int parameterIndex)
    {
        if (parameterIndex < 0 || parameterIndex >= command.Parameters.Count)
        {
            return null;
        }

        var parameter = command.Parameters[parameterIndex];
        if (parameter.Kind is not (LegacyScenarioParameterKind.Word16 or LegacyScenarioParameterKind.Dword32))
        {
            return null;
        }

        var snapshot = _scriptVariableValueResolver.BuildSnapshotToCommand(document, command, preceding);
        return ScriptVariableValueResolver.TryResolvePerson2Reference(parameter.IntValue, snapshot, out var personId, out _)
            ? personId
            : null;
    }

    private string InferEexCategory(CczProject project, string path)
    {
        var relative = TryNormalizeProjectRelativePath(project, path);
        var fileName = Path.GetFileName(path);
        if (relative.StartsWith("RS" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) &&
            fileName.StartsWith("R_", StringComparison.OrdinalIgnoreCase)) return "R剧本EEX";
        if (relative.StartsWith("RS" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) &&
            fileName.StartsWith("S_", StringComparison.OrdinalIgnoreCase)) return "S剧本EEX";
        if (relative.StartsWith("Map" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) return "地图EEX";
        return "EEX";
    }

    private IReadOnlyList<ItemEffectCatalogEntry> BuildDefaultItemEffectCatalogEntries(CczProject project, IReadOnlyList<HexTableDefinition> tables)
        => _itemEffectNameReader.ReadBaseCatalogEntries(project, tables);

    private string BuildExportDirectory(CczProject project, string category)
    {
        var root = project.IsTestCopy
            ? Path.Combine(project.GameRoot, "_CCZModStudio_Exports", category)
            : Path.Combine(project.WorkspaceRoot, "CCZModStudio_Exports", category, project.Name);
        return root;
    }

    private string BuildExportPath(CczProject project, string category, string fileName)
        => Path.Combine(BuildExportDirectory(project, category), MakeSafeFileNameForMcp(fileName));

    private static object BuildImageExportPayload(CczProject project, MapWorkbenchDraft draft, string outputPath, int width, int height, string safetyNote)
        => new
        {
            project.GameRoot,
            Draft = BuildMapDraftSummary(draft),
            OutputPath = outputPath,
            Width = width,
            Height = height,
            SizeBytes = new FileInfo(outputPath).Length,
            SafetyNote = safetyNote
        };

    private static IReadOnlyList<IReadOnlyList<int>> NormalizeAccessoryJobGroups(List<List<int>>? groups)
        => (groups ?? [])
            .Select(group => (IReadOnlyList<int>)(group ?? []))
            .ToArray();

    private static string MakeSafeFileNameForMcp(string value)
    {
        value = string.IsNullOrWhiteSpace(value) ? "export" : value.Trim();
        foreach (var ch in Path.GetInvalidFileNameChars())
        {
            value = value.Replace(ch, '_');
        }

        return value;
    }
}
