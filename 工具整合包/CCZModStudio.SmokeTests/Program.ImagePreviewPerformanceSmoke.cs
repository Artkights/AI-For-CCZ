using CCZModStudio.Core;
using CCZModStudio.Models;
using System.Diagnostics;
using System.Data;
using System.Text.Json;

internal partial class Program
{
    static void RunImagePreviewPerformanceSmoke(CczProject project)
    {
        var pool = E5ImageReadSessionPool.Shared;
        pool.Clear();
        ImagePreviewCache.Shared.ClearMemory();
        ImagePreviewCache.Shared.ClearDisk();

        var resourceNames = new[] { "Pmapobj.e5", "Unit_atk.e5", "Unit_mov.e5", "Unit_spc.e5", "Face.e5" };
        var resourceReports = new List<object>();
        foreach (var resourceName in resourceNames)
        {
            var path = resourceName.Equals("Face.e5", StringComparison.OrdinalIgnoreCase)
                ? CharacterImageResourceService.ResolveFaceFile(project) ?? Path.Combine(project.GameRoot, "E5", resourceName)
                : CharacterImageResourceService.ResolveGameFile(project, resourceName);
            if (!File.Exists(path)) continue;

            var session = pool.GetSession(path);
            var watch = Stopwatch.StartNew();
            var entries = session.ReadIndex();
            watch.Stop();
            var metrics = session.Metrics;
            var fileSize = new FileInfo(path).Length;
            if (fileSize > 1024 * 1024 && metrics.BytesRead >= fileSize / 4)
            {
                throw new InvalidOperationException($"{resourceName} index read consumed too much data: {metrics.BytesRead:N0}/{fileSize:N0} bytes.");
            }

            if (entries.Count > 0)
            {
                var before = session.Metrics.EntryReadCount;
                Task.WhenAll(Enumerable.Range(0, 8).Select(_ => session.ReadDecodedEntryAsync(1))).GetAwaiter().GetResult();
                var entryReads = session.Metrics.EntryReadCount - before;
                if (entryReads != 1)
                {
                    throw new InvalidOperationException($"{resourceName} concurrent entry requests should perform one underlying read; actual={entryReads}.");
                }
            }

            resourceReports.Add(new
            {
                resourceName,
                fileSize,
                entries = entries.Count,
                indexMilliseconds = watch.Elapsed.TotalMilliseconds,
                bytesRead = session.Metrics.BytesRead,
                entryReadCount = session.Metrics.EntryReadCount,
                decodeCount = session.Metrics.DecodeCount
            });
        }

        var previewService = new ImageAssignmentPreviewService();
        var sId = 1;
        var coldWatch = Stopwatch.StartNew();
        using var cold = previewService.LoadAnimationAsync(
            project,
            ImageAssignmentResourceKind.S,
            sId,
            jobId: null,
            CharacterImageResourceService.DefaultSPreviewFactionSlot,
            stageSlot: 1,
            CancellationToken.None).GetAwaiter().GetResult();
        coldWatch.Stop();
        if (cold.PrecomposedFrames.Count == 0)
        {
            throw new InvalidOperationException("S animation performance smoke produced no precomposed frames.");
        }

        var warmWatch = Stopwatch.StartNew();
        using var warm = previewService.LoadAnimationAsync(
            project,
            ImageAssignmentResourceKind.S,
            sId,
            jobId: null,
            CharacterImageResourceService.DefaultSPreviewFactionSlot,
            stageSlot: 1,
            CancellationToken.None).GetAwaiter().GetResult();
        warmWatch.Stop();
        var deadline = DateTime.UtcNow.AddSeconds(2);
        while (DateTime.UtcNow < deadline &&
               (!Directory.Exists(ImagePreviewCache.Shared.CacheDirectory) ||
                Directory.GetFiles(ImagePreviewCache.Shared.CacheDirectory, "*.png").Length < cold.PrecomposedFrames.Count))
        {
            Thread.Sleep(25);
        }

        ImagePreviewCache.Shared.ClearMemory();
        var diskWarmWatch = Stopwatch.StartNew();
        using var diskWarm = previewService.LoadAnimationAsync(
            project,
            ImageAssignmentResourceKind.S,
            sId,
            jobId: null,
            CharacterImageResourceService.DefaultSPreviewFactionSlot,
            stageSlot: 1,
            CancellationToken.None).GetAwaiter().GetResult();
        diskWarmWatch.Stop();
        using var frame = previewService.BuildAnimationCanvas(warm, 0);
        if (frame.Width <= 0 || frame.Height <= 0)
        {
            throw new InvalidOperationException("Cached S animation frame is invalid.");
        }

        var itemData = new DataTable("ItemIconPerformanceSmoke");
        itemData.Columns.Add("图标", typeof(int));
        itemData.Rows.Add(0);
        var itemIconService = new ItemIconCatalogService();
        var itemCatalogWatch = Stopwatch.StartNew();
        var itemCatalog = itemIconService.Build(project, itemData, freeOnly: false);
        itemCatalogWatch.Stop();
        if (itemCatalog.Items.Count == 0)
        {
            throw new InvalidOperationException("Item icon catalog performance smoke found no candidates.");
        }

        var itemThumbnailWatch = Stopwatch.StartNew();
        var itemThumbnail = itemIconService.LoadThumbnailAsync(
            project,
            itemCatalog.Items[0].IconId,
            96,
            CancellationToken.None).GetAwaiter().GetResult();
        itemThumbnailWatch.Stop();
        if (itemThumbnail == null)
        {
            throw new InvalidOperationException("Item icon catalog performance smoke could not render its first thumbnail.");
        }

        var report = new
        {
            generatedAt = DateTimeOffset.Now,
            project = project.GameRoot,
            resources = resourceReports,
            sAnimation = new
            {
                id = sId,
                coldMilliseconds = coldWatch.Elapsed.TotalMilliseconds,
                memoryWarmMilliseconds = warmWatch.Elapsed.TotalMilliseconds,
                diskWarmMilliseconds = diskWarmWatch.Elapsed.TotalMilliseconds,
                frameCount = warm.PrecomposedFrames.Count,
                cacheDirectory = ImagePreviewCache.Shared.CacheDirectory
            },
            itemIcon = new
            {
                candidates = itemCatalog.Items.Count,
                catalogMilliseconds = itemCatalogWatch.Elapsed.TotalMilliseconds,
                firstThumbnailMilliseconds = itemThumbnailWatch.Elapsed.TotalMilliseconds,
                firstThumbnailCache = itemThumbnail.Detail
            }
        };
        Console.WriteLine(JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine("IMAGE_PREVIEW_PERFORMANCE_SMOKE_OK");
    }
}
