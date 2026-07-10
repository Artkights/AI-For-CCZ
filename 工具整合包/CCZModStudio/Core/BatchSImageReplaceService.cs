using System.Drawing;
using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Unicode;
using CCZModStudio.Models;

namespace CCZModStudio.Core;

public sealed class BatchSImageReplaceService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.Create(UnicodeRanges.All)
    };

    private readonly E5TrueColorImageCodec _codec = new();
    private readonly E5ImageReplaceService _replace = new();
    private readonly SImageMaterialLayoutResolver _layoutResolver = new();

    public SImageBatchMaterialScanSummary ScanMaterialRoot(string materialRoot)
    {
        var scan = BuildMaterialRootScan(materialRoot);
        return BuildScanSummary(scan, filteredUnusedDirectories: 0, matchedMaterialDirectories: 0, blockingDirectories: 0);
    }

    public BatchSImageReplacePreviewResult Preview(CczProject project, BatchSImageReplaceRequest request)
    {
        var materialRoot = ResolveMaterialRoot(request.MaterialRoot);
        var requestedStages = NormalizeRequestedStageSlots(request.StageSlots);
        var allowedUsages = NormalizeAllowedUsages(request.AllowedSImageUsages);
        var allowedIds = allowedUsages.Select(usage => usage.SImageId).ToHashSet();
        var filterToAllowedIds = request.IncludeOnlySelectedOrFiltered &&
                                 !request.IncludeAllRecognizedSDirectories &&
                                 allowedIds.Count > 0;

        var materialScan = BuildMaterialRootScan(materialRoot);
        var skipped = new List<BatchImageImportSkippedItem>();
        var warnings = new List<string>();
        var filteredUnusedDirectories = 0;

        foreach (var directory in materialScan.InvalidNameDirectories)
        {
            skipped.Add(Skip(directory.DisplayName, directory.Folder, BatchImageImportSkipReasons.InvalidName));
        }

        foreach (var candidate in materialScan.DuplicateCandidates)
        {
            skipped.Add(Skip(
                candidate.SImageId.ToString(CultureInfo.InvariantCulture),
                candidate.Folder,
                BatchImageImportSkipReasons.DuplicateId));
        }

        var selectedCandidates = new List<SImageMaterialFolderCandidate>();
        foreach (var candidate in materialScan.UniqueCandidates)
        {
            if (filterToAllowedIds && !allowedIds.Contains(candidate.SImageId))
            {
                filteredUnusedDirectories++;
                skipped.Add(Skip(
                    candidate.SImageId.ToString(CultureInfo.InvariantCulture),
                    candidate.Folder,
                    BatchImageImportSkipReasons.Unused,
                    "S id is not referenced by the current visible/filtered character image rows."));
                continue;
            }

            selectedCandidates.Add(candidate);
        }

        var items = new List<BatchSImageReplaceItemPreview>();
        var movRequests = new List<E5ImageBatchReplaceRequest>();
        var atkRequests = new List<E5ImageBatchReplaceRequest>();
        var spcRequests = new List<E5ImageBatchReplaceRequest>();

        foreach (var candidate in selectedCandidates)
        {
            var usages = ResolveUsages(candidate.SImageId, allowedUsages, request.FactionSlot, request.IncludeAllRecognizedSDirectories);
            if (usages.Count == 0)
            {
                skipped.Add(Skip(
                    candidate.SImageId.ToString(CultureInfo.InvariantCulture),
                    candidate.Folder,
                    BatchImageImportSkipReasons.Unused,
                    "S id is not in the requested import scope."));
                continue;
            }

            foreach (var usage in usages)
            {
                var mapping = CharacterImageResourceService.ResolveSUnitImageMapping(project, usage.SImageId, usage.JobId, usage.FactionSlot);
                if (mapping.ImageNumbers.Count == 0)
                {
                    skipped.Add(Skip(BuildUsageKey(usage), candidate.Folder, BatchImageImportSkipReasons.MissingFile, mapping.Detail));
                    continue;
                }

                var stageTargets = CharacterImageResourceService.ResolveSImageStageTargets(
                    project,
                    mapping,
                    requestedStages,
                    defaultAllStages: true);
                if (stageTargets.Count == 0)
                {
                    var detail = request.StageSlots.Count == 0
                        ? "No writable S image stage is available."
                        : $"Selected stages {string.Join("/", request.StageSlots)} do not apply to this S image.";
                    skipped.Add(Skip(BuildUsageKey(usage), candidate.Folder, BatchImageImportSkipReasons.Unused, detail));
                    continue;
                }

                var layout = _layoutResolver.Resolve(candidate.Folder, stageTargets);
                foreach (var stageFiles in layout.StageFiles)
                {
                    var stageTarget = stageTargets.Single(target => target.StageSlot == stageFiles.StageSlot);
                    if (!stageFiles.IsComplete)
                    {
                        skipped.Add(Skip(
                            BuildUsageKey(usage, stageTarget.StageSlot),
                            candidate.Folder,
                            BatchImageImportSkipReasons.MissingFile,
                            string.Join(", ", stageFiles.MissingFiles)));
                        continue;
                    }

                    if (!TryEncode(project, candidate, stageTarget, stageFiles, skipped, warnings, out var encodes))
                    {
                        continue;
                    }

                    items.Add(new BatchSImageReplaceItemPreview
                    {
                        SImageId = usage.SImageId,
                        JobId = usage.JobId,
                        FactionSlot = usage.FactionSlot,
                        StageSlot = stageTarget.StageSlot,
                        StageName = stageTarget.DisplayName,
                        ImageNumber = stageTarget.ImageNumber,
                        MaterialFolder = candidate.Folder,
                        ImageNumbers = new[] { stageTarget.ImageNumber },
                        MappingDetail = mapping.Detail,
                        MovSourcePath = stageFiles.MovPath,
                        AtkSourcePath = stageFiles.AtkPath,
                        SpcSourcePath = stageFiles.SpcPath,
                        MovEncode = encodes.Mov,
                        AtkEncode = encodes.Atk,
                        SpcEncode = encodes.Spc
                    });

                    movRequests.Add(BuildRequest(stageTarget.ImageNumber, encodes.Mov, usage, stageTarget.DisplayName, "mov"));
                    atkRequests.Add(BuildRequest(stageTarget.ImageNumber, encodes.Atk, usage, stageTarget.DisplayName, "atk"));
                    spcRequests.Add(BuildRequest(stageTarget.ImageNumber, encodes.Spc, usage, stageTarget.DisplayName, "spc"));
                }
            }
        }

        var normalizedMovRequests = NormalizeTargetRequests("Unit_mov.e5", movRequests, skipped);
        var normalizedAtkRequests = NormalizeTargetRequests("Unit_atk.e5", atkRequests, skipped);
        var normalizedSpcRequests = NormalizeTargetRequests("Unit_spc.e5", spcRequests, skipped);
        var hasDuplicateTargets = HasDifferentSourceDuplicateTargets(normalizedMovRequests) ||
                                  HasDifferentSourceDuplicateTargets(normalizedAtkRequests) ||
                                  HasDifferentSourceDuplicateTargets(normalizedSpcRequests);

        var filePreviews = new Dictionary<string, E5ImageBatchReplacePreviewResult>(StringComparer.OrdinalIgnoreCase);
        if (!hasDuplicateTargets)
        {
            AddFilePreview(project, filePreviews, "Unit_mov.e5", normalizedMovRequests);
            AddFilePreview(project, filePreviews, "Unit_atk.e5", normalizedAtkRequests);
            AddFilePreview(project, filePreviews, "Unit_spc.e5", normalizedSpcRequests);
        }

        var matchedFolders = items
            .Select(item => item.MaterialFolder)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();
        var scan = BuildScanSummary(
            materialScan,
            filteredUnusedDirectories,
            matchedFolders,
            CountBlockingMaterialDirectories(skipped, materialScan.RecognizedCandidates));

        return new BatchSImageReplacePreviewResult
        {
            Request = request,
            MaterialScan = scan,
            Items = items.OrderBy(item => item.SImageId).ThenBy(item => item.JobId ?? -1).ThenBy(item => item.FactionSlot).ThenBy(item => item.StageSlot).ToArray(),
            SkippedItems = skipped.ToArray(),
            Warnings = warnings.Distinct(StringComparer.Ordinal).ToArray(),
            FilePreviews = filePreviews
        };
    }

    public BatchSImageReplaceResult Replace(CczProject project, BatchSImageReplaceRequest request)
    {
        var preview = Preview(project, request);
        if (!preview.CanWrite)
        {
            throw new InvalidOperationException("Batch S image import has blocking skipped items.");
        }

        if (preview.Items.Count == 0)
        {
            throw new InvalidOperationException("Batch S image import has no writable items.");
        }

        var writeResults = new Dictionary<string, E5ImageBatchReplaceResult>(StringComparer.OrdinalIgnoreCase);
        foreach (var plan in BuildGroupedRequests(preview.Items).Where(plan => plan.Requests.Count > 0))
        {
            var targetPath = CharacterImageResourceService.ResolveGameFile(project, plan.FileName);
            writeResults[plan.FileName] = _replace.ReplaceBatch(project, targetPath, plan.Requests);
        }

        var payload = new BatchSImageReplaceResult
        {
            Request = preview.Request,
            MaterialScan = preview.MaterialScan,
            Items = preview.Items,
            SkippedItems = preview.SkippedItems,
            Warnings = preview.Warnings,
            FilePreviews = preview.FilePreviews,
            WriteResults = writeResults
        };

        return new BatchSImageReplaceResult
        {
            Request = payload.Request,
            MaterialScan = payload.MaterialScan,
            Items = payload.Items,
            SkippedItems = payload.SkippedItems,
            Warnings = payload.Warnings,
            FilePreviews = payload.FilePreviews,
            WriteResults = payload.WriteResults,
            AggregateReportPath = WriteAggregateReport(project, payload)
        };
    }

    private static IReadOnlyList<BatchSImageUsage> NormalizeAllowedUsages(IReadOnlyList<BatchSImageUsage> allowedUsages)
        => allowedUsages.Count > 0
            ? allowedUsages
                .Select(usage => new BatchSImageUsage(
                    usage.SImageId,
                    usage.JobId,
                    CharacterImageResourceService.NormalizeSPreviewFactionSlot(usage.FactionSlot)))
                .Distinct()
                .ToArray()
            : Array.Empty<BatchSImageUsage>();

    private MaterialRootScan BuildMaterialRootScan(string materialRoot)
    {
        var root = ResolveMaterialRoot(materialRoot);
        var folders = Directory.EnumerateDirectories(root)
            .OrderBy(path => path, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();
        var invalidNameDirectories = new List<MaterialDirectoryScanItem>();
        var recognized = new List<SImageMaterialFolderCandidate>();

        foreach (var folder in folders)
        {
            var displayName = Path.GetFileName(folder);
            if (!SImageMaterialLayoutResolver.TryParseSFolderId(displayName, out var id))
            {
                invalidNameDirectories.Add(new MaterialDirectoryScanItem(folder, displayName));
                continue;
            }

            recognized.Add(new SImageMaterialFolderCandidate
            {
                SImageId = id,
                Folder = folder,
                DisplayName = displayName
            });
        }

        var duplicateCandidates = recognized
            .GroupBy(candidate => candidate.SImageId)
            .Where(group => group.Count() > 1)
            .SelectMany(group => group)
            .ToArray();
        var duplicateFolders = duplicateCandidates
            .Select(candidate => candidate.Folder)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var uniqueCandidates = recognized
            .Where(candidate => !duplicateFolders.Contains(candidate.Folder))
            .ToArray();
        var duplicateExamples = recognized
            .GroupBy(candidate => candidate.SImageId)
            .Where(group => group.Count() > 1)
            .OrderBy(group => group.Key)
            .Select(group =>
                $"S{group.Key.ToString(CultureInfo.InvariantCulture)}: {string.Join(", ", group.Select(candidate => candidate.DisplayName))}")
            .ToArray();
        var layouts = recognized
            .OrderBy(candidate => candidate.SImageId)
            .ThenBy(candidate => candidate.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .Select(candidate => _layoutResolver.InspectFolder(candidate.Folder, candidate.SImageId, candidate.DisplayName))
            .ToArray();

        return new MaterialRootScan(
            root,
            folders,
            invalidNameDirectories,
            recognized,
            duplicateCandidates,
            uniqueCandidates,
            duplicateExamples,
            layouts);
    }

    private static SImageBatchMaterialScanSummary BuildScanSummary(
        MaterialRootScan scan,
        int filteredUnusedDirectories,
        int matchedMaterialDirectories,
        int blockingDirectories)
        => new()
        {
            TotalChildDirectories = scan.ChildFolders.Count,
            RecognizedSDirectories = scan.RecognizedCandidates.Count,
            RecognizedSIds = scan.RecognizedCandidates
                .Select(candidate => candidate.SImageId)
                .Distinct()
                .OrderBy(id => id)
                .ToArray(),
            InvalidNameDirectories = scan.InvalidNameDirectories.Count,
            InvalidNameExamples = scan.InvalidNameDirectories
                .Select(directory => directory.DisplayName)
                .Take(20)
                .ToArray(),
            DuplicateIdDirectories = scan.DuplicateCandidates.Count,
            DuplicateIdExamples = scan.DuplicateIdExamples.Take(20).ToArray(),
            FilteredUnusedDirectories = filteredUnusedDirectories,
            MatchedMaterialDirectories = matchedMaterialDirectories,
            BlockingDirectories = blockingDirectories,
            RecognizedFolders = scan.RecognizedCandidates
                .OrderBy(candidate => candidate.SImageId)
                .ThenBy(candidate => candidate.DisplayName, StringComparer.CurrentCultureIgnoreCase)
                .ToArray(),
            FolderLayoutSummaries = scan.FolderLayoutSummaries
        };

    private static int CountBlockingMaterialDirectories(
        IReadOnlyList<BatchImageImportSkippedItem> skipped,
        IReadOnlyList<SImageMaterialFolderCandidate> recognized)
    {
        var recognizedFolders = recognized
            .Select(candidate => candidate.Folder)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in skipped.Where(item => BatchImageImportSkipReasons.IsBlocking(item.Reason)))
        {
            if (recognizedFolders.Contains(item.SourcePath))
            {
                result.Add(item.SourcePath);
                continue;
            }

            var owner = recognizedFolders
                .Where(folder => item.SourcePath.StartsWith(folder, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(folder => folder.Length)
                .FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(owner))
            {
                result.Add(owner);
            }
        }

        return result.Count;
    }

    private bool TryEncode(
        CczProject project,
        SImageMaterialFolderCandidate candidate,
        SImageStageTarget stageTarget,
        SImageMaterialStageFiles stageFiles,
        List<BatchImageImportSkippedItem> skipped,
        List<string> warnings,
        out StageEncodeSet encodes)
    {
        encodes = default;
        if (!TryEncodeFile(project, candidate, stageTarget, stageFiles.MovPath, E5RawImageCodec.UnitMovSpec, "mov", skipped, out var movEncode) ||
            !TryEncodeFile(project, candidate, stageTarget, stageFiles.AtkPath, E5RawImageCodec.UnitAtkSpec, "atk", skipped, out var atkEncode) ||
            !TryEncodeFile(project, candidate, stageTarget, stageFiles.SpcPath, E5RawImageCodec.UnitSpcSpec, "spc", skipped, out var spcEncode))
        {
            return false;
        }

        warnings.AddRange(movEncode.Warnings.Select(warning => $"S{candidate.SImageId} {stageTarget.DisplayName} mov: {warning}"));
        warnings.AddRange(atkEncode.Warnings.Select(warning => $"S{candidate.SImageId} {stageTarget.DisplayName} atk: {warning}"));
        warnings.AddRange(spcEncode.Warnings.Select(warning => $"S{candidate.SImageId} {stageTarget.DisplayName} spc: {warning}"));
        encodes = new StageEncodeSet(movEncode, atkEncode, spcEncode);
        return true;
    }

    private bool TryEncodeFile(
        CczProject project,
        SImageMaterialFolderCandidate candidate,
        SImageStageTarget stageTarget,
        string sourcePath,
        E5RawImageSpec spec,
        string actionName,
        List<BatchImageImportSkippedItem> skipped,
        out E5TrueColorEncodeResult encode)
    {
        encode = new E5TrueColorEncodeResult();
        try
        {
            if (TryReadImageDimensions(sourcePath, out var width, out var height) &&
                (width != spec.Width ||
                 (spec.StrictStripHeight.HasValue && height != spec.StrictStripHeight.Value)))
            {
                var expectedHeight = spec.StrictStripHeight?.ToString(CultureInfo.InvariantCulture) ?? $"multiple of {spec.FrameHeight.ToString(CultureInfo.InvariantCulture)}";
                skipped.Add(Skip(
                    $"S{candidate.SImageId}/turn{stageTarget.StageSlot}/{actionName}",
                    sourcePath,
                    BatchImageImportSkipReasons.InvalidSize,
                    $"expected {spec.Width.ToString(CultureInfo.InvariantCulture)}x{expectedHeight}, actual {width.ToString(CultureInfo.InvariantCulture)}x{height.ToString(CultureInfo.InvariantCulture)}."));
                return false;
            }

            encode = _codec.EncodeFile(project, sourcePath, spec, strictHeight: true);
            return true;
        }
        catch (InvalidOperationException ex)
        {
            skipped.Add(Skip(
                $"S{candidate.SImageId}/turn{stageTarget.StageSlot}/{actionName}",
                sourcePath,
                ResolveEncodeFailureReason(ex),
                ex.Message));
            return false;
        }
        catch (Exception ex) when (ex is ArgumentException || ex is NotSupportedException || ex is System.Runtime.InteropServices.ExternalException)
        {
            skipped.Add(Skip(
                $"S{candidate.SImageId}/turn{stageTarget.StageSlot}/{actionName}",
                sourcePath,
                BatchImageImportSkipReasons.InvalidFormat,
                ex.Message));
            return false;
        }
    }

    private static bool TryReadImageDimensions(string sourcePath, out int width, out int height)
    {
        width = 0;
        height = 0;
        try
        {
            using var bitmap = new Bitmap(sourcePath);
            width = bitmap.Width;
            height = bitmap.Height;
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static string ResolveEncodeFailureReason(InvalidOperationException ex)
    {
        var message = ex.Message;
        return message.Contains("尺寸", StringComparison.Ordinal) ||
               message.Contains("宽度", StringComparison.Ordinal) ||
               message.Contains("高度", StringComparison.Ordinal) ||
               message.Contains("size", StringComparison.OrdinalIgnoreCase) ||
               message.Contains("width", StringComparison.OrdinalIgnoreCase) ||
               message.Contains("height", StringComparison.OrdinalIgnoreCase)
            ? BatchImageImportSkipReasons.InvalidSize
            : BatchImageImportSkipReasons.InvalidFormat;
    }

    private static IReadOnlyList<(string FileName, IReadOnlyList<E5ImageBatchReplaceRequest> Requests)> BuildGroupedRequests(
        IReadOnlyList<BatchSImageReplaceItemPreview> items)
    {
        var mov = new List<E5ImageBatchReplaceRequest>();
        var atk = new List<E5ImageBatchReplaceRequest>();
        var spc = new List<E5ImageBatchReplaceRequest>();
        foreach (var item in items)
        {
            var usage = new BatchSImageUsage(item.SImageId, item.JobId, item.FactionSlot);
            mov.Add(BuildRequest(item.ImageNumber, item.MovEncode, usage, item.StageName, "mov"));
            atk.Add(BuildRequest(item.ImageNumber, item.AtkEncode, usage, item.StageName, "atk"));
            spc.Add(BuildRequest(item.ImageNumber, item.SpcEncode, usage, item.StageName, "spc"));
        }

        return new[]
        {
            ("Unit_mov.e5", NormalizeTargetRequestsForWrite(mov)),
            ("Unit_atk.e5", NormalizeTargetRequestsForWrite(atk)),
            ("Unit_spc.e5", NormalizeTargetRequestsForWrite(spc))
        };
    }

    private static IReadOnlyList<E5ImageBatchReplaceRequest> NormalizeTargetRequests(
        string fileName,
        IReadOnlyList<E5ImageBatchReplaceRequest> requests,
        List<BatchImageImportSkippedItem> skipped)
    {
        var normalized = requests
            .GroupBy(request => new RequestIdentity(request.ImageNumber, request.SourceLabel), RequestIdentityComparer.Instance)
            .Select(group => group.First())
            .ToArray();

        foreach (var duplicate in normalized.GroupBy(request => request.ImageNumber).Where(group => group.Select(x => x.SourceLabel).Distinct(StringComparer.Ordinal).Count() > 1))
        {
            skipped.Add(Skip(
                $"#{duplicate.Key.ToString(CultureInfo.InvariantCulture)}",
                fileName,
                BatchImageImportSkipReasons.DuplicateTarget,
                "different material sources would write the same Unit image number."));
        }

        return normalized;
    }

    private static IReadOnlyList<E5ImageBatchReplaceRequest> NormalizeTargetRequestsForWrite(IReadOnlyList<E5ImageBatchReplaceRequest> requests)
        => requests
            .GroupBy(request => new RequestIdentity(request.ImageNumber, request.SourceLabel), RequestIdentityComparer.Instance)
            .Select(group => group.First())
            .OrderBy(request => request.ImageNumber)
            .ToArray();

    private static bool HasDifferentSourceDuplicateTargets(IReadOnlyList<E5ImageBatchReplaceRequest> requests)
        => requests.GroupBy(request => request.ImageNumber)
            .Any(group => group.Select(request => request.SourceLabel).Distinct(StringComparer.Ordinal).Count() > 1);

    private void AddFilePreview(
        CczProject project,
        Dictionary<string, E5ImageBatchReplacePreviewResult> filePreviews,
        string fileName,
        IReadOnlyList<E5ImageBatchReplaceRequest> requests)
    {
        if (requests.Count == 0) return;

        var target = CharacterImageResourceService.ResolveGameFile(project, fileName);
        filePreviews[fileName] = _replace.PreviewBatchReplacement(project, target, requests);
    }

    private static IReadOnlyList<BatchSImageUsage> ResolveUsages(
        int sImageId,
        IReadOnlyList<BatchSImageUsage> allowedUsages,
        int defaultFactionSlot,
        bool includeAllRecognized)
    {
        var usages = allowedUsages.Where(usage => usage.SImageId == sImageId).Distinct().ToArray();
        if (usages.Length > 0) return usages;

        return includeAllRecognized || allowedUsages.Count == 0
            ? new[] { new BatchSImageUsage(sImageId, null, CharacterImageResourceService.NormalizeSPreviewFactionSlot(defaultFactionSlot)) }
            : Array.Empty<BatchSImageUsage>();
    }

    private static E5ImageBatchReplaceRequest BuildRequest(
        int imageNumber,
        E5TrueColorEncodeResult encode,
        BatchSImageUsage usage,
        string stageName,
        string actionName)
        => new()
        {
            ImageNumber = imageNumber,
            SourceBytes = encode.ImageBytes,
            SourceBytesAreRaw = false,
            SourceLabel = $"{encode.SourcePath} -> {encode.StorageFormat}",
            OperationKind = $"batch S{usage.SImageId} {stageName} {actionName}"
        };

    private static IReadOnlyList<int> NormalizeRequestedStageSlots(IReadOnlyList<int> stageSlots)
        => stageSlots
            .Where(slot => slot is >= 1 and <= 3)
            .Distinct()
            .OrderBy(slot => slot)
            .ToArray();

    private static string ResolveMaterialRoot(string materialRoot)
    {
        if (string.IsNullOrWhiteSpace(materialRoot))
        {
            throw new InvalidOperationException("Missing batch S image material root.");
        }

        var root = Path.GetFullPath(materialRoot);
        if (!Directory.Exists(root))
        {
            throw new DirectoryNotFoundException($"Batch S image material root not found: {root}");
        }

        return root;
    }

    private static string BuildUsageKey(BatchSImageUsage usage)
        => usage.JobId.HasValue
            ? $"S{usage.SImageId}/job{usage.JobId.Value}/faction{usage.FactionSlot}"
            : $"S{usage.SImageId}/faction{usage.FactionSlot}";

    private static string BuildUsageKey(BatchSImageUsage usage, int stageSlot)
        => $"{BuildUsageKey(usage)}/turn{stageSlot}";

    private static BatchImageImportSkippedItem Skip(string key, string sourcePath, string reason, string detail = "")
        => new()
        {
            Key = key,
            SourcePath = sourcePath,
            Reason = string.IsNullOrWhiteSpace(detail) ? reason : $"{reason}: {detail}"
        };

    private static string WriteAggregateReport(CczProject project, BatchSImageReplaceResult result)
    {
        var backupRoot = Path.Combine(project.GameRoot, "_CCZModStudio_Backups");
        Directory.CreateDirectory(backupRoot);
        var stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss_fff", CultureInfo.InvariantCulture);
        var reportPath = Path.Combine(backupRoot, $"{stamp}_BatchSImageTrueColorReplaceReport.json");
        var payload = new
        {
            OperationKind = "Batch S image true-color replace",
            CreatedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
            ProjectRoot = project.GameRoot,
            result.Request.MaterialRoot,
            result.Request.IncludeOnlySelectedOrFiltered,
            result.Request.IncludeAllRecognizedSDirectories,
            result.MaterialScan,
            result.TotalOperationCount,
            result.Warnings,
            result.SkippedItems,
            Targets = result.WriteResults.Select(pair => new
            {
                FileName = pair.Key,
                pair.Value.TargetRelativePath,
                pair.Value.OperationCount,
                pair.Value.BackupPath,
                pair.Value.ReportPath,
                pair.Value.ReportJsonPath
            }),
            Items = result.Items.Select(item => new
            {
                item.SImageId,
                item.JobId,
                item.FactionSlot,
                item.StageSlot,
                item.StageName,
                item.ImageNumber,
                item.MaterialFolder,
                item.ImageNumbers,
                item.MappingDetail,
                item.MovSourcePath,
                item.AtkSourcePath,
                item.SpcSourcePath
            })
        };

        File.WriteAllText(reportPath, JsonSerializer.Serialize(payload, JsonOptions));
        return reportPath;
    }

    private readonly record struct StageEncodeSet(
        E5TrueColorEncodeResult Mov,
        E5TrueColorEncodeResult Atk,
        E5TrueColorEncodeResult Spc);

    private readonly record struct RequestIdentity(int ImageNumber, string SourceLabel);

    private sealed record MaterialDirectoryScanItem(string Folder, string DisplayName);

    private sealed record MaterialRootScan(
        string Root,
        IReadOnlyList<string> ChildFolders,
        IReadOnlyList<MaterialDirectoryScanItem> InvalidNameDirectories,
        IReadOnlyList<SImageMaterialFolderCandidate> RecognizedCandidates,
        IReadOnlyList<SImageMaterialFolderCandidate> DuplicateCandidates,
        IReadOnlyList<SImageMaterialFolderCandidate> UniqueCandidates,
        IReadOnlyList<string> DuplicateIdExamples,
        IReadOnlyList<SImageMaterialFolderLayoutSummary> FolderLayoutSummaries);

    private sealed class RequestIdentityComparer : IEqualityComparer<RequestIdentity>
    {
        public static readonly RequestIdentityComparer Instance = new();

        public bool Equals(RequestIdentity x, RequestIdentity y)
            => x.ImageNumber == y.ImageNumber && string.Equals(x.SourceLabel, y.SourceLabel, StringComparison.Ordinal);

        public int GetHashCode(RequestIdentity obj)
            => HashCode.Combine(obj.ImageNumber, StringComparer.Ordinal.GetHashCode(obj.SourceLabel));
    }
}
