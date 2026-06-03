using System.Globalization;
using CCZModStudio.Models;

namespace CCZModStudio.Formats;

public sealed class ScenarioMapLinkService
{
    public IReadOnlyList<ScenarioMapLinkInfo> BuildLinks(
        IReadOnlyList<ScenarioFileInfo> scenarios,
        IReadOnlyList<ResourceIndexItem> resources,
        HexzmapProbeResult? hexzmap)
    {
        var mapImages = resources
            .Where(x => x.Category == "地图图片")
            .GroupBy(x => NormalizeMapId(x.Id))
            .ToDictionary(g => g.Key, g => g.OrderBy(x => x.Name, StringComparer.CurrentCultureIgnoreCase).First(), StringComparer.OrdinalIgnoreCase);
        var hexBlocks = (hexzmap?.Blocks ?? Array.Empty<HexzmapBlockInfo>())
            .ToDictionary(x => x.MapId, StringComparer.OrdinalIgnoreCase);

        return scenarios
            .OrderBy(x => ScenarioSortKey(x.Id))
            .ThenBy(x => x.FileName, StringComparer.CurrentCultureIgnoreCase)
            .Select(x => BuildLink(x, mapImages, hexBlocks))
            .ToList();
    }

    private static ScenarioMapLinkInfo BuildLink(
        ScenarioFileInfo scenario,
        IReadOnlyDictionary<string, ResourceIndexItem> mapImages,
        IReadOnlyDictionary<string, HexzmapBlockInfo> hexBlocks)
    {
        if (scenario.Kind == "索引/配置" || string.IsNullOrWhiteSpace(scenario.Id) || !int.TryParse(scenario.Id, NumberStyles.Integer, CultureInfo.InvariantCulture, out var numericId))
        {
            return new ScenarioMapLinkInfo
            {
                ScenarioId = scenario.Id,
                ScenarioFileName = scenario.FileName,
                ScenarioTitle = scenario.TitleHint,
                ScenarioKind = scenario.Kind,
                Status = "非普通关卡",
                Annotation = "该 SV 文件更像索引/配置或无法提取稳定数字 ID，暂不按 Mxxx 地图建立候选联动。",
                Suggestion = "通常不需要为索引/配置文件匹配 Map 图片；请优先关注标准/扩展关卡。",
                ScenarioPath = scenario.Path
            };
        }

        var mapId = "M" + numericId.ToString("000", CultureInfo.InvariantCulture);
        mapImages.TryGetValue(mapId, out var map);
        hexBlocks.TryGetValue(mapId, out var block);
        var mapExists = map != null;
        var blockExists = block != null;
        var status = (mapExists, blockExists) switch
        {
            (true, true) => "完整候选",
            (true, false) => "有地图图，缺地形块",
            (false, true) => "有地形块，缺地图图",
            _ => "缺地图图和地形块"
        };

        var title = string.IsNullOrWhiteSpace(scenario.TitleHint) ? "(未识别标题)" : scenario.TitleHint;
        var dominant = block == null
            ? string.Empty
            : string.IsNullOrWhiteSpace(block.DominantTerrainName)
                ? $"0x{block.DominantTerrainId:X2}"
                : $"0x{block.DominantTerrainId:X2} {block.DominantTerrainName}";
        var annotation =
            $"关卡 {scenario.FileName}（{title}）按同编号规则候选对应 {mapId}。地图图片：{(mapExists ? map!.Name : "缺失")}；Hexzmap 地形块：{(blockExists ? "存在，偏移 " + block!.OffsetHex : "缺失")}。" +
            "该联动用于创作时快速核对“剧本文件、战场底图、地形索引”是否成套；由于完整关卡地图引用规则尚未完全反汇编确认，当前标记为候选证据。";
        var suggestion = status switch
        {
            "完整候选" => "可在地图浏览页查看 JPG，在 Hexzmap 地形探针页查看按地图分辨率/48 划分的地形块；修改关卡前建议同时核对二者。",
            "有地图图，缺地形块" => "可先确认该关是否确实使用同编号底图；若需要编辑地形，后续必须找到真实地形索引位置。",
            "有地形块，缺地图图" => "地形索引存在但缺少同编号 JPG，发布前请确认游戏是否使用其他底图或需要补齐 Map 图片。",
            _ => "该关卡缺少同编号地图图和地形块候选；若是新增/扩展关，建议先补齐资源并记录实际引用关系。"
        };

        return new ScenarioMapLinkInfo
        {
            ScenarioId = scenario.Id,
            ScenarioFileName = scenario.FileName,
            ScenarioTitle = scenario.TitleHint,
            ScenarioKind = scenario.Kind,
            MapId = mapId,
            MapImageName = map?.Name ?? string.Empty,
            MapImageExists = mapExists,
            HexzmapBlockExists = blockExists,
            HexzmapOffsetHex = block?.OffsetHex ?? string.Empty,
            DominantTerrain = dominant,
            TopTerrainNames = block?.TopTerrainNames ?? string.Empty,
            KnownTerrainCount = block?.KnownTerrainCount ?? 0,
            Status = status,
            Annotation = annotation,
            Suggestion = suggestion,
            ScenarioPath = scenario.Path,
            MapImagePath = map?.Path ?? string.Empty
        };
    }

    private static string NormalizeMapId(string id)
    {
        if (int.TryParse(id, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number))
        {
            return "M" + number.ToString("000", CultureInfo.InvariantCulture);
        }
        if (id.StartsWith("M", StringComparison.OrdinalIgnoreCase) &&
            int.TryParse(id[1..], NumberStyles.Integer, CultureInfo.InvariantCulture, out number))
        {
            return "M" + number.ToString("000", CultureInfo.InvariantCulture);
        }
        return id;
    }

    private static int ScenarioSortKey(string id)
        => int.TryParse(id, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number) ? number : int.MaxValue;
}
