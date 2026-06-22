using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Globalization;
using CCZModStudio.Models;

namespace CCZModStudio.Core;

public sealed class TerrainDrivenMapGenerationService
{
    private readonly Dictionary<string, CachedImage> _imageCache = new(StringComparer.OrdinalIgnoreCase);

    public void EnsureMaterialPlan(MapWorkbenchDraft draft, IReadOnlyList<MaterialAsset> materials, bool recoverMissingAuto = true)
    {
        if (!CanGenerate(draft)) return;

        draft.TerrainMaterialPlan ??= new List<TerrainMaterialPlanItem>();
        var terrainAssets = BuildTerrainAssets(materials);
        var fingerprint = BuildMaterialRootFingerprint(draft.MaterialRoot);
        var mapId = GetPlanMapId(draft);
        var requiredFamilies = draft.TerrainCells
            .Distinct()
            .Select(id => (TerrainId: id, VisualFamilyKey: BuildVisualFamilyKey(id)))
            .GroupBy(item => item.VisualFamilyKey, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.OrderBy(item => item.TerrainId).First())
            .OrderBy(item => item.TerrainId)
            .ToList();

        foreach (var family in requiredFamilies)
        {
            var item = draft.TerrainMaterialPlan.FirstOrDefault(x => x.VisualFamilyKey.Equals(family.VisualFamilyKey, StringComparison.OrdinalIgnoreCase));
            if (!terrainAssets.TryGetValue(family.TerrainId, out var candidates) || candidates.Count == 0)
            {
                if (item != null &&
                    (item.SelectionMode.Equals(TerrainMaterialSelectionModes.Manual, StringComparison.OrdinalIgnoreCase) ||
                     item.SelectionMode.Equals(TerrainMaterialSelectionModes.MissingManual, StringComparison.OrdinalIgnoreCase)))
                {
                    item.SelectionMode = TerrainMaterialSelectionModes.MissingManual;
                }

                continue;
            }

            if (item != null && TryResolvePlanMaterial(draft, item, candidates, out _))
            {
                item.MapId = mapId;
                item.TerrainId = family.TerrainId;
                item.MaterialRootFingerprint = fingerprint;
                continue;
            }

            if (item != null &&
                (item.SelectionMode.Equals(TerrainMaterialSelectionModes.Manual, StringComparison.OrdinalIgnoreCase) ||
                 item.SelectionMode.Equals(TerrainMaterialSelectionModes.MissingManual, StringComparison.OrdinalIgnoreCase)))
            {
                item.SelectionMode = TerrainMaterialSelectionModes.MissingManual;
                continue;
            }

            if (item != null && !recoverMissingAuto)
            {
                continue;
            }

            var selected = SelectPlanCandidate(candidates, mapId, family.VisualFamilyKey, fingerprint);
            var mode = item == null ? TerrainMaterialSelectionModes.Auto : TerrainMaterialSelectionModes.AutoRecovered;
            UpsertPlanItem(draft, CreatePlanItem(draft, mapId, family.TerrainId, family.VisualFamilyKey, selected, mode, fingerprint));
        }

        draft.TerrainMaterialPlan.RemoveAll(item => !requiredFamilies.Any(family => family.VisualFamilyKey.Equals(item.VisualFamilyKey, StringComparison.OrdinalIgnoreCase)));
        draft.TerrainMaterialPlan = draft.TerrainMaterialPlan
            .OrderBy(item => item.TerrainId)
            .ThenBy(item => item.VisualFamilyKey, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public TerrainGenerationDiagnostics Analyze(MapWorkbenchDraft draft, IReadOnlyList<MaterialAsset> materials)
    {
        if (!CanGenerate(draft))
        {
            return new TerrainGenerationDiagnostics(
                CanGenerate: false,
                MaterialCount: materials.Count,
                TerrainAssetIdCount: 0,
                MatchedCellCount: 0,
                FallbackCellCount: draft.CellCount,
                MissingTerrainIds: Array.Empty<byte>(),
                MatchedTerrainIds: Array.Empty<byte>());
        }

        var terrainAssets = BuildTerrainAssets(materials);
        var matchedIds = new HashSet<byte>();
        var missingIds = new HashSet<byte>();
        var matchedCells = 0;
        var fallbackCells = 0;
        for (var index = 0; index < draft.CellCount; index++)
        {
            var terrainId = draft.TerrainCells[index];
            if (terrainAssets.TryGetValue(terrainId, out var candidates) && candidates.Count > 0)
            {
                matchedCells++;
                matchedIds.Add(terrainId);
            }
            else
            {
                fallbackCells++;
                missingIds.Add(terrainId);
            }
        }

        return new TerrainGenerationDiagnostics(
            CanGenerate: true,
            MaterialCount: materials.Count,
            TerrainAssetIdCount: terrainAssets.Count,
            MatchedCellCount: matchedCells,
            FallbackCellCount: fallbackCells,
            MissingTerrainIds: missingIds.OrderBy(x => x).ToArray(),
            MatchedTerrainIds: matchedIds.OrderBy(x => x).ToArray());
    }

    public IReadOnlyList<MapCellOverride> GenerateMapCells(MapWorkbenchDraft draft, IReadOnlyList<MaterialAsset> materials)
    {
        if (!CanGenerate(draft)) return Array.Empty<MapCellOverride>();

        EnsureMaterialPlan(draft, materials);
        var terrainAssets = BuildTerrainAssets(materials);
        var result = new List<MapCellOverride>(draft.CellCount);
        for (var index = 0; index < draft.CellCount; index++)
        {
            var terrainId = draft.TerrainCells[index];
            if (!terrainAssets.TryGetValue(terrainId, out var candidates) || candidates.Count == 0) continue;

            if (!TrySelectPlanOrAutoMaterial(draft, candidates, terrainId, out var candidate)) continue;
            result.Add(new MapCellOverride
            {
                Index = index,
                MaterialRelativePath = MapDraftService.GetMaterialRelativePath(draft.MaterialRoot, candidate.FilePath),
                MaterialCategory = candidate.Category,
                DisplayName = candidate.FileName,
                Source = MapCellOverrideSources.Generated
            });
        }

        return result;
    }

    public Bitmap RenderBaseTerrain(MapWorkbenchDraft draft, IReadOnlyList<MaterialAsset> materials)
    {
        var tileSize = draft.TileSize <= 0 ? MapResourceItem.MapTilePixelSize : draft.TileSize;
        var bitmap = new Bitmap(draft.GridWidth * tileSize, draft.GridHeight * tileSize, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bitmap);
        g.SmoothingMode = SmoothingMode.None;
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.PixelOffsetMode = PixelOffsetMode.Half;

        if (!CanGenerate(draft))
        {
            g.Clear(Color.Transparent);
            return bitmap;
        }

        EnsureMaterialPlan(draft, materials);
        var terrainAssets = BuildTerrainAssets(materials);
        for (var y = 0; y < draft.GridHeight; y++)
        {
            for (var x = 0; x < draft.GridWidth; x++)
            {
                var index = y * draft.GridWidth + x;
                var terrainId = draft.TerrainCells[index];
                var rect = new Rectangle(x * tileSize, y * tileSize, tileSize, tileSize);
                if (terrainAssets.TryGetValue(terrainId, out var candidates) && candidates.Count > 0)
                {
                    if (TrySelectPlanOrAutoMaterial(draft, candidates, terrainId, out var candidate))
                    {
                        var image = GetCachedImage(candidate.FilePath);
                        if (image != null)
                        {
                            DrawMainMaterial(g, image, rect);
                            continue;
                        }
                    }
                }

                using var brush = new SolidBrush(HexzmapTerrainRenderService.GetTerrainColor(terrainId));
                g.FillRectangle(brush, rect);
            }
        }

        return bitmap;
    }

    public void DisposeCachedImages()
    {
        foreach (var cached in _imageCache.Values)
        {
            cached.Bitmap.Dispose();
        }

        _imageCache.Clear();
    }

    private static bool CanGenerate(MapWorkbenchDraft draft)
        => draft.AutoGenerateMapFromTerrain &&
           draft.GridWidth > 0 &&
           draft.GridHeight > 0 &&
           draft.TerrainCells.Length == draft.CellCount;

    private static Dictionary<byte, List<MaterialAsset>> BuildTerrainAssets(IReadOnlyList<MaterialAsset> materials)
    {
        var result = new Dictionary<byte, List<MaterialAsset>>();
        foreach (var material in materials)
        {
            if (!MaterialHexTagParser.TryParseTerrainId(material.HexTag, out var id))
            {
                continue;
            }

            if (!result.TryGetValue(id, out var list))
            {
                list = new List<MaterialAsset>();
                result[id] = list;
            }

            list.Add(material);
        }

        foreach (var pair in result)
        {
            pair.Value.Sort((left, right) => CompareMaterialAssets(left, right));
        }

        return result;
    }

    private static int CompareMaterialAssets(MaterialAsset left, MaterialAsset right)
    {
        var categoryCompare = GetCategoryPriority(left.Category).CompareTo(GetCategoryPriority(right.Category));
        if (categoryCompare != 0) return categoryCompare;

        var leftNumber = ParseLeadingNumber(left.FileName);
        var rightNumber = ParseLeadingNumber(right.FileName);
        var numberCompare = leftNumber.CompareTo(rightNumber);
        return numberCompare != 0
            ? numberCompare
            : string.Compare(left.FileName, right.FileName, StringComparison.CurrentCultureIgnoreCase);
    }

    private static int ParseLeadingNumber(string fileName)
        => int.TryParse(Path.GetFileNameWithoutExtension(fileName), NumberStyles.Integer, CultureInfo.InvariantCulture, out var number)
            ? number
            : int.MaxValue;

    private static int GetCategoryPriority(string category)
    {
        if (category.Equals("地形", StringComparison.CurrentCultureIgnoreCase) ||
            category.Contains("地形", StringComparison.CurrentCultureIgnoreCase))
        {
            return 0;
        }

        if (category.Contains("建筑", StringComparison.CurrentCultureIgnoreCase)) return 1;
        if (category.Contains("围墙", StringComparison.CurrentCultureIgnoreCase)) return 2;
        if (category.Contains("随机", StringComparison.CurrentCultureIgnoreCase)) return 3;
        return 4;
    }

    private static bool TryResolvePlanMaterial(
        MapWorkbenchDraft draft,
        TerrainMaterialPlanItem item,
        IReadOnlyList<MaterialAsset> candidates,
        out MaterialAsset material)
    {
        material = null!;
        var plannedPath = MapDraftService.ResolveMaterialPath(draft.MaterialRoot, item.MaterialRelativePath);
        material = candidates.FirstOrDefault(candidate =>
            candidate.FilePath.Equals(plannedPath, StringComparison.OrdinalIgnoreCase) ||
            MapDraftService.GetMaterialRelativePath(draft.MaterialRoot, candidate.FilePath).Equals(item.MaterialRelativePath, StringComparison.OrdinalIgnoreCase))!;
        return material != null && File.Exists(material.FilePath);
    }

    public IReadOnlyList<MaterialAsset> GetCandidateMaterialsForTerrain(byte terrainId, IReadOnlyList<MaterialAsset> materials)
        => BuildTerrainAssets(materials).TryGetValue(terrainId, out var candidates)
            ? candidates
            : Array.Empty<MaterialAsset>();

    public void SetManualPlanItem(MapWorkbenchDraft draft, byte terrainId, MaterialAsset material)
    {
        var fingerprint = BuildMaterialRootFingerprint(draft.MaterialRoot);
        var familyKey = BuildVisualFamilyKey(terrainId);
        UpsertPlanItem(draft, CreatePlanItem(
            draft,
            GetPlanMapId(draft),
            terrainId,
            familyKey,
            material,
            TerrainMaterialSelectionModes.Manual,
            fingerprint));
    }

    public void ResetPlanItemToAuto(MapWorkbenchDraft draft, byte terrainId, IReadOnlyList<MaterialAsset> materials)
    {
        var familyKey = BuildVisualFamilyKey(terrainId);
        draft.TerrainMaterialPlan?.RemoveAll(item => item.VisualFamilyKey.Equals(familyKey, StringComparison.OrdinalIgnoreCase));
        EnsureMaterialPlan(draft, materials);
    }

    public void RerandomizePlanItem(MapWorkbenchDraft draft, byte terrainId, IReadOnlyList<MaterialAsset> materials)
    {
        var candidates = GetCandidateMaterialsForTerrain(terrainId, materials);
        if (candidates.Count == 0) return;

        var familyKey = BuildVisualFamilyKey(terrainId);
        var preferred = GetPreferredPlanCandidates(candidates);
        var currentItem = draft.TerrainMaterialPlan?
            .FirstOrDefault(item => item.VisualFamilyKey.Equals(familyKey, StringComparison.OrdinalIgnoreCase));
        var currentPath = currentItem == null
            ? string.Empty
            : MapDraftService.ResolveMaterialPath(draft.MaterialRoot, currentItem.MaterialRelativePath);
        var currentIndex = string.IsNullOrWhiteSpace(currentPath)
            ? -1
            : preferred.FindIndex(asset => asset.FilePath.Equals(currentPath, StringComparison.OrdinalIgnoreCase));
        var selected = preferred[(currentIndex + 1 + preferred.Count) % preferred.Count];
        UpsertPlanItem(draft, CreatePlanItem(
            draft,
            GetPlanMapId(draft),
            terrainId,
            familyKey,
            selected,
            TerrainMaterialSelectionModes.Auto,
            BuildMaterialRootFingerprint(draft.MaterialRoot)));
    }

    private static bool TrySelectPlanOrAutoMaterial(
        MapWorkbenchDraft draft,
        IReadOnlyList<MaterialAsset> candidates,
        byte terrainId,
        out MaterialAsset material)
    {
        var familyKey = BuildVisualFamilyKey(terrainId);
        var item = draft.TerrainMaterialPlan?
            .FirstOrDefault(x => x.VisualFamilyKey.Equals(familyKey, StringComparison.OrdinalIgnoreCase));
        if (item != null)
        {
            if (TryResolvePlanMaterial(draft, item, candidates, out material))
            {
                return true;
            }

            if (item.SelectionMode.Equals(TerrainMaterialSelectionModes.Manual, StringComparison.OrdinalIgnoreCase) ||
                item.SelectionMode.Equals(TerrainMaterialSelectionModes.MissingManual, StringComparison.OrdinalIgnoreCase))
            {
                material = null!;
                return false;
            }
        }

        material = SelectPlanCandidate(candidates, GetPlanMapId(draft), familyKey, BuildMaterialRootFingerprint(draft.MaterialRoot));
        return true;
    }

    private static MaterialAsset SelectPlanCandidate(IReadOnlyList<MaterialAsset> candidates, string mapId, string familyKey, string fingerprint)
    {
        var preferred = GetPreferredPlanCandidates(candidates);
        var hash = StableHash(mapId + "|" + familyKey + "|" + fingerprint, 0, 0, 0x9E3779B9u);
        return preferred[(int)(hash % (uint)preferred.Count)];
    }

    private static List<MaterialAsset> GetPreferredPlanCandidates(IReadOnlyList<MaterialAsset> candidates)
    {
        var topPriority = candidates.Min(asset => GetCategoryPriority(asset.Category));
        var preferred = candidates.Where(asset => GetCategoryPriority(asset.Category) == topPriority).ToList();
        var ordinary = preferred
            .Where(asset => !ContainsAny(asset, "edge", "border", "corner", "transition", "blend", "mix", "边", "岸", "沿", "角", "过渡", "融合", "混合"))
            .ToList();
        return ordinary.Count > 0 ? ordinary : preferred;
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

    private static void DrawMainMaterial(Graphics g, Image source, Rectangle target)
        => g.DrawImage(source, target);

    private static TerrainMaterialPlanItem CreatePlanItem(
        MapWorkbenchDraft draft,
        string mapId,
        byte terrainId,
        string familyKey,
        MaterialAsset material,
        string selectionMode,
        string fingerprint)
        => new()
        {
            MapId = mapId,
            TerrainId = terrainId,
            VisualFamilyKey = familyKey,
            MaterialRelativePath = MapDraftService.GetMaterialRelativePath(draft.MaterialRoot, material.FilePath),
            MaterialCategory = material.Category,
            DisplayName = material.FileName,
            SelectionMode = selectionMode,
            MaterialRootFingerprint = fingerprint
        };

    private static void UpsertPlanItem(MapWorkbenchDraft draft, TerrainMaterialPlanItem item)
    {
        draft.TerrainMaterialPlan ??= new List<TerrainMaterialPlanItem>();
        draft.TerrainMaterialPlan.RemoveAll(existing => existing.VisualFamilyKey.Equals(item.VisualFamilyKey, StringComparison.OrdinalIgnoreCase));
        draft.TerrainMaterialPlan.Add(item);
    }

    private static string BuildVisualFamilyKey(byte terrainId) => terrainId.ToString(CultureInfo.InvariantCulture);

    public string GetVisualFamilyKey(byte terrainId) => BuildVisualFamilyKey(terrainId);

    public string GetMaterialRootFingerprint(string materialRoot) => BuildMaterialRootFingerprint(materialRoot);

    private static string BuildMaterialRootFingerprint(string materialRoot)
    {
        if (string.IsNullOrWhiteSpace(materialRoot)) return string.Empty;
        try
        {
            return Path.GetFullPath(materialRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).ToUpperInvariant();
        }
        catch
        {
            return materialRoot.Trim().ToUpperInvariant();
        }
    }

    private static string GetPlanMapId(MapWorkbenchDraft draft)
        => string.IsNullOrWhiteSpace(draft.BoundMapId) ? draft.DraftId : draft.BoundMapId;

    private static bool ContainsAny(MaterialAsset asset, params string[] tokens)
    {
        var text = asset.FileName + " " + asset.Description;
        return tokens.Any(token => text.Contains(token, StringComparison.CurrentCultureIgnoreCase));
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

    private sealed record CachedImage(DateTime LastWriteUtc, long Length, Bitmap Bitmap);
}

public sealed record TerrainGenerationDiagnostics(
    bool CanGenerate,
    int MaterialCount,
    int TerrainAssetIdCount,
    int MatchedCellCount,
    int FallbackCellCount,
    IReadOnlyList<byte> MissingTerrainIds,
    IReadOnlyList<byte> MatchedTerrainIds);
