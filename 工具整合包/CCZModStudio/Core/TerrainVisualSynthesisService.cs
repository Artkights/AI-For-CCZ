using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Diagnostics;
using CCZModStudio.Formats;
using CCZModStudio.Models;

namespace CCZModStudio.Core;

public sealed class TerrainVisualSynthesisService : IDisposable
{
    private static readonly object SynthesisCacheLock = new();
    private static CachedSynthesisResult? _lastSynthesisResult;

    private readonly Dictionary<string, CachedImage> _imageCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, TileVisualStats> _statsCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly CurrentMapStyleProfileService _styleProfileService = new();
    private readonly TerrainVisualRegionPlanner _regionPlanner = new();
    private readonly TerrainInteriorSynthesisPlanner _interiorPlanner = new();
    private readonly TerrainInteriorSeamBlender _interiorSeamBlender = new();
    private readonly TerrainTransitionMaskBuilder _transitionMaskBuilder = new();
    private readonly TerrainTransitionFieldBuilder _transitionFieldBuilder = new();
    private readonly TerrainRegionTextureSynthesizer _regionTextureSynthesizer = new();
    private readonly TerrainPatchSelector _patchSelector = new();
    private readonly LocalColorTransferService _localColorTransferService = new();
    private readonly ObjectAlphaRepairService _objectAlphaRepairService = new();
    private readonly ObjectGroundInpaintPlanner _objectGroundInpaintPlanner = new();

    public TerrainVisualSynthesisDiagnostics Analyze(MapWorkbenchDraft draft, IReadOnlyList<MaterialAsset> materials, CurrentMapStyleProfile? styleProfile = null)
    {
        using var result = Synthesize(new TerrainVisualSynthesisRequest
        {
            Draft = draft,
            Materials = materials,
            StyleProfile = styleProfile
        });
        return result.Diagnostics;
    }

    public TerrainVisualSynthesisResult Synthesize(TerrainVisualSynthesisRequest request)
    {
        var totalWatch = Stopwatch.StartNew();
        var draft = request.Draft ?? throw new InvalidOperationException("Map draft is required.");
        ValidateDraft(draft);
        var profile = NormalizeProfile(draft.TerrainVisualProfile);
        var tileSize = draft.TileSize <= 0 ? MapResourceItem.MapTilePixelSize : draft.TileSize;
        var styleProfile = request.StyleProfile ?? BuildStyleProfile(draft, profile);

        var width = checked(draft.GridWidth * tileSize);
        var height = checked(draft.GridHeight * tileSize);
        var styleAssets = BuildStyleSampleAssets(styleProfile);
        var allMaterials = styleAssets.Concat(request.Materials).ToList();
        var cacheKey = profile.UseSynthesisCaches
            ? BuildSynthesisCacheKey(draft, profile, allMaterials, styleProfile, request.RedrawIndexes)
            : string.Empty;
        if (profile.UseSynthesisCaches && TryGetCachedSynthesis(cacheKey, out var cached))
        {
            return cached;
        }

        var output = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        using var g = CreateGraphics(output);

        var baseImage = GetValidBaseImage(draft, width, height);
        if (baseImage != null)
        {
            g.DrawImage(baseImage, new Rectangle(0, 0, width, height));
        }
        else
        {
            using var brush = new SolidBrush(Color.FromArgb(42, 42, 42));
            g.FillRectangle(brush, new Rectangle(0, 0, width, height));
        }

        var objectGroundPlan = baseImage != null
            ? _objectGroundInpaintPlanner.Build(draft, profile)
            : CreateEmptyObjectGroundInpaintPlan(draft);
        var groundDraft = objectGroundPlan.ObjectFootprintIndexes.Count > 0
            ? CreateVisualGroundDraft(draft, objectGroundPlan.VisualGroundTerrainCells)
            : draft;

        var candidatesByTerrain = allMaterials
            .Where(asset => asset.TerrainId.HasValue)
            .GroupBy(asset => asset.TerrainId!.Value)
            .ToDictionary(group => group.Key, group => group.ToList());
        var planWatch = Stopwatch.StartNew();
        var requestedRedrawIndexes = BuildEffectiveRedrawIndexes(draft, profile, request.RedrawIndexes);
        requestedRedrawIndexes = MergeForcedGroundRedrawIndexes(
            draft,
            profile,
            baseImage != null,
            requestedRedrawIndexes,
            objectGroundPlan.ForcedGroundRedrawIndexes);
        var plan = _regionPlanner.BuildPlan(
            groundDraft,
            profile,
            baseImage,
            baseImage != null,
            requestedRedrawIndexes,
            styleProfile,
            candidatesByTerrain,
            ScoreCandidateGroup,
            objectGroundPlan.ObjectFootprintIndexes);
        planWatch.Stop();

        var diagnostics = new TerrainVisualSynthesisDiagnostics
        {
            FastPipelineEnabled = profile.UseFastPixelPipeline,
            UsedCurrentMapStyle = styleProfile.SampleCount > 0 && plan.Regions.Any(region => region.UsedCurrentMapStyle),
            StyleSampleCount = styleProfile.SampleCount,
            RedrawnCellCount = plan.ExpandedRedrawIndexes.Count,
            ExpandedRedrawCellCount = plan.ExpandedRedrawIndexes.Count,
            PreservedCellCount = Math.Max(0, draft.CellCount - plan.ExpandedRedrawIndexes.Count),
            RegionCount = plan.Regions.Count,
            RegionLockedMaterialCount = plan.Regions.Count(region => region.HasMaterial),
            FallbackGroupCount = plan.Regions.Count(region => !region.HasMaterial),
            PlanMs = planWatch.ElapsedMilliseconds,
            BuildingGroundRedrawCellCount = CountBuildingGroundRedrawCells(draft, requestedRedrawIndexes),
            BuildingOverlayCellCount = draft.BuildingOverlayCells.Count,
            BuildingVisualPlanCellCount = draft.TerrainVisualProfile.UseGlobalBuildingStyle ? draft.BuildingOverlayCells.Count : 0,
            CurrentMapSampleRejectedCount = styleProfile.RejectedSampleCount,
            CurrentMapPureSampleUsedCount = styleAssets.Count(asset => asset.Category.Equals("CurrentMapStyle", StringComparison.OrdinalIgnoreCase)),
            ObjectGroundFootprintCellCount = objectGroundPlan.ObjectFootprintIndexes.Count,
            ObjectGroundInpaintCellCount = objectGroundPlan.ForcedGroundRedrawIndexes.Count,
            ObjectGroundInferredCellCount = objectGroundPlan.InferredGroundTerrainByCell.Count,
            ObjectGroundFallbackCellCount = objectGroundPlan.FallbackCellCount,
            ObjectGroundContextSampleCount = objectGroundPlan.ObjectGroundContextIndexes.Count,
            TerrainObjectOverlayCellCount = objectGroundPlan.TerrainObjectOverlayCellCount
        };
        PopulateObjectAlphaRepairDiagnostics(draft, request.Materials, profile, diagnostics);

        plan.InteriorTextureByCell = _interiorPlanner.BuildPlan(
            groundDraft,
            profile,
            plan,
            out var naturalizedRegions,
            out var repeatedPenaltyCount,
            out var structureSkippedCount);
        diagnostics.NaturalizedRegionCount = naturalizedRegions;
        diagnostics.RepeatedPatchPenaltyCount = repeatedPenaltyCount;
        diagnostics.StructureTransformSkippedCount = structureSkippedCount;

        if (profile.UseRegionTextureCanvas)
        {
            var regionTextureWatch = Stopwatch.StartNew();
            plan.RegionTextureByCell = _regionTextureSynthesizer.Build(
                groundDraft,
                profile,
                plan,
                (material, index, targetStats) => CreateMaterialTile(groundDraft, material, targetStats, profile, index),
                GetMaterialStats,
                out var regionTextureStats);
            regionTextureWatch.Stop();
            diagnostics.InteriorBlendMs += regionTextureWatch.ElapsedMilliseconds;
            diagnostics.RegionTextureCanvasCount = regionTextureStats.RegionTextureCanvasCount;
            diagnostics.QuiltedPatchCount = regionTextureStats.QuiltedPatchCount;
            diagnostics.PatchOverlapRejectedCount = regionTextureStats.PatchOverlapRejectedCount;
            diagnostics.MacroNoiseAppliedPixels = regionTextureStats.MacroNoiseAppliedPixels;
            diagnostics.InteriorSeamBlendPixelCount += regionTextureStats.InteriorSeamBlendPixelCount;
            diagnostics.SecondaryPatchBlendPixelCount += regionTextureStats.SecondaryPatchBlendPixelCount;
            diagnostics.TileTransformCount += regionTextureStats.TileTransformCount;
        }

        var renderedTiles = new Dictionary<int, Bitmap>();
        try
        {
            foreach (var index in plan.ExpandedRedrawIndexes.OrderBy(index => index))
            {
                var tileWatch = Stopwatch.StartNew();
                var tile = profile.UseGlobalTransitionField
                    ? RenderBaseTerrainTileForGlobalField(groundDraft, profile, plan, baseImage, index, diagnostics)
                    : RenderMixedTerrainTile(groundDraft, profile, plan, baseImage, index, diagnostics);
                tileWatch.Stop();
                diagnostics.TileRenderMs += tileWatch.ElapsedMilliseconds;

                if (!profile.UseGlobalTransitionField || !plan.RegionTextureByCell.ContainsKey(index))
                {
                    var seamWatch = Stopwatch.StartNew();
                    diagnostics.InteriorSeamBlendPixelCount += _interiorSeamBlender.Blend(
                        tile,
                        GetRenderedSameRegionNeighbor(groundDraft, plan, renderedTiles, index, dx: -1, dy: 0),
                        GetRenderedSameRegionNeighbor(groundDraft, plan, renderedTiles, index, dx: 0, dy: -1),
                        groundDraft,
                        profile,
                        index,
                        plan.RegionByCell.GetValueOrDefault(index));
                    seamWatch.Stop();
                    diagnostics.InteriorBlendMs += seamWatch.ElapsedMilliseconds;
                }

                g.DrawImage(tile, GetTileRectangle(groundDraft, index));
                renderedTiles[index] = tile;
            }

            if (profile.UseGlobalTransitionField && profile.UseDirectionalBoundaryBlend)
            {
                ApplyGlobalTransitionField(output, groundDraft, profile, plan, diagnostics);
            }
        }
        finally
        {
            foreach (var tile in renderedTiles.Values)
            {
                tile.Dispose();
            }

            foreach (var tile in plan.RegionTextureByCell.Values)
            {
                tile.Dispose();
            }
        }

        var colorWatch = Stopwatch.StartNew();
        diagnostics.LocalColorTransferPixelCount = _localColorTransferService.Apply(
            output,
            baseImage,
            draft,
            profile,
            plan.ColorTransferIndexes);
        colorWatch.Stop();
        diagnostics.ColorTransferMs = colorWatch.ElapsedMilliseconds;
        diagnostics.BoundaryBlendCount = diagnostics.MixedTerrainCellCount;
        diagnostics.MissingTerrainIds.Sort();
        if (styleProfile.SampleCount == 0)
        {
            diagnostics.Notes.Add("No current-map style samples were available; material library and terrain-color fallback were used.");
        }

        if (diagnostics.RegionCount > 0)
        {
            diagnostics.Notes.Add($"Region material lock: {diagnostics.RegionLockedMaterialCount}/{diagnostics.RegionCount} regions used a locked material group.");
        }

        diagnostics.MaterialLibraryFallbackCount = plan.Regions.Count(region => region.HasMaterial && !region.UsedCurrentMapStyle);
        if (diagnostics.CurrentMapSampleRejectedCount > 0)
        {
            diagnostics.Notes.Add($"Rejected {diagnostics.CurrentMapSampleRejectedCount} current-map samples because they were object-covered, changed, or visually contaminated.");
        }

        if (diagnostics.ObjectGroundFootprintCellCount > 0)
        {
            diagnostics.Notes.Add($"Object ground inpaint rebuilt {diagnostics.ObjectGroundFootprintCellCount} object footprint cells and {diagnostics.ObjectGroundContextSampleCount} surrounding context cells before object overlay.");
        }

        if (diagnostics.MaterialLibraryFallbackCount > 0)
        {
            diagnostics.Notes.Add($"Material library fallback was used for {diagnostics.MaterialLibraryFallbackCount} terrain regions without clean current-map samples.");
        }

        totalWatch.Stop();
        diagnostics.TotalMs = totalWatch.ElapsedMilliseconds;
        if (profile.UseSynthesisCaches)
        {
            StoreCachedSynthesis(cacheKey, output, diagnostics);
        }

        return new TerrainVisualSynthesisResult
        {
            Bitmap = output,
            Diagnostics = diagnostics
        };
    }

    public Bitmap RenderBaseTerrain(MapWorkbenchDraft draft, IReadOnlyList<MaterialAsset> materials)
    {
        var result = Synthesize(new TerrainVisualSynthesisRequest
        {
            Draft = draft,
            Materials = materials
        });
        return result.Bitmap;
    }

    public void Dispose()
    {
        foreach (var cached in _imageCache.Values)
        {
            cached.Bitmap.Dispose();
        }

        _imageCache.Clear();
        _statsCache.Clear();
    }

    private Bitmap RenderMixedTerrainTile(
        MapWorkbenchDraft draft,
        TerrainVisualProfile profile,
        TerrainVisualSynthesisPlan plan,
        Bitmap? baseImage,
        int index,
        TerrainVisualSynthesisDiagnostics diagnostics)
    {
        var region = plan.RegionByCell.GetValueOrDefault(index);
        using var center = RenderTerrainTile(draft, profile, plan, baseImage, index, region, diagnostics, countDiagnostics: true);
        if (!profile.UseDirectionalBoundaryBlend)
        {
            var noBoundaryBlend = new Bitmap(center);
            ApplyRegionTextureUnifier(noBoundaryBlend, draft, profile, index, region);
            return noBoundaryBlend;
        }

        var mask = _transitionMaskBuilder.Build(draft, index, profile);
        if (mask.Neighbors.Count == 0)
        {
            var unchanged = new Bitmap(center);
            ApplyRegionTextureUnifier(unchanged, draft, profile, index, region);
            return unchanged;
        }

        var neighborTiles = new List<(TerrainTransitionNeighbor Neighbor, FastBitmapBuffer Tile)>();
        foreach (var neighbor in mask.Neighbors)
        {
            var neighborRegion = plan.RegionByCell.GetValueOrDefault(neighbor.CellIndex);
            using var neighborTile = RenderTerrainTile(draft, profile, plan, baseImage, neighbor.CellIndex, neighborRegion, diagnostics, countDiagnostics: false);
            neighborTiles.Add((neighbor, FastBitmapBuffer.FromBitmap(neighborTile)));
        }

        var boundaryWatch = Stopwatch.StartNew();
        using var centerBuffer = FastBitmapBuffer.FromBitmap(center);
        var result = new FastBitmapBuffer(center.Width, center.Height);
        var centerMinWeight = Math.Clamp(profile.CenterMinWeight, 0f, 0.95f);
        var centerHasTransparency = centerBuffer.HasAlphaBelow(250);
        for (var y = 0; y < result.Height; y++)
        {
            for (var x = 0; x < result.Width; x++)
            {
                var pixel = y * result.Width + x;
                var centerColor = centerBuffer.Pixels[pixel];
                var centerA = (centerColor >>> 24) & 0xFF;
                if (region?.IsStructure == true &&
                    centerHasTransparency &&
                    centerA >= profile.StructureAlphaPreserveThreshold)
                {
                    result.Pixels[pixel] = centerColor;
                    continue;
                }

                var totalNeighborWeight = 0f;
                foreach (var item in neighborTiles)
                {
                    totalNeighborWeight += item.Neighbor.Weights[pixel];
                }

                if (totalNeighborWeight <= 0.015f)
                {
                    result.Pixels[pixel] = centerColor;
                    continue;
                }

                diagnostics.BoundaryMaskPixelCount++;
                var centerWeight = Math.Max(centerMinWeight, 1f - totalNeighborWeight);
                var totalWeight = centerWeight + totalNeighborWeight;
                var a = centerA * centerWeight;
                var r = ((centerColor >>> 16) & 0xFF) * centerWeight;
                var g = ((centerColor >>> 8) & 0xFF) * centerWeight;
                var b = (centerColor & 0xFF) * centerWeight;
                foreach (var item in neighborTiles)
                {
                    var weight = item.Neighbor.Weights[pixel];
                    if (weight <= 0f) continue;
                    var nx = MapNeighborPixel(x, result.Width, item.Neighbor.Dx, profile.BoundaryFeatherPixels);
                    var ny = MapNeighborPixel(y, result.Height, item.Neighbor.Dy, profile.BoundaryFeatherPixels);
                    var neighborColor = item.Tile.Pixels[ny * item.Tile.Width + nx];
                    a += ((neighborColor >>> 24) & 0xFF) * weight;
                    r += ((neighborColor >>> 16) & 0xFF) * weight;
                    g += ((neighborColor >>> 8) & 0xFF) * weight;
                    b += (neighborColor & 0xFF) * weight;
                }

                result.Pixels[pixel] = FastBitmapBuffer.Pack(
                    (int)MathF.Round(a / totalWeight),
                    (int)MathF.Round(r / totalWeight),
                    (int)MathF.Round(g / totalWeight),
                    (int)MathF.Round(b / totalWeight));
            }
        }

        boundaryWatch.Stop();
        diagnostics.BoundaryBlendMs += boundaryWatch.ElapsedMilliseconds;
        diagnostics.MixedTerrainCellCount++;
        var bitmap = result.ToBitmap();
        ApplyRegionTextureUnifier(bitmap, draft, profile, index, region);
        return bitmap;
    }

    private Bitmap RenderTerrainTile(
        MapWorkbenchDraft draft,
        TerrainVisualProfile profile,
        TerrainVisualSynthesisPlan plan,
        Bitmap? baseImage,
        int index,
        TerrainVisualRegionPlan? region,
        TerrainVisualSynthesisDiagnostics diagnostics,
        bool countDiagnostics)
    {
        var tileSize = draft.TileSize <= 0 ? MapResourceItem.MapTilePixelSize : draft.TileSize;
        if (region == null && baseImage != null)
        {
            return CropBaseTile(baseImage, draft, index, tileSize);
        }

        if (region != null &&
            TerrainVisualSurfaceClassifier.SupportsInteriorSynthesis(region.SurfaceKind) &&
            plan.RegionTextureByCell.TryGetValue(index, out var regionTextureTile))
        {
            if (countDiagnostics)
            {
                diagnostics.MaterialMatchedCellCount++;
            }

            return new Bitmap(regionTextureTile);
        }

        var terrainId = draft.TerrainCells[index];
        var targetStats = region?.TargetStyleStats ??
                          plan.LocalStyleByCell.GetValueOrDefault(index) ??
                          TileVisualStats.Empty;
        var mask = TerrainAutoTileResolver.BuildSameTerrainMask(draft, index);
        var candidates = region?.CandidatePatches ?? Array.Empty<MaterialAsset>();
        var material = _patchSelector.Select(candidates, mask, targetStats, profile.Seed, index, terrainId, GetMaterialStats);
        if (material != null)
        {
            if (countDiagnostics)
            {
                diagnostics.MaterialMatchedCellCount++;
                if (region?.IsStructure == true && !HasExactMask(candidates, mask))
                {
                    diagnostics.MissingTransitionMaskCount++;
                }
            }

            var tile = CreateMaterialTile(draft, material, targetStats, profile, index);
            ApplyInteriorTextureSynthesis(tile, draft, profile, plan, index, region, material, targetStats, diagnostics);
            return tile;
        }

        if (countDiagnostics)
        {
            diagnostics.FallbackCellCount++;
            if (!diagnostics.MissingTerrainIds.Contains(terrainId))
            {
                diagnostics.MissingTerrainIds.Add(terrainId);
            }
        }

        return CreateFallbackTile(terrainId, tileSize);
    }

    private static bool HasExactMask(IReadOnlyList<MaterialAsset> candidates, int mask)
    {
        var normalized = TerrainAutoTileResolver.NormalizeEightWayMask(mask);
        return candidates.Any(asset => TerrainAutoTileResolver.NormalizeEightWayMask(TerrainAutoTileResolver.GetAssetMask(asset)) == normalized);
    }

    private Bitmap CreateMaterialTile(
        MapWorkbenchDraft draft,
        MaterialAsset material,
        TileVisualStats targetStats,
        TerrainVisualProfile profile,
        int index)
    {
        var tileSize = draft.TileSize <= 0 ? MapResourceItem.MapTilePixelSize : draft.TileSize;
        var source = GetCachedImage(material.FilePath);
        if (source == null)
        {
            return CreateFallbackTile(material.TerrainId ?? 0, tileSize);
        }

        var sourceRect = BuildSourceRect(material, source);
        var tile = new Bitmap(tileSize, tileSize, PixelFormat.Format32bppArgb);
        using (var tileGraphics = CreateGraphics(tile))
        {
            tileGraphics.DrawImage(source, new Rectangle(0, 0, tile.Width, tile.Height), sourceRect, GraphicsUnit.Pixel);
        }

        if (targetStats != TileVisualStats.Empty)
        {
            var materialStats = GetMaterialStats(material);
            ApplyStyleAdjustment(tile, materialStats, targetStats, profile, draft.DraftId, index, material.TerrainId ?? 0);
        }

        return tile;
    }

    private static Bitmap CreateFallbackTile(byte terrainId, int tileSize)
    {
        var tile = new Bitmap(tileSize, tileSize, PixelFormat.Format32bppArgb);
        using var g = CreateGraphics(tile);
        using var brush = new SolidBrush(HexzmapTerrainRenderService.GetTerrainColor(terrainId));
        g.FillRectangle(brush, new Rectangle(0, 0, tile.Width, tile.Height));
        return tile;
    }

    private static Bitmap CropBaseTile(Bitmap baseImage, MapWorkbenchDraft draft, int index, int tileSize)
    {
        var tile = new Bitmap(tileSize, tileSize, PixelFormat.Format32bppArgb);
        var rect = GetTileRectangle(draft, index);
        using var g = CreateGraphics(tile);
        g.DrawImage(baseImage, new Rectangle(0, 0, tile.Width, tile.Height), rect, GraphicsUnit.Pixel);
        return tile;
    }

    private void ApplyInteriorTextureSynthesis(
        Bitmap tile,
        MapWorkbenchDraft draft,
        TerrainVisualProfile profile,
        TerrainVisualSynthesisPlan plan,
        int index,
        TerrainVisualRegionPlan? region,
        MaterialAsset primaryMaterial,
        TileVisualStats targetStats,
        TerrainVisualSynthesisDiagnostics diagnostics)
    {
        if (!profile.UseInteriorTextureSynthesis ||
            region == null ||
            !TerrainVisualSurfaceClassifier.SupportsInteriorSynthesis(region.SurfaceKind) ||
            !plan.InteriorTextureByCell.TryGetValue(index, out var texturePlan))
        {
            return;
        }

        var transform = ResolveSafeTransform(primaryMaterial, tile, texturePlan.PrimaryTransform, region.SurfaceKind, profile);
        if (ApplyTileTransform(tile, transform))
        {
            diagnostics.TileTransformCount++;
        }

        var secondary = texturePlan.SecondaryPatch;
        if (secondary == null || texturePlan.SecondaryStrength <= 0.001f)
        {
            return;
        }

        using var secondaryTile = CreateMaterialTile(draft, secondary, targetStats, profile, index);
        var secondaryTransform = ResolveSafeTransform(secondary, secondaryTile, texturePlan.SecondaryTransform, region.SurfaceKind, profile);
        if (ApplyTileTransform(secondaryTile, secondaryTransform))
        {
            diagnostics.TileTransformCount++;
        }

        diagnostics.SecondaryPatchBlendPixelCount += BlendSecondaryPatch(
            tile,
            secondaryTile,
            draft,
            profile,
            index,
            region.TerrainId,
            texturePlan.SecondaryStrength);
    }

    private static TerrainTileTransform ResolveSafeTransform(
        MaterialAsset material,
        Bitmap tile,
        TerrainTileTransform requested,
        TerrainVisualSurfaceKind surfaceKind,
        TerrainVisualProfile profile)
    {
        if (requested == TerrainTileTransform.None ||
            !profile.EnableNaturalTileTransforms ||
            !TerrainVisualSurfaceClassifier.SupportsRandomTransforms(surfaceKind) ||
            !TerrainInteriorSynthesisPlanner.IsInteriorPatchCandidate(material))
        {
            return TerrainTileTransform.None;
        }

        if (surfaceKind == TerrainVisualSurfaceKind.LiquidArea &&
            requested is TerrainTileTransform.Rotate90 or TerrainTileTransform.Rotate180 or TerrainTileTransform.Rotate270)
        {
            return TerrainTileTransform.None;
        }

        if (requested is TerrainTileTransform.Rotate90 or TerrainTileTransform.Rotate270)
        {
            return profile.AllowNinetyDegreeNaturalRotation && IsTileLikelyIsotropic(tile)
                ? requested
                : TerrainTileTransform.Rotate180;
        }

        return requested;
    }

    private static bool ApplyTileTransform(Bitmap tile, TerrainTileTransform transform)
    {
        if (transform == TerrainTileTransform.None) return false;
        using var source = FastBitmapBuffer.FromBitmap(tile);
        var target = new FastBitmapBuffer(source.Width, source.Height);
        for (var y = 0; y < source.Height; y++)
        {
            for (var x = 0; x < source.Width; x++)
            {
                var sourceIndex = y * source.Width + x;
                var (tx, ty) = transform switch
                {
                    TerrainTileTransform.Rotate90 => (source.Height - 1 - y, x),
                    TerrainTileTransform.Rotate180 => (source.Width - 1 - x, source.Height - 1 - y),
                    TerrainTileTransform.Rotate270 => (y, source.Width - 1 - x),
                    TerrainTileTransform.FlipX => (source.Width - 1 - x, y),
                    TerrainTileTransform.FlipY => (x, source.Height - 1 - y),
                    _ => (x, y)
                };
                if ((uint)tx >= (uint)target.Width || (uint)ty >= (uint)target.Height) continue;
                target.Pixels[ty * target.Width + tx] = source.Pixels[sourceIndex];
            }
        }

        target.CopyTo(tile);
        return true;
    }

    private static int BlendSecondaryPatch(
        Bitmap primary,
        Bitmap secondary,
        MapWorkbenchDraft draft,
        TerrainVisualProfile profile,
        int index,
        byte terrainId,
        float strength)
    {
        if (primary.Width != secondary.Width || primary.Height != secondary.Height) return 0;
        using var primaryBuffer = FastBitmapBuffer.FromBitmap(primary);
        using var secondaryBuffer = FastBitmapBuffer.FromBitmap(secondary);
        var tileX = index % draft.GridWidth;
        var tileY = index / draft.GridWidth;
        var changed = 0;
        var scale = Math.Max(16, profile.RegionNoiseScalePixels <= 0 ? 96 : profile.RegionNoiseScalePixels);
        for (var y = 0; y < primaryBuffer.Height; y++)
        {
            for (var x = 0; x < primaryBuffer.Width; x++)
            {
                var pixel = y * primaryBuffer.Width + x;
                var alpha = (primaryBuffer.Pixels[pixel] >>> 24) & 0xFF;
                if (alpha == 0) continue;

                var worldX = tileX * primaryBuffer.Width + x;
                var worldY = tileY * primaryBuffer.Height + y;
                var noise = ValueNoise(profile.Seed, worldX / (float)scale, worldY / (float)scale, terrainId, 0xD1B54A35u);
                var amount = strength * (0.55f + noise * 0.45f);
                if (amount <= 0.003f) continue;
                primaryBuffer.Pixels[pixel] = FastBitmapBuffer.LerpArgb(primaryBuffer.Pixels[pixel], secondaryBuffer.Pixels[pixel], amount);
                changed++;
            }
        }

        if (changed > 0)
        {
            primaryBuffer.CopyTo(primary);
        }

        return changed;
    }

    private static void ApplyRegionTextureUnifier(
        Bitmap tile,
        MapWorkbenchDraft draft,
        TerrainVisualProfile profile,
        int index,
        TerrainVisualRegionPlan? region)
    {
        if (profile.RegionTextureUnifyStrength <= 0f ||
            region == null ||
            !TerrainVisualSurfaceClassifier.SupportsInteriorSynthesis(region.SurfaceKind))
        {
            return;
        }

        using var buffer = FastBitmapBuffer.FromBitmap(tile);
        var tileX = index % draft.GridWidth;
        var tileY = index / draft.GridWidth;
        var scale = Math.Max(16, profile.RegionNoiseScalePixels <= 0 ? 96 : profile.RegionNoiseScalePixels);
        var maxLumaDelta = 25f * Math.Clamp(profile.RegionTextureUnifyStrength, 0f, 1f);
        var changed = false;
        for (var y = 0; y < buffer.Height; y++)
        {
            for (var x = 0; x < buffer.Width; x++)
            {
                var pixel = y * buffer.Width + x;
                var color = buffer.Pixels[pixel];
                if (((color >>> 24) & 0xFF) == 0) continue;
                var worldX = tileX * buffer.Width + x;
                var worldY = tileY * buffer.Height + y;
                var noise = ValueNoise(profile.Seed, worldX / (float)scale, worldY / (float)scale, region.TerrainId, 0x165667B1u) * 2f - 1f;
                var delta = noise * maxLumaDelta;
                if (Math.Abs(delta) < 0.01f) continue;
                var a = (color >>> 24) & 0xFF;
                var r = Math.Clamp((int)MathF.Round(((color >>> 16) & 0xFF) + delta), 0, 255);
                var g = Math.Clamp((int)MathF.Round(((color >>> 8) & 0xFF) + delta * 0.9f), 0, 255);
                var b = Math.Clamp((int)MathF.Round((color & 0xFF) + delta * 0.75f), 0, 255);
                buffer.Pixels[pixel] = FastBitmapBuffer.Pack(a, r, g, b);
                changed = true;
            }
        }

        if (changed)
        {
            buffer.CopyTo(tile);
        }
    }

    private static bool IsTileLikelyIsotropic(Bitmap tile)
    {
        using var buffer = FastBitmapBuffer.FromBitmap(tile);
        double horizontalEdge = 0;
        double verticalEdge = 0;
        var horizontalCount = 0;
        var verticalCount = 0;
        for (var y = 0; y < buffer.Height; y++)
        {
            for (var x = 0; x < buffer.Width; x++)
            {
                var lum = Luminance(buffer.Pixels[y * buffer.Width + x]);
                if (x + 1 < buffer.Width)
                {
                    horizontalEdge += Math.Abs(lum - Luminance(buffer.Pixels[y * buffer.Width + x + 1]));
                    horizontalCount++;
                }

                if (y + 1 < buffer.Height)
                {
                    verticalEdge += Math.Abs(lum - Luminance(buffer.Pixels[(y + 1) * buffer.Width + x]));
                    verticalCount++;
                }
            }
        }

        if (horizontalCount == 0 || verticalCount == 0) return false;
        var horizontal = horizontalEdge / horizontalCount;
        var vertical = verticalEdge / verticalCount;
        var ratio = Math.Abs(horizontal - vertical) / Math.Max(1.0, Math.Max(horizontal, vertical));
        return ratio <= 0.24 && BorderLuminanceRange(buffer) <= 20;
    }

    private static double BorderLuminanceRange(FastBitmapBuffer buffer)
    {
        var top = 0.0;
        var bottom = 0.0;
        var left = 0.0;
        var right = 0.0;
        for (var x = 0; x < buffer.Width; x++)
        {
            top += Luminance(buffer.Pixels[x]);
            bottom += Luminance(buffer.Pixels[(buffer.Height - 1) * buffer.Width + x]);
        }

        for (var y = 0; y < buffer.Height; y++)
        {
            left += Luminance(buffer.Pixels[y * buffer.Width]);
            right += Luminance(buffer.Pixels[y * buffer.Width + buffer.Width - 1]);
        }

        top /= buffer.Width;
        bottom /= buffer.Width;
        left /= buffer.Height;
        right /= buffer.Height;
        var min = Math.Min(Math.Min(top, bottom), Math.Min(left, right));
        var max = Math.Max(Math.Max(top, bottom), Math.Max(left, right));
        return max - min;
    }

    private static double Luminance(int argb)
        => 0.2126 * ((argb >>> 16) & 0xFF) +
           0.7152 * ((argb >>> 8) & 0xFF) +
           0.0722 * (argb & 0xFF);

    private static float ValueNoise(string? seed, float x, float y, byte terrainId, uint salt)
    {
        var x0 = (int)MathF.Floor(x);
        var y0 = (int)MathF.Floor(y);
        var tx = SmoothStep(x - x0);
        var ty = SmoothStep(y - y0);
        var n00 = StableNoise01(seed, x0, y0, terrainId, salt);
        var n10 = StableNoise01(seed, x0 + 1, y0, terrainId, salt);
        var n01 = StableNoise01(seed, x0, y0 + 1, terrainId, salt);
        var n11 = StableNoise01(seed, x0 + 1, y0 + 1, terrainId, salt);
        var top = n00 + (n10 - n00) * tx;
        var bottom = n01 + (n11 - n01) * tx;
        return top + (bottom - top) * ty;
    }

    private static float SmoothStep(float value)
    {
        value = Math.Clamp(value, 0f, 1f);
        return value * value * (3f - 2f * value);
    }

    private static float StableNoise01(string? seed, int x, int y, byte terrainId, uint salt)
    {
        unchecked
        {
            var hash = 2166136261u ^ salt;
            foreach (var ch in seed ?? string.Empty)
            {
                hash ^= ch;
                hash *= 16777619u;
            }

            hash ^= (uint)(x * 73856093);
            hash *= 16777619u;
            hash ^= (uint)(y * 19349663);
            hash *= 16777619u;
            hash ^= terrainId;
            hash *= 16777619u;
            return hash / (float)uint.MaxValue;
        }
    }

    private static int MapNeighborPixel(int value, int size, int direction, int feather)
    {
        if (direction > 0)
        {
            return Math.Clamp(value - (size - Math.Max(1, feather)), 0, size - 1);
        }

        if (direction < 0)
        {
            return Math.Clamp(value + (size - Math.Max(1, feather)), 0, size - 1);
        }

        return Math.Clamp(value, 0, size - 1);
    }

    private static Bitmap? GetRenderedSameRegionNeighbor(
        MapWorkbenchDraft draft,
        TerrainVisualSynthesisPlan plan,
        IReadOnlyDictionary<int, Bitmap> renderedTiles,
        int index,
        int dx,
        int dy)
    {
        var x = index % draft.GridWidth;
        var y = index / draft.GridWidth;
        var nx = x + dx;
        var ny = y + dy;
        if (nx < 0 || ny < 0 || nx >= draft.GridWidth || ny >= draft.GridHeight) return null;
        var neighborIndex = ny * draft.GridWidth + nx;
        if (!plan.RegionByCell.TryGetValue(index, out var region) ||
            !plan.RegionByCell.TryGetValue(neighborIndex, out var neighborRegion) ||
            region.RegionId != neighborRegion.RegionId)
        {
            return null;
        }

        return renderedTiles.GetValueOrDefault(neighborIndex);
    }

    private static bool HasTransparency(Bitmap bitmap)
    {
        for (var y = 0; y < bitmap.Height; y += Math.Max(1, bitmap.Height / 8))
        {
            for (var x = 0; x < bitmap.Width; x += Math.Max(1, bitmap.Width / 8))
            {
                if (bitmap.GetPixel(x, y).A < 250)
                {
                    return true;
                }
            }
        }

        return false;
    }

    private CurrentMapStyleProfile BuildStyleProfile(MapWorkbenchDraft draft, TerrainVisualProfile profile)
    {
        if (!profile.UseCurrentMapSamples)
        {
            return new CurrentMapStyleProfile
            {
                SourceMapPath = draft.BaseLayerPath ?? string.Empty,
                SampleRoot = profile.StyleSampleRoot,
                GridWidth = draft.GridWidth,
                GridHeight = draft.GridHeight,
                TileSize = draft.TileSize <= 0 ? MapResourceItem.MapTilePixelSize : draft.TileSize
            };
        }

        return _styleProfileService.BuildProfile(
            draft,
            profile.StyleSampleRoot,
            profile.AutoExtractCurrentMapSamples && !string.IsNullOrWhiteSpace(profile.StyleSampleRoot));
    }

    private static TerrainVisualProfile NormalizeProfile(TerrainVisualProfile? profile)
    {
        profile ??= new TerrainVisualProfile();
        profile.EdgeFeatherRadius = Math.Clamp(profile.EdgeFeatherRadius <= 0 ? 8 : profile.EdgeFeatherRadius, 0, MapResourceItem.MapTilePixelSize / 2);
        profile.BlendStrength = Math.Clamp(profile.BlendStrength <= 0 ? 2 : profile.BlendStrength, 0, 3);
        profile.ColorAlignmentStrength = Math.Clamp(profile.ColorAlignmentStrength, 0f, 1f);
        profile.TextureNoiseStrength = Math.Clamp(profile.TextureNoiseStrength, 0f, 1f);
        profile.Seed = string.IsNullOrWhiteSpace(profile.Seed) ? "default" : profile.Seed.Trim();
        profile.StyleSampleRoot = profile.StyleSampleRoot?.Trim() ?? string.Empty;
        profile.MaterialOverrides ??= new List<TerrainVisualMaterialOverride>();
        profile.StyleContextRadiusCells = Math.Clamp(profile.StyleContextRadiusCells <= 0 ? 3 : profile.StyleContextRadiusCells, 1, 8);
        profile.BlendContextRadiusCells = Math.Clamp(profile.BlendContextRadiusCells <= 0 ? 2 : profile.BlendContextRadiusCells, 1, 4);
        profile.BoundaryFeatherPixels = Math.Clamp(profile.BoundaryFeatherPixels <= 0 ? profile.EdgeFeatherRadius : profile.BoundaryFeatherPixels, 1, MapResourceItem.MapTilePixelSize);
        profile.BoundaryJitterPixels = Math.Clamp(profile.BoundaryJitterPixels, 0, MapResourceItem.MapTilePixelSize / 2);
        profile.BoundaryNoiseScale = Math.Clamp(profile.BoundaryNoiseScale <= 0 ? 12 : profile.BoundaryNoiseScale, 1, MapResourceItem.MapTilePixelSize);
        profile.OverlapSeamPixels = Math.Clamp(profile.OverlapSeamPixels <= 0 ? 8 : profile.OverlapSeamPixels, 0, MapResourceItem.MapTilePixelSize / 2);
        profile.LocalColorTransferStrength = Math.Clamp(profile.LocalColorTransferStrength, 0f, 1f);
        profile.CenterMinWeight = Math.Clamp(profile.CenterMinWeight <= 0f ? 0.35f : profile.CenterMinWeight, 0f, 0.95f);
        profile.NeighborMaxWeight = Math.Clamp(profile.NeighborMaxWeight <= 0f ? 0.18f + profile.BlendStrength * 0.12f : profile.NeighborMaxWeight, 0f, 0.9f);
        profile.StructureAlphaPreserveThreshold = Math.Clamp(profile.StructureAlphaPreserveThreshold <= 0 ? 48 : profile.StructureAlphaPreserveThreshold, 1, 255);
        profile.InteriorSeamPixels = Math.Clamp(profile.InteriorSeamPixels <= 0 ? 8 : profile.InteriorSeamPixels, 1, MapResourceItem.MapTilePixelSize / 2);
        profile.InteriorSeamJitterPixels = Math.Clamp(profile.InteriorSeamJitterPixels, 0, Math.Max(0, profile.InteriorSeamPixels - 1));
        profile.InteriorSecondaryBlendStrength = Math.Clamp(profile.InteriorSecondaryBlendStrength, 0f, 0.35f);
        profile.RegionTextureUnifyStrength = Math.Clamp(profile.RegionTextureUnifyStrength, 0f, 1f);
        profile.RegionNoiseScalePixels = Math.Clamp(profile.RegionNoiseScalePixels <= 0 ? 96 : profile.RegionNoiseScalePixels, MapResourceItem.MapTilePixelSize, MapResourceItem.MapTilePixelSize * 8);
        profile.MaxDegreeOfParallelism = Math.Max(0, profile.MaxDegreeOfParallelism);
        profile.TileCacheMaxEntries = Math.Clamp(profile.TileCacheMaxEntries <= 0 ? 4096 : profile.TileCacheMaxEntries, 256, 65536);
        profile.BuildingGroundContextRadiusCells = Math.Clamp(profile.BuildingGroundContextRadiusCells, 0, 4);
        profile.TransitionFieldFeatherPixels = Math.Clamp(
            profile.TransitionFieldFeatherPixels <= 0 ? profile.BoundaryFeatherPixels : profile.TransitionFieldFeatherPixels,
            1,
            MapResourceItem.MapTilePixelSize);
        profile.TransitionFieldJitterPixels = Math.Clamp(profile.TransitionFieldJitterPixels, 0, MapResourceItem.MapTilePixelSize / 2);
        profile.QuiltingOverlapPixels = Math.Clamp(
            profile.QuiltingOverlapPixels <= 0 ? profile.OverlapSeamPixels : profile.QuiltingOverlapPixels,
            0,
            MapResourceItem.MapTilePixelSize / 2);
        profile.QuiltingCandidateCount = Math.Clamp(profile.QuiltingCandidateCount <= 0 ? 8 : profile.QuiltingCandidateCount, 1, 32);
        profile.MacroNoiseStrength = Math.Clamp(profile.MacroNoiseStrength, 0f, 0.5f);
        profile.ObjectContactShadowStrength = Math.Clamp(profile.ObjectContactShadowStrength, 0f, 1f);
        profile.ObjectContactBlendPixels = Math.Clamp(profile.ObjectContactBlendPixels <= 0 ? 5 : profile.ObjectContactBlendPixels, 1, 16);
        profile.ObjectGroundContextRadiusCells = Math.Clamp(profile.ObjectGroundContextRadiusCells <= 0 ? 1 : profile.ObjectGroundContextRadiusCells, 0, 4);
        profile.ObjectGroundInferenceRadiusCells = Math.Clamp(profile.ObjectGroundInferenceRadiusCells <= 0 ? 3 : profile.ObjectGroundInferenceRadiusCells, 1, 8);
        profile.AlphaRepairBlackThreshold = Math.Clamp(profile.AlphaRepairBlackThreshold <= 0 ? 24 : profile.AlphaRepairBlackThreshold, 1, 96);
        profile.MinPureSamplesPerTerrain = Math.Clamp(profile.MinPureSamplesPerTerrain <= 0 ? 4 : profile.MinPureSamplesPerTerrain, 1, 32);
        return profile;
    }

    private static void ValidateDraft(MapWorkbenchDraft draft)
    {
        if (draft.GridWidth <= 0 || draft.GridHeight <= 0)
        {
            throw new InvalidOperationException("Map draft grid size is invalid.");
        }

        if (draft.TerrainCells.Length != draft.CellCount)
        {
            throw new InvalidOperationException("Map draft terrain layer size does not match the grid.");
        }
    }

    private static IReadOnlyCollection<int>? BuildEffectiveRedrawIndexes(
        MapWorkbenchDraft draft,
        TerrainVisualProfile profile,
        IReadOnlyCollection<int>? requestedIndexes)
    {
        if (!profile.RegenerateGroundUnderBuildingOverlays || draft.BuildingOverlayCells.Count == 0)
        {
            return requestedIndexes;
        }

        var result = requestedIndexes == null
            ? new HashSet<int>()
            : requestedIndexes.Where(index => (uint)index < (uint)draft.CellCount).ToHashSet();
        var radius = Math.Clamp(Math.Max(profile.BuildingGroundContextRadiusCells, profile.ObjectGroundContextRadiusCells), 0, 4);
        foreach (var cell in draft.BuildingOverlayCells)
        {
            if ((uint)cell.Index >= (uint)draft.CellCount) continue;
            var x = cell.Index % draft.GridWidth;
            var y = cell.Index / draft.GridWidth;
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

    private static IReadOnlyCollection<int>? MergeForcedGroundRedrawIndexes(
        MapWorkbenchDraft draft,
        TerrainVisualProfile profile,
        bool hasValidBaseImage,
        IReadOnlyCollection<int>? requestedIndexes,
        IReadOnlySet<int> forcedIndexes)
    {
        if (forcedIndexes.Count == 0)
        {
            return requestedIndexes;
        }

        if (requestedIndexes != null)
        {
            var result = requestedIndexes.Where(index => (uint)index < (uint)draft.CellCount).ToHashSet();
            result.UnionWith(forcedIndexes.Where(index => (uint)index < (uint)draft.CellCount));
            return result;
        }

        var redrawAll = !hasValidBaseImage ||
                        !profile.RedrawChangedCellsOnly ||
                        draft.OriginalTerrainCells.Length != draft.CellCount;
        if (redrawAll)
        {
            return null;
        }

        var merged = new HashSet<int>();
        for (var i = 0; i < draft.CellCount; i++)
        {
            if (draft.TerrainCells[i] != draft.OriginalTerrainCells[i])
            {
                merged.Add(i);
            }
        }

        merged.UnionWith(forcedIndexes.Where(index => (uint)index < (uint)draft.CellCount));
        return merged;
    }

    private static MapWorkbenchDraft CreateVisualGroundDraft(MapWorkbenchDraft draft, byte[] visualGroundTerrainCells)
        => new()
        {
            DraftId = draft.DraftId,
            BoundMapId = draft.BoundMapId,
            GridWidth = draft.GridWidth,
            GridHeight = draft.GridHeight,
            TileSize = draft.TileSize,
            BaseLayerPath = draft.BaseLayerPath,
            MaterialRoot = draft.MaterialRoot,
            TerrainMaterialPlan = draft.TerrainMaterialPlan,
            MapCellOverrides = draft.MapCellOverrides,
            TerrainBaseCells = draft.TerrainBaseCells,
            GeneratedMapCells = draft.GeneratedMapCells,
            BuildingOverlayCells = draft.BuildingOverlayCells,
            SceneryOverlayCells = draft.SceneryOverlayCells,
            SceneryOverlays = draft.SceneryOverlays,
            OriginalTerrainCells = draft.OriginalTerrainCells.Length == draft.CellCount
                ? draft.OriginalTerrainCells.ToArray()
                : visualGroundTerrainCells.ToArray(),
            TerrainCells = visualGroundTerrainCells.ToArray(),
            GenerationMode = draft.GenerationMode,
            TerrainVisualProfile = draft.TerrainVisualProfile,
            AutoGenerateMapFromTerrain = draft.AutoGenerateMapFromTerrain,
            BeautifyGeneratedMap = draft.BeautifyGeneratedMap,
            BeautifyStrength = draft.BeautifyStrength,
            FeatherRadius = draft.FeatherRadius,
            BeautifyFilterProfile = draft.BeautifyFilterProfile,
            CustomBeautifyFilter = draft.CustomBeautifyFilter,
            CreatedAtText = draft.CreatedAtText,
            UpdatedAtText = draft.UpdatedAtText
        };

    private static ObjectGroundInpaintPlan CreateEmptyObjectGroundInpaintPlan(MapWorkbenchDraft draft)
    {
        var visualGroundTerrainCells = new byte[Math.Max(0, draft.CellCount)];
        if (draft.TerrainCells.Length == draft.CellCount)
        {
            Array.Copy(draft.TerrainCells, visualGroundTerrainCells, draft.CellCount);
        }

        return new ObjectGroundInpaintPlan(
            new HashSet<int>(),
            new HashSet<int>(),
            new HashSet<int>(),
            new Dictionary<int, byte>(),
            visualGroundTerrainCells,
            terrainObjectOverlayCellCount: 0,
            fallbackCellCount: 0);
    }

    private static int CountBuildingGroundRedrawCells(MapWorkbenchDraft draft, IReadOnlyCollection<int>? requestedIndexes)
    {
        if (requestedIndexes == null || draft.BuildingOverlayCells.Count == 0) return 0;
        var requested = requestedIndexes.ToHashSet();
        return draft.BuildingOverlayCells
            .Select(cell => cell.Index)
            .Where(index => (uint)index < (uint)draft.CellCount)
            .Distinct()
            .Count(requested.Contains);
    }

    private void PopulateObjectAlphaRepairDiagnostics(
        MapWorkbenchDraft draft,
        IReadOnlyList<MaterialAsset> materials,
        TerrainVisualProfile profile,
        TerrainVisualSynthesisDiagnostics diagnostics)
    {
        if (draft.BuildingOverlayCells.Count == 0 || materials.Count == 0)
        {
            return;
        }

        var materialLookup = BuildMaterialLookup(draft, materials);
        foreach (var cell in draft.BuildingOverlayCells)
        {
            if (!materialLookup.TryGetValue(cell.MaterialRelativePath, out var asset) ||
                !asset.AssetType.Equals(MaterialAssetTypes.Building, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var source = GetCachedImage(asset.FilePath);
            if (source == null) continue;
            var tileSize = draft.TileSize <= 0 ? MapResourceItem.MapTilePixelSize : draft.TileSize;
            using var tile = new Bitmap(tileSize, tileSize, PixelFormat.Format32bppArgb);
            using (var tileGraphics = CreateGraphics(tile))
            {
                tileGraphics.DrawImage(source, new Rectangle(0, 0, tileSize, tileSize), BuildSourceRect(asset, source), GraphicsUnit.Pixel);
            }

            using var repaired = _objectAlphaRepairService.Repair(tile, profile);
            if (repaired.Repaired)
            {
                diagnostics.AlphaRepairedObjectCount++;
                diagnostics.AlphaRepairedPixelCount += repaired.RepairedPixelCount;
            }

            diagnostics.BlackBackgroundRejectedPixelCount += repaired.RejectedPixelCount;
        }
    }

    private static Dictionary<string, MaterialAsset> BuildMaterialLookup(MapWorkbenchDraft draft, IReadOnlyList<MaterialAsset> materials)
    {
        var result = new Dictionary<string, MaterialAsset>(StringComparer.OrdinalIgnoreCase);
        foreach (var asset in materials)
        {
            var relative = MapDraftService.GetMaterialRelativePath(draft.MaterialRoot, asset.FilePath);
            result[relative] = asset;
            result[asset.FilePath] = asset;
            result[asset.FileName] = asset;
        }

        return result;
    }

    private Bitmap RenderBaseTerrainTileForGlobalField(
        MapWorkbenchDraft draft,
        TerrainVisualProfile profile,
        TerrainVisualSynthesisPlan plan,
        Bitmap? baseImage,
        int index,
        TerrainVisualSynthesisDiagnostics diagnostics)
    {
        var region = plan.RegionByCell.GetValueOrDefault(index);
        var tile = RenderTerrainTile(draft, profile, plan, baseImage, index, region, diagnostics, countDiagnostics: true);
        if (!plan.RegionTextureByCell.ContainsKey(index))
        {
            ApplyRegionTextureUnifier(tile, draft, profile, index, region);
        }

        return tile;
    }

    private void ApplyGlobalTransitionField(
        Bitmap output,
        MapWorkbenchDraft draft,
        TerrainVisualProfile profile,
        TerrainVisualSynthesisPlan plan,
        TerrainVisualSynthesisDiagnostics diagnostics)
    {
        var boundaryWatch = Stopwatch.StartNew();
        var field = _transitionFieldBuilder.Build(draft, profile, plan);
        if (field.Pixels.Count == 0)
        {
            boundaryWatch.Stop();
            diagnostics.BoundaryBlendMs += boundaryWatch.ElapsedMilliseconds;
            return;
        }

        using var source = FastBitmapBuffer.FromBitmap(output);
        var target = new FastBitmapBuffer(source.Width, source.Height, (int[])source.Pixels.Clone());
        foreach (var pixel in field.Pixels)
        {
            if ((uint)pixel.Offset >= (uint)target.Pixels.Length ||
                (uint)pixel.FirstSampleOffset >= (uint)source.Pixels.Length ||
                (uint)pixel.SecondSampleOffset >= (uint)source.Pixels.Length)
            {
                continue;
            }

            target.Pixels[pixel.Offset] = FastBitmapBuffer.LerpArgb(
                source.Pixels[pixel.FirstSampleOffset],
                source.Pixels[pixel.SecondSampleOffset],
                pixel.SecondWeight);
        }

        target.CopyTo(output);
        boundaryWatch.Stop();
        diagnostics.BoundaryBlendMs += boundaryWatch.ElapsedMilliseconds;
        diagnostics.BoundaryMaskPixelCount += field.Pixels.Count;
        diagnostics.TransitionFieldPixelCount += field.Pixels.Count;
        diagnostics.MixedTerrainCellCount = Math.Max(
            diagnostics.MixedTerrainCellCount,
            plan.ExpandedRedrawIndexes.Count(index => HasDifferentNaturalNeighbor(draft, index)));
        diagnostics.BoundaryBlendCount = diagnostics.MixedTerrainCellCount;
        diagnostics.MultiTerrainJunctionPixels += field.MultiTerrainJunctionPixels;
        diagnostics.RepeatedBoundaryBlendPreventedCount += field.RepeatedBoundaryBlendPreventedCount;
    }

    private static bool HasDifferentNaturalNeighbor(MapWorkbenchDraft draft, int index)
    {
        if ((uint)index >= (uint)draft.CellCount) return false;
        var terrain = draft.TerrainCells[index];
        if (!CanParticipateInGlobalTransition(terrain))
        {
            return false;
        }

        var x = index % draft.GridWidth;
        var y = index / draft.GridWidth;
        return IsDifferentNaturalTerrain(draft, x - 1, y, terrain) ||
               IsDifferentNaturalTerrain(draft, x + 1, y, terrain) ||
               IsDifferentNaturalTerrain(draft, x, y - 1, terrain) ||
               IsDifferentNaturalTerrain(draft, x, y + 1, terrain);
    }

    private static bool IsDifferentNaturalTerrain(MapWorkbenchDraft draft, int x, int y, byte terrain)
    {
        if (x < 0 || y < 0 || x >= draft.GridWidth || y >= draft.GridHeight) return false;
        var other = draft.TerrainCells[y * draft.GridWidth + x];
        if (other == terrain) return false;
        return CanParticipateInGlobalTransition(other);
    }

    private static bool CanParticipateInGlobalTransition(byte terrain)
    {
        var kind = TerrainVisualSurfaceClassifier.Classify(terrain);
        return kind is not TerrainVisualSurfaceKind.StructureTerrain
            and not TerrainVisualSurfaceKind.BuildingOverlay;
    }

    private Bitmap? GetValidBaseImage(MapWorkbenchDraft draft, int expectedWidth, int expectedHeight)
    {
        var image = GetCachedImage(draft.BaseLayerPath);
        return image != null && image.Width == expectedWidth && image.Height == expectedHeight ? image : null;
    }

    private static IReadOnlyList<MaterialAsset> BuildStyleSampleAssets(CurrentMapStyleProfile styleProfile)
    {
        var result = new List<MaterialAsset>();
        foreach (var terrain in styleProfile.Terrains)
        {
            var preferredSamples = terrain.PureSamples.Count > 0
                ? terrain.PureSamples
                : terrain.BoundarySamples.Count > 0
                    ? terrain.BoundarySamples
                    : terrain.Samples;
            foreach (var sample in preferredSamples.Where(sample => !sample.IsContaminated && !string.IsNullOrWhiteSpace(sample.FilePath) && File.Exists(sample.FilePath)))
            {
                result.Add(new MaterialAsset
                {
                    Category = "CurrentMapStyle",
                    FileName = Path.GetFileName(sample.FilePath),
                    HexTag = terrain.TerrainId.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    Description = terrain.TerrainName,
                    AssetType = MaterialAssetTypes.Terrain,
                    TerrainId = terrain.TerrainId,
                    TerrainName = terrain.TerrainName,
                    GroupKey = $"CurrentMapStyle:{terrain.TerrainId}",
                    AutoTileSetKey = $"CurrentMapStyle:{terrain.TerrainId}",
                    VariantIndex = result.Count,
                    AutoTileRole = MaterialAutoTileRoles.Default,
                    AutoTileMask = MaterialAutoTileMasks.None,
                    AutoTileMode = MaterialAutoTileModes.Default,
                    AutoTilePriority = 0,
                    SourceX = 0,
                    SourceY = 0,
                    SourceWidth = MapResourceItem.MapTilePixelSize,
                    SourceHeight = MapResourceItem.MapTilePixelSize,
                    Width = MapResourceItem.MapTilePixelSize,
                    Height = MapResourceItem.MapTilePixelSize,
                    SizeBytes = new FileInfo(sample.FilePath).Length,
                    FilePath = sample.FilePath
                });
            }
        }

        return result;
    }

    private double ScoreCandidateGroup(IReadOnlyList<MaterialAsset> group, TileVisualStats target)
    {
        var representative = TerrainAutoTileResolver.SelectAutoTileAssetForMask(group, MaterialAutoTileMasks.None) ?? group[0];
        var stats = GetMaterialStats(representative);
        return TileVisualStatsCalculator.Distance(stats, target);
    }

    private TileVisualStats GetMaterialStats(MaterialAsset material)
    {
        var key = string.Join("|",
            material.FilePath,
            material.SourceX.ToString(System.Globalization.CultureInfo.InvariantCulture),
            material.SourceY.ToString(System.Globalization.CultureInfo.InvariantCulture),
            material.SourceWidth.ToString(System.Globalization.CultureInfo.InvariantCulture),
            material.SourceHeight.ToString(System.Globalization.CultureInfo.InvariantCulture));
        if (_statsCache.TryGetValue(key, out var stats)) return stats;

        var source = GetCachedImage(material.FilePath);
        if (source == null) return TileVisualStats.Empty;

        var rect = BuildSourceRect(material, source);
        using var tile = new Bitmap(MapResourceItem.MapTilePixelSize, MapResourceItem.MapTilePixelSize, PixelFormat.Format32bppArgb);
        using (var g = CreateGraphics(tile))
        {
            g.DrawImage(source, new Rectangle(0, 0, tile.Width, tile.Height), rect, GraphicsUnit.Pixel);
        }

        stats = TileVisualStatsCalculator.Calculate(tile);
        _statsCache[key] = stats;
        return stats;
    }

    private static void ApplyStyleAdjustment(
        Bitmap tile,
        TileVisualStats materialStats,
        TileVisualStats targetStats,
        TerrainVisualProfile profile,
        string seed,
        int index,
        byte terrainId)
    {
        var strength = profile.ColorAlignmentStrength;
        if (strength <= 0f && profile.TextureNoiseStrength <= 0f) return;

        var deltaR = (targetStats.AverageR - materialStats.AverageR) * strength;
        var deltaG = (targetStats.AverageG - materialStats.AverageG) * strength;
        var deltaB = (targetStats.AverageB - materialStats.AverageB) * strength;
        var contrastRatio = materialStats.Contrast <= 0.1f
            ? 1f
            : 1f + ((targetStats.Contrast - materialStats.Contrast) / Math.Max(32f, materialStats.Contrast)) * strength * 0.25f;
        contrastRatio = Math.Clamp(contrastRatio, 0.82f, 1.18f);
        var noiseMagnitude = profile.TextureNoiseStrength * 6f;

        using var buffer = FastBitmapBuffer.FromBitmap(tile);
        var changed = false;
        for (var y = 0; y < buffer.Height; y++)
        {
            for (var x = 0; x < buffer.Width; x++)
            {
                var pixel = y * buffer.Width + x;
                var color = buffer.Pixels[pixel];
                var alpha = (color >>> 24) & 0xFF;
                if (alpha == 0) continue;
                var noise = noiseMagnitude <= 0
                    ? 0
                    : (int)(StableHash(seed, index + x * 17 + y * 31, terrainId, 0x85EBCA77u) % 11) - 5;
                var r = AdjustChannel((color >>> 16) & 0xFF, materialStats.AverageR, deltaR, contrastRatio, noise);
                var g = AdjustChannel((color >>> 8) & 0xFF, materialStats.AverageG, deltaG, contrastRatio, noise);
                var b = AdjustChannel(color & 0xFF, materialStats.AverageB, deltaB, contrastRatio, noise);
                buffer.Pixels[pixel] = FastBitmapBuffer.Pack(alpha, r, g, b);
                changed = true;
            }
        }

        if (changed)
        {
            buffer.CopyTo(tile);
        }
    }

    private static int AdjustChannel(int value, float average, float delta, float contrastRatio, int noise)
        => Math.Clamp((int)MathF.Round((value - average) * contrastRatio + average + delta + noise), 0, 255);

    private Bitmap? GetCachedImage(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return null;
        var fullPath = Path.GetFullPath(path);
        var info = new FileInfo(fullPath);
        if (_imageCache.TryGetValue(fullPath, out var cached) &&
            cached.LastWriteUtc == info.LastWriteTimeUtc &&
            cached.Length == info.Length)
        {
            return cached.Bitmap;
        }

        using var source = Image.FromFile(fullPath);
        var bitmap = new Bitmap(source);
        if (_imageCache.TryGetValue(fullPath, out cached))
        {
            cached.Bitmap.Dispose();
        }

        _imageCache[fullPath] = new CachedImage(info.LastWriteTimeUtc, info.Length, bitmap);
        return bitmap;
    }

    private static Rectangle BuildSourceRect(MaterialAsset asset, Image image)
    {
        var x = Math.Clamp(asset.SourceX, 0, Math.Max(0, image.Width - 1));
        var y = Math.Clamp(asset.SourceY, 0, Math.Max(0, image.Height - 1));
        var width = asset.SourceWidth <= 0 ? Math.Min(MapResourceItem.MapTilePixelSize, image.Width - x) : asset.SourceWidth;
        var height = asset.SourceHeight <= 0 ? Math.Min(MapResourceItem.MapTilePixelSize, image.Height - y) : asset.SourceHeight;
        width = Math.Clamp(width, 1, Math.Max(1, image.Width - x));
        height = Math.Clamp(height, 1, Math.Max(1, image.Height - y));
        return new Rectangle(x, y, width, height);
    }

    private static Rectangle GetTileRectangle(MapWorkbenchDraft draft, int index)
    {
        var tileSize = draft.TileSize <= 0 ? MapResourceItem.MapTilePixelSize : draft.TileSize;
        var x = index % draft.GridWidth;
        var y = index / draft.GridWidth;
        return new Rectangle(x * tileSize, y * tileSize, tileSize, tileSize);
    }

    private static Graphics CreateGraphics(Bitmap bitmap)
    {
        var g = Graphics.FromImage(bitmap);
        g.SmoothingMode = SmoothingMode.None;
        g.InterpolationMode = InterpolationMode.NearestNeighbor;
        g.PixelOffsetMode = PixelOffsetMode.Half;
        return g;
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

    private static bool TryGetCachedSynthesis(string cacheKey, out TerrainVisualSynthesisResult result)
    {
        lock (SynthesisCacheLock)
        {
            if (_lastSynthesisResult != null && _lastSynthesisResult.CacheKey.Equals(cacheKey, StringComparison.Ordinal))
            {
                result = new TerrainVisualSynthesisResult
                {
                    Bitmap = new Bitmap(_lastSynthesisResult.Bitmap),
                    Diagnostics = CloneDiagnostics(_lastSynthesisResult.Diagnostics)
                };
                return true;
            }
        }

        result = null!;
        return false;
    }

    private static void StoreCachedSynthesis(string cacheKey, Bitmap bitmap, TerrainVisualSynthesisDiagnostics diagnostics)
    {
        lock (SynthesisCacheLock)
        {
            _lastSynthesisResult?.Bitmap.Dispose();
            _lastSynthesisResult = new CachedSynthesisResult(
                cacheKey,
                new Bitmap(bitmap),
                CloneDiagnostics(diagnostics));
        }
    }

    private static string BuildSynthesisCacheKey(
        MapWorkbenchDraft draft,
        TerrainVisualProfile profile,
        IReadOnlyList<MaterialAsset> materials,
        CurrentMapStyleProfile styleProfile,
        IReadOnlyCollection<int>? redrawIndexes)
    {
        var builder = new System.Text.StringBuilder();
        builder.Append(draft.DraftId).Append('|')
            .Append(draft.BoundMapId).Append('|')
            .Append(draft.GridWidth).Append('x').Append(draft.GridHeight).Append('|')
            .Append(draft.TileSize).Append('|')
            .Append(NormalizeFileKey(draft.BaseLayerPath)).Append('|')
            .Append(string.Join(",", draft.TerrainCells.Select(item => item.ToString(System.Globalization.CultureInfo.InvariantCulture)))).Append('|')
            .Append(string.Join(",", draft.OriginalTerrainCells.Select(item => item.ToString(System.Globalization.CultureInfo.InvariantCulture)))).Append('|')
            .Append(BuildCellOverrideCacheKey(draft.BuildingOverlayCells)).Append('|')
            .Append(BuildCellOverrideCacheKey(draft.SceneryOverlayCells)).Append('|')
            .Append(BuildSceneryOverlayCacheKey(draft.SceneryOverlays)).Append('|')
            .Append(redrawIndexes == null ? "*" : string.Join(",", redrawIndexes.OrderBy(index => index))).Append('|')
            .Append(styleProfile.SampleCount).Append('|')
            .Append(styleProfile.RejectedSampleCount).Append('|')
            .Append(NormalizePathKey(styleProfile.SampleRoot)).Append('|')
            .Append(ProfileCacheKey(profile)).Append('|');
        foreach (var material in materials.OrderBy(item => item.FilePath, StringComparer.OrdinalIgnoreCase)
                     .ThenBy(item => item.SourceX)
                     .ThenBy(item => item.SourceY)
                     .ThenBy(item => item.VariantIndex))
        {
            builder.Append(NormalizeFileKey(material.FilePath)).Append(':')
                .Append(material.TerrainId?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty).Append(':')
                .Append(material.GroupKey).Append(':')
                .Append(material.AutoTileSetKey).Append(':')
                .Append(material.AutoTileRole).Append(':')
                .Append(material.AutoTileMask?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty).Append(':')
                .Append(material.AutoTileMode).Append(':')
                .Append(material.SourceX).Append(',').Append(material.SourceY).Append(',').Append(material.SourceWidth).Append(',').Append(material.SourceHeight).Append(';');
        }

        return builder.ToString();
    }

    private static string BuildCellOverrideCacheKey(IEnumerable<MapCellOverride> cells)
        => string.Join(";",
            cells.OrderBy(cell => cell.Index)
                .ThenBy(cell => cell.MaterialRelativePath, StringComparer.OrdinalIgnoreCase)
                .Select(cell => string.Join(",",
                    cell.Index.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    cell.MaterialRelativePath,
                    cell.MaterialCategory,
                    cell.Source)));

    private static string BuildSceneryOverlayCacheKey(IEnumerable<MapSceneryOverlay> overlays)
        => string.Join(";",
            overlays.OrderBy(overlay => overlay.ZOrder)
                .ThenBy(overlay => overlay.X)
                .ThenBy(overlay => overlay.Y)
                .Select(overlay => string.Join(",",
                    overlay.MaterialRelativePath,
                    overlay.MaterialCategory,
                    overlay.X.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    overlay.Y.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    overlay.Width.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    overlay.Height.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    overlay.RotationDegrees.ToString("0.####", System.Globalization.CultureInfo.InvariantCulture),
                    overlay.ZOrder.ToString(System.Globalization.CultureInfo.InvariantCulture))));

    private static string ProfileCacheKey(TerrainVisualProfile profile)
        => string.Join(",",
            profile.Seed,
            profile.UseCurrentMapSamples,
            profile.AutoExtractCurrentMapSamples,
            profile.RedrawChangedCellsOnly,
            profile.EdgeFeatherRadius,
            profile.BlendStrength,
            profile.ColorAlignmentStrength.ToString("0.####", System.Globalization.CultureInfo.InvariantCulture),
            profile.TextureNoiseStrength.ToString("0.####", System.Globalization.CultureInfo.InvariantCulture),
            profile.UseRegionConsistentMaterial,
            profile.UseDirectionalBoundaryBlend,
            profile.StyleContextRadiusCells,
            profile.BlendContextRadiusCells,
            profile.BoundaryFeatherPixels,
            profile.BoundaryJitterPixels,
            profile.BoundaryNoiseScale,
            profile.OverlapSeamPixels,
            profile.LocalColorTransferStrength.ToString("0.####", System.Globalization.CultureInfo.InvariantCulture),
            profile.CenterMinWeight.ToString("0.####", System.Globalization.CultureInfo.InvariantCulture),
            profile.NeighborMaxWeight.ToString("0.####", System.Globalization.CultureInfo.InvariantCulture),
            profile.StructureAlphaPreserveThreshold,
            profile.UseInteriorTextureSynthesis,
            profile.EnableNaturalTileTransforms,
            profile.UseInteriorSeamBlend,
            profile.InteriorSeamPixels,
            profile.InteriorSeamJitterPixels,
            profile.InteriorSecondaryBlendStrength.ToString("0.####", System.Globalization.CultureInfo.InvariantCulture),
            profile.RegionTextureUnifyStrength.ToString("0.####", System.Globalization.CultureInfo.InvariantCulture),
            profile.RegionNoiseScalePixels,
            profile.AllowNinetyDegreeNaturalRotation,
            profile.RegenerateGroundUnderBuildingOverlays,
            profile.BuildingGroundContextRadiusCells,
            profile.UseGlobalBuildingStyle,
            profile.UseGlobalTransitionField,
            profile.UseRegionTextureCanvas,
            profile.UseObjectContactBlend,
            profile.TransitionFieldFeatherPixels,
            profile.TransitionFieldJitterPixels,
            profile.QuiltingOverlapPixels,
            profile.QuiltingCandidateCount,
            profile.MacroNoiseStrength.ToString("0.####", System.Globalization.CultureInfo.InvariantCulture),
            profile.ObjectContactShadowStrength.ToString("0.####", System.Globalization.CultureInfo.InvariantCulture),
            profile.ObjectContactBlendPixels,
            profile.ObjectGroundContextRadiusCells,
            profile.UseObjectGroundInpaint,
            profile.ObjectGroundInferenceRadiusCells,
            profile.GroundInpaintIncludesTerrainObjects,
            profile.AlphaRepairBlackThreshold,
            profile.AlphaRepairEdgeConnectivity,
            profile.MinPureSamplesPerTerrain,
            profile.PreferCurrentMapSamplesStrictly,
            profile.IgnoreBasePixelsUnderObjects,
            string.Join(";", (profile.MaterialOverrides ?? new List<TerrainVisualMaterialOverride>())
                .OrderBy(item => item.TerrainId)
                .Select(item => item.TerrainId.ToString(System.Globalization.CultureInfo.InvariantCulture) + "=" + item.MaterialRelativePath)));

    private static string NormalizePathKey(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return string.Empty;
        try
        {
            return Path.GetFullPath(path).ToUpperInvariant();
        }
        catch
        {
            return path.Trim().ToUpperInvariant();
        }
    }

    private static string NormalizeFileKey(string path)
    {
        var normalized = NormalizePathKey(path);
        if (string.IsNullOrWhiteSpace(normalized)) return string.Empty;
        try
        {
            var info = new FileInfo(path);
            return info.Exists
                ? $"{normalized}:{info.LastWriteTimeUtc.Ticks.ToString(System.Globalization.CultureInfo.InvariantCulture)}:{info.Length.ToString(System.Globalization.CultureInfo.InvariantCulture)}"
                : normalized + ":missing";
        }
        catch
        {
            return normalized;
        }
    }

    private static TerrainVisualSynthesisDiagnostics CloneDiagnostics(TerrainVisualSynthesisDiagnostics source)
        => new()
        {
            UsedCurrentMapStyle = source.UsedCurrentMapStyle,
            StyleSampleCount = source.StyleSampleCount,
            RedrawnCellCount = source.RedrawnCellCount,
            PreservedCellCount = source.PreservedCellCount,
            MaterialMatchedCellCount = source.MaterialMatchedCellCount,
            FallbackCellCount = source.FallbackCellCount,
            BoundaryBlendCount = source.BoundaryBlendCount,
            RegionCount = source.RegionCount,
            RegionLockedMaterialCount = source.RegionLockedMaterialCount,
            ExpandedRedrawCellCount = source.ExpandedRedrawCellCount,
            MixedTerrainCellCount = source.MixedTerrainCellCount,
            BoundaryMaskPixelCount = source.BoundaryMaskPixelCount,
            LocalColorTransferPixelCount = source.LocalColorTransferPixelCount,
            FallbackGroupCount = source.FallbackGroupCount,
            MissingTransitionMaskCount = source.MissingTransitionMaskCount,
            NaturalizedRegionCount = source.NaturalizedRegionCount,
            InteriorSeamBlendPixelCount = source.InteriorSeamBlendPixelCount,
            SecondaryPatchBlendPixelCount = source.SecondaryPatchBlendPixelCount,
            TileTransformCount = source.TileTransformCount,
            StructureTransformSkippedCount = source.StructureTransformSkippedCount,
            RepeatedPatchPenaltyCount = source.RepeatedPatchPenaltyCount,
            FastPipelineEnabled = source.FastPipelineEnabled,
            TotalMs = source.TotalMs,
            PlanMs = source.PlanMs,
            TileRenderMs = source.TileRenderMs,
            InteriorBlendMs = source.InteriorBlendMs,
            BoundaryBlendMs = source.BoundaryBlendMs,
            ColorTransferMs = source.ColorTransferMs,
            BuildingGroundRedrawCellCount = source.BuildingGroundRedrawCellCount,
            BuildingOverlayCellCount = source.BuildingOverlayCellCount,
            TransitionFieldPixelCount = source.TransitionFieldPixelCount,
            MultiTerrainJunctionPixels = source.MultiTerrainJunctionPixels,
            RepeatedBoundaryBlendPreventedCount = source.RepeatedBoundaryBlendPreventedCount,
            RegionTextureCanvasCount = source.RegionTextureCanvasCount,
            QuiltedPatchCount = source.QuiltedPatchCount,
            PatchOverlapRejectedCount = source.PatchOverlapRejectedCount,
            MacroNoiseAppliedPixels = source.MacroNoiseAppliedPixels,
            ObjectContactBlendPixelCount = source.ObjectContactBlendPixelCount,
            BuildingVisualPlanCellCount = source.BuildingVisualPlanCellCount,
            AlphaRepairedObjectCount = source.AlphaRepairedObjectCount,
            AlphaRepairedPixelCount = source.AlphaRepairedPixelCount,
            BlackBackgroundRejectedPixelCount = source.BlackBackgroundRejectedPixelCount,
            CurrentMapPureSampleUsedCount = source.CurrentMapPureSampleUsedCount,
            CurrentMapSampleRejectedCount = source.CurrentMapSampleRejectedCount,
            MaterialLibraryFallbackCount = source.MaterialLibraryFallbackCount,
            ObjectGroundFootprintCellCount = source.ObjectGroundFootprintCellCount,
            ObjectGroundInpaintCellCount = source.ObjectGroundInpaintCellCount,
            ObjectGroundInferredCellCount = source.ObjectGroundInferredCellCount,
            ObjectGroundFallbackCellCount = source.ObjectGroundFallbackCellCount,
            ObjectGroundContextSampleCount = source.ObjectGroundContextSampleCount,
            TerrainObjectOverlayCellCount = source.TerrainObjectOverlayCellCount,
            MissingTerrainIds = source.MissingTerrainIds.ToList(),
            Notes = source.Notes.ToList()
        };

    private sealed record CachedSynthesisResult(string CacheKey, Bitmap Bitmap, TerrainVisualSynthesisDiagnostics Diagnostics);
    private sealed record CachedImage(DateTime LastWriteUtc, long Length, Bitmap Bitmap);
}
