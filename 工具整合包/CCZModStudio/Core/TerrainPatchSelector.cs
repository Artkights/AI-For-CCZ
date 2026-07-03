using CCZModStudio.Models;

namespace CCZModStudio.Core;

internal sealed class TerrainPatchSelector
{
    public MaterialAsset? Select(
        IReadOnlyList<MaterialAsset> candidates,
        int mask,
        TileVisualStats targetStats,
        string seed,
        int index,
        byte terrainId,
        Func<MaterialAsset, TileVisualStats> getStats)
    {
        if (candidates.Count == 0)
        {
            return null;
        }

        var normalizedMask = TerrainAutoTileResolver.NormalizeEightWayMask(mask);
        var compatible = new List<(MaterialAsset Asset, int FallbackRank)>();
        var rank = 0;
        foreach (var candidateMask in TerrainAutoTileResolver.BuildAutoTileMaskFallbacks(normalizedMask))
        {
            var matches = candidates
                .Where(asset => TerrainAutoTileResolver.NormalizeEightWayMask(TerrainAutoTileResolver.GetAssetMask(asset)) == candidateMask)
                .Where(asset => !asset.AutoTileRole.Equals(MaterialAutoTileRoles.Fill, StringComparison.OrdinalIgnoreCase))
                .ToList();
            foreach (var match in matches)
            {
                compatible.Add((match, rank));
            }

            if (matches.Count > 0)
            {
                break;
            }

            rank++;
        }

        if (compatible.Count == 0)
        {
            compatible = candidates
                .Select(asset => (asset, 99))
                .ToList();
        }

        return compatible
            .OrderBy(item => Score(item.Asset, item.FallbackRank, targetStats, seed, index, terrainId, getStats))
            .ThenBy(item => item.Asset.AutoTilePriority)
            .ThenBy(item => item.Asset.VariantIndex)
            .Select(item => item.Asset)
            .FirstOrDefault();
    }

    private static double Score(
        MaterialAsset asset,
        int fallbackRank,
        TileVisualStats targetStats,
        string seed,
        int index,
        byte terrainId,
        Func<MaterialAsset, TileVisualStats> getStats)
    {
        var stats = getStats(asset);
        var randomJitter = (StableHash(seed, index, terrainId, (uint)(asset.VariantIndex + 1)) % 1000) / 1000.0 * 0.08;
        return fallbackRank * 0.65 +
               asset.AutoTilePriority * 0.01 +
               TileVisualStatsCalculator.Distance(stats, targetStats) +
               randomJitter;
    }

    private static uint StableHash(string? seed, int index, byte terrainId, uint salt)
    {
        unchecked
        {
            var hash = 2166136261u ^ salt;
            foreach (var ch in seed ?? string.Empty)
            {
                hash ^= ch;
                hash *= 16777619u;
            }

            hash ^= (uint)index;
            hash *= 16777619u;
            hash ^= terrainId;
            hash *= 16777619u;
            return hash;
        }
    }
}
