namespace CCZModStudio.Models;

public static class MapCellOverrideSources
{
    public const string ManualOverride = "ManualOverride";
    public const string Generated = "Generated";
    public const string TerrainBase = "TerrainBase";
    public const string BuildingOverlay = "BuildingOverlay";
    public const string SceneryOverlay = "SceneryOverlay";
}

public sealed class MapCellOverride
{
    public int Index { get; set; }
    public string MaterialRelativePath { get; set; } = string.Empty;
    public string MaterialCategory { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Source { get; set; } = MapCellOverrideSources.ManualOverride;
}
