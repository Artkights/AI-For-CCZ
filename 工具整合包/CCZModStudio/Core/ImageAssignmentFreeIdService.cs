using System.Data;
using System.Drawing;
using System.Globalization;
using CCZModStudio.Models;

namespace CCZModStudio.Core;

internal sealed class ImageAssignmentFreeIdService
{
    private readonly ImageAssignmentPreviewService _previewService;
    private readonly Dictionary<string, Bitmap> _candidatePreviewCache = new(StringComparer.OrdinalIgnoreCase);

    public ImageAssignmentFreeIdService(ImageAssignmentPreviewService previewService)
    {
        _previewService = previewService;
    }

    public void ClearCache()
    {
        foreach (var bitmap in _candidatePreviewCache.Values)
        {
            bitmap.Dispose();
        }

        _candidatePreviewCache.Clear();
        _previewService.ClearCache();
    }

    public ImageAssignmentFreeIdResult Build(
        CczProject project,
        DataTable assignments,
        ImageAssignmentResourceKind kind,
        int sFactionSlot)
    {
        var prefix = NormalizeKind(kind);
        var availableIds = _previewService.GetAvailableCharacterImageIds(project, prefix, includeZero: false, out var availableIdsFromCache);
        var assignedIds = CollectAssignedIds(assignments, kind);
        var candidates = BuildFreeCandidates(availableIds, assignedIds, kind, sFactionSlot);
        var warnings = BuildResourceWarnings(project, kind);

        return new ImageAssignmentFreeIdResult(
            kind,
            availableIds.Count,
            assignedIds.Count,
            candidates,
            warnings,
            availableIdsFromCache);
    }

    public Bitmap? RenderCandidatePreview(
        CczProject project,
        ImageAssignmentResourceKind kind,
        int id,
        int sFactionSlot)
    {
        var cacheKey = BuildCandidatePreviewCacheKey(project, kind, id, sFactionSlot);
        if (_candidatePreviewCache.TryGetValue(cacheKey, out var cached))
        {
            return new Bitmap(cached);
        }

        Bitmap? preview;
        try
        {
            preview = kind switch
            {
                ImageAssignmentResourceKind.Face => _previewService.TryRenderFaceImage(project, id),
                ImageAssignmentResourceKind.S => _previewService.TryRenderCharacterResourceImage(project, "S", id, jobId: null, sFactionSlot),
                _ => _previewService.TryRenderCharacterResourceImage(project, "R", id)
            };
        }
        catch
        {
            return null;
        }

        if (preview == null) return null;

        _candidatePreviewCache[cacheKey] = new Bitmap(preview);
        return preview;
    }

    internal static IReadOnlyList<FreeImageAssignmentCandidate> BuildFreeCandidates(
        IReadOnlyList<int> availableIds,
        IReadOnlySet<int> assignedIds,
        ImageAssignmentResourceKind kind,
        int sFactionSlot)
        => availableIds
            .Where(id => id > 0 && !assignedIds.Contains(id))
            .Distinct()
            .OrderBy(id => id)
            .Select(id => new FreeImageAssignmentCandidate(id, BuildDetail(kind, id, sFactionSlot)))
            .ToArray();

    internal static HashSet<int> CollectAssignedIds(DataTable assignments, ImageAssignmentResourceKind kind)
    {
        var columnName = kind switch
        {
            ImageAssignmentResourceKind.Face => "头像编号",
            ImageAssignmentResourceKind.S => "S形象编号",
            _ => "R形象编号"
        };
        var result = new HashSet<int>();
        if (!assignments.Columns.Contains(columnName)) return result;

        foreach (DataRow row in assignments.Rows)
        {
            if (row.RowState == DataRowState.Deleted) continue;

            if (!TryReadCurrentInt(row, columnName, out var id) || id <= 0)
            {
                continue;
            }

            result.Add(id);
        }

        return result;
    }

    private static bool TryReadCurrentInt(DataRow row, string columnName, out int value)
    {
        value = 0;
        try
        {
            var raw = row.RowState == DataRowState.Detached
                ? row[columnName]
                : row[columnName, DataRowVersion.Current];
            return int.TryParse(Convert.ToString(raw, CultureInfo.InvariantCulture), NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
        }
        catch (VersionNotFoundException)
        {
            return false;
        }
    }

    private static string BuildDetail(ImageAssignmentResourceKind kind, int id, int sFactionSlot)
    {
        if (kind == ImageAssignmentResourceKind.Face)
        {
            var mapping = new CharacterImageResourceService().MapFaceId(id);
            var faceText = mapping.FaceImageNumbers.Count == 1
                ? $"#{mapping.FaceImageNumbers[0]}"
                : $"#{mapping.FaceImageNumbers.First()}-#{mapping.FaceImageNumbers.Last()}";
            return $"Face.e5 {faceText}";
        }

        if (kind == ImageAssignmentResourceKind.S)
        {
            return CharacterImageResourceService.ResolveSUnitImageMapping(id, jobId: null, sFactionSlot).ShortText;
        }

        var front = checked(id * 2 + 1);
        var back = checked(id * 2 + 2);
        return $"Pmapobj.e5 #{front}/#{back}";
    }

    private static IReadOnlyList<string> BuildResourceWarnings(CczProject project, ImageAssignmentResourceKind kind)
    {
        if (kind == ImageAssignmentResourceKind.Face)
        {
            var face = CharacterImageResourceService.ResolveFaceFile(project) ?? Path.Combine(project.GameRoot, "E5", "Face.e5");
            return File.Exists(face)
                ? Array.Empty<string>()
                : new[] { $"未找到 Face.e5：{face}" };
        }

        if (kind == ImageAssignmentResourceKind.R)
        {
            var pmapObj = CharacterImageResourceService.ResolveGameFile(project, "Pmapobj.e5");
            return File.Exists(pmapObj)
                ? Array.Empty<string>()
                : new[] { $"未找到 Pmapobj.e5：{pmapObj}" };
        }

        var warnings = new List<string>();
        foreach (var fileName in new[] { "Unit_atk.e5", "Unit_mov.e5", "Unit_spc.e5" })
        {
            var path = CharacterImageResourceService.ResolveGameFile(project, fileName);
            if (!File.Exists(path))
            {
                warnings.Add($"未找到 {fileName}：{path}");
            }
        }

        return warnings;
    }

    private static string NormalizeKind(ImageAssignmentResourceKind kind) =>
        kind == ImageAssignmentResourceKind.Face ? "Face" : kind == ImageAssignmentResourceKind.S ? "S" : "R";

    private static string BuildCandidatePreviewCacheKey(
        CczProject project,
        ImageAssignmentResourceKind kind,
        int id,
        int sFactionSlot)
    {
        var paths = kind switch
        {
            ImageAssignmentResourceKind.Face => new[] { CharacterImageResourceService.ResolveFaceFile(project) ?? Path.Combine(project.GameRoot, "E5", "Face.e5") },
            ImageAssignmentResourceKind.S => new[] { "Unit_atk.e5", "Unit_mov.e5", "Unit_spc.e5" }
                .Select(fileName => CharacterImageResourceService.ResolveGameFile(project, fileName))
                .ToArray(),
            _ => new[] { CharacterImageResourceService.ResolveGameFile(project, "Pmapobj.e5") }
        };

        return string.Join("|",
            new[] { Path.GetFullPath(project.GameRoot), kind.ToString(), id.ToString(CultureInfo.InvariantCulture), sFactionSlot.ToString(CultureInfo.InvariantCulture) }
                .Concat(paths.Select(BuildFileCacheKey)));
    }

    private static string BuildFileCacheKey(string path)
    {
        var fullPath = Path.GetFullPath(path);
        if (!File.Exists(fullPath))
        {
            return fullPath + "|missing";
        }

        try
        {
            var info = new FileInfo(fullPath);
            return $"{fullPath}|{info.Length}|{info.LastWriteTimeUtc.Ticks}";
        }
        catch
        {
            return fullPath + "|unknown";
        }
    }
}

internal sealed record ImageAssignmentFreeIdResult(
    ImageAssignmentResourceKind Kind,
    int CandidateResourceCount,
    int AssignedCount,
    IReadOnlyList<FreeImageAssignmentCandidate> FreeCandidates,
    IReadOnlyList<string> Warnings,
    bool AvailableIdsFromCache = false);

internal sealed record FreeImageAssignmentCandidate(int Id, string Detail);
