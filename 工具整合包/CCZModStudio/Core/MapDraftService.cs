using System.Globalization;
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
        => Path.Combine(GetNotesRoot(project), $"{MakeSafeFileName(project.Name)}_MapWorkbenchSettings.json");

    public string GetDraftStoreRoot(CczProject project)
        => Path.Combine(GetNotesRoot(project), "MapWorkbenchDrafts", MakeSafeFileName(project.Name));

    public string GetDraftAssetRoot(CczProject project, string draftId)
        => Path.Combine(GetNotesRoot(project), "MapWorkbenchAssets", MakeSafeFileName(project.Name), MakeSafeFileName(draftId));

    public MapWorkbenchSettings LoadSettings(CczProject project)
    {
        var path = GetSettingsPath(project);
        if (!File.Exists(path)) return new MapWorkbenchSettings();

        try
        {
            return JsonSerializer.Deserialize<MapWorkbenchSettings>(File.ReadAllText(path, Encoding.UTF8), JsonOptions)
                   ?? new MapWorkbenchSettings();
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException("地图工作台设置 JSON 解析失败：" + path, ex);
        }
    }

    public void SaveSettings(CczProject project, MapWorkbenchSettings settings)
    {
        var path = GetSettingsPath(project);
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
        draft.TerrainCells = new byte[draft.CellCount];
        return draft;
    }

    public MapWorkbenchDraft CreateDraftFromMap(CczProject project, MapResourceItem item, string materialRoot)
    {
        var gridWidth = item.GridWidth > 0 ? item.GridWidth : 30;
        var gridHeight = item.GridHeight > 0 ? item.GridHeight : 30;
        var draft = CreateBlankDraft(gridWidth, gridHeight, materialRoot);
        draft.BoundMapId = GetMapId(item);
        if (File.Exists(item.Path))
        {
            var assetRoot = GetDraftAssetRoot(project, draft.DraftId);
            Directory.CreateDirectory(assetRoot);
            var extension = Path.GetExtension(item.Path);
            var baseLayerPath = Path.Combine(assetRoot, "base" + (string.IsNullOrWhiteSpace(extension) ? ".jpg" : extension));
            File.Copy(item.Path, baseLayerPath, overwrite: true);
            draft.BaseLayerPath = baseLayerPath;
        }

        return draft;
    }

    public MapWorkbenchDraft LoadDraft(CczProject project, string draftId)
    {
        var path = GetDraftPath(project, draftId);
        if (!File.Exists(path))
        {
            throw new FileNotFoundException("地图工作台草稿不存在。", path);
        }

        try
        {
            var draft = JsonSerializer.Deserialize<MapWorkbenchDraft>(File.ReadAllText(path, Encoding.UTF8), JsonOptions)
                        ?? throw new InvalidOperationException("地图工作台草稿为空：" + path);
            return NormalizeDraft(draft);
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException("地图工作台草稿 JSON 解析失败：" + path, ex);
        }
    }

    public void SaveDraft(CczProject project, MapWorkbenchDraft draft)
    {
        draft = NormalizeDraft(draft);
        draft.UpdatedAtText = NowText();
        if (string.IsNullOrWhiteSpace(draft.CreatedAtText)) draft.CreatedAtText = draft.UpdatedAtText;
        var path = GetDraftPath(project, draft.DraftId);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        if (File.Exists(path))
        {
            File.Copy(path, BuildUniqueBackupPath(path), overwrite: false);
        }

        File.WriteAllText(path, JsonSerializer.Serialize(draft, JsonOptions), Encoding.UTF8);
    }

    public IReadOnlyList<MapWorkbenchDraft> ListDrafts(CczProject project)
    {
        var root = GetDraftStoreRoot(project);
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
                Reason = "底稿图片不存在"
            });
        }

        foreach (var cell in draft.MapCellOverrides.Concat(draft.BuildingOverlayCells))
        {
            var path = ResolveMaterialPath(draft.MaterialRoot, cell.MaterialRelativePath);
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                missing.Add(new MapWorkbenchMissingAsset
                {
                    Index = cell.Index,
                    RelativePath = cell.MaterialRelativePath,
                    ExpectedPath = path,
                    Reason = string.IsNullOrWhiteSpace(draft.MaterialRoot) ? "未设置素材库根目录" : "素材文件不存在"
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
            // Fall back to the absolute path below.
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
        => Path.Combine(GetDraftStoreRoot(project), $"{MakeSafeFileName(draftId)}.json");

    private static MapWorkbenchDraft NormalizeDraft(MapWorkbenchDraft draft)
    {
        if (string.IsNullOrWhiteSpace(draft.DraftId)) draft.DraftId = Guid.NewGuid().ToString("N");
        draft.GridWidth = Math.Max(1, draft.GridWidth);
        draft.GridHeight = Math.Max(1, draft.GridHeight);
        draft.TileSize = draft.TileSize <= 0 ? MapResourceItem.MapTilePixelSize : draft.TileSize;
        if (draft.TileSize != MapResourceItem.MapTilePixelSize) draft.TileSize = MapResourceItem.MapTilePixelSize;
        var cellCount = checked(draft.GridWidth * draft.GridHeight);
        if (draft.TerrainCells == null || draft.TerrainCells.Length != cellCount)
        {
            var normalized = new byte[cellCount];
            if (draft.TerrainCells != null)
            {
                Array.Copy(draft.TerrainCells, normalized, Math.Min(draft.TerrainCells.Length, normalized.Length));
            }
            draft.TerrainCells = normalized;
        }

        draft.MapCellOverrides ??= new List<MapCellOverride>();
        draft.MapCellOverrides = NormalizeCells(draft.MapCellOverrides, cellCount, MapCellOverrideSources.ManualOverride);
        draft.GeneratedMapCells ??= new List<MapCellOverride>();
        draft.GeneratedMapCells = NormalizeCells(draft.GeneratedMapCells, cellCount, MapCellOverrideSources.Generated);
        draft.BuildingOverlayCells ??= new List<MapCellOverride>();
        draft.BuildingOverlayCells = NormalizeCells(draft.BuildingOverlayCells, cellCount, MapCellOverrideSources.BuildingOverlay);
        draft.BeautifyStrength = Math.Clamp(draft.BeautifyStrength, 0, 3);
        draft.FeatherRadius = Math.Clamp(draft.FeatherRadius, 0, MapResourceItem.MapTilePixelSize / 2);
        draft.BoundMapId = draft.BoundMapId?.Trim() ?? string.Empty;
        draft.BaseLayerPath = draft.BaseLayerPath?.Trim() ?? string.Empty;
        draft.MaterialRoot = draft.MaterialRoot?.Trim() ?? string.Empty;
        draft.CreatedAtText = draft.CreatedAtText?.Trim() ?? string.Empty;
        draft.UpdatedAtText = draft.UpdatedAtText?.Trim() ?? string.Empty;
        return draft;
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
