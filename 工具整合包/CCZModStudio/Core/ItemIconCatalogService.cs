using System.Data;
using System.Globalization;
using System.Drawing;
using CCZModStudio.Models;

namespace CCZModStudio.Core;

internal sealed class ItemIconCatalogService
{
    private static readonly string[] ItemIconColumnNames =
    [
        "\u56fe\u6807",
        "鍥炬爣"
    ];

    private readonly ItemIconPreviewService _previewService;
    private readonly E5ImageReadSessionPool _readSessions = E5ImageReadSessionPool.Shared;
    private readonly ImagePreviewCache _previewCache = ImagePreviewCache.Shared;

    public ItemIconCatalogService(ItemIconPreviewService? previewService = null)
    {
        _previewService = previewService ?? new ItemIconPreviewService();
    }

    public ItemIconCatalogResult Build(CczProject project, DataTable itemData, bool freeOnly)
    {
        var warnings = new List<string>();
        var available = CollectAvailableIconIds(project, warnings);
        var used = CollectUsedIconIds(itemData);
        var items = (freeOnly ? available.Where(id => !used.Contains(id)) : available)
            .Distinct()
            .OrderBy(id => id)
            .Select(id => new ItemIconCatalogCandidate(id, BuildDetail(project, id)))
            .ToArray();

        return new ItemIconCatalogResult(
            freeOnly,
            available.Count,
            used.Count,
            items,
            warnings);
    }

    public Task<ItemIconCatalogResult> BuildAsync(
        CczProject project,
        DataTable itemData,
        bool freeOnly,
        CancellationToken cancellationToken)
        => Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Build(project, itemData, freeOnly);
        }, cancellationToken);

    public Bitmap? RenderCandidatePreview(CczProject project, int iconId, int canvasSize)
    {
        var result = _previewService.BuildPreview(project, iconId, canvasSize);
        try
        {
            var source = result.LargeBitmap ?? result.NativeBitmap ?? result.Bitmap;
            return source == null ? null : new Bitmap(source);
        }
        finally
        {
            DisposePreviewResultBitmaps(result);
        }
    }

    public async Task<CachedPreviewImage?> LoadThumbnailAsync(
        CczProject project,
        int iconId,
        int canvasSize,
        CancellationToken cancellationToken)
    {
        var resourceFile = Ccz66RevisedLayout.ResolveItemIconResourceFile(project);
        var path = Ccz66RevisedLayout.IsE5IconResource(resourceFile)
            ? Ccz66RevisedLayout.ResolveResourcePath(project, resourceFile)
            : Path.Combine(project.GameRoot, resourceFile);
        var fingerprint = File.Exists(path)
            ? E5ResourceFingerprint.CreateFast(path).ToString()
            : "missing";
        var key = string.Join("|", "item-icon-v2", Path.GetFullPath(path), fingerprint, iconId, canvasSize);
        var cached = await _previewCache.GetOrCreateAsync(
            key,
            () => Task.Run(() => RenderPng(), CancellationToken.None),
            cancellationToken).ConfigureAwait(false);
        if (cached == null) return null;

        using var stream = new MemoryStream(cached.Bytes, writable: false);
        using var image = Image.FromStream(stream, false, false);
        return new CachedPreviewImage(cached.Bytes, image.Size, key, $"cache={cached.Source}");

        byte[]? RenderPng()
        {
            using var bitmap = RenderCandidatePreview(project, iconId, canvasSize);
            if (bitmap == null) return null;
            using var output = new MemoryStream();
            bitmap.Save(output, System.Drawing.Imaging.ImageFormat.Png);
            return output.ToArray();
        }
    }

    private IReadOnlyList<int> CollectAvailableIconIds(CczProject project, List<string> warnings)
    {
        var resourceFile = Ccz66RevisedLayout.ResolveItemIconResourceFile(project);
        if (Ccz66RevisedLayout.IsE5IconResource(resourceFile))
        {
            return CollectAvailableE5IconIds(project, resourceFile, warnings);
        }

        return CollectAvailableDllIconIds(project, resourceFile, warnings);
    }

    private IReadOnlyList<int> CollectAvailableE5IconIds(
        CczProject project,
        string resourceFile,
        List<string> warnings)
    {
        var sourcePath = Ccz66RevisedLayout.ResolveResourcePath(project, resourceFile);
        if (!File.Exists(sourcePath))
        {
            warnings.Add($"未找到宝物图标资源：{sourcePath}");
            return Array.Empty<int>();
        }

        var entries = _readSessions.GetSession(sourcePath).ReadIndex();
        if (entries.Count == 0)
        {
            warnings.Add($"{Path.GetFileName(sourcePath)} 未识别到 E5 图片索引。");
            return Array.Empty<int>();
        }

        if (entries.Count % 2 != 0)
        {
            warnings.Add($"{Path.GetFileName(sourcePath)} 图片条目数为奇数，最后一个条目不会作为宝物图标字段编号显示。");
        }

        var fieldCount = entries.Count / 2;
        return Enumerable.Range(0, fieldCount).ToArray();
    }

    private IReadOnlyList<int> CollectAvailableDllIconIds(
        CczProject project,
        string resourceFile,
        List<string> warnings)
    {
        var availableCount = _previewService.GetAvailableBitmapIconCount(project, resourceFile);
        if (availableCount <= 0)
        {
            warnings.Add($"{resourceFile} 未解析到可用的图标资源。");
            return Array.Empty<int>();
        }

        return Enumerable.Range(0, availableCount).ToArray();
    }

    private static HashSet<int> CollectUsedIconIds(DataTable itemData)
    {
        var result = new HashSet<int>();
        var columnName = ItemIconColumnNames.FirstOrDefault(itemData.Columns.Contains);
        if (string.IsNullOrWhiteSpace(columnName)) return result;

        foreach (DataRow row in itemData.Rows)
        {
            if (row.RowState == DataRowState.Deleted) continue;
            if (!TryReadCurrentInt(row, columnName, out var iconId) || iconId < 0) continue;
            result.Add(iconId);
        }

        return result;
    }

    private string BuildDetail(CczProject project, int iconId)
    {
        var resourceFile = Ccz66RevisedLayout.ResolveItemIconResourceFile(project);
        if (Ccz66RevisedLayout.IsE5IconResource(resourceFile))
        {
            var pair = Ccz66RevisedLayout.ResolveItemIconImageNumbers(iconId);
            return $"{Path.GetFileName(resourceFile)} small=#{pair.Small}, large=#{pair.Large}";
        }

        return $"{Path.GetFileName(resourceFile)} #{iconId}";
    }

    private static bool TryReadCurrentInt(DataRow row, string columnName, out int value)
    {
        value = 0;
        try
        {
            var raw = row.RowState == DataRowState.Detached
                ? row[columnName]
                : row[columnName, DataRowVersion.Current];
            return int.TryParse(
                Convert.ToString(raw, CultureInfo.InvariantCulture),
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out value);
        }
        catch (VersionNotFoundException)
        {
            return false;
        }
    }

    private static void DisposePreviewResultBitmaps(ItemIconPreviewResult result)
    {
        var bitmap = result.Bitmap;
        var native = result.NativeBitmap;
        var small = result.SmallBitmap;
        var large = result.LargeBitmap;

        bitmap?.Dispose();
        if (!ReferenceEquals(native, bitmap)) native?.Dispose();
        if (!ReferenceEquals(small, bitmap) && !ReferenceEquals(small, native)) small?.Dispose();
        if (!ReferenceEquals(large, bitmap) && !ReferenceEquals(large, native) && !ReferenceEquals(large, small)) large?.Dispose();
    }
}

internal sealed record ItemIconCatalogResult(
    bool FreeOnly,
    int AvailableCount,
    int UsedCount,
    IReadOnlyList<ItemIconCatalogCandidate> Items,
    IReadOnlyList<string> Warnings)
{
    public int DisplayedCount => Items.Count;
}

internal sealed record ItemIconCatalogCandidate(int IconId, string Detail);
