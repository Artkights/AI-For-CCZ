using System.Drawing;
using CCZModStudio.Models;

namespace CCZModStudio.Core;

public sealed class MapResizeService
{
    public MapResizePreview Preview(MapWorkbenchDraft draft, MapResizeRequest request)
    {
        ArgumentNullException.ThrowIfNull(draft);
        ValidateRequest(draft, request);

        return new MapResizePreview
        {
            OldWidth = draft.GridWidth,
            OldHeight = draft.GridHeight,
            NewWidth = request.NewWidth,
            NewHeight = request.NewHeight,
            AddedCells = CountAddedCells(draft.GridWidth, draft.GridHeight, request.NewWidth, request.NewHeight),
            RemovedCells = CountRemovedCells(draft.GridWidth, draft.GridHeight, request.NewWidth, request.NewHeight),
            RemovedManualOverrides = CountRemoved(draft.MapCellOverrides, draft.GridWidth, request.NewWidth, request.NewHeight),
            RemovedTerrainBaseCells = CountRemoved(draft.TerrainBaseCells, draft.GridWidth, request.NewWidth, request.NewHeight),
            RemovedGeneratedCells = CountRemoved(draft.GeneratedMapCells, draft.GridWidth, request.NewWidth, request.NewHeight),
            RemovedBuildingCells = CountRemoved(draft.BuildingOverlayCells, draft.GridWidth, request.NewWidth, request.NewHeight),
            RemovedSceneryCells = CountRemoved(draft.SceneryOverlayCells, draft.GridWidth, request.NewWidth, request.NewHeight),
            RemovedSceneryOverlays = draft.SceneryOverlays.Count(overlay => !IntersectsCanvas(overlay, request.NewWidth, request.NewHeight, draft.TileSize))
        };
    }

    public MapResizePreview Apply(MapWorkbenchDraft draft, MapResizeRequest request)
    {
        var preview = Preview(draft, request);
        var oldWidth = draft.GridWidth;
        var oldHeight = draft.GridHeight;

        draft.OriginalTerrainCells = RemapTerrain(
            draft.OriginalTerrainCells,
            oldWidth,
            oldHeight,
            request.NewWidth,
            request.NewHeight,
            request.TerrainFillId);
        draft.TerrainCells = RemapTerrain(
            draft.TerrainCells,
            oldWidth,
            oldHeight,
            request.NewWidth,
            request.NewHeight,
            request.TerrainFillId);
        draft.MapCellOverrides = RemapCells(draft.MapCellOverrides, oldWidth, request.NewWidth, request.NewHeight, MapCellOverrideSources.ManualOverride);
        draft.TerrainBaseCells = RemapCells(draft.TerrainBaseCells, oldWidth, request.NewWidth, request.NewHeight, MapCellOverrideSources.TerrainBase);
        draft.GeneratedMapCells = RemapCells(draft.GeneratedMapCells, oldWidth, request.NewWidth, request.NewHeight, MapCellOverrideSources.Generated);
        draft.BuildingOverlayCells = RemapCells(draft.BuildingOverlayCells, oldWidth, request.NewWidth, request.NewHeight, MapCellOverrideSources.BuildingOverlay);
        draft.SceneryOverlayCells = RemapCells(draft.SceneryOverlayCells, oldWidth, request.NewWidth, request.NewHeight, MapCellOverrideSources.SceneryOverlay);
        draft.SceneryOverlays = draft.SceneryOverlays
            .Where(overlay => IntersectsCanvas(overlay, request.NewWidth, request.NewHeight, draft.TileSize))
            .ToList();
        draft.GridWidth = request.NewWidth;
        draft.GridHeight = request.NewHeight;
        if (draft.HexzmapBinding != null)
        {
            draft.HexzmapBinding.Width = request.NewWidth;
            draft.HexzmapBinding.Height = request.NewHeight;
        }

        return preview;
    }

    public static List<MapCellOverride> RemapCells(
        IEnumerable<MapCellOverride> cells,
        int oldWidth,
        int newWidth,
        int newHeight,
        string defaultSource)
        => cells
            .Where(cell => IsRetained(cell.Index, oldWidth, newWidth, newHeight))
            .Select(cell =>
            {
                var x = cell.Index % oldWidth;
                var y = cell.Index / oldWidth;
                return new MapCellOverride
                {
                    Index = y * newWidth + x,
                    MaterialRelativePath = cell.MaterialRelativePath,
                    MaterialCategory = cell.MaterialCategory,
                    DisplayName = cell.DisplayName,
                    Source = string.IsNullOrWhiteSpace(cell.Source) ? defaultSource : cell.Source
                };
            })
            .OrderBy(cell => cell.Index)
            .ToList();

    private static byte[] RemapTerrain(
        byte[] source,
        int oldWidth,
        int oldHeight,
        int newWidth,
        int newHeight,
        byte fillId)
    {
        var output = Enumerable.Repeat(fillId, checked(newWidth * newHeight)).ToArray();
        var copyWidth = Math.Min(oldWidth, newWidth);
        var copyHeight = Math.Min(oldHeight, newHeight);
        for (var y = 0; y < copyHeight; y++)
        {
            var available = Math.Clamp(source.Length - y * oldWidth, 0, copyWidth);
            if (available > 0) Array.Copy(source, y * oldWidth, output, y * newWidth, available);
        }

        return output;
    }

    private static int CountRemoved(IEnumerable<MapCellOverride> cells, int oldWidth, int newWidth, int newHeight)
        => cells.Count(cell => !IsRetained(cell.Index, oldWidth, newWidth, newHeight));

    private static bool IsRetained(int index, int oldWidth, int newWidth, int newHeight)
    {
        if (oldWidth <= 0 || index < 0) return false;
        var x = index % oldWidth;
        var y = index / oldWidth;
        return x < newWidth && y < newHeight;
    }

    private static bool IntersectsCanvas(MapSceneryOverlay overlay, int gridWidth, int gridHeight, int tileSize)
    {
        var effectiveTileSize = tileSize <= 0 ? MapResourceItem.MapTilePixelSize : tileSize;
        var canvas = new Rectangle(0, 0, checked(gridWidth * effectiveTileSize), checked(gridHeight * effectiveTileSize));
        var bounds = new Rectangle(
            overlay.X,
            overlay.Y,
            Math.Max(1, overlay.Width),
            Math.Max(1, overlay.Height));
        return canvas.IntersectsWith(bounds);
    }

    private static int CountAddedCells(int oldWidth, int oldHeight, int newWidth, int newHeight)
    {
        var overlap = Math.Min(oldWidth, newWidth) * Math.Min(oldHeight, newHeight);
        return Math.Max(0, checked(newWidth * newHeight) - overlap);
    }

    private static int CountRemovedCells(int oldWidth, int oldHeight, int newWidth, int newHeight)
    {
        var overlap = Math.Min(oldWidth, newWidth) * Math.Min(oldHeight, newHeight);
        return Math.Max(0, checked(oldWidth * oldHeight) - overlap);
    }

    private static void ValidateRequest(MapWorkbenchDraft draft, MapResizeRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.NewWidth < 1 || request.NewHeight < 1)
        {
            throw new InvalidOperationException("地图草稿尺寸不能小于 1x1 格。");
        }
        if (request.OldWidth > 0 && request.OldWidth != draft.GridWidth ||
            request.OldHeight > 0 && request.OldHeight != draft.GridHeight)
        {
            throw new InvalidOperationException("草稿尺寸已在预览后发生变化，请重新预览。");
        }
    }
}
