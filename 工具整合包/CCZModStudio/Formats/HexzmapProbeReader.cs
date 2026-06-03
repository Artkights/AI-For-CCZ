using System.Drawing;
using System.Globalization;
using System.Text;
using CCZModStudio.Models;

namespace CCZModStudio.Formats;

public sealed class HexzmapProbeReader
{
    public const int TerrainHeaderSize = 2;
    private const int DirectoryOffset = 0x110;
    private const int DirectoryStride = 12;

    public HexzmapProbeResult Read(CczProject project, IReadOnlyDictionary<byte, string>? terrainNames = null)
    {
        var path = project.ResolveGameFile("Hexzmap.e5");
        if (!File.Exists(path))
        {
            throw new FileNotFoundException("未找到 Hexzmap.e5，无法读取地形索引。", path);
        }

        var bytes = File.ReadAllBytes(path);
        var magic = bytes.Length >= 4 ? Encoding.ASCII.GetString(bytes, 0, 4) : string.Empty;
        var magicValid = magic is "Ls12" or "Ls11" or "Ls10";
        var payloadOffset = magicValid && bytes.Length >= 16 ? 16 : 0;
        var payload = bytes[payloadOffset..];

        var mapInfos = ReadMapInfos(project);
        var directoryEntries = ReadDirectoryEntries(bytes, BuildMapCellLookup(mapInfos));
        var entriesByMapId = directoryEntries
            .Where(x => !string.IsNullOrWhiteSpace(x.MapId))
            .GroupBy(x => x.MapId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.OrderBy(x => x.Index).First(), StringComparer.OrdinalIgnoreCase);
        var blocks = new List<HexzmapBlockInfo>();

        foreach (var map in mapInfos.OrderBy(x => x.MapNumber).ThenBy(x => x.FileName, StringComparer.CurrentCultureIgnoreCase))
        {
            if (!entriesByMapId.TryGetValue(map.MapId, out var entry))
            {
                continue;
            }

            var dataOffset = entry.FileOffset + TerrainHeaderSize;
            if (dataOffset < 0 || dataOffset + map.CellCount > bytes.Length)
            {
                continue;
            }

            var cells = bytes.AsSpan(dataOffset, map.CellCount).ToArray();
            var groups = cells
                .GroupBy(x => x)
                .OrderByDescending(g => g.Count())
                .ThenBy(g => g.Key)
                .ToList();
            var dominant = groups.FirstOrDefault();
            var dominantId = dominant?.Key ?? 0;
            var dominantName = GetTerrainName(terrainNames, dominantId);
            var knownTerrainCount = groups.Count(g => terrainNames?.ContainsKey(g.Key) == true);
            var unknownTerrainIds = string.Join(" / ", groups
                .Where(g => terrainNames?.ContainsKey(g.Key) != true)
                .Take(12)
                .Select(g => "0x" + g.Key.ToString("X2", CultureInfo.InvariantCulture)));

            blocks.Add(new HexzmapBlockInfo
            {
                Index = blocks.Count,
                MapId = map.MapId,
                OffsetHex = "0x" + dataOffset.ToString("X", CultureInfo.InvariantCulture),
                DataOffset = dataOffset,
                SegmentOffset = entry.FileOffset,
                SegmentLength = entry.SegmentLength,
                MapPixelWidth = map.PixelWidth,
                MapPixelHeight = map.PixelHeight,
                CanEdit = entry.SegmentLength == map.CellCount + TerrainHeaderSize,
                Width = map.GridWidth,
                Height = map.GridHeight,
                BytesRead = map.CellCount,
                UniqueTerrainCount = groups.Count,
                DominantTerrainId = dominantId,
                DominantTerrainName = dominantName,
                DominantTerrainCount = dominant?.Count() ?? 0,
                TopTerrainIds = string.Join(" / ", groups.Take(8).Select(g => $"{g.Key:X2}:{g.Count()}")),
                TopTerrainNames = BuildTopTerrainNames(groups, terrainNames),
                KnownTerrainCount = knownTerrainCount,
                UnknownTerrainIds = unknownTerrainIds,
                MapImageName = map.FileName,
                MapImageExists = true,
                Annotation = BuildAnnotation(map, entry, groups.Count, dominantId, dominantName, dominant?.Count() ?? 0, knownTerrainCount)
            });
        }

        return new HexzmapProbeResult
        {
            Path = path,
            Magic = magic,
            MagicValid = magicValid,
            PayloadOffset = payloadOffset,
            PayloadLength = payload.Length,
            CandidateWidth = 0,
            CandidateHeight = 0,
            TrailingBytes = Math.Max(0, bytes.Length - CalculateLastSegmentEnd(directoryEntries)),
            Payload = payload,
            DirectoryTableOffset = DirectoryOffset,
            DirectoryEntries = directoryEntries,
            Blocks = blocks
        };
    }

    public byte[] GetBlockCells(HexzmapProbeResult result, HexzmapBlockInfo block)
    {
        var expected = block.Width * block.Height;
        var payloadRelativeOffset = block.DataOffset - result.PayloadOffset;
        if (expected <= 0 || payloadRelativeOffset < 0 || payloadRelativeOffset + expected > result.Payload.Length)
        {
            return Array.Empty<byte>();
        }

        return result.Payload.AsSpan(payloadRelativeOffset, expected).ToArray();
    }

    public static IReadOnlyDictionary<byte, string> BuildTerrainNameLookup(IEnumerable<MaterialAsset> materials)
    {
        var groups = materials
            .Where(x => !string.IsNullOrWhiteSpace(x.HexTag))
            .Select(x => new { Asset = x, Parsed = TryParseTerrainId(x.HexTag, out var id) ? id : (byte?)null })
            .Where(x => x.Parsed.HasValue)
            .GroupBy(x => x.Parsed!.Value)
            .OrderBy(g => g.Key)
            .ToDictionary(
                g => g.Key,
                g => string.Join(" / ", g
                    .OrderBy(x => x.Asset.Category == "地形" ? 0 : 1)
                    .ThenBy(x => x.Asset.Category, StringComparer.CurrentCultureIgnoreCase)
                    .ThenBy(x => x.Asset.FileName, StringComparer.CurrentCultureIgnoreCase)
                    .Select(x => BuildMaterialTerrainName(x.Asset))
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Distinct(StringComparer.Ordinal)
                    .Take(6)),
                EqualityComparer<byte>.Default);
        return groups;
    }

    private static IReadOnlyList<HexzmapMapInfo> ReadMapInfos(CczProject project)
    {
        var mapDir = project.ResolveGameFile("Map");
        if (!Directory.Exists(mapDir)) return Array.Empty<HexzmapMapInfo>();

        var maps = new List<HexzmapMapInfo>();
        foreach (var path in Directory.GetFiles(mapDir).Where(IsJpegFile).OrderBy(x => x, StringComparer.CurrentCultureIgnoreCase))
        {
            try
            {
                var stem = Path.GetFileNameWithoutExtension(path);
                if (string.IsNullOrWhiteSpace(stem) ||
                    stem.Length < 2 ||
                    !stem.StartsWith("M", StringComparison.OrdinalIgnoreCase) ||
                    !int.TryParse(stem[1..], NumberStyles.Integer, CultureInfo.InvariantCulture, out var mapNumber))
                {
                    continue;
                }

                using var image = Image.FromFile(path);
                if (image.Width <= 0 ||
                    image.Height <= 0 ||
                    image.Width % ResourceIndexItem.MapTilePixelSize != 0 ||
                    image.Height % ResourceIndexItem.MapTilePixelSize != 0)
                {
                    continue;
                }

                var gridWidth = image.Width / ResourceIndexItem.MapTilePixelSize;
                var gridHeight = image.Height / ResourceIndexItem.MapTilePixelSize;
                maps.Add(new HexzmapMapInfo(
                    "M" + mapNumber.ToString("000", CultureInfo.InvariantCulture),
                    mapNumber,
                    Path.GetFileName(path),
                    image.Width,
                    image.Height,
                    gridWidth,
                    gridHeight));
            }
            catch
            {
                // Malformed map images are reported by the resource indexer and preview UI.
            }
        }

        return maps;
    }

    private static IReadOnlyDictionary<int, List<string>> BuildMapCellLookup(IEnumerable<HexzmapMapInfo> maps)
    {
        var lookup = new Dictionary<int, List<string>>();
        foreach (var map in maps)
        {
            var key = map.CellCount + TerrainHeaderSize;
            if (!lookup.TryGetValue(key, out var list))
            {
                list = new List<string>();
                lookup[key] = list;
            }

            list.Add(map.MapId);
        }

        return lookup;
    }

    private static IReadOnlyList<HexzmapDirectoryEntry> ReadDirectoryEntries(byte[] fileBytes, IReadOnlyDictionary<int, List<string>> mapCellLookup)
    {
        var entries = new List<HexzmapDirectoryEntry>();
        for (var offset = DirectoryOffset; offset + DirectoryStride <= fileBytes.Length; offset += DirectoryStride)
        {
            var segmentLength = ReadUInt32BigEndian(fileBytes, offset);
            var lengthCopy = ReadUInt32BigEndian(fileBytes, offset + 4);
            var fileOffset = ReadUInt32BigEndian(fileBytes, offset + 8);
            if (segmentLength == 0 && lengthCopy == 0 && fileOffset == 0)
            {
                break;
            }

            var isValidSegment = segmentLength > TerrainHeaderSize &&
                                 segmentLength == lengthCopy &&
                                 fileOffset >= 0 &&
                                 fileOffset < fileBytes.Length &&
                                 fileOffset + segmentLength <= fileBytes.Length;
            if (!isValidSegment)
            {
                continue;
            }

            var candidateMapIds = ResolveCandidateMaps(mapCellLookup, segmentLength);
            var mapId = PickMapIdForEntry(candidateMapIds, entries.Count);
            entries.Add(new HexzmapDirectoryEntry
            {
                Index = entries.Count,
                EntryOffset = offset,
                SegmentLength = segmentLength,
                FileOffset = fileOffset,
                NextSegmentLength = lengthCopy,
                CandidateMapIdA = candidateMapIds,
                CandidateMapIdB = candidateMapIds,
                CandidateMapIdC = mapId,
                MapId = mapId,
                IsValidSegment = true,
                Annotation = BuildDirectoryAnnotation(segmentLength, lengthCopy, fileOffset, candidateMapIds, mapId)
            });
        }

        return entries;
    }

    private static string PickMapIdForEntry(string mapIds, int entryIndex)
    {
        var exact = "M" + entryIndex.ToString("000", CultureInfo.InvariantCulture);
        if (string.IsNullOrWhiteSpace(mapIds))
        {
            return exact;
        }

        var candidates = mapIds.Split(" / ", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (candidates.FirstOrDefault(x => x.Equals(exact, StringComparison.OrdinalIgnoreCase)) is { } matched)
        {
            return matched;
        }

        return candidates.FirstOrDefault() ?? exact;
    }

    private static int CalculateLastSegmentEnd(IEnumerable<HexzmapDirectoryEntry> entries)
    {
        var max = 0;
        foreach (var entry in entries)
        {
            if (!entry.IsValidSegment) continue;
            max = Math.Max(max, entry.FileOffset + entry.SegmentLength);
        }

        return max;
    }

    private static bool IsJpegFile(string path)
    {
        var ext = Path.GetExtension(path);
        return ext.Equals(".jpg", StringComparison.OrdinalIgnoreCase) ||
               ext.Equals(".jpeg", StringComparison.OrdinalIgnoreCase);
    }

    private static int ReadUInt32BigEndian(byte[] payload, int offset)
    {
        return
            (payload[offset] << 24) |
            (payload[offset + 1] << 16) |
            (payload[offset + 2] << 8) |
            payload[offset + 3];
    }

    private static string ResolveCandidateMaps(IReadOnlyDictionary<int, List<string>> mapCellLookup, int value)
    {
        if (!mapCellLookup.TryGetValue(value, out var maps) || maps.Count == 0)
        {
            return string.Empty;
        }

        return string.Join(" / ", maps.Take(8));
    }

    private static string BuildDirectoryAnnotation(int segmentLength, int lengthCopy, int fileOffset, string mapIds, string mapId)
    {
        var mapText = string.IsNullOrWhiteSpace(mapIds) ? "未匹配到同格数地图图片" : $"匹配格数：{mapIds}";
        return $"目录项：段长度={segmentLength}，长度副本={lengthCopy}，段起始偏移=0x{fileOffset:X}；地形格数据从段起始偏移+2 开始。{mapText}；当前绑定 {mapId}。";
    }

    private static string BuildAnnotation(
        HexzmapMapInfo map,
        HexzmapDirectoryEntry entry,
        int uniqueCount,
        int dominantId,
        string dominantName,
        int dominantCount,
        int knownTerrainCount)
    {
        var cellCount = Math.Max(1, map.CellCount);
        var dominantPercent = dominantCount * 100.0 / cellCount;
        var dominantText = string.IsNullOrWhiteSpace(dominantName)
            ? $"0x{dominantId:X2}"
            : $"0x{dominantId:X2}（{dominantName}）";
        var knownText = knownTerrainCount > 0
            ? $"其中 {knownTerrainCount} 种可与素材库 hex 标记建立中文名称候选。"
            : "暂未能与素材库 hex 标记建立名称对应。";
        var density = uniqueCount switch
        {
            <= 4 => "地形种类很少，可能是空白、测试或特殊地图。",
            <= 12 => "地形种类中等，像是正常战场地形表。",
            _ => "地形种类较多，可能包含装饰、特殊地形或仍需人工核对。"
        };
        var sizeText = $"{map.PixelWidth}x{map.PixelHeight} / 48 = {map.GridWidth}x{map.GridHeight}";
        return
            $"Hexzmap 地形块 {map.MapId}：按地图图片 {map.FileName} 分辨率计算格数，{sizeText}，共 {map.CellCount} 格。段起始 0x{entry.FileOffset:X}，数据偏移 0x{entry.FileOffset + TerrainHeaderSize:X}，段长度 {entry.SegmentLength}。不同地形 {uniqueCount} 种，主地形 {dominantText} 占 {dominantPercent:0.0}%。{knownText}{density}";
    }

    private static string BuildTopTerrainNames(IEnumerable<IGrouping<byte, byte>> groups, IReadOnlyDictionary<byte, string>? terrainNames)
    {
        return string.Join(" / ", groups.Take(8).Select(g =>
        {
            var name = GetTerrainName(terrainNames, g.Key);
            return string.IsNullOrWhiteSpace(name)
                ? $"0x{g.Key:X2}:{g.Count()}"
                : $"0x{g.Key:X2}({name}):{g.Count()}";
        }));
    }

    private static string GetTerrainName(IReadOnlyDictionary<byte, string>? terrainNames, byte id)
        => terrainNames != null && terrainNames.TryGetValue(id, out var name) ? name : string.Empty;

    private static string BuildMaterialTerrainName(MaterialAsset asset)
    {
        var description = string.IsNullOrWhiteSpace(asset.Description)
            ? Path.GetFileNameWithoutExtension(asset.FileName)
            : asset.Description.Trim();
        return string.IsNullOrWhiteSpace(asset.Category) ? description : $"{asset.Category}:{description}";
    }

    private static bool TryParseTerrainId(string text, out byte id)
    {
        id = 0;
        text = text.Trim();
        if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            return byte.TryParse(text[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out id);
        }

        if (byte.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out id))
        {
            return true;
        }

        return byte.TryParse(text, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out id);
    }

    private sealed record HexzmapMapInfo(
        string MapId,
        int MapNumber,
        string FileName,
        int PixelWidth,
        int PixelHeight,
        int GridWidth,
        int GridHeight)
    {
        public int CellCount => GridWidth * GridHeight;
    }
}
