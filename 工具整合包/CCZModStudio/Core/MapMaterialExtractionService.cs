using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Globalization;
using CCZModStudio.Formats;
using CCZModStudio.Models;

namespace CCZModStudio.Core;

public sealed class MapMaterialExtractionService
{
    private static readonly string[] ImageExtensions = [".png", ".jpg", ".jpeg", ".bmp"];
    private readonly MapCanvasComposeService _composeService = new();
    private readonly MaterialDrivenTerrainService _materialDrivenService = new();

    public MapMaterialExtractionPreview Preview(MapMaterialExtractionRequest request)
    {
        var normalized = NormalizeRequest(request);
        var targetDirectory = ResolveTargetDirectory(normalized.MaterialRoot, normalized.TargetType, normalized.TerrainId);
        var paths = BuildOutputPaths(targetDirectory, CountCells(normalized.CellRange));
        return new MapMaterialExtractionPreview
        {
            TargetDirectory = targetDirectory,
            StartSequence = paths.Count == 0 ? 0 : paths[0].Sequence,
            EndSequence = paths.Count == 0 ? 0 : paths[^1].Sequence,
            FileCount = paths.Count,
            PlannedPaths = paths.Select(path => path.Path).ToList()
        };
    }

    public MapMaterialExtractionResult Extract(MapMaterialExtractionRequest request)
    {
        var normalized = NormalizeRequest(request);
        var targetDirectory = ResolveTargetDirectory(normalized.MaterialRoot, normalized.TargetType, normalized.TerrainId);
        Directory.CreateDirectory(targetDirectory);
        var outputPaths = BuildOutputPaths(targetDirectory, CountCells(normalized.CellRange));
        if (outputPaths.Count == 0)
        {
            throw new InvalidOperationException("No map cells were selected for material extraction.");
        }

        using var source = CreateSourceBitmap(normalized);
        ValidateSourceDimensions(normalized.Draft, source);
        var crops = new List<(int CellIndex, int CellX, int CellY, int Sequence, string Path, Bitmap Bitmap)>();
        var tempFiles = new List<string>();
        try
        {
            var outputIndex = 0;
            foreach (var cell in EnumerateCells(normalized.Draft, normalized.CellRange))
            {
                var output = outputPaths[outputIndex++];
                crops.Add((cell.Index, cell.X, cell.Y, output.Sequence, output.Path, CropCell(source, normalized.Draft, cell.X, cell.Y)));
            }

            foreach (var crop in crops)
            {
                var tempPath = Path.Combine(targetDirectory, "." + Guid.NewGuid().ToString("N") + ".tmp.png");
                crop.Bitmap.Save(tempPath, ImageFormat.Png);
                tempFiles.Add(tempPath);
            }

            var files = new List<MapMaterialExtractionFile>();
            for (var i = 0; i < crops.Count; i++)
            {
                var crop = crops[i];
                var tempPath = tempFiles[i];
                var finalPath = crop.Path;
                if (File.Exists(finalPath))
                {
                    throw new IOException("Material extraction target already exists: " + finalPath);
                }

                File.Move(tempPath, finalPath);
                files.Add(new MapMaterialExtractionFile
                {
                    CellIndex = crop.CellIndex,
                    CellX = crop.CellX,
                    CellY = crop.CellY,
                    Sequence = crop.Sequence,
                    Path = finalPath
                });
            }

            return new MapMaterialExtractionResult
            {
                TargetDirectory = targetDirectory,
                StartSequence = files.Count == 0 ? 0 : files[0].Sequence,
                EndSequence = files.Count == 0 ? 0 : files[^1].Sequence,
                Files = files
            };
        }
        finally
        {
            foreach (var crop in crops)
            {
                crop.Bitmap.Dispose();
            }

            foreach (var tempPath in tempFiles)
            {
                try
                {
                    if (File.Exists(tempPath))
                    {
                        File.Delete(tempPath);
                    }
                }
                catch
                {
                    // Best-effort cleanup for failed extraction attempts.
                }
            }
        }
    }

    public string ResolveTargetDirectory(string materialRoot, MapMaterialExtractionTargetType targetType, byte? terrainId)
    {
        if (string.IsNullOrWhiteSpace(materialRoot))
        {
            throw new InvalidOperationException("Material library root is not set.");
        }

        var root = Path.GetFullPath(materialRoot);
        EnsureNewLayoutMaterialRoot(root);
        return targetType switch
        {
            MapMaterialExtractionTargetType.Terrain => ResolveTypedDirectory(root, "地形", terrainId),
            MapMaterialExtractionTargetType.Building => ResolveTypedDirectory(root, "建筑", terrainId),
            MapMaterialExtractionTargetType.Scenery => Path.Combine(root, "景物", "提取素材"),
            _ => throw new InvalidOperationException("Unsupported material extraction target type.")
        };
    }

    private MapMaterialExtractionRequest NormalizeRequest(MapMaterialExtractionRequest request)
    {
        if (request.Draft == null)
        {
            throw new InvalidOperationException("Map draft is required for material extraction.");
        }

        var draft = request.Draft;
        if (draft.GridWidth <= 0 || draft.GridHeight <= 0)
        {
            throw new InvalidOperationException("Map draft grid size is invalid.");
        }

        var cellRange = NormalizeCellRange(request.CellRange);
        if (cellRange.Width <= 0 || cellRange.Height <= 0)
        {
            throw new InvalidOperationException("No map cells were selected for material extraction.");
        }

        if (cellRange.Left < 0 ||
            cellRange.Top < 0 ||
            cellRange.Right > draft.GridWidth ||
            cellRange.Bottom > draft.GridHeight)
        {
            throw new InvalidOperationException("Selected map cells are outside the current map.");
        }

        if ((request.TargetType == MapMaterialExtractionTargetType.Terrain ||
             request.TargetType == MapMaterialExtractionTargetType.Building) &&
            !request.TerrainId.HasValue)
        {
            throw new InvalidOperationException("Terrain id is required for terrain/building material extraction.");
        }

        return new MapMaterialExtractionRequest
        {
            Draft = draft,
            MaterialRoot = request.MaterialRoot?.Trim() ?? string.Empty,
            CellRange = cellRange,
            TargetType = request.TargetType,
            TerrainId = request.TerrainId,
            Source = request.Source,
            Materials = request.Materials ?? Array.Empty<MaterialAsset>()
        };
    }

    private static Rectangle NormalizeCellRange(Rectangle range)
    {
        if (range.Width < 0)
        {
            range = new Rectangle(range.Right, range.Y, -range.Width, range.Height);
        }

        if (range.Height < 0)
        {
            range = new Rectangle(range.X, range.Bottom, range.Width, -range.Height);
        }

        return range;
    }

    private static string ResolveTypedDirectory(string root, string category, byte? terrainId)
    {
        if (!terrainId.HasValue)
        {
            throw new InvalidOperationException("Terrain id is required for typed material extraction.");
        }

        var id = terrainId.Value;
        return Path.Combine(root, category, $"{id}：{MaterialLibraryIndexer.GetBuiltInTerrainName(id)}");
    }

    private static void EnsureNewLayoutMaterialRoot(string root)
    {
        if (!Directory.Exists(root))
        {
            throw new DirectoryNotFoundException("Material library root does not exist: " + root);
        }

        var terrainRoot = Path.Combine(root, "地形");
        var buildingRoot = Path.Combine(root, "建筑");
        var sceneryRoot = Path.Combine(root, "景物");
        if (!Directory.Exists(terrainRoot) || !Directory.Exists(buildingRoot) || !Directory.Exists(sceneryRoot))
        {
            throw new InvalidOperationException("Material extraction requires the new material library layout with 地形 / 建筑 / 景物 folders.");
        }
    }

    private Bitmap CreateSourceBitmap(MapMaterialExtractionRequest request)
    {
        var draft = CloneDraft(request.Draft);
        return request.Source switch
        {
            MapMaterialExtractionSource.CurrentComposite => _composeService.ComposePreview(
                draft,
                request.Materials,
                showTerrain: false,
                showGrid: false,
                terrainOpacityPercent: 0,
                beautifyGeneratedMap: draft.BeautifyGeneratedMap),
            MapMaterialExtractionSource.OriginalBaseMap => LoadOriginalBaseMap(draft),
            MapMaterialExtractionSource.GeneratedTerrainBase => _materialDrivenService.ComposeBaseTerrain(
                draft,
                request.Materials,
                checkerboardBlank: false),
            _ => throw new InvalidOperationException("Unsupported material extraction source.")
        };
    }

    private static Bitmap LoadOriginalBaseMap(MapWorkbenchDraft draft)
    {
        if (string.IsNullOrWhiteSpace(draft.BaseLayerPath) || !File.Exists(draft.BaseLayerPath))
        {
            throw new FileNotFoundException("Map base layer image does not exist.", draft.BaseLayerPath);
        }

        using var image = Image.FromFile(draft.BaseLayerPath);
        return new Bitmap(image);
    }

    private static void ValidateSourceDimensions(MapWorkbenchDraft draft, Image source)
    {
        var tileSize = draft.TileSize <= 0 ? MapResourceItem.MapTilePixelSize : draft.TileSize;
        var expectedWidth = checked(draft.GridWidth * tileSize);
        var expectedHeight = checked(draft.GridHeight * tileSize);
        if (source.Width != expectedWidth || source.Height != expectedHeight)
        {
            throw new InvalidOperationException($"Material extraction source size mismatch: actual={source.Width}x{source.Height}, expected={expectedWidth}x{expectedHeight}.");
        }
    }

    private static List<(int Sequence, string Path)> BuildOutputPaths(string targetDirectory, int count)
    {
        var result = new List<(int Sequence, string Path)>();
        if (count <= 0) return result;

        var used = Directory.Exists(targetDirectory)
            ? Directory.GetFiles(targetDirectory)
                .Where(path => ImageExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase))
                .Select(path => Path.GetFileNameWithoutExtension(path))
                .Where(name => int.TryParse(name, NumberStyles.Integer, CultureInfo.InvariantCulture, out _))
                .Select(name => int.Parse(name!, CultureInfo.InvariantCulture))
                .ToHashSet()
            : new HashSet<int>();

        var next = used.Count == 0 ? 1 : used.Max() + 1;
        while (result.Count < count)
        {
            var path = Path.Combine(targetDirectory, next.ToString(CultureInfo.InvariantCulture) + ".png");
            if (!used.Contains(next) && !File.Exists(path))
            {
                result.Add((next, path));
                used.Add(next);
            }

            next++;
        }

        return result;
    }

    private static IEnumerable<(int Index, int X, int Y)> EnumerateCells(MapWorkbenchDraft draft, Rectangle cellRange)
    {
        for (var y = cellRange.Top; y < cellRange.Bottom; y++)
        {
            for (var x = cellRange.Left; x < cellRange.Right; x++)
            {
                yield return (y * draft.GridWidth + x, x, y);
            }
        }
    }

    private static int CountCells(Rectangle cellRange)
        => Math.Max(0, cellRange.Width) * Math.Max(0, cellRange.Height);

    private static Bitmap CropCell(Image source, MapWorkbenchDraft draft, int cellX, int cellY)
    {
        var tileSize = draft.TileSize <= 0 ? MapResourceItem.MapTilePixelSize : draft.TileSize;
        var sourceRect = new Rectangle(cellX * tileSize, cellY * tileSize, tileSize, tileSize);
        var bitmap = new Bitmap(MapResourceItem.MapTilePixelSize, MapResourceItem.MapTilePixelSize, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bitmap);
        g.SmoothingMode = SmoothingMode.None;
        g.InterpolationMode = InterpolationMode.NearestNeighbor;
        g.PixelOffsetMode = PixelOffsetMode.Half;
        g.DrawImage(source, new Rectangle(0, 0, bitmap.Width, bitmap.Height), sourceRect, GraphicsUnit.Pixel);
        return bitmap;
    }

    private static MapWorkbenchDraft CloneDraft(MapWorkbenchDraft draft)
        => new()
        {
            DraftId = draft.DraftId,
            BoundMapId = draft.BoundMapId,
            GridWidth = draft.GridWidth,
            GridHeight = draft.GridHeight,
            TileSize = draft.TileSize,
            BaseLayerPath = draft.BaseLayerPath,
            MaterialRoot = draft.MaterialRoot,
            TerrainMaterialPlan = draft.TerrainMaterialPlan.Select(CloneTerrainMaterialPlanItem).ToList(),
            MapCellOverrides = draft.MapCellOverrides.Select(CloneCell).ToList(),
            TerrainBaseCells = draft.TerrainBaseCells.Select(CloneCell).ToList(),
            GeneratedMapCells = draft.GeneratedMapCells.Select(CloneCell).ToList(),
            BuildingOverlayCells = draft.BuildingOverlayCells.Select(CloneCell).ToList(),
            SceneryOverlayCells = draft.SceneryOverlayCells.Select(CloneCell).ToList(),
            SceneryOverlays = draft.SceneryOverlays.Select(CloneSceneryOverlay).ToList(),
            OriginalTerrainCells = draft.OriginalTerrainCells.ToArray(),
            TerrainCells = draft.TerrainCells.ToArray(),
            GenerationMode = draft.GenerationMode,
            TerrainVisualProfile = draft.TerrainVisualProfile.Clone(),
            AutoGenerateMapFromTerrain = draft.AutoGenerateMapFromTerrain,
            BeautifyGeneratedMap = draft.BeautifyGeneratedMap,
            BeautifyStrength = draft.BeautifyStrength,
            FeatherRadius = draft.FeatherRadius,
            BeautifyFilterProfile = draft.BeautifyFilterProfile,
            CustomBeautifyFilter = draft.CustomBeautifyFilter?.Clone(),
            CreatedAtText = draft.CreatedAtText,
            UpdatedAtText = draft.UpdatedAtText
        };

    private static TerrainMaterialPlanItem CloneTerrainMaterialPlanItem(TerrainMaterialPlanItem item)
        => new()
        {
            MapId = item.MapId,
            TerrainId = item.TerrainId,
            VisualFamilyKey = item.VisualFamilyKey,
            MaterialRelativePath = item.MaterialRelativePath,
            MaterialCategory = item.MaterialCategory,
            DisplayName = item.DisplayName,
            SelectionMode = item.SelectionMode,
            MaterialRootFingerprint = item.MaterialRootFingerprint
        };

    private static MapCellOverride CloneCell(MapCellOverride cell)
        => new()
        {
            Index = cell.Index,
            MaterialRelativePath = cell.MaterialRelativePath,
            MaterialCategory = cell.MaterialCategory,
            DisplayName = cell.DisplayName,
            Source = cell.Source
        };

    private static MapSceneryOverlay CloneSceneryOverlay(MapSceneryOverlay overlay)
        => new()
        {
            OverlayId = overlay.OverlayId,
            MaterialRelativePath = overlay.MaterialRelativePath,
            MaterialCategory = overlay.MaterialCategory,
            DisplayName = overlay.DisplayName,
            X = overlay.X,
            Y = overlay.Y,
            Width = overlay.Width,
            Height = overlay.Height,
            RotationDegrees = overlay.RotationDegrees,
            ZOrder = overlay.ZOrder
        };
}
