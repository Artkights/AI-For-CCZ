using CCZModStudio.Formats;
using CCZModStudio.Models;
using System.Drawing;

namespace CCZModStudio.Core;

public sealed class MapTerrainPublishCoordinator
{
    private readonly MapCanvasPublishService _mapPublishService = new();
    private readonly HexzmapEditorService _terrainPublishService = new();

    public MapTerrainPublishResult Publish(
        CczProject project,
        MapWorkbenchDraft draft,
        MapResourceItem mapTarget,
        IReadOnlyList<MaterialAsset> materials,
        HexzmapProbeResult probe,
        HexzmapBlockInfo block,
        byte[] terrainCells,
        IReadOnlyDictionary<byte, string>? terrainNames,
        HexzmapBlockBinding? binding,
        bool publishTerrain,
        Bitmap? confirmedRender = null)
    {
        var mapPath = mapTarget.Path;
        var terrainPath = project.ResolveGameFile("Hexzmap.e5");
        var originalMap = File.ReadAllBytes(mapPath);
        var originalTerrain = File.ReadAllBytes(terrainPath);
        ValidateBeforeMutation(draft, mapTarget, block, terrainCells, binding, publishTerrain);

        MapImageSaveResult? mapResult = null;
        HexzmapSaveResult? terrainResult = null;
        try
        {
            mapResult = _mapPublishService.PublishToMapImage(project, draft, mapTarget, materials, confirmedRender);
            if (publishTerrain)
            {
                terrainResult = _terrainPublishService.SaveBlock(project, probe, block, terrainCells, terrainNames, binding);
            }

            return new MapTerrainPublishResult
            {
                MapResult = mapResult,
                TerrainResult = terrainResult,
                RollbackAttempted = false,
                RollbackSucceeded = false
            };
        }
        catch (Exception publishError)
        {
            var rollbackErrors = new List<Exception>();
            TryRestore(mapPath, originalMap, rollbackErrors);
            TryRestore(terrainPath, originalTerrain, rollbackErrors);
            if (rollbackErrors.Count > 0)
            {
                throw new AggregateException("联合发布失败，且至少一个目标文件回滚失败。", new[] { publishError }.Concat(rollbackErrors));
            }

            throw new InvalidOperationException("联合发布失败；地图与 Hexzmap 已恢复到发布前内容。", publishError);
        }
    }

    private static void ValidateBeforeMutation(
        MapWorkbenchDraft draft,
        MapResourceItem mapTarget,
        HexzmapBlockInfo block,
        byte[] terrainCells,
        HexzmapBlockBinding? binding,
        bool publishTerrain)
    {
        if (mapTarget.GridWidth != draft.GridWidth || mapTarget.GridHeight != draft.GridHeight)
        {
            throw new InvalidOperationException("联合发布前校验失败：地图草稿尺寸与目标地图不一致。");
        }

        if (!publishTerrain) return;
        if (terrainCells.Length != checked(block.Width * block.Height) ||
            block.Width != draft.GridWidth || block.Height != draft.GridHeight || !block.CanEdit)
        {
            throw new InvalidOperationException("联合发布前校验失败：Hexzmap 块尺寸、段结构或草稿格数不一致。");
        }

        if (binding?.AuthorizesWrite != true || binding.DirectoryEntryIndex != block.Index)
        {
            throw new InvalidOperationException("联合发布前校验失败：Hexzmap 块没有可信写入绑定。");
        }
    }

    private static void TryRestore(string targetPath, byte[] original, List<Exception> errors)
    {
        var tempPath = Path.Combine(Path.GetDirectoryName(targetPath)!, $".{Path.GetFileName(targetPath)}.{Guid.NewGuid():N}.rollback.tmp");
        try
        {
            File.WriteAllBytes(tempPath, original);
            try
            {
                File.Replace(tempPath, targetPath, destinationBackupFileName: null, ignoreMetadataErrors: true);
            }
            catch (PlatformNotSupportedException)
            {
                File.Move(tempPath, targetPath, overwrite: true);
            }

            var verify = File.ReadAllBytes(targetPath);
            if (!verify.AsSpan().SequenceEqual(original))
            {
                throw new InvalidOperationException("回滚后逐字节复读不一致：" + targetPath);
            }
        }
        catch (Exception ex)
        {
            errors.Add(ex);
        }
        finally
        {
            if (File.Exists(tempPath)) File.Delete(tempPath);
        }
    }
}

public sealed class MapTerrainPublishResult
{
    public MapImageSaveResult? MapResult { get; init; }
    public HexzmapSaveResult? TerrainResult { get; init; }
    public bool RollbackAttempted { get; init; }
    public bool RollbackSucceeded { get; init; }
}
