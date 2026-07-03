using System.Drawing;
using CCZModStudio.Models;

namespace CCZModStudio.Core;

internal sealed class TerrainVisualRegionPlanner
{
    public TerrainVisualSynthesisPlan BuildPlan(
        MapWorkbenchDraft draft,
        TerrainVisualProfile profile,
        Bitmap? baseImage,
        bool hasValidBaseImage,
        IReadOnlyCollection<int>? requestedIndexes,
        CurrentMapStyleProfile styleProfile,
        IReadOnlyDictionary<byte, List<MaterialAsset>> candidatesByTerrain,
        Func<IReadOnlyList<MaterialAsset>, TileVisualStats, double> scoreGroup,
        IReadOnlySet<int>? basePixelExcludedIndexes = null)
    {
        var redrawSeeds = BuildRedrawSeeds(draft, profile, hasValidBaseImage, requestedIndexes);
        var expanded = ExpandIndexes(draft, redrawSeeds, Math.Max(1, profile.BlendContextRadiusCells));
        if (!hasValidBaseImage || !profile.RedrawChangedCellsOnly || draft.OriginalTerrainCells.Length != draft.CellCount)
        {
            expanded = Enumerable.Range(0, draft.CellCount).ToHashSet();
        }

        var localStats = BuildLocalStyleStats(draft, profile, baseImage, expanded, styleProfile, basePixelExcludedIndexes);
        var regions = BuildRegions(draft, profile, expanded, localStats, styleProfile, candidatesByTerrain, scoreGroup);
        var regionByCell = new Dictionary<int, TerrainVisualRegionPlan>();
        foreach (var region in regions)
        {
            foreach (var index in region.CellIndexes)
            {
                regionByCell[index] = region;
            }
        }

        return new TerrainVisualSynthesisPlan
        {
            RedrawSeedIndexes = redrawSeeds,
            ExpandedRedrawIndexes = expanded,
            ColorTransferIndexes = expanded.ToHashSet(),
            Regions = regions,
            RegionByCell = regionByCell,
            LocalStyleByCell = localStats
        };
    }

    public static bool IsStructureTerrain(byte terrainId)
        => TerrainVisualSurfaceClassifier.UsesStructureConnection(TerrainVisualSurfaceClassifier.Classify(terrainId));

    private static HashSet<int> BuildRedrawSeeds(
        MapWorkbenchDraft draft,
        TerrainVisualProfile profile,
        bool hasValidBaseImage,
        IReadOnlyCollection<int>? requestedIndexes)
    {
        var redrawAll = !hasValidBaseImage ||
                        !profile.RedrawChangedCellsOnly ||
                        draft.OriginalTerrainCells.Length != draft.CellCount;
        if (redrawAll)
        {
            return Enumerable.Range(0, draft.CellCount).ToHashSet();
        }

        var result = new HashSet<int>();
        if (requestedIndexes != null)
        {
            foreach (var index in requestedIndexes)
            {
                if ((uint)index < (uint)draft.CellCount) result.Add(index);
            }

            return result;
        }

        for (var i = 0; i < draft.CellCount; i++)
        {
            if (draft.TerrainCells[i] != draft.OriginalTerrainCells[i])
            {
                result.Add(i);
            }
        }

        return result;
    }

    private static HashSet<int> ExpandIndexes(MapWorkbenchDraft draft, IReadOnlySet<int> indexes, int radius)
    {
        var result = new HashSet<int>();
        foreach (var index in indexes)
        {
            if ((uint)index >= (uint)draft.CellCount) continue;
            var x = index % draft.GridWidth;
            var y = index / draft.GridWidth;
            for (var dy = -radius; dy <= radius; dy++)
            {
                for (var dx = -radius; dx <= radius; dx++)
                {
                    var nx = x + dx;
                    var ny = y + dy;
                    if (nx < 0 || ny < 0 || nx >= draft.GridWidth || ny >= draft.GridHeight) continue;
                    result.Add(ny * draft.GridWidth + nx);
                }
            }
        }

        return result;
    }

    private static Dictionary<int, TileVisualStats> BuildLocalStyleStats(
        MapWorkbenchDraft draft,
        TerrainVisualProfile profile,
        Bitmap? baseImage,
        IReadOnlySet<int> expanded,
        CurrentMapStyleProfile styleProfile,
        IReadOnlySet<int>? basePixelExcludedIndexes)
    {
        var result = new Dictionary<int, TileVisualStats>();
        if (baseImage == null)
        {
            return result;
        }

        var tileSize = draft.TileSize <= 0 ? MapResourceItem.MapTilePixelSize : draft.TileSize;
        var radius = Math.Clamp(profile.StyleContextRadiusCells, 1, 8);
        foreach (var index in expanded)
        {
            var terrain = draft.TerrainCells[index];
            if (basePixelExcludedIndexes?.Contains(index) == true)
            {
                var fallbackStats = styleProfile.FindTerrain(terrain)?.Stats ?? TileVisualStats.Empty;
                if (fallbackStats != TileVisualStats.Empty)
                {
                    result[index] = fallbackStats;
                }

                continue;
            }

            var x = index % draft.GridWidth;
            var y = index / draft.GridWidth;
            var left = Math.Max(0, (x - radius) * tileSize);
            var top = Math.Max(0, (y - radius) * tileSize);
            var right = Math.Min(baseImage.Width, (x + radius + 1) * tileSize);
            var bottom = Math.Min(baseImage.Height, (y + radius + 1) * tileSize);
            if (right <= left || bottom <= top) continue;

            using var crop = new Bitmap(right - left, bottom - top);
            using (var g = Graphics.FromImage(crop))
            {
                g.DrawImage(baseImage, new Rectangle(0, 0, crop.Width, crop.Height), new Rectangle(left, top, crop.Width, crop.Height), GraphicsUnit.Pixel);
            }

            var stats = TileVisualStatsCalculator.Calculate(crop);
            result[index] = stats == TileVisualStats.Empty
                ? styleProfile.FindTerrain(terrain)?.Stats ?? TileVisualStats.Empty
                : stats;
        }

        return result;
    }

    private static List<TerrainVisualRegionPlan> BuildRegions(
        MapWorkbenchDraft draft,
        TerrainVisualProfile profile,
        IReadOnlySet<int> expanded,
        IReadOnlyDictionary<int, TileVisualStats> localStats,
        CurrentMapStyleProfile styleProfile,
        IReadOnlyDictionary<byte, List<MaterialAsset>> candidatesByTerrain,
        Func<IReadOnlyList<MaterialAsset>, TileVisualStats, double> scoreGroup)
    {
        var result = new List<TerrainVisualRegionPlan>();
        if (!profile.UseRegionConsistentMaterial)
        {
            foreach (var index in expanded.OrderBy(index => index))
            {
                var terrain = draft.TerrainCells[index];
                var targetStats = BuildRegionTargetStats([index], terrain, localStats, styleProfile);
                var selected = SelectMaterialGroup(draft, profile, terrain, IsStructureTerrain(terrain), targetStats, candidatesByTerrain, scoreGroup);
                result.Add(new TerrainVisualRegionPlan
                {
                    RegionId = result.Count + 1,
                    TerrainId = terrain,
                    SurfaceKind = TerrainVisualSurfaceClassifier.Classify(terrain),
                    IsStructure = IsStructureTerrain(terrain),
                    CellIndexes = [index],
                    SelectedMaterialGroupKey = selected.GroupKey,
                    CandidatePatches = selected.Assets,
                    TargetStyleStats = targetStats,
                    UsedCurrentMapStyle = selected.UsedCurrentMapStyle,
                    HasMaterial = selected.Assets.Count > 0
                });
            }

            return result;
        }

        var visited = new HashSet<int>();
        foreach (var start in expanded.OrderBy(index => index))
        {
            if (!visited.Add(start)) continue;

            var terrain = draft.TerrainCells[start];
            var surfaceKind = TerrainVisualSurfaceClassifier.Classify(terrain);
            var isStructure = TerrainVisualSurfaceClassifier.UsesStructureConnection(surfaceKind);
            var cells = FloodRegion(draft, expanded, visited, start, terrain, isStructure).ToList();
            cells.Insert(0, start);
            var targetStats = BuildRegionTargetStats(cells, terrain, localStats, styleProfile);
            var selected = SelectMaterialGroup(draft, profile, terrain, isStructure, targetStats, candidatesByTerrain, scoreGroup);
            result.Add(new TerrainVisualRegionPlan
            {
                RegionId = result.Count + 1,
                TerrainId = terrain,
                SurfaceKind = surfaceKind,
                IsStructure = isStructure,
                CellIndexes = cells,
                SelectedMaterialGroupKey = selected.GroupKey,
                CandidatePatches = selected.Assets,
                TargetStyleStats = targetStats,
                UsedCurrentMapStyle = selected.UsedCurrentMapStyle,
                HasMaterial = selected.Assets.Count > 0
            });
        }

        return result;
    }

    private static IEnumerable<int> FloodRegion(
        MapWorkbenchDraft draft,
        IReadOnlySet<int> expanded,
        HashSet<int> visited,
        int start,
        byte terrain,
        bool isStructure)
    {
        var queue = new Queue<int>();
        queue.Enqueue(start);
        var directions = isStructure ? EightDirections : FourDirections;
        while (queue.Count > 0)
        {
            var index = queue.Dequeue();
            var x = index % draft.GridWidth;
            var y = index / draft.GridWidth;
            foreach (var direction in directions)
            {
                var nx = x + direction.Dx;
                var ny = y + direction.Dy;
                if (nx < 0 || ny < 0 || nx >= draft.GridWidth || ny >= draft.GridHeight) continue;
                var next = ny * draft.GridWidth + nx;
                if (!expanded.Contains(next) || draft.TerrainCells[next] != terrain || !visited.Add(next)) continue;
                queue.Enqueue(next);
                yield return next;
            }
        }
    }

    private static TileVisualStats BuildRegionTargetStats(
        IReadOnlyList<int> cells,
        byte terrain,
        IReadOnlyDictionary<int, TileVisualStats> localStats,
        CurrentMapStyleProfile styleProfile)
    {
        var stats = TileVisualStatsCalculator.Combine(cells.Select(index => localStats.TryGetValue(index, out var value) ? value : TileVisualStats.Empty));
        return stats == TileVisualStats.Empty
            ? styleProfile.FindTerrain(terrain)?.Stats ?? TileVisualStats.Empty
            : stats;
    }

    private static SelectedMaterialGroup SelectMaterialGroup(
        MapWorkbenchDraft draft,
        TerrainVisualProfile profile,
        byte terrain,
        bool isStructure,
        TileVisualStats targetStats,
        IReadOnlyDictionary<byte, List<MaterialAsset>> candidatesByTerrain,
        Func<IReadOnlyList<MaterialAsset>, TileVisualStats, double> scoreGroup)
    {
        if (!candidatesByTerrain.TryGetValue(terrain, out var candidates) || candidates.Count == 0)
        {
            return SelectedMaterialGroup.Empty;
        }

        var manual = profile.MaterialOverrides.FirstOrDefault(item => item.TerrainId == terrain);
        if (manual != null)
        {
            var manualPath = MapDraftService.ResolveMaterialPath(draft.MaterialRoot, manual.MaterialRelativePath);
            var manualMaterial = candidates.FirstOrDefault(asset =>
                asset.FilePath.Equals(manualPath, StringComparison.OrdinalIgnoreCase) ||
                MapDraftService.GetMaterialRelativePath(draft.MaterialRoot, asset.FilePath).Equals(manual.MaterialRelativePath, StringComparison.OrdinalIgnoreCase));
            if (manualMaterial != null)
            {
                var groupKey = BuildGroupKey(manualMaterial);
                return new SelectedMaterialGroup(groupKey, candidates.Where(asset => BuildGroupKey(asset).Equals(groupKey, StringComparison.OrdinalIgnoreCase)).ToList(), false);
            }
        }

        var groups = candidates
            .GroupBy(BuildGroupKey, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.ToList())
            .Where(group => group.Count > 0)
            .ToList();
        if (groups.Count == 0)
        {
            return SelectedMaterialGroup.Empty;
        }

        var selected = groups
            .OrderBy(group => ScoreCandidateGroup(group, targetStats, isStructure, profile, scoreGroup))
            .ThenBy(group => group[0].Category, StringComparer.OrdinalIgnoreCase)
            .ThenBy(group => group[0].FileName, StringComparer.OrdinalIgnoreCase)
            .First();
        return new SelectedMaterialGroup(
            BuildGroupKey(selected[0]),
            selected,
            selected.Any(asset => asset.Category.Equals("CurrentMapStyle", StringComparison.OrdinalIgnoreCase)));
    }

    private static string BuildGroupKey(MaterialAsset asset)
        => string.IsNullOrWhiteSpace(asset.AutoTileSetKey)
            ? string.IsNullOrWhiteSpace(asset.GroupKey) ? asset.FilePath : asset.GroupKey
            : asset.AutoTileSetKey;

    private static double ScoreCandidateGroup(
        IReadOnlyList<MaterialAsset> group,
        TileVisualStats targetStats,
        bool isStructure,
        TerrainVisualProfile profile,
        Func<IReadOnlyList<MaterialAsset>, TileVisualStats, double> scoreGroup)
    {
        var score = scoreGroup(group, targetStats);
        if (profile.UseCurrentMapSamples && group.Any(asset => asset.Category.Equals("CurrentMapStyle", StringComparison.OrdinalIgnoreCase)))
        {
            score -= 1000;
        }

        if (isStructure)
        {
            var masks = group.Select(TerrainAutoTileResolver.GetAssetMask).Distinct().Count();
            if (masks <= 1) score += 1.25;
            else if (masks < 4) score += 0.5;
        }

        return score;
    }

    private static readonly GridDirection[] FourDirections =
    [
        new(0, -1),
        new(1, 0),
        new(0, 1),
        new(-1, 0)
    ];

    private static readonly GridDirection[] EightDirections =
    [
        new(0, -1),
        new(1, -1),
        new(1, 0),
        new(1, 1),
        new(0, 1),
        new(-1, 1),
        new(-1, 0),
        new(-1, -1)
    ];

    private readonly record struct GridDirection(int Dx, int Dy);
    private sealed record SelectedMaterialGroup(string GroupKey, IReadOnlyList<MaterialAsset> Assets, bool UsedCurrentMapStyle)
    {
        public static readonly SelectedMaterialGroup Empty = new(string.Empty, Array.Empty<MaterialAsset>(), false);
    }
}

internal sealed class TerrainVisualSynthesisPlan
{
    public required HashSet<int> RedrawSeedIndexes { get; init; }
    public required HashSet<int> ExpandedRedrawIndexes { get; init; }
    public required HashSet<int> ColorTransferIndexes { get; init; }
    public required List<TerrainVisualRegionPlan> Regions { get; init; }
    public required Dictionary<int, TerrainVisualRegionPlan> RegionByCell { get; init; }
    public required Dictionary<int, TileVisualStats> LocalStyleByCell { get; init; }
    public Dictionary<int, TerrainCellTexturePlan> InteriorTextureByCell { get; set; } = new();
    public Dictionary<int, Bitmap> RegionTextureByCell { get; set; } = new();
}

internal sealed class TerrainVisualRegionPlan
{
    public int RegionId { get; init; }
    public byte TerrainId { get; init; }
    public TerrainVisualSurfaceKind SurfaceKind { get; init; }
    public bool IsStructure { get; init; }
    public required List<int> CellIndexes { get; init; }
    public string SelectedMaterialGroupKey { get; init; } = string.Empty;
    public IReadOnlyList<MaterialAsset> CandidatePatches { get; init; } = Array.Empty<MaterialAsset>();
    public TileVisualStats TargetStyleStats { get; init; } = TileVisualStats.Empty;
    public bool UsedCurrentMapStyle { get; init; }
    public bool HasMaterial { get; init; }
}
