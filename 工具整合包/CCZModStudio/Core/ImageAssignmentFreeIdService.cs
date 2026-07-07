using System.Data;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.Collections.Concurrent;
using CCZModStudio.Models;

namespace CCZModStudio.Core;

internal sealed class ImageAssignmentFreeIdService
{
    private readonly ImageAssignmentPreviewService _previewService;
    private readonly ConcurrentDictionary<string, Lazy<Task<CachedPreviewImage?>>> _candidatePreviewCache = new(StringComparer.OrdinalIgnoreCase);

    public ImageAssignmentFreeIdService(ImageAssignmentPreviewService previewService)
    {
        _previewService = previewService;
    }

    public void ClearCache()
    {
        _candidatePreviewCache.Clear();
        _previewService.ClearCache();
    }

    public ImageAssignmentFreeIdResult Build(
        CczProject project,
        DataTable assignments,
        ImageAssignmentResourceKind kind,
        int sFactionSlot)
        => Build(project, assignments, kind, sFactionSlot, freeOnly: true);

    public ImageAssignmentFreeIdResult Build(
        CczProject project,
        DataTable assignments,
        ImageAssignmentResourceKind kind,
        int sFactionSlot,
        bool freeOnly)
    {
        var prefix = NormalizeKind(kind);
        var includeZero = ShouldIncludeZero(kind);
        var availableIds = _previewService.GetAvailableCharacterImageIds(project, prefix, includeZero, out var availableIdsFromCache);
        var assignedIds = CollectAssignedIds(assignments, kind);
        var candidates = freeOnly
            ? BuildFreeCandidates(project, availableIds, assignedIds, kind, sFactionSlot)
            : BuildAllCandidates(project, availableIds, kind, sFactionSlot);
        var warnings = BuildResourceWarnings(project, kind);

        return new ImageAssignmentFreeIdResult(
            kind,
            freeOnly,
            availableIds.Count,
            assignedIds.Count,
            candidates,
            warnings,
            availableIdsFromCache);
    }

    public Task<ImageAssignmentFreeIdResult> BuildAsync(
        CczProject project,
        DataTable assignments,
        ImageAssignmentResourceKind kind,
        int sFactionSlot,
        bool freeOnly,
        CancellationToken cancellationToken)
    {
        var assignedIds = CollectAssignedIds(assignments, kind);
        return Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();

            var prefix = NormalizeKind(kind);
            var includeZero = ShouldIncludeZero(kind);
            var availableIds = _previewService.GetAvailableCharacterImageIds(project, prefix, includeZero, out var availableIdsFromCache);
            cancellationToken.ThrowIfCancellationRequested();

            var candidates = freeOnly
                ? BuildFreeCandidates(project, availableIds, assignedIds, kind, sFactionSlot)
                : BuildAllCandidates(project, availableIds, kind, sFactionSlot);
            var warnings = BuildResourceWarnings(project, kind);

            return new ImageAssignmentFreeIdResult(
                kind,
                freeOnly,
                availableIds.Count,
                assignedIds.Count,
                candidates,
                warnings,
                availableIdsFromCache);
        }, cancellationToken);
    }

    public Bitmap? RenderCandidatePreview(
        CczProject project,
        ImageAssignmentResourceKind kind,
        int id,
        int sFactionSlot)
        => RenderCandidatePreview(project, kind, id, sFactionSlot, ImageAssignmentPreviewStance.Front);

    public Bitmap? RenderCandidatePreview(
        CczProject project,
        ImageAssignmentResourceKind kind,
        int id,
        int sFactionSlot,
        ImageAssignmentPreviewStance stance)
        => RenderCandidatePreviewAsync(project, kind, id, sFactionSlot, stance, CancellationToken.None)
            .ConfigureAwait(false)
            .GetAwaiter()
            .GetResult()
            ?.CreateBitmap();

    public Task<CachedPreviewImage?> RenderCandidatePreviewAsync(
        CczProject project,
        ImageAssignmentResourceKind kind,
        int id,
        int sFactionSlot,
        ImageAssignmentPreviewStance stance,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        stance = NormalizeStance(kind, stance);
        var cacheKey = BuildCandidatePreviewCacheKey(project, kind, id, sFactionSlot, stance);
        var lazy = _candidatePreviewCache.GetOrAdd(cacheKey, key => new Lazy<Task<CachedPreviewImage?>>(
            () => Task.Run(() => RenderCandidatePreviewCore(project, kind, id, sFactionSlot, stance, key), CancellationToken.None),
            LazyThreadSafetyMode.ExecutionAndPublication));

        return AwaitCachedPreviewAsync(cacheKey, lazy, cancellationToken);
    }

    private async Task<CachedPreviewImage?> AwaitCachedPreviewAsync(
        string cacheKey,
        Lazy<Task<CachedPreviewImage?>> lazy,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await lazy.Value.WaitAsync(cancellationToken).ConfigureAwait(false);
            if (result == null)
            {
                RemoveCachedPreviewIfSame(cacheKey, lazy);
            }

            return result;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            RemoveCachedPreviewIfSame(cacheKey, lazy);
            return null;
        }
    }

    private void RemoveCachedPreviewIfSame(string cacheKey, Lazy<Task<CachedPreviewImage?>> lazy)
    {
        if (_candidatePreviewCache.TryGetValue(cacheKey, out var current) && ReferenceEquals(current, lazy))
        {
            _candidatePreviewCache.TryRemove(cacheKey, out _);
        }
    }

    private CachedPreviewImage? RenderCandidatePreviewCore(
        CczProject project,
        ImageAssignmentResourceKind kind,
        int id,
        int sFactionSlot,
        ImageAssignmentPreviewStance stance,
        string cacheKey)
    {
        Bitmap? preview;
        try
        {
            preview = kind switch
            {
                ImageAssignmentResourceKind.Face => _previewService.TryRenderFaceImage(project, id),
                ImageAssignmentResourceKind.S => _previewService.TryRenderBattlefieldMoveIdleFrame(
                    project,
                    id,
                    null,
                    sFactionSlot,
                    StanceToBattlefieldDirection(stance),
                    0,
                    out _),
                _ => _previewService.TryRenderRScenePhysicalStripFrame(
                    project,
                    id,
                    0,
                    StanceToRSceneFacing(stance),
                    out _)
            };
        }
        catch
        {
            return null;
        }

        if (preview == null) return null;

        using (preview)
        {
            return CachedPreviewImage.FromBitmap(preview, cacheKey);
        }
    }

    internal static IReadOnlyList<FreeImageAssignmentCandidate> BuildFreeCandidates(
        IReadOnlyList<int> availableIds,
        IReadOnlySet<int> assignedIds,
        ImageAssignmentResourceKind kind,
        int sFactionSlot)
        => availableIds
            .Where(id => IsCandidateIdInRange(kind, id) && !assignedIds.Contains(id))
            .Distinct()
            .OrderBy(id => id)
            .Select(id => new FreeImageAssignmentCandidate(id, BuildDetail(kind, id, sFactionSlot)))
            .ToArray();

    private static IReadOnlyList<FreeImageAssignmentCandidate> BuildFreeCandidates(
        CczProject project,
        IReadOnlyList<int> availableIds,
        IReadOnlySet<int> assignedIds,
        ImageAssignmentResourceKind kind,
        int sFactionSlot)
        => availableIds
            .Where(id => IsCandidateIdInRange(kind, id) && !assignedIds.Contains(id))
            .Distinct()
            .OrderBy(id => id)
            .Select(id => new FreeImageAssignmentCandidate(id, BuildDetail(project, kind, id, sFactionSlot)))
            .ToArray();

    internal static IReadOnlyList<FreeImageAssignmentCandidate> BuildAllCandidates(
        IReadOnlyList<int> availableIds,
        ImageAssignmentResourceKind kind,
        int sFactionSlot)
        => availableIds
            .Where(id => IsCandidateIdInRange(kind, id))
            .Distinct()
            .OrderBy(id => id)
            .Select(id => new FreeImageAssignmentCandidate(id, BuildDetail(kind, id, sFactionSlot)))
            .ToArray();

    private static IReadOnlyList<FreeImageAssignmentCandidate> BuildAllCandidates(
        CczProject project,
        IReadOnlyList<int> availableIds,
        ImageAssignmentResourceKind kind,
        int sFactionSlot)
        => availableIds
            .Where(id => IsCandidateIdInRange(kind, id))
            .Distinct()
            .OrderBy(id => id)
            .Select(id => new FreeImageAssignmentCandidate(id, BuildDetail(project, kind, id, sFactionSlot)))
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

            if (!TryReadCurrentInt(row, columnName, out var id) || !IsCandidateIdInRange(kind, id))
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

    private static string BuildDetail(CczProject project, ImageAssignmentResourceKind kind, int id, int sFactionSlot)
    {
        if (kind != ImageAssignmentResourceKind.S)
        {
            return BuildDetail(kind, id, sFactionSlot);
        }

        return CharacterImageResourceService.ResolveSUnitImageMapping(project, id, jobId: null, sFactionSlot).ShortText;
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

    private static bool ShouldIncludeZero(ImageAssignmentResourceKind kind)
        => kind is ImageAssignmentResourceKind.Face or ImageAssignmentResourceKind.R;

    private static bool IsCandidateIdInRange(ImageAssignmentResourceKind kind, int id)
        => ShouldIncludeZero(kind) ? id >= 0 : id > 0;

    private static ImageAssignmentPreviewStance NormalizeStance(ImageAssignmentResourceKind kind, ImageAssignmentPreviewStance stance)
    {
        if (kind == ImageAssignmentResourceKind.Face) return ImageAssignmentPreviewStance.Front;
        if (kind == ImageAssignmentResourceKind.R && stance == ImageAssignmentPreviewStance.Side) return ImageAssignmentPreviewStance.Front;
        return stance;
    }

    internal static string GetStanceDisplayText(ImageAssignmentPreviewStance stance)
        => stance switch
        {
            ImageAssignmentPreviewStance.Back => "背面",
            ImageAssignmentPreviewStance.Side => "侧面",
            _ => "正面"
        };

    private static string StanceToRSceneFacing(ImageAssignmentPreviewStance stance)
        => stance == ImageAssignmentPreviewStance.Back ? "上" : "下";

    private static string StanceToBattlefieldDirection(ImageAssignmentPreviewStance stance)
        => stance switch
        {
            ImageAssignmentPreviewStance.Back => "上",
            ImageAssignmentPreviewStance.Side => "左",
            _ => "下"
        };

    private static string BuildCandidatePreviewCacheKey(
        CczProject project,
        ImageAssignmentResourceKind kind,
        int id,
        int sFactionSlot,
        ImageAssignmentPreviewStance stance)
    {
        var paths = kind switch
        {
            ImageAssignmentResourceKind.Face => new[] { CharacterImageResourceService.ResolveFaceFile(project) ?? Path.Combine(project.GameRoot, "E5", "Face.e5") },
            ImageAssignmentResourceKind.S => new[] { CharacterImageResourceService.ResolveGameFile(project, "Unit_mov.e5") },
            _ => new[] { CharacterImageResourceService.ResolveGameFile(project, "Pmapobj.e5") }
        };

        return string.Join("|",
            new[]
            {
                Path.GetFullPath(project.GameRoot),
                kind.ToString(),
                id.ToString(CultureInfo.InvariantCulture),
                NormalizeStance(kind, stance).ToString(),
                sFactionSlot.ToString(CultureInfo.InvariantCulture)
            }
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
    bool FreeOnly,
    int CandidateResourceCount,
    int AssignedCount,
    IReadOnlyList<FreeImageAssignmentCandidate> FreeCandidates,
    IReadOnlyList<string> Warnings,
    bool AvailableIdsFromCache = false)
{
    public IReadOnlyList<FreeImageAssignmentCandidate> Items => FreeCandidates;
}

internal sealed record FreeImageAssignmentCandidate(int Id, string Detail);

internal sealed record CachedPreviewImage(byte[] PngBytes, Size Size, string CacheKey, string? Detail = null)
{
    public Bitmap CreateBitmap()
    {
        using var stream = new MemoryStream(PngBytes, writable: false);
        using var image = Image.FromStream(stream, useEmbeddedColorManagement: false, validateImageData: false);
        return new Bitmap(image);
    }

    public static CachedPreviewImage FromBitmap(Bitmap bitmap, string cacheKey, string? detail = null)
    {
        using var stream = new MemoryStream();
        bitmap.Save(stream, ImageFormat.Png);
        return new CachedPreviewImage(stream.ToArray(), bitmap.Size, cacheKey, detail);
    }
}
