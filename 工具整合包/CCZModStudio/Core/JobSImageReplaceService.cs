using CCZModStudio.Models;

namespace CCZModStudio.Core;

public sealed class JobSImageReplaceService
{
    private readonly SImageReplaceService _sImageReplaceService = new();

    public JobSImageReplacePreviewResult Preview(CczProject project, JobSImageReplaceRequest request)
    {
        var normalized = NormalizeRequest(request);
        var factions = normalized.FactionSlots
            .Select(slot => new JobSImageFactionPreview
            {
                FactionSlot = slot,
                FactionName = CharacterImageResourceService.BuildSPreviewFactionText(slot),
                Preview = _sImageReplaceService.Preview(project, BuildSRequest(normalized, slot))
            })
            .ToArray();

        return new JobSImageReplacePreviewResult
        {
            Request = normalized,
            Factions = factions,
            Warnings = factions.SelectMany(faction => faction.Preview.Warnings).Distinct(StringComparer.Ordinal).ToArray()
        };
    }

    public JobSImageReplaceResult Replace(CczProject project, JobSImageReplaceRequest request)
    {
        var normalized = NormalizeRequest(request);
        var results = normalized.FactionSlots
            .Select(slot => new JobSImageFactionResult
            {
                FactionSlot = slot,
                FactionName = CharacterImageResourceService.BuildSPreviewFactionText(slot),
                Result = _sImageReplaceService.Replace(project, BuildSRequest(normalized, slot))
            })
            .ToArray();

        return new JobSImageReplaceResult
        {
            Request = normalized,
            Factions = results,
            Warnings = results.SelectMany(faction => faction.Result.Warnings).Distinct(StringComparer.Ordinal).ToArray()
        };
    }

    public static JobSImageReplaceRequest NormalizeRequest(JobSImageReplaceRequest request)
    {
        if (request.JobId < 0)
        {
            throw new InvalidOperationException($"Detailed job id must be >= 0: {request.JobId}");
        }

        if (string.IsNullOrWhiteSpace(request.MaterialFolder))
        {
            throw new InvalidOperationException("Missing S image material folder.");
        }

        var slots = request.FactionSlots
            .Distinct()
            .OrderBy(slot => slot)
            .ToArray();
        if (slots.Length == 0)
        {
            throw new InvalidOperationException("Select at least one faction slot.");
        }

        var invalid = slots.Where(slot => slot is < 1 or > 3).ToArray();
        if (invalid.Length > 0)
        {
            throw new InvalidOperationException($"Faction slots must be 1..3: {string.Join(", ", invalid)}");
        }

        return new JobSImageReplaceRequest
        {
            JobId = request.JobId,
            MaterialFolder = Path.GetFullPath(request.MaterialFolder),
            FactionSlots = slots,
            WriteMode = string.IsNullOrWhiteSpace(request.WriteMode) ? "direct" : request.WriteMode
        };
    }

    private static SImageReplaceRequest BuildSRequest(JobSImageReplaceRequest request, int factionSlot)
        => new()
        {
            SImageId = 0,
            MaterialFolder = request.MaterialFolder,
            JobId = request.JobId,
            FactionSlot = factionSlot,
            WriteMode = request.WriteMode
        };
}
