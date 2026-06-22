namespace CCZModStudio.Models;

public static class TerrainMaterialSelectionModes
{
    public const string Auto = "Auto";
    public const string Manual = "Manual";
    public const string AutoRecovered = "AutoRecovered";
    public const string MissingManual = "MissingManual";
}

public sealed class TerrainMaterialPlanItem
{
    public string MapId { get; set; } = string.Empty;
    public byte TerrainId { get; set; }
    public string VisualFamilyKey { get; set; } = string.Empty;
    public string MaterialRelativePath { get; set; } = string.Empty;
    public string MaterialCategory { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string SelectionMode { get; set; } = TerrainMaterialSelectionModes.Auto;
    public string MaterialRootFingerprint { get; set; } = string.Empty;
}

public sealed class PersistedTerrainMaterialPlan
{
    public string ProjectKey { get; set; } = string.Empty;
    public string MapId { get; set; } = string.Empty;
    public List<TerrainMaterialPlanItem> Items { get; set; } = new();
}
