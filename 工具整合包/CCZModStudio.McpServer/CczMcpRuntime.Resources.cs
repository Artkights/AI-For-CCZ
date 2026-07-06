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
    public object ListHexzmapBlocks(string? gameRoot, string? keyword, bool editableOnly, int limit)
    {
        var project = LoadProject(gameRoot);
        var terrainLookup = BuildTerrainNameLookup(project);
        var probe = _hexzmapProbeReader.Read(project, terrainLookup);
        var effectiveLimit = NormalizeLimit(limit, 200, 1000);
        var filtered = probe.Blocks
            .Where(block => !editableOnly || block.CanEdit)
            .Where(block => MatchesHexzmapBlockKeyword(block, keyword))
            .ToList();

        return new
        {
            project.GameRoot,
            HexzmapPath = probe.Path,
            probe.Magic,
            probe.MagicValid,
            probe.PayloadOffset,
            probe.PayloadLength,
            DirectoryTableOffsetHex = HexDisplayFormatter.FormatOffset(probe.DirectoryTableOffset),
            DirectoryEntryCount = probe.DirectoryEntries.Count,
            TotalBlocks = filtered.Count,
            ReturnedBlocks = Math.Min(filtered.Count, effectiveLimit),
            EditableBlocks = filtered.Count(block => block.CanEdit),
            TerrainDictionaryCount = terrainLookup.Count,
            probe.TrailingBytes,
            Blocks = filtered.Take(effectiveLimit).Select(BuildHexzmapBlockPayload),
            SafetyNote = "Read-only Hexzmap block listing. Use read_hexzmap_block to inspect cells before write_hexzmap_block."
        };
    }

    public object ReadHexzmapBlock(string? gameRoot, string mapId, bool includeCells, int maxRows)
    {
        if (string.IsNullOrWhiteSpace(mapId))
        {
            throw new InvalidOperationException("map_id is required, for example M000.");
        }

        var project = LoadProject(gameRoot);
        var terrainLookup = BuildTerrainNameLookup(project);
        var probe = _hexzmapProbeReader.Read(project, terrainLookup);
        var block = probe.Blocks.FirstOrDefault(x => x.MapId.Equals(mapId.Trim(), StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException($"Hexzmap block {mapId} was not found.");
        var cells = _hexzmapProbeReader.GetBlockCells(probe, block);
        var effectiveMaxRows = NormalizeLimit(maxRows, 120, 500);
        var cellRows = includeCells
            ? BuildHexzmapCellRows(cells, block.Width, terrainLookup, effectiveMaxRows)
            : Array.Empty<object>();

        return new
        {
            project.GameRoot,
            HexzmapPath = probe.Path,
            Block = BuildHexzmapBlockPayload(block),
            CellCount = cells.Length,
            ExpectedCellCount = block.Width * block.Height,
            IncludeCells = includeCells,
            MaxRows = effectiveMaxRows,
            ReturnedRows = includeCells ? Math.Min(block.Height, effectiveMaxRows) : 0,
            RowsTruncated = includeCells && block.Height > effectiveMaxRows,
            TopTerrains = BuildHexzmapTerrainCounts(cells, terrainLookup),
            Rows = cellRows,
            SafetyNote = "Read-only Hexzmap cells. Bounds are x=0..width-1 and y=0..height-1; write_hexzmap_block still enforces write_mode, version guard, backup, and reread verification."
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
            result.OldSha256,
            result.NewSha256,
            result.FormatCheckSummary,
            result.Warning
        };
    }

    public object PreviewMapImage(string? gameRoot, string targetRelativePath, string replacementPath)
    {
        var project = LoadProject(gameRoot);
        var targetPath = ResolveProjectFile(project, targetRelativePath, mustExist: true);
        var sourcePath = ResolveExternalFile(project, replacementPath);
        var preview = _mapImageReplace.PreviewMapImage(project, targetPath, sourcePath);
        return BuildMapImageReplacePreviewPayload(preview);
    }

    public object PreviewResourceReplace(string? gameRoot, string targetRelativePath, string replacementPath, bool requireSameExtension)
    {
        var project = LoadProject(gameRoot);
        var targetPath = ResolveProjectFile(project, targetRelativePath, mustExist: true);
        EnsureGenericResourceAllowed(project, targetPath);
        var sourcePath = ResolveExternalFile(project, replacementPath);
        var preview = _resourceReplace.PreviewReplacement(project, targetPath, sourcePath, requireSameExtension);
        return BuildResourceReplacePreviewPayload(preview);
    }

    public object ListImageResources(string? gameRoot, string? keyword, bool previewableOnly, bool replaceableOnly, int limit)
    {
        var project = LoadProject(gameRoot);
        var effectiveLimit = NormalizeLimit(limit, 100, 1000);
        var resources = _imageResourceCatalog.BuildCatalog(project);
        var filtered = resources
            .Where(resource => !previewableOnly || resource.SupportsPreview)
            .Where(resource => !replaceableOnly || resource.CanReplace)
            .Where(resource => MatchesImageResourceKeyword(resource, keyword))
            .ToList();

        return new
        {
            project.GameRoot,
            TotalResources = filtered.Count,
            ReturnedResources = Math.Min(filtered.Count, effectiveLimit),
            PreviewableResources = resources.Count(resource => resource.SupportsPreview),
            ReplaceableResources = resources.Count(resource => resource.CanReplace),
            CategoryCounts = resources
                .GroupBy(resource => resource.Category)
                .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
                .Select(group => new { Category = group.Key, Count = group.Count() }),
            Resources = filtered.Take(effectiveLimit).Select(resource => BuildImageResourcePayload(project, resource)),
            SafetyNote = "Read-only image resource catalog. Use preview/replace E5 and DLL icon tools for indexed resources; map background images use preview_map_image and replace_map_image."
        };
    }

    public object ListImageResourceEntries(string? gameRoot, string resource, string? keyword, int limit)
    {
        var project = LoadProject(gameRoot);
        var item = ResolveImageResource(project, resource);
        var entries = _imageResourceCatalog.ReadEntries(item);
        var effectiveLimit = NormalizeLimit(limit, 200, 10000);
        var filtered = entries
            .Where(entry => MatchesImageResourceEntryKeyword(entry, keyword))
            .ToList();

        return new
        {
            project.GameRoot,
            Resource = BuildImageResourcePayload(project, item),
            TotalEntries = filtered.Count,
            ReturnedEntries = Math.Min(filtered.Count, effectiveLimit),
            Entries = filtered.Take(effectiveLimit).Select(entry => BuildImageResourceEntryPayload(project, entry)),
            SafetyNote = "E5 image numbers are 1-based. DLL icon field indexes are 0-based."
        };
    }

    public object ExportImageResourcePreview(string? gameRoot, string resource, int imageNumber, int width, int height)
    {
        var project = LoadProject(gameRoot);
        var item = ResolveImageResource(project, resource);
        var entry = _imageResourceCatalog.ReadEntries(item).FirstOrDefault(x => x.ImageNumber == imageNumber)
            ?? throw new InvalidOperationException($"Image resource entry {imageNumber} was not found in {resource}.");
        var effectiveWidth = NormalizeLimit(width, 360, 2048);
        var effectiveHeight = NormalizeLimit(height, 260, 2048);

        using var bitmap = _imageResourceCatalog.RenderEntryPreview(project, entry, effectiveWidth, effectiveHeight)
            ?? throw new InvalidOperationException("The selected image resource entry could not be rendered.");

        var exportRoot = Path.Combine(project.WorkspaceRoot, "CCZModStudio_Exports", "ImagePreviews");
        Directory.CreateDirectory(exportRoot);
        var stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss_fff", CultureInfo.InvariantCulture);
        var fileName = $"{stamp}_{MakeSafeFileStem(entry.FileName)}_{entry.ImageNumber}.png";
        var exportPath = Path.Combine(exportRoot, fileName);
        bitmap.Save(exportPath, ImageFormat.Png);

        return new
        {
            project.GameRoot,
            ExportPath = exportPath,
            Width = bitmap.Width,
            Height = bitmap.Height,
            Entry = BuildImageResourceEntryPayload(project, entry),
            SafetyNote = "Preview export writes only CCZModStudio_Exports/ImagePreviews and does not modify game files."
        };
    }

    public object ListCczImageAssetPresets()
        => new
        {
            Count = _aiImageAssetService.ListPresets().Count,
            Presets = _aiImageAssetService.ListPresets()
        };

    public object BuildCczImagePrompt(
        string? gameRoot,
        string preset,
        string description,
        string? targetRelativePath,
        int? imageNumber,
        int? rImageId,
        int? sImageId,
        int? faceId,
        int? jobId,
        int factionSlot,
        string? outputFormat,
        int? width,
        int? height,
        List<string>? referenceImagePaths = null,
        List<string>? referenceRoles = null)
    {
        var project = LoadProject(gameRoot);
        var plan = _aiImageAssetService.BuildPromptPlan(project, preset, description, targetRelativePath, imageNumber, rImageId, sImageId, faceId, jobId, factionSlot, outputFormat, width, height, referenceImagePaths, referenceRoles);
        return BuildAiImagePromptPlanPayload(plan);
    }

    public object PrepareCczGeneratedImage(
        string? gameRoot,
        string preset,
        string description,
        string sourceImagePath,
        string? targetRelativePath,
        int? imageNumber,
        int? rImageId,
        int? sImageId,
        int? faceId,
        int? jobId,
        int factionSlot,
        string? outputFormat,
        int? width,
        int? height)
    {
        var project = LoadProject(gameRoot);
        var plan = _aiImageAssetService.BuildPromptPlan(project, preset, description, targetRelativePath, imageNumber, rImageId, sImageId, faceId, jobId, factionSlot, outputFormat, width, height);
        var sourcePath = ResolveExternalFile(project, sourceImagePath);
        var prepared = _aiImageAssetService.PrepareExistingImage(project, plan, sourcePath, (itemPlan, outputPath) => BuildAiImageReplacementPreview(project, itemPlan, outputPath));
        return BuildAiImagePreparePayload(prepared);
    }

    public object DrawCczImageAsset(
        string? gameRoot,
        string preset,
        string description,
        string? targetRelativePath,
        int? imageNumber,
        int? rImageId,
        int? sImageId,
        int? faceId,
        int? jobId,
        int factionSlot,
        string? outputFormat,
        int? width,
        int? height,
        bool dryRun,
        List<string>? referenceImagePaths = null,
        List<string>? referenceRoles = null)
    {
        var project = LoadProject(gameRoot);
        var plan = _aiImageAssetService.BuildPromptPlan(project, preset, description, targetRelativePath, imageNumber, rImageId, sImageId, faceId, jobId, factionSlot, outputFormat, width, height, referenceImagePaths, referenceRoles);
        var result = _aiImageAssetService.DrawAsync(project, plan, dryRun, (itemPlan, outputPath) => BuildAiImageReplacementPreview(project, itemPlan, outputPath)).GetAwaiter().GetResult();
        return BuildAiImageDrawPayload(result);
    }

    public object DrawAndReplaceCczImageAsset(
        string? gameRoot,
        string preset,
        string description,
        string? targetRelativePath,
        int? imageNumber,
        int? rImageId,
        int? sImageId,
        int? faceId,
        int? jobId,
        int factionSlot,
        string? outputFormat,
        int? width,
        int? height,
        List<string>? referenceImagePaths = null,
        List<string>? referenceRoles = null)
    {
        var project = LoadProject(gameRoot);
        EnsureWriteMode(project, "direct");
        var plan = _aiImageAssetService.BuildPromptPlan(project, preset, description, targetRelativePath, imageNumber, rImageId, sImageId, faceId, jobId, factionSlot, outputFormat, width, height, referenceImagePaths, referenceRoles);
        var draw = _aiImageAssetService.DrawAsync(project, plan, dryRun: false, (itemPlan, outputPath) => BuildAiImageReplacementPreview(project, itemPlan, outputPath)).GetAwaiter().GetResult();
        if (draw.Prepared == null)
        {
            throw new InvalidOperationException("AI image draw did not produce prepared files.");
        }

        var replacements = draw.Prepared.PreparedFiles.Select(file => ApplyPreparedAiImageFile(project, file)).ToList();
        return new
        {
            project.GameRoot,
            Draw = BuildAiImageDrawPayload(draw),
            ReplacementCount = replacements.Count,
            Replacements = replacements,
            SafetyNote = "AI image was generated, post-processed, and written directly to the target E5/DLL resource through dedicated backup/report/reread replacement services."
        };
    }

    public object PreviewItemIconField(string? gameRoot, int fieldValue, string? kind, int canvasSize)
    {
        var project = LoadProject(gameRoot);
        var normalizedKind = string.IsNullOrWhiteSpace(kind) ? "item" : kind;
        var mapping = new ItemIconMappingService().Resolve(project, fieldValue, normalizedKind);
        var preview = new ItemIconPreviewService().BuildPreview(
            project,
            fieldValue,
            mapping.ResourceRelativePath,
            mapping.Kind.Equals("strategy", StringComparison.OrdinalIgnoreCase) ? "strategy icon" : "item icon",
            NormalizeLimit(canvasSize, 96, 512));

        string exportPath = string.Empty;
        if (preview.Bitmap != null)
        {
            var exportRoot = Path.Combine(project.WorkspaceRoot, "CCZModStudio_Exports", "ImagePreviews");
            Directory.CreateDirectory(exportRoot);
            exportPath = Path.Combine(exportRoot, $"{DateTime.Now:yyyyMMdd_HHmmss_fff}_field_{fieldValue}_{mapping.Kind}.png");
            preview.Bitmap.Save(exportPath, ImageFormat.Png);
        }

        return new
        {
            project.GameRoot,
            Mapping = mapping,
            Preview = new
            {
                preview.SourcePath,
                preview.IconIndex,
                preview.AvailableIconCount,
                preview.RenderMode,
                preview.SelectionMode,
                HasBitmap = preview.Bitmap != null,
                ExportPath = exportPath,
                preview.Message,
                preview.Warnings
            },
            SafetyNote = "Read-only preview by table icon field value. 6.6 item icons use E5/Item.e5 small/large mapping; strategy icons use E5/Mtem.e5."
        };
    }

    public object ReplaceItemIconPair(string? gameRoot, int fieldValue, string largeSource, string? smallSource, string? writeMode)
    {
        var project = LoadProject(gameRoot);
        EnsureWriteMode(project, writeMode);
        var mapping = new ItemIconMappingService().Resolve(project, fieldValue, "item");
        if (!Ccz66RevisedLayout.Is66(project))
        {
            throw new InvalidOperationException("replace_item_icon_pair is the 6.6 E5 item-icon pair writer. Use DLL icon tools for pre-6.6 projects.");
        }

        if (!mapping.InRange || !mapping.SmallImageNumber.HasValue)
        {
            throw new InvalidOperationException("Item icon field mapping is not writable: " + mapping.Error);
        }

        var largePath = ResolveExternalFile(project, largeSource);
        var smallPath = string.IsNullOrWhiteSpace(smallSource) ? string.Empty : ResolveExternalFile(project, smallSource);
        var normalizer = new ItemIconRasterNormalizeService();
        ItemIconRasterPair pair;
        if (string.IsNullOrWhiteSpace(smallPath))
        {
            pair = normalizer.NormalizePairFromFile(largePath);
        }
        else
        {
            using var largeImage = System.Drawing.Image.FromFile(largePath);
            using var smallImage = System.Drawing.Image.FromFile(smallPath);
            var large = normalizer.NormalizeToSize(largeImage, ItemIconRasterNormalizeService.LargeIconSize, "large");
            var small = normalizer.NormalizeToSize(smallImage, ItemIconRasterNormalizeService.SmallIconSize, "small");
            pair = new ItemIconRasterPair($"{smallPath};{largePath}", largeImage.Width, largeImage.Height, small, large);
        }

        var requests = new[]
        {
            new E5ImageBatchReplaceRequest
            {
                ImageNumber = mapping.SmallImageNumber.Value,
                SourceBytes = pair.Small.BmpBytes,
                SourceLabel = string.IsNullOrWhiteSpace(smallPath) ? largePath + " (normalized small 16x16)" : smallPath + " (normalized small 16x16)",
                OperationKind = "replace_item_icon_pair small"
            },
            new E5ImageBatchReplaceRequest
            {
                ImageNumber = mapping.LargeImageNumber,
                SourceBytes = pair.Large.BmpBytes,
                SourceLabel = largePath + " (normalized large 32x32)",
                OperationKind = "replace_item_icon_pair large"
            }
        };
        var result = _e5ImageReplace.ReplaceBatch(project, mapping.ResourcePath, requests);
        return new
        {
            project.GameRoot,
            Mapping = mapping,
            NormalizeSummary = pair.Summary,
            Result = BuildE5ImageBatchReplacePayload(result),
            SafetyNote = "6.6 Item.e5 pair write: small is normalized to 16x16 BMP, large to 32x32 BMP, with backup/report/reread validation."
        };
    }

    public object BuildRsPixelCharacterDesign(
        string? gameRoot,
        string packageId,
        string displayName,
        string unitType,
        string designImagePath,
        string? formatActionImagePath,
        string? formatReferenceFolder,
        string? formatReferenceGameRoot,
        int? formatReferenceSImageId,
        int? formatReferenceRowId,
        string? formatReferenceDisplayName,
        string characterBrief,
        string weaponBrief,
        string forbiddenReadings,
        bool generateNow,
        bool dryRun,
        int? rImageId,
        int? sImageId,
        int? jobId,
        int factionSlot)
    {
        var project = LoadProject(gameRoot);
        var request = new RsPixelCharacterDesignRequest
        {
            PackageId = packageId,
            DisplayName = displayName,
            UnitType = unitType,
            DesignImagePath = designImagePath,
            FormatActionImagePath = formatActionImagePath,
            FormatReferenceFolder = formatReferenceFolder,
            FormatReferenceGameRoot = string.IsNullOrWhiteSpace(formatReferenceGameRoot) ? null : ResolveExternalDirectory(project, formatReferenceGameRoot),
            FormatReferenceSImageId = formatReferenceSImageId,
            FormatReferenceRowId = formatReferenceRowId,
            FormatReferenceDisplayName = formatReferenceDisplayName,
            CharacterBrief = characterBrief,
            WeaponBrief = weaponBrief,
            ForbiddenReadings = forbiddenReadings,
            GenerateNow = generateNow,
            DryRun = dryRun,
            RImageId = rImageId,
            SImageId = sImageId,
            JobId = jobId,
            FactionSlot = factionSlot
        };
        var result = _rsPixelCharacterDesignService.Build(project, request, (itemPlan, outputPath) => BuildAiImageReplacementPreview(project, itemPlan, outputPath));
        return BuildRsPixelCharacterDesignPayload(result);
    }

    public object CreateRsPixelEditWorkspace(
        string? gameRoot,
        string packageId,
        string displayName,
        string unitType,
        string designImagePath,
        string formatReferenceRoot,
        bool overwriteExisting)
    {
        var project = LoadProject(gameRoot);
        var result = _rsPixelEditWorkspaceService.CreateWorkspace(project, new RsPixelEditWorkspaceRequest
        {
            PackageId = packageId,
            DisplayName = displayName,
            UnitType = unitType,
            DesignImagePath = designImagePath,
            FormatReferenceRoot = formatReferenceRoot,
            OverwriteExisting = overwriteExisting
        });
        return BuildRsPixelEditWorkspacePayload(result);
    }

    public object BuildRsPixelEditPlan(
        string packageRoot,
        string unitType,
        string characterBrief,
        string weaponBrief)
    {
        var result = _rsPixelEditWorkspaceService.BuildPlan(new RsPixelEditPlanRequest
        {
            PackageRoot = packageRoot,
            UnitType = unitType,
            CharacterBrief = characterBrief,
            WeaponBrief = weaponBrief
        });
        return BuildRsPixelEditPlanPayload(result);
    }

    public object ApplyRsPixelFrameEdits(
        string packageRoot,
        List<RsPixelFrameEditOperation> operations)
    {
        var result = _rsPixelEditWorkspaceService.ApplyEdits(new RsPixelFrameEditBatchRequest
        {
            PackageRoot = packageRoot,
            Operations = operations
        });
        return BuildRsPixelFrameEditBatchPayload(result);
    }

    public object ExportRsPixelContactSheets(
        string packageRoot,
        int scale,
        bool annotate)
    {
        var result = _rsPixelEditWorkspaceService.ExportContactSheets(new RsPixelContactSheetRequest
        {
            PackageRoot = packageRoot,
            Scale = scale,
            Annotate = annotate
        });
        return BuildRsPixelContactSheetPayload(result);
    }

    public object ValidateRsPixelEditWorkspace(
        string? gameRoot,
        string packageRoot,
        int? rImageId,
        int? sImageId,
        int? jobId,
        int factionSlot)
    {
        var project = LoadProject(gameRoot);
        var result = _rsPixelEditWorkspaceService.Validate(project, new RsPixelEditValidationRequest
        {
            PackageRoot = packageRoot,
            RImageId = rImageId,
            SImageId = sImageId,
            JobId = jobId,
            FactionSlot = factionSlot
        });
        return BuildRsPixelEditValidationPayload(result);
    }

    public object BuildRsPixelSampleLearningMvp(
        string? gameRoot,
        string unitType,
        string outputId,
        bool overwriteExisting,
        int topReviewCount,
        List<string>? referenceRoots,
        List<string>? negativeRoots)
    {
        var project = LoadProject(gameRoot);
        var result = _rsPixelSampleLearningService.Build(project, new RsPixelSampleLearningRequest
        {
            UnitType = unitType,
            OutputId = outputId,
            OverwriteExisting = overwriteExisting,
            TopReviewCount = topReviewCount,
            ReferenceRoots = referenceRoots ?? [],
            NegativeRoots = negativeRoots ?? []
        });
        return new
        {
            result.UnitType,
            result.OutputRoot,
            result.CandidateCount,
            result.CompleteCandidateCount,
            result.RGroupCount,
            result.SGroupCount,
            result.StrongMachineCandidateCount,
            result.PartialCandidateCount,
            result.NegativeCandidateCount,
            result.Reports,
            result.ContactSheets,
            result.Warnings,
            result.SafetyNote
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
                entry.StoredLength,
                entry.DecodedLength,
                entry.IsCompressed,
                IndexOffsetHex = HexDisplayFormatter.FormatOffset(entry.IndexOffset),
                DataOffsetHex = HexDisplayFormatter.FormatOffset(entry.DataOffset),
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

    public object PreviewE5ImageBatchReplace(string? gameRoot, string targetRelativePath, List<E5ImageBatchUpdate> updates)
    {
        var project = LoadProject(gameRoot);
        var targetPath = ResolveProjectFile(project, targetRelativePath, mustExist: true);
        EnsureE5ImageTargetAllowed(project, targetPath);
        var requests = BuildE5ImageBatchRequests(project, updates);
        var preview = _e5ImageReplace.PreviewBatchReplacement(project, targetPath, requests);
        return BuildE5ImageBatchReplacePayload(preview);
    }

    public object ReplaceE5ImageBatch(string? gameRoot, string targetRelativePath, List<E5ImageBatchUpdate> updates, string? writeMode)
    {
        var project = LoadProject(gameRoot);
        EnsureWriteMode(project, writeMode);
        var targetPath = ResolveProjectFile(project, targetRelativePath, mustExist: true);
        EnsureE5ImageTargetAllowed(project, targetPath);
        var requests = BuildE5ImageBatchRequests(project, updates);
        var result = _e5ImageReplace.ReplaceBatch(project, targetPath, requests);
        return new
        {
            result.BackupPath,
            result.ReportPath,
            result.ReportJsonPath,
            Preview = BuildE5ImageBatchReplacePayload(result)
        };
    }

    public object PreviewDllIconReplace(string? gameRoot, string targetRelativePath, int iconIndex, string replacementPath)
    {
        var project = LoadProject(gameRoot);
        if (TryBuildObsolete66DllIconPayload(project, targetRelativePath, iconIndex, out var obsolete))
        {
            return obsolete;
        }

        var targetPath = ResolveDllIconTarget(project, targetRelativePath);
        var sourcePath = ResolveExternalFile(project, replacementPath);
        var preview = _iconResourceReplace.PreviewReplaceBitmapIcon(project, targetPath, iconIndex, sourcePath);
        return BuildDllIconReplacePayload(preview);
    }

    public object ReplaceDllIcon(string? gameRoot, string targetRelativePath, int iconIndex, string replacementPath, string? writeMode)
    {
        var project = LoadProject(gameRoot);
        EnsureWriteMode(project, writeMode);
        if (TryBuildObsolete66DllIconPayload(project, targetRelativePath, iconIndex, out var obsolete))
        {
            return obsolete;
        }

        var targetPath = ResolveDllIconTarget(project, targetRelativePath);
        var sourcePath = ResolveExternalFile(project, replacementPath);
        var result = _iconResourceReplace.ReplaceBitmapIcon(project, targetPath, iconIndex, sourcePath);
        return new
        {
            result.BackupPath,
            result.ReportPath,
            result.ReportJsonPath,
            Preview = BuildDllIconReplacePayload(result),
            result.NewFileSizeBytes,
            result.ChangedBytesEstimate,
            result.NewFileSha256
        };
    }

    public object PreviewClearDllIcon(string? gameRoot, string targetRelativePath, int iconIndex)
    {
        var project = LoadProject(gameRoot);
        if (TryBuildObsolete66DllIconPayload(project, targetRelativePath, iconIndex, out var obsolete))
        {
            return obsolete;
        }

        var targetPath = ResolveDllIconTarget(project, targetRelativePath);
        var preview = _iconResourceReplace.PreviewClearBitmapIcon(project, targetPath, iconIndex);
        return BuildDllIconReplacePayload(preview);
    }

    public object ClearDllIcon(string? gameRoot, string targetRelativePath, int iconIndex, string? writeMode)
    {
        var project = LoadProject(gameRoot);
        EnsureWriteMode(project, writeMode);
        if (TryBuildObsolete66DllIconPayload(project, targetRelativePath, iconIndex, out var obsolete))
        {
            return obsolete;
        }

        var targetPath = ResolveDllIconTarget(project, targetRelativePath);
        var result = _iconResourceReplace.ClearBitmapIcon(project, targetPath, iconIndex);
        return new
        {
            result.BackupPath,
            result.ReportPath,
            result.ReportJsonPath,
            Preview = BuildDllIconReplacePayload(result),
            result.NewFileSizeBytes,
            result.ChangedBytesEstimate,
            result.NewFileSha256
        };
    }

    public object ReplaceResource(string? gameRoot, string targetRelativePath, string replacementPath, string? writeMode, bool requireSameExtension)
    {
        var project = LoadProject(gameRoot);
        EnsureWriteMode(project, writeMode);
        var targetPath = ResolveProjectFile(project, targetRelativePath, mustExist: true);
        EnsureGenericResourceAllowed(project, targetPath);
        var sourcePath = ResolveExternalFile(project, replacementPath);

        var result = _resourceReplace.Replace(project, targetPath, sourcePath, requireSameExtension);

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

    private object ApplyPreparedAiImageFile(CczProject project, AiImagePreparedFile file)
    {
        var targetPath = ResolveProjectFile(project, file.TargetRelativePath, mustExist: true);
        if (Path.GetExtension(targetPath).Equals(".dll", StringComparison.OrdinalIgnoreCase))
        {
            var dllPath = ResolveDllIconTarget(project, file.TargetRelativePath);
            var results = file.TargetImageNumbers.Select(iconIndex =>
            {
                var replace = _iconResourceReplace.ReplaceBitmapIcon(project, dllPath, iconIndex, file.OutputPath);
                return new
                {
                    Kind = "dll_icon",
                    IconIndex = iconIndex,
                    replace.BackupPath,
                    replace.ReportPath,
                    replace.ReportJsonPath,
                    Preview = BuildDllIconReplacePayload(replace),
                    replace.NewFileSizeBytes,
                    replace.ChangedBytesEstimate,
                    replace.NewFileSha256
                };
            }).ToList();
            return new
            {
                file.Role,
                file.TargetRelativePath,
                file.OutputPath,
                Results = results
            };
        }

        if (Path.GetExtension(targetPath).Equals(".e5", StringComparison.OrdinalIgnoreCase))
        {
            EnsureE5ImageTargetAllowed(project, targetPath);
            var requests = file.TargetImageNumbers
                .Select(imageNumber => new E5ImageBatchReplaceRequest
                {
                    ImageNumber = imageNumber,
                    SourcePath = file.OutputPath,
                    SourceLabel = file.OutputPath,
                    OperationKind = "AI image direct replacement"
                })
                .ToList();
            var replace = _e5ImageReplace.ReplaceBatch(project, targetPath, requests);
            return new
            {
                Kind = "e5_image_batch",
                file.Role,
                file.TargetRelativePath,
                file.OutputPath,
                file.TargetImageNumbers,
                replace.BackupPath,
                replace.ReportPath,
                replace.ReportJsonPath,
                Preview = BuildE5ImageBatchReplacePayload(replace)
            };
        }

        throw new InvalidOperationException("AI image direct replacement supports only E5 image resources and DLL bitmap icons.");
    }

}
