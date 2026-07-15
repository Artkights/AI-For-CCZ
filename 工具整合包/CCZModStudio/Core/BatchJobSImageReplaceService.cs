using System.Globalization;
using CCZModStudio.Models;

namespace CCZModStudio.Core;

public sealed class BatchJobSImageReplaceService
{
    private readonly JobSImageReplaceService _jobSImageReplace = new();

    public BatchJobSImageReplacePreviewResult Preview(CczProject project, BatchJobSImageReplaceRequest request)
    {
        var root = ResolveMaterialRoot(request.MaterialRoot);
        var slots = JobSImageMaterialLayout.NormalizeFactionSlots(request.FactionSlots);
        var skipped = new List<BatchImageImportSkippedItem>();
        var warnings = new List<string>();
        var items = new List<BatchJobSImageReplaceItemPreview>();

        var candidates = Directory.EnumerateDirectories(root)
            .OrderBy(path => path, StringComparer.CurrentCultureIgnoreCase)
            .Select(folder => (JobId: TryParseJobId(folder), Folder: folder))
            .ToArray();

        foreach (var candidate in candidates.Where(candidate => !candidate.JobId.HasValue))
        {
            skipped.Add(Skip(Path.GetFileName(candidate.Folder), candidate.Folder, BatchImageImportSkipReasons.InvalidName));
        }

        foreach (var duplicate in candidates
                     .Where(candidate => candidate.JobId.HasValue)
                     .GroupBy(candidate => candidate.JobId!.Value)
                     .Where(group => group.Count() > 1))
        {
            foreach (var candidate in duplicate)
            {
                skipped.Add(Skip(duplicate.Key.ToString(CultureInfo.InvariantCulture), candidate.Folder, BatchImageImportSkipReasons.DuplicateId));
            }
        }

        var uniqueCandidates = candidates
            .Where(candidate => candidate.JobId.HasValue)
            .GroupBy(candidate => candidate.JobId!.Value)
            .Where(group => group.Count() == 1)
            .Select(group => group.Single())
            .OrderBy(candidate => candidate.JobId)
            .ToArray();

        foreach (var candidate in uniqueCandidates)
        {
            var jobId = candidate.JobId!.Value;
            if (request.IncludeOnlySelectedOrFiltered &&
                request.AllowedJobIds.Count > 0 &&
                !request.AllowedJobIds.Contains(jobId))
            {
                skipped.Add(Skip(jobId.ToString(CultureInfo.InvariantCulture), candidate.Folder, BatchImageImportSkipReasons.Unused));
                continue;
            }

            PreviewJobFolder(project, request, slots, jobId, candidate.Folder, items, skipped, warnings);
        }

        return new BatchJobSImageReplacePreviewResult
        {
            Request = new BatchJobSImageReplaceRequest
            {
                MaterialRoot = root,
                AllowedJobIds = request.AllowedJobIds,
                IncludeOnlySelectedOrFiltered = request.IncludeOnlySelectedOrFiltered,
                FactionSlots = slots,
                WriteMode = request.WriteMode
            },
            Items = items.OrderBy(item => item.JobId).ThenBy(item => item.FactionSlot).ToArray(),
            SkippedItems = skipped.ToArray(),
            Warnings = warnings.Distinct(StringComparer.Ordinal).ToArray()
        };
    }

    public BatchJobSImageReplaceResult Replace(CczProject project, BatchJobSImageReplaceRequest request)
    {
        var preview = Preview(project, request);
        if (!preview.CanWrite)
        {
            throw new InvalidOperationException("Batch job S image import has blocking skipped items.");
        }

        var requests = preview.Items.Select(item => new JobSImageReplaceRequest
            {
                JobId = item.JobId,
                MaterialFolder = item.MaterialFolder,
                FactionSlots = [item.FactionSlot],
                WriteMode = preview.Request.WriteMode
            }).ToArray();
        var writes = _jobSImageReplace.ReplaceMany(project, requests);
        var results = preview.Items.Select((item, index) => new BatchJobSImageReplaceItemResult
        {
            JobId = item.JobId,
            FactionSlot = item.FactionSlot,
            MaterialFolder = item.MaterialFolder,
            UsesLegacyFlatLayout = item.UsesLegacyFlatLayout,
            Result = writes[index]
        }).ToArray();

        return new BatchJobSImageReplaceResult
        {
            Request = preview.Request,
            Items = preview.Items,
            SkippedItems = preview.SkippedItems,
            Warnings = preview.Warnings,
            Results = results
        };
    }

    private void PreviewJobFolder(
        CczProject project,
        BatchJobSImageReplaceRequest request,
        IReadOnlyList<int> selectedSlots,
        int jobId,
        string jobFolder,
        List<BatchJobSImageReplaceItemPreview> items,
        List<BatchImageImportSkippedItem> skipped,
        List<string> warnings)
    {
        var factionCandidates = Directory.EnumerateDirectories(jobFolder)
            .OrderBy(path => path, StringComparer.CurrentCultureIgnoreCase)
            .Select(folder => (FactionSlot: TryParseFactionSlot(folder), Folder: folder))
            .ToArray();
        var recognized = factionCandidates.Where(candidate => candidate.FactionSlot.HasValue).ToArray();

        if (recognized.Length == 0)
        {
            PreviewLegacyJobFolder(project, request, selectedSlots, jobId, jobFolder, items, skipped, warnings);
            return;
        }

        if (JobSImageMaterialLayout.HasAnyTripletFile(jobFolder))
        {
            warnings.Add($"Job{jobId}: detected Faction directories; legacy flat mov/atk/spc files were ignored.");
        }

        foreach (var invalid in factionCandidates.Where(candidate => !candidate.FactionSlot.HasValue))
        {
            skipped.Add(Skip($"Job{jobId}/{Path.GetFileName(invalid.Folder)}", invalid.Folder, BatchImageImportSkipReasons.InvalidName));
        }

        foreach (var duplicate in recognized.GroupBy(candidate => candidate.FactionSlot!.Value).Where(group => group.Count() > 1))
        {
            foreach (var candidate in duplicate)
            {
                skipped.Add(Skip(BuildKey(jobId, duplicate.Key), candidate.Folder, BatchImageImportSkipReasons.DuplicateId));
            }
        }

        foreach (var candidate in recognized
                     .GroupBy(candidate => candidate.FactionSlot!.Value)
                     .Where(group => group.Count() == 1)
                     .Select(group => group.Single())
                     .OrderBy(candidate => candidate.FactionSlot))
        {
            var factionSlot = candidate.FactionSlot!.Value;
            if (!selectedSlots.Contains(factionSlot))
            {
                skipped.Add(Skip(BuildKey(jobId, factionSlot), candidate.Folder, BatchImageImportSkipReasons.Unused));
                continue;
            }

            PreviewMaterial(project, request, jobId, factionSlot, candidate.Folder, false, items, skipped, warnings);
        }
    }

    private void PreviewLegacyJobFolder(
        CczProject project,
        BatchJobSImageReplaceRequest request,
        IReadOnlyList<int> selectedSlots,
        int jobId,
        string jobFolder,
        List<BatchJobSImageReplaceItemPreview> items,
        List<BatchImageImportSkippedItem> skipped,
        List<string> warnings)
    {
        var missing = JobSImageMaterialLayout.GetMissingRequiredFiles(jobFolder);
        if (missing.Count > 0)
        {
            skipped.Add(Skip(jobId.ToString(CultureInfo.InvariantCulture), jobFolder, BatchImageImportSkipReasons.MissingFile, string.Join(", ", missing)));
            return;
        }

        warnings.Add($"Job{jobId}: imported legacy flat layout into {string.Join("/", selectedSlots.Select(CharacterImageResourceService.BuildSPreviewFactionText))}.");
        foreach (var factionSlot in selectedSlots)
        {
            PreviewMaterial(project, request, jobId, factionSlot, jobFolder, true, items, skipped, warnings);
        }
    }

    private void PreviewMaterial(
        CczProject project,
        BatchJobSImageReplaceRequest request,
        int jobId,
        int factionSlot,
        string materialFolder,
        bool usesLegacyFlatLayout,
        List<BatchJobSImageReplaceItemPreview> items,
        List<BatchImageImportSkippedItem> skipped,
        List<string> warnings)
    {
        var missing = JobSImageMaterialLayout.GetMissingRequiredFiles(materialFolder);
        if (missing.Count > 0)
        {
            skipped.Add(Skip(BuildKey(jobId, factionSlot), materialFolder, BatchImageImportSkipReasons.MissingFile, string.Join(", ", missing)));
            return;
        }

        try
        {
            var preview = _jobSImageReplace.Preview(project, new JobSImageReplaceRequest
            {
                JobId = jobId,
                MaterialFolder = materialFolder,
                FactionSlots = [factionSlot],
                WriteMode = request.WriteMode
            });
            warnings.AddRange(preview.Warnings.Select(warning => $"{BuildKey(jobId, factionSlot)}: {warning}"));
            items.Add(new BatchJobSImageReplaceItemPreview
            {
                JobId = jobId,
                FactionSlot = factionSlot,
                MaterialFolder = materialFolder,
                UsesLegacyFlatLayout = usesLegacyFlatLayout,
                Preview = preview
            });
        }
        catch (Exception ex)
        {
            skipped.Add(Skip(BuildKey(jobId, factionSlot), materialFolder, BatchImageImportSkipReasons.InvalidFormat, ex.Message));
        }
    }

    private static string ResolveMaterialRoot(string materialRoot)
    {
        if (string.IsNullOrWhiteSpace(materialRoot)) throw new InvalidOperationException("Missing batch job S image material root.");
        var root = Path.GetFullPath(materialRoot);
        if (!Directory.Exists(root)) throw new DirectoryNotFoundException($"Batch job S image material root not found: {root}");
        return root;
    }

    private static int? TryParseJobId(string folder)
        => JobSImageMaterialLayout.TryParseJobFolder(folder, out var jobId) ? jobId : null;

    private static int? TryParseFactionSlot(string folder)
        => JobSImageMaterialLayout.TryParseFactionFolder(folder, out var factionSlot) ? factionSlot : null;

    private static string BuildKey(int jobId, int factionSlot)
        => $"Job{jobId}/Faction{factionSlot}";

    private static BatchImageImportSkippedItem Skip(string key, string sourcePath, string reason, string detail = "")
        => new()
        {
            Key = key,
            SourcePath = sourcePath,
            Reason = string.IsNullOrWhiteSpace(detail) ? reason : $"{reason}: {detail}"
        };
}
