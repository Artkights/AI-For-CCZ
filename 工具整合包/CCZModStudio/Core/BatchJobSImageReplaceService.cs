using System.Globalization;
using System.Text.RegularExpressions;
using CCZModStudio.Models;

namespace CCZModStudio.Core;

public sealed class BatchJobSImageReplaceService
{
    private static readonly Regex FolderIdRegex = new(@"^Job_?(\d+)$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private readonly JobSImageReplaceService _jobSImageReplace = new();

    public BatchJobSImageReplacePreviewResult Preview(CczProject project, BatchJobSImageReplaceRequest request)
    {
        var root = ResolveMaterialRoot(request.MaterialRoot);
        var slots = NormalizeFactionSlots(request.FactionSlots);
        var skipped = new List<BatchImageImportSkippedItem>();
        var warnings = new List<string>();
        var items = new List<BatchJobSImageReplaceItemPreview>();
        var candidates = Directory.EnumerateDirectories(root)
            .OrderBy(path => path, StringComparer.CurrentCultureIgnoreCase)
            .Select(folder => (JobId: TryParseFolderId(folder), Folder: folder))
            .ToArray();

        foreach (var candidate in candidates)
        {
            if (!candidate.JobId.HasValue)
            {
                skipped.Add(Skip(Path.GetFileName(candidate.Folder), candidate.Folder, BatchImageImportSkipReasons.InvalidName));
                continue;
            }

            if (request.IncludeOnlySelectedOrFiltered &&
                request.AllowedJobIds.Count > 0 &&
                !request.AllowedJobIds.Contains(candidate.JobId.Value))
            {
                skipped.Add(Skip(candidate.JobId.Value.ToString(CultureInfo.InvariantCulture), candidate.Folder, BatchImageImportSkipReasons.Unused));
                continue;
            }

            var missing = new[] { "mov.bmp", "atk.bmp", "spc.bmp" }
                .Where(fileName => !File.Exists(Path.Combine(candidate.Folder, fileName)))
                .ToArray();
            if (missing.Length > 0)
            {
                skipped.Add(Skip(candidate.JobId.Value.ToString(CultureInfo.InvariantCulture), candidate.Folder, BatchImageImportSkipReasons.MissingFile, string.Join(", ", missing)));
                continue;
            }

            try
            {
                var preview = _jobSImageReplace.Preview(project, new JobSImageReplaceRequest
                {
                    JobId = candidate.JobId.Value,
                    MaterialFolder = candidate.Folder,
                    FactionSlots = slots,
                    WriteMode = request.WriteMode
                });
                warnings.AddRange(preview.Warnings.Select(warning => $"Job{candidate.JobId.Value}: {warning}"));
                items.Add(new BatchJobSImageReplaceItemPreview
                {
                    JobId = candidate.JobId.Value,
                    MaterialFolder = candidate.Folder,
                    Preview = preview
                });
            }
            catch (Exception ex)
            {
                skipped.Add(Skip(candidate.JobId.Value.ToString(CultureInfo.InvariantCulture), candidate.Folder, BatchImageImportSkipReasons.InvalidFormat, ex.Message));
            }
        }

        foreach (var duplicate in items.GroupBy(item => item.JobId).Where(group => group.Count() > 1))
        {
            foreach (var item in duplicate)
            {
                skipped.Add(Skip(item.JobId.ToString(CultureInfo.InvariantCulture), item.MaterialFolder, BatchImageImportSkipReasons.DuplicateId));
            }
        }

        return new BatchJobSImageReplacePreviewResult
        {
            Request = request,
            Items = items
                .GroupBy(item => item.JobId)
                .Where(group => group.Count() == 1)
                .Select(group => group.Single())
                .OrderBy(item => item.JobId)
                .ToArray(),
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

        if (preview.Items.Count == 0)
        {
            throw new InvalidOperationException("Batch job S image import has no writable items.");
        }

        var results = preview.Items.Select(item => new BatchJobSImageReplaceItemResult
        {
            JobId = item.JobId,
            MaterialFolder = item.MaterialFolder,
            Result = _jobSImageReplace.Replace(project, new JobSImageReplaceRequest
            {
                JobId = item.JobId,
                MaterialFolder = item.MaterialFolder,
                FactionSlots = request.FactionSlots,
                WriteMode = request.WriteMode
            })
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

    private static string ResolveMaterialRoot(string materialRoot)
    {
        if (string.IsNullOrWhiteSpace(materialRoot))
        {
            throw new InvalidOperationException("Missing batch job S image material root.");
        }

        var root = Path.GetFullPath(materialRoot);
        if (!Directory.Exists(root))
        {
            throw new DirectoryNotFoundException($"Batch job S image material root not found: {root}");
        }

        return root;
    }

    private static IReadOnlyList<int> NormalizeFactionSlots(IReadOnlyList<int> factionSlots)
    {
        var slots = factionSlots.Distinct().OrderBy(slot => slot).ToArray();
        if (slots.Length == 0)
        {
            throw new InvalidOperationException("Select at least one faction slot.");
        }

        var invalid = slots.Where(slot => slot is < 1 or > 3).ToArray();
        if (invalid.Length > 0)
        {
            throw new InvalidOperationException($"Faction slots must be 1..3: {string.Join(", ", invalid)}");
        }

        return slots;
    }

    private static int? TryParseFolderId(string folder)
    {
        var match = FolderIdRegex.Match(Path.GetFileName(folder));
        return match.Success && int.TryParse(match.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id)
            ? id
            : null;
    }

    private static BatchImageImportSkippedItem Skip(string key, string sourcePath, string reason, string detail = "")
        => new()
        {
            Key = key,
            SourcePath = sourcePath,
            Reason = string.IsNullOrWhiteSpace(detail) ? reason : $"{reason}: {detail}"
        };
}
