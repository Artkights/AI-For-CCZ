using CCZModStudio.Models;

namespace CCZModStudio.Core;

internal sealed class TerrainInteriorSynthesisPlanner
{
    public Dictionary<int, TerrainCellTexturePlan> BuildPlan(
        MapWorkbenchDraft draft,
        TerrainVisualProfile profile,
        TerrainVisualSynthesisPlan synthesisPlan,
        out int naturalizedRegionCount,
        out int repeatedPenaltyCount,
        out int structureSkippedCount)
    {
        naturalizedRegionCount = 0;
        repeatedPenaltyCount = 0;
        structureSkippedCount = 0;
        var result = new Dictionary<int, TerrainCellTexturePlan>();
        if (!profile.UseInteriorTextureSynthesis)
        {
            return result;
        }

        foreach (var region in synthesisPlan.Regions)
        {
            if (!TerrainVisualSurfaceClassifier.SupportsInteriorSynthesis(region.SurfaceKind))
            {
                if (region.HasMaterial && TerrainVisualSurfaceClassifier.UsesStructureConnection(region.SurfaceKind))
                {
                    structureSkippedCount += region.CellIndexes.Count;
                }

                continue;
            }

            naturalizedRegionCount++;
            var transformCandidates = BuildTransformCandidates(profile, region.SurfaceKind);
            var secondaryCandidates = region.CandidatePatches
                .Where(IsInteriorPatchCandidate)
                .OrderBy(asset => asset.AutoTilePriority)
                .ThenBy(asset => asset.VariantIndex)
                .ThenBy(asset => asset.FileName, StringComparer.OrdinalIgnoreCase)
                .ToList();
            var plannedByCell = new Dictionary<int, TerrainCellTexturePlan>();

            foreach (var index in region.CellIndexes.OrderBy(index => index / draft.GridWidth).ThenBy(index => index % draft.GridWidth))
            {
                var hash = StableHash(profile.Seed, index, region.TerrainId, 0xCBF29CE4u);
                var primaryTransform = PickTransform(transformCandidates, hash);
                var secondaryTransform = PickTransform(transformCandidates, RotateHash(hash));
                var secondary = PickSecondaryPatch(secondaryCandidates, hash);
                var strength = Math.Clamp(
                    profile.InteriorSecondaryBlendStrength * (0.65f + ((hash >>> 8) & 0xFF) / 255f * 0.7f),
                    0f,
                    0.35f);

                var signature = BuildSignature(secondary, primaryTransform, secondaryTransform);
                if (MatchesPriorSignature(draft, plannedByCell, index, signature))
                {
                    primaryTransform = PickTransform(transformCandidates, RotateHash(hash ^ 0x9E3779B9u));
                    secondaryTransform = PickTransform(transformCandidates, RotateHash(hash ^ 0x85EBCA77u));
                    signature = BuildSignature(secondary, primaryTransform, secondaryTransform);
                    repeatedPenaltyCount++;
                }

                var plan = new TerrainCellTexturePlan(
                    region.RegionId,
                    region.SurfaceKind,
                    primaryTransform,
                    secondaryTransform,
                    secondary,
                    strength,
                    signature);
                plannedByCell[index] = plan;
                result[index] = plan;
            }
        }

        return result;
    }

    public static bool IsInteriorPatchCandidate(MaterialAsset asset)
    {
        var modeIsDefault = string.IsNullOrWhiteSpace(asset.AutoTileMode) ||
                            asset.AutoTileMode.Equals(MaterialAutoTileModes.Default, StringComparison.OrdinalIgnoreCase);
        var roleIsDefault = string.IsNullOrWhiteSpace(asset.AutoTileRole) ||
                            asset.AutoTileRole.Equals(MaterialAutoTileRoles.Default, StringComparison.OrdinalIgnoreCase) ||
                            asset.AutoTileRole.Equals(MaterialAutoTileRoles.Fill, StringComparison.OrdinalIgnoreCase);
        var mask = TerrainAutoTileResolver.NormalizeEightWayMask(TerrainAutoTileResolver.GetAssetMask(asset));
        return modeIsDefault && roleIsDefault && mask == MaterialAutoTileMasks.None;
    }

    private static IReadOnlyList<TerrainTileTransform> BuildTransformCandidates(TerrainVisualProfile profile, TerrainVisualSurfaceKind kind)
    {
        if (!profile.EnableNaturalTileTransforms)
        {
            return [TerrainTileTransform.None];
        }

        return kind == TerrainVisualSurfaceKind.LiquidArea
            ? [TerrainTileTransform.None, TerrainTileTransform.FlipX, TerrainTileTransform.FlipY]
            : profile.AllowNinetyDegreeNaturalRotation
                ? [
                    TerrainTileTransform.None,
                    TerrainTileTransform.Rotate180,
                    TerrainTileTransform.FlipX,
                    TerrainTileTransform.FlipY,
                    TerrainTileTransform.Rotate90,
                    TerrainTileTransform.Rotate270
                ]
                : [
                    TerrainTileTransform.None,
                    TerrainTileTransform.Rotate180,
                    TerrainTileTransform.FlipX,
                    TerrainTileTransform.FlipY
                ];
    }

    private static TerrainTileTransform PickTransform(IReadOnlyList<TerrainTileTransform> candidates, uint hash)
        => candidates.Count == 0 ? TerrainTileTransform.None : candidates[(int)(hash % (uint)candidates.Count)];

    private static MaterialAsset? PickSecondaryPatch(IReadOnlyList<MaterialAsset> candidates, uint hash)
        => candidates.Count == 0 ? null : candidates[(int)((hash >>> 16) % (uint)candidates.Count)];

    private static bool MatchesPriorSignature(
        MapWorkbenchDraft draft,
        IReadOnlyDictionary<int, TerrainCellTexturePlan> plannedByCell,
        int index,
        string signature)
    {
        var x = index % draft.GridWidth;
        var y = index / draft.GridWidth;
        var left = x > 0 ? index - 1 : -1;
        var top = y > 0 ? index - draft.GridWidth : -1;
        return (left >= 0 && plannedByCell.TryGetValue(left, out var leftPlan) && leftPlan.Signature == signature) ||
               (top >= 0 && plannedByCell.TryGetValue(top, out var topPlan) && topPlan.Signature == signature);
    }

    private static string BuildSignature(MaterialAsset? secondary, TerrainTileTransform primary, TerrainTileTransform secondaryTransform)
        => string.Join("|",
            secondary?.FilePath ?? string.Empty,
            secondary?.SourceX.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty,
            secondary?.SourceY.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty,
            primary.ToString(),
            secondaryTransform.ToString());

    private static uint RotateHash(uint value)
        => (value << 13) | (value >>> 19);

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

internal enum TerrainTileTransform
{
    None,
    Rotate90,
    Rotate180,
    Rotate270,
    FlipX,
    FlipY
}

internal sealed record TerrainCellTexturePlan(
    int RegionId,
    TerrainVisualSurfaceKind SurfaceKind,
    TerrainTileTransform PrimaryTransform,
    TerrainTileTransform SecondaryTransform,
    MaterialAsset? SecondaryPatch,
    float SecondaryStrength,
    string Signature);
