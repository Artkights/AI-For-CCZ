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
        => ReplaceMany(project, new[] { request }).Single();

    public IReadOnlyList<JobSImageReplaceResult> ReplaceMany(
        CczProject project,
        IReadOnlyList<JobSImageReplaceRequest> requests)
    {
        if (requests.Count == 0)
            throw new InvalidOperationException("No job S image replacements were requested.");
        var normalizedRequests = requests.Select(NormalizeRequest).ToArray();
        var flat = normalizedRequests
            .SelectMany((normalized, requestIndex) => normalized.FactionSlots.Select(slot => new
            {
                RequestIndex = requestIndex,
                Normalized = normalized,
                Slot = slot,
                SRequest = BuildSRequest(normalized, slot)
            }))
            .ToArray();
        var writes = _sImageReplaceService.ReplaceMany(
            project,
            flat.Select(item => item.SRequest).ToArray());
        var outputs = new List<JobSImageReplaceResult>(normalizedRequests.Length);
        for (var requestIndex = 0; requestIndex < normalizedRequests.Length; requestIndex++)
        {
            var normalized = normalizedRequests[requestIndex];
            var items = flat.Select((item, flatIndex) => (item, flatIndex))
                .Where(pair => pair.item.RequestIndex == requestIndex)
                .ToArray();
            var results = items
            .Select(pair => new JobSImageFactionResult
            {
                FactionSlot = pair.item.Slot,
                FactionName = CharacterImageResourceService.BuildSPreviewFactionText(pair.item.Slot),
                Result = writes[pair.flatIndex]
            })
            .ToArray();
            outputs.Add(new JobSImageReplaceResult
            {
                Request = normalized,
                Factions = results,
                Warnings = results.SelectMany(faction => faction.Result.Warnings).Distinct(StringComparer.Ordinal).ToArray()
            });
        }
        return outputs;
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
