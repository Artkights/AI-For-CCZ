using System.Globalization;
using System.Drawing;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using CCZModStudio.Models;

namespace CCZModStudio.Core;

public sealed class MapDraftService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public string GetSettingsPath(CczProject project)
        => GetSettingsPath(GetNotesRoot(project), project.Name);

    public string GetSettingsPath(string notesRoot, string profileName)
        => Path.Combine(NormalizeNotesRoot(notesRoot), $"{MakeSafeFileName(profileName)}_MapWorkbenchSettings.json");

    public string GetDraftStoreRoot(CczProject project)
        => GetDraftStoreRoot(GetNotesRoot(project), project.Name);

    public string GetDraftStoreRoot(string notesRoot, string profileName)
        => Path.Combine(NormalizeNotesRoot(notesRoot), "MapWorkbenchDrafts", MakeSafeFileName(profileName));

    public string GetDraftAssetRoot(CczProject project, string draftId)
        => Path.Combine(GetNotesRoot(project), "MapWorkbenchAssets", MakeSafeFileName(project.Name), MakeSafeFileName(draftId));

    public MapWorkbenchSettings LoadSettings(CczProject project)
        => LoadSettings(GetNotesRoot(project), project.Name);

    public MapWorkbenchSettings LoadSettings(string notesRoot, string profileName)
    {
        var path = GetSettingsPath(notesRoot, profileName);
        if (!File.Exists(path)) return new MapWorkbenchSettings();

        try
        {
            var settings = JsonSerializer.Deserialize<MapWorkbenchSettings>(File.ReadAllText(path, Encoding.UTF8), JsonOptions)
                           ?? new MapWorkbenchSettings();
            settings.DefaultCustomBeautifyFilter = NormalizeCustomBeautifyFilter(settings.DefaultCustomBeautifyFilter);
            settings.PersistedTerrainMaterialPlans ??= new List<PersistedTerrainMaterialPlan>();
            return settings;
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException("Map workbench settings JSON parse failed: " + path, ex);
        }
    }

    public void SaveSettings(CczProject project, MapWorkbenchSettings settings)
        => SaveSettings(GetNotesRoot(project), project.Name, settings);

    public void SaveSettings(string notesRoot, string profileName, MapWorkbenchSettings settings)
    {
        var path = GetSettingsPath(notesRoot, profileName);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(settings, JsonOptions), Encoding.UTF8);
    }

    public MapWorkbenchDraft CreateBlankDraft(int gridWidth = 30, int gridHeight = 30, string materialRoot = "")
    {
        var now = NowText();
        var draft = new MapWorkbenchDraft
        {
            DraftId = Guid.NewGuid().ToString("N"),
            GridWidth = Math.Max(1, gridWidth),
            GridHeight = Math.Max(1, gridHeight),
            TileSize = MapResourceItem.MapTilePixelSize,
            MaterialRoot = materialRoot ?? string.Empty,
            CreatedAtText = now,
            UpdatedAtText = now
        };
        draft.OriginalTerrainCells = new byte[draft.CellCount];
        draft.TerrainCells = new byte[draft.CellCount];
        return draft;
    }

    public MapWorkbenchDraft CreateDraftFromMap(CczProject project, MapResourceItem item, string materialRoot)
        => CreateDraftFromMap(item, materialRoot);

    public MapWorkbenchDraft CreateDraftFromMap(MapResourceItem item, string materialRoot)
    {
        var gridWidth = item.GridWidth > 0 ? item.GridWidth : 30;
        var gridHeight = item.GridHeight > 0 ? item.GridHeight : 30;
        var draft = CreateBlankDraft(gridWidth, gridHeight, materialRoot);
        draft.BoundMapId = GetMapId(item);
        if (File.Exists(item.Path)) draft.BaseLayerPath = Path.GetFullPath(item.Path);

        return draft;
    }

    public MapWorkbenchDraft LoadDraft(CczProject project, string draftId)
        => LoadDraft(GetNotesRoot(project), project.Name, draftId);

    public MapWorkbenchDraft LoadDraft(string notesRoot, string profileName, string draftId)
    {
        var path = GetDraftPath(notesRoot, profileName, draftId);
        if (!File.Exists(path))
        {
            throw new FileNotFoundException("Map workbench draft does not exist.", path);
        }

        try
        {
            var draft = JsonSerializer.Deserialize<MapWorkbenchDraft>(File.ReadAllText(path, Encoding.UTF8), JsonOptions)
                        ?? throw new InvalidOperationException("Map workbench draft is empty: " + path);
            return NormalizeDraft(draft);
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException("Map workbench draft JSON parse failed: " + path, ex);
        }
    }

    public void SaveDraft(CczProject project, MapWorkbenchDraft draft)
        => SaveDraft(GetNotesRoot(project), project.Name, draft);

    public void SaveDraft(string notesRoot, string profileName, MapWorkbenchDraft draft)
    {
        draft = NormalizeDraft(draft);
        draft.UpdatedAtText = NowText();
        if (string.IsNullOrWhiteSpace(draft.CreatedAtText)) draft.CreatedAtText = draft.UpdatedAtText;
        var path = GetDraftPath(notesRoot, profileName, draft.DraftId);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        if (File.Exists(path))
        {
            File.Copy(path, BuildUniqueBackupPath(path), overwrite: false);
        }

        File.WriteAllText(path, JsonSerializer.Serialize(draft, JsonOptions), Encoding.UTF8);
    }

    public IReadOnlyList<MapWorkbenchDraft> ListDrafts(CczProject project)
        => ListDrafts(GetNotesRoot(project), project.Name);

    public IReadOnlyList<MapWorkbenchDraft> ListDrafts(string notesRoot, string profileName)
    {
        var root = GetDraftStoreRoot(notesRoot, profileName);
        if (!Directory.Exists(root)) return Array.Empty<MapWorkbenchDraft>();

        var drafts = new List<MapWorkbenchDraft>();
        foreach (var path in Directory.GetFiles(root, "*.json").OrderByDescending(File.GetLastWriteTimeUtc))
        {
            try
            {
                var draft = JsonSerializer.Deserialize<MapWorkbenchDraft>(File.ReadAllText(path, Encoding.UTF8), JsonOptions);
                if (draft != null) drafts.Add(NormalizeDraft(draft));
            }
            catch
            {
                // Keep the workbench usable even when one old draft is corrupt.
            }
        }

        return drafts;
    }

    public IReadOnlyList<MapWorkbenchMissingAsset> FindMissingAssets(MapWorkbenchDraft draft)
    {
        var missing = new List<MapWorkbenchMissingAsset>();
        if (!string.IsNullOrWhiteSpace(draft.BaseLayerPath) && !File.Exists(draft.BaseLayerPath))
        {
            missing.Add(new MapWorkbenchMissingAsset
            {
                Index = -1,
                RelativePath = draft.BaseLayerPath,
                ExpectedPath = draft.BaseLayerPath,
                Reason = "Base layer image is missing."
            });
        }

        foreach (var cell in draft.TerrainBaseCells
                     .Concat(draft.BuildingOverlayCells)
                     .Concat(draft.SceneryOverlayCells)
                     .Concat(draft.MapCellOverrides))
        {
            var path = ResolveMaterialPath(draft.MaterialRoot, cell.MaterialRelativePath);
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                missing.Add(new MapWorkbenchMissingAsset
                {
                    Index = cell.Index,
                    RelativePath = cell.MaterialRelativePath,
                    ExpectedPath = path,
                    Reason = string.IsNullOrWhiteSpace(draft.MaterialRoot)
                        ? "Material library root is not set."
                        : "Material file is missing."
                });
            }
        }

        foreach (var overlay in draft.SceneryOverlays)
        {
            var path = ResolveMaterialPath(draft.MaterialRoot, overlay.MaterialRelativePath);
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                missing.Add(new MapWorkbenchMissingAsset
                {
                    Index = -1,
                    RelativePath = overlay.MaterialRelativePath,
                    ExpectedPath = path,
                    Reason = string.IsNullOrWhiteSpace(draft.MaterialRoot)
                        ? "Material library root is not set."
                        : "Scenery overlay material file is missing."
                });
            }
        }

        return missing;
    }

    public static string GetMaterialRelativePath(string materialRoot, string materialPath)
    {
        if (string.IsNullOrWhiteSpace(materialRoot) || string.IsNullOrWhiteSpace(materialPath)) return materialPath ?? string.Empty;

        try
        {
            var root = Path.GetFullPath(materialRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            var full = Path.GetFullPath(materialPath);
            if (full.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            {
                return Path.GetRelativePath(root, full);
            }
        }
        catch
        {
            // Fall back to the original path below.
        }

        return materialPath;
    }

    public static string ResolveMaterialPath(string materialRoot, string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath)) return string.Empty;
        if (Path.IsPathRooted(relativePath)) return relativePath;
        return string.IsNullOrWhiteSpace(materialRoot) ? relativePath : Path.Combine(materialRoot, relativePath);
    }

    private string GetDraftPath(CczProject project, string draftId)
        => GetDraftPath(GetNotesRoot(project), project.Name, draftId);

    private string GetDraftPath(string notesRoot, string profileName, string draftId)
        => Path.Combine(GetDraftStoreRoot(notesRoot, profileName), $"{MakeSafeFileName(draftId)}.json");

    private static MapWorkbenchDraft NormalizeDraft(MapWorkbenchDraft draft)
    {
        if (string.IsNullOrWhiteSpace(draft.DraftId)) draft.DraftId = Guid.NewGuid().ToString("N");
        draft.GridWidth = Math.Max(1, draft.GridWidth);
        draft.GridHeight = Math.Max(1, draft.GridHeight);
        draft.TileSize = draft.TileSize <= 0 ? MapResourceItem.MapTilePixelSize : draft.TileSize;
        if (draft.TileSize != MapResourceItem.MapTilePixelSize) draft.TileSize = MapResourceItem.MapTilePixelSize;
        var cellCount = checked(draft.GridWidth * draft.GridHeight);

        draft.OriginalTerrainCells = NormalizeTerrainArray(draft.OriginalTerrainCells, cellCount, draft.TerrainCells);
        draft.TerrainCells = NormalizeTerrainArray(draft.TerrainCells, cellCount, draft.OriginalTerrainCells);
        draft.MapCellOverrides ??= new List<MapCellOverride>();
        draft.MapCellOverrides = NormalizeCells(draft.MapCellOverrides, cellCount, MapCellOverrideSources.ManualOverride);
        draft.TerrainBaseCells ??= new List<MapCellOverride>();
        draft.TerrainBaseCells = NormalizeCells(draft.TerrainBaseCells, cellCount, MapCellOverrideSources.TerrainBase);
        draft.GeneratedMapCells ??= new List<MapCellOverride>();
        draft.GeneratedMapCells = NormalizeCells(draft.GeneratedMapCells, cellCount, MapCellOverrideSources.Generated);
        draft.BuildingOverlayCells ??= new List<MapCellOverride>();
        draft.BuildingOverlayCells = NormalizeCells(draft.BuildingOverlayCells, cellCount, MapCellOverrideSources.BuildingOverlay);
        draft.SceneryOverlayCells ??= new List<MapCellOverride>();
        draft.SceneryOverlayCells = NormalizeCells(draft.SceneryOverlayCells, cellCount, MapCellOverrideSources.SceneryOverlay);
        draft.SceneryOverlays ??= new List<MapSceneryOverlay>();
        MigrateLegacyCellsToMaterialLayers(draft);
        MigrateLegacySceneryCellsToOverlays(draft);
        draft.SceneryOverlays = NormalizeSceneryOverlays(draft.SceneryOverlays, draft);
        draft.TerrainMaterialPlan ??= new List<TerrainMaterialPlanItem>();
        draft.TerrainMaterialPlan = NormalizeTerrainMaterialPlan(draft.TerrainMaterialPlan);
        draft.GenerationMode = MapWorkbenchGenerationModes.Normalize(draft.GenerationMode);
        draft.TerrainVisualProfile = NormalizeTerrainVisualProfile(draft.TerrainVisualProfile);
        draft.BeautifyStrength = Math.Clamp(draft.BeautifyStrength, 0, 3);
        draft.FeatherRadius = Math.Clamp(draft.FeatherRadius, 0, MapResourceItem.MapTilePixelSize / 2);
        draft.BeautifyFilterProfile = NormalizeBeautifyFilterProfile(draft.BeautifyFilterProfile);
        draft.CustomBeautifyFilter = NormalizeCustomBeautifyFilter(draft.CustomBeautifyFilter);
        draft.BoundMapId = draft.BoundMapId?.Trim() ?? string.Empty;
        draft.BaseLayerPath = draft.BaseLayerPath?.Trim() ?? string.Empty;
        draft.MaterialRoot = draft.MaterialRoot?.Trim() ?? string.Empty;
        draft.CreatedAtText = draft.CreatedAtText?.Trim() ?? string.Empty;
        draft.UpdatedAtText = draft.UpdatedAtText?.Trim() ?? string.Empty;
        return draft;
    }

    private static TerrainVisualProfile NormalizeTerrainVisualProfile(TerrainVisualProfile? profile)
    {
        profile ??= new TerrainVisualProfile();
        profile.Seed = string.IsNullOrWhiteSpace(profile.Seed) ? "default" : profile.Seed.Trim();
        profile.StyleSampleRoot = profile.StyleSampleRoot?.Trim() ?? string.Empty;
        profile.EdgeFeatherRadius = Math.Clamp(profile.EdgeFeatherRadius <= 0 ? 8 : profile.EdgeFeatherRadius, 0, MapResourceItem.MapTilePixelSize / 2);
        profile.BlendStrength = Math.Clamp(profile.BlendStrength <= 0 ? 2 : profile.BlendStrength, 0, 3);
        profile.ColorAlignmentStrength = Math.Clamp(profile.ColorAlignmentStrength, 0f, 1f);
        profile.TextureNoiseStrength = Math.Clamp(profile.TextureNoiseStrength, 0f, 1f);
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
        profile.TransitionFieldFeatherPixels = Math.Clamp(profile.TransitionFieldFeatherPixels <= 0 ? profile.BoundaryFeatherPixels : profile.TransitionFieldFeatherPixels, 1, MapResourceItem.MapTilePixelSize);
        profile.TransitionFieldJitterPixels = Math.Clamp(profile.TransitionFieldJitterPixels, 0, MapResourceItem.MapTilePixelSize / 2);
        profile.QuiltingOverlapPixels = Math.Clamp(profile.QuiltingOverlapPixels <= 0 ? profile.OverlapSeamPixels : profile.QuiltingOverlapPixels, 0, MapResourceItem.MapTilePixelSize / 2);
        profile.QuiltingCandidateCount = Math.Clamp(profile.QuiltingCandidateCount <= 0 ? 8 : profile.QuiltingCandidateCount, 1, 32);
        profile.MacroNoiseStrength = Math.Clamp(profile.MacroNoiseStrength, 0f, 0.5f);
        profile.ObjectContactShadowStrength = Math.Clamp(profile.ObjectContactShadowStrength, 0f, 1f);
        profile.ObjectContactBlendPixels = Math.Clamp(profile.ObjectContactBlendPixels <= 0 ? 5 : profile.ObjectContactBlendPixels, 1, 16);
        profile.ObjectGroundContextRadiusCells = Math.Clamp(profile.ObjectGroundContextRadiusCells <= 0 ? 1 : profile.ObjectGroundContextRadiusCells, 0, 4);
        profile.ObjectGroundInferenceRadiusCells = Math.Clamp(profile.ObjectGroundInferenceRadiusCells <= 0 ? 3 : profile.ObjectGroundInferenceRadiusCells, 1, 8);
        profile.AlphaRepairBlackThreshold = Math.Clamp(profile.AlphaRepairBlackThreshold <= 0 ? 24 : profile.AlphaRepairBlackThreshold, 1, 96);
        profile.MinPureSamplesPerTerrain = Math.Clamp(profile.MinPureSamplesPerTerrain <= 0 ? 4 : profile.MinPureSamplesPerTerrain, 1, 32);
        profile.MaterialOverrides ??= new List<TerrainVisualMaterialOverride>();
        profile.MaterialOverrides = profile.MaterialOverrides
            .Where(item => !string.IsNullOrWhiteSpace(item.MaterialRelativePath))
            .Select(item => new TerrainVisualMaterialOverride
            {
                TerrainId = item.TerrainId,
                MaterialRelativePath = item.MaterialRelativePath.Trim()
            })
            .ToList();
        return profile;
    }

    private static byte[] NormalizeTerrainArray(byte[]? source, int cellCount, byte[]? fallback = null)
    {
        var normalized = new byte[cellCount];
        if (source != null && source.Length > 0)
        {
            Array.Copy(source, normalized, Math.Min(source.Length, normalized.Length));
            return normalized;
        }

        if (fallback != null && fallback.Length > 0)
        {
            Array.Copy(fallback, normalized, Math.Min(fallback.Length, normalized.Length));
        }

        return normalized;
    }

    private static void MigrateLegacyCellsToMaterialLayers(MapWorkbenchDraft draft)
    {
        if (draft.TerrainBaseCells.Count == 0 && draft.GeneratedMapCells.Count > 0)
        {
            draft.TerrainBaseCells = draft.GeneratedMapCells
                .Select(cell =>
                {
                    var clone = CloneCell(cell);
                    clone.Source = MapCellOverrideSources.TerrainBase;
                    return clone;
                })
                .ToList();
        }

        if (draft.SceneryOverlayCells.Count == 0 && draft.MapCellOverrides.Count > 0)
        {
            draft.SceneryOverlayCells = draft.MapCellOverrides
                .Where(cell => !cell.Source.Equals(MapCellOverrideSources.BuildingOverlay, StringComparison.OrdinalIgnoreCase))
                .Select(cell =>
                {
                    var clone = CloneCell(cell);
                    clone.Source = MapCellOverrideSources.SceneryOverlay;
                    return clone;
                })
                .ToList();
            draft.MapCellOverrides.Clear();
        }
    }

    private static void MigrateLegacySceneryCellsToOverlays(MapWorkbenchDraft draft)
    {
        if (draft.SceneryOverlayCells.Count == 0) return;
        if (draft.SceneryOverlays.Count > 0)
        {
            draft.SceneryOverlayCells.Clear();
            return;
        }

        var tileSize = draft.TileSize <= 0 ? MapResourceItem.MapTilePixelSize : draft.TileSize;
        var z = 0;
        draft.SceneryOverlays = draft.SceneryOverlayCells
            .OrderBy(cell => cell.Index)
            .Select(cell =>
            {
                var x = cell.Index % draft.GridWidth;
                var y = cell.Index / draft.GridWidth;
                var size = TryReadImageSize(ResolveMaterialPath(draft.MaterialRoot, cell.MaterialRelativePath));
                return new MapSceneryOverlay
                {
                    OverlayId = Guid.NewGuid().ToString("N"),
                    MaterialRelativePath = cell.MaterialRelativePath,
                    MaterialCategory = cell.MaterialCategory,
                    DisplayName = cell.DisplayName,
                    X = x * tileSize,
                    Y = y * tileSize,
                    Width = size.Width > 0 ? size.Width : tileSize,
                    Height = size.Height > 0 ? size.Height : tileSize,
                    ZOrder = z++
                };
            })
            .ToList();
        draft.SceneryOverlayCells.Clear();
    }

    private static List<MapCellOverride> NormalizeCells(IEnumerable<MapCellOverride> cells, int cellCount, string defaultSource)
        => cells
            .Where(cell => cell.Index >= 0 && cell.Index < cellCount && !string.IsNullOrWhiteSpace(cell.MaterialRelativePath))
            .Select(cell =>
            {
                cell.MaterialRelativePath = cell.MaterialRelativePath?.Trim() ?? string.Empty;
                cell.MaterialCategory = cell.MaterialCategory?.Trim() ?? string.Empty;
                cell.DisplayName = cell.DisplayName?.Trim() ?? string.Empty;
                cell.Source = string.IsNullOrWhiteSpace(cell.Source) ? defaultSource : cell.Source.Trim();
                return cell;
            })
            .GroupBy(cell => cell.Index)
            .Select(group => group.Last())
            .OrderBy(cell => cell.Index)
            .ToList();

    private static List<MapSceneryOverlay> NormalizeSceneryOverlays(IEnumerable<MapSceneryOverlay> overlays, MapWorkbenchDraft draft)
    {
        var tileSize = draft.TileSize <= 0 ? MapResourceItem.MapTilePixelSize : draft.TileSize;
        var pixelWidth = Math.Max(tileSize, draft.PixelWidth);
        var pixelHeight = Math.Max(tileSize, draft.PixelHeight);
        return overlays
            .Where(overlay => !string.IsNullOrWhiteSpace(overlay.MaterialRelativePath))
            .Select((overlay, index) =>
            {
                overlay.MaterialRelativePath = overlay.MaterialRelativePath?.Trim() ?? string.Empty;
                overlay.OverlayId = string.IsNullOrWhiteSpace(overlay.OverlayId)
                    ? Guid.NewGuid().ToString("N")
                    : overlay.OverlayId.Trim();
                overlay.MaterialCategory = overlay.MaterialCategory?.Trim() ?? string.Empty;
                overlay.DisplayName = overlay.DisplayName?.Trim() ?? string.Empty;
                overlay.X = Math.Clamp(overlay.X, -pixelWidth, pixelWidth);
                overlay.Y = Math.Clamp(overlay.Y, -pixelHeight, pixelHeight);
                if (overlay.Width <= 0 || overlay.Height <= 0)
                {
                    var size = TryReadImageSize(ResolveMaterialPath(draft.MaterialRoot, overlay.MaterialRelativePath));
                    overlay.Width = size.Width > 0 ? size.Width : tileSize;
                    overlay.Height = size.Height > 0 ? size.Height : tileSize;
                }

                overlay.Width = Math.Clamp(overlay.Width, 1, pixelWidth * 2);
                overlay.Height = Math.Clamp(overlay.Height, 1, pixelHeight * 2);
                overlay.RotationDegrees = NormalizeRotationDegrees(overlay.RotationDegrees);
                if (overlay.ZOrder == 0 && index > 0)
                {
                    overlay.ZOrder = index;
                }

                return overlay;
            })
            .OrderBy(overlay => overlay.ZOrder)
            .ThenBy(overlay => overlay.Y)
            .ThenBy(overlay => overlay.X)
            .ToList();
    }

    private static float NormalizeRotationDegrees(float value)
    {
        if (float.IsNaN(value) || float.IsInfinity(value)) return 0;
        value %= 360f;
        if (value < 0) value += 360f;
        return value;
    }

    public static BeautifyCustomFilterSettings? NormalizeCustomBeautifyFilter(BeautifyCustomFilterSettings? value)
    {
        if (value == null) return null;
        value.PhotoR = ClampFinite(value.PhotoR, 0f, 1f, 0.92f);
        value.PhotoG = ClampFinite(value.PhotoG, 0f, 1f, 0.82f);
        value.PhotoB = ClampFinite(value.PhotoB, 0f, 1f, 0.64f);
        value.PhotoDensity = ClampFinite(value.PhotoDensity, 0f, 1f, 0.12f);
        value.BalanceR = ClampFinite(value.BalanceR, -1f, 1f, 0.03f);
        value.BalanceG = ClampFinite(value.BalanceG, -1f, 1f, 0.02f);
        value.BalanceB = ClampFinite(value.BalanceB, -1f, 1f, -0.03f);
        value.Saturation = ClampFinite(value.Saturation, 0f, 3f, 1.04f);
        value.Brightness = ClampFinite(value.Brightness, -1f, 1f, 0.01f);
        value.Contrast = ClampFinite(value.Contrast, 0f, 3f, 1.04f);
        value.HighlightCompression = ClampFinite(value.HighlightCompression, -1f, 1f, 0f);
        value.ShadowLift = ClampFinite(value.ShadowLift, -1f, 1f, 0f);
        value.MidtoneGamma = ClampFinite(value.MidtoneGamma, 0.2f, 5f, 1f);
        return value;
    }

    private static float ClampFinite(float value, float min, float max, float fallback)
    {
        if (float.IsNaN(value) || float.IsInfinity(value)) return fallback;
        return Math.Clamp(value, min, max);
    }

    private static MapCellOverride CloneCell(MapCellOverride cell)
        => new()
        {
            Index = cell.Index,
            MaterialRelativePath = cell.MaterialRelativePath,
            MaterialCategory = cell.MaterialCategory,
            DisplayName = cell.DisplayName,
            Source = cell.Source
        };

    private static List<TerrainMaterialPlanItem> NormalizeTerrainMaterialPlan(IEnumerable<TerrainMaterialPlanItem> items)
        => items
            .Where(item => !string.IsNullOrWhiteSpace(item.VisualFamilyKey) &&
                           !string.IsNullOrWhiteSpace(item.MaterialRelativePath))
            .Select(item =>
            {
                item.MapId = item.MapId?.Trim() ?? string.Empty;
                item.VisualFamilyKey = item.VisualFamilyKey?.Trim() ?? string.Empty;
                item.MaterialRelativePath = item.MaterialRelativePath?.Trim() ?? string.Empty;
                item.MaterialCategory = item.MaterialCategory?.Trim() ?? string.Empty;
                item.DisplayName = item.DisplayName?.Trim() ?? string.Empty;
                item.SelectionMode = string.IsNullOrWhiteSpace(item.SelectionMode)
                    ? TerrainMaterialSelectionModes.Auto
                    : item.SelectionMode.Trim();
                item.MaterialRootFingerprint = item.MaterialRootFingerprint?.Trim() ?? string.Empty;
                return item;
            })
            .GroupBy(item => item.VisualFamilyKey, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.Last())
            .OrderBy(item => item.TerrainId)
            .ThenBy(item => item.VisualFamilyKey, StringComparer.OrdinalIgnoreCase)
            .ToList();

    private static string NormalizeBeautifyFilterProfile(string? profile)
    {
        profile = profile?.Trim() ?? string.Empty;
        return profile switch
        {
            TerrainBeautifyFilterProfiles.Natural => TerrainBeautifyFilterProfiles.Natural,
            TerrainBeautifyFilterProfiles.Night => TerrainBeautifyFilterProfiles.Night,
            TerrainBeautifyFilterProfiles.Autumn => TerrainBeautifyFilterProfiles.Autumn,
            TerrainBeautifyFilterProfiles.Winter => TerrainBeautifyFilterProfiles.Winter,
            TerrainBeautifyFilterProfiles.WarmSun => TerrainBeautifyFilterProfiles.WarmSun,
            TerrainBeautifyFilterProfiles.Custom => TerrainBeautifyFilterProfiles.Custom,
            _ => TerrainBeautifyFilterProfiles.Natural
        };
    }

    private static Size TryReadImageSize(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return Size.Empty;
        try
        {
            using var image = Image.FromFile(path);
            return image.Size;
        }
        catch
        {
            return Size.Empty;
        }
    }

    private static string GetMapId(MapResourceItem item)
    {
        var name = Path.GetFileNameWithoutExtension(item.Name);
        if (name.Length > 1 && (name[0] == 'M' || name[0] == 'm') &&
            int.TryParse(name[1..], NumberStyles.Integer, CultureInfo.InvariantCulture, out var number))
        {
            return $"M{number:D3}";
        }

        return item.Id;
    }

    private static string GetNotesRoot(CczProject project)
        => Path.Combine(project.WorkspaceRoot, "CCZModStudio_Notes");

    private static string NormalizeNotesRoot(string notesRoot)
        => Path.GetFullPath(string.IsNullOrWhiteSpace(notesRoot)
            ? Path.Combine(Environment.CurrentDirectory, "CCZModStudio_Notes")
            : notesRoot);

    private static string NowText()
        => DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);

    private static string BuildUniqueBackupPath(string path)
    {
        var directory = Path.GetDirectoryName(path)!;
        var baseName = Path.GetFileNameWithoutExtension(path);
        var extension = Path.GetExtension(path);
        var stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss_fff", CultureInfo.InvariantCulture);
        var candidate = Path.Combine(directory, $"{baseName}_{stamp}.bak{extension}");
        for (var index = 1; File.Exists(candidate); index++)
        {
            candidate = Path.Combine(directory, $"{baseName}_{stamp}_{index}.bak{extension}");
        }
        return candidate;
    }

    private static string MakeSafeFileName(string name)
    {
        foreach (var ch in Path.GetInvalidFileNameChars())
        {
            name = name.Replace(ch, '_');
        }
        return string.IsNullOrWhiteSpace(name) ? "Project" : name;
    }
}
