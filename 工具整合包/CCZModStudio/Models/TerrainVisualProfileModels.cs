using System.Drawing;

namespace CCZModStudio.Models;

public static class MapWorkbenchGenerationModes
{
    public const string MaterialDriven = "MaterialDriven";
    public const string TerrainDrivenVisual = "TerrainDrivenVisual";

    public static string Normalize(string? value)
        => string.Equals(value, TerrainDrivenVisual, StringComparison.OrdinalIgnoreCase)
            ? TerrainDrivenVisual
            : MaterialDriven;
}

public enum TerrainVisualSurfaceKind
{
    NaturalArea,
    LiquidArea,
    LinearTerrain,
    StructureTerrain,
    BuildingOverlay,
    FallbackColor
}

public sealed class TerrainVisualProfile
{
    public string Seed { get; set; } = "default";
    public bool UseCurrentMapSamples { get; set; } = true;
    public bool AutoExtractCurrentMapSamples { get; set; } = true;
    public bool RedrawChangedCellsOnly { get; set; } = true;
    public int EdgeFeatherRadius { get; set; } = 8;
    public int BlendStrength { get; set; } = 2;
    public float ColorAlignmentStrength { get; set; } = 0.65f;
    public float TextureNoiseStrength { get; set; } = 0.18f;
    public bool UseRegionConsistentMaterial { get; set; } = true;
    public bool UseDirectionalBoundaryBlend { get; set; } = true;
    public int StyleContextRadiusCells { get; set; } = 3;
    public int BlendContextRadiusCells { get; set; } = 2;
    public int BoundaryFeatherPixels { get; set; } = 14;
    public int BoundaryJitterPixels { get; set; } = 8;
    public int BoundaryNoiseScale { get; set; } = 12;
    public int OverlapSeamPixels { get; set; } = 8;
    public float LocalColorTransferStrength { get; set; } = 0.65f;
    public float CenterMinWeight { get; set; } = 0.35f;
    public float NeighborMaxWeight { get; set; } = 0.65f;
    public int StructureAlphaPreserveThreshold { get; set; } = 48;
    public bool UseInteriorTextureSynthesis { get; set; } = true;
    public bool EnableNaturalTileTransforms { get; set; } = true;
    public bool UseInteriorSeamBlend { get; set; } = true;
    public int InteriorSeamPixels { get; set; } = 8;
    public int InteriorSeamJitterPixels { get; set; } = 4;
    public float InteriorSecondaryBlendStrength { get; set; } = 0.16f;
    public float RegionTextureUnifyStrength { get; set; } = 0.12f;
    public int RegionNoiseScalePixels { get; set; } = 96;
    public bool AllowNinetyDegreeNaturalRotation { get; set; } = true;
    public bool UseFastPixelPipeline { get; set; } = true;
    public bool EnableParallelTerrainSynthesis { get; set; } = true;
    public int MaxDegreeOfParallelism { get; set; }
    public bool UseSynthesisCaches { get; set; } = true;
    public int TileCacheMaxEntries { get; set; } = 4096;
    public bool RegenerateGroundUnderBuildingOverlays { get; set; } = true;
    public int BuildingGroundContextRadiusCells { get; set; } = 1;
    public bool UseGlobalBuildingStyle { get; set; } = true;
    public bool UseGlobalTransitionField { get; set; } = true;
    public bool UseRegionTextureCanvas { get; set; } = true;
    public bool UseObjectContactBlend { get; set; } = true;
    public int TransitionFieldFeatherPixels { get; set; } = 18;
    public int TransitionFieldJitterPixels { get; set; } = 7;
    public int QuiltingOverlapPixels { get; set; } = 12;
    public int QuiltingCandidateCount { get; set; } = 8;
    public float MacroNoiseStrength { get; set; } = 0.10f;
    public float ObjectContactShadowStrength { get; set; } = 0.35f;
    public int ObjectContactBlendPixels { get; set; } = 5;
    public int ObjectGroundContextRadiusCells { get; set; } = 1;
    public bool UseObjectGroundInpaint { get; set; } = true;
    public int ObjectGroundInferenceRadiusCells { get; set; } = 3;
    public bool GroundInpaintIncludesTerrainObjects { get; set; } = true;
    public int AlphaRepairBlackThreshold { get; set; } = 24;
    public bool AlphaRepairEdgeConnectivity { get; set; } = true;
    public int MinPureSamplesPerTerrain { get; set; } = 4;
    public bool PreferCurrentMapSamplesStrictly { get; set; } = true;
    public bool IgnoreBasePixelsUnderObjects { get; set; } = true;
    public string StyleSampleRoot { get; set; } = string.Empty;
    public List<TerrainVisualMaterialOverride> MaterialOverrides { get; set; } = new();
    public List<TerrainSurfaceOverride> SurfaceOverrides { get; set; } = new();

    public TerrainVisualProfile Clone()
        => new()
        {
            Seed = Seed,
            UseCurrentMapSamples = UseCurrentMapSamples,
            AutoExtractCurrentMapSamples = AutoExtractCurrentMapSamples,
            RedrawChangedCellsOnly = RedrawChangedCellsOnly,
            EdgeFeatherRadius = EdgeFeatherRadius,
            BlendStrength = BlendStrength,
            ColorAlignmentStrength = ColorAlignmentStrength,
            TextureNoiseStrength = TextureNoiseStrength,
            UseRegionConsistentMaterial = UseRegionConsistentMaterial,
            UseDirectionalBoundaryBlend = UseDirectionalBoundaryBlend,
            StyleContextRadiusCells = StyleContextRadiusCells,
            BlendContextRadiusCells = BlendContextRadiusCells,
            BoundaryFeatherPixels = BoundaryFeatherPixels,
            BoundaryJitterPixels = BoundaryJitterPixels,
            BoundaryNoiseScale = BoundaryNoiseScale,
            OverlapSeamPixels = OverlapSeamPixels,
            LocalColorTransferStrength = LocalColorTransferStrength,
            CenterMinWeight = CenterMinWeight,
            NeighborMaxWeight = NeighborMaxWeight,
            StructureAlphaPreserveThreshold = StructureAlphaPreserveThreshold,
            UseInteriorTextureSynthesis = UseInteriorTextureSynthesis,
            EnableNaturalTileTransforms = EnableNaturalTileTransforms,
            UseInteriorSeamBlend = UseInteriorSeamBlend,
            InteriorSeamPixels = InteriorSeamPixels,
            InteriorSeamJitterPixels = InteriorSeamJitterPixels,
            InteriorSecondaryBlendStrength = InteriorSecondaryBlendStrength,
            RegionTextureUnifyStrength = RegionTextureUnifyStrength,
            RegionNoiseScalePixels = RegionNoiseScalePixels,
            AllowNinetyDegreeNaturalRotation = AllowNinetyDegreeNaturalRotation,
            UseFastPixelPipeline = UseFastPixelPipeline,
            EnableParallelTerrainSynthesis = EnableParallelTerrainSynthesis,
            MaxDegreeOfParallelism = MaxDegreeOfParallelism,
            UseSynthesisCaches = UseSynthesisCaches,
            TileCacheMaxEntries = TileCacheMaxEntries,
            RegenerateGroundUnderBuildingOverlays = RegenerateGroundUnderBuildingOverlays,
            BuildingGroundContextRadiusCells = BuildingGroundContextRadiusCells,
            UseGlobalBuildingStyle = UseGlobalBuildingStyle,
            UseGlobalTransitionField = UseGlobalTransitionField,
            UseRegionTextureCanvas = UseRegionTextureCanvas,
            UseObjectContactBlend = UseObjectContactBlend,
            TransitionFieldFeatherPixels = TransitionFieldFeatherPixels,
            TransitionFieldJitterPixels = TransitionFieldJitterPixels,
            QuiltingOverlapPixels = QuiltingOverlapPixels,
            QuiltingCandidateCount = QuiltingCandidateCount,
            MacroNoiseStrength = MacroNoiseStrength,
            ObjectContactShadowStrength = ObjectContactShadowStrength,
            ObjectContactBlendPixels = ObjectContactBlendPixels,
            ObjectGroundContextRadiusCells = ObjectGroundContextRadiusCells,
            UseObjectGroundInpaint = UseObjectGroundInpaint,
            ObjectGroundInferenceRadiusCells = ObjectGroundInferenceRadiusCells,
            GroundInpaintIncludesTerrainObjects = GroundInpaintIncludesTerrainObjects,
            AlphaRepairBlackThreshold = AlphaRepairBlackThreshold,
            AlphaRepairEdgeConnectivity = AlphaRepairEdgeConnectivity,
            MinPureSamplesPerTerrain = MinPureSamplesPerTerrain,
            PreferCurrentMapSamplesStrictly = PreferCurrentMapSamplesStrictly,
            IgnoreBasePixelsUnderObjects = IgnoreBasePixelsUnderObjects,
            StyleSampleRoot = StyleSampleRoot,
            MaterialOverrides = MaterialOverrides.Select(item => item.Clone()).ToList(),
            SurfaceOverrides = SurfaceOverrides.Select(item => item.Clone()).ToList()
        };
}

public sealed class TerrainSurfaceOverride
{
    public byte TerrainId { get; set; }
    public TerrainVisualSurfaceKind SurfaceKind { get; set; }

    public TerrainSurfaceOverride Clone()
        => new() { TerrainId = TerrainId, SurfaceKind = SurfaceKind };
}

public sealed class TerrainVisualMaterialOverride
{
    public byte TerrainId { get; set; }
    public string MaterialRelativePath { get; set; } = string.Empty;

    public TerrainVisualMaterialOverride Clone()
        => new()
        {
            TerrainId = TerrainId,
            MaterialRelativePath = MaterialRelativePath
        };
}

public sealed class TerrainVisualSynthesisRequest
{
    public MapWorkbenchDraft Draft { get; init; } = null!;
    public IReadOnlyList<MaterialAsset> Materials { get; init; } = Array.Empty<MaterialAsset>();
    public CurrentMapStyleProfile? StyleProfile { get; init; }
    public IReadOnlyCollection<int>? RedrawIndexes { get; init; }
    public CancellationToken CancellationToken { get; init; }
}

public sealed class TerrainVisualSynthesisResult : IDisposable
{
    public Bitmap Bitmap { get; init; } = null!;
    public TerrainVisualSynthesisDiagnostics Diagnostics { get; init; } = new();

    public void Dispose()
    {
        Bitmap.Dispose();
    }
}

public sealed class TerrainVisualSynthesisDiagnostics
{
    public bool UsedCurrentMapStyle { get; set; }
    public int StyleSampleCount { get; set; }
    public int RedrawnCellCount { get; set; }
    public int PreservedCellCount { get; set; }
    public int MaterialMatchedCellCount { get; set; }
    public int FallbackCellCount { get; set; }
    public int BoundaryBlendCount { get; set; }
    public int RegionCount { get; set; }
    public int RegionLockedMaterialCount { get; set; }
    public int ExpandedRedrawCellCount { get; set; }
    public int MixedTerrainCellCount { get; set; }
    public int BoundaryMaskPixelCount { get; set; }
    public int LocalColorTransferPixelCount { get; set; }
    public int FallbackGroupCount { get; set; }
    public int MissingTransitionMaskCount { get; set; }
    public int NaturalizedRegionCount { get; set; }
    public int InteriorSeamBlendPixelCount { get; set; }
    public int SecondaryPatchBlendPixelCount { get; set; }
    public int TileTransformCount { get; set; }
    public int StructureTransformSkippedCount { get; set; }
    public int RepeatedPatchPenaltyCount { get; set; }
    public bool FastPipelineEnabled { get; set; }
    public long TotalMs { get; set; }
    public long PlanMs { get; set; }
    public long TileRenderMs { get; set; }
    public long InteriorBlendMs { get; set; }
    public long BoundaryBlendMs { get; set; }
    public long ColorTransferMs { get; set; }
    public int BuildingGroundRedrawCellCount { get; set; }
    public int BuildingOverlayCellCount { get; set; }
    public int TransitionFieldPixelCount { get; set; }
    public int MultiTerrainJunctionPixels { get; set; }
    public int RepeatedBoundaryBlendPreventedCount { get; set; }
    public int RegionTextureCanvasCount { get; set; }
    public int QuiltedPatchCount { get; set; }
    public int PatchOverlapRejectedCount { get; set; }
    public int MacroNoiseAppliedPixels { get; set; }
    public int ObjectContactBlendPixelCount { get; set; }
    public int BuildingVisualPlanCellCount { get; set; }
    public int AlphaRepairedObjectCount { get; set; }
    public int AlphaRepairedPixelCount { get; set; }
    public int BlackBackgroundRejectedPixelCount { get; set; }
    public int CurrentMapPureSampleUsedCount { get; set; }
    public int CurrentMapSampleRejectedCount { get; set; }
    public int MaterialLibraryFallbackCount { get; set; }
    public int ObjectGroundFootprintCellCount { get; set; }
    public int ObjectGroundInpaintCellCount { get; set; }
    public int ObjectGroundInferredCellCount { get; set; }
    public int ObjectGroundFallbackCellCount { get; set; }
    public int ObjectGroundContextSampleCount { get; set; }
    public int TerrainObjectOverlayCellCount { get; set; }
    public List<byte> MissingTerrainIds { get; set; } = new();
    public List<string> Notes { get; set; } = new();
}

public sealed class CurrentMapStyleProfile
{
    public string SourceMapPath { get; init; } = string.Empty;
    public string SampleRoot { get; init; } = string.Empty;
    public int GridWidth { get; init; }
    public int GridHeight { get; init; }
    public int TileSize { get; init; } = MapResourceItem.MapTilePixelSize;
    public List<CurrentMapStyleTerrain> Terrains { get; init; } = new();
    public int RejectedSampleCount => Terrains.Sum(terrain => terrain.ContaminatedSamples.Count);

    public int SampleCount => Terrains.Sum(terrain => terrain.Samples.Count);

    public CurrentMapStyleTerrain? FindTerrain(byte terrainId)
        => Terrains.FirstOrDefault(terrain => terrain.TerrainId == terrainId);
}

public sealed class CurrentMapStyleTerrain
{
    public byte TerrainId { get; init; }
    public string TerrainName { get; init; } = string.Empty;
    public TileVisualStats Stats { get; init; } = TileVisualStats.Empty;
    public List<CurrentMapStyleTileSample> Samples { get; init; } = new();
    public List<CurrentMapStyleTileSample> PureSamples { get; init; } = new();
    public List<CurrentMapStyleTileSample> BoundarySamples { get; init; } = new();
    public List<CurrentMapStyleTileSample> ContaminatedSamples { get; init; } = new();
    public int PureSampleCount { get; init; }
    public int BoundarySampleCount { get; init; }
}

public sealed class CurrentMapStyleTileSample
{
    public byte TerrainId { get; init; }
    public int CellIndex { get; init; }
    public int CellX { get; init; }
    public int CellY { get; init; }
    public bool IsBoundary { get; init; }
    public bool IsContaminated { get; init; }
    public string ContaminationReason { get; init; } = string.Empty;
    public string FilePath { get; init; } = string.Empty;
    public TileVisualStats Stats { get; init; } = TileVisualStats.Empty;
}

public sealed record TileVisualStats(
    float AverageR,
    float AverageG,
    float AverageB,
    float Luminance,
    float Saturation,
    float Contrast,
    float EdgeStrength,
    float Texture)
{
    public static readonly TileVisualStats Empty = new(0, 0, 0, 0, 0, 0, 0, 0);
}
