using System.Drawing;
using System.Globalization;
using System.Text.RegularExpressions;
using CCZModStudio.Formats;
using CCZModStudio.Models;

namespace CCZModStudio.Core;

public sealed class MapPairImportExportService
{
    private static readonly Regex MapJpegRegex = new(@"^(M\d{3})\.(?:jpg|jpeg)$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex TerrainBmpRegex = new(@"^(M\d{3})\.bmp$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private readonly MapCanvasPublishService _mapCanvasPublishService = new();
    private readonly MapImageReplaceService _mapImageReplaceService = new();
    private readonly HexzmapProbeReader _hexzmapProbeReader = new();
    private readonly HexzmapTerrainBmpCodec _terrainBmpCodec = new();
    private readonly HexzmapEditorService _hexzmapEditorService = new();
    private readonly MapResourceIndexer _mapResourceIndexer = new();

    public MapPairExportResult ExportCurrentMapPair(
        CczProject project,
        MapWorkbenchDraft draft,
        MapResourceItem mapItem,
        HexzmapProbeResult hexzmapProbe,
        HexzmapBlockInfo block,
        IReadOnlyList<MaterialAsset> materials,
        string outputFolder,
        Bitmap? confirmedRender = null)
    {
        if (project == null) throw new ArgumentNullException(nameof(project));
        if (draft == null) throw new ArgumentNullException(nameof(draft));
        if (mapItem == null) throw new ArgumentNullException(nameof(mapItem));
        if (hexzmapProbe == null) throw new ArgumentNullException(nameof(hexzmapProbe));
        if (block == null) throw new ArgumentNullException(nameof(block));
        if (string.IsNullOrWhiteSpace(outputFolder)) throw new ArgumentException("Output folder is required.", nameof(outputFolder));

        var mapId = NormalizeMapId(GetMapIdForMapResource(mapItem));
        if (string.IsNullOrWhiteSpace(mapId))
        {
            throw new InvalidOperationException("Current map slot does not have an Mxxx id.");
        }

        if (!block.MapId.Equals(mapId, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Hexzmap block {block.MapId} does not match map {mapId}.");
        }

        ValidateDraftMapBlockSize(draft, mapItem, block);
        if (draft.TerrainCells.Length != draft.CellCount)
        {
            throw new InvalidOperationException($"Draft terrain cell count must be {draft.CellCount} bytes.");
        }

        var prefix = _hexzmapProbeReader.GetBlockSegmentPrefix(hexzmapProbe, block);
        if (prefix.Length != HexzmapTerrainBmpCodec.PrefixSize)
        {
            throw new InvalidOperationException("Unable to read the Hexzmap terrain segment prefix for " + mapId);
        }

        Directory.CreateDirectory(outputFolder);
        var jpegPath = Path.Combine(outputFolder, mapId + ".JPG");
        var bmpPath = Path.Combine(outputFolder, mapId + ".BMP");
        if (confirmedRender != null)
        {
            _mapCanvasPublishService.ExportJpeg(confirmedRender, jpegPath);
        }
        else
        {
            _mapCanvasPublishService.ExportJpeg(draft, materials, jpegPath);
        }
        _terrainBmpCodec.Encode(block, prefix, draft.TerrainCells, bmpPath);

        using var verifyImage = Image.FromFile(jpegPath);
        var expectedWidth = checked(draft.GridWidth * MapResourceItem.MapTilePixelSize);
        var expectedHeight = checked(draft.GridHeight * MapResourceItem.MapTilePixelSize);
        if (verifyImage.Width != expectedWidth || verifyImage.Height != expectedHeight)
        {
            throw new InvalidOperationException($"Exported JPG size is {verifyImage.Width}x{verifyImage.Height}; expected {expectedWidth}x{expectedHeight}.");
        }

        var decodedBmp = _terrainBmpCodec.Decode(bmpPath);
        if (decodedBmp.Width != draft.GridWidth || decodedBmp.Height != draft.GridHeight ||
            !decodedBmp.PrefixBytes.SequenceEqual(prefix) ||
            !decodedBmp.TerrainCells.SequenceEqual(draft.TerrainCells))
        {
            throw new InvalidOperationException("Exported terrain BMP verification failed.");
        }

        return new MapPairExportResult
        {
            MapId = mapId,
            JpegPath = jpegPath,
            TerrainBmpPath = bmpPath,
            GridWidth = draft.GridWidth,
            GridHeight = draft.GridHeight,
            PixelWidth = verifyImage.Width,
            PixelHeight = verifyImage.Height,
            JpegSizeBytes = new FileInfo(jpegPath).Length,
            TerrainBmpSizeBytes = new FileInfo(bmpPath).Length,
            JpegSha256 = WriteOperationReportService.ComputeSha256(jpegPath),
            TerrainBmpSha256 = WriteOperationReportService.ComputeSha256(bmpPath),
            TerrainPrefixBytes = prefix,
            TerrainCellCount = draft.TerrainCells.Length
        };
    }

    public IReadOnlyList<MapPairImportCandidate> ListImportCandidates(string folder)
    {
        folder = Path.GetFullPath(folder);
        if (!Directory.Exists(folder))
        {
            throw new DirectoryNotFoundException("Map pair import folder does not exist: " + folder);
        }

        var jpgs = Directory.EnumerateFiles(folder, "*.*", SearchOption.TopDirectoryOnly)
            .Select(path => new { Path = path, Match = MapJpegRegex.Match(Path.GetFileName(path)) })
            .Where(x => x.Match.Success)
            .GroupBy(x => NormalizeMapId(x.Match.Groups[1].Value), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.OrderBy(x => x.Path, StringComparer.OrdinalIgnoreCase).First().Path, StringComparer.OrdinalIgnoreCase);

        var bmps = Directory.EnumerateFiles(folder, "*.bmp", SearchOption.TopDirectoryOnly)
            .Select(path => new { Path = path, Match = TerrainBmpRegex.Match(Path.GetFileName(path)) })
            .Where(x => x.Match.Success)
            .GroupBy(x => NormalizeMapId(x.Match.Groups[1].Value), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.OrderBy(x => x.Path, StringComparer.OrdinalIgnoreCase).First().Path, StringComparer.OrdinalIgnoreCase);

        if (jpgs.Count == 1 && bmps.Count == 1 && !jpgs.Keys.First().Equals(bmps.Keys.First(), StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Map pair file names must use the same Mxxx id; found {Path.GetFileName(jpgs.Values.First())} and {Path.GetFileName(bmps.Values.First())}.");
        }

        return jpgs.Keys
            .Intersect(bmps.Keys, StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .Select(mapId => new MapPairImportCandidate
            {
                MapId = mapId,
                JpegPath = jpgs[mapId],
                TerrainBmpPath = bmps[mapId]
            })
            .ToList();
    }

    public MapPairImportPreview PreviewImportFolder(CczProject project, string folder, string? preferredMapId = null)
    {
        var candidates = ListImportCandidates(folder);
        if (candidates.Count == 0)
        {
            throw new InvalidOperationException("No matching Mxxx.JPG/JPEG + Mxxx.BMP pair was found in: " + folder);
        }

        MapPairImportCandidate candidate;
        if (!string.IsNullOrWhiteSpace(preferredMapId) &&
            candidates.FirstOrDefault(x => x.MapId.Equals(NormalizeMapId(preferredMapId), StringComparison.OrdinalIgnoreCase)) is { } preferred)
        {
            candidate = preferred;
        }
        else if (candidates.Count == 1)
        {
            candidate = candidates[0];
        }
        else
        {
            throw new InvalidOperationException("Multiple map pairs were found. Select one pair before previewing import.");
        }

        return PreviewImportCandidate(project, Path.GetFullPath(folder), candidate);
    }

    public MapPairImportPreview PreviewImportCandidate(CczProject project, string folder, MapPairImportCandidate candidate)
    {
        if (project == null) throw new ArgumentNullException(nameof(project));
        if (candidate == null) throw new ArgumentNullException(nameof(candidate));

        var jpegMatch = MapJpegRegex.Match(Path.GetFileName(candidate.JpegPath));
        var bmpMatch = TerrainBmpRegex.Match(Path.GetFileName(candidate.TerrainBmpPath));
        if (!jpegMatch.Success || !bmpMatch.Success)
        {
            throw new InvalidOperationException("Import files must be named Mxxx.JPG/JPEG and Mxxx.BMP.");
        }

        var jpegMapId = NormalizeMapId(jpegMatch.Groups[1].Value);
        var bmpMapId = NormalizeMapId(bmpMatch.Groups[1].Value);
        if (!jpegMapId.Equals(bmpMapId, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Import JPG/BMP ids do not match: {Path.GetFileName(candidate.JpegPath)} + {Path.GetFileName(candidate.TerrainBmpPath)}.");
        }

        var mapId = NormalizeMapId(string.IsNullOrWhiteSpace(candidate.MapId) ? jpegMapId : candidate.MapId);
        if (!mapId.Equals(jpegMapId, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Import candidate id {candidate.MapId} does not match file id {jpegMapId}.");
        }

        var targetMap = FindProjectMap(project, mapId)
                        ?? throw new InvalidOperationException("Current project does not contain Map/" + mapId + ".JPG.");
        var probe = _hexzmapProbeReader.Read(project);
        var block = probe.Blocks.FirstOrDefault(x => x.MapId.Equals(mapId, StringComparison.OrdinalIgnoreCase))
                    ?? throw new InvalidOperationException("Current project does not contain a matching Hexzmap block: " + mapId);
        if (!block.CanEdit)
        {
            throw new InvalidOperationException("Target Hexzmap block is not editable: " + mapId);
        }

        var terrainBmp = _terrainBmpCodec.Decode(candidate.TerrainBmpPath);
        int jpegWidth;
        int jpegHeight;
        using (var image = Image.FromFile(candidate.JpegPath))
        {
            jpegWidth = image.Width;
            jpegHeight = image.Height;
        }

        if (jpegWidth <= 0 || jpegHeight <= 0 ||
            jpegWidth % MapResourceItem.MapTilePixelSize != 0 ||
            jpegHeight % MapResourceItem.MapTilePixelSize != 0)
        {
            throw new InvalidOperationException("Import JPG size must be divisible by 48.");
        }

        var gridWidth = jpegWidth / MapResourceItem.MapTilePixelSize;
        var gridHeight = jpegHeight / MapResourceItem.MapTilePixelSize;
        if (terrainBmp.Width != gridWidth || terrainBmp.Height != gridHeight)
        {
            throw new InvalidOperationException($"Import BMP grid {terrainBmp.Width}x{terrainBmp.Height} does not match JPG-derived grid {gridWidth}x{gridHeight}.");
        }

        if (targetMap.Width != jpegWidth || targetMap.Height != jpegHeight ||
            targetMap.GridWidth != gridWidth || targetMap.GridHeight != gridHeight)
        {
            throw new InvalidOperationException($"Target map {mapId} size {targetMap.Width}x{targetMap.Height} does not match import JPG {jpegWidth}x{jpegHeight}.");
        }

        if (block.Width != gridWidth || block.Height != gridHeight || block.BytesRead != terrainBmp.TerrainCells.Length)
        {
            throw new InvalidOperationException($"Target Hexzmap block {block.Width}x{block.Height} does not match import grid {gridWidth}x{gridHeight}.");
        }

        var targetCells = _hexzmapProbeReader.GetBlockCells(probe, block);
        if (targetCells.Length != terrainBmp.TerrainCells.Length)
        {
            throw new InvalidOperationException("Unable to read target Hexzmap cells for " + mapId);
        }

        var targetPrefix = _hexzmapProbeReader.GetBlockSegmentPrefix(probe, block);
        var sourceJpegSha = WriteOperationReportService.ComputeSha256(candidate.JpegPath);
        var targetJpegSha = WriteOperationReportService.ComputeSha256(targetMap.Path);
        var sourceTerrainSha = WriteOperationReportService.ComputeSha256(terrainBmp.TerrainCells);
        var targetTerrainSha = WriteOperationReportService.ComputeSha256(targetCells);
        var changedTerrain = CountChangedBytes(targetCells, terrainBmp.TerrainCells);

        return new MapPairImportPreview
        {
            FolderPath = Path.GetFullPath(folder),
            MapId = mapId,
            JpegPath = Path.GetFullPath(candidate.JpegPath),
            TerrainBmpPath = Path.GetFullPath(candidate.TerrainBmpPath),
            TargetMap = targetMap,
            TargetBlock = block,
            TerrainBmp = terrainBmp,
            JpegWidth = jpegWidth,
            JpegHeight = jpegHeight,
            GridWidth = gridWidth,
            GridHeight = gridHeight,
            SourceJpegSha256 = sourceJpegSha,
            TargetJpegSha256 = targetJpegSha,
            SourceTerrainSha256 = sourceTerrainSha,
            TargetTerrainSha256 = targetTerrainSha,
            TargetPrefixBytes = targetPrefix,
            ChangedTerrainCells = changedTerrain,
            JpegIdentical = sourceJpegSha.Equals(targetJpegSha, StringComparison.OrdinalIgnoreCase),
            TerrainIdentical = sourceTerrainSha.Equals(targetTerrainSha, StringComparison.OrdinalIgnoreCase),
            Warnings = terrainBmp.Warnings
        };
    }

    public MapPairImportResult ImportMapPair(CczProject project, MapPairImportPreview preview, IReadOnlyDictionary<byte, string>? terrainNames = null)
    {
        if (project == null) throw new ArgumentNullException(nameof(project));
        if (preview == null) throw new ArgumentNullException(nameof(preview));

        MapImageSaveResult? mapResult = null;
        HexzmapSaveResult? terrainResult = null;

        if (!preview.JpegIdentical)
        {
            mapResult = _mapImageReplaceService.ReplaceMapImage(project, preview.TargetMap.Path, preview.JpegPath);
        }

        if (!preview.TerrainIdentical)
        {
            var probe = _hexzmapProbeReader.Read(project, terrainNames);
            var block = probe.Blocks.FirstOrDefault(x => x.MapId.Equals(preview.MapId, StringComparison.OrdinalIgnoreCase))
                        ?? throw new InvalidOperationException("Target Hexzmap block disappeared before import: " + preview.MapId);
            terrainResult = _hexzmapEditorService.SaveBlock(project, probe, block, preview.TerrainBmp.TerrainCells, terrainNames);
        }

        return new MapPairImportResult
        {
            MapId = preview.MapId,
            MapImageResult = mapResult,
            TerrainResult = terrainResult,
            SkippedJpeg = preview.JpegIdentical,
            SkippedTerrain = preview.TerrainIdentical
        };
    }

    private MapResourceItem? FindProjectMap(CczProject project, string mapId)
        => _mapResourceIndexer.Index(project)
            .Where(item => item.Category == "地图图片" || item.Category == "鍦板浘鍥剧墖")
            .FirstOrDefault(item => GetMapIdForMapResource(item).Equals(mapId, StringComparison.OrdinalIgnoreCase));

    private static void ValidateDraftMapBlockSize(MapWorkbenchDraft draft, MapResourceItem mapItem, HexzmapBlockInfo block)
    {
        if (draft.GridWidth <= 0 || draft.GridHeight <= 0)
        {
            throw new InvalidOperationException("Map draft grid size is invalid.");
        }

        if (mapItem.GridWidth != draft.GridWidth || mapItem.GridHeight != draft.GridHeight)
        {
            throw new InvalidOperationException($"Draft grid {draft.GridWidth}x{draft.GridHeight} does not match map slot {mapItem.GridWidth}x{mapItem.GridHeight}.");
        }

        if (block.Width != draft.GridWidth || block.Height != draft.GridHeight || !block.CanEdit)
        {
            throw new InvalidOperationException($"Hexzmap block {block.MapId} is not editable or does not match draft grid.");
        }
    }

    private static int CountChangedBytes(byte[] left, byte[] right)
    {
        var length = Math.Min(left.Length, right.Length);
        var count = Math.Abs(left.Length - right.Length);
        for (var i = 0; i < length; i++)
        {
            if (left[i] != right[i]) count++;
        }

        return count;
    }

    private static string GetMapIdForMapResource(MapResourceItem item)
    {
        if (!string.IsNullOrWhiteSpace(item.MapId)) return NormalizeMapId(item.MapId);
        var name = Path.GetFileNameWithoutExtension(item.Name);
        if (name.Length > 1 &&
            (name[0] == 'M' || name[0] == 'm') &&
            int.TryParse(name[1..], NumberStyles.Integer, CultureInfo.InvariantCulture, out var nameNumber))
        {
            return "M" + nameNumber.ToString("000", CultureInfo.InvariantCulture);
        }

        if (int.TryParse(item.Id, NumberStyles.Integer, CultureInfo.InvariantCulture, out var idNumber))
        {
            return "M" + idNumber.ToString("000", CultureInfo.InvariantCulture);
        }

        return string.Empty;
    }

    private static string NormalizeMapId(string? mapId)
    {
        if (string.IsNullOrWhiteSpace(mapId)) return string.Empty;
        mapId = Path.GetFileNameWithoutExtension(mapId.Trim());
        if (mapId.Length > 1 &&
            (mapId[0] == 'M' || mapId[0] == 'm') &&
            int.TryParse(mapId[1..], NumberStyles.Integer, CultureInfo.InvariantCulture, out var number))
        {
            return "M" + number.ToString("000", CultureInfo.InvariantCulture);
        }

        return mapId.ToUpperInvariant();
    }
}
