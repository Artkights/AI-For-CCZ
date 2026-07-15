using System.Globalization;
using CCZModStudio.Formats;
using CCZModStudio.Models;

namespace CCZModStudio.Core;

public sealed class MapSlotCatalogService
{
    private readonly MapResourceIndexer _mapResourceIndexer = new();
    private readonly HexzmapProbeReader _hexzmapProbeReader = new();
    private readonly HexzmapLayoutWriter _layoutWriter = new();

    public IReadOnlyList<MapSlotCatalogEntry> List(CczProject project)
    {
        var maps = _mapResourceIndexer.Index(project)
            .Where(item => TryGetMapNumber(item, out _))
            .GroupBy(item => GetMapNumber(item))
            .ToDictionary(group => group.Key, group => group.OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase).First());
        var probe = _hexzmapProbeReader.Read(project);
        var hexBytes = File.ReadAllBytes(probe.Path);
        var entryByNumber = probe.DirectoryEntries.ToDictionary(entry => entry.Index);
        var numbers = maps.Keys.Concat(entryByNumber.Keys).Distinct().OrderBy(number => number);
        var result = new List<MapSlotCatalogEntry>();

        foreach (var number in numbers)
        {
            maps.TryGetValue(number, out var map);
            entryByNumber.TryGetValue(number, out var terrainEntry);
            result.Add(BuildEntry(number, map, terrainEntry, hexBytes));
        }

        return result;
    }

    public MapSlotCatalogEntry GetRequiredOverwriteTarget(CczProject project, string mapId)
    {
        var normalized = NormalizeMapId(mapId);
        var slot = List(project).FirstOrDefault(item => item.MapId.Equals(normalized, StringComparison.OrdinalIgnoreCase));
        if (slot == null) throw new InvalidOperationException("没有找到地图槽：" + normalized);
        if (!slot.CanOverwrite)
        {
            throw new InvalidOperationException($"地图槽 {normalized} 不能覆盖发布：{slot.Detail}");
        }

        return slot;
    }

    public int GetNextAppendMapNumber(CczProject project)
    {
        var probe = _hexzmapProbeReader.Read(project);
        return probe.DirectoryEntries.Count;
    }

    public static string NormalizeMapId(string? mapId)
    {
        var text = Path.GetFileNameWithoutExtension(mapId?.Trim() ?? string.Empty);
        if (text.Length > 1 && (text[0] == 'M' || text[0] == 'm') &&
            int.TryParse(text[1..], NumberStyles.Integer, CultureInfo.InvariantCulture, out var number))
        {
            return $"M{number:000}";
        }

        throw new InvalidOperationException("地图编号必须使用 Mxxx 格式：" + mapId);
    }

    private MapSlotCatalogEntry BuildEntry(
        int number,
        MapResourceItem? map,
        HexzmapDirectoryEntry? terrainEntry,
        byte[] hexBytes)
    {
        var mapId = $"M{number:000}";
        if (terrainEntry == null)
        {
            return new MapSlotCatalogEntry
            {
                MapNumber = number,
                MapId = mapId,
                State = map == null ? MapSlotState.InvalidImage : MapSlotState.MissingTerrainBlock,
                MapResource = map,
                GridWidth = map?.GridWidth ?? 0,
                GridHeight = map?.GridHeight ?? 0,
                Detail = map == null ? "底图和地形块均不存在。" : "存在底图，但 Hexzmap 没有同编号目录项。"
            };
        }

        var (terrainWidth, terrainHeight) = _layoutWriter.ReadSegmentDimensions(hexBytes, terrainEntry);
        var terrainSupported = terrainEntry.IsValidSegment &&
                               terrainEntry.SegmentLength == terrainEntry.DecodedLength &&
                               terrainWidth > 0 && terrainHeight > 0 &&
                               terrainEntry.SegmentLength == checked(terrainWidth * terrainHeight + HexzmapLayoutWriter.SegmentPrefixSize);
        if (!terrainSupported)
        {
            return new MapSlotCatalogEntry
            {
                MapNumber = number,
                MapId = mapId,
                State = MapSlotState.UnsupportedTerrainSegment,
                MapResource = map,
                TerrainEntry = terrainEntry,
                GridWidth = terrainWidth,
                GridHeight = terrainHeight,
                Detail = "Hexzmap 地形段不是已确认的未压缩尺寸前缀布局。"
            };
        }

        if (map == null)
        {
            return new MapSlotCatalogEntry
            {
                MapNumber = number,
                MapId = mapId,
                State = MapSlotState.MissingMapImage,
                TerrainEntry = terrainEntry,
                GridWidth = terrainWidth,
                GridHeight = terrainHeight,
                Detail = "存在 Hexzmap 地形块，但 Map 目录没有同编号 JPG。"
            };
        }

        if (map.Width <= 0 || map.Height <= 0 || map.GridWidth <= 0 || map.GridHeight <= 0)
        {
            return new MapSlotCatalogEntry
            {
                MapNumber = number,
                MapId = mapId,
                State = MapSlotState.InvalidImage,
                MapResource = map,
                TerrainEntry = terrainEntry,
                GridWidth = terrainWidth,
                GridHeight = terrainHeight,
                Detail = "地图图片损坏，或宽高不能被 48 整除。"
            };
        }

        if (map.GridWidth != terrainWidth || map.GridHeight != terrainHeight)
        {
            return new MapSlotCatalogEntry
            {
                MapNumber = number,
                MapId = mapId,
                State = MapSlotState.SizeMismatch,
                MapResource = map,
                TerrainEntry = terrainEntry,
                GridWidth = terrainWidth,
                GridHeight = terrainHeight,
                Detail = $"底图为 {map.GridWidth}x{map.GridHeight}，地形块为 {terrainWidth}x{terrainHeight}。"
            };
        }

        return new MapSlotCatalogEntry
        {
            MapNumber = number,
            MapId = mapId,
            State = MapSlotState.Complete,
            MapResource = map,
            TerrainEntry = terrainEntry,
            GridWidth = terrainWidth,
            GridHeight = terrainHeight,
            Detail = "底图与 Hexzmap 地形块完整且尺寸一致。"
        };
    }

    private static bool TryGetMapNumber(MapResourceItem item, out int number)
    {
        number = 0;
        var mapId = string.IsNullOrWhiteSpace(item.MapId)
            ? Path.GetFileNameWithoutExtension(item.Name)
            : item.MapId;
        return mapId.Length > 1 && (mapId[0] == 'M' || mapId[0] == 'm') &&
               int.TryParse(mapId[1..], NumberStyles.Integer, CultureInfo.InvariantCulture, out number);
    }

    private static int GetMapNumber(MapResourceItem item)
        => TryGetMapNumber(item, out var number) ? number : -1;
}
