using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Text.Unicode;
using CCZModStudio.Models;

namespace CCZModStudio.Core;

public sealed class BatchSImageReplaceService
{
    private static readonly Regex FolderIdRegex = new(@"^(?:S_?)?(\d+)$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.Create(UnicodeRanges.All)
    };

    private readonly E5TrueColorImageCodec _codec = new();
    private readonly E5ImageReplaceService _replace = new();

    public BatchSImageReplacePreviewResult Preview(CczProject project, BatchSImageReplaceRequest request)
    {
        var materialRoot = ResolveMaterialRoot(request.MaterialRoot);
        var requestedStages = NormalizeRequestedStageSlots(request.StageSlots);
        var allowedUsages = request.AllowedSImageUsages.Count > 0
            ? request.AllowedSImageUsages
                .Select(usage => new BatchSImageUsage(usage.SImageId, usage.JobId, CharacterImageResourceService.NormalizeSPreviewFactionSlot(usage.FactionSlot)))
                .Distinct()
                .ToArray()
            : Array.Empty<BatchSImageUsage>();
        var allowedIds = allowedUsages.Select(usage => usage.SImageId).ToHashSet();
        var folders = Directory.EnumerateDirectories(materialRoot)
            .OrderBy(path => path, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();
        var skipped = new List<BatchImageImportSkippedItem>();
        var warnings = new List<string>();
        var candidates = new List<(int Id, string Folder)>();

        foreach (var folder in folders)
        {
            var id = TryParseFolderId(folder);
            if (!id.HasValue)
            {
                skipped.Add(Skip(Path.GetFileName(folder), folder, BatchImageImportSkipReasons.InvalidName));
                continue;
            }

            if (request.IncludeOnlySelectedOrFiltered && allowedIds.Count > 0 && !allowedIds.Contains(id.Value))
            {
                skipped.Add(Skip(id.Value.ToString(CultureInfo.InvariantCulture), folder, BatchImageImportSkipReasons.Unused));
                continue;
            }

            candidates.Add((id.Value, folder));
        }

        foreach (var duplicate in candidates.GroupBy(x => x.Id).Where(group => group.Count() > 1))
        {
            foreach (var item in duplicate)
            {
                skipped.Add(Skip(item.Id.ToString(CultureInfo.InvariantCulture), item.Folder, BatchImageImportSkipReasons.DuplicateId));
            }
        }

        var items = new List<BatchSImageReplaceItemPreview>();
        var movRequests = new List<E5ImageBatchReplaceRequest>();
        var atkRequests = new List<E5ImageBatchReplaceRequest>();
        var spcRequests = new List<E5ImageBatchReplaceRequest>();
        foreach (var candidate in candidates.GroupBy(x => x.Id).Where(group => group.Count() == 1).Select(group => group.Single()))
        {
            var usages = ResolveUsages(candidate.Id, allowedUsages, request.FactionSlot);
            if (usages.Count == 0)
            {
                skipped.Add(Skip(candidate.Id.ToString(CultureInfo.InvariantCulture), candidate.Folder, BatchImageImportSkipReasons.Unused));
                continue;
            }

            foreach (var usage in usages)
            {
                var mapping = CharacterImageResourceService.ResolveSUnitImageMapping(usage.SImageId, usage.JobId, usage.FactionSlot);
                if (mapping.ImageNumbers.Count == 0)
                {
                    skipped.Add(Skip(BuildUsageKey(usage), candidate.Folder, BatchImageImportSkipReasons.MissingFile, mapping.Detail));
                    continue;
                }

                var stageTargets = CharacterImageResourceService.ResolveSImageStageTargets(
                    mapping,
                    requestedStages,
                    defaultAllStages: true);
                if (stageTargets.Count == 0)
                {
                    var detail = request.StageSlots.Count == 0
                        ? "没有可写入的 S 形象转数"
                        : $"所选转数 {string.Join("/", request.StageSlots)} 不适用于该 S 形象";
                    skipped.Add(Skip(BuildUsageKey(usage), candidate.Folder, BatchImageImportSkipReasons.Unused, detail));
                    continue;
                }

                foreach (var stageTarget in stageTargets)
                {
                    var movPath = ResolveMaterialFile(candidate.Folder, stageTarget.StageSlot, "mov.bmp");
                    var atkPath = ResolveMaterialFile(candidate.Folder, stageTarget.StageSlot, "atk.bmp");
                    var spcPath = ResolveMaterialFile(candidate.Folder, stageTarget.StageSlot, "spc.bmp");
                    var missing = new List<string>();
                    if (movPath == null) missing.Add($"turn{stageTarget.StageSlot}/mov.bmp 或 mov.bmp");
                    if (atkPath == null) missing.Add($"turn{stageTarget.StageSlot}/atk.bmp 或 atk.bmp");
                    if (spcPath == null) missing.Add($"turn{stageTarget.StageSlot}/spc.bmp 或 spc.bmp");
                    if (missing.Count > 0)
                    {
                        skipped.Add(Skip(
                            BuildUsageKey(usage, stageTarget.StageSlot),
                            candidate.Folder,
                            BatchImageImportSkipReasons.MissingFile,
                            string.Join(", ", missing)));
                        continue;
                    }

                    var movEncode = _codec.EncodeFile(project, movPath!, E5RawImageCodec.UnitMovSpec, strictHeight: true);
                    var atkEncode = _codec.EncodeFile(project, atkPath!, E5RawImageCodec.UnitAtkSpec, strictHeight: true);
                    var spcEncode = _codec.EncodeFile(project, spcPath!, E5RawImageCodec.UnitSpcSpec, strictHeight: true);
                    warnings.AddRange(movEncode.Warnings.Select(warning => $"S{candidate.Id} {stageTarget.DisplayName} mov: {warning}"));
                    warnings.AddRange(atkEncode.Warnings.Select(warning => $"S{candidate.Id} {stageTarget.DisplayName} atk: {warning}"));
                    warnings.AddRange(spcEncode.Warnings.Select(warning => $"S{candidate.Id} {stageTarget.DisplayName} spc: {warning}"));

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
                        MovSourcePath = movPath!,
                        AtkSourcePath = atkPath!,
                        SpcSourcePath = spcPath!,
                        MovEncode = movEncode,
                        AtkEncode = atkEncode,
                        SpcEncode = spcEncode
                    });

                    movRequests.AddRange(BuildRequests(new[] { stageTarget.ImageNumber }, movEncode, usage, stageTarget.DisplayName, "mov"));
                    atkRequests.AddRange(BuildRequests(new[] { stageTarget.ImageNumber }, atkEncode, usage, stageTarget.DisplayName, "atk"));
                    spcRequests.AddRange(BuildRequests(new[] { stageTarget.ImageNumber }, spcEncode, usage, stageTarget.DisplayName, "spc"));
                }
            }
        }

        AddDuplicateTargetSkips(skipped, movRequests, "Unit_mov.e5");
        AddDuplicateTargetSkips(skipped, atkRequests, "Unit_atk.e5");
        AddDuplicateTargetSkips(skipped, spcRequests, "Unit_spc.e5");

        var filePreviews = new Dictionary<string, E5ImageBatchReplacePreviewResult>(StringComparer.OrdinalIgnoreCase);
        if (movRequests.Count > 0 && !HasDuplicateTargets(movRequests))
        {
            var target = CharacterImageResourceService.ResolveGameFile(project, "Unit_mov.e5");
            filePreviews["Unit_mov.e5"] = _replace.PreviewBatchReplacement(project, target, movRequests);
        }

        if (atkRequests.Count > 0 && !HasDuplicateTargets(atkRequests))
        {
            var target = CharacterImageResourceService.ResolveGameFile(project, "Unit_atk.e5");
            filePreviews["Unit_atk.e5"] = _replace.PreviewBatchReplacement(project, target, atkRequests);
        }

        if (spcRequests.Count > 0 && !HasDuplicateTargets(spcRequests))
        {
            var target = CharacterImageResourceService.ResolveGameFile(project, "Unit_spc.e5");
            filePreviews["Unit_spc.e5"] = _replace.PreviewBatchReplacement(project, target, spcRequests);
        }

        return new BatchSImageReplacePreviewResult
        {
            Request = request,
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
            Items = preview.Items,
            SkippedItems = preview.SkippedItems,
            Warnings = preview.Warnings,
            FilePreviews = preview.FilePreviews,
            WriteResults = writeResults
        };

        return new BatchSImageReplaceResult
        {
            Request = payload.Request,
            Items = payload.Items,
            SkippedItems = payload.SkippedItems,
            Warnings = payload.Warnings,
            FilePreviews = payload.FilePreviews,
            WriteResults = payload.WriteResults,
            AggregateReportPath = WriteAggregateReport(project, payload)
        };
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
            mov.AddRange(BuildRequests(item.ImageNumbers, item.MovEncode, usage, item.StageName, "mov"));
            atk.AddRange(BuildRequests(item.ImageNumbers, item.AtkEncode, usage, item.StageName, "atk"));
            spc.AddRange(BuildRequests(item.ImageNumbers, item.SpcEncode, usage, item.StageName, "spc"));
        }

        return new[]
        {
            ("Unit_mov.e5", (IReadOnlyList<E5ImageBatchReplaceRequest>)mov),
            ("Unit_atk.e5", atk),
            ("Unit_spc.e5", spc)
        };
    }

    private static IReadOnlyList<BatchSImageUsage> ResolveUsages(
        int sImageId,
        IReadOnlyList<BatchSImageUsage> allowedUsages,
        int defaultFactionSlot)
    {
        if (allowedUsages.Count > 0)
        {
            return allowedUsages.Where(usage => usage.SImageId == sImageId).Distinct().ToArray();
        }

        return [new BatchSImageUsage(sImageId, null, CharacterImageResourceService.NormalizeSPreviewFactionSlot(defaultFactionSlot))];
    }

    private static IReadOnlyList<E5ImageBatchReplaceRequest> BuildRequests(
        IReadOnlyList<int> imageNumbers,
        E5TrueColorEncodeResult encode,
        BatchSImageUsage usage,
        string stageName,
        string actionName)
        => imageNumbers.Select(imageNumber => new E5ImageBatchReplaceRequest
        {
            ImageNumber = imageNumber,
            SourceBytes = encode.ImageBytes,
            SourceBytesAreRaw = false,
            SourceLabel = $"{encode.SourcePath} -> {encode.StorageFormat}",
            OperationKind = $"batch S{usage.SImageId} {stageName} {actionName}"
        }).ToArray();

    private static void AddDuplicateTargetSkips(
        List<BatchImageImportSkippedItem> skipped,
        IReadOnlyList<E5ImageBatchReplaceRequest> requests,
        string fileName)
    {
        foreach (var duplicate in requests.GroupBy(request => request.ImageNumber).Where(group => group.Count() > 1))
        {
            skipped.Add(Skip($"#{duplicate.Key}", fileName, BatchImageImportSkipReasons.DuplicateTarget, "duplicate target image number"));
        }
    }

    private static bool HasDuplicateTargets(IReadOnlyList<E5ImageBatchReplaceRequest> requests)
        => requests.GroupBy(request => request.ImageNumber).Any(group => group.Count() > 1);

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

    private static int? TryParseFolderId(string folder)
    {
        var match = FolderIdRegex.Match(Path.GetFileName(folder));
        if (!match.Success) return null;
        return int.TryParse(match.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id)
            ? id
            : null;
    }

    private static string? ResolveMaterialFile(string folder, int stageSlot, string fileName)
    {
        var stageFolder = Path.Combine(folder, $"turn{stageSlot.ToString(CultureInfo.InvariantCulture)}");
        var staged = Directory.Exists(stageFolder)
            ? ResolveMaterialFile(stageFolder, fileName)
            : null;
        return staged ?? ResolveMaterialFile(folder, fileName);
    }

    private static string? ResolveMaterialFile(string folder, string fileName)
        => Directory.EnumerateFiles(folder)
            .FirstOrDefault(file => Path.GetFileName(file).Equals(fileName, StringComparison.OrdinalIgnoreCase));

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
}
