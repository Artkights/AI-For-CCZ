using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Security.Cryptography;
using CCZModStudio.Models;

namespace CCZModStudio.Core;

public sealed class MapCanvasPreviewRenderer : IDisposable
{
    private readonly Dictionary<string, CachedImage> _imageCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<int, SolidBrush> _terrainBrushCache = new();
    private readonly MapCanvasComposeService _composeService = new();
    private readonly MaterialDrivenTerrainService _materialDrivenService = new();
    private IReadOnlyList<MaterialAsset> _materials = Array.Empty<MaterialAsset>();

    private Bitmap? _preview;
    private Bitmap? _baseMapCache;
    private Bitmap? _terrainLayerCache;
    private Bitmap? _beautifiedMapCache;
    private readonly HashSet<int> _dirtyTerrainIndexes = new();
    private readonly HashSet<int> _pendingTerrainOverlayIndexes = new();
    private string _cacheKey = string.Empty;
    private string _contentKey = string.Empty;
    private int _gridWidth;
    private int _gridHeight;
    private int _tileSize;
    private bool _showTerrain;
    private bool _showGrid;
    private int _terrainOpacityPercent;
    private bool _terrainLayerOnly;
    private bool _beautifyGeneratedMap;
    private int _baseMapVersion;
    private int _terrainLayerVersion;
    private int _beautifiedMapVersion;
    private int _lastComposedBaseVersion = -1;
    private int _lastComposedTerrainVersion = -1;
    private int _lastComposedBeautifiedVersion = -1;
    private int _lastComposedDirtyCount = -1;
    private int _pendingTerrainOverlayVersion;
    private int _lastComposedPendingTerrainOverlayVersion = -1;
    private int _pendingTerrainOverlayAlphaPercent = 45;
    private bool _lastComposedTerrainLayerOnly;
    private bool _lastComposedShowTerrain;
    private bool _lastComposedShowGrid;
    private bool _lastComposedBeautify;
    private int _lastComposedTerrainOpacityPercent = -1;
    private bool _baseMapDirty;
    private bool _terrainLayerDirty;
    private bool _beautifiedMapDirty;

    public Bitmap Rebuild(MapWorkbenchDraft draft, bool showTerrain, bool showGrid, int terrainOpacityPercent)
        => Rebuild(draft, Array.Empty<MaterialAsset>(), showTerrain, showGrid, terrainOpacityPercent);

    public Bitmap Rebuild(MapWorkbenchDraft draft, IReadOnlyList<MaterialAsset> materials, bool showTerrain, bool showGrid, int terrainOpacityPercent)
        => Rebuild(draft, materials, showTerrain, showGrid, terrainOpacityPercent, beautifyGeneratedMap: draft.BeautifyGeneratedMap);

    public Bitmap Rebuild(
        MapWorkbenchDraft draft,
        IReadOnlyList<MaterialAsset> materials,
        bool showTerrain,
        bool showGrid,
        int terrainOpacityPercent,
        bool beautifyGeneratedMap)
    {
        ValidateDraft(draft);
        EnsureContext(draft, materials);
        _showTerrain = showTerrain;
        _showGrid = showGrid;
        _terrainOpacityPercent = Math.Clamp(terrainOpacityPercent, 0, 100);
        _terrainLayerOnly = false;
        _beautifyGeneratedMap = beautifyGeneratedMap;

        if (_baseMapCache == null || _baseMapDirty)
        {
            RebuildBaseMapCache(draft);
        }

        if (beautifyGeneratedMap && (_beautifiedMapCache == null || _beautifiedMapDirty))
        {
            RebuildBeautifiedMapCache(draft);
        }

        return CreatePreviewSnapshot(ComposeCurrentPreview(draft));
    }

    public Bitmap RebuildTerrainLayer(MapWorkbenchDraft draft, bool showGrid)
    {
        ValidateDraft(draft);
        EnsureContext(draft, _materials);
        _showTerrain = true;
        _showGrid = showGrid;
        _terrainOpacityPercent = 100;
        _terrainLayerOnly = true;
        _beautifyGeneratedMap = false;

        if (_terrainLayerCache == null || _terrainLayerDirty)
        {
            RebuildTerrainLayerCache(draft);
        }

        return CreatePreviewSnapshot(ComposeCurrentPreview(draft));
    }

    public Rectangle UpdateMapCell(MapWorkbenchDraft draft, int index, MapCellOverride? cell)
    {
        if (!CanUpdateCell(draft, index)) return Rectangle.Empty;
        if (_terrainLayerOnly) return Rectangle.Empty;
        var rect = GetTileRectangle(index);
        if (_baseMapCache == null)
        {
            _dirtyTerrainIndexes.Add(index);
            _baseMapDirty = true;
            return rect;
        }

        DrawBaseMapTile(_baseMapCache, draft, index, cell);
        _baseMapVersion++;
        _beautifiedMapDirty = true;
        return ComposeAndReturnDirty(draft, rect);
    }

    public Rectangle UpdateTerrainCell(MapWorkbenchDraft draft, int index)
    {
        if (!CanUpdateCell(draft, index)) return Rectangle.Empty;
        if (draft.TerrainCells.Length != draft.CellCount) return Rectangle.Empty;

        var dirty = MarkTerrainDirty(draft, index);
        if (_terrainLayerOnly && _terrainLayerCache != null)
        {
            RedrawTerrainLayerTile(draft, index);
            _terrainLayerVersion++;
            return ComposeAndReturnDirty(draft, GetTileRectangle(index));
        }

        if (!draft.AutoGenerateMapFromTerrain && _showTerrain && _preview != null)
        {
            RedrawPreviewTile(draft, index, null);
            return GetTileRectangle(index);
        }

        return dirty;
    }

    public Rectangle UpdateTerrainMaterialCells(MapWorkbenchDraft draft, IReadOnlyCollection<int> indexes)
    {
        if (indexes.Count == 0) return Rectangle.Empty;
        if (_terrainLayerOnly) return Rectangle.Empty;

        var validIndexes = indexes
            .Where(index => CanUpdateCell(draft, index))
            .Distinct()
            .OrderBy(index => index)
            .ToList();
        if (validIndexes.Count == 0) return Rectangle.Empty;

        var refreshIndexes = validIndexes
            .SelectMany(ExpandIndexWithNeighbors)
            .Where(index => CanUpdateCell(draft, index))
            .Distinct()
            .OrderBy(index => index)
            .ToList();

        var dirty = Rectangle.Empty;
        foreach (var index in refreshIndexes)
        {
            _dirtyTerrainIndexes.Add(index);
            if (_baseMapCache != null)
            {
                DrawBaseMapTile(_baseMapCache, draft, index, null);
            }

            var rect = GetTileRectangle(index);
            dirty = dirty.IsEmpty ? rect : Rectangle.Union(dirty, rect);
        }

        _baseMapVersion++;
        _baseMapDirty = _baseMapCache == null;
        _beautifiedMapDirty = true;
        return ComposeAndReturnDirty(draft, dirty);
    }

    public Rectangle MarkTerrainDirty(MapWorkbenchDraft draft, int index)
    {
        if (!CanUpdateCell(draft, index)) return Rectangle.Empty;
        var dirty = Rectangle.Empty;
        foreach (var dirtyIndex in ExpandIndexWithNeighbors(index))
        {
            _dirtyTerrainIndexes.Add(dirtyIndex);
            var rect = GetTileRectangle(dirtyIndex);
            dirty = dirty.IsEmpty ? rect : Rectangle.Union(dirty, rect);
        }

        _baseMapDirty = true;
        _beautifiedMapDirty = true;
        if (_terrainLayerCache != null)
        {
            RedrawTerrainLayerTile(draft, index);
            _terrainLayerVersion++;
        }

        return dirty;
    }

    public Rectangle MarkTerrainDirty(MapWorkbenchDraft draft, IEnumerable<int> indexes)
    {
        var dirty = Rectangle.Empty;
        foreach (var index in indexes)
        {
            var rect = MarkTerrainDirty(draft, index);
            dirty = dirty.IsEmpty ? rect : rect.IsEmpty ? dirty : Rectangle.Union(dirty, rect);
        }

        return dirty;
    }

    public Rectangle UpdateGeneratedPreviewTerrainOverlay(MapWorkbenchDraft draft, IReadOnlyCollection<int> indexes, int alphaPercent)
    {
        if (indexes.Count == 0) return Rectangle.Empty;
        ValidateDraft(draft);
        EnsureContext(draft, _materials);
        if (_terrainLayerOnly || draft.TerrainCells.Length != draft.CellCount) return Rectangle.Empty;

        var dirty = Rectangle.Empty;
        var changed = false;
        var clampedAlpha = Math.Clamp(alphaPercent, 10, 90);
        if (_pendingTerrainOverlayAlphaPercent != clampedAlpha)
        {
            _pendingTerrainOverlayAlphaPercent = clampedAlpha;
            changed = true;
        }

        foreach (var index in indexes)
        {
            if (!CanUpdateCell(draft, index)) continue;
            changed |= _pendingTerrainOverlayIndexes.Add(index);
            var rect = GetTileRectangle(index);
            dirty = dirty.IsEmpty ? rect : Rectangle.Union(dirty, rect);
        }

        if (changed || !dirty.IsEmpty)
        {
            _pendingTerrainOverlayVersion++;
        }

        if (!dirty.IsEmpty)
        {
            ComposeCurrentPreview(draft);
        }

        return dirty;
    }

    public void ClearGeneratedPreviewTerrainOverlay()
    {
        if (_pendingTerrainOverlayIndexes.Count == 0) return;
        _pendingTerrainOverlayIndexes.Clear();
        _pendingTerrainOverlayVersion++;
    }

    public void RemoveGeneratedPreviewTerrainOverlay(IEnumerable<int> indexes)
    {
        var changed = false;
        foreach (var index in indexes)
        {
            changed |= _pendingTerrainOverlayIndexes.Remove(index);
        }

        if (!changed) return;
        _pendingTerrainOverlayVersion++;
    }

    public Rectangle RefreshDirtyBaseMap(MapWorkbenchDraft draft, IReadOnlyList<MaterialAsset> materials)
    {
        ValidateDraft(draft);
        EnsureContext(draft, materials);
        if (_baseMapCache == null)
        {
            RebuildBaseMapCache(draft);
            return new Rectangle(0, 0, draft.PixelWidth, draft.PixelHeight);
        }

        if (_dirtyTerrainIndexes.Count == 0 && !_baseMapDirty)
        {
            return Rectangle.Empty;
        }

        var dirty = Rectangle.Empty;
        foreach (var index in _dirtyTerrainIndexes.OrderBy(x => x))
        {
            if (index < 0 || index >= draft.CellCount) continue;
            DrawBaseMapTile(_baseMapCache, draft, index, null);
            var rect = GetTileRectangle(index);
            dirty = dirty.IsEmpty ? rect : Rectangle.Union(dirty, rect);
        }

        if (dirty.IsEmpty)
        {
            RebuildBaseMapCache(draft);
            dirty = new Rectangle(0, 0, draft.PixelWidth, draft.PixelHeight);
        }
        else
        {
            _dirtyTerrainIndexes.Clear();
            _baseMapDirty = false;
            _baseMapVersion++;
            _beautifiedMapDirty = true;
        }

        ComposeCurrentPreview(draft);
        return dirty;
    }

    public Bitmap GetCurrentPreviewImage(
        MapWorkbenchDraft draft,
        IReadOnlyList<MaterialAsset> materials,
        bool terrainLayerOnly,
        bool showGrid,
        int terrainOpacityPercent,
        bool showBeautifiedMap)
    {
        ValidateDraft(draft);
        EnsureContext(draft, materials);
        _terrainLayerOnly = terrainLayerOnly;
        _showTerrain = terrainLayerOnly;
        _showGrid = showGrid;
        _terrainOpacityPercent = terrainLayerOnly ? 100 : Math.Clamp(terrainOpacityPercent, 0, 100);
        _beautifyGeneratedMap = showBeautifiedMap;

        if (terrainLayerOnly)
        {
            if (_terrainLayerCache == null || _terrainLayerDirty)
            {
                RebuildTerrainLayerCache(draft);
            }
        }
        else if (_baseMapCache == null || (_baseMapDirty && _dirtyTerrainIndexes.Count == 0))
        {
            RebuildBaseMapCache(draft);
        }

        return CreatePreviewSnapshot(ComposeCurrentPreview(draft));
    }

    public void SetBeautifiedMapCache(MapWorkbenchDraft draft, Bitmap bitmap)
    {
        ValidateDraft(draft);
        EnsureContext(draft, _materials);
        _beautifiedMapCache?.Dispose();
        _beautifiedMapCache = new Bitmap(bitmap);
        _beautifiedMapDirty = false;
        _beautifiedMapVersion++;
    }

    public Bitmap CreateBaseMapSnapshot(MapWorkbenchDraft draft, IReadOnlyList<MaterialAsset> materials)
    {
        ValidateDraft(draft);
        EnsureContext(draft, materials);
        if (_baseMapCache == null || (_baseMapDirty && _dirtyTerrainIndexes.Count == 0))
        {
            RebuildBaseMapCache(draft);
        }

        return _baseMapCache == null ? _composeService.ComposePreview(draft, materials, false, false, 0, false) : new Bitmap(_baseMapCache);
    }

    public MapCanvasPreviewDiagnostics GetDiagnostics()
    {
        var tileCount = Math.Max(1, _gridWidth * _gridHeight);
        return new MapCanvasPreviewDiagnostics(
            DirtyCellCount: _dirtyTerrainIndexes.Count,
            PendingTerrainOverlayCount: _pendingTerrainOverlayIndexes.Count,
            BaseMapVersion: _baseMapVersion,
            TerrainLayerVersion: _terrainLayerVersion,
            BeautifiedMapVersion: _beautifiedMapVersion,
            IsBaseMapDirty: _baseMapDirty,
            IsBeautifiedMapDirty: _beautifiedMapDirty);
    }

    public void Clear()
    {
        _preview?.Dispose();
        _baseMapCache?.Dispose();
        _terrainLayerCache?.Dispose();
        _beautifiedMapCache?.Dispose();
        _preview = null;
        _baseMapCache = null;
        _terrainLayerCache = null;
        _beautifiedMapCache = null;
        _dirtyTerrainIndexes.Clear();
        _pendingTerrainOverlayIndexes.Clear();
        _cacheKey = string.Empty;
        _contentKey = string.Empty;
        _gridWidth = 0;
        _gridHeight = 0;
        _tileSize = 0;
        _showTerrain = false;
        _showGrid = false;
        _terrainOpacityPercent = 0;
        _terrainLayerOnly = false;
        _beautifyGeneratedMap = false;
        _pendingTerrainOverlayAlphaPercent = 45;
        _materials = Array.Empty<MaterialAsset>();
        _baseMapVersion = 0;
        _terrainLayerVersion = 0;
        _beautifiedMapVersion = 0;
        _lastComposedBaseVersion = -1;
        _lastComposedTerrainVersion = -1;
        _lastComposedBeautifiedVersion = -1;
        _lastComposedDirtyCount = -1;
        _pendingTerrainOverlayVersion = 0;
        _lastComposedPendingTerrainOverlayVersion = -1;
        _lastComposedTerrainLayerOnly = false;
        _lastComposedShowTerrain = false;
        _lastComposedShowGrid = false;
        _lastComposedBeautify = false;
        _lastComposedTerrainOpacityPercent = -1;
        _baseMapDirty = false;
        _terrainLayerDirty = false;
        _beautifiedMapDirty = false;
    }

    public void Dispose()
    {
        Clear();
        foreach (var cached in _imageCache.Values)
        {
            cached.Bitmap.Dispose();
        }

        _imageCache.Clear();
        foreach (var brush in _terrainBrushCache.Values)
        {
            brush.Dispose();
        }

        _terrainBrushCache.Clear();
    }

    private void RedrawTile(MapWorkbenchDraft draft, int index, MapCellOverride? updatedCell)
        => RedrawPreviewTile(draft, index, updatedCell);

    private void RedrawPreviewTile(MapWorkbenchDraft draft, int index, MapCellOverride? updatedCell)
    {
        if (_preview == null) return;
        var rect = GetTileRectangle(index);
        using var g = CreateGraphics(_preview);
        g.SetClip(rect);
        DrawBase(g, draft, rect, checkerboardBlank: true);
        _materialDrivenService.DrawCell(g, draft, _materials, index, includeTerrain: true, includeBuilding: true, includeScenery: true);

        if (_showTerrain && draft.TerrainCells.Length == draft.CellCount)
        {
            DrawTerrainCell(g, draft, index);
        }

        if (!_terrainLayerOnly && draft.TerrainCells.Length == draft.CellCount && _pendingTerrainOverlayIndexes.Contains(index))
        {
            DrawPendingTerrainOverlayCell(g, draft, index);
        }

        if (_showGrid)
        {
            DrawGrid(g, _gridWidth, _gridHeight, _preview.Width, _preview.Height);
        }

        g.ResetClip();
    }

    private void RedrawGeneratedTile(MapWorkbenchDraft draft, int index)
    {
        if (_preview == null) return;
        var rect = GetTileRectangle(index);
        using var g = CreateGraphics(_preview);
        g.SetClip(rect);
        DrawBase(g, draft, rect, checkerboardBlank: true);
        _materialDrivenService.DrawCell(g, draft, _materials, index, includeTerrain: true, includeBuilding: true, includeScenery: true);

        if (_showTerrain && draft.TerrainCells.Length == draft.CellCount)
        {
            DrawTerrainCell(g, draft, index);
        }

        if (!_terrainLayerOnly && draft.TerrainCells.Length == draft.CellCount && _pendingTerrainOverlayIndexes.Contains(index))
        {
            DrawPendingTerrainOverlayCell(g, draft, index);
        }

        if (_showGrid)
        {
            DrawGrid(g, _gridWidth, _gridHeight, _preview.Width, _preview.Height);
        }

        g.ResetClip();
    }

    private void RedrawTerrainLayerTile(MapWorkbenchDraft draft, int index)
    {
        var target = _terrainLayerCache;
        if (target == null) return;
        var rect = GetTileRectangle(index);
        using var g = CreateGraphics(target);
        g.SetClip(rect);
        using var black = new SolidBrush(Color.Black);
        g.FillRectangle(black, rect);

        if (draft.TerrainCells.Length == draft.CellCount)
        {
            DrawTerrainCell(g, draft, index);
        }

        g.ResetClip();
    }

    private void EnsureContext(MapWorkbenchDraft draft, IReadOnlyList<MaterialAsset> materials)
    {
        var tileSize = draft.TileSize <= 0 ? MapResourceItem.MapTilePixelSize : draft.TileSize;
        var contextKey = BuildContextCacheKey(draft, tileSize, materials);
        var contentKey = BuildCacheKey(draft, tileSize, materials);
        if (!_cacheKey.Equals(contextKey, StringComparison.Ordinal))
        {
            Clear();
            _cacheKey = contextKey;
            _gridWidth = draft.GridWidth;
            _gridHeight = draft.GridHeight;
            _tileSize = tileSize;
        }
        else if (!_contentKey.Equals(contentKey, StringComparison.Ordinal) && _dirtyTerrainIndexes.Count == 0)
        {
            _baseMapDirty = true;
            _terrainLayerDirty = true;
            _beautifiedMapDirty = true;
        }

        _contentKey = contentKey;
        _materials = materials;
    }

    private static string BuildContextCacheKey(MapWorkbenchDraft draft, int tileSize, IReadOnlyList<MaterialAsset> materials)
        => string.Join("|",
            draft.DraftId,
            draft.BoundMapId,
            draft.GridWidth.ToString(System.Globalization.CultureInfo.InvariantCulture),
            draft.GridHeight.ToString(System.Globalization.CultureInfo.InvariantCulture),
            tileSize.ToString(System.Globalization.CultureInfo.InvariantCulture),
            NormalizeFileKey(draft.BaseLayerPath),
            NormalizePathKey(draft.MaterialRoot),
            draft.GenerationMode,
            NormalizeTerrainVisualProfileKey(draft.TerrainVisualProfile),
            NormalizeTerrainRenderSettingsKey(draft.TerrainRenderSettings),
            materials.Count.ToString(System.Globalization.CultureInfo.InvariantCulture),
            NormalizeMaterialAutoTileKey(materials));

    private static string BuildCacheKey(MapWorkbenchDraft draft, int tileSize, IReadOnlyList<MaterialAsset> materials)
        => string.Join("|",
            draft.DraftId,
            draft.BoundMapId,
            draft.GridWidth.ToString(System.Globalization.CultureInfo.InvariantCulture),
            draft.GridHeight.ToString(System.Globalization.CultureInfo.InvariantCulture),
            tileSize.ToString(System.Globalization.CultureInfo.InvariantCulture),
            NormalizeFileKey(draft.BaseLayerPath),
            NormalizePathKey(draft.MaterialRoot),
            draft.GenerationMode,
            Convert.ToHexString(SHA256.HashData(draft.TerrainCells)),
            Convert.ToHexString(SHA256.HashData(draft.OriginalTerrainCells)),
            NormalizeTerrainMaterialPlanKey(draft.TerrainMaterialPlan),
            NormalizeTerrainRenderSettingsKey(draft.TerrainRenderSettings),
            NormalizeTerrainVisualProfileKey(draft.TerrainVisualProfile),
            NormalizeOverlayKey(draft),
            draft.BeautifyFilterProfile,
            NormalizeCustomBeautifyFilterKey(draft.CustomBeautifyFilter),
            draft.BeautifyStrength.ToString(System.Globalization.CultureInfo.InvariantCulture),
            draft.FeatherRadius.ToString(System.Globalization.CultureInfo.InvariantCulture),
            materials.Count.ToString(System.Globalization.CultureInfo.InvariantCulture),
            NormalizeMaterialAutoTileKey(materials));

    private static string NormalizeTerrainVisualProfileKey(TerrainVisualProfile? profile)
    {
        if (profile == null) return string.Empty;
        return string.Join(",",
            profile.Seed,
            profile.UseCurrentMapSamples.ToString(System.Globalization.CultureInfo.InvariantCulture),
            profile.AutoExtractCurrentMapSamples.ToString(System.Globalization.CultureInfo.InvariantCulture),
            profile.RedrawChangedCellsOnly.ToString(System.Globalization.CultureInfo.InvariantCulture),
            profile.EdgeFeatherRadius.ToString(System.Globalization.CultureInfo.InvariantCulture),
            profile.BlendStrength.ToString(System.Globalization.CultureInfo.InvariantCulture),
            profile.ColorAlignmentStrength.ToString("0.####", System.Globalization.CultureInfo.InvariantCulture),
            profile.TextureNoiseStrength.ToString("0.####", System.Globalization.CultureInfo.InvariantCulture),
            profile.UseInteriorTextureSynthesis.ToString(System.Globalization.CultureInfo.InvariantCulture),
            profile.EnableNaturalTileTransforms.ToString(System.Globalization.CultureInfo.InvariantCulture),
            profile.UseInteriorSeamBlend.ToString(System.Globalization.CultureInfo.InvariantCulture),
            profile.InteriorSeamPixels.ToString(System.Globalization.CultureInfo.InvariantCulture),
            profile.InteriorSeamJitterPixels.ToString(System.Globalization.CultureInfo.InvariantCulture),
            profile.InteriorSecondaryBlendStrength.ToString("0.####", System.Globalization.CultureInfo.InvariantCulture),
            profile.RegionTextureUnifyStrength.ToString("0.####", System.Globalization.CultureInfo.InvariantCulture),
            profile.RegionNoiseScalePixels.ToString(System.Globalization.CultureInfo.InvariantCulture),
            profile.AllowNinetyDegreeNaturalRotation.ToString(System.Globalization.CultureInfo.InvariantCulture),
            profile.RegenerateGroundUnderBuildingOverlays.ToString(System.Globalization.CultureInfo.InvariantCulture),
            profile.BuildingGroundContextRadiusCells.ToString(System.Globalization.CultureInfo.InvariantCulture),
            profile.UseGlobalBuildingStyle.ToString(System.Globalization.CultureInfo.InvariantCulture),
            profile.UseGlobalTransitionField.ToString(System.Globalization.CultureInfo.InvariantCulture),
            profile.UseRegionTextureCanvas.ToString(System.Globalization.CultureInfo.InvariantCulture),
            profile.UseObjectContactBlend.ToString(System.Globalization.CultureInfo.InvariantCulture),
            profile.TransitionFieldFeatherPixels.ToString(System.Globalization.CultureInfo.InvariantCulture),
            profile.TransitionFieldJitterPixels.ToString(System.Globalization.CultureInfo.InvariantCulture),
            profile.QuiltingOverlapPixels.ToString(System.Globalization.CultureInfo.InvariantCulture),
            profile.QuiltingCandidateCount.ToString(System.Globalization.CultureInfo.InvariantCulture),
            profile.MacroNoiseStrength.ToString("0.####", System.Globalization.CultureInfo.InvariantCulture),
            profile.ObjectContactShadowStrength.ToString("0.####", System.Globalization.CultureInfo.InvariantCulture),
            profile.ObjectContactBlendPixels.ToString(System.Globalization.CultureInfo.InvariantCulture),
            profile.ObjectGroundContextRadiusCells.ToString(System.Globalization.CultureInfo.InvariantCulture),
            profile.UseObjectGroundInpaint.ToString(System.Globalization.CultureInfo.InvariantCulture),
            profile.ObjectGroundInferenceRadiusCells.ToString(System.Globalization.CultureInfo.InvariantCulture),
            profile.GroundInpaintIncludesTerrainObjects.ToString(System.Globalization.CultureInfo.InvariantCulture),
            profile.AlphaRepairBlackThreshold.ToString(System.Globalization.CultureInfo.InvariantCulture),
            profile.AlphaRepairEdgeConnectivity.ToString(System.Globalization.CultureInfo.InvariantCulture),
            profile.MinPureSamplesPerTerrain.ToString(System.Globalization.CultureInfo.InvariantCulture),
            profile.PreferCurrentMapSamplesStrictly.ToString(System.Globalization.CultureInfo.InvariantCulture),
            profile.IgnoreBasePixelsUnderObjects.ToString(System.Globalization.CultureInfo.InvariantCulture),
            NormalizePathKey(profile.StyleSampleRoot),
            string.Join(";", (profile.MaterialOverrides ?? new List<TerrainVisualMaterialOverride>())
                .OrderBy(item => item.TerrainId)
                .Select(item => item.TerrainId.ToString(System.Globalization.CultureInfo.InvariantCulture) + "=" + item.MaterialRelativePath)),
            string.Join(";", (profile.SurfaceOverrides ?? new List<TerrainSurfaceOverride>())
                .OrderBy(item => item.TerrainId)
                .Select(item => item.TerrainId.ToString(System.Globalization.CultureInfo.InvariantCulture) + "=" + item.SurfaceKind)));
    }

    private static string NormalizeTerrainMaterialPlanKey(IEnumerable<TerrainMaterialPlanItem> plan)
        => string.Join(";", plan.OrderBy(item => item.TerrainId).ThenBy(item => item.VisualFamilyKey, StringComparer.OrdinalIgnoreCase)
            .Select(item => string.Join(",", item.MapId, item.TerrainId, item.VisualFamilyKey, item.MaterialRelativePath, item.SelectionMode, item.MaterialRootFingerprint)));

    private static string NormalizeTerrainRenderSettingsKey(TerrainRenderSettings? settings)
        => settings == null
            ? string.Empty
            : string.Join(",", settings.RedrawEnabled, settings.StyleSelectionMode, settings.StylePackId, settings.Seed,
                settings.PreviewQuality, settings.FinalQuality, settings.ObjectPolicy, settings.ToneProfile,
                settings.ToneAmount.ToString("0.####", System.Globalization.CultureInfo.InvariantCulture), settings.LastConfirmedFinalFingerprint);

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

    private static string NormalizeOverlayKey(MapWorkbenchDraft draft)
        => string.Join("|",
            string.Join(";",
                draft.TerrainBaseCells
                    .OrderBy(cell => cell.Index)
                    .ThenBy(cell => cell.MaterialRelativePath, StringComparer.OrdinalIgnoreCase)
                    .Select(NormalizeCellOverrideKey)),
            string.Join(";",
                draft.GeneratedMapCells
                    .OrderBy(cell => cell.Index)
                    .ThenBy(cell => cell.MaterialRelativePath, StringComparer.OrdinalIgnoreCase)
                    .Select(NormalizeCellOverrideKey)),
            string.Join(";",
                draft.BuildingOverlayCells
                    .OrderBy(cell => cell.Index)
                    .ThenBy(cell => cell.MaterialRelativePath, StringComparer.OrdinalIgnoreCase)
                    .Select(NormalizeCellOverrideKey)),
            string.Join(";",
                draft.SceneryOverlayCells
                    .OrderBy(cell => cell.Index)
                    .ThenBy(cell => cell.MaterialRelativePath, StringComparer.OrdinalIgnoreCase)
                    .Select(NormalizeCellOverrideKey)),
            string.Join(";",
            draft.SceneryOverlays
                .OrderBy(overlay => overlay.ZOrder)
                .ThenBy(overlay => overlay.X)
                .ThenBy(overlay => overlay.Y)
                .Select(overlay => string.Join(",",
                    overlay.MaterialRelativePath,
                    overlay.X.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    overlay.Y.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    overlay.Width.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    overlay.Height.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    overlay.RotationDegrees.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    overlay.ZOrder.ToString(System.Globalization.CultureInfo.InvariantCulture)))));

    private static string NormalizeCellOverrideKey(MapCellOverride cell)
        => string.Join(",",
            cell.Index.ToString(System.Globalization.CultureInfo.InvariantCulture),
            cell.MaterialRelativePath,
            cell.MaterialCategory,
            cell.DisplayName,
            cell.Source);

    private static string NormalizeCustomBeautifyFilterKey(BeautifyCustomFilterSettings? settings)
    {
        if (settings == null) return string.Empty;
        return string.Join(",",
            settings.PhotoR.ToString("0.####", System.Globalization.CultureInfo.InvariantCulture),
            settings.PhotoG.ToString("0.####", System.Globalization.CultureInfo.InvariantCulture),
            settings.PhotoB.ToString("0.####", System.Globalization.CultureInfo.InvariantCulture),
            settings.PhotoDensity.ToString("0.####", System.Globalization.CultureInfo.InvariantCulture),
            settings.BalanceR.ToString("0.####", System.Globalization.CultureInfo.InvariantCulture),
            settings.BalanceG.ToString("0.####", System.Globalization.CultureInfo.InvariantCulture),
            settings.BalanceB.ToString("0.####", System.Globalization.CultureInfo.InvariantCulture),
            settings.Saturation.ToString("0.####", System.Globalization.CultureInfo.InvariantCulture),
            settings.Brightness.ToString("0.####", System.Globalization.CultureInfo.InvariantCulture),
            settings.Contrast.ToString("0.####", System.Globalization.CultureInfo.InvariantCulture),
            settings.HighlightCompression.ToString("0.####", System.Globalization.CultureInfo.InvariantCulture),
            settings.ShadowLift.ToString("0.####", System.Globalization.CultureInfo.InvariantCulture),
            settings.MidtoneGamma.ToString("0.####", System.Globalization.CultureInfo.InvariantCulture),
            settings.PreserveLuminosity.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    private static string NormalizeMaterialAutoTileKey(IReadOnlyList<MaterialAsset> materials)
        => string.Join(";",
            materials
                .OrderBy(asset => asset.AutoTileSetKey, StringComparer.OrdinalIgnoreCase)
                .ThenBy(asset => asset.FilePath, StringComparer.OrdinalIgnoreCase)
                .ThenBy(asset => asset.SourceX)
                .Select(asset => string.Join(",",
                    asset.AutoTileSetKey,
                    asset.AutoTileMode,
                    asset.AutoTileRole,
                    (asset.AutoTileMask ?? MaterialAutoTileMasks.None).ToString(System.Globalization.CultureInfo.InvariantCulture),
                    asset.SourceX.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    asset.SourceY.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    asset.SourceWidth.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    asset.SourceHeight.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    asset.SamplingMode.ToString(),
                    asset.SampleBoundsX.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    asset.SampleBoundsY.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    asset.SampleBoundsWidth.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    asset.SampleBoundsHeight.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    asset.SafeBorder.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    asset.PreferredPatchWidth.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    asset.PreferredPatchHeight.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    asset.StylePackId,
                    NormalizeFileKey(asset.FilePath))));

    private void RebuildBaseMapCache(MapWorkbenchDraft draft)
    {
        _baseMapCache?.Dispose();
        draft.TerrainCells = _materialDrivenService.DeriveTerrainCells(draft, _materials);
        _baseMapCache = _materialDrivenService.ComposeVisualMap(draft, _materials, checkerboardBlank: true, beautifyTerrain: false);

        _dirtyTerrainIndexes.Clear();
        _baseMapDirty = false;
        _baseMapVersion++;
        _beautifiedMapDirty = true;
    }

    private void RebuildTerrainLayerCache(MapWorkbenchDraft draft)
    {
        _terrainLayerCache?.Dispose();
        _terrainLayerCache = CreateBitmap(draft.PixelWidth, draft.PixelHeight);
        using var g = CreateGraphics(_terrainLayerCache);
        using var black = new SolidBrush(Color.Black);
        g.FillRectangle(black, new Rectangle(0, 0, _terrainLayerCache.Width, _terrainLayerCache.Height));
        if (draft.TerrainCells.Length == draft.CellCount)
        {
            DrawTerrain(g, draft);
        }

        _terrainLayerDirty = false;
        _terrainLayerVersion++;
    }

    private void RebuildBeautifiedMapCache(MapWorkbenchDraft draft)
    {
        if (_baseMapCache == null || _baseMapDirty)
        {
            RebuildBaseMapCache(draft);
        }

        _beautifiedMapCache?.Dispose();
        draft.TerrainCells = _materialDrivenService.DeriveTerrainCells(draft, _materials);
        _beautifiedMapCache = _materialDrivenService.ComposeVisualMap(draft, _materials, checkerboardBlank: true, beautifyTerrain: true);

        _beautifiedMapDirty = false;
        _beautifiedMapVersion++;
    }

    private Bitmap ComposeCurrentPreview(MapWorkbenchDraft draft)
    {
        var source = SelectCurrentSource();
        if (source == null)
        {
            source = _baseMapCache;
        }

        if (source == null)
        {
            RebuildBaseMapCache(draft);
            source = _baseMapCache!;
        }

        if (_preview != null &&
            _lastComposedBaseVersion == _baseMapVersion &&
            _lastComposedTerrainVersion == _terrainLayerVersion &&
            _lastComposedBeautifiedVersion == _beautifiedMapVersion &&
            _lastComposedDirtyCount == _dirtyTerrainIndexes.Count &&
            _lastComposedPendingTerrainOverlayVersion == _pendingTerrainOverlayVersion &&
            _lastComposedTerrainLayerOnly == _terrainLayerOnly &&
            _lastComposedShowTerrain == _showTerrain &&
            _lastComposedShowGrid == _showGrid &&
            _lastComposedBeautify == _beautifyGeneratedMap &&
            _lastComposedTerrainOpacityPercent == _terrainOpacityPercent)
        {
            return _preview;
        }

        if (_preview == null || _preview.Width != source.Width || _preview.Height != source.Height)
        {
            _preview?.Dispose();
            _preview = CreateBitmap(source.Width, source.Height);
        }

        using var g = CreateGraphics(_preview);
        g.SetClip(new Rectangle(0, 0, _preview.Width, _preview.Height));
        g.DrawImage(source, new Rectangle(0, 0, _preview.Width, _preview.Height));
        if (!_terrainLayerOnly && _showTerrain && draft.TerrainCells.Length == draft.CellCount)
        {
            DrawTerrain(g, draft);
        }

        if (!_terrainLayerOnly && draft.TerrainCells.Length == draft.CellCount && _pendingTerrainOverlayIndexes.Count > 0)
        {
            DrawPendingTerrainOverlay(g, draft);
        }

        if (_showGrid)
        {
            DrawGrid(g, _gridWidth, _gridHeight, _preview.Width, _preview.Height);
        }

        g.ResetClip();

        _lastComposedBaseVersion = _baseMapVersion;
        _lastComposedTerrainVersion = _terrainLayerVersion;
        _lastComposedBeautifiedVersion = _beautifiedMapVersion;
        _lastComposedDirtyCount = _dirtyTerrainIndexes.Count;
        _lastComposedPendingTerrainOverlayVersion = _pendingTerrainOverlayVersion;
        _lastComposedTerrainLayerOnly = _terrainLayerOnly;
        _lastComposedShowTerrain = _showTerrain;
        _lastComposedShowGrid = _showGrid;
        _lastComposedBeautify = _beautifyGeneratedMap;
        _lastComposedTerrainOpacityPercent = _terrainOpacityPercent;
        return _preview;
    }

    private static Bitmap CreatePreviewSnapshot(Bitmap source)
        => new(source);

    private Bitmap? SelectCurrentSource()
    {
        if (_terrainLayerOnly)
        {
            return _terrainLayerCache;
        }

        if (_beautifyGeneratedMap && _beautifiedMapCache != null)
        {
            return _beautifiedMapCache;
        }

        return _baseMapCache;
    }

    private Rectangle ComposeAndReturnDirty(MapWorkbenchDraft draft, Rectangle dirty)
    {
        ComposeCurrentPreview(draft);
        return dirty;
    }

    private void DrawBaseMapTile(Bitmap target, MapWorkbenchDraft draft, int index, MapCellOverride? updatedCell)
    {
        if (index < 0 || index >= draft.CellCount) return;
        var rect = GetTileRectangle(index);
        using var g = CreateGraphics(target);
        g.SetClip(rect);
        DrawBase(g, draft, rect, checkerboardBlank: true);
        draft.TerrainCells = _materialDrivenService.DeriveTerrainCells(draft, _materials);
        _materialDrivenService.DrawCell(g, draft, _materials, index, includeTerrain: true, includeBuilding: true, includeScenery: true);

        g.ResetClip();
    }

    private static void ValidateDraft(MapWorkbenchDraft draft)
    {
        if (draft.GridWidth <= 0 || draft.GridHeight <= 0)
        {
            throw new InvalidOperationException("Map draft grid size is invalid.");
        }
    }

    private bool CanUpdateCell(MapWorkbenchDraft draft, int index)
    {
        if (_gridWidth <= 0 || _gridHeight <= 0 || _tileSize <= 0)
        {
            EnsureContext(draft, _materials);
        }

        if (_gridWidth <= 0 || _gridHeight <= 0 || _tileSize <= 0) return false;
        if (draft.GridWidth != _gridWidth || draft.GridHeight != _gridHeight) return false;
        var tileSize = draft.TileSize <= 0 ? MapResourceItem.MapTilePixelSize : draft.TileSize;
        if (tileSize != _tileSize) return false;
        return index >= 0 && index < _gridWidth * _gridHeight;
    }

    private void DrawBase(Graphics g, MapWorkbenchDraft draft, Rectangle rect, bool checkerboardBlank)
    {
        var baseImage = GetCachedImage(draft.BaseLayerPath);
        if (baseImage != null)
        {
            using var black = new SolidBrush(Color.Black);
            g.FillRectangle(black, rect);
            g.DrawImage(baseImage, rect, rect, GraphicsUnit.Pixel);
        }
        else if (checkerboardBlank)
        {
            DrawCheckerboard(g, rect);
        }
        else
        {
            using var black = new SolidBrush(Color.Black);
            g.FillRectangle(black, rect);
        }
    }

    private void DrawMapCell(Graphics g, MapWorkbenchDraft draft, int index, MapCellOverride cell)
    {
        if (index < 0 || index >= draft.CellCount) return;
        var materialPath = MapDraftService.ResolveMaterialPath(draft.MaterialRoot, cell.MaterialRelativePath);
        var material = GetCachedImage(materialPath);
        if (material == null) return;
        g.DrawImage(material, GetTileRectangle(index));
    }

    private void DrawGeneratedTerrainCell(Graphics g, MapWorkbenchDraft draft, int index)
    {
        if (index < 0 || index >= draft.CellCount) return;
        var generated = draft.GeneratedMapCells.FirstOrDefault(x => x.Index == index);
        if (generated != null)
        {
            DrawMapCell(g, draft, index, generated);
            return;
        }

        if (draft.TerrainCells.Length != draft.CellCount) return;
        using var brush = new SolidBrush(HexzmapTerrainRenderService.GetTerrainColor(draft.TerrainCells[index]));
        g.FillRectangle(brush, GetTileRectangle(index));
    }

    private void DrawTerrain(Graphics g, MapWorkbenchDraft draft)
    {
        for (var index = 0; index < draft.TerrainCells.Length; index++)
        {
            DrawTerrainCell(g, draft, index);
        }
    }

    private void DrawTerrainCell(Graphics g, MapWorkbenchDraft draft, int index)
    {
        if (index < 0 || index >= draft.TerrainCells.Length) return;
        var alpha = _terrainOpacityPercent * 255 / 100;
        if (alpha <= 0) return;
        var color = HexzmapTerrainRenderService.GetTerrainColor(draft.TerrainCells[index]);
        var key = (alpha << 24) | color.ToArgb() & 0x00FFFFFF;
        if (!_terrainBrushCache.TryGetValue(key, out var brush))
        {
            brush = new SolidBrush(Color.FromArgb(alpha, color));
            _terrainBrushCache[key] = brush;
        }

        g.FillRectangle(brush, GetTileRectangle(index));
    }

    private void DrawPendingTerrainOverlay(Graphics g, MapWorkbenchDraft draft)
    {
        foreach (var index in _pendingTerrainOverlayIndexes.OrderBy(x => x))
        {
            DrawPendingTerrainOverlayCell(g, draft, index);
        }
    }

    private void DrawPendingTerrainOverlayCell(Graphics g, MapWorkbenchDraft draft, int index)
    {
        if (index < 0 || index >= draft.TerrainCells.Length) return;
        var rect = GetTileRectangle(index);
        var terrainColor = HexzmapTerrainRenderService.GetTerrainColor(draft.TerrainCells[index]);
        var alpha = Math.Clamp(_pendingTerrainOverlayAlphaPercent, 10, 90) * 255 / 100;
        using var fill = new SolidBrush(Color.FromArgb(alpha, terrainColor));
        using var border = new Pen(Color.FromArgb(230, Color.White), Math.Max(1f, _tileSize / 24f));
        using var shadow = new Pen(Color.FromArgb(180, Color.Black), Math.Max(1f, _tileSize / 32f));
        g.FillRectangle(fill, rect);
        g.DrawRectangle(shadow, rect.Left, rect.Top, rect.Width - 1, rect.Height - 1);
        g.DrawRectangle(border, rect.Left + 1, rect.Top + 1, Math.Max(1, rect.Width - 3), Math.Max(1, rect.Height - 3));

        using var hatchPen = new Pen(Color.FromArgb(95, Color.White), 1f);
        var step = Math.Max(8, _tileSize / 4);
        for (var offset = -rect.Height; offset < rect.Width; offset += step)
        {
            g.DrawLine(
                hatchPen,
                rect.Left + offset,
                rect.Bottom - 1,
                rect.Left + offset + rect.Height,
                rect.Top);
        }
    }

    private static void DrawGrid(Graphics g, int gridWidth, int gridHeight, int pixelWidth, int pixelHeight)
    {
        using var darkPen = new Pen(Color.FromArgb(150, Color.Black));
        using var lightPen = new Pen(Color.FromArgb(70, Color.White));
        for (var x = 0; x <= gridWidth; x++)
        {
            var px = x * pixelWidth / (float)gridWidth;
            g.DrawLine(darkPen, px, 0, px, pixelHeight);
            g.DrawLine(lightPen, px + 1, 0, px + 1, pixelHeight);
        }

        for (var y = 0; y <= gridHeight; y++)
        {
            var py = y * pixelHeight / (float)gridHeight;
            g.DrawLine(darkPen, 0, py, pixelWidth, py);
            g.DrawLine(lightPen, 0, py + 1, pixelWidth, py + 1);
        }
    }

    private Rectangle GetTileRectangle(int index)
    {
        var x = index % _gridWidth;
        var y = index / _gridWidth;
        return new Rectangle(x * _tileSize, y * _tileSize, _tileSize, _tileSize);
    }

    private void AddDirtyIndexWithNeighbors(int index)
    {
        foreach (var dirtyIndex in ExpandIndexWithNeighbors(index))
        {
            _dirtyTerrainIndexes.Add(dirtyIndex);
        }
    }

    private IEnumerable<int> ExpandIndexWithNeighbors(int index)
    {
        if (_gridWidth <= 0 || _gridHeight <= 0) yield break;
        if (index < 0 || index >= _gridWidth * _gridHeight) yield break;
        var x = index % _gridWidth;
        var y = index / _gridWidth;
        for (var dy = -1; dy <= 1; dy++)
        {
            for (var dx = -1; dx <= 1; dx++)
            {
                var nx = x + dx;
                var ny = y + dy;
                if (nx < 0 || ny < 0 || nx >= _gridWidth || ny >= _gridHeight) continue;
                yield return ny * _gridWidth + nx;
            }
        }
    }

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

    private static Bitmap CreateBitmap(int width, int height)
        => new(width, height, PixelFormat.Format32bppArgb);

    private static Graphics CreateGraphics(Image image)
    {
        var g = Graphics.FromImage(image);
        g.SmoothingMode = SmoothingMode.None;
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.PixelOffsetMode = PixelOffsetMode.Half;
        return g;
    }

    private static void DrawCheckerboard(Graphics g, Rectangle rect)
    {
        const int size = 24;
        using var light = new SolidBrush(Color.FromArgb(62, 62, 62));
        using var dark = new SolidBrush(Color.FromArgb(38, 38, 38));
        var startX = rect.Left - PositiveMod(rect.Left, size);
        var startY = rect.Top - PositiveMod(rect.Top, size);
        for (var y = startY; y < rect.Bottom; y += size)
        {
            for (var x = startX; x < rect.Right; x += size)
            {
                var brush = ((x / size) + (y / size)) % 2 == 0 ? light : dark;
                g.FillRectangle(brush, x, y, Math.Min(size, rect.Right - x), Math.Min(size, rect.Bottom - y));
            }
        }
    }

    private static int PositiveMod(int value, int divisor)
    {
        var result = value % divisor;
        return result < 0 ? result + divisor : result;
    }

    private sealed record CachedImage(DateTime LastWriteUtc, long Length, Bitmap Bitmap);
}

public sealed record MapCanvasPreviewDiagnostics(
    int DirtyCellCount,
    int PendingTerrainOverlayCount,
    int BaseMapVersion,
    int TerrainLayerVersion,
    int BeautifiedMapVersion,
    bool IsBaseMapDirty,
    bool IsBeautifiedMapDirty);
